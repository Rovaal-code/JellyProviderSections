using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Jellyfin.Plugin.JellyProviderSections.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Talks to Seerr. Written against the real contract of seerr-team/seerr v3.4.0
/// (see docs/research/06-seerr-api-analysis.md), not adapted verbatim from
/// JellyNotify's read-only client.
///
/// Requests are attributed with the X-API-User header rather than a userId in
/// the body: with the header, Seerr evaluates permissions, quota, overrides AND
/// auto-approval as that real user, which is what an administrator expects. The
/// body-userId route would instead auto-approve everything, because approval is
/// decided by the caller's own permissions (the admin API key).
/// </summary>
public interface ISeerrApiClient
{
    /// <summary>Verifies a URL and API key.</summary>
    /// <param name="serverUrlOverride">URL to test instead of the stored one, or null.</param>
    /// <param name="apiKeyOverride">
    /// Key to test instead of the stored one, or null. Together these let the
    /// admin check what is typed into the form before committing it; requiring a
    /// save first has it exactly backwards.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connection result.</returns>
    Task<SeerrConnectionResult> TestConnectionAsync(
        string? serverUrlOverride,
        string? apiKeyOverride,
        CancellationToken cancellationToken);

    /// <summary>Gets a title's availability, or null when Seerr has no record.</summary>
    /// <param name="contentType">Movie or series.</param>
    /// <param name="tmdbId">The TMDb id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The media info, or null.</returns>
    Task<SeerrMediaInfo?> GetMediaInfoAsync(
        ProviderSectionContentType contentType,
        int tmdbId,
        CancellationToken cancellationToken);

    /// <summary>Resolves the Seerr account linked to a Jellyfin user GUID.</summary>
    /// <param name="jellyfinUserId">The Jellyfin user GUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Seerr user, or null if none is linked.</returns>
    Task<SeerrUser?> GetUserByJellyfinIdAsync(string jellyfinUserId, CancellationToken cancellationToken);

    /// <summary>Imports Jellyfin users into Seerr so requests can be attributed to them.</summary>
    /// <param name="jellyfinUserIds">The Jellyfin user GUIDs to import.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the import call succeeded.</returns>
    Task<bool> ImportUsersFromJellyfinAsync(IReadOnlyList<string> jellyfinUserIds, CancellationToken cancellationToken);

    /// <summary>Creates a request on behalf of a Jellyfin user.</summary>
    /// <param name="contentType">Movie or series.</param>
    /// <param name="tmdbId">The TMDb id.</param>
    /// <param name="jellyfinUserId">The requesting Jellyfin user's GUID.</param>
    /// <param name="allSeasons">For series, request every season.</param>
    /// <param name="seasons">For series, the specific seasons to request.</param>
    /// <param name="is4k">Whether this is a 4K request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<SeerrRequestResult> CreateRequestAsync(
        ProviderSectionContentType contentType,
        int tmdbId,
        string jellyfinUserId,
        bool allSeasons,
        IReadOnlyList<int>? seasons,
        bool is4k,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="ISeerrApiClient" />
public sealed class SeerrApiClient : ISeerrApiClient
{
    private static readonly TimeSpan MediaInfoTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UserTtl = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SeerrApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeerrApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The typed HTTP client.</param>
    /// <param name="cache">Shared in-memory cache.</param>
    /// <param name="logger">Logger.</param>
    public SeerrApiClient(HttpClient httpClient, IMemoryCache cache, ILogger<SeerrApiClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;

        if (_httpClient.Timeout == default || _httpClient.Timeout > TimeSpan.FromSeconds(30))
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }
    }

    private static SeerrSettings? Settings => Plugin.Instance?.Configuration?.SeerrSettings;

    private static bool IsConfigured =>
        Settings is { Enabled: true }
        && !string.IsNullOrWhiteSpace(Settings.ServerUrl)
        && !string.IsNullOrWhiteSpace(Settings.ApiKey);

    /// <inheritdoc />
    public async Task<SeerrConnectionResult> TestConnectionAsync(
        string? serverUrlOverride,
        string? apiKeyOverride,
        CancellationToken cancellationToken)
    {
        var serverUrl = string.IsNullOrWhiteSpace(serverUrlOverride)
            ? Settings?.ServerUrl
            : serverUrlOverride.Trim();

        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? Settings?.ApiKey
            : apiKeyOverride.Trim();

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return new SeerrConnectionResult(false, "No hay ninguna URL de Seerr configurada.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SeerrConnectionResult(false, "No hay ninguna API key de Seerr configurada.");
        }

        // Same guard the save path applies: a non-HTTP scheme here would turn an
        // admin typo into a request to somewhere it has no business reaching.
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return new SeerrConnectionResult(false, "La URL de Seerr debe empezar por http:// o https://");
        }

        try
        {
            using var request = BuildRequest(HttpMethod.Get, "settings/main", null, serverUrl, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new SeerrConnectionResult(false, "Seerr rechazó la API key. Comprueba que es correcta.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new SeerrConnectionResult(
                    false,
                    string.Create(CultureInfo.InvariantCulture, $"Seerr respondió {(int)response.StatusCode}."));
            }

            return new SeerrConnectionResult(true, "Conexión con Seerr correcta.");
        }
        catch (TaskCanceledException)
        {
            return new SeerrConnectionResult(false, "Seerr no respondió a tiempo.");
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or InvalidOperationException)
        {
            return new SeerrConnectionResult(false, $"No se pudo contactar con Seerr: {Sanitize(ex.Message)}");
        }
    }

    /// <inheritdoc />
    public async Task<SeerrMediaInfo?> GetMediaInfoAsync(
        ProviderSectionContentType contentType,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var path = contentType == ProviderSectionContentType.Movie ? "movie" : "tv";
        var cacheKey = $"seerr:media:{path}:{tmdbId}";

        if (_cache.TryGetValue(cacheKey, out SeerrMediaInfo? cached))
        {
            return cached;
        }

        var details = await GetJsonAsync<SeerrMediaDetails>(
            $"{path}/{tmdbId.ToString(CultureInfo.InvariantCulture)}",
            null,
            cancellationToken).ConfigureAwait(false);

        // A null mediaInfo is a legitimate answer meaning "Seerr doesn't track
        // this title", so it is cached too, otherwise every unavailable title
        // would hit the API on every render.
        _cache.Set(cacheKey, details?.MediaInfo, MediaInfoTtl);
        return details?.MediaInfo;
    }

    /// <inheritdoc />
    public async Task<SeerrUser?> GetUserByJellyfinIdAsync(string jellyfinUserId, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(jellyfinUserId))
        {
            return null;
        }

        var cacheKey = $"seerr:user:jellyfin:{jellyfinUserId}";
        if (_cache.TryGetValue(cacheKey, out SeerrUser? cached))
        {
            return cached;
        }

        var user = await GetJsonAsync<SeerrUser>($"user/jellyfin/{jellyfinUserId}", null, cancellationToken)
            .ConfigureAwait(false);

        if (user is not null)
        {
            _cache.Set(cacheKey, user, UserTtl);
        }

        return user;
    }

    /// <inheritdoc />
    public async Task<bool> ImportUsersFromJellyfinAsync(
        IReadOnlyList<string> jellyfinUserIds,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || jellyfinUserIds.Count == 0)
        {
            return false;
        }

        try
        {
            using var request = BuildRequest(
                HttpMethod.Post,
                "user/import-from-jellyfin",
                null);
            request.Content = JsonContent.Create(new { jellyfinUserIds });

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[ProviderSections] Seerr user import returned {StatusCode}",
                    (int)response.StatusCode);
                return false;
            }

            // The freshly imported user invalidates any cached "not linked" answer.
            foreach (var id in jellyfinUserIds)
            {
                _cache.Remove($"seerr:user:jellyfin:{id}");
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning("[ProviderSections] Seerr user import failed: {Message}", Sanitize(ex.Message));
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<SeerrRequestResult> CreateRequestAsync(
        ProviderSectionContentType contentType,
        int tmdbId,
        string jellyfinUserId,
        bool allSeasons,
        IReadOnlyList<int>? seasons,
        bool is4k,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new SeerrRequestResult(
                SeerrRequestOutcome.Unavailable,
                "Seerr no está configurado en este servidor.");
        }

        var (seerrUser, reachable) = await GetJsonWithReachabilityAsync<SeerrUser>(
            $"user/jellyfin/{jellyfinUserId}",
            null,
            cancellationToken).ConfigureAwait(false);

        if (!reachable)
        {
            return new SeerrRequestResult(
                SeerrRequestOutcome.Unavailable,
                "No se pudo contactar con Seerr. Inténtalo de nuevo más tarde.");
        }

        if (seerrUser is null)
        {
            // Try a one-shot import before giving up: an admin who set up Seerr's
            // Jellyfin integration expects new users to just work.
            var imported = await ImportUsersFromJellyfinAsync(new[] { jellyfinUserId }, cancellationToken)
                .ConfigureAwait(false);

            if (imported)
            {
                seerrUser = await GetUserByJellyfinIdAsync(jellyfinUserId, cancellationToken).ConfigureAwait(false);
            }
        }

        if (seerrUser is null)
        {
            return new SeerrRequestResult(
                SeerrRequestOutcome.UserNotLinked,
                "Tu cuenta todavía no existe en Seerr. Inicia sesión una vez en Seerr y vuelve a intentarlo.");
        }

        var body = new SeerrRequestBody
        {
            MediaType = contentType == ProviderSectionContentType.Movie ? "movie" : "tv",
            MediaId = tmdbId,
            Is4k = is4k,
            IgnoreQuota = Settings?.AllowIgnoreQuota ?? false,
        };

        if (contentType == ProviderSectionContentType.Series)
        {
            // Seerr filters out seasons that are already requested or available
            // server-side, so "all" is safe to send without computing the delta.
            body.Seasons = allSeasons || seasons is null || seasons.Count == 0
                ? "all"
                : seasons;
        }

        try
        {
            using var request = BuildRequest(HttpMethod.Post, "request", seerrUser.Id);
            request.Content = JsonContent.Create(body);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => new SeerrRequestResult(
                    SeerrRequestOutcome.AlreadyRequested,
                    "Ya existe una solicitud para este título."),

                HttpStatusCode.Accepted => new SeerrRequestResult(
                    SeerrRequestOutcome.NothingToRequest,
                    "No queda nada nuevo por solicitar de este título."),

                HttpStatusCode.Forbidden => new SeerrRequestResult(
                    SeerrRequestOutcome.NotPermitted,
                    "No tienes permiso para solicitar este contenido, o has agotado tu cuota."),

                _ when response.IsSuccessStatusCode => new SeerrRequestResult(
                    SeerrRequestOutcome.Created,
                    "Solicitud enviada."),

                _ => new SeerrRequestResult(
                    SeerrRequestOutcome.Failed,
                    string.Create(CultureInfo.InvariantCulture, $"Seerr respondió {(int)response.StatusCode}.")),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning("[ProviderSections] Seerr request failed: {Message}", Sanitize(ex.Message));
            return new SeerrRequestResult(
                SeerrRequestOutcome.Unavailable,
                "No se pudo contactar con Seerr.");
        }
        finally
        {
            // Whatever happened, the cached availability for this title is now stale.
            var path = contentType == ProviderSectionContentType.Movie ? "movie" : "tv";
            _cache.Remove($"seerr:media:{path}:{tmdbId}");
        }
    }

    private async Task<T?> GetJsonAsync<T>(
        string path,
        int? asUserId,
        CancellationToken cancellationToken)
        where T : class
        => (await GetJsonWithReachabilityAsync<T>(path, asUserId, cancellationToken).ConfigureAwait(false)).Value;

    /// <summary>
    /// Same as <see cref="GetJsonAsync{T}"/> but also reports whether Seerr
    /// answered at all. "No such user" and "the server is down" are both a null
    /// result, and telling a user to go sign up for an account when the server
    /// is simply unreachable sends them on a pointless errand.
    /// </summary>
    private async Task<(T? Value, bool Reachable)> GetJsonWithReachabilityAsync<T>(
        string path,
        int? asUserId,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var request = BuildRequest(HttpMethod.Get, path, asUserId);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Seerr answered, it just has no such record.
                return (null, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[ProviderSections] Seerr {Path} returned {StatusCode}",
                    path,
                    (int)response.StatusCode);
                return (null, true);
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return (value, true);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("[ProviderSections] Seerr {Path} returned malformed JSON: {Message}", path, ex.Message);
            return (null, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning("[ProviderSections] Seerr {Path} failed: {Message}", path, Sanitize(ex.Message));
            return (null, false);
        }
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        string path,
        int? asUserId,
        string? serverUrlOverride = null,
        string? apiKeyOverride = null)
    {
        // Overrides are only ever passed by the connection test, so it can check
        // credentials the admin has typed but not saved.
        var baseUrl = ((string.IsNullOrWhiteSpace(serverUrlOverride) ? Settings?.ServerUrl : serverUrlOverride)
            ?? string.Empty).TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}/api/v1/{path}");

        request.Headers.TryAddWithoutValidation(
            "X-Api-Key",
            (string.IsNullOrWhiteSpace(apiKeyOverride) ? Settings?.ApiKey : apiKeyOverride) ?? string.Empty);

        if (asUserId.HasValue)
        {
            // Impersonate the real requester so permissions, quota, overrides and
            // auto-approval are all evaluated as that user.
            request.Headers.TryAddWithoutValidation(
                "X-API-User",
                asUserId.Value.ToString(CultureInfo.InvariantCulture));
        }

        return request;
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var key = Settings?.ApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            message = message.Replace(key, "<redacted>", StringComparison.OrdinalIgnoreCase);
        }

        return message;
    }
}

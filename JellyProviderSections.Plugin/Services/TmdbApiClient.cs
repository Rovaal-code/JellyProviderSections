using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Jellyfin.Plugin.JellyProviderSections.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// TMDb API client. Authenticates with the v3 api_key query parameter, caches
/// the slow-moving reference data (image configuration, regions, provider
/// lists) in memory, and backs off on 429 without assuming a fixed rate limit
/// (TMDb no longer publishes one).
/// </summary>
public sealed class TmdbApiClient : ITmdbApiClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3/";
    private const string ImageBaseUrlFallback = "https://image.tmdb.org/t/p/";

    // TMDb caps discover pagination well below this, but the guard keeps a
    // misconfigured MaxItems from walking hundreds of pages.
    private const int MaxPagesPerSection = 20;
    private const int ResultsPerPage = 20;

    private static readonly TimeSpan ImageConfigTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan RegionsTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan ProvidersTtl = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TmdbApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The typed HTTP client.</param>
    /// <param name="cache">Shared in-memory cache.</param>
    /// <param name="logger">Logger.</param>
    public TmdbApiClient(HttpClient httpClient, IMemoryCache cache, ILogger<TmdbApiClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;

        if (_httpClient.Timeout == default || _httpClient.Timeout > TimeSpan.FromSeconds(30))
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(20);
        }
    }

    private static string? ApiKey => Plugin.Instance?.Configuration?.TmdbSettings?.ApiKey;

    /// <inheritdoc />
    public async Task<TmdbConnectionResult> TestConnectionAsync(
        string? apiKeyOverride,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(apiKeyOverride) ? ApiKey : apiKeyOverride.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            return new TmdbConnectionResult(false, "No hay ninguna API key de TMDb configurada.");
        }

        try
        {
            // Built here rather than through SendAsync, which always signs with
            // the stored key; the point of the override is to try one that is not
            // stored yet.
            var url = $"{BaseUrl}configuration?api_key={Uri.EscapeDataString(key)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new TmdbConnectionResult(false, "TMDb rechazó la API key (401). Comprueba que es correcta.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new TmdbConnectionResult(
                    false,
                    string.Create(CultureInfo.InvariantCulture, $"TMDb respondió {(int)response.StatusCode}."));
            }

            return new TmdbConnectionResult(true, "Conexión con TMDb correcta.");
        }
        catch (TaskCanceledException)
        {
            return new TmdbConnectionResult(false, "TMDb no respondió a tiempo.");
        }
        catch (HttpRequestException ex)
        {
            return new TmdbConnectionResult(false, $"No se pudo contactar con TMDb: {Sanitize(ex.Message)}");
        }
    }

    /// <inheritdoc />
    public async Task<TmdbImageConfiguration?> GetImageConfigurationAsync(CancellationToken cancellationToken)
    {
        const string CacheKey = "tmdb:configuration:images";

        if (_cache.TryGetValue(CacheKey, out TmdbImageConfiguration? cached))
        {
            return cached;
        }

        var configuration = await GetJsonAsync<TmdbConfiguration>("configuration", null, cancellationToken)
            .ConfigureAwait(false);

        var images = configuration?.Images;
        if (images is not null)
        {
            _cache.Set(CacheKey, images, ImageConfigTtl);
        }

        return images;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TmdbRegion>> GetWatchProviderRegionsAsync(CancellationToken cancellationToken)
    {
        const string CacheKey = "tmdb:watch:regions";

        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<TmdbRegion>? cached) && cached is not null)
        {
            return cached;
        }

        var result = await GetJsonAsync<TmdbResultList<TmdbRegion>>(
            "watch/providers/regions",
            null,
            cancellationToken).ConfigureAwait(false);

        var regions = (IReadOnlyList<TmdbRegion>)(result?.Results ?? new List<TmdbRegion>());
        if (regions.Count > 0)
        {
            _cache.Set(CacheKey, regions, RegionsTtl);
        }

        return regions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TmdbWatchProvider>> GetWatchProvidersAsync(
        ProviderSectionContentType contentType,
        string watchRegion,
        CancellationToken cancellationToken)
    {
        var path = contentType == ProviderSectionContentType.Movie
            ? "watch/providers/movie"
            : "watch/providers/tv";

        var cacheKey = $"tmdb:watch:providers:{path}:{watchRegion}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<TmdbWatchProvider>? cached) && cached is not null)
        {
            return cached;
        }

        var query = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(watchRegion))
        {
            query.Add(new KeyValuePair<string, string>("watch_region", watchRegion));
        }

        var result = await GetJsonAsync<TmdbResultList<TmdbWatchProvider>>(path, query, cancellationToken)
            .ConfigureAwait(false);

        var providers = (IReadOnlyList<TmdbWatchProvider>)(result?.Results ?? new List<TmdbWatchProvider>());
        if (providers.Count > 0)
        {
            _cache.Set(cacheKey, providers, ProvidersTtl);
        }

        return providers;
    }

    /// <inheritdoc />
    public Task<TmdbDiscoverPage?> DiscoverAsync(SectionDefinition section, int page, CancellationToken cancellationToken)
        => DiscoverAsync(section, page, section.ContentType, cancellationToken);

    private Task<TmdbDiscoverPage?> DiscoverAsync(
        SectionDefinition section,
        int page,
        ProviderSectionContentType contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(section);

        var endpoint = DiscoverQueryBuilder.GetEndpoint(contentType);
        var query = DiscoverQueryBuilder.Build(section, page, contentType);

        return GetJsonAsync<TmdbDiscoverPage>(endpoint, query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TmdbDiscoverItem>> DiscoverAllAsync(
        SectionDefinition section,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.ContentType == ProviderSectionContentType.Mixed)
        {
            return await DiscoverMixedAsync(section, cancellationToken).ConfigureAwait(false);
        }

        return await DiscoverSingleAsync(section, section.ContentType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the movie and the series query and interleaves them one for one.
    ///
    /// Each list keeps its own ranking; they are not merged by a shared score,
    /// because TMDb's popularity is not comparable between the two endpoints and
    /// sorting the union by it simply lets one type bury the other. Alternating
    /// needs no cross-type metric, is stable between runs, and keeps both types
    /// visible at the head of the row, which is the whole point of a mixed one.
    /// If one side comes back short, the other fills the remainder rather than
    /// leaving the row half empty.
    /// </summary>
    private async Task<IReadOnlyList<TmdbDiscoverItem>> DiscoverMixedAsync(
        SectionDefinition section,
        CancellationToken cancellationToken)
    {
        var wanted = Math.Max(1, section.MaxItems);

        var movies = await DiscoverSingleAsync(section, ProviderSectionContentType.Movie, cancellationToken)
            .ConfigureAwait(false);
        var series = await DiscoverSingleAsync(section, ProviderSectionContentType.Series, cancellationToken)
            .ConfigureAwait(false);

        var merged = new List<TmdbDiscoverItem>(wanted);

        for (var i = 0; merged.Count < wanted && (i < movies.Count || i < series.Count); i++)
        {
            if (i < movies.Count)
            {
                merged.Add(movies[i]);
            }

            if (merged.Count < wanted && i < series.Count)
            {
                merged.Add(series[i]);
            }
        }

        return merged;
    }

    private async Task<IReadOnlyList<TmdbDiscoverItem>> DiscoverSingleAsync(
        SectionDefinition section,
        ProviderSectionContentType contentType,
        CancellationToken cancellationToken)
    {
        var wanted = Math.Max(1, section.MaxItems);
        var pagesNeeded = Math.Min(
            MaxPagesPerSection,
            (int)Math.Ceiling(wanted / (double)ResultsPerPage));

        var seen = new HashSet<int>();
        var collected = new List<TmdbDiscoverItem>();

        for (var page = 1; page <= pagesNeeded; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await DiscoverAsync(section, page, contentType, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                // Partial failure: return what we have rather than nothing, the
                // caller records this as PartialFailure in the section state.
                break;
            }

            foreach (var item in result.Results)
            {
                if (seen.Add(item.Id))
                {
                    collected.Add(item);
                }

                if (collected.Count >= wanted)
                {
                    return collected;
                }
            }

            // Respect TMDb's own reported ceiling rather than walking past it.
            if (page >= result.TotalPages)
            {
                break;
            }
        }

        return collected;
    }

    /// <inheritdoc />
    public async Task<TmdbImageDownload?> DownloadImageAsync(
        string logoPath,
        string size,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        var imageConfig = await GetImageConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var baseUrl = imageConfig?.SecureBaseUrl ?? ImageBaseUrlFallback;
        var url = $"{baseUrl.TrimEnd('/')}/{size}/{logoPath.TrimStart('/')}";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[ProviderSections] TMDb image download returned {StatusCode} for size {Size}",
                    (int)response.StatusCode,
                    size);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            return new TmdbImageDownload(bytes, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("[ProviderSections] TMDb image download failed: {Message}", Sanitize(ex.Message));
            return null;
        }
    }

    private async Task<T?> GetJsonAsync<T>(
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? query,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await SendAsync(path, query, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[ProviderSections] TMDb {Path} returned {StatusCode}",
                    path,
                    (int)response.StatusCode);
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("[ProviderSections] TMDb {Path} returned malformed JSON: {Message}", path, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("[ProviderSections] TMDb {Path} failed: {Message}", path, Sanitize(ex.Message));
            return null;
        }
    }

    /// <summary>
    /// Issues the request, retrying once with backoff on 429. TMDb no longer
    /// publishes a fixed rate limit, so this reacts to the response rather than
    /// pacing against an assumed number.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? query,
        CancellationToken cancellationToken)
    {
        const int MaxAttempts = 3;

        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            response = await _httpClient.GetAsync(BuildUrl(path, query), cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaxAttempts)
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));

            _logger.LogWarning(
                "[ProviderSections] TMDb rate limited on {Path}, retrying in {Seconds}s",
                path,
                delay.TotalSeconds);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return response!;
    }

    private static string BuildUrl(string path, IReadOnlyList<KeyValuePair<string, string>>? query)
    {
        var parameters = new List<KeyValuePair<string, string>>();
        if (query is not null)
        {
            parameters.AddRange(query);
        }

        parameters.Add(new KeyValuePair<string, string>("api_key", ApiKey ?? string.Empty));

        var queryString = string.Join(
            "&",
            parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return $"{BaseUrl}{path}?{queryString}";
    }

    /// <summary>
    /// Strips anything that looks like the API key out of a message before it
    /// reaches a log or the admin UI. HttpRequestException messages can echo the
    /// request URL, which carries api_key as a query parameter.
    /// </summary>
    private static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var key = ApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            message = message.Replace(key, "<redacted>", StringComparison.OrdinalIgnoreCase);
        }

        return System.Text.RegularExpressions.Regex.Replace(
            message,
            @"api_key=[^&\s]*",
            "api_key=<redacted>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

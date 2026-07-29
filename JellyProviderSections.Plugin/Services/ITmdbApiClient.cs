using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Jellyfin.Plugin.JellyProviderSections.Models;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Talks to the TMDb API. Authenticates with the v3 api_key query parameter.
/// </summary>
public interface ITmdbApiClient
{
    /// <summary>
    /// Verifies an API key by calling a cheap endpoint.
    /// </summary>
    /// <param name="apiKeyOverride">
    /// The key to test instead of the stored one, or null to test what is saved.
    /// This is what lets the admin check a key typed into the form before
    /// committing it; requiring a save first has it exactly backwards.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result describing success or the reason for failure.</returns>
    Task<TmdbConnectionResult> TestConnectionAsync(string? apiKeyOverride, CancellationToken cancellationToken);

    /// <summary>
    /// Gets TMDb's image configuration (base URL and available sizes), cached.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image configuration, or null if unavailable.</returns>
    Task<TmdbImageConfiguration?> GetImageConfigurationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the list of supported watch-provider regions, cached.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The available regions.</returns>
    Task<IReadOnlyList<TmdbRegion>> GetWatchProviderRegionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the watch providers available for a content type and region, cached.
    /// The movie and TV lists are not identical: a provider may carry films but
    /// no series in a given region, so both are queried separately.
    /// </summary>
    /// <param name="contentType">Movie or series.</param>
    /// <param name="watchRegion">ISO 3166-1 region code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The available providers.</returns>
    Task<IReadOnlyList<TmdbWatchProvider>> GetWatchProvidersAsync(
        ProviderSectionContentType contentType,
        string watchRegion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs one page of a section's discover query.
    /// </summary>
    /// <param name="section">The section definition.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of results, or null on failure.</returns>
    Task<TmdbDiscoverPage?> DiscoverAsync(SectionDefinition section, int page, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a section's discover query across as many pages as needed to reach
    /// MaxItems, deduplicating by TMDb id.
    /// </summary>
    /// <param name="section">The section definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deduplicated results, capped at MaxItems.</returns>
    /// <param name="wantedOverride">
    /// How many titles to collect instead of the section's own maximum. A
    /// section that hides what the library already has asks for more than it
    /// will show, so the row still fills up after the filtering.
    /// </param>
    Task<IReadOnlyList<TmdbDiscoverItem>> DiscoverAllAsync(
        SectionDefinition section,
        CancellationToken cancellationToken,
        int? wantedOverride = null);

    /// <summary>
    /// Downloads a provider logo as raw bytes, for local caching and serving.
    /// </summary>
    /// <param name="logoPath">The TMDb logo_path.</param>
    /// <param name="size">The requested size (e.g. "w92").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image bytes and content type, or null if unavailable.</returns>
    Task<TmdbImageDownload?> DownloadImageAsync(string logoPath, string size, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a TMDb connection test. Property names are pinned to camelCase so
/// the payload matches the rest of this plugin's API; without the attributes a
/// record serializes PascalCase and the frontend silently reads undefined.
/// </summary>
/// <param name="Success">Whether the connection succeeded.</param>
/// <param name="Message">A human-readable message, sanitized of any secret.</param>
public record TmdbConnectionResult(
    [property: System.Text.Json.Serialization.JsonPropertyName("success")] bool Success,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message);

/// <summary>
/// A downloaded image.
/// </summary>
/// <param name="Content">The raw image bytes.</param>
/// <param name="ContentType">The MIME type reported by TMDb.</param>
public record TmdbImageDownload(byte[] Content, string ContentType);

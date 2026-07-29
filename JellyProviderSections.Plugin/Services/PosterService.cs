using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Caches the poster of every external title a section has shown, and serves it
/// from this plugin's own route.
///
/// Jellyfin Web builds a card's artwork URL from the item id, which for a title
/// that is not in the library is a synthetic id no image endpoint can resolve.
/// The home script therefore asks for the poster by TMDb id instead, and this is
/// what answers. Serving it locally rather than linking image.tmdb.org keeps the
/// browser talking only to the Jellyfin server, which is what the section title
/// logos already do.
///
/// The TMDb path is remembered as sections are built, so serving a poster costs
/// no extra TMDb call. Unknown ids answer 404 and the card keeps its placeholder.
/// </summary>
public interface IPosterService
{
    /// <summary>Records what a title needs for its card, so it can be served later.</summary>
    /// <param name="tmdbId">The TMDb id.</param>
    /// <param name="posterPath">The TMDb poster path.</param>
    /// <param name="voteAverage">The TMDb vote average, shown on the card.</param>
    void Remember(int tmdbId, string posterPath, double voteAverage);

    /// <summary>
    /// Gets the TMDb rating for each of the given titles, skipping the unknown.
    /// Answered in one call per row rather than one per card.
    /// </summary>
    /// <param name="tmdbIds">The TMDb ids to look up.</param>
    /// <returns>Rating by TMDb id.</returns>
    IReadOnlyDictionary<int, double> GetRatings(IEnumerable<int> tmdbIds);

    /// <summary>Gets a title's poster, downloading and caching on first use.</summary>
    /// <param name="tmdbId">The TMDb id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached image, or null when the id is unknown or the download fails.</returns>
    Task<CachedLogo?> GetPosterAsync(int tmdbId, CancellationToken cancellationToken);

    /// <summary>Removes every cached poster.</summary>
    void ClearCache();
}

/// <inheritdoc cref="IPosterService" />
public sealed class PosterService : IPosterService
{
    // Portrait cards are about 230 CSS pixels wide, so w500 still looks sharp on
    // a 2x display without storing artwork nobody will see at full size.
    private const string PosterSize = "w500";

    // A section can hold 200 titles and a server can hold many sections; the cap
    // keeps a long-running server from growing this map without bound. Entries
    // are cheap to lose: the next render of that section puts them back.
    private const int MaxRememberedPaths = 5000;

    private readonly ConcurrentDictionary<int, string> _posterPaths = new();
    private readonly ConcurrentDictionary<int, double> _ratings = new();
    private readonly ITmdbApiClient _tmdbClient;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<PosterService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PosterService"/> class.
    /// </summary>
    /// <param name="tmdbClient">TMDb client.</param>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="logger">Logger.</param>
    public PosterService(
        ITmdbApiClient tmdbClient,
        IApplicationPaths applicationPaths,
        ILogger<PosterService> logger)
    {
        _tmdbClient = tmdbClient;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    private string CacheDirectory =>
        Path.Combine(_applicationPaths.PluginConfigurationsPath, "JellyProviderSections", "posters");

    /// <inheritdoc />
    public void Remember(int tmdbId, string posterPath, double voteAverage)
    {
        if (tmdbId <= 0 || string.IsNullOrWhiteSpace(posterPath))
        {
            return;
        }

        if (_posterPaths.Count >= MaxRememberedPaths && !_posterPaths.ContainsKey(tmdbId))
        {
            _posterPaths.Clear();
            _ratings.Clear();
        }

        _posterPaths[tmdbId] = posterPath;

        if (voteAverage > 0)
        {
            _ratings[tmdbId] = voteAverage;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<int, double> GetRatings(IEnumerable<int> tmdbIds)
    {
        ArgumentNullException.ThrowIfNull(tmdbIds);

        var found = new Dictionary<int, double>();

        foreach (var id in tmdbIds)
        {
            if (_ratings.TryGetValue(id, out var rating))
            {
                found[id] = rating;
            }
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<CachedLogo?> GetPosterAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var cachedPath = Path.Combine(
            CacheDirectory,
            $"{tmdbId.ToString(CultureInfo.InvariantCulture)}.img");

        try
        {
            if (File.Exists(cachedPath))
            {
                var cachedBytes = await File.ReadAllBytesAsync(cachedPath, cancellationToken).ConfigureAwait(false);
                if (cachedBytes.Length > 0)
                {
                    return new CachedLogo(cachedBytes, GuessContentType(cachedBytes));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ProviderSections] Could not read the cached poster for {TmdbId}", tmdbId);
        }

        if (!_posterPaths.TryGetValue(tmdbId, out var posterPath))
        {
            return null;
        }

        var download = await _tmdbClient.DownloadImageAsync(posterPath, PosterSize, cancellationToken)
            .ConfigureAwait(false);

        if (download is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(cachedPath, download.Content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Serving the poster matters more than caching it.
            _logger.LogWarning(ex, "[ProviderSections] Could not cache the poster for {TmdbId}", tmdbId);
        }

        return new CachedLogo(download.Content, download.ContentType);
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _posterPaths.Clear();

        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ProviderSections] Could not clear the poster cache");
        }
    }

    private static string GuessContentType(byte[] content)
    {
        if (content.Length >= 8
            && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return "image/png";
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return "image/jpeg";
        }

        return "application/octet-stream";
    }
}

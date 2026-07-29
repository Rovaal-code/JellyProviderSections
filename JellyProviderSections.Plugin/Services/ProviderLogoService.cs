using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Caches provider logos on disk and serves them from this plugin's own route.
///
/// The logo is embedded in the section title markup, so it is fetched by every
/// browser on every home screen load. Serving it locally avoids hammering
/// TMDb's CDN, keeps working when the client has no route to the internet, and
/// means the URL in displayText never points at a third-party host.
///
/// Disk rather than memory: there is one logo per provider actually in use (a
/// handful), they essentially never change, and they should survive restarts.
/// </summary>
public interface IProviderLogoService
{
    /// <summary>
    /// Records a provider's TMDb logo path, so its logo can be served before any
    /// section uses that provider.
    /// </summary>
    /// <param name="tmdbProviderId">The TMDb provider id.</param>
    /// <param name="logoPath">The TMDb logo path.</param>
    void Remember(int tmdbProviderId, string logoPath);

    /// <summary>Gets a provider's logo bytes, downloading and caching on first use.</summary>
    /// <param name="tmdbProviderId">The TMDb provider id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached image, or null when unavailable.</returns>
    Task<CachedLogo?> GetLogoAsync(int tmdbProviderId, CancellationToken cancellationToken);

    /// <summary>Removes every cached logo.</summary>
    void ClearCache();
}

/// <summary>A logo held on disk.</summary>
/// <param name="Content">The image bytes.</param>
/// <param name="ContentType">The MIME type.</param>
public record CachedLogo(byte[] Content, string ContentType);

/// <inheritdoc cref="IProviderLogoService" />
public sealed class ProviderLogoService : IProviderLogoService
{
    // Small enough for a title-height logo, large enough to stay crisp on
    // high-DPI screens. TMDb serves w92 for essentially every provider.
    private const string LogoSize = "w92";

    private readonly ConcurrentDictionary<int, string> _logoPaths = new();
    private readonly ITmdbApiClient _tmdbClient;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<ProviderLogoService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderLogoService"/> class.
    /// </summary>
    /// <param name="tmdbClient">TMDb client.</param>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="logger">Logger.</param>
    public ProviderLogoService(
        ITmdbApiClient tmdbClient,
        IApplicationPaths applicationPaths,
        ILogger<ProviderLogoService> logger)
    {
        _tmdbClient = tmdbClient;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    private string CacheDirectory =>
        Path.Combine(_applicationPaths.PluginConfigurationsPath, "JellyProviderSections", "logos");

    /// <inheritdoc />
    public void Remember(int tmdbProviderId, string logoPath)
    {
        if (tmdbProviderId <= 0 || string.IsNullOrWhiteSpace(logoPath))
        {
            return;
        }

        _logoPaths[tmdbProviderId] = logoPath;
    }

    /// <inheritdoc />
    public async Task<CachedLogo?> GetLogoAsync(int tmdbProviderId, CancellationToken cancellationToken)
    {
        var cachedPath = Path.Combine(
            CacheDirectory,
            $"{tmdbProviderId.ToString(CultureInfo.InvariantCulture)}.img");

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
            _logger.LogWarning(ex, "[ProviderSections] Could not read cached logo for provider {Id}", tmdbProviderId);
        }

        var logoPath = FindLogoPath(tmdbProviderId)
            ?? (_logoPaths.TryGetValue(tmdbProviderId, out var remembered) ? remembered : null);

        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        var download = await _tmdbClient.DownloadImageAsync(logoPath, LogoSize, cancellationToken)
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
            // Serving the logo matters more than caching it; a failed write just
            // means the next request downloads again.
            _logger.LogWarning(ex, "[ProviderSections] Could not cache logo for provider {Id}", tmdbProviderId);
        }

        return new CachedLogo(download.Content, download.ContentType);
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ProviderSections] Could not clear the logo cache");
        }
    }

    /// <summary>
    /// Finds the TMDb logo_path for a provider from the sections that use it.
    /// The path is stored on the section when the admin picks the provider, so
    /// no extra API call is needed to serve a logo. Checked before the remembered
    /// paths because it survives a restart, while those do not.
    /// </summary>
    private static string? FindLogoPath(int tmdbProviderId)
        => Plugin.Instance?.Configuration?.Sections
            .FirstOrDefault(s => s.TmdbProviderId == tmdbProviderId
                && !string.IsNullOrWhiteSpace(s.ProviderLogoPath))
            ?.ProviderLogoPath;

    /// <summary>
    /// Sniffs the image type from magic bytes rather than trusting a stored
    /// extension. TMDb serves PNG for nearly every provider logo, JPEG for a few.
    /// </summary>
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

        if (content.Length >= 4
            && content[0] == 0x3C && content[1] == 0x3F && content[2] == 0x78 && content[3] == 0x6D)
        {
            return "image/svg+xml";
        }

        return "application/octet-stream";
    }
}

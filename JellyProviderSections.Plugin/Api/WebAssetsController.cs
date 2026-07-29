using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyProviderSections.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyProviderSections.Api;

/// <summary>
/// Serves this plugin's static assets: the configuration page's CSS and JS, and
/// the cached provider logos.
///
/// Deliberately not behind [Authorize]. The logo is referenced from the section
/// title markup and is fetched by the browser as a plain image with no session
/// headers; the config page assets are loaded by the plugin-page iframe. None
/// of these responses carry configuration data or secrets, only static files
/// and public artwork already published by TMDb.
/// </summary>
[ApiController]
[Route("JellyProviderSections")]
public sealed class WebAssetsController : ControllerBase
{
    private const string ResourcePrefix = "Jellyfin.Plugin.JellyProviderSections.Web.";

    private readonly IProviderLogoService _logoService;
    private readonly IPosterService _posterService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebAssetsController"/> class.
    /// </summary>
    /// <param name="logoService">Provider logo cache.</param>
    /// <param name="posterService">External title poster cache.</param>
    public WebAssetsController(IProviderLogoService logoService, IPosterService posterService)
    {
        _logoService = logoService;
        _posterService = posterService;
    }

    /// <summary>Serves the configuration page stylesheet.</summary>
    /// <returns>The CSS file.</returns>
    [HttpGet("Configuration/configPage.css")]
    public ActionResult GetConfigPageCss()
        => GetEmbeddedResource("providersections.css", "text/css");

    /// <summary>Serves the configuration page script.</summary>
    /// <returns>The JS file.</returns>
    [HttpGet("Configuration/configPage.js")]
    public ActionResult GetConfigPageScript()
        => GetEmbeddedResource("providersections.js", "application/javascript");

    /// <summary>Serves a provider logo, cached locally after the first fetch.</summary>
    /// <param name="providerId">The TMDb provider id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image, or 404 when the provider has no logo.</returns>
    [HttpGet("Logo/{providerId:int}")]
    public async Task<ActionResult> GetLogo(int providerId, CancellationToken cancellationToken)
    {
        var logo = await _logoService.GetLogoAsync(providerId, cancellationToken).ConfigureAwait(false);

        if (logo is null)
        {
            return NotFound();
        }

        // Provider logos essentially never change, and a stale one is harmless,
        // so this is worth caching hard: it is requested on every home load.
        Response.Headers.CacheControl = "public, max-age=604800";
        return File(logo.Content, logo.ContentType);
    }

    /// <summary>Serves the home screen script injected into Jellyfin Web.</summary>
    /// <returns>The JS file.</returns>
    [HttpGet("Web/home.js")]
    public ActionResult GetHomeScript()
        => GetEmbeddedResource("home.js", "application/javascript");

    /// <summary>
    /// Serves the poster of an external title, cached locally after the first fetch.
    /// Requested by the home script, which knows only the TMDb id.
    /// </summary>
    /// <param name="tmdbId">The TMDb id of the title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image, or 404 when the title is unknown or has no poster.</returns>
    [HttpGet("Poster/{tmdbId:int}")]
    public async Task<ActionResult> GetPoster(int tmdbId, CancellationToken cancellationToken)
    {
        var poster = await _posterService.GetPosterAsync(tmdbId, cancellationToken).ConfigureAwait(false);

        if (poster is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=604800";
        return File(poster.Content, poster.ContentType);
    }

    private ActionResult GetEmbeddedResource(string fileName, string contentType)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache, must-revalidate";
        return File(stream, contentType);
    }
}

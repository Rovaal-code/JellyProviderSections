using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// Serves a bundled service icon.
    ///
    /// Bundled rather than linked from a CDN so the administration page keeps
    /// working on a server with no route to the internet, and so loading it does
    /// not tell a third party who is looking at it. Named explicitly, never from
    /// the request, so this route cannot be walked into the rest of the embedded
    /// resources.
    /// </summary>
    /// <param name="name">The icon name, tmdb or seerr.</param>
    /// <returns>The SVG, or 404 for anything else.</returns>
    [HttpGet("Web/{name}.svg")]
    public ActionResult GetServiceIcon(string name)
    {
        var file = name switch
        {
            "tmdb" => "tmdb.svg",
            "seerr" => "seerr.svg",
            _ => null,
        };

        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=604800";
        return GetEmbeddedResource(file, "image/svg+xml");
    }

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

    /// <summary>
    /// Gets the TMDb rating of several titles at once.
    ///
    /// The home script needs the rating to finish a card, and it knows only the
    /// TMDb id. One call per row rather than one per card: a row holds up to two
    /// hundred of them.
    /// </summary>
    /// <param name="ids">Comma-separated TMDb ids.</param>
    /// <returns>Rating by TMDb id, omitting the ones not known.</returns>
    [HttpGet("Ratings")]
    public ActionResult GetRatings([FromQuery] string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return Ok(new Dictionary<string, double>());
        }

        var parsed = ids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(500)
            .ToList();

        var ratings = _posterService.GetRatings(parsed);

        Response.Headers.CacheControl = "no-cache";
        return Ok(ratings.ToDictionary(
            r => r.Key.ToString(CultureInfo.InvariantCulture),
            r => r.Value));
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

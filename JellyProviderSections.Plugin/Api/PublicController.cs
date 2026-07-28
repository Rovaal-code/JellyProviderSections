using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Jellyfin.Plugin.JellyProviderSections.Models;
using Jellyfin.Plugin.JellyProviderSections.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyProviderSections.Api;

/// <summary>
/// Endpoints called by regular (non-admin) users from the home screen: check a
/// title's availability, and request one through Seerr.
///
/// The requesting identity is always taken from the authenticated Jellyfin
/// session, never from the request body. Accepting a user id from the client
/// would let anyone file requests in someone else's name and burn their quota.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyProviderSections")]
[Produces("application/json")]
public class PublicController : ControllerBase
{
    private readonly ISeerrApiClient _seerrClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicController"/> class.
    /// </summary>
    /// <param name="seerrClient">Seerr client.</param>
    public PublicController(ISeerrApiClient seerrClient)
    {
        _seerrClient = seerrClient;
    }

    /// <summary>Gets non-sensitive feature flags for the client.</summary>
    /// <returns>Flags only, never URLs or keys.</returns>
    [HttpGet("public-settings")]
    public ActionResult GetPublicSettings()
    {
        var config = Plugin.Instance?.Configuration;

        return Ok(new
        {
            seerrEnabled = config?.SeerrSettings.Enabled == true
                && !string.IsNullOrWhiteSpace(config.SeerrSettings.ServerUrl),
            requestsEnabled = config?.Sections.Any(s => s.Enabled && s.RequestsEnabled) == true,
        });
    }

    /// <summary>Gets a title's availability for the current user.</summary>
    /// <param name="tmdbId">The TMDb id.</param>
    /// <param name="contentType">Movie or Series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The availability state.</returns>
    [HttpGet("status/{tmdbId:int}")]
    public async Task<ActionResult> GetStatus(
        int tmdbId,
        [FromQuery] ProviderSectionContentType contentType,
        CancellationToken cancellationToken)
    {
        var mediaInfo = await _seerrClient
            .GetMediaInfoAsync(contentType, tmdbId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new
        {
            tmdbId,
            status = (mediaInfo?.Status ?? SeerrMediaStatus.Unknown).ToString(),
            status4k = (mediaInfo?.Status4k ?? SeerrMediaStatus.Unknown).ToString(),
            seasons = mediaInfo?.Seasons.Select(s => new
            {
                seasonNumber = s.SeasonNumber,
                status = s.Status.ToString(),
                status4k = s.Status4k.ToString(),
            }),
            canRequest = mediaInfo is null
                || mediaInfo.Status is SeerrMediaStatus.Unknown or SeerrMediaStatus.PartiallyAvailable,
        });
    }

    /// <summary>Requests a title through Seerr on behalf of the current user.</summary>
    /// <param name="request">What to request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("request")]
    public async Task<ActionResult> CreateRequest(
        [FromBody] CreateRequestBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jellyfinUserId = GetCurrentUserId();
        if (jellyfinUserId is null)
        {
            return Unauthorized();
        }

        var section = Plugin.Instance?.Configuration.Sections
            .FirstOrDefault(s => string.Equals(s.Id, request.SectionId, StringComparison.OrdinalIgnoreCase));

        // A section with requests switched off must not be a way in, even if the
        // client calls this endpoint directly.
        if (section is not null && !section.RequestsEnabled)
        {
            return BadRequest(new { message = "Esta sección no permite solicitudes." });
        }

        var result = await _seerrClient.CreateRequestAsync(
            request.ContentType,
            request.TmdbId,
            jellyfinUserId,
            request.AllSeasons,
            request.Seasons,
            request.Is4k,
            cancellationToken).ConfigureAwait(false);

        var statusCode = result.Outcome switch
        {
            SeerrRequestOutcome.Created => 200,
            SeerrRequestOutcome.AlreadyRequested => 200,
            SeerrRequestOutcome.NothingToRequest => 200,
            SeerrRequestOutcome.NotPermitted => 403,
            SeerrRequestOutcome.UserNotLinked => 409,
            SeerrRequestOutcome.Unavailable => 503,
            _ => 500,
        };

        return StatusCode(statusCode, new
        {
            outcome = result.Outcome.ToString(),
            message = result.Message,
        });
    }

    /// <summary>
    /// Reads the Jellyfin user id from the authenticated session. Jellyfin's
    /// auth middleware puts it in the claims principal.
    /// </summary>
    private string? GetCurrentUserId()
    {
        var claim = User.FindFirst("Jellyfin-UserId")
            ?? User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("UserId");

        return string.IsNullOrWhiteSpace(claim?.Value) ? null : claim.Value;
    }
}

/// <summary>Body of a request-content call.</summary>
public class CreateRequestBody
{
    /// <summary>Gets or sets the TMDb id.</summary>
    public int TmdbId { get; set; }

    /// <summary>Gets or sets the content type.</summary>
    public ProviderSectionContentType ContentType { get; set; }

    /// <summary>Gets or sets the originating section id, for the requests-enabled check.</summary>
    public string? SectionId { get; set; }

    /// <summary>Gets or sets a value indicating whether every season is requested.</summary>
    public bool AllSeasons { get; set; } = true;

    /// <summary>Gets or sets specific season numbers, when not requesting all.</summary>
    public List<int>? Seasons { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a 4K request.</summary>
    public bool Is4k { get; set; }
}

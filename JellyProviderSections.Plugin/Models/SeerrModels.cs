using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyProviderSections.Models;

/// <summary>
/// Availability state of a title in Seerr.
/// Values verified against seerr-team/seerr server/constants/media.ts (v3.4.0).
/// Note: JellyNotify's own client has Deleted and Blocklisted swapped relative
/// to the real contract; that bug is deliberately not inherited here.
/// </summary>
public enum SeerrMediaStatus
{
    /// <summary>Not tracked by Seerr.</summary>
    Unknown = 1,

    /// <summary>Requested, awaiting approval.</summary>
    Pending = 2,

    /// <summary>Approved and downloading.</summary>
    Processing = 3,

    /// <summary>Some seasons or versions available.</summary>
    PartiallyAvailable = 4,

    /// <summary>Fully available.</summary>
    Available = 5,

    /// <summary>Blocked from being requested.</summary>
    Blocklisted = 6,

    /// <summary>Removed.</summary>
    Deleted = 7,
}

/// <summary>
/// State of a request in Seerr. Includes Completed (5), which JellyNotify's
/// client omits; MediaRequest.request() checks against it explicitly when
/// deciding whether a re-request is a duplicate.
/// </summary>
public enum SeerrRequestStatus
{
    /// <summary>Awaiting admin approval.</summary>
    PendingApproval = 1,

    /// <summary>Approved.</summary>
    Approved = 2,

    /// <summary>Declined by an admin.</summary>
    Declined = 3,

    /// <summary>Failed to process.</summary>
    Failed = 4,

    /// <summary>Completed.</summary>
    Completed = 5,
}

/// <summary>
/// A Seerr user.
/// </summary>
public class SeerrUser
{
    /// <summary>Gets or sets Seerr's internal numeric user id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the linked Jellyfin user GUID, if any.</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string? JellyfinUserId { get; set; }

    /// <summary>Gets or sets the linked Jellyfin username, if any.</summary>
    [JsonPropertyName("jellyfinUsername")]
    public string? JellyfinUsername { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the permission bitmask.</summary>
    [JsonPropertyName("permissions")]
    public int Permissions { get; set; }
}

/// <summary>
/// Per-season availability inside a title's mediaInfo. Carries both the regular
/// and the 4K status, unlike JellyNotify's model which only has the former.
/// </summary>
public class SeerrSeasonStatus
{
    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the non-4K availability status.</summary>
    [JsonPropertyName("status")]
    public SeerrMediaStatus Status { get; set; }

    /// <summary>Gets or sets the 4K availability status.</summary>
    [JsonPropertyName("status4k")]
    public SeerrMediaStatus Status4k { get; set; }
}

/// <summary>
/// The mediaInfo block embedded in a movie/tv details response. Absent when
/// Seerr has no local record for that title at all.
/// </summary>
public class SeerrMediaInfo
{
    /// <summary>Gets or sets the non-4K availability status.</summary>
    [JsonPropertyName("status")]
    public SeerrMediaStatus Status { get; set; }

    /// <summary>Gets or sets the 4K availability status.</summary>
    [JsonPropertyName("status4k")]
    public SeerrMediaStatus Status4k { get; set; }

    /// <summary>Gets or sets the per-season statuses (series only).</summary>
    [JsonPropertyName("seasons")]
    public List<SeerrSeasonStatus> Seasons { get; set; } = new();
}

/// <summary>
/// A movie or series details response. Seerr returns the TMDb payload plus its
/// own mediaInfo; only the fields this plugin needs are mapped.
/// </summary>
public class SeerrMediaDetails
{
    /// <summary>Gets or sets the TMDb id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets Seerr's own tracking info, null when untracked.</summary>
    [JsonPropertyName("mediaInfo")]
    public SeerrMediaInfo? MediaInfo { get; set; }
}

/// <summary>
/// Body of POST /api/v1/request.
/// </summary>
public class SeerrRequestBody
{
    /// <summary>Gets or sets "movie" or "tv".</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "movie";

    /// <summary>Gets or sets the TMDb id (not Seerr's internal media id).</summary>
    [JsonPropertyName("mediaId")]
    public int MediaId { get; set; }

    /// <summary>
    /// Gets or sets the seasons to request. Either a number array or the literal
    /// string "all"; serialized as object for that reason. Null for movies.
    /// </summary>
    [JsonPropertyName("seasons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Seasons { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a 4K request.</summary>
    [JsonPropertyName("is4k")]
    public bool Is4k { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to bypass the target user's quota.
    /// Requires Seerr 3.4.0 or newer; omitted when false.
    /// </summary>
    [JsonPropertyName("ignoreQuota")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IgnoreQuota { get; set; }
}

/// <summary>
/// Why a request attempt ended the way it did. Maps Seerr's real HTTP codes:
/// 403 permission or quota, 409 duplicate, 202 nothing left to request.
/// </summary>
public enum SeerrRequestOutcome
{
    /// <summary>Request created.</summary>
    Created,

    /// <summary>A request for this title already exists (HTTP 409).</summary>
    AlreadyRequested,

    /// <summary>Nothing new to request, e.g. all seasons covered (HTTP 202).</summary>
    NothingToRequest,

    /// <summary>User lacks permission, exceeded quota, or media is blocklisted (HTTP 403).</summary>
    NotPermitted,

    /// <summary>The Jellyfin user has no matching Seerr account.</summary>
    UserNotLinked,

    /// <summary>Seerr is unreachable or misconfigured.</summary>
    Unavailable,

    /// <summary>Anything else.</summary>
    Failed,
}

/// <summary>
/// Result of attempting to create a request.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">A user-facing message, sanitized of secrets.</param>
public record SeerrRequestResult(SeerrRequestOutcome Outcome, string Message);

/// <summary>
/// Outcome of a Seerr connection test.
/// </summary>
/// <param name="Success">Whether the connection succeeded.</param>
/// <param name="Message">A human-readable message, sanitized of secrets.</param>
public record SeerrConnectionResult(bool Success, string Message);

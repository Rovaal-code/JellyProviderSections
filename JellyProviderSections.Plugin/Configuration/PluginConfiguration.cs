using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyProviderSections.Configuration;

/// <summary>
/// Plugin configuration for Jellyfin Provider Sections.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        TmdbSettings = new TmdbSettings();
        SeerrSettings = new SeerrSettings();
        Sections = new List<SectionDefinition>();
    }

    /// <summary>
    /// Gets or sets the configuration schema version. Incremented only when a
    /// breaking (non-additive) change requires an explicit migration step.
    /// See docs/implementation/03-data-model.md.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the TMDb connection settings.
    /// </summary>
    public TmdbSettings TmdbSettings { get; set; }

    /// <summary>
    /// Gets or sets the Seerr connection settings.
    /// </summary>
    public SeerrSettings SeerrSettings { get; set; }

    /// <summary>
    /// Gets or sets the list of configured provider sections.
    /// </summary>
    public List<SectionDefinition> Sections { get; set; }

    /// <summary>
    /// Preserves existing secret values when the incoming payload does not supply
    /// them (i.e. empty/null). The admin UI never re-sends secrets, so without this
    /// every save would wipe previously configured credentials.
    /// Same pattern as JellyNotify's PluginConfiguration.PreserveSecrets.
    /// </summary>
    /// <param name="existing">The currently persisted configuration.</param>
    /// <param name="incoming">The configuration submitted by the admin UI.</param>
    public static void PreserveSecrets(PluginConfiguration existing, PluginConfiguration incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        incoming.TmdbSettings ??= new TmdbSettings();
        incoming.SeerrSettings ??= new SeerrSettings();
        incoming.Sections ??= new List<SectionDefinition>();

        if (string.IsNullOrWhiteSpace(incoming.TmdbSettings.ApiKey))
        {
            incoming.TmdbSettings.ApiKey = existing.TmdbSettings?.ApiKey ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(incoming.SeerrSettings.ApiKey))
        {
            incoming.SeerrSettings.ApiKey = existing.SeerrSettings?.ApiKey ?? string.Empty;
        }

        // Sync/diagnostic state is written by the plugin itself, never by the config
        // form, so an admin save must carry over whatever was already there rather
        // than blanking it.
        if (existing.Sections is not null)
        {
            foreach (var incomingSection in incoming.Sections)
            {
                var existingSection = existing.Sections.FirstOrDefault(
                    s => string.Equals(s.Id, incomingSection.Id, StringComparison.OrdinalIgnoreCase));
                if (existingSection is null)
                {
                    continue;
                }

                incomingSection.CreatedUtc = existingSection.CreatedUtc;
                incomingSection.LastSyncUtc = existingSection.LastSyncUtc;
                incomingSection.LastSyncResult = existingSection.LastSyncResult;
                incomingSection.LastError = existingSection.LastError;
            }
        }
    }
}

/// <summary>
/// Connection settings for TMDb.
/// </summary>
public class TmdbSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the TMDb integration is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the TMDb API Key (v3 auth, sent as the "api_key" query string
    /// parameter). Decision 2026-07-29: v3 key instead of the v4 Bearer Read
    /// Access Token originally proposed, same endpoint coverage, simpler auth.
    /// Never sent back to the frontend once saved (see PreserveSecrets).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Connection settings for Seerr.
/// </summary>
public class SeerrSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the Seerr integration is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Seerr server URL (e.g., http://localhost:5055).
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Seerr API key. Never sent back to the frontend once saved.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether SSL certificate validation is skipped.
    /// </summary>
    public bool IgnoreSslErrors { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an admin may bypass a user's request
    /// quota when creating a request on their behalf (Seerr 3.4.0+ only).
    /// </summary>
    public bool AllowIgnoreQuota { get; set; }
}

/// <summary>
/// Content type a section queries for. No "Mixed" value by design, see
/// docs/implementation/01-product-requirements.md (mixed movie+series rows are
/// out of scope: no deterministic interleaving algorithm was found).
/// </summary>
public enum ProviderSectionContentType
{
    /// <summary>Movies.</summary>
    Movie,

    /// <summary>TV series.</summary>
    Series,

    /// <summary>
    /// Both, in one row. TMDb has no combined discover endpoint, so the section
    /// runs the movie and the series query separately and interleaves them one
    /// for one, each keeping its own ranking.
    ///
    /// Deliberately not merged by a shared score: TMDb's popularity is not
    /// comparable across the two endpoints, so sorting the union by it lets one
    /// type crowd out the other. Alternating needs no cross-type metric and is
    /// stable, which is what makes the row look intentional rather than random.
    /// </summary>
    Mixed,
}

/// <summary>
/// Logical sort order for a section, translated to the real TMDb discover
/// parameter (which differs between movie and tv) at query time.
/// </summary>
public enum ProviderSectionSortBy
{
    /// <summary>TMDb popularity, descending.</summary>
    Popularity,

    /// <summary>Average rating, descending.</summary>
    RatingDesc,

    /// <summary>Release / first air date, descending.</summary>
    ReleaseDateDesc,

    /// <summary>Title, ascending.</summary>
    TitleAsc,
}

/// <summary>
/// Outcome of the most recent synchronization attempt for a section.
/// </summary>
public enum ProviderSectionSyncResult
{
    /// <summary>Never synchronized yet.</summary>
    NeverRun,

    /// <summary>Completed without errors.</summary>
    Success,

    /// <summary>Completed but some pages or lookups failed.</summary>
    PartialFailure,

    /// <summary>Failed outright.</summary>
    Failure,
}

/// <summary>
/// A single administrator-defined provider section.
/// See docs/implementation/03-data-model.md for the full field rationale.
/// </summary>
public class SectionDefinition
{
    /// <summary>
    /// Prefix applied to every generated section id.
    ///
    /// Jellyfin Web puts the section id straight into a CSS class and then looks
    /// the row up with <c>querySelector('.' + id)</c>. A bare GUID starting with
    /// a digit is not a valid CSS identifier, so that call throws a SyntaxError
    /// and aborts the whole home render, not just this row. The leading letters
    /// keep every id a valid selector.
    /// </summary>
    public const string IdPrefix = "jps";

    /// <summary>
    /// Gets or sets the stable identifier for this section. Generated server-side
    /// on creation and never changed afterwards: Home Screen Sections uses this
    /// exact string to persist per-user position and enabled state across
    /// restarts, so changing it would silently reset the user's layout.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Creates a new section id that is safe to use as a CSS class selector.
    /// </summary>
    /// <returns>The generated id.</returns>
    public static string NewId() => IdPrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets a value indicating whether an id can be used as a CSS class selector.
    /// Ids created before <see cref="NewId"/> existed start with a digit and cannot.
    /// </summary>
    /// <param name="id">The id to check.</param>
    /// <returns><c>true</c> when the id is a valid CSS identifier.</returns>
    public static bool IsCssSafeId(string? id) =>
        !string.IsNullOrEmpty(id) && (char.IsAsciiLetter(id[0]) || id[0] == '_');

    /// <summary>
    /// Gets or sets when this section was created (UTC).
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets when this section was last modified (UTC).
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the display name shown in the section title. HTML-encoded
    /// before being embedded in the Home Screen Sections displayText payload:
    /// never treat this value as safe HTML (see DisplayTextBuilder).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this section is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets this section's position within the plugin's own list. Not to
    /// be confused with Home Screen Sections' OrderIndex, which lives in that
    /// plugin's configuration and is owned by the user, not by us.
    /// </summary>
    public int OrderHint { get; set; }

    /// <summary>
    /// Gets or sets the TMDb watch-provider identifier (stable across movie/tv).
    /// </summary>
    public int TmdbProviderId { get; set; }

    /// <summary>
    /// Gets or sets the provider's display name, resolved from TMDb and cached
    /// here for offline display. Not the source of truth (TmdbProviderId is).
    /// </summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider's TMDb logo_path, resolved to a full URL by
    /// ProviderLogoService.
    /// </summary>
    public string ProviderLogoPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type this section queries for.
    /// </summary>
    public ProviderSectionContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the ISO 3166-1 watch region code (e.g. "ES").
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ISO 639-1 metadata language for titles and overviews.
    /// </summary>
    public string MetadataLanguage { get; set; } = "es-ES";

    /// <summary>
    /// Gets or sets the logical sort order.
    /// </summary>
    public ProviderSectionSortBy SortBy { get; set; } = ProviderSectionSortBy.Popularity;

    /// <summary>
    /// Gets or sets the maximum number of items to surface in this section.
    /// </summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>
    /// Gets or sets the TMDb genre ids to include (AND semantics are not used, see
    /// DiscoverQueryBuilder: these are joined with "|" for OR).
    /// </summary>
    public List<int> IncludeGenreIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the TMDb genre ids to exclude.
    /// </summary>
    public List<int> ExcludeGenreIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the ISO 639-1 original language filter, or null for any.
    /// </summary>
    public string? OriginalLanguage { get; set; }

    /// <summary>
    /// Gets or sets the ISO 3166-1 origin country filter, or null for any.
    /// </summary>
    public string? OriginCountry { get; set; }

    /// <summary>
    /// Gets or sets the earliest release / first air date, ISO "yyyy-MM-dd".
    /// Stored as a string rather than DateOnly because Jellyfin persists plugin
    /// configuration with XmlSerializer, which cannot serialize DateOnly, and
    /// because this is the exact format TMDb expects in the query string.
    /// </summary>
    public string? MinDate { get; set; }

    /// <summary>
    /// Gets or sets the latest release / first air date, ISO "yyyy-MM-dd".
    /// </summary>
    public string? MaxDate { get; set; }

    /// <summary>
    /// Gets or sets the minimum average rating (0-10), or null for no floor.
    /// </summary>
    public double? MinRating { get; set; }

    /// <summary>
    /// Gets or sets the minimum vote count. Defaults to a non-zero value because
    /// sorting by rating without it surfaces titles with a handful of perfect
    /// votes (see docs/research/05-tmdb-provider-analysis.md).
    /// </summary>
    public int MinVoteCount { get; set; } = 50;

    /// <summary>
    /// Gets or sets a value indicating whether adult content is included.
    /// </summary>
    public bool IncludeAdult { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether users may request unavailable
    /// content from this section through Seerr.
    /// </summary>
    public bool RequestsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how long this section's discover results stay cached.
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 360;

    /// <summary>
    /// Gets or sets when this section last synchronized successfully (UTC).
    /// </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the outcome of the last synchronization.
    /// </summary>
    public ProviderSectionSyncResult LastSyncResult { get; set; } = ProviderSectionSyncResult.NeverRun;

    /// <summary>
    /// Gets or sets the last error message (sanitized, never contains secrets).
    /// </summary>
    public string? LastError { get; set; }
}

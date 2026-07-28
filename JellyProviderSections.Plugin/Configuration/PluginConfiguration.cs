using System.Collections.Generic;
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
    /// breaking (non-additive) change to <see cref="SectionDefinition"/> or this
    /// class requires an explicit migration step. See
    /// docs/provider-sections/implementation/03-data-model.md.
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
    /// Gets or sets the TMDb API Read Access Token (Bearer). Never sent back to
    /// the frontend once saved — see PreserveSecrets pattern, added in the phase
    /// that implements the admin config endpoints.
    /// </summary>
    public string ApiReadAccessToken { get; set; } = string.Empty;
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
    /// Gets or sets a value indicating whether an admin can bypass a user's request
    /// quota when creating a request on their behalf (Seerr 3.4.0+ only).
    /// </summary>
    public bool AllowIgnoreQuota { get; set; }
}

/// <summary>
/// Content type a section queries for. No "Mixed" value by design — see
/// docs/provider-sections/implementation/01-product-requirements.md (mixed
/// movie+series rows are out of scope).
/// </summary>
public enum ProviderSectionContentType
{
    /// <summary>Movies.</summary>
    Movie,

    /// <summary>TV series.</summary>
    Series,
}

/// <summary>
/// Logical sort order for a section, translated to the real TMDb discover
/// parameter (which differs by content type) at query time.
/// </summary>
public enum ProviderSectionSortBy
{
    /// <summary>Sort by TMDb popularity, descending.</summary>
    Popularity,

    /// <summary>Sort by rating, descending.</summary>
    RatingDesc,

    /// <summary>Sort by release/air date, descending.</summary>
    ReleaseDateDesc,

    /// <summary>Sort by title, ascending.</summary>
    TitleAsc,
}

/// <summary>
/// A single administrator-defined provider section.
/// See docs/provider-sections/implementation/03-data-model.md for the full field
/// rationale. This is the phase-3 skeleton shape; fields are added here as each
/// later phase needs them, never removed or renamed without a schema migration.
/// </summary>
public class SectionDefinition
{
    /// <summary>
    /// Gets or sets the stable identifier for this section. Generated server-side
    /// on creation and never changed afterwards — Home Screen Sections uses this
    /// same string to persist per-user position/enabled state across restarts.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown in the section title (HTML-encoded
    /// before being embedded in the HSS displayText payload — never trust this
    /// value as safe HTML).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this section is active.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the TMDb watch-provider identifier.
    /// </summary>
    public int TmdbProviderId { get; set; }

    /// <summary>
    /// Gets or sets the ISO 3166-1 region code.
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type this section queries for.
    /// </summary>
    public ProviderSectionContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the logical sort order.
    /// </summary>
    public ProviderSectionSortBy SortBy { get; set; } = ProviderSectionSortBy.Popularity;

    /// <summary>
    /// Gets or sets the maximum number of items to fetch for this section.
    /// </summary>
    public int MaxItems { get; set; } = 20;
}

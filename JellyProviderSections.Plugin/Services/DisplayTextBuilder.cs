using System;
using System.Net;
using Jellyfin.Plugin.JellyProviderSections.Configuration;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Builds the displayText that Home Screen Sections renders as a section title.
///
/// SECURITY, do not simplify away: HSS assigns displayText with innerHTML and
/// never escapes it (verified in loadSections.js, see
/// docs/research/08-provider-logo-rendering.md). That is what lets us put a
/// provider logo next to the title with no client-side JavaScript of our own,
/// but it also means anything we interpolate into this string is live HTML in
/// every user's browser.
///
/// The section name is administrator-supplied free text, so it is ALWAYS
/// HTML-encoded. Only the img tag is literal markup, and its src is built from
/// a numeric provider id we control, never from user input. Without the
/// encoding this is a stored XSS against every user of the server, not just the
/// admin who typed it.
/// </summary>
public static class DisplayTextBuilder
{
    /// <summary>
    /// Builds the section title markup: provider logo followed by the name.
    /// </summary>
    /// <param name="section">The section definition.</param>
    /// <param name="includeLogo">
    /// Whether to prepend the logo. False falls back to a plain encoded title,
    /// used when the provider has no logo or logo serving is disabled.
    /// </param>
    /// <returns>The displayText to register with Home Screen Sections.</returns>
    public static string Build(SectionDefinition section, bool includeLogo = true)
    {
        ArgumentNullException.ThrowIfNull(section);

        var safeName = WebUtility.HtmlEncode(section.DisplayName ?? string.Empty);

        if (!includeLogo || string.IsNullOrWhiteSpace(section.ProviderLogoPath))
        {
            return safeName;
        }

        // The id is an int, so it cannot inject anything into the attribute, but
        // it is encoded anyway rather than relying on that invariant holding.
        var logoUrl = WebUtility.HtmlEncode(BuildLogoUrl(section.TmdbProviderId));
        var providerAlt = WebUtility.HtmlEncode(section.ProviderDisplayName ?? string.Empty);

        // onerror hides a broken logo instead of leaving a torn image icon and a
        // gap that would shift the title sideways.
        return $"<img src=\"{logoUrl}\" alt=\"{providerAlt}\" class=\"jps-section-logo\" "
             + "style=\"height:1.2em;width:auto;max-width:6em;object-fit:contain;vertical-align:-0.2em;margin-right:0.45em;\" "
             + "onerror=\"this.style.display='none'\" />"
             + $"<span class=\"jps-section-title\">{safeName}</span>";
    }

    /// <summary>
    /// Builds the plugin-served URL for a provider's cached logo.
    /// </summary>
    /// <param name="tmdbProviderId">The TMDb provider id.</param>
    /// <returns>A root-relative URL.</returns>
    public static string BuildLogoUrl(int tmdbProviderId)
        => $"/JellyProviderSections/Logo/{tmdbProviderId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

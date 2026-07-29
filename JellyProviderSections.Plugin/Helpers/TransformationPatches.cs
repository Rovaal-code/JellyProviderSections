using System;
using Jellyfin.Plugin.JellyProviderSections.Model;

namespace Jellyfin.Plugin.JellyProviderSections.Helpers;

/// <summary>
/// Transformation callbacks File Transformation invokes by reflection.
///
/// Public and static because that is how it resolves them from the registration
/// payload; nothing in this plugin calls them directly.
/// </summary>
public static class TransformationPatches
{
    private const string ScriptTag =
        "<script defer src=\"/JellyProviderSections/Web/home.js\"></script>";

    /// <summary>
    /// Adds this plugin's home-screen script to Jellyfin Web's index page.
    /// </summary>
    /// <param name="payload">The file being served.</param>
    /// <returns>The contents with the script tag added.</returns>
    public static string IndexHtml(PatchRequestPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var contents = payload.Contents;

        // Several plugins transform this same file, and File Transformation may
        // hand us contents another one already modified. Adding the tag twice
        // would run the script twice.
        if (string.IsNullOrEmpty(contents) || contents.Contains(ScriptTag, StringComparison.Ordinal))
        {
            return contents;
        }

        var closingBody = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

        // No </body> means this is not the document we expected. Returning the
        // input unchanged degrades to "cards without artwork", which is far
        // better than corrupting the page every client loads.
        return closingBody < 0
            ? contents
            : contents.Insert(closingBody, ScriptTag);
    }
}

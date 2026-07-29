namespace Jellyfin.Plugin.JellyProviderSections.Model;

/// <summary>
/// Body File Transformation hands to a transformation callback.
///
/// Declared here rather than referenced from File Transformation on purpose:
/// this plugin has no compile-time dependency on it, and File Transformation
/// deserializes the request into whatever type the callback declares, so an
/// own type with a matching shape is all it needs. Home Screen Sections
/// declares its own copy for exactly the same reason.
/// </summary>
public sealed class PatchRequestPayload
{
    /// <summary>Gets or sets the current contents of the file being served.</summary>
    public string Contents { get; set; } = string.Empty;
}

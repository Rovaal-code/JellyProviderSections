using System;

namespace Jellyfin.Plugin.JellyProviderSections.Models;

/// <summary>
/// The payload Home Screen Sections hands to our results provider.
///
/// This mirrors HSS's own Model.Dto.HomeScreenSectionPayload rather than
/// referencing it: the whole integration is deliberately assembly-reference
/// free. HSS binds its JSON onto whatever type our GetResults parameter
/// declares (`jsonPayload.ToObject(method.GetParameters()[0].ParameterType)`),
/// so a plain class with matching property names is all that is needed. This is
/// the same approach jellyfin-plugin-collection-sections takes.
///
/// Declaring `object` here would not work: HSS would hand back a raw JObject,
/// whose CLR properties are Newtonsoft internals, not UserId and AdditionalData.
/// </summary>
public class HomeScreenSectionPayload
{
    /// <summary>
    /// Gets or sets the user the row is being rendered for.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the free-form string supplied at registration time. This
    /// plugin puts the section id there.
    /// </summary>
    public string? AdditionalData { get; set; }
}

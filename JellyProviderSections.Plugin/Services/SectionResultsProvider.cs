using System;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.JellyProviderSections.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// The entry point Home Screen Sections calls to fill one of our rows.
///
/// HSS instantiates this type through ActivatorUtilities using its own DI
/// container, then invokes GetResults by reflection. Two consequences shape
/// this class:
///
/// 1. Constructor parameters must be resolvable from HSS's container, so only
///    Jellyfin core services are injected here. This plugin's own services come
///    from PluginServiceLocator instead.
/// 2. HSS binds its JSON payload onto this method's declared parameter type, so
///    the parameter is a concrete class (see HomeScreenSectionPayload) rather
///    than object, which would arrive as an opaque JObject.
/// </summary>
public class SectionResultsProvider
{
    private readonly IUserManager _userManager;
    private readonly ILogger<SectionResultsProvider>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionResultsProvider"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin's user manager.</param>
    /// <param name="logger">Logger, optional depending on HSS's container.</param>
    public SectionResultsProvider(IUserManager userManager, ILogger<SectionResultsProvider>? logger = null)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns the items for one section. Called by Home Screen Sections.
    /// </summary>
    /// <param name="payload">The section payload, carrying the user and section id.</param>
    /// <returns>The items to render in the row.</returns>
    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload)
    {
        try
        {
            var sectionId = payload?.AdditionalData;

            if (string.IsNullOrWhiteSpace(sectionId))
            {
                _logger?.LogWarning("[ProviderSections] Section payload carried no section id");
                return Empty();
            }

            var section = Plugin.Instance?.Configuration.Sections
                .FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));

            if (section is null || !section.Enabled)
            {
                return Empty();
            }

            var user = payload!.UserId != Guid.Empty
                ? _userManager.GetUserById(payload.UserId)
                : null;

            var builder = PluginServiceLocator.Get<ISectionContentBuilder>();
            if (builder is null)
            {
                _logger?.LogWarning(
                    "[ProviderSections] Content builder unavailable, returning an empty row for {Id}",
                    sectionId);
                return Empty();
            }

            // HSS's call path is synchronous, so the async work is bridged here
            // rather than leaking sync-over-async into the builder itself.
            var items = builder
                .BuildAsync(section, user, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _logger?.LogInformation(
                "[ProviderSections] Section {Name} returned {Count} item(s)",
                section.DisplayName,
                items.Count);

            return new QueryResult<BaseItemDto>(0, items.Count, items.ToArray());
        }
        catch (Exception ex)
        {
            // Never throw into HSS: one failing row must not break the whole
            // home screen for the user.
            _logger?.LogError(ex, "[ProviderSections] Failed to build section results");
            return Empty();
        }
    }

    private static QueryResult<BaseItemDto> Empty()
        => new(0, 0, Array.Empty<BaseItemDto>());
}

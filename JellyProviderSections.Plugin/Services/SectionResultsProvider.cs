using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
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
/// 2. The payload type belongs to HSS and cannot be referenced at compile time,
///    so it is accepted as object and read reflectively. That also makes this
///    resilient to the payload type changing shape between HSS versions.
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
    /// <param name="payload">
    /// HSS's section payload. Read reflectively for AdditionalData (our section
    /// id) and UserId.
    /// </param>
    /// <returns>The items to render in the row.</returns>
    public QueryResult<BaseItemDto> GetResults(object? payload)
    {
        try
        {
            var sectionId = ReadProperty(payload, "AdditionalData") as string;
            var userId = ParseGuid(ReadProperty(payload, "UserId"));

            if (string.IsNullOrWhiteSpace(sectionId))
            {
                return Empty();
            }

            var configuration = Plugin.Instance?.Configuration;
            var section = configuration?.Sections
                .FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));

            if (section is null || !section.Enabled)
            {
                return Empty();
            }

            var user = userId != Guid.Empty ? _userManager.GetUserById(userId) : null;

            var builder = PluginServiceLocator.Get<ISectionContentBuilder>();
            if (builder is null)
            {
                _logger?.LogWarning(
                    "[ProviderSections] Section content builder is not available yet, returning an empty row for {Id}",
                    sectionId);
                return Empty();
            }

            // HSS's call path is synchronous, so the async work is bridged here
            // rather than leaking a sync-over-async pattern into the builder.
            var items = builder
                .BuildAsync(section, user, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return new QueryResult<BaseItemDto>(
                0,
                items.Count,
                items.ToArray());
        }
        catch (Exception ex)
        {
            // Never throw into HSS: a failing row must degrade to an empty row,
            // not break the whole home screen for the user.
            _logger?.LogError(ex, "[ProviderSections] Failed to build section results");
            return Empty();
        }
    }

    private static QueryResult<BaseItemDto> Empty()
        => new(0, 0, Array.Empty<BaseItemDto>());

    private static object? ReadProperty(object? source, string propertyName)
    {
        if (source is null)
        {
            return null;
        }

        var property = source.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return property?.GetValue(source);
    }

    private static Guid ParseGuid(object? value) => value switch
    {
        Guid guid => guid,
        string text when Guid.TryParse(text, out var parsed) => parsed,
        _ => Guid.Empty,
    };
}

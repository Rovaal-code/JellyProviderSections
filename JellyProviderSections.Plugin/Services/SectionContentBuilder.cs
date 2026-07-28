using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Turns a section definition into the list of cards to show: query TMDb,
/// resolve against the local library, cache the outcome.
/// </summary>
public interface ISectionContentBuilder
{
    /// <summary>Builds the items for a section, using cache when it is fresh.</summary>
    /// <param name="section">The section definition.</param>
    /// <param name="user">The viewing user, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items to render.</returns>
    Task<IReadOnlyList<BaseItemDto>> BuildAsync(
        SectionDefinition section,
        User? user,
        CancellationToken cancellationToken);

    /// <summary>Drops every cached artefact belonging to a section.</summary>
    /// <param name="sectionId">The section id.</param>
    void InvalidateSection(string sectionId);
}

/// <inheritdoc cref="ISectionContentBuilder" />
public sealed class SectionContentBuilder : ISectionContentBuilder
{
    private readonly ITmdbApiClient _tmdbClient;
    private readonly ILibraryResolver _libraryResolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SectionContentBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionContentBuilder"/> class.
    /// </summary>
    /// <param name="tmdbClient">TMDb client.</param>
    /// <param name="libraryResolver">Local library resolver.</param>
    /// <param name="cache">Shared in-memory cache.</param>
    /// <param name="logger">Logger.</param>
    public SectionContentBuilder(
        ITmdbApiClient tmdbClient,
        ILibraryResolver libraryResolver,
        IMemoryCache cache,
        ILogger<SectionContentBuilder> logger)
    {
        _tmdbClient = tmdbClient;
        _libraryResolver = libraryResolver;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BaseItemDto>> BuildAsync(
        SectionDefinition section,
        User? user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(section);

        // The TMDb half is cached per section and shared by every user; the
        // library resolution half is per user, because visibility and watch
        // state differ. Caching only the former keeps one user's permissions
        // from ever leaking into another user's row.
        var discoverResults = await GetDiscoverResultsAsync(section, cancellationToken).ConfigureAwait(false);

        if (discoverResults.Count == 0)
        {
            return Array.Empty<BaseItemDto>();
        }

        return _libraryResolver.Resolve(discoverResults, section, user);
    }

    /// <inheritdoc />
    public void InvalidateSection(string sectionId)
    {
        _cache.Remove(DiscoverCacheKey(sectionId));
    }

    private async Task<IReadOnlyList<Models.TmdbDiscoverItem>> GetDiscoverResultsAsync(
        SectionDefinition section,
        CancellationToken cancellationToken)
    {
        var cacheKey = DiscoverCacheKey(section.Id);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Models.TmdbDiscoverItem>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var results = await _tmdbClient.DiscoverAllAsync(section, cancellationToken).ConfigureAwait(false);

            if (results.Count > 0)
            {
                var ttl = TimeSpan.FromMinutes(Math.Max(1, section.CacheDurationMinutes));
                _cache.Set(cacheKey, results, ttl);

                section.LastSyncUtc = DateTime.UtcNow;
                section.LastSyncResult = results.Count >= section.MaxItems
                    ? ProviderSectionSyncResult.Success
                    : ProviderSectionSyncResult.PartialFailure;
                section.LastError = null;
            }
            else
            {
                // Zero results is a legitimate answer (an empty provider catalogue
                // for that region), not necessarily a failure, so it is recorded
                // as a partial rather than a hard error.
                section.LastSyncUtc = DateTime.UtcNow;
                section.LastSyncResult = ProviderSectionSyncResult.PartialFailure;
                section.LastError = "La consulta no devolvió resultados.";
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProviderSections] Discover failed for section {Id}", section.Id);
            section.LastSyncUtc = DateTime.UtcNow;
            section.LastSyncResult = ProviderSectionSyncResult.Failure;
            section.LastError = ex.Message;
            return Array.Empty<Models.TmdbDiscoverItem>();
        }
    }

    private static string DiscoverCacheKey(string sectionId) => $"jps:discover:{sectionId}";
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Jellyfin.Plugin.JellyProviderSections.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Resolves TMDb discover results against the local Jellyfin library.
///
/// Titles the user already has become real BaseItemDto instances, so they open
/// the normal detail page, play, and keep watch state. Titles that are missing,
/// or that live in a library this user cannot see, become synthetic DTOs that
/// carry TMDb metadata and a request affordance instead.
///
/// The "cannot see" case matters: a user must never be able to infer, from this
/// section, that content exists in a library they were not granted access to.
/// Both layers of Jellyfin's visibility model are used, the query-level
/// restriction and an explicit per-item IsVisible check.
/// </summary>
public interface ILibraryResolver
{
    /// <summary>
    /// Maps discover results to DTOs, resolving local items where possible.
    /// </summary>
    /// <param name="items">The TMDb discover results.</param>
    /// <param name="section">The section they came from.</param>
    /// <param name="user">The user viewing the section, or null.</param>
    /// <returns>The DTOs in the same order as the input.</returns>
    IReadOnlyList<BaseItemDto> Resolve(
        IReadOnlyList<TmdbDiscoverItem> items,
        SectionDefinition section,
        User? user);
}

/// <inheritdoc cref="ILibraryResolver" />
public sealed class LibraryResolver : ILibraryResolver
{
    /// <summary>
    /// ProviderIds key used to mark a synthetic (not-in-library) item, so the
    /// frontend can tell it apart from a real one. Mirrors how Home Screen
    /// Sections' own Discover row flags its synthetic items.
    /// </summary>
    public const string ExternalMarkerKey = "JellyProviderSections";

    /// <summary>ProviderIds key carrying the poster URL for a synthetic item.</summary>
    public const string PosterUrlKey = "JellyProviderSectionsPoster";

    /// <summary>ProviderIds key carrying the owning section id.</summary>
    public const string SectionIdKey = "JellyProviderSectionsSection";

    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly IServerApplicationHost _appHost;
    private readonly IPosterService _posterService;
    private readonly ILogger<LibraryResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="dtoService">Jellyfin's DTO service.</param>
    /// <param name="appHost">Server host, used for the server id every DTO must carry.</param>
    /// <param name="posterService">Poster cache, told about every external title seen.</param>
    /// <param name="logger">Logger.</param>
    public LibraryResolver(
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IServerApplicationHost appHost,
        IPosterService posterService,
        ILogger<LibraryResolver> logger)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _appHost = appHost;
        _posterService = posterService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItemDto> Resolve(
        IReadOnlyList<TmdbDiscoverItem> items,
        SectionDefinition section,
        User? user)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(section);

        var results = new List<BaseItemDto>(items.Count);

        foreach (var item in items)
        {
            // A mixed section holds both kinds, so the lookup follows the item.
            var localItem = FindLocalItem(item.Id, item.ItemContentType, user);

            if (localItem is not null)
            {
                // Dropped rather than replaced: the row exists to surface what
                // the provider has and this server does not, and the library has
                // its own rows for the rest. Evaluated per user, so a title in a
                // library someone cannot see is not "already in the library" for
                // them and is still offered.
                if (section.HideLibraryItems)
                {
                    continue;
                }

                var dto = BuildLocalDto(localItem, user);
                if (dto is not null)
                {
                    results.Add(dto);
                    continue;
                }
            }

            results.Add(BuildSyntheticDto(item, section));
        }

        return results;
    }

    /// <summary>
    /// Looks up a title by TMDb id. The query is backed by a real composite index
    /// on (ProviderId, ProviderValue, ItemId), so this does not scan the library.
    /// </summary>
    private BaseItem? FindLocalItem(int tmdbId, ProviderSectionContentType contentType, User? user)
    {
        var itemKind = contentType == ProviderSectionContentType.Movie
            ? BaseItemKind.Movie
            : BaseItemKind.Series;

        try
        {
            // Passing the user makes Jellyfin restrict the query to libraries that
            // user can see, and apply their parental rating and tag restrictions,
            // all at the SQL level.
            var query = new InternalItemsQuery(user)
            {
                HasAnyProviderId = new Dictionary<string, string>
                {
                    [MetadataProvider.Tmdb.ToString()] = tmdbId.ToString(CultureInfo.InvariantCulture),
                },
                IncludeItemTypes = new[] { itemKind },
                Recursive = true,
                Limit = 1,
            };

            var found = _libraryManager.GetItemList(query).FirstOrDefault();

            // Defence in depth: the query-level restriction above only kicks in
            // when ParentId/AncestorIds/TopParentIds are all unset. Re-checking
            // per item keeps this correct even if that ever changes.
            if (found is not null && user is not null && !found.IsVisible(user))
            {
                return null;
            }

            return found;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyProvider Sections] Local lookup failed for TMDb id {TmdbId}", tmdbId);
            return null;
        }
    }

    private BaseItemDto? BuildLocalDto(BaseItem item, User? user)
    {
        try
        {
            var options = new DtoOptions
            {
                // Brings back played state and resume position in the same pass,
                // so the card shows real progress instead of looking unwatched.
                EnableUserData = true,
                EnableImages = true,
            };

            return _dtoService.GetBaseItemDto(item, options, user);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyProvider Sections] Failed to build DTO for local item {ItemId}", item.Id);
            return null;
        }
    }

    /// <summary>
    /// Builds a DTO for a title that is not in the library (or not visible to
    /// this user). ProviderIds doubles as a metadata bag, the same trick Home
    /// Screen Sections uses for its own Discover row.
    /// </summary>
    private BaseItemDto BuildSyntheticDto(TmdbDiscoverItem item, SectionDefinition section)
    {
        var dto = new BaseItemDto
        {
            Id = BuildDeterministicId(item.Id, item.ItemContentType),
            // Jellyfin Web's card builder throws "item or serverId cannot be null"
            // while laying out a Series card without it, and that exception takes
            // the whole row down: the section renders with its title and zero
            // cards. Movie cards happen to survive, which is what made this look
            // like a content-type problem rather than a missing field.
            ServerId = _appHost.SystemId,
            Name = item.Title,
            OriginalTitle = item.OriginalTitle,
            Overview = item.Overview,
            CommunityRating = (float)item.VoteAverage,
            Type = item.ItemContentType == ProviderSectionContentType.Movie
                ? BaseItemKind.Movie
                : BaseItemKind.Series,
            ProviderIds = new Dictionary<string, string>
            {
                [MetadataProvider.Tmdb.ToString()] = item.Id.ToString(CultureInfo.InvariantCulture),
                [ExternalMarkerKey] = "1",
                [SectionIdKey] = section.Id,
            },
        };

        if (!string.IsNullOrWhiteSpace(item.PosterPath))
        {
            dto.ProviderIds[PosterUrlKey] = item.PosterPath;

            // The home script only learns a card's TMDb id, so the path has to be
            // on the server by the time it asks for the artwork.
            _posterService.Remember(item.Id, item.PosterPath, item.VoteAverage);
        }

        if (DateTime.TryParse(
                item.ReleaseDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var premiereDate))
        {
            dto.PremiereDate = premiereDate;
            dto.ProductionYear = premiereDate.Year;
        }

        return dto;
    }

    /// <summary>
    /// Derives a stable GUID from the TMDb id so the same external title keeps
    /// the same DTO id across renders. A random GUID would make the client treat
    /// every refresh as a brand new card.
    /// </summary>
    private static Guid BuildDeterministicId(int tmdbId, ProviderSectionContentType contentType)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(tmdbId).CopyTo(bytes, 0);
        bytes[15] = contentType == ProviderSectionContentType.Movie ? (byte)1 : (byte)2;

        // Marker bytes keep these from colliding with real Jellyfin item ids.
        bytes[4] = 0x4A; // 'J'
        bytes[5] = 0x50; // 'P'
        bytes[6] = 0x53; // 'S'

        return new Guid(bytes);
    }
}

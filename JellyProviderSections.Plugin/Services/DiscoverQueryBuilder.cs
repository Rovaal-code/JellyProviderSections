using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.JellyProviderSections.Configuration;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Translates a <see cref="SectionDefinition"/> into the real TMDb discover
/// query parameters. Pure, no I/O, so the whole translation table is unit
/// testable without touching the network.
///
/// Movie and TV endpoints do NOT share parameter names for date and title
/// ordering (primary_release_date vs first_air_date, original_title vs name),
/// which is exactly why SectionDefinition stores a logical SortBy enum rather
/// than a raw TMDb string. See docs/research/05-tmdb-provider-analysis.md.
///
/// No monetization parameter is ever emitted. with_watch_monetization_types
/// exists in the TMDb API but is deliberately excluded from this plugin.
/// </summary>
public static class DiscoverQueryBuilder
{
    /// <summary>
    /// Gets the endpoint path for a content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>The TMDb discover path.</returns>
    public static string GetEndpoint(ProviderSectionContentType contentType)
        => contentType == ProviderSectionContentType.Movie ? "discover/movie" : "discover/tv";

    /// <summary>
    /// Translates the logical sort order into the real TMDb sort_by value for a
    /// given content type.
    /// </summary>
    /// <param name="sortBy">The logical sort order.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>The TMDb sort_by parameter value.</returns>
    public static string GetSortBy(ProviderSectionSortBy sortBy, ProviderSectionContentType contentType)
    {
        var isMovie = contentType == ProviderSectionContentType.Movie;

        return sortBy switch
        {
            ProviderSectionSortBy.Popularity => "popularity.desc",
            ProviderSectionSortBy.RatingDesc => "vote_average.desc",
            ProviderSectionSortBy.ReleaseDateDesc => isMovie ? "primary_release_date.desc" : "first_air_date.desc",
            ProviderSectionSortBy.TitleAsc => isMovie ? "original_title.asc" : "name.asc",
            _ => "popularity.desc",
        };
    }

    /// <summary>
    /// Builds the full query parameter set for one page of a section's discover
    /// query. The api_key is added separately by the HTTP client, not here, so
    /// this stays free of secrets and safe to log.
    /// </summary>
    /// <param name="section">The section definition.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="contentTypeOverride">
    /// The endpoint this query is for. A mixed section has no single content
    /// type of its own: it issues one query per type, and each needs the
    /// parameter names of its own endpoint.
    /// </param>
    /// <returns>The query parameters, in a stable order.</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> Build(
        SectionDefinition section,
        int page,
        ProviderSectionContentType? contentTypeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(section);

        var contentType = contentTypeOverride ?? section.ContentType;
        var isMovie = contentType == ProviderSectionContentType.Movie;
        var query = new List<KeyValuePair<string, string>>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        // Provider availability. with_watch_providers has no effect without
        // watch_region, so both are emitted together or not at all.
        if (section.TmdbProviderId > 0 && !string.IsNullOrWhiteSpace(section.Region))
        {
            Add("with_watch_providers", section.TmdbProviderId.ToString(CultureInfo.InvariantCulture));
            Add("watch_region", section.Region);
        }

        Add("language", section.MetadataLanguage);
        Add("sort_by", GetSortBy(section.SortBy, contentType));
        Add("page", page.ToString(CultureInfo.InvariantCulture));

        if (section.IncludeGenreIds.Count > 0)
        {
            Add("with_genres", string.Join("|", section.IncludeGenreIds));
        }

        if (section.ExcludeGenreIds.Count > 0)
        {
            Add("without_genres", string.Join("|", section.ExcludeGenreIds));
        }

        Add("with_original_language", section.OriginalLanguage);
        Add("with_origin_country", section.OriginCountry);

        // Date range: movies filter on primary_release_date, TV on first_air_date.
        var dateField = isMovie ? "primary_release_date" : "first_air_date";
        Add($"{dateField}.gte", section.MinDate);
        Add($"{dateField}.lte", section.MaxDate);

        if (section.MinRating.HasValue)
        {
            Add("vote_average.gte", section.MinRating.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (section.MinVoteCount > 0)
        {
            Add("vote_count.gte", section.MinVoteCount.ToString(CultureInfo.InvariantCulture));
        }

        // include_adult is only meaningful on discover/movie; discover/tv ignores it.
        if (isMovie)
        {
            Add("include_adult", section.IncludeAdult ? "true" : "false");
        }

        return query;
    }

    /// <summary>
    /// Renders the query as a URL-ready string, for diagnostics display in the
    /// admin UI ("generated TMDb query"). Contains no secrets by construction.
    /// </summary>
    /// <param name="section">The section definition.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <returns>The endpoint path plus query string.</returns>
    public static string BuildDisplayString(SectionDefinition section, int page)
    {
        ArgumentNullException.ThrowIfNull(section);

        // A mixed section really does issue two requests, so showing one would
        // misrepresent what the admin is about to run.
        if (section.ContentType == ProviderSectionContentType.Mixed)
        {
            return BuildDisplayString(section, page, ProviderSectionContentType.Movie)
                + "\n" + BuildDisplayString(section, page, ProviderSectionContentType.Series);
        }

        return BuildDisplayString(section, page, section.ContentType);
    }

    private static string BuildDisplayString(
        SectionDefinition section,
        int page,
        ProviderSectionContentType contentType)
    {
        var parameters = Build(section, page, contentType);
        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{GetEndpoint(contentType)}?{queryString}";
    }
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyProviderSections.Models;

/// <summary>
/// Subset of TMDb's /configuration response that this plugin uses.
/// </summary>
public class TmdbConfiguration
{
    /// <summary>Gets or sets the image configuration block.</summary>
    [JsonPropertyName("images")]
    public TmdbImageConfiguration? Images { get; set; }
}

/// <summary>
/// Image base URLs and available sizes, from TMDb's /configuration endpoint.
/// </summary>
public class TmdbImageConfiguration
{
    /// <summary>Gets or sets the HTTPS base URL for images.</summary>
    [JsonPropertyName("secure_base_url")]
    public string SecureBaseUrl { get; set; } = "https://image.tmdb.org/t/p/";

    /// <summary>Gets or sets the available logo sizes (e.g. w45, w92, original).</summary>
    [JsonPropertyName("logo_sizes")]
    public List<string> LogoSizes { get; set; } = new();

    /// <summary>Gets or sets the available poster sizes.</summary>
    [JsonPropertyName("poster_sizes")]
    public List<string> PosterSizes { get; set; } = new();

    /// <summary>Gets or sets the available backdrop sizes.</summary>
    [JsonPropertyName("backdrop_sizes")]
    public List<string> BackdropSizes { get; set; } = new();
}

/// <summary>
/// A watch-provider region, from /watch/providers/regions.
/// </summary>
public class TmdbRegion
{
    /// <summary>Gets or sets the ISO 3166-1 country code.</summary>
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    /// <summary>Gets or sets the English country name.</summary>
    [JsonPropertyName("english_name")]
    public string EnglishName { get; set; } = string.Empty;

    /// <summary>Gets or sets the native-language country name.</summary>
    [JsonPropertyName("native_name")]
    public string NativeName { get; set; } = string.Empty;
}

/// <summary>
/// Envelope for list endpoints that wrap their payload in "results".
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class TmdbResultList<T>
{
    /// <summary>Gets or sets the returned items.</summary>
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

/// <summary>
/// A streaming provider, from /watch/providers/movie and /watch/providers/tv.
/// </summary>
public class TmdbWatchProvider
{
    /// <summary>Gets or sets the stable TMDb provider identifier.</summary>
    [JsonPropertyName("provider_id")]
    public int ProviderId { get; set; }

    /// <summary>Gets or sets the provider's display name.</summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider logo path, relative to the image base URL.</summary>
    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; }

    /// <summary>Gets or sets TMDb's own suggested display ordering for this provider.</summary>
    [JsonPropertyName("display_priority")]
    public int DisplayPriority { get; set; }
}

/// <summary>
/// One page of a discover/movie or discover/tv response.
/// </summary>
public class TmdbDiscoverPage
{
    /// <summary>Gets or sets the 1-based page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>Gets or sets the total number of pages available.</summary>
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    /// <summary>Gets or sets the total number of results available.</summary>
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }

    /// <summary>Gets or sets the items on this page.</summary>
    [JsonPropertyName("results")]
    public List<TmdbDiscoverItem> Results { get; set; } = new();
}

/// <summary>
/// A single discover result. Movie and TV responses differ in field names
/// (title/name, release_date/first_air_date), so both are mapped here and
/// normalized through the Title / ReleaseDate helpers.
/// </summary>
public class TmdbDiscoverItem
{
    /// <summary>Gets or sets the TMDb identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the movie title (movies only).</summary>
    [JsonPropertyName("title")]
    public string? MovieTitle { get; set; }

    /// <summary>Gets or sets the series name (TV only).</summary>
    [JsonPropertyName("name")]
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the original movie title (movies only).</summary>
    [JsonPropertyName("original_title")]
    public string? OriginalMovieTitle { get; set; }

    /// <summary>Gets or sets the original series name (TV only).</summary>
    [JsonPropertyName("original_name")]
    public string? OriginalSeriesName { get; set; }

    /// <summary>Gets or sets the overview text.</summary>
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    /// <summary>Gets or sets the poster path, relative to the image base URL.</summary>
    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    /// <summary>Gets or sets the backdrop path, relative to the image base URL.</summary>
    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    /// <summary>Gets or sets the movie release date (movies only), "yyyy-MM-dd".</summary>
    [JsonPropertyName("release_date")]
    public string? MovieReleaseDate { get; set; }

    /// <summary>Gets or sets the first air date (TV only), "yyyy-MM-dd".</summary>
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    /// <summary>Gets or sets the average vote.</summary>
    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    /// <summary>Gets or sets the vote count.</summary>
    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    /// <summary>Gets the display title, whichever of title/name this item carries.</summary>
    [JsonIgnore]
    public string Title => MovieTitle ?? SeriesName ?? string.Empty;

    /// <summary>Gets the original title, whichever of original_title/original_name this item carries.</summary>
    [JsonIgnore]
    public string OriginalTitle => OriginalMovieTitle ?? OriginalSeriesName ?? string.Empty;

    /// <summary>Gets the release date, whichever of release_date/first_air_date this item carries.</summary>
    [JsonIgnore]
    public string? ReleaseDate => MovieReleaseDate ?? FirstAirDate;
}

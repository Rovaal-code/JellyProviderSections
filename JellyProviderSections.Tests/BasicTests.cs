using System.Linq;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Jellyfin.Plugin.JellyProviderSections.Models;
using Jellyfin.Plugin.JellyProviderSections.Services;
using Xunit;

namespace JellyProviderSections.Tests;

public class ConfigurationTests
{
    [Fact]
    public void PluginConfiguration_DefaultsToSchemaVersion1()
    {
        var config = new PluginConfiguration();

        Assert.Equal(1, config.SchemaVersion);
        Assert.Empty(config.Sections);
        Assert.NotNull(config.TmdbSettings);
        Assert.NotNull(config.SeerrSettings);
    }

    [Fact]
    public void SectionDefinition_DefaultsMatchDocumentedPlan()
    {
        var section = new SectionDefinition();

        Assert.Equal(20, section.MaxItems);
        Assert.Equal(ProviderSectionSortBy.Popularity, section.SortBy);
        Assert.Equal(360, section.CacheDurationMinutes);
        Assert.True(section.RequestsEnabled);

        // Non-zero by default: sorting by rating without a vote floor surfaces
        // titles with two perfect votes.
        Assert.Equal(50, section.MinVoteCount);
    }

    [Fact]
    public void PreserveSecrets_KeepsStoredKeysWhenIncomingIsBlank()
    {
        var existing = new PluginConfiguration();
        existing.TmdbSettings.ApiKey = "stored-tmdb";
        existing.SeerrSettings.ApiKey = "stored-seerr";

        var incoming = new PluginConfiguration();

        PluginConfiguration.PreserveSecrets(existing, incoming);

        Assert.Equal("stored-tmdb", incoming.TmdbSettings.ApiKey);
        Assert.Equal("stored-seerr", incoming.SeerrSettings.ApiKey);
    }

    [Fact]
    public void PreserveSecrets_AcceptsAReplacementKey()
    {
        var existing = new PluginConfiguration();
        existing.TmdbSettings.ApiKey = "old";

        var incoming = new PluginConfiguration();
        incoming.TmdbSettings.ApiKey = "new";

        PluginConfiguration.PreserveSecrets(existing, incoming);

        Assert.Equal("new", incoming.TmdbSettings.ApiKey);
    }
}

public class DisplayTextBuilderTests
{
    /// <summary>
    /// The security test that matters most. Home Screen Sections renders
    /// displayText with innerHTML and does not escape it, so an unescaped
    /// section name would be stored XSS against every user of the server.
    /// </summary>
    [Fact]
    public void Build_EscapesHtmlInTheSectionName()
    {
        var section = new SectionDefinition
        {
            DisplayName = "<script>alert('xss')</script>",
            ProviderLogoPath = "/logo.png",
            TmdbProviderId = 283,
        };

        var result = DisplayTextBuilder.Build(section);

        Assert.DoesNotContain("<script>", result, System.StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EscapesHtmlWhenThereIsNoLogo()
    {
        var section = new SectionDefinition
        {
            DisplayName = "<img src=x onerror=alert(1)>",
        };

        var result = DisplayTextBuilder.Build(section);

        Assert.DoesNotContain("<img", result, System.StringComparison.Ordinal);
        Assert.Contains("&lt;img", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EscapesHtmlInTheProviderName()
    {
        var section = new SectionDefinition
        {
            DisplayName = "Popular",
            ProviderDisplayName = "\" onerror=\"alert(1)",
            ProviderLogoPath = "/logo.png",
            TmdbProviderId = 8,
        };

        var result = DisplayTextBuilder.Build(section);

        // The alt attribute must not be breakable out of.
        Assert.DoesNotContain("\" onerror=\"alert(1)\"", result, System.StringComparison.Ordinal);
        Assert.Contains("&quot;", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PutsTheLogoBeforeTheTitle()
    {
        var section = new SectionDefinition
        {
            DisplayName = "Popular en Crunchyroll",
            ProviderLogoPath = "/crunchyroll.png",
            TmdbProviderId = 283,
        };

        var result = DisplayTextBuilder.Build(section);

        var imgIndex = result.IndexOf("<img", System.StringComparison.Ordinal);
        var titleIndex = result.IndexOf("Popular en Crunchyroll", System.StringComparison.Ordinal);

        Assert.True(imgIndex >= 0);
        Assert.True(titleIndex > imgIndex, "The logo must come before the section title.");
    }

    [Fact]
    public void Build_FallsBackToPlainTextWithoutALogo()
    {
        var section = new SectionDefinition { DisplayName = "Sin logo" };

        Assert.Equal("Sin logo", DisplayTextBuilder.Build(section));
    }

    [Fact]
    public void BuildLogoUrl_PointsAtThisPluginsOwnRoute()
    {
        // Never a third-party host: the URL ends up in markup served to every user.
        Assert.Equal("/JellyProviderSections/Logo/283", DisplayTextBuilder.BuildLogoUrl(283));
    }
}

public class DiscoverQueryBuilderTests
{
    [Theory]
    [InlineData(ProviderSectionContentType.Movie, "discover/movie")]
    [InlineData(ProviderSectionContentType.Series, "discover/tv")]
    public void GetEndpoint_MapsContentType(ProviderSectionContentType type, string expected)
        => Assert.Equal(expected, DiscoverQueryBuilder.GetEndpoint(type));

    /// <summary>
    /// Movie and TV do not share parameter names for date and title ordering.
    /// Getting this wrong yields a silently ignored sort, not an error.
    /// </summary>
    [Theory]
    [InlineData(ProviderSectionSortBy.Popularity, ProviderSectionContentType.Movie, "popularity.desc")]
    [InlineData(ProviderSectionSortBy.Popularity, ProviderSectionContentType.Series, "popularity.desc")]
    [InlineData(ProviderSectionSortBy.RatingDesc, ProviderSectionContentType.Movie, "vote_average.desc")]
    [InlineData(ProviderSectionSortBy.ReleaseDateDesc, ProviderSectionContentType.Movie, "primary_release_date.desc")]
    [InlineData(ProviderSectionSortBy.ReleaseDateDesc, ProviderSectionContentType.Series, "first_air_date.desc")]
    [InlineData(ProviderSectionSortBy.TitleAsc, ProviderSectionContentType.Movie, "original_title.asc")]
    [InlineData(ProviderSectionSortBy.TitleAsc, ProviderSectionContentType.Series, "name.asc")]
    public void GetSortBy_TranslatesPerContentType(
        ProviderSectionSortBy sortBy,
        ProviderSectionContentType contentType,
        string expected)
        => Assert.Equal(expected, DiscoverQueryBuilder.GetSortBy(sortBy, contentType));

    [Fact]
    public void Build_EmitsProviderAndRegionTogether()
    {
        var section = new SectionDefinition
        {
            TmdbProviderId = 283,
            Region = "ES",
            ContentType = ProviderSectionContentType.Series,
        };

        var query = DiscoverQueryBuilder.Build(section, 1);

        Assert.Contains(query, p => p.Key == "with_watch_providers" && p.Value == "283");
        Assert.Contains(query, p => p.Key == "watch_region" && p.Value == "ES");
    }

    [Fact]
    public void Build_OmitsProviderWhenRegionIsMissing()
    {
        // with_watch_providers does nothing without watch_region, so sending it
        // alone would quietly return an unfiltered catalogue.
        var section = new SectionDefinition { TmdbProviderId = 283, Region = string.Empty };

        var query = DiscoverQueryBuilder.Build(section, 1);

        Assert.DoesNotContain(query, p => p.Key == "with_watch_providers");
    }

    [Fact]
    public void Build_UsesTheRightDateFieldPerContentType()
    {
        var movie = new SectionDefinition
        {
            ContentType = ProviderSectionContentType.Movie,
            MinDate = "2020-01-01",
        };
        var series = new SectionDefinition
        {
            ContentType = ProviderSectionContentType.Series,
            MinDate = "2020-01-01",
        };

        Assert.Contains(DiscoverQueryBuilder.Build(movie, 1), p => p.Key == "primary_release_date.gte");
        Assert.Contains(DiscoverQueryBuilder.Build(series, 1), p => p.Key == "first_air_date.gte");
    }

    [Fact]
    public void Build_JoinsGenresWithOrSemantics()
    {
        var section = new SectionDefinition();
        section.IncludeGenreIds.AddRange(new[] { 16, 10765 });

        var query = DiscoverQueryBuilder.Build(section, 1);

        Assert.Contains(query, p => p.Key == "with_genres" && p.Value == "16|10765");
    }

    [Fact]
    public void Build_NeverEmitsAnyMonetizationParameter()
    {
        // Hard requirement of the brief: no monetization filtering anywhere.
        var section = new SectionDefinition
        {
            TmdbProviderId = 8,
            Region = "ES",
            MinRating = 7,
            MinVoteCount = 100,
            IncludeAdult = true,
        };
        section.IncludeGenreIds.Add(28);

        var query = DiscoverQueryBuilder.Build(section, 1);

        Assert.DoesNotContain(query, p => p.Key.Contains("monetization", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(query, p => p.Value.Contains("flatrate", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_OmitsIncludeAdultForSeries()
    {
        // discover/tv has no include_adult parameter.
        var section = new SectionDefinition { ContentType = ProviderSectionContentType.Series };

        Assert.DoesNotContain(DiscoverQueryBuilder.Build(section, 1), p => p.Key == "include_adult");
    }

    [Fact]
    public void BuildDisplayString_ContainsNoApiKey()
    {
        // This string is shown in the admin UI and written to diagnostics.
        var section = new SectionDefinition { TmdbProviderId = 8, Region = "ES" };

        var display = DiscoverQueryBuilder.BuildDisplayString(section, 1);

        Assert.DoesNotContain("api_key", display, System.StringComparison.OrdinalIgnoreCase);
    }
}

public class SeerrModelTests
{
    /// <summary>
    /// Verified against seerr-team/seerr server/constants/media.ts. JellyNotify's
    /// own client has the last two swapped; that bug is not inherited here.
    /// </summary>
    [Fact]
    public void SeerrMediaStatus_MatchesTheRealContract()
    {
        Assert.Equal(1, (int)SeerrMediaStatus.Unknown);
        Assert.Equal(2, (int)SeerrMediaStatus.Pending);
        Assert.Equal(3, (int)SeerrMediaStatus.Processing);
        Assert.Equal(4, (int)SeerrMediaStatus.PartiallyAvailable);
        Assert.Equal(5, (int)SeerrMediaStatus.Available);
        Assert.Equal(6, (int)SeerrMediaStatus.Blocklisted);
        Assert.Equal(7, (int)SeerrMediaStatus.Deleted);
    }

    [Fact]
    public void SeerrRequestStatus_IncludesCompleted()
    {
        // Missing in JellyNotify's model; Seerr checks against it when deciding
        // whether a re-request counts as a duplicate.
        Assert.Equal(5, (int)SeerrRequestStatus.Completed);
    }

    [Fact]
    public void SeasonStatus_CarriesBothRegularAnd4k()
    {
        var season = new SeerrSeasonStatus
        {
            SeasonNumber = 2,
            Status = SeerrMediaStatus.Available,
            Status4k = SeerrMediaStatus.Unknown,
        };

        Assert.Equal(SeerrMediaStatus.Available, season.Status);
        Assert.Equal(SeerrMediaStatus.Unknown, season.Status4k);
    }
}

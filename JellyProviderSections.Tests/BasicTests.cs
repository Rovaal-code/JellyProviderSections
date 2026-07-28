using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Xunit;

namespace JellyProviderSections.Tests;

public class BasicTests
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
        Assert.False(section.Enabled);
    }
}

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections;

/// <summary>
/// The main Jellyfin Provider Sections plugin class.
/// Registers the plugin with Jellyfin and serves the configuration web page.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The unique plugin identifier.
    /// </summary>
    public static readonly Guid PluginGuid = Guid.Parse("05cac539-35ae-4f0d-be40-5f0eabd7f43c");

    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{Plugin}"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        Instance = this;
        _logger.LogInformation("Jellyfin Provider Sections plugin v{Version} loaded", Version);
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Jellyfin Provider Sections";

    /// <inheritdoc />
    public override string Description => "Creates dynamic home screen sections based on TMDb streaming providers, with local library resolution and Seerr requests.";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <summary>
    /// Gets the assembly-qualified resource prefix for embedded resources.
    /// </summary>
    private static string ResourcePrefix => "Jellyfin.Plugin.JellyProviderSections";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "jellyprovidersections",
                DisplayName = "Provider Sections",
                EnableInMainMenu = true,
                MenuIcon = "video_library",
                EmbeddedResourcePath = $"{ResourcePrefix}.Configuration.configPage.html",
            },
        };
    }

    /// <summary>
    /// Updates the plugin configuration and saves it.
    /// </summary>
    /// <param name="configuration">The new configuration to apply.</param>
    public void SavePluginConfiguration(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        UpdateConfiguration(configuration);
        _logger.LogInformation("Jellyfin Provider Sections configuration updated");
    }
}

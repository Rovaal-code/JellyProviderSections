using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyProviderSections;

/// <summary>
/// Registers all Jellyfin Provider Sections services into the Jellyfin dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Intentionally empty for the phase-3 skeleton. Real services (TmdbApiClient,
        // SeerrApiClient, HomeSectionsRegistrar, LibraryResolver, ProviderLogoService,
        // SectionCacheService) are added incrementally in later phases, each with its
        // own compile-and-test cycle — see master-implementation-plan.md sections 8-14.
    }
}

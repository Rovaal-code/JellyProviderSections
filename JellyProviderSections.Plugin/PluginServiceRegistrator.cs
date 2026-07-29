using Jellyfin.Plugin.JellyProviderSections.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JellyProviderSections;

/// <summary>
/// Registers this plugin's services into Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Shared cache for TMDb reference data, discover results and Seerr state.
        serviceCollection.AddMemoryCache();

        // Typed HTTP clients, registered under explicit names.
        //
        // The unnamed overload derives the client name from the type name with
        // the namespace stripped, so two plugins that both call an interface
        // ISeerrApiClient collide in the shared factory: whichever registers
        // second throws and Jellyfin disables it as "Malfunctioned". JellyNotify
        // has an interface by that exact name, and this plugin sorts before it,
        // so it was JellyNotify that got knocked out.
        serviceCollection.AddHttpClient<ITmdbApiClient, TmdbApiClient>("JellyProviderSections.Tmdb");
        serviceCollection.AddHttpClient<ISeerrApiClient, SeerrApiClient>("JellyProviderSections.Seerr");

        // All singletons on purpose. Home Screen Sections calls into
        // SectionResultsProvider outside of any HTTP request, so there is no
        // scope to resolve from; a scoped registration would throw there. The
        // Jellyfin services these depend on (ILibraryManager, IDtoService) are
        // themselves singletons, so this is safe.
        serviceCollection.AddSingleton<IHomeSectionsRegistrar, HomeSectionsRegistrar>();
        serviceCollection.AddSingleton<IFileTransformationRegistrar, FileTransformationRegistrar>();
        serviceCollection.AddSingleton<IProviderLogoService, ProviderLogoService>();
        serviceCollection.AddSingleton<IPosterService, PosterService>();
        serviceCollection.AddSingleton<ILibraryResolver, LibraryResolver>();
        serviceCollection.AddSingleton<ISectionContentBuilder, SectionContentBuilder>();

        // Re-registers sections with Home Screen Sections on every server start,
        // which is mandatory: HSS holds third-party registrations in memory only.
        serviceCollection.AddSingleton<IScheduledTask, RegisterSectionsStartupTask>();

        // Publishes the service provider to PluginServiceLocator as soon as the
        // host starts. A plain singleton would not do: nothing would construct
        // it until something asked for it, and the first thing to ask is the
        // very code that needs the locator already populated.
        serviceCollection.AddHostedService<ServiceLocatorInitializer>();
    }
}

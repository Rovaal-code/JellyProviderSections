using System;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Holds a reference to this plugin's service provider.
///
/// Needed because Home Screen Sections instantiates our results provider using
/// ITS OWN dependency injection container, not ours. That container knows about
/// Jellyfin's core services but nothing about this plugin's own ones, so the
/// results provider reaches them through here instead of the constructor.
///
/// Set once during service registration; never mutated afterwards.
/// </summary>
public static class PluginServiceLocator
{
    /// <summary>
    /// Gets or sets the plugin's service provider.
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// Resolves a service, or returns null when the locator is not ready yet
    /// (for instance if something calls in before service registration finished).
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service instance, or null.</returns>
    public static T? Get<T>()
        where T : class
        => ServiceProvider?.GetService<T>();
}

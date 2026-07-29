using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Publishes the plugin's service provider to <see cref="PluginServiceLocator"/>
/// when the host starts.
///
/// Home Screen Sections constructs our results provider through its own DI
/// container, which cannot resolve this plugin's services, so they are reached
/// through the locator. A hosted service is used rather than a plain singleton
/// because the locator has to be populated before anything asks for it, and a
/// singleton is only built on first request.
/// </summary>
public sealed class ServiceLocatorInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceLocatorInitializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceLocatorInitializer"/> class.
    /// </summary>
    /// <param name="serviceProvider">The plugin's service provider.</param>
    /// <param name="logger">Logger.</param>
    public ServiceLocatorInitializer(IServiceProvider serviceProvider, ILogger<ServiceLocatorInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        PluginServiceLocator.ServiceProvider = _serviceProvider;
        _logger.LogInformation("[JellyProvider Sections] Service locator ready");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        PluginServiceLocator.ServiceProvider = null;
        return Task.CompletedTask;
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Re-registers every enabled section with Home Screen Sections at server start.
///
/// This is not an optimisation, it is required: HSS keeps third-party section
/// registrations in memory only, so without this every section would vanish on
/// restart. Same approach jellyfin-plugin-collection-sections uses.
/// </summary>
public sealed class RegisterSectionsStartupTask : IScheduledTask
{
    private readonly IHomeSectionsRegistrar _registrar;
    private readonly IFileTransformationRegistrar _transformationRegistrar;
    private readonly ILogger<RegisterSectionsStartupTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterSectionsStartupTask"/> class.
    /// </summary>
    /// <param name="registrar">The Home Screen Sections registrar.</param>
    /// <param name="transformationRegistrar">The File Transformation registrar.</param>
    /// <param name="logger">Logger.</param>
    public RegisterSectionsStartupTask(
        IHomeSectionsRegistrar registrar,
        IFileTransformationRegistrar transformationRegistrar,
        ILogger<RegisterSectionsStartupTask> logger)
    {
        _registrar = registrar;
        _transformationRegistrar = transformationRegistrar;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Registrar secciones de proveedor";

    /// <inheritdoc />
    public string Key => "JellyProviderSectionsRegister";

    /// <inheritdoc />
    public string Description =>
        "Vuelve a registrar las secciones de proveedor en Home Screen Sections. "
        + "Se ejecuta al arrancar el servidor porque Home Screen Sections no guarda "
        + "en disco las secciones registradas por otros plugins.";

    /// <inheritdoc />
    public string Category => "Provider Sections";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var registered = _registrar.RegisterAll();

        // Registered here too, and not only at install time, because File
        // Transformation keeps its registrations in memory exactly like HSS does.
        _transformationRegistrar.Register();

        _logger.LogInformation(
            "[ProviderSections] Startup registration complete, {Count} section(s) registered",
            registered);

        progress?.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger,
        };
    }
}

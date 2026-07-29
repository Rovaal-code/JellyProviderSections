using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Registers this plugin's home-screen script with the File Transformation plugin.
///
/// Home Screen Sections only draws artwork-carrying "discover" cards for its own
/// three built-in Jellyseerr sections; it picks that renderer by section key, so
/// third-party sections always go through Jellyfin's standard card builder. That
/// builder derives a card's image URL from the item id, and for a title that is
/// not in the library the id is synthetic, so the card renders with a flat
/// placeholder. The only way to put the TMDb poster on those cards is to run
/// some code in the browser, which is what this injects.
///
/// The registration mirrors how HSS itself talks to File Transformation, and for
/// the same reason: no NuGet contract exists, so the whole ecosystem reflects
/// over assemblies already loaded in the process. File Transformation
/// deserializes its callback payload into whatever type the callback declares,
/// which is why <see cref="Model.PatchRequestPayload"/> is our own type and not
/// a shared one.
/// </summary>
public interface IFileTransformationRegistrar
{
    /// <summary>Registers the home-screen script injection.</summary>
    /// <returns><c>true</c> when File Transformation accepted the registration.</returns>
    bool Register();
}

/// <inheritdoc cref="IFileTransformationRegistrar" />
public sealed class FileTransformationRegistrar : IFileTransformationRegistrar
{
    private const string FileTransformationAssemblyMarker = ".FileTransformation";
    private const string PluginInterfaceTypeName = "Jellyfin.Plugin.FileTransformation.PluginInterface";
    private const string RegisterTransformationMethodName = "RegisterTransformation";

    // Stable id: File Transformation keys registrations by it, so re-registering
    // on every start replaces rather than duplicates.
    private const string TransformationId = "9b1f6e2c-0a4d-4a3e-9f2b-1d7c5e8a4306";

    private readonly ILogger<FileTransformationRegistrar> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTransformationRegistrar"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool Register()
    {
        var assembly = FindAssembly(FileTransformationAssemblyMarker);
        if (assembly is null)
        {
            _logger.LogWarning(
                "[JellyProvider Sections] File Transformation is not installed, so external cards will render without "
                + "their TMDb artwork. Home Screen Sections needs it too: install it from "
                + "https://github.com/IAmParadox27/jellyfin-plugin-file-transformation");
            return false;
        }

        var pluginInterface = assembly.GetType(PluginInterfaceTypeName);
        var registerMethod = pluginInterface?.GetMethod(
            RegisterTransformationMethodName,
            BindingFlags.Public | BindingFlags.Static);

        if (registerMethod is null)
        {
            _logger.LogError(
                "[JellyProvider Sections] Found File Transformation but its {Type}.{Method} entry point is missing. "
                + "This build is not compatible.",
                PluginInterfaceTypeName,
                RegisterTransformationMethodName);
            return false;
        }

        try
        {
            registerMethod.Invoke(null, new object?[] { BuildPayload() });
            _logger.LogInformation("[JellyProvider Sections] Registered the home screen script with File Transformation");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyProvider Sections] File Transformation rejected the script registration");
            return false;
        }
    }

    private static object BuildPayload()
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = TransformationId,
            // The literal path, not a regex. File Transformation looks for an
            // exact key first and only falls back to treating keys as regular
            // expressions when that misses, so a registration under "index\\.html"
            // is never consulted: the exact "index.html" key another plugin
            // already registered wins and short-circuits the lookup.
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = typeof(FileTransformationRegistrar).Assembly.FullName,
            ["callbackClass"] = typeof(Helpers.TransformationPatches).FullName,
            ["callbackMethod"] = nameof(Helpers.TransformationPatches.IndexHtml),
        };

        var json = JsonSerializer.Serialize(payload);

        var jObjectType = FindAssembly("Newtonsoft.Json")?.GetType("Newtonsoft.Json.Linq.JObject")
            ?? throw new InvalidOperationException(
                "Newtonsoft.Json is not loaded in this process, so the File Transformation payload cannot be built.");

        var parse = jObjectType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) })
            ?? throw new InvalidOperationException("Newtonsoft.Json.Linq.JObject.Parse(string) was not found.");

        return parse.Invoke(null, new object?[] { json })
            ?? throw new InvalidOperationException("Failed to build the File Transformation payload.");
    }

    private static Assembly? FindAssembly(string marker) =>
        AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly => assembly.FullName?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true);
}

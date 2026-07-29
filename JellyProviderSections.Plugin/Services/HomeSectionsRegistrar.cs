using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Services;

/// <summary>
/// Registers this plugin's sections with the Home Screen Sections plugin.
///
/// HSS exposes no NuGet contract; the whole ecosystem (collection-sections,
/// file-transformation, pages) talks to it by reflecting over assemblies
/// already loaded in the same process. This deliberately mirrors that pattern:
/// zero PackageReference, zero compile-time coupling, and a clean degradation
/// when HSS is absent.
///
/// HSS keeps third-party registrations in memory only, so everything must be
/// re-registered on every server start and whenever the admin saves config.
/// The section Id is what survives: HSS persists per-user position and enabled
/// state keyed by that exact string, which is why it must never change.
///
/// The HTTP route POST /HomeScreen/RegisterSection also exists but has no
/// [Authorize] attribute, so it is deliberately not used.
/// </summary>
public interface IHomeSectionsRegistrar
{
    /// <summary>Gets a value indicating whether HSS was found in this process.</summary>
    bool IsHomeScreenSectionsAvailable { get; }

    /// <summary>Gets a value indicating whether File Transformation was found.</summary>
    bool IsFileTransformationAvailable { get; }

    /// <summary>Gets the detected Home Screen Sections version, when available.</summary>
    string? DetectedHomeScreenSectionsVersion { get; }

    /// <summary>Registers every enabled section, replacing any previous registration.</summary>
    /// <returns>The number of sections successfully registered.</returns>
    int RegisterAll();
}

/// <inheritdoc cref="IHomeSectionsRegistrar" />
public sealed class HomeSectionsRegistrar : IHomeSectionsRegistrar
{
    private const string HomeScreenSectionsAssemblyMarker = ".HomeScreenSections";
    private const string FileTransformationAssemblyMarker = ".FileTransformation";
    private const string PluginInterfaceTypeName = "Jellyfin.Plugin.HomeScreenSections.PluginInterface";
    private const string RegisterSectionMethodName = "RegisterSection";

    private readonly ILogger<HomeSectionsRegistrar> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSectionsRegistrar"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public HomeSectionsRegistrar(ILogger<HomeSectionsRegistrar> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsHomeScreenSectionsAvailable => FindAssembly(HomeScreenSectionsAssemblyMarker) is not null;

    /// <inheritdoc />
    public bool IsFileTransformationAvailable => FindAssembly(FileTransformationAssemblyMarker) is not null;

    /// <inheritdoc />
    public string? DetectedHomeScreenSectionsVersion =>
        FindAssembly(HomeScreenSectionsAssemblyMarker)?.GetName().Version?.ToString();

    /// <inheritdoc />
    public int RegisterAll()
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null)
        {
            return 0;
        }

        MigrateUnsafeIds(configuration);

        var assembly = FindAssembly(HomeScreenSectionsAssemblyMarker);
        if (assembly is null)
        {
            _logger.LogWarning(
                "[ProviderSections] Home Screen Sections is not installed, sections cannot be registered. "
                + "Install it from https://github.com/IAmParadox27/jellyfin-plugin-home-sections");
            return 0;
        }

        var pluginInterface = assembly.GetType(PluginInterfaceTypeName);
        var registerMethod = pluginInterface?.GetMethod(
            RegisterSectionMethodName,
            BindingFlags.Public | BindingFlags.Static);

        if (registerMethod is null)
        {
            _logger.LogError(
                "[ProviderSections] Found Home Screen Sections {Version} but its {Type}.{Method} entry point is "
                + "missing. This build of Home Screen Sections is not compatible.",
                DetectedHomeScreenSectionsVersion,
                PluginInterfaceTypeName,
                RegisterSectionMethodName);
            return 0;
        }

        if (!IsFileTransformationAvailable)
        {
            // HSS still loads and its API answers, but without File Transformation
            // its frontend is never injected, so nothing renders. Registration
            // "succeeding" here would otherwise look like everything is fine.
            _logger.LogWarning(
                "[ProviderSections] File Transformation is not installed. Sections will register but will not be "
                + "visible in Jellyfin Web, because Home Screen Sections cannot inject its frontend without it.");
        }

        var registered = 0;

        foreach (var section in configuration.Sections.Where(s => s.Enabled))
        {
            try
            {
                var payload = BuildPayload(section);
                registerMethod.Invoke(null, new object?[] { payload });
                registered++;

                _logger.LogInformation(
                    "[ProviderSections] Registered section {Name} ({Id})",
                    section.DisplayName,
                    section.Id);
            }
            catch (TargetInvocationException ex)
            {
                _logger.LogError(
                    ex.InnerException ?? ex,
                    "[ProviderSections] Home Screen Sections rejected section {Id}",
                    section.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProviderSections] Failed to register section {Id}", section.Id);
            }
        }

        return registered;
    }

    /// <summary>
    /// Rewrites section ids that cannot be used as a CSS class selector.
    ///
    /// Ids are meant to be immutable, and this is the one exception: a section
    /// whose id starts with a digit makes Jellyfin Web throw a SyntaxError out of
    /// <c>querySelector('.' + id)</c>, which aborts the entire home render, so
    /// every row disappears, not only ours. Such a section could never have worked,
    /// so there is no user layout to preserve. Runs once; afterwards every id
    /// already carries the prefix and the loop is a no-op.
    /// </summary>
    /// <param name="configuration">The configuration to migrate in place.</param>
    private void MigrateUnsafeIds(PluginConfiguration configuration)
    {
        var migrated = 0;

        foreach (var section in configuration.Sections)
        {
            if (SectionDefinition.IsCssSafeId(section.Id))
            {
                continue;
            }

            var oldId = section.Id;
            section.Id = string.IsNullOrEmpty(oldId)
                ? SectionDefinition.NewId()
                : SectionDefinition.IdPrefix + oldId;

            migrated++;

            _logger.LogInformation(
                "[ProviderSections] Migrated section id {OldId} to {NewId}: the old value was not a valid CSS "
                + "identifier and prevented Jellyfin Web from rendering the home screen",
                oldId,
                section.Id);
        }

        if (migrated > 0)
        {
            Plugin.Instance?.SavePluginConfiguration(configuration);
        }
    }

    /// <summary>
    /// Builds the registration payload as the JObject type HSS expects. Newtonsoft
    /// is not referenced by this plugin, so the object is created reflectively
    /// from the Newtonsoft assembly HSS itself already loaded.
    /// </summary>
    private static object BuildPayload(SectionDefinition section)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = section.Id,
            ["displayText"] = DisplayTextBuilder.Build(section),
            ["limit"] = 1,
            ["additionalData"] = section.Id,
            // Must be the FULL assembly name including version and culture: HSS
            // matches it with `Assembly.FullName == payload.ResultsAssembly`, so
            // the short name silently finds nothing and the row renders empty.
            ["resultsAssembly"] = typeof(HomeSectionsRegistrar).Assembly.FullName,
            ["resultsClass"] = typeof(SectionResultsProvider).FullName,
            ["resultsMethod"] = nameof(SectionResultsProvider.GetResults),
        };

        var json = JsonSerializer.Serialize(payload);

        var jObjectType = FindAssembly("Newtonsoft.Json")?.GetType("Newtonsoft.Json.Linq.JObject")
            ?? throw new InvalidOperationException(
                "Newtonsoft.Json is not loaded in this process, so the Home Screen Sections payload cannot be built.");

        var parse = jObjectType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) })
            ?? throw new InvalidOperationException("Newtonsoft.Json.Linq.JObject.Parse(string) was not found.");

        return parse.Invoke(null, new object?[] { json })
            ?? throw new InvalidOperationException("Failed to build the Home Screen Sections payload.");
    }

    private static Assembly? FindAssembly(string marker) =>
        AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly => assembly.FullName?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true);
}

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
    private const string HomeScreenSectionsPluginTypeName =
        "Jellyfin.Plugin.HomeScreenSections.HomeScreenSectionsPlugin";
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
                EnsureSectionSettings(assembly, section);
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
    /// Creates the Home Screen Sections settings row for a section when it has
    /// none, defaulting it to Portrait.
    ///
    /// Two problems this solves, both of which come from the same place. HSS
    /// serves only sections that already have a settings row, and it creates
    /// those rows in its own admin page rather than on registration, so a freshly
    /// created section is invisible on the home screen until the administrator
    /// visits Home Screen Sections' settings and presses save. And when that row
    /// is finally created, its view mode comes from the section's own
    /// DefaultViewMode, which for a plugin-registered section is Landscape; the
    /// viewMode in the registration payload is not read for this. These are
    /// catalogue posters, so Portrait is the right default.
    ///
    /// Only ever adds a missing row, never touches an existing one: once the
    /// administrator has an opinion about a section, it is theirs. Any mismatch
    /// in HSS's shape is swallowed, since failing here must not stop the section
    /// from registering.
    /// </summary>
    private void EnsureSectionSettings(Assembly homeScreenSections, SectionDefinition section)
    {
        try
        {
            var pluginType = homeScreenSections.GetType(HomeScreenSectionsPluginTypeName);
            var instance = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (instance is null)
            {
                _logger.LogWarning(
                    "[ProviderSections] {Type}.Instance was not reachable, so the Home Screen Sections settings "
                    + "row could not be pre-created",
                    HomeScreenSectionsPluginTypeName);
                return;
            }

            var configuration = instance.GetType()
                .GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(instance);

            if (configuration is null)
            {
                _logger.LogWarning("[ProviderSections] Home Screen Sections exposed no Configuration property");
                return;
            }

            if (configuration.GetType().GetProperty("SectionSettings")?.GetValue(configuration)
                is not System.Collections.IList settings)
            {
                _logger.LogWarning(
                    "[ProviderSections] Home Screen Sections' SectionSettings is not a list this build understands");
                return;
            }

            foreach (var existing in settings)
            {
                var id = existing?.GetType().GetProperty("SectionId")?.GetValue(existing) as string;
                if (string.Equals(id, section.Id, StringComparison.Ordinal))
                {
                    return;
                }
            }

            // Taken from the declared property type rather than the instance:
            // the backing field may well be a plain array or a custom collection,
            // neither of which carries a generic argument to read.
            var settingsType = configuration.GetType().GetProperty("SectionSettings")?.PropertyType
                is { IsGenericType: true } declared
                ? declared.GetGenericArguments()[0]
                : settings.GetType().GetElementType();

            if (settingsType is null || Activator.CreateInstance(settingsType) is not { } entry)
            {
                _logger.LogWarning(
                    "[ProviderSections] Could not work out the type of Home Screen Sections' section settings "
                    + "entries, so the row for {Id} was not pre-created",
                    section.Id);
                return;
            }

            void Set(string name, object? value)
                => settingsType.GetProperty(name)?.SetValue(entry, value);

            Set("SectionId", section.Id);
            Set("Enabled", true);
            Set("AllowUserOverride", true);
            Set("LowerLimit", 1);
            Set("UpperLimit", 1);
            Set("OrderIndex", 999);
            Set("HideWatchedItems", false);

            var viewModeProperty = settingsType.GetProperty("ViewMode");
            if (viewModeProperty is not null
                && Enum.TryParse(viewModeProperty.PropertyType, "Portrait", out var portrait))
            {
                viewModeProperty.SetValue(entry, portrait);
            }

            // SectionSettings is an array in this build, so it cannot simply be
            // appended to: grow a copy and assign it back. Handles a list too, in
            // case that ever changes.
            var settingsProperty = configuration.GetType().GetProperty("SectionSettings");

            if (settings.IsFixedSize)
            {
                var grown = Array.CreateInstance(settingsType, settings.Count + 1);
                settings.CopyTo(grown, 0);
                grown.SetValue(entry, settings.Count);

                if (settingsProperty?.CanWrite != true)
                {
                    _logger.LogWarning(
                        "[ProviderSections] Home Screen Sections' SectionSettings is a fixed-size collection that "
                        + "cannot be replaced, so the row for {Id} was not pre-created",
                        section.Id);
                    return;
                }

                settingsProperty.SetValue(configuration, grown);
            }
            else
            {
                settings.Add(entry);
            }

            instance.GetType()
                .GetMethod("SaveConfiguration", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)
                ?.Invoke(instance, null);

            _logger.LogInformation(
                "[ProviderSections] Created the Home Screen Sections settings row for {Name}, defaulting to Portrait",
                section.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ProviderSections] Could not pre-create the Home Screen Sections settings row for {Id}. "
                + "The section still registers, but it will not appear until Home Screen Sections' own "
                + "settings page is saved once.",
                section.Id);
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
            // Portrait: these rows are catalogue posters, not episode stills, and
            // it is the shape the poster artwork the home script applies is cut
            // for. HSS reads this as the section's default; an admin can still
            // override it per section from its own settings page.
            ["viewMode"] = "Portrait",
            // No details menu: it acts on an item the server does not have for
            // every external card. The home script hides the rest of Jellyfin's
            // hover overlay; this covers the window before it has run.
            ["showDetailsMenu"] = false,
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

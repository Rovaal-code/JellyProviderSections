using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyProviderSections.Configuration;
using Jellyfin.Plugin.JellyProviderSections.Models;
using Jellyfin.Plugin.JellyProviderSections.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyProviderSections.Api;

/// <summary>
/// Administrative API: section CRUD, connection tests, diagnostics.
/// Every route here requires an elevated (administrator) Jellyfin session.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyProviderSections/Admin")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly ITmdbApiClient _tmdbClient;
    private readonly ISeerrApiClient _seerrClient;
    private readonly IHomeSectionsRegistrar _registrar;
    private readonly ISectionContentBuilder _contentBuilder;
    private readonly IProviderLogoService _logoService;
    private readonly IPosterService _posterService;
    private readonly ILogger<AdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    /// <param name="tmdbClient">TMDb client.</param>
    /// <param name="seerrClient">Seerr client.</param>
    /// <param name="registrar">Home Screen Sections registrar.</param>
    /// <param name="contentBuilder">Section content builder.</param>
    /// <param name="logoService">Provider logo cache.</param>
    /// <param name="posterService">External title poster cache.</param>
    /// <param name="logger">Logger.</param>
    public AdminController(
        ITmdbApiClient tmdbClient,
        ISeerrApiClient seerrClient,
        IHomeSectionsRegistrar registrar,
        ISectionContentBuilder contentBuilder,
        IProviderLogoService logoService,
        IPosterService posterService,
        ILogger<AdminController> logger)
    {
        _tmdbClient = tmdbClient;
        _seerrClient = seerrClient;
        _registrar = registrar;
        _contentBuilder = contentBuilder;
        _logoService = logoService;
        _posterService = posterService;
        _logger = logger;
    }

    private static PluginConfiguration Config =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>Gets the plugin configuration with secrets redacted.</summary>
    /// <returns>The sanitized configuration.</returns>
    [HttpGet("config")]
    public ActionResult<object> GetConfig()
    {
        var config = Config;

        // Secrets are never echoed back. The booleans let the UI say "saved"
        // versus "not configured" without ever holding the value.
        return Ok(new
        {
            schemaVersion = config.SchemaVersion,
            tmdb = new
            {
                enabled = config.TmdbSettings.Enabled,
                hasApiKey = !string.IsNullOrWhiteSpace(config.TmdbSettings.ApiKey),
            },
            seerr = new
            {
                enabled = config.SeerrSettings.Enabled,
                serverUrl = config.SeerrSettings.ServerUrl,
                ignoreSslErrors = config.SeerrSettings.IgnoreSslErrors,
                allowIgnoreQuota = config.SeerrSettings.AllowIgnoreQuota,
                hasApiKey = !string.IsNullOrWhiteSpace(config.SeerrSettings.ApiKey),
            },
        });
    }

    /// <summary>Saves connection settings, preserving unsent secrets.</summary>
    /// <param name="request">The submitted settings.</param>
    /// <returns>No content on success.</returns>
    [HttpPut("config")]
    public ActionResult SaveConfig([FromBody] SaveConfigRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var config = plugin.Configuration;

        config.TmdbSettings.Enabled = request.TmdbEnabled;
        if (!string.IsNullOrWhiteSpace(request.TmdbApiKey))
        {
            config.TmdbSettings.ApiKey = request.TmdbApiKey.Trim();
        }

        config.SeerrSettings.Enabled = request.SeerrEnabled;
        config.SeerrSettings.IgnoreSslErrors = request.SeerrIgnoreSslErrors;
        config.SeerrSettings.AllowIgnoreQuota = request.SeerrAllowIgnoreQuota;

        if (!string.IsNullOrWhiteSpace(request.SeerrServerUrl))
        {
            var url = request.SeerrServerUrl.Trim();

            // Reject anything that is not plain HTTP(S): a file:// or gopher://
            // URL here would turn an admin typo into an SSRF primitive.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest(new { message = "La URL de Seerr debe empezar por http:// o https://" });
            }

            config.SeerrSettings.ServerUrl = url;
        }

        if (!string.IsNullOrWhiteSpace(request.SeerrApiKey))
        {
            config.SeerrSettings.ApiKey = request.SeerrApiKey.Trim();
        }

        plugin.SavePluginConfiguration(config);
        return NoContent();
    }

    /// <summary>Tests the TMDb connection.</summary>
    /// <param name="request">Credentials to test, or null to use what is stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The test result.</returns>
    [HttpPost("test/tmdb")]
    public async Task<ActionResult<TmdbConnectionResult>> TestTmdb(
        [FromBody] TestConnectionRequest? request,
        CancellationToken cancellationToken)
        => Ok(await _tmdbClient
            .TestConnectionAsync(request?.TmdbApiKey, cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Tests the Seerr connection.</summary>
    /// <param name="request">Credentials to test, or null to use what is stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The test result.</returns>
    [HttpPost("test/seerr")]
    public async Task<ActionResult<SeerrConnectionResult>> TestSeerr(
        [FromBody] TestConnectionRequest? request,
        CancellationToken cancellationToken)
        => Ok(await _seerrClient
            .TestConnectionAsync(request?.SeerrServerUrl, request?.SeerrApiKey, cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Lists the TMDb watch-provider regions.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The available regions.</returns>
    [HttpGet("tmdb/regions")]
    public async Task<ActionResult> GetRegions(CancellationToken cancellationToken)
    {
        var regions = await _tmdbClient.GetWatchProviderRegionsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(regions.Select(r => new { code = r.Iso31661, name = r.NativeName, englishName = r.EnglishName }));
    }

    /// <summary>Lists the providers available for a region and content type.</summary>
    /// <param name="region">ISO 3166-1 region code.</param>
    /// <param name="contentType">Movie or Series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The available providers.</returns>
    [HttpGet("tmdb/providers")]
    public async Task<ActionResult> GetProviders(
        [FromQuery] string region,
        [FromQuery] ProviderSectionContentType contentType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return BadRequest(new { message = "Falta la región." });
        }

        var providers = await _tmdbClient
            .GetWatchProvidersAsync(contentType, region, cancellationToken)
            .ConfigureAwait(false);

        // The selector shows every provider in the region, and each row asks this
        // plugin for a logo. Without this only the providers a section already
        // uses had a resolvable path, so every other one came back 404 and the
        // list rendered with the names but no icons.
        foreach (var provider in providers)
        {
            if (!string.IsNullOrWhiteSpace(provider.LogoPath))
            {
                _logoService.Remember(provider.ProviderId, provider.LogoPath);
            }
        }

        return Ok(providers
            .OrderBy(p => p.DisplayPriority)
            .ThenBy(p => p.ProviderName, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new
            {
                id = p.ProviderId,
                name = p.ProviderName,
                logoPath = p.LogoPath,
            }));
    }

    /// <summary>Lists every configured section.</summary>
    /// <returns>The sections plus their runtime integration state.</returns>
    [HttpGet("sections")]
    public ActionResult GetSections()
    {
        var hssAvailable = _registrar.IsHomeScreenSectionsAvailable;
        var seerrConfigured = Config.SeerrSettings.Enabled
            && !string.IsNullOrWhiteSpace(Config.SeerrSettings.ServerUrl);

        return Ok(Config.Sections
            .OrderBy(s => s.OrderHint)
            .Select(s => ToDto(s, hssAvailable, seerrConfigured)));
    }

    /// <summary>Gets one section.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>The section, or 404.</returns>
    [HttpGet("sections/{id}")]
    public ActionResult GetSection(string id)
    {
        var section = FindSection(id);
        if (section is null)
        {
            return NotFound();
        }

        return Ok(ToDto(
            section,
            _registrar.IsHomeScreenSectionsAvailable,
            Config.SeerrSettings.Enabled));
    }

    /// <summary>Creates a section.</summary>
    /// <param name="request">The section fields.</param>
    /// <returns>The created section.</returns>
    [HttpPost("sections")]
    public ActionResult CreateSection([FromBody] SectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var section = new SectionDefinition
        {
            // Generated here, never accepted from the client: this id is what
            // Home Screen Sections uses to remember each user's layout.
            Id = SectionDefinition.NewId(),
            CreatedUtc = DateTime.UtcNow,
            OrderHint = plugin.Configuration.Sections.Count,
        };

        Apply(request, section);

        plugin.Configuration.Sections.Add(section);
        plugin.SavePluginConfiguration(plugin.Configuration);
        _registrar.RegisterAll();

        return Ok(ToDto(section, _registrar.IsHomeScreenSectionsAvailable, Config.SeerrSettings.Enabled));
    }

    /// <summary>Updates a section. The id is immutable.</summary>
    /// <param name="id">The section id.</param>
    /// <param name="request">The new field values.</param>
    /// <returns>The updated section.</returns>
    [HttpPut("sections/{id}")]
    public ActionResult UpdateSection(string id, [FromBody] SectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var section = FindSection(id);
        if (section is null)
        {
            return NotFound();
        }

        // Changing the id would silently reset every user's position for this
        // row in Modular Home, so an attempt to do so is refused outright.
        if (!string.IsNullOrWhiteSpace(request.Id)
            && !string.Equals(request.Id, section.Id, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "El identificador de una sección no se puede cambiar." });
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        Apply(request, section);
        section.ModifiedUtc = DateTime.UtcNow;

        SaveAndReregister(section.Id);

        return Ok(ToDto(section, _registrar.IsHomeScreenSectionsAvailable, Config.SeerrSettings.Enabled));
    }

    /// <summary>Duplicates a section under a new id.</summary>
    /// <param name="id">The section to copy.</param>
    /// <returns>The new section.</returns>
    [HttpPost("sections/{id}/duplicate")]
    public ActionResult DuplicateSection(string id)
    {
        var source = FindSection(id);
        if (source is null)
        {
            return NotFound();
        }

        var plugin = Plugin.Instance!;

        var copy = new SectionDefinition
        {
            Id = SectionDefinition.NewId(),
            CreatedUtc = DateTime.UtcNow,
            OrderHint = plugin.Configuration.Sections.Count,
            DisplayName = $"{source.DisplayName} (copia)",
            Enabled = false,
            TmdbProviderId = source.TmdbProviderId,
            ProviderDisplayName = source.ProviderDisplayName,
            ProviderLogoPath = source.ProviderLogoPath,
            ContentType = source.ContentType,
            Region = source.Region,
            MetadataLanguage = source.MetadataLanguage,
            SortBy = source.SortBy,
            MaxItems = source.MaxItems,
            IncludeGenreIds = new List<int>(source.IncludeGenreIds),
            ExcludeGenreIds = new List<int>(source.ExcludeGenreIds),
            OriginalLanguage = source.OriginalLanguage,
            OriginCountry = source.OriginCountry,
            MinDate = source.MinDate,
            MaxDate = source.MaxDate,
            MinRating = source.MinRating,
            MinVoteCount = source.MinVoteCount,
            IncludeAdult = source.IncludeAdult,
            RequestsEnabled = source.RequestsEnabled,
            CacheDurationMinutes = source.CacheDurationMinutes,
        };

        plugin.Configuration.Sections.Add(copy);
        plugin.SavePluginConfiguration(plugin.Configuration);

        return Ok(ToDto(copy, _registrar.IsHomeScreenSectionsAvailable, Config.SeerrSettings.Enabled));
    }

    /// <summary>Enables a section.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>No content.</returns>
    [HttpPost("sections/{id}/enable")]
    public ActionResult EnableSection(string id) => SetEnabled(id, true);

    /// <summary>Disables a section.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>No content.</returns>
    [HttpPost("sections/{id}/disable")]
    public ActionResult DisableSection(string id) => SetEnabled(id, false);

    /// <summary>Deletes a section.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>No content.</returns>
    [HttpDelete("sections/{id}")]
    public ActionResult DeleteSection(string id)
    {
        var section = FindSection(id);
        if (section is null)
        {
            return NotFound();
        }

        var plugin = Plugin.Instance!;
        plugin.Configuration.Sections.Remove(section);
        plugin.SavePluginConfiguration(plugin.Configuration);
        _contentBuilder.InvalidateSection(id);

        // Home Screen Sections has no unregister API, so a deleted section stops
        // existing only after the next restart re-registers what remains. Until
        // then it simply returns no items.
        _logger.LogInformation(
            "[JellyProvider Sections] Section {Id} deleted. It stops being registered on the next restart.",
            id);

        return NoContent();
    }

    /// <summary>Runs a section's query without saving it, for previewing filters.</summary>
    /// <param name="request">The section fields to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated query and a sample of results.</returns>
    [HttpPost("test-query")]
    public async Task<ActionResult> TestQuery([FromBody] SectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var section = new SectionDefinition { Id = "preview" };
        Apply(request, section);

        var results = await _tmdbClient.DiscoverAllAsync(section, cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            query = DiscoverQueryBuilder.BuildDisplayString(section, 1),
            count = results.Count,
            items = results.Take(12).Select(r => new
            {
                id = r.Id,
                title = r.Title,
                year = r.ReleaseDate?.Length >= 4 ? r.ReleaseDate[..4] : null,
                voteAverage = r.VoteAverage,
                posterPath = r.PosterPath,
            }),
        });
    }

    /// <summary>
    /// Runs a saved section's Discover query, without touching its cache.
    /// The sibling <c>test-query</c> route does the same for a section the admin
    /// is still filling in and has not saved yet.
    /// </summary>
    /// <param name="id">The section id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated query and what it returned.</returns>
    [HttpPost("sections/{id}/test-query")]
    public async Task<ActionResult> TestSectionQuery(string id, CancellationToken cancellationToken)
    {
        var section = FindSection(id);
        if (section is null)
        {
            return NotFound();
        }

        var results = await _tmdbClient.DiscoverAllAsync(section, cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            query = DiscoverQueryBuilder.BuildDisplayString(section, 1),
            count = results.Count,
            items = results.Take(12).Select(r => new
            {
                id = r.Id,
                title = r.Title,
                year = r.ReleaseDate?.Length >= 4 ? r.ReleaseDate[..4] : null,
                voteAverage = r.VoteAverage,
                posterPath = r.PosterPath,
            }),
        });
    }

    /// <summary>
    /// Previews what a section would put on the home screen, marking which
    /// titles resolve against the local library.
    /// </summary>
    /// <param name="id">The section id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved items.</returns>
    [HttpGet("sections/{id}/preview")]
    public async Task<ActionResult> PreviewSection(string id, CancellationToken cancellationToken)
    {
        var section = FindSection(id);
        if (section is null)
        {
            return NotFound();
        }

        // No user: this is an administrator preview of the section itself, not of
        // what any particular account would be allowed to see.
        var items = await _contentBuilder.BuildAsync(section, null, cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            items = items.Take(24).Select(item =>
            {
                var providerIds = item.ProviderIds ?? new Dictionary<string, string>();
                var isLocal = !providerIds.ContainsKey(LibraryResolver.ExternalMarkerKey);

                return new
                {
                    name = item.Name,
                    isLocal,
                    posterUrl = isLocal
                        ? $"/Items/{item.Id:N}/Images/Primary?maxHeight=300"
                        // Fully qualified: ControllerBase already has a
                        // MetadataProvider member and it shadows the enum here.
                        : providerIds.TryGetValue(
                            MediaBrowser.Model.Entities.MetadataProvider.Tmdb.ToString(),
                            out var tmdbId)
                            ? $"/JellyProviderSections/Poster/{tmdbId}"
                            : null,
                };
            }),
        });
    }

    /// <summary>
    /// Drops every section's cached results and rebuilds them, so the next home
    /// screen load is served from fresh TMDb data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many sections were refreshed.</returns>
    [HttpPost("sync-now")]
    public async Task<ActionResult> SyncNow(CancellationToken cancellationToken)
    {
        var sections = Config.Sections.Where(s => s.Enabled).ToList();
        var refreshed = 0;

        foreach (var section in sections)
        {
            _contentBuilder.InvalidateSection(section.Id);

            try
            {
                await _contentBuilder.BuildAsync(section, null, cancellationToken).ConfigureAwait(false);
                refreshed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One failing section must not stop the others; the per-section
                // outcome is already recorded on the section itself.
                _logger.LogError(ex, "[JellyProvider Sections] Sync failed for section {Id}", section.Id);
            }
        }

        Plugin.Instance?.SavePluginConfiguration(Config);

        return Ok(new { refreshed, total = sections.Count });
    }

    /// <summary>Clears a section's cached results.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>No content.</returns>
    [HttpPost("sections/{id}/clear-cache")]
    public ActionResult ClearSectionCache(string id)
    {
        if (FindSection(id) is null)
        {
            return NotFound();
        }

        _contentBuilder.InvalidateSection(id);
        return NoContent();
    }

    /// <summary>Clears the cached provider logos.</summary>
    /// <returns>No content.</returns>
    [HttpPost("clear-logo-cache")]
    public ActionResult ClearLogoCache()
    {
        _logoService.ClearCache();
        return NoContent();
    }

    /// <summary>Forces an immediate re-registration with Home Screen Sections.</summary>
    /// <returns>How many sections were registered.</returns>
    [HttpPost("register-sections-now")]
    public ActionResult RegisterNow() => Ok(new { registered = _registrar.RegisterAll() });

    /// <summary>Reports integration health.</summary>
    /// <returns>Diagnostic state, free of secrets.</returns>
    [HttpGet("diagnostics")]
    public ActionResult GetDiagnostics()
    {
        var config = Config;

        return Ok(new
        {
            homeScreenSections = new
            {
                available = _registrar.IsHomeScreenSectionsAvailable,
                version = _registrar.DetectedHomeScreenSectionsVersion,
            },
            fileTransformation = new
            {
                available = _registrar.IsFileTransformationAvailable,
            },
            tmdb = new
            {
                configured = !string.IsNullOrWhiteSpace(config.TmdbSettings.ApiKey),
                enabled = config.TmdbSettings.Enabled,
            },
            seerr = new
            {
                configured = !string.IsNullOrWhiteSpace(config.SeerrSettings.ApiKey)
                    && !string.IsNullOrWhiteSpace(config.SeerrSettings.ServerUrl),
                enabled = config.SeerrSettings.Enabled,
            },
            sections = new
            {
                total = config.Sections.Count,
                enabled = config.Sections.Count(s => s.Enabled),
            },
            pluginVersion = Plugin.Instance?.Version?.ToString(),
        });
    }

    private ActionResult SetEnabled(string id, bool enabled)
    {
        var section = FindSection(id);
        if (section is null)
        {
            return NotFound();
        }

        section.Enabled = enabled;
        section.ModifiedUtc = DateTime.UtcNow;
        SaveAndReregister(id);

        return NoContent();
    }

    private void SaveAndReregister(string sectionId)
    {
        var plugin = Plugin.Instance!;
        plugin.SavePluginConfiguration(plugin.Configuration);
        _contentBuilder.InvalidateSection(sectionId);
        _registrar.RegisterAll();
    }

    private static SectionDefinition? FindSection(string id)
        => Plugin.Instance?.Configuration.Sections
            .FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string? Validate(SectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return "El nombre de la sección es obligatorio.";
        }

        if (request.DisplayName.Length > 80)
        {
            return "El nombre no puede superar los 80 caracteres.";
        }

        if (request.TmdbProviderId <= 0)
        {
            return "Hay que elegir un proveedor.";
        }

        if (string.IsNullOrWhiteSpace(request.Region))
        {
            return "Hay que elegir una región.";
        }

        if (request.MaxItems is < 1 or > 200)
        {
            return "El número de elementos debe estar entre 1 y 200.";
        }

        return null;
    }

    private static void Apply(SectionRequest request, SectionDefinition section)
    {
        section.DisplayName = request.DisplayName.Trim();
        section.Enabled = request.Enabled;
        section.TmdbProviderId = request.TmdbProviderId;
        section.ProviderDisplayName = request.ProviderDisplayName ?? string.Empty;
        section.ProviderLogoPath = request.ProviderLogoPath ?? string.Empty;
        section.ContentType = request.ContentType;
        section.Region = request.Region.Trim();
        section.MetadataLanguage = string.IsNullOrWhiteSpace(request.MetadataLanguage)
            ? "es-ES"
            : request.MetadataLanguage.Trim();
        section.SortBy = request.SortBy;
        section.MaxItems = request.MaxItems;
        section.IncludeGenreIds = request.IncludeGenreIds ?? new List<int>();
        section.ExcludeGenreIds = request.ExcludeGenreIds ?? new List<int>();
        section.OriginalLanguage = NullIfBlank(request.OriginalLanguage);
        section.OriginCountry = NullIfBlank(request.OriginCountry);
        section.MinDate = NullIfBlank(request.MinDate);
        section.MaxDate = NullIfBlank(request.MaxDate);
        section.MinRating = request.MinRating;
        section.MinVoteCount = Math.Max(0, request.MinVoteCount);
        section.IncludeAdult = request.IncludeAdult;
        section.RequestsEnabled = request.RequestsEnabled;
        section.CacheDurationMinutes = Math.Max(1, request.CacheDurationMinutes);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Every property is named explicitly in camelCase. Shorthand members
    /// (section.Id) would serialize as PascalCase while named ones stay as
    /// written, producing a mixed-casing payload the frontend cannot rely on.
    /// </summary>
    private static object ToDto(SectionDefinition section, bool hssAvailable, bool seerrConfigured) => new
    {
        id = section.Id,
        displayName = section.DisplayName,
        enabled = section.Enabled,
        orderHint = section.OrderHint,
        tmdbProviderId = section.TmdbProviderId,
        providerDisplayName = section.ProviderDisplayName,
        providerLogoPath = section.ProviderLogoPath,
        contentType = section.ContentType.ToString(),
        region = section.Region,
        metadataLanguage = section.MetadataLanguage,
        sortBy = section.SortBy.ToString(),
        maxItems = section.MaxItems,
        includeGenreIds = section.IncludeGenreIds,
        excludeGenreIds = section.ExcludeGenreIds,
        originalLanguage = section.OriginalLanguage,
        originCountry = section.OriginCountry,
        minDate = section.MinDate,
        maxDate = section.MaxDate,
        minRating = section.MinRating,
        minVoteCount = section.MinVoteCount,
        includeAdult = section.IncludeAdult,
        requestsEnabled = section.RequestsEnabled,
        cacheDurationMinutes = section.CacheDurationMinutes,
        createdUtc = section.CreatedUtc,
        modifiedUtc = section.ModifiedUtc,
        lastSyncUtc = section.LastSyncUtc,
        lastSyncResult = section.LastSyncResult.ToString(),
        lastError = section.LastError,
        homeSectionsRegistered = hssAvailable && section.Enabled,
        seerrConnected = seerrConfigured,
        logoUrl = DisplayTextBuilder.BuildLogoUrl(section.TmdbProviderId),
        generatedQuery = DiscoverQueryBuilder.BuildDisplayString(section, 1),
    };
}

/// <summary>
/// Body of the connection tests. Every field is optional: whatever is absent
/// falls back to what is stored, so the tests work both for credentials the
/// admin has just typed and for the ones already saved.
/// </summary>
public class TestConnectionRequest
{
    /// <summary>Gets or sets the TMDb API key to test.</summary>
    public string? TmdbApiKey { get; set; }

    /// <summary>Gets or sets the Seerr base URL to test.</summary>
    public string? SeerrServerUrl { get; set; }

    /// <summary>Gets or sets the Seerr API key to test.</summary>
    public string? SeerrApiKey { get; set; }
}

/// <summary>Body of the save connection settings request.</summary>
public class SaveConfigRequest
{
    /// <summary>Gets or sets a value indicating whether TMDb is enabled.</summary>
    public bool TmdbEnabled { get; set; }

    /// <summary>Gets or sets the TMDb API key, blank to keep the stored one.</summary>
    public string? TmdbApiKey { get; set; }

    /// <summary>Gets or sets a value indicating whether Seerr is enabled.</summary>
    public bool SeerrEnabled { get; set; }

    /// <summary>Gets or sets the Seerr base URL.</summary>
    public string? SeerrServerUrl { get; set; }

    /// <summary>Gets or sets the Seerr API key, blank to keep the stored one.</summary>
    public string? SeerrApiKey { get; set; }

    /// <summary>Gets or sets a value indicating whether TLS errors are ignored.</summary>
    public bool SeerrIgnoreSslErrors { get; set; }

    /// <summary>Gets or sets a value indicating whether quota bypass is allowed.</summary>
    public bool SeerrAllowIgnoreQuota { get; set; }
}

/// <summary>Body of the create and update section requests.</summary>
public class SectionRequest
{
    /// <summary>Gets or sets the section id. Ignored on create, validated on update.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the section is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the TMDb provider id.</summary>
    public int TmdbProviderId { get; set; }

    /// <summary>Gets or sets the provider display name.</summary>
    public string? ProviderDisplayName { get; set; }

    /// <summary>Gets or sets the provider logo path.</summary>
    public string? ProviderLogoPath { get; set; }

    /// <summary>Gets or sets the content type.</summary>
    public ProviderSectionContentType ContentType { get; set; }

    /// <summary>Gets or sets the watch region.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Gets or sets the metadata language.</summary>
    public string? MetadataLanguage { get; set; }

    /// <summary>Gets or sets the sort order.</summary>
    public ProviderSectionSortBy SortBy { get; set; }

    /// <summary>Gets or sets the maximum item count.</summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>Gets or sets the included genre ids.</summary>
    public List<int>? IncludeGenreIds { get; set; }

    /// <summary>Gets or sets the excluded genre ids.</summary>
    public List<int>? ExcludeGenreIds { get; set; }

    /// <summary>Gets or sets the original language filter.</summary>
    public string? OriginalLanguage { get; set; }

    /// <summary>Gets or sets the origin country filter.</summary>
    public string? OriginCountry { get; set; }

    /// <summary>Gets or sets the earliest date, "yyyy-MM-dd".</summary>
    public string? MinDate { get; set; }

    /// <summary>Gets or sets the latest date, "yyyy-MM-dd".</summary>
    public string? MaxDate { get; set; }

    /// <summary>Gets or sets the minimum rating.</summary>
    public double? MinRating { get; set; }

    /// <summary>Gets or sets the minimum vote count.</summary>
    public int MinVoteCount { get; set; } = 50;

    /// <summary>Gets or sets a value indicating whether adult content is included.</summary>
    public bool IncludeAdult { get; set; }

    /// <summary>Gets or sets a value indicating whether requests are offered.</summary>
    public bool RequestsEnabled { get; set; } = true;

    /// <summary>Gets or sets the cache lifetime in minutes.</summary>
    public int CacheDurationMinutes { get; set; } = 360;
}

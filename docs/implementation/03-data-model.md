# 03 — Modelo de datos

Fuente: sección 6 y 14 del encargo original, `research/02` §3 (patrón de configuración XML de JellyNotify), `research/04` (persistencia de UUID en HSS).

## `SectionDefinition`

Todos los campos pedidos explícitamente en la sección 6 del encargo, mapeados a un tipo concreto:

```csharp
public class SectionDefinition
{
    // Identidad — nunca editable desde la API una vez creada
    public string Id { get; set; }                    // GUID generado server-side al crear, inmutable
    public DateTime CreatedUtc { get; set; }
    public DateTime? ModifiedUtc { get; set; }

    // Presentación
    public string DisplayName { get; set; }            // nombre visible, HTML-encodeado al construir displayText (research/10 §2.2)
    public bool Enabled { get; set; }
    public int OrderHint { get; set; }                  // orden de creación, no confundir con OrderIndex de HSS (ese vive en la config de HSS, no aquí)

    // Proveedor (TMDb)
    public int TmdbProviderId { get; set; }             // identificador estable, ver research/05
    public string ProviderDisplayName { get; set; }     // resuelto en vivo desde TMDb, cacheado, no fuente de verdad
    public string ProviderLogoPath { get; set; }         // logo_path de TMDb, se resuelve a URL vía ProviderLogoService

    // Alcance de la consulta
    public ProviderSectionContentType ContentType { get; set; }  // Movie | Series
    public string Region { get; set; }                   // ISO 3166-1
    public string MetadataLanguage { get; set; }          // ISO 639-1, para nombres/overview localizados

    // Ordenación y límite
    public ProviderSectionSortBy SortBy { get; set; }      // enum lógico, ver 08-tmdb-integration-plan.md
    public int MaxItems { get; set; } = 20;

    // Filtros editoriales (todos ya verificados en research/05, ninguno de monetización)
    public List<int> IncludeGenreIds { get; set; } = new();
    public List<int> ExcludeGenreIds { get; set; } = new();
    public string? OriginalLanguage { get; set; }
    public string? OriginCountry { get; set; }
    public DateOnly? MinDate { get; set; }
    public DateOnly? MaxDate { get; set; }
    public double? MinRating { get; set; }
    public int MinVoteCount { get; set; } = 50;            // valor por defecto no vacío, ver riesgo en research/05
    public bool IncludeAdult { get; set; } = false;

    // Integraciones
    public bool RequestsEnabled { get; set; } = true;       // permite desactivar "Solicitar" por sección
    public int CacheDurationMinutes { get; set; } = 360;    // 6h por defecto, ver 08-tmdb-integration-plan.md

    // Estado de sincronización (solo lectura desde la API de edición)
    public DateTime? LastSyncUtc { get; set; }
    public ProviderSectionSyncResult? LastSyncResult { get; set; }
    public string? LastError { get; set; }

    // Estado de integración (solo lectura, calculado en runtime, no persistido o persistido como último valor conocido)
    public bool HomeSectionsRegistered { get; set; }
    public bool SeerrConnected { get; set; }
}

public enum ProviderSectionContentType { Movie, Series }
public enum ProviderSectionSyncResult { Success, PartialFailure, Failure, NeverRun }
```

**Nota de alcance deliberada**: no existe `ProviderSectionContentType.Mixed` — el modo mixto está fuera de alcance (ver `01-product-requirements.md`). Añadirlo en el futuro es un cambio de enum aditivo, no rompe el esquema existente.

## `PluginConfiguration` (raíz XML del plugin)

```csharp
public class PluginConfiguration : BasePluginConfiguration
{
    public int SchemaVersion { get; set; } = 1;           // versionado explícito desde el día 1
    public TmdbSettings TmdbSettings { get; set; } = new();
    public SeerrSettings SeerrSettings { get; set; } = new();
    public List<SectionDefinition> Sections { get; set; } = new();
}

public class TmdbSettings
{
    // Decisión 2026-07-29: API Key v3 (query string), no Read Access Token v4 (Bearer) — ver
    // implementation/08-tmdb-integration-plan.md §1. Mismo alcance de endpoints, auth más simple.
    public string ApiKey { get; set; } = string.Empty;  // secreto, patrón PreserveSecrets
    public bool Enabled { get; set; }
}

public class SeerrSettings
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;               // secreto, patrón PreserveSecrets
    public bool IgnoreSslErrors { get; set; }
    public bool AllowIgnoreQuota { get; set; }                        // expone o no la bandera ignoreQuota (Seerr 3.4.0+) al crear solicitudes en nombre de otro
}
```

## Persistencia: ¿XML estándar es suficiente?

**Sí, para `PluginConfiguration` (incluida la lista `Sections`).** Precedente directo: JellyNotify ya persiste listas dinámicas (`List<ArrInstanceConfig>`) del mismo orden de magnitud en el XML estándar de `BasePluginConfiguration` sin problema (`research/02` §3). El número esperado de `SectionDefinition` por instalación es bajo (decenas, no miles) — un admin no va a crear cientos de secciones de home. **No se introduce base de datos.**

**Lo que NO vive en `PluginConfiguration`** (mismo criterio que separó los `Store/Json*Store.cs` de JellyNotify de su configuración XML):
- **Resultados de caché de Discover por sección** — volumen potencialmente grande (hasta `MaxItems` × nº de secciones × datos de cada ítem) y de vida corta (minutos/horas) — no pertenece a un fichero de configuración que se reescribe entero en cada guardado. Vive en un store JSON-backed propio (`Store/SectionCacheStore.cs`) o, más simple para el MVP, en un `IMemoryCache` con expiración (sin persistencia entre reinicios — aceptable porque es barato de reconstruir en el primer acceso tras un reinicio).
- **Caché de proveedores/regiones/configuración de imágenes de TMDb** — mismo criterio, `IMemoryCache` con expiración larga (ver duraciones en `08-tmdb-integration-plan.md`).
- **Caché de estado Seerr por título** — mismo criterio, vida corta, `IMemoryCache`.
- **Caché de logos de proveedor descargados** — estos sí conviene persistirlos a disco (no en memoria) porque son pocos (un logo por proveedor usado, no por sección ni por título) y cambian con muy poca frecuencia: fichero por `provider_id` en `ApplicationPaths.PluginConfigurationsPath` (mismo directorio que usa HSS para su propia config, patrón ya observado en `research/04`), servido por `ProviderLogoService`/`WebAssetsController`.

## Versionado de esquema y migraciones

- `PluginConfiguration.SchemaVersion` empieza en `1`. Cualquier cambio de forma incompatible en `SectionDefinition` (renombrar/eliminar un campo con significado distinto) incrementa la versión y añade un paso de migración explícito en `Plugin.cs` (constructor, tras `base(...)`, antes de exponer `Configuration`) — mismo punto de extensión que usa Jellyfin core para migraciones de su propia configuración.
- Los campos añadidos de forma aditiva (nuevo filtro opcional, por ejemplo) **no** requieren incrementar `SchemaVersion` — la deserialización XML de .NET ya tolera campos nuevos con su valor por defecto para configuraciones antiguas.
- `Id` (UUID) nunca se regenera en una migración — es la única invariante que debe sobrevivir a cualquier cambio de esquema, por el requisito de persistencia de posición en HSS (`research/04`).

## Validación

- `Id`: inmutable tras creación — la API de edición debe rechazar (400) cualquier payload que intente cambiarlo.
- `TmdbProviderId`, `ContentType`, `Region`: requeridos, no se puede guardar una sección sin ellos (son los tres campos mínimos para que una consulta Discover sea reproducible).
- `MinVoteCount`: valor por defecto no vacío (`50`) cuando `SortBy` implica ordenación por valoración — validación de UX, no solo de datos (ver riesgo en `research/05`).
- `DisplayName`: longitud máxima razonable (p. ej. 80 caracteres) para no romper el layout de la fila; se HTML-encodea siempre al construir `displayText`, nunca se confía en que el admin no incluya HTML.

## Copias de seguridad y restauración

Al vivir en el mecanismo de configuración estándar de Jellyfin, la copia de seguridad/restauración de `PluginConfiguration` (incluidas todas las `SectionDefinition`) es la misma que la de cualquier plugin: el fichero XML en `plugins/configurations/`. No se requiere mecanismo propio de backup — sí se documenta en `10-testing-environment.md` cómo respaldar/restaurar ese fichero dentro del entorno Docker aislado para las pruebas de rollback.

# 07 — Jellyfin Server: resolución de biblioteca, ProviderIds y plantilla de plugin

> Investigación realizada clonando el código real con `git clone --depth 1 --branch <tag>` a
> `/tmp/research-jellyfin-core/` y leyendo los archivos con `Read`/`grep` (no vía README ni
> documentación derivada). Fecha de consulta: **2026-07-28**.

## Fuente 1: `jellyfin/jellyfin`

- **Nombre**: Jellyfin Server
- **URL**: https://github.com/jellyfin/jellyfin
- **Rama/tag inspeccionado**: tag `v10.11.11`, commit `1fbd8739292cce610231be93daf43368733edf63`
  (verificado con `git ls-remote --tags` — no asumido; existe también `v10.11.10` y `v10.11.9`
  inmediatamente anteriores en la serie 10.11.x).
- **Fecha de publicación del release**: confirmado con `gh release view v10.11.11 --repo
  jellyfin/jellyfin` → `published: 2026-06-06T16:18:54Z`, autor `jellyfin-bot`, no prerelease.
  Changelog del release: un único cambio ("Add lockhelper for UserManager", PR #16944).
- **Paquetes NuGet correspondientes**: `Jellyfin.Controller 10.11.11` y `Jellyfin.Model
  10.11.11` — confirmado que el paquete existe y está publicado ("Last updated 6/6/2026") en
  nuget.org. Coincide exactamente con lo que ya usa `JellyNotify.Plugin.csproj`
  (`PackageReference Include="Jellyfin.Controller" Version="10.11.11"`), así que la versión
  fijada en el proyecto de referencia es correcta y verificable, no una suposición.
- **Licencia**: **GPL-2.0** (confirmado con `gh repo view jellyfin/jellyfin --json
  licenseInfo` → `"key":"gpl-2.0"`, y con el propio archivo `LICENSE` del repo clonado: "GNU
  GENERAL PUBLIC LICENSE Version 2"). Difiere de JellyNotify y de jellyfin-plugin-template
  (ambos GPL-3.0-or-later / GPL-3.0). El consumo normal vía `PackageReference` NuGet no genera
  conflicto; solo copiar código fuente literal y sustancial del server sí lo haría.
- **Archivos inspeccionados** (clon real, no snippets de terceros):
  - `MediaBrowser.Controller/Library/ILibraryManager.cs`
  - `MediaBrowser.Controller/Entities/InternalItemsQuery.cs` (completo)
  - `Jellyfin.Server.Implementations/Item/BaseItemRepository.cs` (implementación EF Core real
    de `QueryItems`/`GetItemsResult`, ~2500 líneas relevantes)
  - `MediaBrowser.Model/Entities/MetadataProvider.cs`
  - `MediaBrowser.Model/Entities/ProviderIdsExtensions.cs`
  - `src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/BaseItemProvider.cs`
  - `src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/BaseItemProviderConfiguration.cs`
  - `src/Jellyfin.Database/Jellyfin.Database.Providers.Sqlite/SqliteDatabaseProvider.cs`
  - `MediaBrowser.Controller/Entities/BaseItem.cs` (`ProviderIds`, `IsVisible`,
    `IsParentalAllowed`, `IsVisibleViaTags`, `GetBlockUnratedValue`)
  - `MediaBrowser.Controller/Entities/Folder.cs` (`IsVisible` override con permisos de
    biblioteca)
  - `MediaBrowser.Controller/Library/IUserViewManager.cs`
  - `Emby.Server.Implementations/Library/UserViewManager.cs` (`GetUserViews`)
  - `Emby.Server.Implementations/Library/LibraryManager.cs` (`GetItemsResult`,
    `AddUserToQuery`, `SetTopParentOrAncestorIds`, `SetTopParentIdsOrAncestors`,
    `GetTopParentIdsForQuery`)
  - `MediaBrowser.Controller/Dto/IDtoService.cs` y `Emby.Server.Implementations/Dto/DtoService.cs`
    (`GetBaseItemDto(s)`, adjunto de `UserData`)
  - `MediaBrowser.Common/Plugins/IPlugin.cs`, `BasePlugin.cs`
  - `MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs`
  - `MediaBrowser.Model/Plugins/IHasWebPages.cs`
- **Nivel de confianza**: Alto — todo lo citado es código fuente real del tag exacto `v10.11.11`,
  clonado y leído directamente, no inferido de documentación ni de versiones antiguas.

### Hallazgos

#### 1. Cómo buscar por `ProviderIds["Tmdb"]` sin iterar toda la biblioteca

`InternalItemsQuery` (`MediaBrowser.Controller/Entities/InternalItemsQuery.cs`) expone:

```csharp
public Dictionary<string, string>? HasAnyProviderId { get; set; }   // línea 338
public Dictionary<string, string>? ExcludeProviderIds { get; set; } // línea 284
```

- **No existe** ningún método literal `AnyProviderIdEquals` en el código de 10.11.11 (búsqueda
  `grep -rn "AnyProviderIdEquals"` sobre todo el repo clonado: 0 resultados). Ese nombre, si
  aparece en guías de terceros o en planes previos, es incorrecto para esta versión y debe
  sustituirse por `HasAnyProviderId`.
- Se consume vía `ILibraryManager.GetItemsResult(InternalItemsQuery)`,
  `ILibraryManager.QueryItems(InternalItemsQuery)` o `ILibraryManager.GetItemList(InternalItemsQuery)`.
- **La traducción real a consulta EF Core** está en
  `Jellyfin.Server.Implementations/Item/BaseItemRepository.cs` líneas 2448-2462:

```csharp
if (filter.HasAnyProviderId is not null && filter.HasAnyProviderId.Count > 0)
{
    var includeAny = filter.HasAnyProviderId.Where(e => string.IsNullOrEmpty(e.Value)).Select(e => e.Key).ToArray();
    if (includeAny.Length > 0)
    {
        baseQuery = baseQuery.Where(e => e.Provider!.Any(f => includeAny.Contains(f.ProviderId)));
    }

    var includeSelected = filter.HasAnyProviderId.Where(e => !string.IsNullOrEmpty(e.Value)).Select(e => $"{e.Key}:{e.Value}").ToArray();
    if (includeSelected.Length > 0)
    {
        baseQuery = baseQuery.Where(e => e.Provider!.Select(f => f.ProviderId + ":" + f.ProviderValue)!.Any(f => includeSelected.Contains(f)));
    }
}
```

  Es decir, `HasAnyProviderId = new() { ["Tmdb"] = "12345" }` se traduce en un filtro SQL sobre
  la tabla relacional `BaseItemProvider` (`ProviderId + ":" + ProviderValue == "Tmdb:12345"`),
  **no** en un escaneo/deserialización de todos los `BaseItem` en memoria.
- Existen además atajos dedicados `HasImdbId`/`HasTmdbId`/`HasTvdbId` (bool?, líneas
  2464-2483 de `BaseItemRepository.cs`) que solo comprueban *presencia* del provider (no del
  valor concreto) — no sirven para "coincide con este id de TMDb", solo `HasAnyProviderId` con
  valor sirve para eso.

#### 2. Índice de base de datos real que respalda esta consulta (responde a la pregunta de caché)

`src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/BaseItemProvider.cs` define
una tabla de relación normalizada `(ItemId, ProviderId, ProviderValue)`, y su configuración EF
Core (`BaseItemProviderConfiguration.cs`) declara explícitamente:

```csharp
builder.HasKey(e => new { e.ItemId, e.ProviderId });
builder.HasOne(e => e.Item);
builder.HasIndex(e => new { e.ProviderId, e.ProviderValue, e.ItemId });
```

Es decir, hay un **índice compuesto real a nivel de base de datos** sobre
`(ProviderId, ProviderValue, ItemId)`. Este índice —no una caché en memoria del servidor— es el
mecanismo que evita el escaneo completo de la biblioteca en cada consulta por TMDb id. No se
encontró ningún `IMemoryCache`/diccionario en memoria en `LibraryManager.cs` ni en
`BaseItemRepository.cs` para este propósito (`grep -n "IMemoryCache\|MemoryCache"` sobre ambos
archivos: 0 resultados) — el rendimiento depende del índice SQL, no de un caché de aplicación.
El proveedor de base de datos por defecto es SQLite
(`UseSqlite(...)` en `SqliteDatabaseProvider.cs`), que sí soporta índices compuestos B-tree de
forma nativa, así que la consulta debería resolverse en tiempo logarítmico sobre el tamaño de
la biblioteca, no lineal — pero esto no se ha medido empíricamente (ver Limitaciones).

#### 3. Misma clave `"Tmdb"` para `Movie` y `Series` — confirmado, sin distinción por tipo

- `ProviderIds` es una propiedad de la clase base **`BaseItem`** (abstracta), no de `Movie` ni
  de `Series` por separado:

```csharp
// MediaBrowser.Controller/Entities/BaseItem.cs línea 105
ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
// línea 721
public Dictionary<string, string> ProviderIds { get; set; }
```

  `Movie`, `Series`, `Episode`, `Season`, etc. heredan todos de `BaseItem`, por lo que
  **no hay ninguna diferencia estructural de almacenamiento entre Movie y Series** para el TMDb
  id: es el mismo diccionario, la misma clave de cadena, el mismo comparador
  case-insensitive.
- El valor canónico de la clave viene del enum `MediaBrowser.Model.Entities.MetadataProvider`
  (`Tmdb = 3`), y `ProviderIdsExtensions.cs` (`SetProviderId(this IHasProviderIds, MetadataProvider provider, string value)`,
  línea 189) usa `provider.ToString()` como clave — es decir, la clave textual es literalmente
  `"Tmdb"` para cualquier tipo de item, confirmado también en el filtro SQL de
  `BaseItemRepository.cs` (`MetadataProvider.Tmdb.ToString().ToLower()` en `HasTmdbId`).
  `ProviderIdsExtensions` también expone `HasProviderId`, `TryGetProviderId`, `GetProviderId`,
  `TrySetProviderId`, `RemoveProviderId` (overloads por `string` y por `MetadataProvider`) como
  API pública recomendada para leer/escribir, en vez de acceder al diccionario a pelo.
- Consecuencia práctica: el motor de resolución del nuevo plugin puede usar exactamente el
  mismo filtro (`HasAnyProviderId = { ["Tmdb"] = tmdbId }`) para películas y para series; la
  única diferencia necesaria es `IncludeItemTypes = [BaseItemKind.Movie]` vs
  `[BaseItemKind.Series]` para acotar el tipo de resultado, no la clave del provider id.

#### 4. Permisos de biblioteca por usuario — mecanismo real, con dos capas

**Capa A — filtrado automático dentro de `ILibraryManager` cuando se pasa un `User`.**
En `Emby.Server.Implementations/Library/LibraryManager.cs`, `GetItemsResult` llama a
`AddUserToQuery(query, query.User)` cuando `query.User is not null` (línea 1657-1660). Y
`AddUserToQuery` (líneas 1701-1726):

```csharp
private void AddUserToQuery(InternalItemsQuery query, User user, bool allowExternalContent = true)
{
    if (query.AncestorIds.Length == 0 &&
        query.ParentId.IsEmpty() &&
        query.ChannelIds.Count == 0 &&
        query.TopParentIds.Length == 0 &&
        string.IsNullOrEmpty(query.AncestorWithPresentationUniqueKey) &&
        string.IsNullOrEmpty(query.SeriesPresentationUniqueKey) &&
        query.ItemIds.Length == 0)
    {
        var userViews = UserViewManager.GetUserViews(new UserViewQuery
        {
            User = user,
            IncludeHidden = true,
            IncludeExternalContent = allowExternalContent
        });

        query.TopParentIds = userViews.SelectMany(i => GetTopParentIdsForQuery(i, user)).ToArray();

        if (query.TopParentIds.Length == 0)
        {
            query.TopParentIds = [Guid.NewGuid()]; // fuerza 0 resultados en vez de "buscar en todo"
        }
    }
}
```

  Es decir: si se construye la consulta con `InternalItemsQuery(user)` (o se asigna
  `query.User`) y **no** se fija manualmente `ParentId`/`AncestorIds`/`TopParentIds`, Jellyfin
  restringe automáticamente `TopParentIds` a las bibliotecas que ese usuario puede ver
  (`UserViewManager.GetUserViews`), y si el usuario no tiene ninguna biblioteca visible, fuerza
  un GUID aleatorio como filtro para garantizar 0 resultados en vez de devolver todo. Esto es
  el mecanismo central que el plugin debe usar para no filtrar manualmente bibliotecas
  restringidas en el caso común.
- Además, `InternalItemsQuery.SetUser(User user)` (líneas 366-390, invocado por el constructor
  `InternalItemsQuery(User? user)`) traduce automáticamente a la consulta SQL las restricciones
  parentales y de tags del usuario: `MaxParentalRating` (desde `user.MaxParentalRatingScore` /
  `MaxParentalRatingSubScore`), `BlockUnratedItems` (desde `PreferenceKind.BlockUnratedItems`),
  `ExcludeInheritedTags`/`IncludeInheritedTags` (desde `PreferenceKind.BlockedTags` /
  `AllowedTags`). Todo esto se aplica **a nivel de consulta SQL**, no como post-filtrado en
  memoria.

**Capa B — verificación puntual sobre un `BaseItem` ya cargado (defensa en profundidad).**
- `BaseItem.IsVisible(User user, bool skipAllowedTagsCheck = false)` (línea 1736) delega en
  `IsParentalAllowed(user, skipAllowedTagsCheck)` (línea 1591), que comprueba tags bloqueados/
  permitidos (`IsVisibleViaTags`) y la puntuación de rating parental
  (`user.MaxParentalRatingScore`/`MaxParentalRatingSubScore` contra
  `LocalizationManager.GetRatingScore(rating)`).
- `Folder.IsVisible` (override, `MediaBrowser.Controller/Entities/Folder.cs` líneas 232-255) es
  el punto **real donde se aplican los permisos de biblioteca por usuario** (no parentales):

```csharp
public override bool IsVisible(User user, bool skipAllowedTagsCheck = false)
{
    if (this is ICollectionFolder && this is not BasePluginFolder)
    {
        var blockedMediaFolders = user.GetPreferenceValues<Guid>(PreferenceKind.BlockedMediaFolders);
        if (blockedMediaFolders.Length > 0)
        {
            if (blockedMediaFolders.Contains(Id)) return false;
        }
        else
        {
            if (!user.HasPermission(PermissionKind.EnableAllFolders)
                && !user.GetPreferenceValues<Guid>(PreferenceKind.EnabledFolders).Contains(Id))
            {
                return false;
            }
        }
    }
    return base.IsVisible(user, skipAllowedTagsCheck);
}
```

  Confirma los nombres reales: `PermissionKind.EnableAllFolders`,
  `PreferenceKind.EnabledFolders`, `PreferenceKind.BlockedMediaFolders`.
- `IUserViewManager.GetUserViews(UserViewQuery query)` (interfaz completa,
  `MediaBrowser.Controller/Library/IUserViewManager.cs`) es el punto público para enumerar qué
  bibliotecas/vistas ve un usuario concreto; su implementación real
  (`Emby.Server.Implementations/Library/UserViewManager.cs`) construye la lista a partir de
  `_libraryManager.GetUserRootFolder().GetChildren(user, true)`, que a su vez filtra por
  `IsVisible(user)` internamente.
- **Recomendación de diseño**: usar la Capa A (pasar `User` a la query) como mecanismo principal
  de rendimiento/seguridad, y añadir una comprobación explícita `item.IsVisible(user)` sobre
  cada resultado antes de exponerlo al frontend como defensa en profundidad (coste marginal
  bajo, ya que el conjunto ya viene acotado por la Capa A).

#### 5. Construcción de `QueryResult<BaseItemDto>` con `UserData`

- `IDtoService` (`MediaBrowser.Controller/Dto/IDtoService.cs`):

```csharp
BaseItemDto GetBaseItemDto(BaseItem item, DtoOptions options, User? user = null, BaseItem? owner = null);
IReadOnlyList<BaseItemDto> GetBaseItemDtos(IReadOnlyList<BaseItem> items, DtoOptions options, User? user = null, BaseItem? owner = null);
```

- La implementación real (`Emby.Server.Implementations/Dto/DtoService.cs`) solo adjunta
  `UserData` cuando `options.EnableUserData` está activo en el `DtoOptions`, delegando en
  `IUserDataManager`:

```csharp
if (options.EnableUserData)
{
    dto.UserData = _userDataRepository.GetUserDataDto(item, dto, user, options); // línea 469
    // o la sobrecarga sin dto: _userDataRepository.GetUserDataDto(item, user);  // línea 506
}
```

  `dto.UserData` (tipo `UserItemDataDto`) es donde viven `Played`, `PlaybackPositionTicks`,
  `IsFavorite`, etc. — el estado de "visto"/progreso por usuario. `ILibraryManager` no expone
  directamente un `QueryResult<BaseItemDto>`; el patrón real es: `QueryResult<BaseItem>` desde
  `ILibraryManager.GetItemsResult(query)` → mapear con `IDtoService.GetBaseItemDtos(items,
  dtoOptions, user)` → envolver el resultado mapeado en un nuevo `QueryResult<BaseItemDto>`
  (mismo `TotalRecordCount`/`StartIndex` que el resultado original de `BaseItem`). Este es el
  patrón que usan los controladores API del propio servidor (`Jellyfin.Api.Controllers.ItemsController`
  fue localizado pero no se profundizó línea a línea; ver Limitaciones).

### Limitaciones

- No se ha medido empíricamente el rendimiento real de `HasAnyProviderId` sobre una biblioteca
  de miles de ítems (solo se confirmó a nivel de código que existe un índice SQL compuesto
  `(ProviderId, ProviderValue, ItemId)`; el plan de implementación debería incluir una prueba
  real contra una instancia con biblioteca grande antes de fijar objetivos de latencia, tal
  como ya señalaba la versión anterior de este documento).
- No se ha leído `Jellyfin.Api.Controllers.ItemsController.cs` línea a línea (solo se confirmó
  su existencia y se usó como referencia indirecta); el mapeo exacto
  `QueryResult<BaseItem>` → `QueryResult<BaseItemDto>` se ha confirmado por composición de
  `IDtoService`/`ILibraryManager`, no citando el controlador literal.
- No se ha revisado si existe algún caché adicional a nivel de `IUserViewManager.GetUserViews`
  (se invoca en cada llamada a `AddUserToQuery`; no se detectó memoización explícita en
  `UserViewManager.cs`, pero tampoco se hizo un análisis exhaustivo de todo el archivo).
- El pipeline de indexación/actualización del propio índice SQL (coste de escritura al escanear
  la biblioteca) no se ha investigado — fuera de alcance de esta tarea, que es sobre lectura.

### Riesgos

- Cualquier documentación o código previo que use el nombre `AnyProviderIdEquals` debe
  corregirse a `HasAnyProviderId` — confirmado que el primero no existe en 10.11.11 (0
  resultados en el repo completo clonado).
- Copiar literalmente fragmentos sustanciales de `jellyfin/jellyfin` (GPL-2.0) dentro del
  plugin (GPL-3.0-or-later) sería incompatible como copia textual; el consumo normal vía
  `PackageReference` NuGet (como ya hace JellyNotify) no tiene este problema.
- Confiar únicamente en la Capa A (auto-restricción por `User` en la query) sin verificación
  puntual `IsVisible()` es razonablemente seguro para el caso común, pero **no** cubre casos
  donde el plugin construya manualmente `TopParentIds`/`ParentId` por otro motivo (por ejemplo,
  para acotar a una biblioteca específica) — en ese caso la auto-restricción de
  `AddUserToQuery` no se activa (la condición exige que `TopParentIds`/`ParentId`/`AncestorIds`
  estén vacíos) y el plugin tendría que aplicar el filtro de biblioteca por sí mismo o llamar
  primero a `IUserViewManager.GetUserViews` y cruzar contra las bibliotecas propias.

### Impacto en el diseño

- El motor de resolución de biblioteca del nuevo plugin debe construir
  `new InternalItemsQuery(currentUser) { HasAnyProviderId = new() { ["Tmdb"] = tmdbId },
  IncludeItemTypes = [BaseItemKind.Movie] /* o Series */, Recursive = true }` y llamar a
  `ILibraryManager.GetItemsResult(query)` — esto ya aplica automáticamente restricciones de
  biblioteca (Capa A) y parentales/tags, apoyado en un índice SQL real, no en iteración en
  memoria.
- Añadir una comprobación `item.IsVisible(currentUser)` explícita sobre cada resultado antes de
  exponerlo al frontend, como defensa en profundidad y para cubrir el caso de riesgo señalado
  arriba (consultas con `ParentId`/`TopParentIds` fijados manualmente).
- Usar la misma clave `"Tmdb"` (vía `ProviderIdsExtensions.TryGetProviderId`/`SetProviderId`,
  no acceso directo al diccionario) tanto para `Movie` como para `Series`; no existe ninguna
  distinción de almacenamiento por tipo que requiera lógica separada.
- Para el DTO final, mapear el `QueryResult<BaseItem>` con
  `IDtoService.GetBaseItemDtos(items, dtoOptions, currentUser)` con `DtoOptions.EnableUserData
  = true` para obtener `Played`/`PlaybackPositionTicks`/`IsFavorite` por usuario en el mismo
  paso, en vez de hacer una segunda consulta a `IUserDataManager`.
- Fijar `Jellyfin.Controller`/`Jellyfin.Model` en `10.11.11` (versión NuGet confirmada
  publicada) y `targetAbi = "10.11.0.0"` en el manifest/`meta.json`, replicando el patrón ya
  usado y probado en producción por JellyNotify.

---

## Fuente 2: `jellyfin/jellyfin-plugin-template`

- **Nombre**: Jellyfin Plugin Template
- **URL**: https://github.com/jellyfin/jellyfin-plugin-template
- **Rama inspeccionada**: `master` (rama por defecto; no está etiquetada por versiones/tags —
  confirmado con `git ls-remote --heads`, que solo devuelve ramas: `10.7`, `master`,
  `unstable`, y varias ramas `renovate/*` de actualización de dependencias, sin tags). Commit
  clonado vía `git clone --depth 1 --branch master` el 2026-07-28.
- **Licencia**: GPL-3.0 (confirmado con `gh repo view jellyfin/jellyfin-plugin-template --json
  licenseInfo` → `"key":"gpl-3.0"`).
- **Archivos inspeccionados** (listado completo del repo clonado, no solo un subconjunto):
  - `Jellyfin.Plugin.Template/Plugin.cs` (completo)
  - `Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj` (completo)
  - `Jellyfin.Plugin.Template/Configuration/PluginConfiguration.cs` (completo)
  - `Jellyfin.Plugin.Template/Configuration/configPage.html` (existencia confirmada, no leído
    en detalle — es HTML/JS trivial de ejemplo)
  - `build.yaml` (completo, raíz del repo)
  - `README.md` (secciones 0–2, hasta configuración básica del plugin)
  - `.github/workflows/*.yaml` (solo listado de nombres: `build.yaml`, `changelog.yaml`,
    `command-dispatch.yaml`, `command-rebase.yaml`, `publish.yaml`, `scan-codeql.yaml`,
    `sync-labels.yaml`, `test.yaml` — no se leyó el contenido de cada workflow)
- **Nivel de confianza**: Alto para lo citado literalmente (código real del `master` en la
  fecha de consulta); **la rama `master` no es inmutable** (no hay tag fijo), así que el
  contenido puede cambiar tras esta fecha — cualquier reconsulta futura debería re-verificar.

### Hallazgos

**Estructura estándar del template (confirmada por lectura directa, no por README):**

- `Plugin.cs`: `public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages`, con
  constructor `Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)`,
  propiedad estática `Instance`, `override string Name`, `override Guid Id`, y
  `GetPages()` devolviendo un único `PluginPageInfo` con
  `EmbeddedResourcePath = "{Namespace}.Configuration.configPage.html"` construido con
  `CultureInfo.InvariantCulture` y `GetType().Namespace`.
- `PluginConfiguration.cs`: clase que hereda de `MediaBrowser.Model.Plugins.BasePluginConfiguration`,
  constructor sin parámetros que fija valores por defecto (patrón trivial, campos
  `bool`/`int`/`string`/`enum` de ejemplo).
- `Jellyfin.Plugin.Template.csproj`: `TargetFramework net9.0`, `Nullable enable`,
  `GenerateDocumentationFile true`, `TreatWarningsAsErrors true`,
  `AnalysisMode AllEnabledByDefault`, `CodeAnalysisRuleSet ../jellyfin.ruleset` — **más
  estricto** que el `.csproj` actual de JellyNotify (que solo tiene `<NoWarn>1591</NoWarn>` y
  no activa `TreatWarningsAsErrors` ni el ruleset compartido).
- **NO existe** `PluginServiceRegistrator.cs` en el template — el patrón de DI moderno
  (`IPluginServiceRegistrator.RegisterServices(IServiceCollection, IServerApplicationHost)`)
  existe en el core (confirmado: `MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs`
  sí está presente en `jellyfin/jellyfin` 10.11.11) pero el template **no** lo ejemplifica; es
  una adición propia de JellyNotify, correcta y recomendable (evita el patrón antiguo
  `IServerEntryPoint`), pero no algo que "seguir tal cual" desde el template porque el template
  no lo cubre.
- **NO existe `meta.json`** en el template. En su lugar usa **`build.yaml`** en la raíz del
  repo (formato YAML con `name`, `guid`, `version`, `targetAbi`, `framework`, `overview`,
  `description`, `category`, `owner`, `artifacts`, `changelog`) — consumido por el workflow
  `build.yaml` de GitHub Actions del propio template (`.github/workflows/build.yaml`, no
  inspeccionado línea a línea) para generar el paquete/manifest de repositorio de plugins
  (flujo típico de la herramienta `jprm`, aunque esto último no se ha verificado directamente
  leyendo el workflow — dejarlo marcado como no verificado literal, solo inferido por
  convención conocida del ecosistema Jellyfin).
- **Discrepancia de versión de paquete dentro del propio template**: el `.csproj` fija
  `Jellyfin.Controller`/`Jellyfin.Model` en **`10.9.11`** (con `<ExcludeAssets>runtime</ExcludeAssets>`),
  pero el `README.md` del mismo repo, en la sección "1. Initialize Your Project", da como
  ejemplo `Version="10.11.3"` y advierte explícitamente: *"Ensure the package reference version
  matches the install version of jellyfin server, otherwise the plugin will show as
  NotSupported."* Es decir, el propio template está desactualizado/inconsistente
  internamente entre su proyecto de ejemplo y su documentación — no debe copiarse la versión
  del `.csproj` de ejemplo sin más, hay que fijarla a mano según la versión objetivo (10.11.11
  en este caso, según Fuente 1).
- `build.yaml` del template fija `targetAbi: "10.9.0.0"` y `framework: "net8.0"`, ambos
  igualmente desactualizados respecto al objetivo 10.11.x/net9.0 de este proyecto.

**Comparación con `JellyNotify.Plugin/` (código real leído del proyecto local):**

| Aspecto | `jellyfin-plugin-template` (master) | `JellyNotify.Plugin/` (real) |
|---|---|---|
| Clase base | `BasePlugin<PluginConfiguration>, IHasWebPages` | Igual — `BasePlugin<PluginConfiguration>, IHasWebPages` (confirmado en `Plugin.cs`) |
| Constructor | `(IApplicationPaths, IXmlSerializer)` | `(IApplicationPaths, IXmlSerializer, ILogger<Plugin>)` — añade logging, no rompe el patrón |
| `GetPages()` | 1 página, `EmbeddedResourcePath` por convención de namespace | 1 página, `EmbeddedResourcePath` explícito + `EnableInMainMenu`, `MenuIcon` — superset, mismo mecanismo |
| DI / `PluginServiceRegistrator` | No existe en el template | Existe (`PluginServiceRegistrator.cs`, implementa `IPluginServiceRegistrator` real del core) — desviación positiva, patrón moderno soportado por el core pero no mostrado en el template |
| Manifest de plugin | `build.yaml` (consumido por CI/jprm, no verificado en detalle) | `meta.json` (JSON, no YAML) — copiado directamente al output por un `build.sh` propio (confirmado: `build.sh` líneas 52-55 y 70 copian literalmente `meta.json`); **no usa `jprm`/`build.yaml`**, es un pipeline de empaquetado custom |
| Versión de paquete NuGet | `10.9.11` (desactualizada, inconsistente con su propio README) | `10.11.11` (correcta y verificada en Fuente 1) |
| `targetAbi` | `10.9.0.0` | `10.11.0.0` |
| Framework | `net9.0` en `.csproj` pero `net8.0` en `build.yaml` (inconsistencia interna del template) | `net9.0` consistente en `.csproj` |
| Rigor de análisis estático | `TreatWarningsAsErrors`, `AnalysisMode=AllEnabledByDefault`, ruleset compartido | Solo `<NoWarn>1591</NoWarn>`, sin ruleset ni `TreatWarningsAsErrors` — más permisivo |
| Licencia | GPL-3.0 | GPL-3.0-or-later (compatible, ligera diferencia de "or-later") |

### Limitaciones

- No se leyó el contenido completo de `.github/workflows/build.yaml`/`publish.yaml` del
  template, así que la afirmación de que `build.yaml` se procesa con `jprm` es una inferencia
  por convención del ecosistema Jellyfin, **no verificada literalmente** en este repo — debe
  tratarse como hallazgo de confianza media, no alta.
- No se leyó `configPage.html` del template en detalle (contenido de ejemplo trivial, bajo
  impacto en el diseño).
- La rama `master` del template no está fijada por tag/commit específico en el enunciado de la
  tarea; se documentó el commit de clonado disponible, pero no hay garantía de estabilidad si
  se re-consulta más adelante (a diferencia del tag inmutable `v10.11.11` del servidor).

### Riesgos

- Copiar el `.csproj` o `build.yaml` del template "tal cual" propagaría versiones de paquete
  desactualizadas (`10.9.11`) e inconsistentes con el propio README del template — riesgo
  concreto y ya observado, no hipotético.
- Si el nuevo plugin decide adoptar `build.yaml`/`jprm` en vez del pipeline custom
  (`build.sh` + `meta.json`) que ya usa JellyNotify, es una decisión de infraestructura nueva
  que debería evaluarse aparte (no es continuista con el proyecto de referencia) — no se ha
  investigado el efecto de mezclar ambos mecanismos en el mismo repositorio de plugins.

### Impacto en el diseño

- Usar `jellyfin-plugin-template` como referencia **solo estructural**: nombre de clases,
  patrón `BasePlugin<TConfig> + IHasWebPages`, ubicación de `configPage.html` como
  `EmbeddedResource` — todo esto ya coincide con lo que hace JellyNotify, así que el nuevo
  plugin de secciones por proveedor debería replicar el mismo patrón ya validado en producción,
  **no** el `.csproj`/`build.yaml` del template.
- Añadir `PluginServiceRegistrator.cs` (patrón JellyNotify, no del template) desde el principio
  para registrar los servicios de resolución TMDb/biblioteca vía DI — es el mecanismo moderno
  soportado por el core (`IPluginServiceRegistrator` confirmado en 10.11.11) y evita el patrón
  legacy `IServerEntryPoint`.
- Mantener el pipeline de empaquetado custom (`build.sh` + `meta.json`) consistente con
  JellyNotify en vez de introducir `jprm`/`build.yaml`, salvo decisión explícita en contra —
  por continuidad operativa entre ambos plugins del mismo autor.
- Fijar explícitamente `Jellyfin.Controller`/`Jellyfin.Model` en `10.11.11` y
  `targetAbi = "10.11.0.0"` a mano en el nuevo `.csproj`/`meta.json`, sin copiar los valores de
  ejemplo del template (que están desactualizados incluso respecto a su propio README).

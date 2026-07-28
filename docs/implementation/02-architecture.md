# 02 — Arquitectura

Fuente: `research/02` (JellyNotify), `research/04` (HSS), `research/07` (Jellyfin core). Este documento describe la arquitectura del nuevo plugin; el detalle de cada integración vive en sus documentos de plan dedicados (`06` a `09`).

## Principio rector

Replicar el esqueleto de plugin ya probado en producción por JellyNotify (`BasePlugin<TConfig>+IHasWebPages`, `PluginServiceRegistrator`, configuración XML nativa, secretos enmascarados, assets embebidos servidos sin auth), y sumar la integración con Home Screen Sections en tiempo de ejecución por reflexión, sin ningún `PackageReference` hacia HSS/File Transformation/Pages (patrón confirmado en todo el ecosistema de IAmParadox27, `research/04` §4.2).

## Estructura de carpetas del nuevo repositorio (`/home/alvaro/Descargas/JellyProviderSections`)

```
JellyProviderSections/
├── JellyProviderSections.Plugin/
│   ├── Api/
│   │   ├── AdminController.cs           # CRUD de secciones, diagnóstico, probar/previsualizar/limpiar caché
│   │   ├── PublicController.cs          # estado de sección para el usuario, acción "Solicitar"
│   │   ├── ResultsController.cs         # handler in-process invocado por HSS (resultsAssembly/Class/Method)
│   │   └── WebAssetsController.cs       # config page CSS/JS + logos de proveedor cacheados (sin auth)
│   ├── Configuration/
│   │   ├── PluginConfiguration.cs       # TmdbSettings, SeerrSettings, List<SectionDefinition>
│   │   └── configPage.html
│   ├── Models/
│   │   ├── SectionDefinition.cs         # ver 03-data-model.md
│   │   ├── TmdbModels.cs
│   │   └── SeerrModels.cs               # reescrito, no heredado literal de JellyNotify (ver 09-seerr-integration-plan.md)
│   ├── Services/
│   │   ├── HomeSectionsRegistrar.cs     # ver 06-home-sections-integration-plan.md
│   │   ├── TmdbApiClient.cs             # ver 08-tmdb-integration-plan.md
│   │   ├── SeerrApiClient.cs            # ver 09-seerr-integration-plan.md
│   │   ├── LibraryResolver.cs           # ver 07 abajo
│   │   ├── ProviderLogoService.cs       # ver 07-provider-logo-plan.md
│   │   ├── SectionCacheService.cs
│   │   └── ScheduledTasks/
│   │       └── RegisterSectionsStartupTask.cs
│   ├── Store/                            # si el volumen de caché de resultados excede lo razonable para XML (ver 03-data-model.md)
│   ├── Web/
│   │   ├── providersections.css          # tokens --jps-* (misma paleta que JellyNotify, tipografía de sistema)
│   │   └── providersections.js           # solo lógica de la página de configuración (no hay inyección global en el MVP)
│   ├── Plugin.cs
│   ├── PluginServiceRegistrator.cs
│   ├── JellyProviderSections.Plugin.csproj
│   └── meta.json
├── JellyProviderSections.Tests/
│   ├── Unit/
│   ├── Integration/
│   └── JellyProviderSections.Tests.csproj
├── build.sh                              # adaptado de build.sh de JellyNotify (ver 13-packaging-and-release.md)
├── manifest.json                         # manifest propio del repo (fuente), distinto del catálogo centralizado
├── LICENSE (GPL-3.0-or-later) / NOTICE.md
└── JellyProviderSections.sln
```

## Componentes y responsabilidades

| Componente | Responsabilidad | Depende de |
|---|---|---|
| `Plugin` | Identidad del plugin, `GetPages()`, acceso estático `Instance` | — |
| `PluginServiceRegistrator` | DI: `HttpClient` tipados para TMDb/Seerr, servicios singleton, `IHostedService`/`IScheduledTask` de registro | — |
| `HomeSectionsRegistrar` | Localiza el ensamblado de HSS por reflexión, construye y envía `SectionRegisterPayload` por cada `SectionDefinition` activa, en arranque y en cada guardado de configuración | HSS (runtime, opcional — degrada si falta) |
| `ResultsController`/handler in-process | Invocado por HSS (`resultsAssembly/Class/Method`) para devolver `QueryResult<BaseItemDto>` cuando el usuario hace scroll sobre una fila | `LibraryResolver`, `TmdbApiClient`, `SectionCacheService` |
| `TmdbApiClient` | Auth, Discover, Watch Providers, configuración de imágenes, caché por capas | TMDb (externo) |
| `LibraryResolver` | Resuelve cada resultado de TMDb contra `ILibraryManager` (`HasAnyProviderId["Tmdb"]`), aplica `IsVisible(user)` | Jellyfin core (`ILibraryManager`, `IDtoService`) |
| `SeerrApiClient` | Estado de disponibilidad/solicitud, creación de solicitudes, resolución de identidad Jellyfin↔Seerr | Seerr (externo, opcional — degrada si falta) |
| `ProviderLogoService` | Cachea localmente el logo del proveedor descargado de TMDb, sirve la URL propia usada en `displayText` | `TmdbApiClient`, `WebAssetsController` |
| `SectionCacheService` | Orquesta las duraciones de caché independientes (config/proveedores/discover/matches/estados Seerr) | — |
| `AdminController` | CRUD de `SectionDefinition`, diagnóstico, probar consulta, previsualizar, limpiar caché | Todos los anteriores |
| `PublicController` | Estado de un título para el usuario actual + acción "Solicitar" | `SeerrApiClient`, sesión de usuario Jellyfin |

## Flujo de datos: renderizado de una sección en la home

```
Usuario abre Jellyfin Web
  → Jellyfin Web pide secciones a Home Screen Sections (mecanismo nativo de HSS)
  → HSS ya tiene registrada (en memoria, desde el arranque) nuestra sección con displayText = logo+nombre
  → HSS invoca nuestro handler in-process (resultsAssembly/Class/Method) pidiendo resultados
  → nuestro handler: SectionCacheService.GetOrBuild(sectionId)
       → si hay caché fresca de discover para esta sección: úsala
       → si no: TmdbApiClient.DiscoverAsync(definition) → pagina hasta MaxItems → dedupe
       → LibraryResolver.ResolveAsync(tmdbResults, currentUser)
            → ítems locales: BaseItemDto real (con UserData: visto/progreso)
            → ítems no locales: BaseItemDto sintético (ProviderIds como bolsa de metadatos, ver research/04 §5bis)
       → SeerrApiClient.GetStatusBatchAsync(tmdbIds) para marcar disponible/pendiente/solicitar en los no locales
  → devuelve QueryResult<BaseItemDto> a HSS
  → HSS/Jellyfin Web renderiza la fila con el título ya incluyendo el logo (displayText)
```

## Flujo de datos: solicitar contenido

```
Usuario pulsa "Solicitar" en una tarjeta no local
  → PublicController.RequestAsync(tmdbId, mediaType, seasons?, is4k?)
       → resuelve currentUser (sesión Jellyfin) → jellyfinUserId
       → SeerrApiClient.ResolveSeerrUserAsync(jellyfinUserId)
            → GET /api/v1/user/jellyfin/:id → si 404 → import-from-jellyfin → si sigue sin existir → error claro al usuario
       → SeerrApiClient.CreateRequestAsync(tmdbId, mediaType, seasons, is4k, X-API-User: seerrUserId)
       → maneja 202/403/409 (ver 09-seerr-integration-plan.md)
       → invalida caché de estado Seerr para ese título
```

## Alternativas descartadas (y por qué)

| Alternativa | Por qué se descarta |
|---|---|
| Vía HTTP `/HomeScreen/RegisterSection` de HSS en vez de reflexión in-process | Endpoint sin autenticación (`research/04` riesgo 1); la vía in-process es el patrón real usado en producción por collection-sections |
| Plugin como proyecto hermano dentro del repo de JellyNotify | Descartado por decisión explícita del usuario tras la investigación — repo separado, catálogo de distribución compartido (ver `01-product-requirements.md`) |
| Inyección frontend propia (`IStartupFilter`+`MutationObserver`) como solución principal del logo | El hallazgo de `displayText` como HTML sin escapar resuelve el mismo problema sin JS propio, sin riesgo de carrera con el DOM — la inyección queda como fallback (`07-provider-logo-plan.md`) |
| Base de datos dedicada para la configuración de secciones | La configuración XML estándar de `BasePluginConfiguration` es suficiente para un número moderado de `SectionDefinition` (mismo patrón que JellyNotify usa hoy para listas de instancias Sonarr/Radarr) — ver `03-data-model.md` para el límite práctico y cuándo reconsiderar |
| Modo mixto películas+series en una fila | Sin algoritmo de mezcla suficientemente determinista (`research/05`) — excluido del alcance, no solo pospuesto |
| Dependencia de compilación (`PackageReference`) hacia HSS | Rompería el patrón de bajo acoplamiento de todo el ecosistema y forzaría fijar una versión exacta de HSS en tiempo de compilación — inviable dado que HSS no publica un paquete NuGet pensado para ser referenciado por terceros |

## Integración con el repositorio JellyNotify

Limitada exclusivamente a **una segunda entrada en `repository/manifest.json`** (y `manifest.json` raíz) de JellyNotify, apuntando a los releases de este repo — el catálogo de distribución es lo único compartido. Toda la documentación (investigación y plan) vive en este repositorio, en `docs/`, no en el de JellyNotify. **No se modifica ningún fichero de `JellyNotify.Plugin/` ni se añade documentación a ese repositorio.**

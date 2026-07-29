# 02 — Análisis local del proyecto JellyNotify

Fecha de consulta: 2026-07-28
Ruta inspeccionada: `/home/alvaro/Descargas/jellyfinnotify/JellyNotify`
Método: lectura directa de código fuente (no solo README), `git log`, `git status`, `git branch`, `git remote`.

## 1. Estado de Git

- Rama actual: `main` (limpia, `nada para hacer commit`).
- Otra rama local: `fix/v1.0.3-jellyfin-enhanced-port` (no inspeccionada en detalle en esta fase; no tocar).
- Remoto: `origin` → `https://github.com/Rovaal-code/JellyNotify.git`.
- 13 commits, todos de tipo `release:`, `fix:`, `wip:`, `chore:` — versión actual instalable: **0.1.0.8** (manifest.json / repository/manifest.json). El `meta.json` embebido en el proyecto aún dice `1.0.3.0` — **discrepancia de versión entre `meta.json` (fuente que compila) y el manifest publicado**; no es nuestro problema a resolver, pero se anota como observación porque el nuevo plugin deberá evitar el mismo patrón de desincronización.
- Última entrada de manifest confirma explícitamente las versiones de terceros verificadas en producción por el propio autor:
  **Jellyfin 10.11.11**, **Seerr 3.3.0**, **Radarr 6.1.1.10360**, **Sonarr 4.0.17.2952**, **Jellyfin Enhanced 11.12.0.0**.
  Esto es la evidencia más fuerte disponible sobre el entorno real objetivo del usuario y debe tratarse como ancla de versión por defecto salvo indicación contraria.

## 2. Estructura general

```
JellyNotify/
├── JellyNotify.Plugin/        # único plugin, net9.0
│   ├── Api/                   # 6 controllers (Admin, ArrWebhook, Diagnostics, Notifications, SeerrWebhook, WebAssets)
│   ├── Configuration/         # PluginConfiguration.cs + configPage.html (embedded resource)
│   ├── Models/                # DTOs (Arr, Seerr, ExternalIds, NotificationEvent, RequestSnapshot, ...)
│   ├── Services/               # ~20 servicios (clientes HTTP, dispatcher, background service...)
│   ├── Store/                  # interfaces + implementaciones JSON-backed
│   └── Web/                    # jellynotify.js (2337 líneas), jellynotify.css (1608 líneas), locales/
├── JellyNotify.Tests/          # xUnit, un solo fichero BasicTests.cs (cobertura mínima)
├── Jellyfin-Enhanced/          # copia vendorizada COMPLETA (con su propio .git) del repo n00bcodr/Jellyfin-Enhanced, usada como referencia de código real
├── references/working-notificator-js/  # script JS de referencia no publicado, gitignored en parte
├── docs/                       # API.md, installation.md, logo.png
├── build.sh                    # build/pack/manifest-update, requiere .NET 9 SDK
├── manifest.json / repository/manifest.json  # catálogo de plugin único (array con 1 entrada)
├── meta.json                   # metadatos embebidos que copia build.sh al output
├── LICENSE (GPL-3.0) / NOTICE.md
└── JellyNotify.sln             # 2 proyectos: JellyNotify.Plugin + JellyNotify.Tests
```

No existe todavía ningún directorio para "JellyProvider Sections"; el repositorio aloja un único plugin.

## 3. Arquitectura técnica del plugin (patrón a replicar)

- **Target**: `net9.0`, `Jellyfin.Controller` / `Jellyfin.Model` NuGet **10.11.11**, `Microsoft.AspNetCore.Mvc.Core 2.2.5`, `EnableDynamicLoading=true`, `PackageLicenseExpression=GPL-3.0-or-later`.
- **Entrada del plugin**: `Plugin : BasePlugin<PluginConfiguration>, IHasWebPages` — patrón estándar de Jellyfin. GUID fijo hardcodeado (`PluginGuid`). Singleton estático `Instance` para acceso desde middleware/servicios.
- **DI**: `PluginServiceRegistrator : IPluginServiceRegistrator` — registra `HttpClient` tipados (`AddHttpClient<IX, X>`), stores JSON singleton, servicios singleton, `IHostedService` para el poller de fondo, y un `IStartupFilter` para la inyección de script.
- **Configuración**: `BasePluginConfiguration` + serialización XML nativa de Jellyfin (vía `IXmlSerializer` que recibe el constructor de `BasePlugin`). **No hay base de datos.** Confirma que la configuración XML estándar es viable para una colección dinámica moderada de objetos (aquí: listas de `ArrInstanceConfig`, settings anidados) — referencia directa para la sección 14 del plan del usuario.
- **Patrón crítico de secretos**: `PluginConfiguration.PreserveSecrets(existing, incoming)` — el frontend nunca reenvía valores secretos (se muestran vacíos/enmascarados); al guardar, si el campo entrante está vacío se conserva el valor persistido. Esto es exactamente el patrón exigido por la sección 11 del prompt para la API key de Seerr (y aquí también aplicaría al TMDb Read Access Token). **Reutilización directa del patrón** (no del código en sí, que es específico de sus propios campos).
- **Inyección de script global en Jellyfin Web**: `ScriptInjectionStartupFilter : IStartupFilter` — middleware ASP.NET registrado *antes* del resto del pipeline, que intercepta la respuesta de `index.html` (detecta `/web/index.html`, `/web/`, `/web`), sólo en GET, desactiva compresión/range para poder reescribir el buffer, inyecta un `<script>` antes de `</body>` de forma idempotente (comprueba que no esté ya presente), nunca lanza excepción hacia el pipeline, y fuerza `Cache-Control: no-store` en la respuesta reescrita para evitar servir un `index.html` cacheado desactualizado. Explícitamente **adaptado de Jellyfin Enhanced (GPL-3.0)**, documentado en `NOTICE.md`. Esta es la pieza más relevante para la sección 7 del prompt (logo junto al título) — ver `08-provider-logo-rendering.md`.
- **Servido de assets estáticos**: `WebAssetsController` sirve JS/CSS/locale JSON como `EmbeddedResource` vía rutas de controlador **no autenticadas** (`[ApiController]` sin `[Authorize]`), justificado porque `index.html` pide el script antes de login. Cabecera `Cache-Control: no-cache, must-revalidate`. Patrón de reutilización directa para servir el logo/CSS/JS del nuevo plugin.
- **Auto-actualización / release checker**: `GitHubReleaseChecker` (singleton, cachea resultado en memoria) — consulta releases de GitHub para avisar de nuevas versiones. Reutilizable como referencia conceptual si el nuevo plugin quiere lo mismo (no imprescindible para el MVP).
- **Background polling**: `JellyNotifyBackgroundService : IHostedService` con lógica de sync, más un segundo hosted service más ligero (`TelegramLinkingService`). Relevante como referencia para el futuro "sync programado" de secciones/caché TMDb, aunque el nuevo plugin probablemente necesite una `IScheduledTask` de Jellyfin más que un `IHostedService` de polling continuo (a decidir en el diseño).
- **Persistencia adicional fuera de XML**: `Store/Json*Store.cs` — stores JSON-backed propios (no la configuración del plugin) para notificaciones, snapshots de requests, bindings de canal, preferencias. Demuestra que el proyecto ya tiene un patrón establecido para persistir colecciones que NO caben bien en `BasePluginConfiguration` (por tamaño/volumen). Relevante si el histórico de sincronizaciones/caché de resultados TMDb por sección creciera demasiado para XML — se evaluará en `14-persistencia`.
- **Cliente Seerr real ya operativo**: `SeerrApiClient` / `ISeerrApiClient` + `SeerrModels.cs` + `SeerrWebhookModels.cs` + `SeerrWebhookController.cs`. Analizado en detalle por el sub-informe `06-seerr-api-analysis.md` (investigación delegada); aquí solo se registra que **existe y es la fuente de verdad más fiable disponible sobre el contrato real de Seerr**, más fiable que la documentación pública genérica.
- **Testing**: `JellyNotify.Tests/BasicTests.cs` — cobertura mínima (xUnit, un único fichero). No hay un patrón de tests de integración con servidor HTTP simulado todavía en este repo; el nuevo plugin tendrá que introducirlo desde cero (no hay nada que reutilizar aquí más allá del `.csproj` de test como plantilla).
- **Build**: `build.sh` invoca `dotnet publish` con `-p:Version=$VERSION`, empaqueta DLL+XML+meta.json en zip vía Python, calcula MD5, actualiza `manifest.json` y `repository/manifest.json` con changelog embebido en el propio script (hardcodeado — frágil, pero funciona). **Riesgo detectado**: en este entorno de trabajo `dotnet` no está instalado (`which dotnet` → no encontrado) y `~/.dotnet` sólo contiene sentinels vacíos, no el SDK real — el build.sh existente asume un SDK que ya no está presente en esta máquina. Esto es una entrada directa al inventario de bloqueantes/decisiones (ver `11-open-questions-and-readiness.md`): compilar en Docker con una imagen `mcr.microsoft.com/dotnet/sdk` es la opción más robusta y reproducible para el entorno aislado de pruebas, en vez de depender de un SDK instalado en el host.

## 4. Sistema visual (JellyNotify.Plugin/Web/jellynotify.css, 1608 líneas)

Variables de diseño (`:root`), identidad "holográfica":

```css
--jn-accent: #a970ff;            /* violeta principal */
--jn-accent-hover: #bd94ff;
--jn-accent-glow: rgba(169, 112, 255, 0.35);
--jn-magenta: #ff5fd8;
--jn-cyan: #35e5f0;
--jn-bg: #05050b;                 /* casi negro */
--jn-surface: rgba(255, 255, 255, 0.045);   /* glass */
--jn-surface-2: rgba(255, 255, 255, 0.03);
--jn-glass-blur: 20px;
--jn-text: #f2f0ff;
--jn-text-muted: #a9a2cf;
--jn-text-faint: #6c6690;
--jn-border: rgba(180, 160, 255, 0.22);
--jn-border-strong: rgba(190, 170, 255, 0.4);
--jn-success: #38f0b8;
--jn-danger: #ff5577;
--jn-warning: #ffbb55;
--jn-info: #35e5f0;
--jn-radius: 16px;
--jn-radius-sm: 10px;
--jn-shadow: 0 8px 30px rgba(0,0,0,0.45);
--jn-transition: 0.2s ease;
--jn-font-display: 'JNOrbitron', -apple-system, 'Segoe UI', sans-serif;  /* fuente embebida base64 en el propio CSS */
--jn-font-body: 'JNExo', -apple-system, 'Segoe UI', Roboto, sans-serif;  /* fuente embebida base64 */
```

Patrones de componente reutilizables observados:
- `.jn-card`: superficie de cristal (`backdrop-filter: blur`), borde 1px, `border-radius: var(--jn-radius)`, **borde en gradiente violeta→cian dibujado con un pseudo-elemento enmascarado** (`::before` con `mask-composite: exclude`) — técnica elegante y reutilizable para las tarjetas de sección del nuevo plugin sin depender de JS.
- `.jn-subhead` + `.jn-subhead-label`: divisor de sección con etiqueta en mayúsculas, letter-spacing amplio, color cian, línea degradada a la derecha — patrón directamente reutilizable para separar bloques dentro de la tarjeta expandida (UUID/consulta TMDb/caché/Seerr, etc.).
- `.jn-toggle` / `.jn-toggle-slider` / `.jn-toggle-label`: switch accesible.
- `.jn-panel-*`, `.jn-toast-*`: panel deslizante y sistema de toasts (bell/panel), conceptualmente relevante pero no reutilizable literal (es específico de notificaciones).
- `.jn-tabs` / `.jn-tab-btn` / `.jn-tab-icon`: navegación por pestañas en la página de configuración (Jellyfin usa Material Icons vía `.jn-tab-icon-material`).
- Dos fuentes (`JNOrbitron`, `JNExo`) embebidas como base64 directamente en el CSS (líneas larguísimas, ~60KB cada una) — coste de carga a tener en cuenta si el nuevo plugin hereda la tipografía literal; alternativa más ligera: cargar solo pesos necesarios o mantenerlas como recurso compartido servido una vez por JellyNotify y referenciado (evita duplicar 120KB en dos plugins) — a decidir en el plan (ver `03-compatibility-matrix.md` / arquitectura).

**Nota de alcance**: el prompt del usuario pide que el nuevo plugin "se sienta visualmente parte del mismo producto" — se interpreta como: reutilizar la MISMA paleta de variables CSS, la misma familia tipográfica, el mismo lenguaje de tarjetas de cristal con borde en gradiente, pero como **hoja de estilos propia del nuevo plugin** (no una dependencia de ensamblado compartida entre plugins — Jellyfin carga cada plugin como assembly aislado, así que no hay forma limpia de "importar" el CSS embebido de otro plugin sin acoplar ambos). Se documenta como decisión preliminar pendiente de confirmación del usuario en la síntesis final.

## 5. `configPage.html` y JS de configuración

- 479 líneas HTML, cargado como `EmbeddedResourcePath` vía `IHasWebPages.GetPages()`.
- Sigue el patrón estándar de plugin-page de Jellyfin (usa el propio `ApiClient` de Jellyfin Web inyectado en el iframe de configuración) y carga su CSS/JS vía `ApiClient.getUrl(...)` en vez de `<link>`/`<script src>` relativos — **esto es un hallazgo técnico importante y no obvio**: las rutas relativas normales NO resuelven correctamente dentro del iframe de la página de plugin de Jellyfin Web; hay que usar `ApiClient.getUrl()` para construir la URL absoluta correcta. Documentado explícitamente en `NOTICE.md` como patrón tomado de Jellyfin Enhanced. **Aplicable directamente** al config page del nuevo plugin.
- `jellynotify.js` (2337 líneas) mezcla lógica de bell/panel global + lógica exclusiva de la página de configuración en el mismo bundle sencillo (sin bundler/build step de JS — vanilla JS servido directo). Simplicidad deliberada, coherente con no añadir tooling de frontend innecesario.

## 6. Clasificación de reutilización

| Componente | Clasificación | Notas |
|---|---|---|
| Paleta de variables CSS (`:root`) y tipografía | **Reutilización directa** | Copiar como hoja de estilos propia del nuevo plugin (no import cruzado) |
| Patrón `.jn-card` (borde en gradiente por pseudo-elemento) | **Reutilización con adaptación** | Renombrar prefijo de clase (`jps-` en vez de `jn-`) para evitar colisiones si ambos plugins acaban conviviendo en la misma página de Jellyfin |
| Patrón `.jn-subhead` (divisores de sección) | **Reutilización directa** | Encaja exactamente con el diseño de tarjeta expandida pedido en la sección 13 del prompt |
| `PluginConfiguration.PreserveSecrets` (no reenvío de secretos) | **Reutilización con adaptación** | Mismo patrón, aplicado a `TmdbSettings.ApiReadAccessToken` y `SeerrSettings.ApiKey` propios del nuevo plugin |
| `ScriptInjectionStartupFilter` | **Reutilización con adaptación (bajo GPL-3.0)** | Ver `08-provider-logo-rendering.md`; el nuevo plugin necesitará su propia inyección solo si la solución de logo elegida lo requiere — no asumir que hace falta sin confirmar la alternativa elegida |
| `WebAssetsController` (assets embebidos sin auth) | **Reutilización con adaptación** | Mismo patrón para servir CSS/JS/logos cacheados del nuevo plugin |
| `SeerrApiClient` / `ISeerrApiClient` / `SeerrModels` | **Reutilización con adaptación** | Ver `06-seerr-api-analysis.md`: probablemente se pueda extraer y generalizar en vez de reescribir desde cero, pero decisión final pendiente de comparar contrato real |
| `GitHubReleaseChecker` | **Solo referencia conceptual** | No es MVP; no todos los plugins necesitan autoactualización anunciada |
| `Jellyfin-Enhanced/` (copia vendorizada completa) | **Riesgo de acoplamiento** | Es una copia de trabajo de un repo de terceros con su propio `.git` anidado dentro de JellyNotify — útil como referencia de código real (ya explotado por el fork de investigación de logo), pero **no debe tratarse como parte del árbol de JellyNotify a mantener**; no modificar, no fusionar su historia de git |
| `references/working-notificator-js/` | **Solo referencia conceptual, no aplicable** | Es específico del dominio de notificaciones, sin relación con secciones de proveedor |
| `JellyNotify.Tests/BasicTests.cs` | **No reutilizable directamente** | Sirve solo como plantilla mínima de `.csproj` de test (xUnit + referencia al proyecto principal) |
| `build.sh` | **Reutilización con adaptación** | Mismo esqueleto (publish → zip → checksum → manifest), pero debe generalizarse para dos plugins en el mismo repo (dos manifests o una entrada más en el array existente) y para build reproducible en Docker en vez de depender de un SDK local ausente |
| Dependencia de `dashboard-icons` CDN externo | **Dependencia que convendría evitar** | JellyNotify carga iconos de servicio desde `cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons` en tiempo de ejecución; el nuevo plugin usará en su lugar los logos oficiales servidos por TMDb (`image.tmdb.org`) cacheados localmente — más apropiado y ya exigido por el prompt del usuario, pero se anota el precedente de "cargar imágenes de terceros en tiempo de ejecución" como patrón ya aceptado en este proyecto |

## 7. Implicación arquitectónica principal

JellyNotify demuestra que este entorno del usuario ya sabe compilar, empaquetar, versionar y distribuir un plugin GPL-3.0 completo para Jellyfin 10.11.11 con inyección de frontend, configuración XML, secretos enmascarados y múltiples integraciones externas (Seerr, Sonarr/Radarr). El nuevo plugin "JellyProvider Sections" puede seguir el mismo esqueleto de proyecto casi 1:1 (mismo target framework, mismas versiones de paquete Jellyfin, mismo patrón de `Plugin`/`PluginServiceRegistrator`/`PluginConfiguration`/`WebAssetsController`), como **proyecto hermano** dentro del mismo repositorio (`JellyProviderSections.Plugin/` junto a `JellyNotify.Plugin/`), con su propio GUID, su propia entrada en el manifest, y sin tocar el código de JellyNotify. Esta es la recomendación arquitectónica preliminar (pendiente de confirmación explícita del usuario, ver readiness gate).

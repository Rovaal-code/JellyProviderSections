# 08 — Renderizado del logotipo del proveedor junto al título de sección

Fecha de consulta: 2026-07-28.
Investigador: agente `research-jellybridge-logo`.

**Nota de revisión**: esta versión reescribe y amplía una versión anterior que analizó JellyBridge únicamente vía `gh api` (metadatos, sin leer el código). Esta versión clona el repositorio real (`git clone` a `/tmp/research-jellybridge/JellyBridge`) y lee el código fuente completo de las tres fuentes con citas de línea concretas. **Conserva íntegro, sin modificar, el hallazgo crítico añadido en paralelo por el agente de investigación de Home Screen Sections** (sección "Home Screen Sections / File Transformation" más abajo) — ese hallazgo cambia la recomendación final y se ha priorizado en consecuencia.

## Alcance de este documento

Cubre tres fuentes, todas leídas directamente en código fuente real (no documentación de terceros ni suposiciones):

1. **JellyBridge** (`kinggeorges12/JellyBridge`) — clonado a `/tmp/research-jellybridge/JellyBridge`.
2. **JellyNotify** (`ScriptInjectionStartupFilter.cs` + `WebAssetsController.cs`) — mecanismo de inyección YA EN PRODUCCIÓN en este mismo repositorio.
3. **Jellyfin Enhanced** (copia vendorizada en `Jellyfin-Enhanced/`) — mecanismo de inyección del que JellyNotify es una adaptación declarada, más sus patrones concretos de creación de secciones/badges vía JS.

Más una cuarta pieza, no investigada por este agente sino por `research-hss` en paralelo y reproducida aquí sin alterarla: el propio renderizado de título de Home Screen Sections (`loadSections.js`), que resulta ser la vía más simple de todas.

No hay contenido de monetización en este análisis.

---

## Fuente 1 — JellyBridge

- **Nombre**: JellyBridge
- **URL**: https://github.com/kinggeorges12/JellyBridge
- **Ruta local del clon**: `/tmp/research-jellybridge/JellyBridge`
- **Commit/tag consultado**: `5260f5a438d1e2dd453eef5a060606d451ab3d51` (rama `main`, README indica versión de release "3.3")
- **Fecha de consulta**: 2026-07-28
- **Licencia**: GPL-3.0 — confirmado leyendo `LICENSE` en el repo (texto íntegro de la GPLv3, "This License refers to version 3 of the GNU General Public License"; el `README.md` lo confirma en texto plano: "This project is open source and available under the GNU General Public License v3.0"). No se declara explícitamente "or later" en el propio fichero de licencia del repo más allá del boilerplate estándar de la FSF.
- **Archivos relevantes inspeccionados**:
  - `README.md` (descripción funcional completa, notas de versión, agradecimientos)
  - `src/Jellyfin.Plugin.JellyBridge/Services/MetadataService.cs`
  - `src/Jellyfin.Plugin.JellyBridge/Services/PlaceholderVideoGenerator.cs`
  - `src/Jellyfin.Plugin.JellyBridge/Services/FavoriteEventHandler.cs`
  - `src/Jellyfin.Plugin.JellyBridge/Services/ApiService.cs`
  - `src/Jellyfin.Plugin.JellyBridge/Services/DiscoverService.cs`
  - `src/Jellyfin.Plugin.JellyBridge/BridgeModels/JellyseerrEndpoint.cs`
  - `src/Jellyfin.Plugin.JellyBridge/JellyseerrModel/Common/interfaces.cs` (y `Server/common.cs`) — campo `LogoPath`
  - Árbol completo de `src/Jellyfin.Plugin.JellyBridge/*.cs` (listado exhaustivo: sin `js/`, sin `wwwroot/`, sin `IStartupFilter` en ningún fichero)
  - Issues #47/#46/#28/#8 (abiertas) y #13 (cerrada, muy relevante) vía `gh issue`

### Hallazgos

**1. Arquitectura fundamentalmente distinta: biblioteca real con ficheros de vídeo placeholder, no filas de Home Screen.**

JellyBridge NO usa Home Screen Sections, NO inyecta JS/CSS en Jellyfin Web y NO tiene ningún `IStartupFilter` (confirmado por `grep -rln "IStartupFilter" --include="*.cs" .` → cero resultados). En su lugar:

- Crea carpetas reales en disco por título descubierto (`MetadataService.GetJellyBridgeItemDirectory`, líneas 429-476), organizadas opcionalmente por "carpeta de red" (proveedor) vía `GetNetworkFolder()` (líneas 419-424), con un prefijo configurable (`NetworkFolderPrefix`).
- Cada carpeta contiene un `.nfo`, un JSON de metadatos propio (`WriteMetadataAsync`, líneas 247-290) y un **vídeo MP4 real generado con FFmpeg** a partir de una imagen estática (`PlaceholderVideoGenerator.cs`, líneas 340-475): compone `movie.png`/`show.png` (embebido o subido por el admin) en un vídeo de duración configurable (`PromoVideoDurationSeconds`) vía `_mediaEncoder.EncoderPath` (el propio FFmpeg de Jellyfin) con flags `-loop 1 -i asset -t duration -vf scale/pad -c:v libx264 -pix_fmt yuv420p -movflags +faststart`.
- Jellyfin escanea esa carpeta como una **biblioteca normal** (`Discover library`) y el contenido "no presente localmente" se representa como **ítems de biblioteca reales y reproducibles** (el vídeo placeholder), no como tarjetas grises "externas" ni overlays de "Solicitar". Se ve en la vista de biblioteca estándar (grid), no como fila dinámica de home.
- El proveedor ("red") es simplemente **un nombre de carpeta**, no una etiqueta visual junto a ningún título de fila.

**2. Cómo obtiene proveedores/regiones — vía Jellyseerr, no directamente TMDb.**

`BridgeModels/JellyseerrEndpoint.cs` (líneas 31-34) define `WatchProvidersRegions`, `WatchProvidersMovies`, `WatchProvidersTv`. `Services/ApiService.cs` mapea esos endpoints a rutas reales:
```
"/api/v1/watchproviders/regions"   (línea 241)
"/api/v1/watchproviders/movies"    (línea 247)
"/api/v1/watchproviders/tv"        (línea 253)
```
Es decir, JellyBridge **nunca llama a la API de TMDb directamente**: delega el descubrimiento por proveedor/región en los endpoints propios de Jellyseerr (que a su vez llaman a TMDb internamente). `DiscoverService.cs` (líneas 32-111) itera una `NetworkMap` configurada por el admin y llama a `DiscoverMovies`/`DiscoverTv` de Jellyseerr pasando `watchRegion` = `network.Country` y `watchProviders` = `network.Id` como query params.

**3. Cómo conecta con Seerr.** `ApiService.cs` línea 320: header `X-Api-Key` con la API key configurada por el admin (`Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.ApiKey))`, línea 783), contra una URL base "Jellyseerr URL" configurable. Nada de OAuth ni sesión de usuario — una única API key de servicio para todas las llamadas admin→Jellyseerr.

**4. Cómo identifica usuarios de Jellyfin.** `FavoriteEventHandler.cs` (`IHostedService`) se suscribe a `IUserDataManager.UserDataSaved` (línea 44). Cuando `SaveReason == UpdateUserRating` y `UserData.IsFavorite == true` sobre un ítem dentro del directorio de sincronización de JellyBridge (`FolderUtils.IsPathInSyncDirectory`, línea 91), resuelve el usuario con `_userManager.GetUserById(e.UserId)` (línea 76) y dispara `ManageFavoriteRequestsController.SyncFavorites()` (líneas 101-114) — es el mecanismo nativo de eventos de `IUserDataManager` de Jellyfin, sin nada específico de "provider sections".

**5. Cómo implementa la solicitud de contenido.** Favoritar un ítem placeholder en Jellyfin (desde cualquier cliente: web, Android TV, Kodi vía Kodi Sync Queue) dispara una solicitud real a Jellyseerr atribuida al usuario que lo favoritó. Tras solicitarse, el ítem se oculta de la biblioteca de JellyBridge (`Favorite Cleanup`). Existen opciones para pedir solo la primera temporada (`RequestOnlyFirstSeason`) y para pedir 4K si el usuario tiene permiso en Jellyseerr.

**6. Logo — campo presente en el modelo, nunca renderizado.** `JellyseerrModel/Common/interfaces.cs` (líneas 290-291, 906-907, 932-933, 955-956, 1119-1120) y `JellyseerrModel/Server/common.cs` (líneas 14-15, 39-40, 178-179) declaran `LogoPath` (`[JsonPropertyName("logo_path")]`/`"logoPath"`) como propiedad de deserialización de las respuestas de Jellyseerr. Se confirmó con `grep -rn "\.LogoPath" --include="*.cs" .` sobre todo el árbol de código que **este campo nunca se lee ni se usa en ningún otro punto del plugin** — se deserializa y se descarta. JellyBridge no pinta ningún logo en ningún sitio.

**7. Issue cerrada #13 — interacción no deseada con Home Screen Sections (evidencia de fragilidad).** Un usuario reporta en Reddit (citado íntegro en la issue) que los vídeos placeholder de JellyBridge, al ser ítems de biblioteca reales y reproducibles, **contaminan las filas "Recently Added" del plugin Home Screen Sections** porque Jellyfin los trata como contenido real recién añadido. La respuesta del mantenedor reconoce el problema y solo ofrece mitigaciones manuales (desmarcar "Enable Rewatching" en Home Screen Sections, marcar todo como visto en JellyBridge) — no hay solución de diseño, solo workarounds del usuario. Evidencia directa de que el enfoque "biblioteca con placeholders reales" tiene fugas de comportamiento hacia otros plugins que consumen la biblioteca de Jellyfin de forma genérica.

### Limitaciones conocidas / issues abiertas relevantes

- #47 "Jellyfin 12 RC support?" — compatibilidad futura incierta.
- #46 "Seeing duplicate movies/shows" — bug de deduplicación (el propio README v3.3 dice haberlo parcheado parcialmente).
- #28 "Films and TV shows from Seer blocklist are visible in Jellyfin" — el blocklist de Jellyseerr no se respeta correctamente.
- #8 "jellyseer search result in jellyfin search" — feature request abierta, sin resolver.
- El README exige explícitamente una **imagen Docker de Jellyseerr parcheada por el propio autor** (`kinggeorges12/jellyseerr:latest`) para que las aprobaciones de solicitudes creadas por JellyBridge funcionen correctamente — dependencia dura de un fork no oficial de Jellyseerr, no del proyecto oficial ni de Seerr estándar.

### Riesgos

- El campo `LogoPath` capturado pero nunca usado sugiere que el propio autor consideró en algún momento mostrar el logo y lo descartó, o no llegó a implementarlo — no hay ninguna pista de por qué en el código ni en el README/CHANGELOG.
- La arquitectura de "placeholder reproducible" es frágil frente a cualquier plugin que trate la biblioteca de Jellyfin de forma genérica (ya demostrado con Home Screen Sections en la issue #13); adoptar ideas de JellyBridge sin cuidado replicaría ese mismo problema.

### Qué NO copiar de JellyBridge

- El patrón de "vídeo placeholder real generado con FFmpeg dentro de una biblioteca de verdad" para representar contenido no presente. Es pesado (llamada a FFmpeg por ítem, caché de vídeos, limpieza), invasivo (ítems reproducibles falsos que ensucian bibliotecas y plugins de terceros, como demuestra la issue #13) y no es la categoría de solución que pide el requisito de "secciones dinámicas de home" — el requisito de Jellyfin Provider Sections apunta a filas de Home Screen Sections con tarjetas marcadas como externas, no a una biblioteca sintética completa.
- La dependencia de un fork parcheado de Jellyseerr para que las aprobaciones funcionen: exactamente el tipo de acoplamiento frágil a evitar; el plan debe funcionar contra Seerr/Jellyseerr estándar.

### Nivel de confianza

**Alto.** Todos los hallazgos provienen de lectura directa del código clonado (no de README, no de metadatos de GitHub) salvo la sección de release notes/README, citada como tal. El grep de `LogoPath` fue exhaustivo sobre el árbol completo.

### Impacto en el diseño

JellyBridge **no aporta ningún patrón técnico reutilizable para el requisito de logo-junto-a-título**, porque no resuelve ese problema en absoluto (ni siquiera lo intenta, pese a tener el dato disponible). Su único aporte útil al proyecto es confirmar, desde una implementación real con adopción (participación activa en issues, releases numeradas hasta 3.3), que "proveedor + región" vía los watch-provider endpoints es un criterio de descubrimiento viable — pero eso ya está cubierto por la investigación de TMDb y no cambia la recomendación de esta sección. Se descarta como fuente de arquitectura para el renderizado del logo.

---

## Fuente 2 — JellyNotify: `ScriptInjectionStartupFilter.cs` + `WebAssetsController.cs`

- **Nombre**: JellyNotify (mecanismo ya en producción en este mismo repositorio de referencia)
- **Ruta local**: `/home/alvaro/Descargas/jellyfinnotify/JellyNotify/JellyNotify.Plugin/Services/ScriptInjectionStartupFilter.cs` y `.../Api/WebAssetsController.cs`
- **Commit/estado**: working tree local (ficheros leídos tal cual están en disco a fecha de consulta)
- **Fecha de consulta**: 2026-07-28
- **Licencia**: GPL-3.0-or-later (licencia del repo JellyNotify); el propio fichero declara en su docstring: *"Adapted from Jellyfin Enhanced's ScriptInjectionStartupFilter (GPL-3.0) — see NOTICE.md at the repository root for attribution."*
- **Archivos relevantes**: los dos ficheros arriba, más `Plugin.cs` (método `BuildScriptTag`) y `Web/jellynotify.js` (2337 líneas) para los patrones de manipulación de DOM ya usados.

### Hallazgos — mecanismo exacto, con líneas citadas

**¿Implementa `IStartupFilter`?** Sí. `ScriptInjectionStartupFilter : IStartupFilter` (línea 32), registrado vía `Configure(Action<IApplicationBuilder> next)` que llama `app.Use(InvokeAsync)` **antes** de `next(app)` (líneas 47-57) — al registrarse antes de `next(app)`, el middleware queda en la posición más externa del pipeline, garantizando que ve la respuesta ya completamente generada por el manejador de ficheros estáticos aguas abajo, para poder reescribirla.

**¿Qué ruta intercepta?** `IsIndexRequest` (líneas 173-183) hace match por sufijo (no ruta exacta) sobre `.../web/index.html`, `.../web/`, `/web` (exacto), usando `EndsWith` — correcto incluso con Jellyfin detrás de un prefijo base-url. Solo actúa sobre `GET` (línea 69); deja pasar `HEAD`/`OPTIONS` sin tocar.

**¿Cómo inserta el `<script>`?**
1. Elimina `Accept-Encoding`, `Range`, `If-Range` de la petición entrante (líneas 86-88) para forzar un 200 completo, sin comprimir y sin rango parcial.
2. Sustituye `context.Response.Body` por un `MemoryStream` propio (líneas 90-92), deja correr el pipeline (`await nextMw()`), y solo entonces lee el HTML completo.
3. Si el content-type es `text/html` y el status es 200 (líneas 109-110), busca `</body>` con `html.LastIndexOf("</body>", ...)` (línea 131) e inserta ahí el tag: `html.Substring(0, bodyClose) + tag + "\n" + html.Substring(bodyClose)` (línea 136).
4. **Idempotencia**: comprueba primero `html.IndexOf("/JellyNotify/script", ...) >= 0` (línea 130) para no duplicar el `<script>` en recargas o reinicios repetidos.
5. El tag exacto se construye en `Plugin.cs` (`BuildScriptTag()`, líneas 101-105):
   ```csharp
   return $"<script plugin=\"{Name}\" version=\"{cacheKey}\" src=\"../JellyNotify/script?v={cacheKey}\" defer></script>";
   ```
   con `cacheKey` derivado de la versión del plugin + sufijo de fichero (cache-busting automático).
6. Tras reescribir, borra `ETag`/`Last-Modified`/`Accept-Ranges` y fuerza `Cache-Control: no-store, no-cache, must-revalidate` (líneas 157-166) — evita que el navegador sirva una copia cacheada de `index.html` de antes de instalar/actualizar el plugin.
7. **Defensivo por diseño**: cualquier excepción durante la reescritura se captura y loguea, sirviendo el HTML original sin el script (líneas 145-149) — nunca rompe la carga de Jellyfin Web. Desactivable con el flag `DisableScriptInjectionMiddleware` (línea 76).

**¿Cómo se sirven después los JS/CSS reales?** `WebAssetsController.cs`, ruta base `[Route("JellyNotify")]` (línea 19):
- `GET JellyNotify/script` → `jellynotify.js` embebido (línea 26)
- `GET JellyNotify/web/jellynotify.css` → `jellynotify.css` embebido (línea 30)
- `GET JellyNotify/Configuration/configPage.css` / `.../configPage.js` → mismos ficheros embebidos, reutilizados por la página de configuración del plugin (líneas 33-38)
- `GET JellyNotify/web/locales/{code}.json` → recurso de idioma embebido, con `code` validado por regex `^[A-Za-z-]{2,20}$` antes de construir el nombre de recurso (líneas 46-51), evitando path/resource injection.

Todo se sirve como **recurso embebido en el ensamblado** (`Assembly.GetExecutingAssembly().GetManifestResourceStream(...)`, línea 58) — sin escritura en el `web/` de Jellyfin en ningún momento, y estas rutas están **deliberadamente sin `[Authorize]`** porque `index.html` las pide antes de que exista sesión (comentario líneas 13-16).

### Limitaciones

- Reescribe *todo* `index.html` en cada petición (buffer completo en memoria) — coste aceptable para un fichero pequeño servido con poca frecuencia, pero no se debe generalizar a rutas de alto tráfico.
- Depende de que `index.html` siga siendo servido como fichero estático simple por el pipeline de Jellyfin (así es en 10.11.11, sin garantía formal futura).
- No cubre cachés externas fuera del control de Jellyfin (proxy agresivo).

### Riesgos

- Bajo: el propio código contempla fallos (nunca rompe el pipeline) y ya lleva tiempo en producción en este mismo repositorio sin incidencias reportadas en la memoria del proyecto.
- El riesgo real no está en este mecanismo de transporte (robusto y probado), sino en lo que se haga *con* el script inyectado del lado del DOM de Home Screen Sections — ver sección de riesgo de acoplamiento al final.

### Nivel de confianza

**Muy alto.** Código leído íntegro, con líneas citadas arriba; es el mismo mecanismo que ya funciona en producción según la memoria del proyecto.

### Impacto en el diseño

Mecanismo de transporte de referencia si hiciera falta inyección JS/CSS propia: un `IStartupFilter` propio del nuevo plugin, calcado de `ScriptInjectionStartupFilter.cs`, sirviendo JS/CSS vía un controlador de assets embebidos calcado de `WebAssetsController.cs`. Ver más abajo por qué, a la luz del hallazgo sobre `loadSections.js`, esto pasa a ser **fallback** y no la vía principal.

---

## Fuente 3 — Jellyfin Enhanced (vendorizado en `Jellyfin-Enhanced/`)

- **Nombre**: Jellyfin Enhanced (`n00bcodr/Jellyfin-Enhanced`)
- **Ruta local**: `/home/alvaro/Descargas/jellyfinnotify/JellyNotify/Jellyfin-Enhanced/`
- **Versión (según `manifest.json` vendorizado)**: `11.12.0.0`, `targetAbi: 10.11.0.0`
- **Fecha de consulta**: 2026-07-28
- **Licencia**: GPL-3.0 (JellyNotify la cita como tal en su atribución; confirmado también por el propio README del repo vendorizado)
- **Archivos relevantes**:
  - `Jellyfin.Plugin.JellyfinEnhanced/Services/ScriptInjectionStartupFilter.cs` (el original del que JellyNotify es adaptación)
  - `Jellyfin.Plugin.JellyfinEnhanced/Services/BrandingAssetStartupFilter.cs`
  - `Jellyfin.Plugin.JellyfinEnhanced/js/jellyseerr/network-discovery.js`
  - `Jellyfin.Plugin.JellyfinEnhanced/js/jellyseerr/discovery-filter-utils.js`
  - `Jellyfin.Plugin.JellyfinEnhanced/js/jellyseerr/hss-discovery-handler.js`
  - `Jellyfin.Plugin.JellyfinEnhanced/js/elsewhere/elsewhere.js`
  - `Jellyfin.Plugin.JellyfinEnhanced/js/jellyseerr/ui.js`
  - `Jellyfin.Plugin.JellyfinEnhanced/PluginPages/` (4 páginas HTML embebidas)

### Hallazgos

**1. `BrandingAssetStartupFilter.cs` — segunda instancia real del mismo patrón `IStartupFilter`, interceptando assets estáticos en vez de HTML.** Intercepta peticiones a imágenes de marca (`icon-transparent.<hash>.png`, `banner-light.<hash>.png`, `touchicon<size>.<hash>.png`, etc.) por **patrón regex sobre el nombre base**, no por ruta exacta, porque el hash de contenido de webpack cambia en cada build de `jellyfin-web` (comentario líneas 43-55). Si existe un fichero personalizado subido por el admin, corta la petición y sirve esos bytes directamente con `ETag`/`Last-Modified`/soporte `304` propio (líneas 79-177); si no, deja pasar sin cambios. Confirma que interceptar **por patrón de nombre, no por ruta fija**, ya se resolvió aquí para assets estáticos.

**2. `hss-discovery-handler.js` — ÚNICA mención real a "Home Screen Sections" (HSS) encontrada en todo el código auditado (JellyBridge, JellyNotify, Jellyfin Enhanced).** 47 líneas: un listener de `click` a nivel de `document` que intercepta clicks sobre `.discover-card` (excepto `.discover-requestbutton`) y, si tiene `data-tmdb-id`/`data-media-type`, abre el modal "more info" de Jellyseerr en vez de dejar navegar el link nativo. **No crea, no estiliza ni pinta logos junto a títulos de sección** — solo manejador de click sobre tarjetas ya renderizadas. Confirma que Jellyfin Enhanced conoce y coexiste con HSS a nivel de click-handling, pero no interviene en el renderizado de sus títulos.

**3. `network-discovery.js` — creación de secciones sintéticas completas inyectadas por JS, con título de texto plano (sin logo).** `createSectionContainer()` (líneas 321-345) construye a mano un `<div class="verticalSection jellyseerr-network-discovery-section ...">` con `data-jellyseerr-network-discovery="true"` y delega en `discovery-filter-utils.js::createSectionHeader()` (líneas 314-336):
```js
const titleElement = document.createElement('h2');
titleElement.className = 'sectionTitle sectionTitle-cards';
titleElement.textContent = title;
titleElement.style.margin = '0';
header.appendChild(titleElement);
```
**Es texto plano (`textContent`), sin ningún `<img>` ni `background-image` junto al título** — evidencia negativa confirmada por grep: ni `network-discovery.js` ni `discovery-filter-utils.js` insertan logo de red/proveedor junto al `<h2>`, pese a trabajar con nombres de redes/estudios y tener acceso a IDs de TMDb Company/Network.

**4. El patrón `<img>` + `logo_path` de TMDb SÍ existe en el código — pero para *badges* de "disponible en", no para títulos de sección.** `elsewhere.js`, función `createServiceBadge()` (líneas 451-489):
```js
const logo = document.createElement('img');
logo.src = `https://image.tmdb.org/t/p/w92${service.logo_path}`;
logo.alt = service.provider_name;
logo.style.cssText = `width: 20px; height: 20px; margin-right: 8px; object-fit: contain; border-radius: 4px;`;
logo.onerror = () => logo.style.display = 'none';
badge.appendChild(logo);
```
seguido de un `<span>` con `service.provider_name`. El mismo patrón (`img.src = https://image.tmdb.org/t/p/w92${provider.logo_path}`) aparece también en `ui.js` línea 1424. Es el precedente más directamente reutilizable encontrado en todo el código auditado para la técnica de **JS imperativo**: creación de `<img>` con URL de TMDb, `onerror` para ocultar el hueco si falla, colocado inmediatamente antes de un `<span>` de texto.

### Limitaciones

- Ningún fichero de Jellyfin Enhanced ni de JellyBridge resuelve el caso concreto "logo junto al `<h2>`/título de una fila de Home Screen Sections" — el patrón `<img>+logo_path` existe pero nunca se aplicó a ese contexto exacto en ninguno de los proyectos auditados.
- `network-discovery.js` no usa Home Screen Sections en absoluto: crea secciones sintéticas propias insertadas por JS en páginas de detalle de estudio/red, no filas registradas como `IPluginHomeScreenSection` — es una tercera arquitectura distinta ("sección DOM inyectada a mano en una página existente"), ni "biblioteca real" (JellyBridge) ni "fila de HSS nativa con HTML propio" (ver hallazgo de `research-hss` más abajo).

### Riesgos

- Los selectores usados (`.sectionTitle.sectionTitle-cards`, `.verticalSection`) son clases genéricas de Jellyfin Web core (no de HSS), por lo que su estabilidad depende de Jellyfin Web, no de un plugin de terceros — relativamente más estables por ser clases estructurales usadas en muchos lugares de la SPA.

### Nivel de confianza

**Alto.** Código leído directamente; los `grep` de `logo_path`/`provider-logo`/`network-logo` fueron exhaustivos sobre todo el árbol vendorizado.

### Impacto en el diseño

Confirma dos cosas de peso: (1) el mecanismo `IStartupFilter` se ha usado dos veces en producción real en este ecosistema (JellyNotify y Jellyfin Enhanced), con las mismas salvaguardas; (2) la técnica de renderizado del logo en sí (`document.createElement('img')` con `src` de TMDb `w92` + `onerror` de fallback) ya funciona en producción para badges de disponibilidad, y es trasladable como **fallback JS** si la vía principal (ver siguiente sección) dejara de funcionar.

---

## Home Screen Sections — hallazgo del agente `research-hss` (reproducido íntegro, no investigado por este agente)

**IMPORTANTE — esto es superior a las tres alternativas a/b/c de transporte JS/CSS analizadas arriba y pasa a ser la recomendación principal.** Ver el análisis completo, con número de línea exacto, en `04-home-screen-sections-integration.md` sección 3. Resumen ejecutivo:

- **Fuente:** `src/Jellyfin.Plugin.HomeScreenSections/Controllers/loadSections.js` (repo `jellyfin-plugin-home-sections`, tag `2.5.11.0`, commit `3b02d90e3c405d63181127fb31d0266a0192525b`, consultado 2026-07-28).
- **Hallazgo:** el título de cada sección se construye con `html += sectionInfo.DisplayText` (líneas 295 y 301) y se asigna con `elem.innerHTML = html` (línea 320) — **sin escapar HTML en ningún punto**. `sectionInfo.DisplayText` es exactamente el campo `displayText` que nuestro plugin envía al registrar la sección.
- **Consecuencia:** nuestro plugin puede fijar `displayText = '<img src="/ProviderSections/Logo/{id}" alt="" class="provider-section-logo" /><span>Popular en Crunchyroll</span>'` y HSS lo renderiza tal cual, **sin ningún script, `MutationObserver` ni `IStartupFilter` propio**. Es más robusta que la inyección frontend: no depende de vigilar el DOM en carrera contra el propio renderizado de HSS, no depende de selectores CSS de Jellyfin Web que puedan cambiar, y sobrevive a cualquier forma en que HSS repinte la fila (scroll infinito, cambio de página) porque el logo **es parte del HTML que HSS genera**, no algo insertado después por fuera.
- **Riesgo específico no cubierto por la inyección frontend:** traducción automática vía LibreTranslate (si el admin la activa en la configuración de HSS) puede mutilar el HTML del `displayText` — ver detalle en `04-home-screen-sections-integration.md`.
- **Riesgo de mantenimiento de esta vía:** si una versión futura de HSS empieza a escapar `DisplayText` (por ejemplo, por una corrección de seguridad ante el propio hallazgo de que es una vía de XSS con entradas no confiables — ver `10-security-and-licensing.md`), esta técnica dejaría de funcionar de un día para otro sin previo aviso, al no ser un contrato documentado ni estable.

---

## Evaluación comparativa de las alternativas del encargo (a/b/c) + la vía superior encontrada por `research-hss`

### a) Reutilizar `ScriptInjectionStartupFilter.cs` de JellyNotify (inyección de JS/CSS propios que localizan el título por texto/identificador estable y anteponen un `<img>`)

**Viable, pero pasa a ser la solución de *fallback*, no la principal.** Es la única de las tres fuentes de transporte estudiadas que resuelve el problema de "llevar JS/CSS propios a Jellyfin Web sin fork" con un patrón ya probado en producción dos veces en este ecosistema (JellyNotify y su origen, Jellyfin Enhanced). El patrón de renderizado del `<img>` en sí (con `onerror` de fallback) ya existe y funciona en producción en Jellyfin Enhanced (`elsewhere.js`, `ui.js`), y es directamente trasladable. Se degrada a fallback porque el hallazgo de `research-hss` permite el mismo resultado visual sin necesidad de este transporte en absoluto — ver siguiente apartado.

### b) El patrón de JellyBridge

**No aplica / no es una alternativa real.** JellyBridge no usa Home Screen Sections, no inyecta JS/CSS, y el campo `LogoPath` que captura de la API de Jellyseerr nunca se renderiza en ningún sitio. Es arquitectónicamente una categoría de solución distinta (biblioteca sintética con vídeos placeholder reales) que además tiene un problema documentado de fugas hacia Home Screen Sections (issue #13). Se descarta por completo para este requisito.

### c) El patrón de Jellyfin Enhanced

**Mismo mecanismo de transporte que (a) — de hecho es su origen —, pero no aporta un patrón de "logo junto a título de fila HSS" independiente.** Cuando construye secciones propias (`network-discovery.js`), inserta secciones DOM completas a mano en páginas de detalle existentes, con título de texto plano sin logo, y coexiste con HSS solo a nivel de click-handling (`hss-discovery-handler.js`), nunca tocando el renderizado de sus títulos. Su aporte real es la pieza de renderizado del `<img>` en sí (`elsewhere.js`), útil como parte del fallback (a), no como alternativa (c) independiente.

### d) Vía superior encontrada en paralelo: `displayText` como HTML en el propio registro de la sección ante HSS

Ver sección anterior. **Es la recomendación principal**: cero dependencias nuevas, cero JS de cliente propio, cero riesgo de carrera con el renderizado de HSS, cero acoplamiento a selectores CSS de Jellyfin Web.

### Recomendación final reconciliada

1. **Solución principal:** `displayText` como HTML (`<img>` + `<span>`) al registrar la sección ante Home Screen Sections — cero dependencias nuevas, cero JS de cliente propio, cero riesgo de carrera con el renderizado de HSS. Implementar primero.
2. **Solución de fallback, ya diseñada y lista si la principal deja de funcionar en una versión futura de HSS:** la inyección frontend propia vía `IStartupFilter` + `MutationObserver` (alternativa a/c), reutilizando el patrón ya probado en producción por `ScriptInjectionStartupFilter.cs` de JellyNotify y `BrandingAssetStartupFilter.cs` de Jellyfin Enhanced, con el `<img>`+`onerror` calcado de `elsewhere.js`.
3. **Estrategia de prueba visual:** cubrir ambas vías en las capturas E2E — no basta con probar la vía principal; el plan de pruebas debe incluir una comprobación periódica (manual o Playwright) de que `displayText` sigue renderizándose sin escapar tras cada actualización del plugin Home Screen Sections en el entorno aislado.

---

## Riesgo de acoplamiento al DOM/estructura de Jellyfin Web y mitigación

Aplica sobre todo a la **solución de fallback** (b/(a) más arriba), ya que la solución principal (`displayText` como HTML) no depende de localizar nada en el DOM — el logo nace ya dentro del HTML que genera HSS.

### Naturaleza del riesgo (fallback)

Si algún día hay que activar el fallback de inyección JS, localizar en el DOM ya renderizado por Home Screen Sections el nodo de texto del título de cada fila para insertar el logo a su izquierda tiene estos problemas, confirmados por el código auditado:

- Las clases CSS observadas (`sectionTitle`, `sectionTitle-cards`, `verticalSection`, `.MuiAppBar-root`, `.MuiToolbar-root`) son **estructurales genéricas de Jellyfin Web**, no exclusivas de Home Screen Sections — un selector como `.sectionTitle` matchea el título de docenas de secciones distintas en toda la home, no solo las registradas por el plugin de secciones de proveedor. Sin un identificador adicional, el script no puede distinguir "esta fila es la sección de Crunchyroll que registré yo" de "esta es la fila nativa de Continue Watching".
- El texto del título (`textContent`) es la señal más frágil: cambia con el idioma del usuario, puede coincidir por casualidad con otro texto, y no es un contrato estable entre versiones.
- Ninguna de las tres fuentes auditadas (JellyBridge, JellyNotify, Jellyfin Enhanced) expone en el DOM un atributo `data-*` que identifique de forma estable qué definición de sección (UUID/ruta) generó una fila concreta — no se encontró ningún `data-section-id`, `data-hss-id` ni equivalente en ningún fichero JS/CSS auditado.

### Mitigación recomendada para el fallback

1. **No anclar por texto del título.** Ni por contenido (rompe con idioma/orden) ni por clase CSS genérica en solitario.
2. **Anclar por el identificador más estable disponible**: si HSS expone algún `data-*`/`id`/atributo de ruta reconocible en el contenedor de la fila, usarlo (a confirmar en el DOM real, no asumir sin verificar). Si no existe, marcar la fila la primera vez que el propio script la detecta (`element.dataset.providerSectionId = 'crunchyroll-es'`, escrito por el script, no leído de HSS) para no tener que re-identificarla en cada callback del observer.
3. **`MutationObserver`** (patrón ya en producción en `jellynotify.js`, con guarda para no registrar dos veces el observer) para detectar cuándo HSS (re)renderiza sus filas — necesario porque el DOM se recrea en scroll infinito, cambio de pestaña o refresco de sesión.
4. **Selectores en cadena con fallback**, igual que ya hace `jellynotify.js` (p. ej. `'.headerRight, .skinHeader-withBackground .headerRight, header .headerRight'`) en vez de un único selector rígido.
5. **`onerror` en el `<img>` del logo** (patrón ya usado en `elsewhere.js`: `logo.onerror = () => logo.style.display = 'none'`) para que un logo caído o una URL de TMDb rota no deje un hueco roto junto al título.
6. **Aislar todo el acoplamiento al DOM en un módulo JS pequeño y autocontenido**, para que una rotura por actualización de Jellyfin Web o de HSS sea rápida de localizar y corregir sin tocar el resto del plugin.
7. **No dar el requisito por resuelto sin prueba visual real** en el entorno aislado, confirmando: tema claro/oscuro, fallback sin logo, persistencia tras reinicio completo de Jellyfin, y que el logo (vía principal o vía fallback) se ancla a la fila correcta y no a otra tras un scroll/recarga que regenere el DOM.

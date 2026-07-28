# 10 — Seguridad, privacidad y licencias

Fecha de consulta: 2026-07-28. Compilado por el agente principal a partir de los hallazgos de licencia reportados por cada sub-informe (`04`, `05`, `06`, `07`, `08`) más verificación directa (`gh repo view ... --json licenseInfo`) y análisis de seguridad propio.

## 1. Matriz de licencias

| Repositorio | Licencia (confirmada) | Fuente de la confirmación | Implicación para el nuevo plugin |
|---|---|---|---|
| `jellyfin/jellyfin` | **GPL-2.0** | `gh repo view` + `LICENSE` del repo clonado (`07-jellyfin-library-resolution.md`) | Se consume solo vía `PackageReference` NuGet (`Jellyfin.Controller`/`Jellyfin.Model`), igual que JellyNotify — sin conflicto. Copiar fragmentos de código fuente literal del server sí generaría un problema de compatibilidad GPL-2.0 ↔ GPL-3.0-or-later — **no hacerlo**. |
| `jellyfin/jellyfin-web` | **GPL-2.0** | `gh repo view` (verificado directamente por el agente principal) | No se toca su código fuente en ningún escenario del diseño (ni la solución principal de logo — `displayText` HTML — ni el fallback de inyección, que solo añade JS/CSS propios en tiempo de ejecución sin modificar ni redistribuir el repo). |
| `jellyfin/jellyfin-plugin-template` | **GPL-3.0** | `gh repo view` (`07-jellyfin-library-resolution.md`) | Compatible con GPL-3.0-or-later. Usar solo como referencia estructural (ver `07`), no copiar su `.csproj`/`build.yaml` desactualizados tal cual. |
| `jellyfin-plugin-home-sections` | **GPL-3.0** | `LICENSE` del repo clonado (`04-home-screen-sections-integration.md`) | Integración exclusivamente en tiempo de ejecución por reflexión sobre `JObject` — cero `PackageReference`, cero código copiado. Sin conflicto de licencia porque no hay enlace ni copia. |
| `jellyfin-plugin-collection-sections` | **GPL-3.0** | Ídem | Usado solo como referencia de patrón arquitectónico (no se copia código; se reimplementa el patrón con nombres/estructura propios). |
| `jellyfin-plugin-file-transformation` | **GPL-3.0** | Ídem | Dependencia transitiva vía HSS; no se referencia ni se copia código. |
| `jellyfin-plugin-pages` | **GPL-3.0** | Ídem | No se usa en el MVP (ver `04` §5); sin implicación de licencia. |
| `Jellyfin Enhanced` (n00bcodr) | **GPL-3.0** | Confirmado por el propio `NOTICE.md` de JellyNotify y por lectura directa del repo vendorizado (`08-provider-logo-rendering.md`) | Si el fallback de inyección JS/CSS se implementa adaptando patrones de `ScriptInjectionStartupFilter`/`BrandingAssetStartupFilter`, el nuevo plugin **debe** licenciarse GPL-3.0 (o "or-later" compatible) y llevar su propio `NOTICE.md` con atribución explícita, exactamente como ya hace JellyNotify — no es opcional, es una obligación de copyleft si se adapta código real, no solo el patrón conceptual. |
| `JellyBridge` (kinggeorges12) | **GPL-3.0** | `08-provider-logo-rendering.md` | Descartado como base de código (arquitectura distinta, riesgo de acoplamiento a un fork parcheado de Jellyseerr) — no se copia nada; se cita solo como referencia conceptual descartada. |
| `seerr-team/seerr` | **MIT** | `LICENSE` + `package.json` del repo clonado (`06-seerr-api-analysis.md`) | Solo se consume su API HTTP pública — sin problema de licencia en ningún escenario (MIT permite incluso copia literal, pero no se prevé copiar código, solo replicar el contrato HTTP). |
| `JellyNotify` (proyecto de referencia local) | **GPL-3.0-or-later** | `LICENSE`/`NOTICE.md` del repo local | El nuevo plugin, si convive en el mismo repositorio como proyecto hermano, **puede** adoptar la misma licencia por consistencia (recomendado), aunque no está obligado a menos que reutilice código literal de JellyNotify (en cuyo caso sí sería obligatorio, igual que con Jellyfin Enhanced). |
| TMDb API (datos, no código) | No es una licencia de software — **Términos de Uso de la API de TMDb** | `05-tmdb-provider-analysis.md` | Exige atribución obligatoria de TMDb **y** de JustWatch (los datos de `watch/providers` están licenciados de JustWatch) allí donde se muestre disponibilidad por proveedor. Redacción exacta pendiente de verificar contra la página de términos vigente en el momento de implementar (no inventada aquí). |

**Recomendación de licencia para el nuevo plugin**: **GPL-3.0-or-later**, por consistencia con JellyNotify y porque, si se implementa el fallback de inyección frontend (patrón `IStartupFilter`), habrá una dependencia de copyleft real hacia Jellyfin Enhanced/JellyNotify que lo exige. Si finalmente el fallback nunca se activa/implementa y no se copia ni una línea de código GPL de terceros, una licencia MIT sería técnicamente posible — pero se recomienda fijar GPL-3.0-or-later desde el inicio para evitar tener que re-licenciar más adelante si se decide implementar el fallback.

## 2. Seguridad — hallazgos consolidados

### 2.1 Almacenamiento y enmascaramiento de secretos

- Dos secretos de backend: **TMDb API Read Access Token** (Bearer) y **Seerr API Key**. Ambos deben seguir el patrón ya verificado en producción en `PluginConfiguration.PreserveSecrets` de JellyNotify (`02-local-project-analysis.md` §3): el frontend nunca recibe el valor real (se sirve vacío/enmascarado en `GET /Admin/config`), y al guardar, un campo vacío entrante conserva el valor persistido en vez de borrarlo.
- Ninguno de los dos debe aparecer jamás en `localStorage`, en las respuestas de endpoints no-admin, en logs, ni en las capturas/evidencias del entorno de pruebas (ver `21-pruebas-visuales` del plan: cualquier captura de la página de configuración debe hacerse con los campos de secreto vacíos o con un valor de prueba explícitamente ficticio, nunca con una clave real visible).

### 2.2 Vector de HTML no escapado en `displayText` (hallazgo de `04`/`08`) — análisis de riesgo propio

Home Screen Sections renderiza `displayText` como `innerHTML` sin escapar (confirmado por código real, ver `08-provider-logo-rendering.md` línea 226). Esto es lo que permite la solución de logo recomendada, pero introduce una responsabilidad de seguridad que recae **enteramente en nuestro propio plugin**, no en HSS:

- El `displayText` que construimos server-side combina: (a) HTML literal controlado por nosotros (`<img src="..." class="...">`), y (b) el **nombre visible de la sección**, que es un campo de texto libre introducido por el administrador en el formulario de creación/edición.
- **Riesgo concreto**: si el nombre de sección se interpola sin escapar HTML dentro del `displayText`, un administrador (o cualquier proceso con acceso a la API de administración del plugin) podría inyectar HTML/JS arbitrario que se ejecutaría en el contexto de Jellyfin Web de **cualquier usuario** que vea la página principal — una self-XSS que en la práctica es un XSS persistente contra todos los usuarios del servidor, no solo contra el propio admin, porque `displayText` se sirve globalmente a través de HSS.
- **Mitigación obligatoria, no opcional**: HTML-encodear (`HttpUtility.HtmlEncode` o equivalente) el nombre de la sección antes de interpolarlo en el `displayText`, dejando como HTML literal *únicamente* la etiqueta `<img>` del logo (con `src` construida internamente a partir del `provider_id` verificado contra TMDb, nunca a partir de una URL arbitraria proporcionada por el admin) y el contenedor `<span>` del texto ya escapado. Este es un hallazgo de diseño de seguridad **nuevo, no anticipado explícitamente en el encargo original**, y debe tratarse como criterio de aceptación obligatorio (ver `15-acceptance-criteria.md` cuando se redacte el plan).
- La URL del logo (`src`) debe construirse siempre server-side a partir de `{secure_base_url}{size}{logo_path}` con `logo_path` obtenido de la respuesta cacheada de `/watch/providers/movie|tv` de TMDb — **nunca** aceptar una URL de logo arbitraria introducida por el admin como texto libre, para no abrir una vía adicional de XSS ni de referencia a contenido no confiable.

### 2.3 Endpoint HTTP de registro de HSS sin autenticación

`POST /HomeScreen/RegisterSection` no tiene `[Authorize]` (hallazgo de `04`). Decisión de diseño ya tomada en la investigación: el nuevo plugin usa exclusivamente la vía in-process por reflexión (`AssemblyLoadContext.All` → `PluginInterface.RegisterSection`), nunca la vía HTTP, precisamente para no depender de ni exponer esa superficie. Esto no es un riesgo que introduzca el nuevo plugin, pero si en algún momento se documenta la vía HTTP como alternativa, debe llevar una advertencia explícita de que no debe exponerse fuera de `localhost`.

### 2.4 SSRF y validación de URLs

- El plugin acepta dos URLs configuradas por el administrador: la URL de Seerr (ya un patrón aceptado en JellyNotify, `SeerrSettings.ServerUrl`) y, para TMDb, no hay URL configurable — el endpoint base es fijo (`api.themoviedb.org`/`image.tmdb.org`), lo que **elimina** el vector SSRF para TMDb por diseño.
- Para Seerr, el riesgo de SSRF es el mismo ya aceptado implícitamente por JellyNotify (un admin malicioso podría apuntar `ServerUrl` a un recurso interno de red) — fuera del modelo de amenaza habitual de un plugin autoadministrado (el admin ya tiene control total del servidor), pero se recomienda igualmente: validar que el esquema sea `http`/`https` (rechazar otros esquemas), no seguir redirecciones automáticas a esquemas distintos, y aplicar un timeout explícito en el `HttpClient` (patrón ya existente en `SeerrApiClient.cs`).
- El logo del proveedor y el poster/backdrop de TMDb se sirven siempre desde el dominio fijo `image.tmdb.org` — no hay URL de imagen arbitraria proporcionada por el usuario final en ningún punto del flujo.

### 2.5 TLS, timeouts, reintentos

- `IgnoreSslErrors` ya existe como patrón de configuración opcional en `SeerrSettings`/`ArrInstanceConfig` de JellyNotify (para instancias con certificados autofirmados en red local) — replicar el mismo patrón para Seerr en el nuevo plugin si se reutiliza el cliente; TMDb siempre usa TLS público estándar (sin necesidad de esa opción).
- Timeouts explícitos y backoff ante `429` para TMDb (ver `05-tmdb-provider-analysis.md` §Rate limiting) — sin límite fijo publicado por TMDb, tratar de forma defensiva.

### 2.6 Autorización de endpoints propios

- Endpoints de administración de secciones (crear/editar/duplicar/activar/desactivar/eliminar/probar consulta/previsualizar/limpiar caché) — **solo admin**, mismo patrón `[Authorize(Policy = "RequiresElevation")]` (o equivalente real usado por JellyNotify en `AdminController`) que ya existe en el proyecto de referencia.
- Endpoint(s) que sirven el logo cacheado de un proveedor (consumido por Jellyfin Web al renderizar `displayText`) — deben ser **no autenticados** (igual que `WebAssetsController` de JellyNotify), porque se cargan como `<img src>` normal del navegador sin cabeceras de sesión; no exponen secretos, solo imágenes públicas ya cacheadas desde TMDb.
- Endpoint de "solicitar" (crea una solicitud en Seerr en nombre del usuario) — **requiere sesión de usuario Jellyfin autenticado** (no admin), y debe resolver la identidad Seerr del usuario que hace la llamada (vía `X-API-User`, ver `06`), nunca aceptar un `userId`/`jellyfinUserId` arbitrario en el payload del cliente sin cruzarlo contra el usuario de la sesión autenticada — de lo contrario un usuario podría solicitar contenido en nombre de otro.

### 2.7 Separación admin/usuario y permisos de biblioteca

- Ya cubierto en profundidad en `07-jellyfin-library-resolution.md` §4 (Capa A: `InternalItemsQuery(user)` + Capa B: `item.IsVisible(user)` como defensa en profundidad). Reafirmado aquí como requisito de seguridad, no solo funcional: un usuario nunca debe poder inferir, a través de una sección de proveedor, la existencia de contenido en una biblioteca a la que no tiene acceso.

### 2.8 Diagnósticos sin secretos

- Igual que `GET /Admin/diagnostics` de JellyNotify ya hace (estado de inyección web, salud de sync, sin exponer claves), el panel de diagnóstico del nuevo plugin (estado TMDb/Seerr/HSS, última sincronización, último error) debe mostrar únicamente booleanos/timestamps/mensajes de error saneados — nunca la cabecera `Authorization` completa ni la API key en un mensaje de error de HTTP crudo (sanear cualquier excepción de `HttpClient` antes de mostrarla en la UI o guardarla en `LastSyncError`).

### 2.9 CSRF

- Todos los endpoints de administración mutan estado vía la sesión/cookie de Jellyfin ya autenticada por el propio framework de plugins — mismo mecanismo de protección que ya usa el resto de la superficie admin de Jellyfin (fuera del control directo del plugin; no se ha identificado necesidad de un mecanismo CSRF adicional propio, mismo criterio que JellyNotify ya aplica implícitamente).

### 2.10 Buenas prácticas de repositorio

- `.env.example` (sin valores reales) para cualquier variable de entorno usada por el entorno de pruebas Docker (ver `10-testing-environment.md` cuando se redacte el plan) — nunca un `.env` real commiteado.
- Ningún token TMDb/Seerr real en los ficheros de prueba de integración (usar un servidor HTTP simulado, ver sección 19 del encargo original) ni en las evidencias/capturas versionadas del entorno de pruebas.

## 3. Limitaciones de este documento

- La redacción exacta de la atribución obligatoria de TMDb/JustWatch no se ha verificado literal contra la página de términos vigente (ver `05`) — pendiente de una verificación puntual final antes de cerrar la pantalla de "Acerca de" en el plan de implementación, no bloqueante para la arquitectura.
- El hallazgo de la sección 2.2 (XSS vía `displayText`) es un análisis de riesgo derivado de código real leído por otros sub-informes, no una prueba de penetración ejecutada — debe verificarse en el entorno de pruebas real (intentar registrar una sección con un nombre que contenga `<script>` y confirmar que se renderiza escapado) antes de dar el requisito de seguridad por cerrado.

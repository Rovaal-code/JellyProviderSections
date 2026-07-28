# 11 — Matriz de pruebas

Derivado íntegramente de `docs/research/01-11` y `research-summary.md` (investigación ya cerrada, READY WITH ASSUMPTIONS). No se reinvestiga nada aquí; cada fila cita el documento de investigación que la sustenta. Decisiones ya cerradas asumidas: repositorio de código separado, Seerr 3.4.0+, 4K en MVP, series por defecto "todas las temporadas" con selección interactiva secundaria, sin modo mixto película/serie.

## 1. Pruebas unitarias

| # | Componente | Qué verifica | Fuente de investigación | Evidencia |
|---|---|---|---|---|
| U1 | Motor de consultas TMDb | Construcción de query string `discover/movie` y `discover/tv` a partir de `SectionDefinition` — `with_watch_providers`, `watch_region`, `with_genres`/`without_genres`, `with_original_language`, `with_origin_country`, rangos de fecha (`primary_release_date.*` vs `first_air_date.*`), `vote_average.gte`, `vote_count.gte` con default no vacío, `include_adult`. Confirma explícitamente que `with_watch_monetization_types` nunca aparece en ninguna query generada. | `05-tmdb-provider-analysis.md` §Discover | Test automatizado (assert sobre query string generada) |
| U2 | Traducción de `SortBy` lógico → parámetro real | El enum propio (`Popularity`, `RatingDesc`, `ReleaseDateDesc`, `TitleAsc`) se traduce a `popularity.desc`/`vote_average.desc`/`primary_release_date.desc` (movie) vs `first_air_date.desc`/`name.asc` (tv) — nombres distintos por tipo de contenido. | `05` §Discover, "Diferencia relevante para el diseño" | Test automatizado, un caso por combinación SortBy×ContentType |
| U3 | Regiones y proveedores | Deserialización de `/watch/providers/regions`, `/watch/providers/movie`, `/watch/providers/tv`; filtrado de proveedores por `ContentType` antes de mostrarlos en el selector. | `05` §Watch Providers | Test automatizado con fixtures JSON |
| U4 | Construcción de URL de logo/imagen | `{secure_base_url}{size}{logo_path}` con tamaño pequeño (`w45`/`w92`) para el logo de sección. | `05` §Configuración de imágenes | Test automatizado |
| U5 | Dedupe y paginación | Motor que pagina `discover` hasta alcanzar `maxItems` o `total_pages`, deduplicando por `id` TMDb entre páginas. | `05` §Paginación | Test automatizado con fixtures multi-página |
| U6 | Resolución de identificadores (Jellyfin) | Construcción de `InternalItemsQuery` con `HasAnyProviderId = {"Tmdb": id}` + `IncludeItemTypes` correcto para Movie/Series; uso de `ProviderIdsExtensions.TryGetProviderId`, nunca acceso directo al diccionario. | `07-jellyfin-library-resolution.md` §1, §3 | Test automatizado (mock de `ILibraryManager`) |
| U7 | Migración de configuración | Esquema versionado de `PluginConfiguration` — actualización de una configuración v1 a v2 sin pérdida de `SectionDefinition.Id` (UUID inmutable). | `04-home-screen-sections-integration.md` §2 (UUID debe sobrevivir a ediciones) | Test automatizado |
| U8 | Caché (TTL por capa) | Expiración independiente para `/configuration`, `/watch/providers/*`, resultados `discover` por sección — cada uno con TTL propio. | `05` §Impacto en el diseño | Test automatizado con reloj simulado |
| U9 | Timeouts/reintentos TMDb | Backoff ante 429 respetando `Retry-After` si está presente; sin asumir límite fijo. | `05` §Rate limiting | Test automatizado (mock de respuestas 429 sucesivas) |
| U10 | Estados de Seerr (enums corregidos) | `MediaRequestStatus` con 5 valores (incluye `Completed=5`); `MediaStatus` con `Blocklisted=6`/`Deleted=7` en el orden correcto (invertido respecto al modelo local heredado de JellyNotify); `SeerrMediaSeasonStatus` con `status`+`status4k`. | `06-seerr-api-analysis.md` §Estados reales, §Comparación con cliente local | Test automatizado — regresión explícita del bug de enum invertido |
| U11 | Permisos de solicitud | Payload `CreateRequestAsync` construido con `seasons: "all"` por defecto, soporte de `seasons: number[]` para selección interactiva, `is4k`, sin `ignoreQuota` salvo bandera admin explícita. | `06` §Endpoint de creación de solicitudes | Test automatizado |
| U12 | Sanitización de logs | Ningún log ni excepción saneada contiene el valor de `Authorization`/API key de TMDb o Seerr. | `10-security-and-licensing.md` §2.8 | Test automatizado (assert sobre formateo de excepciones) |
| U13 | Fallback de logotipos | Si `logo_path` es null/la imagen falla, el `<img>` generado en `displayText` incluye `onerror`/atributo de fallback y no rompe el HTML. | `08-provider-logo-rendering.md` (patrón `elsewhere.js`) | Test automatizado |
| U14 | UUID persistente | Generación de `SectionDefinition.Id` server-side (GUID real), inmutable ante ediciones — rechazo explícito de cualquier intento de cambiarlo vía API de edición. | `04` §2 ("mejora directa sobre el precedente de collection-sections") | Test automatizado |
| U15 | **Escape de HTML en `displayText`** | El nombre de sección (texto libre del admin) se HTML-encodea antes de interpolarse en el `displayText` enviado a HSS; solo el `<img>`/`<span>` construidos internamente quedan como HTML literal. | `10` §2.2 (hallazgo de seguridad nuevo) | Test automatizado — caso explícito con nombre `<script>alert(1)</script>` |
| U16 | Registro/baja de sección (in-process) | El `HomeSectionsRegistrar` construye el `JObject` correcto para `PluginInterface.RegisterSection`; comportamiento cuando el tipo/método no se encuentra por reflexión (log + `return`, sin excepción). | `04` §1, §6 | Test automatizado (mock del ensamblado HSS ausente) |

## 2. Pruebas de integración (servidor HTTP simulado)

TMDb y Seerr se simulan con un servidor HTTP en memoria (p. ej. `WireMock.Net` o `HttpMessageHandler` de prueba) — no se llama a la API real.

| # | Escenario | Verifica | Fuente | Evidencia |
|---|---|---|---|---|
| I1 | TMDb 200 con página parcial | Motor de consultas para de paginar en `total_pages`, no intenta página `total_pages+1`. | `05` §Paginación | Log + assert de nº de llamadas HTTP |
| I2 | TMDb 401 (token inválido) | Diagnóstico de sección marca "TMDb: token inválido", no crashea el resto del sync. | `05` §Autenticación, `11-open-questions...` (token pendiente de usuario) | Respuesta HTTP saneada + log |
| I3 | TMDb 404 (región/proveedor inexistente) | Se distingue de "0 resultados legítimos" (200 con `total_results:0`). | `05` §Inconsistencias región/proveedor | Log |
| I4 | TMDb 429 | Backoff con `Retry-After`, reintento único acotado, no bucle infinito. | `05` §Rate limiting | Log + medición de tiempo de reintento |
| I5 | TMDb 500 / timeout / respuesta incompleta (JSON truncado) | Sección queda en estado "última sincronización: error", sección anterior cacheada se sigue sirviendo (stale-while-revalidate). | `05` §Riesgos; encargo original §15 | Log + captura del estado de diagnóstico |
| I6 | Seerr 403 (cuota/permiso agotado del usuario objetivo) | La UI de "Solicitar" muestra el motivo real, no un error genérico; confirma que la API key admin **no** ignora la cuota del usuario. | `06` §Atribución de usuario, §Riesgos | Respuesta HTTP saneada |
| I7 | Seerr 409 (ya solicitado) | Tratado como "ya en curso", no como error duro — botón pasa a estado "Pendiente"/"Ya solicitado". | `06` §Idempotencia | Log + captura de UI |
| I8 | Seerr 202 (`NoSeasonsAvailableError`, todas las temporadas ya cubiertas) | Tratado como éxito silencioso ("nada nuevo que pedir"), no como error. | `06` §Idempotencia, §Series parcialmente disponibles | Log |
| I9 | Seerr 403 (`BlocklistedMediaError`) | Contenido en blocklist no ofrece botón de solicitud o muestra motivo explícito. | `06` §Idempotencia | Captura de UI |
| I10 | Seerr 401 / API key inválida | Diagnóstico "Seerr: credenciales inválidas". | `06` (patrón ya en `SeerrApiClient` real) | Respuesta HTTP saneada |
| I11 | Seerr timeout / 500 | Botón de solicitud se degrada a deshabilitado con mensaje, resto de la sección (catálogo TMDb) sigue funcionando — Seerr caído no debe tumbar la sección. | Encargo original §15, §10 (Riesgos) | Log + captura |
| I12 | `X-API-User` vs `userId` en body | Test de integración que confirma que usar `X-API-User` aplica permisos/cuota/auto-aprobación del usuario real, mientras que `userId` en el body auto-aprobaría siempre bajo la identidad del admin — regresión explícita del matiz documentado. | `06` §Atribución de usuario (hallazgo no obvio) | Test automatizado contra el mock, dos escenarios paralelos |
| I13 | Resultado TMDb duplicado entre páginas | Dedupe real contra respuestas simuladas con solape de página. | `05` §Paginación; encargo original §19 | Test automatizado |
| I14 | Película y serie con el mismo TMDb id numérico (colisión de espacio de ids) | El filtro de biblioteca local siempre acota `IncludeItemTypes` además de `HasAnyProviderId`, evitando falsos positivos cruzados. | `07` §3 | Test automatizado (mock `ILibraryManager`) |
| I15 | Usuario sin acceso a la biblioteca donde vive el ítem | Un usuario sin esa biblioteca visible ve el ítem como "no disponible/externo", nunca como local — verifica Capa A (`AddUserToQuery`) + Capa B (`IsVisible`). | `07` §4 | Test automatizado (mock con dos usuarios, permisos distintos) |
| I16 | HSS ausente (ensamblado no cargado) | El registrador loguea error y continúa; el resto del plugin (TMDb/Seerr) sigue operativo; diagnóstico muestra "Home Screen Sections: no detectado". | `04` §6 | Log + captura de diagnóstico |
| I17 | HSS presente pero tipo/método no encontrado (versión incompatible) | Mismo comportamiento defensivo que I16. | `04` §6 | Log |

## 3. Pruebas end-to-end (entorno Docker aislado)

Adaptación de los 35 pasos de la sección 20 del encargo original a las decisiones ya cerradas. Numeración propia, no 1:1 con el encargo, agrupada por bloque funcional.

### Bloque A — Instalación y arranque
1. Levantar `docker compose up` con Jellyfin 10.11.11 + HSS + File Transformation + (Pages si se decide incluirlo, no requerido) + Seerr autoalojado + el nuevo plugin construido desde el repo separado. — Evidencia: logs de arranque limpios, `docker compose ps` con todos los contenedores `healthy`.
2. Confirmar que el plugin aparece como cargado en Dashboard → Plugins, sin estado `NotSupported`. — Evidencia: captura.
3. Conectar TMDb (token real de prueba) y Seerr (instancia autoalojada del propio compose) desde la página de configuración; probar conexión con el botón dedicado. — Evidencia: captura + log.

### Bloque B — Creación y registro de secciones
4. Crear sección "Popular en Crunchyroll" (ES, series). 5. Crear "Popular en Netflix" (ES, películas). 6. Crear "Novedades en Prime Video" (ES, películas). — Evidencia: captura de las 3 tarjetas cerradas.
7. Confirmar que las 3 aparecen en Modular Home. 8. Activarlas. 9. Reordenarlas manualmente. — Evidencia: captura de Modular Home antes/después de reordenar.
10. **Reiniciar Jellyfin por completo** (`docker compose restart jellyfin`) y confirmar que las 3 secciones conservan UUID, posición y estado activo — verifica directamente el hallazgo de `04` §2 en un entorno real, no solo por lectura de código. — Evidencia: captura antes/después + diff del `SectionSettings[]` persistido en el XML de HSS.
11. Confirmar el logo a la izquierda del título en las 3 secciones (solución principal `displayText` HTML). — Evidencia: captura con zoom sobre el título de cada fila.

### Bloque C — Contenido y resolución de biblioteca
12. Abrir un ítem que SÍ está en la biblioteca local (reproducir, confirmar progreso/visto se conserva). 13. Confirmar que un ítem no local se muestra como contenido externo con metadatos/imagen de TMDb. 14. Confirmar con un segundo usuario Jellyfin sin acceso a una biblioteca concreta que un ítem de esa biblioteca **no** aparece como local para ese usuario (verifica I15 en vivo).

### Bloque D — Solicitudes (Seerr)
15. Solicitar una película no disponible (usuario normal, no admin). 16. Solicitar una serie completa (confirma `seasons:"all"` por defecto). 17. Solicitar una serie con selección interactiva de temporadas (acción secundaria). 18. Comprobar que el botón pasa a "Pendiente" tras el 202/200 de creación. 19. Comprobar que pasa a "Disponible" tras marcar disponible en Seerr (simulado o esperando el ciclo de sync). 20. Verificar en el panel de Seerr que el usuario solicitante registrado es el usuario Jellyfin correcto, no el admin (verifica `X-API-User`, I12 en vivo). 21. Repetir la solicitud del mismo título (misma temporada) y confirmar el manejo del 409 (verifica I7 en vivo). 22. Probar con un usuario sin permiso de solicitud (403, verifica I6 en vivo). 23. Solicitar en 4K, si el usuario tiene permiso 4K.

### Bloque E — Degradación y resiliencia
24. Parar el contenedor de Seerr; confirmar que el catálogo TMDb de las secciones sigue funcionando y el botón de solicitud se degrada con mensaje claro (verifica I11 en vivo). 25. Volver a levantar Seerr y confirmar recuperación sin reiniciar Jellyfin. 26. Introducir un token TMDb incorrecto; confirmar diagnóstico claro (verifica I2 en vivo). 27. Restaurar el token correcto y confirmar recuperación. 28. Confirmar comportamiento de caché: segunda carga de la home sensiblemente más rápida que la primera (medición, ver Bloque F).

### Bloque F — Gestión de secciones y seguridad
29. Editar el nombre de una sección con un valor que contenga `<script>alert(1)</script>`; confirmar en el DOM real de Jellyfin Web que se renderiza como texto escapado, no como script ejecutado (verifica U15/`10` §2.2 en vivo — **criterio de aceptación de seguridad no negociable**). 30. Editar filtros de una sección existente (cambiar proveedor/región) y confirmar que el UUID y la posición en Modular Home NO cambian. 31. Duplicar una sección y confirmar que la copia recibe un UUID nuevo. 32. Desactivar una sección y confirmar que desaparece de Modular Home sin errores. 33. Eliminar una sección y confirmar el comportamiento de "huérfana" documentado en `04` §2 (no rompe nada, sección deja de re-registrarse). 34. Confirmar en los logs y en las capturas de esta fase completa que ningún secreto (token TMDb, API key Seerr) aparece en texto plano en ningún punto (grep automatizado sobre logs exportados + revisión manual de capturas antes de commitear evidencias). 35. Actualizar el plugin a una build siguiente (mismo GUID, versión incrementada) y confirmar que la configuración y las secciones sobreviven la actualización — probar también el rollback a la build anterior.

## 4. Pruebas visuales

| # | Estado/combinación | Fuente | Evidencia |
|---|---|---|---|
| V1 | Página sin secciones (estado vacío) | Encargo §21; regla de spinner del sistema visual de referencia | Captura |
| V2 | Una tarjeta cerrada | `09-ui-reference-analysis.md` | Captura |
| V3 | Varias tarjetas cerradas (misma altura/alineación) | `09` | Captura |
| V4 | Tarjeta expandida | `09`, `05-ui-and-interaction-specification.md` (plan) | Captura |
| V5 | Estado cargando (spinner, nunca vacío en blanco) | `09` (regla heredada de JellyNotify) | Captura |
| V6 | Estado de error (TMDb/Seerr caídos) | I5, I6, I11 | Captura |
| V7 | Tema claro | Encargo §21 | Captura |
| V8 | Tema oscuro | Encargo §21 | Captura |
| V9 | Escritorio / tablet / móvil | Encargo §21 | 3 capturas responsive |
| V10 | Logo en el selector de proveedor (config) | `05` §Configuración de imágenes | Captura |
| V11 | Logo en la tarjeta cerrada | `09` | Captura |
| V12 | **Logo a la izquierda del título de sección en Modular Home real** | `08` (solución principal), Bloque B paso 11 | Captura con zoom — el criterio de aceptación central del encargo |
| V13 | Fallback sin logo (imagen rota/ausente) | U13 | Captura |
| V14 | Botón de solicitud (estados: disponible para pedir / pendiente / disponible / bloqueado) | I7, I8, I9 | 4 capturas |
| V15 | Seerr desconectado (badge/estado en tarjeta) | I11 | Captura |
| V16 | TMDb desconectado (badge/estado en tarjeta) | I2, I5 | Captura |
| V17 | Nombre de sección con intento de HTML/script — confirmación visual de que se ve como texto plano | U15, Bloque F paso 29 | Captura + inspección DOM |

## 5. Medición de rendimiento (hipótesis a validar, no SLA cerrado)

- **`HasAnyProviderId` sobre biblioteca grande**: hipótesis de objetivo **< 50 ms** por consulta sobre una biblioteca sintética de ≥5.000 ítems (el índice SQL compuesto `(ProviderId, ProviderValue, ItemId)` documentado en `07` §2 debería sostener esto, pero no se ha medido empíricamente — ver `07` Limitaciones). Medir con biblioteca sintética del propio entorno Docker, no asumir el número sin medir.
- **Primera carga de home sin caché** vs **con caché**: medir tiempo hasta que las N secciones terminan de pintar contenido, con caché fría vs caliente — sin objetivo numérico cerrado en esta fase, la propia medición del entorno de pruebas establece la línea base.

## 6. Limitaciones de esta matriz

- No incluye pruebas de carga/concurrencia con múltiples usuarios simultáneos más allá de lo listado en el encargo original — se considera extensión posterior, no MVP.
- Los objetivos de rendimiento son hipótesis a confirmar, no compromisos; ver `07-jellyfin-library-resolution.md` Limitaciones, que ya señala que no se ha medido el índice SQL en vivo.

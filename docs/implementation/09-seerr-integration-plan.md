# 09 — Plan de integración con Seerr

Fuente: `docs/research/06-seerr-api-analysis.md`. Documento de PLAN — sin código C# real, solo especificación accionable.

Decisiones ya cerradas que este plan respeta: repositorio de código nuevo y separado (`/home/alvaro/Descargas/JellyProviderSections`); **Seerr objetivo 3.4.0+** (incluye `ignoreQuota`); **4K incluido en el MVP**; preferencia de solicitud de series por defecto **todas las temporadas** (`seasons: "all"`), con selección interactiva como acción secundaria.

## 1. Reutilización vs. reescritura del cliente Seerr

**Decisión: reescribir un cliente Seerr propio en el nuevo repo, usando `SeerrApiClient.cs`/`ISeerrApiClient.cs` de JellyNotify como plantilla de arranque (copiar la estructura HttpClient/serialización/manejo de excepciones), no como dependencia compartida.** Cada plugin de Jellyfin es un assembly aislado — no hay mecanismo de "librería compartida entre plugins" sin publicar un paquete NuGet propio, lo cual es sobreingeniería para este alcance. Se trata como una reescritura informada por el contrato ya aprendido, no una copia ciega:

- **Los tres bugs conocidos del cliente de JellyNotify NO se heredan, se corrigen desde el diseño**:
  1. `SeerrMediaStatus`: fijar `Blocklisted = 6, Deleted = 7` (JellyNotify los tiene invertidos).
  2. `SeerrRequestStatus`: añadir el quinto valor `Completed = 5`.
  3. `SeerrMediaSeasonStatus`: añadir `Status4k` junto a `Status`.
- El patrón de `HttpClient` tipado (`AddHttpClient<ISeerrApiClient, SeerrApiClient>()`), el header `X-Api-Key`, y el flag `IgnoreSslErrors` sí se replican tal cual (ya probados en producción).

## 2. Nuevos métodos requeridos (no presentes en el cliente de JellyNotify)

| Método | Endpoint real | Payload/parámetros |
|---|---|---|
| `CreateRequestAsync` | `POST /api/v1/request` | `mediaType` (`movie`\|`tv`), `mediaId` (TMDB id, no id interno Seerr), `seasons: number[] \| "all"`, `is4k`, `ignoreQuota` (bool, solo con permiso `MANAGE_REQUESTS` del llamante) |
| `GetUserByJellyfinIdAsync` | `GET /api/v1/user/jellyfin/{jellyfinUserId}` | GUID de Jellyfin → `SeerrUser` o 404 |
| `ImportUsersFromJellyfinAsync` | `POST /api/v1/user/import-from-jellyfin` | `{ jellyfinUserIds: string[] }`, requiere `MANAGE_USERS` de la API key admin |
| `GetMediaDetailsWithStatusAsync` | `GET /api/v1/movie/{tmdbId}` / `GET /api/v1/tv/{tmdbId}` | Extender el modelo de respuesta para capturar `mediaInfo.status`, `mediaInfo.status4k`, `mediaInfo.seasons[].{seasonNumber,status,status4k}` — el `SeerrMediaDetails` actual de JellyNotify no los captura |

## 3. Estrategia de autenticación por petición: `X-API-User`, no `userId` en el body

**Decisión de diseño, justificada por el hallazgo de la investigación**: usar siempre la cabecera `X-API-User: {seerrUserId}` junto con `X-API-Key: {adminApiKey}` para toda operación de creación de solicitud en nombre de un usuario Jellyfin. Se descarta explícitamente el campo `userId` del body, porque con `userId` la auto-aprobación se evalúa contra el permiso del **admin** (siempre auto-aprobada), no contra el usuario real — comportamiento sorprendente que rompería la configuración de auto-aprobación por usuario que el administrador de Seerr ya tiene configurada. Con `X-API-User`, permisos, cuota, overrides de perfil/carpeta raíz y auto-aprobación se evalúan exactamente como si el usuario hubiera solicitado desde la propia UI de Seerr.

### Flujo completo de resolución de identidad (ejecutado la primera vez que un usuario Jellyfin pulsa "Solicitar", con resultado cacheado después)

1. Resolver `jellyfinUserId` (GUID) del usuario Jellyfin autenticado que hace clic.
2. `GET /api/v1/user/jellyfin/{jellyfinUserId}`.
   - **200** → usar el `id` numérico de Seerr devuelto como `X-API-User` para el resto del flujo. Cachear el mapeo `jellyfinUserId → seerrUserId` (ver `10-testing-environment.md`/caché del plan de arquitectura).
   - **404** → paso 3.
3. Intentar `POST /api/v1/user/import-from-jellyfin` con `{ jellyfinUserIds: [jellyfinUserId] }`, usando la API key admin (requiere que el admin haya configurado la integración Jellyfin dentro de Seerr; si Seerr no tiene esa integración configurada, esta llamada fallará de forma predecible).
   - Éxito → repetir el lookup del paso 2, ahora debería devolver 200.
   - Fallo → paso 4.
4. Mostrar al usuario un mensaje claro y accionable: *"Para solicitar contenido, inicia sesión una vez en Seerr con tu cuenta de Jellyfin"* (con enlace a la URL de Seerr configurada por el admin), en vez de un error genérico. No se ofrece la opción de crear la solicitud sin identidad resuelta — está fuera de alcance del MVP suplantar con el admin id 1 de forma silenciosa (rompería la atribución de la sección 11 del encargo original).

## 4. Manejo explícito de respuestas de `POST /api/v1/request`

| HTTP | Causa real | Comportamiento UI |
|---|---|---|
| 201 (implícito, éxito) | Solicitud creada | Botón pasa a "Pendiente"/"Solicitado", invalidar caché de estado del título |
| 403 `QuotaRestrictedError` | Cuota del usuario agotada | Mensaje específico: "Has alcanzado tu límite de solicitudes" — nunca un error genérico |
| 403 `RequestPermissionError` | Usuario sin permiso de solicitar (o de 4K si `is4k=true`) | Ocultar o deshabilitar el botón de origen si se conoce de antemano; si no, mensaje específico |
| 403 `BlocklistedMediaError` | Título en blocklist de Seerr | Ocultar el botón "Solicitar" para ese título, mostrar estado neutro (no tratar como error) |
| 409 `DuplicateMediaRequestError` | Ya existe una solicitud activa | Tratar como éxito idempotente: mostrar "Ya solicitado", no como fallo |
| 202 `NoSeasonsAvailableError` | Serie: todas las temporadas pedidas ya cubiertas | Tratar igual que 409 — éxito silencioso, refrescar estado |
| 500 / timeout / red | Error real | Mensaje de error genérico + reintento manual; no reintento automático silencioso (evitar duplicar solicitudes por reintento) |

## 5. Mapeo de estado de disponibilidad → UI

| `mediaInfo.status` (o `status4k` si la sección es 4K) | Estado visual de la tarjeta |
|---|---|
| `undefined` (sin `mediaInfo`) | "No disponible" + botón "Solicitar" (si el usuario tiene identidad Seerr resuelta o puede resolverla) |
| `PENDING` / `PROCESSING` | "Solicitado" (sin botón, o botón deshabilitado) |
| `PARTIALLY_AVAILABLE` | "Parcialmente disponible" — para series, cruzar con `seasons[].status` para indicar qué temporadas faltan si se implementa selección interactiva |
| `AVAILABLE` | No debería llegar aquí en la práctica — un ítem `AVAILABLE` en Seerr normalmente ya es resoluble como local en Jellyfin (ver `07-jellyfin-library-resolution.md`); tratar como "Disponible" sin botón, de forma defensiva |
| `BLOCKLISTED` | Sin botón "Solicitar", estado neutro, no mostrar como error |
| `DELETED` | Tratar igual que "No disponible" (permite volver a solicitar) |

## 6. Caché de estado Seerr por título

- Clave: `(tmdbId, contentType, is4k)`. Duración corta (recomendado 5–15 min, configurable — igual principio que la caché de resultados Discover de TMDb, ver `08-tmdb-integration-plan.md`), porque el estado de disponibilidad/solicitud cambia con la actividad normal del servidor.
- **Invalidación inmediata** de la entrada correspondiente inmediatamente después de una creación de solicitud exitosa (201/409/202), para que el botón refleje el nuevo estado sin esperar al TTL.
- El mapeo `jellyfinUserId → seerrUserId` se cachea con una duración mucho más larga (cambia solo cuando un usuario se vincula/importa por primera vez).

## 7. Degradación si Seerr está caído o no configurado

- Si `SeerrSettings.Enabled = false` o la prueba de conexión (`GET /api/v1/status`) falla: la sección sigue mostrando el catálogo TMDb con normalidad (esto no depende de Seerr en absoluto); el botón "Solicitar" se sustituye por un estado deshabilitado con tooltip ("Seerr no está configurado/disponible"), nunca se oculta el título completo de la fila ni se bloquea el resto de la sección.
- Timeout corto y explícito en las llamadas de estado (no debe bloquear el render de la sección esperando a Seerr) — ver objetivos de rendimiento en `02-architecture.md`/caché.
- El estado de conexión Seerr (`ok`/`error`/`no configurado`) se expone en el panel de diagnóstico de la página de administración (mismo patrón que JellyNotify).

## 8. Fuera de alcance de este plan (documentado, no implementado en el MVP)

- Selección interactiva de temporadas como UI completa (checkboxes por temporada) — el MVP ofrece "Solicitar todas las temporadas" como acción primaria; la selección interactiva queda como extensión posterior (ver `01-product-requirements.md`), aunque el contrato (`seasons: number[]`) ya la soporta sin cambios de backend.
- OIDC (no disponible en ningún release estable de Seerr todavía).
- Verificación contra una instancia Seerr real del cuerpo exacto de error 4xx/5xx byte a byte — pendiente de la fase de pruebas de integración con servidor HTTP simulado (`server/routes/request.ts` como contrato) y, después, contra la instancia real del entorno Docker.

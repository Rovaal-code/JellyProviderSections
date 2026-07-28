# 04 — Contratos de API

Fuente: patrón de `docs/API.md` de JellyNotify (`research/02` §5), decisiones de `01-product-requirements.md`. Todas las rutas rooted en `/JellyProviderSections`.

## Endpoints de administración (requieren sesión Jellyfin con privilegios de administrador)

| Método | Ruta | Propósito |
|---|---|---|
| `GET` | `/Admin/config` | Configuración completa (secretos redactados) |
| `PUT` | `/Admin/config` | Guardar `TmdbSettings`/`SeerrSettings` globales (preserva secretos no reenviados) |
| `POST` | `/Admin/test/tmdb` | Probar conexión TMDb (valida el token, confirma acceso a `/configuration`) |
| `POST` | `/Admin/test/seerr` | Probar conexión Seerr (valida la API key) |
| `GET` | `/Admin/sections` | Listar todas las `SectionDefinition` (con estado calculado: `HomeSectionsRegistered`, `SeerrConnected`) |
| `POST` | `/Admin/sections` | Crear una `SectionDefinition` nueva (genera `Id` server-side) |
| `GET` | `/Admin/sections/{id}` | Detalle de una sección (para la tarjeta expandida) |
| `PUT` | `/Admin/sections/{id}` | Editar una sección (rechaza cualquier intento de cambiar `Id`) |
| `POST` | `/Admin/sections/{id}/duplicate` | Duplicar (nuevo `Id`, mismo resto de campos, `DisplayName` con sufijo) |
| `POST` | `/Admin/sections/{id}/enable` / `/disable` | Activar/desactivar (dispara re-registro/baja en HSS) |
| `DELETE` | `/Admin/sections/{id}` | Eliminar definitivamente (deja de registrarse en el próximo ciclo) |
| `POST` | `/Admin/sections/{id}/test-query` | Ejecuta la consulta Discover real con los filtros actuales, sin publicarla, devuelve resultados crudos + query TMDb generada |
| `GET` | `/Admin/sections/{id}/preview` | Previsualización renderizada (tarjetas con logo/estado) tal como se vería en la home |
| `POST` | `/Admin/sections/{id}/clear-cache` | Limpia la caché de Discover/matches/estado Seerr de esa sección |
| `GET` | `/Admin/diagnostics` | Estado TMDb/Seerr/HSS, versión de HSS detectada, última sync global, último error |
| `POST` | `/Admin/sync-now` | Fuerza una resincronización inmediata de todas las secciones activas |
| `POST` | `/Admin/register-sections-now` | Fuerza el re-registro en HSS sin esperar al próximo arranque/guardado |

## Endpoints de usuario (requieren sesión Jellyfin autenticada, sin privilegios de admin)

| Método | Ruta | Propósito |
|---|---|---|
| `GET` | `/status/{tmdbId}` | Estado de disponibilidad/solicitud de un título para el usuario actual (local / externo-disponible-en-Seerr / externo-pendiente / externo-solicitable) |
| `POST` | `/request` | Crear una solicitud en Seerr en nombre del usuario actual (`mediaType`, `tmdbId`, `seasons?`, `is4k?`) |
| `GET` | `/public-settings` | Flags no sensibles (¿Seerr habilitado?, ¿4K disponible?) — nunca URLs/keys |

## Endpoint invocado por Home Screen Sections (in-process, no HTTP)

No es una ruta HTTP — es el método estático que `HomeSectionsRegistrar` expone como `resultsClass`/`resultsMethod` en el payload de registro, invocado por reflexión desde el propio proceso de HSS. Ver `06-home-sections-integration-plan.md` para la firma exacta.

## Endpoints estáticos (sin autenticación, mismo patrón que `WebAssetsController` de JellyNotify)

| Método | Ruta | Contenido |
|---|---|---|
| `GET` | `/web/providersections.css` | Hoja de estilos de la página de configuración |
| `GET` | `/Configuration/configPage.css` / `/configPage.js` | Assets de la página de plugin embebida |
| `GET` | `/Logo/{tmdbProviderId}` | Logo del proveedor cacheado localmente (descargado una vez de TMDb, `Cache-Control` largo) |

## Reglas de contrato transversales

- Ningún endpoint de usuario acepta `jellyfinUserId`/`seerrUserId` como parámetro del cliente — siempre se resuelve del lado servidor a partir de la sesión Jellyfin autenticada (ver hallazgo de seguridad en `research/10` §2.6, evita que un usuario solicite en nombre de otro).
- `GET /Admin/config` nunca incluye `TmdbSettings.ApiReadAccessToken` ni `SeerrSettings.ApiKey` con su valor real — mismo patrón de redacción que `docs/API.md` de JellyNotify.
- Todos los endpoints de administración devuelven 401/403 estándar de Jellyfin si la sesión no es de administrador (no hay lógica de autorización propia adicional).
- `POST /Admin/sections` y `PUT /Admin/sections/{id}` validan servidor-side los campos obligatorios de `03-data-model.md` antes de aceptar el guardado — el formulario del frontend valida por UX, pero el backend es la fuente de verdad.

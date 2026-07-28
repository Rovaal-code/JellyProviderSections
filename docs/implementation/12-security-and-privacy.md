# 12 — Seguridad y privacidad (plan concreto)

Fuente: `research/10-security-and-licensing.md`. Este documento convierte cada hallazgo en una acción de implementación verificable.

| Hallazgo (research/10) | Acción concreta | Dónde se verifica |
|---|---|---|
| Secretos (TMDb token, Seerr API key) nunca al frontend | `GET /Admin/config` redacta ambos campos; `PUT` aplica `PreserveSecrets` (campo vacío entrante conserva el valor persistido) | Test unitario de `PluginConfiguration.PreserveSecrets`; test de integración sobre `GET /Admin/config` que confirma que el valor real nunca aparece en el JSON de respuesta |
| XSS vía `displayText` no escapado por HSS (hallazgo nuevo, §2.2) | `DisplayName` se HTML-encodea siempre antes de interpolarse en `displayText`; el único HTML literal permitido es la etiqueta `<img>` del logo, con `src` construida server-side a partir de `TmdbProviderId` verificado, nunca de una URL libre | Test unitario del builder de `displayText` con un `DisplayName` que contiene `<script>`; test E2E que registra una sección con ese nombre y confirma en el DOM real que se renderiza como texto, no como script ejecutado (ver `11-test-matrix.md` bloque F) |
| Endpoint HTTP `/HomeScreen/RegisterSection` de HSS sin auth | El plugin nunca lo usa; solo vía in-process por reflexión | Revisión de código en cada PR que toque `HomeSectionsRegistrar`; ausencia de cualquier `HttpClient` apuntando a esa ruta |
| SSRF | TMDb no tiene URL configurable (dominio fijo); Seerr `ServerUrl` es campo admin-only, se valida esquema `http`/`https`, timeout explícito, sin seguir redirecciones a otro esquema | Test unitario de validación de `ServerUrl` al guardar configuración |
| TLS / certificados autofirmados | `IgnoreSslErrors` opcional en `SeerrSettings`, igual patrón que JellyNotify | Test de integración con servidor HTTP simulado con certificado inválido |
| Autorización de endpoints propios | Todos los `Admin/*` requieren rol admin de Jellyfin; `/status`, `/request` requieren sesión de usuario; `/Logo/*` y assets estáticos son públicos y no exponen secretos | Test de integración: llamar cada endpoint admin sin sesión de admin y confirmar 401/403 |
| Un usuario no debe poder solicitar en nombre de otro | `/request` y `/status` resuelven la identidad del usuario siempre server-side desde la sesión Jellyfin, nunca de un parámetro del cliente | Test de integración: payload con un `jellyfinUserId` distinto al de la sesión, confirmar que se ignora/rechaza |
| Permisos de biblioteca (no revelar contenido oculto) | `LibraryResolver` usa `InternalItemsQuery(currentUser)` (Capa A) + `item.IsVisible(currentUser)` explícito (Capa B), ver `research/07` §4 | Test de integración: usuario sin acceso a una biblioteca no ve ese ítem como local, aunque exista físicamente |
| Diagnósticos sin secretos | `GET /Admin/diagnostics` solo expone booleanos/timestamps/mensajes saneados; cualquier excepción de `HttpClient` se sanea (elimina cabecera `Authorization` y query strings con token) antes de guardarse en `LastError` o mostrarse en UI | Test unitario del saneador de excepciones HTTP |
| CSRF | Delegado al mecanismo de sesión/cookie ya provisto por el framework de plugins de Jellyfin, mismo criterio que JellyNotify | N/A (sin mecanismo propio adicional) |
| Buenas prácticas de repositorio | `.env.example` sin valores reales en el entorno Docker de pruebas; ningún token real en fixtures de test (servidor HTTP simulado); `.gitignore` cubre cualquier fichero de configuración local con secretos | Revisión manual antes de cada commit/push (ver `13-packaging-and-release.md`) |

## Modelo de amenaza asumido

- El administrador del plugin es de confianza (igual que cualquier plugin de Jellyfin con acceso a `BasePluginConfiguration`) — no se protege contra un admin malicioso más allá de lo razonable (validación de esquema de URL, etc.), pero **sí** se protege activamente contra un usuario no-admin que intente explotar la superficie del plugin (XSS vía nombre de sección, suplantación de otro usuario al solicitar, enumeración de bibliotecas ocultas).
- Los tres servicios externos (TMDb, Seerr, HSS) se tratan como no confiables en cuanto a disponibilidad (deben degradar limpio) pero confiables en cuanto a contenido de sus respuestas (no se sanitiza exhaustivamente el HTML/JSON que devuelven, salvo el propio `displayText` que construimos nosotros).

## Verificación previa a cada release

Checklist mínimo (a incorporar en `13-packaging-and-release.md`): grep de `ApiReadAccessToken`/`ApiKey` sobre el diff antes de commitear: cero coincidencias con valores no vacíos; capturas de pantalla del entorno de pruebas revisadas antes de subirlas como evidencia (campos de secreto vacíos o con valor ficticio explícito).

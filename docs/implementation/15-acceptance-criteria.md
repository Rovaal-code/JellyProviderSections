# 15 — Criterios de aceptación

Checklist verificable, mapeado 1:1 sobre la sección 29 del encargo original del usuario. Cada criterio indica cómo se verifica y a qué parte de `11-test-matrix.md` corresponde. El proyecto no se considera terminado hasta que todos los criterios marcados como obligatorios para el MVP estén en verde con evidencia real (no solo "debería funcionar").

## Estado a 2026-07-29

Verificado ejecutando contra el entorno real de `testenv/` (Jellyfin 10.11.11, Home Screen Sections 2.5.11.0, File Transformation 2.5.11.0, Seerr 3.4.0):

| Criterio | Estado | Evidencia |
|---|---|---|
| 1 Compila de forma reproducible | **Cumplido** | Build en contenedor, 0 avisos y 0 errores |
| 2 Se instala correctamente | **Cumplido** | `Jellyfin Provider Sections 0.0.1.0 Active` en `GET /Plugins` |
| 3 Detecta Home Screen Sections | **Cumplido** | Diagnóstico reporta `available: true, version 2.5.11.0` |
| 4 Detecta las dependencias | **Cumplido** | Detecta también File Transformation |
| 5 Registra varias secciones | **Cumplido** | 3 secciones registradas |
| 6 UUID estable | **Cumplido** | Mismos ids tras reiniciar y tras editar |
| 7 Aparece en Modular Home | **Cumplido** | `GET /HomeScreen/Sections` devuelve las 3 |
| 8/9 Activar y desactivar | **Cumplido** | Alta y baja del registro |
| 11 Conserva posición tras reinicio | **Cumplido** | Ids intactos tras reinicio completo del contenedor |
| 12 Obtiene regiones | **Cumplido** | 139 regiones reales de TMDb |
| 13 Obtiene proveedores por región | **Cumplido** | 68 proveedores de series en España |
| 14 Logos en la configuración | **Cumplido** | `logoPath` real por proveedor |
| 15 Logo a la izquierda del título | **Cumplido, con prueba visual** | `evidence/screenshots/02-titulo-logo-*.png`: recorte del título en las tres filas, temas claro y oscuro. El logo queda a la izquierda del texto, centrado verticalmente, sin desplazar los controles de la fila |
| 16 Consultas Discover válidas | **Cumplido** | 60 títulos reales de Crunchyroll España |
| 17/18 Muestra películas y series | **Cumplido** | Secciones de ambos tipos |
| 19 Resuelve contenido local | **Cumplido** | "Ataque a los Titanes" vuelve como ítem real con `UserData` |
| 22 Muestra contenido externo | **Cumplido, con prueba visual** | Las 99 tarjetas externas de las tres filas se dibujan en vertical con su carátula de TMDb, servida por el plugin. Al pulsarlas se abre la ficha de Jellyfin Enhanced (`evidence/screenshots/11-…`) |
| 23 Consulta estados de Seerr | **Cumplido** | `Unknown` a `Processing` tras solicitar |
| 24/25 Solicita películas y series | **Cumplido** | Ambas creadas en Seerr |
| 26 Atribuye al usuario correcto | **Cumplido** | `jellyfinUserId` coincide con el usuario de la sesión |
| 28 Contenido ya solicitado | **Cumplido** | 409 y 202 tratados como estado, no como error |
| 43 No expone secretos | **Cumplido** | `GET /Admin/config` solo devuelve booleanos `hasApiKey` |
| 44 Pruebas unitarias | **Cumplido** | 36 en verde, incluidos 3 de escape XSS y 7 de identificadores de sección |
| Seguridad: XSS vía `displayText` | **Cumplido** | Un nombre con `<script>` llega escapado como `&lt;script&gt;` en el servidor real |

| 20/21 Respeta permisos, no revela bibliotecas no autorizadas | **Cumplido** | Un usuario sin acceso a la biblioteca de Series recibe el título como externo (0 locales), el admin lo recibe como local |
| 31 Se degrada si Seerr cae | **Cumplido** | La fila sigue sirviendo 60 títulos; solicitar devuelve `Unavailable` con HTTP 503 y mensaje correcto |
| 32 Se degrada si TMDb cae | **Cumplido** | Con clave inválida la home responde 200 y la fila queda vacía, sin excepción; el test informa del 401 |
| 33 Utiliza caché | **Cumplido** | Segunda carga dentro del TTL no repite la llamada |
| 35 a 42 Interfaz, temas y responsive | **Cumplido, con prueba visual** | Índice completo en `testenv/evidence/README.md` |

### Lo que descubrió la verificación en navegador

Nueve defectos que ninguna comprobación de API podía revelar, todos corregidos:

1. **El id de sección rompía la página principal entera.** Jellyfin Web resuelve cada fila con `querySelector('.' + id)`; un GUID que empieza por dígito no es un identificador CSS válido, así que lanzaba `SyntaxError` y abortaba el renderizado de *todas* las filas, no solo la nuestra. Los ids se generan ahora con el prefijo `jps` y hay una migración que reescribe los antiguos.
2. **Las filas de series salían vacías.** Los DTO sintéticos no llevaban `ServerId` y el constructor de tarjetas fallaba con `item or serverId cannot be null`. Las de películas sobrevivían, lo que hacía parecer un problema del tipo de contenido.
3. **El selector de proveedor mostraba "Proveedor undefined" y no filtraba.** El cliente leía los nombres crudos de TMDb (`provider_name`) en vez del contrato del servidor (`{ id, name, logoPath }`).
4. **El selector de región mostraba "ES (ES)".** Mismo desajuste con `{ code, name, englishName }`.
5. **El diagnóstico contradecía al servidor**, reportando TMDb, Seerr, HSS y File Transformation como no detectados con las 3 secciones registradas y funcionando: el cliente esperaba una forma plana y el endpoint responde anidada.
6. **La pestaña de conexiones no cargaba nada y al guardar destruía la configuración.** Leía `tmdbSettings`/`seerrSettings` (el servidor devuelve `tmdb`/`seerr`) y enviaba un cuerpo anidado a un endpoint que espera `SaveConfigRequest` plano, de modo que "Guardar" mandaba `enabled: false` en ambas integraciones.
7. **El panel era ilegible en tema claro.** Los tokens de color estaban fijados al tema oscuro. Jellyfin marca el tema activo en `data-theme` del `<html>`; ahora hay un bloque `:root[data-theme="light"]` y las superficies con tinte son tokens en vez de literales.
8. **Tres botones del panel llamaban a rutas inexistentes** y respondían 404: "Sincronizar ahora", "Previsualizar" y el "Probar consulta" de la tarjeta de sección. Los tres endpoints existen ya (`POST sync-now`, `GET sections/{id}/preview`, `POST sections/{id}/test-query`).
9. **El estado de sincronización no se guardaba.** `LastSyncUtc`, `LastSyncResult` y `LastError` solo vivían en memoria, así que el diagnóstico volvía a "Nunca" en cada reinicio mientras un error antiguo guardado en disco se quedaba fijo. Ahora se persiste en el fallo de caché, que es como mucho una vez por sección y TTL, con un cerrojo porque HSS construye varias secciones en paralelo.

### Carátulas verticales y ficha al pulsar

Home Screen Sections elige su renderizador de tarjetas con carátula **por clave de sección** (solo `Discover`, `DiscoverMovies` y `DiscoverTV`), así que las secciones de terceros pasan siempre por el constructor de tarjetas estándar de Jellyfin, que deduce la URL de la imagen del id del ítem. En una tarjeta externa ese id es sintético y ningún endpoint de imagen lo resuelve, de ahí que salieran planas. El plugin inyecta ahora `Web/home.js` mediante File Transformation: decodifica el id de TMDb del propio id sintético, sin ninguna petición extra, pide la carátula a `/JellyProviderSections/Poster/{id}` y marca la tarjeta como `discover-card` con `data-tmdb-id` y `data-media-type`, que es el contrato que la ficha de Jellyfin Enhanced escucha. Las secciones se registran además con `viewMode: "Portrait"`.

Esto añade una dependencia directa de File Transformation que la investigación había descartado; ver la actualización en `research/04-home-screen-sections-integration.md` §4. Jellyfin Enhanced sigue siendo opcional: sin él la tarjeta se dibuja igual y el clic no hace nada, en lugar de navegar a una ficha inexistente.

El script oculta además el overlay de botones que Jellyfin dibuja al pasar el cursor (reproducir, marcar como visto, favorito y menú) **solo en las filas de este plugin**: en una tarjeta externa esos botones actúan sobre algo que el servidor no tiene, y en una fila de catálogo son ruido visual. El registro pasa también `showDetailsMenu: false`, que cubre el intervalo entre que carga la página y corre el script. Los títulos locales de esas filas siguen abriendo su ficha real de Jellyfin.

### Pendientes

- **Medición de rendimiento con biblioteca grande** (criterio 34): la biblioteca sintética tiene 6 títulos.

| # | Criterio | Cómo se verifica | Evidencia | Alcance |
|---|---|---|---|---|
| 1 | Compila de forma reproducible | Build en contenedor Docker (`mcr.microsoft.com/dotnet/sdk:9.0`) desde cero, dos veces seguidas, mismo checksum de salida | Log de build + checksum | MVP |
| 2 | Se instala correctamente | Instalación vía manifest compartido con JellyNotify en un Jellyfin 10.11.11 limpio del entorno Docker | Captura del catálogo de plugins + log de arranque sin error | MVP |
| 3 | Detecta Home Screen Sections | Arranque con HSS instalado → `HomeSectionsRegistered=true`; arranque sin HSS → log de aviso, plugin sigue funcionando sin excepción | Logs de ambos escenarios | MVP |
| 4 | Detecta las dependencias necesarias | Página de diagnóstico muestra versión de HSS detectada y estado de compatibilidad | Captura de la página de diagnóstico | MVP |
| 5 | Registra varias secciones | Crear 3+ secciones (Crunchyroll/Netflix/Prime Video ES) y confirmar las 3 registradas | Captura de Modular Home con las 3 filas | MVP |
| 6 | Cada sección conserva un UUID estable | Editar nombre/filtros de una sección, confirmar que `Id` no cambia en la configuración persistida | Diff del XML de configuración antes/después | MVP |
| 7 | Aparece en Modular Home | Ver las secciones activas en la home real de un usuario de prueba | Captura | MVP |
| 8 | Puede activarse | Toggle `Enabled=true` → aparece en home tras el próximo re-registro | Captura antes/después | MVP |
| 9 | Puede desactivarse | Toggle `Enabled=false` → desaparece de home | Captura antes/después | MVP |
| 10 | Puede moverse | Reordenar en Modular Home (funcionalidad nativa de HSS, no del nuevo plugin) | Captura | MVP |
| 11 | Conserva posición después de reinicios | Reiniciar el contenedor Jellyfin completo, confirmar orden/estado sin cambios | Captura antes/después de reinicio | MVP |
| 12 | Obtiene regiones de TMDb | `GET /watch/providers/regions` con token real, lista poblada en el selector | Captura del selector de región | MVP |
| 13 | Obtiene proveedores por región | `GET /watch/providers/movie\|tv` con `watch_region`, lista poblada y filtrada por tipo de contenido | Captura del selector de proveedor | MVP |
| 14 | Muestra logos en la configuración | Logo del proveedor visible en el selector de la página de administración | Captura | MVP |
| 15 | Muestra el logo a la izquierda del título de la sección | `displayText` con `<img>+<span>` renderizado en Jellyfin Web real, logo alineado verticalmente, sin desplazar controles | Captura en tema claro y oscuro | MVP — **no se da por resuelto sin esta captura real**, ver `07-provider-logo-plan.md` |
| 16 | Construye consultas Discover válidas | Respuesta 200 de TMDb para al menos una sección de cada `ContentType` | Log de la petición/respuesta sanitizada | MVP |
| 17 | Muestra películas | Sección `ContentType=Movie` con resultados reales | Captura | MVP |
| 18 | Muestra series | Sección `ContentType=Series` con resultados reales | Captura | MVP |
| 19 | Resuelve contenido local | Título presente en la biblioteca sintética del entorno de pruebas se muestra como ítem local reproducible | Captura + verificación de reproducción | MVP |
| 20 | Respeta permisos | Usuario sin acceso a una biblioteca no ve el ítem como local (se trata como no disponible) | Prueba con dos usuarios de permisos distintos | MVP |
| 21 | No revela bibliotecas no autorizadas | Mismo caso que #20, confirmando además que no hay pista visual/de datos de que el ítem existe | Inspección de la respuesta HTTP cruda, no solo de la UI | MVP |
| 22 | Muestra contenido externo | Título ausente de la biblioteca se muestra como tarjeta marcada como externa, con metadatos/imagen de TMDb | Captura | MVP |
| 23 | Consulta estados de Seerr | Estado (pendiente/disponible/parcial) reflejado correctamente en la tarjeta de contenido externo | Captura + log de la consulta a Seerr | MVP |
| 24 | Solicita películas | Flujo completo de solicitud de una película desde una sección, confirmada en Seerr real (docker-compose) | Captura del estado "pendiente" en Seerr | MVP |
| 25 | Solicita series | Igual que #24, incluida la opción "todas las temporadas" (preferencia por defecto ya decidida) | Captura | MVP |
| 26 | Atribuye la solicitud al usuario correcto | La solicitud creada en Seerr aparece asociada al usuario de Jellyfin que la originó, no a la API key admin genérica | Captura del detalle de la solicitud en Seerr | MVP |
| 27 | Respeta permisos y restricciones | Usuario sin permiso de solicitud en Seerr recibe error claro, no un fallo silencioso ni un bypass | Prueba con usuario restringido | MVP |
| 28 | Maneja contenido ya solicitado | Segunda solicitud del mismo título no duplica, muestra estado existente | Prueba de idempotencia | MVP |
| 29 | Maneja contenido disponible | Título ya disponible en Seerr no ofrece botón de "Solicitar", refleja el estado real | Captura | MVP |
| 30 | Maneja contenido parcialmente disponible | Serie con temporadas mixtas (algunas disponibles, otras no) refleja el estado por temporada | Captura | MVP |
| 31 | Se degrada correctamente si Seerr está caído | Apagar el contenedor Seerr del entorno de pruebas, confirmar que el resto del plugin sigue funcionando (sin excepción, con aviso claro) | Log + captura del estado degradado | MVP |
| 32 | Se degrada correctamente si TMDb está caído | Token inválido / host inalcanzable simulado, confirmar degradación sin bloquear la home | Log + captura | MVP |
| 33 | Utiliza caché | Verificar que una segunda carga de la misma sección no repite la llamada a TMDb dentro del TTL configurado | Log con contador de llamadas | MVP |
| 34 | No bloquea la página principal | Medir tiempo de carga de home con TMDb/Seerr lentos simulados (timeout artificial) | Medición (ver objetivos en `research/`) | MVP |
| 35 | La interfaz respeta el diseño de JellyNotify | Comparación visual lado a lado (paleta, tarjetas, tipografía) | Capturas comparativas | MVP |
| 36 | Las tarjetas cerradas son uniformes | Varias tarjetas cerradas con misma altura/alineación/posiciones de badge | Captura con 3+ tarjetas | MVP |
| 37 | Las tarjetas se expanden hacia abajo | Sin cambio de anchura, sin salto brusco, `aria-expanded` correcto | Captura + inspección de accesibilidad | MVP |
| 38 | Los campos están siempre estructurados | Mismo orden/posición de campos en todas las tarjetas expandidas | Captura de 2+ tarjetas expandidas distintas | MVP |
| 39 | Funciona con teclado | Navegación completa (abrir/cerrar tarjeta, acciones) sin ratón | Prueba manual documentada | MVP |
| 40 | Funciona en tema claro | Todas las capturas visuales repetidas en tema claro | Capturas | MVP |
| 41 | Funciona en tema oscuro | Ídem en tema oscuro | Capturas | MVP |
| 42 | Funciona de forma responsive | Capturas en escritorio/tablet/móvil sin overflow horizontal ni layout roto | Capturas en 3 anchos | MVP |
| 43 | No expone secretos | Ver checklist completo en `12-security-and-privacy.md` §10 | Grep de logs + inspección de payloads | MVP |
| 44 | Supera pruebas unitarias | Suite completa en verde | Salida de `dotnet test` | MVP |
| 45 | Supera pruebas de integración | Suite contra servidor HTTP simulado en verde | Salida de test + logs sanitizados | MVP |
| 46 | Supera pruebas end-to-end | Los 35 pasos de `research`/encargo original documentados en `11-test-matrix.md` | Informe E2E + capturas | MVP |
| 47 | Genera un paquete instalable | Zip final con checksum, entrada de manifest válida | Artefacto + entrada de manifest | MVP |
| 48 | Incluye documentación | README del nuevo repo + este directorio de implementación | Revisión de contenido | MVP |
| 49 | Incluye evidencias de validación | Carpeta de evidencias (capturas, logs, mediciones) referenciada desde este documento | Índice de evidencias | MVP |

## Regla de cierre

Un criterio solo se marca como cumplido con una evidencia real adjunta (captura, log, medición, salida de test) — nunca por inspección de código o "debería funcionar según el diseño". Esto aplica en particular a los criterios 15 (logo), 24-30 (Seerr) y 31-32 (degradación), que son exactamente los que el encargo original señala explícitamente como "no dar por resueltos sin prueba real".

# 15 — Criterios de aceptación

Checklist verificable, mapeado 1:1 sobre la sección 29 del encargo original del usuario. Cada criterio indica cómo se verifica y a qué parte de `11-test-matrix.md` corresponde. El proyecto no se considera terminado hasta que todos los criterios marcados como obligatorios para el MVP estén en verde con evidencia real (no solo "debería funcionar").

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

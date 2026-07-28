# 01 — Requisitos de producto

Fuente: investigación completa en `docs/research/` (11 documentos + resumen). Este documento traduce esa investigación en requisitos accionables, no repite el detalle técnico ya verificado.

## Identidad del proyecto

| | |
|---|---|
| Nombre provisional | Jellyfin Provider Sections |
| Nombre de ensamblado | `JellyProviderSections.Plugin` |
| Namespace raíz | `Jellyfin.Plugin.JellyProviderSections` |
| GUID del plugin | `05cac539-35ae-4f0d-be40-5f0eabd7f43c` |
| Repositorio de código | Nuevo y separado — por defecto `/home/alvaro/Descargas/JellyProviderSections` (decisión ya confirmada) |
| Catálogo de distribución | Compartido con JellyNotify: segunda entrada en `repository/manifest.json` de `jellyfinnotify/JellyNotify`, apuntando a los releases del nuevo repo (patrón real de IAmParadox27: repos de código separados, un único manifest.json de catálogo) |
| Licencia | GPL-3.0-or-later (ver justificación en `research/10-security-and-licensing.md`) |
| Target | Jellyfin **10.11.11**, `net9.0`, `Jellyfin.Controller`/`Jellyfin.Model` 10.11.11 |

## Objetivo

Permitir a un administrador crear un número arbitrario de secciones dinámicas de la página principal de Jellyfin, basadas en catálogo de proveedores de streaming de TMDb (p. ej. "Popular en Crunchyroll", "Novedades en Prime Video"), con logotipo del proveedor junto al título, resolución contra la biblioteca local, y solicitud vía Seerr para lo que falta — sin ningún campo de monetización.

## Alcance del MVP

Incluye: gestión completa de definiciones de sección (crear/editar/duplicar/activar/desactivar/eliminar), integración con Home Screen Sections vía reflexión in-process, logo del proveedor junto al título (solución `displayText` HTML), motor de consultas Discover de TMDb con caché por capas, resolución contra biblioteca local con respeto de permisos por usuario, integración con Seerr (estado + creación de solicitudes, película y serie, con soporte 4K), página de administración con tarjetas cerrada/expandida en el lenguaje visual de JellyNotify.

## Fuera de alcance (explícitamente, por instrucción directa del encargo)

- Cualquier campo, filtro, modelo o criterio de monetización (suscripción, gratis, con anuncios, alquiler, compra, `with_watch_monetization_types` o equivalente).
- Modo mixto películas+series en una misma fila (decisión ya confirmada: se excluye por no existir un algoritmo de mezcla suficientemente determinista, ver `research/05` §Películas y series combinadas).
- Soporte de clientes Jellyfin distintos de Jellyfin Web de escritorio/navegador (es el único cliente que renderiza Home Screen Sections de forma nativa hoy).
- Modificación del código de JellyNotify o de cualquiera de los plugins de terceros (HSS, File Transformation, Pages, Jellyfin Enhanced) — solo integración en tiempo de ejecución.
- Instancia de producción real de Jellyfin/Seerr — todo el desarrollo y prueba ocurre en el entorno aislado `/home/alvaro/Descargas/jellyprovidersections`.

## Clasificación de funciones (MVP obligatorio / versión inicial recomendable / extensión posterior)

| Función | Clasificación | Justificación |
|---|---|---|
| CRUD de definición de sección (crear/editar/duplicar/activar/desactivar/eliminar) | **MVP obligatorio** | Es el núcleo funcional pedido explícitamente en la sección 6 del encargo |
| UUID interno estable, inmutable tras edición | **MVP obligatorio** | Requisito de persistencia de posición en Modular Home (`research/04`), sin el cual todo el valor de "colocar y reordenar" del encargo se rompe |
| Proveedor + id TMDb + nombre + logo | **MVP obligatorio** | Es el eje central del producto |
| Región, idioma de metadatos, tipo de contenido (película/serie) | **MVP obligatorio** | Filtros básicos sin los que Discover no es reproducible |
| Ordenación (popularidad/valoración/fecha) | **MVP obligatorio** | Pedido explícitamente, y `sort_by` ya está mapeado en `research/05` |
| Nº máximo de elementos | **MVP obligatorio** | Control básico de tamaño de fila |
| Géneros incluidos/excluidos, idioma original, país de origen, fechas mín/máx, valoración mín, votos mín, adultos | **MVP obligatorio** | Todos son parámetros reales y ya verificados de `discover/movie`/`discover/tv` (`research/05`); no añaden complejidad arquitectónica nueva, solo campos de formulario y query params |
| Logo del proveedor junto al título | **MVP obligatorio** | Requisito explícito no negociable de la sección 7 del encargo; solución ya encontrada y de bajo coste (`displayText` HTML, `research/08`) |
| Integración con Home Screen Sections (registro/persistencia/reintento) | **MVP obligatorio** | Sin esto no hay producto |
| Resolución contra biblioteca local (contenido disponible vs. externo, respeto de permisos) | **MVP obligatorio** | Requisito explícito de la sección 10 del encargo, mecanismo ya verificado (`research/07`) |
| Integración con Seerr: estado de disponibilidad/solicitud | **MVP obligatorio** | Sin esto, el contenido externo es un callejón sin salida para el usuario |
| Integración con Seerr: crear solicitud (película, serie completa, temporadas) | **MVP obligatorio** | Pedido explícitamente; contrato ya verificado (`research/06`) |
| Soporte 4K en solicitudes | **MVP obligatorio (decisión ya confirmada)** | Es solo una bandera adicional (`is4k`) sobre un contrato ya soportado, sin coste arquitectónico extra |
| Caché por capas (regiones/proveedores/imágenes/resultados/matches locales/estados Seerr) | **MVP obligatorio** | Sin caché, la página principal se bloquea esperando N llamadas HTTP externas por cada carga — inaceptable para un producto que vive en la home |
| Duración de caché configurable por sección + "Limpiar caché" manual | **MVP obligatorio** | Pedido explícitamente en la sección 12 del encargo (Diagnóstico/Limpieza de caché) |
| Página de administración: tarjeta cerrada/expandida | **MVP obligatorio** | Pedido explícitamente en la sección 13 del encargo, con especificación detallada |
| Diagnóstico (estado TMDb/Seerr/HSS, última sync, último error) | **MVP obligatorio** | Pedido explícitamente; patrón ya probado en JellyNotify (`AdminController`/`GET /Admin/diagnostics`) |
| Previsualización de consulta / "probar consulta" | **MVP obligatorio** | Pedido explícitamente en la sección 12 del encargo, y es la única forma práctica de que un admin valide sus filtros sin publicar la sección |
| Selección interactiva de temporadas al solicitar | **Versión inicial recomendable** | El contrato lo soporta (`seasons: number[]`), pero la UI de selección temporada-a-temporada es una superficie de trabajo adicional no crítica para el primer release; el botón principal ("todas las temporadas") cubre el caso de uso mayoritario |
| i18n (en-US/es-ES/ca, como JellyNotify) | **Extensión posterior** | No pedido explícitamente en el encargo; JellyNotify ya tiene el patrón listo para reutilizar cuando se decida |
| Autoactualización (`GitHubReleaseChecker`) | **Extensión posterior** | Comodidad operativa, no funcionalidad central |
| Modo mixto películas+series | **Fuera de alcance (no "extensión posterior")** | Requiere una decisión de diseño de mezcla que la investigación no pudo cerrar con determinismo suficiente; si en el futuro se retoma, necesita su propia investigación de UX, no es una simple extensión incremental |
| Cuotas/aprobación avanzada más allá de lo que ya ofrece Seerr | **Fuera de alcance** | El plugin no reimplementa lógica de permisos/cuota — delega enteramente en Seerr, que ya la resuelve (`research/06`) |

## Criterio de "hecho" para el MVP

Ver `15-acceptance-criteria.md` para el mapeo verificable completo. En resumen: el plugin compila reproduciblemente, se instala junto a HSS en el entorno aislado, registra al menos 3 secciones reales (Crunchyroll/Netflix/Prime Video, España) que sobreviven a un reinicio con su posición y UUID intactos, muestra el logo del proveedor junto al título, resuelve contenido local y externo respetando permisos de biblioteca, permite solicitar película y serie (con selección de temporadas) atribuidas al usuario correcto, se degrada correctamente si TMDb o Seerr no están disponibles, y no expone secretos en ningún punto de la superficie no-admin.

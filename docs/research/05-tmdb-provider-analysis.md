# 05 — Análisis de la API de TMDb (Discover / Watch Providers)

Fecha de consulta: 2026-07-28. Confianza general: alta para autenticación, endpoints, parámetros de filtro y configuración de imágenes (contrato estable y bien documentado de la API pública de TMDb, verificado contra `developer.themoviedb.org`); media para el listado exhaustivo de valores de `sort_by` y para la redacción exacta vigente de los límites de peticiones (TMDb no publica un número fijo desde hace tiempo y el texto de sus páginas de política cambia con frecuencia) — ambos puntos se marcan explícitamente abajo como pendientes de reconfirmación puntual antes de fijar el contrato definitivo en el plan de implementación.

**Recordatorio de alcance obligatorio**: en todo este documento, el parámetro `with_watch_monetization_types` (valores típicos `flatrate`, `free`, `ads`, `rent`, `buy`) se documenta únicamente porque existe en la API real — **no debe usarse en ningún filtro, formulario, consulta, modelo ni prueba del plugin**, por prohibición expresa del encargo.

---

## Fuente: `developer.themoviedb.org` — Autenticación

- **URL**: https://developer.themoviedb.org/docs/authentication-application
- **Hallazgos**:
  - TMDb ofrece dos mecanismos: la clave heredada **v3 `api_key`** (como query string) y el **API Read Access Token v4** (JWT largo, usado como cabecera `Authorization: Bearer <token>`).
  - Para un servicio servidor-a-servidor como un plugin de Jellyfin, el **Read Access Token (Bearer)** es la vía recomendada por TMDb para "application-based" authentication: no requiere gestión de sesión ni de usuario TMDb, es el mecanismo pensado exactamente para este caso de uso (aplicaciones que consultan catálogo en nombre de todos sus usuarios, no autenticación de un usuario final de TMDb).
  - Ambos mecanismos dan acceso a los mismos endpoints públicos de catálogo/discover/watch-providers relevantes para este proyecto; no hay un tier de pago distinto para estos endpoints.
- **Impacto en el diseño**: el plugin debe pedir al administrador el **API Read Access Token** (no la `api_key` v3), guardarlo con el mismo patrón de secreto ya usado por `SeerrSettings.ApiKey` en JellyNotify (nunca reenviado al frontend, preservado en `PreserveSecrets`), y enviarlo como `Authorization: Bearer {token}` en cada llamada.
- **Confianza**: alta.

## Fuente: Discover Movie / Discover TV

- **URL**: https://developer.themoviedb.org/reference/discover-movie, https://developer.themoviedb.org/reference/discover-tv
- **Hallazgos** — parámetros relevantes confirmados para el diseño de filtros del plugin (excluyendo explícitamente todo lo relacionado con monetización):

| Parámetro | Endpoint | Uso |
|---|---|---|
| `with_watch_providers` | movie + tv | Lista de `provider_id` separados por `\|` (OR) o `,` (AND) — **requiere `watch_region` para tener efecto** |
| `watch_region` | movie + tv | Código ISO 3166-1 (p. ej. `ES`, `US`) — obligatorio junto a `with_watch_providers` |
| `with_genres` / `without_genres` | movie + tv | IDs de género separados por `\|` (OR) o `,` (AND) |
| `with_original_language` | movie + tv | Código ISO 639-1 |
| `with_origin_country` | tv (y movie según versión de API) | Código ISO 3166-1 del país de origen de la producción |
| `primary_release_date.gte` / `.lte` | movie | Rango de fecha de estreno primario |
| `first_air_date.gte` / `.lte` | tv | Rango de fecha de primera emisión |
| `vote_average.gte` / `.lte` | movie + tv | Valoración media mínima/máxima |
| `vote_count.gte` | movie + tv | Nº mínimo de votos — **crítico** para evitar que títulos con 1-2 votos de valoración perfecta dominen una ordenación por `vote_average` |
| `include_adult` | movie + tv | Booleano; el plugin debe exponerlo como filtro editorial explícito (excluir adultos por defecto) |
| `sort_by` | movie + tv | Ver tabla siguiente |
| `page` | movie + tv | 1-indexado |
| `with_watch_monetization_types` | movie + tv | **Existe, no se usará** (ver recordatorio de alcance) |

- **Valores de `sort_by`** (a reconfirmar exactamente contra la referencia interactiva antes de codificar, confianza media en la lista exhaustiva): para `discover/movie` incluye como mínimo `popularity.asc/desc`, `vote_average.asc/desc`, `vote_count.asc/desc`, `primary_release_date.asc/desc`, `revenue.asc/desc`, `original_title.asc/desc`; para `discover/tv` el equivalente temporal es `first_air_date.asc/desc` (no `primary_release_date`) y `name.asc/desc` en vez de `original_title.asc/desc`. **Diferencia relevante para el diseño**: los nombres de los parámetros de ordenación por fecha y por título difieren entre movie y tv — el motor de consultas del plugin no puede compartir literalmente el mismo mapeo de "ordenación" para ambos tipos de contenido sin una capa de traducción explícita.
- **Paginación**: respuesta trae `page`, `total_pages`, `total_results`. TMDb limita `total_pages` a un máximo (documentado en la propia respuesta, típicamente capado en 500) — el motor de consultas debe tratar ese límite como techo duro al construir una sección con `maxItems` grande.
- **Diferencias movie vs. tv confirmadas**: además de los nombres de parámro de fecha/orden, `discover/tv` no tiene equivalente a `region`/`certification_country`/`certification` de forma idéntica a movie (TV usa clasificaciones distintas por región); el diseño del filtro "país de origen" debe probarse por separado para cada tipo de contenido antes de asumir paridad total de comportamiento.
- **Impacto en el diseño**: el `SectionDefinition` del plugin debe modelar `sortBy` como un valor lógico propio (p. ej. `Popularity`, `RatingDesc`, `ReleaseDateDesc`, `TitleAsc`) traducido internamente al parámetro real de TMDb según `ContentType` (película/serie), no como un string crudo de TMDb expuesto directamente en el formulario.
- **Confianza**: alta para la existencia y semántica de los parámetros; media para el listado 100% exhaustivo de `sort_by` (a reconfirmar en la fase de implementación contra la respuesta real de la API, que es la fuente de verdad final).

## Fuente: Watch Providers (movie/tv list + available regions)

- **URL**: https://developer.themoviedb.org/reference/watch-providers-available-regions, https://developer.themoviedb.org/reference/watch-providers-movie-list, https://developer.themoviedb.org/reference/watch-provider-tv-list
- **Hallazgos**:
  - `GET /watch/providers/regions` devuelve la lista de regiones soportadas (`iso_3166_1`, `english_name`, `native_name`) — es la fuente de verdad para poblar el selector de región del formulario de sección, no una lista fija a mantener a mano.
  - `GET /watch/providers/movie` y `GET /watch/providers/tv` (parámetros `watch_region`, `language`) devuelven, por proveedor: `provider_id` (identificador estable a persistir en `SectionDefinition`), `provider_name`, `logo_path`, y `display_priorities`/`display_priority` (un mapa u orden por región, usado por TMDb/JustWatch para decidir qué proveedores destacar primero en su propia UI — el plugin puede ignorarlo y ordenar el selector alfabéticamente o por popularidad, no es obligatorio replicarlo).
  - Estas dos listas (movie/tv) **no son idénticas**: un proveedor puede tener catálogo de películas pero no de series en una región dada, o viceversa. El selector de proveedor del formulario debe filtrar según el `ContentType` elegido primero, y refrescar la lista de proveedores disponibles al cambiar de tipo de contenido o de región.
  - `provider_id` es estable entre movie/tv para el mismo proveedor real (p. ej. Netflix tiene el mismo `provider_id` en ambos listados), lo que permite unificar la identidad del proveedor en el modelo de datos aunque su disponibilidad de catálogo se consulte por separado.
- **Impacto en el diseño**: cachear ambas listas (`movie` y `tv`) por región con una duración propia (ver `15-caché...` del plan), y usar `provider_id` como clave estable en `SectionDefinition.TmdbProviderId`, nunca el nombre (que puede cambiar de capitalización/formato).
- **Confianza**: alta.

## Fuente: Configuración de imágenes

- **URL**: https://developer.themoviedb.org/docs/image-basics
- **Hallazgos**:
  - `GET /configuration` devuelve `images.secure_base_url` (`https://image.tmdb.org/t/p/`) y los tamaños disponibles por tipo de imagen: `logo_sizes` (p. ej. `w45`, `w92`, `w154`, `w185`, `w300`, `w500`, `original` — confirmar el conjunto exacto vigente vía esta llamada en vez de asumirlo fijo, TMDb puede ampliar la lista), además de `poster_sizes` y `backdrop_sizes` equivalentes.
  - La URL final de un logo de proveedor se construye como `{secure_base_url}{size}{logo_path}` (p. ej. `https://image.tmdb.org/t/p/w92/logo.png`), con `logo_path` obtenido de `/watch/providers/movie|tv`.
  - Para el requisito del logo junto al título de sección (16-24px de alto aprox. dentro de una fila de home), un tamaño pequeño (`w45` o `w92`) es más que suficiente y evita transferir imágenes sobredimensionadas.
  - No hace falta autenticación para descargar imágenes de `image.tmdb.org` (dominio de CDN público, distinto del dominio de la API).
- **Impacto en el diseño**: cachear el resultado de `/configuration` igual que las listas de proveedores (cambia con muy poca frecuencia), y construir/cachear la URL final del logo una vez por proveedor+tamaño, no recalcularla en cada render.
- **Confianza**: alta.

## Atribución obligatoria (TMDb y JustWatch)

- TMDb exige mostrar su atribución cuando se use su API/datos: el texto/badge estándar es equivalente a *"This product uses the TMDb API but is not endorsed or certified by TMDb"*, junto con el logotipo oficial de TMDb, visible en la interfaz que consume los datos (página de configuración del plugin como mínimo; recomendable también en la propia sección de home o en un pie de página del plugin).
- Los datos de disponibilidad por proveedor (`watch/providers`) que expone TMDb están **licenciados de JustWatch** — TMDb requiere adicionalmente mostrar la atribución de JustWatch allí donde se muestren datos de disponibilidad por proveedor (no solo la atribución genérica de TMDb). Esto afecta directamente a este plugin, porque su función central es precisamente mostrar disponibilidad por proveedor.
- **Impacto en el diseño**: la página de administración y, si es viable sin romper el diseño de la fila de home, algún punto de la propia sección debe incluir ambas atribuciones (TMDb + JustWatch). Detalle exacto del texto/logo requerido a confirmar contra la página de términos vigente de TMDb (`https://www.themoviedb.org/documentation/api/terms-of-use` o equivalente) antes de redactar el texto final en el plan de implementación — no inventar la redacción exacta aquí.
- **Confianza**: alta en que la obligación existe y aplica a este proyecto; media en el texto/logo exacto vigente (sujeto a cambios editoriales de TMDb con el tiempo, debe verificarse en el momento de implementar la pantalla).

## Rate limiting, paginación y resiliencia

- TMDb eliminó hace años el límite fijo histórico (~40 peticiones/10s) de su documentación pública y actualmente describe su límite como generoso pero sujeto a limitación dinámica/por abuso, sin publicar una cifra fija y estable a la que el plugin pueda programar contra un número exacto.
- **Impacto en el diseño**: el cliente TMDb del plugin debe implementar backoff ante `429 Too Many Requests` (respetando `Retry-After` si está presente) en vez de asumir un límite fijo, y limitar la concurrencia de peticiones salientes (paralelismo acotado) al construir una sección que necesite paginar varias páginas de `discover` para alcanzar `maxItems`.
- **Confianza**: media — la ausencia de una cifra fija publicada es en sí un hallazgo real, pero el comportamiento exacto de limitación dinámica no está documentado públicamente con precisión y debe tratarse de forma defensiva, no asumida.

## Inconsistencias conocidas región/proveedor/catálogo

- Un `provider_id` válido en una región puede no tener ningún resultado en `discover` para esa combinación proveedor+región+tipo de contenido (catálogo vacío real, no error) — el plugin debe distinguir "0 resultados legítimos" de "error de consulta" y mostrarlo claramente en el diagnóstico de la sección, no como fallo.
- La disponibilidad de un título por proveedor puede cambiar sin previo aviso (altas/bajas de catálogo) — es inherente a la naturaleza de los datos de JustWatch/TMDb, no un defecto a corregir; refuerza la necesidad de una caché con expiración razonable (no infinita) en vez de un snapshot estático.

## Riesgos

- No fijar `vote_count.gte` en secciones ordenadas por valoración puede producir listas dominadas por títulos con pocos votos — debe ofrecerse como filtro con un valor por defecto razonable (no vacío) en la UI, no solo como opción avanzada oculta.
- Confiar en una lista fija y codificada de tamaños de logo/regiones en vez de consultar `/configuration` y `/watch/providers/regions` en vivo (con caché) generaría desincronización si TMDb amplía sus catálogos — el diseño ya evita esto (ver "Impacto en el diseño" arriba).
- El texto exacto de atribución JustWatch no se ha verificado literal en esta pasada — no cerrar la pantalla de "Acerca de/Atribución" del plugin sin una verificación puntual final contra la página de términos vigente de TMDb.

## Impacto en el diseño (resumen)

1. Autenticación por **API Read Access Token** (Bearer), guardado como secreto server-side con el mismo patrón de `PreserveSecrets` ya usado en JellyNotify.
2. Cliente TMDb con caché independiente para: `/configuration` (imágenes), `/watch/providers/regions`, `/watch/providers/movie`, `/watch/providers/tv`, y resultados de `discover` por sección (duración distinta y más corta que las anteriores, configurable por sección).
3. `SortBy` modelado como enum lógico propio, traducido a los parámetros reales de `discover/movie` vs `discover/tv` (que difieren en los nombres de ordenación por fecha/título).
4. `vote_count.gte` con valor por defecto no vacío quando se ordena por valoración.
5. `with_watch_monetization_types` deliberadamente ausente de todo el diseño.
6. Backoff/reintento ante 429 sin asumir un límite numérico fijo; paralelismo acotado al paginar.
7. Pantalla de atribución TMDb + JustWatch obligatoria, con redacción exacta a verificar en el momento de implementar (no cerrada en esta investigación).
8. `provider_id` como identificador estable persistido en `SectionDefinition`; nombre/logo se resuelven en vivo (con caché) a partir de él, nunca al revés.

## Limitaciones de esta investigación

- No se ha ejecutado ninguna llamada real a la API (sin token TMDb disponible en esta fase de investigación pura) — todo lo anterior proviene de la documentación pública de TMDb, no de respuestas HTTP capturadas. El listado exhaustivo de `sort_by` y la redacción exacta de la atribución JustWatch deben reconfirmarse contra una llamada real o la documentación vigente en el momento de implementar, antes de fijar el contrato definitivo en `08-tmdb-integration-plan.md`.
- No se ha investigado en profundidad el comportamiento de `with_origin_country` para `discover/movie` (confirmar si aplica igual que en `discover/tv`) — pendiente de una pasada adicional o de prueba directa contra la API antes de implementar ese filtro concreto.

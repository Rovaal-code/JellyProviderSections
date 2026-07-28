# 08 — Plan de integración TMDb

Fuente de contrato: `docs/research/05-tmdb-provider-analysis.md` (no reinvestigado aquí). Decisiones cerradas respetadas: repo nuevo `/home/alvaro/Descargas/JellyProviderSections`, net9.0/Jellyfin 10.11.11, cero monetización, 4K en MVP (vía Seerr, no TMDb).

## 1. Cliente TMDb

**Decisión (2026-07-29, sustituye la propuesta original de esta sección):** autenticación por **API Key v3** (`api_key` como query string), no por Read Access Token v4 (Bearer). El usuario ya aportó una API Key v3 real para el entorno de pruebas; `research/05-tmdb-provider-analysis.md` confirma que ambos mecanismos dan acceso exactamente a los mismos endpoints de Discover/Watch Providers/imágenes que necesita el plugin, así que no hay pérdida de funcionalidad, solo una forma de auth más simple (query string en vez de `DelegatingHandler` con cabecera `Authorization`).

`ITmdbApiClient` / `TmdbApiClient` (HttpClient tipado, mismo patrón `AddHttpClient<I,T>` que `PluginServiceRegistrator` de JellyNotify):

```csharp
Task<TmdbConfiguration> GetConfigurationAsync(CancellationToken ct);
Task<IReadOnlyList<TmdbRegion>> GetWatchProviderRegionsAsync(CancellationToken ct);
Task<IReadOnlyList<TmdbWatchProvider>> GetWatchProvidersAsync(TmdbContentType type, string? watchRegion, CancellationToken ct);
Task<TmdbDiscoverPage> DiscoverAsync(SectionDefinition section, int page, CancellationToken ct);
```

- Auth: `?api_key={ApiKey}` añadido a cada URL de petición (helper centralizado, no repetido a mano por llamada).
- `ApiKey` guardado en `PluginConfiguration.TmdbSettings.ApiKey`, mismo patrón `PreserveSecrets` que `SeerrSettings.ApiKey` (enmascarado en `GET /Admin/config`, no reenviado por el frontend, preservado si llega vacío).
- Botón "Probar conexión" en la UI de admin → `GET /configuration` como healthcheck barato.

## 2. Motor de consultas Discover

`SectionDefinition` (definido en `03-data-model.md`) expone los campos lógicos: `ContentType` (Movie/Series), `TmdbProviderId`, `Region`, `SortBy` (enum lógico), `IncludeGenres[]`, `ExcludeGenres[]`, `OriginalLanguage`, `OriginCountry`, `MinDate`, `MaxDate`, `MinRating`, `MinVoteCount`, `IncludeAdult`, `MaxItems`.

Traducción a parámetros reales (`DiscoverQueryBuilder`, una clase pura sin I/O, testeable por unidad):

| Campo lógico | `discover/movie` | `discover/tv` |
|---|---|---|
| `TmdbProviderId` + `Region` | `with_watch_providers`, `watch_region` | igual |
| `SortBy.Popularity` | `popularity.desc` | `popularity.desc` |
| `SortBy.RatingDesc` | `vote_average.desc` | `vote_average.desc` |
| `SortBy.ReleaseDateDesc` | `primary_release_date.desc` | `first_air_date.desc` |
| `SortBy.TitleAsc` | `original_title.asc` | `name.asc` |
| `MinDate`/`MaxDate` | `primary_release_date.gte/lte` | `first_air_date.gte/lte` |
| `IncludeGenres`/`ExcludeGenres` | `with_genres`/`without_genres` | igual |
| `OriginalLanguage` | `with_original_language` | igual |
| `OriginCountry` | `with_origin_country` (marcar como **no verificado para movie**, ver riesgo abajo) | `with_origin_country` (confirmado) |
| `MinRating` | `vote_average.gte` | igual |
| `MinVoteCount` | `vote_count.gte`, **valor por defecto no vacío** (propuesta: 20) si `SortBy` es de valoración | igual |
| `IncludeAdult` | `include_adult` | igual |

`with_watch_monetization_types` no existe en `SectionDefinition` ni en `DiscoverQueryBuilder` — no hay ningún punto del código donde pueda añadirse sin tocar deliberadamente el builder.

## 3. Paginación y deduplicación

- Bucle: pedir páginas incrementales hasta acumular `MaxItems` resultados válidos o alcanzar `total_pages` (techo duro devuelto por TMDb, ~500).
- Deduplicar por `id` de TMDb dentro de una misma ejecución (por si una página se repite tras un cambio de catálogo entre llamadas).
- Paralelismo acotado: máximo 2 páginas en vuelo simultáneas por sección (evita ráfagas contra TMDb al refrescar muchas secciones a la vez).

## 4. Caché por capas (duraciones concretas, ajustables por configuración global del plugin)

| Recurso | Duración por defecto | Invalidación |
|---|---|---|
| `/configuration` | 24 h | Automática por expiración; sin botón dedicado (cambia rarísima vez) |
| `/watch/providers/regions` | 24 h | Automática |
| `/watch/providers/movie` y `/watch/providers/tv` (por región) | 12 h | Automática + botón "Refrescar proveedores" en el selector del formulario |
| Resultado `discover` por sección | **6 h por defecto, configurable por sección** (`SectionDefinition.CacheDurationMinutes`) | Automática + botón "Limpiar caché" en la tarjeta expandida (sección 13 del encargo) |
| URL final de logo (`{secure_base_url}{size}{logo_path}`) | Igual que la entrada de proveedor que la originó (12 h) | Junto con la anterior |

Todas las cachés en memoria (`IMemoryCache`, como ya usa el resto del ecosistema HSS/collection-sections), sin necesidad de persistencia en disco — se reconstruyen solas al expirar o al reiniciar Jellyfin.

## 5. Resiliencia

- `429`: backoff exponencial con jitter, respetando `Retry-After` si está presente; sin asumir un número fijo de req/s (TMDb no publica uno estable).
- Timeout por request: 10s (mismo orden de magnitud que los clientes *arr de JellyNotify).
- Reintentos: máximo 2 reintentos automáticos solo para errores transitorios (`5xx`, timeout, `429`); nunca reintentar `4xx` de validación (p. ej. `401` por token inválido).
- Distinguir explícitamente "0 resultados legítimos" (200 con `total_results: 0`) de "error de consulta" (cualquier excepción/status ≥400) en el diagnóstico por sección — nunca mostrar ambos casos igual en la UI.

## 6. Atribución TMDb + JustWatch

- Sección fija "Acerca de / Atribución" en la página de configuración del plugin (pestaña General), con el logo oficial de TMDb y el texto de atribución.
- **Texto exacto marcado explícitamente como pendiente de verificación puntual** contra `https://www.themoviedb.org/documentation/api/terms-of-use` (o la URL vigente en el momento de implementar) antes de cualquier release pública — no se fija aquí una redacción definitiva, solo el placeholder y la obligación.
- Misma sección debe incluir la atribución de JustWatch (los datos de disponibilidad por proveedor están licenciados de JustWatch), con el mismo criterio de verificación puntual del texto exacto.
- No se considera bloqueante para redactar el resto del plan; sí es un criterio de aceptación de release (ver `15-acceptance-criteria.md`).

## 7. Identificadores estables

- `SectionDefinition.TmdbProviderId` (int) es la única clave persistida para el proveedor — nunca el nombre.
- Nombre y logo del proveedor se resuelven en vivo contra la caché de `watch/providers/{movie|tv}` de la región de la sección; si el proveedor deja de existir en esa región (catálogo retirado), el diagnóstico de la sección debe mostrarlo como advertencia, no como error duro, y la sección debe seguir registrada (con 0 resultados) en vez de desaparecer silenciosamente.

## 8. Riesgo abierto no cerrado en la investigación

`with_origin_country` en `discover/movie` no se confirmó con la misma certeza que en `discover/tv` (ver `05-tmdb-provider-analysis.md`, limitaciones). Acción: verificar con una llamada real contra la API (ya con token disponible en el entorno de pruebas) antes de exponer el filtro "país de origen" para películas en la UI; si no aplica, ocultar ese filtro solo para `ContentType.Movie` en vez de asumir paridad.

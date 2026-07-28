# 01 — Inventario de fuentes

Fecha de consulta (todas las fuentes): **2026-07-28**. Este inventario agrega, sin repetir el detalle, todas las fuentes inspeccionadas en `02` a `08`. Para el hallazgo completo de cada fuente, ver el documento referenciado en la columna "Detalle".

| # | Fuente | Tipo | URL / ruta | Rama | Tag/commit inspeccionado | Licencia | Confianza | Detalle |
|---|---|---|---|---|---|---|---|---|
| 1 | JellyNotify (proyecto local) | Código local, referencia visual/arquitectónica | `/home/alvaro/Descargas/jellyfinnotify/JellyNotify` | `main` | HEAD (`7d21f12`, limpio) | GPL-3.0-or-later | Alta (lectura directa) | `02`, `09` |
| 2 | jellyfin/jellyfin | Servidor | https://github.com/jellyfin/jellyfin | — | tag `v10.11.11`, commit `1fbd8739292cce610231be93daf43368733edf63` | GPL-2.0 | Alta | `07` |
| 3 | jellyfin/jellyfin-web | Cliente web | https://github.com/jellyfin/jellyfin-web | — | no clonado directamente; licencia verificada vía `gh repo view` | GPL-2.0 | Alta (licencia); media (comportamiento DOM, no clonado) | `10`, `08` (riesgo DOM) |
| 4 | jellyfin/jellyfin-plugin-template | Plantilla de plugin | https://github.com/jellyfin/jellyfin-plugin-template | `master` (sin tags) | commit del `master` en fecha de consulta | GPL-3.0 | Alta (lo citado); media (estabilidad futura, rama no fijada) | `07` |
| 5 | jellyfin-plugin-home-sections | Plugin — HSS | https://github.com/IAmParadox27/jellyfin-plugin-home-sections | main | tag `2.5.11.0`, commit `3b02d90e3c405d63181127fb31d0266a0192525b` | GPL-3.0 | Alta | `04`, `08` |
| 6 | jellyfin-plugin-collection-sections | Plugin — precedente arquitectónico | https://github.com/IAmParadox27/jellyfin-plugin-collection-sections | main | tag `2.3.10.0`, commit `d30740b5575c3b730580fb3a260a4b0c98926dfa` | GPL-3.0 | Alta | `04` |
| 7 | jellyfin-plugin-file-transformation | Plugin — dependencia transitiva | https://github.com/IAmParadox27/jellyfin-plugin-file-transformation | main | tag `2.5.11.0`, commit `5bc7541be72d577a2b13382db124da69babcc162` | GPL-3.0 | Alta | `04` |
| 8 | jellyfin-plugin-pages | Plugin — no usado en MVP | https://github.com/IAmParadox27/jellyfin-plugin-pages | main | tag `2.4.11.0`, commit `352eed217fe8d762c9105a4bd189b685d6be88be` | GPL-3.0 | Alta | `04` |
| 9 | Jellyfin Enhanced (n00bcodr) | Plugin — referencia de inyección frontend | https://github.com/n00bcodr/Jellyfin-Enhanced | — | copia vendorizada local en `Jellyfin-Enhanced/` (con `.git` propio) | GPL-3.0 | Alta (código local real) | `08` |
| 10 | JellyBridge (kinggeorges12) | Proyecto comparable — descartado como base | https://github.com/kinggeorges12/JellyBridge | — | tag `v3.3` | GPL-3.0 | Alta | `08` |
| 11 | seerr-team/seerr | Servicio de solicitudes | https://github.com/seerr-team/seerr | `develop` (default) | tag `v3.4.0` inspeccionado en profundidad; `v3.3.0` (versión declarada por JellyNotify) confirmada existente | MIT | Alta | `06` |
| 12 | JellyNotify.Plugin/Services/SeerrApiClient.cs (+ modelos) | Código local — cliente Seerr real en producción | `/home/alvaro/Descargas/jellyfinnotify/JellyNotify/JellyNotify.Plugin/Services/SeerrApiClient.cs` y ficheros relacionados | `main` | HEAD | GPL-3.0-or-later | Alta | `06` |
| 13 | TMDb API (developer.themoviedb.org) | API pública — datos de catálogo/proveedores | https://developer.themoviedb.org/ | — | documentación pública vigente en fecha de consulta | N/A (Términos de Uso de API, no licencia de software) | Alta (auth/endpoints/imágenes); media (lista exhaustiva de `sort_by`, redacción exacta de atribución JustWatch, límites de tasa) | `05`, `10` |
| 14 | docs.seerr.dev | Documentación pública de Seerr | https://docs.seerr.dev/ | — | vigente en fecha de consulta | N/A | Media (usada como apoyo, el código real de `seerr-team/seerr` es la fuente primaria) | `06` |

## Fuentes NO inspeccionadas / explícitamente fuera de esta pasada

- **Instancia real de Seerr con `/api-docs`** — no disponible en esta fase de investigación pura (no hay token/instancia desplegada todavía). Ver bloqueante en `11-open-questions-and-readiness.md`.
- **Instancia real de TMDb con token de prueba** — no se ha ejecutado ninguna llamada HTTP real contra la API (sin token disponible en esta fase). Toda la Fuente 13 proviene de documentación pública, no de respuestas capturadas.
- **`jellyfin.org/docs/general/server/plugins/`** — cubierto de forma indirecta por el análisis de código real de `jellyfin/jellyfin` y `jellyfin-plugin-template` (más fiable que la documentación derivada); no se cita como fuente primaria independiente.
- **Jellyfin-Enhanced sitio de documentación (`n00bcodr.github.io/Jellyfin-Enhanced/`)** — no consultado; se usó el código fuente real vendorizado localmente en su lugar (más fiable).
- **Issues/PRs específicos de los repos anteriores** — no se ha hecho una auditoría sistemática de issues abiertas más allá de las mencionadas puntualmente en `08` (issue de fuga JellyBridge→HSS) y en los mensajes de commit citados (p. ej. "Add support for 10.11.11" en collection-sections). Se considera cobertura suficiente para el gate de esta fase, no exhaustiva.

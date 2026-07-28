# Plan maestro de implementación — Jellyfin Provider Sections

Fecha: 2026-07-28. Estado del gate: **READY WITH ASSUMPTIONS** (ver `research/11-open-questions-and-readiness.md`). Este documento es el punto de entrada único para implementar el proyecto sin repetir la investigación — enlaza y resume `01` a `15` de este directorio, que a su vez se apoyan en `research/01` a `research/11` (código fuente real citado, no README). Un agente que implemente a partir de aquí no necesita releer la investigación salvo para verificar una cita concreta.

## 1. Objetivo

Construir "Jellyfin Provider Sections", un plugin de Jellyfin que permite a un administrador crear un número arbitrario de secciones dinámicas de home basadas en proveedores de streaming de TMDb (p. ej. "Popular en Crunchyroll"), registradas en Home Screen Sections, resueltas contra la biblioteca local de Jellyfin, y con solicitud de contenido ausente vía Seerr. Detalle completo en `01-product-requirements.md`.

## 2. Alcance

MVP obligatorio (clasificación completa en `01-product-requirements.md`):
- CRUD completo de `SectionDefinition` (todos los campos de la sección 6 del encargo original, ver `03-data-model.md`).
- Registro/re-registro/persistencia de UUID en Home Screen Sections (`06-home-sections-integration-plan.md`).
- Logo del proveedor a la izquierda del título, vía `displayText` HTML (`07-provider-logo-plan.md`).
- Motor de consultas Discover de TMDb con caché (`08-tmdb-integration-plan.md`).
- Resolución de contenido local vía `ILibraryManager`/`ProviderIds`, respetando visibilidad por usuario (`research/07`).
- Creación de solicitudes en Seerr 3.4.0+, incluido 4K y selección de temporadas (`09-seerr-integration-plan.md`).
- Página de administración con tarjetas cerrado/expandido (`05-ui-and-interaction-specification.md`).
- Entorno de pruebas Docker completo con Seerr autoalojado (`10-testing-environment.md`).

## 3. Fuera de alcance

- Cualquier campo, filtro o lógica de monetización (`with_watch_monetization_types` y equivalentes) — prohibición absoluta en todo el proyecto.
- Modo mixto películas+series en una misma fila — sin algoritmo determinista encontrado (`research/09` del encargo original).
- Plugin Pages como dependencia dura — solo Home Screen Sections + File Transformation (transitiva).
- Autoactualización tipo `GitHubReleaseChecker` — extensión posterior.
- i18n multi-idioma del plugin — extensión posterior (JellyNotify soporta en-US/es-ES/ca, no es un requisito de partida aquí).

## 4. Arquitectura

Resumen (detalle en `02-architecture.md`): plugin `net9.0`, `Jellyfin.Controller`/`Jellyfin.Model` 10.11.11, patrón `Plugin : BasePlugin<PluginConfiguration>, IHasWebPages` + `PluginServiceRegistrator : IPluginServiceRegistrator`, configuración XML nativa (`03-data-model.md`), sin base de datos. Dependencia en tiempo de ejecución hacia Home Screen Sections por reflexión (`AssemblyLoadContext.All` → `PluginInterface.RegisterSection`), nunca por `PackageReference` ni por el endpoint HTTP no autenticado.

### Alternativas descartadas

- **Proyecto hermano dentro del repo de JellyNotify** — era la recomendación inicial de la investigación; el usuario decidió explícitamente un **repositorio Git nuevo y separado** (ver §5). El manifest/catálogo de distribución sí sigue siendo el compartido de JellyNotify.
- **Inyección frontend propia (`ScriptInjectionStartupFilter`) como solución principal del logo** — descartada a favor de `displayText` como HTML, que no depende del DOM ni de un `MutationObserver`. Queda documentada como fallback en `07-provider-logo-plan.md`.
- **Endpoint HTTP `POST /HomeScreen/RegisterSection`** para el registro de secciones — descartado por no tener `[Authorize]`; se usa la vía in-process.
- **Base de datos propia** para `SectionDefinition` — descartada; el volumen esperado (decenas de secciones) cabe cómodamente en la configuración XML estándar, mismo patrón ya validado por JellyNotify con `List<ArrInstanceConfig>`.
- **Modo mixto películas+series** — descartado del MVP por falta de un algoritmo de mezcla determinista y coherente.

## 5. Estructura de repositorio y de carpetas

**Código fuente**: repositorio Git nuevo y separado en `/home/alvaro/Descargas/JellyProviderSections` (decisión confirmada por el usuario el 2026-07-28 — no vive dentro de `jellyfinnotify/JellyNotify`). Estructura interna calcada del patrón ya probado por JellyNotify:

```
JellyProviderSections/
├── JellyProviderSections.Plugin/
│   ├── Api/                 # controladores: Sections (CRUD), Diagnostics, Test (TMDb/Seerr)
│   ├── Configuration/       # PluginConfiguration.cs, configPage.html
│   ├── Models/               # SectionDefinition, TmdbModels, SeerrModels (nuevos, no reutilizan los de JellyNotify)
│   ├── Services/              # TmdbClient, SeerrClient, HomeSectionsRegistrar, LibraryResolver, ProviderLogoService, DisplayTextBuilder
│   ├── Store/                 # caché a disco de logos (ver 03-data-model.md)
│   └── Web/                   # CSS (paleta jn-* adaptada) + JS del config page
├── JellyProviderSections.Tests/
├── build.sh                  # adaptado del de JellyNotify, publish→zip→checksum
├── manifest-entry.json       # fragmento a fusionar en el manifest compartido de JellyNotify (no un manifest propio)
├── LICENSE (GPL-3.0-or-later) / NOTICE.md
└── JellyProviderSections.sln
```

**Manifest de distribución**: sigue siendo `repository/manifest.json` de JellyNotify, ampliado con una segunda entrada que apunta a los releases de este nuevo repo — no se crea infraestructura de catálogo nueva (ver `13-packaging-and-release.md`).

**Entorno de pruebas**: `testenv/` (dentro de este repo) (ya existente, vacío), completamente separado de ambos repos de código — ver `10-testing-environment.md`.

## 6. Componentes, servicios, modelos, interfaces

Ver `02-architecture.md` (componentes/servicios) y `03-data-model.md` (`SectionDefinition`, `PluginConfiguration`, `TmdbSettings`, `SeerrSettings`) para las firmas completas. Servicios centrales: `HomeSectionsRegistrar` (registro/re-registro por reflexión), `DisplayTextBuilder` (HTML-encode + construcción del `<img>+<span>`, ver §9), `TmdbDiscoverEngine`, `LibraryResolver`, `SeerrRequestService`, `ProviderLogoService`.

## 7. Endpoints (API del propio plugin)

Ver `04-api-contracts.md` para el contrato completo (CRUD de secciones, probar TMDb/Seerr, previsualizar consulta, limpiar caché, diagnóstico). Todos exigen autorización de administrador (`12-security-and-privacy.md` §6).

## 8. Configuración

`PluginConfiguration` con `SchemaVersion`, `TmdbSettings`, `SeerrSettings`, `List<SectionDefinition>` — persistencia XML nativa, sin base de datos, secretos nunca reenviados al frontend (`PreserveSecrets`, patrón heredado de JellyNotify). Detalle en `03-data-model.md`.

## 9. Flujos

- **Flujo TMDb**: auth Bearer con Read Access Token → caché de regiones/proveedores → construcción de query Discover reproducible por `SectionDefinition` → paginación hasta `MaxItems` → caché de resultados con TTL muy por debajo del límite contractual de 6 meses. Detalle en `08-tmdb-integration-plan.md`.
- **Flujo Jellyfin**: `InternalItemsQuery.HasAnyProviderId = {"Tmdb": id}` (respaldado por índice SQL real) + `IsVisible(user)` como defensa en profundidad antes de exponer cualquier resultado. Detalle en `research/07`.
- **Flujo Seerr**: resolución de identidad Jellyfin→Seerr, creación de solicitud (`POST /api/v1/request`) atribuida al usuario real, soporte 4K y `seasons: "all"` por defecto. Detalle en `09-seerr-integration-plan.md`.
- **Flujo Home Screen Sections**: registro in-process por reflexión al arrancar (`IScheduledTask` con trigger de arranque) y al guardar configuración; UUID estable como clave de persistencia de posición en `ModularHomeUserSettings`. Detalle en `06-home-sections-integration-plan.md`.

## 10. Renderizado del logo

Solución principal: `displayText` como HTML (`<img src="/ProviderSections/Logo/{id}"> <span>{nombre HTML-encodeado}</span>`) al registrar la sección — sin JS propio. Mitigación XSS obligatoria (HTML-encode del nombre, `src` siempre construida server-side desde TMDb). Fallback documentado (no implementado en el MVP): inyección frontend estilo `ScriptInjectionStartupFilter`. Detalle completo y plan de prueba visual en `07-provider-logo-plan.md`.

## 11. Diseño de tarjetas

Estado cerrado uniforme (logo, nombre, proveedor, región, tipo, orden, nº elementos, badges de estado) + estado expandido (todos los campos de `SectionDefinition`, acciones, sin recargar página, `aria-expanded`). Paleta `--jn-*` de JellyNotify reutilizada, tipografía de sistema (no las fuentes embebidas). Detalle en `05-ui-and-interaction-specification.md`.

## 12. Caché

Regiones/proveedores TMDb (TTL largo), resultados Discover por sección (TTL configurable por `SectionDefinition.CacheDurationMinutes`, defecto 360 min), estado Seerr por título (TTL corto), logos de proveedor (persistidos a disco, no en memoria). Ningún caché supera el límite contractual de 6 meses de TMDb. Detalle en `08-tmdb-integration-plan.md` y `03-data-model.md`.

## 13. Seguridad

Secretos solo backend con `PreserveSecrets`, mitigación XSS de `displayText` obligatoria y verificada en vivo, SSRF eliminado por diseño para TMDb (host fijo) y mitigado para Seerr (URL de admin + aviso si `IgnoreSslErrors`), autorización de administrador en todos los endpoints de escritura, permisos de biblioteca respetados con `IsVisible(user)`. Checklist completo en `12-security-and-privacy.md`.

## 14. Migraciones

`PluginConfiguration.SchemaVersion` desde el día 1; `Id` de `SectionDefinition` nunca se regenera en una migración (invariante de persistencia de posición en HSS). Detalle en `03-data-model.md` §"Versionado de esquema y migraciones".

## 15. Observabilidad

Página de diagnóstico (solo admin, sanitizada) con estado de conexión TMDb/Seerr, versión de HSS detectada, última sincronización/resultado/error por sección. Logs nunca contienen secretos (`12-security-and-privacy.md` §1).

## 16. Pruebas

Matriz completa (unitarias, integración contra servidor HTTP simulado, E2E de 35 pasos, visuales) en `11-test-matrix.md`, mapeada a los criterios de `15-acceptance-criteria.md`.

## 17. Empaquetado, instalación, actualización, rollback

Build reproducible en Docker (`mcr.microsoft.com/dotnet/sdk:9.0`, sin depender de SDK local), publish→zip→checksum, manifest compartido con JellyNotify ampliado con una segunda entrada. Rollback = reinstalar versión anterior del zip vía el propio manifest (mismo mecanismo nativo de Jellyfin, sin lógica propia adicional). Detalle en `13-packaging-and-release.md`.

## 18. Criterios de aceptación

49 criterios verificables con evidencia obligatoria, mapeados 1:1 sobre la sección 29 del encargo original — ver `15-acceptance-criteria.md`. Ninguno se marca cumplido sin evidencia real adjunta.

---

## 19. Orden de ejecución — 33 fases verificables

Convención por fase: **Objetivo · Archivos · Dependencias · Implementación prevista · Riesgos · Pruebas · Evidencias · Criterio de salida · Rollback · Fases dependientes**. Las fases 1-4 son de repositorio/entorno (no de código del plugin); 5 en adelante son incrementales y cada una debe compilar y probarse antes de pasar a la siguiente (regla de la sección 17 del encargo original: nunca construir todo entero para probar solo al final).

| Fase | Objetivo | Depende de | Riesgo principal (ver `14`) | Criterio de salida |
|---|---|---|---|---|
| 1. Auditoría de JellyNotify | Confirmar patrones reutilizables antes de tocar nada | — | Ninguno (ya hecha) | `research/02` completo — **hecho** |
| 2. Definición de arquitectura | Cerrar diseño técnico | 1 | Ninguno (ya hecho) | `02-architecture.md` completo — **hecho** |
| 3. Preparación del repositorio | Crear `/home/alvaro/Descargas/JellyProviderSections`, `git init`, `.sln`, `.csproj` vacío, `LICENSE` GPL-3.0-or-later | 2 | Ninguno | Repo compila un plugin "hola mundo" en Docker |
| 4. Preparación del entorno de pruebas | Levantar `docker-compose.yml` de `10-testing-environment.md` (Jellyfin + Seerr + perfil build) | 3 | #1, #12 (riesgos de compatibilidad HSS/versión imagen) | `docker compose up` con Jellyfin y Seerr sanos (healthcheck verde) |
| 5. Esqueleto del plugin | `Plugin.cs`, `PluginServiceRegistrator.cs`, `IHasWebPages`, GUID propio | 4 | Ninguno | Plugin aparece instalado en el Jellyfin de prueba |
| 6. Modelo de configuración | `PluginConfiguration`, `SectionDefinition`, `TmdbSettings`, `SeerrSettings` (`03-data-model.md`) | 5 | Ninguno | Config page vacía carga sin error, XML se persiste |
| 7. Migraciones | `SchemaVersion`, punto de extensión en `Plugin.cs` | 6 | Ninguno | Test unitario de migración de esquema v0→v1 |
| 8. Cliente TMDb | `TmdbClient` con Bearer token, `TestConnectionAsync` | 6 | Ninguno | Prueba de conexión real contra TMDb con token de prueba |
| 9. Regiones y proveedores | Endpoints watch/providers + caché | 8 | #11 (nombres de sort_by a reconfirmar) | Selector de región/proveedor poblado en la UI |
| 10. Motor de consultas Discover | `TmdbDiscoverEngine`, traducción `SortBy` lógico→real por `ContentType` | 9 | #11 | Resultados reales para una sección de prueba de cada tipo de contenido |
| 11. Resolución contra Jellyfin | `LibraryResolver` con `HasAnyProviderId` + `IsVisible` | 10 | #6 (rendimiento no medido) | Título conocido de la biblioteca sintética resuelto como local; título ausente como externo |
| 12. Cliente Seerr | `SeerrClient` reescrito desde cero (no reutiliza el de JellyNotify tal cual) | 6 | #5 (enums a corregir) | Prueba de conexión + lectura de estado real |
| 13. Identidad y permisos | Resolución Jellyfin→Seerr, atribución de usuario | 12 | Ninguno | Solicitud de prueba atribuida al usuario correcto, no a la API key admin |
| 14. Registro en Home Screen Sections | `HomeSectionsRegistrar` por reflexión + `IScheduledTask` de arranque | 5 | #1, #3 | Sección de prueba visible en Modular Home tras reinicio |
| 15. Representación de contenido externo | Tarjetas de contenido no local con metadatos/imagen TMDb | 11 | Ninguno | Captura de tarjeta externa correcta |
| 16. Solicitudes | Flujo completo de "Solicitar" desde una sección | 13, 15 | Ninguno | Solicitud real visible en Seerr de prueba |
| 17. Logotipo en el título | `DisplayTextBuilder` con HTML-encode obligatorio | 14 | #2 (XSS) — **crítico, no diferir** | Captura real del logo en Jellyfin Web + prueba de `<script>` en el nombre |
| 18. Interfaz administrativa | Página de configuración completa (`05-ui-and-interaction-specification.md`) | 6, 8, 12 | Ninguno | CRUD completo operativo desde la UI |
| 19. Tarjetas cerradas | Estado cerrado uniforme | 18 | Ninguno | 3+ tarjetas con misma estructura, captura |
| 20. Tarjetas expandidas | Estado expandido con todas las acciones | 19 | Ninguno | Expansión sin salto de layout, `aria-expanded`, captura |
| 21. Caché | TTLs por tipo de dato (`12-security-and-privacy.md` §9, `08-tmdb-integration-plan.md`) | 10, 12 | #8 (límite 6 meses) | Segunda carga no repite llamada externa dentro del TTL |
| 22. Resiliencia | Timeouts, reintentos con backoff, degradación sin bloquear home | 8, 12 | Ninguno | Home sigue cargando con TMDb/Seerr caídos simulados |
| 23. Seguridad | Checklist completo de `12-security-and-privacy.md` | 17, 18 | #2, #9 | Checklist en verde con evidencia |
| 24. Pruebas unitarias | Suite completa (`11-test-matrix.md`) | 6-17 | Ninguno | `dotnet test` en verde |
| 25. Pruebas de integración | Contra servidor HTTP simulado (TMDb/Seerr) | 24 | #5, #11 | Suite en verde con casos 200/4xx/5xx/timeout |
| 26. Despliegue de Jellyfin de prueba | Entorno Docker completo con plugin instalado vía manifest real | 4, 17-23 | #1 | Instalación de extremo a extremo reproducible desde cero |
| 27. Pruebas end-to-end | 35 pasos de `11-test-matrix.md` | 26 | Todos | Informe E2E completo con capturas |
| 28. QA visual | Capturas tema claro/oscuro/responsive comparadas con JellyNotify | 27 | Ninguno | Capturas archivadas en `evidence/` |
| 29. Compatibilidad | Verificación empírica HSS 2.5.11.0 vs Jellyfin 10.11.11 | 26 | #1 — **cierra el riesgo más alto de la matriz** | Confirmado o corregido con versión alternativa |
| 30. Empaquetado | `13-packaging-and-release.md` — zip, checksum, entrada de manifest | 24-29 | Ninguno | Paquete instalable generado |
| 31. Documentación | README del nuevo repo + este directorio de implementación | 30 | Ninguno | README completo, sin secretos |
| 32. Release candidate | Instalación desde el manifest compartido de JellyNotify en un Jellyfin limpio | 30, 31 | Ninguno | Instalación limpia exitosa |
| 33. Validación final | Los 49 criterios de `15-acceptance-criteria.md` en verde con evidencia | 1-32 | — | Checklist completo, proyecto listo para uso real |

## 20. Evidencias requeridas (resumen)

Toda evidencia se guarda en `jellyprovidersections/evidence/` (logs con timestamp, capturas con nombre descriptivo, salidas de test). Ver el detalle exacto por fase en `11-test-matrix.md` y la regla de cierre en `15-acceptance-criteria.md` (nunca marcar un criterio cumplido sin evidencia real adjunta).

## 21. Siguiente paso inmediato

Fase 3 (preparación del repositorio) requiere autorización explícita del usuario para empezar a programar — esta fase de planificación termina aquí. Cuando el usuario lo autorice: `git init` en `/home/alvaro/Descargas/JellyProviderSections`, esqueleto `.sln`/`.csproj`, y verificación de que compila en el contenedor `dotnet/sdk:9.0` antes de escribir una sola línea de lógica de negocio — primera evidencia de la fase 3.

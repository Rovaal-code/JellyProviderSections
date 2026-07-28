# Research Summary — Jellyfin Provider Sections

Fecha: 2026-07-28. Estado: **READY WITH ASSUMPTIONS**.

Este documento es el punto de entrada a la investigación. El detalle completo, con código citado línea a línea, está en `01` a `11` en este mismo directorio.

## Qué se investigó

1. `01-source-inventory.md` — 14 fuentes primarias (repos + API + código local), todas con fecha/rama/tag/commit/licencia confirmados.
2. `02-local-project-analysis.md` — arquitectura completa de JellyNotify (patrón de plugin, config, secretos, inyección web, sistema visual) y qué es directamente reutilizable.
3. `03-compatibility-matrix.md` — versiones cruzadas de todo el ecosistema; sin incompatibilidades duras encontradas.
4. `04-home-screen-sections-integration.md` — contrato real de registro/persistencia de secciones externas en HSS (código fuente, no README).
5. `05-tmdb-provider-analysis.md` — contrato completo de Discover/Watch Providers/imágenes de TMDb, con exclusión explícita de monetización.
6. `06-seerr-api-analysis.md` — contrato real de creación de solicitudes, permisos, cuotas, idempotencia, comparado contra el cliente ya en producción de JellyNotify.
7. `07-jellyfin-library-resolution.md` — mecanismo real de `ILibraryManager`/`ProviderIds`/permisos por usuario en Jellyfin 10.11.11.
8. `08-provider-logo-rendering.md` — **solución encontrada y verificada por código** para el requisito del logo junto al título de sección.
9. `09-ui-reference-analysis.md` — tokens de diseño y patrones de componente reutilizables de JellyNotify.
10. `10-security-and-licensing.md` — matriz de licencias + hallazgo de seguridad nuevo (XSS vía `displayText`) + mitigación.
11. `11-open-questions-and-readiness.md` — inventario disponible/inferido/opcional/bloqueante y preguntas agrupadas.

## Los tres hallazgos más importantes

1. **El logo junto al título tiene solución nativa, sin inyección frontend propia.** `displayText` se renderiza como `innerHTML` sin escapar en Home Screen Sections — el plugin puede enviar `<img>+<span>` directamente al registrar la sección. Cero JS propio, cero `MutationObserver`, cero riesgo de carrera con el DOM. Esto **invierte la complejidad esperada** del requisito más difícil del encargo (sección 7 del prompt original). La inyección frontend (patrón `ScriptInjectionStartupFilter` de JellyNotify) queda como fallback documentado, no como solución principal.
2. **El registro en HSS es 100% en tiempo de ejecución, por reflexión sobre `AssemblyLoadContext.All`, nunca por `PackageReference`.** El UUID de sección sobrevive a reinicios/ediciones porque HSS lo usa como clave en `ModularHomeUserSettings` y en `PluginConfiguration.SectionSettings[]` — pero el plugin es responsable de volver a registrar todas las secciones en cada arranque (HSS no persiste el registro de terceros, solo el orden/estado). Patrón validado en producción por `jellyfin-plugin-collection-sections`, el precedente arquitectónico más cercano que existe hoy.
3. **El cliente Seerr ya en producción en JellyNotify cubre solo lectura.** Comparado línea a línea contra el código real de `seerr-team/seerr` v3.4.0: falta por completo la creación de solicitudes, dos enums locales están incompletos/invertidos respecto al contrato real, y falta el endpoint clave para resolver identidad Jellyfin→Seerr sin listar todos los usuarios. Ninguno de estos es un bloqueo — todos están documentados con el contrato exacto a implementar.

## Qué falta para poder construir y probar (no para cerrar el diseño)

- TMDb API Read Access Token (credencial del usuario).
- Una instancia Seerr de prueba — o incluirla en el propio `docker-compose.yml` del entorno aislado (no requiere nada del usuario si se elige esta vía).
- Confirmación de 6 decisiones reversibles con valor por defecto ya propuesto (ver `11-open-questions-and-readiness.md`).

## Siguiente entregable

`docs/implementation/` (16 documentos + `master-implementation-plan.md`), a redactar en cuanto el usuario resuelva las preguntas agrupadas de `11-open-questions-and-readiness.md` o autorice proceder con los valores recomendados.

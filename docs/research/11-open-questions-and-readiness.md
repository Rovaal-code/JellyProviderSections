# 11 — Preguntas abiertas y gate de preparación

Fecha: 2026-07-28.

## Estado de preparación: **READY WITH ASSUMPTIONS**

No se ha encontrado ningún bloqueo que cambie sustancialmente la arquitectura. Todos los contratos técnicos centrales (registro en Home Screen Sections, solución del logo, filtros Discover de TMDb, creación de solicitudes en Seerr, resolución de biblioteca de Jellyfin) están verificados con código fuente real, con alta confianza. Lo que falta son (a) credenciales para poder *ejecutar* pruebas reales — no para cerrar el diseño — y (b) un pequeño número de decisiones reversibles con un valor por defecto razonable ya propuesto.

## Disponible (no requiere respuesta del usuario)

- Rutas reales confirmadas: proyecto (`/home/alvaro/Descargas/jellyfinnotify/JellyNotify`, git limpio, rama `main`) y entorno de pruebas (`testenv/` (dentro de este repo), vacío).
- Docker 29.6.1 y Docker Compose 5.1.4 disponibles. `gh` CLI disponible. `dotnet` SDK **no** instalado en el host (ver bloqueante técnico menor abajo, con solución ya identificada).
- Arquitectura de CPU: `x86_64`.
- Versión objetivo de Jellyfin: **10.11.11** (verificada como release real, con paquetes NuGet `Jellyfin.Controller`/`Jellyfin.Model` 10.11.11 publicados) — misma que ya usa JellyNotify en producción.
- Contrato completo de registro/persistencia/logo de Home Screen Sections (código real, ver `04`/`08`).
- Contrato completo de Discover/Watch Providers/imágenes de TMDb (documentación pública real, ver `05`).
- Contrato completo de creación de solicitudes, estados, idempotencia y atribución de usuario de Seerr (código real de `seerr-team/seerr`, ver `06`), comparado línea a línea contra el cliente ya en producción en JellyNotify.
- Mecanismo real de `ILibraryManager`/`ProviderIds`/permisos de biblioteca por usuario en Jellyfin 10.11.11 (código real, ver `07`).
- Licencias de todos los repos involucrados (ver `10`).
- Sistema visual completo de JellyNotify (tokens CSS, tipografía, patrón de tarjeta) reutilizable como base (ver `02`/`09`).

## Inferida y pendiente de validación (no bloqueante, se confirma en el entorno de pruebas)

- Compatibilidad exacta de HSS `2.5.11.0` con Jellyfin 10.11.11 (inferida de un commit de un plugin hermano, no probada en ejecución).
- Persistencia real de posición/UUID tras un reinicio completo de Jellyfin (verificado por lectura de código, no observado en vivo).
- Comportamiento exacto de una sección "huérfana" tras dejar de re-registrarla.
- Que `displayText` siga renderizándose sin escapar en la versión de HSS realmente instalada en el entorno de pruebas (la solución principal del logo depende de esto).
- Rendimiento real de la consulta `HasAnyProviderId` sobre una biblioteca grande (el índice SQL existe por diseño, pero no se ha medido).

## Opcional (mejora el resultado, no bloquea el plan)

- i18n del nuevo plugin (JellyNotify soporta en-US/es-ES/ca) — se puede añadir después.
- Autoactualización tipo `GitHubReleaseChecker` de JellyNotify.
- Soporte 4K en Seerr (existe en el contrato, es una simple bandera adicional).

## Bloqueante técnico menor (con solución ya identificada, no requiere decisión del usuario)

- No hay SDK de .NET instalado en este host (`dotnet` no encontrado; `~/.dotnet` solo contiene sentinels vacíos). **Solución recomendada**: compilar dentro de un contenedor Docker (`mcr.microsoft.com/dotnet/sdk:9.0`) en vez de depender de un SDK local — ya viable porque Docker está disponible. No bloquea el plan, se documentará como parte de `10-testing-environment.md`.

## Preguntas para el usuario (agrupadas en una sola solicitud)

### Bloqueantes para poder *ejecutar* el entorno de pruebas (no para cerrar el diseño)

1. **TMDb API Read Access Token** — necesario para levantar el entorno Docker y probar de verdad las consultas Discover/Watch Providers. Finalidad: autenticar el cliente TMDb del plugin (`Authorization: Bearer <token>`). Dónde se usará: guardado server-side en la configuración del plugin (enmascarado, nunca en frontend/logs/capturas), y en el `.env`/secreto del entorno Docker de pruebas (nunca versionado). Cómo proporcionarlo de forma segura: pégalo cuando lo pidas en el chat solo si estás cómodo, o indícame si prefieres introducirlo tú mismo directamente en el fichero de configuración del entorno de pruebas una vez lo prepare (recomendado) — en ese caso yo dejaré el placeholder y tú rellenas el valor real fuera de este chat.
2. **URL y API key de una instancia Seerr de prueba** (no de producción) — necesarias para probar de verdad la creación de solicitudes. Si no tienes una instancia de prueba todavía, puedo incluir Seerr en el propio `docker-compose.yml` del entorno aislado (recomendado, evita tocar cualquier instancia real) — confírmame si prefieres esa opción o conectar contra una instancia externa que ya tengas.

### Decisiones reversibles con valor por defecto ya propuesto (dime si alguna debe cambiar; si no respondes, se implementa con el valor recomendado)

3. **Versión de Seerr objetivo**: **3.3.0** (paridad exacta con lo que JellyNotify ya declara verificado) o **3.4.0+** (añade `ignoreQuota`, permite que un admin salte la cuota de un usuario al crear una solicitud en su nombre). Recomendado: **3.4.0+** como mínimo, ya que es compatible hacia atrás y añade una función útil sin coste.
4. **Ubicación del nuevo plugin**: como proyecto hermano dentro del mismo repositorio de JellyNotify (`JellyProviderSections.Plugin/` junto a `JellyNotify.Plugin/`, compartiendo `manifest.json`/`repository/manifest.json` con una segunda entrada), sin tocar el código de JellyNotify. Recomendado: **sí**, es justo lo que sugiere el encargo original (ruta doble función) y ya hay un patrón de build/manifest probado que generalizar. Alternativa: repositorio Git completamente separado.
5. **Identidad visual**: reutilizar literalmente la paleta y tipografía de JellyNotify (duplica ~120KB de fuentes embebidas en el CSS del nuevo plugin) o usar la misma paleta de color pero con tipografía del sistema (más ligero). Recomendado: **misma paleta, tipografía del sistema** — mantiene la identidad de familia (color, glass, bordes en gradiente) sin duplicar el coste de descarga de fuentes custom.
6. **Preferencia de solicitud de series por defecto**: primera temporada / todas las temporadas / selección interactiva. El contrato de Seerr soporta las tres con el mismo parámetro (`seasons: number[] | "all"`). Recomendado: **todas las temporadas** por defecto en el botón principal, con opción de selección interactiva como acción secundaria si el usuario quiere elegir.
7. **Soporte 4K en el MVP**: sí/no. Recomendado: **incluirlo desde el MVP** — es solo una bandera adicional (`is4k`) en el mismo contrato ya investigado, sin coste arquitectónico extra.
8. **Modo mixto películas+series en una misma fila**: la investigación no encontró un algoritmo de mezcla suficientemente determinista y coherente (ver alcance del encargo original, sección 9). Recomendado: **excluir del MVP**, ofrecer solo secciones separadas por tipo de contenido. Confirmar que esto es aceptable.
9. **Clientes Jellyfin prioritarios**: ¿hay algún cliente concreto (Jellyfin Web de escritorio, móvil, Jellyfin Media Player, Android TV/tvOS…) que debas verificar explícitamente además de Jellyfin Web de escritorio? La investigación y las pruebas visuales previstas se centran en Jellyfin Web (navegador), que es el único cliente que renderiza Home Screen Sections de forma nativa hoy.

## Decisiones ya confirmadas por el usuario (2026-07-28, tras esta investigación)

- **Código fuente del nuevo plugin**: repositorio Git **nuevo y separado** (no proyecto hermano dentro de `JellyNotify`, se descarta la propuesta inicial de esta investigación). El **manifest/catálogo de distribución sí puede seguir siendo el de JellyNotify** (`repository/manifest.json` con una segunda entrada apuntando a los releases del nuevo repo) — confirmado que ambas decisiones son independientes, replicando el patrón real de IAmParadox27 (repos de código separados por plugin + un único manifest.json centralizado de catálogo, verificado en su README de instalación: "Add `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`..."). **Pendiente de decidir**: ruta/nombre exacto del nuevo repo en disco (propuesta por defecto: `/home/alvaro/Descargas/JellyProviderSections`, hermano de `jellyfinnotify/` y de `jellyprovidersections/`, ninguno de los cuales es ahora la ubicación del código fuente).
- **Versión de Seerr objetivo**: **3.4.0+** (incluye `ignoreQuota`).
- **Soporte 4K**: incluido desde el MVP.
- **Preferencia de solicitud de series por defecto**: todas las temporadas (`seasons: "all"`), con selección interactiva como acción secundaria.
- Identidad visual (misma paleta + tipografía de sistema) y exclusión del modo mixto películas/series: sin objeción del usuario, se mantienen como recomendado.

## Próximo paso

En cuanto se resuelvan las dos preguntas bloqueantes (TMDb token y Seerr de prueba) — o se confirme que Seerr se incluirá en el propio `docker-compose.yml` del entorno aislado, lo cual no requiere nada del usuario — y se confirmen o ajusten las decisiones reversibles (3–9), procederé a redactar el **plan maestro de implementación** completo (`docs/implementation/`, 16 documentos incluyendo `master-implementation-plan.md`) usando exactamente los contratos técnicos ya verificados en esta investigación, sin necesidad de que ningún otro agente repita este trabajo. Si el usuario prefiere que empiece ya con los valores recomendados como asunciones documentadas (sin esperar respuesta a cada punto individualmente), también puedo proceder directamente bajo ese criterio — indícalo explícitamente.

# 13 — Empaquetado y release

Fuente: `research/02` §3 (`build.sh` de JellyNotify, patrón real ya en producción), decisión confirmada de repositorio separado con catálogo de distribución compartido.

## Diferencia clave respecto a JellyNotify

JellyNotify actualiza un único `manifest.json`/`repository/manifest.json` con una sola entrada. El nuevo plugin vive en un repo distinto, así que hay **dos manifests separados**:

1. **`manifest.json` propio** en `/home/alvaro/Descargas/JellyProviderSections/` — historial de versiones del propio plugin, generado por su propio `build.sh`, análogo al de JellyNotify pero con su propio GUID/nombre/`sourceUrl` apuntando a `github.com/<owner>/JellyProviderSections/releases/...` (a confirmar la organización/usuario de GitHub de destino cuando se cree el repo).
2. **`repository/manifest.json` de JellyNotify** — se le añade una **segunda entrada** en el array (mismo fichero, mismo formato, un elemento más) con el GUID y los `sourceUrl` del nuevo plugin. Esto es exactamente el patrón real que usa IAmParadox27: repos de código separados por plugin, un único manifest.json de catálogo centralizado que el administrador añade una sola vez en Jellyfin (`Dashboard → Plugins → Repositorios`).

## `build.sh` (adaptado)

Mismo esqueleto que el de JellyNotify (`dotnet publish` → empaquetar DLL+XML+meta.json en zip vía Python → MD5 → actualizar manifest), con dos cambios:

- Ejecuta `dotnet publish` **dentro de un contenedor** `mcr.microsoft.com/dotnet/sdk:9.0` en vez de asumir un SDK instalado en el host (este host no lo tiene, ver `10-testing-environment.md`), montando el repo como volumen. Alternativa si se instala el SDK localmente más adelante: mismo script, detecta `dotnet` en `PATH` y usa la vía nativa si existe, cae a Docker si no (mismo patrón defensivo que el resto del proyecto).
- Al final, actualiza **dos** manifests: el propio del repo nuevo, y opcionalmente (con confirmación explícita, no automático) `repository/manifest.json` del repo de JellyNotify si está disponible en una ruta relativa configurable — para no acoplar un build del plugin nuevo a tener siempre el checkout de JellyNotify al lado. Se documenta como paso manual/opcional en el README del nuevo repo si el checkout no está presente.

## Versionado

Mismo esquema de 4 componentes (`major.minor.patch.build`) que JellyNotify, `AssemblyVersion`/`FileVersion` inyectados vía `-p:Version=$VERSION` en el `dotnet publish` (evita el bug ya corregido en JellyNotify v0.1.0.7 donde la versión compilada no seguía a la del release, ver `research/02` §1).

## `meta.json`

Mismo formato que JellyNotify (`category`, `description`, `guid`, `imageUrl`, `name`, `overview`, `owner`, `targetAbi: "10.11.0.0"`, `timestamp`, `version`) — copiado al output por `build.sh`, no generado por `jprm`/`build.yaml` (se descarta el patrón de `jellyfin-plugin-template`, inconsistente y desactualizado, ver `research/07`).

## Checklist de release (ciclo de shipping, análogo al ya usado en el ecosistema del usuario)

1. Tests unitarios + de integración en verde.
2. `build.sh --version X.Y.Z.W` genera el zip + checksum.
3. Verificación manual en el entorno Docker aislado: instalar el zip generado, reiniciar Jellyfin, confirmar que la versión reportada coincide (no cae al valor anterior).
4. Smoke test de las 3 secciones de referencia (Crunchyroll/Netflix/Prime Video España) en el entorno aislado.
5. Grep de secretos sobre el diff antes de commitear (ver `12-security-and-privacy.md`).
6. Commit + push del repo nuevo; actualización de `repository/manifest.json` en JellyNotify como commit separado y explícito (no automático en cada build).
7. Release de GitHub con el zip adjunto y el changelog.

## Notas para cuando se cree el repositorio físico

No se crea en esta fase (es un documento de plan, no de implementación — ver regla explícita del encargo de no empezar a programar todavía). Cuando el usuario autorice iniciar la implementación real: `git init` en `/home/alvaro/Descargas/JellyProviderSections`, `dotnet new sln`, proyectos `JellyProviderSections.Plugin`/`JellyProviderSections.Tests`, y solo entonces la primera entrada real en `repository/manifest.json` de JellyNotify.

# 03 — Matriz de compatibilidad

Fecha de consulta: 2026-07-28. Ancla de versión: el `manifest.json` de JellyNotify declara explícitamente, y en producción real, **Jellyfin 10.11.11 · Seerr 3.3.0 · Radarr 6.1.1.10360 · Sonarr 4.0.17.2952 · Jellyfin Enhanced 11.12.0.0** — se toma como el entorno objetivo por defecto salvo que el usuario indique otra cosa.

## Versiones objetivo vs. versiones verificadas por la investigación

| Componente | Versión ancla (JellyNotify producción) | Versión más reciente encontrada | ¿Compatible con la versión ancla? | Notas |
|---|---|---|---|---|
| Jellyfin Server | 10.11.11 | 10.11.11 (última de la serie 10.11.x en fecha de consulta) | Sí — es la misma | `Jellyfin.Controller`/`Jellyfin.Model` NuGet 10.11.11 confirmados publicados y ya usados por JellyNotify |
| net Runtime | net9.0 | net9.0 (HSS/collection-sections/file-transformation/pages usan net9.0 para Jellyfin 10.11.x) | Sí | Ecosistema completo alineado en net9.0 |
| Home Screen Sections | No usado hoy por JellyNotify (dependencia nueva) | `2.5.11.0` (csproj declara `JellyfinVersion=10.11.5`) | Sí, con matiz | El csproj fija 10.11.5 pero collection-sections (mismo autor, ecosistema) confirma soporte explícito a 10.11.11 en un commit reciente ("Add support for 10.11.11") — riesgo de compatibilidad calificado **bajo** por la investigación, a confirmar con Docker real |
| jellyfin-plugin-collection-sections | No usado (solo referencia) | `2.3.10.0` | Sí | Mismo commit confirma 10.11.11 explícitamente |
| jellyfin-plugin-file-transformation | No usado directamente (dependencia transitiva de HSS) | `2.5.11.0` | Sí | `JellyfinVersion=10.11.3` en csproj |
| jellyfin-plugin-pages | No usado (fuera de alcance del MVP) | `2.4.11.0` | Sí | `JellyfinVersion=10.11.2` en csproj |
| Seerr | **3.3.0** (declarado por JellyNotify) | **3.4.0** (última release en fecha de consulta) | Sí, con una diferencia funcional | v3.4.0 añade `ignoreQuota` (bypass de cuota admin) y Quick Connect auth — ninguno rompe el contrato usado hoy. **Decisión pendiente del usuario**: fijar el nuevo plugin contra 3.3.0 (paridad con JellyNotify, sin `ignoreQuota`) o 3.4.0+ (más completo) — ver readiness gate |
| Jellyfin Enhanced | 11.12.0.0 (declarado por JellyNotify) | No re-verificado en esta pasada (se usó el código vendorizado local, no se comprobó la última release de GitHub) | N/A | Jellyfin Enhanced es solo referencia de patrón (inyección frontend), no una dependencia en tiempo de ejecución del nuevo plugin |
| jellyfin-plugin-template | Sin versión (rama `master`, sin tags) | N/A | Referencia estructural únicamente | Su `.csproj`/`build.yaml` de ejemplo fijan versiones desactualizadas (10.9.11 / targetAbi 10.9.0.0) — **no copiar tal cual** |

## Compatibilidad cruzada del ecosistema HSS (verificado por `.csproj` real, sin dependencias de compilación entre sí)

| Plugin | PackageReference propios | Dependencia en tiempo de ejecución (reflexión) |
|---|---|---|
| Home Screen Sections | `Jellyfin.Model`, `Jellyfin.Controller`, `Jellyfin.Extensions`, `Lib.Harmony`, `SkiaSharp`, `Newtonsoft.Json` | → File Transformation (para inyectar su frontend) |
| Collection Sections | `Jellyfin.Model`, `Jellyfin.Controller`, `Newtonsoft.Json` | → Home Screen Sections (para registrar secciones) |
| File Transformation | (no inspeccionado el listado completo, confirmado sin refs a HSS/Pages) | Ninguna hacia HSS/Pages/Collection Sections |
| Pages | (no inspeccionado el listado completo) | → File Transformation (para inyectarse en varios chunks JS) |
| **JellyProvider Sections (nuevo)** | `Jellyfin.Model 10.11.11`, `Jellyfin.Controller 10.11.11` (mismo patrón que JellyNotify) | → Home Screen Sections (reflexión, igual que Collection Sections) y **File Transformation** (reflexión, mismo patrón), esta última añadida al implementar las carátulas de las tarjetas externas; ninguna hacia Pages |

## Grado de confianza de la matriz

- **Alta**: versión de Jellyfin Server, versión de paquetes NuGet, licencias, ausencia de `PackageReference` cruzados entre los plugins del ecosistema HSS (todo verificado leyendo `.csproj` reales).
- **Media**: compatibilidad exacta de HSS 2.5.11.0 contra Jellyfin 10.11.11 (inferida de un commit de un plugin hermano del mismo autor, no probada en ejecución real) — **primera cosa a verificar empíricamente en el entorno Docker aislado**, antes de dar por cerrada esta matriz.
- **Media**: Seerr 3.3.0 vs 3.4.0 — el contrato de creación de solicitudes es compatible en ambas, la diferencia es aditiva (`ignoreQuota`); pendiente de decisión del usuario sobre qué versión fijar como objetivo declarado.

## Implicación directa para el plan

No hay ningún hallazgo de incompatibilidad dura en esta matriz. El estado de esta pieza del readiness gate es **verde con verificación empírica pendiente en el entorno de pruebas** (no bloqueante para redactar el plan maestro, sí bloqueante para dar por *terminada* la fase de integración con HSS — ver criterios de aceptación).

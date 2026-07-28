# 09 — Análisis de referencia UI (JellyNotify)

Fecha de consulta: 2026-07-28. Complementa `02-local-project-analysis.md` con foco específico en qué patrones visuales usar para las tarjetas de sección (cerrada/expandida) pedidas en la sección 13 del prompt del usuario.

## Tokens de diseño a heredar (fuente: `JellyNotify.Plugin/Web/jellynotify.css`)

Ver tabla completa de variables en `02-local-project-analysis.md §4`. Resumen de aplicación directa a las tarjetas de sección:

| Necesidad de la tarjeta | Token/patrón JellyNotify | Aplicación |
|---|---|---|
| Fondo de tarjeta | `--jn-surface` + `backdrop-filter: blur(var(--jn-glass-blur))` | Tarjeta cerrada y expandida comparten el mismo fondo de cristal |
| Borde decorativo | `.jn-card::before` (gradiente violeta→cian enmascarado) | Aplicar igual en la tarjeta de sección; refuerza identidad de familia de producto |
| Radio de esquina | `--jn-radius` (16px tarjeta), `--jn-radius-sm` (10px controles internos) | Igual jerarquía en badges/botones internos |
| Separador de bloque con etiqueta | `.jn-subhead` / `.jn-subhead-label` | Usar para separar, dentro de la tarjeta expandida, los grupos: Identidad (UUID/nombre) · Filtros TMDb · Integración (Home Sections/Seerr) · Diagnóstico |
| Estado activo/inactivo | `--jn-success` (#38f0b8) / `--jn-text-faint` | Badge "ACTIVA" en verde cian-esmeralda vs. gris tenue para inactiva |
| Estado de error | `--jn-danger` (#ff5577) | Badge de "Último error" en la tarjeta expandida y en diagnóstico |
| Estado de advertencia (p.ej. Seerr desconectado) | `--jn-warning` (#ffbb55) | Badge de integración degradada |
| Tipografía de cabecera/etiquetas | `--jn-font-display` ('JNOrbitron') | Nombre de la sección en la tarjeta cerrada |
| Tipografía de cuerpo | `--jn-font-body` ('JNExo') | Metadatos secundarios (proveedor · región · tipo · orden · nº elementos) |
| Transición | `--jn-transition` (0.2s ease) | Animación de expansión/colapso de la tarjeta |

## Patrón de tarjeta cerrada → expandida

JellyNotify no tiene hoy un componente de "tarjeta acordeón" genérico y reutilizable tal cual (su `.jn-card` es una tarjeta de formulario estática, no colapsable) — **no existe un componente literal para copiar**, así que la tarjeta cerrada/expandida de secciones de proveedor es una pieza NUEVA a diseñar, pero construida enteramente con los tokens y el lenguaje visual ya existentes (glass + borde en gradiente + subheads), no con un lenguaje visual distinto. Se clasifica como **reutilización con adaptación** a nivel de tokens, y **construcción nueva** a nivel de componente de interacción (acordeón).

Referencias de interacción a evaluar en la fase de implementación (no resueltas en esta investigación, ver `05-ui-and-interaction-specification.md` cuando se redacte el plan):
- `.jn-panel-*` (panel deslizante del bell) usa `max-height`/transform para animar apertura — técnica candidata para animar la expansión de la tarjeta sin saltos de layout (`transition` sobre `grid-template-rows: 0fr → 1fr` es la técnica moderna recomendada en general para evitar animar `height:auto`, más robusta que lo que usa JellyNotify hoy; a decidir en el plan).
- `.jn-tabs` demuestra ya un patrón de `aria-selected`/foco visible en este proyecto — mismo estándar de accesibilidad a replicar con `aria-expanded` en la tarjeta.

## Iconografía y logos externos

- JellyNotify carga iconos de servicio (Discord/Telegram/Sonarr/Radarr/Seerr) desde un CDN externo de terceros (`dashboard-icons`) en tiempo de ejecución — precedente de "imagen externa cargada en vivo, no empaquetada". El nuevo plugin hará lo mismo pero contra `image.tmdb.org` (dominio oficial y estable de TMDb, ver `05-tmdb-provider-analysis.md` para tamaños de imagen), lo cual es más apropiado por ser la fuente primaria de verdad de cada proveedor en vez de un mirror de terceros.
- Jellyfin Web usa Material Icons (`.jn-tab-icon-material`) — mismo set de iconos disponible de forma nativa, usar para iconografía de acción (editar/duplicar/activar/eliminar/probar/previsualizar/limpiar caché) en vez de introducir una librería de iconos nueva.

## Idiomas

JellyNotify soporte `en-US`, `es-ES`, `ca` vía `Web/locales/*.json` servidos por `WebAssetsController`. Mismo patrón recomendado para el nuevo plugin si se requiere i18n (a confirmar con el usuario si es necesario para el MVP; no lo pide explícitamente el prompt de la sección 6, se trata como extensión posterior salvo que el usuario diga lo contrario).

## Riesgo de acoplamiento visual detectado

Las dos fuentes tipográficas (`JNOrbitron`, `JNExo`) están embebidas como base64 **dentro de cada CSS que las use** (no hay mecanismo de compartición entre plugins en Jellyfin — cada plugin es un assembly/recurso embebido aislado). Si el nuevo plugin reutiliza literalmente la misma tipografía, duplicará ~120KB de datos de fuente en su propio CSS embebido. Impacto: tamaño de descarga adicional la primera vez que se carga la página de Jellyfin con ambos plugins instalados (no se comparte caché de fuente entre los dos `@font-face` con el mismo nombre si los orígenes de red — rutas de embedded resource — son distintos; los navegadores cachean por URL, no por nombre de fuente, así que si ambas exponen rutas físicas distintas no habrá deduplicación real de red, aunque sí de renderizado tras la primera carga de cada una). Se documenta como coste conocido y aceptado, no como bloqueante — a confirmar con el usuario si prefiere una tipografía del sistema más ligera para el nuevo plugin en vez de duplicar las fuentes custom (ver readiness gate).

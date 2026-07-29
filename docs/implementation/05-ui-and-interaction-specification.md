# 05 — Especificación de UI e interacción

Fuente: sección 13 del encargo original, `research/09-ui-reference-analysis.md` (tokens JellyNotify), `research/02` §4 (patrón `.jn-card`). Nomenclatura de clase propia: prefijo `jps-` (JellyProvider Sections), nunca `jn-`, para no colisionar si ambos plugins conviven en la misma página de Jellyfin.

## Tokens visuales heredados (decisión ya confirmada: misma paleta, tipografía de sistema)

```css
:root {
    --jps-accent: #a970ff;
    --jps-accent-hover: #bd94ff;
    --jps-magenta: #ff5fd8;
    --jps-cyan: #35e5f0;
    --jps-bg: #05050b;
    --jps-surface: rgba(255, 255, 255, 0.045);
    --jps-glass-blur: 20px;
    --jps-text: #f2f0ff;
    --jps-text-muted: #a9a2cf;
    --jps-border: rgba(180, 160, 255, 0.22);
    --jps-success: #38f0b8;
    --jps-danger: #ff5577;
    --jps-warning: #ffbb55;
    --jps-radius: 16px;
    --jps-radius-sm: 10px;
    --jps-transition: 0.2s ease;
    --jps-font-display: -apple-system, 'Segoe UI', sans-serif;   /* sistema, no JNOrbitron */
    --jps-font-body: -apple-system, 'Segoe UI', Roboto, sans-serif;
}
```

Patrón de tarjeta (`.jps-card`) idéntico al de JellyNotify: superficie de cristal + borde en gradiente violeta→cian por pseudo-elemento enmascarado (ver `research/02` §4 para el CSS exacto a adaptar).

## Tarjeta cerrada

Estructura fija, misma altura/columnas/posiciones para todas las tarjetas (requisito explícito de la sección 13):

```
┌──────────────────────────────────────────────────────────────────────┐
│ [LOGO] Popular en Crunchyroll                            [ACTIVA]  ⌄ │
│        Crunchyroll · España · Series · Popularidad · 20 elementos    │
│        Home Sections: registrado · Seerr: conectado                 │
└──────────────────────────────────────────────────────────────────────┘
```

- Columna 1 (fija, ancho constante): logo del proveedor, 24px, `object-fit: contain`, fallback a icono genérico si no carga.
- Columna 2 (flexible): nombre de sección (`--jps-font-display`, truncado con `text-overflow: ellipsis` + `title` para tooltip nativo del navegador en valores largos).
- Columna 3 (fija a la derecha): badge de estado (`ACTIVA` en `--jps-success` / `INACTIVA` en `--jps-text-muted`) + botón de despliegue (icono chevron, `aria-expanded`).
- Fila secundaria: metadatos separados por `·` (nunca guión largo, es texto de UI): proveedor, región, tipo de contenido, ordenación, nº de elementos.
- Fila terciaria: estado de integración HSS/Seerr como texto corto + icono de estado (verde/ámbar/rojo), coherente con `--jps-success`/`--jps-warning`/`--jps-danger`.
- Menú de acciones (icono "más opciones", Material Icons como ya usa Jellyfin Web): Editar, Duplicar, Activar/Desactivar, Eliminar, Probar consulta, Previsualizar, Limpiar caché.
- Foco visible (`outline` con `--jps-accent`) en toda la cabecera de la tarjeta, navegable y activable por teclado (`Enter`/`Espacio` despliega, igual que un `<button>` nativo — la cabecera de la tarjeta es semánticamente un botón, no un `<div onclick>`).

## Tarjeta expandida

Se despliega verticalmente hacia abajo dentro de la misma tarjeta (no navega a otra página, no cambia de anchura). Animación recomendada: `grid-template-rows: 0fr → 1fr` con `transition` (más robusta que animar `max-height`, ver `research/09`), duración `--jps-transition`.

Agrupación por `.jps-subhead` (mismo patrón de divisor con etiqueta que JellyNotify):

1. **Identidad**: UUID (solo lectura, copiable), nombre, fecha de creación/modificación.
2. **Proveedor y alcance**: proveedor + logo, id TMDb, región, idioma de metadatos, tipo de contenido.
3. **Filtros**: ordenación, géneros incluidos/excluidos, idioma original, país de origen, fechas mín/máx, valoración mín, votos mín, adultos.
4. **Consulta y resultados**: consulta TMDb generada (solo lectura, para depuración), nº de páginas consultadas, nº de resultados, botón "Probar consulta".
5. **Caché**: duración configurada, última sincronización, próxima actualización estimada, botón "Limpiar caché".
6. **Integraciones**: estado Home Screen Sections (registrado/no detectado/versión no probada), estado Seerr (conectado/no configurado/error), solicitudes activadas o no para esta sección.
7. **Diagnóstico**: último error (si lo hay, con marca de tiempo), resultado de la última sincronización.
8. **Acciones**: Editar, Duplicar, Activar/Desactivar, Eliminar (con confirmación), Probar consulta, Previsualizar.

Requisitos de interacción:
- Los botones internos usan `stopPropagation` para no colapsar la tarjeta accidentalmente al pulsarlos.
- `aria-expanded` en la cabecera, `aria-controls` apuntando al bloque expandido.
- Acciones destructivas (Eliminar) piden confirmación explícita (modal o `window.confirm` como mínimo para el MVP).
- Errores de una acción (p. ej. "Probar consulta" falla) se muestran contextualizados dentro de la propia tarjeta, no como alerta global.

## Múltiples tarjetas abiertas: decisión

**Se permite más de una tarjeta expandida simultáneamente**, no un modelo de acordeón exclusivo. Justificación: un admin gestionando varias secciones a la vez (p. ej. comparando la configuración de "Popular en Crunchyroll" y "Anime popular en Crunchyroll") se beneficia de poder tener ambas abiertas para contrastar filtros; un acordeón exclusivo forzaría cerrar una para ver la otra, lo cual añade fricción sin beneficio claro de claridad visual (cada tarjeta ya tiene su propio contenedor con bordes definidos, no hay riesgo de confusión entre secciones abiertas). Sí se recomienda que la lista completa (decenas de tarjetas) tenga una acción global "Colapsar todo" en la cabecera de la página para volver al estado compacto rápidamente.

## Estados de página

- **Sin secciones**: estado vacío con ilustración/icono simple + botón primario "Crear sección" (nunca una tabla/lista vacía sin explicación, coherente con la regla de UI ya establecida en el ecosistema del usuario de no dejar paneles sin datos cargados sin feedback).
- **Cargando**: spinner animado mientras se resuelve `GET /Admin/sections` (misma regla: nunca un panel vacío que parezca colgado).
- **Error de carga**: mensaje claro + botón de reintentar, sin ocultar el motivo (p. ej. "No se pudo conectar con Jellyfin").

## Formulario de creación/edición de sección

- Selector de proveedor: buscador con logo + nombre, poblado desde `GET /watch/providers/movie|tv` cacheado, filtrado por región y tipo de contenido ya elegidos (refresca si cambian).
- Selector de región: poblado desde `GET /watch/providers/regions`, no una lista fija.
- Todos los filtros opcionales colapsados bajo un bloque "Filtros avanzados" para no abrumar en el flujo de creación rápida (nombre + proveedor + región + tipo de contenido son los únicos campos visibles por defecto).
- Botón "Probar consulta" disponible ya en el formulario de creación (antes de guardar), no solo en la tarjeta ya guardada, para permitir iterar sobre filtros sin publicar nada todavía.

## Accesibilidad y responsive

- Contraste verificado contra `--jps-bg` para todos los tokens de texto (heredado de JellyNotify, ya validado en producción).
- Layout de tarjeta en columna única por debajo de un ancho de corte razonable (p. ej. 640px), manteniendo el mismo orden de información (logo+nombre primero, badges después, metadatos en la fila siguiente).
- Todos los iconos de acción llevan `aria-label` explícito (no solo tooltip visual).

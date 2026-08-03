# Capturas de evidencia

Toma las capturas de `testenv/evidence/screenshots/` contra el entorno de pruebas, de
forma reproducible: sesión real de administrador, temas conmutados desde la propia página
de preferencias de Jellyfin y viewports fijos.

No usa la extensión de Chrome de Claude (nunca llegó a conectarse). Conduce el Chromium
que Playwright deja en caché mediante el protocolo DevTools, con el WebSocket que trae
Node 22 de serie, así que no hace falta instalar nada.

```bash
cd testenv/scripts/capture
node capture-home.mjs dark      # página principal, tema oscuro, más tablet y móvil
node capture-home.mjs light     # página principal, tema claro
node capture-config.mjs dark    # página de administración
node capture-config.mjs light
```

Si el Chromium no está donde se espera, indícalo:

```bash
JPS_CHROME=/ruta/a/chrome node capture-home.mjs dark
```

## Cosas que costaron encontrar y conviene no volver a descubrir

- **`Page.captureScreenshot` recorta en coordenadas de documento, no de viewport.** Hay que
  sumar `window.scrollY` al `y` del `getBoundingClientRect`, o el recorte sale desplazado
  justo lo que la página esté desplazada.
- **Home Screen Sections pagina la página principal**, así que la misma sección existe más
  de una vez en el DOM. Solo una está maquetada; hay que filtrar por
  `getBoundingClientRect().width > 0`.
- **Las filas siguen cargando imágenes y desplazan el layout.** Los recortes se miden dos
  veces y solo se disparan cuando dos medidas seguidas coinciden.
- **Jellyfin usa built-ins personalizados** (`<input is="emby-input">`), de modo que para
  rellenar un campo hay que usar el setter nativo de `HTMLInputElement.prototype`, no
  `el.value`.
- **Chrome cachea `index.html`**, donde va inyectado el script de la página principal. Si
  una comprobación no ve un cambio reciente, recarga con `Page.reload { ignoreCache: true }`.

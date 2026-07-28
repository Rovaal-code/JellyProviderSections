# 07 — Plan de renderizado del logo del proveedor junto al título

Basado en `research/08-provider-logo-rendering.md` (recomendación reconciliada) y en el hallazgo de seguridad `research/10-security-and-licensing.md` §2.2. Documento de PLAN, no implementación.

## 1. Solución principal: `displayText` como HTML controlado

Al registrar cada sección ante Home Screen Sections (ver `06-home-sections-integration-plan.md` §2), el campo `displayText` se construye server-side, en `HomeSectionsRegistrar` (o un helper dedicado `SectionDisplayTextBuilder`), como:

```
<img src="{logoUrl}" alt="" class="jps-section-logo" /><span>{nombreEscapado}</span>
```

Donde:

- **`logoUrl`**: construida internamente por nuestro propio endpoint de logo cacheado (ver §2 abajo), **nunca** una URL arbitraria proporcionada por el admin como texto libre. Fuente última: `{secure_base_url}{size}{logo_path}` de TMDb (`research/05` §Configuración de imágenes), con `logo_path` obtenido de la respuesta ya cacheada de `/watch/providers/movie|tv` para el `provider_id` de esa sección.
- **`nombreEscapado`**: el campo `SectionDefinition.DisplayName` (texto libre del admin) pasado por `System.Net.WebUtility.HtmlEncode` (o `HttpUtility.HtmlEncode`) **antes** de interpolarse. **Obligatorio, no opcional** — ver hallazgo de seguridad citado arriba: sin este escapado, un nombre de sección con HTML/JS embebido se ejecutaría en el navegador de todos los usuarios que ven la home, no solo del admin que lo escribió.
- **`alt=""`** deliberadamente vacío en el `<img>` (decorativo, el nombre ya está en el `<span>` de texto — evita que lectores de pantalla lean el nombre del proveedor duplicado).

Criterio de aceptación de seguridad explícito para el plan de pruebas (`11-test-matrix.md`): crear una sección con `DisplayName = "<script>alert(1)</script>"` y confirmar que el HTML resultante en `displayText` contiene `&lt;script&gt;` literal, no una etiqueta `<script>` real.

## 2. Endpoint de logo cacheado

**Ruta**: `GET /JellyProviderSections/logo/{providerId}` (patrón calcado de `WebAssetsController` de JellyNotify — sin `[Authorize]`, porque se carga como `<img src>` normal del navegador sin cabeceras de sesión, y no expone ningún secreto, solo una imagen pública de TMDb).

**Comportamiento**:
1. Resuelve `providerId` (entero) contra la caché de proveedores TMDb ya cacheada (ver `08-tmdb-integration-plan.md`).
2. Si el proveedor no existe en caché o no tiene `logo_path`, responde `404` (el `<img onerror>` del navegador simplemente no muestra nada roto si se añade `onerror` en el HTML — ver §3).
3. Si existe, redirige (`302`) o hace proxy directo a `{secure_base_url}{size}{logo_path}` — a decidir en implementación entre las dos opciones; redirigir es más simple y TMDb ya sirve con cabeceras de caché HTTP razonables; hacer proxy da control total de `Cache-Control` propio pero añade una llamada de red por el propio servidor. Recomendado para el MVP: **redirect 302**, revisar si en la fase de rendimiento hace falta cambiar a proxy con caché local en disco.

## 3. Fallback sin hueco roto

Aunque la solución principal no depende del DOM, el `<img>` en sí puede fallar a nivel de red (TMDb caído, logo eliminado). Se añade `onerror` inline (mismo patrón que `elsewhere.js` de Jellyfin Enhanced, `research/08` Fuente 3): `onerror="this.style.display='none'"` en el propio `<img>` del `displayText`. Esto no requiere JS externo, es un atributo inline del HTML que ya construimos server-side.

## 4. Especificación visual del `<img>`

| Propiedad | Valor | Justificación |
|---|---|---|
| Altura | 20px (rango aceptable 16-24px) | Coherente con la altura de línea de un `<h2 class="sectionTitle">` de Jellyfin Web sin desplazar el texto |
| Ancho | `auto` | El logo de TMDb no es siempre cuadrado; forzar ancho fijo deformaría proporciones |
| `object-fit` | `contain` | Evita recorte/deformación en logos no cuadrados |
| Tamaño TMDb solicitado | `w92` (ver `research/05`) | Suficiente para 20px de altura en pantallas de alta densidad sin transferir una imagen sobredimensionada |
| Margen derecho | 8px | Separación del `<span>` de texto, mismo valor usado por el precedente de `elsewhere.js` |
| `border-radius` | 4px | Coherente con el lenguaje visual de JellyNotify (bordes redondeados, ver `research/09`) |
| Fondo | Transparente | Los logos de TMDb suelen venir con fondo transparente; no forzar un fondo propio que rompa en tema claro/oscuro |

## 5. Solución de fallback (no implementar en el MVP salvo que la principal deje de funcionar)

Documentada y lista, no activa por defecto: `IStartupFilter` propio (`Services/ScriptInjectionStartupFilter.cs`, calcado del de JellyNotify, con atribución GPL-3.0 en `NOTICE.md` del nuevo repositorio si se llega a usar) + `WebAssetsController` propio + `MutationObserver` en el JS inyectado, ancla por `data-*` propio (nunca por texto de título ni por clase CSS genérica en solitario, ver `research/08` "Mitigación recomendada para el fallback").

**Criterio explícito de activación**: si, durante la fase de pruebas E2E (`11-test-matrix.md`) o tras una actualización futura de Home Screen Sections, se detecta que `displayText` deja de renderizarse como HTML (aparece escapado, p. ej. `&lt;img...`), se activa este fallback. Se documenta como *feature flag* interno (`UseDisplayTextLogoInjection: bool`, default `true`) en la configuración del plugin, para poder desactivar la vía principal y forzar el fallback sin desplegar código nuevo si el problema aparece en producción.

## 6. Plan de prueba visual concreto

Encaja en la fase 17 ("Logotipo en el título") y 28 ("QA visual") del plan maestro. Capturas mínimas requeridas:

1. Una sección con logo correcto, tema claro.
2. Misma sección, tema oscuro.
3. Sección con `providerId` sin `logo_path` en TMDb (logo ausente) → confirmar que no hay hueco roto ni icono de imagen rota del navegador.
4. Sección con nombre conteniendo caracteres especiales (`<`, `&`, comillas) → confirmar que se muestra como texto literal, no interpretado.
5. Varias secciones con logos distintos en la misma home, para confirmar alineación vertical consistente entre filas.
6. Móvil y escritorio (responsive).

No se da el requisito por resuelto hasta tener las 6 capturas reales contra el entorno Docker (no simuladas), conforme a la regla explícita del encargo original de no dar el logo por resuelto sin prueba visual real.

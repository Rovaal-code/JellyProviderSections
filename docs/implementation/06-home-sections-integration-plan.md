# 06 — Plan de integración con Home Screen Sections

Basado en `research/04-home-screen-sections-integration.md` (código real verificado, HSS `2.5.11.0`, commit `3b02d90e3c405d63181127fb31d0266a0192525b`). Este documento es un PLAN de implementación, no la implementación en sí.

Contexto fijo: plugin nuevo en repositorio separado (`/home/alvaro/Descargas/JellyProviderSections`), namespace `Jellyfin.Plugin.JellyProviderSections`, GUID `05cac539-35ae-4f0d-be40-5f0eabd7f43c`, target Jellyfin 10.11.11 / net9.0, HSS `2.5.11.0` como versión mínima probada.

## 1. Componente `HomeSectionsRegistrar`

**Ubicación**: `Services/HomeSectionsRegistrar.cs`, clase `internal sealed class HomeSectionsRegistrar`.

**Responsabilidad única**: traducir cada `SectionDefinition` activa de `PluginConfiguration.Sections` en una llamada a `PluginInterface.RegisterSection(JObject)` de HSS, localizado por reflexión. No conoce TMDb ni Seerr — recibe una lista ya resuelta de definiciones y las registra.

**Detección del ensamblado de HSS** (idéntico al patrón verificado en `collection-sections`, sin modificar la lógica):

```csharp
var hssAssembly = AssemblyLoadContext.All
    .SelectMany(x => x.Assemblies)
    .FirstOrDefault(x => x.FullName?.Contains(".HomeScreenSections") ?? false);

if (hssAssembly is null)
{
    _logger.LogWarning("[JellyProviderSections] Home Screen Sections no detectado. Ninguna sección se registrará hasta que se instale.");
    _diagnostics.HomeSectionsStatus = HomeSectionsStatus.NotDetected;
    return;
}

var pluginInterfaceType = hssAssembly.GetType("Jellyfin.Plugin.HomeScreenSections.PluginInterface");
var registerMethod = pluginInterfaceType?.GetMethod("RegisterSection");

if (registerMethod is null)
{
    _logger.LogWarning("[JellyProviderSections] Home Screen Sections presente pero con un contrato incompatible (tipo/método no encontrado). Versión mínima probada: 2.5.11.0.");
    _diagnostics.HomeSectionsStatus = HomeSectionsStatus.IncompatibleVersion;
    return;
}

_diagnostics.HomeSectionsStatus = HomeSectionsStatus.Ok;
```

Ninguna excepción se propaga fuera de este componente: cada fallo se loguea, se refleja en `_diagnostics.HomeSectionsStatus`, y el registro continúa con las siguientes secciones (una sección con `resultsClass` roto no debe impedir que las demás se registren).

**Nunca se usa la vía HTTP** `POST /HomeScreen/RegisterSection` (confirmado sin `[Authorize]` en `research/04` §1). Solo la vía in-process por reflexión, exactamente igual que el precedente de `collection-sections`.

## 2. Payload `SectionRegisterPayload` construido por sección

Para cada `SectionDefinition` activa:

| Campo del payload HSS | Valor que construye nuestro plugin |
|---|---|
| `id` | `SectionDefinition.Id` (GUID inmutable, generado server-side al crear la sección, **nunca editable desde la API de edición** — ver `03-data-model.md`) |
| `displayText` | HTML construido server-side: `<img>` del logo cacheado + `<span>` con el nombre HTML-encodeado. Ver `07-provider-logo-plan.md` para el detalle exacto y la obligación de escapar el nombre. |
| `limit` | `1` (una instancia por definición) |
| `route` | Ausente/`null` en el MVP (no hay una página de detalle propia de la sección a la que navegar al hacer click en el título; el click en cada tarjeta individual usa su propio comportamiento, ver `05-ui-and-interaction-specification.md`) |
| `additionalData` | `SectionDefinition.Id.ToString()` — permite a nuestro propio `resultsMethod` identificar qué definición debe resolver sin tener que repetir toda la configuración en el payload |
| `resultsAssembly` | Nombre del propio ensamblado del plugin (`typeof(Plugin).Assembly.FullName`) |
| `resultsClass` | `Jellyfin.Plugin.JellyProviderSections.HomeScreen.SectionResultsHandler` (nueva clase, resuelta por HSS vía `ActivatorUtilities.CreateInstance` con el contenedor de DI de HSS, no el nuestro — ojo con las dependencias que se le inyecten, deben estar registradas de forma compatible o resolverse por locator estático como hace `Plugin.Instance`) |
| `resultsMethod` | Nombre del método que devuelve `QueryResult<BaseItemDto>` para esa sección (ver `08-tmdb-integration-plan.md` y `07-jellyfin-library-resolution.md` para cómo se construye ese resultado combinando TMDb + biblioteca local) |
| `resultsEndpoint` | No usado (se prefiere la vía in-process, coherente con el resto del plugin) |

## 3. Ciclo de re-registro

Dos disparadores, ambos re-registran **todas** las secciones activas desde cero (no hay registro incremental — es más simple y ya es el patrón verificado en producción de `collection-sections`):

1. **Arranque del servidor**: `HomeSectionsRegistrar` se ejecuta desde un `IScheduledTask` propio (`Tasks/StartupRegistrationTask.cs`) con trigger de tipo arranque, replicando `StartupServiceHelper.GetStartupTrigger()` — verificar el helper específico por versión de Jellyfin igual que hace `collection-sections` en `JellyfinVersionSpecific/10.11/StartupServiceHelper.cs`.
2. **Guardado de configuración**: suscripción al evento `ConfigurationChanged` de `BasePlugin<PluginConfiguration>` (mismo patrón que `collection-sections`), que dispara el mismo `HomeSectionsRegistrar.RegisterAll()`.

No hay un tercer disparador por temporizador — el registro es barato (solo diccionario en memoria de HSS) y no necesita refrescarse periódicamente; lo que sí se refresca periódicamente es el **contenido** de cada sección (caché TMDb, ver `08-tmdb-integration-plan.md`), que es independiente del registro.

## 4. Activar / desactivar / eliminar / duplicar

| Acción en nuestro plugin | Efecto sobre HSS |
|---|---|
| Crear sección (estado activo) | Se incluye en el siguiente `RegisterAll()` |
| Editar sección (cualquier campo salvo `Id`) | Se re-registra con el mismo `Id` en el siguiente `RegisterAll()` — HSS la reconoce como la misma entidad, conserva posición/estado por usuario (verificado en `research/04` §2) |
| Desactivar sección | Se excluye del siguiente `RegisterAll()`. **No** se llama a ningún "unregister" porque no existe esa API en HSS (confirmado, `research/04` §2) — la sección queda ausente de `m_delegates` de HSS hasta que se reactive; si algún usuario aún la tiene "enabled" en su `ModularHomeUserSettings`, verá una fila vacía, no un error (comportamiento documentado, no un bug a corregir) |
| Eliminar sección | Igual que desactivar (excluida del próximo `RegisterAll()`), más limpieza de la propia `SectionDefinition` de nuestra configuración. Queda un `id` "huérfano" en `ModularHomeUserSettings` de los usuarios que la tenían activa — comportamiento conocido de HSS, documentado como limitación, no bloqueante. El panel de diagnóstico debe explicar esto si el admin pregunta por qué una sección eliminada "sigue apareciendo como hueco" hasta que HSS limpie su propio estado |
| Duplicar sección | Genera un **nuevo** `Id` (GUID distinto) — nunca reutiliza el `Id` de la sección origen, para no colisionar con su posición ya guardada en HSS |

## 5. Diagnóstico en la UI de administración

Estado a mostrar por cada uno de estos tres casos (mapeado 1:1 con `research/04` §6, tabla "Qué pasa si falta una dependencia"):

- **"Home Screen Sections: no detectado"** — el ensamblado no se encuentra. Mensaje sugerido (sin guión largo, apto para UI): `Home Screen Sections no está instalado. Instálalo desde el catálogo de plugins para que las secciones aparezcan en la pantalla de inicio.`
- **"Home Screen Sections: versión no compatible"** — ensamblado presente, tipo/método no encontrado. Mensaje: `Se detectó Home Screen Sections pero con una versión incompatible. Versión mínima probada: 2.5.11.0.`
- **"Home Screen Sections: conectado"** — todo correcto, con la versión detectada del ensamblado (leída de `hssAssembly.GetName().Version`) mostrada junto al estado.
- Caso adicional no cubierto por el registro en sí, pero sí por el diagnóstico general: **"registrado pero no visible"** si File Transformation falta (ver `research/04` §4) — se detecta comprobando, con el mismo patrón de reflexión, si el ensamblado `.FileTransformation` está cargado, y se muestra como advertencia separada: `Home Screen Sections está conectado, pero File Transformation no está instalado: las secciones se registran correctamente pero no se mostrarán en la pantalla de inicio.`

Por cada `SectionDefinition`, la tarjeta expandida (ver `05-ui-and-interaction-specification.md`) muestra el estado de integración HSS heredado de este diagnóstico global (no hay un estado por sección distinto, ya que el registro es todo-o-nada por dependencia detectada, no por sección individual).

## 6. Fuera de alcance de este documento

- Construcción exacta de `QueryResult<BaseItemDto>` dentro de `SectionResultsHandler` (mezcla TMDb + resolución local + Seerr) → `08-tmdb-integration-plan.md` y `09-seerr-integration-plan.md`.
- Construcción exacta del HTML de `displayText` → `07-provider-logo-plan.md`.
- Modelo `SectionDefinition` completo → `03-data-model.md`.

## 7. Verificación pendiente (no cerrada por esta investigación, a confirmar en el entorno Docker)

Idéntica a la de `research/04`: (a) el registro por reflexión aparece realmente en Modular Home, (b) la posición sobrevive a un reinicio real, (c) el comportamiento exacto de una sección huérfana tras eliminarla. Criterio de salida de la fase 26/27 futura (Entorno de pruebas / E2E): captura de pantalla + comprobación manual de los tres puntos.

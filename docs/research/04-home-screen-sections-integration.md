# 04 — Integración con Home Screen Sections, File Transformation y Plugin Pages

Investigación de código real (no solo README) del ecosistema de plugins de IAmParadox27. Todos los repos se clonaron localmente (shallow clone, `--depth 50`) y se inspeccionaron los tags más recientes disponibles el día de la consulta.

## Fuentes inspeccionadas

### jellyfin-plugin-home-sections
- **URL:** https://github.com/IAmParadox27/jellyfin-plugin-home-sections
- **Rama:** main
- **Tag inspeccionado:** `2.5.11.0`
- **Commit:** `3b02d90e3c405d63181127fb31d0266a0192525b`
- **Fecha de consulta:** 2026-07-28
- **Licencia:** GPL-3.0 (fichero `LICENSE`, cabecera "GNU GENERAL PUBLIC LICENSE Version 3")
- **Target framework declarado en csproj:** `JellyfinVersion = 10.11.5` → `net9.0` para 10.11.x (10.10.7 usa net8.0). Compatible con el target de JellyNotify (Jellyfin 10.11.11 / net9.0).
- **Archivos relevantes leídos íntegramente:**
  - `src/Jellyfin.Plugin.HomeScreenSections/PluginInterface.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/HomeScreen/Sections/PluginDefinedSection.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Library/IHomeScreenManager.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/HomeScreen/HomeScreenManager.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Model/SectionRegisterPayload.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Model/Dto/HomeScreenSectionPayload.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Controllers/HomeScreenController.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Controllers/loadSections.js` (parcial, líneas 1-360 de 641)
  - `src/Jellyfin.Plugin.HomeScreenSections/Helpers/TransformationPatches.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Services/StartupService.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Services/HomeScreenSectionService.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Services/TranslationManager.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/PluginServiceRegistrator.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/ModuleInitializer.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/HomeScreenSectionsPlugin.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/HomeScreen/Sections/DiscoverSection.cs` (precedente sintético TMDb/Seerr, ver 5bis)
  - `src/Jellyfin.Plugin.HomeScreenSections/Configuration/PluginConfiguration.cs`
  - `src/Jellyfin.Plugin.HomeScreenSections/Jellyfin.Plugin.HomeScreenSections.csproj`

### jellyfin-plugin-collection-sections (referencia de implementación de terceros)
- **URL:** https://github.com/IAmParadox27/jellyfin-plugin-collection-sections
- **Tag inspeccionado:** `2.3.10.0`
- **Commit:** `d30740b5575c3b730580fb3a260a4b0c98926dfa` (mensaje: "Add support for 10.11.11 - need to change this to a nicer more future proofed solution in the future")
- **Fecha de consulta:** 2026-07-28
- **Licencia:** GPL-3.0
- **Por qué importa:** es el ÚNICO ejemplo real, en producción, de un plugin *externo* al propio HSS que registra secciones dinámicas definidas por el administrador (N secciones configurables, cada una con UUID propio). Es el precedente arquitectónico más cercano a "Jellyfin Provider Sections" que existe hoy en el ecosistema público.
- **Target framework declarado en csproj:** `JellyfinVersion = 10.11.2` → `net9.0`; commit inspeccionado añade explícitamente soporte a `10.11.11` (el target exacto de JellyNotify).
- **Archivos relevantes leídos íntegramente:**
  - `src/Jellyfin.Plugin.CollectionSections/CollectionSectionPlugin.cs`
  - `src/Jellyfin.Plugin.CollectionSections/Services/StartupService.cs`
  - `src/Jellyfin.Plugin.CollectionSections/Configuration/SectionsConfig.cs`
  - `src/Jellyfin.Plugin.CollectionSections/ResultsHandler.cs`
  - `src/Jellyfin.Plugin.CollectionSections/Model/HomeScreenSectionPayload.cs`
  - `src/Jellyfin.Plugin.CollectionSections/Configuration/config.html` (UI admin: confirma que `UniqueId` es texto libre introducido por el admin, valor por defecto `"CHANGE_ME"`, no un GUID auto-generado por JS)
  - `src/Jellyfin.Plugin.CollectionSections/JellyfinVersionSpecific/10.11/StartupServiceHelper.cs` (confirma `TaskTriggerInfoType.StartupTrigger`)
  - `src/Jellyfin.Plugin.CollectionSections/Jellyfin.Plugin.CollectionSections.csproj` (confirma cero `PackageReference` a HSS)

### jellyfin-plugin-file-transformation
- **URL:** https://github.com/IAmParadox27/jellyfin-plugin-file-transformation
- **Tag inspeccionado:** `2.5.11.0`
- **Commit:** `5bc7541be72d577a2b13382db124da69babcc162`
- **Fecha de consulta:** 2026-07-28
- **Licencia:** GPL-3.0
- **Target framework declarado en csproj:** `JellyfinVersion = 10.11.3` → `net9.0`.
- **Archivos relevantes leídos íntegramente:**
  - `src/Jellyfin.Plugin.FileTransformation/PluginInterface.cs` (contrato `RegisterTransformation(JObject payload)`)
  - `src/Jellyfin.Plugin.FileTransformation/Models/TransformationRegistrationPayload.cs`
  - `src/Jellyfin.Plugin.FileTransformation/Helpers/TransformationHelper.cs`
  - `src/Jellyfin.Plugin.FileTransformation/Infrastructure/WebFileTransformationService.cs`
  - `src/Jellyfin.Plugin.FileTransformation/PluginServiceRegistrator.cs`
  - `src/Jellyfin.Plugin.FileTransformation/Jellyfin.Plugin.FileTransformation.csproj`

### jellyfin-plugin-pages
- **URL:** https://github.com/IAmParadox27/jellyfin-plugin-pages
- **Tag inspeccionado:** `2.4.11.0`
- **Commit:** `352eed217fe8d762c9105a4bd189b685d6be88be` (mensaje: "Added support for 10.11.9")
- **Fecha de consulta:** 2026-07-28
- **Licencia:** GPL-3.0
- **Target framework declarado en csproj:** `JellyfinVersion = 10.11.2` → `net9.0`.
- **Archivos relevantes leídos íntegramente:**
  - `src/Jellyfin.Plugin.PluginPages/Library/IPluginPagesManager.cs`
  - `src/Jellyfin.Plugin.PluginPages/PluginPagesPlugin.cs`
  - `src/Jellyfin.Plugin.PluginPages/Manager/PluginPagesManager.cs`
  - `src/Jellyfin.Plugin.PluginPages/Services/StartupService.cs`
  - `src/Jellyfin.Plugin.PluginPages/Jellyfin.Plugin.PluginPages.csproj`

---

## 1. Contrato real de registro de sección externa

**No existe una interfaz `IPluginSection` documentada públicamente.** El mecanismo real es un contrato débilmente acoplado por reflexión + JSON, no una interfaz C# compartida por NuGet. Esto es deliberado por parte del autor: evita que los plugins de terceros tengan que fijar una versión exacta del ensamblado de HSS.

### Cómo se registra (mecanismo verificado)

En tiempo de ejecución, cualquier plugin ya cargado en el mismo proceso de Jellyfin puede registrar una sección así (código real, `PluginInterface.cs` líneas 16-75):

```csharp
public static class PluginInterface
{
    public static void RegisterSection(JObject rawPayload)
    {
        IHomeScreenManager homeScreenManager = HomeScreenSectionsPlugin.Instance.ServiceProvider
            .GetRequiredService<IHomeScreenManager>();

        SectionRegisterPayload? payload = rawPayload.ToObject<SectionRegisterPayload>();
        homeScreenManager.RegisterResultsDelegate(new PluginDefinedSection(
            payload.Id, payload.DisplayText!, payload.Route, payload.AdditionalData)
        {
            OnGetResults = sectionPayload => { /* invoca ResultsAssembly/Class/Method o ResultsEndpoint */ }
        });
    }
}
```

El payload real (`SectionRegisterPayload.cs`) acepta estos campos JSON:

| Campo JSON | Tipo | Uso |
|---|---|---|
| `id` | string (requerido) | UUID/clave estable de la sección. Es la clave del diccionario interno `m_delegates`. |
| `displayText` | string | Texto/HTML mostrado como título (ver sección 3, logo). |
| `limit` | int? | Número de instancias de la sección (normalmente 1). |
| `route` | string? | Ruta SPA al hacer click en el título. |
| `additionalData` | string? | Cadena libre pasada de vuelta en cada petición de resultados (p. ej. usada por collection-sections para llevar el nombre de la colección). |
| `resultsEndpoint` | string? | URL HTTP a la que HSS hace `POST` con el payload de la petición y espera un `QueryResult<BaseItemDto>` JSON. |
| `resultsAssembly` / `resultsClass` / `resultsMethod` | string? | Alternativa **in-process**: HSS localiza el ensamblado ya cargado (`AssemblyLoadContext.All`), crea una instancia de la clase vía `ActivatorUtilities.CreateInstance` (con inyección de dependencias del *propio* contenedor de HSS) e invoca el método, que debe devolver `QueryResult<BaseItemDto>`. |

**Hay dos vías de invocación equivalentes**, ambas verificadas en código:
1. **HTTP:** `POST /HomeScreen/RegisterSection` en `HomeScreenController.cs` (línea 276). **Nota de seguridad:** este endpoint HTTP **no tiene atributo `[Authorize]`** — cualquier proceso con acceso de red al puerto de Jellyfin puede registrar una sección falsa. En la práctica de referencia (collection-sections) esta vía HTTP está en el código pero es **código muerto** (hay un `continue;` antes de alcanzarla, línea 96 de `CollectionSectionPlugin.cs`), es decir, el propio autor la abandonó a favor de la vía in-process.
2. **In-process por reflexión** (la vía realmente usada por collection-sections, y la recomendada para nuestro plugin): buscar el ensamblado cargado cuyo `FullName` contiene `.HomeScreenSections`, obtener el tipo `Jellyfin.Plugin.HomeScreenSections.PluginInterface` y su método estático `RegisterSection`, e invocarlo pasando un `JObject`. Código real de referencia (`CollectionSectionPlugin.cs` líneas 131-153):

```csharp
Assembly? homeScreenSectionsAssembly = AssemblyLoadContext.All.SelectMany(x => x.Assemblies)
    .FirstOrDefault(x => x.FullName?.Contains(".HomeScreenSections") ?? false);

if (homeScreenSectionsAssembly == null) {
    logger.LogError("Couldn't find Home Screen Sections assembly ... Ensure you have `Home Screen Sections` installed on your server.");
    return; // degradación correcta: no lanza excepción, sólo loguea y continúa
}

Type? pluginInterfaceType = homeScreenSectionsAssembly.GetType("Jellyfin.Plugin.HomeScreenSections.PluginInterface");
pluginInterfaceType?.GetMethod("RegisterSection")?.Invoke(null, new object?[] { payload });
```

Esta es exactamente la misma técnica que HSS usa para hablar con File Transformation (ver sección 4), así que es un patrón idiomático y probado en todo el ecosistema, no una improvisación.

**Confianza: alta.** Código fuente completo leído y verificado línea por línea, con un ejemplo de uso real en producción (collection-sections) que confirma el patrón funciona.

## 2. Cuándo se registra, cómo se descubre, cómo persiste, qué pasa al reiniciar

Hallazgo crítico: **`IHomeScreenManager` guarda las secciones registradas en un diccionario en memoria (`Dictionary<string, IHomeScreenSection> m_delegates`), sin persistencia propia.** HSS no escribe a disco qué secciones de terceros están registradas. Esto significa:

- **Al reiniciar Jellyfin, todas las secciones registradas por plugins externos desaparecen** hasta que ese plugin vuelva a llamar a `RegisterSection`.
- La responsabilidad de volver a registrar tras cada arranque es exclusivamente del plugin consumidor (nosotros), **no** de HSS.
- El patrón verificado y usado en producción (`StartupService.cs` de collection-sections, y el propio `StartupService.cs` de HSS para su registro con File Transformation) es un `IScheduledTask` con un trigger de tipo arranque (`StartupServiceHelper.GetDefaultTriggers()` / `GetStartupTrigger()`, específico por versión de Jellyfin en `JellyfinVersionSpecific/*/StartupServiceHelper.cs`) que Jellyfin ejecuta automáticamente una vez al iniciar el servidor.
- collection-sections además re-registra sus secciones cada vez que el administrador guarda la página de configuración del plugin (evento `ConfigurationChanged` de `BasePlugin<TConfig>`), así que el ciclo completo es: **arranque del servidor → tarea programada de arranque → re-registro de todas las secciones definidas en la configuración del plugin; guardado de configuración → mismo re-registro**.

### Qué SÍ persiste, y por qué el UUID sobrevive a reinicios y ediciones

**Corrección importante tras verificar `HomeScreenSectionService.cs` línea a línea:** el orden/posición de una sección en Modular Home **NO** viene del orden dentro de la lista `EnabledSections` de cada usuario. `EnabledSections` es únicamente un filtro booleano de inclusión (`sectionTypes.Where(x => settings.EnabledSections.Contains(x.Section))`, `HomeScreenSectionService.cs` línea 160). La posición real viene de un array **global, a nivel de administrador**, `PluginConfiguration.SectionSettings[]` (definido en `Configuration/PluginConfiguration.cs` líneas 65 y 84-101), donde cada entrada tiene `SectionId` (string) + `OrderIndex` (int) + `ViewMode` + `Enabled` + `AllowUserOverride`. En `CacheSectionsForUser` (`HomeScreenSectionService.cs` líneas 162-165) las secciones se agrupan y ordenan así:

```csharp
IGrouping<int, SectionSettings>[] groupedOrderedSections = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings
    .OrderBy(x => x.OrderIndex)
    .GroupBy(x => x.OrderIndex)
    .ToArray();
```

Y el emparejamiento entre una sección registrada y su configuración de orden es, de nuevo, por el string `SectionId == sectionType.Section` (línea 207). Cuando varias secciones comparten el mismo `OrderIndex` (p. ej. dos "huecos" del mismo grupo), el orden interno entre ellas se decide con `sectionList.Shuffle()` (línea 237) — es decir, dentro de un mismo índice el orden es aleatorio en cada carga, por diseño.

Lo que HSS persiste en disco es: (a) `ModularHomeSettings.json` dentro de `PluginConfigurationsPath/Jellyfin.Plugin.HomeScreenSections/` — el **estado por usuario** (`ModularHomeUserSettings`): `EnabledSections` (filtro de inclusión, sin orden) y `LockedSections`; y (b) el propio `PluginConfiguration.SectionSettings[]` de HSS, persistido en el XML de configuración estándar de plugin de Jellyfin (`Jellyfin.Plugin.HomeScreenSections.xml`, vía `BasePluginConfiguration`), que es donde realmente vive el `OrderIndex` — **una posición global, no por usuario**. Ambos estructuras referencian las secciones **por el mismo string `id`** que se pasa en el payload de registro.

Conclusión verificada: mientras nuestro plugin registre **siempre la misma cadena `id` (UUID)** para una sección determinada en cada arranque — incluso si cambia `displayText`, `additionalData`, filtros, etc. — HSS seguirá reconociendo esa sección como la misma entidad en `ModularHomeUserSettings`, y el usuario conservará su posición y estado de activación tras reinicios, ediciones e incluso tras actualizar el plugin HSS o el nuestro. **Editar cualquier campo salvo el `id` no debe romper la posición.** Esto confirma directamente el requisito de la sección 8 del brief.

**Detalle adicional verificado en collection-sections (`Configuration/config.html` líneas 65-129):** el propio "UUID" (`UniqueId`) de cada sección **no se genera automáticamente** (no hay `crypto.randomUUID()` ni similar en el JS de configuración) — es un campo de texto libre que el administrador escribe a mano en el formulario, con valor por defecto literal `"CHANGE_ME"` si no se edita. Si el admin deja dos secciones con el mismo `UniqueId`, o lo cambia por error tras la primera publicación, se pierde la asociación de posición/estado descrita arriba. **Para nuestro plugin, generar el `id` automáticamente en el backend (GUID real, inmutable, nunca editable desde la UI) es una mejora directa sobre el precedente de collection-sections**, no sólo una opción — evita esta clase de error humano documentado en el propio código de referencia.

- **Eliminar una sección** (borrar su definición en nuestro plugin) simplemente implica dejar de re-registrarla en el siguiente arranque/guardado; quedará "huérfana" en `EnabledSections` de los usuarios que la tenían activa hasta que ellos mismos la desactiven o el admin limpie via configuración de HSS (comportamiento no destructivo, pero sí deja un id huérfano en el JSON — a documentar como limitación conocida, no bloqueante).
- **Desactivar una sección** en nuestro plugin: dado que HSS no tiene un concepto explícito de sección "deshabilitada por el proveedor" en su modelo de terceros, la única forma limpia de "desactivar" es **no re-registrarla** en el próximo ciclo de arranque/guardado (queda ausente de `m_delegates`, por lo que `InvokeResultsDelegate` devuelve `QueryResult<BaseItemDto>` vacío para esa clave si un usuario aún la tiene "enabled"). Alternativa más agresiva no verificada: no se ha encontrado una API de "unregister" — no existe un método `UnregisterResultsDelegate` en `IHomeScreenManager` (confirmado leyendo la interfaz completa). **Limitación real de HSS, no inventada.**

**Confianza: alta** para el mecanismo de registro/persistencia de posición (código leído). **Confianza media** para el comportamiento exacto de "sección huérfana" tras eliminar — es una inferencia razonable a partir del código leído, no se ha probado en vivo (pendiente de validar en el entorno de pruebas real, fase E2E).

## 3. Logo a la izquierda del título — SOLUCIÓN ENCONTRADA Y VERIFICADA POR CÓDIGO

Ver `08-provider-logo-rendering.md` para el análisis completo. Resumen: **`displayText` se inyecta sin escapar como `innerHTML`** en el título de la sección (`loadSections.js`, línea 320: `elem.innerHTML = html;`, con `html += sectionInfo.DisplayText` sin sanitizar en las líneas 295 y 301). Esto permite pasar HTML real (p. ej. `<img>` + `<span>`) como `displayText` y renderizar el logo exactamente a la izquierda del título, usando el mecanismo nativo de HSS, sin fork de `jellyfin-web` ni dependencia añadida de File Transformation por nuestra parte.

### 3.1 Confirmación explícita: NO existe ningún campo de icono/logo nativo

Búsqueda exhaustiva (`grep -rniE "\bicon\b|\blogo\b"` sobre todo `*.cs`/`*.js`/`*.html`/`*.css` del repo, excluyendo `obj/`/`bin/`) para no dar nada por supuesto:

- La interfaz de contrato completa `IHomeScreenSection` (`Library/IHomeScreenManager.cs` líneas 32-53) expone exactamente: `Section`, `DisplayText`, `Limit`, `Route`, `AdditionalData`, `OriginalPayload`, `TranslationMetadata`. **Ningún campo `Icon`, `Logo`, `Image` ni `IconUrl`.**
- El DTO de info que via el API `HomeScreenSectionInfo` (mismas líneas 72-99) añade `ContainerClass`, `ViewMode`, `DisplayTitleText`, `ShowDetailsMenu`, `AllowViewModeChange`, `AllowHideWatched`, `OrderIndex` — tampoco ningún campo de icono/imagen.
- El payload de registro de terceros `SectionRegisterPayload.cs` (campos `id`, `displayText`, `limit`, `route`, `additionalData`, `resultsEndpoint`, `resultsAssembly/Class/Method`) — tampoco.
- El único acierto que devuelve el grep es `HomeScreenSectionsPlugin.cs:93`, `{ "Icon", "ballot" }` — pero es el **icono Material del ítem de menú del panel de administración de Jellyfin** para la propia página de ajustes de HSS ("Modular Home" en el sidebar del Dashboard), **no** un icono asociado a la fila/sección en la home del usuario. Mismo patrón en `jellyfin-plugin-pages`: `PluginPage.Icon` (`Library/IPluginPagesManager.cs` línea 18) también es exclusivamente el icono del ítem del menú lateral de administración, sin relación con secciones de la home.
- El frontend (`loadSections.js` líneas 277-301) renderiza el título así: `html += '<h2 class="sectionTitle sectionTitle-cards">'; html += sectionInfo.DisplayText; html += "</h2>";` — texto/HTML puro, sin ningún `<img>`/`<span class="icon">` reservado junto al título.

**Conclusión: confirmado por código, no inferido — HSS no tiene soporte nativo de icono/logo por sección.** La única vía nativa disponible es el truco de HTML embebido en `displayText` descrito arriba (sección 3, ver detalle completo en `08-provider-logo-rendering.md`).

## 4. Dependencia real de File Transformation

HSS **sí depende en tiempo de ejecución** de File Transformation para parchear `jellyfin-web` (inyectar su propio `.js`/`.css` en `index.html`, y parchear el chunk que contiene `,loadSections:` para añadir el renderizado de secciones de terceros). Verificado en `StartupService.cs` de HSS (líneas 80-95): usa la **misma técnica de reflexión** (`AssemblyLoadContext.All` → tipo `Jellyfin.Plugin.FileTransformation.PluginInterface` → método `RegisterTransformation`) para registrarse con File Transformation.

**Implicación para nuestro plugin:** no necesitamos hablar con File Transformation directamente. Nuestra dependencia es transitiva: *Provider Sections → depende de HSS (vía reflexión) → HSS depende de File Transformation (para poder pintar algo en absoluto)*. Si File Transformation falta, HSS seguirá cargando igualmente como plugin .NET (sus servicios y API HTTP funcionan), pero **su frontend no se inyectará en Jellyfin Web** — es decir, ninguna sección (ni las nuestras ni las nativas de HSS) aparecerá visualmente, aunque el registro haya tenido éxito a nivel de API. Este es un caso de degradación a documentar: "TMDb/Seerr conectados y sección registrada correctamente, pero invisible en Modular Home" — el plan debe incluir un diagnóstico que lo detecte (p. ej. comprobar si el ensamblado de File Transformation está cargado, y avisar al admin).

Contrato de `RegisterTransformation` verificado (`PluginInterface.cs` de file-transformation): recibe `JObject` con `Id`, `FileNamePattern`, y referencias de callback (`callbackAssembly`/`callbackClass`/`callbackMethod`), igual patrón que HSS.

### 4.1 Mecanismo interno de File Transformation (cómo intercepta realmente `index.html`)

Verificado leyendo `PluginServiceRegistrator.cs`, `WebFileTransformationService.cs`, `TransformationHelper.cs` de `jellyfin-plugin-file-transformation` (commit inspeccionado más abajo):

1. **Enganche al pipeline de archivos estáticos de Jellyfin:** `PluginServiceRegistrator.RegisterServices` asigna `StartupHelper.WebDefaultFilesFileProvider` y `StartupHelper.WebStaticFilesFileProvider` a un delegado propio (`GetFileTransformationFileProvider`) que **sustituye** el `PhysicalFileProvider` estándar de ASP.NET Core por un `PhysicalTransformedFileProvider` a medida (`Infrastructure/PhysicalTransformedFileProvider.cs`). Esto requiere parchear con Harmony el método `Startup.Configure()` interno del servidor Jellyfin (referencia a `Lib.Harmony` en el `.csproj`) — es decir, **no es una API pública de extensión de Jellyfin, es un parche de runtime sobre el propio host**. Si esa firma cambia entre versiones de Jellyfin server, File Transformation puede dejar de funcionar sin previo aviso (riesgo documentado por el propio autor en un comentario del código: *"The Harmony prefix on Startup.Configure() may fire before the DI container is fully built..."*).
2. **Coincidencia de ruta por regex:** `WebFileTransformationService.NeedsTransformation`/`RunTransformation` (líneas 23-83) primero intentan una coincidencia exacta de ruta normalizada y si no, recorren las claves registradas tratándolas como **expresiones regulares** (`new Regex(x).IsMatch(path)`). Esto es lo que permite a HSS registrar un patrón dinámico como `"nombreChunk\\.[^.]+\\.chunk\\.js"` para enganchar el hash de compilación variable de un chunk de Jellyfin Web (ver más abajo).
3. **Tres formas de resolver la transformación en sí** (`TransformationHelper.ApplyTransformation`, con fallback en cascada): (a) invocación in-process por reflexión sobre `callbackAssembly/Class/Method` (la que usan HSS y Pages); (b) IPC vía `NamedPipeClientStream` con `transformationPipe` (protocolo propio: 8 bytes de longitud + payload UTF-8 JSON); (c) `POST` HTTP a `transformationEndpoint`. HSS y Pages sólo usan la vía (a) en la práctica.
4. **Nivel de invasividad real usado por HSS:** no se limita a `index.html`. `StartupService.cs` de HSS (líneas 58-78) **escanea todos los `*.chunk.js` del `WebPath` de Jellyfin buscando el string literal `",loadSections:"`**, y para el/los que lo contienen registra una segunda transformación (`TransformationPatches.LoadSections`) que hace un *string replace* quirúrgico: inserta su propio `loadSections.js` justo después de la función `loadSections` original del bundle minificado de Jellyfin Web, capturando por regex el nombre de variable minificado que la contiene (`var\s+([a-zA-Z][^=]*)=`) para poder referenciarla. Esto es **extremadamente frágil** (depende de nombres de variable generados por el minificador de webpack, que cambian entre builds de `jellyfin-web`) y es la razón de que el repo mantenga carpetas `JellyfinVersionSpecific/10.10.7/` y `JellyfinVersionSpecific/10.11/` con hooks distintos (`{{cardbuilder_hook}}` = `"h"` en 10.10.7 vs `"u"` en 10.11.x). **Para nuestro plugin, este nivel de patching NO es necesario ni recomendable** — nos basta con el truco de `displayText` como HTML (sección 3), que no requiere tocar ningún chunk JS.

### 4.2 Confirmación independiente: cero acoplamiento en tiempo de compilación

Se ha verificado leyendo los 4 `.csproj` reales que **ninguno de los cuatro plugins tiene un `PackageReference` a los ensamblados de los otros**. `jellyfin-plugin-collection-sections.csproj` sólo referencia `Jellyfin.Model`, `Jellyfin.Controller` y `Newtonsoft.Json` — nada de HSS. `jellyfin-plugin-home-sections.csproj` sólo referencia `Jellyfin.Model`, `Jellyfin.Controller`, `Jellyfin.Extensions`, `Lib.Harmony`, `SkiaSharp`, `Newtonsoft.Json` — nada de File Transformation ni Pages. Esto confirma de forma independiente (no sólo por inspección del código de registro) que el ecosistema completo de IAmParadox27 se apoya exclusivamente en reflexión sobre ensamblados ya cargados en el mismo proceso (`AssemblyLoadContext.All`), nunca en referencias de compilación. Nuestro plugin debe seguir exactamente el mismo patrón: cero `PackageReference` hacia HSS/File Transformation/Pages.

## 5. Plugin Pages — NO es una dependencia necesaria

Ampliado tras leer código real (no sólo el README) de `jellyfin-plugin-pages` (`Library/IPluginPagesManager.cs`, `PluginPagesPlugin.cs`, `Manager/PluginPagesManager.cs`, `Services/StartupService.cs`, `HomeScreenSectionsPlugin.cs` líneas 70-98 del lado de HSS):

- **Contrato:** `IPluginPagesManager.RegisterPluginPage(PluginPage page)`, con `PluginPage { Id, Url, DisplayText, Icon }`. `Icon` aquí es un nombre de icono Material para el ítem del menú hamburguesa de usuario — **no** tiene relación con el título de una sección de la home (confirmado, ver sección 3.1).
- **Descubrimiento — un CUARTO mecanismo distinto a los tres ya vistos (HTTP, reflexión in-process, IPC por pipe):** Pages no expone una vía de registro por reflexión como HSS/File Transformation. En su lugar, **lee un fichero JSON compartido en disco** (`PluginConfigurationsPath/Jellyfin.Plugin.PluginPages/config.json`) dentro del **constructor** de `PluginPagesPlugin` (`PluginPagesPlugin.cs` líneas 22-54), y cualquier otro plugin que quiera añadir una página escribe directamente ese `config.json` (así lo hace HSS: `HomeScreenSectionsPlugin.cs` líneas 75-97, añadiendo un `JObject` al array `pages` y reescribiendo el fichero completo).
- **Riesgo de orden de carga (no documentado por el autor, inferido del código):** como la lectura ocurre **una sola vez, en el constructor** de `PluginPagesPlugin`, si otro plugin escribe su entrada en `config.json` **después** de que Jellyfin ya haya instanciado `PluginPagesPlugin` durante el arranque, esa página no se registrará hasta el **siguiente reinicio** del servidor. HSS mitiga esto comprobando primero si su propia entrada ya existe en el `config.json` antes de reescribirlo (para no duplicar en cada arranque), pero no hay ninguna garantía de orden de instanciación de plugins en Jellyfin — es un riesgo latente compartido por cualquier plugin que dependa de Pages, incluido el nuestro si decidiéramos usarlo en el futuro.
- Requiere File Transformation para inyectarse en `index.html`, `userpluginsettings.html` y hasta **cinco chunks JS distintos** (`user-plugin.undefined.chunk.js`, `user-plugin-index-html.undefined.chunk.js`, `main.jellyfin.bundle.js` ×2 callbacks distintos, `runtime.bundle.js`) — verificado en `Services/StartupService.cs` líneas 30-107. Este es, con diferencia, el uso más invasivo de File Transformation de los cuatro repos analizados, porque Pages necesita añadir rutas enteras nuevas al router SPA de Jellyfin Web, no sólo texto.
- **No interviene en el registro de secciones ni en el renderizado del título/logo.** Nuestro plugin no lo necesita para el MVP: la página de administración de "Jellyfin Provider Sections" puede usar el mecanismo estándar de página de configuración de plugin (`IHasWebPages.GetPages()` + HTML embebido), exactamente el patrón que ya usan tanto collection-sections como el propio JellyNotify (`configPage.html`).

## 5bis. Precedente directo para TMDb + Seerr: `DiscoverSection.cs` (código real de HSS)

Hallazgo con alto valor de diseño para nuestro plugin, no pedido explícitamente en el encargo original pero descubierto al leer el código: **HSS ya incluye, de fábrica, una sección "Discover" que resuelve exactamente el mismo problema que Provider Sections** (contenido no presente en la biblioteca local, obtenido de una fuente externa, con botón de "solicitar"). Archivo: `HomeScreen/Sections/DiscoverSection.cs` (y sus variantes `DiscoverMoviesSection.cs`/`DiscoverTvSection.cs`).

Puntos verificados en código:

- Usa **Jellyseerr** (no TMDb directamente) como fuente: llama a `{JellyseerrUrl}/api/v1/discover/trending`, resuelve el ID de usuario de Jellyseerr buscando por `Username` (`/api/v1/user?q=...`), y añade la cabecera `X-Api-User` para autenticar como ese usuario.
- **Construye objetos `BaseItemDto` totalmente sintéticos** para ítems que no existen en la biblioteca — no vienen de `ILibraryManager` ni de `IDtoService`. Rellena sólo: `Name`, `OriginalTitle`, `SourceType` (`"movie"`/`"tv"`), `CommunityRating`, `PremiereDate`, y sobre todo `ProviderIds` como bolsa de datos ad-hoc: `{"JellyseerrRoot": <url base para el link>, "Jellyseerr": <id>, "JellyseerrPoster": <url de imagen cacheada>}`. Es decir, **reutiliza el diccionario `ProviderIds` (pensado para IDs de proveedor tipo IMDB/TMDB) como canal genérico de metadatos propios** — un patrón directamente aplicable a nuestro plugin (podríamos usar `ProviderIds["Tmdb"]`, y campos propios como `ProviderIds["JellyProviderSections.PosterUrl"]`).
- **Imágenes cacheadas localmente:** nunca sirve la URL de `image.tmdb.org` directamente al cliente; pasa por `ImageCacheHelper.GetCachedImageUrl` → `ImageCacheService`, que descarga, redimensiona (`MaxImageWidth`, `ImageJpegQuality` en `PluginConfiguration.cs`) y sirve desde `/HomeScreen/CachedImage/{cacheKey}`. Mitiga hotlinking y permite servir sobre HTTPS del propio servidor aunque TMDb no esté accesible desde el cliente final.
- **Frontend (`loadSections.js` función `createDiscoverCards`, líneas ~121-172):** detecta estos ítems sintéticos por convención (`item.ProviderIds.Jellyseerr` presente) y renderiza una tarjeta especial `discover-card` con un botón de overlay `discover-requestbutton` (icono `add`) en vez del botón de reproducción normal; el click en la tarjeta o el título abre `{JellyseerrRoot}/{SourceType}/{Jellyseerr}` en una pestaña nueva (no navegación interna de Jellyfin), y el botón de request llama a un endpoint propio (`POST /HomeScreen/DiscoverRequest`, `HomeScreenController.cs` líneas 301-358) que reenvía la petición de solicitud a la API de Jellyseerr (`POST /api/v1/request`).
- **Implicación directa para nuestro diseño:** no necesitamos inventar desde cero el patrón "ítem no local + botón solicitar"; podemos seguir el mismo esquema (BaseItemDto sintético + `ProviderIds` como bolsa de metadatos + caché de imagen local + botón de overlay dedicado), adaptándolo de Jellyseerr a **TMDb directo** (para el catálogo/listado del proveedor) y a **Seerr genérico** (Jellyseerr u Overseerr, según corresponda) sólo para la acción de "solicitar". A diferencia de `DiscoverSection`, nuestras secciones no dependen de que Jellyseerr tenga sincronizado el catálogo de streaming — la fuente de verdad del catálogo es TMDb (`watch/providers` / discover por proveedor), y Seerr es opcional y sólo para la acción de petición.

## 6. Qué pasa si falta una dependencia (comportamiento verificado, no supuesto)

| Escenario | Comportamiento verificado |
|---|---|
| Falta HSS | Nuestro `AssemblyLoadContext.All...FirstOrDefault` devuelve `null`; el patrón de referencia (collection-sections) simplemente loguea un error y hace `return` — no lanza excepción, el resto del plugin sigue funcionando. Debemos replicar esto y reflejar el estado "Home Screen Sections: no detectado" en la UI de cada sección. |
| HSS presente pero versión incompatible (tipo/método no existen) | `GetType(...)` o `GetMethod(...)` devuelven `null` — mismo patrón defensivo aplica (comprobar null antes de invocar). Verificado en el propio código de referencia. |
| Falta File Transformation | HSS se instala y su API HTTP funciona, pero su propio frontend no se inyecta (ver punto 4). Las secciones registradas no serán visibles. No verificado en vivo — pendiente de comprobar en el entorno de pruebas (parar/no instalar File Transformation y observar). |
| Falta Plugin Pages | Sin impacto en nuestra funcionalidad (ver punto 5). |

## Limitaciones de esta investigación

- No se ha instanciado un Jellyfin real con estos plugins instalados; todo lo anterior es lectura de código fuente, no observación en tiempo de ejecución. La fase de entorno de pruebas (Docker) debe confirmar empíricamente: (a) que el registro por reflexión efectivamente aparece en Modular Home, (b) que la posición sobrevive a un reinicio real, (c) el comportamiento exacto de una sección "huérfana" tras dejar de registrarla.
- No se ha leído el 100% de `loadSections.js` (641 líneas; se leyeron ~360, incluyendo el bloque completo de renderizado del título). El resto del fichero trata del `fetchData`/paginación de scroll infinito, no relevante para el contrato de registro ni para el logo.
- La versión objetivo fijada en el csproj de HSS (`10.11.5`) es ligeramente anterior a la que usa JellyNotify (`10.11.11`), pero el commit de collection-sections más reciente confirma explícitamente soporte para 10.11.11 ("Add support for 10.11.11"), y HSS usa el mismo patrón `JellyfinVersionSpecific/10.11/*` para abstraer diferencias — riesgo de compatibilidad calificado como **bajo**, a confirmar con un `docker compose up` real con Jellyfin 10.11.11 antes de dar por cerrado el plan.

## Riesgos identificados

1. **Endpoint HTTP `/HomeScreen/RegisterSection` sin autenticación.** No es un riesgo que creemos nosotros, pero si documentamos esa vía como alternativa debemos advertir explícitamente que no debe exponerse fuera de localhost. Recomendación: usar exclusivamente la vía in-process por reflexión (no HTTP) para evitar esta superficie.
2. **Sin API de "unregister".** No existe forma de eliminar limpiamente una sección ya registrada de `m_delegates`; sólo se puede dejar de re-registrarla. A mitigar documentando el comportamiento esperado (sección deshabilitada devuelve resultados vacíos) y probándolo en el entorno E2E.
3. **Acoplamiento por reflexión a nombres de tipo/método como cadenas literales** (`"Jellyfin.Plugin.HomeScreenSections.PluginInterface"`, `"RegisterSection"`). Si el autor de HSS renombra la clase o el método en una versión futura, nuestro plugin fallará silenciosamente (con el mismo patrón defensivo de "log y continue"). Mitigación: fijar y documentar la versión mínima de HSS probada (2.5.11.0) y comprobar el tipo/método en el diagnóstico de la página de administración.
4. **Dependencia transitiva no declarada formalmente.** Jellyfin no tiene un mecanismo de manifest.json para declarar "depende de plugin X"; todo el ecosistema de IAmParadox27 resuelve esto en tiempo de ejecución por reflexión. Nuestro plugin debe seguir el mismo patrón y no puede impedir la instalación si faltan dependencias — sólo advertir en su propia UI de diagnóstico.

## Impacto en el diseño

- El componente "HomeSectionsRegistrar" de nuestro plugin debe ser un `IScheduledTask` de arranque (como collection-sections) que reintenta/registra todas las secciones activas cada vez que el servidor arranca y cada vez que se guarda la configuración.
- El campo `id` (UUID) de cada `SectionDefinition` en nuestro modelo de configuración es el campo crítico que debe permanecer inmutable a través de ediciones — validar esto en el backend (rechazar cualquier intento de cambiarlo desde la API de edición).
- `displayText` se construye en nuestro backend como HTML controlado (logo + texto), nunca como texto plano si queremos el logo.
- No se requiere ninguna dependencia de compilación (`PackageReference`) hacia HSS/File Transformation/Pages — toda la integración es en tiempo de ejecución vía reflexión sobre `JObject`, igual que el resto del ecosistema.

---

## Home Screen Sections / File Transformation (por agente HSS)

(Contenido específico de logo trasladado a `08-provider-logo-rendering.md`, sección con este mismo encabezado, para no duplicar.)

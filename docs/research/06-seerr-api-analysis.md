# 06 — Análisis de la API de Seerr

> Investigación pura, sin código de producción. Objetivo: determinar el contrato real de la API de
> Seerr (fork/continuación comunitaria de Jellyseerr/Overseerr) para diseñar la integración
> "Solicitar contenido" del nuevo plugin `Jellyfin Provider Sections`, y compararlo con el cliente
> Seerr ya en producción en JellyNotify (`SeerrApiClient.cs`).
>
> **No se ha podido probar contra una instancia Seerr real en ejecución** (no hay servidor Seerr
> disponible en este entorno). Todo lo documentado abajo proviene de lectura directa del código
> fuente del repositorio público `seerr-team/seerr` (clonado localmente) y del código local ya en
> producción. Todo lo que no se pudo confirmar así se marca explícitamente como
> **"pendiente de verificación contra instancia real"**.

---

## Nota añadida (2026-07-29): Seerr no requiere que el admin configure su propia clave de TMDb

Verificado por lectura directa de `server/api/themoviedb/index.ts` en un clon fresco de
`seerr-team/seerr` (rama `develop`, consultado 2026-07-29): la clase `TheMovieDb` construye su
cliente HTTP con `api_key: '431a8708161bcd1f1fbe7536137e61ed'` **hardcodeada en el código fuente**,
sin ningún campo de configuración para sobrescribirla desde Ajustes. Es la misma clave "comunitaria"
que Overseerr/Jellyseerr llevan usando desde su origen para su propio consumo interno de metadatos
(carátulas, sinopsis, búsquedas dentro de la propia UI de Seerr).

**Implicación para el entorno de pruebas y para el plugin nuevo:**

- La instancia Seerr autoalojada en `docker-compose.yml` (`10-testing-environment.md`) **no necesita
  ningún TMDb API key configurado para funcionar** — arranca y sirve metadatos por sí sola con su
  clave embebida, sin ningún paso adicional de configuración TMDb en Seerr.
- Esto es **completamente independiente** del TMDb API Read Access Token que el propio plugin
  `Jellyfin Provider Sections` necesita para sus propias llamadas a Discover/Watch Providers — el
  plugin habla con la API de TMDb directamente, no a través de Seerr, así que sigue necesitando su
  propio token (ver `05-tmdb-provider-analysis.md`).
- **No se debe reutilizar la clave embebida de Seerr en el plugin nuevo**: está pensada solo para el
  consumo interno de Seerr (rate limit propio, alcance de uso no documentado para terceros), y
  depender de una constante hardcodeada de otro proyecto de terceros sería frágil y no es una
  práctica de integración legítima — el admin del plugin sigue debiendo aportar su propio token, tal
  como ya recoge `research/11-open-questions-and-readiness.md`.

## Fuente 1: Código local ya en producción — `JellyNotify.Plugin`

- **Nombre**: JellyNotify (cliente Seerr ya implementado y en producción)
- **URL**: N/A (repositorio local)
- **Tag/commit**: working tree actual, sin cambios pendientes relevantes al análisis
- **Fecha de consulta**: 2026-07-28
- **Archivos relevantes**:
  - `/home/alvaro/Descargas/jellyfinnotify/JellyNotify/JellyNotify.Plugin/Services/SeerrApiClient.cs`
  - `/home/alvaro/Descargas/jellyfinnotify/JellyNotify/JellyNotify.Plugin/Services/ISeerrApiClient.cs`
  - `/home/alvaro/Descargas/jellyfinnotify/JellyNotify/JellyNotify.Plugin/Models/SeerrModels.cs`
  - `/home/alvaro/Descargas/jellyfinnotify/JellyNotify/JellyNotify.Plugin/Models/SeerrWebhookModels.cs`
- **Nivel de confianza**: Alto (código propio, en producción)

### Hallazgos

- **Autenticación**: header `X-Api-Key: {ApiKey}` (ver `CreateClient()`, línea 283-284 de
  `SeerrApiClient.cs`). Es una API key global de administrador guardada en
  `PluginConfiguration.SeerrSettings.ApiKey`. No usa nunca el header `X-API-User` (ver Fuente 2) ni
  ningún flujo de login (local/Jellyfin/Plex).
- **Endpoints ya cubiertos**:
  - `GET api/v1/status` → `SeerrTestResponse { version }` — test de conexión.
  - `GET api/v1/request?take=&skip=&sort=added` (paginado con `pageInfo`) y
    `GET api/v1/request/{id}` — solo lectura.
  - `GET api/v1/user/{id}` y `GET api/v1/user?take=&skip=` — solo lectura, por **ID numérico interno
    de Seerr**, nunca por GUID de Jellyfin.
  - `GET api/v1/movie/{tmdbId}` / `GET api/v1/tv/{tmdbId}` → mapeados a `SeerrMediaDetails`, que
    **solo captura `posterPath`, `title`/`name`, `releaseDate`/`firstAirDate`** — no captura el
    campo `mediaInfo` embebido en la respuesta real (ver Fuente 2), así que hoy el cliente no puede
    saber si algo ya está disponible/parcialmente disponible a partir de esta llamada.
  - `GET/POST api/v1/settings/notifications/webhook` y `POST .../webhook/test` — único endpoint de
    escritura que usa el cliente, y es para configurar el webhook saliente de Seerr, no para crear
    solicitudes.
- **Lo que el código local NO hace en absoluto**: no existe ningún método para `POST api/v1/request`
  (crear solicitudes). `ISeerrApiClient` no tiene ni una firma de método para esto. JellyNotify es
  puramente un consumidor de lectura + configurador de webhook; **toda la capacidad de "solicitar
  contenido" tiene que construirse desde cero** para el nuevo plugin.
- **Modelos de estado ya definidos localmente** (`SeerrModels.cs`):
  ```csharp
  public enum SeerrMediaStatus
  {
      Unknown = 1, Pending = 2, Processing = 3, PartiallyAvailable = 4,
      Available = 5, Deleted = 6, Blocklisted = 7
  }
  public enum SeerrRequestStatus
  {
      PendingApproval = 1, Approved = 2, Declined = 3, Failed = 4
  }
  ```
  **Ambos difieren del contrato real** — ver discrepancias en la sección de comparación.
- `SeerrMediaSeasonStatus` solo modela `seasonNumber` + `status` (un único campo `int`). La API real
  expone `status` **y** `status4k` por temporada (ver Fuente 2) — el modelo local no tiene dónde
  guardar el estado 4K de una temporada.
- `SeerrUser.JellyfinUserId` ya existe como campo (`[JsonPropertyName("jellyfinUserId")]`), así que
  el modelo de usuario está preparado para el mapeo Jellyfin↔Seerr, pero **no hay ningún método en
  `ISeerrApiClient` para buscar un usuario de Seerr a partir de un GUID de Jellyfin** — solo por ID
  numérico interno de Seerr (`GetUserByIdAsync(int seerrUserId)`).

### Limitaciones

- No cubre creación de solicitudes, selección de temporadas, 4K, cuotas, permisos, ni idempotencia.
- El `HttpClientHandler` local permite `IgnoreSslErrors` — a tener en cuenta en el documento de
  seguridad del plan si el nuevo plugin reutiliza este patrón.

### Riesgos

- Cualquier diseño del nuevo plugin que asuma que "ya hay algo reutilizable para crear peticiones"
  se equivoca: hay que construir el cliente de escritura completo.

### Impacto en el diseño

- El nuevo plugin necesita, como mínimo: (a) un método `CreateRequestAsync` completo con soporte de
  `seasons`/`is4k`/`userId`, (b) enriquecer `SeerrMediaDetails` (o un modelo nuevo) con el objeto
  `mediaInfo` completo (`status`, `status4k`, `seasons[].status`, `seasons[].status4k`) para poder
  pintar el botón "Solicitar" correctamente, (c) corregir los dos enums de estado, (d) un método de
  búsqueda de usuario de Seerr por GUID de Jellyfin.

---

## Fuente 2: `seerr-team/seerr` (repositorio público, código fuente)

- **Nombre**: Seerr (continuación/fork comunitario de Jellyseerr, que a su vez es un fork de
  Overseerr) — **confirmado**: el propio `README.md` del repo tiene una sección
  "## Migrating from Overseerr/Jellyseerr to Seerr" con un enlace a
  `https://docs.seerr.dev/blog/seerr-release` ("to learn what Seerr means for Jellyseerr and
  Overseerr users") y a una guía de migración (`https://docs.seerr.dev/migration-guide`).
- **URL**: https://github.com/seerr-team/seerr
- **Método de acceso**: clon completo (`git clone`) a `/tmp/research-seerr/` — código fuente real
  leído directamente, no documentación ni memoria del modelo.
- **Rama por defecto**: `develop` (confirmado vía `origin/HEAD -> origin/develop`).
- **Licencia**: MIT (`LICENSE`, `package.json: "license": "mit"`) — distinta de la GPL-3.0-or-later
  de JellyNotify/el plugin nuevo; no hay incompatibilidad para consumir la API vía HTTP (no se
  enlaza código), pero anotar si en algún momento se copiara código fuente de Seerr.
- **Tags/releases disponibles** (más recientes primero, `git tag --sort=-creatordate`):
  `v3.4.0`, `v3.3.0`, `v3.2.0`, `v3.1.1`, `v3.1.0`, `v3.0.1`, `v3.0.0`, más numerosas ramas
  `preview-*` de features en curso (no releases).
- **Tag inspeccionado en profundidad**: `v3.4.0` (commit `2dbe8860`, "chore(release): prepare
  v3.4.0", fechado 2026-07-28 — el mismo día de esta investigación). **`v3.3.0` (la versión que
  `manifest.json` de JellyNotify declara como verificada) existe y es real**, pero **ya no es la
  última versión estable**: `v3.4.0` la sucede.
- **Fecha de consulta**: 2026-07-28
- **Archivos inspeccionados** (contenido completo o en profundidad):
  - `server/routes/request.ts` (707 líneas, completo)
  - `server/entity/MediaRequest.ts` (método estático `request()` completo, ~545 líneas)
  - `server/interfaces/api/requestInterfaces.ts`
  - `server/middleware/auth.ts` (completo)
  - `server/constants/media.ts` (completo)
  - `server/routes/user/index.ts` (endpoints de import/lookup)
  - `server/routes/settings/index.ts` (sección `/jellyfin/sync`)
  - `server/routes/auth.ts` (lista de endpoints de login)
  - `server/routes/movie.ts` + `server/models/Movie.ts` (forma de la respuesta `mediaInfo`)
  - `server/entity/Media.ts`, `server/entity/Season.ts` (columnas `status`/`status4k`)
- **Nivel de confianza**: Alto para todo lo citado con nombre de archivo/línea (código fuente real
  de un tag de release). Medio para partes no revisadas exhaustivamente (`tv.ts` en detalle,
  `lib/permissions.ts` bit a bit, OIDC).

### Diferencia respecto a v3.3.0 (la versión que JellyNotify declara verificada)

Comparando `v3.3.0..v3.4.0` en los archivos relevantes para peticiones, hay **un cambio de API
relevante**:

- `feat(requests): allow admins to bypass user quota limits (#2026)` — añade el campo
  `ignoreQuota` al payload de creación de solicitud (`MediaRequestBody.ignoreQuota?: boolean`) y la
  lógica correspondiente en `MediaRequest.request()`. **Este campo no existía en v3.3.0.** Si el
  nuevo plugin quiere poder saltarse la cuota de un usuario de forma explícita al crear una
  solicitud en su nombre, necesita apuntar a Seerr **v3.4.0 o superior**, no v3.3.0.
- `feat: add jellyfin/emby quick connect authentication (#2212)` — añade autenticación por Quick
  Connect (`/auth/jellyfin/quickconnect/*`), irrelevante para un cliente servidor-a-servidor con API
  key.

No se detectaron cambios de ruptura (breaking changes) en los endpoints de película/serie/creación
de solicitudes entre v3.3.0 y v3.4.0 más allá de esa adición aditiva.

### Autenticación (código real: `server/middleware/auth.ts`)

Mecanismo de resolución de `req.user` (función `checkUser`, ejecutada en cada request):

```ts
if (req.header('X-API-Key') === settings.main.apiKey) {
  let userId = 1; // Work on original administrator account
  if (req.header('X-API-User')) {
    userId = Number(req.header('X-API-User'));
  }
  user = await userRepository.findOne({ where: { id: userId } });
} else if (req.session?.userId) {
  // sesión de cookie (login web)
}
```

Hallazgos clave (nombres de header exactos):

- **`X-API-Key`**: la API key global de administrador (`settings.main.apiKey`, generada por Seerr,
  visible en Ajustes → General). Si se envía **sola**, Seerr actúa como el **usuario administrador
  con `id: 1`** (el "original administrator account"), no como un admin "sin usuario".
- **`X-API-User: <id numérico>`**: header adicional, **solo tiene efecto si `X-API-Key` es
  correcta**. Hace que Seerr resuelva `req.user` como **ese usuario concreto** (por ID interno de
  Seerr) en lugar del admin id 1. **No hay ninguna comprobación de permisos para poder usar este
  header** — cualquiera que tenga la API key maestra puede suplantar a cualquier usuario. Esto es
  distinto y más directo que el campo `userId` del payload de creación de solicitud (ver abajo):
  con `X-API-User`, **todas** las comprobaciones de permisos/cuota/auto-aprobación de la petición se
  evalúan como si el usuario suplantado hubiera hecho la llamada él mismo.
- Login local (`POST /auth/local`, email+password), login Jellyfin/Emby (`POST /auth/jellyfin`,
  más Quick Connect en v3.4.0), login Plex (`POST /auth/plex`) — son flujos de sesión de navegador
  (cookie), no aplicables a un plugin servidor-a-servidor.
- **OIDC**: existe una rama `preview-new-oidc` en el repo, **no fusionada en ningún tag de release
  hasta v3.4.0** — no se encontró ninguna ruta `oidc` en `server/routes/` de v3.4.0. Tratar como
  **función futura, no disponible todavía en una versión estable**.

### Endpoint de creación de solicitudes

`POST /api/v1/request` (`server/routes/request.ts`, línea ~303). Requiere `req.user` (autenticado
por API key, sesión, etc.). Payload real (`server/interfaces/api/requestInterfaces.ts`):

```ts
export type MediaRequestBody = {
  mediaType: MediaType;        // 'movie' | 'tv'
  mediaId: number;             // TMDB id, NO el id interno de Seerr
  tvdbId?: number;
  seasons?: number[] | 'all';  // solo TV; 'all' = todas las temporadas no-especiales
  is4k?: boolean;
  serverId?: number;           // instancia Radarr/Sonarr concreta
  profileId?: number;
  profileName?: string;
  rootFolder?: string;
  languageProfileId?: number;
  userId?: number;             // id interno de Seerr del usuario en cuyo nombre se solicita
  tags?: number[];
  ignoreQuota?: boolean;       // añadido en v3.4.0 (#2026) — no existe en v3.3.0
};
```

Confirmado en `server/entity/MediaRequest.ts::request()`:

- `mediaId` es el **TMDB id**, usado para `tmdb.getMovie({movieId})` / `tmdb.getTvShow({tvId})`
  — no el id interno numérico del objeto `Media` de Seerr.
- `seasons: 'all'` expande a todas las temporadas de TMDB excepto la 0 (especiales), salvo que
  `settings.main.enableSpecialEpisodes` esté activo.
- 4K se controla con `is4k: boolean`, comprobado contra los permisos `REQUEST_4K`/`REQUEST_4K_MOVIE`
  /`REQUEST_4K_TV` del usuario destinatario de la solicitud.

### Atribución de usuario (`userId` en el payload) — comportamiento real, no asumido

Código real (líneas 58-74 de `MediaRequest.request()`):

```ts
let requestUser = user; // 'user' = req.user, el usuario autenticado por la llamada

if (
  requestBody.userId &&
  !requestUser.hasPermission([Permission.MANAGE_USERS, Permission.MANAGE_REQUESTS])
) {
  throw new RequestPermissionError('You do not have permission to modify the request user.');
} else if (requestBody.userId) {
  requestUser = await userRepository.findOneOrFail({ where: { id: requestBody.userId } });
}
```

- Si se manda `userId` en el body, el llamante (`req.user`, es decir, el dueño de la API key /
  sesión — con la API key maestra sola, esto es el admin id 1) **debe tener `MANAGE_USERS` o
  `MANAGE_REQUESTS`**. Con la API key global de admin esto siempre se cumple.
- Comprobaciones de **permiso para solicitar** (`REQUEST`/`REQUEST_MOVIE`/`REQUEST_TV`/variantes 4K)
  y de **cuota** (`requestUser.getQuota()`) se evalúan contra **`requestUser`** (el usuario indicado
  en `userId`), **no contra el admin que llama**. Es decir: **la API key de admin NO ignora la
  cuota/permisos del usuario en cuyo nombre se solicita** — la petición fallará con 403 si ese
  usuario no tiene permiso o ha agotado su cuota, salvo que se use `ignoreQuota: true` (solo
  disponible desde v3.4.0, y solo si el llamante tiene `MANAGE_REQUESTS`).
- **Pero la auto-aprobación SÍ se evalúa contra el llamante, no contra `requestUser`**: el campo
  `status` de la solicitud creada (línea ~374 y ~486) usa
  `user.hasPermission([AUTO_APPROVE..., MANAGE_REQUESTS], {type:'or'})` — donde `user` es
  **el autenticado original (`req.user`)**, no `requestUser`. Con la API key maestra de admin (que
  tiene `MANAGE_REQUESTS`), **cualquier solicitud creada así queda auto-aprobada
  independientemente de si el usuario destinatario tendría o no auto-aprobación por sí mismo**. Este
  es un matiz real y no obvio: cuota/permiso-de-solicitar se respetan del usuario objetivo, pero
  aprobación se decide por el llamante.
- Además, con la API key de admin sola (`useOverrides = !user.hasPermission([MANAGE_REQUESTS])`,
  línea 239), **no se aplican las "override rules"** (reglas de carpeta raíz/perfil de calidad por
  usuario) que sí se aplicarían si el usuario hiciera la petición él mismo desde la UI.
- Alternativa con `X-API-User`: si en vez de mandar `userId` en el body se usa el header
  `X-API-User: <id>`, entonces `req.user` **es directamente ese usuario** desde el principio de la
  petición, y **tanto permisos, cuota, overrides, como auto-aprobación se evalúan como ese usuario
  real**, no como el admin. Esta vía es más fiel al comportamiento que tendría el usuario solicitando
  él mismo, y es la opción recomendada si el nuevo plugin quiere que las solicitudes se comporten
  exactamente igual que si el usuario las hubiera creado desde la UI de Seerr (con su propia cuota,
  permisos y auto-aprobación).

### Relación de identidad Jellyfin↔Seerr

- Al hacer login con Jellyfin (`POST /auth/jellyfin`, `server/routes/auth.ts` línea ~235), Seerr
  busca/crea el usuario por `where: { jellyfinUserId: account.User.Id }` — **el identificador
  primario de vínculo es el GUID de usuario de Jellyfin**, no el email ni el username (el username
  de Jellyfin se guarda aparte en `jellyfinUsername` y se actualiza si cambia, pero no es la clave
  de búsqueda).
- **Import explícito sin necesidad de login previo**: `POST /api/v1/user/import-from-jellyfin`
  (`server/routes/user/index.ts` línea 729-804), protegido con `isAuthenticated(MANAGE_USERS)`.
  Body: `{ jellyfinUserIds: string[] }` (GUIDs de Jellyfin). Para cada GUID no existente, Seerr
  consulta la lista de usuarios de Jellyfin (vía la API key de Jellyfin configurada en Seerr) y crea
  un usuario Seerr con `jellyfinUserId`, `jellyfinUsername`, `email: jellyfinUser.Name` (**el email
  se rellena con el username de Jellyfin, no con un email real**, hasta que el usuario inicie sesión
  él mismo) y `permissions: settings.main.defaultPermissions`.
- **Lookup directo por GUID**: `GET /api/v1/user/jellyfin/:jellyfinUserId`
  (`server/routes/user/index.ts` línea 423-445) — devuelve el usuario Seerr correspondiente a un
  GUID de Jellyfin, o 404 si no existe. **Este endpoint no está implementado en el cliente local
  (`ISeerrApiClient` solo tiene `GetUserByIdAsync(int seerrUserId)`)** y es exactamente lo que
  necesita el nuevo plugin para resolver "¿qué usuario de Seerr corresponde a este usuario de
  Jellyfin que está viendo la sección?" sin tener que listar todos los usuarios y buscar en memoria.
- Conclusión práctica: un usuario de Jellyfin **no existe automáticamente en Seerr** hasta que (a)
  inicia sesión en Seerr al menos una vez, o (b) un admin lo importa explícitamente vía
  `import-from-jellyfin`. El nuevo plugin, para poder atribuir solicitudes correctamente sin
  depender de que cada usuario haya iniciado sesión en Seerr, probablemente necesite ofrecer un
  flujo de "importar/vincular usuarios de Jellyfin" (llamando a `import-from-jellyfin` con el admin,
  o guiando al usuario a iniciar sesión una vez) antes de poder crear solicitudes en su nombre.

### Estados reales (nombres y valores exactos de `server/constants/media.ts`)

```ts
export enum MediaRequestStatus {
  PENDING = 1, APPROVED, DECLINED, FAILED, COMPLETED, // 1..5
}
export enum MediaType { MOVIE = 'movie', TV = 'tv' }
export enum MediaStatus {
  UNKNOWN = 1, PENDING, PROCESSING, PARTIALLY_AVAILABLE, AVAILABLE, BLOCKLISTED, DELETED, // 1..7
}
```

- `MediaRequestStatus` real tiene **5 valores** (incluye `COMPLETED = 5`); el modelo local
  `SeerrRequestStatus` solo tiene 4 (`PendingApproval..Failed`), **le falta `Completed`**. Esto
  importa: `MediaRequest.request()` usa explícitamente `!== MediaRequestStatus.COMPLETED` como
  condición para no bloquear una re-solicitud (ver idempotencia abajo) — sin ese valor, cualquier
  lógica local que reconstruya ese enum estará incompleta.
- `MediaStatus` real: **`BLOCKLISTED = 6`, `DELETED = 7`**. El modelo local
  `SeerrMediaStatus` tiene **`Deleted = 6`, `Blocklisted = 7` — los dos últimos valores están
  intercambiados respecto al contrato real**. Es una discrepancia de bajo impacto funcional
  inmediato (ninguna lógica actual del plugin distingue entre "borrado" y "en blocklist" de forma
  visible), pero es un bug real si en algún momento se compara ese entero contra la API real (por
  ejemplo al decidir si mostrar "Solicitar" para contenido blocklisted vs. borrado).
- Campo real donde vive el estado, confirmado en `server/models/Movie.ts` (`mapMovieDetails`):
  la respuesta de `GET /api/v1/movie/{tmdbId}` (y análogamente `/tv/{tmdbId}`) es el objeto TMDB
  **más** `mediaInfo: media`, donde `media` es la entidad `Media` de Seerr (o `undefined` si no hay
  ningún registro local para ese título). Es decir, el campo exacto para "¿esto está
  disponible/pendiente/etc.?" es **`mediaInfo.status`** (no-4K) y **`mediaInfo.status4k`** (4K), tal
  y como recordaba la nota del usuario — confirmado contra código fuente, no memoria.
- Por temporada: `server/entity/Season.ts` — cada `Season` de un `Media` tiene **`status` y
  `status4k`** independientes (mismos valores de `MediaStatus`). En la respuesta HTTP esto llega
  como `mediaInfo.seasons: [{ seasonNumber, status, status4k }]`. El modelo local
  `SeerrMediaSeasonStatus` solo tiene `status` — **le falta `status4k`**, así que hoy no se podría
  distinguir "temporada 2 disponible en SD pero no en 4K" desde el modelo local.

### Series parcialmente disponibles

Seerr modela esto de forma nativa: el estado global de la serie
(`Media.status`/`Media.status4k`) puede ser `PARTIALLY_AVAILABLE`, y además cada temporada tiene su
propio `status`/`status4k` independiente en `mediaInfo.seasons[]`. Al crear una nueva solicitud de
temporadas, `MediaRequest.request()` (líneas ~427-467) calcula `existingSeasons` combinando (a) las
temporadas ya pedidas en solicitudes no declinadas/no completadas, y (b) las temporadas que ya
tienen `status`/`status4k` distinto de `UNKNOWN`/`DELETED` en `media.seasons`, y resta eso de las
temporadas solicitadas (`finalSeasons`). Si el resultado es vacío, lanza `NoSeasonsAvailableError`
(HTTP 202, ver idempotencia). Esto significa que el payload de creación puede pedir siempre
`seasons: 'all'` sin que el cliente tenga que calcular qué temporadas faltan — Seerr ya filtra las
duplicadas del lado servidor.

### Idempotencia — códigos HTTP reales (`server/routes/request.ts`, líneas ~318-335)

```ts
switch (error.constructor) {
  case RequestPermissionError:
  case QuotaRestrictedError:      return next({ status: 403, message: error.message });
  case DuplicateMediaRequestError: return next({ status: 409, message: error.message });
  case NoSeasonsAvailableError:    return next({ status: 202, message: error.message });
  case BlocklistedMediaError:      return next({ status: 403, message: error.message });
  default:                         return next({ status: 500, message: error.message });
}
```

- **Película ya solicitada** (y la solicitud existente no está `DECLINED` ni `COMPLETED`):
  **`409 Conflict`**, `DuplicateMediaRequestError: 'Request for this media already exists.'`.
- **Serie completa donde todas las temporadas pedidas ya estaban cubiertas**: no es un 409, es
  **`202 Accepted`** con `NoSeasonsAvailableError: 'No seasons available to request'` — Seerr lo
  trata como "no hay nada nuevo que hacer", no como un conflicto duro.
- **Cuota agotada o sin permiso**: `403`, no 400.
- **Media en blocklist**: `403`, `BlocklistedMediaError: 'This media is blocklisted.'`.
- Nunca hay un `200` silencioso ni un `400` genérico para duplicados — el nuevo plugin debe manejar
  explícitamente 409 y 202 como "ya en curso", no como errores duros de UI.

### Comparación directa con el cliente local (`SeerrApiClient.cs` / `ISeerrApiClient.cs`)

| Aspecto | Cliente local (JellyNotify) | API real (Seerr v3.4.0) | Estado |
|---|---|---|---|
| Auth | `X-Api-Key` únicamente | `X-API-Key` (+ `X-API-User` opcional para suplantar usuario) | Correcto pero incompleto — falta soporte de `X-API-User` |
| Crear solicitud | No existe ningún método | `POST /api/v1/request` con `mediaType, mediaId, seasons, is4k, userId, ignoreQuota, ...` | **Falta por completo** |
| Estado de disponibilidad | `SeerrMediaDetails` no captura `mediaInfo` | `GET /api/v1/movie\|tv/{tmdbId}` devuelve `mediaInfo.status`/`status4k`/`seasons[]` | **Falta el campo entero** |
| Enum `SeerrMediaStatus` | `Deleted=6, Blocklisted=7` | `BLOCKLISTED=6, DELETED=7` | **Invertido — bug** |
| Enum `SeerrRequestStatus` | 4 valores (falta `Completed`) | 5 valores, incluye `COMPLETED=5` | **Incompleto** |
| `SeerrMediaSeasonStatus` | Solo `status` | `status` + `status4k` | **Incompleto** |
| Lookup usuario por GUID Jellyfin | No existe | `GET /api/v1/user/jellyfin/:jellyfinUserId` | **Falta** |
| Import de usuarios Jellyfin | No existe | `POST /api/v1/user/import-from-jellyfin` | **Falta** |
| Paginación de requests/usuarios | Correcta (`take`/`skip`/`pageInfo`) | Igual | OK |
| Webhook saliente | Correcto (`GET/POST .../webhook`, `.../webhook/test`) | Igual | OK |
| Manejo de 409/202/403 | No aplica (no crea solicitudes) | Confirmado arriba | A implementar |

### Limitaciones de esta investigación

- No se ha podido probar contra una instancia Seerr real en ejecución (sin servidor disponible en
  este entorno) — todo lo anterior es lectura de código fuente de un tag de release real
  (`v3.4.0`), no observación de tráfico HTTP real. **Marcar como pendiente de verificación contra
  instancia real**: (a) forma exacta del cuerpo de error 4xx/5xx en todos los casos (se ha
  confirmado el `status` HTTP y la clase de error, pero no se ha ejecutado la app para capturar el
  JSON de respuesta byte a byte), (b) comportamiento exacto de cuotas por defecto/límites numéricos
  (dependen de configuración de cada instancia), (c) OIDC (no disponible en ningún tag todavía).
- No se ha revisado `server/routes/tv.ts` con el mismo detalle que `movie.ts` — se asume estructura
  simétrica (`mapTvDetails` con `mediaInfo` embebido igual) por convención del código, pero no se ha
  leído línea a línea.
- No se ha revisado `server/lib/permissions.ts` bit a bit para documentar el valor numérico exacto
  de cada permiso (`REQUEST`, `MANAGE_REQUESTS`, `AUTO_APPROVE`, etc.) — solo se han confirmado los
  nombres de las constantes usadas en `MediaRequest.request()`.

### Riesgos

- Diseñar el nuevo plugin asumiendo que "API key de admin = ignora todo" es **incorrecto**: cuota y
  permiso-de-solicitar se evalúan contra el usuario objetivo. Un diseño que no maneje `403` en la
  creación de solicitudes fallará silenciosamente para usuarios sin cuota.
- Si el plugin usa `userId` en el body en vez de `X-API-User`, las solicitudes se auto-aprobarán
  siempre (porque se evalúa el permiso del admin, no del usuario objetivo) — esto puede sorprender
  a administradores que esperan que se respete la configuración de auto-aprobación por usuario.
  Recomendación de diseño: preferir `X-API-User` sobre `userId` en el body para fidelidad de
  comportamiento, documentando la diferencia al administrador.
- Apuntar a Seerr v3.3.0 (como dice el `manifest.json` de JellyNotify) deja fuera `ignoreQuota`
  (v3.4.0). Si el nuevo plugin quiere ese campo, el manifest/compatibilidad declarada debe
  actualizarse a v3.4.0 o superior — a decidir, no a asumir.

### Impacto en el diseño

1. El cliente de escritura (`CreateRequestAsync` o similar) debe implementar el payload completo de
   `MediaRequestBody`, con soporte de `seasons: number[] | "all"` y `is4k`.
2. Para atribuir la solicitud al usuario de Jellyfin que pulsa "Solicitar", el flujo recomendado es:
   resolver el `jellyfinUserId` → `GET /api/v1/user/jellyfin/:jellyfinUserId` (nuevo método a
   añadir) → si 404, decidir entre auto-importar (`POST /api/v1/user/import-from-jellyfin`, requiere
   que el admin haya configurado la integración Jellyfin de Seerr) o pedir al usuario que inicie
   sesión en Seerr una vez → usar el `id` numérico resultante en el header `X-API-User` para todas
   las llamadas de creación de solicitud en su nombre (no en el body `userId`, por el matiz de
   auto-aprobación documentado arriba).
3. Para el botón "Solicitar" en una fila de contenido no local, hace falta un nuevo modelo que
   capture `mediaInfo.status`, `mediaInfo.status4k` y `mediaInfo.seasons[].{status,status4k}` desde
   `GET /api/v1/movie|tv/{tmdbId}` — el `SeerrMediaDetails` actual no sirve tal cual.
4. Corregir los dos enums (`SeerrMediaStatus`: `Blocklisted=6/Deleted=7`; `SeerrRequestStatus`:
   añadir `Completed=5`) antes de reutilizar código del plugin existente como base.
5. El manejo de errores de creación debe distinguir explícitamente 409 (ya solicitado — UI: mostrar
   "ya solicitado"), 202 (nada nuevo que pedir — UI: igual que 409 en la práctica), 403 (cuota o
   permiso — UI: mensaje específico, no genérico) y 500 (error real).

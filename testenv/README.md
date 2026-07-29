# Entorno de pruebas aislado

Jellyfin + Seerr en Docker para desarrollar y probar el plugin sin tocar
ninguna instancia real. Todo el estado vive bajo esta carpeta y está en
`.gitignore`.

> **Nunca** se commitea `.env`. Contiene tu clave de TMDb.

## Versiones fijadas

Todos los tags se verificaron con un `docker pull` real el 2026-07-29. No se usa
`latest` en ningún sitio: una versión base que se mueve sola hace imposible
razonar sobre "ayer funcionaba".

| Servicio | Imagen | Digest |
|---|---|---|
| Jellyfin | `jellyfin/jellyfin:10.11.11` | `sha256:aefb67e6…d2b35db` |
| Seerr | `ghcr.io/seerr-team/seerr:v3.4.0` | `sha256:d206d9e4…702fe086` |
| Build | `mcr.microsoft.com/dotnet/sdk:9.0` | (solo para compilar) |

Los tags de Seerr llevan prefijo `v` (`v3.4.0`); el tag `3.4.0` a secas **no
existe**.

## Puesta en marcha

```bash
cp .env.example .env          # y rellena TMDB_API_KEY
scripts/up.sh                 # levanta Jellyfin y Seerr
scripts/seed-synthetic-library.sh
scripts/build-and-install-plugin.sh
```

- Jellyfin: <http://localhost:8096>
- Seerr: <http://localhost:5055>

En Jellyfin, completa el asistente inicial y añade dos bibliotecas apuntando a
las rutas **dentro del contenedor**:

| Biblioteca | Ruta |
|---|---|
| Películas | `/media/Movies` |
| Series | `/media/Shows` |

## Ciclo de trabajo

```
editar código
  → scripts/build-and-install-plugin.sh     compila, instala, reinicia Jellyfin
  → mirar el navegador
  → scripts/logs.sh                          los logs del plugin
  → repetir
```

## Scripts

| Script | Qué hace |
|---|---|
| `up.sh` | Arranca los contenedores y espera a que Jellyfin responda |
| `down.sh` | Para los contenedores, conservando el estado |
| `build-and-install-plugin.sh` | Compila en el contenedor del SDK, instala el DLL y reinicia Jellyfin |
| `logs.sh` | Logs de Jellyfin filtrados al plugin (`--all` para todo, `--seerr` para Seerr) |
| `seed-synthetic-library.sh` | Crea la biblioteca sintética |
| `reset-environment.sh` | **Destructivo.** Borra contenedores y todo el estado persistente |

## Biblioteca sintética

Vídeos de un segundo en negro (~1,8 KB cada uno, generados con ffmpeg). A
Jellyfin solo le hace falta un fichero escaneable con un nombre reconocible.

Los TMDb id están verificados uno a uno contra themoviedb.org, y cada título
está de verdad en el proveedor indicado para la región ES. Eso es
deliberado: da a las secciones de proveedor una mezcla predecible de
resultados "ya en la biblioteca" y "no está en la biblioteca", que es
justo lo que hay que poder distinguir al probar las tarjetas.

| Título | TMDb | Proveedor (ES) |
|---|---|---|
| The Matrix (1999) | 603 | Netflix |
| Blade Runner 2049 (2017) | 335984 | Prime Video |
| Dune (2021) | 438631 | Netflix / Max |
| Attack on Titan (2013) | 1429 | Crunchyroll |
| Arcane (2021) | 94605 | Netflix |
| Breaking Bad (2008) | 1396 | Netflix |

El sufijo `[tmdbid-N]` es la convención del propio Jellyfin y hace que el
escáner asocie exactamente ese id, en vez de adivinarlo por el título.

## La clave de TMDb

`TMDB_API_KEY` en `.env` es la **API Key v3** (32 caracteres hexadecimales), de
themoviedb.org → Ajustes → API. Es solo para el plugin.

Seerr **no** necesita ninguna clave de TMDb: trae una embebida en su código
para su propio uso interno (ver `docs/research/06-seerr-api-analysis.md`). Esa
clave es suya y no se reutiliza aquí.

## Estado verificado (2026-07-29)

Comprobado de verdad en esta máquina, no asumido:

- Jellyfin arranca y responde en `/health`, reporta versión `10.11.11`.
- Seerr arranca y responde en `/api/v1/settings/public`.
- La biblioteca sintética se genera y el contenedor la ve en `/media` (solo lectura).
- `build-and-install-plugin.sh` compila, instala y reinicia; Jellyfin carga el
  plugin: `JellyProvider Sections plugin v0.0.1.0 loaded`.
- **Home Screen Sections 2.5.11.0 y File Transformation 2.5.11.0 cargan sin
  errores en Jellyfin 10.11.11.** Esto cierra empíricamente el riesgo de
  compatibilidad más alto de `docs/implementation/14-risks-and-mitigations.md`,
  que hasta ahora solo estaba inferido a partir de un commit de un plugin
  hermano.

Pendiente de probar cuando el plugin tenga funcionalidad: registro real de
secciones en Modular Home, persistencia de posición tras reinicio, y el logo
junto al título.

## Notas de implementación

- Jellyfin corre como tu usuario (`JPS_UID`/`JPS_GID`), para que la config
  montada siga siendo editable desde el host sin `sudo`. `UID` es de solo
  lectura en bash, de ahí los nombres propios.
- El contenedor del SDK también corre como tu usuario, así `bin/`, `obj/` y
  `dist/` no quedan propiedad de root. Eso obliga a redirigir `DOTNET_CLI_HOME`
  y `NUGET_PACKAGES` a `/tmp`, porque `$HOME` no es escribible para un uid
  arbitrario.
- Si alguna vez compilaste como root y `obj/` quedó con permisos de root:
  ```bash
  docker run --rm -v "$PWD:/src" -w /src alpine:3 \
      sh -c 'rm -rf */obj */bin dist'
  ```

## Verificación end-to-end realizada (2026-07-29)

Todo lo siguiente se ejecutó contra el entorno real de esta carpeta, no en tests unitarios.

| Comprobación | Resultado |
|---|---|
| Jellyfin 10.11.11 + HSS 2.5.11.0 + File Transformation 2.5.11.0 | Cargan sin errores |
| Plugin carga y registra sus secciones | 3 secciones registradas |
| UUID y posición sobreviven a reinicio | Mismos ids tras reiniciar |
| Consulta Discover real | 60 títulos de Crunchyroll España, en español |
| Logo del proveedor | JPEG 92x92 servido y cacheado desde TMDb |
| Logo en el título de sección | `<img>` presente en el displayText que sirve HSS |
| Escape de HTML en el nombre (XSS) | `<script>` llega como `&lt;script&gt;` |
| Resolución local | Un título de la biblioteca vuelve como ítem real con UserData |
| Conexión Seerr | Correcta |
| Crear solicitud (serie y película) | Creada y atribuida al usuario Jellyfin correcto |
| Duplicados | 409 y 202 manejados como estados, no como error |

### Configurar Seerr por API

Su asistente espera el host y el puerto por separado, y `urlBase` vacío explícito
(si se omite, lo concatena literalmente como "undefined"):

```bash
curl -X POST http://localhost:5055/api/v1/auth/jellyfin \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"...","hostname":"jps-jellyfin","port":8096,"useSsl":false,"urlBase":"","email":"admin@test.local","serverType":2}'
```

### Habilitar Modular Home

Home Screen Sections viene con `Enabled=false` y su lista de secciones vacía, y
en ese estado su endpoint `/HomeScreen/Sections` devuelve 500 (hace un `Max`
sobre una secuencia vacía). Hay que activarlo y añadir una entrada por sección
en su propia configuración antes de que las filas aparezcan.

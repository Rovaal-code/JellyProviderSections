# 10 — Entorno de pruebas aislado

Ubicación: `testenv/` **dentro de este mismo repositorio** (decisión del usuario 2026-07-29: unificar código y entorno de pruebas en un solo sitio, en vez de dos carpetas hermanas de nombre casi idéntico). El estado persistente de los contenedores, las evidencias y el `.env` con el token de TMDb están en `.gitignore`. Ninguna instancia de producción se usa ni se modifica en ningún punto.

## Estructura propuesta

```
testenv/
├── docker-compose.yml
├── .env.example                     # sin valores reales; .env real gitignored
├── jellyfin/
│   ├── config/                       # volumen persistente, vacío al inicio
│   ├── plugins-extra/                # HSS + File Transformation + (opcional Pages) descargados y fijados por versión
│   └── media-synthetic/              # biblioteca sintética (ver abajo)
├── seerr/
│   └── config/                       # volumen persistente de la instancia Seerr autoalojada
├── scripts/
│   ├── build-and-install-plugin.sh   # compila el plugin nuevo en contenedor dotnet SDK, copia el DLL al volumen de plugins, reinicia Jellyfin
│   ├── restart-jellyfin.sh
│   ├── seed-synthetic-library.sh     # genera ficheros de vídeo dummy + estructura de carpetas reconocible por Jellyfin
│   ├── create-test-users.sh          # crea usuarios Jellyfin de prueba vía API
│   ├── reset-environment.sh          # baja contenedores, limpia volúmenes, vuelve al estado inicial
│   └── capture-evidence.sh           # wrapper para guardar logs/capturas en evidence/ con timestamp
├── evidence/
│   ├── logs/
│   └── screenshots/
└── README.md
```

## `docker-compose.yml` (diseño, sin implementar todavía)

Servicios:

| Servicio | Imagen (a fijar por tag/digest concreto, no `latest`) | Rol |
|---|---|---|
| `jellyfin` | `jellyfin/jellyfin` — **fijar el tag que corresponda exactamente a la versión 10.11.11 verificada en `research/07`**; confirmar en el momento de crear el entorno que el tag existe en Docker Hub (no asumido en esta fase de investigación pura, es un paso de verificación de la fase 4 de ejecución) | Servidor Jellyfin de prueba |
| `seerr` | `ghcr.io/seerr-team/seerr` — fijar el tag correspondiente a **3.4.0** (versión objetivo ya confirmada); verificar el nombre exacto de la imagen publicada por el proyecto en el momento de crear el entorno (no asumido aquí, evita inventar un nombre de imagen no verificado) | Instancia Seerr autoalojada de prueba (decisión ya confirmada: Seerr vive en este compose, no se conecta a ninguna instancia externa) |
| `dotnet-build` (perfil `build`, no arranca con `up` normal) | `mcr.microsoft.com/dotnet/sdk:9.0` | Compila el plugin nuevo montando el repo `/home/alvaro/Descargas/JellyProviderSections` como volumen, sin necesitar SDK en el host |

Red interna dedicada (`jps-net`), sin exponer Seerr al exterior salvo el puerto necesario para pruebas manuales desde el navegador del host. Healthchecks: `GET /health` (o equivalente real de cada imagen, a confirmar) antes de considerar el servicio listo en los scripts de espera.

Volúmenes persistentes para `jellyfin/config`, `jellyfin/media-synthetic` y `seerr/config`, para poder reiniciar contenedores sin perder el estado entre sesiones de prueba, y `reset-environment.sh` para volver a cero cuando haga falta un entorno limpio.

## Plugins de terceros a instalar (versiones ya fijadas por la investigación)

- Home Screen Sections **2.5.11.0**
- File Transformation **2.5.11.0** (dependencia transitiva de HSS)
- Plugin Pages **2.4.11.0** (opcional, no requerido por el MVP, se instala solo si se quiere probar su ausencia no rompe nada — ver `research/04` §6)
- JellyProvider Sections (el plugin nuevo, compilado por `build-and-install-plugin.sh`)

Instalación vía el repositorio de catálogo real de cada plugin (añadiendo sus manifests públicos en **Dashboard → Plugins → Repositorios** del Jellyfin de prueba) en vez de copiar DLLs a mano, para que el flujo de prueba sea representativo del flujo real de un administrador.

## Biblioteca sintética

- Un pequeño conjunto de ficheros de vídeo dummy (pocos KB, generados con `ffmpeg` o simplemente ficheros vacíos con extensión de vídeo, ya que Jellyfin solo necesita poder escanearlos y asociarles metadatos) organizados con nomenclatura reconocible por el escáner de Jellyfin, con `ProviderIds.Tmdb` conocidos de antemano (elegidos a propósito para coincidir con títulos reales disponibles en Crunchyroll/Netflix/Prime Video España, de forma que las secciones de prueba tengan al menos algunos resultados "locales" y otros "externos" de forma predecible).
- Al menos: 3-5 películas y 2-3 series con TMDb id conocido y verificado manualmente en la web de TMDb antes de fijarlos en el script de semillas.

## Usuarios de prueba

- Un usuario administrador.
- Al menos dos usuarios no-admin: uno con acceso a toda la biblioteca sintética, otro con una biblioteca restringida (para probar el requisito de no revelar contenido oculto, `research/07` §4 y `12-security-and-privacy.md`).
- Usuarios Seerr correspondientes, vinculados por `jellyfinUserId` (ver `09-seerr-integration-plan.md`) — creados vía login inicial contra el propio Jellyfin de prueba (Seerr autoalojado ya está configurado para hablar con ese mismo Jellyfin) o vía `import-from-jellyfin`.

## Credenciales necesarias (no gestionadas por este documento, ver `.env.example`)

- `TMDB_API_READ_ACCESS_TOKEN` — a rellenar por el usuario en `.env` (nunca commiteado). Es exclusivamente para el plugin nuevo (llamadas propias a Discover/Watch Providers). El propio README del entorno explica cómo obtenerlo (cuenta gratuita en themoviedb.org → Configuración → API).
- Seerr no necesita credencial externa de ningún tipo para arrancar: al ser autoalojado dentro del mismo compose, su API key (para que nuestro plugin lo llame) se genera en su primer arranque y se guarda en un fichero local no versionado que los scripts leen automáticamente. **Tampoco necesita un TMDb API key propio** — Seerr trae una clave de TMDb ya embebida en su código fuente para su propio uso interno (verificado en `research/06-seerr-api-analysis.md`, nota 2026-07-29); esa clave es solo para Seerr y no debe ni puede reutilizarse en el plugin nuevo, que sigue necesitando su propio `TMDB_API_READ_ACCESS_TOKEN`.

## Ciclo de trabajo esperado

```
scripts/build-and-install-plugin.sh   # compila + copia + reinicia Jellyfin
→ abrir Jellyfin de prueba en el navegador
→ configurar TMDb (token) y Seerr (URL interna del compose, API key autogenerada)
→ crear secciones de prueba (Crunchyroll/Netflix/Prime Video España)
→ scripts/capture-evidence.sh          # logs + capturas con timestamp
→ inspeccionar, corregir código, repetir desde el primer paso
```

## Reconstrucción y limpieza

- `reset-environment.sh`: `docker compose down -v` + limpieza de `jellyfin/config`, `jellyfin/media-synthetic`, `seerr/config` — vuelve al estado inicial para probar instalación desde cero y migraciones.
- Nunca usar `docker compose down -v` sobre nada que no sea este proyecto (separación estricta de cualquier entorno Docker de producción del usuario, si lo hubiera).

## Limitación de esta fase

Este documento es un **diseño**, no la implementación del entorno — coherente con la regla del encargo de no empezar a programar hasta que el usuario lo autorice explícitamente tras aprobar el plan. Los nombres exactos de imagen/tag de Seerr y el tag exacto de Jellyfin 10.11.11 en Docker Hub deben confirmarse con un `docker pull` real en el momento de construir el entorno (fase 4 de la ejecución futura), no se dan por verificados aquí.

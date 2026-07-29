# JellyProvider Sections

Adds home screen rows by streaming provider to Jellyfin: "Popular en Crunchyroll", "Novedades
en Prime Video", whatever the administrator defines. Each row is a TMDb Discover query for a
provider and region, resolved against the local library, with the rest offered as requests
through [Seerr](https://github.com/seerr-team/seerr).

Titles already in the library open their real Jellyfin page and keep their watch state.
Everything else is drawn as a portrait card with its TMDb artwork, which the plugin serves
and caches itself, so browsers never talk to image.tmdb.org.

Sibling project of [JellyNotify](https://github.com/Rovaal-code/JellyNotify): same visual
language, separate codebase, distributed through the same plugin catalogue.

## Requirements

| | |
|---|---|
| Jellyfin | 10.11.x (built and verified against 10.11.11) |
| [Home Screen Sections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) | Required. The rows are registered with it |
| [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) | Required. Home Screen Sections needs it to draw anything, and this plugin needs it to put artwork on the cards |
| A TMDb API key | Required. Free, from themoviedb.org → Settings → API. The v3 key, 32 hex characters |
| Seerr | Optional. Without it the rows still work, read only |
| [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced) | Optional. When present, clicking a card that is not in the library opens its Jellyseerr detail modal |

## Install

Add the shared catalogue once in Jellyfin, under Dashboard → Plugins → Repositories:

```text
https://raw.githubusercontent.com/Rovaal-code/jellyfin-plugins/main/manifest.json
```

"JellyProvider Sections" then appears in the plugin catalogue alongside JellyNotify.
Install it and restart Jellyfin.

## Configure

Dashboard → Plugins → **JellyProvider Sections**.

1. **Conexiones**: paste the TMDb API key and, if you want requests, the Seerr URL and key.
   Keys are stored server side and are never sent back to the browser.
2. **Secciones**: create a section. Pick the content type, the region and the provider from
   the list (the search box filters it), name the row, and save. Advanced filters cover
   genres, original language, dates, rating and vote floors.
3. **Diagnóstico**: confirms Home Screen Sections and File Transformation were detected, and
   reports each section's last sync.

New sections register with Home Screen Sections immediately. Their position and per-user
visibility are then managed from Home Screen Sections' own settings, as with any other
section.

## Build

No host SDK needed; everything runs in the .NET SDK container.

```bash
./build.sh --version 0.1.0.0     # compiles, packages the zip, updates the manifests
```

There is a full disposable test environment (Jellyfin, Seerr, the dependency plugins and a
synthetic library) under [`testenv/`](testenv/README.md).

## Documentation

```text
docs/research/          technical investigation, with source cited from every dependency
docs/implementation/    the plan, start at master-implementation-plan.md
```

`docs/implementation/15-acceptance-criteria.md` tracks what has been verified against a real
server, and how.

## License

GPL-3.0-or-later, see [`LICENSE`](LICENSE).

This product uses the TMDb API but is not endorsed or certified by TMDb. Provider
availability data comes from JustWatch through TMDb.

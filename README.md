# Jellyfin Provider Sections

Creates dynamic Jellyfin home screen sections based on TMDb streaming providers (e.g. "Popular on Crunchyroll"), registered with [Home Screen Sections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections), resolved against the local Jellyfin library, with [Seerr](https://github.com/seerr-team/seerr) request support for content that isn't available yet.

Sibling project of [JellyNotify](https://github.com/Rovaal-code/JellyNotify): same visual language, separate codebase, distributed through JellyNotify's shared plugin repository manifest.

Status: early skeleton (phase 3 of the implementation plan). Full design and rationale, with real source code cited from every dependency, lives in this repository:

```text
docs/research/          — technical investigation (12 documents)
docs/implementation/    — implementation plan, start at master-implementation-plan.md
```

## Build

Requires the .NET 9 SDK. No SDK is assumed to be installed on the host, use the Docker SDK image:

```bash
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:9.0 dotnet build
```

## License

GPL-3.0-or-later, see [`LICENSE`](LICENSE). Will document any adapted third-party code in `NOTICE.md`, same convention as JellyNotify, if and when that becomes applicable.

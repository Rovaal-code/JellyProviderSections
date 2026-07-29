#!/usr/bin/env bash
# build.sh - Builds, packages and records a release of Jellyfin Provider Sections.
# Usage: ./build.sh [--version 0.1.0.0]
#
# Compiles in the .NET SDK container, so no host SDK is needed. Same shape as
# JellyNotify's build.sh, which is deliberate: the two plugins share one
# distribution catalogue and their release artifacts have to look alike.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="JellyProviderSections.Plugin/JellyProviderSections.Plugin.csproj"
OUTPUT_DIR="$SCRIPT_DIR/dist"
RELEASES_DIR="$SCRIPT_DIR/releases"
OWNER="Rovaal-code"
REPO="JellyProviderSections"

VERSION="$(python3 -c "import json;print(json.load(open('$SCRIPT_DIR/JellyProviderSections.Plugin/meta.json'))['version'])")"
if [[ "${1:-}" == "--version" && -n "${2:-}" ]]; then
    VERSION="$2"
fi

echo "→ Building Jellyfin Provider Sections ${VERSION}..."
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR" "$RELEASES_DIR"

# Runs as the host user so dist/, bin/ and obj/ stay user-owned. That leaves
# $HOME unwritable, hence the redirected SDK home and package cache.
docker run --rm \
    -v "${SCRIPT_DIR}:/src" \
    -w /src \
    -e HOME=/tmp \
    -e DOTNET_CLI_HOME=/tmp \
    -e NUGET_PACKAGES=/tmp/nuget \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e DOTNET_NOLOGO=1 \
    -u "$(id -u):$(id -g)" \
    mcr.microsoft.com/dotnet/sdk:9.0 \
    dotnet publish "$PROJECT" \
        --configuration Release \
        --output /src/dist \
        -p:Version="${VERSION}"

[[ -f "$OUTPUT_DIR/JellyProviderSections.Plugin.dll" ]] \
    || { echo "✗ Build produced no DLL"; exit 1; }

cp "$SCRIPT_DIR/JellyProviderSections.Plugin/meta.json" "$OUTPUT_DIR/meta.json"

ZIP_NAME="jellyprovidersections_${VERSION}.zip"
ZIP_PATH="$RELEASES_DIR/$ZIP_NAME"

echo "→ Packaging ${ZIP_NAME}..."
(
    cd "$OUTPUT_DIR"
    python3 -c "
import zipfile, os, sys
zip_path = sys.argv[1]
candidates = ['JellyProviderSections.Plugin.dll', 'JellyProviderSections.Plugin.xml', 'meta.json']
files = [f for f in candidates if os.path.exists(f)]
if not files:
    print('ERROR: nothing to package', file=sys.stderr)
    sys.exit(1)
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zf:
    for f in files:
        zf.write(f)
        print('  + ' + f)
" "$ZIP_PATH"
)

CHECKSUM=$(md5sum "$ZIP_PATH" | awk '{print $1}')
echo "✓ ${ZIP_PATH}"
echo "✓ MD5 ${CHECKSUM}"

# The catalogue is the neutral repository listing every plugin; that is the URL
# an administrator adds to Jellyfin. JellyNotify's older copy is still written to
# because it is referenced from the Jellyfin Universal Plugin Repo; drop it from
# this list once that reference is gone.
CHANGELOG_FILE="$SCRIPT_DIR/CHANGELOG-current.md"
CHANGELOG=""
[[ -f "$CHANGELOG_FILE" ]] && CHANGELOG="$(cat "$CHANGELOG_FILE")"

CATALOGUE_MANIFEST="/home/alvaro/Descargas/jellyfin-plugins/manifest.json"
LEGACY_MANIFEST="/home/alvaro/Descargas/jellyfinnotify/JellyNotify/repository/manifest.json"

for manifest_file in "$CATALOGUE_MANIFEST" "$LEGACY_MANIFEST"; do
    [[ -f "$manifest_file" ]] || continue
    echo "→ Updating $(basename "$(dirname "$manifest_file")")/$(basename "$manifest_file")..."
    python3 -c "
import sys, json
from datetime import datetime, timezone

version, checksum, filepath, changelog, owner, repo = sys.argv[1:7]
timestamp = datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
version_tag = version[:-2] if version.endswith('.0') else version
source_url = f'https://github.com/{owner}/{repo}/releases/download/v{version_tag}/jellyprovidersections_{version}.zip'

meta = json.load(open('$SCRIPT_DIR/JellyProviderSections.Plugin/meta.json', encoding='utf-8'))

with open(filepath, encoding='utf-8') as f:
    data = json.load(f)

plugin = next((p for p in data if p.get('guid') == meta['guid']), None)
if plugin is None:
    plugin = {'guid': meta['guid'], 'versions': []}
    data.append(plugin)

plugin.update({
    'category': meta['category'],
    'name': meta['name'],
    'description': meta['description'],
    'overview': meta['overview'],
    'owner': owner,
    'imageUrl': meta.get('imageUrl', ''),
})

versions = plugin.setdefault('versions', [])
entry = next((v for v in versions if v.get('version') == version), None)
if entry is None:
    entry = {}
    versions.insert(0, entry)
entry.update({
    'version': version,
    'changelog': changelog,
    'targetAbi': meta['targetAbi'],
    'sourceUrl': source_url,
    'checksum': checksum,
    'timestamp': timestamp,
})

def version_key(v):
    return tuple(int(p) if p.isdigit() else 0 for p in v.get('version', '0').split('.'))

versions.sort(key=version_key, reverse=True)

with open(filepath, 'w', encoding='utf-8') as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write('\n')
print('  ✓ ' + version + ' -> ' + source_url)
" "$VERSION" "$CHECKSUM" "$manifest_file" "$CHANGELOG" "$OWNER" "$REPO"
done

# A zip built twice from the same source does not come out byte for byte
# identical, so its MD5 changes on every run. The manifests now describe the zip
# that was just built, and if a release for this version is already published
# with a different one, Jellyfin will refuse the download. Catch that here rather
# than at install time on someone else's server.
if command -v gh >/dev/null 2>&1; then
    PUBLISHED_URL="https://github.com/${OWNER}/${REPO}/releases/download/v${VERSION%.0}/${ZIP_NAME}"
    PUBLISHED_MD5="$(curl -sfL "$PUBLISHED_URL" 2>/dev/null | md5sum 2>/dev/null | awk '{print $1}')"

    if [[ -n "$PUBLISHED_MD5" && "$PUBLISHED_MD5" != "$CHECKSUM" ]]; then
        echo
        echo "⚠ v${VERSION%.0} is already published with a different checksum:"
        echo "    published ${PUBLISHED_MD5}"
        echo "    just built ${CHECKSUM}"
        echo "  The manifests now describe the new zip, so either replace the release"
        echo "  asset or bump the version. Leaving it as is breaks installation."
    fi
fi

echo
echo "✓ Done. Publish with:"
echo "    gh release create v${VERSION%.0} \"$ZIP_PATH\" --repo ${OWNER}/${REPO}"

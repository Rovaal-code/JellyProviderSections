#!/usr/bin/env bash
# Compiles the plugin in a .NET SDK container (no host SDK required), installs
# the result into Jellyfin's plugin folder, and restarts Jellyfin.
#
# This is the inner loop of the whole project: edit code, run this, look at the
# browser.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

VERSION="$(plugin_version)"
PLUGIN_DIR="${TESTENV_DIR}/jellyfin/config/plugins/JellyProviderSections_${VERSION}"

log "Building JellyProviderSections ${VERSION} in mcr.microsoft.com/dotnet/sdk:9.0..."
# Runs as the host user so bin/obj and dist/ stay user-owned rather than root.
# That leaves $HOME pointing at an unwritable "/", so DOTNET_CLI_HOME and
# NUGET_PACKAGES have to be redirected somewhere writable or the SDK aborts on
# its first-run configuration step.
docker run --rm \
    -v "${REPO_DIR}:/src" \
    -w /src \
    -e HOME=/tmp \
    -e DOTNET_CLI_HOME=/tmp \
    -e NUGET_PACKAGES=/tmp/nuget \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e DOTNET_NOLOGO=1 \
    -u "$(id -u):$(id -g)" \
    mcr.microsoft.com/dotnet/sdk:9.0 \
    dotnet publish JellyProviderSections.Plugin/JellyProviderSections.Plugin.csproj \
        --configuration Release \
        --output /src/dist \
        -p:Version="${VERSION}"

[[ -f "${REPO_DIR}/dist/JellyProviderSections.Plugin.dll" ]] \
    || die "Build produced no DLL at dist/JellyProviderSections.Plugin.dll"
ok "Built dist/JellyProviderSections.Plugin.dll"

# Wipe the target folder first: a stale DLL from a previous version left
# alongside the new one makes Jellyfin's plugin loader pick unpredictably.
log "Installing into ${PLUGIN_DIR#"${TESTENV_DIR}/"}..."
rm -rf "${PLUGIN_DIR}"
mkdir -p "${PLUGIN_DIR}"
cp "${REPO_DIR}/dist/JellyProviderSections.Plugin.dll" "${PLUGIN_DIR}/"
[[ -f "${REPO_DIR}/dist/JellyProviderSections.Plugin.pdb" ]] \
    && cp "${REPO_DIR}/dist/JellyProviderSections.Plugin.pdb" "${PLUGIN_DIR}/"
cp "${REPO_DIR}/JellyProviderSections.Plugin/meta.json" "${PLUGIN_DIR}/"
ok "Installed"

if docker ps --format '{{.Names}}' | grep -qx "${JELLYFIN_CONTAINER}"; then
    log "Restarting Jellyfin..."
    docker restart "${JELLYFIN_CONTAINER}" >/dev/null
    wait_for_jellyfin 180 || die "Jellyfin did not come back up after restart"
    echo
    ok "Plugin ${VERSION} installed and Jellyfin restarted"
    echo "   Check it at ${JELLYFIN_URL}/web/#/dashboard/plugins"
else
    warn "Jellyfin container is not running. Start it with scripts/up.sh."
fi

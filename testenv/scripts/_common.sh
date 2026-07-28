#!/usr/bin/env bash
# Shared helpers. Sourced by the other scripts, not run directly.

set -euo pipefail

TESTENV_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_DIR="$(cd "${TESTENV_DIR}/.." && pwd)"

JELLYFIN_URL="http://localhost:8096"
SEERR_URL="http://localhost:5055"
JELLYFIN_CONTAINER="jps-jellyfin"
SEERR_CONTAINER="jps-seerr"

# Jellyfin runs as the invoking user (see docker-compose.yml), so bind-mounted
# config stays writable from the host without sudo. Note UID is readonly in
# bash, hence the distinct names passed through to compose.
export JPS_UID="$(id -u)"
export JPS_GID="$(id -g)"

compose() {
    docker compose -f "${TESTENV_DIR}/docker-compose.yml" "$@"
}

log()  { printf '\033[1;35m→\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m✓\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m✗\033[0m %s\n' "$*" >&2; exit 1; }

# Waits for Jellyfin's /health to answer. Reports the container's own healthcheck
# state on failure, which is usually the piece that explains why.
wait_for_jellyfin() {
    local timeout="${1:-180}"
    local waited=0
    log "Waiting for Jellyfin at ${JELLYFIN_URL} (up to ${timeout}s)..."
    while (( waited < timeout )); do
        if curl -fsS "${JELLYFIN_URL}/health" >/dev/null 2>&1; then
            ok "Jellyfin is up (${waited}s)"
            return 0
        fi
        sleep 3
        waited=$(( waited + 3 ))
    done
    warn "Jellyfin did not answer within ${timeout}s. Container state:"
    docker inspect --format '{{.State.Status}} / health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
        "${JELLYFIN_CONTAINER}" 2>&1 || true
    return 1
}

# Reads the plugin version straight from meta.json so the installed folder name
# always matches what the build actually produced.
plugin_version() {
    python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['version'])" \
        "${REPO_DIR}/JellyProviderSections.Plugin/meta.json"
}

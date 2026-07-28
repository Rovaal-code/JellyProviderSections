#!/usr/bin/env bash
# Starts the isolated test environment and waits until Jellyfin answers.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

mkdir -p "${TESTENV_DIR}"/jellyfin/{config,cache,media} \
         "${TESTENV_DIR}"/seerr/config \
         "${TESTENV_DIR}"/evidence/{logs,screenshots}

log "Starting containers..."
compose up -d

wait_for_jellyfin 180 || die "Jellyfin did not come up. Try: $(basename "${BASH_SOURCE[0]%/*}")/logs.sh --all"

echo
ok "Environment ready"
echo "   Jellyfin: ${JELLYFIN_URL}"
echo "   Seerr:    ${SEERR_URL}"
echo
echo "Next: scripts/seed-synthetic-library.sh, then scripts/build-and-install-plugin.sh"

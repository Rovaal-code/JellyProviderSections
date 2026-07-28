#!/usr/bin/env bash
# DESTRUCTIVE. Removes the containers and every bit of persistent state:
# Jellyfin's config (users, libraries, installed plugins) and Seerr's config.
# Used to prove a from-scratch install still works.
#
# The synthetic media itself is regenerable, so it goes too; re-run
# seed-synthetic-library.sh afterwards.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

cat <<EOF
This will permanently delete:
  ${TESTENV_DIR}/jellyfin/config   (users, libraries, installed plugins)
  ${TESTENV_DIR}/jellyfin/cache
  ${TESTENV_DIR}/jellyfin/media    (synthetic library)
  ${TESTENV_DIR}/seerr/config      (Seerr setup and its API key)

Your .env (TMDb key) is NOT touched.
EOF

read -r -p "Type 'reset' to confirm: " answer
[[ "${answer}" == "reset" ]] || die "Aborted, nothing was deleted."

log "Removing containers and volumes..."
compose down -v --remove-orphans || true

# Jellyfin writes some files as its own uid; if the host user cannot remove
# them, say so plainly instead of failing with a bare permission error.
log "Deleting persistent state..."
rm -rf "${TESTENV_DIR}/jellyfin/config" \
       "${TESTENV_DIR}/jellyfin/cache" \
       "${TESTENV_DIR}/jellyfin/media" \
       "${TESTENV_DIR}/seerr/config" 2>/dev/null || {
    warn "Some files could not be removed as $(id -un)."
    warn "They were written by a container running as another user."
    warn "Remove them manually with: sudo rm -rf ${TESTENV_DIR}/{jellyfin,seerr}"
    exit 1
}

ok "Environment reset. Run scripts/up.sh to start fresh."

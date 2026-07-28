#!/usr/bin/env bash
# Stops the containers. Persistent state under jellyfin/ and seerr/ is kept,
# so the next up.sh resumes exactly where this left off.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

log "Stopping containers (state is preserved)..."
compose down
ok "Stopped. Use reset-environment.sh to also wipe the persistent state."

#!/usr/bin/env bash
# Follows Jellyfin's log, filtered to this plugin's lines by default.
#
#   logs.sh          only JellyProviderSections / ProviderSections lines
#   logs.sh --all    the full Jellyfin log, unfiltered
#   logs.sh --seerr  Seerr's log

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

case "${1:-}" in
    --all)
        log "Full Jellyfin log (Ctrl+C to stop)"
        docker logs -f --tail 200 "${JELLYFIN_CONTAINER}"
        ;;
    --seerr)
        log "Seerr log (Ctrl+C to stop)"
        docker logs -f --tail 200 "${SEERR_CONTAINER}"
        ;;
    *)
        log "Jellyfin log, plugin lines only (Ctrl+C to stop). Use --all for everything."
        # --line-buffered keeps grep from holding output back in its pipe buffer,
        # which otherwise makes a followed log look frozen.
        docker logs -f --tail 500 "${JELLYFIN_CONTAINER}" 2>&1 \
            | grep --line-buffered -iE 'JellyProviderSections|ProviderSections|HomeScreenSections' \
            || true
        ;;
esac

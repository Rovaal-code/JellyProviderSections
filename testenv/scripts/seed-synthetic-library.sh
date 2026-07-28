#!/usr/bin/env bash
# Creates a tiny synthetic library. Jellyfin only needs a scannable file with a
# recognisable name; the bytes are irrelevant, so these are ~1 second of black
# video (or empty files if ffmpeg is unavailable).
#
# Every TMDb id below was verified against themoviedb.org on 2026-07-29, and
# every title is one that really is on the provider named in the comment for
# region ES. That combination is the point: it gives the provider sections a
# predictable mix of "already in the library" and "not in the library" results
# to render, so both card states can be tested without guessing.
#
# The [tmdbid-N] suffix is Jellyfin's own naming convention and makes the
# scanner attach the exact id we want, instead of guessing from the title.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

MEDIA_DIR="${TESTENV_DIR}/jellyfin/media"
MOVIES_DIR="${MEDIA_DIR}/Movies"
SHOWS_DIR="${MEDIA_DIR}/Shows"

# "Title (Year) [tmdbid-ID]"    provider (region ES)
MOVIES=(
    "The Matrix (1999) [tmdbid-603]"            # Netflix ES
    "Blade Runner 2049 (2017) [tmdbid-335984]"  # Prime Video ES
    "Dune (2021) [tmdbid-438631]"               # Netflix / Max ES
)
SHOWS=(
    "Attack on Titan (2013) [tmdbid-1429]"      # Crunchyroll ES
    "Arcane (2021) [tmdbid-94605]"              # Netflix ES
    "Breaking Bad (2008) [tmdbid-1396]"         # Netflix ES
)

have_ffmpeg=0
if command -v ffmpeg >/dev/null 2>&1; then
    have_ffmpeg=1
else
    warn "ffmpeg not found: creating empty .mkv files instead."
    warn "Jellyfin will still index them, but they cannot be played back."
fi

make_video() {
    local target="$1"
    if (( have_ffmpeg )); then
        ffmpeg -nostdin -loglevel error -y \
            -f lavfi -i color=c=black:s=320x240:d=1 \
            -c:v libx264 -preset ultrafast -pix_fmt yuv420p \
            "${target}" </dev/null
    else
        : > "${target}"
    fi
}

log "Creating synthetic library in ${MEDIA_DIR#"${TESTENV_DIR}/"}..."
mkdir -p "${MOVIES_DIR}" "${SHOWS_DIR}"

for movie in "${MOVIES[@]}"; do
    dir="${MOVIES_DIR}/${movie}"
    mkdir -p "${dir}"
    file="${dir}/${movie}.mkv"
    [[ -s "${file}" ]] && { ok "exists: ${movie}"; continue; }
    make_video "${file}"
    ok "movie: ${movie}"
done

for show in "${SHOWS[@]}"; do
    # One episode is enough for the library to hold a Series item with the id.
    dir="${SHOWS_DIR}/${show}/Season 01"
    mkdir -p "${dir}"
    title="${show%% (*}"
    file="${dir}/${title} S01E01.mkv"
    [[ -s "${file}" ]] && { ok "exists: ${show}"; continue; }
    make_video "${file}"
    ok "series: ${show}"
done

echo
ok "Synthetic library ready: ${#MOVIES[@]} movies, ${#SHOWS[@]} series"
cat <<EOF

Add them in Jellyfin (${JELLYFIN_URL}) under Dashboard > Libraries:
  Movies  ->  /media/Movies
  Shows   ->  /media/Shows

The container mounts this folder read-only at /media.
EOF

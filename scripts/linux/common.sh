# Shared helpers for the Linux (F-13) build scripts. Source it; do not execute it.
# shellcheck shell=bash

# Repository root = two levels above this file (scripts/linux/common.sh).
LLP_REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"

# Per-user cache for downloads and generated test media (never inside the repository).
LLP_CACHE_DIR="${LLPLAYER_CACHE_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/llplayer}"

# FFmpeg 8.1 shared build from BtbN/FFmpeg-Builds. Linux FlyleafLib binds to exactly these sonames
# (same majors as the tracked Windows FFmpeg/*.dll set: avcodec-62, avformat-62, avutil-60, ...).
LLP_FFMPEG_ASSET_DEFAULT="ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz"
LLP_FFMPEG_BASE_URL_DEFAULT="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest"
LLP_FFMPEG_SONAMES=(
    "libavcodec.so.62"
    "libavdevice.so.62"
    "libavfilter.so.11"
    "libavformat.so.62"
    "libavutil.so.60"
    "libswresample.so.6"
    "libswscale.so.9"
)
LLP_FFMPEG_DEST_DEFAULT="$LLP_REPO_ROOT/FFmpeg/linux-x64"
LLP_FFMPEG_BIN_DEFAULT="$LLP_CACHE_DIR/ffmpeg-bin"

llp_log() { printf '[%s] %s\n' "${LLP_SCRIPT_NAME:-linux}" "$*" >&2; }
llp_die() { printf '[%s] ERROR: %s\n' "${LLP_SCRIPT_NAME:-linux}" "$*" >&2; exit 1; }

llp_require_cmd() {
    local cmd
    for cmd in "$@"; do
        command -v "$cmd" >/dev/null 2>&1 || llp_die "required command '$cmd' is not on PATH."
    done
}

# Prints the absolute form of a path whose parent may not exist yet.
llp_abspath() {
    case "$1" in
        /*) printf '%s\n' "$1" ;;
        *) printf '%s/%s\n' "$(pwd -P)" "$1" ;;
    esac
}

# True when every expected FFmpeg soname is present (file or symlink resolving to a file) in $1.
llp_ffmpeg_libs_present() {
    local dir="$1" so
    for so in "${LLP_FFMPEG_SONAMES[@]}"; do
        [[ -f "$dir/$so" ]] || return 1
    done
    return 0
}

# Reads <Version> from a csproj (first match). Prints nothing when absent.
llp_csproj_version() {
    sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$1" | head -n 1
}

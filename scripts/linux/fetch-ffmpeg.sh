#!/usr/bin/env bash
# Downloads, verifies and installs the FFmpeg 8.1 shared libraries for the Linux build (F-13).
#
# Why fetched and not committed: the Linux shared libs are ~210 MB unpacked (libavcodec alone ~115 MB), far above
# what belongs in git history. The Windows FFmpeg/*.dll set stays tracked exactly as before (release packaging
# copies it from the repository); only the Linux libs are fetched, into the gitignored FFmpeg/linux-x64/.
#
# Source: BtbN/FFmpeg-Builds, release tag "latest", asset ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz, verified
# against the checksums.sha256 published in the same release.
#
# Reproducibility trade-off: "latest" is a ROLLING tag. BtbN rebuilds the n8.1 branch regularly, so the asset and its
# checksum change over time (same 8.1 ABI and sonames, newer patch level / dependencies). The sha256 check proves the
# download matches what the release published, not that two fetches on different days are byte-identical. For a
# reproducible build, pin both LLPLAYER_FFMPEG_URL (a dated "autobuild-YYYY-MM-DD-HH-MM" release asset) and
# LLPLAYER_FFMPEG_SHA256. BtbN prunes old dated releases, so a pinned URL can disappear; keep a copy of the tarball
# if long-term reproducibility matters. Locally, the verified tarball is cached and reused until --refresh.
#
# Usage: scripts/linux/fetch-ffmpeg.sh [--dest DIR] [--bin-dir DIR | --no-bin] [--refresh]
#   --dest DIR     where lib*.so* are installed (default: <repo>/FFmpeg/linux-x64)
#   --bin-dir DIR  where the ffmpeg/ffprobe CLI goes, for test-media generation (default: ~/.cache/llplayer/ffmpeg-bin)
#   --no-bin       do not install the CLI
#   --refresh      ignore the cached tarball/checksum and fetch the current "latest" again
# Environment:
#   LLPLAYER_FFMPEG_URL     tarball URL override (its checksums.sha256 is looked up next to it unless pinned)
#   LLPLAYER_FFMPEG_SHA256  expected sha256 of the tarball (pins it; checksums.sha256 is then not downloaded)
#   LLPLAYER_CACHE_DIR      download cache (default: ${XDG_CACHE_HOME:-~/.cache}/llplayer)
# Prints the absolute library directory on stdout (logs go to stderr), e.g.
#   export LLPLAYER_FFMPEG_DIR="$(scripts/linux/fetch-ffmpeg.sh)"
set -euo pipefail

LLP_SCRIPT_NAME="fetch-ffmpeg"
# shellcheck source=scripts/linux/common.sh
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"

usage() { sed -n '/^# Usage:/,/^# Prints/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2; }

dest="$LLP_FFMPEG_DEST_DEFAULT"
bin_dir="$LLP_FFMPEG_BIN_DEFAULT"
install_bin=1
refresh=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --dest) [[ $# -ge 2 ]] || llp_die "--dest needs a value"; dest="$2"; shift 2 ;;
        --bin-dir) [[ $# -ge 2 ]] || llp_die "--bin-dir needs a value"; bin_dir="$2"; shift 2 ;;
        --no-bin) install_bin=0; shift ;;
        --refresh) refresh=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage; llp_die "unknown argument: $1" ;;
    esac
done

llp_require_cmd curl sha256sum tar xz find cp mv
dest="$(llp_abspath "$dest")"
bin_dir="$(llp_abspath "$bin_dir")"

url="${LLPLAYER_FFMPEG_URL:-$LLP_FFMPEG_BASE_URL_DEFAULT/$LLP_FFMPEG_ASSET_DEFAULT}"
[[ "$url" == https://* ]] || llp_die "LLPLAYER_FFMPEG_URL must be an https:// URL: $url"
asset="${url##*/}"
[[ "$asset" =~ ^[A-Za-z0-9._+-]+\.tar\.xz$ ]] || llp_die "unexpected FFmpeg asset name '$asset' (need *.tar.xz)."
checksums_url="${url%/*}/checksums.sha256"

pinned_sha=""
if [[ -n "${LLPLAYER_FFMPEG_SHA256:-}" ]]; then
    pinned_sha="$(printf '%s' "$LLPLAYER_FFMPEG_SHA256" | tr 'A-F' 'a-f')"
    [[ "$pinned_sha" =~ ^[0-9a-f]{64}$ ]] || llp_die "LLPLAYER_FFMPEG_SHA256 must be 64 hex characters."
fi

mkdir -p "$LLP_CACHE_DIR"
# Serialize concurrent runs that share the cache (parallel CI jobs / agents); flock is util-linux, present on Ubuntu.
if command -v flock >/dev/null 2>&1; then
    exec 9> "$LLP_CACHE_DIR/.fetch-ffmpeg.lock"
    flock 9
fi
tarball="$LLP_CACHE_DIR/$asset"
sums_cache="$LLP_CACHE_DIR/$asset.sha256"

download() { # url out
    curl -fL --retry 3 --retry-delay 3 --connect-timeout 30 --no-progress-meter -o "$2" "$1"
}

# Downloads checksums.sha256 and stores the single expected hash of $asset in $sums_cache.
refresh_expected_sha() {
    local tmp hashes
    tmp="$(mktemp "$LLP_CACHE_DIR/checksums.XXXXXX")"
    llp_log "downloading $checksums_url"
    if ! download "$checksums_url" "$tmp"; then
        rm -f "$tmp"
        llp_die "could not download $checksums_url (set LLPLAYER_FFMPEG_SHA256 to pin the hash instead)."
    fi
    # Lines are "<sha256>  <file>"; exact file-name match, duplicates allowed only if they agree.
    hashes="$(awk -v f="$asset" '$2 == f || $2 == "*" f { print tolower($1) }' "$tmp" | sort -u)"
    rm -f "$tmp"
    [[ -n "$hashes" ]] || llp_die "checksums.sha256 has no entry for $asset."
    [[ "$(printf '%s\n' "$hashes" | wc -l)" -eq 1 ]] || llp_die "checksums.sha256 lists conflicting hashes for $asset."
    [[ "$hashes" =~ ^[0-9a-f]{64}$ ]] || llp_die "malformed sha256 for $asset in checksums.sha256."
    printf '%s\n' "$hashes" > "$sums_cache"
}

sha_of() { sha256sum "$1" | awk '{ print $1 }'; }

expected=""
sums_from_cache=0
if [[ -n "$pinned_sha" ]]; then
    expected="$pinned_sha"
    llp_log "using pinned sha256 $expected"
elif [[ $refresh -eq 0 && -f "$tarball" && -f "$sums_cache" ]]; then
    expected="$(tr -d '[:space:]' < "$sums_cache")"
    [[ "$expected" =~ ^[0-9a-f]{64}$ ]] || { refresh_expected_sha; expected="$(cat "$sums_cache")"; }
    sums_from_cache=1
else
    refresh_expected_sha
    expected="$(cat "$sums_cache")"
fi

if [[ -f "$tarball" && $refresh -eq 0 ]] && [[ "$(sha_of "$tarball")" == "$expected" ]]; then
    llp_log "cached tarball verified: $tarball"
else
    if [[ -f "$tarball" && $sums_from_cache -eq 1 ]]; then
        # Cached tarball does not match its cached hash: re-resolve the rolling "latest" before downloading.
        refresh_expected_sha
        expected="$(cat "$sums_cache")"
    fi
    part="$tarball.part"
    rm -f "$part"
    llp_log "downloading $url"
    download "$url" "$part" || { rm -f "$part"; llp_die "download failed: $url"; }
    actual="$(sha_of "$part")"
    if [[ "$actual" != "$expected" ]]; then
        rm -f "$part"
        llp_die "sha256 mismatch for $asset: expected $expected, got $actual. If the rolling 'latest' release was being republished, retry with --refresh."
    fi
    mv -f "$part" "$tarball"
    llp_log "tarball verified: $tarball"
fi

stamp_name=".llplayer-ffmpeg-source"
stamp_line="$expected  $asset"

bin_ok() {
    [[ -x "$bin_dir/bin/ffmpeg" && -x "$bin_dir/bin/ffprobe" && -L "$bin_dir/lib" ]] &&
        [[ "$(readlink "$bin_dir/lib")" == "$dest" ]] &&
        [[ "$(cat "$bin_dir/$stamp_name" 2>/dev/null || true)" == "$stamp_line" ]]
}

if [[ $refresh -eq 0 && -f "$dest/$stamp_name" && "$(cat "$dest/$stamp_name")" == "$stamp_line" ]] &&
    llp_ffmpeg_libs_present "$dest" && [[ -f "$dest/LICENSE.txt" ]] && { [[ $install_bin -eq 0 ]] || bin_ok; }; then
    llp_log "already installed: $dest"
    printf '%s\n' "$dest"
    exit 0
fi

# Refuse to replace a directory this script did not create (protects a mistyped --dest).
if [[ -d "$dest" && ! -f "$dest/$stamp_name" ]] && [[ -n "$(ls -A "$dest")" ]]; then
    llp_die "refusing to replace non-empty $dest that has no $stamp_name marker; remove it or pick another --dest."
fi
case "$dest" in
    /|"$HOME"|"$LLP_REPO_ROOT"|"$LLP_REPO_ROOT/FFmpeg") llp_die "refusing to use $dest as the install directory." ;;
esac
# Same protection for the CLI directory: it gets bin/ffmpeg, bin/ffprobe and a lib -> <dest> symlink.
if [[ $install_bin -eq 1 ]]; then
    if [[ -d "$bin_dir" && ! -f "$bin_dir/$stamp_name" ]] && [[ -n "$(ls -A "$bin_dir")" ]]; then
        llp_die "refusing to write into non-empty $bin_dir that has no $stamp_name marker; pick another --bin-dir."
    fi
    if [[ -e "$bin_dir/lib" && ! -L "$bin_dir/lib" ]]; then
        llp_die "$bin_dir/lib exists and is not a symlink; refusing to replace it."
    fi
fi

work="$(mktemp -d "$LLP_CACHE_DIR/extract.XXXXXX")"
cleanup() { rm -rf "$work"; [[ -n "${stage:-}" ]] && rm -rf "$stage"; return 0; }
trap cleanup EXIT

members=('*/lib/*.so*' '*/LICENSE.txt')
[[ $install_bin -eq 1 ]] && members+=('*/bin/ffmpeg' '*/bin/ffprobe')
llp_log "extracting ${members[*]}"
tar -xJf "$tarball" -C "$work" --wildcards --no-same-owner "${members[@]}" ||
    llp_die "could not extract ${members[*]} from $asset."
mapfile -t lib_dirs < <(find "$work" -mindepth 2 -maxdepth 2 -type d -name lib)
[[ ${#lib_dirs[@]} -eq 1 ]] || llp_die "expected exactly one <top>/lib directory in $asset, found ${#lib_dirs[@]}."
src_lib="${lib_dirs[0]}"
src_bin="$(dirname "$src_lib")/bin"

# Explicit soname major check: FlyleafLib binds to these exact majors (same as the Windows DLL set).
# Pass 1: file names (every expected soname present, no other major shipped). Pass 2: the ELF SONAME itself.
for so in "${LLP_FFMPEG_SONAMES[@]}"; do
    base="${so%%.so.*}"                     # libavcodec
    [[ -f "$src_lib/$so" ]] || llp_die "$asset does not provide $so (FFmpeg ABI changed?)."
    while IFS= read -r other; do
        name="${other##*/}"
        [[ "$name" == "$base.so" || "$name" == "$so" || "$name" == "$so".* ]] ||
            llp_die "$asset ships unexpected $name; expected major ${so##*.so.} only."
    done < <(find "$src_lib" -maxdepth 1 -name "$base.so*")
done
if command -v readelf >/dev/null 2>&1; then
    for so in "${LLP_FFMPEG_SONAMES[@]}"; do
        soname="$(readelf -d "$src_lib/$so" 2>/dev/null | sed -n 's/.*Library soname: \[\(.*\)\].*/\1/p')" || soname=""
        [[ "$soname" == "$so" ]] || llp_die "$so has ELF SONAME '$soname' (expected '$so')."
    done
else
    llp_log "readelf not found; ELF SONAME check skipped (file-name check done)."
fi

mkdir -p "$(dirname "$dest")"
stage="$(mktemp -d "$(dirname "$dest")/.$(basename "$dest").tmp.XXXXXX")"
chmod 755 "$stage"   # mktemp -d creates 0700; the app may run as another user
cp -a "$src_lib"/*.so* "$stage/"
# GPL build: the license text travels with the libraries (publish.sh ships it as FFmpeg/LICENSE.txt).
[[ -f "$(dirname "$src_lib")/LICENSE.txt" ]] || llp_die "$asset has no LICENSE.txt."
cp "$(dirname "$src_lib")/LICENSE.txt" "$stage/LICENSE.txt"
printf '%s\n' "$stamp_line" > "$stage/$stamp_name"
llp_ffmpeg_libs_present "$stage" || llp_die "installed libraries are incomplete."
rm -rf "$dest"
mv "$stage" "$dest"
stage=""
llp_log "installed $(find "$dest" -maxdepth 1 -name 'lib*.so*' | wc -l) library entries into $dest"

if [[ $install_bin -eq 1 ]]; then
    [[ -x "$src_bin/ffmpeg" && -x "$src_bin/ffprobe" ]] || llp_die "$asset has no bin/ffmpeg + bin/ffprobe."
    mkdir -p "$bin_dir/bin"
    cp -f "$src_bin/ffmpeg" "$src_bin/ffprobe" "$bin_dir/bin/"
    # The CLI has RPATH $ORIGIN/../lib; point that at the installed libraries instead of duplicating 200 MB.
    rm -f "$bin_dir/lib"
    ln -s "$dest" "$bin_dir/lib"
    printf '%s\n' "$stamp_line" > "$bin_dir/$stamp_name"
    cli_version="$("$bin_dir/bin/ffmpeg" -hide_banner -version)" || llp_die "installed ffmpeg CLI does not run."
    llp_log "CLI installed: $bin_dir/bin/ffmpeg (${cli_version%%$'\n'*})"
fi

printf '%s\n' "$dest"

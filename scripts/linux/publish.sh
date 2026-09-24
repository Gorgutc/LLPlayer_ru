#!/usr/bin/env bash
# Builds the Linux package LLPlayer-<version>-linux-x64.tar.gz from LLPlayer.Avalonia (F-13).
# Linux counterpart of the Windows packaging in .github/actions/build-package/action.yml, which stays the source of
# truth for the Windows release. Framework-dependent: end users need the .NET 10 runtime
# (Ubuntu: sudo apt install dotnet-runtime-10.0) and OpenAL Soft (Ubuntu: sudo apt install libopenal1).
#
# Usage: scripts/linux/publish.sh [--out DIR] [--ffmpeg-dir DIR] [--staging DIR] [--force]
#   --out DIR         where the archive (+ .sha256) is written (default: <repo>/artifacts/linux, gitignored)
#   --ffmpeg-dir DIR  FFmpeg 8.1 shared libs to bundle (default: $LLPLAYER_FFMPEG_DIR, else <repo>/FFmpeg/linux-x64)
#   --staging DIR     keep the unpacked package in DIR (must not exist); default: a temp dir removed on exit
#   --force           replace an existing archive with the same name
#   --check-package DIR  only run the content validation on an unpacked package directory, then exit
# Prints the absolute archive path on stdout (nothing with --check-package).
set -euo pipefail

LLP_SCRIPT_NAME="publish"
# shellcheck source=scripts/linux/common.sh
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"

usage() { sed -n '/^# Usage:/,/^# Prints/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2; }

out_dir="$LLP_REPO_ROOT/artifacts/linux"
ffmpeg_dir="${LLPLAYER_FFMPEG_DIR:-$LLP_FFMPEG_DEST_DEFAULT}"
staging=""
force=0
check_dir=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --out) [[ $# -ge 2 ]] || llp_die "--out needs a value"; out_dir="$2"; shift 2 ;;
        --ffmpeg-dir) [[ $# -ge 2 ]] || llp_die "--ffmpeg-dir needs a value"; ffmpeg_dir="$2"; shift 2 ;;
        --staging) [[ $# -ge 2 ]] || llp_die "--staging needs a value"; staging="$2"; shift 2 ;;
        --force) force=1; shift ;;
        --check-package) [[ $# -ge 2 ]] || llp_die "--check-package needs a value"; check_dir="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) usage; llp_die "unknown argument: $1" ;;
    esac
done

reject() { llp_die "Linux package must not include $1: $2"; }

# Positive and negative content validation of an unpacked package directory (also: --check-package DIR).
validate_package() {
    local pkg
    pkg="$(readlink -f "$1")"
    [[ -d "$pkg" ]] || llp_die "package directory not found: $1"
    # ---- Positive content validation (mirrors "Release package is missing required file" on Windows) ----
    local required rel path resolved exe hits so_path name ok so link target allowed_natives
    required=(
        "LLPlayer.Avalonia"
        "LLPlayer.Avalonia.dll"
        "LLPlayer.Avalonia.deps.json"
        "LLPlayer.Avalonia.runtimeconfig.json"
        "FlyleafLib.dll"
        "libSkiaSharp.so"
        "libHarfBuzzSharp.so"
        "llplayer"
        "llplayer.desktop"
        "LLPlayer.png"
        "LICENSE"
        "THIRD-PARTY-NOTICES.md"
        "FFmpeg/LICENSE.txt"
        "FFmpeg/SOURCE.txt"
        "dub_sidecar/server.py"
        "dub_sidecar/pyproject.toml"
        "dub_sidecar/uv.lock"
        "dub_sidecar/README.md"
    )
    for so in "${LLP_FFMPEG_SONAMES[@]}"; do required+=("FFmpeg/$so"); done
    for rel in "${required[@]}"; do
        path="$pkg/$rel"
        [[ -e "$path" ]] || llp_die "Linux package is missing required file $rel."
        resolved="$(readlink -f "$path")"
        [[ "$resolved" == "$pkg/"* && -f "$resolved" && -s "$resolved" ]] ||
            llp_die "Linux package required file $rel must be a non-empty regular file inside the package."
    done
    for exe in LLPlayer.Avalonia llplayer; do
        [[ -x "$pkg/$exe" ]] || llp_die "Linux package file $exe must be executable."
    done

    # ---- Negative validation (mirrors the Windows ship rules in docs/agent/dependency-baseline.md) ----
    hits="$(find "$pkg" \( -name 'LLPlayer.Config.json' -o -name 'LLPlayer.Engine.json' -o -name 'LLPlayer.PlayerConfig.json' \
        -o -name 'LLPlayer.WordList.json' -o -name 'crash.log' -o -name '*.log' -o -name '*.dmp' -o -name '*.dump' \
        -o -name '.env*' \) -print)"
    [[ -z "$hits" ]] || reject "runtime config / logs / dumps / secrets" "$hits"
    hits="$(find "$pkg" -type d \( -name DubEngine -o -name dubmodels -o -name '.venv' -o -name venv -o -name __pycache__ \
        -o -name whispermodels -o -name Whisper -o -name tesseractmodels -o -name Recordings -o -name Snapshots \) -print)"
    [[ -z "$hits" ]] || reject "dubbing venv / model / user media folders" "$hits"
    hits="$(find "$pkg" \( -name '*.ru.dub.*' -o -name '*.ru.voices.json' -o -name 'ggml-*.bin' -o -name '*.traineddata' \) -print)"
    [[ -z "$hits" ]] || reject "rendered dubs, voice companion files or downloaded models" "$hits"
    # Shared objects outside FFmpeg/: only the known runtime natives of the referenced NuGet packages
    # (Avalonia/Skia/HarfBuzz rendering, ONNX Runtime for Silero VAD).
    allowed_natives=" libSkiaSharp.so libHarfBuzzSharp.so libonnxruntime.so libonnxruntime_providers_shared.so "
    while IFS= read -r so_path; do
        rel="${so_path#"$pkg"/}"
        case "$rel" in
            FFmpeg/*) continue ;;
        esac
        [[ "$allowed_natives" == *" $rel "* ]] || reject "an unexpected native library outside FFmpeg/" "$rel"
    done < <(find "$pkg" \( -name '*.so' -o -name '*.so.*' \) -print)
    # FFmpeg/ holds exactly the pinned majors.
    while IFS= read -r so_path; do
        name="${so_path##*/}"
        ok=0
        for so in "${LLP_FFMPEG_SONAMES[@]}"; do
            [[ "$name" == "$so" || "$name" == "$so".* ]] && ok=1
        done
        [[ $ok -eq 1 ]] || reject "an unexpected FFmpeg library" "FFmpeg/$name"
    done < <(find "$pkg/FFmpeg" -name 'lib*' -print)
    # Windows-only native payloads that a linux-x64 publish must not carry.
    hits="$(find "$pkg" -maxdepth 1 -type d \( -name x86 -o -name x64 \) -print
        find "$pkg" -path '*/runtimes/win*' -prune -print)"
    [[ -z "$hits" ]] || reject "Windows native payload folders" "$hits"
    # No symlink may point outside the package.
    while IFS= read -r link; do
        target="$(readlink -f "$link" || true)"
        [[ "$target" == "$pkg/"* ]] || reject "a symlink escaping the package" "${link#"$pkg"/} -> $(readlink "$link")"
    done < <(find "$pkg" -type l -print)
    llp_log "package content validation passed: $pkg"
}

if [[ -n "$check_dir" ]]; then
    llp_require_cmd find readlink
    validate_package "$check_dir"
    exit 0
fi

cd "$LLP_REPO_ROOT"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
llp_require_cmd dotnet tar gzip sha256sum find readlink

app_proj="LLPlayer.Avalonia/LLPlayer.Avalonia.csproj"
[[ -f "$app_proj" ]] || llp_die "required project $app_proj is missing; the Linux package is built from LLPlayer.Avalonia."
ffmpeg_dir="$(llp_abspath "$ffmpeg_dir")"
llp_ffmpeg_libs_present "$ffmpeg_dir" ||
    llp_die "FFmpeg 8.1 libraries not found in $ffmpeg_dir. Run scripts/linux/fetch-ffmpeg.sh first."
[[ -f "$ffmpeg_dir/LICENSE.txt" ]] || llp_die "$ffmpeg_dir/LICENSE.txt is missing (re-run scripts/linux/fetch-ffmpeg.sh --refresh)."

version="$(llp_csproj_version "$app_proj")"
[[ -n "$version" ]] || version="$(llp_csproj_version LLPlayer/LLPlayer.csproj)"
[[ "$version" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,63}$ ]] || llp_die "cannot determine a path-safe version (got '$version')."
pkg_name="LLPlayer-$version-linux-x64"
out_dir="$(llp_abspath "$out_dir")"
archive="$out_dir/$pkg_name.tar.gz"
mkdir -p "$out_dir"
if [[ -e "$archive" && $force -eq 0 ]]; then
    llp_die "refusing to overwrite existing $archive (use --force)."
fi

if [[ -n "$staging" ]]; then
    staging="$(llp_abspath "$staging")"
    [[ ! -e "$staging" ]] || llp_die "--staging $staging already exists."
    mkdir -p "$staging"
    keep_staging=1
else
    staging="$(mktemp -d "${TMPDIR:-/tmp}/llplayer-publish.XXXXXX")"
    keep_staging=0
fi
tmp_archive=""
cleanup() {
    [[ $keep_staging -eq 1 ]] || rm -rf "$staging"
    [[ -z "$tmp_archive" ]] || rm -f "$tmp_archive"
}
trap cleanup EXIT
pkg="$staging/$pkg_name"

llp_log "publishing $app_proj ($version) into $pkg"
dotnet publish "$app_proj" -c Release -r linux-x64 --self-contained false -warnaserror -o "$pkg"

# Clean-up TesseractOCR: its build targets copy Windows-only x64/ and x86/ leptonica/tesseract DLLs regardless of
# the RID (Linux OCR will use the system Tesseract). Remove only the known payload; anything else is drift.
for dir in x64 x86; do
    [[ -d "$pkg/$dir" ]] || continue
    unexpected="$(find "$pkg/$dir" -mindepth 1 ! -name 'leptonica-*.dll' ! -name 'tesseract*.dll' -print)"
    [[ -z "$unexpected" ]] || llp_die "unexpected files in $dir/ (publish output drifted): $unexpected"
    rm -rf "${pkg:?}/$dir"
done

# FFmpeg: runtime sonames and their versioned targets only (no unversioned dev symlinks, no stamp).
mkdir -p "$pkg/FFmpeg"
find "$ffmpeg_dir" -maxdepth 1 -name 'lib*.so.*' -exec cp -a {} "$pkg/FFmpeg/" \;
cp "$ffmpeg_dir/LICENSE.txt" "$pkg/FFmpeg/LICENSE.txt"
# Provenance of the bundled GPL FFmpeg build (the fetch-ffmpeg.sh marker records "<sha256>  <asset>").
ffmpeg_stamp=""
[[ -f "$ffmpeg_dir/.llplayer-ffmpeg-source" ]] && ffmpeg_stamp="$(head -n 1 "$ffmpeg_dir/.llplayer-ffmpeg-source")"
{
    printf 'FFmpeg shared libraries bundled with LLPlayer (Linux)\n\n'
    printf 'License: GPL (see LICENSE.txt in this folder). LLPlayer loads these libraries dynamically.\n'
    if [[ -n "$ffmpeg_stamp" ]]; then
        printf 'Build: %s (BtbN/FFmpeg-Builds)\n' "${ffmpeg_stamp#*  }"
        printf 'Build sha256: %s\n' "${ffmpeg_stamp%%  *}"
    else
        printf 'Build: supplied via --ffmpeg-dir; not fetched by scripts/linux/fetch-ffmpeg.sh (provenance not recorded)\n'
    fi
    printf 'Build recipe: https://github.com/BtbN/FFmpeg-Builds\n'
    printf 'FFmpeg source (the n8.1 build follows the release/8.1 branch): https://git.ffmpeg.org/ffmpeg.git\n'
    printf 'The exact FFmpeg revision is reported by the libraries (av_version_info, e.g. in the LLPlayer log).\n'
} > "$pkg/FFmpeg/SOURCE.txt"
[[ -n "$ffmpeg_stamp" ]] || llp_log "warning: $ffmpeg_dir has no fetch-ffmpeg.sh marker; FFmpeg/SOURCE.txt records no build hash."

# dub_sidecar sources ship like the Windows package (the venv and model weights are provisioned at runtime).
mkdir -p "$pkg/dub_sidecar"
for f in server.py pyproject.toml uv.lock README.md; do
    [[ -f "$pkg/dub_sidecar/$f" ]] || cp "dub_sidecar/$f" "$pkg/dub_sidecar/$f"
done

cp LICENSE "$pkg/LICENSE"
[[ -f LLPlayer.Avalonia/THIRD-PARTY-NOTICES.md ]] ||
    llp_die "LLPlayer.Avalonia/THIRD-PARTY-NOTICES.md is missing (shadcn/ui MIT, Lucide ISC/MIT and FFmpeg notices ship with the package)."
cp LLPlayer.Avalonia/THIRD-PARTY-NOTICES.md "$pkg/THIRD-PARTY-NOTICES.md"
cp LLPlayer.png "$pkg/LLPlayer.png"

cat > "$pkg/llplayer" <<'LAUNCHER'
#!/bin/sh
# LLPlayer launcher (Linux). Needs the .NET 10 runtime (Ubuntu: apt install dotnet-runtime-10.0) and OpenAL Soft
# (apt install libopenal1). FFmpeg is bundled in ./FFmpeg; LLPLAYER_FFMPEG_DIR or --ffmpeg-dir override it.
here="$(dirname "$(readlink -f "$0")")"
if [ -x "$here/LLPlayer.Avalonia" ]; then
    exec "$here/LLPlayer.Avalonia" "$@"
fi
exec dotnet "$here/LLPlayer.Avalonia.dll" "$@"
LAUNCHER
chmod 755 "$pkg/llplayer"

cat > "$pkg/llplayer.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Version=1.5
Name=LLPlayer
GenericName=Media Player
Comment=Media player for language learning with dual subtitles
Exec=llplayer %F
Icon=llplayer
Terminal=false
Categories=AudioVideo;Video;Player;Education;
MimeType=video/mp4;video/x-matroska;video/webm;video/quicktime;video/x-msvideo;video/mpeg;audio/mpeg;audio/mp4;audio/flac;audio/ogg;audio/x-wav;application/x-subrip;
Keywords=video;subtitles;language;learning;
DESKTOP
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$pkg/llplayer.desktop"
fi

validate_package "$pkg"

# ---- Archive (sorted, root-owned, gzip without timestamps) + integrity test ----
mtime="$(git log -1 --format=%ct 2>/dev/null || date +%s)"
tmp_archive="$(mktemp "$out_dir/.$pkg_name.XXXXXX.tar.gz")"
tar -C "$staging" --sort=name --owner=0 --group=0 --numeric-owner --mtime="@$mtime" -cf - "$pkg_name" |
    gzip -n -9 > "$tmp_archive"
tar -tzf "$tmp_archive" > /dev/null || llp_die "archive integrity test failed."
chmod 644 "$tmp_archive"
mv -f "$tmp_archive" "$archive"
tmp_archive=""
sha="$(sha256sum "$archive" | awk '{ print $1 }')"
printf '%s  %s\n' "$sha" "$pkg_name.tar.gz" > "$archive.sha256"
llp_log "archive: $archive ($(stat -c %s "$archive") bytes, sha256 $sha)"
[[ $keep_staging -eq 1 ]] && llp_log "staging kept: $pkg"
printf '%s\n' "$archive"

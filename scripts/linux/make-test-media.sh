#!/usr/bin/env bash
# Generates the deterministic Linux test clip used by the env-gated FFmpeg integration tests (LLPLAYER_TEST_MEDIA):
#   test-720p.mp4  12 s, H.264 High 1280x720 30 fps (lavfi testsrc2) + AAC-LC 48 kHz mono 440 Hz sine
#   test-720p.srt  3 cues: 0.5-3.0 / 3.5-6.5 / 7.0-11.0
# Encoding is single-threaded with bitexact flags and no metadata, so the same FFmpeg build yields the same bytes.
#
# Usage: scripts/linux/make-test-media.sh [--out DIR] [--ffmpeg PATH] [--force]
#   --out DIR      output directory (default: ~/.cache/llplayer/media)
#   --ffmpeg PATH  ffmpeg CLI (default: $LLPLAYER_FFMPEG_CLI, else the one installed by fetch-ffmpeg.sh)
#   --force        regenerate even if both files already exist
# Prints the absolute path of test-720p.mp4 on stdout, e.g.
#   export LLPLAYER_TEST_MEDIA="$(scripts/linux/make-test-media.sh)"
set -euo pipefail

LLP_SCRIPT_NAME="make-test-media"
# shellcheck source=scripts/linux/common.sh
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"

usage() { sed -n '/^# Usage:/,/^# Prints/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2; }

out_dir="$LLP_CACHE_DIR/media"
ffmpeg="${LLPLAYER_FFMPEG_CLI:-$LLP_FFMPEG_BIN_DEFAULT/bin/ffmpeg}"
force=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --out) [[ $# -ge 2 ]] || llp_die "--out needs a value"; out_dir="$2"; shift 2 ;;
        --ffmpeg) [[ $# -ge 2 ]] || llp_die "--ffmpeg needs a value"; ffmpeg="$2"; shift 2 ;;
        --force) force=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage; llp_die "unknown argument: $1" ;;
    esac
done

[[ -x "$ffmpeg" ]] || llp_die "ffmpeg CLI not found at '$ffmpeg'. Run scripts/linux/fetch-ffmpeg.sh first or pass --ffmpeg."
out_dir="$(llp_abspath "$out_dir")"
mkdir -p "$out_dir"
mp4="$out_dir/test-720p.mp4"
srt="$out_dir/test-720p.srt"

if [[ $force -eq 0 && -s "$mp4" && -s "$srt" ]]; then
    llp_log "already present: $mp4"
    printf '%s\n' "$mp4"
    exit 0
fi

tmp_mp4="$(mktemp "$out_dir/.test-720p.XXXXXX.mp4")"
tmp_srt="$(mktemp "$out_dir/.test-720p.XXXXXX.srt")"
trap 'rm -f "$tmp_mp4" "$tmp_srt"' EXIT

llp_log "encoding $mp4"
"$ffmpeg" -hide_banner -nostdin -loglevel error -y \
    -f lavfi -i "testsrc2=size=1280x720:rate=30:duration=12" \
    -f lavfi -i "sine=frequency=440:sample_rate=48000:duration=12" \
    -map 0:v:0 -map 1:a:0 \
    -c:v libx264 -preset veryfast -profile:v high -pix_fmt yuv420p -g 30 -x264-params threads=1 \
    -c:a aac -b:a 64k -ac 1 \
    -threads 1 -map_metadata -1 -fflags +bitexact -flags:v +bitexact -flags:a +bitexact \
    -movflags +faststart -f mp4 "$tmp_mp4"

cat > "$tmp_srt" <<'SRT'
1
00:00:00,500 --> 00:00:03,000
Hello world, this is a test.

2
00:00:03,500 --> 00:00:06,500
The quick brown fox jumps over the lazy dog.

3
00:00:07,000 --> 00:00:11,000
Language learning with subtitles.
SRT

# Sanity check with the ffprobe next to ffmpeg (skipped if absent).
ffprobe="$(dirname "$ffmpeg")/ffprobe"
if [[ -x "$ffprobe" ]]; then
    probe="$("$ffprobe" -v error -show_entries stream=codec_name,width,height,sample_rate -of csv=p=0 "$tmp_mp4")"
    [[ "$probe" == *"h264,1280,720"* && "$probe" == *"aac,48000"* ]] || llp_die "unexpected streams in generated clip: $probe"
fi

chmod 644 "$tmp_mp4" "$tmp_srt"
mv -f "$tmp_mp4" "$mp4"
mv -f "$tmp_srt" "$srt"
llp_log "sha256 $(sha256sum "$mp4" | awk '{ print $1 }')  test-720p.mp4"
printf '%s\n' "$mp4"

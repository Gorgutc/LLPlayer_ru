#!/usr/bin/env bash
# Deterministic, offline self-tests for scripts/linux (run by scripts/linux/verify.sh).
#  - publish.sh --check-package: a synthetic valid package passes; each forbidden/missing item is rejected.
#  - fetch-ffmpeg.sh: soname-major, license, dest-safety and input checks on tiny pinned tarballs from a pre-seeded
#    cache (no network); the happy path needs a C compiler to build stub ELF libraries and is skipped without one.
#  - make-test-media.sh: fails clearly without an ffmpeg CLI.
# ok() only increments a counter and always succeeds, so `check && ok || bad msg` is a safe if/else here.
# shellcheck disable=SC2015
set -euo pipefail

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
linux_dir="$(dirname "$here")"
work="$(mktemp -d "${TMPDIR:-/tmp}/llplayer-script-tests.XXXXXX")"
trap 'rm -rf "$work"' EXIT

passed=0
failed=0
ok() { passed=$((passed + 1)); }
bad() { failed=$((failed + 1)); printf 'FAIL: %s\n' "$*" >&2; }

# expect_fail <description> <stderr pattern> <command...>
expect_fail() {
    local desc="$1" pattern="$2" out
    shift 2
    if out="$("$@" 2>&1)"; then
        bad "$desc: expected failure, command succeeded"
    elif [[ "$out" != *"$pattern"* ]]; then
        bad "$desc: failed for the wrong reason (want '$pattern'): $out"
    else
        ok
    fi
}
# expect_ok <description> <command...>
expect_ok() {
    local desc="$1" out
    shift
    if out="$("$@" 2>&1)"; then ok; else bad "$desc: $out"; fi
}

sonames=(libavcodec.so.62 libavdevice.so.62 libavfilter.so.11 libavformat.so.62 libavutil.so.60 libswresample.so.6 libswscale.so.9)

# ---------------------------------------------------------------- publish.sh --check-package
make_package() { # dir
    local p="$1" so
    mkdir -p "$p/FFmpeg" "$p/dub_sidecar"
    for f in LLPlayer.Avalonia LLPlayer.Avalonia.dll LLPlayer.Avalonia.deps.json LLPlayer.Avalonia.runtimeconfig.json \
        FlyleafLib.dll libSkiaSharp.so libHarfBuzzSharp.so libonnxruntime.so llplayer llplayer.desktop LLPlayer.png \
        LICENSE FFmpeg/LICENSE.txt dub_sidecar/server.py dub_sidecar/pyproject.toml dub_sidecar/uv.lock \
        dub_sidecar/README.md; do
        printf 'x\n' > "$p/$f"
    done
    chmod 755 "$p/LLPlayer.Avalonia" "$p/llplayer"
    for so in "${sonames[@]}"; do
        printf 'x\n' > "$p/FFmpeg/$so.1.100"
        ln -s "$so.1.100" "$p/FFmpeg/$so"
    done
}
check_pkg() { bash "$linux_dir/publish.sh" --check-package "$1"; }

base="$work/pkg-base"
make_package "$base"
expect_ok "valid synthetic package" check_pkg "$base"

mutate() { # name command-string -> fresh copy in $work/m-<name>, prints its path
    local dir="$work/m-$1"
    rm -rf "$dir"
    cp -a "$base" "$dir"
    (cd "$dir" && eval "$2")
    printf '%s\n' "$dir"
}
expect_fail "missing FFmpeg soname" "missing required file FFmpeg/libavcodec.so.62" \
    check_pkg "$(mutate missing-so 'rm FFmpeg/libavcodec.so.62')"
expect_fail "empty LICENSE" "LICENSE must be a non-empty regular file" \
    check_pkg "$(mutate empty-license ': > LICENSE')"
expect_fail "required file links outside" "LICENSE must be a non-empty regular file inside the package" \
    check_pkg "$(mutate outside-license 'rm LICENSE; ln -s /etc/hostname LICENSE')"
expect_fail "launcher not executable" "llplayer must be executable" \
    check_pkg "$(mutate noexec 'chmod 644 llplayer')"
expect_fail "runtime config JSON" "runtime config" \
    check_pkg "$(mutate config 'echo {} > LLPlayer.Config.json')"
expect_fail "crash log" "runtime config" \
    check_pkg "$(mutate crashlog 'echo x > crash.log')"
expect_fail "env file" "runtime config" \
    check_pkg "$(mutate envfile 'echo KEY=1 > .env.local')"
expect_fail "dub venv" "dubbing venv" \
    check_pkg "$(mutate venv 'mkdir -p dub_sidecar/.venv/bin')"
expect_fail "dub engine" "dubbing venv" \
    check_pkg "$(mutate dubengine 'mkdir DubEngine')"
expect_fail "rendered dub" "rendered dubs" \
    check_pkg "$(mutate dub 'echo x > movie.ru.dub.flac')"
expect_fail "voice companion" "rendered dubs" \
    check_pkg "$(mutate voices 'echo {} > movie.ru.voices.json')"
expect_fail "whisper model" "downloaded models" \
    check_pkg "$(mutate ggml 'echo x > ggml-base.bin')"
expect_fail "unexpected native" "unexpected native library outside FFmpeg/: libfoo.so" \
    check_pkg "$(mutate native 'echo x > libfoo.so')"
expect_fail "nested unexpected native" "unexpected native library outside FFmpeg/: runtimes/linux-x64/native/libbar.so.1" \
    check_pkg "$(mutate nested-native 'mkdir -p runtimes/linux-x64/native; echo x > runtimes/linux-x64/native/libbar.so.1')"
expect_fail "wrong FFmpeg major" "unexpected FFmpeg library: FFmpeg/libavcodec.so.61" \
    check_pkg "$(mutate major 'echo x > FFmpeg/libavcodec.so.61')"
expect_fail "Windows x64 payload" "Windows native payload" \
    check_pkg "$(mutate x64 'mkdir x64; echo x > x64/tesseract55.dll')"
expect_fail "Windows runtimes payload" "Windows native payload" \
    check_pkg "$(mutate winrt 'mkdir -p runtimes/win-x64/native; echo x > runtimes/win-x64/native/a.dll')"
expect_fail "escaping symlink" "symlink escaping the package" \
    check_pkg "$(mutate escape 'ln -s /etc evil')"
expect_fail "missing package dir" "package directory not found" check_pkg "$work/does-not-exist"

# ---------------------------------------------------------------- fetch-ffmpeg.sh (offline)
# fake_tarball <name> <setup-command>: builds <name>.tar.xz with top dir "fake/" in its own cache; prints "cache sha".
fake_tarball() {
    local name="$1" setup="$2" src="$work/src-$1" cache="$work/cache-$1"
    mkdir -p "$src/fake/lib" "$src/fake/bin" "$cache"
    (cd "$src/fake" && eval "$setup")
    tar -cJf "$cache/$name.tar.xz" -C "$src" fake
    printf '%s %s\n' "$cache" "$(sha256sum "$cache/$name.tar.xz" | cut -d' ' -f1)"
}
# shellcheck disable=SC2016  # single quotes are intentional: this text is a script run later by bash -c
stub_libs='for so in '"${sonames[*]}"'; do echo x > "lib/$so.1.100"; ln -s "$so.1.100" "lib/$so"; done; echo GPL > LICENSE.txt'
run_fetch() { # name cache sha dest [args...]
    local name="$1" cache="$2" sha="$3" dest="$4"
    shift 4
    LLPLAYER_CACHE_DIR="$cache" LLPLAYER_FFMPEG_URL="https://invalid.example/$name.tar.xz" \
        LLPLAYER_FFMPEG_SHA256="$sha" bash "$linux_dir/fetch-ffmpeg.sh" --dest "$dest" "$@"
}

read -r c s < <(fake_tarball wrongmajor "$stub_libs"'; rm lib/libavcodec.so.62 lib/libavcodec.so.62.1.100; echo x > lib/libavcodec.so.61.1.100; ln -s libavcodec.so.61.1.100 lib/libavcodec.so.61')
expect_fail "fetch: wrong soname major" "does not provide libavcodec.so.62" run_fetch wrongmajor "$c" "$s" "$work/out-a" --no-bin
[[ ! -e "$work/out-a" ]] && ok || bad "fetch: wrong major must not create the destination"

read -r c s < <(fake_tarball extramajor "$stub_libs"'; ln -s libswscale.so.9.1.100 lib/libswscale.so.10')
expect_fail "fetch: extra soname major" "unexpected libswscale.so.10" run_fetch extramajor "$c" "$s" "$work/out-b" --no-bin

read -r c s < <(fake_tarball nolicense "$stub_libs"'; rm LICENSE.txt')
expect_fail "fetch: missing LICENSE.txt" "could not extract" run_fetch nolicense "$c" "$s" "$work/out-c" --no-bin

read -r c s < <(fake_tarball foreign "$stub_libs")
mkdir -p "$work/foreign"
echo keep > "$work/foreign/important.txt"
expect_fail "fetch: foreign destination" "refusing to replace non-empty" run_fetch foreign "$c" "$s" "$work/foreign" --no-bin
[[ "$(cat "$work/foreign/important.txt")" == keep ]] && ok || bad "fetch: foreign destination was modified"
mkdir -p "$work/foreign-bin"
echo keep > "$work/foreign-bin/tool"
expect_fail "fetch: foreign CLI directory" "refusing to write into non-empty" \
    run_fetch foreign "$c" "$s" "$work/out-h" --bin-dir "$work/foreign-bin"
mkdir -p "$work/own-bin/lib"
echo "$s  foreign.tar.xz" > "$work/own-bin/.llplayer-ffmpeg-source"
expect_fail "fetch: CLI lib is a real directory" "is not a symlink" \
    run_fetch foreign "$c" "$s" "$work/out-i" --bin-dir "$work/own-bin"
[[ -d "$work/own-bin/lib" && ! -e "$work/out-h" && ! -e "$work/out-i" ]] && ok || bad "fetch: refused runs must not touch the CLI dir or create the destination"

expect_fail "fetch: sha mismatch triggers download that fails offline" "download failed" \
    run_fetch foreign "$c" "0000000000000000000000000000000000000000000000000000000000000000" "$work/out-d" --no-bin
expect_fail "fetch: malformed pinned sha" "64 hex characters" run_fetch foreign "$c" "xyz" "$work/out-e" --no-bin
expect_fail "fetch: non-https URL" "must be an https:// URL" \
    env LLPLAYER_FFMPEG_URL=http://invalid.example/x.tar.xz bash "$linux_dir/fetch-ffmpeg.sh" --dest "$work/out-f"
expect_fail "fetch: unknown argument" "unknown argument" bash "$linux_dir/fetch-ffmpeg.sh" --bogus

if command -v cc >/dev/null 2>&1 && command -v readelf >/dev/null 2>&1; then
    # Happy path with stub ELF libraries that carry the right SONAMEs and a stub CLI.
    # shellcheck disable=SC2016  # single quotes are intentional: this text is a script run later by bash -c
    elf_setup='printf "int llp_stub;\n" > stub.c
        for so in '"${sonames[*]}"'; do cc -shared -fPIC -Wl,-soname,"$so" -o "lib/$so.1.100" stub.c; ln -s "$so.1.100" "lib/$so"; done
        rm stub.c
        printf "#!/bin/sh\necho \"ffmpeg version stub\"\n" > bin/ffmpeg; cp bin/ffmpeg bin/ffprobe; chmod 755 bin/ffmpeg bin/ffprobe
        echo GPL > LICENSE.txt'
    read -r c s < <(fake_tarball good "$elf_setup")
    expect_ok "fetch: install from pinned cached tarball" run_fetch good "$c" "$s" "$work/good-lib" --bin-dir "$work/good-bin"
    all=1
    for so in "${sonames[@]}"; do [[ -f "$work/good-lib/$so" ]] || all=0; done
    [[ $all -eq 1 && -f "$work/good-lib/LICENSE.txt" && -x "$work/good-bin/bin/ffmpeg" ]] && ok ||
        bad "fetch: installed layout incomplete"
    [[ "$(stat -c %a "$work/good-lib")" == 755 ]] && ok || bad "fetch: destination must be world-readable (755)"
    [[ "$(readlink "$work/good-bin/lib")" == "$work/good-lib" ]] && ok || bad "fetch: CLI lib symlink must point at the destination"
    out="$(run_fetch good "$c" "$s" "$work/good-lib" --bin-dir "$work/good-bin" 2>&1)" &&
        [[ "$out" == *"already installed"* ]] && ok || bad "fetch: second run must be a no-op: $out"
    read -r c2 s2 < <(fake_tarball badsoname "${elf_setup/-Wl,-soname,\"\$so\"/-Wl,-soname,libwrong.so.1}")
    expect_fail "fetch: ELF SONAME mismatch" "has ELF SONAME 'libwrong.so.1'" run_fetch badsoname "$c2" "$s2" "$work/out-g" --no-bin
else
    printf 'SKIP: fetch happy path (needs cc + readelf)\n' >&2
fi

# ---------------------------------------------------------------- make-test-media.sh
expect_fail "media: missing ffmpeg CLI" "ffmpeg CLI not found" \
    bash "$linux_dir/make-test-media.sh" --ffmpeg "$work/no-such-ffmpeg" --out "$work/media"

printf 'scripts/linux self-tests: %d passed, %d failed\n' "$passed" "$failed" >&2
[[ $failed -eq 0 ]]

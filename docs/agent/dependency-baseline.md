# Dependency Baseline

This document freezes current dependency decisions from `main`.

## Project Targets

- `LLPlayer`: `net10.0-windows10.0.18362.0`, `WinExe`, WPF, `win-x64`.
- `FlyleafLib`: `net10.0-windows10.0.18362.0`, WPF and Windows Forms enabled; plus portable `net10.0` for Linux (F-13) without WPF, Windows Forms, or Vortice.
- `WpfColorFontDialog`: `net10.0-windows10.0.18362.0`, WPF.
- `Plugins/YoutubeDL`: `net10.0-windows10.0.18362.0`, `win-x64`.
- `FlyleafLibTests`: xUnit test project targeting `net10.0-windows10.0.18362.0` (on Linux build hosts it also builds and runs `net10.0`).
- `LLPlayer.Avalonia` (F-13): `net10.0`, `linux-x64`, framework-dependent Avalonia app. `LLPlayer.Avalonia.Tests`: `net10.0`.

## NuGet Baseline

Runtime-sensitive package versions are part of the frozen baseline:

| Project | Package | Version |
| --- | --- | --- |
| `FlyleafLib` | `CliWrap` | `3.10.1` |
| `FlyleafLib` | `DeepL.net` | `1.21.0` |
| `FlyleafLib` | `Flyleaf.FFmpeg.Bindings` | `8.0.1` |
| `FlyleafLib` | `Microsoft.ML.OnnxRuntime` | `1.20.1` |
| `FlyleafLib` | `SearchPioneer.Lingua` | `1.0.5` |
| `FlyleafLib` | `TesseractOCR` | `5.5.2` |
| `FlyleafLib` | `UTF.Unknown` | `2.6.0` |
| `FlyleafLib` | `Vortice.D3DCompiler` | `3.7.6-beta` |
| `FlyleafLib` | `Vortice.Direct3D11` | `3.7.6-beta` |
| `FlyleafLib` | `Vortice.DirectComposition` | `3.7.6-beta` |
| `FlyleafLib` | `Vortice.Mathematics` | `1.9.3` |
| `FlyleafLib` | `Vortice.MediaFoundation` | `3.7.6-beta` |
| `FlyleafLib` | `Vortice.XAudio2` | `3.7.6-beta` |
| `FlyleafLib` | `Whisper.net` | `1.9.0` |
| `LLPlayer` | `Flyleaf.FFmpeg.Bindings` | `8.0.1` |
| `LLPlayer` | `LibNMeCab` | `0.10.2` |
| `LLPlayer` | `LibNMeCab.IpaDicBin` | `0.10.0` |
| `LLPlayer` | `MaterialDesignThemes` | `5.3.1` |
| `LLPlayer` | `Microsoft.Data.Sqlite` | `9.0.17` |
| `LLPlayer` | `SQLitePCLRaw.bundle_e_sqlite3` | `3.0.3` |
| `LLPlayer` | `Prism.DryIoc` | `9.0.537` |
| `LLPlayer` | `Squid-Box.SevenZipSharp.Lite` | `1.6.2.24` |
| `LLPlayer` | `Whisper.net.Runtime` | `1.9.0` |
| `LLPlayer` | `Whisper.net.Runtime.Cuda.Windows` | `1.9.0` |
| `LLPlayer` | `Whisper.net.Runtime.NoAvx` | `1.9.0` |
| `LLPlayer` | `Whisper.net.Runtime.OpenVino` | `1.9.0` |
| `LLPlayer` | `Whisper.net.Runtime.Vulkan` | `1.9.0` |
| `FlyleafLibTests` | `AwesomeAssertions` | `9.4.0` |
| `FlyleafLibTests` | `Microsoft.NET.Test.Sdk` | `18.4.0` |
| `FlyleafLibTests` | `xunit.v3` | `3.2.2` |
| `FlyleafLibTests` | `xunit.runner.visualstudio` | `3.1.5` |

## Flyleaf.FFmpeg.Bindings alignment

`LLPlayer` and `FlyleafLib` both reference `Flyleaf.FFmpeg.Bindings` `8.0.1`, matching the tracked native FFmpeg 8.0 DLLs shipped under `FFmpeg/` (`avcodec-62`, `avutil-60`, `avformat-62`, `avfilter-11`, `swscale-9`, `swresample-6`, `avdevice-62`).

## SQLite (Anki .apkg export, F-10)

`LLPlayer` references `Microsoft.Data.Sqlite` to write Anki `.apkg` decks (a SQLite `collection.anki2` zipped with a `media` manifest). The native `e_sqlite3.dll` it depends on is supplied by `SQLitePCLRaw.bundle_e_sqlite3`, which `dotnet publish` copies into the output `runtimes/win-x64/native/` folder alongside the other bundled native assets (no manual packaging step needed). This is security-sensitive: the whole `SQLitePCLRaw 2.1.x` line carries advisory GHSA-2m69-gcr7-jv3q (vulnerable bundled SQLite, no 2.1.x fix), so the bundle is pinned to the patched `3.0.3` (which ships native `e_sqlite3` `3.50.3`). NuGet audit (`-warnaserror` / NU1903) fails the build if a vulnerable SQLitePCLRaw is reintroduced; keep these on a non-vulnerable release rather than downgrading.

Historically `FlyleafLib` referenced `7.1.1` while `LLPlayer` referenced `8.0.1`. That was a benign-but-confusing mismatch: with no central package management, NuGet unified the conflicting references **up** to `8.0.1` for the app output, so the actually-shipped managed binding already matched the 8.0 native DLLs; the heavy FFmpeg interop lives in `FlyleafLib`, while `LLPlayer` only consumes managed `LoadProfile`/`LogLevel` enums (no P/Invoke). Task T-01 aligned `FlyleafLib` **up** to `8.0.1` so the compile-time reference matches the runtime-unified binding and the shipped DLLs (verified: `FlyleafLib` + `LLPlayer` build `-warnaserror` 0/0 against `8.0.1`). Down-aligning to `7.1.1` was rejected: it would have forced the unified runtime binding **down** to `7.1.1` against the 8.0 DLLs, replacing a correct pairing with a real mismatch.

Any future change to FFmpeg binding versions (or the tracked `FFmpeg/` DLLs) requires explicit review and playback/package verification.

## Tracked Native Assets

These tracked files are intentional release/runtime assets:

- `FFmpeg/avcodec-62.dll`
- `FFmpeg/avdevice-62.dll`
- `FFmpeg/avfilter-11.dll`
- `FFmpeg/avformat-62.dll`
- `FFmpeg/avutil-60.dll`
- `FFmpeg/swresample-6.dll`
- `FFmpeg/swscale-9.dll`
- `LLPlayer/lib/7z.dll`
- `LLPlayer/lib/license.7z.txt`
- `LLPlayer/Assets/silero_vad.onnx` (Silero VAD model, MIT, F-19 slice 2)
- `Plugins/YoutubeDL/Libs/yt-dlp.exe_here`

Do not add downloaded `yt-dlp.exe`, Whisper/faster-whisper engines or models, Tesseract data, dubbing runtime data (`DubEngine/`, `dubmodels/`, `*.ru.dub.*`, `*.ru.voices.json`), runtime JSON, crash logs, dumps, recordings, snapshots, publish output, `bin`, or `obj` as tracked files.

## Silero VAD (speech-aware cue snapping, F-19 slice 2)

`FlyleafLib` references `Microsoft.ML.OnnxRuntime` (`1.20.1`, MIT) to run the Silero VAD model on CPU for F-19 slice 2 (snapping ASR re-segmentation cue boundaries onto speech pauses). The native `onnxruntime.dll` it depends on is copied into the `LLPlayer` publish output by `dotnet publish` (win-x64), alongside the other bundled native assets; `.github/actions/build-package/action.yml` positively validates `onnxruntime.dll` is present so a missing native lib fails the release rather than silently shipping a no-op feature.

The VAD C# code is **vendored** (not a NuGet) from `snakers4/silero-vad` (`examples/csharp`, **MIT**, Copyright (c) 2020-present Silero Team) into `FlyleafLib/Vad/` (`SileroVadOnnxModel.cs`, `SileroVadDetector.cs`, `SileroSpeechSegment.cs`), each carrying an attribution header. Local changes: namespace, the NAudio WAV reader replaced by a `float[]` entry point fed from the ASR resampler, and `Dispose()` now actually disposes the ONNX session. Upgrading the vendored files or ONNX Runtime is dependency work (re-verify against the bundled model), not incidental cleanup.

The `silero_vad.onnx` model itself (**MIT**, ~2.2 MB) is a committed tracked asset at `LLPlayer/Assets/silero_vad.onnx`, bundled next to the exe (resolved at runtime as `BaseDirectory\Assets\silero_vad.onnx`). It is small and required at runtime, so unlike the large downloadable Whisper/Tesseract models it is committed and shipped (the same policy as the other `Assets/` files). Everything is fail-soft: a missing model or an ONNX Runtime load failure disables snapping and leaves subtitle timing byte-identical.

## Dubbing Sidecar Lock

`dub_sidecar/pyproject.toml` keeps the broad project requirement (`torch>=2.7.0`) routed to the `pytorch-cu128` index, while committed `dub_sidecar/uv.lock` freezes the reviewed runtime resolution to `torch` `2.11.0+cu128` from `https://download.pytorch.org/whl/cu128`. Changing the resolved torch version or CUDA wheel index is dependency work, not incidental cleanup, and requires `scripts/codex/verify.ps1`, `scripts/codex/check-dub-licenses.ps1`, and relevant dubbing smoke on the target Windows/RTX 5090 environment.

## VC++ Redistributable

Whisper/ASR diagnostics already ask users whether Microsoft Visual C++ Redistributable 2022 or newer is installed. Treat VC++ 2022 Redistributable as a native-runtime troubleshooting prerequisite, not a bundled source artifact. Release/package work must preserve that diagnostic expectation unless a future task explicitly changes the packaging policy.

## Upgrade Rules

- Do not upgrade framework target, runtime identifiers, native bindings, Whisper/Tesseract runtime packages, Vortice packages, or FFmpeg assets as incidental cleanup.
- Dependency upgrades require a focused task, verification with `scripts/codex/verify.ps1`, and relevant manual smoke checks.
- Release packaging remains tied to `.github/actions/build-package/action.yml`.

## Linux Baseline (F-13)

The Linux build is additive; nothing above changes for Windows.

| Project | Package | Version |
| --- | --- | --- |
| `LLPlayer.Avalonia` | `Avalonia` | `12.1.3` |
| `LLPlayer.Avalonia` | `Avalonia.Desktop` | `12.1.3` |
| `LLPlayer.Avalonia` | `Avalonia.Themes.Fluent` | `12.1.3` |
| `LLPlayer.Avalonia` | `Avalonia.Fonts.Inter` | `12.1.3` |
| `LLPlayer.Avalonia` | `CommunityToolkit.Mvvm` | `8.4.2` |
| `LLPlayer.Avalonia` | `Avalonia.BuildServices` | `11.3.2` (`ExcludeAssets="all"`, `PrivateAssets="all"`: only switches off Avalonia's build-time telemetry task) |
| `LLPlayer.Avalonia.Tests` | `Avalonia.Headless.XUnit` | `12.1.3` |
| `LLPlayer.Avalonia.Tests` | `Avalonia.Skia` | `12.1.3` |
| `LLPlayer.Avalonia.Tests` | `Avalonia.BuildServices` | `11.3.2` (`ExcludeAssets="all"`, `PrivateAssets="all"`, telemetry opt-out) |
| `LLPlayer.Avalonia.Tests` | `AwesomeAssertions` | `9.4.0` |
| `LLPlayer.Avalonia.Tests` | `Microsoft.NET.Test.Sdk` | `18.4.0` |
| `LLPlayer.Avalonia.Tests` | `xunit.v3` | `3.2.2` |
| `LLPlayer.Avalonia.Tests` | `xunit.runner.visualstudio` | `3.1.5` |

The portable `net10.0` TFM of `FlyleafLib` adds no package: the OpenAL Soft output binds `libopenal.so.1` at run time
with `NativeLibrary.TryLoad` + `GetExport` (no `DllImport` resolver, so it cannot collide with another resolver in
the assembly), and the software renderer uses the FFmpeg libraries already bound by `Flyleaf.FFmpeg.Bindings`
(`swscale`, `avfilter`: `bwdif`/`yadif`/`estdif`, `zscale`+`tonemap`).

Test infrastructure packages in `LLPlayer.Avalonia.Tests` use the same versions as `FlyleafLibTests`. Versions are
pinned exactly in each `.csproj` (no ranges, no `Directory.Packages.props`); the `.csproj` files are the source of
truth, and adding or upgrading any Linux package is the same focused dependency work as on Windows.

- **UI decision (owner, 2026-09-24):** Avalonia 12.1.3 with the built-in FluentTheme plus our own theme built from
  shadcn/ui design tokens (shadcn/ui is MIT and is used only as a design source: colours, radii, spacing — no code or
  package). ShadUI was rejected as a dependency (0.x, single maintainer), and no other theme package (SukiUI, Semi) is
  used either, so an Avalonia upgrade never waits on a third-party theme; a web UI with real shadcn/ui was rejected because it cannot carry FlyleafLib video frames
  efficiently and would reintroduce the web stack listed under "What Not To Port" in `AGENTS.md`.
- **FFmpeg:** BtbN/FFmpeg-Builds `ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz` (release tag `latest`), verified
  against the release's `checksums.sha256` by `scripts/linux/fetch-ffmpeg.sh`, which also fails unless the sonames
  are exactly `libavcodec.so.62`, `libavformat.so.62`, `libavutil.so.60`, `libswscale.so.9`, `libswresample.so.6`,
  `libavfilter.so.11`, and `libavdevice.so.62` — the same majors as the tracked Windows DLLs, matching
  `Flyleaf.FFmpeg.Bindings` `8.0.1`. The libraries are fetched into the gitignored `FFmpeg/linux-x64/` and are
  never committed (~210 MB). `latest` is a rolling build: the checksum proves integrity, not reproducibility; pin
  `LLPLAYER_FFMPEG_URL` + `LLPLAYER_FFMPEG_SHA256` to a dated BtbN release for a reproducible build. It is a GPL
  build, so the Linux package ships its `LICENSE.txt` as `FFmpeg/LICENSE.txt` plus `FFmpeg/SOURCE.txt` (asset name,
  sha256 from the fetch marker, build-recipe and FFmpeg source URLs); a formal GPL source offer is part of the
  Linux release integration (backlog F-13). Windows packaging copies `FFmpeg/`
  recursively, so do not build a Windows package from a checkout that has `FFmpeg/linux-x64/` (release jobs use
  fresh checkouts).
- **OpenAL Soft:** the system `libopenal.so.1` (Ubuntu `libopenal1`, LGPL-2.1) is a runtime prerequisite and is not
  bundled. Without it, audio falls back to the silent null sink.
- **Bundled NuGet natives in the Linux package:** `libSkiaSharp.so`, `libHarfBuzzSharp.so` (Avalonia rendering) and
  `libonnxruntime.so`, `libonnxruntime_providers_shared.so` (Silero VAD). The Windows-only `x64/`/`x86/` Tesseract
  DLLs that the TesseractOCR build targets copy are removed from the Linux package.
- **Prerequisites for end users:** .NET 10 runtime (Ubuntu: `apt install dotnet-runtime-10.0`) and `libopenal1`.

### Linux package rules

`scripts/linux/publish.sh` publishes `LLPlayer.Avalonia` (`-c Release -r linux-x64 --self-contained false
-warnaserror`) and archives `LLPlayer-<version>-linux-x64.tar.gz` (version from `LLPlayer.Avalonia.csproj`, else
`LLPlayer.csproj`). Mirroring the Windows ship rules, it must:

- positively validate the required contents: the `LLPlayer.Avalonia` apphost, `.dll`, `.deps.json`,
  `.runtimeconfig.json`, `FlyleafLib.dll`, `libSkiaSharp.so`, `libHarfBuzzSharp.so`, all seven FFmpeg sonames and
  `FFmpeg/LICENSE.txt` + `FFmpeg/SOURCE.txt`, the committed `dub_sidecar` source (`server.py`, `pyproject.toml`,
  `uv.lock`, `README.md`), `LICENSE`, `THIRD-PARTY-NOTICES.md` (shadcn/ui MIT, Lucide ISC/MIT, FFmpeg), the `llplayer` launcher, `llplayer.desktop`, and `LLPlayer.png` — each a non-empty file inside the package;
- reject runtime config JSON (`LLPlayer.*.json`), logs, dumps, `.env*`, dubbing runtime data (`DubEngine`,
  `dubmodels`, `*.ru.dub.*`, `*.ru.voices.json`), Python venvs, downloaded models (`ggml-*.bin`, `*.traineddata`),
  user media folders, any `*.so` outside `FFmpeg/` other than the four NuGet natives above, FFmpeg libraries with
  other majors, Windows native payload folders, and symlinks that escape the package.

## Release Packaging Tail

- `.github/actions/build-package/action.yml` is the source of truth for release-only cleanup, `yt-dlp.exe` download, and 7-Zip archive creation.
- Both Stable and Testing release workflows must be dispatched from the trusted default branch with an exact lowercase 40-character commit input equal to the workflow run's `${{ github.sha }}`. They must checkout that same immutable commit, use the pinned `setup-dotnet` v5.4.0 action to install the repository's `10.0.x` SDK channel, and complete the canonical full `scripts/codex/verify.ps1` gate before invoking the shared packaging action. A missing verifier, SHA mismatch, or preflight-order regression fails before publish/archive.
- The runtime cleanup list is intentionally strict. Local `scripts/codex/ship.ps1` should fail if expected cleanup targets disappear instead of silently passing a layout that the GitHub Action would fail.
- The release action must positively validate required publish contents (`LLPlayer.exe`, `lib/7z.dll`, `Assets/silero_vad.onnx`, `onnxruntime.dll`, all copied `FFmpeg/*.dll`, `Plugins/YoutubeDL/YoutubeDL.dll`, `YoutubeDL.pdb`, `Plugins/YoutubeDL/yt-dlp.exe_here`, `Plugins/YoutubeDL/yt-dlp.exe`, and committed `dub_sidecar` source) and recursively reject dubbing runtime/model/output artifacts (`DubEngine`, `dubmodels`, `*.ru.dub.*`, `*.ru.voices.json`).
- Local ship smoke creates the `Plugins/YoutubeDL/yt-dlp.exe_here` placeholder and verifies the release action markers for `yt-dlp.exe` download, positive content checks, recursive dubbing-artifact rejection, and 7-Zip archive command. It does not download `yt-dlp.exe` unless a future release task explicitly requests network packaging.

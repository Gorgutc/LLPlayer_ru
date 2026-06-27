# Dependency Baseline

This document freezes current dependency decisions from `main`.

## Project Targets

- `LLPlayer`: `net10.0-windows10.0.18362.0`, `WinExe`, WPF, `win-x64`.
- `FlyleafLib`: `net10.0-windows10.0.18362.0`, WPF and Windows Forms enabled.
- `WpfColorFontDialog`: `net10.0-windows10.0.18362.0`, WPF.
- `Plugins/YoutubeDL`: `net10.0-windows10.0.18362.0`, `win-x64`.
- `FlyleafLibTests`: xUnit test project targeting `net10.0-windows10.0.18362.0`.

## NuGet Baseline

Runtime-sensitive package versions are part of the frozen baseline:

| Project | Package | Version |
| --- | --- | --- |
| `FlyleafLib` | `CliWrap` | `3.10.1` |
| `FlyleafLib` | `DeepL.net` | `1.21.0` |
| `FlyleafLib` | `Flyleaf.FFmpeg.Bindings` | `8.0.1` |
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
- `Plugins/YoutubeDL/Libs/yt-dlp.exe_here`

Do not add downloaded `yt-dlp.exe`, Whisper/faster-whisper engines or models, Tesseract data, dubbing runtime data (`DubEngine/`, `dubmodels/`, `*.ru.dub.*`), runtime JSON, crash logs, dumps, recordings, snapshots, publish output, `bin`, or `obj` as tracked files.

## VC++ Redistributable

Whisper/ASR diagnostics already ask users whether Microsoft Visual C++ Redistributable 2022 or newer is installed. Treat VC++ 2022 Redistributable as a native-runtime troubleshooting prerequisite, not a bundled source artifact. Release/package work must preserve that diagnostic expectation unless a future task explicitly changes the packaging policy.

## Upgrade Rules

- Do not upgrade framework target, runtime identifiers, native bindings, Whisper/Tesseract runtime packages, Vortice packages, or FFmpeg assets as incidental cleanup.
- Dependency upgrades require a focused task, verification with `scripts/codex/verify.ps1`, and relevant manual smoke checks.
- Release packaging remains tied to `.github/actions/build-package/action.yml`.

## Release Packaging Tail

- `.github/actions/build-package/action.yml` is the source of truth for release-only cleanup, `yt-dlp.exe` download, and 7-Zip archive creation.
- The runtime cleanup list is intentionally strict. Local `scripts/codex/ship.ps1` should fail if expected cleanup targets disappear instead of silently passing a layout that the GitHub Action would fail.
- Local ship smoke creates the `Plugins/YoutubeDL/yt-dlp.exe_here` placeholder and verifies the release action markers for `yt-dlp.exe` download and 7-Zip archive command. It does not download `yt-dlp.exe` unless a future release task explicitly requests network packaging.

# Technical Stack

- Language: C# with nullable enabled where configured.
- Runtime: `.NET 10`, Windows target `net10.0-windows10.0.18362.0` (FlyleafLib also builds portable `net10.0` for Linux).
- UI: WPF, Prism.DryIoc, MaterialDesignThemes.
- Media: FlyleafLib, FFmpeg bindings, DirectX/Vortice, MediaFoundation, XAudio2.
- ASR/OCR: Whisper.net runtimes, faster-whisper integration, TesseractOCR, Microsoft OCR paths.
- Translation: Google/Bing/Azure/DeepL/DeepLX/Ollama/LM Studio/OpenAI-like services.
- Tests: xUnit v3 and AwesomeAssertions.
- Packaging: GitHub Actions on `windows-latest`, .NET 10 SDK, publish profiles, 7-Zip archive.

No Node/web stack is part of the baseline.

## Linux (F-13)

- Runtime/UI: FlyleafLib portable `net10.0` target + `LLPlayer.Avalonia` (Avalonia 12.1.3, built-in FluentTheme,
  Avalonia.Fonts.Inter, own theme from shadcn/ui design tokens, CommunityToolkit.Mvvm 8.4.2); `linux-x64`,
  framework-dependent.
- Media: FFmpeg 8.1 shared libraries (BtbN `gpl-shared` build, fetched and sha256-verified by
  `scripts/linux/fetch-ffmpeg.sh`), software `sws_scale` → BGRA video path, OpenAL Soft (`libopenal.so.1`) audio.
- Tests: `FlyleafLibTests` portable target and `LLPlayer.Avalonia.Tests` (Avalonia.Headless.XUnit 12.1.3);
  FFmpeg/media integration tests are gated on `LLPLAYER_FFMPEG_DIR` / `LLPLAYER_TEST_MEDIA` and skip without them.
- CI: `.github/workflows/build-linux.yml` on `ubuntu-24.04`.

Linux toolchain notes:

- **SDK:** on Ubuntu 24.04, `sudo apt install dotnet-sdk-10.0` installs the Ubuntu-built SDK (10.0.1xx band) from
  `noble-updates`; it builds both FlyleafLib targets and compile-checks the WPF projects because
  `Directory.Build.props` sets `EnableWindowsTargeting` on non-Windows hosts. CI uses `actions/setup-dotnet` 10.0.x.
- **PowerShell:** the `scripts/codex/*.ps1` validators run under PowerShell 7 from `dotnet tool install -g PowerShell`
  (`~/.dotnet/tools/pwsh`); `scripts/linux/verify.sh` finds it there or on `PATH`. `check-environment.ps1` and
  `verify-plugin.ps1` are Windows-only by design.
- **Native runtime:** `libopenal1` (OpenAL Soft) for sound; `xvfb` only for headless app runs. FFmpeg is not taken
  from the distribution (Ubuntu 24.04 ships FFmpeg 6.1, `libavcodec.so.60`): the engine binds to the FFmpeg 8
  sonames (`libavcodec.so.62`, `libavutil.so.60`, ...).
- **Package:** `scripts/linux/publish.sh` → `LLPlayer-<version>-linux-x64.tar.gz` (app, `FFmpeg/`, launcher,
  `.desktop` file, icon, licenses); end users install `dotnet-runtime-10.0` and `libopenal1`.

## Local Development Environment (T-11)

The project **targets** `.NET 10` (`net10.0-windows10.0.18362.0`), and CI/release must build with the **.NET 10.0.x SDK** (see Packaging above). Local developer machines may differ, and that is expected:

- **No .NET 10 SDK is required locally to build.** The maintainer's machine has the .NET 8 and 9 SDKs plus a **.NET 11 preview SDK**, and the .NET 11 preview SDK builds the `net10.0` target fine. `scripts/codex/check-environment.ps1` warns ("`.NET SDK 10.0.x was not found`") when no 10.0.x SDK is present — this warning is non-fatal for local builds; CI is the authority for the pinned 10.0.x build.
- **Sandboxed `dotnet` can fail reading the Windows SDK.** When `dotnet build`/`restore`/`test` runs under a restricted sandbox, MSBuild may fail while reading the Windows SDK under `%LOCALAPPDATA%` / `AppData`. If a build fails for that reason, **request the approved escalation and rerun the exact same command** (this mirrors the note in `AGENTS.md` → Verification Gates). The failure is an environment/permission issue, not a code or dependency problem, so do not change project files in response to it.
- **Other local tooling quirks** (recorded so a fresh session does not rediscover them): use **PowerShell, not Bash**, for `dotnet` (PowerShell has no heredoc; pass multi-line `git commit` text via `git commit -F <file>` because the PS parser splits `-m` on embedded `"`); `dotnet publish` does **not** copy the tracked `FFmpeg/` folder, so a local publish/launch-test must `Copy-Item .\FFmpeg -Destination $publishDir -Recurse`; the Store `python` alias is a stub, so use **`py -3`**.

These are developer-environment notes only; the frozen build target and the pinned CI SDK in `dependency-baseline.md` are unchanged.

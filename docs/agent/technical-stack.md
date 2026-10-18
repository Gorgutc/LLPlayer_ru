# Technical Stack

- Language: C# with nullable enabled where configured.
- Runtime: `.NET 10`, WPF Windows target `net10.0-windows10.0.18362.0`; FlyleafLib portable `net10.0` serves the shared Windows/Linux Avalonia host.
- UI: shipped WPF/Prism.DryIoc/MaterialDesignThemes and the additive shared Avalonia 12.1.3 UI described below.
- Media: FlyleafLib, FFmpeg bindings, DirectX/Vortice, MediaFoundation, XAudio2.
- ASR/OCR: Whisper.net runtimes, faster-whisper integration, TesseractOCR, Microsoft OCR paths.
- Translation: Google/Bing/Azure/DeepL/DeepLX/Ollama/LM Studio/OpenAI-like services.
- Tests: xUnit v3 and AwesomeAssertions.
- Packaging: GitHub Actions on `windows-latest`, .NET 10 SDK, publish profiles, 7-Zip archive.

No Node/web stack is part of the baseline.

## Shared Windows/Linux Avalonia (F-13)

- One `LLPlayer.Avalonia` Views/ViewModels/Themes tree and `LLPlayer.Core` serves `win-x64;linux-x64`. Both host
  builds use FlyleafLib portable `net10.0`; the WPF-specific Windows engine target does not supply portable seams.
- Native Windows: existing tracked FFmpeg DLLs, software BGRA video, app-local OpenAL Soft 1.23.1 from pinned
  `Silk.NET.OpenAL.Soft.Native`. The framework-dependent package requires .NET 10 runtime and Microsoft Visual C++
  x64 Redistributable 2022 or newer. Windows ASR/OCR and GPU remain separately evidenced slices.
- Windows gate: `scripts/windows/verify-avalonia.ps1`; package contract tests: `test-package-avalonia.ps1` in the
  same folder; publish: `scripts/windows/publish-avalonia.ps1` produces
  `LLPlayer-Avalonia-<version>-win-x64.zip` plus checksum. Extract and launch `LLPlayer.Avalonia.exe`.
- The additive Windows Avalonia job in `build.yml` and existing Linux `build-linux.yml` both check shared changes.
  WPF `scripts/codex/verify.ps1` and its separate `LLPlayer.exe`/release packaging remain supported. Native desktop
  and model acceptance are separate from managed/headless/package evidence; current receipts are in
  `f13-status-2026-10-18.md`.

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
- **Owner's local refresh workflow (2026-10-03):** after each completed and reviewed Linux slice, publish and verify
  a new immutable installed package under `/models/codex-builds/LLPlayer_ru/installed`, then atomically switch
  `/models/codex-builds/LLPlayer_ru/installed/current` to that package. Both maintained launchers—the existing
  `~/.local/share/applications/llplayer.desktop` and the original
  `~/Загрузки/LLPlayer-linux-x64/LLPlayer-0.3.62-linux-x64/llplayer.desktop`—must resolve `Exec` and `Icon` through
  `current/llplayer.sh %F` and `current/llplayer.png`, respectively. Verify both launcher resolutions and record
  them in the refresh receipt; preserve older packages and user XDG data. Reopen the application after switching
  `current`. This is local owner practice, not a CI or release requirement; it does not add a watcher or
  auto-download. Treat deployment as complete only when its verification receipt exists; a successful publish alone
  does not establish installation or launcher readiness.

### Owner storage layout (verified 2026-10-03)

- Verified relocation placed repository artifacts at `/models/codex-builds/LLPlayer_ru/artifacts` and eight projects'
  `bin`/`obj` directories under `/models/codex-builds/LLPlayer_ru/build/<project>/{bin,obj}`; 17 repository-local
  symlink redirects were read back. Receipt: `/models/codex-builds/LLPlayer_ru/storage-migration-2026-10-03/receipt.json`.
- Future package outputs, evidence, and logs use the artifact path; future desktop-installed versions use
  `/models/codex-builds/LLPlayer_ru/installed`. Keep future model downloads under `/models` as needed; existing model
  locations were not moved.
- For future heavy commands, use process-scoped `TMPDIR=/models/codex-builds/LLPlayer_ru/tmp` or explicit publish
  staging under artifacts. This is local-machine guidance, not a global environment or CI default.
- Tracked source, global caches, XDG data, and currently installed versions were preserved. Exact redirect-ignore
  entries were added to local `.git/info/exclude` with a backup. Measured root space reclaimed during migration was
  6.92 GiB; check `df` on `/` and `/models` before future heavy builds. Avoid `git clean`: it can remove the redirect
  symlinks, causing later builds to write under the repository root.

## Local Development Environment (T-11)

The project **targets** `.NET 10` (`net10.0-windows10.0.18362.0`), and CI/release must build with the **.NET 10.0.x SDK** (see Packaging above). Local developer machines may differ, and that is expected:

- **No .NET 10 SDK is required locally to build.** The maintainer's machine has the .NET 8 and 9 SDKs plus a **.NET 11 preview SDK**, and the .NET 11 preview SDK builds the `net10.0` target fine. `scripts/codex/check-environment.ps1` warns ("`.NET SDK 10.0.x was not found`") when no 10.0.x SDK is present — this warning is non-fatal for local builds; CI is the authority for the pinned 10.0.x build.
- **Sandboxed `dotnet` can fail reading the Windows SDK.** When `dotnet build`/`restore`/`test` runs under a restricted sandbox, MSBuild may fail while reading the Windows SDK under `%LOCALAPPDATA%` / `AppData`. If a build fails for that reason, **request the approved escalation and rerun the exact same command** (this mirrors the note in `AGENTS.md` → Verification Gates). The failure is an environment/permission issue, not a code or dependency problem, so do not change project files in response to it.
- **Other local tooling quirks** (recorded so a fresh session does not rediscover them): use **PowerShell, not Bash**, for `dotnet` (PowerShell has no heredoc; pass multi-line `git commit` text via `git commit -F <file>` because the PS parser splits `-m` on embedded `"`); `dotnet publish` does **not** copy the tracked `FFmpeg/` folder, so a local publish/launch-test must `Copy-Item .\FFmpeg -Destination $publishDir -Recurse`; the Store `python` alias is a stub, so use **`py -3`**.

These are developer-environment notes only; the frozen build target and the pinned CI SDK in `dependency-baseline.md` are unchanged.

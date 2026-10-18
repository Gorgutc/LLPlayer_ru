# Run Instructions

The supported Windows WPF product is `LLPlayer` targeting `.NET 10` and `win-x64`. F-13 adds the shared Avalonia
app `LLPlayer.Avalonia`, targeting portable `net10.0` for both `win-x64` and `linux-x64`. One set of Views, ViewModels,
Themes and shared Core serves both OSes; use the matching commands below. WPF remains supported until explicit
replacement acceptance. Native desktop/model results are recorded separately in
[current F-13 status](docs/agent/f13-status-2026-10-18.md).

## Baseline

```powershell
dotnet restore -warnaserror
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore -warnaserror .\FlyleafLibTests
```

## Codex Gates

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\ship.ps1
```

`ship.ps1` performs a publish smoke into a temporary directory. Release packaging remains defined by `.github/actions/build-package/action.yml`.

Before final handoff, run spawned `/review` with at least `verification_reviewer`. If no subagent spawn tool is available, report that and do not claim `/review` has been satisfied.

## Windows Avalonia

Use the .NET 10 SDK to build. The framework-dependent package needs the .NET 10 runtime and Microsoft Visual C++
x64 Redistributable 2022 or newer. WSL is not needed to run the native Windows app.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\verify-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-package-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\publish-avalonia.ps1
```

Extract `artifacts/windows-avalonia/LLPlayer-Avalonia-<version>-win-x64.zip` and launch `LLPlayer.Avalonia.exe` from
the extracted folder, preserving `FFmpeg/` and native dependencies. The existing `LLPlayer.exe` is the WPF app.
The Avalonia ZIP carries app-local OpenAL Soft 1.23.1 and the existing tracked FFmpeg DLLs; Windows ASR/OCR and
GPU acceptance remain separate. The verification script provisions a pinned, SHA-256-verified test FFmpeg CLI
when `LLPLAYER_FFMPEG_CLI` is unset; it is never shipped. For development:

```powershell
dotnet build LLPlayer.Avalonia -warnaserror
dotnet LLPlayer.Avalonia/bin/Debug/net10.0/LLPlayer.Avalonia.dll --ffmpeg-dir ./FFmpeg video.mp4
```

Shared UI/Core/portable changes require both Windows Avalonia and Linux gates, plus WPF compatibility checks.
The separate Windows Avalonia job in `build.yml` verifies and uploads its package; Linux uses `build-linux.yml`.

## Linux

Prerequisites (Ubuntu 24.04, where `dotnet-sdk-10.0` comes from `noble-updates`; other distributions need the same pieces):

```bash
sudo apt install dotnet-sdk-10.0 libopenal1 curl xz-utils   # SDK from the Ubuntu archive; end users need only dotnet-runtime-10.0 + libopenal1
dotnet tool install -g PowerShell                           # pwsh for the scripts/codex validators (optional locally, required in CI)
sudo apt install xvfb                                       # optional: headless runs of the app
```

Fetch FFmpeg, verify, run, and package:

```bash
scripts/linux/fetch-ffmpeg.sh        # FFmpeg 8.1 shared libs -> FFmpeg/linux-x64, CLI -> ~/.cache/llplayer/ffmpeg-bin
scripts/linux/make-test-media.sh     # optional: test clip + srt -> ~/.cache/llplayer/media (verify.sh does this itself)
scripts/linux/verify.sh              # full Linux gate; --fast skips the Windows WPF/plugin compile check
LLPLAYER_FFMPEG_DIR="$PWD/FFmpeg/linux-x64" dotnet run --project LLPlayer.Avalonia -- ~/.cache/llplayer/media/test-720p.mp4
scripts/linux/publish.sh             # artifacts/linux/LLPlayer-<version>-linux-x64.tar.gz (+ .sha256)
```

Environment variables: `LLPLAYER_FFMPEG_DIR` (FFmpeg library folder; the packaged app defaults to its own `FFmpeg/`),
`LLPLAYER_TEST_MEDIA` (clip for the env-gated integration tests, which skip when it is unset),
`LLPLAYER_AUDIO_BACKEND` (`null`, `openal`, or `auto`; `verify.sh` forces `null`), `LLPLAYER_CONFIG_DIR`
(overrides the XDG config folder `~/.config/LLPlayer`), `LLPLAYER_LOG_LEVEL` (engine log level, default `Warn`;
the log is `$XDG_STATE_HOME/LLPlayer/flyleaf.log`), `LLPLAYER_MANAGED_DIALOGS=1` (Avalonia's built-in file chooser
when neither xdg-desktop-portal nor GTK 3 is available), and `LLPLAYER_FFMPEG_URL` / `LLPLAYER_FFMPEG_SHA256`
(pin a specific FFmpeg build for reproducibility; the default BtbN `latest` asset is a rolling build).

Install the unpacked package for the current user (the `.desktop` file expects `llplayer` on `PATH`):

```bash
mkdir -p ~/.local/opt ~/.local/bin
tar -xzf LLPlayer-<version>-linux-x64.tar.gz -C ~/.local/opt
ln -sf ~/.local/opt/LLPlayer-<version>-linux-x64/llplayer ~/.local/bin/llplayer
install -Dm644 ~/.local/opt/LLPlayer-<version>-linux-x64/llplayer.desktop ~/.local/share/applications/llplayer.desktop
install -Dm644 ~/.local/opt/LLPlayer-<version>-linux-x64/LLPlayer.png ~/.local/share/icons/hicolor/256x256/apps/llplayer.png
```

The Windows gates above remain the authority for the WPF product; the Linux scripts add to them and never replace them.

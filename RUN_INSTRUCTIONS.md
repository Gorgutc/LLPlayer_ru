# Run Instructions

The Windows product is the WPF app `LLPlayer` targeting `.NET 10` and `win-x64`. The Linux build (F-13, in progress)
is the Avalonia app `LLPlayer.Avalonia` targeting `net10.0` and `linux-x64`; see [Linux](#linux) below.

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

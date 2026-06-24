---
name: llplayer-runtime-assets
description: Use when touching LLPlayer_ru native binaries, models, plugin assets, or runtime dependency packaging.
---

# LLPlayer Runtime Assets

Runtime assets are part of app behavior.

## Tracked Assets

- `FFmpeg/*.dll`
- `LLPlayer/lib/7z.dll`
- `LLPlayer/lib/license.7z.txt`
- Placeholder `Plugins/YoutubeDL/Libs/yt-dlp.exe_here`

## Do Not Commit

Downloaded `yt-dlp.exe`, Whisper models, faster-whisper engines, Tesseract data, dubbing engine venvs (`DubEngine/`), dubbing model weights (`dubmodels/`), rendered dub tracks (`*.ru.dub.*`), logs, dumps, local runtime configs, and publish output.

`dub_sidecar/` is committed GPLv3 source; its runtime venv/model/output files are not.

Check `DO_NOT_PUSH.md` before adding any binary.

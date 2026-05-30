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

Downloaded `yt-dlp.exe`, Whisper models, faster-whisper engines, Tesseract data, logs, dumps, local runtime configs, and publish output.

Check `DO_NOT_PUSH.md` before adding any binary.

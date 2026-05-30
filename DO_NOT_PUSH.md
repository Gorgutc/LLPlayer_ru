# Do Not Push

Do not commit these files unless a user explicitly requests and reviews the change:

- `bin/`, `obj/`, `TestResults/`, coverage output, publish folders, and temporary package directories.
- Downloaded `yt-dlp.exe`, Whisper models, faster-whisper engines, Tesseract data, OCR/ASR caches.
- Local media output folders such as `Recordings/` and `Snapshots/`.
- Runtime config and local state: `LLPlayer.Config.json`, `LLPlayer.Engine.json`, `LLPlayer.PlayerConfig.json`, `crash.log`, logs, dumps.
- Secrets, API keys, translator credentials, `.env*`, local Codex memories, local machine paths.
- Screenshots, videos, benchmark artifacts, or generated reports unless the task explicitly asks for evidence artifacts.

Allowed tracked native assets:

- `FFmpeg/*.dll`
- `LLPlayer/lib/7z.dll`
- `LLPlayer/lib/license.7z.txt`
- `Plugins/YoutubeDL/Libs/yt-dlp.exe_here`

Treat any new binary as suspicious until its role in the Windows release package is documented.

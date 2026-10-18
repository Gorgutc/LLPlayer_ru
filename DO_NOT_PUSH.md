# Do Not Push

Do not commit these files unless a user explicitly requests and reviews the change:

- `bin/`, `obj/`, `TestResults/`, coverage output, publish folders, and temporary package directories.
- Downloaded `yt-dlp.exe`, Whisper models, faster-whisper engines, Tesseract data, OCR/ASR caches.
- AI dubbing runtime data: the dub Python venv (`DubEngine/`), downloaded TTS model weights (`dubmodels/`), rendered dub tracks (`*.ru.dub.*`), and per-line voice companion files (`*.ru.voices.json`). NOTE: `dub_sidecar/` (server.py, pyproject.toml, uv.lock) IS committed GPLv3 source.
- Local media output folders such as `Recordings/` and `Snapshots/`.
- Runtime config and local state: `LLPlayer.Config.json`, `LLPlayer.Engine.json`, `LLPlayer.PlayerConfig.json`, `LLPlayer.WordList.json`, `LLPlayer.Avalonia.json`, `LLPlayer.Recognition.json`, `LLPlayer.Translation.json`, `crash.log`, logs, dumps. Keep leading-dot atomic-write temps such as `.LLPlayer.Recognition.json.<guid>.tmp` and `.LLPlayer.Translation.json.<guid>.tmp` out of source control too.
- Secrets, API keys, translator credentials, `.env*`, local Codex memories, local machine paths.
- Screenshots, videos, benchmark artifacts, or generated reports unless the task explicitly asks for evidence artifacts.
- Linux (F-13): the fetched FFmpeg shared libraries (`FFmpeg/linux-x64/`, any `*.so`/`*.so.*`), the downloaded FFmpeg
  tarball/CLI, generated test media, Linux packages (`LLPlayer-*-linux-x64.tar.gz`, `artifacts/`), and Linux smoke
  evidence (keep it under `~/.cache/llplayer/evidence/`).
- Windows Avalonia (F-13): downloaded test FFmpeg CLI archives/executables, generated media, restored native
  payloads such as `soft_oal.dll`, package staging, ZIP/checksum output and smoke evidence stay under ignored
  `artifacts/` or caches. Native binaries come from pinned restore/explicit packaging; commit source and legal
  texts rather than copied build outputs.

Allowed tracked native assets:

- `FFmpeg/*.dll`
- `LLPlayer/lib/7z.dll`
- `LLPlayer/lib/license.7z.txt`
- `LLPlayer/Assets/silero_vad.onnx`
- `Plugins/YoutubeDL/Libs/yt-dlp.exe_here`

Treat any new binary as suspicious until its role is documented in the WPF release package,
`scripts/windows/publish-avalonia.ps1` or `scripts/linux/publish.sh`, and `docs/agent/dependency-baseline.md`.

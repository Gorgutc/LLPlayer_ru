# Frozen Decisions

These are current decisions, not universal preferences.

- LLPlayer is Windows-only WPF targeting `.NET 10`.
- `LLPlayer`, `FlyleafLib`, `WpfColorFontDialog`, `FlyleafLibTests`, and `Plugins/YoutubeDL` remain separate projects.
- Release packaging is controlled by `.github/actions/build-package/action.yml`.
- `FFmpeg/*.dll` and `LLPlayer/lib/7z.dll` are tracked required native assets.
- `yt-dlp.exe` is downloaded for release packaging and is not a normal source artifact.
- No web/Node quality stack is part of baseline verification.
- No `global.json` is added in this first Codex infrastructure pass; environment checks report the .NET 10 SDK requirement.

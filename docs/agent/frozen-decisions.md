# Frozen Decisions

These are current decisions, not universal preferences.

- LLPlayer is Windows-only WPF targeting `.NET 10`.
- `LLPlayer`, `FlyleafLib`, `WpfColorFontDialog`, `FlyleafLibTests`, and `Plugins/YoutubeDL` remain separate projects.
- Product positioning is a specialized media player for language learning, not a general VLC/mpv replacement.
- Main app design is media-first: video surface, overlay controls, optional subtitle sidebar, dual subtitles, and settings/cheat-sheet workflows remain central.
- User-facing behavior is frozen in `docs/agent/product-behavior-contract.md`.
- WPF visual/layout behavior is frozen in `docs/agent/wpf-design-contract.md`.
- Media runtime flow is frozen in `docs/agent/media-runtime-contract.md`.
- Config persistence and local data rules are frozen in `docs/agent/config-data-contract.md`.
- Dependency and native runtime baseline are frozen in `docs/agent/dependency-baseline.md`.
- Manual verification expectations are frozen in `docs/agent/manual-smoke-matrix.md`.
- Required review ownership is frozen in `docs/agent/subagent-review-matrix.md`.
- Release packaging is controlled by `.github/actions/build-package/action.yml`.
- `FFmpeg/*.dll` and `LLPlayer/lib/7z.dll` are tracked required native assets.
- `yt-dlp.exe` is downloaded for release packaging and is not a normal source artifact.
- No web/Node quality stack is part of baseline verification.
- No `global.json` is added in this first Codex infrastructure pass; environment checks report the .NET 10 SDK requirement.

Do not change these decisions incidentally. If a task requires one of them to change, say that explicitly in the plan, update the matching contract, and run the relevant verification and smoke checks.

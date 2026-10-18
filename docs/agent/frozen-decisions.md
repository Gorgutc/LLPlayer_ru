# Frozen Decisions

These are current decisions, not universal preferences.

- The shipped Windows product is the WPF app `LLPlayer` targeting `.NET 10`; its build and behaviour do not change for the Linux port.
- Linux support (F-13, owner decision 2026-09-24) is a separate Avalonia 12.1.3 app, `LLPlayer.Avalonia` (built-in FluentTheme + own theme from shadcn/ui tokens; no ShadUI/SukiUI/Semi packages, no web UI), on FlyleafLib's portable `net10.0` target. One repository and one branch serve both platforms.
- `LLPlayer`, `FlyleafLib`, `WpfColorFontDialog`, `FlyleafLibTests`, and `Plugins/YoutubeDL` remain separate projects; `LLPlayer.Avalonia` and `LLPlayer.Avalonia.Tests` are added for Linux.
- Product positioning is a specialized media player for language learning, not a general VLC/mpv replacement.
- Main app design is media-first: video surface, overlay controls, optional subtitle sidebar, dual subtitles, and settings/cheat-sheet workflows remain central.
- User-facing behavior is frozen in `docs/agent/product-behavior-contract.md`.
- WPF visual/layout behavior is frozen in `docs/agent/wpf-design-contract.md`.
- Media runtime flow is frozen in `docs/agent/media-runtime-contract.md`.
- Config persistence and local data rules are frozen in `docs/agent/config-data-contract.md`.
- Dependency and native runtime baseline are frozen in `docs/agent/dependency-baseline.md`.
- Manual verification expectations are frozen in `docs/agent/manual-smoke-matrix.md`.
- Required review ownership is frozen in `docs/agent/subagent-review-matrix.md`.
- AI dubbing feature boundaries are frozen in `docs/agent/dubbing-contract.md` (additive, opt-in, local-first; grows as phases ship).
- Release packaging is controlled by `.github/actions/build-package/action.yml`. The Linux package is built by `scripts/linux/publish.sh` and is not yet part of the release workflows.
- `FFmpeg/*.dll` and `LLPlayer/lib/7z.dll` are tracked required native assets. The Linux FFmpeg shared libraries are fetched by `scripts/linux/fetch-ffmpeg.sh` and never tracked.
- `yt-dlp.exe` is downloaded for release packaging and is not a normal source artifact.
- No web/Node quality stack is part of baseline verification.
- No `global.json` is added in this first Codex infrastructure pass; environment checks report the .NET 10 SDK requirement.

Do not change these decisions incidentally. If a task requires one of them to change, say that explicitly in the plan, update the matching contract, and run the relevant verification and smoke checks.

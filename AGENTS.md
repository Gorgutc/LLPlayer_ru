# LLPlayer Agent Instructions

This file is the source of truth for Codex work in this repository.

## Authority Order

1. Direct user instructions in the current thread.
2. This `AGENTS.md`.
3. Repo-local LLPlayer Codex skills and docs.
4. General Codex/system guidance.

If `CLAUDE.md`, `GEMINI.md`, or other legacy files disagree with this file, follow this file.

## Project Snapshot

LLPlayer is a C#/.NET 10 media player for language learning. The shipped Windows front-end is **WPF**;
the in-progress F-13 front-end is **Avalonia**, with one UI and shared Core for native Windows and Linux.
The Windows Avalonia software-playback build/package baseline is implemented; native desktop acceptance and
full F-13 parity are separate gates. WPF remains supported until explicit replacement acceptance. The repository contains:

- `LLPlayer/`: WPF `WinExe`, Prism/DryIoc, MaterialDesignThemes, app configuration, views, view models, controls, and dialogs.
- `LLPlayer.Core/`: shared app library for the model catalog, recognition/translation settings and actions, word
  snapshots/library, and Anki/APKG export. Full app-config, service, and view-model extraction remains incremental.
- `FlyleafLib/`: media engine library based on FFmpeg, DirectX/Vortice, MediaFoundation, XAudio2, subtitles, translation, ASR, OCR, and plugins.
  It multi-targets `net10.0-windows10.0.18362.0` (WPF Windows) and portable `net10.0` (Windows/Linux Avalonia); the
  portable-only seams (software renderer, `IVideoSurface`, `IAudioSink`/`IAudioBackend`, `IUIDispatcher`, `IHostServices`,
  WPF type stand-ins) live in `FlyleafLib/Platform/Portable/` and are not compiled into the Windows target.
- `LLPlayer.Avalonia/`: shared Windows/Linux desktop app (Avalonia 12.1.3 + built-in FluentTheme + our own theme built from shadcn/ui
  design tokens, CommunityToolkit.Mvvm) on FlyleafLib's `net10.0` target. `LLPlayer.Avalonia.Tests/`: headless UI tests.
- `Plugins/YoutubeDL/`: .NET plugin that integrates `yt-dlp.exe`.
- `WpfColorFontDialog/`: WPF color/font dialog dependency.
- `FlyleafLibTests/`: xUnit v3 tests (Windows target on Windows; the portable `net10.0` target on Linux).
- `FFmpeg/`, `LLPlayer/lib/7z.dll`, and `LLPlayer/Assets/silero_vad.onnx`: tracked native/runtime assets required by packaging.
  The Linux FFmpeg shared libraries are fetched into the gitignored `FFmpeg/linux-x64/` by `scripts/linux/fetch-ffmpeg.sh`.

The WPF application targets `net10.0-windows10.0.18362.0`, `win-x64`, and publishes as a framework-dependent single-file Windows exe.
Avalonia targets `net10.0`, `win-x64;linux-x64`, framework-dependent, using FlyleafLib's portable engine surface on both OSes.
Windows packaging produces `LLPlayer-Avalonia-<version>-win-x64.zip`; Linux produces `LLPlayer-<version>-linux-x64.tar.gz`.
Keep one set of Avalonia views, view models, themes and shared Core for both platforms. Limit OS-specific code to
native loading, paths and host adapters. Windows ASR/OCR and GPU support require separately evidenced slices;
shipping their native files does not establish acceptance. See `docs/agent/f13-status-2026-10-18.md` for current
verification and historical provenance.
Do not assume this is a web, Node, React, or Playwright project.

This is a fork of upstream `umlx5h/LLPlayer`; the `_ru` suffix denotes the Russified agent/automation infrastructure layer (this `AGENTS.md`, `docs/agent/`, `scripts/codex/`, `Plugins/llplayer-codex/`, Russian commit messages), **not** a Russian-localized build — the player UI/code tracks upstream and is not localized. See `docs/agent/architecture.md` ("Fork Relationship") and the local developer-environment notes in `docs/agent/technical-stack.md` ("Local Development Environment").

## Frozen Product Contracts

Before changing product behavior, inspect the matching frozen contract:

- `docs/agent/product-behavior-contract.md`: user-facing functions and feature boundaries.
- `docs/agent/wpf-design-contract.md`: WPF layout, dialogs, subtitles UI, MaterialDesign usage, and shortcut discoverability.
- `docs/agent/media-runtime-contract.md`: Flyleaf engine, FFmpeg, player, subtitles, ASR/OCR, translation, plugins, threading, and rendering boundaries.
- `docs/agent/config-data-contract.md`: config persistence, defaults, key bindings, user data, local files, and secrets.
- `docs/agent/dependency-baseline.md`: package/native/runtime dependency baseline and upgrade rules.
- `docs/agent/manual-smoke-matrix.md`: manual checks for behavior that unit tests do not cover.
- `docs/agent/subagent-review-matrix.md`: path scopes mapped to required review agents.
- `docs/agent/dubbing-contract.md`: AI dubbing (TTS / voice synthesis) feature boundaries — additive, opt-in, local-first; spec + roadmap under `docs/agent/dubbing/`.

These contracts preserve shipped WPF behavior and document additive F-13 boundaries. Historical receipts do not
establish acceptance of the current imported tree. Change product decisions only when the user explicitly asks.

## Required Workflow

- Work on a `codex/*` branch unless the user explicitly asks otherwise.
- Use explicit spawned subagents for meaningful reviews, audits, or parallel sidecar analysis. Do not simulate agents inline.
- If a subagent spawn tool is unavailable, notify the user and do not claim `/review` has been satisfied.
- Keep application-code changes separate from agent/tooling changes. Limit behavior changes to the owner-authorized task scope.
- Use existing C#/.NET/WPF and Avalonia patterns and keep generated infrastructure small and readable.
- Make product changes as narrowly as possible. Preserve unrelated frozen contracts unless the user explicitly requests a broader redesign.
- Ask or notify the user when requirements are unclear or when an assumption could change the delivered behavior.
- Always run `/review` before final handoff. In this environment, that means spawn a review subagent and address Critical/Important findings.

## Verification Gates

Fast infrastructure gate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
```

Full build/test gate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
```

Ship gate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\ship.ps1
```

The current baseline commands are:

```powershell
dotnet restore -warnaserror
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore -warnaserror .\FlyleafLibTests
```

Coverage is risk-based: behavior changes and bug fixes need a deterministic, non-vacuous regression test when a
safe seam exists, with intentional RED evidence where applicable. If WPF, native, GPU, network, or timing boundaries
make that unsafe, document the reason and the exact manual or integration smoke instead. Do not use a global coverage
percentage or a hard-coded passing-test total as a quality gate; the full unfiltered suite remains mandatory.

Windows Avalonia gates add to the WPF gate above:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\verify-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-package-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\publish-avalonia.ps1
```

The verification script runs portable engine/Core/UI builds and both portable test suites with native Windows
FFmpeg, a generated media/subtitle fixture and OpenAL checks. The fixture CLI is downloaded from a pinned,
SHA-256-verified source when no override is provided; it is a test tool and is never shipped. The separate
`LLPlayer Windows Avalonia Build & Test` CI job runs verification, package-contract tests, publish and artifact upload.
Shared UI/Core/portable changes require Windows Avalonia and Linux gates as well as WPF compatibility checks.
Headless/native-library checks do not substitute for desktop and real-model acceptance.

Linux gates (F-13; they add to the Windows gates above, which stay unchanged and remain required for the WPF product):

```bash
scripts/linux/fetch-ffmpeg.sh   # FFmpeg 8.1 shared libs -> FFmpeg/linux-x64 (gitignored), sha256-verified
scripts/linux/verify.sh         # full Linux gate; --fast = validators + portable builds + tests
scripts/linux/publish.sh        # LLPlayer-<version>-linux-x64.tar.gz with positive/negative content validation
```

`verify.sh` runs restore, the Windows WPF app/plugin compile check (via `EnableWindowsTargeting`), the portable
FlyleafLib/Avalonia builds, the Linux test suites, and the platform-neutral `scripts/codex` validators. On Linux, agents
run those validators with PowerShell 7 installed by `dotnet tool install -g PowerShell`; `check-environment.ps1` and
`verify-plugin.ps1` are Windows-only (Windows OS check, NTFS junction fixture) and are skipped there. CI runs the same
scripts in `.github/workflows/build-linux.yml` (job `LLPlayer Linux Build & Test`).

On this machine, sandboxed `dotnet` can fail when MSBuild reads the Windows SDK under AppData. If that happens, request the approved escalation and rerun the same command.

## What Not To Port

Do not copy PL_RU/codex web gates as-is. The following are not LLPlayer quality gates unless a future task explicitly introduces a web surface:

- `package.json`, `pnpm`, `npm`, Next.js, React, TypeScript-only gates.
- Playwright browser smoke, Lighthouse, pa11y, HTMLHint, Stylelint, ESLint.
- Knip, dependency-cruiser, web visual regression, browser accessibility gates.
- Rules about Tailwind, CSS-in-JS, localStorage, Blueprint imports, or web preview contracts.

## Shipping Rules

- Keep `.github/actions/build-package/action.yml` as the source of truth for release packaging.
- Windows Avalonia is additive: `scripts/windows/publish-avalonia.ps1` produces a separate ZIP, validates its
  allowlisted native contents and notices, and leaves the WPF release action unchanged. Pin app-local OpenAL Soft
  through `Silk.NET.OpenAL.Soft.Native` 1.23.1; require the x64 Visual C++ 2022 redistributable and .NET 10 runtime.
- Preserve the separate app publish and `Plugins/YoutubeDL` publish flow.
- Keep tracked native/runtime assets intentional: `FFmpeg/*.dll`, `LLPlayer/lib/7z.dll`, and `LLPlayer/Assets/silero_vad.onnx`.
- Linux: never commit the fetched FFmpeg `*.so*` libraries or the Linux package; `scripts/linux/publish.sh` (CI:
  `.github/workflows/build-linux.yml`) builds the tar.gz and does not change the Windows release packaging.
- Linux: the published tar.gz must ship `llplayer.png`, `llplayer.sh` and `llplayer.desktop` so the package is icon-launchable on unpack (enforced by `scripts/linux/publish.sh` content validation).
- Do not commit publish output, downloaded `yt-dlp.exe`, Whisper/Tesseract models, logs, dumps, local runtime config JSON, secrets, or Codex memories.

## GitHub Flow

- Prefer draft PRs for large Codex infrastructure changes.
- Before pushing, run the full build/test gate and note any unverified ship-only checks.
- If a GitHub Actions check fails, inspect the failing check/log before changing code.

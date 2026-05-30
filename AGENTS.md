# LLPlayer Agent Instructions

This file is the source of truth for Codex work in this repository.

## Authority Order

1. Direct user instructions in the current thread.
2. This `AGENTS.md`.
3. Repo-local LLPlayer Codex skills and docs.
4. General Codex/system guidance.

If `CLAUDE.md`, `GEMINI.md`, or other legacy files disagree with this file, follow this file.

## Project Snapshot

LLPlayer is a Windows-only C#/.NET 10 WPF media player for language learning. The repository contains:

- `LLPlayer/`: WPF `WinExe`, Prism/DryIoc, MaterialDesignThemes, app configuration, views, view models, controls, and dialogs.
- `FlyleafLib/`: media engine library based on FFmpeg, DirectX/Vortice, MediaFoundation, XAudio2, subtitles, translation, ASR, OCR, and plugins.
- `Plugins/YoutubeDL/`: .NET plugin that integrates `yt-dlp.exe`.
- `WpfColorFontDialog/`: WPF color/font dialog dependency.
- `FlyleafLibTests/`: xUnit v3 tests.
- `FFmpeg/` and `LLPlayer/lib/7z.dll`: tracked native runtime assets required by packaging.

The application targets `net10.0-windows10.0.18362.0`, `win-x64`, and publishes as a framework-dependent single-file Windows exe. Do not assume this is a web, Node, React, or Playwright project.

## Required Workflow

- Work on a `codex/*` branch unless the user explicitly asks otherwise.
- Use explicit spawned subagents for meaningful reviews, audits, or parallel sidecar analysis. Do not simulate agents inline.
- Keep application-code changes separate from agent/tooling changes. For this Codex infrastructure pass, do not change app behavior.
- Use existing C#/.NET/WPF patterns and keep generated infrastructure small and readable.
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
dotnet restore
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore .\FlyleafLibTests
```

On this machine, sandboxed `dotnet` can fail when MSBuild reads the Windows SDK under AppData. If that happens, request the approved escalation and rerun the same command.

## What Not To Port

Do not copy PL_RU/codex web gates as-is. The following are not LLPlayer quality gates unless a future task explicitly introduces a web surface:

- `package.json`, `pnpm`, `npm`, Next.js, React, TypeScript-only gates.
- Playwright browser smoke, Lighthouse, pa11y, HTMLHint, Stylelint, ESLint.
- Knip, dependency-cruiser, web visual regression, browser accessibility gates.
- Rules about Tailwind, CSS-in-JS, localStorage, Blueprint imports, or web preview contracts.

## Shipping Rules

- Keep `.github/actions/build-package/action.yml` as the source of truth for release packaging.
- Preserve the separate app publish and `Plugins/YoutubeDL` publish flow.
- Keep tracked native assets intentional: `FFmpeg/*.dll` and `LLPlayer/lib/7z.dll`.
- Do not commit publish output, downloaded `yt-dlp.exe`, Whisper/Tesseract models, logs, dumps, local runtime config JSON, secrets, or Codex memories.

## GitHub Flow

- Prefer draft PRs for large Codex infrastructure changes.
- Before pushing, run the full build/test gate and note any unverified ship-only checks.
- If a GitHub Actions check fails, inspect the failing check/log before changing code.

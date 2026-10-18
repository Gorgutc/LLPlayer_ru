---
name: llplayer-bootstrap
description: Use when starting work in LLPlayer_ru or refreshing repository context before choosing a workflow.
---

# LLPlayer Bootstrap

Start by reading `AGENTS.md`, `docs/agent/README.md`, and the specific skill for the task.

## Required Context

- This is a C#/.NET 10 desktop repository: the shipped Windows app is WPF; F-13 adds one Windows/Linux Avalonia host and shared Core.
- Shipped WPF app: `LLPlayer/LLPlayer.csproj`. Shared Avalonia host/tests: `LLPlayer.Avalonia/` and `LLPlayer.Avalonia.Tests/`; shared services: `LLPlayer.Core/`.
- Media core: `FlyleafLib/FlyleafLib.csproj`, targeting Windows WPF and portable `net10.0`.
- Runtime plugin: `Plugins/YoutubeDL/YoutubeDL.csproj`.
- Tests: `FlyleafLibTests/FlyleafLibTests.csproj`.
- Codex plugin: `Plugins/llplayer-codex`. This uses uppercase `Plugins` because the repo already has that directory on Windows.

## First Checks

```powershell
git status --short --branch
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
```

Native Windows and Linux use the same Avalonia views, view models, themes and Core on portable `net10.0`.
The Windows build/package baseline is implemented; fresh runtime acceptance is recorded separately. WPF remains
supported until explicit replacement acceptance. Read `docs/agent/architecture.md` and `docs/agent/f13-status-2026-10-18.md`.

If work touches app code, `.\scripts\codex\verify.ps1` remains mandatory for WPF. Shared Core, portable FlyleafLib
or Avalonia changes also require `scripts/windows/verify-avalonia.ps1` and `scripts/linux/verify.sh`. Windows
Avalonia packaging uses `scripts/windows/publish-avalonia.ps1`; Linux uses `scripts/linux/publish.sh`.
Use `llplayer-quality-gate` for scope and acceptance limits; Windows ASR/OCR and GPU are separate acceptance work.

Before final handoff, run spawned `/review` with at least `verification_reviewer`. If no subagent spawn tool is available, notify the user and do not claim `/review` has been satisfied.

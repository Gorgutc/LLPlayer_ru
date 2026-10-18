---
name: llplayer-bootstrap
description: Use when starting work in LLPlayer_ru or refreshing repository context before choosing a workflow.
---

# LLPlayer Bootstrap

Start by reading `AGENTS.md`, `docs/agent/README.md`, and the specific skill for the task.

## Required Context

- This is a C#/.NET 10 desktop repository: the shipped Windows app is WPF; F-13 adds the current Linux Avalonia host and shared Core.
- Shipped WPF app: `LLPlayer/LLPlayer.csproj`. Linux host/tests: `LLPlayer.Avalonia/` and `LLPlayer.Avalonia.Tests/`; shared services: `LLPlayer.Core/`.
- Media core: `FlyleafLib/FlyleafLib.csproj`, targeting Windows WPF and portable `net10.0`.
- Runtime plugin: `Plugins/YoutubeDL/YoutubeDL.csproj`.
- Tests: `FlyleafLibTests/FlyleafLibTests.csproj`.
- Codex plugin: `Plugins/llplayer-codex`. This uses uppercase `Plugins` because the repo already has that directory on Windows.

## First Checks

```powershell
git status --short --branch
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
```

The target is the same Avalonia UI/Core on native Windows and Linux. The Windows Avalonia baseline remains proposed work; WPF remains supported until explicit replacement acceptance. Read `docs/agent/architecture.md` and `docs/agent/f13-status-2026-10-18.md` for current boundaries.

If work touches app code, `.\scripts\codex\verify.ps1` remains mandatory for the Windows WPF product. Shared Core, portable FlyleafLib, Avalonia, or Linux runtime changes also require `scripts/linux/verify.sh`; Linux packaging uses `scripts/linux/publish.sh`. Use `llplayer-quality-gate` for scope and acceptance limits.

Before final handoff, run spawned `/review` with at least `verification_reviewer`. If no subagent spawn tool is available, notify the user and do not claim `/review` has been satisfied.

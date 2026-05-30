---
name: llplayer-bootstrap
description: Use when starting work in LLPlayer_ru or refreshing repository context before choosing a workflow.
---

# LLPlayer Bootstrap

Start by reading `AGENTS.md`, `docs/agent/README.md`, and the specific skill for the task.

## Required Context

- This is a Windows-only C#/.NET 10 WPF desktop app, not a web repo.
- Main app: `LLPlayer/LLPlayer.csproj`.
- Media core: `FlyleafLib/FlyleafLib.csproj`.
- Runtime plugin: `Plugins/YoutubeDL/YoutubeDL.csproj`.
- Tests: `FlyleafLibTests/FlyleafLibTests.csproj`.
- Codex plugin: `Plugins/llplayer-codex`. This uses uppercase `Plugins` because the repo already has that directory on Windows.

## First Checks

```powershell
git status --short --branch
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
```

If work touches app code, plan to run `.\scripts\codex\verify.ps1` before handoff.

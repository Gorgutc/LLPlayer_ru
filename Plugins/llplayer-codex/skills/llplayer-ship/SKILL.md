---
name: llplayer-ship
description: Use when preparing LLPlayer_ru changes for commit, push, PR, or release handoff.
---

# LLPlayer Ship

Ship is not just tests. It includes packaging awareness.

## Required

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
```

For release/package changes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\ship.ps1
```

Use a spawned review subagent before final handoff. Summarize unverified risks and do not hide failures.

---
name: llplayer-quality-gate
description: Use when deciding which LLPlayer_ru verification gate must run before handoff.
---

# LLPlayer Quality Gate

Choose the smallest gate that proves the claim.

## Gates

- Codex docs/skills/hooks only: `.\scripts\codex\verify-fast.ps1`.
- C#/XAML/project files: `.\scripts\codex\verify.ps1`.
- Release/package behavior: `.\scripts\codex\ship.ps1`.

Always read command output and report failures directly. If sandbox blocks MSBuild access to Windows SDK paths, rerun with approved escalation.

## Review Before Handoff

After the relevant gate passes, run spawned `/review` before final handoff. If no subagent spawn tool is available, notify the user and do not claim `/review` has been satisfied.

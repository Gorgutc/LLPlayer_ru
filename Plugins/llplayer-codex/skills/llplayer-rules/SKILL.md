---
name: llplayer-rules
description: Use when making any LLPlayer_ru change that must follow repository-wide agent rules.
---

# LLPlayer Rules

`AGENTS.md` is authoritative. Follow it before local preferences or stale instructions.

## Rules

- Work on `codex/*` branches.
- Use explicit spawned subagents for meaningful audits and `/review`.
- Keep Codex/tooling changes separate from product behavior changes.
- Preserve the shipped Windows WPF product while F-13 adds the Linux Avalonia host, shared Core, and portable `net10.0` engine. The target is the same Avalonia UI on native Windows and Linux; the Windows Avalonia baseline remains proposed work, and WPF retirement requires explicit acceptance.
- Do not port web verification from PL_RU/codex into this .NET desktop project.
- Notify the user when an assumption affects scope, release packaging, or runtime assets.

## Handoff

Before final handoff, run the relevant verification gate and a spawned review subagent. The Windows full gate remains mandatory for app changes; shared/portable/Avalonia and Linux runtime changes also require `scripts/linux/verify.sh`, with `scripts/linux/publish.sh` for Linux packaging. Report native desktop acceptance separately from build/headless evidence.

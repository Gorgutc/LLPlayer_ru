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
- Do not port web verification from PL_RU/codex into this WPF project.
- Notify the user when an assumption affects scope, release packaging, or runtime assets.

## Handoff

Before final handoff, run the relevant verification gate and a spawned review subagent.

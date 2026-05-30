---
name: llplayer-instruction-drift
description: Use when reviewing LLPlayer_ru instructions, skills, hooks, docs, and scripts for inconsistencies.
---

# LLPlayer Instruction Drift

Compare instruction surfaces for contradictions.

## Surfaces

- `AGENTS.md`
- `CLAUDE.md`, `GEMINI.md`
- `Plugins/llplayer-codex/skills/**/SKILL.md`
- `.codex/agents/*.toml`
- `.codex/hooks.json`, `.codex/hooks/*.ps1`
- `scripts/codex/*.ps1`
- `docs/agent/*.md`

Flag stale web references, stale command names, hard-coded pass counts, and missing `/review` or subagent requirements.

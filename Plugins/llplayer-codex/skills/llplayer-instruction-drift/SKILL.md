---
name: llplayer-instruction-drift
description: Use when reviewing LLPlayer_ru instructions, skills, hooks, docs, and scripts for inconsistencies.
---

# LLPlayer Instruction Drift

Compare instruction surfaces for contradictions.

## Surfaces

- `AGENTS.md`
- `CLAUDE.md`, `GEMINI.md`
- `.codex/config.toml`
- `Plugins/llplayer-codex/skills/**/SKILL.md`
- `.agents/plugins/marketplace.json`
- `.codex/agents/*.toml`
- `.codex/hooks.json`, `.codex/hooks/*.ps1`
- `.github/workflows/*.yml`
- `scripts/codex/*.ps1`
- `docs/agent/*.md`
- `docs/agent/dubbing/**`
- `RUN_INSTRUCTIONS.md`, `DO_NOT_PUSH.md`

Flag stale web references, stale command names, hard-coded pass counts, and missing `/review` or subagent requirements.

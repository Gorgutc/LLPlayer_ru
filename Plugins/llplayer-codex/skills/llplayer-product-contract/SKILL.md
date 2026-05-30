---
name: llplayer-product-contract
description: Use when changing LLPlayer_ru user-facing behavior, WPF design, media runtime, config persistence, or dependency decisions.
---

# LLPlayer Product Contract

Before changing product behavior, read the matching frozen contract:

- `docs/agent/product-behavior-contract.md`
- `docs/agent/wpf-design-contract.md`
- `docs/agent/media-runtime-contract.md`
- `docs/agent/config-data-contract.md`
- `docs/agent/dependency-baseline.md`
- `docs/agent/manual-smoke-matrix.md`
- `docs/agent/subagent-review-matrix.md`

## Rule

Make the smallest targeted change that satisfies the user request. Preserve unrelated product decisions unless the user explicitly asks to change them.

If a frozen contract must change, update the contract in the same branch and explain why.

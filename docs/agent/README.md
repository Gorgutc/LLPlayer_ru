# LLPlayer Agent Docs

These docs describe how agents should work in `LLPlayer_ru`.

Start with:

- `AGENTS.md`
- `docs/agent/bootstrap.md`
- `docs/agent/verification.md`
- `docs/agent/frozen-decisions.md`
- The relevant `llplayer-*` skill under `Plugins/llplayer-codex/skills`

This repository is a C#/.NET 10 desktop app: the shipped Windows WPF app plus the in-progress Linux Avalonia app (F-13, see `architecture.md`). Do not import PL_RU/codex web gates unless a future task explicitly adds a web surface.

Frozen product contracts:

- `product-behavior-contract.md`
- `wpf-design-contract.md`
- `media-runtime-contract.md`
- `config-data-contract.md`
- `dependency-baseline.md`
- `manual-smoke-matrix.md`
- `subagent-review-matrix.md`
- `dubbing-contract.md`

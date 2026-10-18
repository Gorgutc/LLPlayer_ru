# LLPlayer Agent Docs

These docs describe how agents should work in `LLPlayer_ru`.

Start with:

- `AGENTS.md`
- `docs/agent/bootstrap.md`
- `docs/agent/verification.md`
- `docs/agent/frozen-decisions.md`
- The relevant `llplayer-*` skill under `Plugins/llplayer-codex/skills`

This repository is a C#/.NET 10 desktop app: the shipped Windows WPF app plus the in-progress shared Windows/Linux
Avalonia app (F-13, see `architecture.md`). Both Avalonia RIDs use one UI/Core and the portable `net10.0` engine.
The additive Windows build/package baseline has its own `scripts/windows/verify-avalonia.ps1` and
`publish-avalonia.ps1`; Linux uses `scripts/linux/verify.sh` and `publish.sh`. Keep WPF compatibility gates and
report native desktop/model acceptance separately. Do not import web gates unless a task adds a web surface.

Frozen product contracts:

- `product-behavior-contract.md`
- `wpf-design-contract.md`
- `media-runtime-contract.md`
- `config-data-contract.md`
- `dependency-baseline.md`
- `manual-smoke-matrix.md`
- `subagent-review-matrix.md`
- `dubbing-contract.md`

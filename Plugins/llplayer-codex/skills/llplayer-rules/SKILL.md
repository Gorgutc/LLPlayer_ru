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
- Preserve the shipped Windows WPF product while F-13 develops the shared Windows/Linux Avalonia host, Core and
  portable `net10.0` engine. Use one Views/ViewModels/Themes tree for both platforms. The Windows build/package
  baseline is implemented; native acceptance and Windows ASR/OCR/GPU remain separate. WPF retirement requires
  explicit replacement acceptance and an owner decision.
- Do not port web verification from PL_RU/codex into this .NET desktop project.
- Notify the user when an assumption affects scope, release packaging, or runtime assets.

## Handoff

Before final handoff, run the relevant verification gate and a spawned review subagent. The Windows WPF full gate
remains mandatory for app changes; shared/portable/Avalonia changes also require `scripts/windows/verify-avalonia.ps1`
and `scripts/linux/verify.sh`. Package with `scripts/windows/publish-avalonia.ps1` and `scripts/linux/publish.sh`
for the respective Avalonia platforms. Report native desktop acceptance separately from build/headless evidence.

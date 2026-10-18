---
name: llplayer-quality-gate
description: Use when deciding which LLPlayer_ru verification gate must run before handoff.
---

# LLPlayer Quality Gate

Choose the smallest gate that proves the claim.

## Gates

- Codex docs/skills/hooks only: `.\scripts\codex\verify-fast.ps1`.
- C#/XAML/AXAML/project files: `.\scripts\codex\verify.ps1` remains mandatory for the shipped Windows WPF product.
- Shared Core, portable FlyleafLib, Avalonia, or Linux runtime changes: also run the full `scripts/linux/verify.sh` gate. Its `--fast` mode skips the WPF/plugin compile check and does not replace the required Windows gate.
- Windows release/package behavior: `.\scripts\codex\ship.ps1`; `.github/actions/build-package/action.yml` remains the Windows packaging source of truth.
- Linux package behavior: `scripts/linux/publish.sh` for package-content validation alongside the Linux verification gate.

Keep local runtime-asset checks offline unless explicitly shipping; report missing prerequisites. Always read command output and report failures directly. If sandbox blocks MSBuild access to Windows SDK paths, rerun with approved escalation.

Report each platform gate separately. Linux headless tests and historical receipts do not establish native desktop acceptance. Native Windows Avalonia is a proposed baseline using the portable `net10.0` engine, not an implemented or accepted replacement for WPF; Windows ASR/OCR and GPU behavior require separate evidence.

## Review Before Handoff

After the relevant gate passes, run spawned `/review` before final handoff. If no subagent spawn tool is available, notify the user and do not claim `/review` has been satisfied.

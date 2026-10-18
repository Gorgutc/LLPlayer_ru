---
name: llplayer-quality-gate
description: Use when deciding which LLPlayer_ru verification gate must run before handoff.
---

# LLPlayer Quality Gate

Choose the smallest gate that proves the claim.

## Gates

- Codex docs/skills/hooks only: `.\scripts\codex\verify-fast.ps1`.
- C#/XAML/AXAML/project files: `.\scripts\codex\verify.ps1` remains mandatory for the shipped Windows WPF product.
- Shared Core, portable FlyleafLib or Avalonia changes: also run `scripts/windows/verify-avalonia.ps1` and the full
  `scripts/linux/verify.sh` gate. Linux `--fast` mode skips the WPF/plugin compile check and does not replace the
  required Windows WPF gate. OS-specific runtime changes require the matching platform gate.
- Windows release/package behavior: `.\scripts\codex\ship.ps1`; `.github/actions/build-package/action.yml` remains the Windows packaging source of truth.
- Linux package behavior: `scripts/linux/publish.sh` for package-content validation alongside the Linux verification gate.
- Windows Avalonia package behavior: `scripts/windows/test-package-avalonia.ps1` and
  `scripts/windows/publish-avalonia.ps1` for positive/negative content rules, ZIP integrity and checksum.

Keep local runtime-asset checks offline unless explicitly shipping; report missing prerequisites. Always read command output and report failures directly. If sandbox blocks MSBuild access to Windows SDK paths, rerun with approved escalation.

Report WPF, Windows Avalonia and Linux gates separately. Headless tests, native-library checks and historical
receipts do not establish desktop acceptance. The Windows Avalonia build/package baseline uses the same portable
`net10.0` UI/engine as Linux; native Windows ASR/OCR and GPU behavior require separate evidence. WPF remains
supported until replacement acceptance and the owner's retirement decision.

## Review Before Handoff

After the relevant gate passes, run spawned `/review` before final handoff. If no subagent spawn tool is available, notify the user and do not claim `/review` has been satisfied.

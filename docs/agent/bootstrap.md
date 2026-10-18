# Bootstrap

1. Check branch and working tree:

   ```powershell
   git status --short --branch
   ```

2. Read `AGENTS.md`.
3. Load the relevant `llplayer-*` skill.
4. Identify whether the task touches:
   - Codex infrastructure only
   - C#/XAML/product code
   - native/runtime assets
   - release packaging

5. Choose verification:
   - Infra only: `scripts/codex/verify-fast.ps1`
   - App/build changes: `scripts/codex/verify.ps1`
   - Packaging changes: `scripts/codex/ship.ps1`
   - Shared Core/portable/Avalonia changes: also `scripts/windows/verify-avalonia.ps1` and `scripts/linux/verify.sh`
   - Windows Avalonia packaging: `scripts/windows/test-package-avalonia.ps1` and `scripts/windows/publish-avalonia.ps1`
   - Linux packaging: `scripts/linux/publish.sh`

   F-13 uses one Windows/Linux Avalonia Views/ViewModels/Themes tree and shared Core over portable `net10.0`.
   The Windows build/package baseline is implemented; native desktop, Windows ASR/OCR and GPU acceptance are
   separate evidence. Preserve the supported WPF product until replacement acceptance and an owner retirement decision.

6. Before final handoff, run spawned `/review` with at least `verification_reviewer`. If no subagent spawn tool is available, say so and do not claim `/review` is satisfied.

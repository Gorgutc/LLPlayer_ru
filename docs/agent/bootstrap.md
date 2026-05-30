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

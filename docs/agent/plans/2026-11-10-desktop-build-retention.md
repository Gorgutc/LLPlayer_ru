# Windows test-build retention and cleanup

> For agentic workers: use superpowers:subagent-driven-development. This is one owner-authorized tooling stage, not a new F-13 feature.

**Goal:** Remove accumulated generated archives/staging and keep the selected Windows Avalonia test build plus one verified fallback, preserving sources, settings and models.

**Owner decision:** November 10 GO explicitly supersedes unlimited preservation of older payloads. No app launch/stop or full-F13 native acceptance is authorized.

**Architecture:** Keep the current publisher behavior and package contracts. Add a small PowerShell retention helper, invoked only after a successful updater activation/readback outside rollback. Default retention is two verified builds; referenced/running, unknown/changed or unsafe builds are protected exceptions. Preserve every retained receipt's archive/package-source dependencies. Unreferenced generated desktop publish workspaces are eligible only after strict boundary, ownership, contents and reparse checks. Historical QA files are cleaned once from an independently reviewed literal allowlist; their evidence and source backups remain. No generic whole-directory artifacts/worktree/model cleanup.

**Approaches:** Unlimited history caused the measured growth. Blind age-based artifacts deletion risks user data and receipt dependencies. Bounded verified retention plus a one-time reviewed QA cleanup meets the explicit owner request with the existing PowerShell tooling.

**Verification:** Meaningful offline retention regression fixtures (including RED where practical), existing Desktop fixtures, fast gate and mandatory full WPF verify. Packaging checks where changed integration requires them; fresh Windows/Linux PR/main CI for delivery. Commit reviewed tooling before refreshing the blue build; scope Second Brain sync to actual cleanup/retention receipts. No product/UI change or dependency upgrade.

## Constraints and review focus

- Only resolved literal targets inside the named install/build or LLPlayer artifacts roots; reject ancestor and descendant reparse points before recursive deletion. Never traverse/delete a source worktree.
- Current and fallback identities/dependencies, WPF/other links and running executables are protected. Unknown contents, user configs/models or modified payloads are skipped rather than deleted.
- Snapshot/CAS checks immediately before destructive writes; report partial cleanup and locked files honestly. Cleanup failure cannot roll back an already selected usable build.
- Retained manifests/receipts and compact reports remain. Logical bytes are not promised physically reclaimable bytes; measure actual free space before/after.
- No model/user-settings migration, runtime installation, process termination, native GUI or Computer Use.

## Coordinated implementation and execution

- [x] Produce exact generated-file allowlists and protection manifest; independently review before live removal.
- [x] Implement the default-two post-success retention helper and deterministic fixtures; close independent Critical/Important findings.
- [x] Apply only reviewed cleanup targets, rechecking current/backup/refs and filesystem boundaries; record actual logical removal and volume snapshots without claiming exclusive physical attribution.
- Delivery results (gates, reviewed commit/PR/merge, fresh selected build, final review and scoped knowledge sync) belong in the task closeout and Second Brain session. This design plan is not a second active backlog.

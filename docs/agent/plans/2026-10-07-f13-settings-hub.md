# F-13 Avalonia Settings Hub — bounded first slice

**Status:** implementation, focused tests, Linux `--fast`, and scoped source/UI reviews passed. Package review and installation/readback passed. Independent final verification review passed with zero Critical/Important findings; native desktop/owner and Windows acceptance remain open.\
**Target evidence directory:** `/models/codex-builds/LLPlayer_ru/artifacts/f13-settings-hub-2026-10-07/`

## Goal and scope

Add one discoverable Avalonia Settings hub, using the existing shadcn theme, with left navigation for Appearance and Subtitles plus appropriately grouped links to existing tools. It opens from View → Settings… and a visible gear button. The modal hub is a singleton. Its Appearance/Subtitle controls expose exactly the six existing `AppPrefs` fields: `Theme` (Dark/Light), `SubtitleFontSize` (12–96 DIP), `SecondaryFontScale` (0.4–1.5, optionally shown as 40–150%), `SubtitlesVisible`, `SidebarVisible`, and `SidebarWidth` (220–900 DIP).

Settings edits are detached. **Save & Close** commits the hub-owned fields; **Close** and Esc discard them. A settings link closes the hub and discards its unsaved draft before opening the separately owned existing dialog (Translation, Recognition ASR/OCR, or keyboard shortcut viewer, grouped by purpose). The hub must warn clearly that following a link discards unsaved hub edits. No inactive placeholder controls, editable-key workflow, dubbing settings, or full WPF parity claim are in scope.

## Persistence and behavior constraints

Do not add schema fields, defaults, migrations, or subtitle-position persistence. On save, merge only the six hub-owned values into a fresh current `AppPrefs` candidate, preserving current volume, mute, recents, last folder, legacy provider preference, and any other unrelated fields. Persist the candidate before applying live theme, overlay visibility/font sizing, or sidebar state. If persistence fails, keep the live UI unchanged, leave the hub open, and show the error. A scale-only save must recompute the secondary overlay font size.

Windows/WPF code and behavior remain unchanged. The implementation is Linux Avalonia only and is not a claim of full Settings/Keys/Subtitles/ASR/Translation/Dubbing parity.

## Implementation and verification receipts

Implementation is recorded against the pre-existing dirty baseline in `implementation-manifest.json` and `scoped-delta.patch`; `AppPrefs.cs` is unchanged, with no schema/default changes. The production behavior includes the six-field detached candidate merge, save-before-apply, failure preservation, scale-only recomputation, menu/gear singleton routing, and the three existing Settings link routes.

Focused test command: `LLPLAYER_SETTINGS_ARTIFACTS=/models/codex-builds/LLPlayer_ru/artifacts/f13-settings-hub-2026-10-07/screenshots dotnet test LLPlayer.Avalonia.Tests --no-restore -warnaserror --filter 'FullyQualifiedName~LLPlayer.Avalonia.Tests.SettingsTests|FullyQualifiedName~LLPlayer.Avalonia.Tests.SettingsWindowTests'`. `focused.exit=0`; 20 passed, 0 failed/skipped. Genuine assertion RED before implementation: `red.exit=1`, successful build then one expected failure because SettingsMenu was absent.

Full Linux fast gate with restore: `LLPLAYER_SETTINGS_ARTIFACTS=/models/codex-builds/LLPlayer_ru/artifacts/f13-settings-hub-2026-10-07/screenshots scripts/linux/verify.sh --fast`. `linux-fast.exit=0`; FlyleafLib 1593 passed/6 skipped, Avalonia 216 passed/1 skipped; shell checks, portable builds, and validators passed. Windows WPF/plugin compile is excluded by `--fast` and remains unverified.

The evidence directory contains `task-report.md`, `focused.log`/`.exit`, `red.log`/`.exit`, `linux-fast.log`/`.exit`, `scoped-delta.patch`, `implementation-manifest.json`, and `screenshots-manifest.json`. The 15 screenshot hashes are recorded there. Independent source-quality and UI reviews passed with zero Critical, Important, or UI-minor findings. Root independently viewed seven representative screenshots; the review covered all 15. Native desktop/owner acceptance remains open.

## Package and installed launcher receipt

**Package review passed.** Archive: `/models/codex-builds/LLPlayer_ru/artifacts/f13-settings-hub-2026-10-07/package/LLPlayer-0.3.62-linux-x64.tar.gz`, 180,020,698 bytes, SHA-256 `337a37210ec0bf1897df06b550f7f6cf2e8b47695b06e47997fbdb8711cdbe4b`. The archive contains 473 regular files and 7 symlinks matching staging; four Release/staging hashes match. Source before/after/current explicit projections are stable: 722 records (718 files and 4 absent tracked entries), with no build outputs. The archive checksum was read back from the package and verified.

**Installation/readback passed** (`installation-receipt.json`, `installation-readback.json`). Installed target and resolved `current` are `/models/codex-builds/LLPlayer_ru/installed/LLPlayer-0.3.62-linux-x64-settings-337a37210ec0`; 473 regular files were verified and installed content validation exited 0. Apphost and wrapper are executable. The prior package remains preserved; all seven old critical payload hashes match before/after. Both existing desktop files are unchanged (same SHA-256 `589547d56407a84919e8c7b73846fe394d3da9e799226b201a6d1e1770b85108` and bytes) and their stable `current` Exec/Icon resolve to the new package. Three XDG state files are unchanged (`xdg_concurrent_delta={}`, no writes). No GUI launch or process stop occurred.

Independent final verification review passed with zero Critical/Important findings. Native desktop/owner and Windows acceptance remain open.

## Review and completion boundary

Scoped implementation and UI reviews, Linux verification, package review, and installation/readback are evidenced above. Independent final verification review passed with zero Critical/Important findings; native GUI/owner and Windows acceptance remain open, and full Settings parity remains incomplete. Keep the plan and four companion docs aligned with receipts as they arrive.

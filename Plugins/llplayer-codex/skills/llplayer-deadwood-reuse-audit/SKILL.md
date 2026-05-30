---
name: llplayer-deadwood-reuse-audit
description: Use when auditing LLPlayer_ru for dead code, duplication, or reuse opportunities.
---

# LLPlayer Deadwood And Reuse Audit

Audit read-only unless assigned a specific cleanup.

## Focus

- Duplicate view model or XAML patterns in `LLPlayer/`.
- Media logic duplication across `FlyleafLib/MediaPlayer` and `FlyleafLib/MediaFramework`.
- Unused Codex docs, skills, hooks, or scripts.
- Runtime/plugin code that is still referenced by reflection or packaging.

Be careful: plugin discovery and WPF bindings can make code look unused when it is runtime-bound.

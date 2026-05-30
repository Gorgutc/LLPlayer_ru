# Subagent Review Matrix

Use explicit spawned subagents. If no spawn tool is available, notify the user and do not claim `/review` has been satisfied.

## Path Scope To Agents

- `LLPlayer/**/*.xaml`, `LLPlayer/Views/**`, `LLPlayer/Controls/**`, `LLPlayer/ViewModels/**`: `wpf_xaml_reviewer`, `verification_reviewer`.
- `LLPlayer/Services/AppConfig.cs`, `FlyleafLib/Engine/Config.cs`, settings controls, key bindings: `dotnet_quality_guardian`, `instruction_drift_auditor`, `verification_reviewer`.
- `FlyleafLib/Engine/**`, `FlyleafLib/MediaPlayer/**`, `FlyleafLib/MediaFramework/**`: `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `FlyleafLib/MediaPlayer/Translation/**`: `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `Plugins/YoutubeDL/**`: `media_runtime_mapper`, `packaging_release_reviewer`, `verification_reviewer`.
- `FFmpeg/**`, `LLPlayer/lib/**`, publish profiles, `.github/actions/build-package/action.yml`: `native_dependency_auditor`, `packaging_release_reviewer`, `verification_reviewer`.
- `docs/agent/product-behavior-contract.md`: `wpf_xaml_reviewer`, `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `docs/agent/wpf-design-contract.md`: `wpf_xaml_reviewer`, `verification_reviewer`.
- `docs/agent/media-runtime-contract.md`: `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `docs/agent/config-data-contract.md`: `dotnet_quality_guardian`, `instruction_drift_auditor`, `verification_reviewer`.
- `docs/agent/dependency-baseline.md`: `tech_stack_cartographer`, `native_dependency_auditor`, `packaging_release_reviewer`, `verification_reviewer`.
- `docs/agent/manual-smoke-matrix.md`: `wpf_xaml_reviewer`, `media_runtime_mapper`, `packaging_release_reviewer`, `verification_reviewer`.
- `AGENTS.md`, `.codex/**`, `Plugins/llplayer-codex/**`, `docs/agent/**`, `scripts/codex/**`: `codex_infra_architect`, `instruction_drift_auditor`, `verification_reviewer`.

## Review Rules

- One agent should own one bounded question.
- Review findings are ordered by severity.
- Critical and Important findings must be fixed or explicitly accepted before handoff.
- `/review` means at least `verification_reviewer`; broad changes also need the relevant domain agent above.

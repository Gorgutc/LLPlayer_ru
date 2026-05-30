# Orchestration

Use spawned subagents for independent analysis and final review. Do not simulate subagents inline.

## Recommended Roles

- `tech_stack_cartographer`: stack and project boundaries.
- `media_runtime_mapper`: media/runtime flow.
- `wpf_xaml_reviewer`: WPF/XAML review.
- `dotnet_quality_guardian`: build/test/project review.
- `native_dependency_auditor`: native assets and runtime dependencies.
- `packaging_release_reviewer`: publish/release packaging.
- `instruction_drift_auditor`: docs/skills/hooks/script consistency.
- `codex_infra_architect`: repo-local Codex plugin, hooks, scripts, and marketplace structure.
- `verification_reviewer`: final `/review`.
- `deadwood_reuse_auditor`: dead code, duplication, reuse, and stale infrastructure.

One agent should own one question. Keep prompts bounded and read-only unless the agent is explicitly assigned edits.

Path-to-agent ownership is frozen in `docs/agent/subagent-review-matrix.md`. For broad changes, use every relevant role from that matrix and always include `verification_reviewer` for `/review`.

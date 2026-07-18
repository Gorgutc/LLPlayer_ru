# Plan Template

Use this shape for LLPlayer implementation plans.

```markdown
# Title

## Summary
- What changes.
- What does not change.

## Key Changes
- Files or subsystems.
- Interfaces or commands.

## Affected Frozen Contracts
- Product behavior, WPF design, media runtime, config/data, dependency baseline, packaging, or agent infrastructure.
- State `none` only when the change is outside these surfaces.

## Verification
- Fast gate, full gate, or ship gate.
- Manual smoke checks if app/runtime behavior changes.
- Coverage decision: deterministic regression and intentional RED evidence, or the reason no safe deterministic seam
  exists plus the exact manual/integration smoke boundary.
- Never use a global coverage percentage or hard-coded passing-test total as the acceptance criterion.
- Route changed paths through `docs/agent/subagent-review-matrix.md` and use explicit spawned reviewers.
- Finish with a spawned `/review` including `verification_reviewer`; resolve all Critical/Important findings.

## Assumptions
- Windows/.NET/packaging constraints.
- Any deferred risks.
```

Plans that touch product code must state whether UI, media runtime, config/data, native assets, or release packaging are affected.

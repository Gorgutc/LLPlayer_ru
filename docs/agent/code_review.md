# Code Review

Findings come first and must include severity and path.

## Severity

- Critical: data loss, security, build break, app crash, release corruption.
- Important: likely user-visible regression, missing required verification, packaging mismatch.
- Minor: maintainability or clarity issue that does not block the task.

## Review Focus

- C#/XAML correctness and WPF bindings.
- Threading and dispatcher boundaries.
- Media/runtime side effects.
- Native asset and packaging behavior.
- Tests or verification evidence.
- Instruction drift for Codex infrastructure.

Always run or cite fresh verification before claiming work is complete.
Final `/review` must be performed by a spawned subagent, at minimum `verification_reviewer`. If no subagent spawn tool is available, say that explicitly instead of simulating the review inline.

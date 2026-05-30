$ErrorActionPreference = "Stop"

Write-Host "Before changing LLPlayer:"
Write-Host "- Read AGENTS.md and relevant llplayer-* skills."
Write-Host "- Use spawned subagents for meaningful review/audit work."
Write-Host "- Keep app code changes separate from Codex/tooling work."
Write-Host "- Run .\scripts\codex\verify-fast.ps1 for infra changes and .\scripts\codex\verify.ps1 before handoff."

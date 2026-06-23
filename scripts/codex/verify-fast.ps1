$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    & ".\scripts\codex\check-environment.ps1"
    & ".\scripts\codex\verify-plugin.ps1"
    & ".\scripts\codex\verify-doc-coverage.ps1"
    & ".\scripts\codex\verify-frozen.ps1"
    & ".\scripts\codex\check-dub-licenses.ps1"
    Write-Host "LLPlayer fast verification completed."
}
finally {
    Pop-Location
}

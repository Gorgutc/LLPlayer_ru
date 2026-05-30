param(
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    function Invoke-Checked {
        param(
            [Parameter(Mandatory = $true)]
            [string]$FilePath,
            [Parameter(ValueFromRemainingArguments = $true)]
            [string[]]$Arguments
        )

        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
        }
    }

    & ".\scripts\codex\verify-fast.ps1"

    if (-not $SkipRestore) {
        Invoke-Checked dotnet "restore"
    }

    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\LLPlayer"
    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\Plugins\YoutubeDL"
    Invoke-Checked dotnet "test" "--no-restore" ".\FlyleafLibTests"

    Write-Host "LLPlayer full verification completed."
}
finally {
    Pop-Location
}

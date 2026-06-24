$ErrorActionPreference = "Stop"

# Dub license gate: the bundled dubbing venv must contain only redistribution-safe (GPLv3-compatible)
# packages. XTTS-v2, F5-TTS, NeMo, IndexTTS2, NLLB-200, audeering, ttsfrd and coqui TTS are non-commercial /
# unvetted and must be USER-INSTALLED ONLY, never bundled. We scan the committed dub_sidecar manifests
# (the bundled truth): pyproject.toml dependency lines now, uv.lock once generated. See
# docs/agent/dubbing-contract.md.

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $pyproject = ".\dub_sidecar\pyproject.toml"
    $lock = ".\dub_sidecar\uv.lock"
    $forbidden = @(
        "tts", "coqui-tts", "xtts", "f5-tts", "f5_tts",
        "nemo-toolkit", "nemo_toolkit", "indextts", "index-tts",
        "audeering",
        "nllb", "nllb-200", "ttsfrd"
    )

    $scanned = $false
    $hits = New-Object System.Collections.Generic.List[string]

    # uv.lock: match package stanzas (name = "pkg").
    if (Test-Path $lock) {
        $scanned = $true
        $lockText = Get-Content $lock -Raw
        foreach ($pkg in $forbidden) {
            if ($lockText -match ('name\s*=\s*"' + [regex]::Escape($pkg) + '"')) {
                $hits.Add("$pkg (uv.lock)")
            }
        }
    }

    # pyproject.toml: match dependency strings ("pkg>=..."/"pkg=="/"pkg @ ...") on NON-comment lines only
    # (the file documents the excluded set in comments, which must not trip the gate).
    if (Test-Path $pyproject) {
        $scanned = $true
        $depLines = Get-Content $pyproject | Where-Object { $_ -notmatch '^\s*#' }
        $depText = ($depLines -join "`n")
        foreach ($pkg in $forbidden) {
            if ($depText -match ('"' + [regex]::Escape($pkg) + '\s*([><=~!@"])')) {
                $hits.Add("$pkg (pyproject.toml)")
            }
        }
    }

    if (-not $scanned) {
        Write-Host "Dub license gate: no dub_sidecar manifest yet - skipping scan."
        return
    }

    if ($hits.Count -gt 0) {
        Write-Error ("Dubbing venv would bundle non-redistributable package(s): " + ($hits -join ", ") +
            ". These are non-commercial / GPLv3-incompatible and must be user-installed only, never bundled. " +
            "See docs/agent/dubbing-contract.md.")
        exit 1
    }

    Write-Host "Dub license gate: dub_sidecar manifests are clean."
}
finally {
    Pop-Location
}

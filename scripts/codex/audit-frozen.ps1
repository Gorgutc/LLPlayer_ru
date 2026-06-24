$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $changed = New-Object System.Collections.Generic.List[string]

    foreach ($path in @(git diff --name-only)) {
        if ($path) { $changed.Add($path) }
    }
    foreach ($path in @(git diff --cached --name-only)) {
        if ($path) { $changed.Add($path) }
    }
    foreach ($path in @(git ls-files --others --exclude-standard)) {
        if ($path) { $changed.Add($path) }
    }

    $changed = @($changed | Sort-Object -Unique)
    if ($changed.Count -eq 0) {
        Write-Host "No changed paths found."
        return
    }

    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($path in $changed) {
        $normalized = $path -replace "\\", "/"
        $contracts = New-Object System.Collections.Generic.List[string]
        $agents = New-Object System.Collections.Generic.List[string]
        $gates = New-Object System.Collections.Generic.List[string]

        if ($normalized -eq 'docs/agent/product-behavior-contract.md') {
            $contracts.Add("product-behavior-contract.md")
            $agents.Add("wpf_xaml_reviewer")
            $agents.Add("media_runtime_mapper")
            $agents.Add("dotnet_quality_guardian")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("manual smoke by changed behavior")
        }
        if ($normalized -eq 'docs/agent/wpf-design-contract.md') {
            $contracts.Add("wpf-design-contract.md")
            $agents.Add("wpf_xaml_reviewer")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("manual UI smoke")
        }
        if ($normalized -eq 'docs/agent/media-runtime-contract.md') {
            $contracts.Add("media-runtime-contract.md")
            $agents.Add("media_runtime_mapper")
            $agents.Add("dotnet_quality_guardian")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("playback smoke")
        }
        if ($normalized -eq 'docs/agent/config-data-contract.md') {
            $contracts.Add("config-data-contract.md")
            $agents.Add("dotnet_quality_guardian")
            $agents.Add("instruction_drift_auditor")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("settings persistence smoke")
        }
        if ($normalized -eq 'docs/agent/dependency-baseline.md') {
            $contracts.Add("dependency-baseline.md")
            $agents.Add("tech_stack_cartographer")
            $agents.Add("native_dependency_auditor")
            $agents.Add("packaging_release_reviewer")
            $agents.Add("verification_reviewer")
            $gates.Add("ship")
        }
        if ($normalized -eq 'docs/agent/manual-smoke-matrix.md') {
            $contracts.Add("manual-smoke-matrix.md")
            $agents.Add("wpf_xaml_reviewer")
            $agents.Add("media_runtime_mapper")
            $agents.Add("packaging_release_reviewer")
            $agents.Add("verification_reviewer")
            $gates.Add("manual smoke by changed behavior")
        }
        if ($normalized -match '^(\.gitignore|DO_NOT_PUSH\.md)$') {
            $contracts.Add("dependency-baseline.md")
            $contracts.Add("config-data-contract.md")
            $agents.Add("native_dependency_auditor")
            $agents.Add("packaging_release_reviewer")
            $agents.Add("instruction_drift_auditor")
            $agents.Add("verification_reviewer")
            $gates.Add("verify-fast")
        }

        if ($normalized -match '^(AGENTS\.md|\.codex/|\.agents/|Plugins/llplayer-codex/|docs/agent/|scripts/codex/)') {
            $contracts.Add("agent infrastructure")
            $agents.Add("codex_infra_architect")
            $agents.Add("instruction_drift_auditor")
            $agents.Add("verification_reviewer")
            $gates.Add("verify-fast")
        }
        if ($normalized -match '^(LLPlayer/.*\.xaml|LLPlayer/(Views|Controls|ViewModels|Themes|Resources)/)') {
            $contracts.Add("wpf-design-contract.md")
            $contracts.Add("product-behavior-contract.md")
            $agents.Add("wpf_xaml_reviewer")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("manual UI smoke")
        }
        if ($normalized -match '^(LLPlayer/Services/AppConfig\.cs|LLPlayer/.*Settings|FlyleafLib/Engine/Config\.cs)') {
            $contracts.Add("config-data-contract.md")
            $agents.Add("dotnet_quality_guardian")
            $agents.Add("instruction_drift_auditor")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("settings persistence smoke")
        }
        if ($normalized -match '^FlyleafLib/(Engine|MediaPlayer|MediaFramework)/') {
            $contracts.Add("media-runtime-contract.md")
            $agents.Add("media_runtime_mapper")
            $agents.Add("dotnet_quality_guardian")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("playback smoke")
        }
        if ($normalized -match '^FlyleafLib/MediaPlayer/Dubbing/') {
            $contracts.Add("dubbing-contract.md")
            $agents.Add("media_runtime_mapper")
            $agents.Add("dotnet_quality_guardian")
            $agents.Add("native_dependency_auditor")
            $agents.Add("verification_reviewer")
            $gates.Add("verify")
            $gates.Add("dubbing manual smoke")
        }
        if ($normalized -match '^dub_sidecar/') {
            $contracts.Add("dubbing-contract.md")
            $contracts.Add("dependency-baseline.md")
            $agents.Add("media_runtime_mapper")
            $agents.Add("native_dependency_auditor")
            $agents.Add("packaging_release_reviewer")
            $agents.Add("verification_reviewer")
            $gates.Add("verify-fast")
            $gates.Add("dubbing manual smoke")
        }
        if ($normalized -match '^(FFmpeg/|LLPlayer/lib/|Plugins/YoutubeDL/|\.github/actions/build-package/|LLPlayer/Properties/PublishProfiles/|.*\.pubxml$)') {
            $contracts.Add("dependency-baseline.md")
            $contracts.Add("media-runtime-contract.md")
            $agents.Add("native_dependency_auditor")
            $agents.Add("packaging_release_reviewer")
            $agents.Add("verification_reviewer")
            $gates.Add("ship")
        }

        if ($contracts.Count -eq 0) {
            $contracts.Add("check relevant frozen contract")
            $agents.Add("verification_reviewer")
            $gates.Add("verify-fast")
        }

        $rows.Add([pscustomobject]@{
            Path = $path
            Contracts = (@($contracts | Sort-Object -Unique) -join ", ")
            Agents = (@($agents | Sort-Object -Unique) -join ", ")
            Gates = (@($gates | Sort-Object -Unique) -join ", ")
        })
    }

    $rows | Format-Table -AutoSize
}
finally {
    Pop-Location
}

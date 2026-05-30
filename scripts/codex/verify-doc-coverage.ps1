$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $failures = New-Object System.Collections.Generic.List[string]

    function Require-Text($Path, $Pattern, $Message) {
        if (-not (Test-Path $Path)) {
            $failures.Add("Missing $Path.")
            return
        }

        $text = Get-Content $Path -Raw
        if ($text -notmatch $Pattern) {
            $failures.Add($Message)
        }
    }

    $contractDocs = @(
        "product-behavior-contract.md",
        "wpf-design-contract.md",
        "media-runtime-contract.md",
        "config-data-contract.md",
        "dependency-baseline.md",
        "manual-smoke-matrix.md",
        "subagent-review-matrix.md"
    )

    foreach ($doc in $contractDocs) {
        Require-Text ".\AGENTS.md" ([regex]::Escape("docs/agent/$doc")) "AGENTS.md must link docs/agent/$doc."
        Require-Text ".\docs\agent\README.md" ([regex]::Escape($doc)) "docs/agent/README.md must list $doc."
        Require-Text ".\docs\agent\frozen-decisions.md" ([regex]::Escape($doc)) "docs/agent/frozen-decisions.md must list $doc."
    }

    Require-Text ".\docs\agent\skill-map.md" "llplayer-product-contract" "Skill map must include llplayer-product-contract."
    Require-Text ".\Plugins\llplayer-codex\.codex-plugin\plugin.json" "llplayer-product-contract" "Plugin default prompt must mention llplayer-product-contract."
    Require-Text ".\Plugins\llplayer-codex\skills\llplayer-product-contract\SKILL.md" "product-behavior-contract\.md" "Product contract skill must link product behavior contract."
    Require-Text ".\Plugins\llplayer-codex\skills\llplayer-frozen-decisions\SKILL.md" "product-behavior-contract\.md" "Frozen decisions skill must link product behavior contract."
    Require-Text ".\Plugins\llplayer-codex\skills\llplayer-spec-guardian\SKILL.md" "Which frozen contracts are touched" "Spec guardian must ask which frozen contracts are touched."
    Require-Text ".\docs\agent\verification.md" "verify-doc-coverage\.ps1" "Verification docs must mention verify-doc-coverage.ps1."
    Require-Text ".\docs\agent\verification.md" "audit-frozen\.ps1" "Verification docs must mention audit-frozen.ps1."
    Require-Text ".\docs\agent\plan_template.md" "Affected Frozen Contracts" "Plan template must require affected frozen contracts."

    $configText = Get-Content ".\.codex\config.toml" -Raw
    if ($configText -notmatch "LLPlayer_ru") {
        $failures.Add(".codex/config.toml must describe LLPlayer_ru.")
    }
    foreach ($staleToken in @("PL_RU", "Blueprints_lib", "Osiris_ref", "package_manager")) {
        if ($configText -match [regex]::Escape($staleToken)) {
            $failures.Add(".codex/config.toml contains stale token '$staleToken'.")
        }
    }

    $gitignore = Get-Content ".\.gitignore" -Raw
    foreach ($pattern in @(
        "LLPlayer.Config.json",
        "LLPlayer.Engine.json",
        "LLPlayer.PlayerConfig.json",
        "crash.log",
        "Recordings/",
        "Snapshots/",
        "whispermodels/",
        "Whisper/",
        "tesseractmodels/",
        ".env*"
    )) {
        if ($gitignore -notmatch [regex]::Escape($pattern)) {
            $failures.Add(".gitignore must ignore $pattern.")
        }
    }

    $configText = Get-Content ".\.codex\config.toml" -Raw
    foreach ($forbiddenWebArtifact in @(
        "package.json",
        "pnpm-lock.yaml",
        "playwright.config.ts",
        "playwright.config.mjs",
        "lighthouserc.cjs",
        ".htmlhintrc",
        "eslint.config.mjs",
        "stylelint.config.mjs",
        "dependency-cruiser.config.cjs",
        "knip.json"
    )) {
        if ($configText -notmatch [regex]::Escape($forbiddenWebArtifact)) {
            $failures.Add(".codex/config.toml must list forbidden web artifact $forbiddenWebArtifact.")
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Error $failure
        }
        exit 1
    }

    Write-Host "LLPlayer documentation coverage verification completed."
}
finally {
    Pop-Location
}

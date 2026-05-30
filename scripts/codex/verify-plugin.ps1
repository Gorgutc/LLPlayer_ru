$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $failures = New-Object System.Collections.Generic.List[string]

    function Require-Path($Path, $Message) {
        if (-not (Test-Path $Path)) {
            $failures.Add($Message)
        }
    }

    Require-Path ".\AGENTS.md" "AGENTS.md is required."
    Require-Path ".\Plugins\llplayer-codex\.codex-plugin\plugin.json" "LLPlayer Codex plugin manifest is required."
    Require-Path ".\.agents\plugins\marketplace.json" "Marketplace manifest is required."
    Require-Path ".\.codex\hooks.json" ".codex/hooks.json is required."
    Require-Path ".\.codex\config.toml" ".codex/config.toml is required."

    if (Test-Path ".\.codex\config.toml") {
        $allowedDirectAgentKeys = @(
            "max_threads",
            "max_depth",
            "job_max_runtime_seconds",
            "interrupt_message"
        )
        $invalidDirectAgentKeys = New-Object System.Collections.Generic.List[string]
        $inAgentsTable = $false
        $lineNumber = 0
        foreach ($line in Get-Content ".\.codex\config.toml") {
            $lineNumber++
            if ($line -match '^\s*\[[^\]]+\]\s*$') {
                $inAgentsTable = $line -match '^\s*\[agents\]\s*$'
                continue
            }
            if (-not $inAgentsTable) {
                continue
            }
            if ($line -match '^\s*([A-Za-z0-9_-]+)\s*=') {
                $key = $Matches[1]
                if ($key -notin $allowedDirectAgentKeys) {
                    $invalidDirectAgentKeys.Add("line $lineNumber agents.$key")
                }
            }
        }
        if ($invalidDirectAgentKeys.Count -gt 0) {
            $failures.Add(".codex/config.toml has invalid direct [agents] values: $($invalidDirectAgentKeys -join ', '). [agents] is reserved for Codex agent settings; move custom metadata outside this table or define a role table.")
        }
    }

    $plugin = Get-Content ".\Plugins\llplayer-codex\.codex-plugin\plugin.json" -Raw | ConvertFrom-Json
    if ($plugin.name -ne "llplayer-codex") {
        $failures.Add("plugin.json name must be llplayer-codex.")
    }
    if ($plugin.skills -ne "./skills/") {
        $failures.Add("plugin.json skills must point to ./skills/.")
    }

    $marketplace = Get-Content ".\.agents\plugins\marketplace.json" -Raw | ConvertFrom-Json
    $entry = @($marketplace.plugins | Where-Object { $_.name -eq "llplayer-codex" })[0]
    if (-not $entry) {
        $failures.Add("Marketplace is missing llplayer-codex entry.")
    } elseif ($entry.source.path -ne "./Plugins/llplayer-codex") {
        $failures.Add("Marketplace source path must be ./Plugins/llplayer-codex on this Windows repository.")
    } elseif (-not (Test-Path $entry.source.path)) {
        $failures.Add("Marketplace source path does not resolve to the plugin directory.")
    }

    $requiredSkills = @(
        "llplayer-bootstrap",
        "llplayer-rules",
        "llplayer-product-contract",
        "llplayer-dotnet-rules",
        "llplayer-context-keeper",
        "llplayer-spec-guardian",
        "llplayer-frozen-decisions",
        "llplayer-quality-gate",
        "llplayer-quality-tooling",
        "llplayer-deadwood-reuse-audit",
        "llplayer-instruction-drift",
        "llplayer-ship",
        "llplayer-runtime-assets",
        "llplayer-packaging-release",
        "llplayer-wpf-xaml-review"
    )

    foreach ($skill in $requiredSkills) {
        $skillPath = ".\Plugins\llplayer-codex\skills\$skill\SKILL.md"
        Require-Path $skillPath "Missing skill $skillPath."
        if (Test-Path $skillPath) {
            $text = Get-Content $skillPath -Raw
            if ($text -notmatch "(?s)^---\s*name:\s*$skill\s*description:\s*Use when .+?---") {
                $failures.Add("Skill $skill must have frontmatter with matching name and Use when description.")
            }
        }
    }

    $requiredAgents = @(
        "tech_stack_cartographer",
        "media_runtime_mapper",
        "wpf_xaml_reviewer",
        "dotnet_quality_guardian",
        "native_dependency_auditor",
        "packaging_release_reviewer",
        "instruction_drift_auditor",
        "codex_infra_architect",
        "verification_reviewer",
        "deadwood_reuse_auditor"
    )
    foreach ($agent in $requiredAgents) {
        $agentPath = ".\.codex\agents\$agent.toml"
        Require-Path $agentPath "Missing agent .codex/agents/$agent.toml."
        if (Test-Path $agentPath) {
            $agentText = Get-Content $agentPath -Raw
            if ($agentText -notmatch 'sandbox_mode\s*=\s*"read-only"') {
                $failures.Add("Agent .codex/agents/$agent.toml must use sandbox_mode = `"read-only`".")
            }
        }
    }

    $requiredDocs = @(
        "README",
        "bootstrap",
        "architecture",
        "technical-stack",
        "orchestration",
        "verification",
        "quality-tooling",
        "code_review",
        "archive_policy",
        "plan_template",
        "frozen-decisions",
        "skill-map",
        "migration-from-source-repos",
        "product-behavior-contract",
        "wpf-design-contract",
        "media-runtime-contract",
        "config-data-contract",
        "dependency-baseline",
        "manual-smoke-matrix",
        "subagent-review-matrix"
    )
    foreach ($doc in $requiredDocs) {
        Require-Path ".\docs\agent\$doc.md" "Missing docs/agent/$doc.md."
    }

    $requiredScripts = @(
        "check-environment",
        "verify-fast",
        "verify",
        "verify-plugin",
        "verify-doc-coverage",
        "verify-frozen",
        "audit-frozen",
        "ship"
    )
    foreach ($script in $requiredScripts) {
        Require-Path ".\scripts\codex\$script.ps1" "Missing scripts/codex/$script.ps1."
    }

    foreach ($pointer in @("CLAUDE.md", "GEMINI.md")) {
        if ((Test-Path $pointer) -and ((Get-Content $pointer -Raw) -notmatch "AGENTS\.md")) {
            $failures.Add("$pointer must point to AGENTS.md.")
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Error $failure
        }
        exit 1
    }

    Write-Host "LLPlayer Codex plugin verification completed."
}
finally {
    Pop-Location
}

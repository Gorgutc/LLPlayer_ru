$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $failures = New-Object System.Collections.Generic.List[string]

    function Test-MarkerAt([string]$Text, [int]$Index, [string]$Marker) {
        return $Index + $Marker.Length -le $Text.Length -and
            [string]::CompareOrdinal($Text, $Index, $Marker, 0, $Marker.Length) -eq 0
    }

    function Get-ActiveGuardText([string]$Text, [string]$Kind) {
        if ($Kind -eq "none") {
            return [pscustomobject]@{ Valid = $true; Text = $Text }
        }

        if ($Kind -eq "html") {
            $open = "<!--"
            $close = "-->"
        }
        elseif ($Kind -eq "powershell") {
            $open = "<#"
            $close = "#>"
        }
        else {
            throw "Unknown guard-text kind '$Kind'."
        }

        $builder = [System.Text.StringBuilder]::new()
        $inComment = $false
        $index = 0
        while ($index -lt $Text.Length) {
            if (-not $inComment) {
                if (Test-MarkerAt $Text $index $open) {
                    [void]$builder.Append(' ' * $open.Length)
                    $inComment = $true
                    $index += $open.Length
                    continue
                }
                if (Test-MarkerAt $Text $index $close) {
                    return [pscustomobject]@{ Valid = $false; Text = "" }
                }
                [void]$builder.Append($Text[$index])
                $index++
                continue
            }

            if (Test-MarkerAt $Text $index $open) {
                return [pscustomobject]@{ Valid = $false; Text = "" }
            }
            if (Test-MarkerAt $Text $index $close) {
                [void]$builder.Append(' ' * $close.Length)
                $inComment = $false
                $index += $close.Length
                continue
            }
            if ($Text[$index] -eq "`r" -or $Text[$index] -eq "`n") {
                [void]$builder.Append($Text[$index])
            }
            else {
                [void]$builder.Append(' ')
            }
            $index++
        }

        if ($inComment) {
            return [pscustomobject]@{ Valid = $false; Text = "" }
        }
        return [pscustomobject]@{ Valid = $true; Text = $builder.ToString() }
    }

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

    function Require-UniqueCaseSensitiveText($Path, $Pattern, $Message) {
        if (-not (Test-Path $Path)) {
            $failures.Add("Missing $Path.")
            return
        }

        $extension = [System.IO.Path]::GetExtension($Path)
        $kind = if ($extension -ieq ".md") { "html" } elseif ($extension -ieq ".ps1") { "powershell" } else { "none" }
        $guardText = Get-ActiveGuardText (Get-Content $Path -Raw) $kind
        if (-not $guardText.Valid) {
            $failures.Add("$Path contains unbalanced or nested native block-comment markers.")
            return
        }
        $matchCount = [regex]::Matches($guardText.Text, $Pattern).Count
        if ($matchCount -ne 1) {
            $failures.Add("$Message Found $matchCount case-sensitive matches.")
        }
    }

    function Require-CanonicalFileText($Path, $ExpectedText, $Message) {
        if (-not (Test-Path $Path)) {
            $failures.Add("Missing $Path.")
            return
        }

        $actual = ((Get-Content $Path -Raw) -replace "`r`n", "`n").Trim()
        $expected = ($ExpectedText -replace "`r`n", "`n").Trim()
        if ($actual -cne $expected) {
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
        "subagent-review-matrix.md",
        "dubbing-contract.md"
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

    $testCommandSurfaces = @(
        @{ Path = ".\AGENTS.md"; Pattern = '(?m)^dotnet test --no-restore -warnaserror \.\\FlyleafLibTests\r?$' },
        @{ Path = ".\RUN_INSTRUCTIONS.md"; Pattern = '(?m)^dotnet test --no-restore -warnaserror \.\\FlyleafLibTests\r?$' },
        @{ Path = ".\.github\workflows\build.yml"; Pattern = '(?m)^      run: dotnet test --no-restore -warnaserror \.\\FlyleafLibTests\r?$' },
        @{ Path = ".\.codex\agents\dotnet_quality_guardian.toml"; Pattern = '(?m)^Required baseline is .* dotnet test --no-restore -warnaserror \.\\\\FlyleafLibTests\.\r?$' },
        @{ Path = ".\docs\agent\verification.md"; Pattern = '(?m)^dotnet test --no-restore -warnaserror \.\\FlyleafLibTests\r?$' },
        @{ Path = ".\docs\agent\quality-tooling.md"; Pattern = '(?m)^- `dotnet test --no-restore -warnaserror \.\\FlyleafLibTests`\r?$' },
        @{ Path = ".\docs\agent\dubbing\dubbing-roadmap.md"; Pattern = '(?m)^- Documented baseline commands stay green: .*`dotnet test --no-restore -warnaserror \.\\FlyleafLibTests`, and `verify`\r?$' },
        @{ Path = ".\docs\agent\dubbing\dubbing-spec.md"; Pattern = '(?m)^  `dotnet test --no-restore -warnaserror \.\\FlyleafLibTests`; `verify-fast`/`verify` \(frozen\)\r?$' },
        @{ Path = ".\Plugins\llplayer-codex\skills\llplayer-dotnet-rules\SKILL.md"; Pattern = '(?m)^dotnet test --no-restore -warnaserror \.\\FlyleafLibTests\r?$' },
        @{ Path = ".\Plugins\llplayer-codex\skills\llplayer-quality-tooling\SKILL.md"; Pattern = '(?m)^- `dotnet test --no-restore -warnaserror \.\\FlyleafLibTests`\.\r?$' }
    )
    foreach ($surface in $testCommandSurfaces) {
        Require-UniqueCaseSensitiveText $surface.Path $surface.Pattern "$($surface.Path) must contain exactly one surface-specific canonical warning-clean test command."
        Require-UniqueCaseSensitiveText $surface.Path '(?i)dotnet test' "$($surface.Path) must declare exactly one dotnet test command in any casing."
    }

    $canonicalDotnetGuardian = @'
name = "dotnet_quality_guardian"
description = "Reviews .NET build, test, analyzer, and project-file quality for LLPlayer."
sandbox_mode = "read-only"

developer_instructions = """
Review C#, csproj, publish profile, and test changes.
Prefer dotnet restore/build/test evidence over assumptions.
Required baseline is dotnet restore -warnaserror, dotnet build --no-restore -warnaserror .\\LLPlayer, dotnet build --no-restore -warnaserror .\\Plugins\\YoutubeDL, dotnet test --no-restore -warnaserror .\\FlyleafLibTests.
Do not recommend npm/pnpm/browser gates.
Return actionable findings first.
"""
'@
    Require-CanonicalFileText ".\.codex\agents\dotnet_quality_guardian.toml" $canonicalDotnetGuardian "dotnet_quality_guardian must match the canonical read-only warning-clean reviewer contract."

    $plainCommandPattern = '(?m)^dotnet test --no-restore -warnaserror \.\\FlyleafLibTests\r?$'
    $plainCommand = 'dotnet test --no-restore -warnaserror .\FlyleafLibTests'
    $guardFixtures = @(
        @{ Description = "suffix path"; Kind = "none"; Text = "$plainCommand.evil" },
        @{ Description = "shell chaining"; Kind = "none"; Text = "$plainCommand; exit 0" },
        @{ Description = "second filtered command"; Kind = "none"; Text = "$plainCommand`n$plainCommand --filter FullyQualifiedName=__T03_NoSuchTest__" },
        @{ Description = "line comment decoy"; Kind = "none"; Text = "# $plainCommand" },
        @{ Description = "HTML comment decoy"; Kind = "html"; Text = "<!--`n$plainCommand`n-->" },
        @{ Description = "unclosed HTML comment"; Kind = "html"; Text = "<!--`n$plainCommand" },
        @{ Description = "nested HTML comment"; Kind = "html"; Text = "<!--`n<!--`n-->`n$plainCommand" },
        @{ Description = "unmatched HTML comment close"; Kind = "html"; Text = "-->`n$plainCommand" },
        @{ Description = "inline HTML marker in Markdown"; Kind = "html"; Text = 'dotnet test --no<!--hidden-->-restore -warnaserror .\FlyleafLibTests' },
        @{ Description = "inline HTML marker in TOML or YAML"; Kind = "none"; Text = 'dotnet test --no<!--hidden-->-restore -warnaserror .\FlyleafLibTests' },
        @{ Description = "balanced PowerShell block comment"; Kind = "powershell"; Text = "<#`n$plainCommand`n#>" },
        @{ Description = "unclosed PowerShell block comment"; Kind = "powershell"; Text = "<#`n$plainCommand" },
        @{ Description = "nested PowerShell block comment"; Kind = "powershell"; Text = "<#`n<#`n#>`n$plainCommand" },
        @{ Description = "unmatched PowerShell comment close"; Kind = "powershell"; Text = "#>`n$plainCommand" },
        @{ Description = "inline PowerShell marker in script"; Kind = "powershell"; Text = 'dotnet test --no<#hidden#>-restore -warnaserror .\FlyleafLibTests' },
        @{ Description = "inline PowerShell marker in TOML or YAML"; Kind = "none"; Text = 'dotnet test --no<#hidden#>-restore -warnaserror .\FlyleafLibTests' }
    )

    foreach ($kind in @("none", "html", "powershell")) {
        $positiveGuard = Get-ActiveGuardText $plainCommand $kind
        if (-not $positiveGuard.Valid -or
            [regex]::Matches($positiveGuard.Text, $plainCommandPattern).Count -ne 1 -or
            [regex]::Matches($positiveGuard.Text, '(?i)dotnet test').Count -ne 1) {
            $failures.Add("Canonical test-command guard rejected its '$kind' positive fixture.")
        }
    }
    foreach ($fixture in $guardFixtures) {
        $activeFixture = Get-ActiveGuardText $fixture.Text $fixture.Kind
        $exactCount = if ($activeFixture.Valid) { [regex]::Matches($activeFixture.Text, $plainCommandPattern).Count } else { 0 }
        $commandCount = if ($activeFixture.Valid) { [regex]::Matches($activeFixture.Text, '(?i)dotnet test').Count } else { 0 }
        if ($activeFixture.Valid -and $exactCount -eq 1 -and $commandCount -eq 1) {
            $failures.Add("Canonical test-command guard accepted adversarial fixture: $($fixture.Description).")
        }
    }

    $canonicalVerifyFast = @'
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    & ".\scripts\codex\check-environment.ps1"
    & ".\scripts\codex\verify-plugin.ps1"
    & ".\scripts\codex\verify-doc-coverage.ps1"
    & ".\scripts\codex\verify-frozen.ps1"
    & ".\scripts\codex\verify-full-gate.ps1"
    & ".\scripts\codex\verify-build-workflow.ps1"
    & ".\scripts\codex\verify-release-workflow.ps1"
    & ".\scripts\codex\check-dub-licenses.ps1"
    Write-Host "LLPlayer fast verification completed."
}
finally {
    Pop-Location
}
'@
    Require-CanonicalFileText ".\scripts\codex\verify-fast.ps1" $canonicalVerifyFast "Fast verification must match the canonical fail-closed validator sequence."

    Require-Text ".\docs\agent\verification.md" "Risk-Based Coverage Policy" "Verification docs must define the risk-based coverage policy."
    Require-Text ".\docs\agent\verification.md" "intentional RED evidence" "Verification docs must require intentional RED evidence where applicable."
    Require-Text ".\docs\agent\verification.md" "No global coverage percentage" "Verification docs must reject a global coverage percentage gate."
    Require-Text ".\docs\agent\verification.md" "no safe deterministic" "Verification docs must define the no-safe-seam boundary."
    Require-Text ".\docs\agent\plan_template.md" "Coverage decision" "Plan template must require an explicit coverage decision."
    Require-Text ".\docs\agent\plan_template.md" "intentional RED evidence" "Plan template must route intentional RED evidence."
    Require-Text ".\docs\agent\plan_template.md" "Never use a global coverage percentage" "Plan template must reject a global coverage percentage gate."
    Require-UniqueCaseSensitiveText ".\scripts\codex\verify-fast.ps1" '(?m)^    & "\.\\scripts\\codex\\verify-full-gate\.ps1"\s*$' "Fast verification must execute the full-gate contract validator."
    Require-UniqueCaseSensitiveText ".\docs\agent\verification.md" '(?m)^- `scripts/codex/verify-full-gate\.ps1`\r?$' "Verification inventory must list the executable full-gate validator."
    Require-UniqueCaseSensitiveText ".\docs\agent\verification.md" '`verify-full-gate\.ps1` protects' "Coverage policy must explain what the executable full-gate validator protects."
    Require-UniqueCaseSensitiveText ".\docs\agent\plan_template.md" 'docs/agent/subagent-review-matrix\.md' "Plan template must route changed paths through the reviewer matrix."
    Require-UniqueCaseSensitiveText ".\docs\agent\plan_template.md" 'spawned `/review` including `verification_reviewer`' "Plan template must require the final spawned review."
    Require-UniqueCaseSensitiveText ".\Plugins\llplayer-codex\skills\llplayer-dotnet-rules\SKILL.md" 'docs/agent/subagent-review-matrix\.md' ".NET skill must route changed paths through the reviewer matrix."
    Require-UniqueCaseSensitiveText ".\Plugins\llplayer-codex\skills\llplayer-dotnet-rules\SKILL.md" 'spawned `/review` including `verification_reviewer`' ".NET skill must require the final spawned review."

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

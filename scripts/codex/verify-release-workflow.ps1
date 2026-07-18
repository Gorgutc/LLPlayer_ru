$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$validator = Join-Path $PSScriptRoot "validate-release-token.ps1"
$testingWorkflow = Join-Path $repoRoot ".github\workflows\testing-release.yml"
$stableWorkflow = Join-Path $repoRoot ".github\workflows\stable-release.yml"
$packageAction = Join-Path $repoRoot ".github\actions\build-package\action.yml"

function Assert-TokenPass([string]$Kind, [string]$Value, [string]$Expected = $Value) {
    $actual = & $validator -Kind $Kind -Value $Value
    if ($actual -ne $Expected) {
        throw "Expected $Kind token '$Expected', got '$actual'."
    }
}

function Assert-TokenFail([string]$Kind, [string]$Value) {
    try {
        $null = & $validator -Kind $Kind -Value $Value
    }
    catch {
        return
    }
    $displayValue = $Value.Replace("`r", "<CR>").Replace("`n", "<LF>")
    throw "Unsafe $Kind token unexpectedly passed validation: '$displayValue'."
}

function Require-Fragment([string]$Text, [string]$Fragment, [string]$Message) {
    if ($Text.IndexOf($Fragment, [System.StringComparison]::Ordinal) -lt 0) {
        throw $Message
    }
}

function Forbid-Fragment([string]$Text, [string]$Fragment, [string]$Message) {
    if ($Text.IndexOf($Fragment, [System.StringComparison]::Ordinal) -ge 0) {
        throw $Message
    }
}

function Get-StepBlock([string]$Path, [string]$StepName) {
    $lines = @(Get-Content -LiteralPath $Path)
    $marker = "- name: $StepName"
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index].Trim() -ne $marker) {
            continue
        }

        $indent = $lines[$index].Length - $lines[$index].TrimStart().Length
        $block = New-Object System.Collections.Generic.List[string]
        for ($blockIndex = $index; $blockIndex -lt $lines.Count; $blockIndex++) {
            $line = $lines[$blockIndex]
            $leading = $line.Length - $line.TrimStart().Length
            if ($blockIndex -gt $index -and $line.Trim() -and $leading -le $indent -and
                $line.TrimStart().StartsWith("- ", [System.StringComparison]::Ordinal)) {
                break
            }
            $block.Add($line)
        }
        return $block -join [Environment]::NewLine
    }
    throw "Workflow step '$StepName' is missing from $Path."
}

function Require-ExactStepBlock([string]$Path, [string]$StepName, [string[]]$ExpectedLines) {
    $marker = "- name: $StepName"
    $markerCount = @(Get-Content -LiteralPath $Path | Where-Object {
        [string]::Equals($_.Trim(), $marker, [System.StringComparison]::Ordinal)
    }).Count
    if ($markerCount -ne 1) {
        throw "$Path must contain exactly one '$StepName' step; found $markerCount."
    }
    $block = Get-StepBlock $Path $StepName
    $actualLines = @($block -split '\r?\n' | Where-Object { $_.Trim() })
    if ($actualLines.Count -ne $ExpectedLines.Count) {
        throw "Workflow step '$StepName' in $Path must contain exactly $($ExpectedLines.Count) nonblank lines; found $($actualLines.Count)."
    }
    for ($index = 0; $index -lt $ExpectedLines.Count; $index++) {
        if (-not [string]::Equals($actualLines[$index], $ExpectedLines[$index], [System.StringComparison]::Ordinal)) {
            throw "Workflow step '$StepName' in $Path drifted at line $($index + 1): '$($actualLines[$index])'."
        }
    }
}

function Assert-ExactCompositeActionUses([string]$Text, [string]$Source) {
    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    $lines = @($normalized -split "`n")
    $expectedReference = "actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1"
    $runsIndices = @()
    $stepsIndices = @()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ([string]::Equals($lines[$index], "runs:", [System.StringComparison]::Ordinal)) {
            $runsIndices += $index
        }
        if ([string]::Equals($lines[$index], "  steps:", [System.StringComparison]::Ordinal)) {
            $stepsIndices += $index
        }
    }
    if ($runsIndices.Count -ne 1 -or $stepsIndices.Count -ne 1 -or $stepsIndices[0] -le $runsIndices[0]) {
        throw "$Source must contain exactly one canonical runs.steps mapping."
    }

    $runsHeader = @(
        for ($index = $runsIndices[0] + 1; $index -le $stepsIndices[0]; $index++) {
            $trimmed = $lines[$index].Trim()
            if ($trimmed -and -not $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
                $lines[$index]
            }
        }
    )
    if ($runsHeader.Count -ne 2 -or
        -not [string]::Equals($runsHeader[0], '  using: "composite"', [System.StringComparison]::Ordinal) -or
        -not [string]::Equals($runsHeader[1], "  steps:", [System.StringComparison]::Ordinal)) {
        throw "$Source must bind exact canonical using and steps keys directly beneath runs."
    }

    $rootKeys = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }
        $leading = $line.Length - $line.TrimStart().Length
        if ($leading -ne 0) {
            continue
        }
        if ($line -cnotmatch '^(?<key>name|description|inputs|outputs|runs):(?:\s.*)?$') {
            throw "$Source contains a non-canonical root action mapping: '$trimmed'."
        }
        $rootKeys.Add($Matches["key"])
    }
    $expectedRootKeys = @("name", "description", "inputs", "outputs", "runs")
    if ($rootKeys.Count -ne $expectedRootKeys.Count) {
        throw "$Source must contain exactly the canonical root action mappings."
    }
    for ($index = 0; $index -lt $expectedRootKeys.Count; $index++) {
        if ($rootKeys[$index] -cne $expectedRootKeys[$index]) {
            throw "$Source must keep canonical root action mapping order."
        }
    }

    $usesReferences = New-Object System.Collections.Generic.List[string]
    $stepCount = 0
    for ($index = $stepsIndices[0] + 1; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }

        $leading = $line.Length - $line.TrimStart().Length
        if ($leading -eq 4) {
            if ($line -cnotmatch '^    - name:\s+\S.*$') {
                throw "$Source contains a non-canonical composite step mapping at line $($index + 1): '$trimmed'."
            }
            $stepCount++
            continue
        }

        if ($leading -eq 6) {
            if ($line -cnotmatch '^      (?<key>uses|with|shell|run|id|env):(?:\s.*)?$') {
                throw "$Source contains a non-canonical composite step property at line $($index + 1): '$trimmed'."
            }
            if ($stepCount -eq 0) {
                throw "$Source contains a composite step property before the first canonical step."
            }
            if ($Matches["key"] -ceq "uses") {
                $reference = $line.Substring($line.IndexOf(":", [System.StringComparison]::Ordinal) + 1).Trim()
                $commentIndex = $reference.IndexOf(" #", [System.StringComparison]::Ordinal)
                if ($commentIndex -ge 0) {
                    $reference = $reference.Substring(0, $commentIndex).TrimEnd()
                }
                $usesReferences.Add($reference)
            }
            continue
        }

        if ($leading -lt 8) {
            throw "$Source contains non-canonical indentation in composite steps at line $($index + 1): '$trimmed'."
        }
    }

    if ($stepCount -eq 0) {
        throw "$Source composite action must contain at least one canonical step."
    }
    if ($usesReferences.Count -ne 1) {
        throw "$Source must contain exactly one composite-action uses reference; found $($usesReferences.Count)."
    }
    if ($usesReferences[0] -cne $expectedReference) {
        throw "$Source must use only the canonical pinned action reference '$expectedReference'."
    }
}

function Replace-FirstOrdinal(
    [string]$Text,
    [string]$OldValue,
    [string]$NewValue,
    [string]$Description
) {
    $index = $Text.IndexOf($OldValue, [System.StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "Composite-action adversarial fixture could not find $Description."
    }

    return $Text.Remove($index, $OldValue.Length).Insert($index, $NewValue)
}

function Assert-CompositeActionUsesRejected(
    [string]$Text,
    [string]$Description,
    [string]$ExpectedErrorFragment
) {
    try {
        Assert-ExactCompositeActionUses $Text "adversarial composite-action fixture"
    }
    catch {
        $message = $_.Exception.Message
        if ($message.IndexOf($ExpectedErrorFragment, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Composite-action fixture '$Description' failed for the wrong reason: $message"
        }
        return
    }

    throw "Composite-action uses validator accepted adversarial fixture: $Description."
}

function Assert-NoExpressionInRunBlock([string]$Path, [string]$ForbiddenExpression) {
    $insideRun = $false
    $runIndent = -1
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $Path) {
        $lineNumber++
        $leading = $line.Length - $line.TrimStart().Length
        if ($insideRun -and $line.Trim() -and $leading -le $runIndent) {
            $insideRun = $false
            $runIndent = -1
        }
        if ($line -match '^(\s*)(?:-\s+)?run:\s*(.*)$') {
            $runIndent = $Matches[1].Length
            $runValue = $Matches[2].Trim()
            if ($runValue.StartsWith("|", [System.StringComparison]::Ordinal) -or
                $runValue.StartsWith(">", [System.StringComparison]::Ordinal)) {
                $insideRun = $true
                continue
            }
            $insideRun = $false
            if ($runValue.IndexOf($ForbiddenExpression, [System.StringComparison]::Ordinal) -ge 0) {
                throw "$Path line $lineNumber interpolates '$ForbiddenExpression' directly inside an inline run command."
            }
            continue
        }
        if ($insideRun -and $line.IndexOf($ForbiddenExpression, [System.StringComparison]::Ordinal) -ge 0) {
            throw "$Path line $lineNumber interpolates '$ForbiddenExpression' directly inside a run block."
        }
    }
}

function Assert-RunFixtureRejected([string]$Yaml, [string]$Description) {
    $fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("llplayer-run-fixture-" + [guid]::NewGuid().ToString("N") + ".yml")
    try {
        [System.IO.File]::WriteAllText($fixture, $Yaml, [System.Text.UTF8Encoding]::new($false))
        $rejected = $false
        try {
            Assert-NoExpressionInRunBlock $fixture '${{'
        }
        catch {
            $rejected = $true
        }
        if (-not $rejected) {
            throw "Run-block scanner did not reject $Description."
        }
    }
    finally {
        Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath $validator)) {
    throw "Release token validator is missing: $validator"
}

foreach ($ref in @(
    "main",
    "codex/t13a-testing-release-hardening",
    "refs/heads/codex/t13a-testing-release-hardening",
    "refs/tags/v0.3.61+build.1",
    "0123456789abcdef0123456789abcdef01234567"
)) {
    Assert-TokenPass "Ref" $ref
}

foreach ($ref in @(
    "main; Write-Output owned",
    '$(Get-Process)',
    "--help",
    "main`nname=owned",
    "foo..bar",
    "foo@{bar",
    "refs/pull/1/head",
    "/absolute",
    "topic/.hidden",
    "topic/bad.lock",
    "abcdef1"
)) {
    Assert-TokenFail "Ref" $ref
}

foreach ($tag in @("v0.3.61", "v0.3.61+build.1", "release-2026.07.11")) {
    Assert-TokenPass "Tag" $tag
}
foreach ($tag in @("v0.3.61;owned", "release/2026", "tag`nname=owned", '$(Get-Process)', "bad..tag")) {
    Assert-TokenFail "Tag" $tag
}

Assert-TokenPass "Hash" "DEADBEEF1234" "deadbeef1234"
foreach ($hash in @("deadbee;owned", "123456", "xyzxyz1", "deadbee`nname=owned")) {
    Assert-TokenFail "Hash" $hash
}

Assert-TokenPass "Archive" "LLPlayer-testing-v0.3.61-deadbeef1234.7z"
Assert-TokenPass "Archive" "LLPlayer-testing-v0.3.61+build.1-deadbeef1234.7z"
Assert-TokenPass "Archive" "LLPlayer-v0.3.61-x64.7z"
foreach ($archive in @(
    "..\LLPlayer-testing-v0.3.61-deadbeef1234.7z",
    "LLPlayer-testing-v0.3.61-deadbeef1234.7z;owned",
    "LLPlayer-testing-v0.3.61-deadbeef1234`nname=owned.7z",
    "LLPlayer-testing-bad..tag-deadbeef1234.7z"
)) {
    Assert-TokenFail "Archive" $archive
}

$outputProbe = Join-Path ([System.IO.Path]::GetTempPath()) ("llplayer-release-output-" + [guid]::NewGuid().ToString("N"))
try {
    $null = & $validator -Kind Ref -Value "main" -OutputName "value" -OutputFile $outputProbe
    $expectedOutput = "value=main$([Environment]::NewLine)"
    $expectedBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($expectedOutput)
    $actualBytes = [System.IO.File]::ReadAllBytes($outputProbe)
    if ($actualBytes.Length -ne $expectedBytes.Length) {
        throw "Validated GitHub output must be exactly one BOM-free UTF-8 line."
    }
    for ($index = 0; $index -lt $expectedBytes.Length; $index++) {
        if ($actualBytes[$index] -ne $expectedBytes[$index]) {
            throw "Validated GitHub output must be exactly one BOM-free UTF-8 line."
        }
    }
}
finally {
    Remove-Item -LiteralPath $outputProbe -Force -ErrorAction SilentlyContinue
}

$inlineRunFixture = @'
steps:
  - run: pwsh -Command "${{ inputs.commit }}"
'@
$chompedRunFixture = @'
steps:
  - run: |-
      pwsh -Command "${{ inputs.commit }}"
'@
$indentedRunFixture = @'
steps:
  - run: >2+
      pwsh -Command "${{ inputs.commit }}"
'@
Assert-RunFixtureRejected $inlineRunFixture "an inline run interpolation"
Assert-RunFixtureRejected $chompedRunFixture "a chomped block-scalar interpolation"
Assert-RunFixtureRejected $indentedRunFixture "an indented block-scalar interpolation"

$workflowText = Get-Content -LiteralPath $testingWorkflow -Raw
$stableText = Get-Content -LiteralPath $stableWorkflow -Raw
$actionText = Get-Content -LiteralPath $packageAction -Raw

& (Join-Path $PSScriptRoot "verify-testing-release-boundary.ps1")
& (Join-Path $PSScriptRoot "verify-stable-release-boundary.ps1")

Assert-ExactCompositeActionUses $actionText "canonical build-package action"

$mutableSetupActionFixture = Replace-FirstOrdinal `
    $actionText `
    "actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1" `
    "actions/setup-dotnet@v5" `
    "the immutable setup-dotnet reference"
Assert-CompositeActionUsesRejected `
    $mutableSetupActionFixture `
    "mutable setup-dotnet reference" `
    "canonical pinned action reference"

$extraPinnedActionFixture = Replace-FirstOrdinal `
    $actionText `
    "    - name: Restore dependencies" `
    "    - name: Unauthorized pinned action`n      uses: example/unapproved@0123456789abcdef0123456789abcdef01234567`n`n    - name: Restore dependencies" `
    "the Restore dependencies step"
Assert-CompositeActionUsesRejected `
    $extraPinnedActionFixture `
    "additional pinned action" `
    "exactly one composite-action uses reference"

$quotedUsesKeyFixture = Replace-FirstOrdinal `
    $actionText `
    "      uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1" `
    '      "uses": actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1' `
    "the canonical uses key"
Assert-CompositeActionUsesRejected `
    $quotedUsesKeyFixture `
    "quoted uses key" `
    "non-canonical composite step property"

$escapedUsesKeyFixture = Replace-FirstOrdinal `
    $actionText `
    "      uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1" `
    '      "u\u0073es": example/unapproved@main' `
    "the canonical uses key for the escaped-key fixture"
Assert-CompositeActionUsesRejected `
    $escapedUsesKeyFixture `
    "escaped uses key" `
    "non-canonical composite step property"

$explicitUsesKeyFixture = Replace-FirstOrdinal `
    $actionText `
    "      uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1" `
    "      ? uses`n      : example/unapproved@main" `
    "the canonical uses key for the explicit-key fixture"
Assert-CompositeActionUsesRejected `
    $explicitUsesKeyFixture `
    "explicit uses key" `
    "non-canonical composite step property"

$flowStepFixture = Replace-FirstOrdinal `
    $actionText `
    "    - name: Restore dependencies" `
    "    - { name: Hidden action, uses: example/unapproved@main }`n`n    - name: Restore dependencies" `
    "the Restore dependencies step for the flow-step fixture"
Assert-CompositeActionUsesRejected `
    $flowStepFixture `
    "flow-style action step" `
    "non-canonical composite step mapping"

$anchoredStepFixture = Replace-FirstOrdinal `
    $actionText `
    "    - name: Restore dependencies" `
    "    - &hidden`n      name: Hidden action`n      uses: example/unapproved@main`n`n    - name: Restore dependencies" `
    "the Restore dependencies step for the anchored-step fixture"
Assert-CompositeActionUsesRejected `
    $anchoredStepFixture `
    "anchor-derived action step" `
    "non-canonical composite step mapping"

$blockScalarDecoyFixture = Replace-FirstOrdinal `
    $actionText `
    'description: "Builds the solution, clean, archive with 7z"' `
    "" `
    "the canonical description for the block-scalar fixture"
$blockScalarDecoyFixture = Replace-FirstOrdinal `
    $blockScalarDecoyFixture `
    "  steps:" `
    '  "s\u0074eps":' `
    "the canonical steps key for the block-scalar fixture"
$blockScalarDecoyFixture = Replace-FirstOrdinal `
    $blockScalarDecoyFixture `
    "    - name: Restore dependencies" `
    "    - name: Hidden action`n      uses: example/unapproved@main`n`n    - name: Restore dependencies" `
    "the Restore dependencies step for the block-scalar fixture"
$blockScalarDecoyFixture += "`ndescription: |`n  steps:`n    - name: Setup .NET`n      uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1`n"
Assert-CompositeActionUsesRejected `
    $blockScalarDecoyFixture `
    "block-scalar steps decoy" `
    "exact canonical using and steps keys directly beneath runs"

Assert-NoExpressionInRunBlock $testingWorkflow '${{'
Assert-NoExpressionInRunBlock $stableWorkflow '${{'
Assert-NoExpressionInRunBlock $packageAction '${{ inputs.archive-name }}'

Require-ExactStepBlock $packageAction "Setup .NET" @(
    "    - name: Setup .NET",
    "      uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0",
    "      with:",
    "        dotnet-version: 10.0.x"
)

foreach ($fragment in @(
    'git rev-parse --short ${{ github.event.inputs.commit }}',
    '$tag = ${{ steps.latest-tag.outputs.result }}',
    '$hash = "${{ steps.short-hash.outputs.sha }}"',
    'gh release upload v0.0.1 ${{ steps.archive-name.outputs.name }} --clobber',
    '$out = "${{ inputs.archive-name }}"'
)) {
    Forbid-Fragment ($workflowText + $stableText + $actionText) $fragment "Unsafe release interpolation returned: $fragment"
}

Require-Fragment $actionText 'ARCHIVE_NAME: ${{ inputs.archive-name }}' "Build/package action must pass archive-name through env."
Require-Fragment $actionText '$out = "$env:ARCHIVE_NAME"' "Build/package action must read archive-name from env."

Write-Host "Release workflow input/output and token-boundary verification completed."

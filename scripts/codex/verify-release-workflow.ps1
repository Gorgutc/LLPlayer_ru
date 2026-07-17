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

function Require-StepFragments([string]$Path, [string]$StepName, [string[]]$Fragments) {
    $block = Get-StepBlock $Path $StepName
    foreach ($fragment in $Fragments) {
        Require-Fragment $block $fragment "Workflow step '$StepName' is missing required fragment: $fragment"
    }
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

function Normalize-WorkflowText([string]$Text) {
    return (($Text -replace "`r`n", "`n") -replace "`r", "`n").TrimEnd("`n")
}

function Get-StableReleaseSteps([string]$Text, [string]$Source) {
    $normalized = Normalize-WorkflowText $Text
    $lines = @($normalized -split "`n")
    $jobs = @()
    $release = @()
    $steps = @()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ([string]::Equals($lines[$index], "jobs:", [System.StringComparison]::Ordinal)) {
            $jobs += $index
        }
        if ([string]::Equals($lines[$index], "  release:", [System.StringComparison]::Ordinal)) {
            $release += $index
        }
        if ([string]::Equals($lines[$index], "    steps:", [System.StringComparison]::Ordinal)) {
            $steps += $index
        }
    }
    if ($jobs.Count -ne 1 -or $release.Count -ne 1 -or $steps.Count -ne 1) {
        throw "$Source must contain exactly one canonical jobs.release.steps path."
    }
    if (-not ($jobs[0] -lt $release[0] -and $release[0] -lt $steps[0])) {
        throw "$Source jobs.release.steps hierarchy is malformed."
    }

    $jobNames = New-Object System.Collections.Generic.List[string]
    for ($index = $jobs[0] + 1; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $trimmed = $line.Trim()
        $indent = $line.Length - $line.TrimStart().Length
        if ($trimmed -and $indent -eq 0) {
            break
        }
        if (-not $trimmed -or $trimmed.StartsWith("#", [System.StringComparison]::Ordinal) -or $indent -ne 2) {
            continue
        }
        if ($line -cnotmatch '^  ([A-Za-z0-9_-]+):\s*(?:#.*)?$') {
            throw "$Source jobs contains a non-canonical job entry: '$trimmed'."
        }
        $jobNames.Add($Matches[1])
    }
    if ($jobNames.Count -ne 1 -or -not [string]::Equals(
        $jobNames[0],
        "release",
        [System.StringComparison]::Ordinal)) {
        throw "$Source jobs must contain only the protected release job."
    }

    $stepLines = New-Object System.Collections.Generic.List[string]
    for ($index = $steps[0] + 1; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $trimmed = $line.Trim()
        $indent = $line.Length - $line.TrimStart().Length
        if ($trimmed -and $indent -le 4) {
            break
        }
        $stepLines.Add($line)
    }
    if ($stepLines.Count -eq 0) {
        throw "$Source jobs.release.steps must not be empty."
    }
    return $stepLines.ToArray()
}

function Get-StableNamedStep([string[]]$StepLines, [string]$Name, [string]$Source) {
    $marker = "      - name: $Name"
    $indices = @()
    for ($index = 0; $index -lt $StepLines.Count; $index++) {
        if ([string]::Equals($StepLines[$index], $marker, [System.StringComparison]::Ordinal)) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source jobs.release.steps must contain exactly one '$Name' step; found $($indices.Count)."
    }

    $start = [int]$indices[0]
    $end = $StepLines.Count
    for ($index = $start + 1; $index -lt $StepLines.Count; $index++) {
        if ($StepLines[$index] -cmatch '^      - ') {
            $end = $index
            break
        }
    }
    return @($StepLines[$start..($end - 1)] | Where-Object { $_.Trim() })
}

function Assert-ExactStableStep(
    [string[]]$Actual,
    [string[]]$Expected,
    [string]$Description,
    [string]$Source
) {
    if ($Actual.Count -ne $Expected.Count) {
        throw "$Source $Description must contain exactly $($Expected.Count) nonblank lines; found $($Actual.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [string]::Equals($Actual[$index], $Expected[$index], [System.StringComparison]::Ordinal)) {
            throw "$Source $Description drifted at line $($index + 1): '$($Actual[$index])'."
        }
    }
}

function Assert-StableReleasePreflightContract([string]$Text, [string]$Source) {
    $stepLines = Get-StableReleaseSteps $Text $Source
    $normalized = Normalize-WorkflowText $Text
    $packageUses = @($normalized -split "`n" | Where-Object {
        [string]::Equals($_.Trim(), "uses: ./.github/actions/build-package", [System.StringComparison]::Ordinal)
    })
    if ($packageUses.Count -ne 1) {
        throw "$Source must invoke the shared build/package action exactly once; found $($packageUses.Count)."
    }

    $actualNames = New-Object System.Collections.Generic.List[string]
    foreach ($line in $stepLines) {
        $indent = $line.Length - $line.TrimStart().Length
        if ($indent -ne 6 -or -not $line.TrimStart().StartsWith("- ", [System.StringComparison]::Ordinal)) {
            continue
        }
        if ($line -cnotmatch '^      - name:\s*(.+?)\s*$') {
            throw "$Source jobs.release.steps contains an anonymous or non-canonical step: '$($line.Trim())'."
        }
        $actualNames.Add($Matches[1])
    }

    $expectedNames = @(
        "Checkout",
        "Setup .NET",
        "Full verification preflight",
        "Build & Package",
        "Create or update GitHub Draft Release & Upload Asset"
    )
    if ($actualNames.Count -ne $expectedNames.Count) {
        throw "$Source jobs.release.steps must contain exactly $($expectedNames.Count) named steps; found $($actualNames.Count)."
    }
    for ($index = 0; $index -lt $expectedNames.Count; $index++) {
        if (-not [string]::Equals($actualNames[$index], $expectedNames[$index], [System.StringComparison]::Ordinal)) {
            throw "$Source jobs.release.steps has unexpected order at position $($index + 1): '$($actualNames[$index])'."
        }
    }

    Assert-ExactStableStep (Get-StableNamedStep $stepLines "Checkout" $Source) @(
        "      - name: Checkout",
        "        uses: actions/checkout@v5",
        "        with:",
        '          ref: ${{ github.sha }}'
    ) "checkout step" $Source
    Assert-ExactStableStep (Get-StableNamedStep $stepLines "Setup .NET" $Source) @(
        "      - name: Setup .NET",
        "        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0",
        "        with:",
        "          dotnet-version: 10.0.x"
    ) ".NET setup step" $Source
    Assert-ExactStableStep (Get-StableNamedStep $stepLines "Full verification preflight" $Source) @(
        "      - name: Full verification preflight",
        "        shell: pwsh",
        "        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1"
    ) "full verification preflight step" $Source
    Assert-ExactStableStep (Get-StableNamedStep $stepLines "Build & Package" $Source) @(
        "      - name: Build & Package",
        "        uses: ./.github/actions/build-package",
        "        with:",
        '          archive-name: ${{ env.ARCHIVE_NAME }}'
    ) "build/package step" $Source
}

function Assert-StableContractRejected([string]$Text, [string]$Description) {
    $rejected = $false
    try {
        Assert-StableReleasePreflightContract $Text "adversarial fixture ($Description)"
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Stable Release preflight validator accepted adversarial fixture: $Description."
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

Assert-StableReleasePreflightContract $stableText "stable-release.yml"
$normalizedStableText = Normalize-WorkflowText $stableText

$stablePreflightBlock = @'
      - name: Full verification preflight
        shell: pwsh
        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
'@
$stablePackageBlock = @'
      - name: Build & Package
        uses: ./.github/actions/build-package
        with:
          archive-name: ${{ env.ARCHIVE_NAME }}
'@
Assert-StableContractRejected `
    ($normalizedStableText.Replace($stablePreflightBlock + "`n`n", "")) `
    "a missing full verification preflight"
Assert-StableContractRejected `
    ($normalizedStableText.Replace(
        "        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0",
        "        uses: actions/setup-dotnet@v5")) `
    "a mutable release SDK setup action"
Assert-StableContractRejected `
    ($normalizedStableText.Replace(
        "          dotnet-version: 10.0.x",
        "          dotnet-version: 11.0.x")) `
    "the wrong release SDK channel"
Assert-StableContractRejected `
    ($normalizedStableText.Replace(
        $stablePreflightBlock + "`n`n" + $stablePackageBlock,
        $stablePackageBlock + "`n`n" + $stablePreflightBlock)) `
    "full verification after packaging"
Assert-StableContractRejected `
    ($normalizedStableText.Replace(
        "        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1",
        "        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1")) `
    "a fast-only release preflight"
Assert-StableContractRejected `
    ($normalizedStableText.Replace(
        "        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1",
        "        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1 -SkipRestore")) `
    "a full verification preflight with restore skipped"
Assert-StableContractRejected `
    ($normalizedStableText.Replace(
        "      - name: Full verification preflight`n        shell: pwsh",
        "      - name: Full verification preflight`n        continue-on-error: true`n        shell: pwsh")) `
    "continue-on-error on the full verification preflight"
Assert-StableContractRejected `
    ($normalizedStableText.Replace(
        "      - name: Full verification preflight`n        shell: pwsh",
        '      - name: Full verification preflight' + "`n" + '        if: ${{ always() }}' + "`n" + "        shell: pwsh")) `
    "an always-run conditional on the full verification preflight"
Assert-StableContractRejected `
    ($normalizedStableText + @'

  bypass-package:
    runs-on: windows-latest
    steps: # unprotected package path
      - name: Package without preflight
        uses: ./.github/actions/build-package
'@) `
    "a sibling packaging job without the preflight"

Require-StepFragments $testingWorkflow "Validate requested ref" @(
    '& "$env:VALIDATOR_PATH"',
    '-Kind Ref',
    '-Value "$env:REQUESTED_REF"',
    '-OutputName value',
    '-OutputFile "$env:GITHUB_OUTPUT"'
)
Require-StepFragments $testingWorkflow "Validate stable release tag" @(
    '& "$env:VALIDATOR_PATH"',
    '-Kind Tag',
    '-Value "$env:STABLE_TAG"',
    '-OutputName value',
    '-OutputFile "$env:GITHUB_OUTPUT"'
)
Require-StepFragments $testingWorkflow "Resolve immutable release commit" @(
    'git -C .\selected-source rev-parse HEAD',
    '-Kind Hash',
    '-Value "$full"',
    '-OutputName sha',
    '-Value "$short"',
    '-OutputName short',
    '-OutputFile "$env:GITHUB_OUTPUT"'
)
Require-StepFragments $testingWorkflow "Set archive name" @(
    '& "$env:VALIDATOR_PATH"',
    '-Kind Archive',
    '-Value "$archiveName"',
    '-OutputName name',
    '-OutputFile "$env:GITHUB_OUTPUT"'
)

Assert-NoExpressionInRunBlock $testingWorkflow '${{'
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
    Forbid-Fragment ($workflowText + $actionText) $fragment "Unsafe release interpolation returned: $fragment"
}

Require-Fragment $actionText 'ARCHIVE_NAME: ${{ inputs.archive-name }}' "Build/package action must pass archive-name through env."
Require-Fragment $actionText '$out = "$env:ARCHIVE_NAME"' "Build/package action must read archive-name from env."

Write-Host "Release workflow input/output and token-boundary verification completed."

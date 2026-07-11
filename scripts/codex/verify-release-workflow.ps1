$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$validator = Join-Path $PSScriptRoot "validate-release-token.ps1"
$testingWorkflow = Join-Path $repoRoot ".github\workflows\testing-release.yml"
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
$actionText = Get-Content -LiteralPath $packageAction -Raw

foreach ($fragment in @(
    'REQUESTED_REF: ${{ inputs.commit }}',
    'VALIDATOR_PATH: ${{ runner.temp }}\validate-release-token.ps1',
    'ref: ${{ steps.release-ref.outputs.value }}',
    'git rev-parse --short=12 HEAD',
    'result-encoding: string',
    'STABLE_TAG: ${{ steps.latest-tag.outputs.result }}',
    'SHORT_HASH: ${{ steps.short-hash.outputs.sha }}',
    'ARCHIVE_NAME: ${{ steps.archive-name.outputs.name }}',
    'gh release upload v0.0.1 "$env:ARCHIVE_NAME" --clobber'
)) {
    Require-Fragment $workflowText $fragment "Testing Release workflow is missing required injection-safe fragment: $fragment"
}

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
Require-StepFragments $testingWorkflow "Get short commit hash" @(
    '& "$env:VALIDATOR_PATH"',
    '-Kind Hash',
    '-Value "$short"',
    '-OutputName sha',
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

Write-Host "Release workflow injection verification completed."

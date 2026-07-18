$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$verifyScript = Join-Path $repoRoot "scripts\codex\verify.ps1"

$canonicalVerifyScript = @'
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
        Invoke-Checked dotnet "restore" "-warnaserror"
    }

    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\LLPlayer"
    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\Plugins\YoutubeDL"
    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"

    Write-Host "LLPlayer full verification completed."
}
finally {
    Pop-Location
}
'@

function Get-UniqueCommandIndex(
    [string[]]$Lines,
    [string]$Pattern,
    [string]$Description,
    [string]$Source
) {
    $indices = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -cmatch $Pattern) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source must contain exactly one $Description; found $($indices.Count)."
    }
    return [int]$indices[0]
}

function Assert-FullGateContract([string]$Text, [string]$Source) {
    $lines = @($Text -split '\r?\n')
    $fast = Get-UniqueCommandIndex $lines '^    & "\.\\scripts\\codex\\verify-fast\.ps1"\s*$' "canonical fast-gate invocation" $Source
    $restore = Get-UniqueCommandIndex $lines '^        Invoke-Checked dotnet "restore" "-warnaserror"\s*$' "canonical warning-clean restore invocation" $Source
    $buildApp = Get-UniqueCommandIndex $lines '^    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" "\.\\LLPlayer"\s*$' "canonical app build invocation" $Source
    $buildPlugin = Get-UniqueCommandIndex $lines '^    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" "\.\\Plugins\\YoutubeDL"\s*$' "canonical plugin build invocation" $Source
    $test = Get-UniqueCommandIndex $lines '^    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" "\.\\FlyleafLibTests"\s*$' "exact unfiltered warning-clean test invocation" $Source

    $dotnetInvocations = @($lines | Where-Object { $_ -cmatch '^\s*Invoke-Checked dotnet\s+' }).Count
    if ($dotnetInvocations -ne 4) {
        throw "$Source must contain exactly four protected dotnet invocations; found $dotnetInvocations."
    }

    if (-not ($fast -lt $restore -and
              $restore -lt $buildApp -and
              $buildApp -lt $buildPlugin -and
              $buildPlugin -lt $test)) {
        throw "$Source must order fast verification, restore, app build, plugin build, then the full unfiltered test suite."
    }

    $normalizedActual = ($Text -replace "`r`n", "`n").Trim()
    $normalizedCanonical = ($canonicalVerifyScript -replace "`r`n", "`n").Trim()
    if ($normalizedActual -cne $normalizedCanonical) {
        throw "$Source must match the canonical fail-closed full-gate body."
    }
}

function Assert-ContractRejected(
    [string]$Text,
    [string]$Description,
    [string]$ExpectedMessagePattern
) {
    try {
        Assert-FullGateContract $Text "adversarial fixture ($Description)"
    }
    catch {
        if ($_.Exception.Message -cnotmatch $ExpectedMessagePattern) {
            throw "Full-gate validator rejected '$Description' for the wrong reason: $($_.Exception.Message)"
        }
        return
    }
    throw "Full-gate validator accepted adversarial fixture: $Description."
}

$positiveFixture = $canonicalVerifyScript
Assert-FullGateContract $positiveFixture "positive fixture"

$earlyReturnFixture = $positiveFixture.Replace(
    '    & ".\scripts\codex\verify-fast.ps1"',
    '    & ".\scripts\codex\verify-fast.ps1"' + "`n    return"
)
Assert-ContractRejected $earlyReturnFixture "full gate returns before restore/build/test" "must match the canonical fail-closed full-gate body"

$noOpRunnerFixture = $positiveFixture.Replace(
    '        & $FilePath @Arguments',
    '        return'
)
Assert-ContractRejected $noOpRunnerFixture "Invoke-Checked no longer executes its command" "must match the canonical fail-closed full-gate body"

$falseConditionalFixture = $positiveFixture.Replace(
    '    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"',
    'if ($false) {' + "`n" + '    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"' + "`n}"
)
Assert-ContractRejected $falseConditionalFixture "test invocation is unreachable behind a false condition" "must match the canonical fail-closed full-gate body"

$blockCommentFixture = $positiveFixture.Replace(
    '    & ".\scripts\codex\verify-fast.ps1"',
    '<#' + "`n" + '    & ".\scripts\codex\verify-fast.ps1"'
).Replace(
    '    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"',
    '    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"' + "`n#>"
)
Assert-ContractRejected $blockCommentFixture "all protected commands are hidden in a block comment" "must match the canonical fail-closed full-gate body"

$weakTestFixture = $positiveFixture.Replace(
    '    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"',
    '    Invoke-Checked dotnet "test" "--no-restore" ".\FlyleafLibTests"'
)
Assert-ContractRejected $weakTestFixture "test warnings are not fatal" "exact unfiltered warning-clean test invocation"

$filteredTestFixture = $positiveFixture.Replace(
    '    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"',
    '    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests" "--filter" "FullyQualifiedName=__T03_NoSuchTest__"'
)
Assert-ContractRejected $filteredTestFixture "test suite is filtered" "exact unfiltered warning-clean test invocation"

$wrongProjectFixture = $positiveFixture.Replace(".\FlyleafLibTests", ".\LLPlayer")
Assert-ContractRejected $wrongProjectFixture "test targets the wrong project" "exact unfiltered warning-clean test invocation"

$extraInvocationFixture = $positiveFixture + "`n    Invoke-Checked dotnet `"test`" `"--list-tests`""
Assert-ContractRejected $extraInvocationFixture "an extra dotnet invocation is appended" "exactly four protected dotnet invocations"

$nestedTestFixture = $positiveFixture.Replace(
    '    Invoke-Checked dotnet "test"',
    '        Invoke-Checked dotnet "test"'
)
Assert-ContractRejected $nestedTestFixture "test invocation is hidden in a nested block" "exact unfiltered warning-clean test invocation"

$wrongOrderFixture = @'
    & ".\scripts\codex\verify-fast.ps1"
        Invoke-Checked dotnet "restore" "-warnaserror"
    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\LLPlayer"
    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests"
    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\Plugins\YoutubeDL"
'@
Assert-ContractRejected $wrongOrderFixture "tests run before plugin build" "must order fast verification"

if (-not (Test-Path -LiteralPath $verifyScript)) {
    throw "Full verification script is missing: $verifyScript"
}
Assert-FullGateContract (Get-Content -LiteralPath $verifyScript -Raw) $verifyScript

Write-Host "Full build/test gate contract verification completed."

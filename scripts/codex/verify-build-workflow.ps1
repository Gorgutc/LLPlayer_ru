$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$buildWorkflow = Join-Path $repoRoot ".github\workflows\build.yml"

function Get-UniqueLineIndex(
    [string[]]$Lines,
    [string]$Pattern,
    [string]$Description,
    [string]$Source
) {
    $lineIndices = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -cmatch $Pattern) {
            $lineIndices += $index
        }
    }
    if ($lineIndices.Count -ne 1) {
        throw "$Source must contain exactly one $Description; found $($lineIndices.Count)."
    }
    return [int]$lineIndices[0]
}

function Get-MappingKey([string]$Line, [int]$Indent) {
    $actualIndent = $Line.Length - $Line.TrimStart().Length
    if ($actualIndent -ne $Indent) {
        return $null
    }
    $trimmed = $Line.Trim()
    if (-not $trimmed -or $trimmed.StartsWith("#", [System.StringComparison]::Ordinal) -or
        $trimmed.StartsWith("- ", [System.StringComparison]::Ordinal)) {
        return $null
    }
    $colonIndex = $trimmed.IndexOf(':')
    if ($colonIndex -lt 1) {
        throw "Protected workflow structure must use canonical mapping syntax; found '$trimmed'."
    }
    $key = $trimmed.Substring(0, $colonIndex).Trim()
    if ($key -cnotmatch '^[A-Za-z0-9_-]+$') {
        throw "Protected workflow mapping keys must use canonical unquoted syntax; found '$key'."
    }
    return $key
}

function Get-UniqueBlockKeyIndex(
    [string[]]$Lines,
    [int]$Indent,
    [string]$Key,
    [string]$Description,
    [string]$Source
) {
    $lineIndices = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $lineKey = Get-MappingKey $Lines[$index] $Indent
        if (-not [string]::Equals($lineKey, $Key, [System.StringComparison]::Ordinal)) {
            continue
        }
        $lineIndices += $index
    }
    if ($lineIndices.Count -ne 1) {
        throw "$Source must contain exactly one $Description; found $($lineIndices.Count)."
    }
    $trimmed = $Lines[$lineIndices[0]].Trim()
    $remainder = $trimmed.Substring($trimmed.IndexOf(':') + 1).Trim()
    if ($remainder -and -not $remainder.StartsWith("#", [System.StringComparison]::Ordinal)) {
        throw "$Source $Description must use a canonical block mapping."
    }
    return [int]$lineIndices[0]
}

function Get-JobsBlock([string]$Text, [string]$Source) {
    $lines = @($Text -split '\r?\n')
    $start = Get-UniqueBlockKeyIndex $lines 0 "jobs" "top-level jobs entry" $Source
    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        $key = Get-MappingKey $lines[$index] 0
        if ($null -ne $key) {
            $end = $index
            break
        }
    }
    if ($end -le $start + 1) {
        throw "$Source top-level jobs entry must not be empty."
    }
    return @($lines[($start + 1)..($end - 1)])
}

function Get-BuildJobBlock([string]$Text, [string]$Source) {
    $jobsLines = Get-JobsBlock $Text $Source
    $start = Get-UniqueBlockKeyIndex $jobsLines 2 "build" "jobs.build entry" $Source
    $end = $jobsLines.Count
    for ($index = $start + 1; $index -lt $jobsLines.Count; $index++) {
        $key = Get-MappingKey $jobsLines[$index] 2
        if ($null -ne $key) {
            $end = $index
            break
        }
    }
    return [pscustomobject]@{
        Lines = @($jobsLines[$start..($end - 1)])
    }
}

function Get-StepsBlock([string[]]$JobLines, [string]$Source) {
    $stepsIndex = Get-UniqueLineIndex $JobLines '^    steps:\s*$' "jobs.build.steps entry" $Source
    $end = $JobLines.Count
    for ($index = $stepsIndex + 1; $index -lt $JobLines.Count; $index++) {
        $trimmed = $JobLines[$index].Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }
        $indent = $JobLines[$index].Length - $JobLines[$index].TrimStart().Length
        if ($indent -lt 4 -or ($indent -eq 4 -and -not $trimmed.StartsWith("- ", [System.StringComparison]::Ordinal))) {
            $end = $index
            break
        }
    }
    if ($end -le $stepsIndex + 1) {
        throw "$Source jobs.build.steps must not be empty."
    }
    return @($JobLines[($stepsIndex + 1)..($end - 1)])
}

function Get-ChildMappingBlock(
    [string[]]$Lines,
    [int]$Indent,
    [string]$Key,
    [string]$Description,
    [string]$Source
) {
    $start = Get-UniqueBlockKeyIndex $Lines $Indent $Key $Description $Source
    $end = $Lines.Count
    for ($index = $start + 1; $index -lt $Lines.Count; $index++) {
        $nextKey = Get-MappingKey $Lines[$index] $Indent
        if ($null -ne $nextKey) {
            $end = $index
            break
        }
    }
    if ($end -le $start + 1) {
        throw "$Source $Description must not be empty."
    }
    return @($Lines[($start + 1)..($end - 1)])
}

function Get-NamedStep([string[]]$StepLines, [string]$Name, [string]$Source) {
    $pattern = '^    - name:\s*' + [regex]::Escape($Name) + '\s*$'
    $start = Get-UniqueLineIndex $StepLines $pattern "'$Name' step in jobs.build.steps" $Source
    $end = $StepLines.Count
    for ($index = $start + 1; $index -lt $StepLines.Count; $index++) {
        if ($StepLines[$index] -match '^    - ') {
            $end = $index
            break
        }
    }
    return [pscustomobject]@{
        Start = $start
        Lines = @($StepLines[$start..($end - 1)])
    }
}

function Require-StepLine(
    [string[]]$StepLines,
    [string]$Pattern,
    [string]$Description,
    [string]$Source
) {
    $count = @($StepLines | Where-Object { $_ -cmatch $Pattern }).Count
    if ($count -ne 1) {
        throw "$Source $Description; found $count matching lines."
    }
}

function Assert-AllowedMappingKeys(
    [string[]]$Lines,
    [int]$Indent,
    [string[]]$AllowedKeys,
    [string]$Description,
    [string]$Source
) {
    $seenKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in $Lines) {
        $key = Get-MappingKey $line $Indent
        if ($null -eq $key) {
            continue
        }
        $allowed = @($AllowedKeys | Where-Object {
            [string]::Equals($_, $key, [System.StringComparison]::Ordinal)
        }).Count -eq 1
        if (-not $allowed) {
            throw "$Source $Description contains forbidden or unexpected key '$key'."
        }
        if (-not $seenKeys.Add($key)) {
            throw "$Source $Description contains duplicate key '$key'."
        }
    }
}

function Assert-NoMappingKeys(
    [string[]]$Lines,
    [int]$Indent,
    [string[]]$ForbiddenKeys,
    [string]$Description,
    [string]$Source
) {
    foreach ($line in $Lines) {
        $key = Get-MappingKey $line $Indent
        foreach ($forbiddenKey in $ForbiddenKeys) {
            if ([string]::Equals($key, $forbiddenKey, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "$Source $Description must not define '$key'."
            }
        }
    }
}

function Assert-NoNestedContent(
    [string[]]$Lines,
    [int]$MaximumIndent,
    [string]$Description,
    [string]$Source
) {
    foreach ($line in $Lines) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }
        $indent = $line.Length - $line.TrimStart().Length
        if ($indent -gt $MaximumIndent) {
            throw "$Source $Description contains forbidden nested or multiline scalar content: '$trimmed'."
        }
    }
}

function Assert-BuildWorkflowTriggers([string]$Text, [string]$Source) {
    $rootLines = @($Text -split '\r?\n')
    $onIndex = Get-UniqueBlockKeyIndex $rootLines 0 "on" "top-level on entry" $Source
    $end = $rootLines.Count
    for ($index = $onIndex + 1; $index -lt $rootLines.Count; $index++) {
        $key = Get-MappingKey $rootLines[$index] 0
        if ($null -ne $key) {
            $end = $index
            break
        }
    }
    $onLines = @($rootLines[($onIndex + 1)..($end - 1)])
    Assert-AllowedMappingKeys $onLines 2 @("push", "pull_request") "workflow triggers" $Source

    foreach ($trigger in @("push", "pull_request")) {
        $triggerLines = Get-ChildMappingBlock $onLines 2 $trigger "$trigger trigger" $Source
        Assert-AllowedMappingKeys $triggerLines 4 @("branches") "$trigger trigger" $Source
        Assert-NoNestedContent $triggerLines 4 "$trigger trigger" $Source
        Require-StepLine $triggerLines '^    branches:\s*\[ "main" \]\s*$' "$trigger trigger must target only main" $Source
    }
}

function Assert-BuildWorkflowContract([string]$Text, [string]$Source) {
    $rootLines = @($Text -split '\r?\n')
    Assert-AllowedMappingKeys $rootLines 0 @("name", "on", "jobs") "workflow root" $Source
    Assert-BuildWorkflowTriggers $Text $Source

    $jobsLines = Get-JobsBlock $Text $Source
    Assert-AllowedMappingKeys $jobsLines 2 @("build") "top-level jobs" $Source
    $job = Get-BuildJobBlock $Text $Source
    Assert-AllowedMappingKeys $job.Lines 4 @("runs-on", "steps") "jobs.build" $Source

    $runsOnIndex = Get-UniqueLineIndex $job.Lines '^    runs-on:\s*windows-latest\s*$' "jobs.build Windows runner" $Source
    $stepsEntryIndex = Get-UniqueLineIndex $job.Lines '^    steps:\s*$' "jobs.build.steps entry" $Source
    if ($runsOnIndex -ge $stepsEntryIndex) {
        throw "$Source must declare runs-on: windows-latest before jobs.build.steps."
    }
    for ($index = $runsOnIndex + 1; $index -lt $stepsEntryIndex; $index++) {
        $trimmed = $job.Lines[$index].Trim()
        if ($trimmed -and -not $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            throw "$Source jobs.build runs-on must not use multiline scalar continuation."
        }
    }

    $stepLines = Get-StepsBlock $job.Lines $Source
    $stepCount = @($stepLines | Where-Object { $_ -cmatch '^    - ' }).Count
    if ($stepCount -ne 7) {
        throw "$Source jobs.build.steps must contain exactly the seven protected steps; found $stepCount."
    }

    $checkout = Get-NamedStep $stepLines "Checkout source" $Source
    $setup = Get-NamedStep $stepLines "Setup .NET" $Source
    $verify = Get-NamedStep $stepLines "Verify fast repository gates" $Source
    $restore = Get-NamedStep $stepLines "Restore dependencies" $Source
    $buildApp = Get-NamedStep $stepLines "Build App" $Source
    $buildPlugin = Get-NamedStep $stepLines "Build Plugin (YoutubeDL)" $Source
    $test = Get-NamedStep $stepLines "Test" $Source

    Assert-AllowedMappingKeys $checkout.Lines 6 @("uses") "Checkout source step" $Source
    Assert-AllowedMappingKeys $setup.Lines 6 @("uses", "with") "Setup .NET step" $Source
    Assert-AllowedMappingKeys $verify.Lines 6 @("run") "fast verification step" $Source
    Assert-AllowedMappingKeys $restore.Lines 6 @("run") "Restore dependencies step" $Source
    Assert-AllowedMappingKeys $buildApp.Lines 6 @("run") "Build App step" $Source
    Assert-AllowedMappingKeys $buildPlugin.Lines 6 @("run") "Build Plugin (YoutubeDL) step" $Source
    Assert-AllowedMappingKeys $test.Lines 6 @("run") "Test step" $Source

    Assert-NoNestedContent $checkout.Lines 6 "Checkout source step" $Source
    Assert-NoNestedContent $verify.Lines 6 "fast verification step" $Source
    Assert-NoNestedContent $restore.Lines 6 "Restore dependencies step" $Source
    Assert-NoNestedContent $buildApp.Lines 6 "Build App step" $Source
    Assert-NoNestedContent $buildPlugin.Lines 6 "Build Plugin (YoutubeDL) step" $Source
    Assert-NoNestedContent $test.Lines 6 "Test step" $Source

    Require-StepLine $checkout.Lines '^      uses:\s*actions/checkout@v[1-9][0-9]*\s*$' "Checkout source must use a versioned actions/checkout release without ref overrides" $Source
    Require-StepLine $setup.Lines '^      uses:\s*actions/setup-dotnet@v[1-9][0-9]*\s*$' "Setup .NET must use a versioned actions/setup-dotnet release" $Source
    $setupInputs = Get-ChildMappingBlock $setup.Lines 6 "with" "Setup .NET with block" $Source
    Assert-AllowedMappingKeys $setupInputs 8 @("dotnet-version") "Setup .NET with block" $Source
    Assert-NoNestedContent $setupInputs 8 "Setup .NET with block" $Source
    Require-StepLine $setupInputs '^        dotnet-version:\s*10\.0\.x\s*$' "Setup .NET with block must install the frozen .NET 10.0.x SDK" $Source
    Require-StepLine $verify.Lines '^      run:\s*powershell(?:\.exe)?\s+-NoProfile\s+-ExecutionPolicy\s+Bypass\s+-File\s+\.\\scripts\\codex\\verify-fast\.ps1\s*$' "fast verification must run the canonical verify-fast command" $Source
    Require-StepLine $restore.Lines '^      run:\s*dotnet restore -warnaserror\s*$' "Restore dependencies must run dotnet restore -warnaserror" $Source
    Require-StepLine $buildApp.Lines '^      run:\s*dotnet build --no-restore -warnaserror \.\\LLPlayer\s*$' "Build App must run the canonical warning-clean build command" $Source
    Require-StepLine $buildPlugin.Lines '^      run:\s*dotnet build --no-restore -warnaserror \.\\Plugins\\YoutubeDL\s*$' "Build Plugin (YoutubeDL) must run the canonical warning-clean build command" $Source
    Require-StepLine $test.Lines '^      run:\s*dotnet test --no-restore -warnaserror \.\\FlyleafLibTests\s*$' "Test must run the exact unfiltered warning-clean test command" $Source

    if (-not ($checkout.Start -lt $setup.Start -and
              $setup.Start -lt $verify.Start -and
              $verify.Start -lt $restore.Start -and
              $restore.Start -lt $buildApp.Start -and
              $buildApp.Start -lt $buildPlugin.Start -and
              $buildPlugin.Start -lt $test.Start)) {
        throw "$Source must order Checkout source, Setup .NET, Verify fast repository gates, Restore dependencies, Build App, Build Plugin (YoutubeDL), then Test in jobs.build.steps."
    }
}

function Assert-ContractRejected(
    [string]$Text,
    [string]$Description,
    [string]$ExpectedMessagePattern = ""
) {
    $rejected = $false
    try {
        Assert-BuildWorkflowContract $Text "adversarial fixture ($Description)"
    }
    catch {
        if ($ExpectedMessagePattern -and $_.Exception.Message -cnotmatch $ExpectedMessagePattern) {
            throw "Build workflow validator rejected adversarial fixture '$Description' for the wrong reason: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Build workflow validator accepted adversarial fixture: $Description."
    }
}

function Swap-AdjacentNamedSteps([string]$Text, [string]$FirstName, [string]$SecondName) {
    $lines = @($Text -split '\r?\n')
    $first = Get-UniqueLineIndex $lines ('^    - name:\s*' + [regex]::Escape($FirstName) + '\s*$') "'$FirstName' fixture step" "order fixture"
    $second = Get-UniqueLineIndex $lines ('^    - name:\s*' + [regex]::Escape($SecondName) + '\s*$') "'$SecondName' fixture step" "order fixture"

    $nextStep = $null
    for ($index = $first + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -cmatch '^    - ') {
            $nextStep = $index
            break
        }
    }
    if ($nextStep -ne $second) {
        throw "Order fixture requires '$FirstName' and '$SecondName' to be adjacent."
    }

    $secondEnd = $lines.Count
    for ($index = $second + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -cmatch '^    - ') {
            $secondEnd = $index
            break
        }
    }

    $result = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $first; $index++) { $result.Add($lines[$index]) }
    for ($index = $second; $index -lt $secondEnd; $index++) { $result.Add($lines[$index]) }
    for ($index = $first; $index -lt $second; $index++) { $result.Add($lines[$index]) }
    for ($index = $secondEnd; $index -lt $lines.Count; $index++) { $result.Add($lines[$index]) }
    return $result -join "`n"
}

$positiveFixture = @'
name: Build & Test
on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
jobs:
  build:
    runs-on: windows-latest
    steps:
    - name: Checkout source
      uses: actions/checkout@v5
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
    - name: Restore dependencies
      run: dotnet restore -warnaserror
    - name: Build App
      run: dotnet build --no-restore -warnaserror .\LLPlayer
    - name: Build Plugin (YoutubeDL)
      run: dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
    - name: Test
      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests
'@
Assert-BuildWorkflowContract $positiveFixture "positive fixture"

$missingRunnerFixture = $positiveFixture.Replace("    runs-on: windows-latest", "    # runs-on intentionally missing")
Assert-ContractRejected $missingRunnerFixture "build runner is missing" "jobs\.build Windows runner"

$wrongRunnerFixture = $positiveFixture.Replace("    runs-on: windows-latest", "    runs-on: ubuntu-latest")
Assert-ContractRejected $wrongRunnerFixture "build uses a non-Windows runner" "jobs\.build Windows runner"

$continuedRunnerFixture = $positiveFixture.Replace(
    "    runs-on: windows-latest",
    "    runs-on: windows-latest`n      unexpected-runner-suffix"
)
Assert-ContractRejected $continuedRunnerFixture "build runner uses a multiline scalar continuation" "runs-on must not use multiline scalar continuation"

$wrongCheckoutActionFixture = $positiveFixture.Replace("      uses: actions/checkout@v5", "      uses: example/checkout@v1")
Assert-ContractRejected $wrongCheckoutActionFixture "Checkout source uses the wrong action" "Checkout source must use a versioned actions/checkout release"

$wrongFastCommandFixture = $positiveFixture.Replace(
    "      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1",
    "      run: Write-Output 'fast gate skipped'"
)
Assert-ContractRejected $wrongFastCommandFixture "fast verification runs the wrong command" "fast verification must run the canonical verify-fast command"

$wrongRestoreCommandFixture = $positiveFixture.Replace("      run: dotnet restore -warnaserror", "      run: dotnet restore")
Assert-ContractRejected $wrongRestoreCommandFixture "restore drops warning enforcement" "Restore dependencies must run dotnet restore -warnaserror"

$wrongPluginBuildCommandFixture = $positiveFixture.Replace(
    "      run: dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL",
    "      run: dotnet build --no-restore -warnaserror .\LLPlayer"
)
Assert-ContractRejected $wrongPluginBuildCommandFixture "plugin build targets the app" "Build Plugin \(YoutubeDL\) must run the canonical warning-clean build command"

$missingPushFixture = [regex]::Replace($positiveFixture, '(?m)^  push:\r?\n    branches: \[ "main" \]\r?\n?', '')
Assert-ContractRejected $missingPushFixture "push trigger is missing" "exactly one push trigger; found 0"

$missingPullRequestFixture = [regex]::Replace($positiveFixture, '(?m)^  pull_request:\r?\n    branches: \[ "main" \]\r?\n?', '')
Assert-ContractRejected $missingPullRequestFixture "pull_request trigger is missing" "exactly one pull_request trigger; found 0"

$wrongPushBranchFixture = [regex]::Replace(
    $positiveFixture,
    '(?m)(^  push:\r?\n    branches: \[ )"main"( \]\r?$)',
    '${1}"develop"${2}'
)
Assert-ContractRejected $wrongPushBranchFixture "push targets the wrong branch" "push trigger must target only main"

$wrongPullRequestBranchFixture = [regex]::Replace(
    $positiveFixture,
    '(?m)(^  pull_request:\r?\n    branches: \[ )"main"( \]\r?$)',
    '${1}"develop"${2}'
)
Assert-ContractRejected $wrongPullRequestBranchFixture "pull_request targets the wrong branch" "pull_request trigger must target only main"

$orderPairs = @(
    @("Checkout source", "Setup .NET"),
    @("Setup .NET", "Verify fast repository gates"),
    @("Verify fast repository gates", "Restore dependencies"),
    @("Restore dependencies", "Build App"),
    @("Build App", "Build Plugin (YoutubeDL)"),
    @("Build Plugin (YoutubeDL)", "Test")
)
foreach ($pair in $orderPairs) {
    $swappedOrderFixture = Swap-AdjacentNamedSteps $positiveFixture $pair[0] $pair[1]
    Assert-ContractRejected $swappedOrderFixture "$($pair[0]) runs after $($pair[1])" "must order Checkout source"
}

$extraTriggerFixture = $positiveFixture.Replace(
    "jobs:",
    "  pull_request_target:`n    branches: [ `"main`" ]`njobs:"
)
Assert-ContractRejected $extraTriggerFixture "pull_request_target adds an untrusted trigger path" "workflow triggers contains forbidden or unexpected key 'pull_request_target'"

$siblingJobFixture = $positiveFixture + @'

  package-bypass:
    runs-on: windows-latest
    steps:
    - name: Bypass protected build
      run: Write-Output "alternate job"
'@
Assert-ContractRejected $siblingJobFixture "a sibling job bypasses the protected build" "top-level jobs contains forbidden or unexpected key 'package-bypass'"

$checkoutRefFixture = $positiveFixture.Replace(
    "      uses: actions/checkout@v5",
    "      uses: actions/checkout@v5`n      with:`n        ref: main"
)
Assert-ContractRejected $checkoutRefFixture "Checkout source overrides the event-selected ref" "Checkout source step contains forbidden or unexpected key 'with'"

$extraStepFixture = $positiveFixture.Replace(
    "    - name: Restore dependencies",
    "    - name: Mutate test environment`n      run: Write-Output 'GITHUB_PATH override'`n    - name: Restore dependencies"
)
Assert-ContractRejected $extraStepFixture "an extra state-mutating step is inserted" "must contain exactly the seven protected steps"

$filteredTestFixture = $positiveFixture.Replace(
    "      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests",
    "      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests --filter FullyQualifiedName=__T03_NoSuchTest__"
)
Assert-ContractRejected $filteredTestFixture "Test filters the suite down to zero matching tests" "Test must run the exact unfiltered warning-clean test command"

$continuedTestFixture = $positiveFixture.Replace(
    "      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests",
    "      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests`n        --filter FullyQualifiedName=__T03_NoSuchTest__"
)
Assert-ContractRejected $continuedTestFixture "Test hides a zero-match filter in a multiline plain scalar" "Test step contains forbidden nested or multiline scalar content"

$continuedExitFixture = $positiveFixture.Replace(
    "      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests",
    "      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests`n        ; exit 0"
)
Assert-ContractRejected $continuedExitFixture "Test hides an exit override in a multiline plain scalar" "Test step contains forbidden nested or multiline scalar content"

$continuedFastFixture = $positiveFixture.Replace(
    "      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1",
    "      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1`n        ; exit 0"
)
Assert-ContractRejected $continuedFastFixture "fast verification hides an exit override in a multiline plain scalar" "fast verification step contains forbidden nested or multiline scalar content"

$continuedRestoreFixture = $positiveFixture.Replace(
    "      run: dotnet restore -warnaserror",
    "      run: dotnet restore -warnaserror`n        ; exit 0"
)
Assert-ContractRejected $continuedRestoreFixture "restore hides an exit override in a multiline plain scalar" "Restore dependencies step contains forbidden nested or multiline scalar content"

$continuedAppBuildFixture = $positiveFixture.Replace(
    "      run: dotnet build --no-restore -warnaserror .\LLPlayer",
    "      run: dotnet build --no-restore -warnaserror .\LLPlayer`n        ; exit 0"
)
Assert-ContractRejected $continuedAppBuildFixture "app build hides an exit override in a multiline plain scalar" "Build App step contains forbidden nested or multiline scalar content"

$continuedPluginBuildFixture = $positiveFixture.Replace(
    "      run: dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL",
    "      run: dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL`n        ; exit 0"
)
Assert-ContractRejected $continuedPluginBuildFixture "plugin build hides an exit override in a multiline plain scalar" "Build Plugin \(YoutubeDL\) step contains forbidden nested or multiline scalar content"

$missingTestFixture = $positiveFixture.Replace("    - name: Test", "    - name: Test omitted")
Assert-ContractRejected $missingTestFixture "Test step is missing"

$wrongTestProjectFixture = $positiveFixture.Replace(
    "      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests",
    "      run: dotnet test --no-restore -warnaserror .\LLPlayer"
)
Assert-ContractRejected $wrongTestProjectFixture "Test targets the wrong project"

$testContinueFixture = $positiveFixture.Replace(
    "    - name: Test",
    "    - name: Test`n      continue-on-error: true"
)
Assert-ContractRejected $testContinueFixture "Test continues on error"

$testConditionalFixture = $positiveFixture.Replace(
    "    - name: Test",
    '    - name: Test' + "`n" + '      if: ${{ false }}'
)
Assert-ContractRejected $testConditionalFixture "Test is conditional"

$wrongBuildFixture = $positiveFixture.Replace(
    "      run: dotnet build --no-restore -warnaserror .\LLPlayer",
    "      run: dotnet build --no-restore .\LLPlayer"
)
Assert-ContractRejected $wrongBuildFixture "Build App suppresses the warning-clean gate"

$wrongSetupFixture = $positiveFixture.Replace(
    "      uses: actions/setup-dotnet@v5",
    "      uses: example/setup@v1 # actions/setup-dotnet@v5"
)
Assert-ContractRejected $wrongSetupFixture "setup-dotnet only appears in a comment"

$wrongSdkFixture = $positiveFixture.Replace(
    "        dotnet-version: 10.0.x",
    "        dotnet-version: 9.0.x"
)
Assert-ContractRejected $wrongSdkFixture "Setup .NET installs the wrong SDK"

$duplicateSdkFixture = $positiveFixture.Replace(
    "        dotnet-version: 10.0.x",
    "        dotnet-version: 10.0.x`n        dotnet-version: 11.0.x"
)
Assert-ContractRejected $duplicateSdkFixture "Setup .NET overrides the frozen SDK with a duplicate input"

$misnestedSdkFixture = $positiveFixture.Replace(
    "      with:`n        dotnet-version: 10.0.x",
    "      with:`n        cache: false`n      env:`n        dotnet-version: 10.0.x"
)
Assert-ContractRejected $misnestedSdkFixture "dotnet-version is outside the Setup .NET with block"

$continueFixture = $positiveFixture.Replace(
    "    - name: Verify fast repository gates",
    '    - name: Verify fast repository gates' + "`n" + "      'continue-on-error' : true"
)
Assert-ContractRejected $continueFixture "fast verification uses a quoted continue-on-error key"

$conditionalFixture = $positiveFixture.Replace(
    "    - name: Verify fast repository gates",
    '    - name: Verify fast repository gates' + "`n" + '      "if" : ${{ false }}'
)
Assert-ContractRejected $conditionalFixture "fast verification uses a quoted conditional key"

$setupConditionalFixture = $positiveFixture.Replace(
    "    - name: Setup .NET",
    "    - name: Setup .NET`n      if : false"
)
Assert-ContractRejected $setupConditionalFixture "Setup .NET uses a spaced conditional key"

$restoreContinueFixture = $positiveFixture.Replace(
    "    - name: Restore dependencies",
    "    - name: Restore dependencies`n      continue-on-error : true"
)
Assert-ContractRejected $restoreContinueFixture "Restore dependencies continues on error"

$duplicateVerifyRunFixture = $positiveFixture.Replace(
    "      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1",
    "      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1`n      run: exit 0"
)
Assert-ContractRejected $duplicateVerifyRunFixture "fast verification command is overridden by a duplicate run key"

$stepShellFixture = $positiveFixture.Replace(
    "    - name: Verify fast repository gates",
    "    - name: Verify fast repository gates`n      shell: powershell -NoProfile -Command `"& '{0}'; exit 0`""
)
Assert-ContractRejected $stepShellFixture "fast verification uses a custom shell"

$jobDefaultsFixture = $positiveFixture.Replace(
    "    runs-on: windows-latest",
    "    runs-on: windows-latest`n    defaults:`n      run:`n        shell: powershell -NoProfile -Command `"& '{0}'; exit 0`""
)
Assert-ContractRejected $jobDefaultsFixture "build job defines a custom default shell"

$workflowDefaultsFixture = $positiveFixture.Replace(
    "jobs:",
    "defaults:`n  run:`n    shell: powershell -NoProfile -Command `"& '{0}'; exit 0`"`njobs:"
)
Assert-ContractRejected $workflowDefaultsFixture "workflow defines a custom default shell"

$escapedWorkflowDefaultsFixture = $positiveFixture.Replace(
    "jobs:",
    "`"d\u0065faults`":`n  run:`n    shell: powershell -NoProfile -Command `"& '{0}'; exit 0`"`njobs:"
)
Assert-ContractRejected $escapedWorkflowDefaultsFixture "workflow hides custom defaults behind an escaped quoted key"

$explicitWorkflowDefaultsFixture = $positiveFixture.Replace(
    "jobs:",
    "? defaults`n:`n  run:`n    shell: powershell -NoProfile -Command `"& '{0}'; exit 0`"`njobs:"
)
Assert-ContractRejected $explicitWorkflowDefaultsFixture "workflow defines custom defaults with explicit mapping-key syntax"

$jobContinueFixture = $positiveFixture.Replace(
    "  build:",
    "  build:`n    'continue-on-error' : true"
)
Assert-ContractRejected $jobContinueFixture "build job uses a quoted continue-on-error key"

$blockScalarFixture = $positiveFixture.Replace(
    "      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1",
    "      run: |`n        Write-Output 'run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1'"
)
Assert-ContractRejected $blockScalarFixture "verify-fast command is only block-scalar text"

$crossJobFixture = @'
name: Build & Test
on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
jobs:
  build:
    runs-on: windows-latest
    steps:
    - name: Checkout source
      uses: actions/checkout@v5
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
    - name: Restore dependencies
      run: dotnet restore -warnaserror
    - name: Build App
      run: dotnet build --no-restore -warnaserror .\LLPlayer
    - name: Build Plugin (YoutubeDL)
      run: dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
  test:
    runs-on: windows-latest
    steps:
    - name: Test
      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests
'@
Assert-ContractRejected $crossJobFixture "required steps are split across jobs" "top-level jobs contains forbidden or unexpected key 'test'"

$outsideJobsFixture = @'
name: Build & Test
on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
metadata:
  build:
    runs-on: windows-latest
    steps:
    - name: Checkout source
      uses: actions/checkout@v5
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
    - name: Restore dependencies
      run: dotnet restore -warnaserror
    - name: Build App
      run: dotnet build --no-restore -warnaserror .\LLPlayer
    - name: Build Plugin (YoutubeDL)
      run: dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
    - name: Test
      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests
jobs:
  other:
    runs-on: windows-latest
'@
Assert-ContractRejected $outsideJobsFixture "build decoy is outside top-level jobs" "workflow root contains forbidden or unexpected key 'metadata'"

$uppercaseJobsFixture = $positiveFixture.Replace("jobs:", "Jobs:")
Assert-ContractRejected $uppercaseJobsFixture "top-level Jobs key has invalid casing"

$duplicateJobsFixture = $positiveFixture + "`njobs: {}"
Assert-ContractRejected $duplicateJobsFixture "top-level jobs block is overridden by a duplicate key"

$commentedNextJobFixture = @'
name: Build & Test
on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
jobs:
  build:
    runs-on: windows-latest
    steps:
    - name: Checkout source
      uses: actions/checkout@v5
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
    - name: Restore dependencies
      run: dotnet restore -warnaserror
    - name: Build App
      run: dotnet build --no-restore -warnaserror .\LLPlayer
    - name: Build Plugin (YoutubeDL)
      run: dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
  gate: # next job has a trailing comment
    runs-on: windows-latest
    steps:
    - name: Test
      run: dotnet test --no-restore -warnaserror .\FlyleafLibTests
'@
Assert-ContractRejected $commentedNextJobFixture "next job has a trailing comment" "top-level jobs contains forbidden or unexpected key 'gate'"

if (-not (Test-Path -LiteralPath $buildWorkflow)) {
    throw "Build workflow is missing: $buildWorkflow"
}
Assert-BuildWorkflowContract (Get-Content -LiteralPath $buildWorkflow -Raw) $buildWorkflow

Write-Host "Build workflow build/test verification completed."

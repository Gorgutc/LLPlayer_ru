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

function Assert-BuildWorkflowContract([string]$Text, [string]$Source) {
    $rootLines = @($Text -split '\r?\n')
    Assert-NoMappingKeys $rootLines 0 @("defaults", "env", "<<") "workflow root" $Source

    $job = Get-BuildJobBlock $Text $Source
    Assert-AllowedMappingKeys $job.Lines 4 @("runs-on", "steps") "jobs.build" $Source

    $stepLines = Get-StepsBlock $job.Lines $Source
    $setup = Get-NamedStep $stepLines "Setup .NET" $Source
    $verify = Get-NamedStep $stepLines "Verify fast repository gates" $Source
    $restore = Get-NamedStep $stepLines "Restore dependencies" $Source

    Assert-AllowedMappingKeys $setup.Lines 6 @("uses", "with") "Setup .NET step" $Source
    Assert-AllowedMappingKeys $verify.Lines 6 @("run") "fast verification step" $Source
    Assert-AllowedMappingKeys $restore.Lines 6 @("run") "Restore dependencies step" $Source

    Require-StepLine $setup.Lines '^      uses:\s*actions/setup-dotnet@v[1-9][0-9]*\s*$' "Setup .NET must use a versioned actions/setup-dotnet release" $Source
    $setupInputs = Get-ChildMappingBlock $setup.Lines 6 "with" "Setup .NET with block" $Source
    Assert-AllowedMappingKeys $setupInputs 8 @("dotnet-version") "Setup .NET with block" $Source
    Require-StepLine $setupInputs '^        dotnet-version:\s*10\.0\.x\s*$' "Setup .NET with block must install the frozen .NET 10.0.x SDK" $Source
    Require-StepLine $verify.Lines '^      run:\s*powershell(?:\.exe)?\s+-NoProfile\s+-ExecutionPolicy\s+Bypass\s+-File\s+\.\\scripts\\codex\\verify-fast\.ps1\s*$' "fast verification must run the canonical verify-fast command" $Source
    Require-StepLine $restore.Lines '^      run:\s*dotnet restore -warnaserror\s*$' "Restore dependencies must run dotnet restore -warnaserror" $Source

    if (-not ($setup.Start -lt $verify.Start -and $verify.Start -lt $restore.Start)) {
        throw "$Source must order Setup .NET, Verify fast repository gates, then Restore dependencies in jobs.build.steps."
    }
}

function Assert-ContractRejected([string]$Text, [string]$Description) {
    $rejected = $false
    try {
        Assert-BuildWorkflowContract $Text "adversarial fixture ($Description)"
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Build workflow validator accepted adversarial fixture: $Description."
    }
}

$positiveFixture = @'
name: Build & Test
jobs:
  build:
    runs-on: windows-latest
    steps:
    - uses: actions/checkout@v5
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
    - name: Restore dependencies
      run: dotnet restore -warnaserror
'@
Assert-BuildWorkflowContract $positiveFixture "positive fixture"

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
jobs:
  build:
    runs-on: windows-latest
    steps:
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
  verify:
    runs-on: windows-latest
    steps:
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
  restore:
    runs-on: windows-latest
    steps:
    - name: Restore dependencies
      run: dotnet restore -warnaserror
'@
Assert-ContractRejected $crossJobFixture "required steps are split across jobs"

$outsideJobsFixture = @'
name: Build & Test
metadata:
  build:
    runs-on: windows-latest
    steps:
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
    - name: Restore dependencies
      run: dotnet restore -warnaserror
jobs:
  other:
    runs-on: windows-latest
'@
Assert-ContractRejected $outsideJobsFixture "build decoy is outside top-level jobs"

$uppercaseJobsFixture = $positiveFixture.Replace("jobs:", "Jobs:")
Assert-ContractRejected $uppercaseJobsFixture "top-level Jobs key has invalid casing"

$duplicateJobsFixture = $positiveFixture + "`njobs: {}"
Assert-ContractRejected $duplicateJobsFixture "top-level jobs block is overridden by a duplicate key"

$commentedNextJobFixture = @'
name: Build & Test
jobs:
  build:
    runs-on: windows-latest
    steps:
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
  gate: # next job has a trailing comment
    runs-on: windows-latest
    steps:
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
    - name: Restore dependencies
      run: dotnet restore -warnaserror
'@
Assert-ContractRejected $commentedNextJobFixture "next job has a trailing comment"

$wrongOrderFixture = @'
name: Build & Test
jobs:
  build:
    runs-on: windows-latest
    steps:
    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x
    - name: Restore dependencies
      run: dotnet restore -warnaserror
    - name: Verify fast repository gates
      run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
'@
Assert-ContractRejected $wrongOrderFixture "fast verification runs after restore"

if (-not (Test-Path -LiteralPath $buildWorkflow)) {
    throw "Build workflow is missing: $buildWorkflow"
}
Assert-BuildWorkflowContract (Get-Content -LiteralPath $buildWorkflow -Raw) $buildWorkflow

Write-Host "Build workflow fast-gate verification completed."

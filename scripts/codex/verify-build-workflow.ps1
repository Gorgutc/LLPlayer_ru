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

    $workflowNameIndex = Get-UniqueLineIndex $rootLines '^name:\s*Build & Test\s*$' "stable workflow name 'Build & Test'" $Source
    $onIndex = Get-UniqueBlockKeyIndex $rootLines 0 "on" "top-level on entry" $Source
    if ($workflowNameIndex -ge $onIndex) {
        throw "$Source must declare the stable workflow name 'Build & Test' before the top-level on entry."
    }
    for ($index = $workflowNameIndex + 1; $index -lt $onIndex; $index++) {
        $trimmed = $rootLines[$index].Trim()
        if ($trimmed -and -not $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            throw "$Source stable workflow name must not use multiline scalar continuation."
        }
    }
    Assert-BuildWorkflowTriggers $Text $Source

    $jobsLines = Get-JobsBlock $Text $Source
    Assert-AllowedMappingKeys $jobsLines 2 @("build") "top-level jobs" $Source
    $job = Get-BuildJobBlock $Text $Source
    Assert-AllowedMappingKeys $job.Lines 4 @("name", "runs-on", "steps") "jobs.build" $Source

    $jobNameIndex = Get-UniqueLineIndex $job.Lines '^    name:\s*LLPlayer Build & Test\s*$' "jobs.build stable required-check name 'LLPlayer Build & Test'" $Source
    $runsOnIndex = Get-UniqueLineIndex $job.Lines '^    runs-on:\s*windows-latest\s*$' "jobs.build Windows runner" $Source
    $stepsEntryIndex = Get-UniqueLineIndex $job.Lines '^    steps:\s*$' "jobs.build.steps entry" $Source
    if (-not ($jobNameIndex -lt $runsOnIndex -and $runsOnIndex -lt $stepsEntryIndex)) {
        throw "$Source must declare the stable jobs.build name 'LLPlayer Build & Test', then runs-on: windows-latest, then jobs.build.steps."
    }
    for ($index = $jobNameIndex + 1; $index -lt $runsOnIndex; $index++) {
        $trimmed = $job.Lines[$index].Trim()
        if ($trimmed -and -not $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            throw "$Source jobs.build stable required-check name must not use multiline scalar continuation."
        }
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

function Assert-ExpectedWorkflowInventory(
    [System.Collections.IDictionary]$WorkflowTexts,
    [string]$Source
) {
    # This is intentionally a filename allowlist, not a semantic YAML parser. The exact build.yml
    # contract is validated above; non-build workflow job structure is validated separately below.
    # F-13: build-linux.yml is the additive Linux check; its exact contract is Assert-LinuxBuildWorkflowContract.
    $expectedWorkflowNames = @("build.yml", "stable-release.yml", "testing-release.yml", "build-linux.yml")
    $actualWorkflowNames = @($WorkflowTexts.Keys | ForEach-Object { [string]$_ })

    if ($actualWorkflowNames.Count -ne $expectedWorkflowNames.Count) {
        throw "$Source must contain exactly the four protected workflow files; found $($actualWorkflowNames.Count)."
    }
    foreach ($expectedWorkflowName in $expectedWorkflowNames) {
        $matchCount = @($actualWorkflowNames | Where-Object {
            [string]::Equals($_, $expectedWorkflowName, [System.StringComparison]::Ordinal)
        }).Count
        if ($matchCount -ne 1) {
            throw "$Source must contain exactly one workflow named '$expectedWorkflowName'; found $matchCount."
        }
    }
    foreach ($actualWorkflowName in $actualWorkflowNames) {
        $allowed = @($expectedWorkflowNames | Where-Object {
            [string]::Equals($_, $actualWorkflowName, [System.StringComparison]::Ordinal)
        }).Count -eq 1
        if (-not $allowed) {
            throw "$Source contains unexpected workflow file '$actualWorkflowName'."
        }
    }
}

function Assert-NoJobDisplayNames(
    [string]$Text,
    [string]$Source
) {
    $lines = @($Text -split '\r?\n')
    foreach ($line in $lines) {
        $leadingWhitespace = [regex]::Match($line, '^[ \t]*').Value
        if ($leadingWhitespace.Contains("`t")) {
            throw "$Source must not use tab indentation."
        }
    }

    $jobsLines = Get-JobsBlock $Text $Source
    $seenJobs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $currentJob = $null
    $currentJobHasDirectProperty = $false
    foreach ($line in $jobsLines) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }

        $indent = $line.Length - $line.TrimStart().Length
        if ($indent -eq 2) {
            if ($null -ne $currentJob -and -not $currentJobHasDirectProperty) {
                throw "$Source jobs.$currentJob must contain at least one canonical direct property at four-space indentation."
            }
            $jobKey = Get-MappingKey $line 2
            if ($null -eq $jobKey) {
                throw "$Source jobs entries must use canonical unquoted block mapping keys."
            }
            $remainder = $trimmed.Substring($trimmed.IndexOf(':') + 1).Trim()
            if ($remainder -and -not $remainder.StartsWith("#", [System.StringComparison]::Ordinal)) {
                throw "$Source jobs.$jobKey must use a canonical block mapping without flow, alias, anchor, or merge syntax."
            }
            if (-not $seenJobs.Add($jobKey)) {
                throw "$Source jobs contains duplicate job key '$jobKey'."
            }
            $currentJob = $jobKey
            $currentJobHasDirectProperty = $false
            continue
        }

        if ($indent -lt 4) {
            throw "$Source jobs content must use canonical two-space job indentation."
        }
        if ($indent -gt 4) {
            if (-not $currentJobHasDirectProperty) {
                throw "$Source jobs.$currentJob must declare a canonical direct property at four-space indentation before nested content."
            }
            continue
        }
        if ($trimmed.StartsWith("- ", [System.StringComparison]::Ordinal)) {
            # GitHub Actions permits indentless sequences under a job property such as steps:.
            if (-not $currentJobHasDirectProperty) {
                throw "$Source jobs.$currentJob first direct child must be a canonical property, not a sequence item."
            }
            continue
        }
        if ($null -eq $currentJob) {
            throw "$Source job properties must follow a canonical block job entry."
        }

        $propertyKey = Get-MappingKey $line 4
        if ($null -eq $propertyKey) {
            throw "$Source jobs.$currentJob properties must use canonical unquoted mapping keys."
        }
        if ([string]::Equals($propertyKey, "name", [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$Source jobs.$currentJob must not define a job-level 'name' property."
        }
        $currentJobHasDirectProperty = $true
    }

    if ($seenJobs.Count -eq 0) {
        throw "$Source top-level jobs entry must contain at least one canonical block job."
    }
    if ($null -ne $currentJob -and -not $currentJobHasDirectProperty) {
        throw "$Source jobs.$currentJob must contain at least one canonical direct property at four-space indentation."
    }
}

function Assert-NoJobDisplayNamesRejected(
    [string]$Text,
    [string]$Description,
    [string]$ExpectedMessagePattern
) {
    $rejected = $false
    try {
        Assert-NoJobDisplayNames $Text "adversarial non-build workflow ($Description)"
    }
    catch {
        if ($ExpectedMessagePattern -and $_.Exception.Message -cnotmatch $ExpectedMessagePattern) {
            throw "Non-build job-name guard rejected adversarial workflow '$Description' for the wrong reason: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Non-build job-name guard accepted adversarial workflow: $Description."
    }
}

function Assert-WorkflowInventoryRejected(
    [System.Collections.IDictionary]$WorkflowTexts,
    [string]$Description,
    [string]$ExpectedMessagePattern
) {
    $rejected = $false
    try {
        Assert-ExpectedWorkflowInventory $WorkflowTexts "adversarial workflow inventory ($Description)"
    }
    catch {
        if ($ExpectedMessagePattern -and $_.Exception.Message -cnotmatch $ExpectedMessagePattern) {
            throw "Workflow inventory guard rejected adversarial inventory '$Description' for the wrong reason: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Workflow inventory guard accepted adversarial inventory: $Description."
    }
}

# F-13: build-linux.yml is an additive Linux check. It must never shadow or compose the protected Windows
# required-check name, must stay read-only, and may use only the reviewed action references below, all pinned to
# full commit SHAs. checkout, setup-dotnet and upload-artifact reuse the SHAs pinned by the release workflows;
# actions/cache is pinned to the v6.1.0 commit.
$linuxAllowedUsesLines = @(
    "      uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1",
    "      uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0",
    "      uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0",
    "      uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1"
)
$linuxStepNames = @(
    "Checkout source",
    "Setup .NET",
    "Install system packages",
    "Cache FFmpeg tarball",
    "Fetch FFmpeg",
    "Verify",
    "Publish",
    "Upload package"
)

function Assert-LinuxBuildWorkflowContract([string]$Text, [string]$Source) {
    $lines = @($Text -split '\r?\n')
    foreach ($line in $lines) {
        $leadingWhitespace = [regex]::Match($line, '^[ \t]*').Value
        if ($leadingWhitespace.Contains("`t")) {
            throw "$Source must not use tab indentation."
        }
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }
        if ($trimmed -cmatch '^(-\s+)?["''?]' -or $trimmed -cmatch '^(-\s+)?<<\s*:' -or $trimmed -cmatch ':\s*[&*][A-Za-z0-9_-]') {
            throw "$Source must use canonical unquoted block mapping keys without explicit keys, anchors, aliases, or merges: '$trimmed'."
        }
        if ($trimmed -cmatch '^(-\s+)?(continue-on-error|if)\s*:') {
            throw "$Source must not make any step conditional or non-fatal: '$trimmed'."
        }
    }
    if ($Text.Contains("LLPlayer Build & Test")) {
        throw "$Source must not use the protected Windows required-check name 'LLPlayer Build & Test'."
    }
    foreach ($forbiddenToken in @("pull_request_target", "secrets.", "GITHUB_TOKEN", "github.token")) {
        if ($Text.Contains($forbiddenToken)) {
            throw "$Source must not reference '$forbiddenToken'."
        }
    }

    Assert-AllowedMappingKeys $lines 0 @("name", "on", "permissions", "jobs") "workflow root" $Source
    $workflowNameIndex = Get-UniqueLineIndex $lines '^name:\s*Linux Build & Test\s*$' "workflow name 'Linux Build & Test'" $Source
    $onIndex = Get-UniqueBlockKeyIndex $lines 0 "on" "top-level on entry" $Source
    if ($workflowNameIndex -ge $onIndex) {
        throw "$Source must declare the workflow name before the top-level on entry."
    }
    for ($index = $workflowNameIndex + 1; $index -lt $onIndex; $index++) {
        $trimmed = $lines[$index].Trim()
        if ($trimmed -and -not $trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            throw "$Source workflow name must not use multiline scalar continuation."
        }
    }

    $onLines = Get-ChildMappingBlock $lines 0 "on" "top-level on entry" $Source
    Assert-AllowedMappingKeys $onLines 2 @("push", "pull_request", "workflow_dispatch") "workflow triggers" $Source
    foreach ($trigger in @("push", "pull_request")) {
        $triggerLines = Get-ChildMappingBlock $onLines 2 $trigger "$trigger trigger" $Source
        Assert-AllowedMappingKeys $triggerLines 4 @("branches") "$trigger trigger" $Source
        Assert-NoNestedContent $triggerLines 4 "$trigger trigger" $Source
        Require-StepLine $triggerLines '^    branches:\s*\[ "main" \]\s*$' "$trigger trigger must target only main" $Source
    }
    Require-StepLine $onLines '^  workflow_dispatch:\s*$' "workflow_dispatch must be a plain manual trigger" $Source
    Assert-NoNestedContent $onLines 4 "workflow triggers" $Source

    $permissionLines = Get-ChildMappingBlock $lines 0 "permissions" "top-level permissions entry" $Source
    Assert-AllowedMappingKeys $permissionLines 2 @("contents") "top-level permissions" $Source
    Assert-NoNestedContent $permissionLines 2 "top-level permissions" $Source
    Require-StepLine $permissionLines '^  contents:\s*read\s*$' "permissions must be exactly contents: read" $Source

    $jobsLines = Get-JobsBlock $Text $Source
    Assert-AllowedMappingKeys $jobsLines 2 @("linux") "top-level jobs" $Source
    $jobLines = Get-ChildMappingBlock $jobsLines 2 "linux" "jobs.linux entry" $Source
    Assert-AllowedMappingKeys $jobLines 4 @("name", "runs-on", "timeout-minutes", "env", "steps") "jobs.linux" $Source
    $jobNameIndex = Get-UniqueLineIndex $jobLines '^    name:\s*LLPlayer Linux Build & Test\s*$' "jobs.linux name 'LLPlayer Linux Build & Test'" $Source
    $runsOnIndex = Get-UniqueLineIndex $jobLines '^    runs-on:\s*ubuntu-24\.04\s*$' "jobs.linux runner 'ubuntu-24.04'" $Source
    if ($runsOnIndex -ne $jobNameIndex + 1) {
        throw "$Source must declare jobs.linux runs-on: ubuntu-24.04 directly after its name (no scalar continuation)."
    }
    $envLines = Get-ChildMappingBlock $jobLines 4 "env" "jobs.linux env" $Source
    Assert-AllowedMappingKeys $envLines 6 @("DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO") "jobs.linux env" $Source
    Assert-NoNestedContent $envLines 6 "jobs.linux env" $Source

    $usesLines = @($lines | Where-Object { $_ -match 'u(ses|\\u0073es)' })
    foreach ($usesLine in $usesLines) {
        if ($linuxAllowedUsesLines -cnotcontains $usesLine.TrimEnd()) {
            throw "$Source uses an unapproved or unpinned action reference: '$($usesLine.Trim())'."
        }
    }
    foreach ($allowedUses in $linuxAllowedUsesLines) {
        $count = @($usesLines | Where-Object { $_.TrimEnd() -ceq $allowedUses }).Count
        if ($count -ne 1) {
            throw "$Source must use '$($allowedUses.Trim())' exactly once; found $count."
        }
    }

    $stepsIndex = Get-UniqueLineIndex $jobLines '^    steps:\s*$' "jobs.linux.steps entry" $Source
    if ($stepsIndex -ge $jobLines.Count - 1) {
        throw "$Source jobs.linux.steps must not be empty."
    }
    $stepLines = @($jobLines[($stepsIndex + 1)..($jobLines.Count - 1)])
    $stepCount = @($stepLines | Where-Object { $_ -cmatch '^    - ' }).Count
    if ($stepCount -ne $linuxStepNames.Count) {
        throw "$Source jobs.linux.steps must contain exactly the $($linuxStepNames.Count) reviewed steps; found $stepCount."
    }
    $previousStart = -1
    $steps = @{}
    foreach ($stepName in $linuxStepNames) {
        $step = Get-NamedStep $stepLines $stepName $Source
        if ($step.Start -le $previousStart) {
            throw "$Source must order jobs.linux.steps as: $($linuxStepNames -join ', ')."
        }
        $previousStart = $step.Start
        $steps[$stepName] = $step
    }
    foreach ($runOnlyStep in @("Fetch FFmpeg", "Verify", "Publish")) {
        Assert-AllowedMappingKeys $steps[$runOnlyStep].Lines 6 @("run") "$runOnlyStep step" $Source
        Assert-NoNestedContent $steps[$runOnlyStep].Lines 6 "$runOnlyStep step" $Source
    }
    Require-StepLine $steps["Fetch FFmpeg"].Lines '^      run:\s*bash scripts/linux/fetch-ffmpeg\.sh\s*$' "Fetch FFmpeg must run scripts/linux/fetch-ffmpeg.sh" $Source
    Require-StepLine $steps["Verify"].Lines '^      run:\s*xvfb-run -a bash scripts/linux/verify\.sh\s*$' "Verify must run the full scripts/linux/verify.sh gate" $Source
    Require-StepLine $steps["Publish"].Lines '^      run:\s*bash scripts/linux/publish\.sh --out "\$RUNNER_TEMP/linux-package"\s*$' "Publish must run scripts/linux/publish.sh" $Source
    Require-StepLine $steps["Setup .NET"].Lines '^        dotnet-version:\s*10\.0\.x\s*$' "Setup .NET must install the frozen .NET 10.0.x SDK" $Source
    Require-StepLine $steps["Cache FFmpeg tarball"].Lines '^        key:\s*linux-ffmpeg-n8\.1-latest-linux64-gpl-shared-8\.1\.tar\.xz\s*$' "Cache FFmpeg tarball must be keyed on the FFmpeg asset name" $Source
    Require-StepLine $steps["Upload package"].Lines '^        if-no-files-found:\s*error\s*$' "Upload package must fail when the package is missing" $Source
}

function Assert-LinuxContractRejected(
    [string]$Text,
    [string]$Description,
    [string]$ExpectedMessagePattern
) {
    $rejected = $false
    try {
        Assert-LinuxBuildWorkflowContract $Text "adversarial Linux fixture ($Description)"
    }
    catch {
        if ($ExpectedMessagePattern -and $_.Exception.Message -cnotmatch $ExpectedMessagePattern) {
            throw "Linux workflow validator rejected adversarial fixture '$Description' for the wrong reason: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Linux workflow validator accepted adversarial fixture: $Description."
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
    name: LLPlayer Build & Test
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

$positiveLinuxFixture = @'
name: Linux Build & Test

on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
  workflow_dispatch:

permissions:
  contents: read

jobs:
  linux:
    name: LLPlayer Linux Build & Test
    runs-on: ubuntu-24.04
    timeout-minutes: 60
    env:
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
      DOTNET_NOLOGO: "1"

    steps:
    - name: Checkout source
      uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1
      with:
        persist-credentials: false

    - name: Setup .NET
      uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0
      with:
        dotnet-version: 10.0.x

    - name: Install system packages
      run: |
        sudo apt-get update
        sudo apt-get install -y --no-install-recommends libopenal1 xvfb

    - name: Cache FFmpeg tarball
      uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0
      with:
        path: |
          ~/.cache/llplayer/ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz
        key: linux-ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz

    - name: Fetch FFmpeg
      run: bash scripts/linux/fetch-ffmpeg.sh

    - name: Verify
      run: xvfb-run -a bash scripts/linux/verify.sh

    - name: Publish
      run: bash scripts/linux/publish.sh --out "$RUNNER_TEMP/linux-package"

    - name: Upload package
      uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1
      with:
        name: LLPlayer-linux-x64
        path: ${{ runner.temp }}/linux-package/*.tar.gz
        if-no-files-found: error
'@
Assert-LinuxBuildWorkflowContract $positiveLinuxFixture "positive Linux fixture"

$positiveWorkflowInventory = [ordered]@{
    "build.yml" = $positiveFixture
    "stable-release.yml" = "name: Stable Release`njobs:`n  build:`n    runs-on: windows-latest"
    "testing-release.yml" = "name: Testing Release`njobs:`n  build:`n    runs-on: windows-latest"
    "build-linux.yml" = $positiveLinuxFixture
}
Assert-ExpectedWorkflowInventory $positiveWorkflowInventory "positive workflow inventory"

$foldedScalarCollisionInventory = [ordered]@{
    "build.yml" = $positiveFixture
    "stable-release.yml" = "name: Stable Release`njobs:`n  build:`n    runs-on: windows-latest"
    "testing-release.yml" = "name: Testing Release`njobs:`n  build:`n    runs-on: windows-latest"
    "build-linux.yml" = $positiveLinuxFixture
    "folded-collision.yml" = "name: Folded Collision`njobs:`n  build:`n    name: >-`n      LLPlayer Build &`n      Test"
}
Assert-WorkflowInventoryRejected $foldedScalarCollisionInventory "a fifth workflow composes the required check with a folded scalar" "exactly the four protected workflow files; found 5"

$expressionComposedCollisionInventory = [ordered]@{
    "build.yml" = $positiveFixture
    "stable-release.yml" = "name: Stable Release`njobs:`n  build:`n    runs-on: windows-latest"
    "testing-release.yml" = "name: Testing Release`njobs:`n  build:`n    runs-on: windows-latest"
    "build-linux.yml" = $positiveLinuxFixture
    "expression-collision.yaml" = 'name: Expression Collision' + "`n" + 'jobs:' + "`n" + '  build:' + "`n" + "    name: `${{ 'LLPlayer Build &' }}`${{ ' Test' }}"
}
Assert-WorkflowInventoryRejected $expressionComposedCollisionInventory "a fifth workflow composes the required check with expressions" "exactly the four protected workflow files; found 5"

$unexpectedFourthWorkflowInventory = [ordered]@{
    "build.yml" = $positiveFixture
    "stable-release.yml" = "name: Stable Release"
    "testing-release.yml" = "name: Testing Release"
    "build-linux.yml" = "name: Linux Build & Test"
    "unexpected.yml" = "name: Unexpected Workflow"
}
Assert-WorkflowInventoryRejected $unexpectedFourthWorkflowInventory "an unrelated fifth workflow bypasses the protected inventory" "exactly the four protected workflow files; found 5"

$missingLinuxWorkflowInventory = [ordered]@{
    "build.yml" = $positiveFixture
    "stable-release.yml" = "name: Stable Release"
    "testing-release.yml" = "name: Testing Release"
}
Assert-WorkflowInventoryRejected $missingLinuxWorkflowInventory "the Linux workflow is removed" "exactly the four protected workflow files; found 3"

$renamedLinuxWorkflowInventory = [ordered]@{
    "build.yml" = $positiveFixture
    "stable-release.yml" = "name: Stable Release"
    "testing-release.yml" = "name: Testing Release"
    "build-linux.yaml" = $positiveLinuxFixture
}
Assert-WorkflowInventoryRejected $renamedLinuxWorkflowInventory "the Linux workflow is renamed to escape its contract" "exactly one workflow named 'build-linux.yml'; found 0"

$linuxRequiredCheckCollisionFixture = $positiveLinuxFixture.Replace("    name: LLPlayer Linux Build & Test", "    name: LLPlayer Build & Test")
Assert-LinuxContractRejected $linuxRequiredCheckCollisionFixture "the Linux job reuses the Windows required-check name" "protected Windows required-check name"

$linuxExpressionNameFixture = $positiveLinuxFixture.Replace("    name: LLPlayer Linux Build & Test", "    name: `${{ 'LLPlayer Build &' }}`${{ ' Test' }}")
Assert-LinuxContractRejected $linuxExpressionNameFixture "the Linux job composes the required-check name with expressions" "jobs.linux name 'LLPlayer Linux Build & Test'"

$linuxContinuedNameFixture = $positiveLinuxFixture.Replace("    name: LLPlayer Linux Build & Test", "    name: LLPlayer Linux Build & Test`n      shadow-suffix")
Assert-LinuxContractRejected $linuxContinuedNameFixture "the Linux job name uses a multiline continuation" "directly after its name"

$linuxMutableCheckoutFixture = $positiveLinuxFixture.Replace("actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1", "actions/checkout@v5")
Assert-LinuxContractRejected $linuxMutableCheckoutFixture "the Linux workflow uses a mutable checkout reference" "unapproved or unpinned action reference"

$linuxUnapprovedActionFixture = $positiveLinuxFixture.Replace("    - name: Fetch FFmpeg", "    - name: Extra`n      uses: example/unapproved@main`n`n    - name: Fetch FFmpeg")
Assert-LinuxContractRejected $linuxUnapprovedActionFixture "the Linux workflow adds an unapproved action" "unapproved or unpinned action reference"

$linuxMutableCacheFixture = $positiveLinuxFixture.Replace("      uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0", "      uses: actions/cache@v6")
Assert-LinuxContractRejected $linuxMutableCacheFixture "the Linux workflow uses a mutable cache reference" "unapproved or unpinned action reference"

$linuxQuotedUsesFixture = $positiveLinuxFixture.Replace("      uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0", '      "u\u0073es": example/unapproved@main')
Assert-LinuxContractRejected $linuxQuotedUsesFixture "the Linux workflow hides an action behind an escaped quoted key" "canonical unquoted block mapping keys"

$linuxWritePermissionFixture = $positiveLinuxFixture.Replace("  contents: read", "  contents: write")
Assert-LinuxContractRejected $linuxWritePermissionFixture "the Linux workflow requests write access" "permissions must be exactly contents: read"

$linuxExtraPermissionFixture = $positiveLinuxFixture.Replace("  contents: read", "  contents: read`n  actions: write")
Assert-LinuxContractRejected $linuxExtraPermissionFixture "the Linux workflow adds a permission" "forbidden or unexpected key 'actions'"

$linuxNonFatalVerifyFixture = $positiveLinuxFixture.Replace("      run: xvfb-run -a bash scripts/linux/verify.sh", "      continue-on-error: true`n      run: xvfb-run -a bash scripts/linux/verify.sh")
Assert-LinuxContractRejected $linuxNonFatalVerifyFixture "the Linux verify step is non-fatal" "conditional or non-fatal"

$linuxFastOnlyVerifyFixture = $positiveLinuxFixture.Replace("bash scripts/linux/verify.sh", "bash scripts/linux/verify.sh --fast")
Assert-LinuxContractRejected $linuxFastOnlyVerifyFixture "the Linux verify step runs only the fast gate" "Verify must run the full scripts/linux/verify.sh gate"

$linuxSecondJobFixture = $positiveLinuxFixture.TrimEnd() + "`n  extra:`n    runs-on: ubuntu-24.04`n    steps:`n    - run: echo extra"
Assert-LinuxContractRejected $linuxSecondJobFixture "the Linux workflow adds a second job" "forbidden or unexpected key 'extra'"

$linuxTargetTriggerFixture = $positiveLinuxFixture.Replace("  workflow_dispatch:", "  workflow_dispatch:`n  pull_request_target:")
Assert-LinuxContractRejected $linuxTargetTriggerFixture "the Linux workflow runs on pull_request_target" "pull_request_target"

$linuxSecretFixture = $positiveLinuxFixture.Replace('      DOTNET_NOLOGO: "1"', '      DOTNET_NOLOGO: "1"' + "`n" + '      TOKEN: ${{ secrets.RELEASE_TOKEN }}')
Assert-LinuxContractRejected $linuxSecretFixture "the Linux workflow reads a secret" "must not reference 'secrets.'"


$positiveNonBuildWorkflow = @'
name: Non-Build Workflow
on:
  workflow_dispatch:
jobs:
  build:
    runs-on: windows-latest
    steps:
    - name: Placeholder
      run: Write-Output "ok"
'@
Assert-NoJobDisplayNames $positiveNonBuildWorkflow "positive non-build workflow"

$literalNonBuildJobNameFixture = $positiveNonBuildWorkflow.Replace(
    "    runs-on: windows-latest",
    "    name: LLPlayer Build & Test`n    runs-on: windows-latest"
)
Assert-NoJobDisplayNamesRejected $literalNonBuildJobNameFixture "a testing job uses the literal required-check name" "must not define a job-level 'name' property"

$expressionNonBuildJobNameFixture = $positiveNonBuildWorkflow.Replace(
    "    runs-on: windows-latest",
    '    name: LLPlayer ${{ ''Build'' }} & Test' + "`n    runs-on: windows-latest"
)
Assert-NoJobDisplayNamesRejected $expressionNonBuildJobNameFixture "a testing job composes the required-check name with an expression" "must not define a job-level 'name' property"

$foldedNonBuildJobNameFixture = $positiveNonBuildWorkflow.Replace(
    "    runs-on: windows-latest",
    "    name: >-`n      LLPlayer Build &`n      Test`n    runs-on: windows-latest"
)
Assert-NoJobDisplayNamesRejected $foldedNonBuildJobNameFixture "a testing job composes the required-check name with a folded scalar" "must not define a job-level 'name' property"

$overIndentedLiteralJobNameFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    "  build:`n      name: LLPlayer Build & Test`n      runs-on: windows-latest`n      steps:`n      - name: Placeholder`n        run: Write-Output `"ok`""
)
Assert-NoJobDisplayNamesRejected $overIndentedLiteralJobNameFixture "a testing job over-indents a literal display name before its first direct property" "before nested content"

$overIndentedExpressionJobNameFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    '  build:' + "`n" + '      name: LLPlayer ${{ ''Build'' }} & Test' + "`n" + "      runs-on: windows-latest`n      steps:`n      - name: Placeholder`n        run: Write-Output `"ok`""
)
Assert-NoJobDisplayNamesRejected $overIndentedExpressionJobNameFixture "a testing job over-indents an expression-composed display name before its first direct property" "before nested content"

$overIndentedFoldedJobNameFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    "  build:`n      name: >-`n        LLPlayer Build &`n        Test`n      runs-on: windows-latest`n      steps:`n      - name: Placeholder`n        run: Write-Output `"ok`""
)
Assert-NoJobDisplayNamesRejected $overIndentedFoldedJobNameFixture "a testing job over-indents a folded display name before its first direct property" "before nested content"

$emptyJobFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    "  build:"
)
Assert-NoJobDisplayNamesRejected $emptyJobFixture "a non-build workflow defines an empty job" "must contain at least one canonical direct property"

$sequenceOnlyJobFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    "  build:`n    - name: Sequence-only bypass`n      run: Write-Output `"bypass`""
)
Assert-NoJobDisplayNamesRejected $sequenceOnlyJobFixture "a non-build workflow defines a sequence-only job" "first direct child must be a canonical property"

$escapedQuotedJobNameKeyFixture = $positiveNonBuildWorkflow.Replace(
    "    runs-on: windows-latest",
    '    "n\u0061me": LLPlayer Build & Test' + "`n    runs-on: windows-latest"
)
Assert-NoJobDisplayNamesRejected $escapedQuotedJobNameKeyFixture "a testing job hides name behind an escaped quoted key" "canonical unquoted syntax"

$explicitJobNameKeyFixture = $positiveNonBuildWorkflow.Replace(
    "    runs-on: windows-latest",
    "    ? name`n    : LLPlayer Build & Test`n    runs-on: windows-latest"
)
Assert-NoJobDisplayNamesRejected $explicitJobNameKeyFixture "a testing job uses explicit mapping-key syntax for name" "canonical mapping syntax"

$tabIndentedJobFixture = $positiveNonBuildWorkflow.Replace("  build:", "`tbuild:")
Assert-NoJobDisplayNamesRejected $tabIndentedJobFixture "a non-build workflow uses tab-indented jobs" "must not use tab indentation"

$quotedJobKeyFixture = $positiveNonBuildWorkflow.Replace("  build:", '  "build":')
Assert-NoJobDisplayNamesRejected $quotedJobKeyFixture "a non-build workflow quotes a job key" "canonical unquoted syntax"

$explicitJobKeyFixture = $positiveNonBuildWorkflow.Replace("  build:", "  ? build`n  :")
Assert-NoJobDisplayNamesRejected $explicitJobKeyFixture "a non-build workflow uses explicit job-key syntax" "canonical mapping syntax"

$flowJobDefinitionFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    "  build: { runs-on: windows-latest }"
)
Assert-NoJobDisplayNamesRejected $flowJobDefinitionFixture "a non-build workflow uses a flow job definition" "must use a canonical block mapping"

$aliasJobDefinitionFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    "  build: *shared-job"
)
Assert-NoJobDisplayNamesRejected $aliasJobDefinitionFixture "a non-build workflow aliases a job definition" "must use a canonical block mapping"

$anchorJobDefinitionFixture = $positiveNonBuildWorkflow.Replace(
    "  build:`n    runs-on: windows-latest`n    steps:`n    - name: Placeholder`n      run: Write-Output `"ok`"",
    "  build: &shared-job"
)
Assert-NoJobDisplayNamesRejected $anchorJobDefinitionFixture "a non-build workflow anchors a job definition" "must use a canonical block mapping"

$mergeJobDefinitionFixture = $positiveNonBuildWorkflow.Replace("  build:", "  <<: *shared-job")
Assert-NoJobDisplayNamesRejected $mergeJobDefinitionFixture "a non-build workflow merges a job definition" "canonical unquoted syntax"

$missingWorkflowNameFixture = $positiveFixture.Replace("name: Build & Test", "# workflow name intentionally missing")
Assert-ContractRejected $missingWorkflowNameFixture "stable workflow name is missing" "stable workflow name 'Build & Test'"

$wrongWorkflowNameFixture = $positiveFixture.Replace("name: Build & Test", "name: CI")
Assert-ContractRejected $wrongWorkflowNameFixture "stable workflow name drifts" "stable workflow name 'Build & Test'"

$duplicateWorkflowNameFixture = $positiveFixture.Replace(
    "name: Build & Test",
    "name: Build & Test`nname: CI"
)
Assert-ContractRejected $duplicateWorkflowNameFixture "stable workflow name is overridden by a duplicate key" "duplicate key 'name'"

$continuedWorkflowNameFixture = $positiveFixture.Replace(
    "name: Build & Test",
    "name: Build & Test`n  hidden-suffix"
)
Assert-ContractRejected $continuedWorkflowNameFixture "stable workflow name hides a multiline suffix" "workflow name must not use multiline scalar continuation"

$missingRequiredCheckNameFixture = $positiveFixture.Replace("    name: LLPlayer Build & Test", "    # required-check name intentionally missing")
Assert-ContractRejected $missingRequiredCheckNameFixture "required-check name is missing" "stable required-check name 'LLPlayer Build & Test'"

$releaseStyleCheckNameFixture = $positiveFixture.Replace("    name: LLPlayer Build & Test", "    name: build")
Assert-ContractRejected $releaseStyleCheckNameFixture "required-check name collides with release build jobs" "stable required-check name 'LLPlayer Build & Test'"

$genericRequiredCheckNameFixture = $positiveFixture.Replace("    name: LLPlayer Build & Test", "    name: Build & Test")
Assert-ContractRejected $genericRequiredCheckNameFixture "required-check name drifts to a generic value" "stable required-check name 'LLPlayer Build & Test'"

$expressionRequiredCheckNameFixture = $positiveFixture.Replace(
    "    name: LLPlayer Build & Test",
    '    name: ${{ github.ref }}'
)
Assert-ContractRejected $expressionRequiredCheckNameFixture "required-check name becomes dynamic" "stable required-check name 'LLPlayer Build & Test'"

$duplicateRequiredCheckNameFixture = $positiveFixture.Replace(
    "    name: LLPlayer Build & Test",
    "    name: LLPlayer Build & Test`n    name: build"
)
Assert-ContractRejected $duplicateRequiredCheckNameFixture "required-check name is overridden by a duplicate key" "duplicate key 'name'"

$continuedRequiredCheckNameFixture = $positiveFixture.Replace(
    "    name: LLPlayer Build & Test",
    "    name: LLPlayer Build & Test`n      release-style-collision"
)
Assert-ContractRejected $continuedRequiredCheckNameFixture "required-check name hides a multiline suffix" "required-check name must not use multiline scalar continuation"

$commentDecoyRequiredCheckNameFixture = $positiveFixture.Replace(
    "    name: LLPlayer Build & Test",
    "    # name: LLPlayer Build & Test`n    name: build"
)
Assert-ContractRejected $commentDecoyRequiredCheckNameFixture "required-check name exists only in a comment decoy" "stable required-check name 'LLPlayer Build & Test'"

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

$workflowDirectory = Split-Path -Parent $buildWorkflow
$workflowTexts = [ordered]@{}
Get-ChildItem -LiteralPath $workflowDirectory -File |
    Where-Object { $_.Extension -in @(".yml", ".yaml") } |
    Sort-Object -Property FullName |
    ForEach-Object { $workflowTexts[$_.Name] = Get-Content -LiteralPath $_.FullName -Raw }
Assert-ExpectedWorkflowInventory $workflowTexts $workflowDirectory
Assert-LinuxBuildWorkflowContract $workflowTexts["build-linux.yml"] (Join-Path $workflowDirectory "build-linux.yml")
foreach ($nonBuildWorkflowName in @("stable-release.yml", "testing-release.yml")) {
    Assert-NoJobDisplayNames $workflowTexts[$nonBuildWorkflowName] (Join-Path $workflowDirectory $nonBuildWorkflowName)
}

Write-Host "Build workflow build/test verification completed."

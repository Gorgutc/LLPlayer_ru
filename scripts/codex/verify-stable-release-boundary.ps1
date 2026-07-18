$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$workflowPath = Join-Path $repoRoot ".github\workflows\stable-release.yml"
$expectedWorkflowSha256 = "255bf52b5320d634dbec388f0f55126f7dd62200d3381e5de448059ac38d72c4"

function Normalize-Text([string]$Text) {
    return (($Text -replace "`r`n", "`n") -replace "`r", "`n").TrimEnd("`n")
}

function Get-TextSha256([string]$Text) {
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Text)
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $hasher.ComputeHash($bytes)
    }
    finally {
        $hasher.Dispose()
    }
    return ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

function Assert-CanonicalWorkflowHash([string]$Text, [string]$Source) {
    $normalized = Normalize-Text $Text
    $actualWorkflowSha256 = Get-TextSha256 $normalized
    if (-not [string]::Equals(
        $actualWorkflowSha256,
        $expectedWorkflowSha256,
        [System.StringComparison]::Ordinal)) {
        throw "$Source drifted from the reviewed normalized Stable workflow SHA-256."
    }
}

function Require-Fragment(
    [string]$Text,
    [string]$Fragment,
    [string]$Description,
    [string]$Source
) {
    $count = ([regex]::Matches($Text, [regex]::Escape($Fragment))).Count
    if ($count -ne 1) {
        throw "$Source must contain exactly one $Description; found $count."
    }
}

function Require-Pattern(
    [string]$Text,
    [string]$Pattern,
    [string]$Description,
    [string]$Source,
    [int]$ExpectedCount = 1
) {
    $count = ([regex]::Matches($Text, $Pattern)).Count
    if ($count -ne $ExpectedCount) {
        throw "$Source must contain exactly $ExpectedCount $Description; found $count."
    }
}

function Forbid-Pattern(
    [string]$Text,
    [string]$Pattern,
    [string]$Description,
    [string]$Source
) {
    if ([regex]::IsMatch($Text, $Pattern)) {
        throw "$Source contains forbidden $Description."
    }
}

function Assert-ExactUsesMultiset(
    [string]$Text,
    [string[]]$ExpectedUses,
    [string]$Description,
    [string]$Source
) {
    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $Text,
        '(?m)^ {8}uses:[ \t]+(?<value>[^\r\n]+?)[ \t]*$')
    $actualUses = @(
        foreach ($match in $matches) {
            $match.Groups['value'].Value.Trim()
        }
    )
    $actualSorted = @($actualUses | Sort-Object -CaseSensitive)
    $expectedSorted = @($ExpectedUses | Sort-Object -CaseSensitive)
    if ($actualSorted.Count -ne $expectedSorted.Count) {
        throw "$Source must match the $Description; expected $($expectedSorted.Count) uses entries, found $($actualSorted.Count)."
    }
    for ($index = 0; $index -lt $expectedSorted.Count; $index++) {
        if (-not [string]::Equals(
                $actualSorted[$index],
                $expectedSorted[$index],
                [System.StringComparison]::Ordinal)) {
            throw "$Source must match the $Description; unexpected uses entry '$($actualSorted[$index])'."
        }
    }
}

function Assert-CanonicalActionSyntax([string]$Text, [string]$Source) {
    $forbiddenPatterns = @(
        @{
            Pattern = '(?m)^ {0,4}[A-Za-z0-9_.-]+[ \t]*:[ \t]*[|>][0-9+-]*[ \t]*(?:#[^\r\n]*)?$'
            Description = 'workflow or job block scalar'
        },
        @{
            Pattern = '(?m)^ {0,8}[A-Za-z0-9_.-]+[ \t]*:[ \t]*"(?:(?:\\.)|[^"\\])*(?:\\)?[ \t]*$'
            Description = 'multiline double-quoted workflow, job, or step scalar'
        },
        @{
            Pattern = '(?m)^ {0,8}[A-Za-z0-9_.-]+[ \t]*:[ \t]*''(?:''''|[^''])*[ \t]*$'
            Description = 'multiline single-quoted workflow, job, or step scalar'
        },
        @{
            Pattern = '(?m)^[ \t]*-[ \t]+name[ \t]*:[ \t]*[|>][0-9+-]*[ \t]*(?:#[^\r\n]*)?$'
            Description = 'step-name block scalar'
        },
        @{
            Pattern = '(?m)^[ \t]*-[ \t]+name[ \t]*:[ \t]*"(?:(?:\\.)|[^"\\])*(?:\\)?[ \t]*$'
            Description = 'multiline double-quoted step-name scalar'
        },
        @{
            Pattern = '(?m)^[ \t]*-[ \t]+name[ \t]*:[ \t]*''(?:''''|[^''])*[ \t]*$'
            Description = 'multiline single-quoted step-name scalar'
        },
        @{
            Pattern = '(?m)^[ \t]*-[ \t]*(?:#[^\r\n]*)?$'
            Description = 'bare step declaration with arbitrarily deep child mappings'
        },
        @{
            Pattern = '(?m)^[ \t]*-[ \t]*[\{\[\?&*!"''<]'
            Description = 'flow, explicit, anchored, aliased, or tagged step declaration'
        },
        @{
            Pattern = '(?m)^[ \t]*(?:"[^\r\n]*"|''[^\r\n]*'')[ \t]*:'
            Description = 'quoted or escaped workflow key'
        },
        @{
            Pattern = '(?m)^[ \t]*(?:\?[ \t]*(?:#[^\r\n]*)?$|\?[ \t]+[^\r\n]+|<<[ \t]*:)'
            Description = 'explicit or merged workflow key'
        },
        @{
            Pattern = '(?m)^[ \t]*(?:&[^ \t\r\n,\[\]\{\}]+[ \t]+[^:\r\n]+|\*[^ \t\r\n,\[\]\{\}]+[ \t]*):'
            Description = 'anchored or aliased workflow key'
        },
        @{
            Pattern = '(?m)^[ \t]*![^\r\n:]*[ \t]+[^\r\n:]+:'
            Description = 'tagged workflow key'
        },
        @{
            Pattern = '(?m)^[ \t]*[A-Za-z0-9_.-]+[ \t]*:[ \t]*[&*!]'
            Description = 'anchored, aliased, or tagged workflow value'
        },
        @{
            Pattern = '(?m)^[ \t]*[A-Za-z0-9_.-]+[ \t]*:[ \t]*\{[ \t]*[^}\r\n \t]'
            Description = 'flow-style workflow mapping value'
        },
        @{
            Pattern = '(?m)^[ \t]*steps[ \t]*:[ \t]*\['
            Description = 'flow-style steps sequence'
        }
    )
    foreach ($entry in $forbiddenPatterns) {
        if ([System.Text.RegularExpressions.Regex]::IsMatch($Text, $entry.Pattern)) {
            throw "$Source contains forbidden noncanonical action syntax: $($entry.Description)."
        }
    }
}

function Get-JobBlock(
    [string[]]$Lines,
    [string]$JobName,
    [string]$Source
) {
    $marker = "  $JobName`:"
    $indices = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ([string]::Equals($Lines[$index], $marker, [System.StringComparison]::Ordinal)) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source must contain exactly one jobs.$JobName block; found $($indices.Count)."
    }

    $start = [int]$indices[0]
    $end = $Lines.Count
    for ($index = $start + 1; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -cmatch '^  [A-Za-z0-9_-]+:\s*$') {
            $end = $index
            break
        }
    }
    return @($Lines[$start..($end - 1)])
}

function Get-MappingKey([string]$Line, [int]$Indent, [string]$Source) {
    $actualIndent = $Line.Length - $Line.TrimStart().Length
    if ($actualIndent -ne $Indent) {
        return $null
    }

    $trimmed = $Line.Trim()
    if (-not $trimmed -or
        $trimmed.StartsWith("#", [System.StringComparison]::Ordinal) -or
        $trimmed.StartsWith("- ", [System.StringComparison]::Ordinal)) {
        return $null
    }

    $colonIndex = $trimmed.IndexOf(':')
    if ($colonIndex -lt 1) {
        throw "$Source protected Stable mapping is malformed: '$trimmed'."
    }
    $key = $trimmed.Substring(0, $colonIndex).Trim()
    if ($key -cnotmatch '^[A-Za-z0-9_-]+$') {
        throw "$Source protected Stable key must use canonical unquoted syntax: '$key'."
    }
    return $key
}

function Get-BlockLines(
    [string[]]$Lines,
    [int]$Indent,
    [string]$Key,
    [string]$Description,
    [string]$Source
) {
    $indices = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $lineKey = Get-MappingKey $Lines[$index] $Indent $Source
        if ([string]::Equals($lineKey, $Key, [System.StringComparison]::Ordinal)) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source must contain exactly one $Description block; found $($indices.Count)."
    }

    $start = [int]$indices[0]
    $remainder = $Lines[$start].Trim().Substring($Lines[$start].Trim().IndexOf(':') + 1).Trim()
    if ($remainder -and -not $remainder.StartsWith("#", [System.StringComparison]::Ordinal)) {
        throw "$Source $Description must be a canonical block mapping."
    }

    $end = $Lines.Count
    for ($index = $start + 1; $index -lt $Lines.Count; $index++) {
        if ($null -ne (Get-MappingKey $Lines[$index] $Indent $Source)) {
            $end = $index
            break
        }
    }
    if ($end -le $start + 1) {
        throw "$Source $Description block must not be empty."
    }
    return @($Lines[($start + 1)..($end - 1)])
}

function Assert-AllowedMappingKeys(
    [string[]]$Lines,
    [int]$Indent,
    [string[]]$AllowedKeys,
    [string[]]$RequiredKeys,
    [string]$Description,
    [string]$Source
) {
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in $Lines) {
        $key = Get-MappingKey $line $Indent $Source
        if ($null -eq $key) {
            continue
        }
        if ($AllowedKeys -cnotcontains $key) {
            throw "$Source $Description contains forbidden or unexpected key '$key'."
        }
        if (-not $seen.Add($key)) {
            throw "$Source $Description contains duplicate key '$key'."
        }
    }
    foreach ($requiredKey in $RequiredKeys) {
        if (-not $seen.Contains($requiredKey)) {
            throw "$Source $Description is missing required key '$requiredKey'."
        }
    }
}

function Get-StepBlock(
    [string[]]$JobBlock,
    [string]$StepName,
    [string]$JobName,
    [string]$Source
) {
    $marker = "      - name: $StepName"
    $indices = @()
    for ($index = 0; $index -lt $JobBlock.Count; $index++) {
        if ([string]::Equals($JobBlock[$index], $marker, [System.StringComparison]::Ordinal)) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source jobs.$JobName must contain exactly one '$StepName' step; found $($indices.Count)."
    }

    $start = [int]$indices[0]
    $end = $JobBlock.Count
    for ($index = $start + 1; $index -lt $JobBlock.Count; $index++) {
        if ($JobBlock[$index] -cmatch '^      - ') {
            $end = $index
            break
        }
    }
    return @($JobBlock[$start..($end - 1)])
}

function Assert-StepKeys(
    [string[]]$JobBlock,
    [string]$StepName,
    [string]$JobName,
    [string[]]$ExpectedKeys,
    [string]$Source
) {
    $step = Get-StepBlock $JobBlock $StepName $JobName $Source
    Assert-AllowedMappingKeys $step 8 $ExpectedKeys $ExpectedKeys "jobs.$JobName step '$StepName'" $Source
}

function Assert-JobNames(
    [string[]]$Lines,
    [string[]]$Expected,
    [string]$Source
) {
    $jobsIndex = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ([string]::Equals($Lines[$index], "jobs:", [System.StringComparison]::Ordinal)) {
            $jobsIndex += $index
        }
    }
    if ($jobsIndex.Count -ne 1) {
        throw "$Source must contain exactly one top-level jobs block."
    }

    $actual = New-Object System.Collections.Generic.List[string]
    for ($index = $jobsIndex[0] + 1; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -cmatch '^  ([A-Za-z0-9_-]+):\s*$') {
            $actual.Add($Matches[1])
        }
    }
    if ($actual.Count -ne $Expected.Count) {
        throw "$Source must contain exactly the four protected Stable jobs; found $($actual.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [string]::Equals($actual[$index], $Expected[$index], [System.StringComparison]::Ordinal)) {
            throw "$Source Stable job order drifted at position $($index + 1): '$($actual[$index])'."
        }
    }
}

function Assert-StepNames(
    [string[]]$JobBlock,
    [string[]]$Expected,
    [string]$JobName,
    [string]$Source
) {
    $actual = New-Object System.Collections.Generic.List[string]
    foreach ($line in $JobBlock) {
        if ($line -cmatch '^      - name:\s*(.+?)\s*$') {
            $actual.Add($Matches[1])
            continue
        }
        if ($line -cmatch '^      - ') {
            throw "$Source jobs.$JobName contains an anonymous or non-canonical step: '$($line.Trim())'."
        }
    }
    if ($actual.Count -ne $Expected.Count) {
        throw "$Source jobs.$JobName must contain exactly $($Expected.Count) named steps; found $($actual.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [string]::Equals($actual[$index], $Expected[$index], [System.StringComparison]::Ordinal)) {
            throw "$Source jobs.$JobName step order drifted at position $($index + 1): '$($actual[$index])'."
        }
    }
}

function Assert-OrderedFragments(
    [string]$Text,
    [string[]]$Fragments,
    [string]$Description,
    [string]$Source
) {
    $cursor = -1
    foreach ($fragment in $Fragments) {
        $next = $Text.IndexOf($fragment, $cursor + 1, [System.StringComparison]::Ordinal)
        if ($next -lt 0) {
            throw "$Source is missing ordered $Description fragment '$fragment'."
        }
        $cursor = $next
    }
}

function Assert-PowerShellRunBlocks(
    [string]$Text,
    [string]$Source,
    [int]$ExpectedCount
) {
    $lines = @($Text -split "`n")
    $runCount = 0
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -cnotmatch '^        run:\s*(.*)$') {
            continue
        }

        $runCount++
        $value = $Matches[1].Trim()
        if ([string]::Equals($value, "|", [System.StringComparison]::Ordinal)) {
            $body = New-Object System.Collections.Generic.List[string]
            for ($bodyIndex = $index + 1; $bodyIndex -lt $lines.Count; $bodyIndex++) {
                $line = $lines[$bodyIndex]
                $trimmed = $line.Trim()
                $indent = $line.Length - $line.TrimStart().Length
                if ($trimmed -and $indent -le 8) {
                    break
                }
                if (-not $trimmed) {
                    $body.Add("")
                    continue
                }
                if ($indent -lt 10) {
                    throw "$Source run block $runCount contains a line outside its canonical scalar indentation."
                }
                $body.Add($line.Substring(10))
            }
            $scriptText = ($body -join "`n").TrimEnd("`n")
        }
        elseif ($value.StartsWith(">", [System.StringComparison]::Ordinal) -or
                $value.StartsWith("|", [System.StringComparison]::Ordinal)) {
            throw "$Source run block $runCount must use only the canonical inline or '|' form."
        }
        else {
            $scriptText = $value
        }

        $tokens = $null
        $parseErrors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput(
            $scriptText,
            [ref]$tokens,
            [ref]$parseErrors)
        if ($parseErrors.Count -ne 0) {
            $messages = @($parseErrors | ForEach-Object { $_.Message }) -join "; "
            throw "$Source PowerShell run block $runCount has syntax error(s): $messages"
        }
    }

    if ($runCount -ne $ExpectedCount) {
        throw "$Source must contain exactly $ExpectedCount reviewed PowerShell run blocks; found $runCount."
    }
}

function Assert-StableReleaseBoundary([string]$Text, [string]$Source) {
    $normalized = Normalize-Text $Text
    $lines = @($normalized -split "`n")
    Assert-CanonicalActionSyntax $normalized $Source
    Assert-PowerShellRunBlocks $normalized $Source 9

    Assert-AllowedMappingKeys $lines 0 @("name", "on", "permissions", "concurrency", "jobs") @("name", "on", "permissions", "concurrency", "jobs") "workflow root" $Source
    $onBlock = Get-BlockLines $lines 0 "on" "top-level on" $Source
    Assert-AllowedMappingKeys $onBlock 2 @("workflow_dispatch") @("workflow_dispatch") "workflow triggers" $Source
    $dispatchBlock = Get-BlockLines $onBlock 2 "workflow_dispatch" "workflow_dispatch" $Source
    Assert-AllowedMappingKeys $dispatchBlock 4 @("inputs") @("inputs") "workflow_dispatch" $Source
    $inputsBlock = Get-BlockLines $dispatchBlock 4 "inputs" "workflow_dispatch inputs" $Source
    Assert-AllowedMappingKeys $inputsBlock 6 @("commit_sha", "release_tag") @("commit_sha", "release_tag") "workflow_dispatch inputs" $Source
    $commitInput = Get-BlockLines $inputsBlock 6 "commit_sha" "commit_sha input" $Source
    $tagInput = Get-BlockLines $inputsBlock 6 "release_tag" "release_tag input" $Source
    Assert-AllowedMappingKeys $commitInput 8 @("description", "required", "type") @("description", "required", "type") "commit_sha input" $Source
    Assert-AllowedMappingKeys $tagInput 8 @("description", "required", "type") @("description", "required", "type") "release_tag input" $Source
    $concurrencyBlock = Get-BlockLines $lines 0 "concurrency" "Stable concurrency" $Source
    Assert-AllowedMappingKeys $concurrencyBlock 2 @("group", "cancel-in-progress") @("group", "cancel-in-progress") "Stable concurrency" $Source

    Require-Fragment $normalized "name: Stable Release" "Stable workflow name" $Source
    Require-Fragment $normalized "permissions: {}" "deny-by-default workflow permission block" $Source
    Require-Fragment $normalized "concurrency:`n  group: stable-release`n  cancel-in-progress: false" "fixed non-cancelling Stable concurrency" $Source
    Require-Fragment $normalized "  workflow_dispatch:" "manual workflow_dispatch trigger" $Source
    Require-Fragment $normalized "        description: 'Exact full lowercase commit SHA to release'" "exact commit input" $Source
    Require-Fragment $normalized "        description: 'Strict stable tag (vMAJOR.MINOR.PATCH)'" "strict release tag input" $Source
    Require-Pattern $normalized '(?m)^        required: true$' "required Stable input markers" $Source 2
    Require-Pattern $normalized '(?m)^        type: string$' "string Stable input types" $Source 2

    foreach ($trigger in @("push", "pull_request", "schedule", "repository_dispatch", "workflow_call")) {
        Forbid-Pattern $normalized "(?m)^  $trigger`:" "$trigger trigger" $Source
    }
    Forbid-Pattern $normalized '(?m)^\s*if\s*:' "conditional success bypass" $Source

    Assert-JobNames $lines @("prepare", "build", "verify", "publish") $Source
    $prepare = Get-JobBlock $lines "prepare" $Source
    $build = Get-JobBlock $lines "build" $Source
    $verify = Get-JobBlock $lines "verify" $Source
    $publish = Get-JobBlock $lines "publish" $Source
    $prepareText = $prepare -join "`n"
    $buildText = $build -join "`n"
    $verifyText = $verify -join "`n"
    $publishText = $publish -join "`n"

    Assert-AllowedMappingKeys $prepare 4 @("runs-on", "permissions", "outputs", "steps") @("runs-on", "permissions", "outputs", "steps") "jobs.prepare" $Source
    Assert-AllowedMappingKeys $build 4 @("needs", "runs-on", "permissions", "outputs", "steps") @("needs", "runs-on", "permissions", "outputs", "steps") "jobs.build" $Source
    Assert-AllowedMappingKeys $verify 4 @("needs", "runs-on", "permissions", "outputs", "steps") @("needs", "runs-on", "permissions", "outputs", "steps") "jobs.verify" $Source
    Assert-AllowedMappingKeys $publish 4 @("needs", "runs-on", "permissions", "steps") @("needs", "runs-on", "permissions", "steps") "jobs.publish" $Source

    foreach ($jobEntry in @(
        @{ Name = "prepare"; Block = $prepare; Permission = "read"; OutputKeys = @("commit_sha", "release_tag", "archive_name") },
        @{ Name = "build"; Block = $build; Permission = "read"; OutputKeys = @("yt_dlp_version", "yt_dlp_sha256", "yt_dlp_size", "archive_sha256", "archive_size") },
        @{ Name = "verify"; Block = $verify; Permission = "read"; OutputKeys = @("yt_dlp_version", "yt_dlp_sha256", "yt_dlp_size", "archive_sha256", "archive_size") },
        @{ Name = "publish"; Block = $publish; Permission = "write"; OutputKeys = @() }
    )) {
        $permissionBlock = Get-BlockLines $jobEntry.Block 4 "permissions" "jobs.$($jobEntry.Name).permissions" $Source
        Assert-AllowedMappingKeys $permissionBlock 6 @("contents") @("contents") "jobs.$($jobEntry.Name).permissions" $Source
        if ($jobEntry.OutputKeys.Count -gt 0) {
            $outputBlock = Get-BlockLines $jobEntry.Block 4 "outputs" "jobs.$($jobEntry.Name).outputs" $Source
            Assert-AllowedMappingKeys $outputBlock 6 $jobEntry.OutputKeys $jobEntry.OutputKeys "jobs.$($jobEntry.Name).outputs" $Source
        }
    }

    foreach ($entry in @(
        @{ Name = "prepare"; Text = $prepareText; Permission = "read"; Needs = $null },
        @{ Name = "build"; Text = $buildText; Permission = "read"; Needs = "    needs: prepare" },
        @{ Name = "verify"; Text = $verifyText; Permission = "read"; Needs = "    needs: [prepare, build]" },
        @{ Name = "publish"; Text = $publishText; Permission = "write"; Needs = "    needs: [prepare, verify]" }
    )) {
        Require-Fragment $entry.Text "    runs-on: windows-latest" "jobs.$($entry.Name) GitHub-hosted Windows runner" $Source
        Require-Fragment $entry.Text "    permissions:`n      contents: $($entry.Permission)" "jobs.$($entry.Name) least-privilege permission" $Source
        if ($null -ne $entry.Needs) {
            Require-Fragment $entry.Text $entry.Needs "jobs.$($entry.Name) dependency" $Source
        }
    }
    Require-Pattern $normalized '(?m)^      contents: read$' "read-only job permission blocks" $Source 3
    Require-Pattern $normalized '(?m)^      contents: write$' "write-only publish permission block" $Source 1

    Assert-StepNames $prepare @(
        "Require trusted workflow ref",
        "Checkout trusted control source",
        "Validate stable release request",
        "Verify selected commit and release absence"
    ) "prepare" $Source
    Assert-StepNames $build @(
        "Checkout immutable release commit",
        "Verify immutable checkout",
        "Verify release version",
        "Setup .NET",
        "Full verification preflight",
        "Build & Package",
        "Validate package evidence",
        "Upload unverified stable artifact"
    ) "build" $Source
    Assert-StepNames $verify @(
        "Download unverified stable artifact",
        "Validate stable package and evidence",
        "Upload verified stable artifact"
    ) "verify" $Source
    Assert-StepNames $publish @(
        "Download verified stable artifact",
        "Validate verified artifact for publication",
        "Create immutable tag and draft release"
    ) "publish" $Source

    foreach ($stepContract in @(
        @{ Job = "prepare"; Block = $prepare; Name = "Require trusted workflow ref"; Keys = @("shell", "env", "run") },
        @{ Job = "prepare"; Block = $prepare; Name = "Checkout trusted control source"; Keys = @("uses", "with") },
        @{ Job = "prepare"; Block = $prepare; Name = "Validate stable release request"; Keys = @("id", "shell", "env", "run") },
        @{ Job = "prepare"; Block = $prepare; Name = "Verify selected commit and release absence"; Keys = @("uses", "env", "with") },
        @{ Job = "build"; Block = $build; Name = "Checkout immutable release commit"; Keys = @("uses", "with") },
        @{ Job = "build"; Block = $build; Name = "Verify immutable checkout"; Keys = @("shell", "env", "run") },
        @{ Job = "build"; Block = $build; Name = "Verify release version"; Keys = @("shell", "env", "run") },
        @{ Job = "build"; Block = $build; Name = "Setup .NET"; Keys = @("uses", "with") },
        @{ Job = "build"; Block = $build; Name = "Full verification preflight"; Keys = @("shell", "run") },
        @{ Job = "build"; Block = $build; Name = "Build & Package"; Keys = @("id", "uses", "with") },
        @{ Job = "build"; Block = $build; Name = "Validate package evidence"; Keys = @("id", "shell", "env", "run") },
        @{ Job = "build"; Block = $build; Name = "Upload unverified stable artifact"; Keys = @("uses", "with") },
        @{ Job = "verify"; Block = $verify; Name = "Download unverified stable artifact"; Keys = @("uses", "with") },
        @{ Job = "verify"; Block = $verify; Name = "Validate stable package and evidence"; Keys = @("id", "shell", "env", "run") },
        @{ Job = "verify"; Block = $verify; Name = "Upload verified stable artifact"; Keys = @("uses", "with") },
        @{ Job = "publish"; Block = $publish; Name = "Download verified stable artifact"; Keys = @("uses", "with") },
        @{ Job = "publish"; Block = $publish; Name = "Validate verified artifact for publication"; Keys = @("id", "shell", "env", "run") },
        @{ Job = "publish"; Block = $publish; Name = "Create immutable tag and draft release"; Keys = @("shell", "env", "run") }
    )) {
        Assert-StepKeys $stepContract.Block $stepContract.Name $stepContract.Job $stepContract.Keys $Source
    }

    $checkoutAction = 'actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1'
    $githubScriptAction = 'actions/github-script@f28e40c7f34bde8b3046d885e986cb6290c5673b # v7.1.0'
    $setupDotnetAction = 'actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0'
    $packageAction = './.github/actions/build-package'
    $uploadArtifactAction = 'actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1'
    $downloadArtifactAction = 'actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1'

    Assert-ExactUsesMultiset `
        -Text $normalized `
        -ExpectedUses @(
            $checkoutAction, $checkoutAction, $githubScriptAction,
            $setupDotnetAction, $packageAction,
            $uploadArtifactAction, $uploadArtifactAction,
            $downloadArtifactAction, $downloadArtifactAction) `
        -Description "workflow exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $prepareText `
        -ExpectedUses @($checkoutAction, $githubScriptAction) `
        -Description "prepare-job exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $buildText `
        -ExpectedUses @($checkoutAction, $setupDotnetAction, $packageAction, $uploadArtifactAction) `
        -Description "build-job exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $verifyText `
        -ExpectedUses @($downloadArtifactAction, $uploadArtifactAction) `
        -Description "verify-job exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $publishText `
        -ExpectedUses @($downloadArtifactAction) `
        -Description "publish-job exact uses multiset" `
        -Source $Source

    Require-Fragment $prepareText ("      - name: Checkout trusted control source`n        uses: " + $checkoutAction) "trusted-control checkout action routing" $Source
    Require-Fragment $prepareText ("      - name: Verify selected commit and release absence`n        uses: " + $githubScriptAction) "release-absence action routing" $Source
    Require-Fragment $buildText ("      - name: Checkout immutable release commit`n        uses: " + $checkoutAction) "immutable build checkout action routing" $Source
    Require-Fragment $buildText ("      - name: Setup .NET`n        uses: " + $setupDotnetAction) ".NET setup action routing" $Source
    Require-Fragment $buildText ("      - name: Build & Package`n        id: package`n        uses: " + $packageAction) "package action routing" $Source
    Require-Fragment $buildText ("      - name: Upload unverified stable artifact`n        uses: " + $uploadArtifactAction) "unverified artifact upload routing" $Source
    Require-Fragment $verifyText ("      - name: Download unverified stable artifact`n        uses: " + $downloadArtifactAction) "unverified artifact download routing" $Source
    Require-Fragment $verifyText ("      - name: Upload verified stable artifact`n        uses: " + $uploadArtifactAction) "verified artifact upload routing" $Source
    Require-Fragment $publishText ("      - name: Download verified stable artifact`n        uses: " + $downloadArtifactAction) "verified artifact download routing" $Source

    Forbid-Pattern $prepareText 'uses: \./\.github/' "local action in trusted preparation" $Source
    Forbid-Pattern $verifyText 'uses: \./\.github/' "local action in trusted verification" $Source
    Forbid-Pattern $publishText 'uses: \./\.github/' "local action in privileged publication" $Source
    Forbid-Pattern $verifyText 'actions/checkout@' "checkout in trusted verification" $Source
    Forbid-Pattern $publishText 'actions/checkout@' "checkout in privileged publication" $Source
    Require-Pattern $normalized '(?m)^          persist-credentials: false$' "credential-free checkout settings" $Source 2

    foreach ($fragment in @(
        '          WORKFLOW_REF: ${{ github.ref }}',
        '          DEFAULT_BRANCH: ${{ github.event.repository.default_branch }}',
        '$expectedRef = "refs/heads/$env:DEFAULT_BRANCH"',
        'throw "Stable Release must be dispatched from the default branch."',
        '          ref: ${{ github.sha }}',
        '          REQUESTED_COMMIT_SHA: ${{ inputs.commit_sha }}',
        '          REQUESTED_RELEASE_TAG: ${{ inputs.release_tag }}',
        '          WORKFLOW_COMMIT_SHA: ${{ github.sha }}',
        "if (`$commitSha -cnotmatch '^[0-9a-f]{40}$')",
        "if (`$workflowCommitSha -cnotmatch '^[0-9a-f]{40}$' -or",
        "              -not [string]::Equals(`n                `$commitSha,`n                `$workflowCommitSha,`n                [System.StringComparison]::Ordinal)) {",
        'Stable Release commit SHA must equal the trusted default-branch workflow commit.',
        '$rawCommitSha.Trim()',
        'Stable Release commit SHA must not contain leading or trailing whitespace.',
        '$rawReleaseTag.Trim()',
        'Stable Release tag must not contain leading or trailing whitespace.',
        "`$releaseTag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'",
        'archive_name=LLPlayer-$releaseTag-x64.7z',
        'github.rest.repos.getCommit',
        'github.rest.git.getRef',
        'github.rest.repos.getReleaseByTag'
    )) {
        Require-Fragment $prepareText $fragment "trusted prepare invariant '$fragment'" $Source
    }

    Assert-OrderedFragments $buildText @(
        '      - name: Checkout immutable release commit',
        '      - name: Verify immutable checkout',
        '      - name: Verify release version',
        '      - name: Setup .NET',
        '      - name: Full verification preflight',
        '      - name: Build & Package',
        '      - name: Validate package evidence',
        '      - name: Upload unverified stable artifact'
    ) "build admission order" $Source
    foreach ($fragment in @(
        '          ref: ${{ needs.prepare.outputs.commit_sha }}',
        '$xmlSettings = [System.Xml.XmlReaderSettings]::new()',
        '$xmlSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit',
        '$xmlSettings.XmlResolver = $null',
        '$projectDocument = [System.Xml.XmlDocument]::new()',
        '$projectDocument.XmlResolver = $null',
        '$projectDocument.Load($xmlReader)',
        '$versionNodes = @($projectDocument.SelectNodes(',
        '$versionNodes.Count -ne 1',
        "`$rawVersion -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'",
        '$expectedTag = "v$rawVersion"',
        'Stable tag must equal v plus the exact LLPlayer project Version.',
        '        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1',
        '          dotnet-version: 10.0.x',
        '        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1',
        "      - name: Build & Package`n        id: package`n",
        '          archive-name: ${{ needs.prepare.outputs.archive_name }}',
        "`$ytDlpSha256 -cnotmatch '^[0-9a-f]{64}$'",
        "`$archiveSha256 -cnotmatch '^[0-9a-f]{64}$'",
        "`$ytDlpSize -cnotmatch '^[1-9][0-9]*$'",
        "`$archiveSize -cnotmatch '^[1-9][0-9]*$'",
        '          name: llplayer-stable-release-unverified',
        '          overwrite: false',
        '          if-no-files-found: error'
    )) {
        Require-Fragment $buildText $fragment "read-only build invariant '$fragment'" $Source
    }

    foreach ($fragment in @(
        '          name: llplayer-stable-release-unverified',
        '          digest-mismatch: error',
        '        id: verified-evidence',
        '$entries = @(Get-ChildItem -LiteralPath $root -Force)',
        '$entries.Count -ne 1',
        '($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)',
        '$file.Length -le 0',
        'Downloaded archive name does not match the trusted release metadata.',
        'Downloaded archive escaped the fixed artifact directory.',
        '& "$sevenZip" t "$expectedPath"',
        '7-Zip archive integrity test failed',
        '& "$sevenZip" e "$expectedPath" "Plugins\YoutubeDL\yt-dlp.exe"',
        'yt-dlp evidence does not match the tested archive.',
        '          name: llplayer-stable-release-verified',
        '          overwrite: false'
    )) {
        Require-Fragment $verifyText $fragment "trusted verification invariant '$fragment'" $Source
    }
    Require-Pattern $verifyText '(?m)^          digest-mismatch: error$' "unverified artifact digest enforcement" $Source 1
    foreach ($selectorKey in @("github-token", "run-id", "artifact-ids", "pattern", "merge-multiple")) {
        Forbid-Pattern $verifyText "(?m)^          $selectorKey`:" "cross-run or dynamic unverified artifact selector '$selectorKey'" $Source
    }

    foreach ($fragment in @(
        '          name: llplayer-stable-release-verified',
        '          digest-mismatch: error',
        '        id: release-asset',
        '$entries = @(Get-ChildItem -LiteralPath $root -Force)',
        '$entries.Count -ne 1',
        '($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)',
        '$file.Length -le 0',
        'Verified archive hash or size changed before publication.',
        '          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}',
        '$tagCheck.Status -eq 200',
        '$tagCheck.Status -ne 404',
        '$releaseCheck.Status -eq 200',
        '$releaseCheck.Status -ne 404',
        '            ref = "refs/tags/$releaseTag"',
        '            sha = $commitSha',
        '$tagCreate.Status -ne 201',
        'draft = $true',
        'prerelease = $false',
        '$releaseCreate.Status -ne 201',
        'application/x-7z-compressed',
        '$assetUpload.Status -ne 201',
        '-not [string]::Equals($assetUpload.Body.digest, "sha256:$archiveSha256", [System.StringComparison]::Ordinal)',
        'Uploaded Stable asset digest does not match the verified archive.',
        '$expectedRemoteDigest = "sha256:$archiveSha256"',
        'for ($attempt = 1; $attempt -le 6; $attempt++)',
        '$releaseReadback = Invoke-GitHubRequest -Uri "$repoUri/releases/$($releaseCreate.Body.id)" -Method Get',
        'Draft release readback failed.',
        '$releaseAssets.Count -gt 1',
        '$releaseAssets.Count -eq 1',
        '-not [string]::Equals($releaseAssets[0].digest, $expectedRemoteDigest, [System.StringComparison]::Ordinal)',
        'Draft release asset digest does not match the verified archive.',
        'Tag readback no longer matches the approved commit.',
        'Start-Sleep -Seconds 5',
        '$null -eq $verifiedRelease',
        'Draft release never exposed the required SHA-256 asset digest.'
    )) {
        Require-Fragment $publishText $fragment "privileged publication invariant '$fragment'" $Source
    }
    Require-Pattern $publishText '(?m)^          digest-mismatch: error$' "verified artifact digest enforcement" $Source 1
    foreach ($selectorKey in @("github-token", "run-id", "artifact-ids", "pattern", "merge-multiple")) {
        Forbid-Pattern $publishText "(?m)^          $selectorKey`:" "cross-run or dynamic verified artifact selector '$selectorKey'" $Source
    }

    foreach ($forbidden in @(
        @{ Pattern = 'softprops/action-gh-release'; Description = "mutable release helper" },
        @{ Pattern = 'actions/checkout@v'; Description = "mutable checkout action" },
        @{ Pattern = '(?i)continue-on-error\s*:\s*true'; Description = "continue-on-error bypass" },
        @{ Pattern = '(?i)runs-on:\s*self-hosted'; Description = "self-hosted release runner" },
        @{ Pattern = '(?i)(--force|--clobber)'; Description = "force or clobber mutation" },
        @{ Pattern = '(?i)(method\s*=\s*["'']?(patch|delete)|-Method\s+(Patch|Delete))'; Description = "tag/release update or delete method" },
        @{ Pattern = '(?i)draft\s*=\s*\$false'; Description = "published release creation" },
        @{ Pattern = '(?i)prerelease\s*=\s*\$true'; Description = "prerelease mutation" }
    )) {
        Forbid-Pattern $normalized $forbidden.Pattern $forbidden.Description $Source
    }
    foreach ($forbidden in @(
        @{ Pattern = '(?i)expand-archive'; Description = "archive extraction in write job" },
        @{ Pattern = '(?i)7z(?:\.exe)?["'']?\s+(?:e|x|t)\b'; Description = "archive parsing in write job" },
        @{ Pattern = '(?i)invoke-expression'; Description = "dynamic code execution in write job" },
        @{ Pattern = '(?i)start-process'; Description = "process execution in write job" },
        @{ Pattern = '&\s*["'']?\$env:ARCHIVE_PATH'; Description = "artifact execution in write job" }
    )) {
        Forbid-Pattern $publishText $forbidden.Pattern $forbidden.Description $Source
    }
}

function Replace-ExactlyOnce(
    [string]$Text,
    [string]$OldValue,
    [string]$NewValue,
    [string]$Description
) {
    $index = $Text.IndexOf($OldValue, [System.StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "Stable adversarial fixture could not find $Description."
    }
    $secondIndex = $Text.IndexOf(
        $OldValue,
        $index + $OldValue.Length,
        [System.StringComparison]::Ordinal)
    if ($secondIndex -ge 0) {
        throw "Stable adversarial fixture expected exactly one $Description."
    }
    return $Text.Substring(0, $index) + $NewValue + $Text.Substring($index + $OldValue.Length)
}

function Assert-SemanticsRejected(
    [string]$Baseline,
    [string]$Text,
    [string]$Description,
    [string]$ExpectedErrorFragment
) {
    if ([string]::Equals($Text, $Baseline, [System.StringComparison]::Ordinal)) {
        throw "Stable semantic fixture did not mutate $Description."
    }
    try {
        # Keep the source label independent from the expected invariant so an
        # unrelated source-label echo cannot satisfy the reason assertion.
        Assert-StableReleaseBoundary $Text "adversarial semantic fixture"
    }
    catch {
        $message = $_.Exception.Message
        if ($message.IndexOf($ExpectedErrorFragment, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Stable semantic fixture '$Description' failed for the wrong reason: $message"
        }
        return
    }
    throw "Stable Release boundary validator accepted adversarial fixture: $Description."
}

function Assert-CanonicalSyntaxRejected(
    [string]$Text,
    [string]$Description,
    [string]$ExpectedErrorFragment
) {
    try {
        Assert-CanonicalActionSyntax $Text "adversarial canonical fixture"
    }
    catch {
        $message = $_.Exception.Message
        if ($message.IndexOf($ExpectedErrorFragment, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Stable canonical fixture '$Description' failed for the wrong reason: $message"
        }
        return
    }
    throw "Stable canonical action validator accepted adversarial fixture: $Description."
}

function Assert-UsesMultisetRejected(
    [string]$Text,
    [string[]]$ExpectedUses,
    [string]$Description,
    [string]$ExpectedErrorFragment
) {
    try {
        Assert-ExactUsesMultiset `
            -Text $Text `
            -ExpectedUses $ExpectedUses `
            -Description "isolated canonical uses multiset" `
            -Source "adversarial uses fixture"
    }
    catch {
        $message = $_.Exception.Message
        if ($message.IndexOf($ExpectedErrorFragment, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Stable uses fixture '$Description' failed for the wrong reason: $message"
        }
        return
    }
    throw "Stable exact uses validator accepted adversarial fixture: $Description."
}

function Assert-MutationRejected(
    [string]$Baseline,
    [string]$Old,
    [string]$New,
    [string]$Description,
    [string]$ExpectedReason = $null
) {
    $mutated = $Baseline.Replace($Old, $New)
    if ([string]::Equals($mutated, $Baseline, [System.StringComparison]::Ordinal)) {
        throw "Stable validator fixture did not mutate $Description."
    }
    $rejected = $false
    try {
        Assert-StableReleaseBoundary $mutated "adversarial mutation fixture"
    }
    catch {
        if ($ExpectedReason -and
            $_.Exception.Message.IndexOf($ExpectedReason, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Stable validator fixture rejected $Description for the wrong reason: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Stable Release boundary validator accepted adversarial fixture: $Description."
    }
}

function Assert-CanonicalOnlyMutationRouting([string]$Baseline) {
    $mutated = $Baseline.Replace(
        "name: Stable Release",
        "name: Stable Release`n# canonical-lock routing probe")
    if ([string]::Equals($mutated, $Baseline, [System.StringComparison]::Ordinal)) {
        throw "Stable canonical-lock routing fixture did not mutate the workflow."
    }

    Assert-StableReleaseBoundary $mutated "canonical-lock routing fixture"
    $hashRejected = $false
    try {
        Assert-CanonicalWorkflowHash $mutated "canonical-lock routing fixture"
    }
    catch {
        $hashRejected = $true
    }
    if (-not $hashRejected) {
        throw "Stable canonical hash accepted a comment-only workflow mutation."
    }
}

if (-not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) {
    throw "Stable Release workflow is missing: $workflowPath"
}

$workflowText = Get-Content -LiteralPath $workflowPath -Raw
Assert-CanonicalWorkflowHash $workflowText "stable-release.yml"
Assert-StableReleaseBoundary $workflowText "stable-release.yml"
Assert-CanonicalOnlyMutationRouting $workflowText

$jobBlockScalarFixture = "jobs:`n  build:`n    name: |`n      Hidden job"
Assert-CanonicalSyntaxRejected `
    $jobBlockScalarFixture `
    "a job block scalar" `
    "workflow or job block scalar"

$jobDoubleQuotedScalarFixture = "jobs:`n  build:`n    name: `"Hidden`n      job`""
Assert-CanonicalSyntaxRejected `
    $jobDoubleQuotedScalarFixture `
    "a multiline double-quoted job scalar" `
    "multiline double-quoted workflow, job, or step scalar"

$jobSingleQuotedScalarFixture = "jobs:`n  build:`n    name: 'Hidden`n      job'"
Assert-CanonicalSyntaxRejected `
    $jobSingleQuotedScalarFixture `
    "a multiline single-quoted job scalar" `
    "multiline single-quoted workflow, job, or step scalar"

$stepNameBlockScalarFixture = "steps:`n  - name: |`n      Hidden action`n    run: echo ok"
Assert-CanonicalSyntaxRejected `
    $stepNameBlockScalarFixture `
    "a step-name block scalar" `
    "step-name block scalar"

$stepNameDoubleQuotedScalarFixture = "steps:`n  - name: `"Hidden`n      action`"`n    run: echo ok"
Assert-CanonicalSyntaxRejected `
    $stepNameDoubleQuotedScalarFixture `
    "a multiline double-quoted step-name scalar" `
    "multiline double-quoted step-name scalar"

$stepNameSingleQuotedScalarFixture = "steps:`n  - name: 'Hidden`n      action'`n    run: echo ok"
Assert-CanonicalSyntaxRejected `
    $stepNameSingleQuotedScalarFixture `
    "a multiline single-quoted step-name scalar" `
    "multiline single-quoted step-name scalar"

$plainScalarUsesFixture = "        uses:actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0"
Assert-UsesMultisetRejected `
    $plainScalarUsesFixture `
    @("actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0") `
    "a no-space plain scalar that resembles an action key" `
    "expected 1 uses entries, found 0"

$arbitraryDepthBareFixture = "    steps:`n                              -`n                                      name: Arbitrary-depth pinned action`n                                      `"u\u0073es`": actions/cache@0123456789abcdef0123456789abcdef01234567"
Assert-CanonicalSyntaxRejected `
    $arbitraryDepthBareFixture `
    "an arbitrary-depth bare-step action" `
    "bare step declaration with arbitrarily deep child mappings"

$flowStyleStepFixture = "steps:`n  - { name: Flow action, uses: actions/cache@0123456789abcdef0123456789abcdef01234567 }"
Assert-CanonicalSyntaxRejected `
    $flowStyleStepFixture `
    "a flow-style action step" `
    "flow, explicit, anchored, aliased, or tagged step declaration"

$anchoredStepFixture = "steps:`n  - &cache_step { name: Anchored action, uses: actions/cache@0123456789abcdef0123456789abcdef01234567 }"
Assert-CanonicalSyntaxRejected `
    $anchoredStepFixture `
    "an anchored action step" `
    "flow, explicit, anchored, aliased, or tagged step declaration"

$anchoredKeyFixture = "steps:`n  - name: Anchored-key action`n    &hidden uses: actions/cache@0123456789abcdef0123456789abcdef01234567"
Assert-CanonicalSyntaxRejected `
    $anchoredKeyFixture `
    "an anchored action mapping key" `
    "anchored or aliased workflow key"

$aliasedKeyFixture = "steps:`n  - name: &hidden uses`n    run: echo seed`n  - name: Aliased-key action`n    *hidden : actions/cache@0123456789abcdef0123456789abcdef01234567"
Assert-CanonicalSyntaxRejected `
    $aliasedKeyFixture `
    "an aliased action mapping key" `
    "anchored or aliased workflow key"

$mergedKeyFixture = "seed:`n  - value: &hidden { uses: actions/cache@0123456789abcdef0123456789abcdef01234567 }`nsteps:`n  - name: Merged action`n    <<: *hidden"
Assert-CanonicalSyntaxRejected `
    $mergedKeyFixture `
    "a merged action mapping key" `
    "explicit or merged workflow key"

Assert-MutationRejected $workflowText "  workflow_dispatch:" "  push:" "an automatic tag trigger"
Assert-MutationRejected $workflowText "  workflow_dispatch:" "  workflow_run:" "an unapproved workflow_run trigger"
Assert-MutationRejected $workflowText "permissions: {}`n`nconcurrency:" "permissions: {}`npermissions: {}`n`nconcurrency:" "duplicate workflow permissions"
Assert-MutationRejected $workflowText "permissions: {}`n`nconcurrency:" ('"permissions": {}' + "`n`nconcurrency:") "a quoted protected root key"
Assert-MutationRejected $workflowText "permissions: {}`n`nconcurrency:" "permissions: {}`ndefaults:`n  run:`n    shell: cmd`n`nconcurrency:" "custom workflow command defaults"
Assert-MutationRejected $workflowText "  group: stable-release" '  group: stable-release-${{ inputs.release_tag }}' "dynamic Stable concurrency"
Assert-MutationRejected $workflowText "  cancel-in-progress: false" "  cancel-in-progress: true" "cancelling an in-progress Stable mutation"
Assert-MutationRejected $workflowText "      contents: read" "      contents: write" "write permission in a selected-code job"
Assert-MutationRejected $workflowText "    needs: [prepare, verify]" "    needs: [prepare, build]" "publication bypassing trusted verification"
Assert-MutationRejected $workflowText "    runs-on: windows-latest" "    runs-on: self-hosted" "a self-hosted release runner"
Assert-MutationRejected $workflowText "@93cb6efe18208431cddfb8368fd83d5badbf9bfd" "@v5" "a mutable checkout action"
Assert-MutationRejected $workflowText "actions/github-script@f28e40c7f34bde8b3046d885e986cb6290c5673b" "example/unapproved@0123456789abcdef0123456789abcdef01234567" "an arbitrary pinned action"

$checkoutActionReference = "actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1"
$setupDotnetActionReference = "actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0"
$downloadArtifactActionReference = "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1"

$setupStep = "      - name: Setup .NET`n        uses: $setupDotnetActionReference"
$publishDownloadStep = "      - name: Download verified stable artifact`n        uses: $downloadArtifactActionReference"
$crossJobActionSwapFixture = Replace-ExactlyOnce `
    $workflowText `
    $setupStep `
    "      - name: Setup .NET`n        uses: __STABLE_CROSS_JOB_ACTION__" `
    "build setup action staging"
$crossJobActionSwapFixture = Replace-ExactlyOnce `
    $crossJobActionSwapFixture `
    $publishDownloadStep `
    "      - name: Download verified stable artifact`n        uses: $setupDotnetActionReference" `
    "publish download action swap"
$crossJobActionSwapFixture = Replace-ExactlyOnce `
    $crossJobActionSwapFixture `
    "uses: __STABLE_CROSS_JOB_ACTION__" `
    "uses: $downloadArtifactActionReference" `
    "build setup action completion"
Assert-SemanticsRejected `
    $workflowText `
    $crossJobActionSwapFixture `
    "expected actions moved between build and publish while preserving the global multiset" `
    "build-job exact uses multiset"

$buildCheckoutStep = "      - name: Checkout immutable release commit`n        uses: $checkoutActionReference"
$buildActionRouteSwapFixture = Replace-ExactlyOnce `
    $workflowText `
    $buildCheckoutStep `
    "      - name: Checkout immutable release commit`n        uses: __STABLE_BUILD_ROUTE_ACTION__" `
    "build checkout action staging"
$buildActionRouteSwapFixture = Replace-ExactlyOnce `
    $buildActionRouteSwapFixture `
    $setupStep `
    "      - name: Setup .NET`n        uses: $checkoutActionReference" `
    "build setup action route swap"
$buildActionRouteSwapFixture = Replace-ExactlyOnce `
    $buildActionRouteSwapFixture `
    "uses: __STABLE_BUILD_ROUTE_ACTION__" `
    "uses: $setupDotnetActionReference" `
    "build checkout action route completion"
Assert-SemanticsRejected `
    $workflowText `
    $buildActionRouteSwapFixture `
    "expected build actions swapped between compatible named steps" `
    "immutable build checkout action routing"

Assert-MutationRejected $workflowText "          persist-credentials: false" "          persist-credentials: true" "persisted checkout credentials"
Assert-MutationRejected $workflowText "        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1" "        run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1" "a fast-only release preflight"
Assert-MutationRejected $workflowText "          digest-mismatch: error" "          digest-mismatch: warn" "non-failing artifact digest validation"
$verifiedPublishSelector = "      - name: Download verified stable artifact`n        uses: $downloadArtifactActionReference`n        with:`n          name: llplayer-stable-release-verified"
$verifiedPublishDownloadBlock = $verifiedPublishSelector + "`n" + '          path: ${{ runner.temp }}\llplayer-stable-release-verified' + "`n          digest-mismatch: error"
$crossRunPublishSelectorFixture = Replace-ExactlyOnce `
    $workflowText `
    $verifiedPublishDownloadBlock `
    ($verifiedPublishDownloadBlock + "`n" + '          github-token: ${{ secrets.GITHUB_TOKEN }}' + "`n          run-id: 1") `
    "verified publish artifact download block"
Assert-SemanticsRejected `
    $workflowText `
    $crossRunPublishSelectorFixture `
    "cross-run artifact selection in publish" `
    "cross-run or dynamic verified artifact selector 'github-token'"

$unverifiedPublishSelectorFixture = Replace-ExactlyOnce `
    $workflowText `
    $verifiedPublishSelector `
    "      - name: Download verified stable artifact`n        uses: $downloadArtifactActionReference`n        with:`n          name: llplayer-stable-release-unverified" `
    "verified publish artifact selector"
Assert-SemanticsRejected `
    $workflowText `
    $unverifiedPublishSelectorFixture `
    "write job consuming an unverified artifact" `
    "privileged publication invariant '          name: llplayer-stable-release-verified'"

$dynamicPublishSelectorFixture = Replace-ExactlyOnce `
    $workflowText `
    $verifiedPublishSelector `
    ("      - name: Download verified stable artifact`n        uses: $downloadArtifactActionReference`n        with:`n" + '          name: ${{ needs.prepare.outputs.archive_name }}') `
    "verified publish artifact selector"
Assert-SemanticsRejected `
    $workflowText `
    $dynamicPublishSelectorFixture `
    "a dynamic verified artifact selector" `
    "privileged publication invariant '          name: llplayer-stable-release-verified'"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download verified stable artifact" `
    "    steps:`n      -`n          name: Unexpected deep-indented pinned action`n          `"u\u0073es`": actions/cache@0123456789abcdef0123456789abcdef01234567`n`n      - name: Download verified stable artifact" `
    "a bare publish-job step with a deep-indented escaped uses key" `
    "bare step declaration with arbitrarily deep child mappings"
Assert-MutationRejected $workflowText `
    "      - name: Download verified stable artifact`n        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" `
    "      - name: Download verified stable artifact`n        &hidden uses: actions/cache@0123456789abcdef0123456789abcdef01234567" `
    "a publish-job action encoded with an anchored mapping key" `
    "anchored or aliased workflow key"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download verified stable artifact`n        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" `
    "    steps:`n      - name: &hidden uses`n        run: echo seed`n`n      - name: Download verified stable artifact`n        *hidden : actions/cache@0123456789abcdef0123456789abcdef01234567" `
    "a publish-job action encoded with a defined aliased mapping key" `
    "anchored or aliased workflow key"
Assert-MutationRejected $workflowText '& "$sevenZip" t "$expectedPath"' '# archive test removed' "removal of trusted archive testing"
Assert-MutationRejected $workflowText "-Method Post -Body `$tagBody" "-Method Patch -Body `$tagBody" "tag mutation by PATCH"
Assert-MutationRejected $workflowText 'draft = $true' 'draft = $false' "publication instead of a draft"
Assert-MutationRejected $workflowText `
    '$releaseReadback = Invoke-GitHubRequest -Uri "$repoUri/releases/$($releaseCreate.Body.id)" -Method Get' `
    '$releaseReadback = Invoke-GitHubRequest -Uri "$repoUri/releases/tags/$encodedTag" -Method Get' `
    "post-create Stable readback through a draft-blind tag endpoint" `
    "privileged publication invariant"
Assert-MutationRejected $workflowText 'if ($null -eq $verifiedRelease)' 'if ($false)' "acceptance without a remote asset digest"
Assert-MutationRejected $workflowText "    runs-on: windows-latest`n    permissions:" "    runs-on: windows-latest`n    if: `${{ always() }}`n    permissions:" "an always-run success bypass"
Assert-MutationRejected $workflowText "if (`$commitSha -cnotmatch '^[0-9a-f]{40}$')" "if (`$commitSha -cnotmatch '^[0-9a-f]{7,40}$')" "a non-exact commit id"
Assert-MutationRejected $workflowText "if (`$workflowCommitSha -cnotmatch '^[0-9a-f]{40}$' -or" "if (`$false -and" "a selected commit not bound to the trusted workflow commit"
Assert-MutationRejected $workflowText `
    "              -not [string]::Equals(`n                `$commitSha,`n                `$workflowCommitSha,`n                [System.StringComparison]::Ordinal)) {" `
    "              -not [string]::Equals(`n                `$commitSha,`n                `$commitSha,`n                [System.StringComparison]::Ordinal)) {" `
    "a trusted workflow commit equality operand replaced by the selected commit"
Assert-MutationRejected $workflowText 'if (-not [string]::Equals($rawCommitSha, $rawCommitSha.Trim(), [System.StringComparison]::Ordinal))' 'if ($false)' "silent commit input trimming"
Assert-MutationRejected $workflowText 'if (-not [string]::Equals($rawReleaseTag, $rawReleaseTag.Trim(), [System.StringComparison]::Ordinal))' 'if ($false)' "silent tag input trimming"
Assert-MutationRejected $workflowText '$expectedTag = "v$rawVersion"' '$expectedTag = "$env:APPROVED_RELEASE_TAG"' "release tag not bound to project Version"
Assert-MutationRejected $workflowText "  publish:`n    needs: [prepare, verify]" "  bypass:`n    runs-on: windows-latest`n    steps:`n      - name: Bypass`n        run: echo bypass`n`n  publish:`n    needs: [prepare, verify]" "an extra release job"
Assert-MutationRejected $workflowText "      - name: Create immutable tag and draft release" "      - name: Execute artifact`n        run: '& `$env:ARCHIVE_PATH'`n`n      - name: Create immutable tag and draft release" "artifact execution in the write job"

Write-Host "Stable Release trusted-control-plane verification completed."

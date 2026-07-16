$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$workflowPath = Join-Path $repoRoot ".github\workflows\testing-release.yml"

function Normalize-Text([string]$Text) {
    return (($Text -replace "`r`n", "`n") -replace "`r", "`n").TrimEnd("`n")
}

function Get-UniqueLineIndex(
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

function Get-MappingKey([string]$Line, [int]$Indent) {
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
        throw "Protected release workflow structure must use canonical mapping syntax; found '$trimmed'."
    }

    $key = $trimmed.Substring(0, $colonIndex).Trim()
    if ($key -cnotmatch '^[A-Za-z0-9_-]+$') {
        throw "Protected release workflow keys must use canonical unquoted syntax; found '$key'."
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
    $indices = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $lineKey = Get-MappingKey $Lines[$index] $Indent
        if ([string]::Equals($lineKey, $Key, [System.StringComparison]::Ordinal)) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source must contain exactly one $Description; found $($indices.Count)."
    }

    $trimmed = $Lines[$indices[0]].Trim()
    $remainder = $trimmed.Substring($trimmed.IndexOf(':') + 1).Trim()
    if ($remainder -and -not $remainder.StartsWith("#", [System.StringComparison]::Ordinal)) {
        throw "$Source $Description must use a canonical block mapping."
    }
    return [int]$indices[0]
}

function Get-BlockLines(
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

function Assert-AllowedMappingKeys(
    [string[]]$Lines,
    [int]$Indent,
    [string[]]$AllowedKeys,
    [string]$Description,
    [string]$Source
) {
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
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
        if (-not $seen.Add($key)) {
            throw "$Source $Description contains duplicate key '$key'."
        }
    }
}

function Require-ExactLine(
    [string[]]$Lines,
    [string]$Expected,
    [string]$Description,
    [string]$Source
) {
    $count = @($Lines | Where-Object {
        [string]::Equals($_, $Expected, [System.StringComparison]::Ordinal)
    }).Count
    if ($count -ne 1) {
        throw "$Source $Description; expected exact line '$Expected', found $count."
    }
}

function Assert-ExactMappingBlock(
    [string[]]$Lines,
    [int]$ParentIndent,
    [string]$Key,
    [string[]]$ExpectedLines,
    [string]$Description,
    [string]$Source
) {
    $block = Get-BlockLines $Lines $ParentIndent $Key $Description $Source
    $childIndent = $ParentIndent + 2
    $allowedKeys = New-Object System.Collections.Generic.List[string]
    foreach ($expectedLine in $ExpectedLines) {
        $expectedKey = Get-MappingKey $expectedLine $childIndent
        if ($null -eq $expectedKey) {
            throw "Validator fixture error: '$expectedLine' is not a canonical mapping line."
        }
        $allowedKeys.Add($expectedKey)
        Require-ExactLine $block $expectedLine "$Description must keep '$expectedKey'" $Source
    }
    Assert-AllowedMappingKeys $block $childIndent $allowedKeys.ToArray() $Description $Source
    return $block
}

function Get-NamedStep(
    [string[]]$StepLines,
    [string]$Name,
    [string]$JobName,
    [string]$Source
) {
    $marker = "      - name: $Name"
    $indices = @()
    for ($index = 0; $index -lt $StepLines.Count; $index++) {
        if ([string]::Equals($StepLines[$index], $marker, [System.StringComparison]::Ordinal)) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source jobs.$JobName must contain exactly one '$Name' step; found $($indices.Count)."
    }

    $start = [int]$indices[0]
    $end = $StepLines.Count
    for ($index = $start + 1; $index -lt $StepLines.Count; $index++) {
        if ($StepLines[$index] -cmatch '^      - ') {
            $end = $index
            break
        }
    }
    return @($StepLines[$start..($end - 1)])
}

function Assert-StepOrder(
    [string[]]$StepLines,
    [string[]]$ExpectedNames,
    [string]$JobName,
    [string]$Source
) {
    $actualNames = New-Object System.Collections.Generic.List[string]
    foreach ($line in $StepLines) {
        $indent = $line.Length - $line.TrimStart().Length
        if ($indent -ne 6 -or -not $line.TrimStart().StartsWith("- ", [System.StringComparison]::Ordinal)) {
            continue
        }
        if ($line -cnotmatch '^      - name:\s*(.+?)\s*$') {
            throw "$Source jobs.$JobName.steps contains an anonymous or non-canonical step: '$($line.Trim())'."
        }
        $actualNames.Add($Matches[1])
    }

    if ($actualNames.Count -ne $ExpectedNames.Count) {
        throw "$Source jobs.$JobName.steps must contain exactly $($ExpectedNames.Count) named steps; found $($actualNames.Count)."
    }
    for ($index = 0; $index -lt $ExpectedNames.Count; $index++) {
        if (-not [string]::Equals(
            $actualNames[$index],
            $ExpectedNames[$index],
            [System.StringComparison]::Ordinal)) {
            throw "$Source jobs.$JobName.steps has unexpected order at position $($index + 1): '$($actualNames[$index])'."
        }
    }
}

function Assert-ExactBlockScalar(
    [string[]]$Lines,
    [int]$KeyIndent,
    [string]$Key,
    [string]$ExpectedBody,
    [string]$Description,
    [string]$Source
) {
    $indices = @()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $lineKey = Get-MappingKey $Lines[$index] $KeyIndent
        if ([string]::Equals($lineKey, $Key, [System.StringComparison]::Ordinal)) {
            $indices += $index
        }
    }
    if ($indices.Count -ne 1) {
        throw "$Source must contain exactly one $Description block scalar; found $($indices.Count)."
    }

    $start = [int]$indices[0]
    if (-not [string]::Equals($Lines[$start].Trim(), "$Key`: |", [System.StringComparison]::Ordinal)) {
        throw "$Source $Description must use the canonical '$Key`: |' form."
    }

    $end = $Lines.Count
    for ($index = $start + 1; $index -lt $Lines.Count; $index++) {
        $nextKey = Get-MappingKey $Lines[$index] $KeyIndent
        if ($null -ne $nextKey) {
            $end = $index
            break
        }
    }
    if ($end -le $start + 1) {
        throw "$Source $Description must not be empty."
    }

    $contentIndent = $KeyIndent + 2
    $bodyLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines[($start + 1)..($end - 1)]) {
        if (-not $line.Trim()) {
            $bodyLines.Add("")
            continue
        }
        $indent = $line.Length - $line.TrimStart().Length
        if ($indent -lt $contentIndent) {
            throw "$Source $Description contains a line outside its block scalar."
        }
        $bodyLines.Add($line.Substring($contentIndent))
    }

    $actualBody = Normalize-Text ($bodyLines -join "`n")
    $normalizedExpected = Normalize-Text $ExpectedBody
    if (-not [string]::Equals($actualBody, $normalizedExpected, [System.StringComparison]::Ordinal)) {
        throw "$Source $Description body drifted from the reviewed trusted implementation."
    }
}

function Assert-ShellStep(
    [string[]]$Step,
    [string[]]$AllowedKeys,
    [string]$Description,
    [string]$Source
) {
    Assert-AllowedMappingKeys $Step 8 $AllowedKeys $Description $Source
    Require-ExactLine $Step "        shell: pwsh" "$Description must use pwsh" $Source
}

function Assert-TestingReleaseContract([string]$Text, [string]$Source) {
    $normalized = Normalize-Text $Text
    $rootLines = @($normalized -split "`n")

    Assert-AllowedMappingKeys $rootLines 0 @("name", "on", "permissions", "jobs") "workflow root" $Source
    Require-ExactLine $rootLines "name: Testing Release" "workflow name must remain fixed" $Source
    Require-ExactLine $rootLines "permissions: {}" "workflow-level permissions must default to none" $Source

    $onLines = Get-BlockLines $rootLines 0 "on" "top-level on entry" $Source
    Assert-AllowedMappingKeys $onLines 2 @("workflow_dispatch") "workflow triggers" $Source
    $dispatchLines = Get-BlockLines $onLines 2 "workflow_dispatch" "workflow_dispatch entry" $Source
    Assert-AllowedMappingKeys $dispatchLines 4 @("inputs") "workflow_dispatch" $Source
    $inputLines = Get-BlockLines $dispatchLines 4 "inputs" "workflow_dispatch inputs" $Source
    Assert-AllowedMappingKeys $inputLines 6 @("commit") "workflow_dispatch inputs" $Source
    $commitLines = Get-BlockLines $inputLines 6 "commit" "commit input" $Source
    Assert-AllowedMappingKeys $commitLines 8 @("description", "required") "commit input" $Source
    Require-ExactLine $commitLines "        description: 'Build Commit Hash or ref'" "commit input description must remain fixed" $Source
    Require-ExactLine $commitLines "        required: true" "commit input must remain required" $Source

    $jobsLines = Get-BlockLines $rootLines 0 "jobs" "top-level jobs entry" $Source
    Assert-AllowedMappingKeys $jobsLines 2 @("prepare", "build", "verify", "upload") "jobs" $Source
    $prepare = Get-BlockLines $jobsLines 2 "prepare" "jobs.prepare entry" $Source
    $build = Get-BlockLines $jobsLines 2 "build" "jobs.build entry" $Source
    $verify = Get-BlockLines $jobsLines 2 "verify" "jobs.verify entry" $Source
    $upload = Get-BlockLines $jobsLines 2 "upload" "jobs.upload entry" $Source

    Assert-AllowedMappingKeys $prepare 4 @("runs-on", "permissions", "outputs", "steps") "jobs.prepare" $Source
    Require-ExactLine $prepare "    runs-on: windows-latest" "jobs.prepare must use a fresh GitHub-hosted Windows runner" $Source
    $null = Assert-ExactMappingBlock $prepare 4 "permissions" @(
        "      contents: read"
    ) "jobs.prepare.permissions" $Source
    $null = Assert-ExactMappingBlock $prepare 4 "outputs" @(
        '      commit_sha: ${{ steps.release-commit.outputs.sha }}',
        '      archive_name: ${{ steps.archive-name.outputs.name }}'
    ) "jobs.prepare.outputs" $Source

    Assert-AllowedMappingKeys $build 4 @("needs", "runs-on", "permissions", "steps") "jobs.build" $Source
    Require-ExactLine $build "    needs: prepare" "jobs.build must depend only on trusted preparation" $Source
    Require-ExactLine $build "    runs-on: windows-latest" "jobs.build must use a fresh GitHub-hosted Windows runner" $Source
    $null = Assert-ExactMappingBlock $build 4 "permissions" @(
        "      contents: read"
    ) "jobs.build.permissions" $Source

    Assert-AllowedMappingKeys $verify 4 @("needs", "runs-on", "permissions", "steps") "jobs.verify" $Source
    Require-ExactLine $verify "    needs: [prepare, build]" "jobs.verify must require successful prepare and build jobs" $Source
    Require-ExactLine $verify "    runs-on: windows-latest" "jobs.verify must use a fresh GitHub-hosted Windows runner" $Source
    $null = Assert-ExactMappingBlock $verify 4 "permissions" @(
        "      contents: read"
    ) "jobs.verify.permissions" $Source

    Assert-AllowedMappingKeys $upload 4 @("needs", "runs-on", "permissions", "steps") "jobs.upload" $Source
    Require-ExactLine $upload "    needs: [prepare, verify]" "jobs.upload must require trusted preparation and verified artifact jobs" $Source
    Require-ExactLine $upload "    runs-on: windows-latest" "jobs.upload must use a fresh GitHub-hosted Windows runner" $Source
    $null = Assert-ExactMappingBlock $upload 4 "permissions" @(
        "      contents: write"
    ) "jobs.upload.permissions" $Source

    $prepareSteps = Get-BlockLines $prepare 4 "steps" "jobs.prepare.steps" $Source
    $buildSteps = Get-BlockLines $build 4 "steps" "jobs.build.steps" $Source
    $verifySteps = Get-BlockLines $verify 4 "steps" "jobs.verify.steps" $Source
    $uploadSteps = Get-BlockLines $upload 4 "steps" "jobs.upload.steps" $Source

    Assert-StepOrder $prepareSteps @(
        "Require trusted workflow ref",
        "Checkout workflow control source",
        "Stage trusted release validator",
        "Validate requested ref",
        "Get latest stable release tag",
        "Validate stable release tag",
        "Checkout selected ref for resolution",
        "Resolve immutable release commit",
        "Set archive name"
    ) "prepare" $Source
    Assert-StepOrder $buildSteps @(
        "Checkout immutable release commit",
        "Verify immutable checkout",
        "Build & Package",
        "Upload testing release artifact"
    ) "build" $Source
    Assert-StepOrder $verifySteps @(
        "Download unverified testing release artifact",
        "Validate unverified testing package",
        "Upload verified testing release artifact"
    ) "verify" $Source
    Assert-StepOrder $uploadSteps @(
        "Download testing release artifact",
        "Validate downloaded testing package",
        "Upload Testing Asset (overwrite)"
    ) "upload" $Source

    $trustedRef = Get-NamedStep $prepareSteps "Require trusted workflow ref" "prepare" $Source
    Assert-ShellStep $trustedRef @("shell", "env", "run") "trusted workflow ref step" $Source
    $null = Assert-ExactMappingBlock $trustedRef 8 "env" @(
        '          WORKFLOW_REF: ${{ github.ref }}',
        '          DEFAULT_BRANCH: ${{ github.event.repository.default_branch }}'
    ) "trusted workflow ref env" $Source
    Assert-ExactBlockScalar $trustedRef 8 "run" @'
$ErrorActionPreference = "Stop"
$expectedRef = "refs/heads/$env:DEFAULT_BRANCH"
if (-not [string]::Equals(
    "$env:WORKFLOW_REF",
    $expectedRef,
    [System.StringComparison]::Ordinal)) {
  throw "Testing Release must be dispatched from the default branch."
}
'@ "trusted workflow ref run" $Source

    $controlCheckout = Get-NamedStep $prepareSteps "Checkout workflow control source" "prepare" $Source
    Assert-AllowedMappingKeys $controlCheckout 8 @("uses", "with") "control checkout step" $Source
    Require-ExactLine $controlCheckout "        uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1" "control checkout action must remain immutable" $Source
    $null = Assert-ExactMappingBlock $controlCheckout 8 "with" @(
        '          ref: ${{ github.sha }}',
        "          persist-credentials: false"
    ) "control checkout inputs" $Source

    $stageValidator = Get-NamedStep $prepareSteps "Stage trusted release validator" "prepare" $Source
    Assert-ShellStep $stageValidator @("shell", "env", "run") "validator staging step" $Source
    $null = Assert-ExactMappingBlock $stageValidator 8 "env" @(
        '          VALIDATOR_PATH: ${{ runner.temp }}\validate-release-token.ps1'
    ) "validator staging env" $Source
    Assert-ExactBlockScalar $stageValidator 8 "run" @'
Copy-Item `
  -LiteralPath ".\scripts\codex\validate-release-token.ps1" `
  -Destination "$env:VALIDATOR_PATH" `
  -Force
'@ "validator staging run" $Source

    $validateRef = Get-NamedStep $prepareSteps "Validate requested ref" "prepare" $Source
    Assert-ShellStep $validateRef @("id", "shell", "env", "run") "requested ref validation step" $Source
    Require-ExactLine $validateRef "        id: release-ref" "requested ref step id must remain fixed" $Source
    $null = Assert-ExactMappingBlock $validateRef 8 "env" @(
        '          REQUESTED_REF: ${{ inputs.commit }}',
        '          VALIDATOR_PATH: ${{ runner.temp }}\validate-release-token.ps1'
    ) "requested ref validation env" $Source
    Assert-ExactBlockScalar $validateRef 8 "run" @'
& "$env:VALIDATOR_PATH" `
  -Kind Ref `
  -Value "$env:REQUESTED_REF" `
  -OutputName value `
  -OutputFile "$env:GITHUB_OUTPUT" | Out-Null
'@ "requested ref validation run" $Source

    $latestTag = Get-NamedStep $prepareSteps "Get latest stable release tag" "prepare" $Source
    Assert-AllowedMappingKeys $latestTag 8 @("id", "uses", "with") "latest stable tag step" $Source
    Require-ExactLine $latestTag "        id: latest-tag" "latest stable tag step id must remain fixed" $Source
    Require-ExactLine $latestTag "        uses: actions/github-script@f28e40c7f34bde8b3046d885e986cb6290c5673b # v7.1.0" "latest stable tag action must remain immutable" $Source
    $latestTagWith = Assert-ExactMappingBlock $latestTag 8 "with" @(
        '          github-token: ${{ secrets.GITHUB_TOKEN }}',
        "          result-encoding: string",
        "          script: |"
    ) "latest stable tag inputs" $Source
    Assert-ExactBlockScalar $latestTagWith 10 "script" @'
const latest = await github.rest.repos.getLatestRelease({
  owner: context.repo.owner,
  repo: context.repo.repo
});
return latest.data.tag_name;
'@ "latest stable tag script" $Source

    $validateTag = Get-NamedStep $prepareSteps "Validate stable release tag" "prepare" $Source
    Assert-ShellStep $validateTag @("id", "shell", "env", "run") "stable tag validation step" $Source
    Require-ExactLine $validateTag "        id: stable-tag" "stable tag step id must remain fixed" $Source
    $null = Assert-ExactMappingBlock $validateTag 8 "env" @(
        '          STABLE_TAG: ${{ steps.latest-tag.outputs.result }}',
        '          VALIDATOR_PATH: ${{ runner.temp }}\validate-release-token.ps1'
    ) "stable tag validation env" $Source
    Assert-ExactBlockScalar $validateTag 8 "run" @'
& "$env:VALIDATOR_PATH" `
  -Kind Tag `
  -Value "$env:STABLE_TAG" `
  -OutputName value `
  -OutputFile "$env:GITHUB_OUTPUT" | Out-Null
'@ "stable tag validation run" $Source

    $selectedCheckout = Get-NamedStep $prepareSteps "Checkout selected ref for resolution" "prepare" $Source
    Assert-AllowedMappingKeys $selectedCheckout 8 @("uses", "with") "selected ref resolution checkout step" $Source
    Require-ExactLine $selectedCheckout "        uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1" "selected ref resolution checkout action must remain immutable" $Source
    $null = Assert-ExactMappingBlock $selectedCheckout 8 "with" @(
        '          ref: ${{ steps.release-ref.outputs.value }}',
        "          path: selected-source",
        "          persist-credentials: false"
    ) "selected ref resolution checkout inputs" $Source

    $resolveCommit = Get-NamedStep $prepareSteps "Resolve immutable release commit" "prepare" $Source
    Assert-ShellStep $resolveCommit @("id", "shell", "env", "run") "immutable commit resolution step" $Source
    Require-ExactLine $resolveCommit "        id: release-commit" "immutable commit step id must remain fixed" $Source
    $null = Assert-ExactMappingBlock $resolveCommit 8 "env" @(
        '          VALIDATOR_PATH: ${{ runner.temp }}\validate-release-token.ps1'
    ) "immutable commit resolution env" $Source
    Assert-ExactBlockScalar $resolveCommit 8 "run" @'
$ErrorActionPreference = "Stop"
$full = (& git -C .\selected-source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $full -notmatch '^[0-9A-Fa-f]{40}$') {
  throw "Could not resolve the selected ref to one full commit id."
}

& "$env:VALIDATOR_PATH" `
  -Kind Hash `
  -Value "$full" `
  -OutputName sha `
  -OutputFile "$env:GITHUB_OUTPUT" | Out-Null

$short = $full.Substring(0, 12)
& "$env:VALIDATOR_PATH" `
  -Kind Hash `
  -Value "$short" `
  -OutputName short `
  -OutputFile "$env:GITHUB_OUTPUT" | Out-Null
'@ "immutable commit resolution run" $Source

    $archiveName = Get-NamedStep $prepareSteps "Set archive name" "prepare" $Source
    Assert-ShellStep $archiveName @("id", "shell", "env", "run") "archive name step" $Source
    Require-ExactLine $archiveName "        id: archive-name" "archive name step id must remain fixed" $Source
    $null = Assert-ExactMappingBlock $archiveName 8 "env" @(
        '          STABLE_TAG: ${{ steps.stable-tag.outputs.value }}',
        '          SHORT_HASH: ${{ steps.release-commit.outputs.short }}',
        '          VALIDATOR_PATH: ${{ runner.temp }}\validate-release-token.ps1'
    ) "archive name env" $Source
    Assert-ExactBlockScalar $archiveName 8 "run" @'
$archiveName = "LLPlayer-testing-$env:STABLE_TAG-$env:SHORT_HASH.7z"
& "$env:VALIDATOR_PATH" `
  -Kind Archive `
  -Value "$archiveName" `
  -OutputName name `
  -OutputFile "$env:GITHUB_OUTPUT" | Out-Null
'@ "archive name run" $Source

    $buildCheckout = Get-NamedStep $buildSteps "Checkout immutable release commit" "build" $Source
    Assert-AllowedMappingKeys $buildCheckout 8 @("uses", "with") "immutable build checkout step" $Source
    Require-ExactLine $buildCheckout "        uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1" "immutable build checkout action must remain immutable" $Source
    $null = Assert-ExactMappingBlock $buildCheckout 8 "with" @(
        '          ref: ${{ needs.prepare.outputs.commit_sha }}',
        "          persist-credentials: false"
    ) "immutable build checkout inputs" $Source

    $verifyCheckout = Get-NamedStep $buildSteps "Verify immutable checkout" "build" $Source
    Assert-ShellStep $verifyCheckout @("shell", "env", "run") "immutable checkout verification step" $Source
    $null = Assert-ExactMappingBlock $verifyCheckout 8 "env" @(
        '          EXPECTED_COMMIT_SHA: ${{ needs.prepare.outputs.commit_sha }}'
    ) "immutable checkout verification env" $Source
    Assert-ExactBlockScalar $verifyCheckout 8 "run" @'
$ErrorActionPreference = "Stop"
$actual = (& git rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or -not [string]::Equals(
    $actual,
    "$env:EXPECTED_COMMIT_SHA",
    [System.StringComparison]::Ordinal)) {
  throw "The build checkout does not match the prepared commit id."
}
'@ "immutable checkout verification run" $Source

    $buildPackage = Get-NamedStep $buildSteps "Build & Package" "build" $Source
    Assert-AllowedMappingKeys $buildPackage 8 @("uses", "with") "build/package step" $Source
    Require-ExactLine $buildPackage "        uses: ./.github/actions/build-package" "selected build/package action must remain local to the checked-out commit" $Source
    $null = Assert-ExactMappingBlock $buildPackage 8 "with" @(
        '          archive-name: ${{ needs.prepare.outputs.archive_name }}'
    ) "build/package inputs" $Source

    $artifactUpload = Get-NamedStep $buildSteps "Upload testing release artifact" "build" $Source
    Assert-AllowedMappingKeys $artifactUpload 8 @("uses", "with") "artifact upload step" $Source
    Require-ExactLine $artifactUpload "        uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1" "artifact upload action must remain immutable" $Source
    $null = Assert-ExactMappingBlock $artifactUpload 8 "with" @(
        "          name: llplayer-testing-release-unverified",
        '          path: ${{ needs.prepare.outputs.archive_name }}',
        "          if-no-files-found: error",
        "          overwrite: false",
        "          compression-level: 0",
        "          include-hidden-files: false",
        "          retention-days: 1"
    ) "artifact upload inputs" $Source

    $artifactValidationBody = @'
$ErrorActionPreference = "Stop"

$name = "$env:EXPECTED_ARCHIVE_NAME"
if ($name.Length -gt 160 -or
    $name -notmatch '^LLPlayer-[0-9A-Za-z][0-9A-Za-z._+\-]{0,139}\.7z$' -or
    $name.Contains("..")) {
  throw "Unexpected release archive name."
}

$root = [System.IO.Path]::GetFullPath("$env:ARTIFACT_DIRECTORY")
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
  throw "Downloaded artifact directory is missing."
}

$entries = @(Get-ChildItem -LiteralPath $root -Force)
if ($entries.Count -ne 1) {
  throw "Artifact must contain exactly one direct entry."
}

$file = $entries[0]
if ($file.PSIsContainer -or
    (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) -or
    $file.Length -le 0) {
  throw "Artifact entry must be one non-empty regular file."
}

if (-not [string]::Equals(
    $file.Name,
    $name,
    [System.StringComparison]::Ordinal)) {
  throw "Downloaded archive name does not match the trusted build metadata."
}

$expectedPath = [System.IO.Path]::GetFullPath((Join-Path $root $name))
if (-not [string]::Equals(
    $file.FullName,
    $expectedPath,
    [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Downloaded archive escaped the fixed artifact directory."
}

[System.IO.File]::AppendAllText(
  $env:GITHUB_OUTPUT,
  "path=$expectedPath$([Environment]::NewLine)",
  [System.Text.UTF8Encoding]::new($false))
'@

    $unverifiedDownload = Get-NamedStep $verifySteps "Download unverified testing release artifact" "verify" $Source
    Assert-AllowedMappingKeys $unverifiedDownload 8 @("uses", "with") "unverified artifact download step" $Source
    Require-ExactLine $unverifiedDownload "        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" "unverified artifact download action must remain immutable" $Source
    $null = Assert-ExactMappingBlock $unverifiedDownload 8 "with" @(
        "          name: llplayer-testing-release-unverified",
        '          path: ${{ runner.temp }}\llplayer-testing-release-unverified',
        "          digest-mismatch: error"
    ) "unverified artifact download inputs" $Source

    $verifyArtifact = Get-NamedStep $verifySteps "Validate unverified testing package" "verify" $Source
    Assert-ShellStep $verifyArtifact @("id", "shell", "env", "run") "unverified artifact validation step" $Source
    Require-ExactLine $verifyArtifact "        id: verified-asset" "unverified artifact validation id must remain fixed" $Source
    $null = Assert-ExactMappingBlock $verifyArtifact 8 "env" @(
        '          EXPECTED_ARCHIVE_NAME: ${{ needs.prepare.outputs.archive_name }}',
        '          ARTIFACT_DIRECTORY: ${{ runner.temp }}\llplayer-testing-release-unverified'
    ) "unverified artifact validation env" $Source
    Assert-ExactBlockScalar $verifyArtifact 8 "run" $artifactValidationBody "unverified artifact validation run" $Source

    $verifiedUpload = Get-NamedStep $verifySteps "Upload verified testing release artifact" "verify" $Source
    Assert-AllowedMappingKeys $verifiedUpload 8 @("uses", "with") "verified artifact upload step" $Source
    Require-ExactLine $verifiedUpload "        uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1" "verified artifact upload action must remain immutable" $Source
    $null = Assert-ExactMappingBlock $verifiedUpload 8 "with" @(
        "          name: llplayer-testing-release-verified",
        '          path: ${{ steps.verified-asset.outputs.path }}',
        "          if-no-files-found: error",
        "          overwrite: false",
        "          compression-level: 0",
        "          include-hidden-files: false",
        "          retention-days: 1"
    ) "verified artifact upload inputs" $Source

    $artifactDownload = Get-NamedStep $uploadSteps "Download testing release artifact" "upload" $Source
    Assert-AllowedMappingKeys $artifactDownload 8 @("uses", "with") "artifact download step" $Source
    Require-ExactLine $artifactDownload "        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" "artifact download action must remain immutable" $Source
    $null = Assert-ExactMappingBlock $artifactDownload 8 "with" @(
        "          name: llplayer-testing-release-verified",
        '          path: ${{ runner.temp }}\llplayer-testing-release-verified',
        "          digest-mismatch: error"
    ) "artifact download inputs" $Source

    $validateArtifact = Get-NamedStep $uploadSteps "Validate downloaded testing package" "upload" $Source
    Assert-ShellStep $validateArtifact @("id", "shell", "env", "run") "downloaded artifact validation step" $Source
    Require-ExactLine $validateArtifact "        id: release-asset" "downloaded artifact validation id must remain fixed" $Source
    $null = Assert-ExactMappingBlock $validateArtifact 8 "env" @(
        '          EXPECTED_ARCHIVE_NAME: ${{ needs.prepare.outputs.archive_name }}',
        '          ARTIFACT_DIRECTORY: ${{ runner.temp }}\llplayer-testing-release-verified'
    ) "downloaded artifact validation env" $Source
    Assert-ExactBlockScalar $validateArtifact 8 "run" $artifactValidationBody "downloaded artifact validation run" $Source

    $releaseUpload = Get-NamedStep $uploadSteps "Upload Testing Asset (overwrite)" "upload" $Source
    Assert-ShellStep $releaseUpload @("shell", "env", "run") "release asset upload step" $Source
    $null = Assert-ExactMappingBlock $releaseUpload 8 "env" @(
        '          ARCHIVE_PATH: ${{ steps.release-asset.outputs.path }}',
        '          RELEASE_REPOSITORY: ${{ github.repository }}',
        '          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}'
    ) "release asset upload env" $Source
    Assert-ExactBlockScalar $releaseUpload 8 "run" @'
$ErrorActionPreference = "Stop"
& gh release upload v0.0.1 "$env:ARCHIVE_PATH" `
  --clobber `
  --repo "$env:RELEASE_REPOSITORY"
if ($LASTEXITCODE -ne 0) {
  throw "Testing release upload failed."
}
'@ "release asset upload run" $Source
}

function Assert-ContractRejected([string]$Text, [string]$Description) {
    $rejected = $false
    try {
        Assert-TestingReleaseContract $Text "adversarial fixture ($Description)"
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Testing Release boundary validator accepted adversarial fixture: $Description."
    }
}

function Replace-First(
    [string]$Text,
    [string]$OldValue,
    [string]$NewValue,
    [string]$Description
) {
    $index = $Text.IndexOf($OldValue, [System.StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "Adversarial fixture setup could not find '$Description'."
    }
    return $Text.Substring(0, $index) + $NewValue + $Text.Substring($index + $OldValue.Length)
}

function Assert-MutationRejected(
    [string]$Text,
    [string]$OldValue,
    [string]$NewValue,
    [string]$Description
) {
    $fixture = Replace-First $Text $OldValue $NewValue $Description
    Assert-ContractRejected $fixture $Description
}

function Replace-Last(
    [string]$Text,
    [string]$OldValue,
    [string]$NewValue,
    [string]$Description
) {
    $index = $Text.LastIndexOf($OldValue, [System.StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "Adversarial fixture setup could not find '$Description'."
    }
    return $Text.Substring(0, $index) + $NewValue + $Text.Substring($index + $OldValue.Length)
}

function Assert-LastMutationRejected(
    [string]$Text,
    [string]$OldValue,
    [string]$NewValue,
    [string]$Description
) {
    $fixture = Replace-Last $Text $OldValue $NewValue $Description
    Assert-ContractRejected $fixture $Description
}

function Test-DownloadedArtifactShape([string]$Directory, [string]$ExpectedName) {
    $name = "$ExpectedName"
    if ($name.Length -gt 160 -or
        $name -notmatch '^LLPlayer-[0-9A-Za-z][0-9A-Za-z._+\-]{0,139}\.7z$' -or
        $name.Contains("..")) {
        throw "Unexpected release archive name."
    }

    $root = [System.IO.Path]::GetFullPath($Directory)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Downloaded artifact directory is missing."
    }

    $entries = @(Get-ChildItem -LiteralPath $root -Force)
    if ($entries.Count -ne 1) {
        throw "Artifact must contain exactly one direct entry."
    }

    $file = $entries[0]
    if ($file.PSIsContainer -or
        (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) -or
        $file.Length -le 0) {
        throw "Artifact entry must be one non-empty regular file."
    }

    if (-not [string]::Equals(
        $file.Name,
        $name,
        [System.StringComparison]::Ordinal)) {
        throw "Downloaded archive name does not match the trusted build metadata."
    }

    $expectedPath = [System.IO.Path]::GetFullPath((Join-Path $root $name))
    if (-not [string]::Equals(
        $file.FullName,
        $expectedPath,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Downloaded archive escaped the fixed artifact directory."
    }
    return $expectedPath
}

function Assert-ArtifactShapeRejected(
    [string]$Directory,
    [string]$ExpectedName,
    [string]$Description
) {
    try {
        $null = Test-DownloadedArtifactShape $Directory $ExpectedName
    }
    catch {
        return
    }
    throw "Downloaded artifact validator accepted $Description."
}

if (-not (Test-Path -LiteralPath $workflowPath)) {
    throw "Testing Release workflow is missing: $workflowPath"
}

$workflowText = Normalize-Text (Get-Content -LiteralPath $workflowPath -Raw)
Assert-TestingReleaseContract $workflowText "testing-release.yml"

Assert-MutationRejected $workflowText `
    "permissions: {}" `
    "permissions:`n  contents: write" `
    "workflow-level write permission"
Assert-MutationRejected $workflowText `
    "  prepare:`n    runs-on: windows-latest`n    permissions:`n      contents: read" `
    "  prepare:`n    runs-on: windows-latest`n    permissions:`n      contents: write" `
    "write permission in prepare"
Assert-MutationRejected $workflowText `
    "  build:`n    needs: prepare`n    runs-on: windows-latest`n    permissions:`n      contents: read" `
    "  build:`n    needs: prepare`n    runs-on: windows-latest`n    permissions:`n      contents: write" `
    "write permission in build"
Assert-MutationRejected $workflowText `
    "  build:`n    needs: prepare`n    runs-on: windows-latest`n    permissions:`n      contents: read`n`n    steps:" `
    "  build:`n    needs: prepare`n    runs-on: windows-latest`n    permissions:`n      contents: read`n    outputs:`n      artifact_id: `${{ steps.package.outputs.artifact-id }}`n`n    steps:" `
    "an untrusted build-job output"
Assert-MutationRejected $workflowText `
    "  verify:`n    needs: [prepare, build]`n    runs-on: windows-latest`n    permissions:`n      contents: read" `
    "  verify:`n    needs: [prepare, build]`n    runs-on: windows-latest`n    permissions:`n      contents: write" `
    "write permission in verify"
Assert-MutationRejected $workflowText `
    "  verify:`n    needs: [prepare, build]" `
    "  verify:`n    needs: prepare" `
    "verify job without the selected build"
Assert-MutationRejected $workflowText `
    "  verify:`n    needs: [prepare, build]" `
    "  verify:`n    needs: [prepare, build]`n    if: always()" `
    "if always on the verify job"
Assert-MutationRejected $workflowText `
    "  verify:`n    needs: [prepare, build]`n    runs-on: windows-latest" `
    "  verify:`n    needs: [prepare, build]`n    runs-on: self-hosted" `
    "a self-hosted verification runner"
Assert-MutationRejected $workflowText `
    "    permissions:`n      contents: write`n`n    steps:" `
    "    permissions: write-all`n`n    steps:" `
    "write-all in upload"
Assert-MutationRejected $workflowText `
    "    permissions:`n      contents: write`n`n    steps:" `
    "    permissions:`n      contents: write`n      actions: write`n`n    steps:" `
    "an extra upload-job permission"
Assert-MutationRejected $workflowText `
    "  upload:`n    needs: [prepare, verify]" `
    "  upload:`n    needs: [prepare, verify]`n    if: always()" `
    "if always on the write job"
Assert-MutationRejected $workflowText `
    "  upload:`n    needs: [prepare, verify]" `
    "  upload:`n    needs: [prepare, build]" `
    "write job bypassing trusted verification"
Assert-MutationRejected $workflowText `
    "  upload:`n    needs: [prepare, verify]`n    runs-on: windows-latest" `
    "  upload:`n    needs: [prepare, verify]`n    runs-on: self-hosted" `
    "a self-hosted write runner"
Assert-MutationRejected $workflowText `
    "  upload:`n    needs: [prepare, verify]" `
    "  decoy:`n    runs-on: windows-latest`n    steps:`n      - name: Decoy`n        run: echo decoy`n`n  upload:`n    needs: [prepare, verify]" `
    "an extra job"
Assert-MutationRejected $workflowText `
    "on:`n  workflow_dispatch:" `
    "on:`n  push:`n  workflow_dispatch:" `
    "an automatic push trigger"
Assert-MutationRejected $workflowText `
    "      - name: Require trusted workflow ref" `
    "      - name: Skip trusted workflow ref" `
    "removal of the default-branch dispatch guard"
Assert-MutationRejected $workflowText `
    '          ref: ${{ needs.prepare.outputs.commit_sha }}' `
    '          ref: ${{ inputs.commit }}' `
    "build checkout of the moving user ref"
Assert-MutationRejected $workflowText `
    "        uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1" `
    "        uses: actions/checkout@v5" `
    "a mutable control checkout action"
Assert-LastMutationRejected $workflowText `
    "        uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1" `
    "        uses: actions/checkout@v5" `
    "a mutable build checkout action"
Assert-MutationRejected $workflowText `
    "        uses: actions/github-script@f28e40c7f34bde8b3046d885e986cb6290c5673b # v7.1.0" `
    "        uses: actions/github-script@v7" `
    "a mutable release metadata action"
Assert-MutationRejected $workflowText `
    "        uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1" `
    "        uses: actions/upload-artifact@v7" `
    "a mutable unverified artifact upload action"
Assert-LastMutationRejected $workflowText `
    "        uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1" `
    "        uses: actions/upload-artifact@v7" `
    "a mutable verified artifact upload action"
Assert-MutationRejected $workflowText `
    "          name: llplayer-testing-release-unverified`n          path: `${{ needs.prepare.outputs.archive_name }}" `
    "          name: `${{ needs.prepare.outputs.archive_name }}`n          path: `${{ needs.prepare.outputs.archive_name }}" `
    "a dynamic unverified artifact name"
Assert-MutationRejected $workflowText `
    '          path: ${{ needs.prepare.outputs.archive_name }}' `
    "          path: '*.7z'" `
    "a wildcard unverified artifact upload path"
Assert-MutationRejected $workflowText `
    "          overwrite: false" `
    "          overwrite: true" `
    "artifact overwrite in the selected-code job"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download unverified testing release artifact" `
    "    steps:`n      - name: Checkout selected code in verify job`n        uses: actions/checkout@v5`n`n      - name: Download unverified testing release artifact" `
    "checkout in the verify job"
Assert-MutationRejected $workflowText `
    "      - name: Upload verified testing release artifact" `
    "      - name: Execute selected action in verify job`n        uses: ./.github/actions/build-package`n`n      - name: Upload verified testing release artifact" `
    "a local action in the verify job"
Assert-MutationRejected $workflowText `
    "        id: verified-asset`n        shell: pwsh" `
    "        id: verified-asset`n        continue-on-error: true`n        shell: pwsh" `
    "continue-on-error on unverified package validation"
Assert-MutationRejected $workflowText `
    '          ARTIFACT_DIRECTORY: ${{ runner.temp }}\llplayer-testing-release-unverified' `
    "          ARTIFACT_DIRECTORY: `${{ runner.temp }}\llplayer-testing-release-unverified`n          GH_TOKEN: `${{ secrets.GITHUB_TOKEN }}" `
    "token exposure to the verify job"
Assert-MutationRejected $workflowText `
    "        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" `
    "        uses: actions/download-artifact@v8" `
    "a mutable unverified artifact download action"
Assert-MutationRejected $workflowText `
    "          name: llplayer-testing-release-unverified`n          path: `${{ runner.temp }}\llplayer-testing-release-unverified" `
    "          artifact-ids: `${{ needs.build.outputs.artifact_id }}`n          path: `${{ runner.temp }}\llplayer-testing-release-unverified" `
    "artifact selection through an untrusted build output"
Assert-MutationRejected $workflowText `
    "          digest-mismatch: error" `
    "          digest-mismatch: warn" `
    "non-failing unverified artifact digest validation"
Assert-MutationRejected $workflowText `
    "          digest-mismatch: error" `
    "          digest-mismatch: error`n          github-token: `${{ secrets.GITHUB_TOKEN }}`n          run-id: 1" `
    "cross-run unverified artifact download inputs"
Assert-MutationRejected $workflowText `
    "          name: llplayer-testing-release-verified`n          path: `${{ steps.verified-asset.outputs.path }}" `
    "          name: llplayer-testing-release-verified`n          path: `${{ needs.prepare.outputs.archive_name }}" `
    "verified re-upload bypassing trusted path validation"
Assert-MutationRejected $workflowText `
    "          name: llplayer-testing-release-verified`n          path: `${{ steps.verified-asset.outputs.path }}" `
    "          name: llplayer-testing-release-unverified`n          path: `${{ steps.verified-asset.outputs.path }}" `
    "verified re-upload using the raw artifact name"
Assert-LastMutationRejected $workflowText `
    "          overwrite: false" `
    "          overwrite: true" `
    "artifact overwrite in the verification job"
Assert-LastMutationRejected $workflowText `
    "        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" `
    "        uses: actions/download-artifact@v8" `
    "a mutable artifact download action in the write job"
Assert-MutationRejected $workflowText `
    "          name: llplayer-testing-release-verified`n          path: `${{ runner.temp }}\llplayer-testing-release-verified" `
    "          name: llplayer-testing-release-unverified`n          path: `${{ runner.temp }}\llplayer-testing-release-unverified" `
    "write job consuming the unverified artifact"
Assert-MutationRejected $workflowText `
    "          name: llplayer-testing-release-verified`n          path: `${{ runner.temp }}\llplayer-testing-release-verified" `
    "          artifact-ids: `${{ needs.verify.outputs.artifact_id }}`n          path: `${{ runner.temp }}\llplayer-testing-release-verified" `
    "write-job artifact selection through an output"
Assert-LastMutationRejected $workflowText `
    "          digest-mismatch: error" `
    "          digest-mismatch: warn" `
    "non-failing verified artifact digest validation"
Assert-LastMutationRejected $workflowText `
    "          digest-mismatch: error" `
    "          digest-mismatch: error`n          github-token: `${{ secrets.GITHUB_TOKEN }}`n          run-id: 1" `
    "cross-run verified artifact download inputs"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download testing release artifact" `
    "    steps:`n      - name: Checkout selected code in write job`n        uses: actions/checkout@v5`n`n      - name: Download testing release artifact" `
    "checkout in the write job"
Assert-MutationRejected $workflowText `
    "      - name: Upload Testing Asset (overwrite)" `
    "      - name: Execute selected action in write job`n        uses: ./.github/actions/build-package`n`n      - name: Upload Testing Asset (overwrite)" `
    "a local action in the write job"
Assert-MutationRejected $workflowText `
    "        id: release-asset`n        shell: pwsh" `
    "        id: release-asset`n        continue-on-error: true`n        shell: pwsh" `
    "continue-on-error on package validation"
Assert-MutationRejected $workflowText `
    '          ARTIFACT_DIRECTORY: ${{ runner.temp }}\llplayer-testing-release-verified' `
    "          ARTIFACT_DIRECTORY: `${{ runner.temp }}\llplayer-testing-release-verified`n          GH_TOKEN: `${{ secrets.GITHUB_TOKEN }}" `
    "write token exposure to package validation"
Assert-LastMutationRejected $workflowText `
    '          $entries = @(Get-ChildItem -LiteralPath $root -Force)' `
    "          `$entries = @(Get-ChildItem -LiteralPath `$root -Force)`n          Expand-Archive -LiteralPath `$entries[0].FullName" `
    "artifact extraction in the write job"
Assert-LastMutationRejected $workflowText `
    '          $file = $entries[0]' `
    "          `$file = `$entries[0]`n          & `$file.FullName" `
    "artifact execution in the write job"
Assert-MutationRejected $workflowText `
    '          ARCHIVE_PATH: ${{ steps.release-asset.outputs.path }}' `
    '          ARCHIVE_PATH: ${{ needs.prepare.outputs.archive_name }}' `
    "release upload bypassing write-job path validation"
Assert-MutationRejected $workflowText `
    '          & gh release upload v0.0.1 "$env:ARCHIVE_PATH" `' `
    '          & gh release upload v0.0.1 "${{ needs.prepare.outputs.archive_name }}" `' `
    "direct expression interpolation in the privileged upload command"
Assert-MutationRejected $workflowText `
    "permissions: {}" `
    "permissions: {}`ndefaults:`n  run:`n    shell: cmd" `
    "workflow-level custom shell defaults"
Assert-MutationRejected $workflowText `
    "permissions: {}" `
    "permissions: {}`npermissions: {}" `
    "duplicate workflow permissions"
Assert-MutationRejected $workflowText `
    "    permissions:`n      contents: read" `
    '    "permissions":' + "`n      contents: read" `
    "a quoted protected permission key"

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixtureRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $tempRoot ("llplayer-release-boundary-" + [guid]::NewGuid().ToString("N"))))
if (-not $fixtureRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Artifact fixture path escaped the system temporary directory."
}

$expectedArchive = "LLPlayer-testing-v0.3.61-deadbeef1234.7z"
try {
    $positive = Join-Path $fixtureRoot "positive"
    $null = New-Item -ItemType Directory -Path $positive -Force
    [System.IO.File]::WriteAllBytes((Join-Path $positive $expectedArchive), [byte[]](1, 2, 3))
    $positivePath = Test-DownloadedArtifactShape $positive $expectedArchive
    if (-not [string]::Equals(
        $positivePath,
        [System.IO.Path]::GetFullPath((Join-Path $positive $expectedArchive)),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Downloaded artifact validator returned an unexpected positive path."
    }

    $extra = Join-Path $fixtureRoot "extra"
    $null = New-Item -ItemType Directory -Path $extra -Force
    [System.IO.File]::WriteAllBytes((Join-Path $extra $expectedArchive), [byte[]](1))
    [System.IO.File]::WriteAllBytes((Join-Path $extra "extra.txt"), [byte[]](1))
    Assert-ArtifactShapeRejected $extra $expectedArchive "an artifact with an extra entry"

    $nested = Join-Path $fixtureRoot "nested"
    $null = New-Item -ItemType Directory -Path (Join-Path $nested "nested") -Force
    [System.IO.File]::WriteAllBytes((Join-Path $nested "nested\$expectedArchive"), [byte[]](1))
    Assert-ArtifactShapeRejected $nested $expectedArchive "a nested archive"

    $empty = Join-Path $fixtureRoot "empty"
    $null = New-Item -ItemType Directory -Path $empty -Force
    [System.IO.File]::WriteAllBytes((Join-Path $empty $expectedArchive), [byte[]]@())
    Assert-ArtifactShapeRejected $empty $expectedArchive "an empty archive"

    $wrongName = Join-Path $fixtureRoot "wrong-name"
    $null = New-Item -ItemType Directory -Path $wrongName -Force
    [System.IO.File]::WriteAllBytes((Join-Path $wrongName "LLPlayer-testing-v0.3.61-cafebabe1234.7z"), [byte[]](1))
    Assert-ArtifactShapeRejected $wrongName $expectedArchive "a mismatched archive basename"

    Assert-ArtifactShapeRejected $positive "..\LLPlayer-testing-v0.3.61-deadbeef1234.7z" "an unsafe expected basename"
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Host "Testing Release write-token boundary verification completed."

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$workflowPath = Join-Path $repoRoot ".github\workflows\testing-release.yml"
$expectedWorkflowSha256 = "ade832a68c5b49611c8d949518a7510b7358d6dfa9a4faee01bfd545a2964e35"

function Normalize-Text([string]$Text) {
    return (($Text -replace "`r`n", "`n") -replace "`r", "`n").TrimEnd("`n")
}

function Get-TextSha256([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((Normalize-Text $Text))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
        return ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Require-LiteralCount(
    [string]$Text,
    [string]$Literal,
    [int]$ExpectedCount,
    [string]$Description,
    [string]$Source
) {
    $count = 0
    $offset = 0
    while ($offset -le $Text.Length - $Literal.Length) {
        $index = $Text.IndexOf($Literal, $offset, [System.StringComparison]::Ordinal)
        if ($index -lt 0) {
            break
        }
        $count++
        $offset = $index + $Literal.Length
    }
    if ($count -ne $ExpectedCount) {
        throw "$Source must contain exactly $ExpectedCount $Description; found $count."
    }
}

function Forbid-Literal(
    [string]$Text,
    [string]$Literal,
    [string]$Description,
    [string]$Source
) {
    if ($Text.Contains($Literal)) {
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
        '(?m)(?:^|[,{]|^[ \t]*-[ \t]*)[ \t]*(?:uses|"uses"|''uses'')[ \t]*:[ \t]*(?<value>[^,}\r\n]+?)[ \t]*(?=,|}|$)')
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
            Pattern = '(?m)^ {0,4}[A-Za-z0-9_.-]+[ \t]*:[ \t]*"(?:(?:\\.)|[^"\\])*(?:\\)?[ \t]*$'
            Description = 'multiline double-quoted workflow or job scalar'
        },
        @{
            Pattern = '(?m)^ {0,4}[A-Za-z0-9_.-]+[ \t]*:[ \t]*''(?:''''|[^''])*[ \t]*$'
            Description = 'multiline single-quoted workflow or job scalar'
        },
        @{
            Pattern = '(?m)^[ \t]*-[ \t]*(?:#[^\r\n]*)?$'
            Description = 'bare step declaration with deep-indented child mappings'
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

function Assert-TestingReleaseSemantics([string]$Text, [string]$Source) {
    $normalized = Normalize-Text $Text
    Require-LiteralCount $normalized "name: Testing Release" 1 "workflow name" $Source
    Require-LiteralCount $normalized "  workflow_dispatch:" 1 "manual trigger" $Source
    Require-LiteralCount $normalized "  group: testing-release" 1 "serialized release concurrency group" $Source
    Require-LiteralCount $normalized "  cancel-in-progress: false" 1 "non-cancelling release concurrency policy" $Source
    Require-LiteralCount $normalized "        description: 'Lowercase full 40-character commit SHA'" 1 "immutable lowercase input description" $Source
    Require-LiteralCount $normalized '          WORKFLOW_REF: ${{ github.ref }}' 1 "trusted workflow ref input" $Source
    Require-LiteralCount $normalized '          DEFAULT_BRANCH: ${{ github.event.repository.default_branch }}' 1 "default-branch identity input" $Source
    Require-LiteralCount $normalized '$expectedRef = "refs/heads/$env:DEFAULT_BRANCH"' 1 "default-branch ref construction" $Source
    Require-LiteralCount $normalized '              "$env:WORKFLOW_REF",' 1 "workflow ref equality operand" $Source
    Require-LiteralCount $normalized '              $expectedRef,' 1 "default-branch equality operand" $Source
    Require-LiteralCount $normalized "Testing Release must be dispatched from the default branch." 1 "default-branch control gate" $Source
    Require-LiteralCount $normalized '          REQUESTED_COMMIT: ${{ inputs.commit }}' 1 "raw input boundary" $Source
    Require-LiteralCount $normalized '          CONTROL_COMMIT: ${{ github.sha }}' 1 "trusted control-commit input" $Source
    Require-LiteralCount $normalized "if (`"`$env:REQUESTED_COMMIT`" -cnotmatch '^[0-9a-f]{40}`$')" 1 "lowercase full-SHA input guard" $Source
    Require-LiteralCount $normalized "`"`$env:CONTROL_COMMIT`" -cnotmatch '^[0-9a-f]{40}`$'" 1 "trusted control-commit format guard" $Source
    Require-LiteralCount $normalized '                "$env:REQUESTED_COMMIT",' 1 "requested commit equality operand" $Source
    Require-LiteralCount $normalized '                "$env:CONTROL_COMMIT",' 1 "trusted control commit equality operand" $Source
    Require-LiteralCount $normalized "Testing Release requires the requested commit to equal the trusted default-branch workflow commit." 1 "trusted control-commit equality gate" $Source
    Require-LiteralCount $normalized "            -Kind Hash ``" 2 "trusted hash validation invocation" $Source

    $defaultBranchGateIndex = $normalized.IndexOf(
        "Testing Release must be dispatched from the default branch.",
        [System.StringComparison]::Ordinal)
    $controlCheckoutIndex = $normalized.IndexOf(
        "      - name: Checkout workflow control source",
        [System.StringComparison]::Ordinal)
    if ($defaultBranchGateIndex -lt 0 -or
        $controlCheckoutIndex -lt 0 -or
        $defaultBranchGateIndex -ge $controlCheckoutIndex) {
        throw "$Source must verify the default-branch control ref before checkout."
    }

    $controlGateIndex = $normalized.IndexOf(
        "Testing Release requires the requested commit to equal the trusted default-branch workflow commit.",
        [System.StringComparison]::Ordinal)
    $selectedCheckoutIndex = $normalized.IndexOf(
        "      - name: Checkout requested commit",
        [System.StringComparison]::Ordinal)
    if ($controlGateIndex -lt 0 -or
        $selectedCheckoutIndex -lt 0 -or
        $controlGateIndex -ge $selectedCheckoutIndex) {
        throw "$Source must enforce selected equals trusted control commit before checkout."
    }

    Require-LiteralCount $normalized "  prepare:" 1 "prepare job" $Source
    Require-LiteralCount $normalized "  build:" 1 "build job" $Source
    Require-LiteralCount $normalized "  verify:" 1 "verify job" $Source
    Require-LiteralCount $normalized "  upload:" 1 "upload job" $Source
    Require-LiteralCount $normalized "      contents: read" 3 "read-only job permission" $Source
    Require-LiteralCount $normalized "      contents: write" 1 "narrow write-job permission" $Source
    Require-LiteralCount $normalized "          persist-credentials: false" 3 "credential-free checkout" $Source

    $checkoutAction = 'actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1'
    $setupDotnetAction = 'actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0'
    $uploadArtifactAction = 'actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1'
    $downloadArtifactAction = 'actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1'
    $packageAction = './.github/actions/build-package'

    $prepareJobIndex = $normalized.IndexOf("`n  prepare:", [System.StringComparison]::Ordinal)
    $buildJobIndex = $normalized.IndexOf("`n  build:", [System.StringComparison]::Ordinal)
    $verifyJobIndex = $normalized.IndexOf("`n  verify:", [System.StringComparison]::Ordinal)
    $uploadJobIndex = $normalized.IndexOf("`n  upload:", [System.StringComparison]::Ordinal)
    if ($prepareJobIndex -lt 0 -or
        $buildJobIndex -le $prepareJobIndex -or
        $verifyJobIndex -le $buildJobIndex -or
        $uploadJobIndex -le $verifyJobIndex) {
        throw "$Source must preserve the reviewed prepare/build/verify/upload job routing."
    }
    $prepareBlock = $normalized.Substring($prepareJobIndex, $buildJobIndex - $prepareJobIndex)
    $buildBlock = $normalized.Substring($buildJobIndex, $verifyJobIndex - $buildJobIndex)
    $verifyBlock = $normalized.Substring($verifyJobIndex, $uploadJobIndex - $verifyJobIndex)
    $uploadBlock = $normalized.Substring($uploadJobIndex)

    Forbid-Literal $uploadBlock "actions/checkout@" "checkout in the write job" $Source
    Forbid-Literal $uploadBlock "uses: ./.github/actions/" "local action execution in the write job" $Source
    Forbid-Literal $uploadBlock "Expand-Archive" "archive extraction in the write job" $Source
    Forbid-Literal $uploadBlock "& `$archivePath" "artifact execution in the write job" $Source

    Assert-CanonicalActionSyntax $normalized $Source

    Require-LiteralCount $normalized "        uses: actions/checkout@93cb6efe18208431cddfb8368fd83d5badbf9bfd # v5.0.1" 3 "immutable checkout action" $Source
    Require-LiteralCount $normalized "        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0" 1 "immutable .NET setup action" $Source
    Require-LiteralCount $normalized "        uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1" 2 "immutable artifact upload action" $Source
    Require-LiteralCount $normalized "        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" 2 "immutable artifact download action" $Source
    Require-LiteralCount $normalized "        uses: ./.github/actions/build-package" 1 "selected local packaging action" $Source

    Assert-ExactUsesMultiset `
        -Text $normalized `
        -ExpectedUses @(
            $checkoutAction, $checkoutAction, $checkoutAction,
            $setupDotnetAction, $packageAction,
            $uploadArtifactAction, $uploadArtifactAction,
            $downloadArtifactAction, $downloadArtifactAction) `
        -Description "workflow exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $prepareBlock `
        -ExpectedUses @($checkoutAction, $checkoutAction) `
        -Description "prepare-job exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $buildBlock `
        -ExpectedUses @($checkoutAction, $setupDotnetAction, $packageAction, $uploadArtifactAction) `
        -Description "build-job exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $verifyBlock `
        -ExpectedUses @($downloadArtifactAction, $uploadArtifactAction) `
        -Description "verify-job exact uses multiset" `
        -Source $Source
    Assert-ExactUsesMultiset `
        -Text $uploadBlock `
        -ExpectedUses @($downloadArtifactAction) `
        -Description "upload-job exact uses multiset" `
        -Source $Source

    Require-LiteralCount $prepareBlock ("      - name: Checkout workflow control source`n        uses: " + $checkoutAction) 1 "workflow-control checkout step routing" $Source
    Require-LiteralCount $prepareBlock ("      - name: Checkout requested commit`n        uses: " + $checkoutAction) 1 "requested-commit checkout step routing" $Source
    Require-LiteralCount $buildBlock ("      - name: Checkout immutable release commit`n        uses: " + $checkoutAction) 1 "build checkout step routing" $Source
    Require-LiteralCount $buildBlock ("      - name: Setup .NET`n        uses: " + $setupDotnetAction) 1 ".NET setup step routing" $Source
    Require-LiteralCount $buildBlock ("      - name: Build & Package`n        id: package`n        uses: " + $packageAction) 1 "packaging action step routing" $Source
    Require-LiteralCount $buildBlock ("      - name: Upload testing release artifact`n        uses: " + $uploadArtifactAction) 1 "unverified artifact upload step routing" $Source
    Require-LiteralCount $verifyBlock ("      - name: Download unverified testing release artifact`n        uses: " + $downloadArtifactAction) 1 "unverified artifact download step routing" $Source
    Require-LiteralCount $verifyBlock ("      - name: Upload verified testing release artifact`n        uses: " + $uploadArtifactAction) 1 "verified artifact upload step routing" $Source
    Require-LiteralCount $uploadBlock ("      - name: Download verified testing release artifact`n        uses: " + $downloadArtifactAction) 1 "verified artifact download step routing" $Source

    Require-LiteralCount $normalized '      release_tag: ${{ steps.release-metadata.outputs.tag }}' 1 "trusted release-tag output" $Source
    Require-LiteralCount $normalized '      archive_name: ${{ steps.release-metadata.outputs.archive }}' 1 "trusted archive-name output" $Source
    Require-LiteralCount $normalized '$tag = "testing-$short"' 1 "commit-scoped testing tag formula" $Source
    Require-LiteralCount $normalized '$archive = "LLPlayer-testing-$short-x64.7z"' 1 "commit-scoped archive formula" $Source
    Require-LiteralCount $normalized '          ref: ${{ needs.prepare.outputs.commit_sha }}' 1 "immutable selected checkout" $Source
    Require-LiteralCount $normalized "        id: package" 1 "packaging evidence step id" $Source
    Require-LiteralCount $normalized '      yt_dlp_version: ${{ steps.package.outputs.yt-dlp-version }}' 1 "yt-dlp version evidence output" $Source
    Require-LiteralCount $normalized '      yt_dlp_sha256: ${{ steps.package.outputs.yt-dlp-sha256 }}' 1 "yt-dlp digest evidence output" $Source
    Require-LiteralCount $normalized '      yt_dlp_size: ${{ steps.package.outputs.yt-dlp-size }}' 1 "yt-dlp size evidence output" $Source
    Require-LiteralCount $normalized '      archive_sha256: ${{ steps.package.outputs.archive-sha256 }}' 1 "archive digest evidence output" $Source
    Require-LiteralCount $normalized '      archive_size: ${{ steps.package.outputs.archive-size }}' 1 "archive size evidence output" $Source

    Require-LiteralCount $normalized "          digest-mismatch: error" 2 "fail-closed artifact digest policy" $Source
    Require-LiteralCount $normalized "          overwrite: false" 2 "non-overwriting workflow artifact policy" $Source
    Require-LiteralCount $normalized "          retention-days: 1" 2 "short artifact retention" $Source
    Require-LiteralCount $normalized '& $sevenZip t "$expectedPath"' 1 "7-Zip integrity test" $Source
    Require-LiteralCount $normalized '& $sevenZip e "$expectedPath" "Plugins\YoutubeDL\yt-dlp.exe" "-o$ytDlpRoot" -y' 1 "bounded yt-dlp evidence extraction" $Source
    Require-LiteralCount $normalized "Downloaded archive does not match packaging evidence." 1 "archive evidence comparison" $Source
    Require-LiteralCount $normalized "Archived yt-dlp.exe does not match packaging evidence." 1 "yt-dlp evidence comparison" $Source
    Require-LiteralCount $normalized "Verified artifact metadata changed before the write boundary." 1 "write-boundary evidence comparison" $Source

    Require-LiteralCount $normalized '$tag -cnotmatch ''^testing-[0-9a-f]{12}$''' 1 "privileged tag allowlist" $Source
    Require-LiteralCount $normalized '$name -cnotmatch ''^LLPlayer-testing-[0-9a-f]{12}-x64\.7z$''' 1 "privileged asset allowlist" $Source
    Require-LiteralCount $normalized '"refs/tags/$Tag"' 1 "exact tag-ref comparison" $Source
    Require-LiteralCount $normalized '"ref=refs/tags/$tag"' 1 "non-force exact tag creation" $Source
    Require-LiteralCount $normalized "          function Get-GhReleaseByTag(" 1 "authenticated draft release lookup helper" $Source
    Require-LiteralCount $normalized '            $commandErrorPreference = $ErrorActionPreference' 1 "native lookup error-preference capture" $Source
    Require-LiteralCount $normalized '              $ErrorActionPreference = "Continue"' 1 "nonterminating native not-found capture" $Source
    Require-LiteralCount $normalized '              $PSNativeCommandUseErrorActionPreference = $false' 1 "PowerShell 7 native exit capture" $Source
    Require-LiteralCount $normalized '              $PSNativeCommandUseErrorActionPreference = $nativeErrorPreference' 1 "PowerShell 7 native exit preference restoration" $Source
    Require-LiteralCount $normalized '              $ErrorActionPreference = $commandErrorPreference' 1 "native lookup error-preference restoration" $Source
    Require-LiteralCount $normalized '              $lines = @(& gh release view "$Tag" `' 1 "draft-aware GitHub CLI lookup" $Source
    Require-LiteralCount $normalized '                --repo "$Repo" `' 1 "exact draft lookup repository binding" $Source
    Require-LiteralCount $normalized "                --json databaseId,tagName,isDraft,isPrerelease,assets 2>&1)" 1 "draft lookup metadata allowlist" $Source
    Require-LiteralCount $normalized '                    "release not found",' 1 "exact release-not-found classification" $Source
    Require-LiteralCount $normalized '            if (-not [long]::TryParse("$($view.databaseId)", [ref]$releaseId) -or' 1 "positive numeric draft identity parsing" $Source
    Require-LiteralCount $normalized '                $releaseId -le 0) {' 1 "positive numeric draft identity guard" $Source
    Require-LiteralCount $normalized '              tag_name = "$($view.tagName)"' 1 "draft tag-name normalization" $Source
    Require-LiteralCount $normalized '              draft = $view.isDraft' 1 "draft-state normalization" $Source
    Require-LiteralCount $normalized '              prerelease = $view.isPrerelease' 1 "prerelease-state normalization" $Source
    Require-LiteralCount $normalized '              assets = @($view.assets)' 1 "draft asset normalization" $Source
    Require-LiteralCount $normalized '          $release = Get-GhReleaseByTag $repo $tag -AllowNotFound' 1 "pre-create authenticated draft lookup" $Source
    Require-LiteralCount $normalized '          if ($null -eq $release) {' 1 "existing-draft recovery branch" $Source
    Require-LiteralCount $normalized '            $release = Get-GhReleaseByTag $repo $tag' 1 "post-create authenticated draft lookup" $Source
    Require-LiteralCount $normalized '          $releaseEndpoint = "repos/$repo/releases/$($release.id)"' 1 "numeric-id draft readback binding" $Source
    Require-LiteralCount $normalized '          $preUploadRelease = Invoke-GhApiJson $releaseEndpoint' 1 "pre-upload numeric-id draft readback" $Source
    Require-LiteralCount $normalized '            $finalRelease = Invoke-GhApiJson $releaseEndpoint' 1 "post-upload numeric-id draft readback" $Source
    Require-LiteralCount $normalized "              --verify-tag ``" 1 "existing-tag release creation" $Source
    Require-LiteralCount $normalized "              --draft ``" 1 "draft-only release creation" $Source
    Require-LiteralCount $normalized "          Assert-DraftRelease `$release `$tag `$name" 1 "pre-upload draft assertion" $Source
    Require-LiteralCount $normalized "                `$Release.prerelease -ne `$true)" 1 "draft prerelease state assertion" $Source
    Require-LiteralCount $normalized "          Assert-TagTarget `$preUploadTag `$tag `$sha" 1 "immediate pre-upload tag assertion" $Source
    Require-LiteralCount $normalized "          Assert-DraftRelease `$preUploadRelease `$tag `$name" 1 "immediate pre-upload draft assertion" $Source
    Require-LiteralCount $normalized "            --clobber ``" 1 "scoped testing asset overwrite" $Source
    Require-LiteralCount $normalized "          Assert-DraftRelease `$finalRelease `$tag `$name" 1 "post-upload draft assertion" $Source
    Require-LiteralCount $normalized "Testing draft must contain exactly one verified asset after upload." 1 "post-upload asset-shape assertion" $Source
    Require-LiteralCount $normalized "Uploaded testing asset digest does not match trusted evidence." 1 "post-upload digest assertion" $Source
    Require-LiteralCount $normalized "          if (-not `$digestConfirmed)" 1 "missing remote digest failure" $Source
    Require-LiteralCount $normalized "              Start-Sleep -Seconds 5" 1 "bounded remote digest polling" $Source

    Forbid-Literal $normalized "getLatestRelease" "latest published release dependency" $Source
    Forbid-Literal $normalized "v0.0.1" "legacy shared testing release target" $Source
    Forbid-Literal $normalized "continue-on-error" "failure bypass" $Source
    Forbid-Literal $normalized "self-hosted" "non-ephemeral runner" $Source
    Forbid-Literal $normalized "permissions: write-all" "broad write permissions" $Source
    Forbid-Literal $normalized "actions/github-script@" "obsolete release metadata action" $Source
    Forbid-Literal $normalized 'repos/$repo/releases/tags/$tag' "published-only release-by-tag draft lookup" $Source
    if ($normalized -cmatch '(?m)^\s*uses:\s+[^@\s]+@(?:v|main|master)(?:\s|$)') {
        throw "$Source contains a mutable external action reference."
    }
}

function Assert-TestingReleaseContract([string]$Text, [string]$Source) {
    $normalized = Normalize-Text $Text
    $actualHash = Get-TextSha256 $normalized
    if (-not [string]::Equals(
        $actualHash,
        $expectedWorkflowSha256,
        [System.StringComparison]::Ordinal)) {
        throw "$Source drifted from the reviewed Testing Release workflow (SHA-256 $actualHash)."
    }
    Assert-TestingReleaseSemantics $normalized $Source
}

function Assert-SemanticsRejected(
    [string]$Text,
    [string]$Description,
    [string]$ExpectedErrorFragment
) {
    try {
        # Keep the source label free of the fixture description and invariant
        # text so an unrelated failure cannot satisfy the reason assertion.
        Assert-TestingReleaseSemantics $Text "adversarial semantic fixture"
    }
    catch {
        $message = $_.Exception.Message
        if (-not $message.Contains($ExpectedErrorFragment)) {
            throw "Testing Release semantic fixture '$Description' failed for the wrong reason: $message"
        }
        return
    }
    throw "Testing Release semantic validator accepted adversarial fixture: $Description."
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
        if (-not $message.Contains($ExpectedErrorFragment)) {
            throw "Canonical syntax fixture '$Description' failed for the wrong reason: $message"
        }
        return
    }
    throw "Canonical action validator accepted adversarial fixture: $Description."
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
    [string]$Description,
    [string]$ExpectedErrorFragment
) {
    $fixture = Replace-First $Text $OldValue $NewValue $Description
    Assert-SemanticsRejected $fixture $Description $ExpectedErrorFragment
}

function Test-DownloadedArtifactEvidence(
    [string]$Directory,
    [string]$ExpectedName,
    [string]$ExpectedSha256,
    [long]$ExpectedSize
) {
    if ($ExpectedName -cnotmatch '^LLPlayer-testing-[0-9a-f]{12}-x64\.7z$' -or
        $ExpectedSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $ExpectedSize -le 0) {
        throw "Expected artifact evidence is invalid."
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
        $file.Length -ne $ExpectedSize -or
        -not [string]::Equals(
            $file.Name,
            $ExpectedName,
            [System.StringComparison]::Ordinal)) {
        throw "Artifact entry does not match trusted shape evidence."
    }

    $expectedPath = [System.IO.Path]::GetFullPath((Join-Path $root $ExpectedName))
    $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::Equals(
            $file.FullName,
            $expectedPath,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            $actualHash,
            $ExpectedSha256,
            [System.StringComparison]::Ordinal)) {
        throw "Artifact entry does not match trusted path or digest evidence."
    }
    return $expectedPath
}

function Assert-ArtifactEvidenceRejected(
    [string]$Directory,
    [string]$ExpectedName,
    [string]$ExpectedSha256,
    [long]$ExpectedSize,
    [string]$Description
) {
    try {
        $null = Test-DownloadedArtifactEvidence `
            $Directory `
            $ExpectedName `
            $ExpectedSha256 `
            $ExpectedSize
    }
    catch {
        return
    }
    throw "Downloaded artifact evidence validator accepted $Description."
}

if (-not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) {
    throw "Testing Release workflow is missing: $workflowPath"
}

$workflowText = Normalize-Text (Get-Content -LiteralPath $workflowPath -Raw -Encoding UTF8)
Assert-TestingReleaseContract $workflowText "testing-release.yml"

$commentOnlyFixture = Replace-First `
    $workflowText `
    "name: Testing Release`n" `
    "name: Testing Release`n# semantic no-op fixture`n" `
    "a semantic no-op comment"
Assert-TestingReleaseSemantics $commentOnlyFixture "semantic no-op control fixture"
$hashRejectedComment = $false
try {
    Assert-TestingReleaseContract $commentOnlyFixture "hash-lock control fixture"
}
catch {
    if ($_.Exception.Message.Contains("drifted from the reviewed Testing Release workflow")) {
        $hashRejectedComment = $true
    }
    else {
        throw
    }
}
if (-not $hashRejectedComment) {
    throw "Testing Release hash lock accepted a semantic no-op workflow mutation."
}

$variableDepthBareFixture = "    steps:`n          -`n              name: Variable-depth pinned action`n              `"u\u0073es`": actions/cache@0123456789abcdef0123456789abcdef01234567"
Assert-CanonicalSyntaxRejected `
    $variableDepthBareFixture `
    "a variable-depth bare-step action" `
    "bare step declaration with deep-indented child mappings"
Assert-CanonicalSyntaxRejected `
    "      - { name: Flow action, uses: actions/cache@0123456789abcdef0123456789abcdef01234567 }" `
    "a flow-style action step" `
    "flow, explicit, anchored, aliased, or tagged step declaration"
Assert-CanonicalSyntaxRejected `
    "        &hidden uses: actions/cache@0123456789abcdef0123456789abcdef01234567" `
    "an anchored mapping key" `
    "anchored or aliased workflow key"
Assert-CanonicalSyntaxRejected `
    "        <<: *hidden" `
    "a merged mapping key" `
    "explicit or merged workflow key"

$resolvedAliasActionFixture = Replace-First `
    $workflowText `
    "      - name: Stage trusted release validator" `
    "      - name: &hidden uses`n        shell: pwsh`n        run: Write-Host 'define scalar alias key anchor'`n`n      - name: Stage trusted release validator" `
    "a scalar uses-key anchor defined on an ordinary run step"
$resolvedAliasActionFixture = Replace-First `
    $resolvedAliasActionFixture `
    "    steps:`n      - name: Download verified testing release artifact" `
    "    steps:`n      - name: Unexpected aliased-key pinned action`n        *hidden : actions/cache@0123456789abcdef0123456789abcdef01234567`n        with:`n          path: alias-fixture`n          key: alias-fixture`n`n      - name: Download verified testing release artifact" `
    "the resolved alias key used by an upload-job action"
Assert-SemanticsRejected `
    $resolvedAliasActionFixture `
    "a resolved aliased uses key in the write job" `
    "anchored or aliased workflow key"

$misroutedActionFixture = Replace-First `
    $workflowText `
    "      - name: Stage trusted release validator" `
    "      - name: Misrouted .NET setup`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0`n`n      - name: Stage trusted release validator" `
    "a setup action inserted into prepare"
$misroutedActionFixture = Replace-First `
    $misroutedActionFixture `
    "      - name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0" `
    "      - name: Setup .NET`n        shell: pwsh`n        run: Write-Host 'setup action misrouted'" `
    "the setup action removed from build"
Assert-SemanticsRejected `
    $misroutedActionFixture `
    "an expected action moved to the wrong job" `
    "prepare-job exact uses multiset"

$writeLocalSwapFixture = Replace-First `
    $workflowText `
    "      - name: Build & Package`n        id: package`n        uses: ./.github/actions/build-package" `
    "      - name: Build & Package`n        id: package`n        uses: __SWAPPED_LOCAL_ACTION__" `
    "the build-package action staged for a job swap"
$writeLocalSwapFixture = Replace-First `
    $writeLocalSwapFixture `
    "      - name: Download verified testing release artifact`n        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" `
    "      - name: Download verified testing release artifact`n        uses: ./.github/actions/build-package" `
    "the local action moved into the write job"
$writeLocalSwapFixture = Replace-First `
    $writeLocalSwapFixture `
    "uses: __SWAPPED_LOCAL_ACTION__" `
    "uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" `
    "the download action moved into the build job"
Assert-SemanticsRejected `
    $writeLocalSwapFixture `
    "a global-multiset-preserving local-action job swap" `
    "local action execution in the write job"

$blockScalarActionSpoofFixture = Replace-First `
    $workflowText `
    "      - name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0" `
    "      - name: Setup .NET`n        shell: pwsh`n        run: Write-Host 'real setup action removed'" `
    "the real setup action removed before block-scalar spoofing"
$blockScalarActionSpoofFixture = Replace-First `
    $blockScalarActionSpoofFixture `
    "  build:`n    needs: prepare" `
    "  build:`n    name: |`n      - name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0`n    needs: prepare" `
    "the setup action text moved into a job-name block scalar"
Assert-SemanticsRejected `
    $blockScalarActionSpoofFixture `
    "action-looking text inside a job-level block scalar" `
    "workflow or job block scalar"

$quotedScalarActionBase = Replace-First `
    $workflowText `
    "      - name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0" `
    "      - name: Setup .NET`n        shell: pwsh`n        run: Write-Host 'real setup action removed'" `
    "the real setup action removed before quoted-scalar spoofing"
$doubleQuotedActionSpoofFixture = Replace-First `
    $quotedScalarActionBase `
    "  build:`n    needs: prepare" `
    "  build:`n    name: `"metadata`n      - name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0`n      `"`n    needs: prepare" `
    "the setup action text moved into a multiline double-quoted job name"
Assert-SemanticsRejected `
    $doubleQuotedActionSpoofFixture `
    "action-looking text inside a multiline double-quoted job scalar" `
    "multiline double-quoted workflow or job scalar"
$singleQuotedActionSpoofFixture = Replace-First `
    $quotedScalarActionBase `
    "  build:`n    needs: prepare" `
    "  build:`n    name: 'metadata`n      - name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0`n      '`n    needs: prepare" `
    "the setup action text moved into a multiline single-quoted job name"
Assert-SemanticsRejected `
    $singleQuotedActionSpoofFixture `
    "action-looking text inside a multiline single-quoted job scalar" `
    "multiline single-quoted workflow or job scalar"

Assert-MutationRejected $workflowText `
    "        description: 'Lowercase full 40-character commit SHA'" `
    "        description: 'Build Commit Hash or ref'" `
    "a branch-or-ref input" `
    "immutable lowercase input description"
Assert-MutationRejected $workflowText `
    "  cancel-in-progress: false" `
    "  cancel-in-progress: true" `
    "cancellation of an active release mutation" `
    "non-cancelling release concurrency policy"
Assert-MutationRejected $workflowText `
    "if (`"`$env:REQUESTED_COMMIT`" -cnotmatch '^[0-9a-f]{40}`$')" `
    "if (`"`$env:REQUESTED_COMMIT`" -cnotmatch '^[0-9A-Fa-f]{40}`$')" `
    "acceptance of uppercase commit input" `
    "lowercase full-SHA input guard"
Assert-MutationRejected $workflowText `
    "if (`"`$env:REQUESTED_COMMIT`" -cnotmatch '^[0-9a-f]{40}`$')" `
    "if (`"`$env:REQUESTED_COMMIT`" -cnotmatch '^[0-9a-f]{7,40}`$')" `
    "an abbreviated commit id" `
    "lowercase full-SHA input guard"
Assert-MutationRejected $workflowText `
    '              $expectedRef,' `
    '              "$env:WORKFLOW_REF",' `
    "acceptance of a non-default workflow ref" `
    "workflow ref equality operand"
Assert-MutationRejected $workflowText `
    "                `"`$env:REQUESTED_COMMIT`",`n                `"`$env:CONTROL_COMMIT`"," `
    "                `"`$env:REQUESTED_COMMIT`",`n                `"`$env:REQUESTED_COMMIT`"," `
    "acceptance of a selected commit different from the control head" `
    "requested commit equality operand"
Assert-MutationRejected $workflowText `
    '$tag = "testing-$short"' `
    '$tag = "testing"' `
    "a shared movable testing tag" `
    "commit-scoped testing tag formula"
Assert-MutationRejected $workflowText `
    '$archive = "LLPlayer-testing-$short-x64.7z"' `
    '$archive = "LLPlayer-testing-x64.7z"' `
    "a cross-commit shared asset name" `
    "commit-scoped archive formula"
Assert-MutationRejected $workflowText `
    "          persist-credentials: false" `
    "          persist-credentials: true" `
    "persisted checkout credentials" `
    "credential-free checkout"
Assert-MutationRejected $workflowText `
    "      contents: read" `
    "      contents: write" `
    "write permission before the upload job" `
    "read-only job permission"
Assert-MutationRejected $workflowText `
    "& `$sevenZip t `"`$expectedPath`"" `
    "Write-Host `"Archive test skipped.`"" `
    "a missing 7-Zip integrity test" `
    "7-Zip integrity test"
Assert-MutationRejected $workflowText `
    "          digest-mismatch: error" `
    "          digest-mismatch: warn" `
    "a non-failing artifact digest policy" `
    "fail-closed artifact digest policy"
Assert-MutationRejected $workflowText `
    "              --verify-tag ``" `
    "              --target `"`$sha`" ``" `
    "implicit tag movement during release creation" `
    "existing-tag release creation"
Assert-MutationRejected $workflowText `
    "              --draft ``" `
    "              --latest ``" `
    "a published testing release" `
    "draft-only release creation"
Assert-MutationRejected $workflowText `
    '          $release = Get-GhReleaseByTag $repo $tag -AllowNotFound' `
    '          $release = Invoke-GhApiJson "repos/$repo/releases/tags/$tag" -AllowNotFound' `
    "a published-only REST lookup for an existing draft" `
    "pre-create authenticated draft lookup"
Assert-MutationRejected $workflowText `
    '          $releaseEndpoint = "repos/$repo/releases/$($release.id)"' `
    '          $releaseEndpoint = "repos/$repo/releases/tags/$tag"' `
    "post-create readback through a draft-blind tag endpoint" `
    "numeric-id draft readback binding"
Assert-MutationRejected $workflowText `
    '                    "release not found",' `
    '                    "Not Found",' `
    "broad release not-found classification" `
    "exact release-not-found classification"
Assert-MutationRejected $workflowText `
    '              $ErrorActionPreference = "Continue"' `
    '              $ErrorActionPreference = "Stop"' `
    "terminating native not-found output before classification" `
    "nonterminating native not-found capture"
Assert-MutationRejected $workflowText `
    '                --repo "$Repo" `' `
    '                --repo "Gorgutc/another-repository" `' `
    "draft lookup in a different repository" `
    "exact draft lookup repository binding"
Assert-MutationRejected $workflowText `
    '              $PSNativeCommandUseErrorActionPreference = $nativeErrorPreference' `
    '              $PSNativeCommandUseErrorActionPreference = $null' `
    "a leaked PowerShell native error preference" `
    "PowerShell 7 native exit preference restoration"
Assert-MutationRejected $workflowText `
    '                $releaseId -le 0) {' `
    '                $false) {' `
    "acceptance of a missing or invalid draft release id" `
    "positive numeric draft identity guard"
Assert-MutationRejected $workflowText `
    '              tag_name = "$($view.tagName)"' `
    '              tag_name = $Tag' `
    "replacement of authenticated tag metadata with requested input" `
    "draft tag-name normalization"
Assert-MutationRejected $workflowText `
    '              draft = $view.isDraft' `
    '              draft = $true' `
    "replacement of authenticated draft state with a constant" `
    "draft-state normalization"
Assert-MutationRejected $workflowText `
    '              prerelease = $view.isPrerelease' `
    '              prerelease = $true' `
    "replacement of authenticated prerelease state with a constant" `
    "prerelease-state normalization"
Assert-MutationRejected $workflowText `
    '            $release = Get-GhReleaseByTag $repo $tag' `
    '            $release = $null' `
    "a missing post-create authenticated draft lookup" `
    "post-create authenticated draft lookup"
Assert-MutationRejected $workflowText `
    '          if ($null -eq $release) {' `
    '          if ($true) {' `
    "unconditional replacement of an existing exact draft" `
    "existing-draft recovery branch"
Assert-MutationRejected $workflowText `
    '          $preUploadRelease = Invoke-GhApiJson $releaseEndpoint' `
    '          $preUploadRelease = $release' `
    "a stale pre-upload draft snapshot" `
    "pre-upload numeric-id draft readback"
Assert-MutationRejected $workflowText `
    '            $finalRelease = Invoke-GhApiJson $releaseEndpoint' `
    '            $finalRelease = $release' `
    "a stale post-upload draft snapshot" `
    "post-upload numeric-id draft readback"
Assert-MutationRejected $workflowText `
    "                `$Release.prerelease -ne `$true)" `
    "                `$false)" `
    "a non-prerelease testing target" `
    "draft prerelease state assertion"
Assert-MutationRejected $workflowText `
    "          if (-not `$digestConfirmed)" `
    "          if (`$false)" `
    "acceptance of a missing remote asset digest" `
    "missing remote digest failure"
Assert-MutationRejected $workflowText `
    "            --clobber ``" `
    "            --repo `"`$repo`"" `
    "a missing exact-asset overwrite flag" `
    "scoped testing asset overwrite"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download verified testing release artifact" `
    "    steps:`n      - name: Checkout selected code`n        uses: actions/checkout@v5`n`n      - name: Download verified testing release artifact" `
    "checkout in the write job" `
    "checkout in the write job"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download verified testing release artifact" `
    "    steps:`n      -`n          name: Unexpected deep-indented pinned action`n          `"u\u0073es`": actions/cache@0123456789abcdef0123456789abcdef01234567`n`n      - name: Download verified testing release artifact" `
    "a bare write-job step with a deep-indented escaped uses key" `
    "noncanonical action syntax"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download verified testing release artifact" `
    "    steps:`n      - name: Unexpected anchored-key pinned action`n        &hidden uses: actions/cache@0123456789abcdef0123456789abcdef01234567`n`n      - name: Download verified testing release artifact" `
    "a write-job action encoded with an anchored mapping key" `
    "anchored or aliased workflow key"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download verified testing release artifact" `
    "    steps:`n      - name: Unexpected multiline-explicit pinned action`n        ?`n          uses`n        : actions/cache@0123456789abcdef0123456789abcdef01234567`n`n      - name: Download verified testing release artifact" `
    "a write-job action encoded with a multiline explicit key" `
    "explicit or merged workflow key"
Assert-MutationRejected $workflowText `
    "    steps:`n      - name: Download verified testing release artifact" `
    "    steps:`n      - uses: actions/cache@0123456789abcdef0123456789abcdef01234567`n`n      - name: Download verified testing release artifact" `
    "a write-job action encoded as an inline sequence mapping" `
    "workflow exact uses multiset"
Assert-MutationRejected $workflowText `
    "      - name: Create or update Testing Draft Release" `
    "      - name: Execute selected action`n        uses: ./.github/actions/build-package`n`n      - name: Create or update Testing Draft Release" `
    "local action execution in the write job" `
    "local action execution in the write job"
Assert-MutationRejected $workflowText `
    "      - name: Stage trusted release validator" `
    "      - name: Unexpected fully pinned action`n        uses: actions/cache@0123456789abcdef0123456789abcdef01234567 # adversarial fixture`n`n      - name: Stage trusted release validator" `
    "an additional fully SHA-pinned action" `
    "exact uses multiset"
Assert-MutationRejected $workflowText `
    "      - name: Stage trusted release validator" `
    "      - name: Unexpected double-quoted pinned action`n        `"uses`": actions/cache@0123456789abcdef0123456789abcdef01234567 # adversarial fixture`n`n      - name: Stage trusted release validator" `
    "an additional fully SHA-pinned action with a double-quoted uses key" `
    "quoted or escaped workflow key"
Assert-MutationRejected $workflowText `
    "      - name: Stage trusted release validator" `
    "      - name: Unexpected single-quoted pinned action`n        'uses': actions/cache@0123456789abcdef0123456789abcdef01234567 # adversarial fixture`n`n      - name: Stage trusted release validator" `
    "an additional fully SHA-pinned action with a single-quoted uses key" `
    "quoted or escaped workflow key"
Assert-MutationRejected $workflowText `
    "      - name: Stage trusted release validator" `
    "      - name: Unexpected escaped-key pinned action`n        `"u\u0073es`": actions/cache@0123456789abcdef0123456789abcdef01234567 # adversarial fixture`n`n      - name: Stage trusted release validator" `
    "an additional fully SHA-pinned action with an escaped uses key" `
    "noncanonical action syntax"
Assert-MutationRejected $workflowText `
    "      - name: Stage trusted release validator" `
    "      - name: Unexpected explicit-key pinned action`n        ? uses`n        : actions/cache@0123456789abcdef0123456789abcdef01234567`n`n      - name: Stage trusted release validator" `
    "an additional fully SHA-pinned action with an explicit uses key" `
    "noncanonical action syntax"
Assert-MutationRejected $workflowText `
    "      - name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0" `
    "      - &setup_step`n        name: Setup .NET`n        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0" `
    "an anchored expected action step" `
    "noncanonical action syntax"
Assert-MutationRejected $workflowText `
    "      - name: Build & Package`n        id: package`n        uses: ./.github/actions/build-package" `
    "      - name: Misnamed package action`n        id: package`n        uses: ./.github/actions/build-package" `
    "an expected action routed through the wrong named step" `
    "packaging action step routing"
Assert-MutationRejected $workflowText `
    "      - name: Stage trusted release validator" `
    "      - { name: Unexpected flow-style pinned action, uses: actions/cache@0123456789abcdef0123456789abcdef01234567 }`n`n      - name: Stage trusted release validator" `
    "an additional fully SHA-pinned flow-style action" `
    "flow, explicit, anchored, aliased, or tagged step declaration"
Assert-MutationRejected $workflowText `
    "        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1" `
    "        uses: actions/download-artifact@v8" `
    "a mutable external action" `
    "immutable artifact download action"

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixtureRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $tempRoot ("llplayer-testing-boundary-" + [guid]::NewGuid().ToString("N"))))
if (-not $fixtureRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Artifact fixture path escaped the system temporary directory."
}

$expectedArchive = "LLPlayer-testing-deadbeef1234-x64.7z"
try {
    $positive = Join-Path $fixtureRoot "positive"
    $null = New-Item -ItemType Directory -Path $positive -Force
    $bytes = [byte[]](1, 2, 3)
    $positiveFile = Join-Path $positive $expectedArchive
    [System.IO.File]::WriteAllBytes($positiveFile, $bytes)
    $positiveHash = (Get-FileHash -LiteralPath $positiveFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $positivePath = Test-DownloadedArtifactEvidence `
        $positive `
        $expectedArchive `
        $positiveHash `
        $bytes.Length
    if (-not [string]::Equals(
        $positivePath,
        [System.IO.Path]::GetFullPath($positiveFile),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Downloaded artifact evidence validator returned an unexpected positive path."
    }

    Assert-ArtifactEvidenceRejected `
        $positive `
        $expectedArchive `
        ("0" * 64) `
        $bytes.Length `
        "a mismatched SHA-256"
    Assert-ArtifactEvidenceRejected `
        $positive `
        $expectedArchive `
        $positiveHash `
        ($bytes.Length + 1) `
        "a mismatched size"
    Assert-ArtifactEvidenceRejected `
        $positive `
        "LLPlayer-testing.7z" `
        $positiveHash `
        $bytes.Length `
        "a shared unsafe asset name"

    $extra = Join-Path $fixtureRoot "extra"
    $null = New-Item -ItemType Directory -Path $extra -Force
    [System.IO.File]::WriteAllBytes((Join-Path $extra $expectedArchive), $bytes)
    [System.IO.File]::WriteAllBytes((Join-Path $extra "extra.txt"), [byte[]](1))
    Assert-ArtifactEvidenceRejected `
        $extra `
        $expectedArchive `
        $positiveHash `
        $bytes.Length `
        "an artifact with an extra direct entry"

    $empty = Join-Path $fixtureRoot "empty"
    $null = New-Item -ItemType Directory -Path $empty -Force
    [System.IO.File]::WriteAllBytes((Join-Path $empty $expectedArchive), [byte[]]@())
    Assert-ArtifactEvidenceRejected `
        $empty `
        $expectedArchive `
        $positiveHash `
        $bytes.Length `
        "an empty archive"
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Host "Testing Release bootstrap/write-token boundary verification completed."

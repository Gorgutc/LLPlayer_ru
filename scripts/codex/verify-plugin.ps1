$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Runtime.Serialization

function Assert-UniqueJsonObjectKeys {
    param(
        [Parameter(Mandatory = $true)][string]$Json,
        [Parameter(Mandatory = $true)][string]$Source
    )

    $reader = $null
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Json)
        $reader = [System.Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader(
            $bytes,
            [System.Xml.XmlDictionaryReaderQuotas]::Max)
        $document = New-Object System.Xml.XmlDocument
        $document.Load($reader)
    }
    catch {
        throw "$Source is not valid JSON: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $reader) {
            $reader.Close()
        }
    }

    foreach ($objectNode in @($document.SelectNodes("//*[@type='object']"))) {
        $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($child in @($objectNode.ChildNodes)) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) {
                continue
            }

            $name = $child.LocalName
            if ($child.NamespaceURI -eq "item" -and $null -ne $child.Attributes["item"]) {
                $name = $child.Attributes["item"].Value
            }
            if (-not $seen.Add($name)) {
                throw "$Source has duplicate JSON property '$name'."
            }
        }
    }
}

function Assert-ExactObjectShape {
    param(
        $Value,
        [Parameter(Mandatory = $true)][string[]]$AllowedProperties,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$RequiredProperties,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($null -eq $Value -or $Value -isnot [System.Management.Automation.PSCustomObject]) {
        throw "$Context must be a JSON object."
    }

    $properties = @($Value.PSObject.Properties)
    foreach ($property in $properties) {
        if ($AllowedProperties -cnotcontains $property.Name) {
            throw "$Context has unknown property '$($property.Name)'."
        }
    }
    foreach ($required in $RequiredProperties) {
        if (@($properties.Name) -cnotcontains $required) {
            throw "$Context is missing required property '$required'."
        }
    }
}

function Get-CanonicalWindowsHookTarget {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ([string]::IsNullOrWhiteSpace($Command)) {
        throw "$Context commandWindows must be a non-empty string."
    }

    # Codex runs commandWindows through %COMSPEC% /C before PowerShell sees it.
    foreach ($codePoint in @(10, 13, 33, 35, 37, 38, 39, 40, 41, 59, 60, 62, 94, 96, 124)) {
        if ($Command.IndexOf([char]$codePoint) -ge 0) {
            throw "$Context commandWindows contains a forbidden CMD or shell metacharacter."
        }
    }
    foreach ($character in $Command.ToCharArray()) {
        if ([char]::IsWhiteSpace($character) -and
            $character -ne [char]0x20 -and
            $character -ne [char]0x09) {
            throw "$Context commandWindows contains whitespace that the Windows CLI does not treat as an argument separator."
        }
    }

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput(
        $Command,
        [ref]$tokens,
        [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "$Context commandWindows has PowerShell parse errors."
    }

    $statements = @($ast.EndBlock.Statements)
    if ($statements.Count -ne 1 -or
        $statements[0] -isnot [System.Management.Automation.Language.PipelineAst]) {
        throw "$Context commandWindows must use the canonical single-command form."
    }

    $pipeline = $statements[0]
    $pipelineElements = @($pipeline.PipelineElements)
    if ($pipelineElements.Count -ne 1 -or
        $pipelineElements[0] -isnot [System.Management.Automation.Language.CommandAst]) {
        throw "$Context commandWindows must use the canonical single-command form."
    }

    $commandAst = $pipelineElements[0]
    $allCommands = @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))
    if ($allCommands.Count -ne 1 -or
        $commandAst.Redirections.Count -ne 0 -or
        $commandAst.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Unknown) {
        throw "$Context commandWindows must use the canonical single-command form."
    }

    $elements = @($commandAst.CommandElements)
    if ($elements.Count -ne 6 -or
        $elements[0] -isnot [System.Management.Automation.Language.StringConstantExpressionAst] -or
        $elements[0].Value -ine "powershell.exe" -or
        $elements[1] -isnot [System.Management.Automation.Language.CommandParameterAst] -or
        $elements[1].Extent.Text -ine "-NoProfile" -or
        $elements[1].ParameterName -ine "NoProfile" -or
        $null -ne $elements[1].Argument -or
        $elements[2] -isnot [System.Management.Automation.Language.CommandParameterAst] -or
        $elements[2].Extent.Text -ine "-ExecutionPolicy" -or
        $elements[2].ParameterName -ine "ExecutionPolicy" -or
        $null -ne $elements[2].Argument -or
        $elements[3] -isnot [System.Management.Automation.Language.StringConstantExpressionAst] -or
        $elements[3].Value -ine "Bypass" -or
        $elements[4] -isnot [System.Management.Automation.Language.CommandParameterAst] -or
        $elements[4].Extent.Text -ine "-File" -or
        $elements[4].ParameterName -ine "File" -or
        $null -ne $elements[4].Argument -or
        $elements[5] -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) {
        throw "$Context commandWindows must be exactly 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File <literal>'."
    }

    $shadowLauncherPath = Join-Path $RepositoryRoot "powershell.exe"
    if (Test-Path -LiteralPath $shadowLauncherPath) {
        throw "$Context repository root contains a local powershell.exe that would shadow the system launcher."
    }

    $target = $elements[5].Value
    if (-not ($target.StartsWith(".\") -or $target.StartsWith("./"))) {
        throw "$Context -File target must be explicitly repository-relative."
    }
    if ([System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($target)) {
        throw "$Context -File target must not contain wildcard characters."
    }
    if ([System.IO.Path]::GetExtension($target) -ine ".ps1") {
        throw "$Context -File target must have the .ps1 extension."
    }

    $relativePart = $target.Substring(2)
    if ($relativePart.Contains(":")) {
        throw "$Context -File target must not use a provider or drive-qualified path."
    }
    foreach ($segment in @($relativePart -split '[\\/]')) {
        if ([string]::IsNullOrEmpty($segment) -or $segment -eq "." -or $segment -eq "..") {
            throw "$Context -File target must not contain empty or parent-traversal segments."
        }
    }

    $rootFull = [System.IO.Path]::GetFullPath($RepositoryRoot)
    while ($rootFull.Length -gt 3 -and ($rootFull.EndsWith("\") -or $rootFull.EndsWith("/"))) {
        $rootFull = $rootFull.Substring(0, $rootFull.Length - 1)
    }
    $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $rootFull $target))
    if (-not $candidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Context -File target must stay inside the repository."
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        if (Test-Path -LiteralPath $candidate) {
            throw "$Context -File target must resolve to a regular file."
        }
        throw "$Context -File target does not exist: $target"
    }

    $resolved = @(Resolve-Path -LiteralPath $candidate -ErrorAction Stop)
    if ($resolved.Count -ne 1 -or $resolved[0].Provider.Name -ne "FileSystem") {
        throw "$Context -File target must resolve once through the FileSystem provider."
    }
    $resolvedPath = [System.IO.Path]::GetFullPath($resolved[0].ProviderPath)
    if (-not $resolvedPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Context -File target must stay inside the repository after resolution."
    }

    $current = $resolvedPath
    while (-not [string]::Equals($current, $rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        if (-not $current.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$Context -File target must stay inside the repository."
        }
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Context -File target must stay inside the repository and cannot traverse reparse points."
        }
        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $current) {
            throw "$Context -File target could not be resolved unambiguously."
        }
        $current = $parent
    }

    return $resolvedPath
}

function Assert-WindowsHookConfiguration {
    param(
        [Parameter(Mandatory = $true)][string]$Json,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Source
    )

    Assert-UniqueJsonObjectKeys $Json $Source
    try {
        $document = $Json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "$Source is not valid JSON: $($_.Exception.Message)"
    }

    Assert-ExactObjectShape $document @("description", "hooks") @("hooks") "$Source root"
    if ($null -ne $document.description -and $document.description -isnot [string]) {
        throw "$Source description must be a string."
    }

    Assert-ExactObjectShape $document.hooks @(
        "PreToolUse",
        "PermissionRequest",
        "PostToolUse",
        "PreCompact",
        "PostCompact",
        "SessionStart",
        "SessionEnd",
        "UserPromptSubmit",
        "SubagentStart",
        "SubagentStop",
        "Stop"
    ) @() "$Source hooks"

    $events = @($document.hooks.PSObject.Properties)
    if ($events.Count -eq 0) {
        throw "$Source hooks must contain at least one event."
    }

    $handlerCount = 0
    $targets = New-Object System.Collections.Generic.List[string]
    foreach ($event in $events) {
        if ($event.Value -isnot [System.Array] -or @($event.Value).Count -eq 0) {
            throw "$Source event '$($event.Name)' must be a non-empty array of matcher groups."
        }
        $groupIndex = 0
        foreach ($group in @($event.Value)) {
            $groupIndex++
            $groupContext = "$Source event '$($event.Name)' matcher group $groupIndex"
            Assert-ExactObjectShape $group @("matcher", "hooks") @("hooks") $groupContext
            if ($null -ne $group.matcher -and $group.matcher -isnot [string]) {
                throw "$groupContext matcher must be a string."
            }
            if ($group.hooks -isnot [System.Array] -or @($group.hooks).Count -eq 0) {
                throw "$groupContext hooks must be a non-empty array."
            }

            $handlerIndex = 0
            foreach ($handler in @($group.hooks)) {
                $handlerIndex++
                $handlerContext = "$groupContext handler $handlerIndex"
                Assert-ExactObjectShape $handler @(
                    "type",
                    "command",
                    "commandWindows",
                    "timeout",
                    "async",
                    "statusMessage"
                ) @("type", "command", "commandWindows") $handlerContext
                if ($handler.type -cne "command") {
                    throw "$handlerContext type must be 'command'."
                }
                if ($handler.command -isnot [string] -or [string]::IsNullOrWhiteSpace($handler.command)) {
                    throw "$handlerContext command must be a non-empty string."
                }
                if ($handler.commandWindows -isnot [string]) {
                    throw "$handlerContext commandWindows must be a string."
                }
                $handlerPropertyNames = @($handler.PSObject.Properties.Name)
                if ($handlerPropertyNames -ccontains "timeout") {
                    if (($handler.timeout -isnot [int] -and $handler.timeout -isnot [long]) -or $handler.timeout -le 0) {
                        throw "$handlerContext timeout must be a positive integer."
                    }
                }
                if ($handlerPropertyNames -ccontains "async") {
                    if ($handler.'async' -isnot [bool]) {
                        throw "$handlerContext async must be a Boolean."
                    }
                    if ($handler.'async') {
                        throw "$handlerContext async hooks are not allowed because Codex skips them."
                    }
                }
                if ($handlerPropertyNames -ccontains "statusMessage" -and $handler.statusMessage -isnot [string]) {
                    throw "$handlerContext statusMessage must be a string."
                }

                $target = Get-CanonicalWindowsHookTarget $handler.commandWindows $RepositoryRoot $handlerContext
                $targets.Add($target)
                $handlerCount++
            }
        }
    }

    if ($handlerCount -eq 0) {
        throw "$Source must contain at least one Windows command hook."
    }

    return [pscustomobject]@{
        HandlerCount = $handlerCount
        Targets = @($targets)
    }
}

function New-HookTestJson {
    param([Parameter(Mandatory = $true)][string[]]$Commands)

    $handlers = @()
    foreach ($command in $Commands) {
        $handlers += ,([ordered]@{
            type = "command"
            command = $command
            commandWindows = $command
        })
    }
    return ([ordered]@{
        hooks = [ordered]@{
            SessionStart = @(
                [ordered]@{
                    hooks = @($handlers)
                }
            )
        }
    } | ConvertTo-Json -Depth 8 -Compress)
}

function Assert-HookFixtureRejected {
    param(
        [Parameter(Mandatory = $true)][string]$Json,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage,
        [Parameter(Mandatory = $true)][string]$Description
    )

    try {
        $null = Assert-WindowsHookConfiguration $Json $RepositoryRoot "fixture $Description"
    }
    catch {
        if ($_.Exception.Message.IndexOf($ExpectedMessage, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "Hook fixture '$Description' failed for an unexpected reason: $($_.Exception.Message)"
        }
        return
    }
    throw "Hook validator accepted adversarial fixture: $Description."
}

function Invoke-HookValidatorSelfTests {
    $fixtureBase = Join-Path ([System.IO.Path]::GetTempPath()) ("llplayer-hook-targets-" + [guid]::NewGuid().ToString("N"))
    $fixtureRoot = Join-Path $fixtureBase "repo"
    $escapeRoot = Join-Path $fixtureBase "repo-escape"
    $junctionPath = Join-Path $fixtureRoot "linked"
    try {
        $hooksDirectory = Join-Path $fixtureRoot "hooks"
        $outsideDirectory = Join-Path $escapeRoot "hooks"
        $null = New-Item -ItemType Directory -Path $hooksDirectory -Force
        $null = New-Item -ItemType Directory -Path $outsideDirectory -Force
        [System.IO.File]::WriteAllText((Join-Path $hooksDirectory "valid.ps1"), "")
        [System.IO.File]::WriteAllText((Join-Path $hooksDirectory "path with spaces.ps1"), "")
        $null = New-Item -ItemType Directory -Path (Join-Path $hooksDirectory "directory.ps1") -Force
        [System.IO.File]::WriteAllText((Join-Path $outsideDirectory "outside.ps1"), "")

        $validCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\valid.ps1'
        $quotedCommand = 'powershell.exe -noprofile -executionpolicy bypass -FILE ".\hooks\path with spaces.ps1"'
        $null = Assert-WindowsHookConfiguration (New-HookTestJson @($validCommand, $quotedCommand)) $fixtureRoot "positive hook fixture"

        Assert-HookFixtureRejected '{"hooks":' $fixtureRoot "not valid JSON" "malformed JSON"
        Assert-HookFixtureRejected '{"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"x","commandWindows":"x","commandWindows":"y"}]}]}}' $fixtureRoot "duplicate JSON property" "duplicate commandWindows"
        Assert-HookFixtureRejected '{"hooks":{}}' $fixtureRoot "at least one event" "empty hooks object"
        Assert-HookFixtureRejected '{"hooks":{"UnknownEvent":[]}}' $fixtureRoot "unknown property" "unknown event"
        Assert-HookFixtureRejected '{"hooks":{"SessionStart":[{"commandWindows":"powershell -File .\\hooks\\valid.ps1"}]}}' $fixtureRoot "unknown property" "flat legacy hook shape"
        Assert-HookFixtureRejected '{"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"powershell -File .\\hooks\\valid.ps1"}]}]}}' $fixtureRoot "missing required property 'commandWindows'" "missing commandWindows"
        Assert-HookFixtureRejected (New-HookTestJson @($validCommand, 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\missing.ps1')) $fixtureRoot "does not exist" "later invalid handler"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell -NoProfile -ExecutionPolicy Bypass -File .\hooks\valid.ps1')) $fixtureRoot "exactly" "extensionless launcher"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass .\hooks\valid.ps1')) $fixtureRoot "exactly" "missing File parameter"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\valid.ps1 -File .\hooks\valid.ps1')) $fixtureRoot "exactly" "duplicate File parameter"
        $unicodeDash = [char]0x2013
        $unicodeDashCommand = "powershell.exe ${unicodeDash}NoProfile ${unicodeDash}ExecutionPolicy Bypass ${unicodeDash}File .\hooks\valid.ps1"
        Assert-HookFixtureRejected (New-HookTestJson @($unicodeDashCommand)) $fixtureRoot "exactly" "Unicode dash switches"
        $nonBreakingSpace = [char]0x00A0
        $unicodeWhitespaceCommand = "powershell.exe${nonBreakingSpace}-NoProfile${nonBreakingSpace}-ExecutionPolicy${nonBreakingSpace}Bypass${nonBreakingSpace}-File${nonBreakingSpace}.\hooks\valid.ps1"
        Assert-HookFixtureRejected (New-HookTestJson @($unicodeWhitespaceCommand)) $fixtureRoot "whitespace" "Unicode argument separators"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -Command Write-Host -File .\hooks\valid.ps1')) $fixtureRoot "exactly" "Command mode with decoy File"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\hook.ps1"')) $fixtureRoot "exactly" "dynamic target"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\valid.ps1 | Write-Host')) $fixtureRoot "metacharacter" "pipeline"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\valid.ps1 # trailing CMD argument')) $fixtureRoot "metacharacter" "PowerShell comment interpreted as CMD arguments"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File %CD%\hooks\valid.ps1')) $fixtureRoot "metacharacter" "CMD expansion"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\missing.ps1')) $fixtureRoot "does not exist" "missing target"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\directory.ps1')) $fixtureRoot "regular file" "directory target"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\..\repo-escape\hooks\outside.ps1')) $fixtureRoot "parent-traversal" "repository escape"
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hooks\*.ps1')) $fixtureRoot "wildcard" "wildcard target"

        $null = New-Item -ItemType Junction -Path $junctionPath -Target $outsideDirectory -ErrorAction Stop
        Assert-HookFixtureRejected (New-HookTestJson @('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\linked\outside.ps1')) $fixtureRoot "repository" "junction escape"

        $shadowLauncherPath = Join-Path $fixtureRoot "powershell.exe"
        [System.IO.File]::WriteAllText($shadowLauncherPath, "")
        Assert-HookFixtureRejected (New-HookTestJson @($validCommand)) $fixtureRoot "shadow" "repository-local launcher shadow"
        [System.IO.File]::Delete($shadowLauncherPath)
    }
    finally {
        if (Test-Path -LiteralPath $junctionPath) {
            [System.IO.Directory]::Delete($junctionPath, $false)
        }
        if (Test-Path -LiteralPath $junctionPath) {
            throw "Hook validator fixture junction could not be removed safely: $junctionPath"
        }
        if (Test-Path -LiteralPath $fixtureBase) {
            Remove-Item -LiteralPath $fixtureBase -Recurse -Force
        }
        if (Test-Path -LiteralPath $fixtureBase) {
            throw "Hook validator fixture directory could not be removed safely: $fixtureBase"
        }
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $failures = New-Object System.Collections.Generic.List[string]

    function Require-Path($Path, $Message) {
        if (-not (Test-Path $Path)) {
            $failures.Add($Message)
        }
    }

    Require-Path ".\AGENTS.md" "AGENTS.md is required."
    Require-Path ".\Plugins\llplayer-codex\.codex-plugin\plugin.json" "LLPlayer Codex plugin manifest is required."
    Require-Path ".\.agents\plugins\marketplace.json" "Marketplace manifest is required."
    Require-Path ".\.codex\hooks.json" ".codex/hooks.json is required."
    Require-Path ".\.codex\config.toml" ".codex/config.toml is required."

    if (Test-Path -LiteralPath ".\.codex\hooks.json" -PathType Leaf) {
        try {
            $hooksJson = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath ".\.codex\hooks.json").ProviderPath)
            $hookResult = Assert-WindowsHookConfiguration $hooksJson $repoRoot.ProviderPath ".codex/hooks.json"
            Write-Host "Validated $($hookResult.HandlerCount) Windows hook target(s)."
        }
        catch {
            $failures.Add($_.Exception.Message)
        }
    }

    try {
        Invoke-HookValidatorSelfTests
    }
    catch {
        $failures.Add("Hook validator self-tests failed: $($_.Exception.Message) $($_.ScriptStackTrace)")
    }

    if (Test-Path ".\.codex\config.toml") {
        $allowedDirectAgentKeys = @(
            "max_threads",
            "max_depth",
            "job_max_runtime_seconds",
            "interrupt_message"
        )
        $invalidDirectAgentKeys = New-Object System.Collections.Generic.List[string]
        $inAgentsTable = $false
        $lineNumber = 0
        foreach ($line in Get-Content ".\.codex\config.toml") {
            $lineNumber++
            if ($line -match '^\s*\[[^\]]+\]\s*$') {
                $inAgentsTable = $line -match '^\s*\[agents\]\s*$'
                continue
            }
            if (-not $inAgentsTable) {
                continue
            }
            if ($line -match '^\s*([A-Za-z0-9_-]+)\s*=') {
                $key = $Matches[1]
                if ($key -notin $allowedDirectAgentKeys) {
                    $invalidDirectAgentKeys.Add("line $lineNumber agents.$key")
                }
            }
        }
        if ($invalidDirectAgentKeys.Count -gt 0) {
            $failures.Add(".codex/config.toml has invalid direct [agents] values: $($invalidDirectAgentKeys -join ', '). [agents] is reserved for Codex agent settings; move custom metadata outside this table or define a role table.")
        }
    }

    $plugin = Get-Content ".\Plugins\llplayer-codex\.codex-plugin\plugin.json" -Raw | ConvertFrom-Json
    if ($plugin.name -ne "llplayer-codex") {
        $failures.Add("plugin.json name must be llplayer-codex.")
    }
    if ($plugin.skills -ne "./skills/") {
        $failures.Add("plugin.json skills must point to ./skills/.")
    }

    $marketplace = Get-Content ".\.agents\plugins\marketplace.json" -Raw | ConvertFrom-Json
    $entry = @($marketplace.plugins | Where-Object { $_.name -eq "llplayer-codex" })[0]
    if (-not $entry) {
        $failures.Add("Marketplace is missing llplayer-codex entry.")
    } elseif ($entry.source.path -ne "./Plugins/llplayer-codex") {
        $failures.Add("Marketplace source path must be ./Plugins/llplayer-codex on this Windows repository.")
    } elseif (-not (Test-Path $entry.source.path)) {
        $failures.Add("Marketplace source path does not resolve to the plugin directory.")
    }

    $requiredSkills = @(
        "llplayer-bootstrap",
        "llplayer-rules",
        "llplayer-product-contract",
        "llplayer-dotnet-rules",
        "llplayer-context-keeper",
        "llplayer-spec-guardian",
        "llplayer-frozen-decisions",
        "llplayer-quality-gate",
        "llplayer-quality-tooling",
        "llplayer-deadwood-reuse-audit",
        "llplayer-instruction-drift",
        "llplayer-ship",
        "llplayer-runtime-assets",
        "llplayer-packaging-release",
        "llplayer-wpf-xaml-review"
    )

    foreach ($skill in $requiredSkills) {
        $skillPath = ".\Plugins\llplayer-codex\skills\$skill\SKILL.md"
        Require-Path $skillPath "Missing skill $skillPath."
        if (Test-Path $skillPath) {
            $text = Get-Content $skillPath -Raw
            if ($text -notmatch "(?s)^---\s*name:\s*$skill\s*description:\s*Use when .+?---") {
                $failures.Add("Skill $skill must have frontmatter with matching name and Use when description.")
            }
        }
    }

    $requiredAgents = @(
        "tech_stack_cartographer",
        "media_runtime_mapper",
        "wpf_xaml_reviewer",
        "dotnet_quality_guardian",
        "native_dependency_auditor",
        "packaging_release_reviewer",
        "instruction_drift_auditor",
        "codex_infra_architect",
        "verification_reviewer",
        "deadwood_reuse_auditor"
    )
    foreach ($agent in $requiredAgents) {
        $agentPath = ".\.codex\agents\$agent.toml"
        Require-Path $agentPath "Missing agent .codex/agents/$agent.toml."
        if (Test-Path $agentPath) {
            $agentText = Get-Content $agentPath -Raw
            if ($agentText -notmatch 'sandbox_mode\s*=\s*"read-only"') {
                $failures.Add("Agent .codex/agents/$agent.toml must use sandbox_mode = `"read-only`".")
            }
        }
    }

    $requiredDocs = @(
        "README",
        "bootstrap",
        "architecture",
        "technical-stack",
        "orchestration",
        "verification",
        "quality-tooling",
        "code_review",
        "archive_policy",
        "plan_template",
        "frozen-decisions",
        "skill-map",
        "migration-from-source-repos",
        "product-behavior-contract",
        "wpf-design-contract",
        "media-runtime-contract",
        "config-data-contract",
        "dependency-baseline",
        "dubbing-contract",
        "manual-smoke-matrix",
        "subagent-review-matrix"
    )
    foreach ($doc in $requiredDocs) {
        Require-Path ".\docs\agent\$doc.md" "Missing docs/agent/$doc.md."
    }

    $requiredScripts = @(
        "check-environment",
        "verify-fast",
        "verify",
        "verify-plugin",
        "verify-doc-coverage",
        "verify-frozen",
        "verify-build-workflow",
        "verify-release-workflow",
        "verify-testing-release-boundary",
        "validate-release-token",
        "audit-frozen",
        "ship"
    )
    foreach ($script in $requiredScripts) {
        Require-Path ".\scripts\codex\$script.ps1" "Missing scripts/codex/$script.ps1."
    }

    foreach ($pointer in @("CLAUDE.md", "GEMINI.md")) {
        if ((Test-Path $pointer) -and ((Get-Content $pointer -Raw) -notmatch "AGENTS\.md")) {
            $failures.Add("$pointer must point to AGENTS.md.")
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Error $failure
        }
        exit 1
    }

    Write-Host "LLPlayer Codex plugin verification completed."
}
finally {
    Pop-Location
}

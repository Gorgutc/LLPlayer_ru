param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'LLPlayer-Avalonia-TestBuild'),
    [string]$DesktopPath = [Environment]::GetFolderPath('DesktopDirectory'),
    [string]$DotNetRoot,
    [string]$IconPath = (Join-Path $PSScriptRoot 'assets/llplayer-avalonia-blue.ico'),
    [string]$PackageDir
)
# Local testing only: publish/validate, retain immutable payloads, then atomically select one.
# Never starts a player, stops a process, installs a runtime or changes the WPF shortcut.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Desktop test builds require Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ownerTag = 'LLPlayer.Avalonia.DesktopTest'
$utf8 = New-Object Text.UTF8Encoding($false)
function Get-NormalPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A local path is required.' }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^[A-Za-z]:\\') { throw "A local drive path is required: $full" }
    $drive = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $drive.Length) { $full = $full.TrimEnd('\','/') }
    $ancestor = $full
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Desktop test build paths must not contain reparse points: $ancestor"
            }
        }
        $parent = [IO.Directory]::GetParent($ancestor)
        $ancestor = if ($parent) { $parent.FullName } else { $null }
    }
    return $full
}
function Test-Within([string]$Parent, [string]$Child) {
    return $Child.StartsWith($Parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}
function Get-Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Atomic([string]$Path, [byte[]]$Bytes) {
    $Path = Get-NormalPath $Path
    if (-not (Test-Within $InstallRoot $Path)) { throw 'Atomic writes must stay inside the managed install root.' }
    $temporary = Get-NormalPath (Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.desktop-' + [guid]::NewGuid().ToString('N') + '.tmp'))
    [IO.File]::WriteAllBytes($temporary, $Bytes)
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [System.Management.Automation.Language.NullString]::Value) }
    else { [IO.File]::Move($temporary, $Path) }
}
function Read-Json([string]$Path) { return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
function Get-CleanCommit {
    $commit = [string](& git -C $repoRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve the source commit.' }
    $dirty = @(& git -C $repoRoot status --porcelain=v1)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Commit the reviewed task before refreshing the desktop build; the source tree must be clean.' }
    return $commit
}
function Resolve-DotNet {
    $candidates = if ($DotNetRoot) { @($DotNetRoot) } else {
        @($env:DOTNET_ROOT_X64, $env:DOTNET_ROOT, (Join-Path $env:ProgramFiles 'dotnet'))
    }
    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        $root = Get-NormalPath $candidate
        if (-not (Test-Path -LiteralPath (Join-Path $root 'dotnet.exe') -PathType Leaf)) { continue }
        $shared = Join-Path $root 'shared/Microsoft.NETCore.App'
        if (-not (Test-Path -LiteralPath $shared -PathType Container)) { continue }
        $versions = @(Get-ChildItem -LiteralPath $shared -Directory | Where-Object { $_.Name -match '^10\.[0-9]+\.[0-9]+$' } |
            Sort-Object { [version]$_.Name } -Descending)
        foreach ($version in $versions) {
            $core = Get-NormalPath (Join-Path $version.FullName 'coreclr.dll')
            $fxr = Get-NormalPath (Join-Path $root ('host/fxr/' + $version.Name + '/hostfxr.dll'))
            if ((Test-Path -LiteralPath $core -PathType Leaf) -and (Test-Path -LiteralPath $fxr -PathType Leaf)) {
                $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($core))
                try {
                    if ($reader.BaseStream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { continue }
                    $reader.BaseStream.Position = 0x3c
                    $pe = $reader.ReadInt32()
                    if ($pe -lt 0 -or $pe + 6 -gt $reader.BaseStream.Length) { continue }
                    $reader.BaseStream.Position = $pe
                    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { continue }
                    return [pscustomobject]@{ root=$root; version=$version.Name }
                }
                finally { $reader.Dispose() }
            }
        }
    }
    throw 'An existing x64 .NET 10 runtime is required. Pass -DotNetRoot or set process DOTNET_ROOT; no runtime is installed automatically.'
}
$launcher = @'
# User-clicked launcher; the updater and its tests never invoke this file.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
try {
    $root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    function Checked-Path([string]$Path) {
        $full = [IO.Path]::GetFullPath($Path)
        if ($full -notmatch '^[A-Za-z]:\\') { throw 'The test build requires local drive paths.' }
        $ancestor = $full
        while ($ancestor) {
            if ((Test-Path -LiteralPath $ancestor) -and (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
                throw 'The test build contains a reparse point.'
            }
            $parent = [IO.Directory]::GetParent($ancestor)
            $ancestor = if ($parent) { $parent.FullName } else { $null }
        }
        return $full
    }
    $statePath = Checked-Path (Join-Path $root 'current.json')
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.owner -ne 'LLPlayer.Avalonia.DesktopTest' -or $state.schema -ne 1 -or $state.buildId -notmatch '^[A-Za-z0-9._-]+$' -or $state.buildId -in @('.','..')) { throw 'The desktop build pointer is invalid.' }
    $build = Checked-Path (Join-Path $root ('builds/' + $state.buildId))
    $receiptPath = Checked-Path (Join-Path $build 'receipt.json')
    if ($receiptPath -ine [string]$state.receiptPath -or (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $state.receiptSha256) { throw 'The desktop build receipt changed.' }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $payload = Checked-Path (Join-Path $build 'payload')
    $exe = Checked-Path (Join-Path $payload 'LLPlayer.Avalonia.exe')
    if ($receipt.owner -ne $state.owner -or $receipt.schema -ne 1 -or $receipt.payloadPath -ine $payload -or $receipt.executablePath -ine $exe -or $receipt.sourceCommit -cne $state.sourceCommit) { throw 'The desktop build identity is invalid.' }
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() -cne $receipt.executableSha256) { throw 'The desktop executable changed.' }
    $runtime = Checked-Path ([string]$receipt.dotNetRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $runtime ('shared/Microsoft.NETCore.App/' + $receipt.runtimeVersion + '/coreclr.dll')) -PathType Leaf)) { throw 'The selected .NET 10 runtime is unavailable; refresh the desktop test build.' }
    $env:DOTNET_ROOT = $runtime
    $env:DOTNET_ROOT_X64 = $runtime
    Start-Process -FilePath $exe -WorkingDirectory $payload -WindowStyle Normal | Out-Null
}
catch {
    $failure = $_
    try {
        $log = Checked-Path (Join-Path $PSScriptRoot 'launch-error.txt')
        [IO.File]::WriteAllText($log, $failure.Exception.ToString(), (New-Object Text.UTF8Encoding($false)))
    }
    catch { } # Logging must not replace the original launch failure or follow a child link.
    throw $failure
}
'@
$InstallRoot = Get-NormalPath $InstallRoot
$DesktopPath = Get-NormalPath $DesktopPath
$IconPath = Get-NormalPath $IconPath
if (-not (Test-Path -LiteralPath $DesktopPath -PathType Container)) { throw 'DesktopPath must be an existing directory.' }
if ($InstallRoot -eq $DesktopPath -or (Test-Within $InstallRoot $DesktopPath) -or (Test-Within $DesktopPath $InstallRoot)) { throw 'InstallRoot and DesktopPath must not overlap.' }
if (-not (Test-Path -LiteralPath $IconPath -PathType Leaf)) { throw "The blue desktop icon is missing: $IconPath" }
$iconBytes = [IO.File]::ReadAllBytes($IconPath)
if ($iconBytes.Length -lt 22 -or [BitConverter]::ToUInt16($iconBytes,0) -ne 0 -or [BitConverter]::ToUInt16($iconBytes,2) -ne 1 -or [BitConverter]::ToUInt16($iconBytes,4) -eq 0) { throw 'IconPath must contain a Windows ICO image.' }
$runtime = Resolve-DotNet
$ownerPath = Get-NormalPath (Join-Path $InstallRoot 'owner.json')
$currentPath = Get-NormalPath (Join-Path $InstallRoot 'current.json')
$launcherPath = Get-NormalPath (Join-Path $InstallRoot 'launcher.ps1')
$stableIcon = Get-NormalPath (Join-Path $InstallRoot 'blue.ico')
$shortcut = Get-NormalPath (Join-Path $DesktopPath 'LLPlayer Avalonia (Windows).lnk')
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f $launcherPath
$description = 'LLPlayer native Windows Avalonia test build; managed by update-desktop-test-build.ps1.'
$launcherBytes = $utf8.GetBytes($launcher + [Environment]::NewLine)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $launcherHash = ([BitConverter]::ToString($sha.ComputeHash($launcherBytes))).Replace('-','').ToLowerInvariant()
    $iconHash = ([BitConverter]::ToString($sha.ComputeHash($iconBytes))).Replace('-','').ToLowerInvariant()
}
finally { $sha.Dispose() }
if (Test-Path -LiteralPath $InstallRoot) {
    if (-not (Test-Path -LiteralPath $InstallRoot -PathType Container)) { throw 'InstallRoot must be a directory.' }
    if (Test-Path -LiteralPath $ownerPath) {
        $owner = Read-Json $ownerPath
        if ($owner.owner -ne $ownerTag -or $owner.schema -ne 1 -or $owner.installRoot -ine $InstallRoot -or $owner.launcherSha256 -cne $launcherHash -or $owner.iconSha256 -cne $iconHash) { throw 'InstallRoot has an incompatible or foreign owner record.' }
        foreach ($pair in @(@($launcherPath,$launcherHash),@($stableIcon,$iconHash))) {
            if ((Test-Path -LiteralPath $pair[0]) -and (Get-Hash $pair[0]) -cne $pair[1]) { throw 'A managed launcher or icon was modified; preserving it.' }
        }
        if (Test-Path -LiteralPath $currentPath) {
            $old = Read-Json $currentPath
            if ($old.owner -ne $ownerTag -or $old.schema -ne 1 -or $old.buildId -notmatch '^[A-Za-z0-9._-]+$' -or $old.buildId -in @('.','..')) { throw 'The existing current.json is foreign or invalid.' }
            $oldReceipt = Get-NormalPath (Join-Path $InstallRoot ('builds/' + $old.buildId + '/receipt.json'))
            if ($old.receiptPath -ine $oldReceipt -or (Get-Hash $oldReceipt) -cne $old.receiptSha256) { throw 'The existing desktop build receipt is invalid.' }
        }
    }
    elseif (@(Get-ChildItem -LiteralPath $InstallRoot -Force).Count -gt 0) { throw 'Refusing to claim a non-empty foreign InstallRoot.' }
}
$shell = New-Object -ComObject WScript.Shell
function Assert-Shortcut {
    $linkPath = Get-NormalPath $shortcut
    $readback = $shell.CreateShortcut($linkPath)
    try {
        if ($readback.TargetPath -ine $powershell -or $readback.Arguments -cne $arguments -or $readback.WorkingDirectory -ine $InstallRoot -or $readback.IconLocation -ine ($stableIcon + ',0') -or $readback.Description -cne $description -or $readback.WindowStyle -ne 7) {
            throw 'Refusing to overwrite a foreign or edited desktop shortcut; COM readback differs.'
        }
    }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($readback) }
}
try {
    if (Test-Path -LiteralPath $shortcut) {
        Assert-Shortcut
    }
    $sourceCommit = $null
    $sourceArchive = $null
    $archiveHash = $null
    $buildMode = 'prebuilt-validated'
    if ($PackageDir) {
        $package = Get-NormalPath $PackageDir
        if ($package -eq $InstallRoot -or (Test-Within $package $InstallRoot) -or (Test-Within $InstallRoot $package)) { throw 'PackageDir and InstallRoot must not overlap.' }
    }
    else {
        $buildMode = 'fresh-published'
        $sourceCommit = Get-CleanCommit
        $job = Join-Path $repoRoot ('artifacts/windows-desktop-test-build-' + [guid]::NewGuid().ToString('N'))
        $staging = Join-Path $job 'staging'
        $savedRuntimeRoot = $env:DOTNET_ROOT
        $savedRuntimeRootX64 = $env:DOTNET_ROOT_X64
        $savedPath = $env:PATH
        try {
            $env:DOTNET_ROOT = $runtime.root
            $env:DOTNET_ROOT_X64 = $runtime.root
            $env:PATH = $runtime.root + ';' + $savedPath
            $published = @(& (Join-Path $PSScriptRoot 'publish-avalonia.ps1') -OutDir (Join-Path $job 'archive') -StagingDir $staging)
        }
        finally {
            $env:DOTNET_ROOT = $savedRuntimeRoot
            $env:DOTNET_ROOT_X64 = $savedRuntimeRootX64
            $env:PATH = $savedPath
        }
        if ((Get-CleanCommit) -cne $sourceCommit) { throw 'Source HEAD changed while publishing; the desktop selection is unchanged.' }
        $sourceArchive = Get-NormalPath ([string]$published[-1])
        $archiveHash = Get-Hash $sourceArchive
        $packages = @(Get-ChildItem -LiteralPath $staging -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'LLPlayer.Avalonia.exe') -PathType Leaf })
        if ($packages.Count -ne 1) { throw 'The publisher did not produce one Avalonia package.' }
        $package = Get-NormalPath $packages[0].FullName
    }
    & (Join-Path $PSScriptRoot 'validate-avalonia-package.ps1') -PackageDir $package | Out-Null
    $config = Read-Json (Join-Path $package 'LLPlayer.Avalonia.runtimeconfig.json')
    if ($config.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App' -or [version]$config.runtimeOptions.framework.version -lt [version]'10.0.0' -or ([version]$config.runtimeOptions.framework.version).Major -ne 10 -or [version]$runtime.version -lt [version]$config.runtimeOptions.framework.version) { throw 'The package must use the selected .NET 10 runtime.' }
    [IO.Directory]::CreateDirectory($InstallRoot) | Out-Null
    if (-not (Test-Path -LiteralPath $ownerPath)) {
        $owner = [ordered]@{ schema=1; owner=$ownerTag; installRoot=$InstallRoot; launcherSha256=$launcherHash; iconSha256=$iconHash }
        Write-Atomic $ownerPath $utf8.GetBytes(($owner | ConvertTo-Json) + [Environment]::NewLine)
    }
    $prefix = if ($sourceCommit) { $sourceCommit.Substring(0,12) } else { 'prebuilt' }
    $buildId = $prefix + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
    $build = Get-NormalPath (Join-Path $InstallRoot ('builds/' + $buildId))
    $payload = Join-Path $build 'payload'
    [IO.Directory]::CreateDirectory($payload) | Out-Null
    $files = New-Object 'System.Collections.Generic.List[object]'
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $pending.Enqueue($package)
    while ($pending.Count) {
        foreach ($item in @(Get-ChildItem -LiteralPath $pending.Dequeue() -Force)) {
            $source = Get-NormalPath $item.FullName
            if ($item.PSIsContainer) { $pending.Enqueue($source); continue }
            $relative = $source.Substring($package.Length + 1)
            $target = Get-NormalPath (Join-Path $payload $relative)
            if (-not (Test-Within $payload $target)) { throw 'Unsafe payload destination.' }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.File]::Copy($source, $target, $false)
            $hash = Get-Hash $source
            if ((Get-Hash $target) -cne $hash) { throw "Installed file differs from its package: $relative" }
            $files.Add([ordered]@{ path=$relative.Replace('\','/'); bytes=$item.Length; sha256=$hash })
        }
    }
    & (Join-Path $PSScriptRoot 'validate-avalonia-package.ps1') -PackageDir $payload | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $build 'blue.ico'), $iconBytes)
    if ($sourceCommit -and (Get-CleanCommit) -cne $sourceCommit) { throw 'Source HEAD changed before activation; the desktop selection is unchanged.' }
    if (-not (Test-Path -LiteralPath $launcherPath)) { Write-Atomic $launcherPath $launcherBytes }
    if (-not (Test-Path -LiteralPath $stableIcon)) { Write-Atomic $stableIcon $iconBytes }
    if (-not (Test-Path -LiteralPath $shortcut)) {
        $stagedLink = Join-Path $build 'desktop-shortcut.lnk'
        $link = $shell.CreateShortcut($stagedLink)
        $link.TargetPath = $powershell
        $link.Arguments = $arguments
        $link.WorkingDirectory = $InstallRoot
        $link.IconLocation = $stableIcon + ',0'
        $link.Description = $description
        $link.WindowStyle = 7
        $link.Save()
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link)
        [IO.File]::Copy($stagedLink, $shortcut, $false)
    }
    $exe = Join-Path $payload 'LLPlayer.Avalonia.exe'
    $receipt = [ordered]@{
        schema=1; owner=$ownerTag; buildId=$buildId; buildMode=$buildMode; sourceCommit=$sourceCommit
        createdUtc=[DateTime]::UtcNow.ToString('o'); repository=$repoRoot; packageSource=$package
        sourceArchive=$sourceArchive; sourceArchiveSha256=$archiveHash
        payloadPath=$payload; executablePath=$exe; executableSha256=(Get-Hash $exe)
        dotNetRoot=$runtime.root; runtimeVersion=$runtime.version
        launcherPath=$launcherPath; launcherSha256=$launcherHash; iconPath=$stableIcon; iconSha256=$iconHash
        shortcutPath=$shortcut; shortcutSha256=(Get-Hash $shortcut); files=@($files.ToArray())
    }
    $receiptPath = Join-Path $build 'receipt.json'
    [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 5) + [Environment]::NewLine, $utf8)
    $pointer = [ordered]@{ schema=1; owner=$ownerTag; buildId=$buildId; buildMode=$buildMode; sourceCommit=$sourceCommit; receiptPath=$receiptPath; receiptSha256=(Get-Hash $receiptPath) }
    if ($sourceCommit -and (Get-CleanCommit) -cne $sourceCommit) { throw 'Source HEAD changed before selection; the old desktop build is retained.' }
    Assert-Shortcut
    Write-Atomic $currentPath $utf8.GetBytes(($pointer | ConvertTo-Json) + [Environment]::NewLine)
    Write-Host "Desktop test build selected: $buildId ($buildMode). The player was not launched."
    Write-Output ([pscustomobject]@{ currentPath=$currentPath; receiptPath=$receiptPath; shortcutPath=$shortcut; payloadPath=$payload; sourceCommit=$sourceCommit; buildMode=$buildMode })
}
finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }

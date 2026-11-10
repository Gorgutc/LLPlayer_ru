param(
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Applications/LLPlayer-Avalonia-TestBuild'),
    [string]$DesktopPath = [Environment]::GetFolderPath('DesktopDirectory'),
    [ValidateRange(2,100)][int]$KeepLast = 2,
    [string]$PreviousBuildId,
    [string]$IconPath = (Join-Path $PSScriptRoot 'assets/llplayer-avalonia-blue.ico'),
    [switch]$Apply,
    [string]$CreatedPublishJob,
    [string[]]$AdditionalProtectedPaths = @(),
    [string[]]$ReferenceRoots,
    [string[]]$RunningPathsForTests = @(),
    [object[]]$ProcessDescriptorsForTests = @(),
    [scriptblock]$BeforeDeleteForTests,
    [switch]$FailDeleteForTests
)
# Preview by default. Only verified, unreferenced immutable builds and their owned publish jobs are eligible.
# No process is started/stopped. A protected/unknown build is an extra, never a replacement for the fallback.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Desktop retention requires Windows.' }
$ownerTag = 'LLPlayer.Avalonia.DesktopTest'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not ('LLPlayerRetention.Native' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace LLPlayerRetention {
 public static class Native {
  [StructLayout(LayoutKind.Sequential)] struct Info { public uint attributes; public System.Runtime.InteropServices.ComTypes.FILETIME creation, access, write; public uint volume, sizeHigh, sizeLow, links, indexHigh, indexLow; }
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
  [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
  public static string[] Arguments(string commandLine) {
   int count; IntPtr block = CommandLineToArgvW(commandLine, out count);
   if (block == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   try { string[] result = new string[count]; for (int i=0; i<count; i++) result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(block, i*IntPtr.Size)); return result; }
   finally { LocalFree(block); }
  }
  public static string ResolveReference(string path) { return CheckInternal(path, false, false); }
  public static string Check(string path, bool file) { return CheckInternal(path, file, true); }
  public static string NormalizeResolvedReference(string result) {
   if (result.StartsWith(@"\\?\", StringComparison.Ordinal)) result = result.Substring(4);
   if (!System.Text.RegularExpressions.Regex.IsMatch(result, @"^[A-Za-z]:\\")) throw new InvalidOperationException("A resolved reference must be an absolute local drive path: " + result);
   return result;
  }
  static string CheckInternal(string path, bool file, bool requireSame) {
   using (SafeFileHandle h = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
    if (h.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    Info info;
    if (!GetFileInformationByHandle(h, out info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    if (file && info.links != 1) throw new InvalidOperationException("Hardlinked file is protected: " + path);
    StringBuilder b = new StringBuilder(32768);
    uint n = GetFinalPathNameByHandle(h, b, (uint)b.Capacity, 0);
    if (n == 0 || n >= b.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    string result = NormalizeResolvedReference(b.ToString());
    if (requireSame && !String.Equals(System.IO.Path.GetFullPath(path), result, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Aliased or virtualized path is protected: " + path);
    return result;
   }
  }
 }
}
"@
}
function Normal([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:[\\/]') { throw "An absolute local drive path is required: $Path" }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $ancestor = $full
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse path is protected: $ancestor" }
        }
        $parent = [IO.Directory]::GetParent($ancestor)
        $ancestor = if ($parent) { $parent.FullName } else { $null }
    }
    return $full
}
function Within([string]$Parent, [string]$Child) { return $Child.StartsWith($Parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) }
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Physical([string]$Path) {
    $full = Normal $Path
    $item = Get-Item -LiteralPath $full -Force
    return [LLPlayerRetention.Native]::Check($full, -not $item.PSIsContainer)
}
function Json([string]$Path) { $null = Physical $Path; return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
function Tree([string]$Root) {
    $Root = Physical $Root
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $pending.Enqueue($Root)
    $found = New-Object 'System.Collections.Generic.List[object]'
    while ($pending.Count) {
        foreach ($item in @(Get-ChildItem -LiteralPath $pending.Dequeue() -Force)) {
            $path = Physical $item.FullName
            if (-not (Within $Root $path)) { throw 'Tree escaped its literal boundary.' }
            if ($item.PSIsContainer) {
                $found.Add([pscustomobject]@{ path=($path.Substring($Root.Length+1).Replace('\','/')+'/'); bytes=0; sha256='directory' })
                $pending.Enqueue($path)
            }
            else { $found.Add([pscustomobject]@{ path=$path.Substring($Root.Length+1).Replace('\','/'); bytes=$item.Length; sha256=(Hash $path) }) }
        }
    }
    return @($found.ToArray() | Sort-Object path)
}
function Fingerprint($Files) { return (($Files | ConvertTo-Json -Depth 4 -Compress) -join '') }
function Check-Manifest([string]$Root, $Files) {
    $seen = @{}
    foreach ($entry in @($Files)) {
        $relative = [string]$entry.path
        if ($relative -notmatch '^[^\\/:]+(/[^\\/:]+)*$' -or @($relative.Split('/') | Where-Object { $_ -in @('.','..') -or $_.TrimEnd(' .') -cne $_ }).Count -or $seen.ContainsKey($relative)) { throw 'Invalid/duplicate manifest relative path.' }
        if ([string]$entry.sha256 -cnotmatch '^[0-9a-f]{64}$' -or [long]$entry.bytes -lt 0) { throw 'Invalid manifest hash/size.' }
        $path = Normal (Join-Path $Root $relative)
        if (-not (Within $Root $path)) { throw 'Manifest path escaped payload.' }
        $seen[$relative] = $entry
    }
    $tree = @(Tree $Root)
    $expectedDirectories = @{}
    foreach ($relative in $seen.Keys) {
        $parts = $relative.Split('/')
        for ($i = 1; $i -lt $parts.Length; $i++) { $expectedDirectories[(($parts[0..($i-1)] -join '/')+'/')] = $true }
    }
    foreach ($directory in @($tree | Where-Object { $_.sha256 -ceq 'directory' })) {
        if (-not $expectedDirectories.ContainsKey($directory.path)) { throw "Unknown payload directory is protected: $($directory.path)" }
    }
    $actual = @($tree | Where-Object { $_.sha256 -cne 'directory' })
    if ($actual.Count -ne $seen.Count -or $actual.Count -eq 0) { throw 'Extra or missing payload files are protected.' }
    foreach ($file in $actual) {
        if (-not $seen.ContainsKey($file.path) -or $file.bytes -ne $seen[$file.path].bytes -or $file.sha256 -cne $seen[$file.path].sha256) { throw "Modified or unknown payload file is protected: $($file.path)" }
    }
    return $actual
}
function Build([string]$Directory) {
    $Directory = Physical $Directory
    $id = [IO.Path]::GetFileName($Directory)
    if ($id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or $Directory -ine (Join-Path $buildsRoot $id)) { throw 'Invalid build boundary or identity.' }
    $all = @(Tree $Directory)
    $top = @(Get-ChildItem -LiteralPath $Directory -Force)
    if ($top.Count -ne 3 -or @($top | Where-Object { $_.Name -notin @('receipt.json','blue.ico','payload') }).Count) { throw 'Unknown build contents are protected.' }
    $receiptPath = Join-Path $Directory 'receipt.json'
    $r = Json $receiptPath
    $payload = Join-Path $Directory 'payload'; $exe = Join-Path $payload 'LLPlayer.Avalonia.exe'; $blue = Join-Path $Directory 'blue.ico'
    if ($r.schema -ne 2 -or $r.owner -cne $ownerTag -or $r.buildId -cne $id -or $r.payloadPath -ine $payload -or $r.executablePath -ine $exe -or $r.workingDirectory -ine $payload -or $r.iconPath -ine $blue -or $r.targetArguments -cne '') { throw 'Foreign or invalid schema-2 build identity.' }
    if ($r.executableSha256 -cne (Hash $exe) -or $r.iconSha256 -cne (Hash $blue) -or $r.iconSha256 -cne $iconHash) { throw 'Modified executable or blue icon is protected.' }
    $null = Check-Manifest $payload $r.files
    $created = [DateTime]::Parse($r.createdUtc).ToUniversalTime()
    return [pscustomobject]@{ id=$id; path=$Directory; receipt=$r; receiptHash=(Hash $receiptPath); fingerprint=(Fingerprint $all); created=$created }
}
function Assert-State {
    foreach ($state in $stateHashes.GetEnumerator()) {
        $null = Physical $state.Key
        if ((Hash $state.Key) -cne $state.Value) { throw "Selection changed during retention: $($state.Key)" }
    }
}
function Has-Dependency([string]$Target,[string]$ExcludedBuild) {
    foreach ($other in @($buildDirectories | Where-Object { $_.FullName -ine $ExcludedBuild })) {
        if (-not (Test-Path -LiteralPath $other.FullName)) { continue }
        try {
            $otherReceipt = Json (Join-Path $other.FullName 'receipt.json')
            foreach ($field in @('sourceArchive','packageSource')) {
                if ($otherReceipt.PSObject.Properties[$field] -and $otherReceipt.$field) {
                    $path = Normal ([string]$otherReceipt.$field)
                    if ($path -ieq $Target -or (Within $Target $path)) { return $true }
                }
            }
        }
        catch { return $true } # An unreadable receipt cannot establish that its dependencies are disposable.
    }
    return $false
}
function Assert-Retained {
    foreach ($retained in @($valid | Where-Object { $keep.Contains($_.id) })) {
        if ((Build $retained.path).fingerprint -cne $retained.fingerprint) { throw 'A retained current/fallback build changed during retention.' }
    }
}
function Check-GeneratedPaths($Files) {
    foreach ($file in $Files) {
        # Publisher products only. Settings, model downloads and arbitrary additions never gain ownership.
        if ($file.path -match '(^|/)(models?|configs?|settings|userdata|cache|history|logs?|\.git)(/|\.|$)' -or
            ($file.path -match '\.(gguf|safetensors|pt|pth|ckpt|db|sqlite|sqlite3)$') -or
            ($file.path -match '\.onnx$' -and $file.path -notmatch '/Assets/silero_vad\.onnx$')) { throw "User data/model-like publish contents are protected: $($file.path)" }
        if ($file.path -match '\.json$' -and $file.path -notmatch '(\.deps|\.runtimeconfig|/native-dependencies)\.json$') { throw "Unknown JSON in publish workspace is protected: $($file.path)" }
    }
}
function Job-Path($Receipt) {
    $repository = Physical ([string]$Receipt.repository)
    if (-not (Test-Path -LiteralPath (Join-Path $repository 'LLPlayer.sln')) -and -not (Test-Path -LiteralPath (Join-Path $repository 'LLPlayer.slnx'))) { throw 'Associated repository is not an existing LLPlayer checkout.' }
    if (-not (Test-Path -LiteralPath (Join-Path $repository 'LLPlayer.Avalonia/LLPlayer.Avalonia.csproj') -PathType Leaf)) { throw 'Associated repository lacks LLPlayer project identity.' }
    $package = Normal ([string]$Receipt.packageSource)
    $archive = Normal ([string]$Receipt.sourceArchive)
    $artifacts = Join-Path $repository 'artifacts'
    $relative = $package.Substring($artifacts.Length + 1)
    if (-not (Within $artifacts $package) -or $relative -notmatch '^(windows-desktop-test-build-[0-9a-f]{32})\\staging\\(LLPlayer-Avalonia-[0-9A-Za-z._+-]+-win-x64)$') { throw 'Package source is not an exact generated desktop publish workspace.' }
    $job = Join-Path $artifacts $Matches[1]
    $packageName = $Matches[2]
    if ($archive -ine (Join-Path $job ('archive/' + $packageName + '.zip'))) { throw 'Associated archive is outside the exact publish workspace.' }
    $null = Physical $job
    return $job
}
function Job($Build, [switch]$Record) {
    $r = $Build.receipt; $job = Job-Path $r
    if ((Hash $r.sourceArchive) -cne $r.sourceArchiveSha256) { throw 'Associated archive hash changed.' }
    $markerPath = Join-Path $job 'desktop-publish-owner.json'
    if ($Record) {
        if (Test-Path -LiteralPath $markerPath) { throw 'Refusing to overwrite publish ownership evidence.' }
        $name = [IO.Path]::GetFileName($r.packageSource)
        $top = @(Get-ChildItem -LiteralPath $job -Force)
        if ($top.Count -ne 2 -or @($top | Where-Object { $_.Name -notin @('archive','staging') -or -not $_.PSIsContainer }).Count) { throw 'Unexpected publish workspace roots.' }
        $staged = @(Get-ChildItem -LiteralPath (Join-Path $job 'staging') -Force)
        if ($staged.Count -ne 4 -or @($staged | Where-Object { $_.Name -notin @($name,($name+'.zip'),'archive-readback','portable-plugin') }).Count) { throw 'Unexpected generated staging roots.' }
        $null = Check-Manifest $r.packageSource $r.files
        $null = Check-Manifest (Join-Path $job ('staging/archive-readback/' + $name)) $r.files
        $readback = @(Get-ChildItem -LiteralPath (Join-Path $job 'staging/archive-readback') -Force)
        if ($readback.Count -ne 1 -or -not $readback[0].PSIsContainer -or $readback[0].Name -cne $name) { throw 'Unknown archive readback contents are protected.' }
        $pluginRoot = Join-Path $job 'staging/portable-plugin'
        $pluginFiles = @(Tree $pluginRoot)
        $pluginNames = @('YoutubeDL.dll','YoutubeDL.pdb','YoutubeDL.deps.json')
        if ($pluginFiles.Count -ne 3 -or @($pluginFiles | Where-Object { $_.path -cnotin $pluginNames -or $_.sha256 -ceq 'directory' }).Count) { throw 'Unknown portable-plugin contents are protected; expected only YoutubeDL.dll/pdb/deps.json.' }
        foreach ($pluginName in @('YoutubeDL.dll','YoutubeDL.pdb')) {
            if ((Hash (Join-Path $pluginRoot $pluginName)) -cne (Hash (Join-Path $r.packageSource ('Plugins/YoutubeDL/'+$pluginName)))) { throw 'Portable plugin differs from the deployed package.' }
        }
        $null = Json (Join-Path $pluginRoot 'YoutubeDL.deps.json')
        if ((Hash (Join-Path $job ('staging/' + $name + '.zip'))) -cne $r.sourceArchiveSha256) { throw 'Staging archive differs.' }
        $archiveFiles = @(Get-ChildItem -LiteralPath (Join-Path $job 'archive') -Force)
        if ($archiveFiles.Count -ne 2 -or @($archiveFiles | Where-Object { $_.PSIsContainer -or $_.Name -notin @(($name+'.zip'),($name+'.zip.sha256')) }).Count) { throw 'Unexpected publish archive contents.' }
        $files = @(Tree $job); Check-GeneratedPaths $files
        $marker = [ordered]@{ schema=1; owner=$ownerTag; buildId=$Build.id; receiptSha256=$Build.receiptHash; files=$files }
        [IO.File]::WriteAllText($markerPath, ($marker | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    }
    $m = Json $markerPath
    if ($m.schema -ne 1 -or $m.owner -cne $ownerTag -or $m.buildId -cne $Build.id -or $m.receiptSha256 -cne $Build.receiptHash) { throw 'Invalid publish ownership evidence.' }
    $files = @(Tree $job | Where-Object { $_.path -cne 'desktop-publish-owner.json' })
    Check-GeneratedPaths $files
    if ((Fingerprint $files) -cne (Fingerprint @($m.files | Sort-Object path))) { throw 'Changed/extra generated publish contents are protected.' }
    return [pscustomobject]@{ path=$job; fingerprint=(Fingerprint @(Tree $job)) }
}
function Add-ProcessPath($Paths,[string]$ProcessName,[string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        if ($ProcessName -like 'LLPlayer*') { throw 'A running LLPlayer path cannot be inspected; preserving candidates.' }
        return
    }
    if ($Path -notmatch '^[A-Za-z]:[\\/]') { throw 'A running process image path cannot be resolved safely; preserving candidates.' }
    $full = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $full) { $full = [LLPlayerRetention.Native]::ResolveReference($full) }
    elseif ($ProcessName -like 'LLPlayer*') { throw 'A running LLPlayer image cannot be resolved; preserving candidates.' }
    $Paths.Add($full)
}
function Protected-Paths {
    $paths = New-Object 'System.Collections.Generic.List[string]'
    foreach ($p in @($AdditionalProtectedPaths) + @($RunningPathsForTests)) { if ($p) { $paths.Add((Normal $p)) } }
    foreach ($process in @(Get-Process)) {
        try { Add-ProcessPath $paths $process.ProcessName $process.Path }
        catch { if ($process.ProcessName -like 'LLPlayer*') { throw } }
        if ($process.ProcessName -ieq 'dotnet') {
            try { $hosted = Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId = '+$process.Id) -Property CommandLine -OperationTimeoutSec 3 -ErrorAction Stop }
            catch { throw 'A running dotnet host command line cannot be inspected; preserving candidates.' }
            Add-HostedPaths $paths ([string]$hosted.CommandLine)
        }
    }
    foreach ($process in $ProcessDescriptorsForTests) {
        try { Add-ProcessPath $paths ([string]$process.ProcessName) ([string]$process.Path) }
        catch { if ($process.ProcessName -like 'LLPlayer*') { throw } }
        if ($process.ProcessName -ieq 'dotnet') { Add-HostedPaths $paths ([string]$process.CommandLine) }
    }
    foreach ($root in @($ReferenceRoots)) {
        if (-not $root -or -not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        $pending = New-Object 'System.Collections.Generic.Queue[string]'; $pending.Enqueue($root)
        while ($pending.Count) {
            foreach ($item in @(Get-ChildItem -LiteralPath $pending.Dequeue() -Force)) {
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    # Skip only targets independently scanned through a reference root or its physical descendants.
                    $covered = $false
                    if ($item.PSIsContainer) {
                        $resolved = [LLPlayerRetention.Native]::ResolveReference($item.FullName)
                        foreach ($referenceRoot in @($ReferenceRoots)) {
                            if (-not $referenceRoot -or -not (Test-Path -LiteralPath $referenceRoot -PathType Container)) { continue }
                            $referencePath = [LLPlayerRetention.Native]::ResolveReference($referenceRoot)
                            if ($referencePath -ieq $resolved -or
                                ((Within $referencePath $resolved) -and (Physical $referenceRoot) -ieq $referencePath -and (Physical $resolved) -ieq $resolved)) { $covered = $true; break }
                        }
                    }
                    if ($covered) { continue }
                    throw "Cannot inventory shortcut references through reparse point: $($item.FullName)"
                }
                if ($item.PSIsContainer) { $pending.Enqueue($item.FullName); continue }
                if ($item.Extension -ine '.lnk') { continue }
                $link = $shell.CreateShortcut($item.FullName)
                try {
                    foreach ($p in @([string]$link.TargetPath,[string]$link.WorkingDirectory,([string]$link.IconLocation -replace ',\s*-?\d+$',''))) {
                        $p = [Environment]::ExpandEnvironmentVariables($p.Trim('"'))
                        if ($p -match '^[A-Za-z]:[\\/]') {
                            # Resolve read-only references through aliases; destructive targets always require exact physical paths.
                            $full = [IO.Path]::GetFullPath($p)
                            if (Test-Path -LiteralPath $full) { $full = [LLPlayerRetention.Native]::ResolveReference($full) }
                            $paths.Add($full)
                        }
                    }
                    # Arguments may reference an older payload even when the target is a launcher.
                    foreach ($directory in $buildDirectories) { if (([string]$link.Arguments).Replace('/','\') -like ('*' + $directory.FullName + '*')) { $paths.Add($directory.FullName) } }
                }
                finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
            }
        }
    }
    return @($paths.ToArray())
}
function Add-HostedPaths($Paths,[string]$CommandLine) {
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { throw 'A running dotnet host command line is unknown; preserving candidates.' }
    foreach ($argument in @([LLPlayerRetention.Native]::Arguments($CommandLine))) {
        if ([IO.Path]::GetFileName($argument) -ine 'LLPlayer.Avalonia.dll') { continue }
        if ($argument -notmatch '^[A-Za-z]:[\\/]') { throw 'A relative dotnet-hosted LLPlayer assembly cannot be resolved safely; preserving candidates.' }
        $path = [IO.Path]::GetFullPath($argument)
        if (Test-Path -LiteralPath $path) { $path = [LLPlayerRetention.Native]::ResolveReference($path) }
        $Paths.Add($path)
    }
}
$InstallRoot = Physical $InstallRoot; $DesktopPath = Physical $DesktopPath; $iconHash = Hash (Physical $IconPath)
$buildsRoot = Physical (Join-Path $InstallRoot 'builds')
if ($RunningPathsForTests.Count -or $ProcessDescriptorsForTests.Count -or $BeforeDeleteForTests -or $FailDeleteForTests) {
    $fixtureBoundary = Join-Path $repoRoot 'artifacts'
    if (-not (Within $fixtureBoundary $InstallRoot) -or $InstallRoot -notmatch '\\desktop-retention-fixture-[0-9a-f]{32}\\') { throw 'Retention test hooks require a unique repository artifacts fixture.' }
}
$ownerPath = Join-Path $InstallRoot 'owner.json'; $currentPath = Join-Path $InstallRoot 'current.json'
$o = Json $ownerPath; $c = Json $currentPath
if ($o.schema -ne 2 -or $o.owner -cne $ownerTag -or $o.installRoot -ine $InstallRoot -or $o.shortcutName -cne 'LLPlayer Avalonia (Windows).lnk' -or $c.schema -ne 2 -or $c.owner -cne $ownerTag) { throw 'A managed schema-2 owner/current selection is required.' }
$buildDirectories = @(Get-ChildItem -LiteralPath $buildsRoot -Force)
$valid = New-Object 'System.Collections.Generic.List[object]'; $protected = New-Object 'System.Collections.Generic.List[object]'
$errors = New-Object 'System.Collections.Generic.List[string]'; $deleted = New-Object 'System.Collections.Generic.List[string]'; $deletedJobs = New-Object 'System.Collections.Generic.List[string]'
foreach ($directory in $buildDirectories) {
    try { if (-not $directory.PSIsContainer) { throw 'Unknown file in builds root.' }; $valid.Add((Build $directory.FullName)) }
    catch { $protected.Add([pscustomobject]@{ path=$directory.FullName; reason=$_.Exception.Message }) }
}
$current = @($valid | Where-Object { $_.id -ceq $c.buildId })
if ($current.Count -ne 1) { throw 'The current build could not be fully verified; no retention applied.' }
$current = $current[0]; $r = $current.receipt
if ($c.receiptPath -ine (Join-Path $current.path 'receipt.json') -or $c.receiptSha256 -cne $current.receiptHash -or $c.sourceCommit -cne $r.sourceCommit) { throw 'Current receipt identity differs.' }
foreach ($field in @('executablePath','executableSha256','workingDirectory','iconPath','iconSha256','shortcutPath','shortcutSha256')) {
    if ([string]$c.$field -cne [string]$r.$field) { throw "Current identity differs: $field" }
}
$shortcut = Physical ([string]$r.shortcutPath)
if ($shortcut -ine (Join-Path $DesktopPath $o.shortcutName) -or (Hash $shortcut) -cne $r.shortcutSha256) { throw 'Current shortcut identity differs.' }
$stateHashes = @{}; foreach ($p in @($ownerPath,$currentPath,$shortcut,(Join-Path $current.path 'receipt.json'))) { $stateHashes[$p] = Hash $p }
if (-not $ReferenceRoots) {
    $ReferenceRoots = @($DesktopPath,[Environment]::GetFolderPath('CommonDesktopDirectory'),[Environment]::GetFolderPath('StartMenu'),[Environment]::GetFolderPath('CommonStartMenu'),[Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('CommonPrograms'))
    if ($env:APPDATA) { $ReferenceRoots += Join-Path $env:APPDATA 'Microsoft/Internet Explorer/Quick Launch/User Pinned' }
}
$shell = New-Object -ComObject WScript.Shell
try {
    $selectedLink = $shell.CreateShortcut($shortcut)
    try {
        if ($selectedLink.TargetPath -ine $r.executablePath -or $selectedLink.WorkingDirectory -ine $r.payloadPath -or $selectedLink.Arguments -cne '' -or $selectedLink.IconLocation -ine ($r.iconPath+',0')) { throw 'Selected direct shortcut fields differ.' }
    }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($selectedLink) }
    $keep = New-Object 'System.Collections.Generic.List[string]'; $keep.Add($current.id)
    $fallbacks = @($valid | Where-Object { $_.id -cne $current.id } | Sort-Object @{Expression={ if ($PreviousBuildId -and $_.id -ceq $PreviousBuildId) { 1 } else { 0 } }; Descending=$true}, @{Expression={$_.created}; Descending=$true})
    foreach ($fallback in $fallbacks) { if ($keep.Count -lt $KeepLast) { $keep.Add($fallback.id) } }
    if ($CreatedPublishJob -and $Apply) {
        try {
            $requestedJob = Normal $CreatedPublishJob
            $associated = @($valid | Where-Object {
                if (-not $keep.Contains($_.id)) { return $false }
                try { return (Job-Path $_.receipt) -ieq $requestedJob } catch { return $false }
            })
            if ($associated.Count -ne 1) { throw 'Publish ownership requires the exact job of one fully verified retained current/fallback receipt.' }
            Assert-State
            Assert-Retained
            $null = Job $associated[0] -Record
        }
        catch { $errors.Add('Publish ownership: ' + $_.Exception.Message) }
    }
    foreach ($candidate in @($valid | Where-Object { -not $keep.Contains($_.id) })) {
        try {
            $refs = @(Protected-Paths)
            if (@($refs | Where-Object { $_ -ieq $candidate.path -or (Within $candidate.path $_) }).Count) { throw 'Referenced or running build is protected.' }
            if (Has-Dependency $candidate.path $candidate.path) { throw 'Retained/protected/unknown receipt depends on this build.' }
            # Resolve dependencies of every retained/protected/unknown receipt before considering its job.
            $job = $null
            try { $job = Job $candidate } catch { $protected.Add([pscustomobject]@{ path=[string]$candidate.receipt.packageSource; reason=('Publish workspace preserved: '+$_.Exception.Message) }) }
            if ($BeforeDeleteForTests) { & $BeforeDeleteForTests $candidate.path $currentPath }
            Assert-State
            $fresh = Build $candidate.path
            if ($fresh.fingerprint -cne $candidate.fingerprint) { throw 'Candidate changed during retention.' }
            Assert-Retained
            $refs = @(Protected-Paths)
            if (@($refs | Where-Object { $_ -ieq $candidate.path -or (Within $candidate.path $_) }).Count) { throw 'Build acquired a running/shortcut reference.' }
            if (Has-Dependency $candidate.path $candidate.path) { throw 'Build acquired a receipt dependency.' }
            if ($Apply) {
                if ($FailDeleteForTests) { throw 'Injected retention delete failure.' }
                # Tree/ancestors, hardlinks, hashes and current CAS were just rechecked. Never remove a supplied package path.
                Remove-Item -LiteralPath $candidate.path -Recurse -Force
                $deleted.Add($candidate.path)
            }
            if ($job) {
                $refs = @(Protected-Paths)
                if ((Has-Dependency $job.path $candidate.path) -or @($refs | Where-Object { $_ -ieq $job.path -or (Within $job.path $_) }).Count) {
                    $protected.Add([pscustomobject]@{ path=$job.path; reason='Retained/protected/unknown receipt, shortcut or running dependency.' }); continue
                }
                Assert-State
                Assert-Retained
                if ((Fingerprint @(Tree $job.path)) -cne $job.fingerprint) { throw 'Publish workspace changed during retention.' }
                if ($Apply) { Remove-Item -LiteralPath $job.path -Recurse -Force; $deletedJobs.Add($job.path) }
            }
        }
        catch { $protected.Add([pscustomobject]@{ path=$candidate.path; reason=$_.Exception.Message }); $errors.Add($_.Exception.Message) }
    }
    [pscustomobject]@{
        mode=$(if ($Apply) { 'apply' } else { 'preview' }); status=$(if ($errors.Count) { 'partial' } else { 'ok' })
        currentBuildId=$current.id; retainedBuildIds=@($keep.ToArray()); protected=@($protected.ToArray())
        candidates=@($valid | Where-Object { -not $keep.Contains($_.id) } | ForEach-Object { $_.path })
        deletedBuilds=@($deleted.ToArray()); deletedJobs=@($deletedJobs.ToArray()); errors=@($errors.ToArray())
    }
}
finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }

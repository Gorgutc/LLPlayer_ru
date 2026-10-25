param(
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Applications/LLPlayer-Avalonia-TestBuild'),
    [string]$DesktopPath = [Environment]::GetFolderPath('DesktopDirectory'),
    [string]$DotNetRoot,
    [string]$IconPath = (Join-Path $PSScriptRoot 'assets/llplayer-avalonia-blue.ico'),
    [string]$PackageDir,
    [string[]]$LegacyRootsForTests
)
# Local test deployment only. Never starts/stops a player, installs a runtime or deletes old payloads.
# -LegacyRootsForTests is a contract-fixture seam: it only extends the bound legacy-root allowlist and is
# rejected without -PackageDir, so a normal refresh still has to resolve the shortcut into a bound root
# and verify every owner/receipt/executable/icon/launcher/shortcut identity below.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Desktop test builds require Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ownerTag = 'LLPlayer.Avalonia.DesktopTest'
$script:testLegacyRootAllowlist = @()
$shortcutName = 'LLPlayer Avalonia (Windows).lnk'
$description = 'LLPlayer native Windows Avalonia test build; managed by update-desktop-test-build.ps1.'
$utf8 = New-Object Text.UTF8Encoding($false)
function Get-NormalPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A local path is required.' }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^[A-Za-z]:\\') { throw "A local drive path is required: $full" }
    if ($full.Length -gt [IO.Path]::GetPathRoot($full).Length) { $full = $full.TrimEnd('\','/') }
    $ancestor = $full
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Desktop test build paths must not contain reparse points: $ancestor"
        }
        $parent = [IO.Directory]::GetParent($ancestor)
        $ancestor = if ($parent) { $parent.FullName } else { $null }
    }
    return $full
}
function Test-Within([string]$Parent, [string]$Child) { return $Child.StartsWith($Parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) }
function Get-Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Assert-PhysicalPath([string]$Path) {
    $full = Get-NormalPath $Path
    $physical = [LLPlayerDesktopTest.NativeShortcutV2]::PhysicalPath($full)
    if ($physical -ine $full) { throw "Aliased or virtualized path is not a deployment destination: $full -> $physical" }
    return $physical
}
function Resolve-PhysicalDestination([string]$Path) {
    # Resolves the nearest existing ancestor through a handle, then rebuilds the missing tail. A redirected
    # ancestor (for example an MSIX AppData alias) therefore cannot become a new deployment destination.
    $full = Get-NormalPath $Path
    $tail = New-Object 'System.Collections.Generic.List[string]'
    $probe = $full
    while (-not (Test-Path -LiteralPath $probe)) {
        $tail.Insert(0, [IO.Path]::GetFileName($probe))
        $parent = [IO.Directory]::GetParent($probe)
        if ($null -eq $parent) { throw "A local deployment directory has no existing ancestor: $full" }
        $probe = $parent.FullName
    }
    $resolved = [LLPlayerDesktopTest.NativeShortcutV2]::PhysicalPath($probe)
    foreach ($segment in $tail) { $resolved = Join-Path $resolved $segment }
    $resolved = Get-NormalPath $resolved
    if ($resolved -ine $full) { throw "Aliased or virtualized path is not a deployment destination: $full -> $resolved" }
    return $resolved
}
function Get-BytesHash([byte[]]$Bytes) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant() }
    finally { $hash.Dispose() }
}
function Read-Json([string]$Path) { return Get-Content -LiteralPath (Get-NormalPath $Path) -Raw -Encoding UTF8 | ConvertFrom-Json }
function Write-Atomic([string]$Path, [byte[]]$Bytes) {
    $Path = Get-NormalPath $Path
    if (-not (Test-Within $InstallRoot $Path) -and $Path -ine $shortcut) { throw 'Atomic writes must stay in the managed root or exact owned shortcut.' }
    $temporary = Get-NormalPath (Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.desktop-' + [guid]::NewGuid().ToString('N') + '.tmp'))
    try {
        [IO.File]::WriteAllBytes($temporary, $Bytes)
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            try { [IO.File]::Delete($temporary) } catch { }
        }
    }
}
function Get-Snapshot([string]$Path) {
    $Path = Get-NormalPath $Path
    if (-not (Test-Path -LiteralPath $Path)) { return [pscustomobject]@{ path=$Path; bytes=$null; hash=$null } }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Managed state must be a regular file: $Path" }
    $bytes = [IO.File]::ReadAllBytes($Path)
    return [pscustomobject]@{ path=$Path; bytes=$bytes; hash=(Get-BytesHash $bytes) }
}
function Assert-Snapshot($Snapshot) {
    $now = Get-Snapshot $Snapshot.path
    if ($now.hash -cne $Snapshot.hash) { throw "Managed state changed during deployment; preserving it: $($Snapshot.path)" }
}
function Restore-Snapshot($Snapshot, [string]$WrittenHash) {
    $now = Get-Snapshot $Snapshot.path
    if ($now.hash -ceq $Snapshot.hash) { return }
    if ($now.hash -cne $WrittenHash) { throw "Rollback refuses to overwrite changed state: $($Snapshot.path)" }
    if ($null -ne $Snapshot.bytes) { Write-Atomic $Snapshot.path $Snapshot.bytes }
    else {
        # Remove only the exact new metadata/link created by this transaction, never a payload.
        $path = Get-NormalPath $Snapshot.path
        if ($path -ine $currentPath -and $path -ine $ownerPath -and $path -ine $shortcut) { throw 'Unsafe rollback file.' }
        [IO.File]::Delete($path)
    }
}
function Get-CleanCommit {
    $commit = [string](& git -C $repoRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve the source commit.' }
    $dirty = @(& git -C $repoRoot status --porcelain=v1)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Commit the reviewed task before refreshing the desktop build; the source tree must be clean.' }
    return $commit
}
function Resolve-GlobalRuntime([version]$Minimum) {
    $candidates = @()
    foreach ($base in @($env:ProgramW6432,$env:ProgramFiles)) { if ($base) { $candidates += Join-Path $base 'dotnet' } }
    $registration = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\dotnet\Setup\InstalledVersions\x64'
    if (Test-Path -LiteralPath $registration) {
        $registered = Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue
        if ($registered -and $registered.PSObject.Properties['InstallLocation']) { $candidates += [string]$registered.InstallLocation }
    }
    $candidateRoots = @($candidates | Select-Object -Unique | ForEach-Object { Get-NormalPath $_ })
    # A persistent override is acceptable only when it names the same registered installation; any other
    # value would make the direct apphost depend on environment not controlled by this refresh.
    foreach ($scope in @('User','Machine')) {
        foreach ($variable in @('DOTNET_ROOT_X64','DOTNET_ROOT')) {
            $value = [Environment]::GetEnvironmentVariable($variable, $scope)
            if (-not $value) { continue }
            $overrideRoot = Get-NormalPath $value
            $matched = @($candidateRoots | Where-Object { $overrideRoot -ieq $_ })
            if (-not $matched.Count) { throw "Direct desktop launch requires no persistent $scope $variable override outside the registered x64 .NET installation; no global environment is changed automatically." }
        }
    }
    $found = New-Object 'System.Collections.Generic.List[object]'
    foreach ($root in $candidateRoots) {
        $shared = Join-Path $root 'shared/Microsoft.NETCore.App'
        if (-not (Test-Path -LiteralPath $shared -PathType Container)) { continue }
        $root = Assert-PhysicalPath $root
        foreach ($version in @(Get-ChildItem -LiteralPath $shared -Directory | Where-Object { $_.Name -match '^10\.[0-9]+\.[0-9]+$' } | Sort-Object { [version]$_.Name })) {
            if ([version]$version.Name -lt $Minimum) { continue }
            $core = Get-NormalPath (Join-Path $version.FullName 'coreclr.dll')
            $fxr = Get-NormalPath (Join-Path $root ('host/fxr/' + $version.Name + '/hostfxr.dll'))
            if (-not (Test-Path -LiteralPath $core -PathType Leaf) -or -not (Test-Path -LiteralPath $fxr -PathType Leaf)) { continue }
            $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($core))
            try {
                if ($reader.BaseStream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { continue }
                $reader.BaseStream.Position = 0x3c
                $pe = $reader.ReadInt32()
                if ($pe -lt 0 -or $pe + 6 -gt $reader.BaseStream.Length) { continue }
                $reader.BaseStream.Position = $pe
                if ($reader.ReadUInt32() -eq 0x4550 -and $reader.ReadUInt16() -eq 0x8664) {
                    $found.Add([pscustomobject]@{ root=$root; version=[version]$version.Name })
                }
            }
            finally { $reader.Dispose() }
        }
    }
    if (-not $found.Count) { throw "A globally installed x64 .NET 10 runtime >= $Minimum is required for direct launch. No runtime is installed or copied automatically." }
    # Framework-dependent apphost default roll-forward (Minor): the latest patch of the requested minor is
    # preferred; the lowest higher minor is used only when that minor is absent.
    $sameMinor = @($found | Where-Object { $_.version.Major -eq $Minimum.Major -and $_.version.Minor -eq $Minimum.Minor } | Sort-Object { $_.version } -Descending)
    if ($sameMinor.Count) { return [pscustomobject]@{ root=$sameMinor[0].root; version=$sameMinor[0].version.ToString() } }
    $higher = @($found | Where-Object { $_.version.Major -eq $Minimum.Major -and $_.version.Minor -gt $Minimum.Minor } | Sort-Object { $_.version } -Descending)
    if ($higher.Count) { return [pscustomobject]@{ root=$higher[0].root; version=$higher[0].version.ToString() } }
    throw "A globally installed x64 .NET 10 runtime compatible with $Minimum is required for direct launch. No runtime is installed or copied automatically."
}
if (-not ('LLPlayerDesktopTest.NativeShortcutV2' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
namespace LLPlayerDesktopTest {
    public sealed class LinkReadback {
        public string TargetPath;
        public string IconPath;
        public int IconIndex;
        public bool ShellIconAvailable;
    }
    public static class NativeShortcutV2 {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink {}
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLink {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
            void GetIDList(out IntPtr value); void SetIDList(IntPtr value);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetHotkey(out short value); void SetHotkey(short value);
            void GetShowCmd(out int value); void SetShowCmd(int value);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string text, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
        struct ShellFileInfo {
            public IntPtr icon; public int index; public uint attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string displayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=80)] public string typeName;
        }
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern void SHChangeNotify(uint action, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr second);
        public static string PhysicalPath(string path) {
            using (Microsoft.Win32.SafeHandles.SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                StringBuilder buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                string result = buffer.ToString();
                if (result.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("A local drive path is required.");
                if (result.StartsWith("\\\\?\\", StringComparison.Ordinal)) result = result.Substring(4);
                return Path.GetFullPath(result);
            }
        }
        public static LinkReadback Inspect(string shortcut, string target, string icon) {
            object instance = new ShellLink();
            try {
                ((IPersistFile)instance).Load(shortcut, 0);
                IShellLink link = (IShellLink)instance;
                link.Resolve(IntPtr.Zero, 0x39); // NO_UI | NOUPDATE | NOSEARCH | NOTRACK
                StringBuilder resolved = new StringBuilder(32768);
                StringBuilder located = new StringBuilder(32768);
                int index;
                link.GetPath(resolved, resolved.Capacity, IntPtr.Zero, 0);
                link.GetIconLocation(located, located.Capacity, out index);
                string resolvedPath = PhysicalPath(Environment.ExpandEnvironmentVariables(resolved.ToString()));
                string iconPath = PhysicalPath(Environment.ExpandEnvironmentVariables(located.ToString()));
                if (!String.Equals(resolvedPath, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(iconPath, Path.GetFullPath(icon), StringComparison.OrdinalIgnoreCase) || index != 0)
                    throw new InvalidOperationException("Native ShellLink target/icon readback differs.");
                IntPtr large, small;
                uint extracted = ExtractIconEx(icon, 0, out large, out small, 1);
                try { if (extracted == 0 || (large == IntPtr.Zero && small == IntPtr.Zero)) throw new InvalidOperationException("Windows could not extract the blue ICO."); }
                finally { if (large != IntPtr.Zero) DestroyIcon(large); if (small != IntPtr.Zero && small != large) DestroyIcon(small); }
                ShellFileInfo info = new ShellFileInfo();
                IntPtr result = SHGetFileInfo(shortcut, 0, ref info, (uint)Marshal.SizeOf(typeof(ShellFileInfo)), 0x101);
                try { if (result == IntPtr.Zero || info.icon == IntPtr.Zero) throw new InvalidOperationException("Windows Shell icon readback failed."); }
                finally { if (info.icon != IntPtr.Zero) DestroyIcon(info.icon); }
                return new LinkReadback { TargetPath=resolvedPath, IconPath=iconPath, IconIndex=index, ShellIconAvailable=true };
            }
            finally { Marshal.FinalReleaseComObject(instance); }
        }
        public static void Notify(string path) { SHChangeNotify(0x2000, 0x1005, path, IntPtr.Zero); } // own UPDATEITEM/PATHW only
    }
}
'@
}
$InstallRoot = Get-NormalPath $InstallRoot
$DesktopPath = Get-NormalPath $DesktopPath
$IconPath = Get-NormalPath $IconPath
if (-not (Test-Path -LiteralPath $DesktopPath -PathType Container)) { throw 'DesktopPath must be an existing directory.' }
if ($InstallRoot -ieq $DesktopPath -or (Test-Within $InstallRoot $DesktopPath) -or (Test-Within $DesktopPath $InstallRoot)) { throw 'InstallRoot and DesktopPath must not overlap.' }
if (-not (Test-Path -LiteralPath $IconPath -PathType Leaf)) { throw "The blue desktop icon is missing: $IconPath" }
$DesktopPath = Assert-PhysicalPath $DesktopPath
$IconPath = Assert-PhysicalPath $IconPath
$repoRoot = Assert-PhysicalPath $repoRoot
$InstallRoot = Resolve-PhysicalDestination $InstallRoot
if ($InstallRoot -ieq $DesktopPath -or (Test-Within $InstallRoot $DesktopPath) -or (Test-Within $DesktopPath $InstallRoot)) { throw 'InstallRoot and DesktopPath must not overlap.' }
if ($LegacyRootsForTests) {
    if (-not $PackageDir) { throw '-LegacyRootsForTests is a contract-fixture input and requires -PackageDir; a normal refresh only migrates the bound known legacy roots.' }
    $script:testLegacyRootAllowlist = @($LegacyRootsForTests | ForEach-Object { Get-NormalPath $_ })
}
$iconBytes = [IO.File]::ReadAllBytes($IconPath)
if ($iconBytes.Length -lt 22 -or [BitConverter]::ToUInt16($iconBytes,0) -ne 0 -or [BitConverter]::ToUInt16($iconBytes,2) -ne 1 -or [BitConverter]::ToUInt16($iconBytes,4) -eq 0) { throw 'IconPath must contain a Windows ICO image.' }
$iconHash = Get-BytesHash $iconBytes
$ownerPath = Get-NormalPath (Join-Path $InstallRoot 'owner.json')
$currentPath = Get-NormalPath (Join-Path $InstallRoot 'current.json')
$shortcut = Get-NormalPath (Join-Path $DesktopPath $shortcutName)
$shell = New-Object -ComObject WScript.Shell
function Get-LinkFields([string]$Path) {
    $link = $shell.CreateShortcut((Get-NormalPath $Path))
    try {
        $iconLocation = [string]$link.IconLocation
        $comma = $iconLocation.LastIndexOf(',')
        $iconFile = $null
        if ($comma -ge 0 -and $iconLocation.Substring($comma + 1).Trim() -eq '0') {
            $iconFile = Get-NormalPath ($iconLocation.Substring(0,$comma).Trim('"'))
        }
        return [pscustomobject]@{ target=[string]$link.TargetPath; arguments=[string]$link.Arguments; working=[string]$link.WorkingDirectory; icon=$iconFile; description=[string]$link.Description; style=[int]$link.WindowStyle }
    }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
}
function Assert-DirectLink([string]$Path, [string]$Exe, [string]$Payload, [string]$Icon) {
    $link = Get-LinkFields $Path
    if ($link.target -ine $Exe -or $link.arguments -cne '' -or $link.working -ine $Payload -or $link.icon -ine $Icon -or $link.description -cne $description -or $link.style -ne 1) {
        throw 'Refusing a foreign or edited desktop shortcut; direct COM readback differs.'
    }
}
function Test-KnownLegacyRoot([string]$Path) {
    $full = Get-NormalPath $Path
    if (@($script:testLegacyRootAllowlist) -contains $full) { return $true }
    $profile = [Environment]::GetFolderPath('UserProfile')
    $nominal = [IO.Path]::GetFullPath((Join-Path $profile 'AppData/Local/LLPlayer-Avalonia-TestBuild'))
    $cachePattern = '^' + [regex]::Escape((Join-Path $profile 'AppData/Local/Packages')) + '\\OpenAI\.Codex_[A-Za-z0-9._-]+\\LocalCache\\Local\\LLPlayer-Avalonia-TestBuild$'
    return ($full -ieq $nominal -or $full -match $cachePattern)
}
function Get-ControlledLegacyRoot($Link) {
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if ($Link.target -match '^(.*)\\builds\\[A-Za-z0-9._-]+\\payload\\LLPlayer\.Avalonia\.exe$' -and $Link.arguments -ceq '') { $candidates.Add($Matches[1]) }
    else {
        $powershell = [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'))
        if ($Link.target -ieq $powershell -and $Link.arguments -match '^-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "(.*)\\launcher\.ps1"$') {
            $candidates.Add($Matches[1])
            # A logical AppData launcher root can be unreachable outside the virtualizing host; the recorded
            # working directory is the same physical install root and must still resolve into a bound root.
            if ($Link.working) { $candidates.Add($Link.working) }
        }
    }
    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        try {
            $normal = Get-NormalPath $candidate
            if (-not (Test-KnownLegacyRoot $normal)) { continue }
            $physical = [LLPlayerDesktopTest.NativeShortcutV2]::PhysicalPath($normal)
            if (-not (Test-KnownLegacyRoot $physical)) { continue }
            return Get-NormalPath $physical
        }
        catch { continue }
    }
    throw 'Refusing a foreign desktop shortcut outside the controlled legacy roots.'
}
function Convert-LegacyPath([string]$Path, [string]$RecordedRoot, [string]$PhysicalRoot) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -ieq $RecordedRoot) { return Get-NormalPath $PhysicalRoot }
    if (Test-Within $RecordedRoot $full) { return Get-NormalPath (Join-Path $PhysicalRoot $full.Substring($RecordedRoot.Length + 1)) }
    if ($full -ieq $PhysicalRoot -or (Test-Within $PhysicalRoot $full)) { return Get-NormalPath $full }
    throw 'A legacy record points outside its verified install root.'
}
$stagedLink = $null
try {
    $destinationOwner = $null; $oldOwner = $null; $oldState = $null; $oldReceipt = $null; $legacyVariant = $null
    $stateRoot = $InstallRoot; $stateOwnerPath = $ownerPath; $stateCurrentPath = $currentPath
    if (Test-Path -LiteralPath $InstallRoot) {
        if (-not (Test-Path -LiteralPath $InstallRoot -PathType Container)) { throw 'InstallRoot must be a directory.' }
        if (Test-Path -LiteralPath $ownerPath) {
            $destinationOwner = Read-Json $ownerPath
            $ownerBound = $destinationOwner.installRoot -ieq $InstallRoot -or ($destinationOwner.schema -eq 1 -and (Test-KnownLegacyRoot $destinationOwner.installRoot) -and (Test-KnownLegacyRoot $InstallRoot))
            if ($destinationOwner.owner -ne $ownerTag -or $destinationOwner.schema -notin @(1,2) -or -not $ownerBound) { throw 'InstallRoot has a foreign or incompatible owner record.' }
        }
        elseif (@(Get-ChildItem -LiteralPath $InstallRoot -Force).Count -gt 0) { throw 'Refusing to claim a non-empty foreign InstallRoot.' }
    }
    if ($destinationOwner -and -not (Test-Path -LiteralPath $currentPath)) { throw 'The managed install root has an owner record but no current.json; refusing to guess a build.' }
    if ($destinationOwner -and (Test-Path -LiteralPath $currentPath)) { $oldOwner = $destinationOwner }
    elseif (Test-Path -LiteralPath $shortcut) {
        $stateRoot = Get-ControlledLegacyRoot (Get-LinkFields $shortcut)
        $stateOwnerPath = Get-NormalPath (Join-Path $stateRoot 'owner.json')
        $stateCurrentPath = Get-NormalPath (Join-Path $stateRoot 'current.json')
        $oldOwner = Read-Json $stateOwnerPath
        if ($oldOwner.owner -ne $ownerTag -or $oldOwner.schema -ne 1) { throw 'External migration accepts only the verified original schema1 install.' }
    }
    if ($oldOwner) {
        $recordedRoot = [IO.Path]::GetFullPath([string]$oldOwner.installRoot).TrimEnd('\')
        $rootBound = (Test-KnownLegacyRoot $recordedRoot) -and (Test-KnownLegacyRoot $stateRoot)
        if ($recordedRoot -ine $stateRoot -and -not $rootBound) { throw 'The old owner record is not bound to the legacy install root.' }
        if ($oldOwner.schema -eq 1) {
            $legacyLauncher = Convert-LegacyPath (Join-Path $recordedRoot 'launcher.ps1') $recordedRoot $stateRoot
            $legacyIcon = Convert-LegacyPath (Join-Path $recordedRoot 'blue.ico') $recordedRoot $stateRoot
            if ((Get-Hash $legacyLauncher) -cne $oldOwner.launcherSha256 -or (Get-Hash $legacyIcon) -cne $oldOwner.iconSha256) { throw 'The managed schema1 launcher or icon was edited; preserving it.' }
        }
        $oldState = Read-Json $stateCurrentPath
        if ($oldState.owner -ne $ownerTag -or $oldState.schema -ne $oldOwner.schema -or $oldState.buildId -notmatch '^[A-Za-z0-9._-]+$' -or $oldState.buildId -in @('.','..')) { throw 'The existing current.json is foreign or invalid.' }
        $oldBuild = Get-NormalPath (Join-Path $stateRoot ('builds/' + $oldState.buildId))
        $oldReceiptPath = Get-NormalPath (Join-Path $oldBuild 'receipt.json')
        $recordedReceipt = Convert-LegacyPath $oldState.receiptPath $recordedRoot $stateRoot
        if ($recordedReceipt -ine $oldReceiptPath -or (Get-Hash $oldReceiptPath) -cne $oldState.receiptSha256) { throw 'The existing desktop build receipt is invalid.' }
        $oldReceipt = Read-Json $oldReceiptPath
        $oldPayload = Get-NormalPath (Join-Path $oldBuild 'payload')
        $oldExe = Get-NormalPath (Join-Path $oldPayload 'LLPlayer.Avalonia.exe')
        $recordedPayload = Convert-LegacyPath $oldReceipt.payloadPath $recordedRoot $stateRoot
        $recordedExe = Convert-LegacyPath $oldReceipt.executablePath $recordedRoot $stateRoot
        if ($oldReceipt.owner -ne $ownerTag -or $oldReceipt.schema -ne $oldOwner.schema -or $oldReceipt.buildId -cne $oldState.buildId -or $oldReceipt.sourceCommit -cne $oldState.sourceCommit -or $recordedPayload -ine $oldPayload -or $recordedExe -ine $oldExe -or $oldReceipt.shortcutPath -ine $shortcut -or (Get-Hash $oldExe) -cne $oldReceipt.executableSha256) { throw 'The existing payload identity is invalid.' }
        if ($oldOwner.schema -eq 2) {
            $oldBlue = Get-NormalPath (Join-Path $oldBuild 'blue.ico')
            if ($oldReceipt.iconPath -ine $oldBlue -or (Get-Hash $oldBlue) -cne $oldReceipt.iconSha256) { throw 'The selected per-build icon was edited.' }
            if ($oldReceipt.PSObject.Properties['workingDirectory'] -and [string]$oldReceipt.workingDirectory -ine $oldPayload) { throw 'The existing build receipt working directory differs from its payload.' }
            if (Test-Path -LiteralPath $shortcut) {
                Assert-DirectLink $shortcut $oldExe $oldPayload $oldBlue
                if ((Get-Hash $shortcut) -cne $oldReceipt.shortcutSha256) { throw 'The selected build shortcut differs from its receipt.' }
            }
            foreach ($field in @(@('executablePath',$oldExe), @('workingDirectory',$oldPayload), @('iconPath',$oldBlue), @('shortcutPath',$shortcut), @('shortcutSha256',$oldReceipt.shortcutSha256))) {
                if (-not $oldState.PSObject.Properties[$field[0]]) { throw "The existing current.json is missing the $($field[0]) identity field." }
                if ([string]$oldState.($field[0]) -ine $field[1]) { throw "The existing current.json $($field[0]) differs from the selected build." }
            }
        }
        elseif (Test-Path -LiteralPath $shortcut) {
            $link = Get-LinkFields $shortcut
            $linkIcon = if ($link.icon) { Convert-LegacyPath $link.icon $recordedRoot $stateRoot } else { $null }
            $linkWorking = Convert-LegacyPath $link.working $recordedRoot $stateRoot
            $powershell = [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'))
            $legacyArguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f (Join-Path $recordedRoot 'launcher.ps1')
            if ($link.target -ieq $powershell -and $link.arguments -ceq $legacyArguments -and $linkWorking -ieq $stateRoot -and $linkIcon -ieq $legacyIcon -and $link.description -ceq $description -and $link.style -eq 7) { $legacyVariant = 'powershell' }
            else {
                $linkTarget = Convert-LegacyPath $link.target $recordedRoot $stateRoot
                $oldBuildIcon = Get-NormalPath (Join-Path $oldBuild 'blue.ico')
                $dibRepairIcon = Get-NormalPath (Join-Path $stateRoot 'desktop-blue-dib.ico')
                $legacyImage = ($linkIcon -ieq $legacyIcon -or $linkIcon -ieq $oldBuildIcon) -and $linkIcon -and (Get-Hash $linkIcon) -ceq $oldOwner.iconSha256
                $newImage = $linkIcon -ieq $dibRepairIcon -and $linkIcon -and (Get-Hash $linkIcon) -ceq $iconHash
                if ($linkTarget -ine $oldExe -or $link.arguments -cne '' -or $linkWorking -ine $oldPayload -or $link.description -cne $description -or $link.style -ne 1 -or (-not $legacyImage -and -not $newImage)) { throw 'Refusing a foreign or edited schema1 desktop shortcut.' }
                $legacyVariant = if ($newImage) { 'direct-current-source-icon' } else { 'direct-legacy-icon' }
            }
        }
    }
    $beforeLink = Get-Snapshot $shortcut
    $beforePointer = Get-Snapshot $currentPath
    $beforeOwner = Get-Snapshot $ownerPath
    $sourceCommit = $null; $sourceArchive = $null; $archiveHash = $null; $buildMode = 'prebuilt-validated'
    if ($PackageDir) {
        $package = Assert-PhysicalPath $PackageDir
        if ($package -ieq $InstallRoot -or (Test-Within $package $InstallRoot) -or (Test-Within $InstallRoot $package)) { throw 'PackageDir and InstallRoot must not overlap.' }
    }
    else {
        $buildMode = 'fresh-published'
        $sourceCommit = Get-CleanCommit
        $job = Join-Path $repoRoot ('artifacts/windows-desktop-test-build-' + [guid]::NewGuid().ToString('N'))
        $staging = Join-Path $job 'staging'
        $savedRoot = $env:DOTNET_ROOT; $savedRootX64 = $env:DOTNET_ROOT_X64; $savedPath = $env:PATH
        try {
            $sdk = if ($DotNetRoot) { Get-NormalPath $DotNetRoot } elseif ($env:DOTNET_ROOT_X64) { Get-NormalPath $env:DOTNET_ROOT_X64 } elseif ($env:DOTNET_ROOT) { Get-NormalPath $env:DOTNET_ROOT } else { $null }
            if ($sdk) {
                if (-not (Test-Path -LiteralPath (Join-Path $sdk 'dotnet.exe') -PathType Leaf)) { throw 'DotNetRoot must contain the build SDK host.' }
                $env:DOTNET_ROOT = $sdk; $env:DOTNET_ROOT_X64 = $sdk; $env:PATH = $sdk + ';' + $savedPath
            }
            # The publisher forwards native `dotnet publish` stdout into this pipeline; select its archive output.
            $publishedRaw = @(& (Join-Path $PSScriptRoot 'publish-avalonia.ps1') -OutDir (Join-Path $job 'archive') -StagingDir $staging)
        }
        finally { $env:DOTNET_ROOT = $savedRoot; $env:DOTNET_ROOT_X64 = $savedRootX64; $env:PATH = $savedPath }
        $published = @($publishedRaw | Where-Object { $_ -is [string] -and $_.Trim().EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase) })
        if ($published.Count -ne 1) { throw 'The Avalonia publisher did not return exactly one archive path.' }
        if ((Get-CleanCommit) -cne $sourceCommit) { throw 'Source HEAD changed while publishing; desktop selection is unchanged.' }
        $sourceArchive = Assert-PhysicalPath ([string]$published[0])
        $archiveHash = Get-Hash $sourceArchive
        $packages = @(Get-ChildItem -LiteralPath $staging -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'LLPlayer.Avalonia.exe') -PathType Leaf })
        if ($packages.Count -ne 1) { throw 'The publisher did not produce one Avalonia package.' }
        $package = Assert-PhysicalPath $packages[0].FullName
    }
    & (Join-Path $PSScriptRoot 'validate-avalonia-package.ps1') -PackageDir $package | Out-Null
    $config = Read-Json (Join-Path $package 'LLPlayer.Avalonia.runtimeconfig.json')
    if ($config.runtimeOptions.PSObject.Properties['frameworks']) { throw 'The package must declare exactly one Microsoft.NETCore.App 10 framework.' }
    if ($config.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App' -or ([version]$config.runtimeOptions.framework.version).Major -ne 10) { throw 'The package must target Microsoft.NETCore.App 10.' }
    if ($config.runtimeOptions.PSObject.Properties['rollForward'] -or $config.runtimeOptions.PSObject.Properties['applyPatches']) { throw 'Custom runtime roll-forward settings need a separate direct-launch compatibility decision.' }
    if (@(Get-ChildItem -LiteralPath $package -Recurse -File -Filter '*.runtimeconfig.dev.json').Count) { throw 'A runtimeconfig.dev.json build artifact is not allowed in a deployed package.' }
    $depsPath = Join-Path $package 'LLPlayer.Avalonia.deps.json'
    if (Test-Path -LiteralPath $depsPath -PathType Leaf) {
        $deps = Read-Json $depsPath
        $depsTarget = $null
        if ($deps.PSObject.Properties['runtimeTarget'] -and $deps.runtimeTarget.PSObject.Properties['name']) { $depsTarget = [string]$deps.runtimeTarget.name }
        if ($depsTarget -and $depsTarget -notmatch 'Version=v10\.0') { throw 'The package deps.json runtime target does not match the .NET 10 runtimeconfig.' }
    }
    $runtime = Resolve-GlobalRuntime ([version]$config.runtimeOptions.framework.version)
    $installRootExisted = Test-Path -LiteralPath $InstallRoot
    $build = $null; $buildId = $null; $ownerBytes = $null; $pointerBytes = $null; $newLinkHash = $null; $receiptPath = $null
    [IO.Directory]::CreateDirectory($InstallRoot) | Out-Null
    $InstallRoot = Assert-PhysicalPath $InstallRoot
    try {
        $prefix = if ($sourceCommit) { $sourceCommit.Substring(0,12) } else { 'prebuilt' }
        $buildId = $prefix + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
        $build = Get-NormalPath (Join-Path $InstallRoot ('builds/' + $buildId))
        if (Test-Path -LiteralPath $build) { throw "The new immutable build directory already exists: $build" }
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
                if ((Get-Hash $target) -cne $hash) { throw "Installed file differs from package: $relative" }
                $files.Add([ordered]@{ path=$relative.Replace('\','/'); bytes=$item.Length; sha256=$hash })
            }
        }
        & (Join-Path $PSScriptRoot 'validate-avalonia-package.ps1') -PackageDir $payload | Out-Null
        $payload = Assert-PhysicalPath $payload
        $exe = Assert-PhysicalPath (Join-Path $payload 'LLPlayer.Avalonia.exe')
        $exeHash = Get-Hash $exe
        $blue = Get-NormalPath (Join-Path $build 'blue.ico')
        [IO.File]::WriteAllBytes($blue, $iconBytes)
        $blue = Assert-PhysicalPath $blue
        if ((Get-Hash $blue) -cne $iconHash) { throw 'The installed per-build blue icon differs from the validated source icon.' }
        $stagedLink = Get-NormalPath (Join-Path $DesktopPath ('.LLPlayer-Avalonia-' + [guid]::NewGuid().ToString('N') + '.lnk'))
        $link = $shell.CreateShortcut($stagedLink)
        try {
            $link.TargetPath = $exe; $link.Arguments = ''; $link.WorkingDirectory = $payload
            $link.IconLocation = $blue + ',0'; $link.Description = $description; $link.WindowStyle = 1
            $link.Save()
        }
        finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
        Assert-DirectLink $stagedLink $exe $payload $blue
        $native = [LLPlayerDesktopTest.NativeShortcutV2]::Inspect($stagedLink, $exe, $blue)
        $newLinkBytes = [IO.File]::ReadAllBytes($stagedLink)
        $newLinkHash = Get-BytesHash $newLinkBytes
        $newOwner = [ordered]@{ schema=2; owner=$ownerTag; installRoot=$InstallRoot; shortcutName=$shortcutName }
        $legacyMigration = $null
        if ($oldOwner -and $oldOwner.schema -eq 1) {
            $legacyMigration = [ordered]@{
                variant=$legacyVariant; sourceInstallRoot=$stateRoot; sourceRecordedInstallRoot=$recordedRoot
                sourceOwnerSha256=(Get-Hash $stateOwnerPath); sourceCurrentSha256=(Get-Hash $stateCurrentPath)
                sourceShortcutPath=$shortcut; sourceShortcutSha256=$beforeLink.hash
                previousBuildId=$oldState.buildId; previousSourceCommit=$oldState.sourceCommit
                launcherPath=$legacyLauncher; launcherSha256=$oldOwner.launcherSha256
                iconPath=$legacyIcon; iconSha256=$oldOwner.iconSha256
            }
            $newOwner['migratedFromSchema'] = 1
            $newOwner['legacy'] = $legacyMigration
        }
        elseif ($oldOwner -and $oldOwner.PSObject.Properties['legacy']) { $newOwner['legacy'] = $oldOwner.legacy }
        $ownerBytes = $utf8.GetBytes(($newOwner | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        $receipt = [ordered]@{
            schema=2; owner=$ownerTag; buildId=$buildId; buildMode=$buildMode; sourceCommit=$sourceCommit
            createdUtc=[DateTime]::UtcNow.ToString('o'); repository=$repoRoot; packageSource=$package
            sourceArchive=$sourceArchive; sourceArchiveSha256=$archiveHash
            payloadPath=$payload; executablePath=$exe; executableSha256=$exeHash
            workingDirectory=$payload; targetArguments=''
            globalRuntimeRoot=$runtime.root; globalRuntimeVersion=$runtime.version
            iconPath=$blue; iconSha256=$iconHash; shortcutPath=$shortcut; shortcutSha256=$newLinkHash
            shortcutDescription=$description; shortcutWindowStyle=1
            legacyMigration=$legacyMigration
            nativeReadback=$native; files=@($files.ToArray())
        }
        $receiptPath = Join-Path $build 'receipt.json'
        [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 5) + [Environment]::NewLine, $utf8)
        $receiptHash = Get-Hash $receiptPath
        $pointer = [ordered]@{
            schema=2; owner=$ownerTag; buildId=$buildId; buildMode=$buildMode; sourceCommit=$sourceCommit
            receiptPath=$receiptPath; receiptSha256=$receiptHash
            executablePath=$exe; executableSha256=$exeHash; workingDirectory=$payload
            iconPath=$blue; iconSha256=$iconHash; shortcutPath=$shortcut; shortcutSha256=$newLinkHash
        }
        $pointerBytes = $utf8.GetBytes(($pointer | ConvertTo-Json) + [Environment]::NewLine)
        Assert-Snapshot $beforeLink; Assert-Snapshot $beforePointer; Assert-Snapshot $beforeOwner
        if ($sourceCommit -and (Get-CleanCommit) -cne $sourceCommit) { throw 'Source HEAD changed before selection; the previous build is retained.' }
        if (Test-Path -LiteralPath $shortcut) { [IO.File]::Replace($stagedLink, $shortcut, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($stagedLink, $shortcut) }
        Write-Atomic $currentPath $pointerBytes
        Write-Atomic $ownerPath $ownerBytes
        Assert-DirectLink $shortcut $exe $payload $blue
        [void][LLPlayerDesktopTest.NativeShortcutV2]::Inspect($shortcut, $exe, $blue)
    }
    catch {
        $failure = $_
        $rollbackFailures = @()
        $written = @(
            @($beforeOwner, $(if ($null -ne $ownerBytes) { Get-BytesHash $ownerBytes } else { $null })),
            @($beforePointer, $(if ($null -ne $pointerBytes) { Get-BytesHash $pointerBytes } else { $null })),
            @($beforeLink, $newLinkHash)
        )
        foreach ($item in $written) {
            if ($null -eq $item[1]) { continue }
            try { Restore-Snapshot $item[0] $item[1] }
            catch { $rollbackFailures += $_.Exception.Message }
        }
        if ($build) {
            $safeBuild = Get-NormalPath $build
            $expectedBuild = Get-NormalPath (Join-Path $InstallRoot ('builds/' + $buildId))
            if ($safeBuild -cne $expectedBuild) { $rollbackFailures += 'Unsafe build rollback target.' }
            elseif ((Test-Path -LiteralPath $safeBuild -PathType Container) -and
                (-not $installRootExisted -or -not (Test-Path -LiteralPath (Join-Path $safeBuild 'receipt.json') -PathType Leaf))) {
                # A complete unselected build is retained as failed-refresh evidence only in an existing managed root.
                try { [IO.Directory]::Delete($safeBuild, $true) } catch { $rollbackFailures += $_.Exception.Message }
            }
        }
        if (-not $destinationOwner) {
            $buildsDir = Get-NormalPath (Join-Path $InstallRoot 'builds')
            if ((Test-Path -LiteralPath $buildsDir -PathType Container) -and @(Get-ChildItem -LiteralPath $buildsDir -Force).Count -eq 0) {
                # Return a pre-existing empty destination to its empty state so the next attempt is not refused as foreign.
                try { [IO.Directory]::Delete($buildsDir, $false) } catch { $rollbackFailures += $_.Exception.Message }
            }
        }
        if (-not $installRootExisted -and (Test-Path -LiteralPath $InstallRoot -PathType Container)) {
            $unexpected = @(Get-ChildItem -LiteralPath $InstallRoot -Force | Where-Object { $_.Name -notin @('owner.json', 'current.json', 'builds') })
            if ($unexpected.Count -eq 0) { try { [IO.Directory]::Delete($InstallRoot, $true) } catch { $rollbackFailures += $_.Exception.Message } }
            else { $rollbackFailures += "Refusing to delete an unexpected new install root: $InstallRoot" }
        }
        if ($rollbackFailures.Count) { throw ($failure.Exception.Message + '; rollback: ' + ($rollbackFailures -join '; ')) }
        throw $failure
    }
    [LLPlayerDesktopTest.NativeShortcutV2]::Notify($shortcut)
    Write-Host "Desktop direct test build selected: $buildId ($buildMode). The player was not launched."
    Write-Output ([pscustomobject]@{
        currentPath=$currentPath; ownerPath=$ownerPath; receiptPath=$receiptPath; shortcutPath=$shortcut; payloadPath=$payload
        sourceCommit=$sourceCommit; buildMode=$buildMode; schema=2; buildId=$buildId
        receiptSha256=$receiptHash; shortcutSha256=$newLinkHash; executableSha256=$exeHash; iconSha256=$iconHash
        legacyVariant=$(if ($legacyMigration) { $legacyMigration.variant } else { $null })
    })
}
finally {
    if ($stagedLink -and (Test-Path -LiteralPath $stagedLink -PathType Leaf)) {
        $safeStaged = Get-NormalPath $stagedLink
        if ([IO.Path]::GetDirectoryName($safeStaged) -ine $DesktopPath -or [IO.Path]::GetFileName($safeStaged) -notmatch '^\.LLPlayer-Avalonia-[0-9a-f]{32}\.lnk$') { throw 'Unsafe temporary shortcut cleanup.' }
        [IO.File]::Delete($safeStaged)
    }
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

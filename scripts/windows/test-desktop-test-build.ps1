param([string]$DotNetRoot = $env:DOTNET_ROOT)
# Offline filesystem/COM contracts for the direct schema-2 Windows desktop test build.
# All runs use -PackageDir fixtures: these checks never claim a fresh publish of HEAD and never launch the player.
# Fixtures live under ignored artifacts/. The real default root and the real desktop shortcut are not touched here.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Desktop shortcut contract tests require Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$updater = Join-Path $PSScriptRoot 'update-desktop-test-build.ps1'
$work = Join-Path $repoRoot ('artifacts/windows-desktop-build-tests-' + [guid]::NewGuid().ToString('N'))
$utf8 = New-Object Text.UTF8Encoding($false)
$ownerTag = 'LLPlayer.Avalonia.DesktopTest'
$description = 'LLPlayer native Windows Avalonia test build; managed by update-desktop-test-build.ps1.'
$shortcutName = 'LLPlayer Avalonia (Windows).lnk'
$legacySourceCommit = '4756c2ea357a156d7aeebd487792acbbe9c4dab0'
$script:passed = 0
[IO.Directory]::CreateDirectory($work) | Out-Null
if (-not ('DesktopTestProbe.Physical' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;
namespace DesktopTestProbe {
    public static class Physical {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        public static string Path(string path) {
            using (SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                StringBuilder buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                string result = buffer.ToString();
                if (result.StartsWith("\\\\?\\", StringComparison.Ordinal)) result = result.Substring(4);
                return System.IO.Path.GetFullPath(result);
            }
        }
    }
    public sealed class LinkReadback {
        public string TargetPath;
        public string WorkingDirectory;
        public string Arguments;
        public string IconPath;
        public int IconIndex;
        public bool IconExtracted;
    }
    public static class ShellLinkProbe {
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
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static LinkReadback Read(string shortcut) {
            object instance = new ShellLink();
            try {
                ((IPersistFile)instance).Load(shortcut, 0);
                IShellLink link = (IShellLink)instance;
                StringBuilder target = new StringBuilder(32768); StringBuilder working = new StringBuilder(32768);
                StringBuilder arguments = new StringBuilder(32768); StringBuilder icon = new StringBuilder(32768);
                int iconIndex;
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                link.GetWorkingDirectory(working, working.Capacity);
                link.GetArguments(arguments, arguments.Capacity);
                link.GetIconLocation(icon, icon.Capacity, out iconIndex);
                string iconPath = Environment.ExpandEnvironmentVariables(icon.ToString());
                IntPtr large, small;
                uint extracted = ExtractIconEx(iconPath, iconIndex, out large, out small, 1);
                try {
                    return new LinkReadback {
                        TargetPath = Environment.ExpandEnvironmentVariables(target.ToString()),
                        WorkingDirectory = Environment.ExpandEnvironmentVariables(working.ToString()),
                        Arguments = arguments.ToString(), IconPath = iconPath,
                        IconIndex = iconIndex, IconExtracted = extracted != 0 && (large != IntPtr.Zero || small != IntPtr.Zero)
                    };
                }
                finally { if (large != IntPtr.Zero) DestroyIcon(large); if (small != IntPtr.Zero && small != large) DestroyIcon(small); }
            }
            finally { Marshal.FinalReleaseComObject(instance); }
        }
    }
}
'@
}
$shell = New-Object -ComObject WScript.Shell
function Write-File([string]$Root, [string]$Relative, [string]$Text = 'fixture') {
    $path = Join-Path $Root $Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Text, $utf8)
}
function Write-Json([string]$Path, $Object) { [IO.File]::WriteAllText($Path, ($Object | ConvertTo-Json -Depth 6) + [Environment]::NewLine, $utf8) }
function Read-Json([string]$Path) { return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message }; $script:passed++ }
function Assert-Rejected([scriptblock]$Action, [string]$Pattern) {
    try { & $Action | Out-Null }
    catch {
        if ($Pattern -and $_.Exception.Message -notmatch $Pattern) { throw "Rejected for wrong reason: $($_.Exception.Message)" }
        $script:passed++
        return
    }
    throw 'An unsafe deployment was accepted.'
}
function Assert-Physical([string]$Path, [string]$Label) {
    Assert-True (-not [string]::IsNullOrWhiteSpace($Path)) "$Label is empty."
    $full = [IO.Path]::GetFullPath($Path)
    Assert-True (-not $full.StartsWith('\\?\')) "$Label uses a device path: $full"
    Assert-True ([DesktopTestProbe.Physical]::Path($full) -ieq $full) "$Label is not a physical/canonical path: $full"
}
function Copy-Tree([string]$Source, [string]$Destination) {
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $Source -Force)) {
        $target = Join-Path $Destination $item.Name
        if ($item.PSIsContainer) { Copy-Tree $item.FullName $target } else { [IO.File]::Copy($item.FullName, $target, $false) }
    }
}
function New-Desktop([string]$Name) {
    $path = Join-Path $work $Name
    [IO.Directory]::CreateDirectory($path) | Out-Null
    return $path
}
function New-Link([string]$Path, [string]$Target, [string]$Arguments, [string]$Working, [string]$IconLocation, [int]$Style) {
    $link = $shell.CreateShortcut($Path)
    try {
        $link.TargetPath = $Target; $link.Arguments = $Arguments; $link.WorkingDirectory = $Working
        $link.IconLocation = $IconLocation; $link.Description = $description; $link.WindowStyle = $Style
        $link.Save()
    }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
}
function Get-Link([string]$Path) {
    $link = $shell.CreateShortcut($Path)
    try {
        $iconLocation = [string]$link.IconLocation
        $comma = $iconLocation.LastIndexOf(',')
        $iconFile = if ($comma -ge 0) { $iconLocation.Substring(0, $comma).Trim('"') } else { $iconLocation }
        $iconIndex = if ($comma -ge 0) { [int]$iconLocation.Substring($comma + 1) } else { 0 }
        return [pscustomobject]@{
            target = [string]$link.TargetPath; arguments = [string]$link.Arguments; working = [string]$link.WorkingDirectory
            iconFile = $iconFile; iconIndex = $iconIndex; description = [string]$link.Description; style = [int]$link.WindowStyle
        }
    }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
}
function Get-NativeLink([string]$Path) { return [DesktopTestProbe.ShellLinkProbe]::Read($Path) }
function New-Package([string]$Name, [switch]$OmitCore) {
    $dir = Join-Path $work $Name
    $native = @('av_libglesv2.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'e_sqlite3.dll', 'soft_oal.dll') +
        @('ggml-base-whisper.dll', 'ggml-cpu-whisper.dll', 'ggml-whisper.dll', 'whisper.dll' | ForEach-Object { 'runtimes/win-x64/' + $_ }) +
        @('avcodec-62.dll', 'avdevice-62.dll', 'avfilter-11.dll', 'avformat-62.dll', 'avutil-60.dll', 'swresample-6.dll', 'swscale-9.dll' | ForEach-Object { 'FFmpeg/' + $_ })
    $required = @('LLPlayer.Avalonia.exe', 'LLPlayer.Avalonia.dll', 'FlyleafLib.dll', 'Assets/silero_vad.onnx', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
        'FFmpeg/LICENSE.GPLv3.txt', 'FFmpeg/SOURCE.txt', 'licenses/OpenALSoft.LICENSE', 'licenses/OpenALSoft.SOURCE.txt',
        'licenses/Avalonia.ANGLE.LICENSE', 'licenses/OpenALSoft.BSD-3Clause.txt', 'dub_sidecar/server.py', 'dub_sidecar/pyproject.toml', 'dub_sidecar/uv.lock', 'dub_sidecar/README.md')
    if (-not $OmitCore) { $required += 'LLPlayer.Core.dll' }
    foreach ($file in @($required) + @($native)) { Write-File $dir $file ($Name + ':' + $file) }
    Write-File $dir 'LLPlayer.Avalonia.deps.json' '{}'
    Write-File $dir 'LLPlayer.Avalonia.runtimeconfig.json' '{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'
    $manifest = [ordered]@{
        schema = 1; runtime = 'win-x64'; components = @(
            @{ name = 'FFmpeg'; version = 'fixture'; license = 'GPL'; source = 'fixture'; provenance = 'Synthetic offline contract fixture' },
            @{ name = 'OpenAL Soft'; version = '1.23.1'; license = 'LGPL'; source = 'fixture'; provenance = 'Synthetic offline contract fixture' }
        )
        files = @($native | ForEach-Object { @{ path = $_; sha256 = (Hash (Join-Path $dir $_)) } })
    }
    Write-File $dir 'native-dependencies.json' ($manifest | ConvertTo-Json -Depth 5)
    return $dir
}
# A real one-pixel BGRA ICO (ICONDIR + ICONDIRENTRY + BITMAPINFOHEADER + XOR + AND).
$icon = Join-Path $work 'fixture-blue.ico'
$stream = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]1); $writer.Write([byte]1); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]48); $writer.Write([uint32]22)
    $writer.Write([uint32]40); $writer.Write([int32]1); $writer.Write([int32]2)
    $writer.Write([uint16]1); $writer.Write([uint16]16); $writer.Write([uint32]0); $writer.Write([uint32]4)
    1..4 | ForEach-Object { $writer.Write([uint32]0) }
    $writer.Write([byte[]]@(230, 148, 50, 255, 0, 0, 0, 0))
    [IO.File]::WriteAllBytes($icon, $stream.ToArray())
}
finally { $writer.Dispose() }
$iconHash = Hash $icon
function New-LegacyInstall([string]$Name, [string]$Variant, [string]$Desktop) {
    # Reproduces the verified 2026-10-25 schema-1 installation shape: owner/current/receipt record the logical
    # AppData root while the files physically live in the resolved backing directory; the shortcut was repaired to
    # the physical direct EXE with the physical blue icon (or still uses the original PowerShell launcher).
    $physical = New-Desktop $Name
    $logical = [IO.Path]::GetFullPath((Join-Path (Join-Path $work 'legacy-recorded') $Name))
    $buildId = 'aaaaaaaaaaaa-20261025T000000000Z-11111111'
    $payload = Join-Path $physical ('builds/' + $buildId + '/payload')
    Write-File $payload 'LLPlayer.Avalonia.exe' ($Name + ':legacy executable')
    Write-File $payload 'LLPlayer.Avalonia.dll' ($Name + ':legacy library')
    Write-File $physical 'launcher.ps1' ('# legacy schema1 launcher: ' + $Name)
    [IO.File]::Copy($icon, (Join-Path $physical 'blue.ico'))
    [IO.File]::Copy($icon, (Join-Path $physical 'desktop-blue-dib.ico'))
    $logicalBuild = Join-Path $logical ('builds/' + $buildId)
    $launcher = Join-Path $physical 'launcher.ps1'
    $blueIco = Join-Path $physical 'blue.ico'
    $dibIco = Join-Path $physical 'desktop-blue-dib.ico'
    $exe = Join-Path $payload 'LLPlayer.Avalonia.exe'
    $shortcut = Join-Path $Desktop $shortcutName
    $receiptPath = Join-Path $physical ('builds/' + $buildId + '/receipt.json')
    $receipt = [ordered]@{
        schema = 1; owner = $ownerTag; buildId = $buildId; buildMode = 'fresh-published'; sourceCommit = $legacySourceCommit
        payloadPath = (Join-Path $logicalBuild 'payload'); executablePath = (Join-Path $logicalBuild 'payload/LLPlayer.Avalonia.exe')
        executableSha256 = (Hash $exe); launcherPath = (Join-Path $logical 'launcher.ps1'); launcherSha256 = (Hash $launcher)
        iconPath = (Join-Path $logical 'blue.ico'); iconSha256 = (Hash $blueIco); shortcutPath = $shortcut
    }
    Write-Json $receiptPath $receipt
    $current = [ordered]@{
        schema = 1; owner = $ownerTag; buildId = $buildId; buildMode = 'fresh-published'; sourceCommit = $legacySourceCommit
        receiptPath = (Join-Path $logicalBuild 'receipt.json'); receiptSha256 = (Hash $receiptPath)
    }
    Write-Json (Join-Path $physical 'current.json') $current
    $owner = [ordered]@{ schema = 1; owner = $ownerTag; installRoot = $logical; launcherSha256 = (Hash $launcher); iconSha256 = (Hash $blueIco) }
    Write-Json (Join-Path $physical 'owner.json') $owner
    if ($Variant -eq 'powershell') {
        $arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $logical 'launcher.ps1') + '"'
        New-Link $shortcut ([IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'))) $arguments $physical ($blueIco + ',0') 7
    }
    elseif ($Variant -eq 'legacy-icon') {
        New-Link $shortcut $exe '' $payload ($blueIco + ',0') 1
    }
    else {
        New-Link $shortcut $exe '' $payload ($dibIco + ',0') 1
    }
    return [pscustomobject]@{
        physical = $physical; logical = $logical; buildId = $buildId; payload = $payload; exe = $exe; variant = $Variant
        launcher = $launcher; blueIco = $blueIco; dibIco = $dibIco; receiptPath = $receiptPath
        ownerPath = (Join-Path $physical 'owner.json'); currentPath = (Join-Path $physical 'current.json'); shortcutPath = $shortcut
    }
}
function Get-LegacyHashes($Legacy) {
    return [pscustomobject]@{
        launcher = (Hash $Legacy.launcher); blue = (Hash $Legacy.blueIco); dib = (Hash $Legacy.dibIco)
        owner = (Hash $Legacy.ownerPath); current = (Hash $Legacy.currentPath); receipt = (Hash $Legacy.receiptPath)
        exe = (Hash $Legacy.exe); shortcut = (Hash $Legacy.shortcutPath)
    }
}
function Assert-LegacyRootUnchanged($Legacy, $Before, [string]$Message) {
    $after = Get-LegacyHashes $Legacy
    Assert-True ($after.launcher -ceq $Before.launcher -and $after.blue -ceq $Before.blue -and $after.dib -ceq $Before.dib -and
        $after.owner -ceq $Before.owner -and $after.current -ceq $Before.current -and $after.receipt -ceq $Before.receipt -and
        $after.exe -ceq $Before.exe) $Message
}
function Assert-LegacyUnchanged($Legacy, $Before, [string]$Message) {
    Assert-LegacyRootUnchanged $Legacy $Before $Message
    Assert-True ((Hash $Legacy.shortcutPath) -ceq $Before.shortcut) $Message
}
function Get-StateHashes([string]$InstallRoot, [string]$DesktopPath) {
    $currentPath = Join-Path $InstallRoot 'current.json'
    $icon = $null
    if (Test-Path -LiteralPath $currentPath -PathType Leaf) {
        $state = Read-Json $currentPath
        if ($state.PSObject.Properties['iconPath'] -and (Test-Path -LiteralPath $state.iconPath -PathType Leaf)) { $icon = Hash $state.iconPath }
    }
    return [pscustomobject]@{
        current = (Hash $currentPath); owner = (Hash (Join-Path $InstallRoot 'owner.json'))
        shortcut = (Hash (Join-Path $DesktopPath $shortcutName)); icon = $icon
    }
}
function Assert-StateUnchanged($Before, [string]$InstallRoot, [string]$DesktopPath, [string]$Message) {
    $after = Get-StateHashes $InstallRoot $DesktopPath
    Assert-True ($after.current -ceq $Before.current -and $after.owner -ceq $Before.owner -and
        $after.shortcut -ceq $Before.shortcut -and $after.icon -ceq $Before.icon) $Message
}
function Assert-NoAtomicTemps([string]$Root, [string]$Message) {
    $left = @(Get-ChildItem -LiteralPath $Root -Force -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like '.desktop-*.tmp' -or $_.Name -like '.LLPlayer-Avalonia-*.lnk' })
    Assert-True ($left.Count -eq 0) $Message
}
function Assert-DesktopOnlyContract([string]$DesktopPath, [string]$WpfPath, [string]$Message) {
    $names = @(Get-ChildItem -LiteralPath $DesktopPath -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Sort-Object)
    $expected = @((Split-Path $WpfPath -Leaf), $shortcutName) | Sort-Object
    Assert-True (($names -join '|') -ceq ($expected -join '|')) $Message
}
$dotnetRootBefore = [Environment]::GetEnvironmentVariable('DOTNET_ROOT'); $dotnetRootX64Before = [Environment]::GetEnvironmentVariable('DOTNET_ROOT_X64')
$pathBefore = $env:PATH
$packageA = New-Package 'package-a'
$packageB = New-Package 'package-b'
$desktop = New-Desktop 'desktop'
$wpf = Join-Path $desktop 'LLPlayer.lnk'
Write-File $desktop 'LLPlayer.lnk' 'Original WPF shortcut sentinel'
$wpfHash = Hash $wpf
$install = Join-Path $work 'install'
$params = @{ InstallRoot = $install; DesktopPath = $desktop; IconPath = $icon }
try {
    # --- 1. Static deployment contract -------------------------------------------------------------------
    $updaterText = Get-Content -LiteralPath $updater -Raw
    $updaterTokens = $null; $updaterErrors = $null
    $updaterAst = [System.Management.Automation.Language.Parser]::ParseFile($updater, [ref]$updaterTokens, [ref]$updaterErrors)
    Assert-True (@($updaterErrors).Count -eq 0) 'The updater does not parse cleanly.'
    $defaultText = @($updaterAst.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'InstallRoot' } | ForEach-Object { $_.DefaultValue.Extent.Text })
    Assert-True ($defaultText.Count -eq 1) 'The updater InstallRoot default is not a single inspectable expression.'
    Assert-True ($defaultText[0] -match 'Applications/LLPlayer-Avalonia-TestBuild' -and $defaultText[0] -match "GetFolderPath\('UserProfile'\)") 'The updater default root expression is not %USERPROFILE%\Applications\LLPlayer-Avalonia-TestBuild.'
    $evaluatedDefault = & ([scriptblock]::Create($defaultText[0]))
    $documentedDefault = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Applications/LLPlayer-Avalonia-TestBuild'))
    Assert-True ($evaluatedDefault -ieq $documentedDefault) 'The updater default root does not evaluate to the documented Applications test-build directory.'
    Assert-True (-not $documentedDefault.StartsWith([Environment]::GetFolderPath('LocalApplicationData') + '\', [StringComparison]::OrdinalIgnoreCase)) 'The default install root is inside the virtualized LocalAppData tree.'
    Assert-True (-not ($updaterText -match 'Start-Process|Stop-Process|Get-Process|taskkill|\.Kill\(')) 'The updater may start or stop a player process.'
    Assert-True (-not ($updaterText -match 'SetEnvironmentVariable|dotnet-install|Invoke-WebRequest|Invoke-RestMethod|winget|msiexec|Install-Package')) 'The updater may install a runtime, download code or change persistent environment.'
    Assert-True ($updaterText -match 'publish-avalonia\.ps1') 'The fresh path no longer uses the repository Avalonia publisher.'
    Assert-True ($updaterText -match 'GetFinalPathNameByHandle' -and $updaterText -match 'PhysicalPath') 'The updater no longer resolves final physical paths through handles.'
    Assert-True ($updaterText -match [regex]::Escape('$savedRoot = $env:DOTNET_ROOT') -and
        $updaterText -match [regex]::Escape('finally { $env:DOTNET_ROOT = $savedRoot; $env:DOTNET_ROOT_X64 = $savedRootX64; $env:PATH = $savedPath }')) 'The fresh publish branch no longer saves/restores the process build environment.'
    Assert-True ($updaterText -match 'Get-CleanCommit' -and $updaterText -match 'sourceCommit = Get-CleanCommit') 'The fresh branch no longer binds the clean source HEAD.'
    Assert-True ($updaterText -match '\$link\.TargetPath = \$exe; \$link\.Arguments = ''''') 'The new shortcut creation is no longer a direct argument-free executable target.'

    # --- 2. First direct selection and independent readback ----------------------------------------------
    $first = & $updater @params -PackageDir $packageA
    $state1 = Read-Json $first.currentPath
    $owner1 = Read-Json $first.ownerPath
    $receipt1 = Read-Json $first.receiptPath
    Assert-True ($first.schema -eq 2 -and $state1.schema -eq 2 -and $owner1.schema -eq 2 -and $receipt1.schema -eq 2) 'The deployment did not record schema 2 in every managed record.'
    Assert-True ($state1.owner -eq $ownerTag -and $owner1.owner -eq $ownerTag -and $receipt1.owner -eq $ownerTag) 'A managed record has a foreign owner tag.'
    Assert-True ($first.buildMode -eq 'prebuilt-validated' -and $state1.buildMode -eq 'prebuilt-validated' -and $receipt1.buildMode -eq 'prebuilt-validated') 'A prebuilt fixture claimed a fresh build mode.'
    Assert-True ($null -eq $first.sourceCommit -and $null -eq $state1.sourceCommit -and $null -eq $receipt1.sourceCommit) 'A prebuilt fixture falsely claimed a fresh source commit.'
    Assert-True ($first.buildId -eq $state1.buildId -and $first.buildId -eq $receipt1.buildId) 'The result, current pointer and receipt disagree on the build id.'
    Assert-True ($state1.buildId -match '^prebuilt-[0-9]{8}T[0-9]{9}Z-[0-9a-f]{8}$') 'The prebuilt build id is not immutable/unique.'
    Assert-True ((Hash $first.receiptPath) -ceq $state1.receiptSha256 -and (Hash $first.receiptPath) -ceq $first.receiptSha256) 'The current pointer or result does not bind the selected receipt hash.'
    Assert-True ($owner1.installRoot -ieq $install -and $owner1.shortcutName -eq $shortcutName) 'The owner record does not bind the requested install root and shortcut name.'
    Assert-True ($receipt1.executableSha256 -ceq (Hash $receipt1.executablePath)) 'The receipt executable hash does not match the installed executable.'
    Assert-True ($receipt1.executableSha256 -ceq (Hash (Join-Path $packageA 'LLPlayer.Avalonia.exe'))) 'The installed executable differs from the selected package.'
    Assert-True ($receipt1.iconSha256 -ceq $iconHash -and (Hash $receipt1.iconPath) -ceq $iconHash) 'The selected per-build blue icon is not the validated blue ICO.'
    Assert-True ($receipt1.iconPath -ieq (Join-Path (Split-Path $receipt1.payloadPath -Parent) 'blue.ico') -and (Test-Path -LiteralPath $receipt1.iconPath -PathType Leaf)) 'The selected icon is not the per-build blue.ico beside the selected payload.'
    Assert-True ($receipt1.shortcutSha256 -ceq (Hash $first.shortcutPath)) 'The receipt does not bind the installed shortcut bytes.'
    Assert-True ($receipt1.workingDirectory -ieq $receipt1.payloadPath -and $receipt1.targetArguments -ceq '') 'The receipt target contract is not an argument-free direct launch in the payload directory.'
    Assert-True ($receipt1.shortcutWindowStyle -eq 1 -and $receipt1.shortcutDescription -ceq $description) 'The receipt shortcut style/description contract is wrong.'
    Assert-True ($receipt1.globalRuntimeVersion -match '^10\.' -and $receipt1.globalRuntimeRoot) 'The receipt does not record the global x64 .NET 10 runtime used for direct launch.'
    Assert-True (Test-Path -LiteralPath (Join-Path $receipt1.globalRuntimeRoot ('shared/Microsoft.NETCore.App/' + $receipt1.globalRuntimeVersion + '/coreclr.dll')) -PathType Leaf) 'The recorded global runtime identity does not contain its coreclr host file.'
    $payloadFiles = @(Get-ChildItem -LiteralPath $receipt1.payloadPath -Recurse -File)
    Assert-True ($payloadFiles.Count -eq @($receipt1.files).Count -and $payloadFiles.Count -gt 0) 'The installed payload file manifest does not cover the payload.'
    foreach ($file in $receipt1.files) {
        Assert-True ((Hash (Join-Path $first.payloadPath $file.path)) -ceq $file.sha256) 'An installed file failed manifest readback.'
    }
    Assert-True ((Hash (Join-Path $first.payloadPath 'LLPlayer.Avalonia.exe')) -ceq $receipt1.executableSha256) 'The payload manifest executable differs from the receipt executable.'
    # Independent Shell readback in this test process: IShellLink fields plus ExtractIconEx on the selected ICO.
    $native1 = Get-NativeLink $first.shortcutPath
    Assert-True ($native1.TargetPath -ieq $receipt1.executablePath) 'Independent IShellLink readback target is not the selected executable.'
    Assert-True ($native1.Arguments -ceq '' -and $native1.WorkingDirectory -ieq $receipt1.payloadPath) 'Independent IShellLink readback arguments/working directory are not the direct payload contract.'
    Assert-True ($native1.IconPath -ieq $receipt1.iconPath -and $native1.IconIndex -eq 0 -and $native1.IconExtracted) 'Independent IShellLink/ExtractIconEx readback did not extract the selected per-build blue icon.'
    Assert-True ($null -ne $receipt1.nativeReadback -and $receipt1.nativeReadback.targetPath -ieq $native1.TargetPath -and
        $receipt1.nativeReadback.iconPath -ieq $native1.IconPath -and $receipt1.nativeReadback.iconIndex -eq $native1.IconIndex -and
        $receipt1.nativeReadback.shellIconAvailable -eq $native1.IconExtracted) 'The updater receipt native readback disagrees with the independent Shell readback.'
    foreach ($pair in @(
        @($state1.receiptPath, 'current.receiptPath'), @($state1.executablePath, 'current.executablePath'), @($state1.workingDirectory, 'current.workingDirectory'),
        @($state1.iconPath, 'current.iconPath'), @($state1.shortcutPath, 'current.shortcutPath'), @($owner1.installRoot, 'owner.installRoot'),
        @($receipt1.payloadPath, 'receipt.payloadPath'), @($receipt1.executablePath, 'receipt.executablePath'), @($receipt1.iconPath, 'receipt.iconPath'),
        @($receipt1.shortcutPath, 'receipt.shortcutPath'), @($receipt1.repository, 'receipt.repository'), @($receipt1.globalRuntimeRoot, 'receipt.globalRuntimeRoot'),
        @($first.currentPath, 'result.currentPath'), @($first.ownerPath, 'result.ownerPath'), @($first.shortcutPath, 'result.shortcutPath')
    )) { Assert-Physical $pair[0] $pair[1] }
    Assert-True ($state1.executablePath -ieq $receipt1.executablePath -and $state1.workingDirectory -ieq $receipt1.payloadPath -and $state1.iconPath -ieq $receipt1.iconPath) 'current.json and the receipt disagree on the selected executable/working directory/icon.'
    $link1 = Get-Link $first.shortcutPath
    Assert-True ($link1.target -ieq $receipt1.executablePath) 'The shortcut does not target the selected payload executable directly.'
    Assert-True ($link1.arguments -ceq '') 'The shortcut passes arguments instead of a plain direct launch.'
    Assert-True ($link1.working -ieq $receipt1.payloadPath) 'The shortcut working directory is not the selected payload directory.'
    Assert-True ($link1.iconFile -ieq $receipt1.iconPath -and $link1.iconIndex -eq 0) 'The shortcut does not use the selected per-build blue icon.'
    Assert-True ($link1.description -ceq $description -and $link1.style -eq 1) 'The shortcut description/window style does not match the managed contract.'
    Assert-True (-not $link1.target.ToLowerInvariant().Contains('powershell') -and -not $link1.target.ToLowerInvariant().Contains('launcher.ps1')) 'The normal launch path still uses a PowerShell launcher.'
    $selectedFromLink = $null
    if ($link1.target -match '\\builds\\([^\\]+)\\payload\\LLPlayer\.Avalonia\.exe$') { $selectedFromLink = $Matches[1] }
    Assert-True ($selectedFromLink -eq $state1.buildId) 'current.json and the shortcut select different builds.'
    Assert-True ((Hash $wpf) -ceq $wpfHash) 'The deployment modified the original WPF shortcut.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $install 'launcher.ps1')) -and -not (Test-Path -LiteralPath (Join-Path $install 'blue.ico'))) 'The schema-2 root created legacy launcher/icon files.'
    Assert-True (@(Get-ChildItem -LiteralPath $install -Force | Where-Object { $_.Name -notin @('owner.json', 'current.json', 'builds') }).Count -eq 0) 'The schema-2 install root contains unexpected entries.'

    # --- 3. Refresh keeps earlier payloads and rebinds the direct link ------------------------------------
    $firstBuildId = $state1.buildId
    $firstPayload = $first.payloadPath
    $firstReceipt = $first.receiptPath
    $firstExeHash = $receipt1.executableSha256
    $firstReceiptHash = Hash $firstReceipt
    $ownerBeforeRefresh = Hash $first.ownerPath
    $second = & $updater @params -PackageDir $packageB
    $state2 = Read-Json $second.currentPath
    $receipt2 = Read-Json $second.receiptPath
    Assert-True ($state2.buildId -cne $firstBuildId -and $second.payloadPath -cne $firstPayload) 'Refresh reused or overwrote the prior build directory.'
    Assert-True ((Hash (Join-Path $firstPayload 'LLPlayer.Avalonia.exe')) -ceq $firstExeHash) 'Refresh modified the earlier payload.'
    Assert-True ((Hash $firstReceipt) -ceq $firstReceiptHash) 'Refresh modified the earlier receipt.'
    Assert-True ((Hash (Join-Path $second.payloadPath 'LLPlayer.Avalonia.exe')) -ceq (Hash (Join-Path $packageB 'LLPlayer.Avalonia.exe'))) 'Refresh did not select the newer payload.'
    Assert-True ((Hash (Join-Path (Split-Path $firstPayload -Parent) 'blue.ico')) -ceq $iconHash) 'Refresh modified the retained per-build icon.'
    Assert-True ($receipt2.iconPath -ieq (Join-Path (Split-Path $receipt2.payloadPath -Parent) 'blue.ico') -and (Hash $receipt2.iconPath) -ceq $iconHash) 'Refresh did not install the blue icon beside the new payload.'
    $native2 = Get-NativeLink $second.shortcutPath
    Assert-True ($native2.TargetPath -ieq $receipt2.executablePath -and $native2.Arguments -ceq '' -and $native2.WorkingDirectory -ieq $receipt2.payloadPath -and
        $native2.IconPath -ieq $receipt2.iconPath -and $native2.IconIndex -eq 0 -and $native2.IconExtracted) 'Independent Shell readback after refresh does not match the new direct payload/icon contract.'
    $flatOwner = Read-Json $first.ownerPath
    Assert-True ((Hash $first.ownerPath) -ceq $ownerBeforeRefresh -and $null -eq $flatOwner.PSObject.Properties['legacy']) 'Refresh changed a flat owner record or invented a migration.'
    Assert-True ((Hash $wpf) -ceq $wpfHash) 'Refresh changed the original WPF shortcut.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory).Count -eq 2) 'Refresh did not retain exactly the two immutable builds.'
    $link2 = Get-Link $second.shortcutPath
    Assert-True ($link2.target -ieq $receipt2.executablePath -and $link2.working -ieq $receipt2.payloadPath -and $link2.iconFile -ieq $receipt2.iconPath) 'Refresh did not rebind the direct link to the new payload/icon.'
    Assert-True ($link2.target -ieq $state2.executablePath -and $state2.iconPath -ieq $receipt2.iconPath) 'current.json and the refreshed shortcut select different payload identities.'
    Assert-True ((Hash $second.shortcutPath) -ceq $receipt2.shortcutSha256) 'The refreshed shortcut bytes do not match the new receipt.'

    # --- 4. Atomic activation rollback --------------------------------------------------------------------
    $beforeRollback = Get-StateHashes $install $desktop
    $buildsBefore = @(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory | Select-Object -ExpandProperty Name)
    $pointerLock = [IO.File]::Open($second.currentPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Assert-Rejected { & $updater @params -PackageDir $packageA } '' }
    finally { $pointerLock.Dispose() }
    Assert-StateUnchanged $beforeRollback $install $desktop 'A pointer-write failure changed the current selection or shortcut.'
    $unselected = @(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory | Where-Object { $buildsBefore -notcontains $_.Name })
    Assert-True ($unselected.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $unselected[0].FullName 'receipt.json') -PathType Leaf)) 'The pointer-failure fixture did not leave exactly one complete unselected build.'
    $orphanReceipt = Read-Json (Join-Path $unselected[0].FullName 'receipt.json')
    Assert-True ($orphanReceipt.schema -eq 2 -and $orphanReceipt.executableSha256 -ceq (Hash (Join-Path $packageA 'LLPlayer.Avalonia.exe')) -and
        $orphanReceipt.iconPath -ieq (Join-Path $unselected[0].FullName 'blue.ico') -and (Hash $orphanReceipt.iconPath) -ceq $iconHash) 'The pointer-failure run failed before producing the complete selected build it was intended to activate.'
    Assert-NoAtomicTemps $install 'A failed atomic pointer write left temporary files behind.'
    Assert-NoAtomicTemps $desktop 'A failed atomic pointer write left desktop temporary files behind.'
    Assert-DesktopOnlyContract $desktop $wpf 'A failed atomic pointer write changed the Desktop contents.'
    $buildsBefore = @(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory | Select-Object -ExpandProperty Name)
    $ownerLock = [IO.File]::Open($second.ownerPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Assert-Rejected { & $updater @params -PackageDir $packageA } '' }
    finally { $ownerLock.Dispose() }
    Assert-StateUnchanged $beforeRollback $install $desktop 'An owner-write failure did not restore the shortcut/current selection.'
    $unselected = @(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory | Where-Object { $buildsBefore -notcontains $_.Name })
    Assert-True ($unselected.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $unselected[0].FullName 'receipt.json') -PathType Leaf)) 'The owner-failure fixture did not leave exactly one complete unselected build.'
    $orphanReceipt = Read-Json (Join-Path $unselected[0].FullName 'receipt.json')
    Assert-True ($orphanReceipt.schema -eq 2 -and $orphanReceipt.executableSha256 -ceq (Hash (Join-Path $packageA 'LLPlayer.Avalonia.exe')) -and
        (Hash $orphanReceipt.iconPath) -ceq $iconHash) 'The owner-failure run failed before producing the complete selected build it was intended to activate.'
    Assert-NoAtomicTemps $install 'A failed atomic owner write left temporary files behind.'
    Assert-NoAtomicTemps $desktop 'A failed atomic owner write left desktop temporary files behind.'
    Assert-DesktopOnlyContract $desktop $wpf 'A failed atomic owner write changed the Desktop contents.'

    # --- 5. Safe migration from the verified schema-1 direct/physical-icon form ---------------------------
    $migrationDesktop = New-Desktop 'desktop-migration-direct'
    $legacyDirect = New-LegacyInstall 'legacy-direct' 'direct' $migrationDesktop
    $legacyDirectBefore = Get-LegacyHashes $legacyDirect
    $migratedInstall = Join-Path $work 'install-migrated-direct'
    # The fixture only extends the bound legacy-root allowlist; shortcut discovery, handle resolution and every
    # owner/receipt/executable/icon/launcher/shortcut identity check run through the production path.
    $migrated = & $updater -InstallRoot $migratedInstall -DesktopPath $migrationDesktop -IconPath $icon -PackageDir $packageA -LegacyRootsForTests @($legacyDirect.logical, $legacyDirect.physical)
    $migratedOwner = Read-Json (Join-Path $migratedInstall 'owner.json')
    $migratedState = Read-Json (Join-Path $migratedInstall 'current.json')
    $migratedReceipt = Read-Json $migrated.receiptPath
    Assert-True ($migrated.legacyVariant -eq 'direct-current-source-icon') 'The repaired direct physical-icon schema-1 form was not recognized as the accepted migration variant.'
    Assert-True ($migratedOwner.schema -eq 2 -and $migratedOwner.migratedFromSchema -eq 1 -and $null -ne $migratedOwner.legacy) 'Migration did not record schema-2 ownership with schema-1 provenance.'
    Assert-True ($migratedOwner.legacy.variant -eq 'direct-current-source-icon' -and $migratedOwner.legacy.sourceInstallRoot -ieq $legacyDirect.physical) 'The owner migration record lost the source install root or variant.'
    Assert-True ($migratedOwner.legacy.sourceRecordedInstallRoot -ieq $legacyDirect.logical) 'The owner migration record lost the recorded logical legacy root.'
    Assert-True ($migratedOwner.legacy.sourceOwnerSha256 -ceq $legacyDirectBefore.owner -and $migratedOwner.legacy.sourceCurrentSha256 -ceq $legacyDirectBefore.current) 'The owner migration record does not bind the retained legacy owner/current bytes.'
    Assert-True ($migratedOwner.legacy.sourceShortcutSha256 -ceq $legacyDirectBefore.shortcut -and $migratedOwner.legacy.previousBuildId -eq $legacyDirect.buildId) 'The owner migration record does not bind the replaced shortcut and previous build.'
    Assert-True ($migratedReceipt.legacyMigration.variant -eq 'direct-current-source-icon' -and $migratedReceipt.legacyMigration.sourceShortcutSha256 -ceq $legacyDirectBefore.shortcut) 'The new receipt does not preserve the schema-1 migration provenance.'
    Assert-LegacyRootUnchanged $legacyDirect $legacyDirectBefore 'Migration modified or deleted the retained schema-1 installation.'
    Assert-True ((Hash $legacyDirect.shortcutPath) -cne $legacyDirectBefore.shortcut -and $migratedOwner.legacy.sourceShortcutSha256 -ceq $legacyDirectBefore.shortcut) 'Migration did not replace the legacy link while recording its prior bytes.'
    Assert-True (-not (Test-Path -LiteralPath $legacyDirect.logical)) 'Migration created the recorded logical legacy root.'
    $migratedLink = Get-Link (Join-Path $migrationDesktop $shortcutName)
    Assert-True ($migratedLink.target -ieq $migratedReceipt.executablePath -and $migratedLink.arguments -ceq '' -and $migratedLink.working -ieq $migratedReceipt.payloadPath -and $migratedLink.iconFile -ieq $migratedReceipt.iconPath) 'Migration did not replace the legacy link with the direct schema-2 launch contract.'
    Assert-True ($migratedState.buildId -eq $migratedReceipt.buildId -and (Hash $migrated.receiptPath) -ceq $migratedState.receiptSha256) 'Migrated current.json and receipt disagree.'
    Assert-True ($migratedReceipt.executableSha256 -ceq (Hash (Join-Path $packageA 'LLPlayer.Avalonia.exe'))) 'Migration selected a payload different from the requested package.'
    foreach ($pair in @(
        @($migratedOwner.installRoot, 'migrated.owner.installRoot'), @($migratedState.executablePath, 'migrated.current.executablePath'),
        @($migratedState.iconPath, 'migrated.current.iconPath'), @($migratedState.shortcutPath, 'migrated.current.shortcutPath'),
        @($migratedReceipt.payloadPath, 'migrated.receipt.payloadPath'), @($migratedReceipt.executablePath, 'migrated.receipt.executablePath'),
        @($migratedReceipt.iconPath, 'migrated.receipt.iconPath'), @($migrated.receiptPath, 'migrated.receiptPath')
    )) {
        Assert-Physical $pair[0] $pair[1]
        Assert-True (Test-Path -LiteralPath $pair[0]) "Migrated record path does not exist: $($pair[1])"
    }
    $migratedNative = Get-NativeLink (Join-Path $migrationDesktop $shortcutName)
    Assert-True ($migratedNative.TargetPath -ieq $migratedReceipt.executablePath -and $migratedNative.Arguments -ceq '' -and
        $migratedNative.WorkingDirectory -ieq $migratedReceipt.payloadPath -and $migratedNative.IconPath -ieq $migratedReceipt.iconPath -and
        $migratedNative.IconIndex -eq 0 -and $migratedNative.IconExtracted) 'Migrated link failed the independent Shell readback contract.'
    # A later refresh must preserve the recorded migration provenance and keep working without the fixture seam.
    $afterMigration = & $updater -InstallRoot $migratedInstall -DesktopPath $migrationDesktop -IconPath $icon -PackageDir $packageB
    $afterOwner = Read-Json (Join-Path $migratedInstall 'owner.json')
    Assert-True ($afterOwner.schema -eq 2 -and -not $afterMigration.legacyVariant -and $afterOwner.legacy.variant -eq 'direct-current-source-icon') 'A later refresh lost or overwrote the recorded schema-1 migration provenance.'
    Assert-True ((Hash $legacyDirect.launcher) -ceq $legacyDirectBefore.launcher -and (Hash $legacyDirect.exe) -ceq $legacyDirectBefore.exe) 'A later refresh modified the retained schema-1 installation.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $migratedInstall 'builds') -Directory).Count -eq 2) 'A later refresh did not retain the migrated build.'

    # --- 6. Safe migration from the original PowerShell-launcher schema-1 form ----------------------------
    $powershellDesktop = New-Desktop 'desktop-migration-powershell'
    $legacyPowerShell = New-LegacyInstall 'legacy-powershell' 'powershell' $powershellDesktop
    $legacyPowerShellBefore = Get-LegacyHashes $legacyPowerShell
    $powershellInstall = Join-Path $work 'install-migrated-powershell'
    $powershellMigrated = & $updater -InstallRoot $powershellInstall -DesktopPath $powershellDesktop -IconPath $icon -PackageDir $packageA -LegacyRootsForTests @($legacyPowerShell.logical, $legacyPowerShell.physical)
    $powershellOwner = Read-Json (Join-Path $powershellInstall 'owner.json')
    Assert-True ($powershellMigrated.legacyVariant -eq 'powershell') 'The original PowerShell-launcher schema-1 form was not recognized.'
    Assert-True ($powershellOwner.legacy.variant -eq 'powershell' -and $powershellOwner.legacy.launcherSha256 -eq $legacyPowerShellBefore.launcher) 'The PowerShell migration record lost the retained launcher identity.'
    Assert-LegacyRootUnchanged $legacyPowerShell $legacyPowerShellBefore 'PowerShell-form migration modified or deleted the retained schema-1 installation.'
    $powershellLink = Get-Link (Join-Path $powershellDesktop $shortcutName)
    Assert-True ($powershellLink.target -match '\\builds\\[^\\]+\\payload\\LLPlayer\.Avalonia\.exe$' -and $powershellLink.target.ToLowerInvariant().Contains($powershellInstall.ToLowerInvariant()) -and $powershellLink.arguments -ceq '') 'PowerShell-form migration did not install the direct new payload target.'
    Assert-True ($powershellLink.working -ieq (Split-Path $powershellLink.target -Parent)) 'PowerShell-form migration did not set the payload directory as working directory.'
    Assert-True (-not $powershellLink.target.ToLowerInvariant().EndsWith('powershell.exe')) 'PowerShell-form migration still launches through PowerShell.'
    $powershellNative = Get-NativeLink (Join-Path $powershellDesktop $shortcutName)
    Assert-True ($powershellNative.IconExtracted -and $powershellNative.IconIndex -eq 0 -and $powershellNative.Arguments -ceq '') 'PowerShell-form migration did not produce an extractable direct link icon/target.'

    # The accepted intermediate direct form still had the old blue.ico (not the physical DIB repair).
    $legacyIconDesktop = New-Desktop 'desktop-migration-legacy-icon'
    $legacyIconForm = New-LegacyInstall 'legacy-icon-form' 'legacy-icon' $legacyIconDesktop
    $legacyIconBefore = Get-LegacyHashes $legacyIconForm
    $legacyIconInstall = Join-Path $work 'install-migrated-legacy-icon'
    $legacyIconMigrated = & $updater -InstallRoot $legacyIconInstall -DesktopPath $legacyIconDesktop -IconPath $icon -PackageDir $packageA -LegacyRootsForTests @($legacyIconForm.logical, $legacyIconForm.physical)
    $legacyIconOwner = Read-Json (Join-Path $legacyIconInstall 'owner.json')
    Assert-True ($legacyIconMigrated.legacyVariant -eq 'direct-legacy-icon') 'The direct schema-1 form with the old blue.ico was not recognized.'
    Assert-True ($legacyIconOwner.legacy.variant -eq 'direct-legacy-icon' -and $legacyIconOwner.legacy.iconSha256 -eq $legacyIconBefore.blue) 'The legacy-icon migration record lost its retained icon identity.'
    Assert-LegacyRootUnchanged $legacyIconForm $legacyIconBefore 'Legacy-icon-form migration modified or deleted the retained schema-1 installation.'

    # --- 7. Refusing foreign or edited managed schema-2 state ---------------------------------------------
    $selected = Get-StateHashes $install $desktop
    $payloadSnapshot = @(Get-ChildItem -LiteralPath $second.payloadPath -Recurse -File | ForEach-Object { $_.FullName + '|' + (Hash $_.FullName) } | Sort-Object)
    $selectedExe = $state2.executablePath
    $selectedBlue = $state2.iconPath
    $selectedOwnerPath = $second.ownerPath
    $selectedCurrentPath = $second.currentPath
    $selectedLinkPath = $second.shortcutPath
    [IO.File]::WriteAllBytes($selectedExe, $utf8.GetBytes('tampered executable'))
    try { Assert-Rejected { & $updater @params -PackageDir $packageA } 'existing payload identity is invalid' } finally { [IO.File]::WriteAllText($selectedExe, ((Split-Path $packageB -Leaf) + ':LLPlayer.Avalonia.exe'), $utf8) }
    Assert-StateUnchanged $selected $install $desktop 'A tampered executable still allowed a new selection.'
    [IO.File]::WriteAllBytes($selectedBlue, $utf8.GetBytes('not the blue icon'))
    try { Assert-Rejected { & $updater @params -PackageDir $packageA } 'per-build icon was edited' } finally { [IO.File]::WriteAllBytes($selectedBlue, [IO.File]::ReadAllBytes($icon)) }
    Assert-StateUnchanged $selected $install $desktop 'A tampered per-build icon still allowed a new selection.'
    $currentBytes = [IO.File]::ReadAllBytes($selectedCurrentPath)
    try {
        $tampered = Read-Json $selectedCurrentPath; $tampered.receiptSha256 = ('0' * 64); Write-Json $selectedCurrentPath $tampered
        Assert-Rejected { & $updater @params -PackageDir $packageA } 'existing desktop build receipt is invalid'
    }
    finally { [IO.File]::WriteAllBytes($selectedCurrentPath, $currentBytes) }
    Assert-StateUnchanged $selected $install $desktop 'A tampered receipt pointer still allowed a new selection.'
    $ownerBytes = [IO.File]::ReadAllBytes($selectedOwnerPath)
    try {
        $foreignOwner = Read-Json $selectedOwnerPath; $foreignOwner.owner = 'Foreign.Desktop.Owner'; Write-Json $selectedOwnerPath $foreignOwner
        Assert-Rejected { & $updater @params -PackageDir $packageA } 'foreign or incompatible owner record'
    }
    finally { [IO.File]::WriteAllBytes($selectedOwnerPath, $ownerBytes) }
    Assert-StateUnchanged $selected $install $desktop 'A foreign owner record still allowed a new selection.'
    try {
        $foreignRoot = Read-Json $selectedOwnerPath; $foreignRoot.installRoot = (Join-Path $work 'elsewhere'); Write-Json $selectedOwnerPath $foreignRoot
        Assert-Rejected { & $updater @params -PackageDir $packageA } 'foreign or incompatible owner record'
    }
    finally { [IO.File]::WriteAllBytes($selectedOwnerPath, $ownerBytes) }
    try {
        $missingField = Read-Json $selectedCurrentPath
        $missingField.PSObject.Properties.Remove('executablePath')
        Write-Json $selectedCurrentPath $missingField
        Assert-Rejected { & $updater @params -PackageDir $packageA } 'missing the executablePath identity field'
    }
    finally { [IO.File]::WriteAllBytes($selectedCurrentPath, $currentBytes) }
    Assert-StateUnchanged $selected $install $desktop 'A pointer without its identity fields still allowed a new selection.'
    $linkBytes = [IO.File]::ReadAllBytes($selectedLinkPath)
    try {
        New-Link $selectedLinkPath $selectedExe '' $work ([IO.Path]::GetFullPath($selectedBlue) + ',0') 1
        Assert-Rejected { & $updater @params -PackageDir $packageA } 'foreign or edited desktop shortcut'
    }
    finally { [IO.File]::WriteAllBytes($selectedLinkPath, $linkBytes) }
    Assert-StateUnchanged $selected $install $desktop 'An edited direct shortcut still allowed a new selection.'
    $selected2 = Get-StateHashes $install $desktop
    $payloadAfter = @(Get-ChildItem -LiteralPath $second.payloadPath -Recurse -File | ForEach-Object { $_.FullName + '|' + (Hash $_.FullName) } | Sort-Object)
    Assert-True (($payloadSnapshot -join "`n") -ceq ($payloadAfter -join "`n")) 'A refused update changed the selected payload bytes.'
    Assert-True ($selected2.icon -eq $selected.icon -and $selected2.current -eq $selected.current) 'A refused update changed the selected icon/pointer identity.'

    # --- 8. Refusing unsafe inputs and packages ------------------------------------------------------------
    $foreignDesktop = New-Desktop 'foreign-desktop'
    New-Link (Join-Path $foreignDesktop $shortcutName) (Join-Path $env:SystemRoot 'System32/notepad.exe') '' $foreignDesktop ('"' + (Join-Path $env:SystemRoot 'System32/notepad.exe') + '"') 1
    $foreignShortcutHash = Hash (Join-Path $foreignDesktop $shortcutName)
    $foreignInstall = Join-Path $work 'unclaimed-install'
    Assert-Rejected { & $updater -InstallRoot $foreignInstall -DesktopPath $foreignDesktop -IconPath $icon -PackageDir $packageA } 'foreign desktop shortcut'
    Assert-True ((Hash (Join-Path $foreignDesktop $shortcutName)) -ceq $foreignShortcutHash -and -not (Test-Path -LiteralPath $foreignInstall)) 'A foreign shortcut collision wrote to disk.'
    $unowned = New-Desktop 'unowned-install'
    Write-File $unowned 'keep.txt' 'Do not overwrite'
    Assert-Rejected { & $updater -InstallRoot $unowned -DesktopPath $desktop -IconPath $icon -PackageDir $packageA } 'non-empty foreign InstallRoot'
    Assert-True ((Get-Content -LiteralPath (Join-Path $unowned 'keep.txt') -Raw) -ceq 'Do not overwrite') 'Foreign install data changed.'
    $ownerWithoutCurrent = New-Desktop 'owner-without-current'
    Write-Json (Join-Path $ownerWithoutCurrent 'owner.json') ([ordered]@{ schema = 2; owner = $ownerTag; installRoot = $ownerWithoutCurrent; shortcutName = $shortcutName })
    Assert-Rejected { & $updater -InstallRoot $ownerWithoutCurrent -DesktopPath $desktop -IconPath $icon -PackageDir $packageA } 'owner record but no current.json'
    Assert-Rejected { & $updater @params -PackageDir $install } 'must not overlap'
    Assert-Rejected { & $updater -InstallRoot $desktop -DesktopPath $desktop -IconPath $icon -PackageDir $packageA } 'must not overlap'
    $badIcon = Join-Path $work 'invalid.ico'
    # A structurally sized ICONDIR with zero images must be rejected by the counting check, not just by length.
    [IO.File]::WriteAllBytes($badIcon, [byte[]]@(0,0, 1,0, 0,0, 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25))
    $wrongTypeIcon = Join-Path $work 'wrong-type.ico'
    [IO.File]::WriteAllBytes($wrongTypeIcon, [byte[]]@(0,0, 2,0, 1,0, 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25))
    $badIconParams = @{ InstallRoot = $install; DesktopPath = $desktop; IconPath = $badIcon }
    Assert-Rejected { & $updater @badIconParams -PackageDir $packageA } 'Windows ICO image'
    Assert-Rejected { & $updater -InstallRoot $install -DesktopPath $desktop -IconPath $wrongTypeIcon -PackageDir $packageA } 'Windows ICO image'
    $missingCore = New-Package 'package-missing-core' -OmitCore
    Assert-Rejected { & $updater @params -PackageDir $missingCore } 'missing required file'
    $runtime9 = Join-Path $work 'package-runtime9'
    Copy-Tree $packageA $runtime9
    Write-File $runtime9 'LLPlayer.Avalonia.runtimeconfig.json' '{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}'
    Assert-Rejected { & $updater @params -PackageDir $runtime9 } 'Microsoft.NETCore.App 10'
    $rollForward = Join-Path $work 'package-rollforward'
    Copy-Tree $packageA $rollForward
    Write-File $rollForward 'LLPlayer.Avalonia.runtimeconfig.json' '{"runtimeOptions":{"rollForward":"latestPatch","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'
    Assert-Rejected { & $updater @params -PackageDir $rollForward } 'roll-forward'
    $pluralFrameworks = Join-Path $work 'package-frameworks'
    Copy-Tree $packageA $pluralFrameworks
    Write-File $pluralFrameworks 'LLPlayer.Avalonia.runtimeconfig.json' '{"runtimeOptions":{"frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}'
    Assert-Rejected { & $updater @params -PackageDir $pluralFrameworks } 'exactly one Microsoft.NETCore.App 10 framework'
    $devRuntime = Join-Path $work 'package-runtimeconfig-dev'
    Copy-Tree $packageA $devRuntime
    Write-File $devRuntime 'LLPlayer.Avalonia.runtimeconfig.dev.json' '{}'
    Assert-Rejected { & $updater @params -PackageDir $devRuntime } 'runtimeconfig.dev.json'
    $depsMismatch = Join-Path $work 'package-deps-mismatch'
    Copy-Tree $packageA $depsMismatch
    Write-File $depsMismatch 'LLPlayer.Avalonia.deps.json' '{"runtimeTarget":{"name":".NETCoreApp,Version=v9.0/win-x64"}}'
    Assert-Rejected { & $updater @params -PackageDir $depsMismatch } 'deps.json runtime target'
    Assert-StateUnchanged $selected2 $install $desktop 'A rejected package or input changed the selected build.'
    $lockedPackage = Join-Path $work 'package-locked'
    Copy-Tree $packageA $lockedPackage
    $lockedFile = [IO.File]::Open((Join-Path $lockedPackage 'LICENSE'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Assert-Rejected { & $updater @params -PackageDir $lockedPackage } '' }
    finally { $lockedFile.Dispose() }
    Assert-StateUnchanged $selected2 $install $desktop 'A copy failure changed the selected build or shortcut.'
    # A failed first installation must leave a pre-existing empty destination retryable, not refused as foreign.
    $retryDesktop = New-Desktop 'retry-desktop'
    $retryRoot = New-Desktop 'retry-empty-root'
    $retryLock = [IO.File]::Open((Join-Path $lockedPackage 'LICENSE'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Assert-Rejected { & $updater -InstallRoot $retryRoot -DesktopPath $retryDesktop -IconPath $icon -PackageDir $lockedPackage } '' }
    finally { $retryLock.Dispose() }
    Assert-True (@(Get-ChildItem -LiteralPath $retryRoot -Force).Count -eq 0) 'A failed first install left state in a pre-existing empty install root.'
    $retryParams = @{ InstallRoot = $retryRoot; DesktopPath = $retryDesktop; IconPath = $icon }
    $retried = & $updater @retryParams -PackageDir $packageA
    $retriedState = Read-Json $retried.currentPath
    Assert-True ($retried.buildMode -eq 'prebuilt-validated' -and $retriedState.buildId -eq $retried.buildId -and (Test-Path -LiteralPath (Join-Path $retryDesktop $shortcutName) -PathType Leaf)) 'A retry after a failed first installation was refused or incomplete.'
    Assert-NoAtomicTemps $install 'A rejected package/input left install-root temporary files behind.'
    Assert-NoAtomicTemps $desktop 'A rejected package/input left Desktop temporary files behind.'
    Assert-DesktopOnlyContract $desktop $wpf 'A rejected package/input changed the Desktop contents.'
    $junction = Join-Path $work 'install-junction'
    New-Item -ItemType Junction -Path $junction -Target $install | Out-Null
    Assert-Rejected { & $updater -InstallRoot $junction -DesktopPath $desktop -IconPath $icon -PackageDir $packageA } 'reparse'
    # A drive-letter alias (the same class of redirect as an MSIX AppData view) must not become a destination.
    $aliasDrive = $null
    foreach ($letter in @('X', 'Y', 'Z', 'W', 'V')) { if (-not (Test-Path -LiteralPath ($letter + ':\'))) { $aliasDrive = $letter + ':'; break } }
    Assert-True ($null -ne $aliasDrive) 'No free drive letter is available for the alias destination test.'
    & subst $aliasDrive $work | Out-Null
    try { Assert-Rejected { & $updater -InstallRoot (Join-Path ($aliasDrive + '\') 'alias-install') -DesktopPath $desktop -IconPath $icon -PackageDir $packageA } 'Aliased or virtualized' }
    finally { & subst $aliasDrive /D | Out-Null }
    Assert-StateUnchanged $selected2 $install $desktop 'A redirected drive-letter destination changed the selected build.'
    $desktopJunction = Join-Path $work 'desktop-junction'
    New-Item -ItemType Junction -Path $desktopJunction -Target $desktop | Out-Null
    Assert-Rejected { & $updater -InstallRoot $install -DesktopPath $desktopJunction -IconPath $icon -PackageDir $packageA } 'reparse'
    $childTarget = New-Desktop 'owned-child-target'
    Write-File $childTarget 'keep.txt' 'Do not follow a child junction'
    $childTargetHash = Hash (Join-Path $childTarget 'keep.txt')
    $childRoot = New-Desktop 'install-child-junction'
    Copy-Tree $install $childRoot
    $childOwner = Read-Json (Join-Path $childRoot 'owner.json'); $childOwner.installRoot = $childRoot; Write-Json (Join-Path $childRoot 'owner.json') $childOwner
    $childOwnerCopy = Join-Path $childRoot 'owner.json.real'
    [IO.File]::Move((Join-Path $childRoot 'owner.json'), $childOwnerCopy)
    New-Item -ItemType Junction -Path (Join-Path $childRoot 'owner.json') -Target $childTarget | Out-Null
    Assert-Rejected { & $updater -InstallRoot $childRoot -DesktopPath $desktop -IconPath $icon -PackageDir $packageA } 'reparse'
    Assert-True ((Hash (Join-Path $childTarget 'keep.txt')) -ceq $childTargetHash) 'An owned child junction changed its external target.'
    Assert-StateUnchanged $selected2 $install $desktop 'A rejected junction input changed the selected build or shortcut.'

    # --- 9. Refusing foreign or edited legacy state -------------------------------------------------------
    function Test-LegacyRejection([string]$Name, [string]$Variant, [scriptblock]$Mutation, [string]$Pattern) {
        $desk = New-Desktop ('legacy-desk-' + $Name)
        $legacy = New-LegacyInstall ('legacy-' + $Name) $Variant $desk
        $before = Get-LegacyHashes $legacy
        & $Mutation $legacy
        $mutated = Get-LegacyHashes $legacy
        $newInstall = Join-Path $work ('legacy-install-' + $Name)
        Assert-Rejected { & $updater -InstallRoot $newInstall -DesktopPath $desk -IconPath $icon -PackageDir $packageA -LegacyRootsForTests @($legacy.logical, $legacy.physical) } $Pattern
        Assert-True (-not (Test-Path -LiteralPath $newInstall)) "Rejected legacy fixture '$Name' wrote a new install root."
        $after = Get-LegacyHashes $legacy
        Assert-True ($after.owner -ceq $mutated.owner -and $after.current -ceq $mutated.current -and $after.receipt -ceq $mutated.receipt -and
            $after.launcher -ceq $mutated.launcher -and $after.exe -ceq $mutated.exe) "Rejected legacy fixture '$Name' modified the old installation."
        return $null
    }
    Test-LegacyRejection 'edited-launcher' 'powershell' { param($l) [IO.File]::AppendAllText($l.launcher, '# edited') } 'launcher or icon was edited'
    Test-LegacyRejection 'edited-icon' 'direct' { param($l) [IO.File]::WriteAllBytes($l.blueIco, [Text.Encoding]::UTF8.GetBytes('not the blue icon')) } 'launcher or icon was edited'
    Test-LegacyRejection 'edited-dib-icon' 'direct' { param($l) [IO.File]::WriteAllBytes($l.dibIco, [Text.Encoding]::UTF8.GetBytes('not the blue icon')) } 'schema1 desktop shortcut'
    Test-LegacyRejection 'foreign-owner' 'direct' { param($l) $o = Read-Json $l.ownerPath; $o.owner = 'Foreign.Desktop.Owner'; Write-Json $l.ownerPath $o } 'verified original schema1 install'
    Test-LegacyRejection 'bad-receipt-hash' 'direct' { param($l) $c = Read-Json $l.currentPath; $c.receiptSha256 = ('0' * 64); Write-Json $l.currentPath $c } 'desktop build receipt is invalid'
    Test-LegacyRejection 'edited-executable' 'direct' { param($l) [IO.File]::WriteAllText($l.exe, 'tampered legacy executable') } 'existing payload identity is invalid'
    Test-LegacyRejection 'edited-receipt-exe' 'direct' { param($l) $r = Read-Json $l.receiptPath; $r.executableSha256 = ('0' * 64); Write-Json $l.receiptPath $r; $c = Read-Json $l.currentPath; $c.receiptSha256 = (Hash $l.receiptPath); Write-Json $l.currentPath $c } 'existing payload identity is invalid'
    Test-LegacyRejection 'edited-link-target' 'direct' { param($l) New-Link $l.shortcutPath (Join-Path $env:SystemRoot 'System32/notepad.exe') '' $l.payload ($l.dibIco + ',0') 1 } 'controlled legacy roots'
    # The fixture allowlist extension must be rejected before any fresh publish when -PackageDir is absent.
    $seamDesktop = New-Desktop 'legacy-desk-seam'
    $seamLegacy = New-LegacyInstall 'legacy-seam' 'direct' $seamDesktop
    $seamBefore = Get-LegacyHashes $seamLegacy
    $seamInstall = Join-Path $work 'legacy-install-seam'
    Assert-Rejected { & $updater -InstallRoot $seamInstall -DesktopPath $seamDesktop -IconPath $icon -LegacyRootsForTests @($seamLegacy.logical, $seamLegacy.physical) } 'contract-fixture input'
    Assert-True (-not (Test-Path -LiteralPath $seamInstall)) 'A fixture root input without -PackageDir wrote a new install root.'
    Assert-LegacyUnchanged $seamLegacy $seamBefore 'A fixture root input without -PackageDir modified the legacy installation.'
    # Without the fixture seam, a schema-1-shaped root outside the two bound legacy locations is foreign state.
    $unboundDesktop = New-Desktop 'legacy-desk-unbound'
    $unboundLegacy = New-LegacyInstall 'legacy-unbound' 'direct' $unboundDesktop
    $unboundBefore = Get-LegacyHashes $unboundLegacy
    $unboundInstall = Join-Path $work 'legacy-install-unbound'
    Assert-Rejected { & $updater -InstallRoot $unboundInstall -DesktopPath $unboundDesktop -IconPath $icon -PackageDir $packageA } 'controlled legacy roots'
    Assert-True (-not (Test-Path -LiteralPath $unboundInstall)) 'An unbound legacy root wrote a new install root.'
    Assert-LegacyUnchanged $unboundLegacy $unboundBefore 'An unbound legacy root was modified instead of rejected.'
    # A legacy-shaped link resolving through a redirected drive alias must not be trusted as a physical root.
    $aliasLegacyDesktop = New-Desktop 'legacy-desk-alias'
    $aliasLegacy = New-LegacyInstall 'legacy-alias' 'direct' $aliasLegacyDesktop
    $aliasLegacyBefore = Get-LegacyHashes $aliasLegacy
    Assert-True ($null -ne $aliasDrive) 'The drive-letter alias fixture is missing.'
    & subst $aliasDrive $work | Out-Null
    try {
        $aliasExe = Join-Path ($aliasDrive + '\') ('legacy-alias/builds/' + $aliasLegacy.buildId + '/payload/LLPlayer.Avalonia.exe')
        New-Link $aliasLegacy.shortcutPath $aliasExe '' $aliasLegacy.payload ($aliasLegacy.dibIco + ',0') 1
        $aliasLegacyInstall = Join-Path $work 'legacy-install-alias'
        Assert-Rejected { & $updater -InstallRoot $aliasLegacyInstall -DesktopPath $aliasLegacyDesktop -IconPath $icon -PackageDir $packageA } 'controlled legacy roots'
        Assert-True (-not (Test-Path -LiteralPath $aliasLegacyInstall)) 'An aliased legacy link wrote a new install root.'
    }
    finally { & subst $aliasDrive /D | Out-Null }
    Assert-LegacyRootUnchanged $aliasLegacy $aliasLegacyBefore 'An aliased legacy link modified the old installation.'

    # --- 10. -DotNetRoot is build-only; direct launch uses the registered system runtime ------------------
    $emptySdk = New-Desktop 'empty-sdk'
    $runtimeOnly = & $updater @params -DotNetRoot $emptySdk -PackageDir $packageA
    $runtimeReceipt = Read-Json $runtimeOnly.receiptPath
    Assert-True ($runtimeOnly.buildMode -eq 'prebuilt-validated' -and $runtimeReceipt.globalRuntimeVersion -match '^10\.') 'A bogus build SDK blocked an already validated package or the recorded runtime is not .NET 10.'
    Assert-Physical $runtimeReceipt.globalRuntimeRoot 'receipt.globalRuntimeRoot'
    Assert-True (Test-Path -LiteralPath (Join-Path $runtimeReceipt.globalRuntimeRoot ('shared/Microsoft.NETCore.App/' + $runtimeReceipt.globalRuntimeVersion + '/coreclr.dll')) -PathType Leaf) 'The recorded global runtime is not a resolvable x64 .NET host installation.'
    Assert-True ([Environment]::GetEnvironmentVariable('DOTNET_ROOT') -eq $dotnetRootBefore -and [Environment]::GetEnvironmentVariable('DOTNET_ROOT_X64') -eq $dotnetRootX64Before -and $env:PATH -ceq $pathBefore) 'The prebuilt run changed the process environment.'
    Assert-True ($null -eq ([Environment]::GetEnvironmentVariable('DOTNET_ROOT', 'User')) -and $null -eq ([Environment]::GetEnvironmentVariable('DOTNET_ROOT', 'Machine')) -and $null -eq ([Environment]::GetEnvironmentVariable('DOTNET_ROOT_X64', 'User')) -and $null -eq ([Environment]::GetEnvironmentVariable('DOTNET_ROOT_X64', 'Machine'))) 'The updater created a persistent runtime environment override.'
    $finalNative = Get-NativeLink $runtimeOnly.shortcutPath
    Assert-True ($finalNative.TargetPath -ieq $runtimeReceipt.executablePath -and $finalNative.WorkingDirectory -ieq $runtimeReceipt.payloadPath -and
        $finalNative.Arguments -ceq '' -and $finalNative.IconPath -ieq $runtimeReceipt.iconPath -and $finalNative.IconIndex -eq 0 -and $finalNative.IconExtracted) 'The final selected link failed the independent direct EXE/icon readback.'
    $finalState = Read-Json $runtimeOnly.currentPath
    Assert-True ($finalState.buildId -eq $runtimeReceipt.buildId -and $finalState.shortcutSha256 -ceq $runtimeReceipt.shortcutSha256 -and
        $finalState.iconSha256 -ceq $runtimeReceipt.iconSha256) 'The final current.json, receipt and shortcut do not select one build.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory).Count -eq 5) 'The contract fixture lost or merged immutable builds.'
    Assert-NoAtomicTemps $install 'The contract suite left install-root temporary files behind.'
    Assert-NoAtomicTemps $desktop 'The contract suite left Desktop temporary files behind.'
    Assert-DesktopOnlyContract $desktop $wpf 'The contract suite left unexpected Desktop entries.'
    Assert-True ((Hash $wpf) -ceq $wpfHash) 'The contract suite modified the original WPF shortcut.'
    Write-Host "Windows desktop test-build contracts passed: $script:passed. Fixtures retained: $work"
}
finally {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

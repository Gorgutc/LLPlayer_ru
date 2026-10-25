param([string]$DotNetRoot = $env:DOTNET_ROOT)
# Offline filesystem/COM contracts. All fixtures remain under ignored artifacts/.
# The fixture apphost is deliberately not executable; neither updater nor launcher may run it.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Desktop shortcut contract tests require Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$updater = Join-Path $PSScriptRoot 'update-desktop-test-build.ps1'
$work = Join-Path $repoRoot ('artifacts/windows-desktop-build-tests-' + [guid]::NewGuid().ToString('N'))
$utf8 = New-Object Text.UTF8Encoding($false)
$script:passed = 0
[IO.Directory]::CreateDirectory($work) | Out-Null
function Write-File([string]$Root, [string]$Relative, [string]$Text = 'fixture') {
    $path = Join-Path $Root $Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Text, $utf8)
}
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
function New-Package([string]$Name, [switch]$OmitCore) {
    $dir = Join-Path $work $Name
    $native = @('av_libglesv2.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','onnxruntime.dll','onnxruntime_providers_shared.dll','e_sqlite3.dll','soft_oal.dll') +
        @('ggml-base-whisper.dll','ggml-cpu-whisper.dll','ggml-whisper.dll','whisper.dll' | ForEach-Object { 'runtimes/win-x64/' + $_ }) +
        @('avcodec-62.dll','avdevice-62.dll','avfilter-11.dll','avformat-62.dll','avutil-60.dll','swresample-6.dll','swscale-9.dll' | ForEach-Object { 'FFmpeg/' + $_ })
    $required = @('LLPlayer.Avalonia.exe','LLPlayer.Avalonia.dll','FlyleafLib.dll','Assets/silero_vad.onnx','LICENSE','THIRD-PARTY-NOTICES.md',
        'FFmpeg/LICENSE.GPLv3.txt','FFmpeg/SOURCE.txt','licenses/OpenALSoft.LICENSE','licenses/OpenALSoft.SOURCE.txt',
        'licenses/Avalonia.ANGLE.LICENSE','licenses/OpenALSoft.BSD-3Clause.txt','dub_sidecar/server.py','dub_sidecar/pyproject.toml','dub_sidecar/uv.lock','dub_sidecar/README.md')
    if (-not $OmitCore) { $required += 'LLPlayer.Core.dll' }
    foreach ($file in @($required) + @($native)) { Write-File $dir $file ($Name + ':' + $file) }
    Write-File $dir 'LLPlayer.Avalonia.deps.json' '{}'
    Write-File $dir 'LLPlayer.Avalonia.runtimeconfig.json' '{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'
    $manifest = [ordered]@{
        schema=1; runtime='win-x64'; components=@(
            @{ name='FFmpeg'; version='fixture'; license='GPL'; source='fixture'; provenance='Synthetic offline contract fixture' },
            @{ name='OpenAL Soft'; version='1.23.1'; license='LGPL'; source='fixture'; provenance='Synthetic offline contract fixture' }
        )
        files=@($native | ForEach-Object { @{ path=$_; sha256=(Hash (Join-Path $dir $_)) } })
    }
    Write-File $dir 'native-dependencies.json' ($manifest | ConvertTo-Json -Depth 5)
    return $dir
}
function New-Desktop([string]$Name) {
    $path = Join-Path $work $Name
    [IO.Directory]::CreateDirectory($path) | Out-Null
    return $path
}
$icon = Join-Path $work 'fixture-blue.ico'
# A real one-pixel BGRA ICO (ICONDIR + ICONDIRENTRY + BITMAPINFOHEADER + XOR + AND).
$stream = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]1); $writer.Write([byte]1); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]48); $writer.Write([uint32]22)
    $writer.Write([uint32]40); $writer.Write([int32]1); $writer.Write([int32]2)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0); $writer.Write([uint32]4)
    1..4 | ForEach-Object { $writer.Write([uint32]0) }
    $writer.Write([byte[]]@(230,148,50,255,0,0,0,0))
    [IO.File]::WriteAllBytes($icon, $stream.ToArray())
}
finally { $writer.Dispose() }
$packageA = New-Package 'package-a'
$packageB = New-Package 'package-b'
$desktop = New-Desktop 'desktop'
$install = Join-Path $work 'install'
$params = @{ InstallRoot=$install; DesktopPath=$desktop; DotNetRoot=$DotNetRoot; IconPath=$icon }
$wpf = Join-Path $desktop 'LLPlayer.lnk'
Write-File $desktop 'LLPlayer.lnk' 'Original WPF shortcut sentinel'
$wpfHash = Hash $wpf
$first = & $updater @params -PackageDir $packageA
$state1 = Get-Content -LiteralPath $first.currentPath -Raw -Encoding UTF8 | ConvertFrom-Json
$receipt1 = Get-Content -LiteralPath $first.receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-True ($state1.buildMode -eq 'prebuilt-validated' -and $null -eq $state1.sourceCommit) 'Prebuilt input falsely claimed a fresh source commit.'
Assert-True ((Hash $first.receiptPath) -ceq $state1.receiptSha256) 'Pointer does not bind the selected receipt.'
Assert-True ((Hash $receipt1.executablePath) -ceq (Hash (Join-Path $packageA 'LLPlayer.Avalonia.exe'))) 'Installed executable differs from selected package.'
foreach ($file in $receipt1.files) { Assert-True ((Hash (Join-Path $first.payloadPath $file.path)) -ceq $file.sha256) 'An installed file failed manifest readback.' }
$shortcutHash = Hash $first.shortcutPath
$shell = New-Object -ComObject WScript.Shell
try {
    $link = $shell.CreateShortcut($first.shortcutPath)
    Assert-True ($link.TargetPath -like '*\WindowsPowerShell\v1.0\powershell.exe') 'Shortcut does not use the managed runtime launcher.'
    Assert-True ($link.Arguments -match '-WindowStyle Hidden -File "' -and $link.Arguments.EndsWith('launcher.ps1"')) 'Shortcut launcher arguments are invalid.'
    Assert-True ($link.IconLocation -eq ((Join-Path $install 'blue.ico') + ',0')) 'Shortcut does not select the blue test icon.'
    Assert-True ($link.WindowStyle -eq 7) 'The helper shortcut is not minimized/hidden as intended.'
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link)
}
finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
$second = & $updater @params -PackageDir $packageB
$state2 = Get-Content -LiteralPath $second.currentPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-True ($state2.buildId -cne $state1.buildId -and $second.payloadPath -cne $first.payloadPath) 'Refresh overwrote the prior build directory.'
Assert-True ((Hash (Join-Path $first.payloadPath 'LLPlayer.Avalonia.exe')) -ceq (Hash (Join-Path $packageA 'LLPlayer.Avalonia.exe'))) 'Refresh modified the old payload.'
Assert-True ((Hash (Join-Path $second.payloadPath 'LLPlayer.Avalonia.exe')) -ceq (Hash (Join-Path $packageB 'LLPlayer.Avalonia.exe'))) 'Refresh did not select the newer payload.'
Assert-True ((Hash $first.shortcutPath) -ceq $shortcutHash -and (Hash $wpf) -ceq $wpfHash) 'Refresh changed a stable shortcut or original WPF shortcut.'
$selectedHash = Hash $first.currentPath
$badPackage = New-Package 'package-missing-core' -OmitCore
Assert-Rejected { & $updater @params -PackageDir $badPackage } 'missing required file.*LLPlayer.Core'
Assert-True ((Hash $first.currentPath) -ceq $selectedHash -and (Hash $first.shortcutPath) -ceq $shortcutHash) 'Rejected package changed the current selection.'
$copyFailure = New-Package 'package-locked'
$lock = [IO.File]::Open((Join-Path $copyFailure 'LICENSE'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try { Assert-Rejected { & $updater @params -PackageDir $copyFailure } '' }
finally { $lock.Dispose() }
Assert-True ((Hash $first.currentPath) -ceq $selectedHash -and (Hash $first.shortcutPath) -ceq $shortcutHash) 'Copy failure changed the existing selection or shortcut.'
$beforeSelectionFailure = @(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory | Select-Object -ExpandProperty Name)
$pointerLock = [IO.File]::Open($first.currentPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Assert-Rejected { & $updater @params -PackageDir $packageA } '' }
finally { $pointerLock.Dispose() }
$unselected = @(Get-ChildItem -LiteralPath (Join-Path $install 'builds') -Directory | Where-Object { $beforeSelectionFailure -notcontains $_.Name })
Assert-True ($unselected.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $unselected[0].FullName 'receipt.json') -PathType Leaf)) 'The atomic-pointer failure fixture did not reach selection after creating a complete payload/receipt.'
Assert-True ((Hash $first.currentPath) -ceq $selectedHash -and (Hash $first.shortcutPath) -ceq $shortcutHash -and (Hash $wpf) -ceq $wpfHash) 'Atomic selection failure changed the current pointer or shortcut.'
$foreignDesktop = New-Desktop 'foreign-desktop'
$foreignShortcut = Join-Path $foreignDesktop 'LLPlayer Avalonia (Windows).lnk'
$foreignShell = New-Object -ComObject WScript.Shell
try {
    $foreignLink = $foreignShell.CreateShortcut($foreignShortcut)
    $foreignLink.TargetPath = Join-Path $env:SystemRoot 'System32/notepad.exe'
    $foreignLink.Description = 'Foreign shortcut sentinel'
    $foreignLink.Save()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($foreignLink)
}
finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($foreignShell) }
$foreignHash = Hash $foreignShortcut
$foreignInstall = Join-Path $work 'unclaimed-install'
Assert-Rejected { & $updater -InstallRoot $foreignInstall -DesktopPath $foreignDesktop -DotNetRoot $DotNetRoot -IconPath $icon -PackageDir $packageA } 'foreign or edited desktop shortcut'
Assert-True ((Hash $foreignShortcut) -ceq $foreignHash -and -not (Test-Path -LiteralPath $foreignInstall)) 'Foreign shortcut collision wrote to disk.'
$unowned = New-Desktop 'unowned-install'
Write-File $unowned 'keep.txt' 'Do not overwrite'
Assert-Rejected { & $updater -InstallRoot $unowned -DesktopPath $desktop -DotNetRoot $DotNetRoot -IconPath $icon -PackageDir $packageA } 'non-empty foreign InstallRoot'
Assert-True ((Get-Content -LiteralPath (Join-Path $unowned 'keep.txt') -Raw) -ceq 'Do not overwrite') 'Foreign install data changed.'
Assert-Rejected { & $updater @params -PackageDir $install } 'must not overlap'
Assert-Rejected { & $updater -InstallRoot $desktop -DesktopPath $desktop -DotNetRoot $DotNetRoot -IconPath $icon -PackageDir $packageA } 'must not overlap'
$emptyRuntime = New-Desktop 'empty-runtime'
Assert-Rejected { & $updater -InstallRoot $install -DesktopPath $desktop -DotNetRoot $emptyRuntime -IconPath $icon -PackageDir $packageA } 'existing x64 .NET 10 runtime'
$badIcon = Join-Path $work 'invalid.ico'
Write-File $work 'invalid.ico' 'This is not an ICO'
Assert-Rejected { & $updater -InstallRoot $install -DesktopPath $desktop -DotNetRoot $DotNetRoot -IconPath $badIcon -PackageDir $packageA } 'Windows ICO image'
$junction = Join-Path $work 'install-junction'
New-Item -ItemType Junction -Path $junction -Target $install | Out-Null
Assert-Rejected { & $updater -InstallRoot $junction -DesktopPath $desktop -DotNetRoot $DotNetRoot -IconPath $icon -PackageDir $packageA } 'reparse'
$desktopJunction = Join-Path $work 'desktop-junction'
New-Item -ItemType Junction -Path $desktopJunction -Target $desktop | Out-Null
Assert-Rejected { & $updater -InstallRoot $install -DesktopPath $desktopJunction -DotNetRoot $DotNetRoot -IconPath $icon -PackageDir $packageA } 'reparse'
$childTarget = New-Desktop 'owned-child-target'
Write-File $childTarget 'keep.txt' 'Do not follow a child junction'
$childTargetHash = Hash (Join-Path $childTarget 'keep.txt')
foreach ($child in @('owner.json','current.json','launcher.ps1','blue.ico')) {
    $childRoot = New-Desktop ('owned-child-' + $child.Replace('.','-'))
    $owner = Get-Content -LiteralPath (Join-Path $install 'owner.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $owner.installRoot = $childRoot
    if ($child -ne 'owner.json') { Write-File $childRoot 'owner.json' ($owner | ConvertTo-Json) }
    if ($child -ne 'launcher.ps1') { [IO.File]::Copy((Join-Path $install 'launcher.ps1'), (Join-Path $childRoot 'launcher.ps1')) }
    if ($child -ne 'blue.ico') { [IO.File]::Copy((Join-Path $install 'blue.ico'), (Join-Path $childRoot 'blue.ico')) }
    New-Item -ItemType Junction -Path (Join-Path $childRoot $child) -Target $childTarget | Out-Null
    Assert-Rejected { & $updater -InstallRoot $childRoot -DesktopPath $desktop -DotNetRoot $DotNetRoot -IconPath $icon -PackageDir $packageA } 'reparse'
    Assert-True ((Hash (Join-Path $childTarget 'keep.txt')) -ceq $childTargetHash) 'An owned child junction changed its external target.'
}
$linkedDesktop = New-Desktop 'child-link-desktop'
New-Item -ItemType Junction -Path (Join-Path $linkedDesktop 'LLPlayer Avalonia (Windows).lnk') -Target $childTarget | Out-Null
Assert-Rejected { & $updater -InstallRoot $install -DesktopPath $linkedDesktop -DotNetRoot $DotNetRoot -IconPath $icon -PackageDir $packageA } 'reparse'
Assert-True ((Hash $first.currentPath) -ceq $selectedHash -and (Hash $first.shortcutPath) -ceq $shortcutHash) 'Rejected unsafe inputs changed selection or shortcut.'
Write-Host "Windows desktop test-build contracts passed: $script:passed. Fixtures retained: $work"

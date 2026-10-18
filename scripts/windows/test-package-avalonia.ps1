# Deterministic, offline package-content tests. No application build or network access.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$validator = Join-Path $PSScriptRoot 'validate-avalonia-package.ps1'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) { throw 'Package validator has not been implemented.' }
$work = Join-Path $repoRoot ('artifacts\windows-avalonia-package-tests-' + [guid]::NewGuid().ToString('N'))
$junctions = New-Object 'System.Collections.Generic.List[string]'
$passed = 0
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Assert-SafeFixturePath([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $boundary = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($boundary, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture path outside checkout artifacts/.' }
    $ancestor = $full
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Fixture paths must not contain reparse points: $ancestor" }
        }
        $parent = [System.IO.Directory]::GetParent($ancestor)
        if ($null -eq $parent) { break }
        $ancestor = $parent.FullName
    }
}
function Write-FixtureFile([string]$Root, [string]$Relative, [string]$Content = 'fixture') {
    $path = Join-Path $Root $Relative
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    [System.IO.File]::WriteAllText($path, $Content, $utf8)
}
$ffmpeg = @('avcodec-62.dll','avdevice-62.dll','avfilter-11.dll','avformat-62.dll','avutil-60.dll','swresample-6.dll','swscale-9.dll')
$natives = @('av_libglesv2.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','onnxruntime.dll','onnxruntime_providers_shared.dll','e_sqlite3.dll','soft_oal.dll') +
    @('ggml-base-whisper.dll','ggml-cpu-whisper.dll','ggml-whisper.dll','whisper.dll' | ForEach-Object { 'runtimes/win-x64/' + $_ }) +
    @($ffmpeg | ForEach-Object { 'FFmpeg/' + $_ })
function New-Fixture([string]$Name) {
    $dir = Join-Path $work $Name
    $required = @('LLPlayer.Avalonia.exe','LLPlayer.Avalonia.dll','LLPlayer.Avalonia.deps.json','LLPlayer.Avalonia.runtimeconfig.json',
        'LLPlayer.Core.dll','FlyleafLib.dll','Assets/silero_vad.onnx','LICENSE','THIRD-PARTY-NOTICES.md',
        'FFmpeg/LICENSE.GPLv3.txt','FFmpeg/SOURCE.txt','licenses/OpenALSoft.LICENSE','licenses/OpenALSoft.SOURCE.txt','licenses/Avalonia.ANGLE.LICENSE','licenses/OpenALSoft.BSD-3Clause.txt',
        'dub_sidecar/server.py','dub_sidecar/pyproject.toml','dub_sidecar/uv.lock','dub_sidecar/README.md')
    foreach ($path in @($required) + @($natives)) { Write-FixtureFile $dir $path }
    $files = @($natives | ForEach-Object { [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $dir $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
    $manifest = [ordered]@{ schema=1; runtime='win-x64'; components=@(
        [ordered]@{ name='FFmpeg'; version='n8.0-16-gd8605a6b55-20250925'; license='GPL'; source='https://git.ffmpeg.org/ffmpeg.git'; provenance='Tracked binaries; original build recipe and corresponding source offer remain unverified.' },
        [ordered]@{ name='OpenAL Soft'; version='1.23.1'; license='LGPL-2.0-or-later'; source='https://github.com/kcat/openal-soft/tree/1.23.1'; provenance='Pinned OpenALSoft NuGet 1.23.1' }
    ); files=$files }
    Write-FixtureFile $dir 'native-dependencies.json' ($manifest | ConvertTo-Json -Depth 6)
    return $dir
}
function Assert-Accepted([string]$Label, [string]$Dir) {
    & $validator -PackageDir $Dir | Out-Null
    $script:passed++
}
function Assert-Rejected([string]$Label, [scriptblock]$Mutation, [string]$Pattern) {
    $dir = New-Fixture ('case-' + $script:passed)
    & $Mutation $dir
    try { & $validator -PackageDir $dir | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw "$Label rejected for wrong reason: $($_.Exception.Message)" }
        $script:passed++
        return
    }
    throw "$Label was accepted."
}
Assert-SafeFixturePath $work
try {
    Assert-Accepted 'valid synthetic package' (New-Fixture 'valid')
    Assert-Rejected 'missing Avalonia ANGLE native' { param($d) Remove-Item -LiteralPath (Join-Path $d 'av_libglesv2.dll') } 'missing required file.*av_libglesv2'
    Assert-Rejected 'missing Core' { param($d) Remove-Item -LiteralPath (Join-Path $d 'LLPlayer.Core.dll') } 'missing required file.*LLPlayer.Core.dll'
    Assert-Rejected 'empty legal file' { param($d) Write-FixtureFile $d 'LICENSE' '' } 'non-empty regular file.*LICENSE'
    Assert-Rejected 'missing FFmpeg' { param($d) Remove-Item -LiteralPath (Join-Path $d 'FFmpeg/avcodec-62.dll') } 'missing required file.*avcodec-62.dll'
    Assert-Rejected 'missing OpenAL' { param($d) Remove-Item -LiteralPath (Join-Path $d 'soft_oal.dll') } 'missing required file.*soft_oal.dll'
    Assert-Rejected 'missing OpenAL BSD notice' { param($d) Remove-Item -LiteralPath (Join-Path $d 'licenses/OpenALSoft.BSD-3Clause.txt') } 'missing required file.*OpenALSoft.BSD-3Clause'
    Assert-Rejected 'missing uv.lock' { param($d) Remove-Item -LiteralPath (Join-Path $d 'dub_sidecar/uv.lock') } 'missing required file.*uv.lock'
    foreach ($name in @('LLPlayer.Avalonia.json','LLPlayer.Avalonia.json.bad','.LLPlayer.Avalonia.json.token.tmp',
        'LLPlayer.Translation.json','.LLPlayer.Translation.json.token.tmp','LLPlayer.Recognition.json.bad',
        'LLPlayer.WordList.json','LLPlayer.Config.json','crash.log','nested/session.LOG','.env.local','.envtoken',
        'movie.ru.dub.flac','movie.ru.voices.json','ggml-base.bin','eng.traineddata','yt-dlp.exe')) {
        $fixtureName = $name
        Assert-Rejected ("forbidden file " + $name) { param($d) Write-FixtureFile $d $fixtureName } 'forbidden runtime|foreign native'
    }
    foreach ($name in @('DubEngine','dubmodels','dub_sidecar/.venv','whispermodels','tesseractmodels','Recordings','Snapshots',
        'cuda','runtimes/linux-x64','runtimes/cuda/win-x64','runtimes/win-x86','x86')) {
        $fixtureName = $name
        Assert-Rejected ("forbidden directory " + $name) { param($d) [System.IO.Directory]::CreateDirectory((Join-Path $d $fixtureName)) | Out-Null } 'forbidden runtime|unexpected runtime'
    }
    Assert-Rejected 'foreign root native' { param($d) Write-FixtureFile $d 'libfoo.so' } 'foreign native'
    Assert-Rejected 'unexpected FFmpeg' { param($d) Write-FixtureFile $d 'FFmpeg/avcodec-61.dll' } 'unexpected FFmpeg'
    Assert-Rejected 'unexpected runtime payload' { param($d) Write-FixtureFile $d 'runtimes/win-x64/surprise.dll' } 'unexpected runtime'
    Assert-Rejected 'manifest mismatch' { param($d) Write-FixtureFile $d 'soft_oal.dll' 'tampered' } 'native manifest hash mismatch'
    Assert-Rejected 'missing manifest entry' {
        param($d)
        $manifest = Get-Content -LiteralPath (Join-Path $d 'native-dependencies.json') -Raw | ConvertFrom-Json
        $manifest.files = @($manifest.files | Where-Object { $_.path -ne 'soft_oal.dll' })
        Write-FixtureFile $d 'native-dependencies.json' ($manifest | ConvertTo-Json -Depth 6)
    } 'native manifest.*missing'
    Assert-Rejected 'manifest traversal' {
        param($d)
        $manifest = Get-Content -LiteralPath (Join-Path $d 'native-dependencies.json') -Raw | ConvertFrom-Json
        $manifest.files[0].path = '../outside.dll'
        Write-FixtureFile $d 'native-dependencies.json' ($manifest | ConvertTo-Json -Depth 6)
    } 'native manifest.*path'
    # NTFS junction fixtures are possible without admin/symlink privileges.
    $outside = New-Fixture 'junction-target'
    $junctionParent = Join-Path $work 'parent-link'
    New-Item -ItemType Junction -Path $junctionParent -Target $outside | Out-Null
    $junctions.Add($junctionParent)
    try { & $validator -PackageDir $junctionParent | Out-Null; throw 'root junction was accepted.' }
    catch { if ($_.Exception.Message -notmatch 'reparse') { throw }; $passed++ }
    $parentChild = New-Fixture 'junction-target/inside'
    try { & $validator -PackageDir (Join-Path $junctionParent 'inside') | Out-Null; throw 'Parent junction was accepted.' }
    catch { if ($_.Exception.Message -notmatch 'reparse') { throw }; $passed++ }
    Assert-Rejected 'nested directory junction' {
        param($d)
        $link = Join-Path $d 'linked-data'
        New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
        $junctions.Add($link)
    } 'reparse'
    $archiver = Join-Path $PSScriptRoot 'write-avalonia-archive.ps1'
    if (-not (Test-Path -LiteralPath $archiver -PathType Leaf)) { throw 'Package ZIP writer has not been implemented.' }
    $archivePackage = New-Fixture 'archive-package'
    $unsafeArchive = Join-Path $junctionParent 'archive-parent/LLPlayer-Avalonia-fixture-win-x64.zip'
    try { & $archiver -PackageDir $archivePackage -ArchivePath $unsafeArchive | Out-Null; throw 'ZIP ancestor junction was accepted.' }
    catch { if ($_.Exception.Message -notmatch 'reparse') { throw } }
    if (Test-Path -LiteralPath (Join-Path $outside 'archive-parent')) { throw 'ZIP writer created a directory through a junction before rejecting it.' }
    $passed++
    $archivePath = Join-Path $work 'LLPlayer-Avalonia-fixture-win-x64.zip'
    & $archiver -PackageDir $archivePackage -ArchivePath $archivePath | Out-Null
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or (Get-Item -LiteralPath $archivePath).Length -eq 0) { throw 'ZIP writer did not produce a real archive.' }
    $passed++
    try { & $archiver -PackageDir $archivePackage -ArchivePath $archivePath | Out-Null; throw 'Existing ZIP was overwritten.' }
    catch { if ($_.Exception.Message -notmatch 'already exists') { throw }; $passed++ }
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $lock = @($zip.Entries | Where-Object { $_.FullName -eq 'LLPlayer-Avalonia-fixture-win-x64/dub_sidecar/uv.lock' })
        if ($lock.Count -ne 1 -or $lock[0].Length -eq 0) { throw 'Real archive has no non-empty uv.lock source entry.' }
        $passed++
    }
    finally { $zip.Dispose() }
    $publisher = Join-Path $PSScriptRoot 'publish-avalonia.ps1'
    $savedPath = $env:PATH
    try {
        $env:PATH = ''
        & $publisher -CheckPackage $archivePackage | Out-Null
        $passed++
    }
    finally { $env:PATH = $savedPath }
    try { & $publisher -CheckPackage $archivePackage -OutDir $work; throw 'CheckPackage accepted publish options.' }
    catch { if ($_.Exception.Message -notmatch 'cannot be combined') { throw }; $passed++ }
    # Run a copy under a synthetic checkout whose artifacts/ is a junction. A pre-existing sentinel in its
    # target must survive both the runner's creation guard and its finally cleanup path.
    $childRepo = Join-Path $work 'guard-checkout'
    $childScripts = Join-Path $childRepo 'scripts/windows'
    [System.IO.Directory]::CreateDirectory($childScripts) | Out-Null
    foreach ($name in @('validate-avalonia-package.ps1','write-avalonia-archive.ps1','publish-avalonia.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $childScripts $name)
    }
    $childRunner = Join-Path $childScripts 'test-package-avalonia.ps1'
    $childSource = [System.IO.File]::ReadAllText($PSCommandPath).Replace("[guid]::NewGuid().ToString('N')", "'guardprobe'")
    [System.IO.File]::WriteAllText($childRunner, $childSource, $utf8)
    $escapeTarget = Join-Path $work 'guard-target'
    $escapeWork = Join-Path $escapeTarget 'windows-avalonia-package-tests-guardprobe'
    Write-FixtureFile $escapeWork 'keep.txt' 'keep'
    $childArtifacts = Join-Path $childRepo 'artifacts'
    New-Item -ItemType Junction -Path $childArtifacts -Target $escapeTarget | Out-Null
    $junctions.Add($childArtifacts)
    $childPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $savedNativePreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $childOutput = & $childPowerShell -NoProfile -ExecutionPolicy Bypass -File $childRunner 2>&1 | Out-String
        $childExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $savedNativePreference }
    if ($childExit -eq 0 -or $childOutput -notmatch 'reparse') { throw "Nested runner did not reject artifacts junction: $childOutput" }
    if (-not (Test-Path -LiteralPath (Join-Path $escapeWork 'keep.txt'))) { throw 'Fixture runner cleanup escaped through artifacts junction and deleted the target sentinel.' }
    if (Test-Path -LiteralPath (Join-Path $escapeWork 'valid')) { throw 'Fixture runner created a package through artifacts junction.' }
    $passed++
    Write-Host "Windows Avalonia offline package tests passed: $passed."
}
finally {
    foreach ($link in $junctions) {
        $fullLink = [System.IO.Path]::GetFullPath($link)
        if (-not $fullLink.StartsWith($work + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture junction cleanup target.' }
        Assert-SafeFixturePath ([System.IO.Path]::GetDirectoryName($fullLink))
        if (Test-Path -LiteralPath $fullLink) { [System.IO.Directory]::Delete($fullLink) }
    }
    $fullWork = [System.IO.Path]::GetFullPath($work)
    $allowed = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullWork.StartsWith($allowed, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup target.' }
    Assert-SafeFixturePath $fullWork
    if (Test-Path -LiteralPath $fullWork) { Remove-Item -LiteralPath $fullWork -Recurse -Force }
}

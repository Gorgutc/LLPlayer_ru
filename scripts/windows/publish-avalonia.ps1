param(
    [string]$OutDir,
    [string]$StagingDir,
    [switch]$Force,
    [string]$CheckPackage
)
# Additive Windows Avalonia ZIP. WPF release packaging remains build-package/action.yml.
# -CheckPackage runs only read-only validation: no build, restore or network.
# The default staging directory is inside this checkout's ignored artifacts/ and is removed safely.
# An explicitly supplied, new -StagingDir is retained for inspection.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$validator = Join-Path $PSScriptRoot 'validate-avalonia-package.ps1'
if ($CheckPackage) {
    if ($OutDir -or $StagingDir -or $Force) { throw '-CheckPackage cannot be combined with publish options.' }
    & $validator -PackageDir $CheckPackage
    return
}
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'The Windows Avalonia publisher must run on Windows.' }
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\','/')
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Assert-NormalPath([string]$Path) {
    $current = [System.IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Publish paths must not contain reparse points: $current" }
        }
        $parent = [System.IO.Directory]::GetParent($current)
        if ($null -eq $parent) { break }
        $current = $parent.FullName
    }
}
function Copy-RegularFile([string]$Source, [string]$Target) {
    Assert-NormalPath $Source
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf) -or (Get-Item -LiteralPath $Source).Length -le 0) { throw "Required source file is missing or empty: $Source" }
    Assert-NormalPath $Target
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Target)) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Target -Force
}
function Write-PackageText([string]$Relative, [string]$Content) {
    $path = Join-Path $package $Relative
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    [System.IO.File]::WriteAllText($path, $Content, $utf8)
}
function Resolve-RestoredPackage([string]$Id, [string]$Version) {
    $key = "$Id/$Version".ToLowerInvariant()
    $library = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name.ToLowerInvariant() -eq $key })
    if ($library.Count -ne 1) { throw "Expected restored package $Id $Version is missing from project.assets.json." }
    foreach ($folder in $assets.packageFolders.PSObject.Properties) {
        $candidate = Join-Path $folder.Name $library[0].Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) { Assert-NormalPath $candidate; return $candidate }
    }
    throw "Restored package $Id $Version cannot be located."
}
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'LLPlayer.Avalonia/LLPlayer.Avalonia.csproj') -Raw
$version = [string](@($project.Project.PropertyGroup.Version | Where-Object { $_ })[0])
if ($version -notmatch '^[0-9A-Za-z][0-9A-Za-z._+-]{0,63}$' -or $version.Contains('..')) { throw 'Avalonia project version is not path-safe.' }
$name = "LLPlayer-Avalonia-$version-win-x64"
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'artifacts/windows-avalonia' }
$out = [System.IO.Path]::GetFullPath($OutDir)
Assert-NormalPath $out
[System.IO.Directory]::CreateDirectory($out) | Out-Null
$archive = Join-Path $out ($name + '.zip')
$checksum = $archive + '.sha256'
foreach ($target in @($archive,$checksum)) {
    Assert-NormalPath $target
    if (Test-Path -LiteralPath $target) {
        if (-not $Force) { throw "Refusing to overwrite $target (use -Force)." }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Output target is not a regular file: $target" }
    }
}
$keepStaging = [bool]$StagingDir
if (-not $StagingDir) { $StagingDir = Join-Path $repoRoot ('artifacts/windows-avalonia-staging-' + [guid]::NewGuid().ToString('N')) }
$staging = [System.IO.Path]::GetFullPath($StagingDir)
$stagingBoundary = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
if (-not $staging.StartsWith($stagingBoundary, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'StagingDir must be a new directory inside this checkout artifacts/.' }
Assert-NormalPath $staging
if (Test-Path -LiteralPath $staging) { throw "Staging directory already exists: $staging" }
[System.IO.Directory]::CreateDirectory($staging) | Out-Null
$package = Join-Path $staging $name
$ffmpeg = @('avcodec-62.dll','avdevice-62.dll','avfilter-11.dll','avformat-62.dll','avutil-60.dll','swresample-6.dll','swscale-9.dll')
$publishSucceeded = $false
Push-Location $repoRoot
try {
    & dotnet publish 'LLPlayer.Avalonia/LLPlayer.Avalonia.csproj' -c Release -r win-x64 --self-contained false -warnaserror -o $package | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Avalonia publish failed with exit code $LASTEXITCODE." }
    Assert-NormalPath $package
    # TesseractOCR's transitive targets copy both Windows native folders. Portable OCR remains Linux-only.
    # Reject drift before removing just the known unused Tesseract payload.
    foreach ($folder in @('x64','x86')) {
        $path = Join-Path $package $folder
        if (Test-Path -LiteralPath $path) {
            Assert-NormalPath $path
            foreach ($item in @(Get-ChildItem -LiteralPath $path -Force)) {
                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.PSIsContainer -or
                    $item.Name -notmatch '^(leptonica-[0-9.]+|tesseract[0-9]+)\.dll$') { throw "Unexpected unused Tesseract publish payload: $($item.FullName)" }
            }
            $cleanup = [System.IO.Path]::GetFullPath($path)
            if (-not $cleanup.StartsWith($package + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publish cleanup target.' }
            Remove-Item -LiteralPath $cleanup -Recurse -Force
        }
    }
    foreach ($dll in $ffmpeg) { Copy-RegularFile (Join-Path $repoRoot "FFmpeg/$dll") (Join-Path $package "FFmpeg/$dll") }
    foreach ($file in @('server.py','pyproject.toml','uv.lock','README.md')) { Copy-RegularFile (Join-Path $repoRoot "dub_sidecar/$file") (Join-Path $package "dub_sidecar/$file") }
    Copy-RegularFile (Join-Path $repoRoot 'LICENSE') (Join-Path $package 'LICENSE')
    Copy-RegularFile (Join-Path $repoRoot 'LICENSE') (Join-Path $package 'FFmpeg/LICENSE.GPLv3.txt')
    Copy-RegularFile (Join-Path $repoRoot 'LLPlayer.Avalonia/THIRD-PARTY-NOTICES.md') (Join-Path $package 'THIRD-PARTY-NOTICES.md')
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot 'LLPlayer.Avalonia/obj/project.assets.json') -Raw | ConvertFrom-Json
    $angleRecord = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Avalonia.Angle.Windows.Natives/*' })
    if ($angleRecord.Count -ne 1) { throw 'Expected one restored Avalonia ANGLE native package.' }
    $angleVersion = $angleRecord[0].Name.Split('/')[1]
    $angle = Resolve-RestoredPackage 'Avalonia.Angle.Windows.Natives' $angleVersion
    Copy-RegularFile (Join-Path $angle 'LICENSE') (Join-Path $package 'licenses/Avalonia.ANGLE.LICENSE')
    [xml]$angleNuspec = Get-Content -LiteralPath (Join-Path $angle 'avalonia.angle.windows.natives.nuspec') -Raw
    $angleSource = [string]$angleNuspec.package.metadata.repository.url
    $angleCommit = [string]$angleNuspec.package.metadata.repository.commit
    $openal = Resolve-RestoredPackage 'Silk.NET.OpenAL.Soft.Native' '1.23.1'
    $restoredOpenal = Join-Path $openal 'runtimes/win-x64/native/soft_oal.dll'
    if ((Get-FileHash -LiteralPath $restoredOpenal -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath (Join-Path $package 'soft_oal.dll') -Algorithm SHA256).Hash) { throw 'Published OpenAL DLL differs from the pinned restored native package.' }
    # The NuGet package declares LGPL-2.0-or-later but contains no license texts. Ship the exact upstream
    # 1.23.1 texts pinned to immutable commit d3875f333fb6abe2f39d82caca329414871ae53b.
    foreach ($legal in @(
        @{ File='COPYING'; Target='licenses/OpenALSoft.LICENSE'; Hash='d808ce217e5b611854da622b57ec29fe545584c48bc5352fae72a4b6e5074a15' },
        @{ File='BSD-3Clause'; Target='licenses/OpenALSoft.BSD-3Clause.txt'; Hash='b5a08cf98996dd83aa906a72c163600f6cb17a65ce546a553815a1267abd43ff' }
    )) {
        $source = Join-Path $repoRoot ('LLPlayer.Avalonia/Licenses/OpenALSoft/' + $legal.File)
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -cne $legal.Hash) { throw "Pinned OpenAL legal text changed: $($legal.File)" }
        Copy-RegularFile $source (Join-Path $package $legal.Target)
    }
    Write-PackageText 'licenses/OpenALSoft.SOURCE.txt' @"
OpenAL Soft 1.23.1
NuGet: Silk.NET.OpenAL.Soft.Native 1.23.1 (restored dependency, no publisher download)
Upstream source: https://github.com/kcat/openal-soft/tree/d3875f333fb6abe2f39d82caca329414871ae53b
Upstream tag 1.23.1 resolves to commit d3875f333fb6abe2f39d82caca329414871ae53b.
License: LGPL-2.0-or-later, with BSD-3-Clause portions.
The NuGet package declares the license expression but does not include the legal texts.
COPYING and BSD-3Clause are shipped verbatim from that immutable upstream commit.
COPYING SHA-256: d808ce217e5b611854da622b57ec29fe545584c48bc5352fae72a4b6e5074a15
BSD-3Clause SHA-256: b5a08cf98996dd83aa906a72c163600f6cb17a65ce546a553815a1267abd43ff
The exact shipped DLL SHA-256 is recorded in native-dependencies.json.
The separate dynamically loaded DLL can be replaced by the user.
Windows prerequisite: Microsoft Visual C++ x64 Redistributable 2022 or newer (MSVCP140/VCRUNTIME140/VCRUNTIME140_1).
This provenance receipt does not establish a formal corresponding-source offer for the native package.
"@
    $ffmpegVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'FFmpeg/avcodec-62.dll')).ProductVersion
    $ffmpegRevision = 'not available in PE version metadata'
    if ($ffmpegVersion -match '-g([0-9a-f]+)(-|$)') { $ffmpegRevision = $Matches[1] }
    Write-PackageText 'FFmpeg/SOURCE.txt' @"
FFmpeg Windows x64 shared libraries copied individually from the tracked FFmpeg/ files.
Binary version: $ffmpegVersion
Upstream revision reported by the binaries: $ffmpegRevision
Upstream source: https://git.ffmpeg.org/ffmpeg.git
License text: LICENSE.GPLv3.txt (GPLv3 text shipped by LLPlayer).
Exact DLL SHA-256 values: ../native-dependencies.json.
The original binary build archive, complete build recipe/dependency source provenance and a formal corresponding-source offer have not been established by this packaging baseline.
This receipt does not establish public release or source-offer acceptance.
"@
    $cpu = @('ggml-base-whisper.dll','ggml-cpu-whisper.dll','ggml-whisper.dll','whisper.dll')
    $native = @('av_libglesv2.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','onnxruntime.dll','onnxruntime_providers_shared.dll','e_sqlite3.dll','soft_oal.dll') +
        @($cpu | ForEach-Object { 'runtimes/win-x64/' + $_ }) + @($ffmpeg | ForEach-Object { 'FFmpeg/' + $_ })
    $files = @($native | ForEach-Object {
        $path = Join-Path $package $_
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Expected published Windows native is missing: $_" }
        [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ schema=1; runtime='win-x64'; components=@(
        [ordered]@{ name='FFmpeg'; version=$ffmpegVersion; license='GPL'; source='https://git.ffmpeg.org/ffmpeg.git'; provenance='Tracked Windows DLLs; original build/dependency source provenance and formal source offer remain unverified.' },
        [ordered]@{ name='Avalonia ANGLE'; version=$angleVersion; license='See licenses/Avalonia.ANGLE.LICENSE'; source=$angleSource; provenance=($angleCommit + '; restored Avalonia.Angle.Windows.Natives package') },
        [ordered]@{ name='OpenAL Soft'; version='1.23.1'; license='LGPL-2.0-or-later; BSD-3-Clause portions'; source='https://github.com/kcat/openal-soft/tree/d3875f333fb6abe2f39d82caca329414871ae53b'; provenance='Silk.NET.OpenAL.Soft.Native NuGet 1.23.1; LGPL COPYING and BSD-3Clause from the immutable upstream 1.23.1 commit, hashes verified.' },
        [ordered]@{ name='Whisper.net CPU'; version='1.9.0'; license='MIT'; source='https://github.com/sandrohanea/whisper.net/tree/1.9.0'; provenance='Four explicit Windows x64 CPU DLLs from the pinned runtime package; model/runtime acceptance separate.' }
    ); files=$files }
    Write-PackageText 'native-dependencies.json' ($manifest | ConvertTo-Json -Depth 6)
    & $validator -PackageDir $package
    $stagedArchive = Join-Path $staging ($name + '.zip')
    & (Join-Path $PSScriptRoot 'write-avalonia-archive.ps1') -PackageDir $package -ArchivePath $stagedArchive | Out-Null
    $sha = (Get-FileHash -LiteralPath $stagedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-NormalPath $archive
    Copy-Item -LiteralPath $stagedArchive -Destination $archive -Force
    $copiedSha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($copiedSha -cne $sha) { throw 'Published ZIP differs from the verified staging archive.' }
    Assert-NormalPath $checksum
    [System.IO.File]::WriteAllText($checksum, ($sha + '  ' + $name + ".zip" + [Environment]::NewLine), $utf8)
    Write-Host "Windows Avalonia archive: $archive ($((Get-Item -LiteralPath $archive).Length) bytes, sha256 $sha)"
    $publishSucceeded = $true
    Write-Output $archive
}
finally {
    Pop-Location
    if ($publishSucceeded -and -not $keepStaging -and (Test-Path -LiteralPath $staging)) {
        $allowed = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
        $cleanup = [System.IO.Path]::GetFullPath($staging)
        if (-not $cleanup.StartsWith($allowed, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe staging cleanup target: $cleanup" }
        Assert-NormalPath $cleanup
        Remove-Item -LiteralPath $cleanup -Recurse -Force
    }
}

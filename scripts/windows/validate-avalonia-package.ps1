param([Parameter(Mandatory = $true)][string]$PackageDir)
# Read-only positive/negative validation, usable without restore, build or network.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$root = [System.IO.Path]::GetFullPath($PackageDir).TrimEnd('\','/')
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Windows Avalonia package directory not found: $root" }
function Assert-NotReparse([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Windows Avalonia package contains a reparse point: $Path" }
}
# Checking ancestors prevents a package directory reached through a junction from bypassing the check.
$ancestor = $root
while ($ancestor) {
    Assert-NotReparse $ancestor
    $parent = [System.IO.Directory]::GetParent($ancestor)
    if ($null -eq $parent) { break }
    $ancestor = $parent.FullName
}
$items = New-Object 'System.Collections.Generic.List[System.IO.FileSystemInfo]'
$pending = New-Object 'System.Collections.Generic.Queue[string]'
$pending.Enqueue($root)
while ($pending.Count -gt 0) {
    foreach ($item in @(Get-ChildItem -LiteralPath $pending.Dequeue() -Force)) {
        Assert-NotReparse $item.FullName
        $items.Add($item)
        if ($item.PSIsContainer) { $pending.Enqueue($item.FullName) }
    }
}
$ffmpeg = @('avcodec-62.dll','avdevice-62.dll','avfilter-11.dll','avformat-62.dll','avutil-60.dll','swresample-6.dll','swscale-9.dll')
$cpu = @('ggml-base-whisper.dll','ggml-cpu-whisper.dll','ggml-whisper.dll','whisper.dll')
$native = @('av_libglesv2.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','onnxruntime.dll','onnxruntime_providers_shared.dll','e_sqlite3.dll','soft_oal.dll') +
    @($cpu | ForEach-Object { 'runtimes/win-x64/' + $_ }) + @($ffmpeg | ForEach-Object { 'FFmpeg/' + $_ })
$required = @('LLPlayer.Avalonia.exe','LLPlayer.Avalonia.dll','LLPlayer.Avalonia.deps.json','LLPlayer.Avalonia.runtimeconfig.json',
    'FlyleafLib.dll','LLPlayer.Core.dll','Assets/silero_vad.onnx','LICENSE','THIRD-PARTY-NOTICES.md','native-dependencies.json',
    'FFmpeg/LICENSE.GPLv3.txt','FFmpeg/SOURCE.txt','licenses/OpenALSoft.LICENSE','licenses/OpenALSoft.SOURCE.txt','licenses/Avalonia.ANGLE.LICENSE','licenses/OpenALSoft.BSD-3Clause.txt',
    'dub_sidecar/server.py','dub_sidecar/pyproject.toml','dub_sidecar/uv.lock','dub_sidecar/README.md') + $native
foreach ($relative in $required) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Windows Avalonia package is missing required file $relative." }
    if ((Get-Item -LiteralPath $path -Force).Length -le 0) { throw "Windows Avalonia package requires a non-empty regular file $relative." }
}
function Test-ManagedDll([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $reader = New-Object System.IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { return $false }
        $stream.Position = 0x3c
        $pe = $reader.ReadInt32()
        if ($pe -lt 0 -or $pe + 256 -gt $stream.Length) { return $false }
        $stream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550) { return $false }
        $stream.Position = $pe + 24
        $magic = $reader.ReadUInt16()
        if ($magic -eq 0x10b) { $directory = $pe + 24 + 96 }
        elseif ($magic -eq 0x20b) { $directory = $pe + 24 + 112 }
        else { return $false }
        $stream.Position = $directory + 14 * 8
        return ($reader.ReadUInt32() -ne 0 -and $reader.ReadUInt32() -ne 0)
    }
    finally { $reader.Dispose() }
}
foreach ($item in $items) {
    $relative = $item.FullName.Substring($root.Length + 1).Replace('\','/')
    $name = $item.Name
    if ($item.PSIsContainer) {
        if ($name -match '^(DubEngine|dubmodels|\.venv|venv|__pycache__|whispermodels|Whisper|tesseractmodels|Recordings|Snapshots|x86|x64|cuda|vulkan|openvino|noavx|linux-x64|win-x86|win-arm64|osx-x64|osx-arm64)$') {
            throw "Windows Avalonia package contains forbidden runtime directory $relative."
        }
        if ($relative -like 'runtimes/*' -and $relative -ne 'runtimes/win-x64') { throw "Windows Avalonia package contains an unexpected runtime directory $relative." }
        continue
    }
    if ($name -match '^\.?LLPlayer\.(Avalonia|Translation|Recognition|WordList|Config|Engine|PlayerConfig)\.json($|\.)' -or
        $name -match '^\.env|\.log$|\.dmp$|\.dump$|\.ru\.dub\.|\.ru\.voices\.json$|^ggml-.*\.bin$|\.traineddata$|^yt-dlp\.exe$') {
        throw "Windows Avalonia package contains forbidden runtime file $relative."
    }
    if ($relative -like 'FFmpeg/*' -and $item.Extension -eq '.dll' -and $ffmpeg -notcontains $name) { throw "Windows Avalonia package contains an unexpected FFmpeg library $relative." }
    if ($relative -like 'runtimes/*' -and $native -notcontains $relative) { throw "Windows Avalonia package contains an unexpected runtime payload $relative." }
    if ($name -match '\.so($|\.)|\.dylib$') { throw "Windows Avalonia package contains a foreign native library $relative." }
    if ($item.Extension -eq '.dll' -and $native -notcontains $relative -and $relative -notin @('LLPlayer.Avalonia.dll','LLPlayer.Core.dll','FlyleafLib.dll')) {
        if (-not (Test-ManagedDll $item.FullName)) { throw "Windows Avalonia package contains an unexpected native or invalid DLL $relative." }
    }
}
$manifest = Get-Content -LiteralPath (Join-Path $root 'native-dependencies.json') -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.runtime -ne 'win-x64') { throw 'Windows Avalonia native manifest schema/runtime is invalid.' }
$seen = @{}
foreach ($record in @($manifest.files)) {
    $relative = [string]$record.path
    if ($relative -notmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -or $relative.Contains('..') -or $native -notcontains $relative) {
        throw "Windows Avalonia native manifest has an unexpected path $relative."
    }
    if ($seen.ContainsKey($relative)) { throw "Windows Avalonia native manifest has a duplicate path $relative." }
    $seen[$relative] = $true
    $expected = [string]$record.sha256
    if ($expected -cnotmatch '^[0-9a-f]{64}$') { throw "Windows Avalonia native manifest hash is invalid for $relative." }
    $actual = (Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $expected) { throw "Windows Avalonia native manifest hash mismatch for $relative." }
}
foreach ($relative in $native) {
    if (-not $seen.ContainsKey($relative)) { throw "Windows Avalonia native manifest is missing $relative." }
}
foreach ($name in @('FFmpeg','OpenAL Soft')) {
    $components = @($manifest.components | Where-Object { $_.name -eq $name })
    if ($components.Count -ne 1 -or -not $components[0].version -or -not $components[0].license -or
        -not $components[0].source -or -not $components[0].provenance) { throw "Windows Avalonia native manifest is missing component provenance for $name." }
}
Write-Host "Windows Avalonia package content validation passed: $root"

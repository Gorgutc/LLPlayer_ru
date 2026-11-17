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
$native = @('av_libglesv2.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','onnxruntime.dll','onnxruntime_providers_shared.dll','e_sqlite3.dll','soft_oal.dll',
    'lib/7z.dll','runtimes/win-x64/leptonica-1.85.0.dll','runtimes/win-x64/tesseract55.dll') +
    @($cpu | ForEach-Object { 'runtimes/win-x64/' + $_ }) + @($ffmpeg | ForEach-Object { 'FFmpeg/' + $_ })
$windowsMediaAssemblies = @('Vortice.DXGI.dll','Vortice.MediaFoundation.dll','Vortice.DirectX.dll',
    'Vortice.Mathematics.dll','SharpGen.Runtime.dll','SharpGen.Runtime.COM.dll')
$diarization = @('Assets/Diarization/sidecar.py','Assets/Diarization/requirements.txt',
    'Assets/Diarization/README.md','Assets/Diarization/NOTICE.md')
$controlApps = @('LLPlayer.ControlHost','mcp/LLPlayer.Mcp')
$control = @('LLPlayer.Control.dll','mcp/LLPlayer.Control.dll','mcp/ModelContextProtocol.dll','mcp/ModelContextProtocol.Core.dll','licenses/ModelContextProtocol.LICENSE') +
    @($controlApps | ForEach-Object { "$_.exe", "$_.dll", "$_.deps.json", "$_.runtimeconfig.json" })
$required = @('LLPlayer.Avalonia.exe','LLPlayer.Avalonia.dll','LLPlayer.Avalonia.deps.json','LLPlayer.Avalonia.runtimeconfig.json',
    'FlyleafLib.dll','LLPlayer.Core.dll','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','SevenZipSharp.dll',
    'licenses/Tesseract.LICENSE','licenses/Leptonica.LICENSE','licenses/SevenZip.LICENSE','licenses/SevenZipSharp.LICENSE',
    'licenses/Vortice.Windows.LICENSE','licenses/Vortice.Mathematics.LICENSE','licenses/SharpGen.Runtime.LICENSE',
    'Plugins/YoutubeDL/YoutubeDL.dll','Plugins/YoutubeDL/YoutubeDL.pdb','Assets/silero_vad.onnx','LICENSE','THIRD-PARTY-NOTICES.md','native-dependencies.json',
    'LibNMeCab.dll','LibNMeCab.IpaDicBin.dll','IpaDic/sys.dic','IpaDic/unk.dic','IpaDic/matrix.bin','IpaDic/char.bin','IpaDic/COPYING','IpaDic/AUTHORS',
    'FFmpeg/LICENSE.GPLv3.txt','FFmpeg/SOURCE.txt','licenses/OpenALSoft.LICENSE','licenses/OpenALSoft.SOURCE.txt','licenses/Avalonia.ANGLE.LICENSE','licenses/OpenALSoft.BSD-3Clause.txt',
    'dub_sidecar/server.py','dub_sidecar/pyproject.toml','dub_sidecar/uv.lock','dub_sidecar/README.md') + $native + $windowsMediaAssemblies + $diarization + $control
foreach ($relative in $required) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Windows Avalonia package is missing required file $relative." }
    if ((Get-Item -LiteralPath $path -Force).Length -le 0) { throw "Windows Avalonia package requires a non-empty regular file $relative." }
}
foreach ($app in $controlApps) {
    try {
        $deps = Get-Content -LiteralPath (Join-Path $root "$app.deps.json") -Raw | ConvertFrom-Json
        $runtime = (Get-Content -LiteralPath (Join-Path $root "$app.runtimeconfig.json") -Raw | ConvertFrom-Json).runtimeOptions
        $target = $deps.runtimeTarget.name
        $tfm = $runtime.tfm
    }
    catch { throw "Windows Avalonia companion runtime target metadata is invalid: $app." }
    if ($target -cne '.NETCoreApp,Version=v10.0/win-x64' -or $tfm -cne 'net10.0') {
        throw "Windows Avalonia companion runtime target must be portable net10.0/win-x64: $app."
    }
    $frameworks = if ($runtime.PSObject.Properties['frameworks']) { @($runtime.frameworks) }
        elseif ($runtime.PSObject.Properties['framework']) { @($runtime.framework) }
        else { @() }
    $expected = @('Microsoft.NETCore.App')
    if ($app -ceq 'LLPlayer.ControlHost') { $expected += 'Microsoft.AspNetCore.App' }
    $actual = @($frameworks | Where-Object { $_.version -cmatch '^10\.0\.\d+$' } | ForEach-Object { $_.name } | Sort-Object)
    if (@($frameworks).Count -ne $expected.Count -or ($actual -join ',') -cne (($expected | Sort-Object) -join ',')) {
        throw "Windows Avalonia companion framework references are incorrect: $app."
    }
}
$pluginPath = Join-Path $root 'Plugins/YoutubeDL/YoutubeDL.dll'
try { $pluginIdentity = [System.Reflection.AssemblyName]::GetAssemblyName($pluginPath) }
catch { throw 'Windows Avalonia YoutubeDL plugin must be a valid managed assembly.' }
# Read-only metadata screening works in Windows PowerShell 5.1 with an empty PATH. The .NET10
# package-load regression additionally resolves its types against the portable shared engine.
$pluginMetadata = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($pluginPath))
if ($pluginIdentity.Name -cne 'YoutubeDL' -or -not $pluginMetadata.Contains('.NETCoreApp,Version=v10.0') -or
    $pluginMetadata -match 'TargetPlatformAttribute|Version=v10.0-windows|PresentationFramework|PresentationCore|WindowsBase|Microsoft.WindowsDesktop.App') {
    throw 'Windows Avalonia YoutubeDL plugin must target portable net10.0 without WPF references.'
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
    if ($name -match '^YoutubeDL\.(dll|pdb)$' -and $relative -notin @('Plugins/YoutubeDL/YoutubeDL.dll','Plugins/YoutubeDL/YoutubeDL.pdb')) {
        throw "Windows Avalonia package contains misplaced plugin payload $relative."
    }
    if ($name -match '^(Vortice|SharpGen)\..*\.dll$' -and $windowsMediaAssemblies -notcontains $relative) {
        throw "Windows Avalonia package contains an unexpected Windows media dependency $relative."
    }
    if ($name -match '^(yt-dlp(\.exe|_linux.*)?|deno(\.exe)?|pdic-pipe(\.exe)?)$') { throw "Windows Avalonia package contains forbidden runtime helper $relative." }
    if ($name -match '^(yt-dlp|deno|pdic-pipe)([._-].*)?\.(zip|tar|tar\.gz|tar\.xz|tgz|7z)$' -or
        $name -match '^(\.deno|\.yt-dlp|deno-cache|yt-dlp-cache)$') {
        throw "Windows Avalonia package contains forbidden runtime helper archive or cache $relative."
    }
    if ($relative -like 'Plugins/*' -and $relative -notin @('Plugins/YoutubeDL','Plugins/YoutubeDL/YoutubeDL.dll','Plugins/YoutubeDL/YoutubeDL.pdb')) {
        throw "Windows Avalonia package contains unexpected plugin payload $relative."
    }
    if ($relative -like 'Assets/Diarization/*' -and ($item.PSIsContainer -or $relative -notin $diarization)) {
        throw "Windows Avalonia package contains unexpected diarization payload $relative."
    }
    if ($item.PSIsContainer) {
        if ($name -match '^(Automation|Control|DubEngine|dubmodels|\.venv|venv|__pycache__|whispermodels|Whisper|tesseractmodels|Recordings|Snapshots|x86|x64|cuda|vulkan|openvino|noavx|linux-x64|win-x86|win-arm64|osx-x64|osx-arm64)$') {
            throw "Windows Avalonia package contains forbidden runtime directory $relative."
        }
        if ($relative -like 'runtimes/*' -and $relative -ne 'runtimes/win-x64') { throw "Windows Avalonia package contains an unexpected runtime directory $relative." }
        continue
    }
    if ($name -match '^host\.json($|\.)' -or $name -match '^(jobs\.sqlite(?:-(wal|shm|journal))?|control\.lock)$' -or
        $name -match '^\.?LLPlayer\.(OnlineVideo|Avalonia|Translation|Recognition|WordList|Playback|Pdic|Batch|Dubbing|Config|Engine|PlayerConfig)\.json($|\.)' -or
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
foreach ($name in @('FFmpeg','OpenAL Soft','Tesseract','Leptonica','7-Zip')) {
    $components = @($manifest.components | Where-Object { $_.name -eq $name })
    if ($components.Count -ne 1 -or -not $components[0].version -or -not $components[0].license -or
        -not $components[0].source -or -not $components[0].provenance) { throw "Windows Avalonia native manifest is missing component provenance for $name." }
}
Write-Host "Windows Avalonia package content validation passed: $root"

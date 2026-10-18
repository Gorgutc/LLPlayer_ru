param(
    [string]$OutDir,
    [switch]$Offline
)
# Test tools only: this does not change tracked FFmpeg DLLs, production packaging, PATH or installed software.
# Pin: microsoft/winget-pkgs commit 173ea9c7f69b1ae1175be787d635cec8674d120c,
# manifests/g/Gyan/FFmpeg/Essentials/8.0/Gyan.FFmpeg.Essentials.installer.yaml.
# Only two exact ZIP members are copied; the verified archive remains an ignored local cache.
# Returns FFmpegPath, FFprobePath, ArchivePath, ReceiptPath and Version. -Offline never downloads.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows FFmpeg test tools require Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\','/')
$artifactRoot = Join-Path $repoRoot 'artifacts'
if (-not $OutDir) { $OutDir = Join-Path $artifactRoot 'tools/ffmpeg-test-8.0' }
$destination = [IO.Path]::GetFullPath($OutDir).TrimEnd('\','/')
if (-not $destination.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw '-OutDir must be inside this checkout artifacts/ directory, below its root.'
}
function Assert-NormalPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Test-tool paths must not contain reparse points: $current" }
        }
        $parent = [IO.Directory]::GetParent($current)
        if ($null -eq $parent) { break }
        $current = $parent.FullName
    }
}
Assert-NormalPath $destination
if ((Test-Path -LiteralPath $destination) -and -not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Test-tool output must be a directory.' }
$version = '8.0'
$asset = 'ffmpeg-8.0-essentials_build.zip'
$sourceUrl = 'https://github.com/GyanD/codexffmpeg/releases/download/8.0/ffmpeg-8.0-essentials_build.zip'
$expectedSha = '647e467caf82b9fa200a562769b5ff4d736aaf725804ed2c64ea9752106fa569'
$manifestUrl = 'https://raw.githubusercontent.com/microsoft/winget-pkgs/173ea9c7f69b1ae1175be787d635cec8674d120c/manifests/g/Gyan/FFmpeg/Essentials/8.0/Gyan.FFmpeg.Essentials.installer.yaml'
$archivePath = Join-Path $destination $asset
$receiptPath = Join-Path $destination 'test-tool-provenance.json'
$allowed = @($asset, 'ffmpeg.exe', 'ffprobe.exe', 'test-tool-provenance.json')
if (Test-Path -LiteralPath $destination) {
    foreach ($item in @(Get-ChildItem -LiteralPath $destination -Force)) {
        Assert-NormalPath $item.FullName
        if ($item.PSIsContainer -or $allowed -notcontains $item.Name) { throw "Unexpected existing test-tool file: $($item.FullName). Choose an empty directory; nothing is overwritten." }
    }
}
function Assert-ArchiveHash([string]$Path) {
    Assert-NormalPath $Path
    if ((Get-Item -LiteralPath $Path -Force).Length -gt 250MB) { throw 'Test-tool archive exceeds the expected size limit.' }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedSha) { throw "Archive SHA-256 mismatch: $Path. Cache preserved; no executable is trusted." }
}
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    if ($Offline) { throw "Offline cache archive is missing: $archivePath" }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Assert-NormalPath $destination
    $partial = Join-Path $destination ('.' + $asset + '.' + [Guid]::NewGuid().ToString('N') + '.part')
    try {
        Write-Host "Fetching pinned FFmpeg $version test tools from $sourceUrl"
        $previousProgress = $ProgressPreference
        try {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -UseBasicParsing -Uri $sourceUrl -OutFile $partial -TimeoutSec 600
        }
        finally { $ProgressPreference = $previousProgress }
        Assert-ArchiveHash $partial
        # Move fails if another process created the cache; an existing cache is never replaced.
        [IO.File]::Move($partial, $archivePath)
    }
    finally {
        if (Test-Path -LiteralPath $partial -PathType Leaf) { Remove-Item -LiteralPath $partial -Force }
    }
}
Assert-ArchiveHash $archivePath
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-EntryHash($Entry) {
    $stream = $Entry.Open()
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $hash.Dispose(); $stream.Dispose() }
}
function Assert-X64Executable([string]$Path) {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($Path))
    try {
        if ($reader.BaseStream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { throw "Invalid Windows test executable: $Path" }
        $reader.BaseStream.Position = 0x3c
        $pe = $reader.ReadInt32()
        if ($pe -lt 64 -or $pe + 6 -gt $reader.BaseStream.Length) { throw "Invalid PE header in $Path" }
        $reader.BaseStream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Expected Windows x64 test executable: $Path" }
    }
    finally { $reader.Dispose() }
}
$records = @()
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
        $entryName = 'ffmpeg-8.0-essentials_build/bin/' + $name
        $entryMatches = @($zip.Entries | Where-Object { $_.FullName -ieq $entryName })
        if ($entryMatches.Count -ne 1 -or $entryMatches[0].FullName -cne $entryName -or $entryMatches[0].Length -le 0 -or $entryMatches[0].Length -gt 150MB) {
            throw "Pinned test archive must contain exactly one non-empty expected asset: $entryName"
        }
        $entry = $entryMatches[0]
        $expectedBinaryHash = Get-EntryHash $entry
        $target = Join-Path $destination $name
        Assert-NormalPath $target
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedBinaryHash) { throw "Cached test binary SHA-256 mismatch: $target. File preserved; no overwrite." }
        }
        else {
            $inputStream = $entry.Open()
            $outputStream = $null
            try {
                $outputStream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                $inputStream.CopyTo($outputStream)
            }
            finally { if ($null -ne $outputStream) { $outputStream.Dispose() }; $inputStream.Dispose() }
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedBinaryHash) { throw "Extracted test binary SHA-256 mismatch: $target" }
        }
        Assert-X64Executable $target
        $records += [ordered]@{ name=$name; archiveEntry=$entryName; sha256=$expectedBinaryHash }
    }
}
finally { $zip.Dispose() }
$receipt = [ordered]@{ schema=1; purpose='Windows CI test fixtures only; never production payload'; version=$version; sourceUrl=$sourceUrl; archiveSha256=$expectedSha; manifestUrl=$manifestUrl; binaries=$records }
if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
    $existing = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($existing.schema -ne 1 -or $existing.version -cne $version -or $existing.sourceUrl -cne $sourceUrl -or
        $existing.archiveSha256 -cne $expectedSha -or $existing.manifestUrl -cne $manifestUrl -or @($existing.binaries).Count -ne 2) { throw 'Cached test-tool provenance does not match the pinned release. File preserved.' }
    foreach ($record in $records) {
        $found = @($existing.binaries | Where-Object { $_.name -ceq $record.name })
        if ($found.Count -ne 1 -or $found[0].sha256 -cne $record.sha256 -or $found[0].archiveEntry -cne $record.archiveEntry) { throw 'Cached test-tool binary provenance does not match the verified archive. File preserved.' }
    }
}
else {
    $text = ($receipt | ConvertTo-Json -Depth 5) + [Environment]::NewLine
    $stream = [IO.File]::Open($receiptPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text); $stream.Write($bytes,0,$bytes.Length) }
    finally { $stream.Dispose() }
}
[pscustomobject]@{
    FFmpegPath = Join-Path $destination 'ffmpeg.exe'
    FFprobePath = Join-Path $destination 'ffprobe.exe'
    ArchivePath = $archivePath
    ReceiptPath = $receiptPath
    Version = $version
}

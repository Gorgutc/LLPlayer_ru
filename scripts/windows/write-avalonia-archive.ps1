param(
    [Parameter(Mandatory = $true)][string]$PackageDir,
    [Parameter(Mandatory = $true)][string]$ArchivePath
)
# Creates a ZIP and reads every entry back against the source SHA-256 inventory.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
& (Join-Path $PSScriptRoot 'validate-avalonia-package.ps1') -PackageDir $PackageDir
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [System.IO.Path]::GetFullPath($PackageDir).TrimEnd('\','/')
$archive = [System.IO.Path]::GetFullPath($ArchivePath)
$prefix = [System.IO.Path]::GetFileNameWithoutExtension($archive)
if ($prefix -notmatch '^LLPlayer-Avalonia-[0-9A-Za-z][0-9A-Za-z._+-]{0,100}-win-x64$' -or $prefix.Contains('..')) { throw 'ZIP name must be LLPlayer-Avalonia-<version>-win-x64.zip.' }
if ([System.IO.Path]::GetExtension($archive) -cne '.zip') { throw 'Archive extension must be .zip.' }
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
$parent = [System.IO.Path]::GetDirectoryName($archive)
$ancestor = $parent
while ($ancestor) {
    if (Test-Path -LiteralPath $ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Archive parent must not be a reparse point: $ancestor" }
    }
    $parentInfo = [System.IO.Directory]::GetParent($ancestor)
    if ($null -eq $parentInfo) { break }
    $ancestor = $parentInfo.FullName
}
[System.IO.Directory]::CreateDirectory($parent) | Out-Null
$inventory = @{}
$files = @(Get-ChildItem -LiteralPath $root -File -Recurse -Force | Sort-Object FullName)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
    $inventory[$prefix + '/' + $relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$stream = [System.IO.File]::Open($archive, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite)
$zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
        $entry = $zip.CreateEntry($prefix + '/' + $relative, [System.IO.Compression.CompressionLevel]::Optimal)
        $input = [System.IO.File]::OpenRead($file.FullName)
        $output = $entry.Open()
        try { $input.CopyTo($output) }
        finally { $output.Dispose(); $input.Dispose() }
    }
}
finally { $zip.Dispose() }
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
$seen = @{}
try {
    if ($zip.Entries.Count -ne $inventory.Count) { throw 'ZIP entry inventory does not match the validated package.' }
    foreach ($entry in $zip.Entries) {
        if (-not $inventory.ContainsKey($entry.FullName) -or $seen.ContainsKey($entry.FullName)) { throw "ZIP has an unexpected or duplicate member $($entry.FullName)." }
        $seen[$entry.FullName] = $true
        $input = $entry.Open()
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = ([System.BitConverter]::ToString($sha.ComputeHash($input))).Replace('-','').ToLowerInvariant() }
        finally { $sha.Dispose(); $input.Dispose() }
        if ($actual -cne $inventory[$entry.FullName]) { throw "ZIP integrity readback failed for $($entry.FullName)." }
    }
}
finally { $zip.Dispose() }
Write-Host "ZIP integrity readback passed: $($inventory.Count) files."
Write-Output $archive

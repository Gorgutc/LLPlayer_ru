$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $failures = New-Object System.Collections.Generic.List[string]

    function Require-Text($Path, $Pattern, $Message) {
        if (-not (Test-Path $Path)) {
            $failures.Add("Missing $Path.")
            return
        }
        $text = Get-Content $Path -Raw
        if ($text -notmatch $Pattern) {
            $failures.Add($Message)
        }
    }

    Require-Text ".\LLPlayer\LLPlayer.csproj" "<TargetFramework>net10\.0-windows10\.0\.18362\.0</TargetFramework>" "LLPlayer must target net10.0-windows10.0.18362.0."
    Require-Text ".\LLPlayer\LLPlayer.csproj" "<UseWPF>true</UseWPF>" "LLPlayer must remain a WPF app."
    Require-Text ".\LLPlayer\LLPlayer.csproj" "<OutputType>WinExe</OutputType>" "LLPlayer must remain a WinExe."
    Require-Text ".\Plugins\YoutubeDL\YoutubeDL.csproj" "<TargetFramework>net10\.0-windows10\.0\.18362\.0</TargetFramework>" "YoutubeDL plugin must target the LLPlayer framework."
    Require-Text ".\LLPlayer\Properties\PublishProfiles\FolderProfile.pubxml" "<PublishSingleFile>true</PublishSingleFile>" "LLPlayer publish profile must keep PublishSingleFile=true."
    Require-Text ".\.github\actions\build-package\action.yml" "Copy-Item \.\\FFmpeg" "Release package action must copy FFmpeg."
    Require-Text ".\.github\actions\build-package\action.yml" "yt-dlp\.exe" "Release package action must handle yt-dlp.exe."

    $forbidden = @(
        "package.json",
        "pnpm-lock.yaml",
        "playwright.config.ts",
        "playwright.config.mjs",
        "lighthouserc.cjs",
        ".htmlhintrc",
        "stylelint.config.mjs",
        "eslint.config.mjs",
        "dependency-cruiser.config.cjs",
        "knip.json"
    )
    foreach ($path in $forbidden) {
        if (Test-Path $path) {
            $failures.Add("Forbidden web tooling artifact found at $path. LLPlayer gates must be .NET/WPF-first.")
        }
    }

    $ffmpegDlls = @(Get-ChildItem ".\FFmpeg" -Filter "*.dll" -ErrorAction SilentlyContinue)
    if ($ffmpegDlls.Count -lt 5) {
        $failures.Add("Expected tracked FFmpeg DLLs under FFmpeg/.")
    }

    if (-not (Test-Path ".\LLPlayer\lib\7z.dll")) {
        $failures.Add("LLPlayer/lib/7z.dll must remain available for packaging.")
    }

    $llPlayerCsproj = Get-Content ".\LLPlayer\LLPlayer.csproj" -Raw
    $flyleafCsproj = Get-Content ".\FlyleafLib\FlyleafLib.csproj" -Raw
    $llPlayerBindingVersion = [regex]::Match($llPlayerCsproj, 'Flyleaf\.FFmpeg\.Bindings" Version="([^"]+)"')
    $flyleafBindingVersion = [regex]::Match($flyleafCsproj, 'Flyleaf\.FFmpeg\.Bindings" Version="([^"]+)"')
    if ($llPlayerBindingVersion.Success -and $flyleafBindingVersion.Success) {
        if ($llPlayerBindingVersion.Groups[1].Value -ne $flyleafBindingVersion.Groups[1].Value) {
            Write-Warning "Flyleaf.FFmpeg.Bindings versions differ between LLPlayer and FlyleafLib in the current baseline. Keep this visible in review."
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Error $failure
        }
        exit 1
    }

    Write-Host "Frozen LLPlayer decisions verification completed."
}
finally {
    Pop-Location
}

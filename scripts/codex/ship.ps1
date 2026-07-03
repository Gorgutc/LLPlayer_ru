param(
    [switch]$SkipVerify
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    function Invoke-Checked {
        param(
            [Parameter(Mandatory = $true)]
            [string]$FilePath,
            [Parameter(ValueFromRemainingArguments = $true)]
            [string[]]$Arguments
        )

        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
        }
    }

    if (-not $SkipVerify) {
        & ".\scripts\codex\verify.ps1"
    }

    $tempBase = if (Test-Path "C:\tmp") { "C:\tmp" } else { [System.IO.Path]::GetTempPath() }
    $publishRoot = Join-Path $tempBase ("llplayer-codex-ship-" + [System.Guid]::NewGuid().ToString("N"))
    $appPublish = Join-Path $publishRoot "publish"
    $pluginPublish = Join-Path $publishRoot "publish-YoutubeDL"

    New-Item -ItemType Directory -Path $appPublish -Force | Out-Null
    New-Item -ItemType Directory -Path $pluginPublish -Force | Out-Null

    Invoke-Checked dotnet "restore" ".\LLPlayer\LLPlayer.csproj" "/p:PublishReadyToRun=true" "-warnaserror"
    Invoke-Checked dotnet "msbuild" ".\LLPlayer\LLPlayer.csproj" "/t:Publish" "/p:PublishProfile=FolderProfile" "/p:PublishDir=$appPublish" "/warnaserror"

    if (-not (Test-Path (Join-Path $appPublish "LLPlayer.exe"))) {
        throw "Publish smoke did not produce LLPlayer.exe."
    }
    if (-not (Test-Path (Join-Path $appPublish "lib\7z.dll"))) {
        throw "Publish smoke is missing LLPlayer\lib\7z.dll."
    }
    foreach ($sidecarSource in @("dub_sidecar\server.py", "dub_sidecar\pyproject.toml", "dub_sidecar\uv.lock", "dub_sidecar\README.md")) {
        if (-not (Test-Path (Join-Path $appPublish $sidecarSource))) {
            throw "Publish smoke is missing committed dubbing sidecar source $sidecarSource."
        }
    }
    $forbiddenDubRuntimeDirs = @(Get-ChildItem $appPublish -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("DubEngine", "dubmodels") })
    if ($forbiddenDubRuntimeDirs.Count -gt 0) {
        throw "Publish smoke must not include dubbing runtime data folder(s): $($forbiddenDubRuntimeDirs.FullName -join ', ')."
    }
    $publishedDubOutputs = @(Get-ChildItem $appPublish -Filter "*.ru.dub.*" -Recurse -ErrorAction SilentlyContinue)
    if ($publishedDubOutputs.Count -gt 0) {
        throw "Publish smoke must not include rendered dub output(s): $($publishedDubOutputs.FullName -join ', ')."
    }
    $publishedVoiceAssignments = @(Get-ChildItem $appPublish -Filter "*.ru.voices.json" -Recurse -ErrorAction SilentlyContinue)
    if ($publishedVoiceAssignments.Count -gt 0) {
        throw "Publish smoke must not include per-line voice assignment file(s): $($publishedVoiceAssignments.FullName -join ', ')."
    }

    $pathsToRemove = @(
        "runtimes\noavx\linux-x64",
        "runtimes\noavx\win-x86",
        "runtimes\openvino\linux-x64",
        "runtimes\vulkan\linux-x64",
        "runtimes\win-arm64",
        "runtimes\win-x86",
        "x86"
    )

    $cleanupTargets = @($pathsToRemove | ForEach-Object { Join-Path $appPublish $_ })
    $missingCleanupTargets = @($cleanupTargets | Where-Object { -not (Test-Path $_) })
    if ($missingCleanupTargets.Count -gt 0) {
        throw "Publish cleanup target(s) missing: $($missingCleanupTargets -join ', '). Keep scripts/codex/ship.ps1 in sync with .github/actions/build-package/action.yml."
    }

    Remove-Item -LiteralPath $cleanupTargets -Recurse -Force

    foreach ($targetPath in $cleanupTargets) {
        if (Test-Path $targetPath) {
            throw "Publish cleanup failed for $targetPath."
        }
    }

    Copy-Item ".\FFmpeg" -Destination $appPublish -Recurse -Force
    foreach ($ffmpegDll in @(
        "avcodec-62.dll",
        "avdevice-62.dll",
        "avfilter-11.dll",
        "avformat-62.dll",
        "avutil-60.dll",
        "swresample-6.dll",
        "swscale-9.dll"
    )) {
        if (-not (Test-Path (Join-Path $appPublish "FFmpeg\$ffmpegDll"))) {
            throw "Publish smoke is missing copied FFmpeg DLL $ffmpegDll."
        }
    }

    Invoke-Checked dotnet "restore" ".\Plugins\YoutubeDL\YoutubeDL.csproj" "/p:PublishReadyToRun=true" "-warnaserror"
    Invoke-Checked dotnet "msbuild" ".\Plugins\YoutubeDL\YoutubeDL.csproj" "/t:Publish" "/p:PublishProfile=FolderProfile" "/p:PublishDir=$pluginPublish" "/warnaserror"

    $pluginOut = Join-Path $appPublish "Plugins\YoutubeDL"
    New-Item -ItemType Directory -Path $pluginOut -Force | Out-Null
    Copy-Item (Join-Path $pluginPublish "YoutubeDL.dll") -Destination $pluginOut -Force
    Copy-Item (Join-Path $pluginPublish "YoutubeDL.pdb") -Destination $pluginOut -Force

    if (-not (Test-Path (Join-Path $pluginOut "YoutubeDL.dll"))) {
        throw "Publish smoke is missing Plugins\YoutubeDL\YoutubeDL.dll."
    }
    if (-not (Test-Path (Join-Path $pluginOut "YoutubeDL.pdb"))) {
        throw "Publish smoke is missing Plugins\YoutubeDL\YoutubeDL.pdb."
    }

    $packageAction = Get-Content ".\.github\actions\build-package\action.yml" -Raw
    $releaseTailChecks = @{
        "yt-dlp input version" = "yt-dlp-version"
        "yt-dlp GitHub release URL" = "https://github.com/yt-dlp/yt-dlp/releases/download/`$ver/yt-dlp.exe"
        "yt-dlp download command" = "Invoke-WebRequest"
        "yt-dlp placeholder" = "yt-dlp.exe_here"
        "release required content check" = "Release package is missing required file"
        "release recursive dub runtime rejection" = "Get-ChildItem `$pub -Directory -Recurse"
        "release FFmpeg avcodec check" = "FFmpeg\avcodec-62.dll"
        "release FFmpeg avdevice check" = "FFmpeg\avdevice-62.dll"
        "release FFmpeg avfilter check" = "FFmpeg\avfilter-11.dll"
        "release FFmpeg avformat check" = "FFmpeg\avformat-62.dll"
        "release FFmpeg avutil check" = "FFmpeg\avutil-60.dll"
        "release FFmpeg swresample check" = "FFmpeg\swresample-6.dll"
        "release FFmpeg swscale check" = "FFmpeg\swscale-9.dll"
        "7-Zip executable" = "C:\Program Files\7-Zip\7z.exe"
        "7-Zip add command" = " a -t7z -mx=8 -mmt=4 "
    }
    foreach ($check in $releaseTailChecks.GetEnumerator()) {
        if (-not $packageAction.Contains($check.Value)) {
            throw "Release packaging action is missing expected $($check.Key) marker: $($check.Value)"
        }
    }

    $ytDlpPlaceholder = Join-Path $pluginOut "yt-dlp.exe_here"
    New-Item -Path $ytDlpPlaceholder -ItemType File -Force | Out-Null
    if (-not (Test-Path $ytDlpPlaceholder)) {
        throw "Release dry-run did not create Plugins\YoutubeDL\yt-dlp.exe_here placeholder."
    }
    if (Test-Path (Join-Path $pluginOut "yt-dlp.exe")) {
        throw "Ship dry-run must not download or carry a local yt-dlp.exe."
    }

    $sevenZipPath = "C:\Program Files\7-Zip\7z.exe"
    if (-not (Test-Path $sevenZipPath)) {
        Write-Warning "7-Zip is not installed at $sevenZipPath; archive command is verified from .github/actions/build-package/action.yml only."
    }

    Write-Host "LLPlayer ship smoke completed at $publishRoot."
    Write-Host "Release packaging source remains .github/actions/build-package/action.yml."
}
finally {
    Pop-Location
}

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

    function Assert-RegularPublishFile {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Root,

            [Parameter(Mandatory = $true)]
            [string]$RelativePath,

            [switch]$AllowEmpty
        )

        $path = Join-Path $Root $RelativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Publish smoke is missing required file $RelativePath."
        }

        $file = Get-Item -LiteralPath $path
        if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Publish smoke required file must not be a reparse point: $RelativePath."
        }
        if (-not $AllowEmpty -and $file.Length -le 0) {
            throw "Publish smoke required file must not be empty: $RelativePath."
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

    foreach ($requiredFile in @(
        "LLPlayer.exe"
        "lib\7z.dll"
        "lib\license.7z.txt"
        "Assets\silero_vad.onnx"
        "onnxruntime.dll"
        "onnxruntime_providers_shared.dll"
        "e_sqlite3.dll"
        "x64\leptonica-1.85.0.dll"
        "x64\tesseract55.dll"
        "runtimes\win-x64\ggml-base-whisper.dll"
        "runtimes\win-x64\ggml-cpu-whisper.dll"
        "runtimes\win-x64\ggml-whisper.dll"
        "runtimes\win-x64\whisper.dll"
        "runtimes\noavx\win-x64\ggml-base-whisper.dll"
        "runtimes\noavx\win-x64\ggml-cpu-whisper.dll"
        "runtimes\noavx\win-x64\ggml-whisper.dll"
        "runtimes\noavx\win-x64\whisper.dll"
        "runtimes\openvino\win-x64\ggml-base-whisper.dll"
        "runtimes\openvino\win-x64\ggml-cpu-whisper.dll"
        "runtimes\openvino\win-x64\ggml-whisper.dll"
        "runtimes\openvino\win-x64\whisper.dll"
        "runtimes\vulkan\win-x64\ggml-base-whisper.dll"
        "runtimes\vulkan\win-x64\ggml-cpu-whisper.dll"
        "runtimes\vulkan\win-x64\ggml-vulkan-whisper.dll"
        "runtimes\vulkan\win-x64\ggml-whisper.dll"
        "runtimes\vulkan\win-x64\whisper.dll"
        "runtimes\cuda\win-x64\ggml-base-whisper.dll"
        "runtimes\cuda\win-x64\ggml-cpu-whisper.dll"
        "runtimes\cuda\win-x64\ggml-cuda-whisper.dll"
        "runtimes\cuda\win-x64\ggml-whisper.dll"
        "runtimes\cuda\win-x64\whisper.dll"
        "dub_sidecar\server.py"
        "dub_sidecar\pyproject.toml"
        "dub_sidecar\uv.lock"
        "dub_sidecar\README.md"
    )) {
        Assert-RegularPublishFile -Root $appPublish -RelativePath $requiredFile
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
        Assert-RegularPublishFile -Root $appPublish -RelativePath "FFmpeg\$ffmpegDll"
    }

    Invoke-Checked dotnet "restore" ".\Plugins\YoutubeDL\YoutubeDL.csproj" "/p:PublishReadyToRun=true" "-warnaserror"
    Invoke-Checked dotnet "msbuild" ".\Plugins\YoutubeDL\YoutubeDL.csproj" "/t:Publish" "/p:PublishProfile=FolderProfile" "/p:PublishDir=$pluginPublish" "/warnaserror"

    $pluginOut = Join-Path $appPublish "Plugins\YoutubeDL"
    New-Item -ItemType Directory -Path $pluginOut -Force | Out-Null
    Copy-Item (Join-Path $pluginPublish "YoutubeDL.dll") -Destination $pluginOut -Force
    Copy-Item (Join-Path $pluginPublish "YoutubeDL.pdb") -Destination $pluginOut -Force

    Assert-RegularPublishFile -Root $appPublish -RelativePath "Plugins\YoutubeDL\YoutubeDL.dll"
    Assert-RegularPublishFile -Root $appPublish -RelativePath "Plugins\YoutubeDL\YoutubeDL.pdb"

    $packageAction = Get-Content ".\.github\actions\build-package\action.yml" -Raw
    $releaseTailChecks = @{
        "yt-dlp input version" = "yt-dlp-version"
        "yt-dlp GitHub release URL" = "https://github.com/yt-dlp/yt-dlp/releases/download/`$ver/yt-dlp.exe"
        "yt-dlp download command" = "Invoke-WebRequest"
        "yt-dlp placeholder" = "yt-dlp.exe_here"
        "release required content check" = "Release package is missing required file"
        "release recursive dub runtime rejection" = "Get-ChildItem `$pub -Directory -Recurse"
        "release Silero VAD model check" = "Assets\silero_vad.onnx"
        "release ONNX Runtime native check" = "onnxruntime.dll"
        "release ONNX provider native check" = "onnxruntime_providers_shared.dll"
        "release SQLite native check" = "e_sqlite3.dll"
        "release 7-Zip license check" = "lib\license.7z.txt"
        "release Tesseract native check" = "x64\tesseract55.dll"
        "release Whisper CUDA native check" = "runtimes\cuda\win-x64\ggml-cuda-whisper.dll"
        "release FFmpeg avcodec check" = "FFmpeg\avcodec-62.dll"
        "release FFmpeg avdevice check" = "FFmpeg\avdevice-62.dll"
        "release FFmpeg avfilter check" = "FFmpeg\avfilter-11.dll"
        "release FFmpeg avformat check" = "FFmpeg\avformat-62.dll"
        "release FFmpeg avutil check" = "FFmpeg\avutil-60.dll"
        "release FFmpeg swresample check" = "FFmpeg\swresample-6.dll"
        "release FFmpeg swscale check" = "FFmpeg\swscale-9.dll"
        "7-Zip executable" = "C:\Program Files\7-Zip\7z.exe"
        "7-Zip add command" = " a -t7z -mx=8 -mmt=4 "
        "7-Zip integrity test" = '& "$sevenZip" t "$archivePath"'
        "yt-dlp SHA-256 output" = "yt-dlp-sha256"
        "archive SHA-256 output" = "archive-sha256"
    }
    foreach ($check in $releaseTailChecks.GetEnumerator()) {
        if (-not $packageAction.Contains($check.Value)) {
            throw "Release packaging action is missing expected $($check.Key) marker: $($check.Value)"
        }
    }

    $ytDlpPlaceholder = Join-Path $pluginOut "yt-dlp.exe_here"
    New-Item -Path $ytDlpPlaceholder -ItemType File -Force | Out-Null
    Assert-RegularPublishFile -Root $appPublish -RelativePath "Plugins\YoutubeDL\yt-dlp.exe_here" -AllowEmpty
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

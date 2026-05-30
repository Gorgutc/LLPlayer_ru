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

    Invoke-Checked dotnet "restore" ".\LLPlayer\LLPlayer.csproj" "/p:PublishReadyToRun=true"
    Invoke-Checked dotnet "msbuild" ".\LLPlayer\LLPlayer.csproj" "/t:Publish" "/p:PublishProfile=FolderProfile" "/p:PublishDir=$appPublish"

    if (-not (Test-Path (Join-Path $appPublish "LLPlayer.exe"))) {
        throw "Publish smoke did not produce LLPlayer.exe."
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

    foreach ($relativePath in $pathsToRemove) {
        $targetPath = Join-Path $appPublish $relativePath
        if (Test-Path $targetPath) {
            Remove-Item -Recurse -Force $targetPath
        }
        if (Test-Path $targetPath) {
            throw "Publish cleanup failed for $relativePath."
        }
    }

    Copy-Item ".\FFmpeg" -Destination $appPublish -Recurse -Force
    if (-not (Test-Path (Join-Path $appPublish "FFmpeg\avcodec-62.dll"))) {
        throw "Publish smoke is missing copied FFmpeg DLLs."
    }

    Invoke-Checked dotnet "restore" ".\Plugins\YoutubeDL\YoutubeDL.csproj" "/p:PublishReadyToRun=true"
    Invoke-Checked dotnet "msbuild" ".\Plugins\YoutubeDL\YoutubeDL.csproj" "/t:Publish" "/p:PublishProfile=FolderProfile" "/p:PublishDir=$pluginPublish"

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

    Write-Host "LLPlayer ship smoke completed at $publishRoot."
    Write-Host "Release packaging source remains .github/actions/build-package/action.yml."
}
finally {
    Pop-Location
}

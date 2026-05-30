$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    $failures = New-Object System.Collections.Generic.List[string]

    $isWindowsOs = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
    if (-not $isWindowsOs) {
        $failures.Add("LLPlayer requires Windows for WPF/WindowsDesktop verification.")
    }

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        $failures.Add("git is not available on PATH.")
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        $failures.Add("dotnet CLI is not available on PATH.")
    } else {
        $sdks = dotnet --list-sdks
        if (-not ($sdks -match '^10\.')) {
            Write-Warning ".NET SDK 10.0.x was not found. Local builds may use another SDK; CI/release should use 10.0.x."
        }
    }

    $ffmpegDlls = @(Get-ChildItem -Path ".\FFmpeg" -Filter "*.dll" -ErrorAction SilentlyContinue)
    if ($ffmpegDlls.Count -eq 0) {
        $failures.Add("FFmpeg DLLs are missing from .\FFmpeg.")
    }

    if (-not (Test-Path ".\LLPlayer\lib\7z.dll")) {
        $failures.Add("LLPlayer/lib/7z.dll is missing.")
    }

    if (-not (Test-Path ".\.github\actions\build-package\action.yml")) {
        $failures.Add("Release packaging source .github/actions/build-package/action.yml is missing.")
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Error $failure
        }
        exit 1
    }

    Write-Host "Environment check completed."
}
finally {
    Pop-Location
}

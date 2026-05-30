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

    function Require-PackageVersion($ProjectPath, $PackageName, $Version) {
        $pattern = '<PackageReference\s+Include="' + [regex]::Escape($PackageName) + '"\s+Version="' + [regex]::Escape($Version) + '"'
        Require-Text $ProjectPath $pattern "$ProjectPath must keep $PackageName version $Version."
    }

    function Require-TrackedPath($Path) {
        if (-not (Test-Path $Path)) {
            $failures.Add("Required tracked asset is missing: $Path.")
            return
        }

        $tracked = git ls-files -- $Path
        if (-not $tracked) {
            $failures.Add("Required asset must be tracked by git: $Path.")
        }
    }

    Require-Text ".\LLPlayer\LLPlayer.csproj" "<TargetFramework>net10\.0-windows10\.0\.18362\.0</TargetFramework>" "LLPlayer must target net10.0-windows10.0.18362.0."
    Require-Text ".\LLPlayer\LLPlayer.csproj" "<UseWPF>true</UseWPF>" "LLPlayer must remain a WPF app."
    Require-Text ".\LLPlayer\LLPlayer.csproj" "<OutputType>WinExe</OutputType>" "LLPlayer must remain a WinExe."
    Require-Text ".\Plugins\YoutubeDL\YoutubeDL.csproj" "<TargetFramework>net10\.0-windows10\.0\.18362\.0</TargetFramework>" "YoutubeDL plugin must target the LLPlayer framework."
    Require-Text ".\LLPlayer\Properties\PublishProfiles\FolderProfile.pubxml" "<RuntimeIdentifier>win-x64</RuntimeIdentifier>" "LLPlayer publish profile must target win-x64."
    Require-Text ".\LLPlayer\Properties\PublishProfiles\FolderProfile.pubxml" "<SelfContained>false</SelfContained>" "LLPlayer publish profile must remain framework-dependent."
    Require-Text ".\LLPlayer\Properties\PublishProfiles\FolderProfile.pubxml" "<PublishSingleFile>true</PublishSingleFile>" "LLPlayer publish profile must keep PublishSingleFile=true."
    Require-Text ".\LLPlayer\Properties\PublishProfiles\FolderProfile.pubxml" "<PublishReadyToRun>true</PublishReadyToRun>" "LLPlayer publish profile must keep PublishReadyToRun=true."
    Require-Text ".\Plugins\YoutubeDL\Properties\PublishProfiles\FolderProfile.pubxml" "<RuntimeIdentifier>win-x64</RuntimeIdentifier>" "YoutubeDL publish profile must target win-x64."
    Require-Text ".\Plugins\YoutubeDL\Properties\PublishProfiles\FolderProfile.pubxml" "<SelfContained>false</SelfContained>" "YoutubeDL publish profile must remain framework-dependent."
    Require-Text ".\Plugins\YoutubeDL\Properties\PublishProfiles\FolderProfile.pubxml" "<PublishSingleFile>false</PublishSingleFile>" "YoutubeDL publish profile must keep PublishSingleFile=false."
    Require-Text ".\Plugins\YoutubeDL\Properties\PublishProfiles\FolderProfile.pubxml" "<PublishReadyToRun>true</PublishReadyToRun>" "YoutubeDL publish profile must keep PublishReadyToRun=true."
    Require-Text ".\.github\actions\build-package\action.yml" "Copy-Item \.\\FFmpeg" "Release package action must copy FFmpeg."
    Require-Text ".\.github\actions\build-package\action.yml" "Remove-Item -Recurse \`$pathsToRemove" "Release package action must keep strict runtime cleanup."
    Require-Text ".\.github\actions\build-package\action.yml" 'Remove-Item -Recurse "\$pub\\x86"' "Release package action must keep Tesseract x86 cleanup."
    Require-Text ".\.github\actions\build-package\action.yml" "yt-dlp\.exe" "Release package action must handle yt-dlp.exe."
    Require-Text ".\.github\actions\build-package\action.yml" "Invoke-WebRequest .*yt-dlp\.exe" "Release package action must download yt-dlp.exe."
    Require-Text ".\.github\actions\build-package\action.yml" "C:\\Program Files\\7-Zip\\7z\.exe" "Release package action must archive with 7-Zip."
    Require-Text ".\scripts\codex\ship.ps1" "Publish cleanup target\(s\) missing" "Ship smoke must fail if release cleanup targets drift."
    Require-Text ".\scripts\codex\ship.ps1" "Release dry-run" "Ship smoke must dry-run release-only packaging tail."
    Require-Text ".\scripts\codex\ship.ps1" "yt-dlp\.exe_here" "Ship smoke must create yt-dlp placeholder."
    Require-Text ".\scripts\codex\ship.ps1" "7-Zip is not installed" "Ship smoke must document local 7-Zip dry-run fallback."
    Require-Text ".\docs\agent\frozen-decisions.md" "product-behavior-contract\.md" "Frozen decisions must link product behavior contract."
    Require-Text ".\docs\agent\frozen-decisions.md" "wpf-design-contract\.md" "Frozen decisions must link WPF design contract."
    Require-Text ".\docs\agent\frozen-decisions.md" "media-runtime-contract\.md" "Frozen decisions must link media runtime contract."
    Require-Text ".\docs\agent\frozen-decisions.md" "config-data-contract\.md" "Frozen decisions must link config/data contract."
    Require-Text ".\docs\agent\frozen-decisions.md" "dependency-baseline\.md" "Frozen decisions must link dependency baseline."
    Require-Text ".\docs\agent\product-behavior-contract.md" "dual subtitle" "Product contract must preserve dual subtitles."
    Require-Text ".\docs\agent\wpf-design-contract.md" "media-first" "WPF contract must preserve media-first layout."
    Require-Text ".\docs\agent\wpf-design-contract.md" "CustomColorTheme" "WPF contract must preserve App.xaml resource dictionary baseline."
    Require-Text ".\docs\agent\wpf-design-contract.md" "Settings Keys is an editable DataGrid workflow" "WPF contract must preserve Settings Keys workflow."
    Require-Text ".\docs\agent\wpf-design-contract.md" "Text subtitle interaction is part of the learning workflow" "WPF contract must preserve subtitle word/mouse workflow."
    Require-Text ".\docs\agent\wpf-design-contract.md" "ShowSingleton" "WPF contract must preserve dialog singleton behavior."
    Require-Text ".\docs\agent\media-runtime-contract.md" "PacketQueue" "Media runtime contract must preserve native queue ownership guidance."
    Require-Text ".\docs\agent\media-runtime-contract.md" "Leading-colon paths" "Media runtime contract must preserve colon path resolution."
    Require-Text ".\docs\agent\media-runtime-contract.md" "WPF Dispatcher Boundaries" "Media runtime contract must preserve dispatcher boundaries."
    Require-Text ".\docs\agent\media-runtime-contract.md" "IScrapeItem" "Media runtime contract must preserve scrape item plugin hook."
    Require-Text ".\docs\agent\config-data-contract.md" "LLPlayer\.PlayerConfig\.json" "Config contract must mention runtime player config."
    Require-Text ".\docs\agent\config-data-contract.md" "Settings Keys edits the live key-binding list" "Config contract must preserve Settings Keys behavior."
    Require-Text ".\docs\agent\dependency-baseline.md" "net10\.0-windows10\.0\.18362\.0" "Dependency baseline must preserve target framework."
    Require-Text ".\docs\agent\dependency-baseline.md" "Vortice\.Direct3D11.*3\.7\.6-beta" "Dependency baseline must freeze Vortice versions."
    Require-Text ".\docs\agent\dependency-baseline.md" "Whisper\.net\.Runtime\.Cuda\.Windows.*1\.9\.0" "Dependency baseline must freeze Whisper runtime versions."
    Require-Text ".\docs\agent\dependency-baseline.md" "Microsoft Visual C\+\+ Redistributable 2022" "Dependency baseline must document VC++ Redistributable prerequisite."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "Save & Close" "Manual smoke matrix must cover settings persistence."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "Left-click a subtitle word" "Manual smoke matrix must cover subtitle word lookup."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "Open CheatSheet with F1" "Manual smoke matrix must cover CheatSheet workflow."
    Require-Text ".\docs\agent\subagent-review-matrix.md" "verification_reviewer" "Subagent review matrix must require verification review."
    Require-Text ".\.codex\config.toml" "LLPlayer_ru" ".codex/config.toml must describe LLPlayer_ru."

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

    $configText = Get-Content ".\.codex\config.toml" -Raw
    foreach ($forbiddenText in @("PL_RU", "Blueprints_lib", "Osiris_ref", "package_manager")) {
        if ($configText -match [regex]::Escape($forbiddenText)) {
            $failures.Add(".codex/config.toml contains stale non-LLPlayer token '$forbiddenText'.")
        }
    }

    $gitignore = Get-Content ".\.gitignore" -Raw
    foreach ($pattern in @(
        "LLPlayer.Config.json",
        "LLPlayer.Engine.json",
        "LLPlayer.PlayerConfig.json",
        "crash.log",
        "Recordings/",
        "Snapshots/",
        "whispermodels/",
        "Whisper/",
        "tesseractmodels/",
        ".env*"
    )) {
        if ($gitignore -notmatch [regex]::Escape($pattern)) {
            $failures.Add(".gitignore must ignore LLPlayer runtime/user data pattern $pattern.")
        }
    }

    $expectedTrackedAssets = @(
        "FFmpeg/avcodec-62.dll",
        "FFmpeg/avdevice-62.dll",
        "FFmpeg/avfilter-11.dll",
        "FFmpeg/avformat-62.dll",
        "FFmpeg/avutil-60.dll",
        "FFmpeg/swresample-6.dll",
        "FFmpeg/swscale-9.dll",
        "LLPlayer/lib/7z.dll",
        "LLPlayer/lib/license.7z.txt",
        "Plugins/YoutubeDL/Libs/yt-dlp.exe_here"
    )
    foreach ($asset in $expectedTrackedAssets) {
        Require-TrackedPath $asset
    }

    $actualFfmpegDlls = @(Get-ChildItem ".\FFmpeg" -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Sort-Object)
    $expectedFfmpegDlls = @(
        "avcodec-62.dll",
        "avdevice-62.dll",
        "avfilter-11.dll",
        "avformat-62.dll",
        "avutil-60.dll",
        "swresample-6.dll",
        "swscale-9.dll"
    )
    $extraFfmpegDlls = @($actualFfmpegDlls | Where-Object { $_ -notin $expectedFfmpegDlls })
    $missingFfmpegDlls = @($expectedFfmpegDlls | Where-Object { $_ -notin $actualFfmpegDlls })
    if ($extraFfmpegDlls.Count -gt 0) {
        $failures.Add("Unexpected FFmpeg DLL(s) found: $($extraFfmpegDlls -join ', ').")
    }
    if ($missingFfmpegDlls.Count -gt 0) {
        $failures.Add("Expected FFmpeg DLL(s) missing: $($missingFfmpegDlls -join ', ').")
    }

    $forbiddenTracked = @(git ls-files -- `
        ":(glob)**/yt-dlp.exe" `
        ":(glob)**/LLPlayer.Config.json" `
        ":(glob)**/LLPlayer.Engine.json" `
        ":(glob)**/LLPlayer.PlayerConfig.json" `
        ":(glob)**/crash.log" `
        ":(glob)**/.env*" `
        ":(glob)**/*.dmp" `
        ":(glob)**/*.dump" `
        ":(glob)**/Recordings/**" `
        ":(glob)**/Snapshots/**" `
        ":(glob)**/whispermodels/**" `
        ":(glob)**/Whisper/**" `
        ":(glob)**/tesseractmodels/**" `
        ":(glob)**/bin/**" `
        ":(glob)**/obj/**" `
        ":(glob)**/publish/**")
    if ($forbiddenTracked.Count -gt 0) {
        $failures.Add("Forbidden runtime/user artifact(s) are tracked: $($forbiddenTracked -join ', ').")
    }

    if (-not (Test-Path ".\Plugins\YoutubeDL\Properties\PublishProfiles\FolderProfile.pubxml")) {
        $failures.Add("YoutubeDL publish profile must remain available for packaging.")
    }
    if (-not (Test-Path ".\WpfColorFontDialog\WpfColorFontDialog.csproj")) {
        $failures.Add("WpfColorFontDialog project must remain available.")
    }
    if (-not (Test-Path ".\FlyleafLibTests\FlyleafLibTests.csproj")) {
        $failures.Add("FlyleafLibTests project must remain available.")
    }

    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "CliWrap" "3.10.1"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "DeepL.net" "1.21.0"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Flyleaf.FFmpeg.Bindings" "7.1.1"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "SearchPioneer.Lingua" "1.0.5"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "TesseractOCR" "5.5.2"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "UTF.Unknown" "2.6.0"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Vortice.D3DCompiler" "3.7.6-beta"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Vortice.Direct3D11" "3.7.6-beta"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Vortice.DirectComposition" "3.7.6-beta"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Vortice.Mathematics" "1.9.3"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Vortice.MediaFoundation" "3.7.6-beta"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Vortice.XAudio2" "3.7.6-beta"
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Whisper.net" "1.9.0"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Flyleaf.FFmpeg.Bindings" "8.0.1"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "LibNMeCab" "0.10.2"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "LibNMeCab.IpaDicBin" "0.10.0"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "MaterialDesignThemes" "5.3.1"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Prism.DryIoc" "9.0.537"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Squid-Box.SevenZipSharp.Lite" "1.6.2.24"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Whisper.net.Runtime" "1.9.0"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Whisper.net.Runtime.Cuda.Windows" "1.9.0"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Whisper.net.Runtime.NoAvx" "1.9.0"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Whisper.net.Runtime.OpenVino" "1.9.0"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Whisper.net.Runtime.Vulkan" "1.9.0"
    Require-PackageVersion ".\FlyleafLibTests\FlyleafLibTests.csproj" "AwesomeAssertions" "9.4.0"
    Require-PackageVersion ".\FlyleafLibTests\FlyleafLibTests.csproj" "Microsoft.NET.Test.Sdk" "18.4.0"
    Require-PackageVersion ".\FlyleafLibTests\FlyleafLibTests.csproj" "xunit.v3" "3.2.2"
    Require-PackageVersion ".\FlyleafLibTests\FlyleafLibTests.csproj" "xunit.runner.visualstudio" "3.1.5"

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

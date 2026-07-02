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
    Require-Text ".\.github\actions\build-package\action.yml" "dotnet restore \.\\LLPlayer\\LLPlayer\.csproj /p:PublishReadyToRun=true -warnaserror" "Release package action app restore must treat NuGet audit warnings as errors."
    Require-Text ".\.github\actions\build-package\action.yml" "dotnet restore \.\\Plugins\\YoutubeDL\\YoutubeDL\.csproj /p:PublishReadyToRun=true -warnaserror" "Release package action YoutubeDL restore must treat NuGet audit warnings as errors."
    Require-Text ".\.github\actions\build-package\action.yml" '(?s)- name: Build App(?:(?!\r?\n\s*-\sname:).)*dotnet msbuild \.\\LLPlayer\\LLPlayer\.csproj(?:(?!\r?\n\s*-\sname:).)*/warnaserror' "Release package action app publish must treat compiler warnings as errors."
    Require-Text ".\.github\actions\build-package\action.yml" '(?s)- name: Build Plugins(?:(?!\r?\n\s*-\sname:).)*dotnet msbuild \.\\Plugins\\YoutubeDL\\YoutubeDL\.csproj(?:(?!\r?\n\s*-\sname:).)*/warnaserror' "Release package action YoutubeDL publish must treat compiler warnings as errors."
    Require-Text ".\.github\actions\build-package\action.yml" "DubEngine" "Release package action must reject dubbing runtime venv artifacts."
    Require-Text ".\.github\actions\build-package\action.yml" "dubmodels" "Release package action must reject downloaded dubbing model artifacts."
    Require-Text ".\.github\actions\build-package\action.yml" "\*\.ru\.dub\.\*" "Release package action must reject rendered dub output artifacts."
    Require-Text ".\.github\actions\build-package\action.yml" "\*\.ru\.voices\.json" "Release package action must reject per-line voice assignment companion files."
    Require-Text ".\.github\actions\build-package\action.yml" "Release package is missing required file" "Release package action must positively validate required publish contents."
    Require-Text ".\.github\actions\build-package\action.yml" "LLPlayer\.exe" "Release package action must verify LLPlayer.exe is present."
    foreach ($ffmpegDll in @(
        "avcodec-62\.dll",
        "avdevice-62\.dll",
        "avfilter-11\.dll",
        "avformat-62\.dll",
        "avutil-60\.dll",
        "swresample-6\.dll",
        "swscale-9\.dll"
    )) {
        Require-Text ".\.github\actions\build-package\action.yml" "FFmpeg\\$ffmpegDll" "Release package action must verify FFmpeg DLL $ffmpegDll is present."
        Require-Text ".\scripts\codex\ship.ps1" $ffmpegDll "Ship smoke must verify copied FFmpeg DLL $ffmpegDll is present."
    }
    Require-Text ".\.github\actions\build-package\action.yml" "Plugins\\YoutubeDL\\YoutubeDL\.dll" "Release package action must verify YoutubeDL.dll is present."
    Require-Text ".\.github\actions\build-package\action.yml" "dub_sidecar\\uv\.lock" "Release package action must verify committed dubbing lockfile is present."
    Require-Text ".\.github\actions\build-package\action.yml" "Get-ChildItem \`$pub -Directory -Recurse" "Release package action must recursively reject dubbing runtime/model directories."
    Require-Text ".\.github\workflows\build.yml" "dotnet restore -warnaserror" "Build workflow restore must treat NuGet audit warnings as errors."
    Require-Text ".\scripts\codex\verify.ps1" 'Invoke-Checked dotnet "restore" "-warnaserror"' "Full verification restore must treat NuGet audit warnings as errors."
    Require-Text ".\scripts\codex\ship.ps1" 'Invoke-Checked dotnet "restore" "\.\\LLPlayer\\LLPlayer\.csproj" "/p:PublishReadyToRun=true" "-warnaserror"' "Ship smoke app restore must treat NuGet audit warnings as errors."
    Require-Text ".\scripts\codex\ship.ps1" 'Invoke-Checked dotnet "restore" "\.\\Plugins\\YoutubeDL\\YoutubeDL\.csproj" "/p:PublishReadyToRun=true" "-warnaserror"' "Ship smoke YoutubeDL restore must treat NuGet audit warnings as errors."
    Require-Text ".\scripts\codex\ship.ps1" 'Invoke-Checked dotnet "msbuild" "\.\\LLPlayer\\LLPlayer\.csproj".*"/warnaserror"' "Ship smoke app publish must treat compiler warnings as errors."
    Require-Text ".\scripts\codex\ship.ps1" 'Invoke-Checked dotnet "msbuild" "\.\\Plugins\\YoutubeDL\\YoutubeDL\.csproj".*"/warnaserror"' "Ship smoke YoutubeDL publish must treat compiler warnings as errors."
    Require-Text ".\Plugins\llplayer-codex\skills\llplayer-dotnet-rules\SKILL.md" "dotnet restore -warnaserror" "LLPlayer .NET skill must document restore audit warnings as errors."
    Require-Text ".\Plugins\llplayer-codex\skills\llplayer-quality-tooling\SKILL.md" "dotnet restore -warnaserror" "LLPlayer quality tooling skill must document restore audit warnings as errors."
    Require-Text ".\docs\agent\quality-tooling.md" "dotnet restore -warnaserror" "Quality tooling docs must document restore audit warnings as errors."
    Require-Text ".\.codex\agents\dotnet_quality_guardian.toml" "dotnet restore -warnaserror" "dotnet_quality_guardian must require restore audit warnings as errors."
    Require-Text ".\scripts\codex\ship.ps1" "Publish cleanup target\(s\) missing" "Ship smoke must fail if release cleanup targets drift."
    Require-Text ".\scripts\codex\ship.ps1" "Release dry-run" "Ship smoke must dry-run release-only packaging tail."
    Require-Text ".\scripts\codex\ship.ps1" "yt-dlp\.exe_here" "Ship smoke must create yt-dlp placeholder."
    Require-Text ".\scripts\codex\ship.ps1" "LLPlayer\\lib\\7z\.dll" "Ship smoke must verify publish output contains LLPlayer/lib/7z.dll."
    Require-Text ".\scripts\codex\ship.ps1" "dub_sidecar\\server\.py" "Ship smoke must verify committed dubbing sidecar source is published."
    Require-Text ".\scripts\codex\ship.ps1" "dub_sidecar\\uv\.lock" "Ship smoke must verify committed dubbing lockfile is published."
    Require-Text ".\scripts\codex\ship.ps1" "DubEngine" "Ship smoke must verify dubbing runtime engine is not published."
    Require-Text ".\scripts\codex\ship.ps1" "\*\.ru\.dub\.\*" "Ship smoke must verify rendered dub outputs are not published."
    Require-Text ".\scripts\codex\ship.ps1" "\*\.ru\.voices\.json" "Ship smoke must verify per-line voice assignment companion files are not published."
    Require-Text ".\scripts\codex\ship.ps1" "Get-ChildItem \`$appPublish -Directory -Recurse" "Ship smoke must recursively reject dubbing runtime/model directories."
    Require-Text ".\scripts\codex\ship.ps1" "7-Zip is not installed" "Ship smoke must document local 7-Zip dry-run fallback."
    Require-Text ".\docs\agent\dependency-baseline.md" "positively validate required publish contents" "Dependency baseline must document release positive content validation."
    Require-Text ".\docs\agent\dependency-baseline.md" "recursively reject dubbing runtime/model/output artifacts" "Dependency baseline must document recursive dubbing artifact rejection."
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
    Require-Text ".\docs\agent\wpf-design-contract.md" "SubLanguageBadgeVisibilityConv" "WPF contract must preserve live language-badge visibility binding."
    Require-Text ".\LLPlayer\Converters\SubtitleConverters.cs" "SubLanguageBadgeConverter" "Subtitle language badge converter must remain wired."
    Require-Text ".\LLPlayer\Converters\SubtitleConverters.cs" "SubLanguageBadgeVisibilityConverter" "Subtitle language badge visibility converter must remain wired."
    Require-Text ".\LLPlayer\Views\SubtitlesSidebar.xaml" "ASRPerSegmentLanguage" "Subtitle sidebar language badge must bind to the live ASRPerSegmentLanguage gate."
    Require-Text ".\FlyleafLib\Utils\LanguageBadge.cs" "LanguageBadge" "Language badge formatting helper must remain in FlyleafLib Utils."
    Require-Text ".\docs\agent\wpf-design-contract.md" "BatchSubtitlesDialogVM.*snapshots the assigned rows" "WPF contract must document current-session per-line voice batch snapshot."
    Require-Text ".\LLPlayer\Controls\Settings\SettingsSubtitlesDubbing.xaml" "sidebar per-line overrides can change individual lines" "Dubbing settings text must not describe per-line voice as a future-only phase."
    Require-Text ".\LLPlayer\Views\SettingsDialog.xaml" "per-line line row sidebar voice override" "Settings search keywords must include per-line dubbing voice override terms."
    Require-Text ".\docs\agent\media-runtime-contract.md" "PacketQueue" "Media runtime contract must preserve native queue ownership guidance."
    Require-Text ".\docs\agent\media-runtime-contract.md" "Leading-colon paths" "Media runtime contract must preserve colon path resolution."
    Require-Text ".\docs\agent\media-runtime-contract.md" "WPF Dispatcher Boundaries" "Media runtime contract must preserve dispatcher boundaries."
    Require-Text ".\docs\agent\media-runtime-contract.md" "IScrapeItem" "Media runtime contract must preserve scrape item plugin hook."
    Require-Text ".\docs\agent\media-runtime-contract.md" "DubbingVoiceAssignmentMap" "Media runtime contract must document per-line voice assignment map in batch dubbing."
    Require-Text ".\FlyleafLib\MediaPlayer\Translation\TranslateChatConfig.cs" "TranslateMethod\s*\{[^\r\n}]*\}\s*=\s*ChatTranslateMethod\.ContextWindow\s*;" "TranslateChatConfig must keep ContextWindow as the default LLM chat method."
    Require-Text ".\FlyleafLib\MediaPlayer\Translation\TranslateChatConfig.cs" "GrammarCheckEnabled\s*\{[^\r\n}]*\}\s*=\s*true\s*;" "TranslateChatConfig must keep GrammarCheckEnabled default on."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "SubtitleMaxCharsPerLine\s*\{[^\r\n}]*\}\s*=\s*48\s*;" "SubtitleMaxCharsPerLine default must remain 48."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "SubtitleMaxLinesPerCue\s*\{[^\r\n}]*\}\s*=\s*3\s*;" "SubtitleMaxLinesPerCue default must remain 3."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "SubtitleMaxCjkCharsPerLine\s*\{[^\r\n}]*\}\s*=\s*24\s*;" "SubtitleMaxCjkCharsPerLine default must remain 24."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "SubtitleMaxCueDurationSec\s*\{[^\r\n}]*\}\s*=\s*7\.0\s*;" "SubtitleMaxCueDurationSec default must remain 7.0."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "FixAllCaps\s*\{[^\r\n}]*\}\s*=\s*true\s*;" "FixAllCaps (ALL-CAPS ASR normalization) default must remain on."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "ASRSplitOnSilence\s*\{[^\r\n}]*\}\s*=\s*true\s*;" "ASRSplitOnSilence (T-09 silence-preferred chunk cut) default must remain on."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "ASRSilenceSoftFraction\s*\{[^\r\n}]*\}\s*=\s*0\.6\s*;" "ASRSilenceSoftFraction default must remain 0.6."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "ASRSilenceRmsThreshold\s*\{[^\r\n}]*\}\s*=\s*0\.01\s*;" "ASRSilenceRmsThreshold default must remain 0.01."
    Require-Text ".\FlyleafLib\Engine\Config.cs" "ASRFoldBack\s*\{[^\r\n}]*\}\s*=\s*false\s*;" "ASRFoldBack (T-08 mid-video fold-back) default must remain off."
    Require-Text ".\FlyleafLib\Engine\Config.cs" '(?s)loadedVer\s*<=\s*System\.Version\.Parse\("0\.3\.5"\).*TranslateMethod\s*==\s*ChatTranslateMethod\.KeepContext.*TranslateMethod\s*=\s*ChatTranslateMethod\.ContextWindow' "Config.UpdateDefault must migrate old KeepContext default to ContextWindow."
    Require-Text ".\FlyleafLib\Engine\Config.cs" '(?s)loadedVer\s*<=\s*System\.Version\.Parse\("0\.3\.6"\).*SubtitleMaxLinesPerCue\s*==\s*2.*SubtitleMaxLinesPerCue\s*=\s*3.*SubtitleMaxCharsPerLine\s*==\s*42.*SubtitleMaxCharsPerLine\s*=\s*48.*SubtitleMaxCjkCharsPerLine\s*==\s*21.*SubtitleMaxCjkCharsPerLine\s*=\s*24.*SubtitleMaxCueDurationSec\s*==\s*6\.0.*SubtitleMaxCueDurationSec\s*=\s*7\.0' "Config.UpdateDefault must migrate old subtitle re-segmentation defaults to 0.3.7 values."
    Require-Text ".\FlyleafLib\MediaPlayer\Translation\Services\ITranslateSettings.cs" "TimeoutMs\s*=\s*180000\s*;" "Local LLM (Ollama/LM Studio/KoboldCpp) default request timeout must be 180000ms (reasoning-model headroom)."
    Require-Text ".\FlyleafLib\Engine\Config.cs" '(?s)loadedVer\s*<=\s*System\.Version\.Parse\("0\.3\.8"\).*MigrateLocalLlmTimeoutDefault\(Subtitles\.TranslateServiceSettings\).*TimeoutMs\s*==\s*60000.*TimeoutMs\s*=\s*180000' "Config.UpdateDefault must invoke MigrateLocalLlmTimeoutDefault under the 0.3.8 gate and migrate the old 60000 local LLM timeout default to 180000."
    Require-Text ".\docs\agent\config-data-contract.md" "LLPlayer\.PlayerConfig\.json" "Config contract must mention runtime player config."
    Require-Text ".\docs\agent\config-data-contract.md" "Settings Keys edits the live key-binding list" "Config contract must preserve Settings Keys behavior."
    Require-Text ".\docs\agent\config-data-contract.md" "DefaultVoiceId.*normalized on set" "Config contract must document dubbing default voice normalization."
    Require-Text ".\docs\agent\config-data-contract.md" "CustomVoiceIds.*non-null list" "Config contract must document custom voice id normalization."
    Require-Text ".\docs\agent\dependency-baseline.md" "net10\.0-windows10\.0\.18362\.0" "Dependency baseline must preserve target framework."
    Require-Text ".\docs\agent\dependency-baseline.md" "Vortice\.Direct3D11.*3\.7\.6-beta" "Dependency baseline must freeze Vortice versions."
    Require-Text ".\docs\agent\dependency-baseline.md" "Whisper\.net\.Runtime\.Cuda\.Windows.*1\.9\.0" "Dependency baseline must freeze Whisper runtime versions."
    Require-Text ".\docs\agent\dependency-baseline.md" "Microsoft Visual C\+\+ Redistributable 2022" "Dependency baseline must document VC++ Redistributable prerequisite."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "Save & Close" "Manual smoke matrix must cover settings persistence."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "Left-click a subtitle word" "Manual smoke matrix must cover subtitle word lookup."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "Open CheatSheet with F1" "Manual smoke matrix must cover CheatSheet workflow."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "current-session assignment also reaches the SRT-only render path" "Manual smoke matrix must cover per-line voice batch render from existing SRT."
    Require-Text ".\docs\agent\dubbing-contract.md" "IDubbingVoiceAssignmentProvider" "Dubbing contract must document per-line voice assignment provider."
    Require-Text ".\docs\agent\dubbing-contract.md" "current-session / in-memory only" "Dubbing contract must keep per-line voice persistence boundary explicit."
    Require-Text ".\docs\agent\dubbing-contract.md" "DubbingVoiceAssignmentMap" "Dubbing contract must document per-line voice assignment map."
    Require-Text ".\docs\agent\dubbing-contract.md" "DefaultVoiceId.*normalized on set" "Dubbing contract must document DefaultVoiceId normalization."
    Require-Text ".\docs\agent\dubbing-contract.md" "PersistPerLineVoices" "Dubbing contract must document opt-in per-line voice persistence."
    Require-Text ".\docs\agent\config-data-contract.md" "video\.ru\.voices\.json" "Config-data contract must document the per-line voice companion file."
    Require-Text ".\docs\agent\dubbing\dubbing-roadmap.md" "Phase 2a progress" "Dubbing roadmap must include per-line voice phase 2a progress."
    Require-Text ".\docs\agent\subagent-review-matrix.md" "verification_reviewer" "Subagent review matrix must require verification review."
    Require-Text ".\docs\agent\subagent-review-matrix.md" "LLPlayer/Converters/\*\*" "Subagent review matrix must route LLPlayer converters through WPF review."
    Require-Text ".\docs\agent\subagent-review-matrix.md" "FlyleafLib/Utils/\*\*" "Subagent review matrix must route FlyleafLib utilities through media/.NET review."
    Require-Text ".\docs\agent\subagent-review-matrix.md" "FlyleafLibTests/\*\*" "Subagent review matrix must route tests through .NET review."
    Require-Text ".\docs\agent\subagent-review-matrix.md" "\*\.csproj" "Subagent review matrix must route project files through .NET/package review."
    Require-Text ".\scripts\codex\audit-frozen.ps1" "LLPlayer/\(Views\|Controls\|ViewModels\|Converters\|Themes\|Resources\)" "Frozen audit must route LLPlayer converters/resources/themes through WPF review."
    Require-Text ".\scripts\codex\audit-frozen.ps1" "FlyleafLib/Utils/" "Frozen audit must route FlyleafLib utilities through media/.NET review."
    Require-Text ".\scripts\codex\audit-frozen.ps1" "FlyleafLibTests/" "Frozen audit must route tests through .NET review."
    Require-Text ".\scripts\codex\audit-frozen.ps1" "\.csproj" "Frozen audit must route project files through .NET/package review."
    Require-Text ".\.codex\config.toml" "LLPlayer_ru" ".codex/config.toml must describe LLPlayer_ru."
    Require-Text ".\LLPlayer\LLPlayer.csproj" "dub_sidecar\\uv\.lock" "LLPlayer publish items must include dub_sidecar/uv.lock."
    Require-Text ".\dub_sidecar\pyproject.toml" "pytorch-cu128" "Dubbing sidecar must pin torch to the CUDA 12.8 PyTorch index."
    Require-Text ".\dub_sidecar\uv.lock" 'name = "torch"\s+version = "2\.11\.0\+cu128"' "Dubbing lockfile must keep the reviewed torch 2.11.0+cu128 resolution."
    Require-Text ".\dub_sidecar\uv.lock" "https://download\.pytorch\.org/whl/cu128" "Dubbing lockfile must resolve torch from the CUDA 12.8 PyTorch index."
    Require-Text ".\docs\agent\dependency-baseline.md" "torch.*2\.11\.0\+cu128" "Dependency baseline must document the reviewed torch lockfile resolution."
    Require-Text ".\DO_NOT_PUSH.md" "\*\.ru\.voices\.json" "Do-not-push guidance must mention per-line voice assignment companion files."
    Require-Text ".\docs\agent\dependency-baseline.md" "\*\.ru\.voices\.json" "Dependency baseline must document voice companion files as runtime data."
    Require-Text ".\docs\agent\verification.md" "\*\.ru\.voices\.json" "Verification docs must document ship rejection of voice companion files."
    Require-Text ".\docs\agent\manual-smoke-matrix.md" "\*\.ru\.voices\.json" "Manual smoke matrix must document packaging rejection of voice companion files."

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
        "LLPlayer.WordList.json",
        "crash.log",
        "*.dmp",
        "*.dump",
        "Recordings/",
        "Snapshots/",
        "whispermodels/",
        "Whisper/",
        "tesseractmodels/",
        "DubEngine/",
        "dubmodels/",
        "*.ru.dub.*",
        "*.ru.voices.json",
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
        "Plugins/YoutubeDL/Libs/yt-dlp.exe_here",
        "dub_sidecar/uv.lock"
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
        ":(glob)**/LLPlayer.WordList.json" `
        ":(glob)**/crash.log" `
        ":(glob)**/.env*" `
        ":(glob)**/*.dmp" `
        ":(glob)**/*.dump" `
        ":(glob)**/Recordings/**" `
        ":(glob)**/Snapshots/**" `
        ":(glob)**/whispermodels/**" `
        ":(glob)**/Whisper/**" `
        ":(glob)**/tesseractmodels/**" `
        ":(glob)**/DubEngine/**" `
        ":(glob)**/dubmodels/**" `
        ":(glob)**/*.ru.dub.*" `
        ":(glob)**/*.ru.voices.json" `
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
    Require-PackageVersion ".\FlyleafLib\FlyleafLib.csproj" "Flyleaf.FFmpeg.Bindings" "8.0.1"
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
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Microsoft.Data.Sqlite" "9.0.17"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "Prism.DryIoc" "9.0.537"
    Require-PackageVersion ".\LLPlayer\LLPlayer.csproj" "SQLitePCLRaw.bundle_e_sqlite3" "3.0.3"
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

    # LLPlayer and FlyleafLib are intentionally aligned to the same Flyleaf.FFmpeg.Bindings version (8.0.1, matching
    # the shipped FFmpeg 8.0 native DLLs) — see docs/agent/dependency-baseline.md (T-01). Both pins are hard-enforced
    # by the Require-PackageVersion calls above, so any re-divergence fails here rather than passing as a soft warning.

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

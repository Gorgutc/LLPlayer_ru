# Additive native Windows Avalonia gate. Run scripts/codex/verify.ps1 separately for the WPF product.
# Uses tracked FFmpeg DLLs and provisions pinned, SHA-256-verified test tools when no CLI override is supplied.
# Generates the same H.264/AAC + SRT fixture as Linux; validates caller-supplied media and CLI.
$ErrorActionPreference = "Stop"

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "The native Windows Avalonia gate must run on Windows. Use scripts/linux/verify.sh on Linux."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
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

    if ([string]::IsNullOrWhiteSpace($env:LLPLAYER_FFMPEG_DIR)) {
        $env:LLPLAYER_FFMPEG_DIR = Join-Path $repoRoot "FFmpeg"
    }
    foreach ($library in @("avutil-60.dll", "avcodec-62.dll", "avformat-62.dll", "avfilter-11.dll", "swscale-9.dll", "swresample-6.dll")) {
        $path = Join-Path $env:LLPLAYER_FFMPEG_DIR $library
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Windows FFmpeg library is missing: $path. Set LLPLAYER_FFMPEG_DIR to the complete FFmpeg 8 DLL folder."
        }
    }
    if ([string]::IsNullOrWhiteSpace($env:LLPLAYER_FFMPEG_CLI)) {
        $tools = & (Join-Path $PSScriptRoot "fetch-test-ffmpeg.ps1")
        $env:LLPLAYER_FFMPEG_CLI = $tools.FFmpegPath
        $ffprobe = $tools.FFprobePath
    }
    else {
        $env:LLPLAYER_FFMPEG_CLI = [IO.Path]::GetFullPath($env:LLPLAYER_FFMPEG_CLI)
        $ffprobe = Join-Path ([IO.Path]::GetDirectoryName($env:LLPLAYER_FFMPEG_CLI)) "ffprobe.exe"
    }
    foreach ($executable in @($env:LLPLAYER_FFMPEG_CLI, $ffprobe)) {
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "FFmpeg fixture executable is missing: $executable. LLPLAYER_FFMPEG_CLI must have ffprobe.exe beside it."
        }
    }

    # Keep text, timings and encoding flags equal to scripts/linux/make-test-media.sh.
    $expectedSubtitles = @"
1
00:00:00,500 --> 00:00:03,000
Hello world, this is a test.

2
00:00:03,500 --> 00:00:06,500
The quick brown fox jumps over the lazy dog.

3
00:00:07,000 --> 00:00:11,000
Language learning with subtitles.
"@
    if ([string]::IsNullOrWhiteSpace($env:LLPLAYER_TEST_MEDIA)) {
        $fixtureRoot = Join-Path $repoRoot "artifacts\windows-avalonia-test-media"
        # Generated files stay in an ordinary artifact directory; do not follow an existing junction/symlink.
        for ($current = $fixtureRoot; $current; $current = [IO.Path]::GetDirectoryName($current)) {
            if ((Test-Path -LiteralPath $current) -and
                ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Generated test-media path contains a reparse point: $current"
            }
        }
        [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
        $env:LLPLAYER_TEST_MEDIA = Join-Path $fixtureRoot "test-720p.mp4"
        $generatedSubtitles = [IO.Path]::ChangeExtension($env:LLPLAYER_TEST_MEDIA, ".srt")
        $hasMedia = Test-Path -LiteralPath $env:LLPLAYER_TEST_MEDIA
        $hasSubtitles = Test-Path -LiteralPath $generatedSubtitles
        if ($hasMedia -ne $hasSubtitles) {
            throw "Incomplete cached test-media fixture in $fixtureRoot; preserve or remove it explicitly before retrying."
        }
        if (-not $hasMedia) {
            $temporaryMedia = Join-Path $fixtureRoot (".test-720p." + [Guid]::NewGuid().ToString("N") + ".mp4")
            $temporarySubtitles = [IO.Path]::ChangeExtension($temporaryMedia, ".srt")
            try {
                Invoke-Checked $env:LLPLAYER_FFMPEG_CLI "-hide_banner" "-nostdin" "-loglevel" "error" "-y" `
                    "-f" "lavfi" "-i" "testsrc2=size=1280x720:rate=30:duration=12" `
                    "-f" "lavfi" "-i" "sine=frequency=440:sample_rate=48000:duration=12" `
                    "-map" "0:v:0" "-map" "1:a:0" `
                    "-c:v" "libx264" "-preset" "veryfast" "-profile:v" "high" "-pix_fmt" "yuv420p" "-g" "30" "-x264-params" "threads=1" `
                    "-c:a" "aac" "-b:a" "64k" "-ac" "1" `
                    "-threads" "1" "-map_metadata" "-1" "-fflags" "+bitexact" "-flags:v" "+bitexact" "-flags:a" "+bitexact" `
                    "-movflags" "+faststart" "-f" "mp4" $temporaryMedia
                $utf8 = New-Object Text.UTF8Encoding($false)
                [IO.File]::WriteAllText($temporarySubtitles, ($expectedSubtitles -replace "`r`n", "`n") + "`n", $utf8)
                [IO.File]::Move($temporaryMedia, $env:LLPLAYER_TEST_MEDIA)
                [IO.File]::Move($temporarySubtitles, $generatedSubtitles)
            }
            finally {
                foreach ($temporaryFile in @($temporaryMedia, $temporarySubtitles)) {
                    if (Test-Path -LiteralPath $temporaryFile -PathType Leaf) { Remove-Item -LiteralPath $temporaryFile -Force }
                }
            }
        }
    }

    $env:LLPLAYER_TEST_MEDIA = [IO.Path]::GetFullPath($env:LLPLAYER_TEST_MEDIA)
    if (-not (Test-Path -LiteralPath $env:LLPLAYER_TEST_MEDIA -PathType Leaf)) {
        throw "LLPLAYER_TEST_MEDIA does not identify an existing test clip: $env:LLPLAYER_TEST_MEDIA"
    }
    $subtitlePath = [IO.Path]::ChangeExtension($env:LLPLAYER_TEST_MEDIA, ".srt")
    if (-not (Test-Path -LiteralPath $subtitlePath -PathType Leaf)) {
        throw "The playback fixture subtitle file is missing: $subtitlePath."
    }
    $actualSubtitles = [IO.File]::ReadAllText($subtitlePath) -replace "`r`n", "`n"
    if ($actualSubtitles.Trim() -cne ($expectedSubtitles -replace "`r`n", "`n").Trim()) {
        throw "The playback fixture must contain the canonical three Linux/Windows test cues: $subtitlePath."
    }
    $probeJson = & $ffprobe "-v" "error" "-show_entries" "format=duration:stream=codec_type,codec_name,width,height,sample_rate,channels" "-of" "json" $env:LLPLAYER_TEST_MEDIA
    if ($LASTEXITCODE -ne 0) { throw "ffprobe failed to validate LLPLAYER_TEST_MEDIA (exit $LASTEXITCODE)." }
    $probe = ($probeJson -join "`n") | ConvertFrom-Json
    $video = @($probe.streams | Where-Object { $_.codec_type -eq "video" })
    $audio = @($probe.streams | Where-Object { $_.codec_type -eq "audio" })
    $duration = [double]::Parse($probe.format.duration, [Globalization.CultureInfo]::InvariantCulture)
    if ($video.Count -ne 1 -or $video[0].codec_name -ne "h264" -or $video[0].width -ne 1280 -or $video[0].height -ne 720 -or
        $audio.Count -ne 1 -or $audio[0].codec_name -ne "aac" -or $audio[0].sample_rate -ne "48000" -or $audio[0].channels -ne 1 -or
        [Math]::Abs($duration - 12) -gt 0.5) {
        throw "Unexpected playback fixture: require 12-second 1280x720 H.264 and mono AAC 48000 Hz."
    }
    $mediaSha = (Get-FileHash -LiteralPath $env:LLPLAYER_TEST_MEDIA -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Validated Windows playback fixture sha256 $mediaSha"

    # Device-free pacing for ordinary tests; the separate OpenAL wave test launches its own real native backend.
    $env:LLPLAYER_AUDIO_BACKEND = "null"
    Write-Host "Windows Avalonia native fixtures: FFmpeg=$env:LLPLAYER_FFMPEG_DIR; media=$env:LLPLAYER_TEST_MEDIA; CLI=$env:LLPLAYER_FFMPEG_CLI"

    Invoke-Checked powershell.exe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\windows\test-package-avalonia.ps1"
    # The opt-in restore includes both Windows/WPF and portable targets, retaining the original Windows asset graph.
    Invoke-Checked dotnet "restore" "-warnaserror" "-p:LLPlayerPortableTests=true"
    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\FlyleafLib" "-f" "net10.0"
    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\LLPlayer.Avalonia"
    Invoke-Checked dotnet "build" "--no-restore" "-warnaserror" ".\LLPlayer.Avalonia.Tests"
    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\FlyleafLibTests" "-f" "net10.0" "-p:LLPlayerPortableTests=true"
    Invoke-Checked dotnet "test" "--no-restore" "-warnaserror" ".\LLPlayer.Avalonia.Tests"

    Write-Host "LLPlayer native Windows Avalonia verification completed. The independent Windows WPF gate remains required."
}
finally {
    Pop-Location
}

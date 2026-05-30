$ErrorActionPreference = "Stop"

if (-not (Test-Path ".\scripts\codex\verify-fast.ps1")) {
    Write-Host "LLPlayer post-tool verify skipped: verify-fast.ps1 is not present yet."
    exit 0
}

$changed = @()
$changed += git diff --name-only 2>$null
$changed += git diff --cached --name-only 2>$null
$changed += git ls-files --others --exclude-standard 2>$null

if (-not $changed) {
    Write-Host "LLPlayer post-tool verify skipped: no working tree changes detected."
    exit 0
}

$patterns = @(
    '\.cs$',
    '\.xaml$',
    '\.csproj$',
    '\.slnx$',
    '^Directory\.Build\.',
    '^AGENTS\.md$',
    '^CLAUDE\.md$',
    '^GEMINI\.md$',
    '^DO_NOT_PUSH\.md$',
    '^RUN_INSTRUCTIONS\.md$',
    '^\.codex/',
    '^\.agents/',
    '^docs/agent/',
    '^scripts/codex/',
    '^Plugins/llplayer-codex/',
    '^Plugins/YoutubeDL/',
    '^FFmpeg/',
    '^LLPlayer/lib/'
)

$normalized = $changed |
    Where-Object { $_ } |
    ForEach-Object { ($_ -replace '\\', '/').Trim() }
$shouldRun = $false
foreach ($line in $normalized) {
    foreach ($pattern in $patterns) {
        if ($line -match $pattern) {
            $shouldRun = $true
            break
        }
    }
    if ($shouldRun) { break }
}

if (-not $shouldRun) {
    Write-Host "LLPlayer post-tool verify skipped: changed paths do not affect Codex or .NET/WPF gates."
    exit 0
}

Write-Host "Running LLPlayer fast verification after relevant changes..."
& ".\scripts\codex\verify-fast.ps1"

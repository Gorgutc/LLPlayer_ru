$ErrorActionPreference = "Stop"

Write-Host "LLPlayer Codex session"
Write-Host "- Source of truth: AGENTS.md"
Write-Host "- Stack: C#/.NET 10/WPF, Windows-only, FlyleafLib, FFmpeg, Whisper/Tesseract, YoutubeDL plugin"
Write-Host "- Fast gate: .\scripts\codex\verify-fast.ps1"
Write-Host "- Full gate: .\scripts\codex\verify.ps1"
Write-Host "- Ship gate: .\scripts\codex\ship.ps1"
Write-Host "- Do not port web/Node/Playwright/Lighthouse gates unless a task explicitly adds a web surface."

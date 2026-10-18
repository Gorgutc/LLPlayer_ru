---
name: llplayer-dotnet-rules
description: Use when editing C#, XAML, csproj, publish profiles, or .NET verification for LLPlayer_ru.
---

# LLPlayer .NET Rules

The shipped Windows WPF app targets `net10.0-windows10.0.18362.0` and `win-x64`.
F-13 adds shared `LLPlayer.Core` and one native Windows/Linux `LLPlayer.Avalonia` host over FlyleafLib portable `net10.0`.
The Avalonia host has `win-x64;linux-x64` RIDs with shared views, view models and themes. Its Windows build/package
baseline is implemented; native acceptance and Windows ASR/OCR/GPU require separate evidence. Preserve WPF until
explicit replacement acceptance; see `docs/agent/architecture.md`.

## Preserve

- `LLPlayer` remains `WinExe` and `UseWPF=true`.
- `FlyleafLib` remains the media engine boundary.
- `Plugins/YoutubeDL` remains a separate .NET plugin.
- `LLPlayer/Properties/PublishProfiles/FolderProfile.pubxml` remains the Windows WPF app publish profile.

## Verify

The Windows WPF gate remains required:

```powershell
dotnet restore -warnaserror
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore -warnaserror .\FlyleafLibTests
```

For shared/Core/portable/Avalonia changes, also run `scripts/windows/verify-avalonia.ps1` and
`scripts/linux/verify.sh`; package with `scripts/windows/publish-avalonia.ps1` or `scripts/linux/publish.sh`.
Keep the portable `net10.0` engine reference on Windows Avalonia: the WPF-specific Windows engine excludes
portable seams. Report native desktop acceptance separately from build/headless evidence.

Route changed paths through `docs/agent/subagent-review-matrix.md`, use explicit spawned reviewers, and finish with a
spawned `/review` including `verification_reviewer`. Resolve every Critical/Important finding before handoff.

Do not add Node, browser, Lighthouse, or Playwright gates for this desktop app.

---
name: llplayer-dotnet-rules
description: Use when editing C#, XAML, csproj, publish profiles, or .NET verification for LLPlayer_ru.
---

# LLPlayer .NET Rules

The shipped Windows WPF app targets `net10.0-windows10.0.18362.0` and `win-x64`.
F-13 adds shared `LLPlayer.Core` and the current Linux `LLPlayer.Avalonia` host over FlyleafLib portable `net10.0`.
The target is the same Avalonia UI/Core on native Windows and Linux; the Windows Avalonia baseline is proposed,
not implemented or accepted. Preserve WPF until explicit replacement acceptance; see `docs/agent/architecture.md`.

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

For shared/Core/portable/Avalonia or Linux runtime changes, also run `scripts/linux/verify.sh`; Linux packaging uses
`scripts/linux/publish.sh`. Report native desktop acceptance separately from build/headless evidence.

Route changed paths through `docs/agent/subagent-review-matrix.md`, use explicit spawned reviewers, and finish with a
spawned `/review` including `verification_reviewer`. Resolve every Critical/Important finding before handoff.

Do not add Node, browser, Lighthouse, or Playwright gates for this desktop app.

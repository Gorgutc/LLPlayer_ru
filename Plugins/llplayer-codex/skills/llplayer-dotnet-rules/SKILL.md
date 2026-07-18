---
name: llplayer-dotnet-rules
description: Use when editing C#, XAML, csproj, publish profiles, or .NET verification for LLPlayer_ru.
---

# LLPlayer .NET Rules

LLPlayer targets `net10.0-windows10.0.18362.0` and `win-x64`.

## Preserve

- `LLPlayer` remains `WinExe` and `UseWPF=true`.
- `FlyleafLib` remains the media engine boundary.
- `Plugins/YoutubeDL` remains a separate .NET plugin.
- `LLPlayer/Properties/PublishProfiles/FolderProfile.pubxml` remains the app publish profile.

## Verify

```powershell
dotnet restore -warnaserror
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore -warnaserror .\FlyleafLibTests
```

Route changed paths through `docs/agent/subagent-review-matrix.md`, use explicit spawned reviewers, and finish with a
spawned `/review` including `verification_reviewer`. Resolve every Critical/Important finding before handoff.

Do not add Node, browser, Lighthouse, or Playwright gates for this desktop app.

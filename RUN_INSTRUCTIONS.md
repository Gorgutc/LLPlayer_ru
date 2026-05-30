# Run Instructions

LLPlayer is a Windows-only WPF app targeting `.NET 10` and `win-x64`.

## Baseline

```powershell
dotnet restore
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore .\FlyleafLibTests
```

## Codex Gates

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\ship.ps1
```

`ship.ps1` performs a publish smoke into a temporary directory. Release packaging remains defined by `.github/actions/build-package/action.yml`.

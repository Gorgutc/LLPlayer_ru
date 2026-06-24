# Verification

## Fast Infra Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
```

Checks environment basics, plugin/skills/docs structure, documentation coverage, hooks, and frozen stack/product decisions.

The fast gate includes:

- `scripts/codex/check-environment.ps1`
- `scripts/codex/verify-plugin.ps1`
- `scripts/codex/verify-doc-coverage.ps1`
- `scripts/codex/verify-frozen.ps1`
- `scripts/codex/check-dub-licenses.ps1`

Use this read-only helper before review when you need to map changed files to frozen contracts, agents, and gates:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\audit-frozen.ps1
```

## Full Build/Test Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
```

Runs fast verification plus:

```powershell
dotnet restore
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore .\FlyleafLibTests
```

## Ship Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\ship.ps1
```

Runs full verification and publish smoke in a temp directory. GitHub release packaging remains defined in `.github/actions/build-package/action.yml`.

The ship gate verifies app publish, `LLPlayer/lib/7z.dll` in publish output, strict runtime cleanup, FFmpeg copy, separate YoutubeDL plugin publish, plugin DLL/PDB copy, `yt-dlp.exe_here` placeholder creation, and dry-run markers for `yt-dlp.exe` download plus the 7-Zip archive command. It does not perform the network `yt-dlp.exe` download during local smoke.

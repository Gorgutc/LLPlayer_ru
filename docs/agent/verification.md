# Verification

## Fast Infra Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
```

Checks environment basics, plugin/skills/docs structure, documentation coverage, hooks, frozen stack/product decisions,
and release-workflow input/output safety.

The fast gate includes:

- `scripts/codex/check-environment.ps1`
- `scripts/codex/verify-plugin.ps1`
- `scripts/codex/verify-doc-coverage.ps1`
- `scripts/codex/verify-frozen.ps1`
- `scripts/codex/verify-build-workflow.ps1`
- `scripts/codex/verify-release-workflow.ps1`
- `scripts/codex/check-dub-licenses.ps1`

`verify-release-workflow.ps1` executes positive and adversarial fixtures against
`validate-release-token.ps1`, then fails closed if `testing-release.yml` again interpolates dispatch inputs or
derived release metadata directly inside PowerShell. The workflow validates the requested ref, latest stable tag,
checked-out commit hash, and archive basename before writing GitHub outputs or calling the overwrite upload tail.
This gate does not dispatch a release or modify GitHub assets.

GitHub's `Build & Test` workflow runs the fast gate after setting up .NET 10 and before its separate
restore, app/plugin build, and test steps, so infrastructure or frozen-contract drift fails before compilation.
`verify-build-workflow.ps1` validates .NET 10 setup and fast-gate placement relative to restore inside
`jobs.build`, rejects conditional or continue-on-error bypasses, and exercises adversarial hierarchy,
cross-job, block-scalar, setup, SDK, and ordering fixtures.

Use this read-only helper before review when you need to map changed files to frozen contracts, agents, and gates:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\audit-frozen.ps1
```

Before final handoff, run spawned `/review` with at least `verification_reviewer`. If no subagent spawn tool is available, report that explicitly and do not claim `/review` has been satisfied.

## Full Build/Test Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
```

Runs fast verification plus:

```powershell
dotnet restore -warnaserror
dotnet build --no-restore -warnaserror .\LLPlayer
dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL
dotnet test --no-restore .\FlyleafLibTests
```

## Ship Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\ship.ps1
```

Runs full verification and publish smoke in a temp directory. GitHub release packaging remains defined in `.github/actions/build-package/action.yml`.

The ship gate verifies app publish, `LLPlayer/lib/7z.dll`, `Assets/silero_vad.onnx`, `onnxruntime.dll`, committed `dub_sidecar` source plus `uv.lock` in publish output, strict runtime cleanup, FFmpeg copy, separate YoutubeDL plugin publish, plugin DLL/PDB copy, `yt-dlp.exe_here` placeholder creation, and dry-run markers for `yt-dlp.exe` download plus the 7-Zip archive command. It also rejects dubbing runtime/model/output artifacts such as `DubEngine`, `dubmodels`, `*.ru.dub.*`, and `*.ru.voices.json` in the publish layout. It does not perform the network `yt-dlp.exe` download during local smoke.

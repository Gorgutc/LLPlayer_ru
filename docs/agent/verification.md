# Verification

## Fast Infra Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
```

Checks environment basics, plugin/skills/docs structure, documentation coverage, hooks, frozen stack/product decisions,
and release-workflow input/output safety.

Hook verification requires the current Codex nested shape (`event -> matcher group -> hooks[] -> command handler`) and
walks every configured Windows command handler. Each handler must use the single static form
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File <repo-relative .ps1>`; CMD expansion/metacharacters,
dynamic targets, duplicate JSON keys or `-File` arguments, wildcard/traversal, missing or non-file targets, paths outside
the repository, and reparse-point escapes fail closed. The gate also rejects a repository-root `powershell.exe` that
could shadow the system launcher. System `PATH` and `COMSPEC` remain an operating-system/process trust boundary. The
repository-relative command assumes Codex starts the hook from the repository root, so a subdirectory-start remains an
explicit runtime limitation rather than something this structural gate can prove.

The fast gate includes:

- `scripts/codex/check-environment.ps1`
- `scripts/codex/verify-plugin.ps1`
- `scripts/codex/verify-doc-coverage.ps1`
- `scripts/codex/verify-frozen.ps1`
- `scripts/codex/verify-build-workflow.ps1`
- `scripts/codex/verify-release-workflow.ps1`
- `scripts/codex/check-dub-licenses.ps1`

`verify-release-workflow.ps1` executes positive and adversarial fixtures against
`validate-release-token.ps1`, rejects direct expression interpolation inside PowerShell, and invokes the structural
`verify-testing-release-boundary.ps1` contract. Testing Release is split across four fresh GitHub-hosted jobs:

- `prepare` runs with `contents: read`, requires the workflow itself to be dispatched from the default branch,
  validates the requested ref and release metadata, and resolves the selected ref once to a full commit id;
- `build` runs that immutable commit with `contents: read`, packages it, and uploads one fixed-name unverified
  workflow artifact without exposing any build-job output to the privileged job;
- `verify` runs trusted workflow-owned validation with `contents: read`, downloads the unverified artifact from the
  current run with digest mismatch set to `error`, accepts exactly one non-empty regular archive, and republishes
  only its validated absolute path under a distinct fixed verified-artifact name;
- `upload` runs with `contents: write`, performs no checkout and executes no selected-ref code, depends on `verify`,
  downloads only that fixed verified artifact with digest mismatch set to `error`, repeats the path/shape validation,
  and passes only its validated absolute path to the fixed Testing Release upload command.

The structural gate uses exact job/step allowlists plus adversarial mutations for permission widening, missing
prepare/build/verify dependencies, moving-ref checkout, self-hosted runners, mutable action tags, wildcard or
overwrite transport, raw-artifact delivery to the write job, cross-run downloads, digest downgrade, checkout/local
actions in the write job, token leakage, path-validation bypass, artifact extraction, expression injection,
duplicate/quoted keys, and custom shell defaults. It also runs filesystem fixtures for valid, extra, nested, empty,
mismatched, and unsafe archive shapes. This gate does not dispatch a release or modify GitHub assets; the overwrite
run remains owner-gated. The boundary protects the write token and artifact transport, but it does not attest that
owner-selected package bytes are trustworthy, and the default-branch guard is an accidental-misdispatch check rather
than protection from an authorized contributor changing the control workflow.

Both release callers have a mandatory fresh full-verification preflight before the shared packaging action:

1. checkout the exact tag/immutable commit;
2. use the immutable `setup-dotnet` v5.4.0 action to install the repository's `10.0.x` SDK channel;
3. run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1` with no skip switch;
4. only after success invoke `.github/actions/build-package/action.yml` and its publish/archive tail.

The preflight is caller-owned rather than present only inside the composite action. Testing Release intentionally
executes the local action from the owner-selected commit, so an older selected ref could otherwise carry a preflight-
free action. With the caller gate, a selected ref that lacks the canonical `verify.ps1` fails closed before packaging.
Stable tags are protected when they point to a commit containing this workflow revision; historical tag commits cannot
be changed retroactively.

`verify-testing-release-boundary.ps1` locks the Testing build-job order after immutable-checkout verification.
`verify-release-workflow.ps1` locks the Stable same-job order. Their adversarial fixtures reject a missing or late
preflight, `verify-fast.ps1` substitution, `-SkipRestore`, `continue-on-error`, conditional bypass,
anonymous/duplicate steps, and packaging before the gate. These structural checks do not push tags, dispatch a
workflow, create a draft release, or overwrite `v0.0.1`; both controlled release runs remain separately owner-gated.
The separate Stable risk of executing tag-selected repository code in a job with `contents: write` is not resolved by
the preflight.

GitHub's `Build & Test` workflow runs the fast gate after setting up .NET 10 and before its separate
restore, app/plugin build, and test steps, so infrastructure or frozen-contract drift fails before compilation.
`verify-build-workflow.ps1` validates .NET 10 setup and fast-gate placement relative to restore inside
`jobs.build`, rejects conditional or continue-on-error bypasses, and exercises adversarial hierarchy,
cross-job, block-scalar, setup, SDK, and ordering fixtures.

Use this read-only helper before review when you need to map changed files to frozen contracts, agents, and gates:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\audit-frozen.ps1
```

Routing rules are cumulative. Every tracked or newly-created `*.cs`, `*.xaml`, `*.csproj`, `*.sln`, and `*.slnx` path must
receive the literal full `verify` gate plus the minimum reviewers from `subagent-review-matrix.md`; narrower WPF,
media, plugin, native, or packaging matches add requirements and never replace those minimums. `ship` remains an
additional packaging gate even though `ship.ps1` invokes the full gate internally.

`verify-frozen.ps1` enforces these extension floors over the complete tracked target set through the same structured
router used by the human-readable command. It also checks representative additive domain routes, table-driven
future/untracked paths, case and slash variants, exact-extension near misses, and adversarial routes missing `verify`
or a mandatory reviewer. This is executable behavior coverage, not a source-text marker substitute.

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

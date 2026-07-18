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
- `scripts/codex/verify-full-gate.ps1`
- `scripts/codex/verify-build-workflow.ps1`
- `scripts/codex/verify-release-workflow.ps1`
- `scripts/codex/check-dub-licenses.ps1`

`verify-release-workflow.ps1` executes positive and adversarial fixtures against
`validate-release-token.ps1`, rejects direct expression interpolation inside PowerShell, and invokes both release
boundary contracts: `verify-testing-release-boundary.ps1` and `verify-stable-release-boundary.ps1`. Each boundary
validator locks the normalized reviewed workflow SHA-256 and then checks semantic invariants and adversarial
mutations, so duplicate keys, extra jobs/triggers, mutable Actions, broader permissions, bypassed verification, or
privileged artifact execution fail closed.

Testing Release is split across four fresh GitHub-hosted jobs:

- `prepare` runs with `contents: read`, requires the workflow itself to be dispatched from the default branch,
  accepts only one exact lowercase 40-character commit SHA, requires it to equal the trusted workflow `${{ github.sha }}`,
  resolves that same commit exactly once, and derives the immutable
  `testing-<12sha>` tag plus `LLPlayer-testing-<12sha>-x64.7z` asset name;
- `build` runs that immutable commit with `contents: read`, packages it, and uploads one fixed-name unverified
  workflow artifact without exposing any build-job output to the privileged job;
- `verify` runs trusted workflow-owned validation with `contents: read`, downloads the unverified artifact from the
  current run with digest mismatch set to `error`, accepts exactly one non-empty regular archive, recomputes its
  size/SHA-256, runs `7z t`, verifies the archived yt-dlp size/SHA-256, and republishes only its validated absolute
  path under a distinct fixed verified-artifact name;
- `upload` runs with `contents: write`, performs no checkout and executes no selected-ref code, depends on `verify`,
  downloads only that fixed verified artifact with digest mismatch set to `error`, repeats path/shape/hash validation,
  and creates or reuses only the exact per-commit draft prerelease. The job never moves a tag; `--clobber` is allowed
  only when the existing tag still points directly to the same commit and the Release is still a draft containing no
  unexpected assets.

Stable Release uses the same four-stage isolation with `prepare`, `build`, `verify`, and `publish`:

- it is manually dispatched from the default branch with an exact lowercase 40-character commit SHA that must equal
  the trusted workflow `${{ github.sha }}`, plus a strict `vMAJOR.MINOR.PATCH` tag;
- the read-only build proves the tag matches the single `<Version>` value in `LLPlayer/LLPlayer.csproj`, runs the full
  gate, packages selected code, and emits no GitHub release mutation;
- trusted verification independently tests the archive and evidence before republishing a fixed verified artifact;
- only `publish` receives `contents: write`; it has no checkout/local action/archive parsing, revalidates the file hash
  and size, creates a new direct tag without force, creates a draft non-prerelease, uploads one asset, and reads the
  tag/Release/asset back. It never publishes or replaces an existing Stable tag/Release.

The structural gates exercise adversarial mutations for permission widening, missing dependencies, moving-ref
checkout, self-hosted runners, mutable or unexpected Actions, raw-artifact delivery to a write job, digest downgrade,
checkout/local actions under a write token, path-validation bypass, expression injection, duplicate keys, and bypass
conditions. Filesystem fixtures cover valid, extra, empty, wrong-name, wrong-size, and wrong-digest shapes. These gates
do not dispatch workflows, create tags/Releases, or upload assets. They protect token/transport boundaries but do not
replace review of the selected package bytes; the default-branch guard is an accidental-misdispatch check, not a
defense against an authorized contributor changing the trusted control workflow.

Both release callers have a mandatory fresh full-verification preflight before the shared packaging action:

1. checkout the exact tag/immutable commit;
2. use the immutable `setup-dotnet` v5.4.0 action to install the repository's `10.0.x` SDK channel;
3. run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1` with no skip switch;
4. only after success invoke `.github/actions/build-package/action.yml` and its publish/archive tail.

The preflight is caller-owned rather than present only inside the composite action. Both workflows fail closed unless
the dispatch input, trusted default-branch workflow `${{ github.sha }}`, run `head_sha`, and selected commit are the same
commit. The caller gate and frozen boundary checks reject a missing canonical `verify.ps1` or a preflight-order
regression before packaging. Stable no longer executes a tag-selected workflow: the trusted default-branch control
plane creates the requested tag only after that same commit has passed read-only build and trusted artifact verification.

Both boundary validators lock checkout -> exact-SHA/version verification -> immutable .NET 10 setup -> full preflight
-> package ordering. Their fixtures reject a missing or late preflight, `verify-fast.ps1` substitution, bypass flags,
conditional execution, packaging before the gate, and any write-token regression. Static verification never creates
tags, draft Releases, or assets; each real controlled run remains a separately authorized external action. Operational
release evidence must prove the dispatched control ref, selected SHA, run SHA, tag target, draft state, exact asset
name/size/SHA-256, `7z t`, and resolved yt-dlp version/size/SHA-256.

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
dotnet test --no-restore -warnaserror .\FlyleafLibTests
```

## Risk-Based Coverage Policy

Coverage decisions follow risk, not a target count. Every behavior change or bug fix must add or update a deterministic,
non-vacuous regression test when a safe seam exists. Record intentional RED evidence where applicable, then restore the
production implementation and prove the focused test plus the full unfiltered suite green.

No global coverage percentage and no hard-coded passing-test total is a quality gate. If there is no safe deterministic
seam because the boundary is WPF, native, GPU, network, or timing-dependent, document why and name the exact manual or
integration smoke that carries the residual risk. CI structurally locks checkout provenance, the complete step sequence,
and the exact unfiltered warning-clean test command through `verify-build-workflow.ps1`; `verify-full-gate.ps1` protects
the executable local/full gate. Filtered, conditional, missing, reordered, or wrong-project Test steps are rejected.

## Ship Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\ship.ps1
```

Runs full verification and publish smoke in a temp directory. GitHub release packaging remains defined in `.github/actions/build-package/action.yml`.

The ship gate verifies app publish, `LLPlayer/lib/7z.dll`, `Assets/silero_vad.onnx`, `onnxruntime.dll`, committed `dub_sidecar` source plus `uv.lock` in publish output, strict runtime cleanup, FFmpeg copy, separate YoutubeDL plugin publish, plugin DLL/PDB copy, `yt-dlp.exe_here` placeholder creation, and dry-run markers for `yt-dlp.exe` download plus the 7-Zip archive command. It also rejects dubbing runtime/model/output artifacts such as `DubEngine`, `dubmodels`, `*.ru.dub.*`, and `*.ru.voices.json` in the publish layout. It does not perform the network `yt-dlp.exe` download during local smoke.

# Windows desktop test build

The owner requested a blue Desktop shortcut named **LLPlayer Avalonia (Windows)** and a fresh build behind it after
every completed LLPlayer task/stage, including documentation and tooling work. This is a native Windows test shortcut
for the existing shared Avalonia app. Windows and Linux still use one Views/ViewModels/Themes tree and shared Core;
the shortcut does not replace the supported WPF product or establish desktop/model acceptance.

On 2026-11-10 the owner replaced unlimited build retention with automatic **Keep2**: the current build and one verified
fallback, plus protected exceptions. This changes local deployment retention, not player behavior or native acceptance.

## Refresh before handoff

Run from the repository on Windows after the task's final commit is in place. The updater refuses an uncommitted or dirty
source tree (it re-checks the clean `HEAD` before publishing, after publishing and before activation), so commit the
reviewed task first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\update-desktop-test-build.ps1
```

Normal refresh must omit `-PackageDir`: the updater freshly publishes the Windows Avalonia package through
`publish-avalonia.ps1`, validates it and records the actual clean Git `HEAD`. It checks that source identity before
and after publication and before activating the payload. Do not make later task edits and still describe that receipt
as the final build. Re-run refresh after the final source/docs/tooling commit changes `HEAD`.

Use an already available .NET 10 SDK for publication (pass it via `-DotNetRoot` when it is not the default `PATH` SDK
and record the SDK version in the handoff; the updater never installs or selects an SDK itself) and the existing
registered system x64 .NET 10 runtime for the direct app target. The updater accepts `-DotNetRoot` for an existing SDK
directory and also resolves the process's `DOTNET_ROOT` or the system installation. For example:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\update-desktop-test-build.ps1 -DotNetRoot 'C:\path\to\existing\dotnet'
```

`-DotNetRoot` is build-only: the updater temporarily sets process `DOTNET_ROOT`/`DOTNET_ROOT_X64`/`PATH` for the
publisher and restores the caller's environment. The Desktop link has no runtime wrapper or private runtime copy.
Refresh performs no runtime installer, global tool installation or persistent environment change. The recorded global
runtime follows the apphost's framework-dependent roll-forward policy: the latest patch of the declared `10.0` minor,
falling back to the lowest higher `10.x` minor only when that minor is absent; a hypothetical higher minor installed
alongside `10.0` is not recorded as the launch runtime. If the required SDK, system runtime, native prerequisites,
permissions or blue asset are unavailable, report the failure instead of installing or changing the machine implicitly.
Existing package validation and the separate WPF/Windows/Linux gates remain required for their normal scopes.

Direct user read-only/no-build or other explicit execution constraints override this refresh rule. On a non-Windows
host, or when refresh cannot run, record **NOT_RUN** and the concrete reason. Do not claim that the Windows icon is
current. A build or shortcut receipt never counts as native GUI/DPI/keyboard, model/provider or GPU acceptance.

## Stable shortcut and retained payloads

The defaults use `%USERPROFILE%\Applications\LLPlayer-Avalonia-TestBuild`, outside AppData virtualization, and the
Windows Desktop known folder. Optional
`-InstallRoot` and `-DesktopPath` override those locations for scoped verification; they do not change global settings.
The default icon source is `scripts/windows/assets/llplayer-avalonia-blue.ico` (`-IconPath` can select an explicit icon).
The blue ICO uses DIB encoding (361,102 bytes; SHA-256 `8354c6f8a8a42e6f9038a93a80afc96d8db2dbc1e300cbb7309990b058bdb72d`);
the source PNG is unchanged. See [asset provenance](../../scripts/windows/assets/icon-provenance.json). Encoding
compatibility is separate from the proven path-alias defect below.

The managed root uses schema 2 ownership/current metadata in `owner.json` and `current.json`, plus `builds/<id>/`.
Each build keeps its own `payload/`, `blue.ico` and `receipt.json`. The stable
`LLPlayer Avalonia (Windows).lnk` targets the selected build's `payload/LLPlayer.Avalonia.exe` directly, with empty
arguments, the `payload/` working directory and that build's `blue.ico`. There is no PowerShell launcher in the normal
launch path. Windows **Open File Location** must lead to the actual selected app folder. `current.json` carries the
selected build id, receipt path/hash, executable path/hash, working directory, icon path/hash and shortcut path/hash so
the pointer, receipt and link can be cross-checked; the receipt records the same target/working-directory/icon
contract plus the global runtime root/version, `legacyMigration` provenance, native ShellLink readback and the installed
file manifest.

A later refresh revalidates the existing owner/current/receipt/executable/icon/shortcut identities before writing
anything, including the required `current.json` identity fields and the selected shortcut bytes against the receipt
hash. `current.json` and the Desktop link must identify the same selected build. A recognized owned schema 1
installation may be migrated safely to the direct schema 2 link; its legacy launcher/icon files and previous payloads
are retained. Three verified schema 1 shortcut forms are accepted: the original PowerShell launcher, the direct
physical-icon repair (`desktop-blue-dib.ico`), and the intermediate direct link still using the old `blue.ico`.
The PowerShell form records logical AppData paths for its arguments, working directory and icon; a refresh resolves it
only where that logical view reaches the same physical root (the virtualizing host that created it, or an equivalent
alias). On a host where the logical root is unavailable the updater refuses instead of guessing, so repair such a link
to the physical direct form first and record that as a separate action. Migration records the physical and recorded
legacy roots, the retained owner/current/shortcut byte hashes, the previous
build id and the launcher/icon identities in `owner.json` and in the new receipt. Foreign or edited ownership/link
content must be rejected rather than overwritten; the default refresh also refuses a schema-1-shaped root outside the
bound legacy locations. The migrating build's receipt carries `legacyMigration`; later refresh receipts describe only
their own build, while `owner.json` keeps `legacy` and `migratedFromSchema` as the permanent ownership provenance.
Record an owned migration in the update evidence and handoff.

Persist only physical paths for the link target, working directory, icon, payload, receipts and current metadata.
The updater validates existing filesystem paths through handles and rejects logical aliases. The install destination is
resolved through its nearest existing ancestor, so a redirected or virtualized ancestor cannot become a new deployment
destination either. Migration from a verified owned Codex MSIX-cache installation must preserve its old physical
payloads/ownership evidence; it must not stop a running player, delete the old installation or touch foreign
configuration. The new default needs no global install or registry change. A persistent `DOTNET_ROOT`/`DOTNET_ROOT_X64`
override in the user or machine scope is accepted only when it names the same registered installation the updater
validates; any other value is refused, because the direct apphost would then depend on an environment that does not
survive on another host. Process-scoped `-DotNetRoot` remains build-only.

The updater and its tests never launch the player, stop an existing player process or modify the original WPF
shortcut. The previous selection remains available throughout activation; successful updates then apply Keep2 below.
Activation follows package validation, installation and shortcut/readback
checks and is a compensating transaction: it snapshots the previous shortcut/pointer/owner, then updates the link,
`current.json` and `owner.json`, and re-reads the link natively. Any exception restores the snapshots and refuses to
overwrite state that changed concurrently. This is not crash-atomic: if the process is killed between the link
replacement and the pointer write, the next run refuses the delivery because the shortcut no longer matches the
receipt. Recover by inspecting which retained build's receipt matches the installed link; the previous selection stays
in `current.json`. A failed refresh keeps a complete unselected build as evidence only inside an already managed root
(with a `current.json`); a failed first installation removes its new root, incomplete build directories are removed,
and an emptied `builds` directory is pruned, so the next attempt can run cleanly. These guarantees apply within a run
that reaches its rollback path: a process killed mid-run can leave an incomplete build directory or temporary file
for the next handoff to inspect, because there is no startup sweep. Report a failed refresh from its actual receipt and
preserved prior state rather than claiming that a new payload became current.

## Automatic Keep2 retention

After successful activation and readback, `desktop-build-retention.ps1` keeps the current immutable build and one
verified fallback when available. It prefers the verified previously selected build supplied by the updater; otherwise
it uses the newest verified noncurrent receipt's `createdUtc`. Directory modification time is not selection evidence.
The helper defaults to `-KeepLast 2` (minimum two); normal updater use applies the policy automatically, without another
confirmation. There is no player setting or separate product task for retention.

Running builds and builds referenced by Desktop or pinned Taskbar shortcuts are protected extras. Changed, foreign,
unverifiable or unsafe candidates are skipped and reported; unresolved protection must not be guessed away to meet a
count. More than two builds can therefore remain. Protect user data, downloaded models, configuration, legacy
installations and the WPF shortcut. An arbitrary prebuilt `-PackageDir` is never a cleanup target.

Every kept or protected receipt's `sourceArchive` and `packageSource` dependencies remain available. Associated publish
jobs can be removed only inside a verified owned `artifacts/windows-desktop-test-build-<32hex>/` boundary, after their
build is pruned and no retained receipt depends on them. New jobs carry separate ownership and whole-tree hash
manifests; immutable build receipts are not rewritten. Historical jobs without those manifests are skipped by automatic
retention and belong to the owner's reviewed one-time cleanup allowlist. Generic `artifacts/`, QA, SDK and unrelated
source/data directories are not swept.

Pruning runs outside the activation rollback transaction. Failed activation does not trigger retention; cleanup errors
do not roll back a successfully selected new build. The updater returns a separate `retention` result with retained and
protected builds, deleted builds/jobs and errors. Report partial or skipped cleanup honestly rather than claiming a
strict two-build result. Revalidate physical boundaries, ownership, content and the current selection before deletion;
never follow reparse points or delete changed/unowned content to satisfy Keep2.

The helper can also show a read-only preview; omit `-Apply` for that mode:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\desktop-build-retention.ps1
```

Scoped helper inputs are `-InstallRoot`, `-DesktopPath`, `-IconPath`, `-KeepLast` and optional `-PreviousBuildId`.
The updater supplies `-Apply` only after its successful readback. Preview does not replace the actual cleanup receipt.

## Evidence and recovery

Before reporting success, retain the verification log/output and read back `current.json`, the selected build's
`receipt.json`, the installed file manifest and the Desktop link. In default fresh-publish mode, receipt `sourceCommit`
must equal the actual final repository `HEAD`; package/source identities and installed hashes must match the fresh
publish. Record the payload path, package hash and receipt identity in the handoff. A blue icon alone proves no build
identity, and an old receipt proves only its own checkpoint.

COM link readback alone does not prove a working click or the rendered Desktop icon. The 2026-10-25 diagnostic proved
that Codex MSIX virtualization redirected logical AppData writes into the user's physical
`AppData\Local\Packages\OpenAI.Codex_*\LocalCache\Local\...` backing store. Explorer resolved the executable to its
physical file and the app opened, but `IconLocation` still named the logical AppData path readable inside Codex;
Explorer therefore displayed a generic icon. A native ShellLink/ICO-extraction probe inside Codex still returned the
icon file, so extraction inside the virtualizing host did not establish cross-process path usability. The confirmed
cause is the logical/physical path mismatch; neither stale display/cache nor ICO encoding is established as its cause.

The system x64 runtime present on this machine is .NET `10.0.7`, which satisfies the package's declared `10.0.0`
framework requirement. The user-confirmed direct-EXE startup of the retained payload presumably resolved that
registered runtime (it is the only eligible `10.x` installation), but the runtime identity was not instrumented, so
execution on `10.0.7` is inferred rather than measured. One separately authorized `--version` trace
exited 0 using an explicit temporary `DOTNET_ROOT_X64` runtime `10.0.13`, not the registered `10.0.7` runtime, and
proves only that non-GUI host resolution. At the 2026-10-25 bounded manual checkpoint, the user confirmed application
startup and **Open File Location** on the earlier direct link (while its `IconLocation` still named the logical AppData
path), and after the physical `IconLocation` repair answered that the Desktop icon became blue. Launch through the
repaired physical-target link itself is **NOT_RUN**; only the blue icon was re-confirmed after that repair. These checks
apply only to the retained `4756…` payload and its repaired icon path. They do not accept a future schema 2 deployment,
full GUI/DPI/keyboard behavior, media/models/providers/GPU or the new updater's source changes. Keep those source
reviews and deployment receipts separate. The updater/tests do not perform the player launch.

Local receipt digests for that bounded checkpoint (files are kept under ignored `artifacts/`, not in source Git):
`user-manual-checkpoint.json` SHA-256 `224483ecde0a5c51b44b0f2ec6e088717aec2e6fec8a3ea295c1f6d8c43c7f13` and
`desktop-msix-path-fix.json` SHA-256 `649b51c63eb29e70baec31328e7bab3127d5984b3a8f597e1a1aa005621809ea`.
They record the three user reports, the `appLaunched:false` path-fix boundary and the logical/physical path pairs; the
repository guide, commit history and these digests are the tracked description of that local evidence.

`-PackageDir` is an offline/prebuilt validation mode for contract tests or an explicitly requested republish of a
verified earlier payload. Its receipt uses `buildMode=prebuilt-validated` and `sourceCommit=null`; it must never be
reported as a fresh build of current `HEAD`. It is not the normal refresh command. In that mode only, two
contract-fixture inputs exist: `-LegacyRootsForTests` adds explicit fixture roots to the bound schema-1 legacy-root
allowlist, and `-FailAfterReceiptForTests` injects a post-receipt failure for cleanup contracts. Both require
`-PackageDir` and are rejected before any fresh publish; they do not skip shortcut discovery, handle-based physical
resolution or any owner/receipt/executable/icon/launcher/shortcut identity check. A normal refresh can therefore only
migrate the bound known legacy locations and cannot inject failures.

Do not edit the pointer by hand, bypass Keep2 protection or restart the player to recover from an update failure.
Keep the previous build/receipt and failed verification output. If the owner explicitly requests rollback, extract a
verified previous archive/package into separate staging **outside `InstallRoot`**, then select that staged package
through the updater's prebuilt mode. `-PackageDir` must not point inside the managed installation, including retained
`builds/<id>/payload/`. The resulting receipt shows only `buildMode=prebuilt-validated`, `sourceCommit=null`,
`sourceArchive=null` and `packageSource=<staging>`; it carries no automatic rollback label, so record the deliberate
rollback, the staged archive SHA-256 and its older provenance in the handoff and do not describe it as current-source
refresh. There is no rollback flag or automatic app launch. Failed-attempt cleanup remains limited to incomplete or
newly created build/root state; Keep2 pruning runs only after a successful update.

Each default refresh creates an ignored `artifacts/windows-desktop-test-build-<id>/` directory containing the published
ZIP and staging package referenced by its receipt (`sourceArchive`, `packageSource`). These are local evidence, not
source. Keep2 preserves dependencies of retained/protected receipts and prunes only verified obsolete owned jobs as
described above; historical unmanifested jobs are handled by the already authorized one-time cleanup scope.

The offline retention and updater contract checks are separate from a fresh build. Run all three commands;
the full updater suite and the targeted third-refresh mode are distinct invocations:

```powershell
# Retention helper contracts.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-desktop-build-retention.ps1
# Full updater contracts, including later migration, rollback and negative-package cases.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-desktop-test-build.ps1
# Targeted third-refresh retention regression; this mode returns early.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-desktop-test-build.ps1 -RetentionAfterThirdRefresh
```

All three run in the `LLPlayer Windows Avalonia Build & Test` CI job. They create synthetic package/filesystem fixtures
under ignored `artifacts/`; the real default installation and Desktop shortcut are not changed. The helper suite covers
Keep2 retention, protected references/processes, retained source dependencies and refusal of unsafe cleanup. The targeted
third-refresh mode checks successful-update pruning and exits before the later updater cases; it supplements the full
suite rather than replacing it.

The full updater suite exercises, through the production code path: direct
argument-free target/working-directory/per-build-icon COM and independent IShellLink/`ExtractIconEx` readback;
physical/canonical stored paths; a fresh schema-2 root; repeated refresh with current/fallback immutable payloads,
receipts and icons;
all three verified schema 1 shortcut forms (the PowerShell form through a subst logical alias, including
logical-to-physical record translation) and preserved legacy bytes; refusal of edited/foreign owner, pointer, receipt,
payload, icon, launcher and link identities; refusal of missing pointer identity fields, unbound/aliased legacy roots,
foreign install roots, foreign shortcuts, reparse points, redirected drive aliases, overlapping inputs, invalid icons,
plural/roll-forward/dev/non-matching deps runtimeconfig and broken/locked packages; static checks of the fresh publish
branch, clean-`HEAD` binding and publisher location; injected post-receipt cleanup for pre-existing empty and new roots;
compensating pointer/owner
failure rollback without temporary or staged link files; and the absence of player starts/stops, runtime installation or
persistent environment changes.

All three checks require Windows, writable fixture storage and ShellLink COM. Both updater modes require a registered
x64 .NET 10 runtime; the full updater suite additionally requires `subst` with a free drive letter. These prerequisites
also apply to the Windows CI runner. The real nominal/Codex-cache legacy-root regexes are exercised by the actual
migration, not by the fixture allowlist. The offline suites use synthetic/prebuilt fixtures: static source assertions
check fresh-publish wiring, but do not execute a real fresh publish, clean-`HEAD` deployment or player launch.
Only the real default refresh and its readback establish deployment evidence. Bind every reported suite result to the
tested commit and script SHA-256 values and keep the actual log; report the actual result without a fixed passing-case
count. Complete a spawned infrastructure/instruction review and final `/review` before handoff, and keep generated
payloads, receipts and logs out of source Git.

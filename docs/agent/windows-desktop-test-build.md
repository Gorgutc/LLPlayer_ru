# Windows desktop test build

The owner requested a blue Desktop shortcut named **LLPlayer Avalonia (Windows)** and a fresh build behind it after
every completed LLPlayer task/stage, including documentation and tooling work. This is a native Windows test shortcut
for the existing shared Avalonia app. Windows and Linux still use one Views/ViewModels/Themes tree and shared Core;
the shortcut does not replace the supported WPF product or establish desktop/model acceptance.

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

Use an already available .NET 10 SDK for publication and the existing registered system x64 .NET 10 runtime for the
direct app target. The updater accepts `-DotNetRoot` for an existing SDK directory and also resolves the process's
`DOTNET_ROOT` or the system installation. For example:

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
Migration records the physical and recorded legacy roots, the retained owner/current/shortcut byte hashes, the previous
build id and the launcher/icon identities in `owner.json` and in the new receipt. Foreign or edited ownership/link
content must be rejected rather than overwritten; the default refresh also refuses a schema-1-shaped root outside the
bound legacy locations. Record an owned migration in the update evidence and handoff.

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
shortcut. Earlier payloads remain available. Activation follows package validation, installation and shortcut/readback
checks and is a compensating transaction: it snapshots the previous shortcut/pointer/owner, then updates the link,
`current.json` and `owner.json`, and re-reads the link natively. Any exception restores the snapshots and refuses to
overwrite state that changed concurrently. This is not crash-atomic: if the process is killed between the link
replacement and the pointer write, the next run refuses the delivery because the shortcut no longer matches the
receipt. Recover by inspecting which retained build's receipt matches the installed link; the previous selection stays
in `current.json`. A failed refresh keeps a complete unselected build as evidence only inside an already managed root
(with a `current.json`); a failed first installation removes its new root, incomplete build directories are removed,
and an emptied `builds` directory is pruned, so the next attempt can run cleanly. Report a failed refresh from its
actual receipt and preserved prior state rather than claiming that a new payload became current.

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
framework requirement; no application execution on `10.0.7` is recorded. One separately authorized `--version` trace
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
reported as a fresh build of current `HEAD`. It is not the normal refresh command. In that mode only,
`-LegacyRootsForTests` may add explicit fixture roots to the bound schema-1 legacy-root allowlist for the offline
migration contracts; it is rejected without `-PackageDir` and does not skip shortcut discovery, handle-based physical
resolution or any owner/receipt/executable/icon/launcher/shortcut identity check. A normal refresh can therefore only
migrate the bound known legacy locations. Both inputs are test or recovery seams, not alternate production paths.

Do not edit the pointer by hand, delete older payloads or restart the player to recover from an update failure.
Keep the previous build/receipt and failed verification output. If the owner explicitly requests rollback, extract a
verified previous archive/package into separate staging **outside `InstallRoot`**, then select that staged package
through the updater's prebuilt mode. `-PackageDir` must not point inside the managed installation, including retained
`builds/<id>/payload/`. The resulting receipt shows only `buildMode=prebuilt-validated`, `sourceCommit=null`,
`sourceArchive=null` and `packageSource=<staging>`; it carries no automatic rollback label, so record the deliberate
rollback, the staged archive SHA-256 and its older provenance in the handoff and do not describe it as current-source
refresh. There is no rollback flag and no automatic cleanup of earlier selected payloads or app launch; incomplete or
newly created build/root state from a failed attempt is the only thing the updater removes.

Each default refresh retains its ignored
`artifacts/windows-desktop-test-build-<id>/` directory with the published ZIP and the staging package referenced by the
new receipt (`sourceArchive`, `packageSource`). That directory is local evidence, not source; it is not pruned
automatically, so a later cleanup needs an explicit owner request.

The offline updater contract checks are separate from a fresh build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-desktop-test-build.ps1
```

They build synthetic packages under ignored `artifacts/` and exercise, through the production code path: direct
argument-free target/working-directory/per-build-icon COM and independent IShellLink/`ExtractIconEx` readback;
physical/canonical stored paths; a fresh schema-2 root; repeated refresh with retained immutable payloads/receipts/icons;
all three verified schema 1 shortcut forms including logical-to-physical record translation and preserved legacy bytes;
refusal of edited/foreign owner, pointer, receipt, payload, icon, launcher and link identities; refusal of missing
pointer identity fields, unbound/aliased legacy roots, foreign install roots, foreign shortcuts, reparse points,
redirected drive aliases, overlapping inputs, invalid icons, plural/roll-forward/dev/non-matching deps runtimeconfig and
broken/locked packages; publisher capture filtering; compensating pointer/owner failure rollback without temporary or
staged link files; and the absence of process, runtime-install or persistent environment changes. The suite requires
Windows, `subst` with a free drive letter and a registered x64 .NET 10 runtime; it is not part of CI. The offline suite
always uses prebuilt fixtures, so it cannot prove the fresh-publish wiring, clean-`HEAD` binding or the actual
deployment; only the real default refresh and its readback do that. Bind every reported suite result to the tested
commit and script SHA-256 values and keep the actual log; report the actual result without a fixed passing-case count.
Complete a spawned infrastructure/instruction review and final `/review` before handoff, and keep generated payloads,
receipts and logs out of source Git.

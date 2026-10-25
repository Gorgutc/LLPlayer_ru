# Windows desktop test build

The owner requested a blue Desktop shortcut named **LLPlayer Avalonia (Windows)** and a fresh build behind it after
every completed LLPlayer task/stage, including documentation and tooling work. This is a native Windows test shortcut
for the existing shared Avalonia app. Windows and Linux still use one Views/ViewModels/Themes tree and shared Core;
the shortcut does not replace the supported WPF product or establish desktop/model acceptance.

## Refresh before handoff

Run from the repository on Windows after the task's final commit is in place:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\update-desktop-test-build.ps1
```

Normal refresh must omit `-PackageDir`: the updater freshly publishes the Windows Avalonia package through
`publish-avalonia.ps1`, validates it and records the actual clean Git `HEAD`. It checks that source identity before
and after publication and before activating the payload. Do not make later task edits and still describe that receipt
as the final build. Re-run refresh after the final source/docs/tooling commit changes `HEAD`.

Use the already available .NET 10 SDK/runtime. The updater accepts `-DotNetRoot` for an existing .NET directory and
also resolves the process's `DOTNET_ROOT` or the system installation. For example:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\update-desktop-test-build.ps1 -DotNetRoot 'C:\path\to\existing\dotnet'
```

This performs no .NET installer, global tool installation or persistent environment change. The launcher sets
`DOTNET_ROOT`/`DOTNET_ROOT_X64` only for its child process when an explicit runtime root is needed. If the required SDK,
runtime, native prerequisites, permissions or blue asset are unavailable, report the failure instead of installing or
changing the machine implicitly. Existing package validation and the separate WPF/Windows/Linux gates remain required
for their normal scopes.

Direct user read-only/no-build or other explicit execution constraints override this refresh rule. On a non-Windows
host, or when refresh cannot run, record **NOT_RUN** and the concrete reason. Do not claim that the Windows icon is
current. A build or shortcut receipt never counts as native GUI/DPI/keyboard, model/provider or GPU acceptance.

## Stable shortcut and retained payloads

The defaults use `%LOCALAPPDATA%\LLPlayer-Avalonia-TestBuild` and the Windows Desktop known folder. Optional
`-InstallRoot` and `-DesktopPath` override those locations for scoped verification; they do not change global settings.
The default icon source is `scripts/windows/assets/llplayer-avalonia-blue.ico` (`-IconPath` can select an explicit icon).

The local root contains `owner.json`, `launcher.ps1`, `current.json` and `builds/<id>/`. Each build keeps its own
`payload/`, `blue.ico` and `receipt.json`. The stable `LLPlayer Avalonia (Windows).lnk` points at the root launcher;
the launcher reads `current.json` to find the selected immutable payload. Its PowerShell helper is hidden. A user click
opens the player as a normal visible application.

The updater and its tests never launch the player, stop an existing player process or modify the original WPF
shortcut. Earlier payloads remain available. The current pointer changes only after package validation, installation
and shortcut/readback checks succeed; a failed refresh preserves the previous selected payload.

## Evidence and recovery

Before reporting success, retain the verification log/output and read back `current.json`, the selected build's
`receipt.json`, the installed file manifest and the Desktop link. In default fresh-publish mode, receipt `sourceCommit`
must equal the actual final repository `HEAD`; package/source identities and installed hashes must match the fresh
publish. Record the payload path, package hash and receipt identity in the handoff. A blue icon alone proves no build
identity, and an old receipt proves only its own checkpoint.

`-PackageDir` is an offline/prebuilt validation mode for contract tests or an explicitly requested republish of a
verified earlier payload. Its receipt uses `buildMode=prebuilt-validated` and `sourceCommit=null`; it must never be
reported as a fresh build of current `HEAD`. It is not the normal refresh command.

Do not edit the pointer by hand, delete older payloads or restart the player to recover from an update failure.
Keep the previous build/receipt and failed verification output. If the owner explicitly requests rollback, select a
verified previous `payload/` through the updater's prebuilt mode and label the resulting receipt as that deliberate
rollback, with its older provenance; do not describe it as current-source refresh. No automatic cleanup or app launch
is part of refresh or rollback.

The offline updater contract checks are separate from a fresh build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-desktop-test-build.ps1
```

Report their actual result without a fixed passing-case count. Complete a spawned infrastructure/instruction review
and final `/review` before handoff, and keep generated payloads, receipts and logs out of source Git.

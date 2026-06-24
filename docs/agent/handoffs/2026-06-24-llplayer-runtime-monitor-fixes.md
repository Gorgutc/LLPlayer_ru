# Handoff: LLPlayer runtime monitor fixes

Run time: 2026-06-24T15:47:35+03:00
Branch: `codex/llplayer-runtime-monitor-fixes`

## Update: 2026-06-24T16:06:46+03:00

The .NET SDK was installed at `C:\Program Files\dotnet\dotnet.exe` and verified as SDK `10.0.301`. The active Codex
process still did not see `dotnet` through its inherited `PATH`, so verification commands were run with
`C:\Program Files\dotnet` prepended to `PATH` for the command process.

Full verification initially exposed a compile error in `DubbingOutputPathBuilder.ResolveExistingRussianDubPath`: the
new `.OrderBy(...).FirstOrDefault(...)` chain needed LINQ. The follow-up branch
`codex/llplayer-dotnet-verify-fix` replaces that chain with `Directory.GetFiles` plus `System.Array.Sort`, avoiding a
new `using` and preserving deterministic file selection.

Verified after the follow-up fix:

- `git diff --check`
- `powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $env:Path = 'C:\Program Files\dotnet;' + $env:Path; & .\scripts\codex\verify-fast.ps1 }"`
- `powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $env:Path = 'C:\Program Files\dotnet;' + $env:Path; & .\scripts\codex\verify.ps1 }"`

Full gate result: `LLPlayer full verification completed.` Tests: `168` passed, `0` failed, `0` skipped.

## What Changed

- Fixed WPF dispatcher access around dubbed audio auto-load so `ExternalAudioStreams` is updated on the UI thread and stale selected media is ignored.
- Preserved `TranslationException.Kind` in `OpenAIBaseTranslateService.SendChatRequest` so retry/fallback logic can distinguish `EmptyResponse`, `NullContent`, and `Truncated`.
- Fixed batch ASR source-language policy for `PreferRussianAudio`, including the `tiny.en`/English-only model precedence case.
- Added scan policy for existing `.ru.srt` without `.ru.dub.*`, so default dubbing runs include already translated files that still need dubbed audio.
- Reworked batch dubbing ownership so `BatchSubtitlesDialogVM` owns a run-scoped `DubbingRenderer`; `BatchSubtitleProcessor` no longer disposes injected renderers.
- Added any-format `.ru.dub.*` detection and reuse through `DubbingOutputPathBuilder`.
- Added/updated guardrails for forbidden tracked artifacts, `audeering`, dubbing review routing, `LLPlayer/lib/7z.dll` publish checks, `dub_sidecar` publish files, and README `yt-dlp.exe` guidance.

## Why

These changes close monitor findings where runtime behavior could drift from frozen contracts: WPF thread affinity, translation fallback semantics, Russian-audio batch behavior, delayed dubbing of already translated subtitles, and release/package guard coverage.

## Verification Evidence

Passed:

- `git diff --check`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-plugin.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-doc-coverage.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-frozen.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\check-dub-licenses.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\audit-frozen.ps1`

Blocked:

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1`

Both fail in `scripts\codex\check-environment.ps1` with:

```text
dotnet CLI is not available on PATH.
```

Additional environment evidence:

- `where.exe dotnet`: `INFO: Could not find files for the given pattern(s).`
- `where.exe uv`: `INFO: Could not find files for the given pattern(s).`

## Review Notes

Spawned reviewer found one Important issue after the first pass: English-only ASR model precedence could still be overwritten by selected Russian track metadata. This was fixed in `BatchAsrTranscriber.ResolveReportedSourceLanguage` and covered by `ResolveReportedSourceLanguage_KeepsEnglishOnlyModelOverSelectedRussianTrack`.

## Known Blockers

- Full .NET restore/build/test is not verified until .NET 10 SDK is installed/restored on `PATH`.
- `dub_sidecar/uv.lock` is still absent because `uv` is not available locally; generate and commit it only if the lockfile remains part of the contract.
- DSP/audio assembly still runs through the Python sidecar; frozen contract expects bundled FFmpeg DSP boundary. Moving this needs a separate runtime design and test pass.

## Next Steps

1. Restore `dotnet` on `PATH`.
2. Run `verify-fast.ps1`, then `verify.ps1`.
3. Decide whether `dub_sidecar/uv.lock` remains required; if yes, install/use `uv` and generate the lock.
4. Plan a separate DSP migration from Python sidecar assembly to bundled FFmpeg/C# runtime.

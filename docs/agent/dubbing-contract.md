# Dubbing Contract

This document freezes the behavior boundaries of the **AI Dubbing** feature. It grows as phases
ship; sections marked **(Phase N — planned)** are advisory until delivered. Design rationale and the
phased plan: [`dubbing/dubbing-spec.md`](dubbing/dubbing-spec.md),
[`dubbing/dubbing-roadmap.md`](dubbing/dubbing-roadmap.md). Change a *shipped* boundary only when the
user explicitly asks to change that product decision.

## Scope & Position

- Dubbing produces a **pre-rendered, selectable Russian audio track** for a video. It does **not**
  replace, re-time, or mutate the original audio/video streams, and never runs on the realtime audio
  thread. It is an offline/batch render, analogous to batch subtitle generation.
- Dubbing is **additive and opt-in**. With dubbing disabled (the default, `GenerateDubbing=false`),
  ASR, translation, subtitle output, and playback are **byte-for-byte unchanged**.
- Local engines are the **default**; any cloud engine is an opt-in slot requiring the user's own API
  key (Phase 6 — planned).

## Output & Data

- The rendered track is written **beside the source video** as `video.ru.dub.flac` (FLAC avoids AAC
  encoder priming, preserving A/V sync). Format is configurable; if AAC/m4a is used, the ~21–45 ms
  priming shift must be accounted for. An existing non-empty `video.ru.dub.*` is detected, shown as
  done, and excluded from the default run unless overwrite is explicitly enabled (its own check
  against the `.ru.dub` path, independent of the `.ru.srt` path).
- **A/V sync invariant:** the dub is **one continuous audio stream spanning PTS 0..video_duration**;
  each line is placed at its source start on a full-length sidecar dub bed, **never concatenated**.
  This invariant is mandatory; regressing it silently desyncs the track.
- **Committed source vs runtime data:** `dub_sidecar/` (`server.py`, `pyproject.toml`, `uv.lock`) is
  **committed GPLv3 source**. The TTS venv (`DubEngine/`), model weights (`dubmodels/`), and the
  output (`video.ru.dub.*`) are **user runtime data, never tracked** (same policy as `Whisper/`,
  `whispermodels/`, `video.ru.srt`). `DO_NOT_PUSH.md` / `.gitignore` / `ship.ps1` must encode both
  sides so scan-time and run-time policy never diverge.

## Pipeline & Runtime Boundaries

- Dubbing reuses the **batch** pipeline. The render is an **optional step after** subtitle
  translation/write inside `BatchSubtitleProcessor.TranslateAndSaveAsync` (between the SRT write and
  the `Completed` report), gated on an **optional ctor-injected** `IDubbingRenderer? dubber` (null ⇒
  unchanged behavior) **and** `options.GenerateDubbing`. An optional
  `IDubbingVoiceAssignmentProvider?` can apply current-session sidebar `AssignedVoiceId` snapshots just
  before render; null preserves the single-voice behavior. ASR and translation results are never
  altered by the presence of dubbing.
- **GPU-no-overlap invariant:** the GPU TTS render must **not** run in the concurrent pipelined
  translation worker. When `GenerateDubbing` is on, processing **forces serialize-mode** (ASR →
  translate → dub → save, one file at a time) so a GPU ASR engine, a local-LLM translator, and the
  TTS sidecar never saturate the GPU at once (preserves the PR #33/d3bed9c contention guarantee). The
  existing Win32 idle gate also brackets the sidecar.
- **Neural work and dub-track assembly/DSP run in the Python sidecar.** C# owns orchestration and
  unit-tested isochrony placement math (`DubbingIsochrony`); the sidecar executes stretch, placement,
  duck/mix, and encode from the immutable run snapshot. The bundled FFmpeg remains the app/media
  native runtime, but it is not the current dubbing assembly backend.
- **Sidecar lifetime:** a **run-scoped `DubSidecarHost`** owns the python child, HttpClient, port,
  readiness probe, and watchdog. It is created from an **immutable `DubbingConfig` snapshot** at run
  start, loads the model **once**, and is stopped in the same `finally` that ends the run. **Exactly
  one sidecar instance process-wide** (run-scoped singleton) — batch and single-file reuse one GPU
  process, never two. Config changes require an explicit restart, never live mutation. One-shot
  CLI-per-segment is forbidden (model cold-start per call).
- **Port & readiness:** the sidecar binds port 0 and prints `DUB_PORT=NNNNN` on stdout; C# launches
  it with `System.Diagnostics.Process` and reads the port from `OutputDataReceived` (the `DUB_PORT=`
  marker, raced against `WaitForExitAsync`), then polls `/health` with a bounded, generous timeout
  that surfaces a recoverable error (not a crash) on failure. (Raw `Process` rather than CliWrap so
  the child handle is available to assign to the Job Object.)
- **Orphan safety (mandatory):** the python child is reaped on **parent-process death**, not only on
  graceful shutdown — it is placed in a Windows **Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`**
  and the sidecar **self-terminates if its parent PID disappears**. The host is **run-scoped**: a
  `await using DubSidecarHost` per render disposes it (graceful `/shutdown`, then `Kill(entireProcessTree)`,
  then close the Job handle, then delete its temp work dir) in the run's `finally`/cancel path, and a
  failed start disposes it too. A cancel (`_cts.Cancel`) cancels the in-flight HTTP synth and the
  renderer best-effort-deletes any partial/unwanted output. Batch tray KeepAlive brackets the sidecar
  (it is inside the `IsRunning` window). There is no app-lifetime daemon, so nothing dub-specific is
  wired into `App.OnExit`.
- **Trust model:** the sidecar binds **loopback only (127.0.0.1)** and has **no auth** — it assumes
  a single trusted local user (same model as the existing faster-whisper process). Any future
  remote/cloud driver MUST add a per-run token + request path validation before accepting non-local
  callers.
- Russian text is run through a **mandatory stress/homograph normalization** pass before synthesis
  (graceful-degrade to raw text if the module is unavailable).
- **Isochrony (MVP):** C# computes a capped `atempo` factor (pitch-preserving intent; one factor per
  clip) + **drift-reset at every 300 ms gap**. The sidecar executes the stretch/resample/mix:
  PyAV decodes the source audio, `librosa` applies time-stretch/resample when needed, `numpy` places
  clips on the dub bed and applies envelope ducking, and `soundfile` atomically writes the final
  track. A line may run long / start late and resync at the next pause as a documented limitation,
  not a bug.
- **Track exposure:** the rendered track is surfaced through the existing external-audio mechanism —
  it appears under the existing **Audio ▸ External** menu (`PopupMenu.xaml`, bound to
  `Playlist.Selected.ExternalAudioStreams`). `DubbedAudioAutoLoader` adds it on player-open via the
  documented `AddExternalStream` API, **marshalling the `ObservableCollection` mutation to the UI
  dispatcher**. Users can also open it manually via the existing external-audio menu. (A plugin
  provider à la `OpenSubtitles : ISearchLocalSubtitles` is the cleaner long-term form — Phase 1.)

## MVP (Phase 0 — shipped scope)

- A single bundled **preset CosyVoice2 Russian voice**; **no** diarization, cloning, source
  separation, or voice bank. The whole dub is one narrator over the original **ducked** during dubbed
  spans — a single-voice Russian voiceover. The C# slice and committed `dub_sidecar/server.py` ship
  as a **compiled, frozen-safe contract artifact**; the neural render is owner first-run (see
  roadmap DoD). A `--mock` sidecar mode allows deterministic off-GPU validation of the C# orchestration +
  sidecar assembly path.

## Voices (Phase 1–3 — planned)

- **Phase 1 voice bank (shipped, additive):** the selectable preset bank is mirrored in C# as the
  GPU-free `VoiceBankResolver.BuiltIn` (`FlyleafLib/MediaPlayer/Dubbing/VoiceBankResolver.cs`), kept in
  **lockstep** with `dub_sidecar/server.py`'s `VOICES` (a unit test pins the ids/order). The picker
  (batch dialog + **Settings ▸ Subtitles ▸ Dubbing**) binds this static bank and **never starts the
  sidecar** to enumerate voices; the engine `ITtsService.GetVoicesAsync` stays the phase-2 live-discovery
  seam, surfaced by `VoiceBankResolver.ResolveAsync` (fail-soft; built-in metadata wins on id collision;
  not yet wired into the UI). The chosen voice writes `DubbingConfig.DefaultVoiceId` (one voice for the
  whole dub); `DefaultVoiceId` is **normalized on set** (trim, blank/null -> built-in default
  `ru-preset-1`, known built-in ids -> canonical casing) so a hand-edited config value still matches the
  picker entries and the engine voice ids (the ComboBox never blanks).
  This single voice is the **default** for every line; **per-line manual override is phase 2a** (below) and
  per-speaker (diarization-driven) selection remains phase 2/3. The renderer reads `DefaultVoiceId` live at run
  start, so no batch-snapshot coverage is required.
- **Phase 2 custom voice ids (shipped, additive):** the user can register extra voice ids
  (`DubbingConfig.CustomVoiceIds`, default empty → byte-identical) via **Settings ▸ Subtitles ▸ Dubbing ▸
  Custom voice IDs** (list + Add/Remove). Both pickers (Settings + batch dialog) merge them after the
  built-in bank via `VoiceBankResolver.ForConfig(selected, customVoiceIds)`; the persisted list is normalized
  on set (trim, skip blanks/nulls, dedup by Id OrdinalIgnoreCase, declared order), and the selected id stays
  selectable. The id is sent to the engine
  **verbatim** at synth time (`DefaultVoiceId` → `TtsRequest.VoiceId`); LLPlayer does not validate it
  against a running engine and **still never starts the sidecar** to populate the picker. Persisted in
  `LLPlayer.PlayerConfig.json` under `Subtitles.DubbingConfig.CustomVoiceIds` (absent-defaulting, no
  migration). The live-discovery refresh (`ResolveAsync`) remains unwired (it would require starting the GPU sidecar).
- **Phase 2a per-line voice override (shipped, additive):** the user can assign a specific dub voice to an
  individual subtitle line from the **sidebar per-row voice button** (a left-click context menu of the same
  GPU-free voice bank — built-in presets + `CustomVoiceIds` — plus a leading **"Use default voice"** entry that
  clears the override). The choice is stored on the cue as the new per-cue field `SubtitleData.AssignedVoiceId`
  (beside `Language`/`SpeakerId`; copied in `Clone()` and at both re-segmentation split sites). The renderer
  threads it through `DubbingRenderer.BuildLines` → `DubbingLine.VoiceId` and synthesizes each line with
  `DubbingRenderer.ResolveVoiceId(line.VoiceId, DefaultVoiceId)` — a blank/absent override falls back to the run's
  default voice, so a dub with **no** assignments is byte-identical to the single-voice render (the id is sent to
  the engine verbatim, exactly as `DefaultVoiceId`). The override is **current-session / in-memory only**:
  `SubtitleData` is never serialized to SRT or config, but `BatchSubtitlesDialogVM` snapshots overrides from the
  currently open local media and passes them through `DubbingVoiceAssignmentMap` /
  `IDubbingVoiceAssignmentProvider`; when a batch job's media path and cue millisecond timings match, the
  provider applies them to both fresh ASR/translation subtitles and existing `.ru.srt` subtitles just before
  render. After restart, with no matching open media, or with mismatched timings, render falls back to
  `DefaultVoiceId` for every line (unless the opt-in persistence below restored them). Per-speaker
  (diarization-driven) auto-assignment remains phase 2/3 (needs F-03).
- **Phase 2a persistence (opt-in, F-16, since 0.3.37):** the default-OFF toggle `Subtitles.PersistPerLineVoices`
  mirrors per-line overrides to a companion JSON file beside the media — `video.ru.voices.json` (deliberately NOT
  a `.ru.dub.*` name, so the dub-detection glob `{name}.ru.dub.*` does not mistake it for a rendered dub). Pure
  logic lives in `DubbingVoiceAssignmentStore` (path builder + tolerant JSON round-trip + atomic temp-file save +
  `LoadMap`). Assigning or clearing a sidebar voice writes the file atomically (`SaveAtomic`; clearing every
  override deletes it); opening the media restores the saved voices onto the loaded/ASR cues (`Subtitles.Load` /
  `EnableASR`, fill-empty so a fresh in-session edit still wins); and a batch dub layers a
  `DiskVoiceAssignmentProvider` UNDER the current-session snapshot (`CompositeVoiceAssignmentProvider`) so any
  batch file with a companion dubs with its saved voices, not just the open one. Matching uses the same
  SRT-millisecond `[start,end]` key, so a re-segmented/edited cue that no longer matches is silently skipped; it
  never throws on a missing/corrupt/locked file. Default OFF → byte-identical: nothing is written, restored, or
  read. The companion file is user runtime data (git-ignored and rejected from release packages, like `*.ru.dub.*`).
- **Hybrid:** by default, diarize speakers and **clone each speaker's timbre** into Russian
  (CosyVoice2 zero-shot from a per-speaker reference clip), preserving gender; **any speaker can be
  overridden** with a preset bank voice. Gender uses a license-free F0 heuristic + manual override.
- Non-commercial engines (XTTS-v2, F5-TTS-Russian) are exposed **only as user-installed advanced
  backends** behind the same `ITtsService`; their weights are never bundled.

## Source Separation (Phase 4 — planned)

- Optional, behind a toggle. Separation **code** may ship (MIT); **weights are
  download-on-first-run, user opt-in with a no-clear-license notice, never silently auto-fetched and
  never bundled** (the weights are effectively unlicensed). When enabled, the dub mixes over the
  preserved music/SFX bed instead of ducking.

## Cloud Slot (Phase 6 — planned)

- `ITtsService` cloud providers (ElevenLabs primary; Azure/Cartesia) require the user's **own paid
  API key**, **default to preset Russian voices**, and must **reject any source-speaker reference
  clip in code** (cloud cloning of arbitrary speakers violates provider consent ToS). No
  pre-generated cloud audio is ever bundled.

## Configuration

- Engine/model settings: `Config.Subtitles.DubbingConfig` (player config). Batch toggle:
  `AppConfigBatchSubtitles.GenerateDubbing` (`LLPlayer.Config.json`). All keys **additive and
  absent-defaulting**; any future default change is version-gated and migrated once, per
  `config-data-contract.md`. Phase 0 needs **no** new JSON converter; a typed interface converter
  (mirror `ITranslateSettings`) is required only when an interface-typed `ITtsSettings` lands
  (Phase 6).

## Licensing & Attribution (GPLv3)

- LLPlayer is **GPLv3** and ships a **GPL FFmpeg** build. The legal test for dubbing components is
  **GPLv3 compatibility + separate-process aggregation**, not "permissive enough to redistribute".
  The Python sidecar is a **separate process over localhost HTTP (mere aggregation)**, which keeps
  the proprietary CUDA/torch stack legal beside the GPLv3 app. The C# `Dubbing/*` and `dub_sidecar/`
  are GPLv3 Corresponding Source.
- **Tiers:** *bundle (GPLv3-compatible)* — CosyVoice2 (Apache-2.0), pyannote community-1 (CC-BY-4.0
  +attribution), pyannote.audio/faster-whisper/RVC/Silero-base/audio-separator-code (MIT), WhisperX
  (BSD-2), uv. *download-on-first-run, opt-in* — separation **weights**. *user-installed only* —
  XTTS-v2, F5-TTS/-Russian, Silero non-base. *excluded* — IndexTTS2, NeMo Sortformer, audeering
  gender, NLLB-200, `ttsfrd`.
- A **build gate** (`scripts/codex/check-dub-licenses.ps1`, run by `verify-fast`) fails if any
  non-commercial / unvetted package (TTS/coqui, xtts, f5-tts, nemo, indextts, nllb, ttsfrd) appears
  in the committed `dub_sidecar` manifests — `pyproject.toml` dependency lines and the committed
  `uv.lock`.
- A full **NOTICES** screen ships covering the GPLv3 app + FFmpeg (with §6 written offer now covering
  `dub_sidecar/`) and every bundled component's attribution. The pre-existing FFmpeg GPL
  source-offer obligation must be verified by the owner and **not widened** by dubbing.

## Manual Checks When Touched

Unit tests do not cover GPU synthesis, the sidecar lifecycle, real media muxing, or Russian audio
quality. See `manual-smoke-matrix.md` for: first-run provisioning UX; sidecar launch/shutdown/
crash-restart and **no-orphan-on-kill (VRAM freed)**; ear-test of CosyVoice2 Russian on real
content; dub track appears in **Audio ▸ External**, is selectable, and plays in **sync at 0:00 /
mid / near end**; ducking audible; cancellation mid-dub leaves no orphan or partial file; and the
**published-.exe launch-test on the real RTX 5090**.

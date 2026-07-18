# AI Dubbing — Architecture Specification

Status: **Phase 0 / MVP scoped for implementation** (architecture approved by a 5-lens adversarial
design review; corrections from that review are folded in here).
Owner-decided constraints locked 2026-06-23. Backed by the `dubbing-research` study (15 agents,
adversarially verified) — see [`research-summary.md`](research-summary.md). Phased roadmap + goal:
[`dubbing-roadmap.md`](dubbing-roadmap.md). Frozen behavior boundaries:
[`../dubbing-contract.md`](../dubbing-contract.md).

> **Design-review note (load-bearing):** LLPlayer is licensed **GPLv3** (`LICENSE`) and already
> ships a **full GPL FFmpeg** build (`avcodec-62`/`avfilter-11`/`avutil-60`, with
> librubberband/x264/x265/vidstab linked). This re-frames the licensing logic below: the test is
> **GPLv3 compatibility + separate-process aggregation**, not "permissive enough to redistribute",
> and the time-stretch filter choice is a **quality** decision, not a licensing one.

---

## 1. Goal

Today LLPlayer translates *any* source language into **Russian subtitles** while keeping the
original speech. This feature adds **AI dubbing**: translate the spoken audio into **Russian
speech**, rendered as a **pre-generated, selectable audio track**, preserving prosody and speaker
gender, with per-speaker timbre cloning by default and a curated voice bank to override any
speaker — **local-first**, with an optional pluggable cloud slot.

## 2. Owner-decided constraints

| Decision | Choice | Consequence |
| --- | --- | --- |
| Engine source | **Local-first**, optional cloud plugin | Bundle only GPLv3-compatible local models; cloud is a user-key slot (Phase 6). |
| Playback model | **Pre-render** a selectable track (not live) | Reuse the batch pipeline + the existing external-audio track mechanism; no audio-thread risk. |
| Voice strategy | **Hybrid**: diarize + clone per speaker, override from a bank | Cloning is the default path; bank voices are the override (and the MVP's only voice). |
| This session | **Research + spec + MVP slice + .exe** | Ship a compiled, frozen-safe vertical slice (Phase 0); the neural render is owner first-run. |

## 3. Recommended stack (license-checked against GPLv3)

| Concern | Choice | Licensing under GPLv3 |
| --- | --- | --- |
| TTS (default, bundled) | **CosyVoice2-0.5B** | Apache-2.0 (GPLv3-compatible), ungated, Russian, cross-lingual zero-shot cloning + duration control. Runs in the sidecar (separate process). |
| Russian stress/prosody | **Silero** stress + homograph normalization (MIT base) | **Mandatory pre-synthesis** (graceful-degrade to raw text if unavailable). |
| Diarization (Phase 2) | **pyannote.audio 4.x** `speaker-diarization-community-1` | Lib MIT; model CC-BY-4.0 (attribution in NOTICES). HF-gated first download. |
| Gender (Phase 2) | **F0 median-pitch heuristic** + manual override | License-free (librosa/torchaudio). |
| Source separation (Phase 4) | **Mel-Band RoFormer** / **Demucs** via `python-audio-separator` | Code MIT; **weights download-on-first-run, user opt-in with a no-clear-license notice** (weights are effectively unlicensed — download does not fully cure this). |
| Word timing | faster-whisper word timestamps / **WhisperX** (BSD-2) | Pin a tested triple; diarization-handoff smoke test (Phase 2). |
| Time-stretch | C# capped `atempo` factor, executed by sidecar `librosa.effects.time_stretch` | Quality choice for small corrections; current backend is sidecar DSP. |
| .NET integration | **Long-lived localhost HTTP Python sidecar** | Separate process = mere aggregation → keeps the proprietary CUDA/torch stack legal beside the GPLv3 app. |
| Cloud slot (Phase 6) | **ElevenLabs** primary; Azure/Cartesia alt | User's own paid key; **preset voices only**; impls must **reject any source-speaker reference clip** (enforced in code). |

**User-installed only (not bundled):** XTTS-v2 (CPML non-commercial), F5-TTS/-Russian (CC-BY-NC/-SA),
Silero non-base — non-commercial restriction is incompatible with redistributing them inside the
program; the user installs them at their own choice (still a separate-process sidecar). **Excluded:**
IndexTTS2, NeMo Sortformer, audeering gender, NLLB-200. **Exclude `ttsfrd`** from the sidecar lock
(unvetted license) — use WeTextProcessing/Silero normalization.

## 4. End-to-end pipeline (full vision)

```
video ─▶ [1] extract audio (ffmpeg, bundled)
       ─▶ [2] (Phase 4) vocal/music separation ─▶ dialogue stem + music/SFX bed
       ─▶ [3] ASR + word timestamps (faster-whisper, existing)
       ─▶ [4] (Phase 2) diarization (pyannote) ─▶ per-segment speaker labels
       ─▶ [5] (Phase 2) per-speaker reference clips (ffmpeg) + gender (F0)
       ─▶ [6] duration-budgeted translation to Russian (LM Studio, existing)
       ─▶ [7] Russian stress/normalization (Silero)            ← mandatory, graceful-degrade
       ─▶ [8] per-segment TTS (CosyVoice2: cloned timbre, or bank voice override)
       -> [9] C# isochrony fit (TTS duration ctrl -> pause-spill -> capped atempo factor -> drift-reset)
       -> [10] sidecar assemble: place each clip onto a full-length silence bed -> continuous dub stream
       -> [11] sidecar mix: (Phase 4) over music bed; (MVP) envelope-duck the original under the dub
       -> [12] sidecar encode -> video.ru.dub.flac/m4a -> external audio track (selectable)
```

Substrate: a single long-lived localhost HTTP Python sidecar runs the **neural** steps and current
dub-track DSP/assembly. C# owns orchestration and pure isochrony placement math; the sidecar decodes,
stretches, places, ducks/mixes, and encodes the final track.

### 4.1 A/V sync invariant (load-bearing — B5)

The dub is **one continuous audio stream spanning PTS 0..video_duration**: each synthesized line is
placed at its source start on a full-length sidecar dub bed, never concatenated (concatenation
desyncs everything after line 1). Encode the dub as **WAV or FLAC** (no encoder priming delay); if
AAC/m4a is chosen, account for the ~21-45 ms priming shift (and `Config.Audio.Delay` is the runtime
escape hatch). Smoke-test sync at 0:00, mid-file, and near the end.

### 4.2 Ducking & assembly (B6, I6)

- **Ducking** uses a sidecar-computed envelope from the dub bed - one mix pass, no per-span
  filtergraph enumeration (the design avoids generating a giant operation per line for thousands of
  spans).
- **Sample-rate/channels:** CosyVoice2 emits 24 kHz mono; films are typically 48 kHz stereo. The
  sidecar decodes the source audio via PyAV, resamples clips via `librosa` when needed, preserves the
  source sample rate/channel count for the original bed, and writes the final track atomically via
  `soundfile`.

### 4.3 Isochrony — MVP behavior (I7)

Russian runs ~10–30 % longer; a 1.15× atempo cap cannot absorb that alone. MVP isochrony is
**capped atempo + drift-reset only**: synthesize, fit with capped `atempo`; if a line still
overflows, let it run long / start the next late, and **hard-reset accumulated drift at the next
≥300 ms gap** so error never compounds. The drift-reset is a **Phase-0 pure, unit-tested function**.
This "may rush/lag a line, resync at the pause" result is a **documented known limitation**, not a
bug; full layered isochrony (pause-spill, duration-budgeted translation) is Phase 5.

## 5. MVP (Phase 0) — what ships this session

A single-narrator Russian *закадровый* voiceover, delivered as a **compiled, frozen-safe slice**
(the neural render runs on the owner's machine at first-run — see roadmap DoD).

Flow: existing faster-whisper ASR + LM Studio translation → timed Russian lines → (each line Silero
stress-normalized) → synthesize the whole dub with **one bundled preset CosyVoice2 Russian voice**
→ capped `atempo` fit + drift-reset → sidecar placement onto a full-length silence bed →
envelope-duck the original under the dub → encode `video.ru.dub.flac` → selectable external audio
track.

No diarization, cloning, separation, or voice bank.

## 6. Code architecture

### 6.1 FlyleafLib (media engine)

```
FlyleafLib/MediaPlayer/Dubbing/
  ITtsService.cs            // cheap, per-file; holds a reference to the sidecar host
  TtsServiceType.cs         // enum { LocalCosyVoice, ElevenLabs, AzureSpeech, Cartesia, XttsV2Local, F5Local }
  TtsServiceFactory.cs      // lazy, provider-based (mirror TranslateServiceFactory)
  TtsModels.cs              // TtsRequest(text, voiceId, refClipPath?, targetDurationMs, gender), TtsVoice, TtsResult
  DubSidecarHost.cs         // RUN-SCOPED: owns python child, HttpClient, port, readiness, watchdog, Job Object
  LocalCosyVoiceTtsService.cs   // ITtsService over DubSidecarHost (per-file, cheap)
  DubbingRenderer.cs        // orchestrates synth -> atempo+drift-reset -> sidecar assemble/duck/mix/encode
  IDubbingRenderer.cs
  DubbingIsochrony.cs       // PURE: atempo factor clamp + drift-reset-at-pause (unit-tested)
  DubbingOutputPathBuilder.cs   // BuildRussianDubPath(media) → "video.ru.dub.flac"  (sibling of SubtitleOutputPathBuilder)
```

**Sidecar lifetime (B3, B4, I3, I4):**
- **`DubSidecarHost` is run-scoped, not per-file and not an app daemon.** It starts at the
  beginning of a batch run (or on-demand for single-file), loads the model **once**, and is stopped
  in the same `finally` that ends the run (`BatchSubtitlesDialogVM` run teardown). **Exactly one
  instance process-wide** (run-scoped singleton/lock) so batch + single-file never double VRAM.
- Built from an **immutable `DubbingConfig` snapshot** captured at run start (mirrors the batch ASR
  config-snapshot precedent); a config change requires an explicit restart, never live mutation.
- **Port:** python binds port 0 and prints `DUB_PORT=NNNNN` on stdout; C# launches it with raw
  `System.Diagnostics.Process` so the child handle can be assigned to the Job Object, reads the port
  from `OutputDataReceived`, then runs a bounded `/health` poll with a generous timeout, progress UI,
  and a recoverable error (not a crash) on timeout.
- **Orphan safety:** the python child is placed in a **Windows Job Object with
  `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`** (the OS reaps it if LLPlayer dies/crashes/is killed), and
  the sidecar **self-terminates if its parent PID disappears**. Teardown is wired into the
  batch-run `finally`; there is no app-lifetime daemon.

**Batch integration (I1, I9):**
- Hook is an **optional ctor param** `IDubbingRenderer? dubber = null` on `BatchSubtitleProcessor`
  (ctor injection — the type has no field injection). The render runs inside `TranslateAndSaveAsync`
  **between** the SRT write and the `Completed` report, guarded by
  `dubber is not null && options.GenerateDubbing`, with its **own** `.ru.dub` OutputExists /
  OverwriteExisting check (not piggybacking the `.srt` path).
- **GPU-no-overlap invariant (I1):** the GPU TTS render must **not** run in the concurrent pipelined
  translation worker (that would re-create the ASR↔translate GPU contention PR #33/d3bed9c removed).
  When `GenerateDubbing` is on, the processor **forces serialize-mode** (ASR → translate → dub →
  save, one file at a time). Idle-gating (the existing Win32 active-user check) also brackets the
  sidecar.

### 6.2 LLPlayer (app)

```
LLPlayer/Services/
  DubbingEngineProvisioner.cs   // thin shell-out to the `uv` binary (documented command sequence) + manual fallback
  DubbedAudioAutoLoader.cs      // on player-open: if video.ru.dub.* exists → AddExternalStream (UI-dispatcher marshalled)
LLPlayer/Views(+VM)/
  DubbingEngineDownloadDialog   // first-run provisioning UI
  SettingsSubtitlesDubbing      // engine/model/voice/ducking/atempo settings (mirror SettingsSubtitlesASR)
```

- **Auto-load (I2, M1):** an existing **external-audio menu already exists** —
  `PopupMenu.xaml` "Audio ▸ External" is bound to `Playlist.Selected.ExternalAudioStreams`. The dub
  track therefore appears there automatically once added. `DubbedAudioAutoLoader` subscribes to
  player-open, checks for `video.ru.dub.*` beside the media, and adds it via the documented
  `AddExternalStream` API. The `ExternalAudioStreams` mutation is a UI-bound `ObservableCollection`
  → **marshal the add to the UI dispatcher** (the file-existence check may run off-thread). (There is
  no pre-existing local external-*audio* auto-detect; the cleaner long-term form is a plugin
  provider à la `OpenSubtitles : ISearchLocalSubtitles` — noted as a Phase-1 refinement so the MVP
  stays out of the frozen plugin layer.) Users can also open the dub manually via the existing
  external-audio menu, so auto-load is convenience, not load-bearing.
- **Batch toggle:** `AppConfigBatchSubtitles.GenerateDubbing` (additive, default `false`) + a
  checkbox in `BatchSubtitlesDialog`; the renderer instance is wired in `BatchSubtitlesDialogVM`.

### 6.3 Python sidecar (committed as a contract artifact)

```
dub_sidecar/                    // OUR GPLv3 code — committed; heavy deps provisioned by uv on first run
  server.py                     // stdlib http.server, binds port 0, prints DUB_PORT, loads CosyVoice2 once;
                                //   --mock mode synthesizes a tone/silence of target duration (no heavy deps)
  pyproject.toml / uv.lock      // pinned: torch>=2.7.0+cu128, soundfile/librosa/numpy/PyAV; NO fastapi/uvicorn, NO ttsfrd, NO NC pkgs
```

Endpoints (MVP): `GET /health` → `{ready:true}`; `POST /synthesize {text, voice_id,
target_duration_ms}` → `{wav_path}` (temp WAV path, not base64). The sidecar self-terminates if its
parent PID disappears. A **`--mock`** flag (tone/silence of the requested duration, pure-stdlib)
exists so the **entire C# pipeline + sidecar assembly can be validated deterministically off-GPU**
(in tests / dev), while the real CosyVoice2 is a drop-in on the same HTTP contract.

## 7. Configuration (additive, absent-defaulting)

Player config (`LLPlayer.PlayerConfig.json`), new `Config.Subtitles.DubbingConfig` (mirrors
`FasterWhisperConfig`): `TtsServiceType` (default `LocalCosyVoice`), `UseManualEngine`/
`ManualEnginePath`, `Model` (`cosyvoice2-0.5b`), `DefaultVoiceId` (`ru-preset-1`), `DuckingPercent`
(15), `AtempoMin`/`AtempoMax` (0.9/1.15), `StressNormalization` (true), `OutputFormat` (`flac`).

App config (`LLPlayer.Config.json`), `AppConfigBatchSubtitles.GenerateDubbing` (default `false`).

**Serialization (M7):** `GenerateDubbing` (bool) + scalar/enum `DubbingConfig` need **no** new JSON
converter for Phase 0. A typed interface-converter (mirror `ITranslateSettings`) becomes mandatory
only when an interface-typed `ITtsSettings` lands in Phase 6 — flagged so it is neither added early
nor forgotten then. All keys absent-defaulting; any future default change is version-gated.

## 8. Output & data (M6)

- Output: **`video.ru.dub.flac`** beside the source video (sibling of `video.ru.srt`; FLAC avoids
  AAC priming — §4.1). An existing non-empty `.ru.dub.*` is detected/excluded unless overwrite is on.
- **Committed source:** `dub_sidecar/` (`server.py`, `pyproject.toml`, `uv.lock`).
- **Never committed:** `DubEngine/` (venv), `dubmodels/` (weights), `video.ru.dub.*` (user output),
  `video.ru.voices.json` (per-line dub voice map, Phase 2a).
  Update `DO_NOT_PUSH.md` / `.gitignore` / `ship.ps1` strict-cleanup in the same change.

## 9. Licensing (GPLv3) — rules that must hold every phase

- **Legal test = GPLv3 compatibility, not "permissive".** The sidecar is a **separate process**
  communicating over localhost HTTP → **mere aggregation / arms-length IPC**, which is what keeps
  the proprietary CUDA/torch stack legal next to the GPLv3 app. The new C# `Dubbing/*` and
  `dub_sidecar/` are themselves **GPLv3 Corresponding Source** (shipped via the public repo).
- **Bundle (GPLv3-compatible):** CosyVoice2-0.5B (Apache-2.0); pyannote community-1 (CC-BY-4.0 +
  attribution); pyannote.audio (MIT); WhisperX (BSD-2); faster-whisper (MIT); python-audio-separator
  **code** (MIT + UVR/Anjok07 attribution); RVC (MIT); Silero base (MIT); uv (Apache/MIT).
- **Download-on-first-run, user opt-in (M2):** separation **weights** (effectively unlicensed —
  download reduces but does not eliminate exposure; gate behind a no-clear-license notice, no silent
  auto-fetch). **Never bundle / user-install only:** XTTS-v2, F5-TTS/-Russian, Silero non-base.
- **NOTICES bundle (I8):** ship a full third-party-NOTICES screen — GPLv3 (app + FFmpeg + x264/x265/
  vidstab/librubberband) with the §6 written offer **now covering `dub_sidecar/`**, plus Apache-2.0
  (CosyVoice2/uv), CC-BY-4.0 (pyannote, credited), BSD-2 (WhisperX), MIT (faster-whisper/RVC/
  Silero-base/audio-separator + UVR). **Flag to owner:** verify the *existing* release already
  satisfies the FFmpeg GPL source-offer; if not, that is a pre-existing gap dubbing must **not**
  widen.
- **Build gate (B2 — code):** the verifier fails the build if any non-commercial package (xtts,
  tts-coqui, f5-tts, nemo, audeering, ttsfrd) appears in the bundled dub venv `uv.lock`.
- **Cloud:** user-supplied paid keys only; never bundle cloud audio; cloud cloning is consent-gated
  to the user's own voice → preset voices only; cloud impls **reject `refClipPath`** in code (M3).
- **CUDA** delivered by pip wheels per-user (NVIDIA EULA-clean); never bundle CUDA DLLs.

## 10. Key risks

| Risk | Sev | Mitigation |
| --- | --- | --- |
| RTX 5090 sm_120 / cu128; faster-whisper INT8 crashes on sm_120 | High | Pin `torch>=2.7.0+cu128` (stable); force ASR `compute_type=float16`; **launch-test on the real 5090** (owner first-run). |
| Orphan multi-GB GPU python process on crash/kill | High | Job Object `KILL_ON_JOB_CLOSE` + parent-PID self-terminate + run-finally teardown. |
| A/V desync of a separately-rendered track | High | One continuous sidecar-assembled stream (not concat); FLAC (no priming); sync smoke at 0/mid/end. |
| Assembly/mix operation scales with many cues | High | Sidecar assembly uses array operations instead of generating a per-cue ffmpeg filtergraph. |
| Batch GPU contention regression | High | `GenerateDubbing` forces serialize-mode; dub never in the pipelined translation worker; idle-gate sidecar. |
| Non-redistributable weights | High | Bundle only Apache/CC-BY/MIT; separation weights first-run + opt-in notice; user-install XTTS/F5; lockfile gate. |
| CosyVoice2 Russian quality / accent | High | Mandatory Silero stress; voice-bank override; owner A/B ear-test vs user-installed XTTS/F5 before locking default. |
| MVP isochrony can't fully fit Russian | Med | Documented limitation: capped atempo + drift-reset-at-pause; full budget is Phase 5. |
| pyannote HF-gating (Phase 2) | Med | Guided first-run token+terms then offline clone; deferred to Phase 2 so MVP is gating-free. |

## 11. Testing & gates

- **Unit (xUnit, off-GPU, deterministic):** `DubbingOutputPathBuilder`; `DubbingIsochrony`
  (atempo clamp, drift-reset-at-pause); `DubbingConfig` defaults; `DubbedAudioAutoLoader` path
  logic. The `--mock` sidecar enables an optional end-to-end sidecar-assembly smoke with a generated tone.
- **Gates:** `dotnet build --no-restore -warnaserror .\LLPlayer`;
  `dotnet build --no-restore -warnaserror .\Plugins\YoutubeDL`;
  `dotnet test --no-restore -warnaserror .\FlyleafLibTests`; `verify-fast`/`verify` (frozen)
  **incl. the new lockfile NC-package gate**;
  multi-agent `/review` (close Critical/Important).
- **Owner first-run acceptance (NOT a merge gate):** provisioning download; sidecar boot; CosyVoice2
  Russian **ear-test** on real content; dub track appears in Audio ▸ External, plays, ducking
  audible; cancellation mid-dub leaves no orphan/partial; published-.exe launch-test on the 5090.

## 12. Open decisions for the owner

1. **Russian cloning quality bar** — ear-test CosyVoice2 vs user-installed XTTS-v2/F5-Russian
   (Phase 3).
2. **Commercial distribution?** — under GPLv3 the user-install boundary for XTTS/F5 is the safe
   line; CosyVoice2 stays the bundled default either way.
3. **Voice bank source** (Phase 1) — CosyVoice2-rendered presets vs add Silero MIT base voices.
4. **Bundle vs first-run download for CosyVoice2 weights** *(needed before provisioner work — I10)* —
   default here is first-run download (consistent with faster-whisper engine UX; the cu128 venv is
   multi-GB regardless).
5. **pyannote HF-gated first-run UX** (Phase 2) — or a non-gated diarizer.
6. **Isochrony default** — TTS rate control vs atempo; cap aggressiveness.
7. **Cloud slot priority** (Phase 6) — ElevenLabs vs Azure vs Cartesia; confirm preset-only.
8. **Full-offline install required?** *(needed before provisioner work — I10)* — or first-run
   internet download acceptable (uv approach).

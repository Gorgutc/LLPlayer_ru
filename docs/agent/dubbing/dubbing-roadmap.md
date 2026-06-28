# AI Dubbing — Goal & Phased Roadmap

Companion to [`dubbing-spec.md`](dubbing-spec.md). Fulfils the `/goal` + `/sprint-planning` intent
for the dubbing initiative. Dates are absolute; this initiative started **2026-06-23**.

---

## Goal

> Let a user turn any foreign-language video into a **Russian-dubbed** one **locally**: a
> pre-rendered, selectable Russian audio track that preserves each speaker's intonation and gender
> by cloning their timbre, with the option to override any speaker using a curated voice bank —
> at maximum quality, with an optional cloud fallback for those who want it.

**Success looks like:** the owner picks a video, runs dubbing (batch or single), and selects a
natural-sounding Russian track in the player — speakers distinguishable, lip-timing acceptable,
background music intact, with zero non-redistributable weights shipped in the free `.exe`.

**Non-goals (for now):** live realtime dubbing during playback; lip-sync video reanimation;
non-Russian target languages; bundling non-commercial models.

## Guiding principles

- **Local-first**, redistribution-safe (the license of the *weights* is the hard filter).
- **Pre-render** + reuse the batch pipeline & `ExternalAudioStream`; never touch the audio thread.
- **Neural in the Python sidecar, DSP in bundled FFmpeg** (clean licensing + separation of concerns).
- **Additive & frozen-safe**: every change is opt-in; ASR/translation results never change.
- **Ear-test on real content on the real 5090** before declaring any phase done — gates miss it.

## Phases

| Phase | Scope | Depends on | New heavy deps |
| --- | --- | --- | --- |
| **0 — MVP** | Sidecar + CosyVoice2 single preset Russian voice; timestamp placement + capped atempo + **ducking**; `ExternalAudioStream` output. Proves the architecture in a shippable `.exe`. | — | CosyVoice2 (Apache-2.0), torch cu128 |
| **1 — Voice bank** | Curate/pre-render a bank of preset Russian voices (genders covered) from CosyVoice2 (optionally Silero MIT). Per-line voice selection; manual gender tag. | 0 | — |
| **2 — Diarization + per-speaker presets** | pyannote community-1 diarization (HF-gated first-run + attribution), per-speaker reference clips (ffmpeg), F0 gender + override UI, speaker→voice mapping (same-gender bank voice). Still presets, not cloning. | 1 | pyannote (CC-BY-4.0) |
| **3 — Per-speaker cloning (hybrid core)** | Default per-speaker path → CosyVoice2 zero-shot cloning from each diarized clip; Phase-2 bank override retained. Ear-test cloning; expose XTTS-v2 / F5-Russian as **optional user-installed** advanced backends. | 2 | — (XTTS/F5 user-installed) |
| **4 — Source separation** | Mel-Band RoFormer (fallback Demucs) via `python-audio-separator`, **weights first-run download**. Isolated dialogue (better refs + ASR) + music/SFX bed; mix dub over the preserved bed instead of ducking. Toggle; validate on heavy-SFX content. | 3 | separator code (MIT), weights (download-only) |
| **5 — Isochrony + translation polish** | Full layered isochrony (duration-budgeted LLM translation, pause-spill, TTS rate ctrl, smoothed capped atempo, reset-at-pause). VideoLingo Translate-Reflect-Adapt + ViDove proofreader ideas for Russian consistency. Optional RVC (MIT) post-pass (prototype first). | 4 | RVC (MIT, optional) |
| **6 — Optional cloud slot** | `ITtsService` with ElevenLabs (TTS stable; dubbing endpoint experimental), Azure/Cartesia alt. User-supplied paid key; **defaults to preset voices** (cloud source-speaker cloning is ToS-prohibited). Local stays default. | 3 | none (user keys) |

> **Phase 1 progress (F-16, voice-bank slice — shipped v0.3.30):** the C#-side voice bank landed —
> `VoiceBankResolver` (GPU-free preset bank mirroring `dub_sidecar/server.py` VOICES + fail-soft
> `ResolveAsync` engine-merge seam) plus a voice picker in the batch dialog and a new **Settings ▸
> Subtitles ▸ Dubbing** section (voice / ducking / atempo / output format). Selecting a voice writes
> `DubbingConfig.DefaultVoiceId` (one voice per dub). **Still phase-2:** per-line / per-speaker selection
> (needs diarization + per-line data) and the manual gender override UI; pre-rendering additional preset
> voices is owner first-run on the GPU.
>
> **Phase 2 progress (F-16, custom voice ids — shipped v0.3.31):** the voice picker now merges
> user-declared `DubbingConfig.CustomVoiceIds` with the built-in bank via the new
> `VoiceBankResolver.ForConfig(selected, customVoiceIds)` overload (dedup/trim/order-preserving), and
> **Settings ▸ Subtitles ▸ Dubbing** gained a **Custom voice IDs** Add/Remove editor. This lets a voice
> the user added to `dub_sidecar/server.py` VOICES be selected (and reach the synth request as
> `DefaultVoiceId`) without hand-editing config — GPU-free, default empty → byte-identical, no engine
> probe. **Still phase-2:** diarization + per-speaker presets + F0/manual gender override UI; the engine
> live-discovery refresh (`ResolveAsync`) remains unwired (it would require starting the GPU sidecar).

## This session's sprint (Phase 0)

> **Honest scope (design-review correction B7):** the long-lived HTTP sidecar, the `uv` provisioner,
> and the watchdog are **all net-new** — faster-whisper is a one-shot prebuilt `.exe` via CliWrap
> (no HTTP, no uv, no daemon), so this is **not** a "mirror" of an existing render path. Nothing
> neural can be stood up in-session (it needs the owner's RTX 5090 + multi-GB cu128 venv +
> CosyVoice2 download). Phase 0 therefore delivers a **compiled, frozen-safe, but explicitly-unrun
> vertical slice**; the real render is owner first-run.

**In scope (deliver, build, commit, .exe) — C# compiled, not executed:**
- `ITtsService` + `TtsServiceType` + `TtsServiceFactory` + models.
- **`DubSidecarHost`** (run-scoped: owns python child + HttpClient + port + readiness + watchdog +
  **Job Object `KILL_ON_JOB_CLOSE`**) + `LocalCosyVoiceTtsService` (cheap per-file over the host).
  One instance process-wide; built from a `DubbingConfig` snapshot; port via stdout `DUB_PORT`.
- `DubbingRenderer` — synth per line → Silero stress pre-pass → **capped `atempo` + drift-reset** →
  **adelay onto a full-length silence bed** → **`sidechaincompress` duck** (via
  `-filter_complex_script`) → encode `video.ru.dub.flac` (all DSP = bundled ffmpeg/CliWrap).
- **Pure, unit-tested:** `DubbingFilterGraph` (adelay bed + sidechaincompress + aresample/channels),
  `DubbingIsochrony` (atempo clamp + drift-reset-at-≥300ms), `DubbingOutputPathBuilder`.
- Batch integration: **optional ctor param** `IDubbingRenderer? dubber = null`; hook in
  `TranslateAndSaveAsync` between write and Completed, guarded by `dubber != null &&
  options.GenerateDubbing`, own `.ru.dub` overwrite check; **force serialize-mode when dubbing on**
  (GPU-no-overlap); `BatchSubtitleStatus.Dubbing` progress; `AppConfigBatchSubtitles.GenerateDubbing`
  + batch checkbox; renderer wired in `BatchSubtitlesDialogVM`.
- `DubbedAudioAutoLoader` — on player-open, add `.ru.dub.*` to `ExternalAudioStreams` **on the UI
  dispatcher** (appears under the existing **Audio ▸ External** menu).
- `Config.Subtitles.DubbingConfig` (additive, no new converter) + minimal settings section.
- `dub_sidecar/server.py` (FastAPI `/health` + `/synthesize`, port-0 + `DUB_PORT`, parent-PID
  self-terminate, **`--mock` mode**) + pinned `pyproject.toml`/`uv.lock` (no `ttsfrd`, no NC pkgs) —
  **committed as a contract artifact, not stood up**.
- `DubbingEngineProvisioner` — thin shell-out to the `uv` binary with a documented command sequence +
  manual fallback (happy-path owner-validated, **not** gate-validated).
- **Licensing/process:** NC-package `uv.lock` build gate (verifier); NOTICES bundle + GPLv3 §6 offer
  covering `dub_sidecar/`; `DO_NOT_PUSH.md`/`.gitignore`/`ship.ps1` updated (commit `dub_sidecar/`;
  never `DubEngine/`, `dubmodels/`, `*.ru.dub.*`).

**Out of scope this session:** diarization, cloning, separation, voice bank, cloud, full isochrony
budget. The MVP is a single-voice *закадровый* voiceover over ducked original.

**Definition of done — `.exe` (merge gate):**
- `dotnet build -warnaserror` 0/0 (LLPlayer + Plugins/YoutubeDL); `dotnet test` green; `verify`
  (frozen) green **including the new NC-package lockfile gate**; multi-agent `/review`
  Critical/Important closed.
- Published `.exe` **launches**, and a launch-test with **`GenerateDubbing=false` proves
  byte-for-byte-unchanged** existing behavior (the additive guarantee).

**Owner first-run acceptance (NOT a merge gate):** `uv` provisioner downloads the cu128 venv +
CosyVoice2; sidecar boots and `/synthesize` works; **ear-test** Russian quality on real content; dub
track appears in Audio ▸ External, plays in sync, ducking audible; cancel mid-dub → no orphan
python.exe (VRAM freed) / no partial file. These run on the owner's 5090 and are documented in the
handoff (exactly as faster-whisper's engine download is a user first-run step today).

**Owner decisions to resolve before investing in the provisioner UI (I10):** #4 (bundle vs
first-run download for CosyVoice2 weights) and #8 (full-offline vs first-run internet).

## Sequencing risks & notes

- Phase 2 introduces the only **gated** dependency (pyannote) — keep MVP gating-free by deferring it.
- Phase 4 weights are the **legal** sensitivity — first-run download is mandatory, not optional.
- Phases 3 and 6 both depend only on 2/3 respectively and can be reordered by owner preference
  (e.g. ship cloud slot before separation if cloud quality is wanted sooner).
- The cu128/sm_120 toolchain is the single biggest *technical* unknown — it is front-loaded into the
  MVP on purpose so it is proven before any further investment.

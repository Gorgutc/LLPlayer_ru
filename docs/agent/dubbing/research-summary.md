# AI Dubbing — Research Summary (cited)

Distilled from the `dubbing-research` multi-agent study (2026-06-23): 7 parallel web-research
threads, each adversarially verified by an independent skeptic (15 agents total, ~1.05M tokens).
Verification **changed** several conclusions — those corrections are called out below. This file is
the traceable evidence base for [`dubbing-spec.md`](dubbing-spec.md) and
[`dubbing-roadmap.md`](dubbing-roadmap.md).

> Confidence note repeated by every thread: **"supports Russian" ≠ "good Russian".** Real
> cross-lingual Russian prosody/accent is content- and reference-dependent and **must be
> ear-tested on the owner's real clips** before locking any default. Published demos/Elo boards
> rarely isolate Russian cloning.

---

## 1. TTS + voice cloning (Russian, local, RTX 5090)

**Decision:** Bundle **CosyVoice2-0.5B** (Apache-2.0 code **and** weights, ungated, Russian,
cross-lingual zero-shot cloning + duration control) as the default. For the strongest *cloned*
timbre, the license-clean high-quality route is **two-stage: CosyVoice2 (good Russian) → optional
RVC (MIT) timbre post-pass** (Phase 3/5; prototype before enabling). For the preset **voice bank**,
render presets from CosyVoice2 and/or use **Silero BASE voices (MIT)**, which uniquely ship Russian
stress + homograph normalization.

**Other bundle-safe options:** Chatterbox Multilingual (MIT — but embeds an inaudible Perth
watermark and degrades after ~5 repeated Russian gens → re-prime per segment), OpenVoice v2 (MIT —
but its MeloTTS base has **no native Russian**, only via cross-lingual converter), RVC (MIT),
OpenF5-TTS-Base (Apache-2.0 — Russian quality unverified).

**Verification corrections:**
- ❌ **IndexTTS2 dropped** — NOT MIT (bilibili restricted license), Russian output documented broken
  (wrong-language phonetics, issue #394), and its headline duration control is **not enabled** in
  the public release.
- ⚠️ **XTTS-v2** CPML is non-commercial for **weights AND outputs**; vendor Coqui defunct (Jan 2024)
  so no commercial license is purchasable → **user-installed only, never bundled**.
- ⚠️ **F5-TTS** base CC-BY-NC, Russian finetune CC-BY-NC-SA → user-installed only.
- ⚠️ Re-verify **CosyVoice3** / Chatterbox-v3 licenses separately before any upgrade; only
  CosyVoice2-0.5B is confirmed Apache-2.0.

Sources: huggingface.co/FunAudioLLM/CosyVoice2-0.5B · github.com/FunAudioLLM/CosyVoice ·
huggingface.co/coqui/XTTS-v2 · docs.coqui.ai/en/latest/models/xtts.html ·
huggingface.co/ResembleAI/chatterbox · github.com/resemble-ai/chatterbox/issues/360 ·
github.com/RVC-Project/Retrieval-based-Voice-Conversion · github.com/swivid/f5-tts

## 2. Diarization + gender (Phase 2)

**Decision:** **pyannote.audio 4.x** + **`speaker-diarization-community-1`** (lib MIT, model
**CC-BY-4.0** → attribution required). Orchestrate via **WhisperX** (BSD-2, reuses faster-whisper)
or run pyannote 4 directly. Per-speaker reference clips: cut the longest non-overlapping
single-speaker run (~6–15 s) with FFmpeg. Gender: **license-free F0 median-pitch heuristic**
(librosa/torchaudio) + manual per-speaker override.

**Verification corrections / caveats:**
- ⚠️ **WhisperX ↔ pyannote-4/community-1 is NOT a settled drop-in** (issue #1300 open; torch/
  torchaudio/pyannote mismatch breakage). Pin an exact triple + diarization-handoff smoke test;
  be ready to run pyannote 4 directly with your own word→speaker mapping.
- ⚠️ community-1 is **HF-gated**: first download needs an authenticated token + one-time terms
  acceptance; after a local git-clone it runs fully offline. Installer/first-run UX must handle it.
- ❌ NeMo Sortformer (CC-BY-NC) and audeering wav2vec2 gender (CC-BY-NC-SA) → excluded (use F0).
- ℹ️ Senko (MIT, Silero-VAD) is a viable gate-free fallback diarizer (no word alignment).

Sources: huggingface.co/pyannote/speaker-diarization-community-1 · pyannote.ai/blog/community-1 ·
github.com/pyannote/pyannote-audio · github.com/m-bain/whisperx · github.com/narcotic-sh/senko ·
huggingface.co/nvidia/diar_sortformer_4spk-v1

## 3. Source / vocal separation (Phase 4)

**Decision:** **MVP uses DUCKING, not separation** (zero weight-redistribution risk, no GPU
contention, music/SFX preserved; faint original under the dub is the accepted industry first pass).
**Phase 4:** **Mel-Band RoFormer** (fallback **Demucs htdemucs_ft**) via the **MIT
`python-audio-separator`** (now under the nomadkaraoke org, honor UVR/Anjok07 attribution).

**Verification correction (important):** the **WEIGHTS are the real legal risk and worse than
"inconsistently documented"** — top RoFormer vocal checkpoints carry **NO stated license**
(effectively unlicensed); Demucs weights are **unspecified, not affirmatively MIT** (trained on 800
proprietary songs, issue #327 unanswered). → **Download-on-first-run is the primary legal
mitigation** (distribute MIT code only, never weights), not just an installer-size choice. Validate
on heavy-SFX cinematic content (music-trained models on dialogue is an untested distribution).

Sources: pypi.org/project/audio-separator · github.com/KimberleyJensen/Mel-Band-Roformer-Vocal-Model
· github.com/facebookresearch/demucs · arxiv.org/abs/2310.01809 · vocalremover.cloud/blog/uvr-best-model-aug-2025

## 4. Isochrony / time-alignment

**Decision:** Treat isochrony as a **layered budget, cheapest-first**:
1. **Duration-budgeted LLM translation** (reuse LM Studio; ask for Russian that fits
   `(src_end−src_start) + fraction of trailing silence`; re-prompt "make ~X% shorter" with a meaning
   floor + capped iterations). Shortening words is free; distorting audio is not.
2. **Place at source timestamp + spill into source silences** (pause = ≥300 ms).
3. **TTS speed/duration control** toward the budget.
4. **Capped pitch-preserving time-stretch via ffmpeg `atempo`** (LGPL), bounded ~0.9–1.15× (never
   beyond ~1.3×); if the factor exceeds the cap, borrow trailing silence / nudge start / accept
   bounded drift and **reset at the next pause** so the ~18% per-segment drift floor
   (IsoChronoMeter) never accumulates. Smooth stretch factors across neighbors.

**Verification corrections:**
- ⚠️ **Licensing reframe (design review B1):** LLPlayer is **GPLv3** and already ships a **GPL
  FFmpeg** (librubberband/x264/x265 linked), so the original "avoid rubberband, atempo is LGPL-safe"
  framing is a **non-existent constraint**. `atempo` is still the right default — for **quality**
  (clean pitch-preserving small ±10–15 % corrections; single instance, 0.5–100×, no chaining) — not
  licensing.
- ✅ Realism: target "tolerable, pause-contained drift", **not** frame-perfect sync. **No lip-sync**
  (mouth-shape video editing) — separate, much harder generative-video problem, unnecessary for an
  audio track.

Sources: arxiv.org/abs/2302.12979 (isochrony NMT) · arxiv.org/html/2506.21619 (IsoChronoMeter) ·
arxiv.org/pdf/2110.03847 (verbosity control) · ffmpeg-cookbook.com (atempo)

## 5. .NET integration of the Python ML

**Current implementation correction:** the early design said to mirror the faster-whisper CliWrap path.
The shipped dubbing path instead uses raw `System.Diagnostics.Process` so the child can be placed in a
Windows Job Object, then talks to a LONG-LIVED localhost HTTP sidecar (127.0.0.1:ephemeral, port from
`DUB_PORT=` stdout, bounded readiness probe, graceful shutdown). Models load **once**; per-segment synth
returns temp WAV paths (not base64), and the Python sidecar performs stretch/placement/duck/mix/encode
from the immutable run snapshot. The dedicated dub venv is still lockfile-pinned and provisioned via the
`uv` standalone binary, separate from the ASR env.

**Verification corrections:**
- ✅ **sm_120 is no longer bleeding-edge:** PyTorch **2.7.0 was the first STABLE release with cu128
  wheels + sm_120 kernels** (the research's "nightly/unofficial" framing was outdated). Pin
  `torch>=2.7.0+cu128`; re-verify on every bump; ensure VC++ redistributable present.
- ⚠️ faster-whisper/CTranslate2 **INT8 crashes on sm_120** (CUBLAS_STATUS_NOT_SUPPORTED) → force
  `compute_type=float16`.
- ℹ️ **sherpa-onnx** (k2-fsa, in-.NET ONNX) is NOT a substitute for cloning/Russian, but is a viable
  **dependency-free preset-voice fallback** (Piper/VITS Russian `.onnx`) for no-GPU users.
- ⚠️ Avoid PyInstaller for this stack (torch/CUDA fragility); download torch+CUDA on first run.

Sources: github.com/Tyrrrz/CliWrap · docs.astral.sh/uv · github.com/SYSTRAN/faster-whisper/issues/1086
· github.com/k2-fsa/sherpa-onnx · github.com/k2-fsa/sherpa-onnx/blob/master/dotnet-examples/kokoro-tts

## 6. Reference pipelines to borrow (architecture only)

- **open-dubbing** (Softcatala, Apache-2.0) — closest to CliWrap-sidecar + faster-whisper +
  per-speaker **gender** assignment. **Copy architecture only** — its default NLLB-200 translator is
  CC-BY-NC and some default TTS are non-commercial; keep LM Studio for translation.
- **Auto-Synced-Translated-Dubs / ASTD** (ThioJoe) — the isochrony algorithm (synth per line →
  ffmpeg atempo to subtitle duration → two-pass speaking-rate re-synthesis). **NO LICENSE file =
  all-rights-reserved → REIMPLEMENT, do not copy code.**
- **SoniTranslate** (R3gm, Apache-2.0) — full diarize+clone+hybrid-voice flow + multi-TTS plug-in
  abstraction (reference, not a dependency; last major release May 2024 — confirm state).
- **VideoLingo** (Apache-2.0) — Translate-Reflect-Adapt 3-step LLM loop for Russian quality.
- **ViDove** (EMNLP 2025) — proofreader-agent + domain-memory for long-form terminology/name
  consistency.
- **ViDubb / Linly-Dubbing** — separate-vocals → dub → **re-mix over original music/SFX bed** pattern.

Sources: github.com/Softcatala/open-dubbing · github.com/ThioJoe/Auto-Synced-Translated-Dubs ·
github.com/R3gm/SoniTranslate · github.com/Huanshere/VideoLingo · github.com/Kedreamix/Linly-Dubbing

## 7. Optional cloud slot (Phase 6)

**Decision:** **ElevenLabs** primary behind a minimal `ITtsService`
(`SynthesizeAsync(text, voiceRef, lang, sampleClip?) → audioStream`): best-in-class Russian, clean
REST + SDKs, plus a dubbing endpoint (keep **behind an experimental flag** — Dubbing v2 API not GA
mid-2026, v1 in maintenance). Secondary: Azure AI Speech (best .NET fit; cloning approval-gated) or
Cartesia Sonic (cheapest cloning; **Russian confirmed**).

**Critical legal constraint (this is *why* local-first is the default):** every cloud provider gates
voice cloning to the **uploader's OWN consented voice** (ElevenLabs voice-captcha; Azure/Google
recorded consent). Cloning an arbitrary film speaker via cloud APIs **violates their ToS** → the
cloud slot must **default to PRESET Russian voices**, never arbitrary source-speaker cloning. The
app must require the **user's own paid API key** (free tiers forbid commercial use / require
attribution) and must **never bundle or ship pre-generated cloud audio**.

Sources: elevenlabs.io/docs/overview/capabilities/dubbing · elevenlabs.io/docs/overview/models ·
learn.microsoft.com/.../speech-service/personal-voice-overview · cartesia.ai/sonic

---

## Consolidated ship-safe licensing table

| Tier | Items |
| --- | --- |
| **Bundle (permissive)** | CosyVoice2-0.5B (Apache-2.0); pyannote community-1 (CC-BY-4.0 **+attribution**); pyannote.audio (MIT); WhisperX (BSD-2); faster-whisper/CTranslate2 (MIT); python-audio-separator **code** (MIT **+attribution**); RVC (MIT); Silero BASE (MIT); ffmpeg `atempo` (LGPL); librosa/torchaudio (ISC/BSD); uv (Apache/MIT) |
| **Download-on-first-run (never bundle weights)** | all separation **weights** (RoFormer unlicensed; Demucs unspecified) |
| **User-installed only (non-commercial)** | XTTS-v2 (CPML); F5-TTS / F5-Russian (CC-BY-NC / -SA); Silero non-base (CC-BY-NC) |
| **Excluded** | IndexTTS2; NeMo Sortformer; audeering gender; NLLB-200 |
| **Cloud** | user-supplied paid keys only; never bundle cloud audio; cloning consent-gated to user's own voice |

**Process invariants (every phase):** ship a third-party-licenses/attribution screen; pin the
torch cu128 triple and **launch-test on the real RTX 5090**; ear-test Russian before locking
defaults; AGENTS.md `verify-frozen` gate + published-.exe launch-test discipline apply throughout.

# LLPlayer Dubbing Sidecar

Local TTS + DSP engine for the AI dubbing feature. A long-lived localhost HTTP server that LLPlayer
launches and supervises (`DubSidecarHost`). GPLv3 — part of LLPlayer's Corresponding Source.

See `docs/agent/dubbing/dubbing-spec.md` for the architecture and `docs/agent/dubbing-contract.md`
for the frozen boundaries.

## Files (committed)

- `server.py` — the sidecar (stdlib HTTP server; heavy ML/DSP imported lazily; `--mock` runs on bare Python).
- `pyproject.toml` — pinned, GPLv3-compatible dependency set (no non-commercial packages).
- `uv.lock` — generated during provisioning (`uv lock`) and committed.

Runtime data (NOT committed): the venv (`DubEngine/`), model weights (`dubmodels/`), output (`*.ru.dub.*`).

## Owner first-run provisioning (RTX 5090 / Blackwell, sm_120)

```powershell
# 1. Get uv (https://docs.astral.sh/uv) — shipped beside the app or installed once.
# 2. Create the dedicated dub venv next to the exe (separate from the ASR env):
uv venv "DubEngine"
# 3. Install the CUDA 12.8 build of torch (sm_120) FIRST, then the rest:
uv pip install --python "DubEngine\Scripts\python.exe" torch>=2.7.0 --index-url https://download.pytorch.org/whl/cu128
uv pip install --python "DubEngine\Scripts\python.exe" soundfile numpy librosa av
# 4. Install CosyVoice2 (Apache-2.0) from upstream and download the 0.5B weights into dubmodels\.
#    (Pin a commit; see https://github.com/FunAudioLLM/CosyVoice and huggingface.co/FunAudioLLM/CosyVoice2-0.5B)
# 5. Smoke the contract WITHOUT the GPU stack (pure stdlib mock):
DubEngine\Scripts\python.exe dub_sidecar\server.py --port 0 --mock
#    -> prints "DUB_PORT=<n>"; GET /health returns {"ready":true}.
```

LLPlayer then drives it: ASR + LM Studio translation → per-line `/synthesize` → `/assemble` →
`video.ru.dub.flac` beside the video, selectable under **Audio ▸ External**.

## Licensing guardrail

The bundled venv must contain only redistribution-safe packages. The build gate
`scripts/codex/check-dub-licenses.ps1` fails if `uv.lock` pulls any non-commercial / unvetted
package (TTS/coqui, xtts, f5-tts, nemo, audeering, ttsfrd). XTTS-v2 / F5-TTS are **user-installed
only** — never add them to this lockfile.

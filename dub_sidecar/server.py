#!/usr/bin/env python3
# LLPlayer AI Dubbing — local TTS/DSP sidecar (GPLv3, part of LLPlayer's Corresponding Source).
#
# A long-lived localhost HTTP server launched + supervised by the C# app (DubSidecarHost). It loads the
# (multi-GB) TTS model ONCE and serves many per-line synth requests, then assembles the final dub track.
# Heavy ML/DSP deps (torch, cosyvoice, av/PyAV, librosa, soundfile, numpy) are imported LAZILY so the
# `--mock` path runs on a bare Python (tone/silence, no heavy deps) for off-GPU C#-pipeline smoke tests.
#
# Transport: stdlib http.server (no FastAPI dependency -> smaller venv, mock needs nothing installed).
# The server binds 127.0.0.1 on the requested port (0 = OS-assigned) and prints `DUB_PORT=<n>` on stdout
# for the C# host to read. It self-terminates if its parent process disappears (belt-and-suspenders with
# the Windows Job Object the host assigns).
#
# Endpoints:
#   GET  /health     -> {"ready": true}
#   POST /synthesize {"text","voice_id","target_duration_ms"} -> {"wav_path","duration_ms"}
#   GET  /voices     -> [{"id","name","gender","language"}]
#   POST /assemble   {"media_path","output_path","output_format","ducking_percent","total_ms","clips":[...]}
#   POST /shutdown   -> {"ok": true}
#
# Licensing: bundle-safe local models only (CosyVoice2-0.5B Apache-2.0). Never import non-commercial
# packages here (xtts/tts-coqui/f5-tts/nemo/audeering/ttsfrd) — the build gate enforces this on uv.lock.

import argparse
import json
import math
import os
import shutil
import struct
import tempfile
import threading
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# --------------------------------------------------------------------------------------------------
# Globals (populated lazily). The real model is loaded once on first /synthesize.
# --------------------------------------------------------------------------------------------------
ARGS = None
_OWN_WORKDIR = False  # True only when WE created the work dir (manual run, no --work-dir): clean it on shutdown.
_MODEL = None
_MODEL_LOCK = threading.Lock()
_SYNTH_RATE = 24000  # CosyVoice2 native sample rate (mono)

# A tiny preset "voice bank" for the MVP override path. The real engine maps these to model voices.
# NOTE: the C# UI mirrors this list in VoiceBankResolver.BuiltIn (FlyleafLib/MediaPlayer/Dubbing/
# VoiceBankResolver.cs) so the voice picker can populate WITHOUT starting this sidecar — keep the two
# lists (ids/names/genders/language) in lockstep; a C# unit test pins the expected presets.
VOICES = [
    {"id": "ru-preset-1", "name": "Russian Narrator (M)", "gender": "male", "language": "ru"},
    {"id": "ru-preset-2", "name": "Russian Narrator (F)", "gender": "female", "language": "ru"},
]


# --------------------------------------------------------------------------------------------------
# Synthesis
# --------------------------------------------------------------------------------------------------
def _safe_unlink(path):
    try:
        os.unlink(path)
    except OSError:
        pass


def _atomic_tmp_path(out):
    # Temp name for the atomic write. It must NOT match the C# resolver glob "{name}.ru.dub.*", or a
    # crash-orphaned temp would look like a finished dub (blocking re-render / auto-attaching a stub).
    # A leading dot keeps it out of that glob; the pid makes it unique per process.
    return os.path.join(os.path.dirname(out) or ".", ".{}.{}.tmp".format(os.path.basename(out), os.getpid()))


def _write_wav_mono16(path, samples, rate):
    """Write a list/iterable of float [-1,1] samples as 16-bit mono PCM WAV (stdlib only)."""
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        frames = bytearray()
        for s in samples:
            v = int(max(-1.0, min(1.0, s)) * 32767.0)
            frames += struct.pack("<h", v)
        w.writeframes(bytes(frames))


def synth_mock(text, target_duration_ms):
    """Mock synthesis: a short, quiet tone whose length tracks the text/slot. Pure stdlib."""
    # Estimate a natural length from text if no usable target was given (~14 chars/sec Russian-ish).
    est_ms = max(300, int(len(text) / 14.0 * 1000.0))
    dur_ms = int(target_duration_ms) if target_duration_ms and target_duration_ms > 0 else est_ms
    # Deliberately render a bit long so the C# isochrony (atempo + drift-reset) path is exercised.
    dur_ms = int(dur_ms * 1.2)
    n = int(_SYNTH_RATE * dur_ms / 1000.0)
    freq = 180.0
    samples = (0.05 * math.sin(2.0 * math.pi * freq * i / _SYNTH_RATE) for i in range(n))
    fd, path = tempfile.mkstemp(suffix=".wav", prefix="dubclip_", dir=ARGS.work_dir)
    os.close(fd)
    _write_wav_mono16(path, samples, _SYNTH_RATE)
    return path, dur_ms


def _load_model():
    """Load CosyVoice2 once. Heavy imports are deferred to here (real path only)."""
    global _MODEL
    if _MODEL is not None:
        return _MODEL
    with _MODEL_LOCK:
        if _MODEL is not None:
            return _MODEL
        # Lazy import — only when actually synthesizing on a provisioned env.
        from cosyvoice.cli.cosyvoice import CosyVoice2  # type: ignore
        model_dir = os.path.join(ARGS.models_dir, ARGS.model)
        _MODEL = CosyVoice2(model_dir, load_jit=False, load_trt=False, fp16=True)
        return _MODEL


def maybe_normalize_russian(text):
    """Mandatory-ish Russian stress/homograph normalization; graceful-degrade to raw text."""
    try:
        # Silero text normalization / stress (MIT base). Optional — never fail synthesis over it.
        import silero  # type: ignore  # noqa: F401
        # Placeholder: real call wired during owner first-run validation.
        return text
    except Exception:
        return text


def synth_real(text, voice_id, target_duration_ms):
    import soundfile as sf  # type: ignore
    model = _load_model()
    text = maybe_normalize_russian(text)
    # CosyVoice2 zero-shot / preset synthesis (API pinned during owner first-run validation).
    audio = None
    for out in model.inference_sft(text, voice_id):  # type: ignore[attr-defined]
        audio = out["tts_speech"].numpy().reshape(-1)
        break
    if audio is None:
        raise RuntimeError("CosyVoice2 produced no audio")
    fd, path = tempfile.mkstemp(suffix=".wav", prefix="dubclip_", dir=ARGS.work_dir)
    os.close(fd)
    sf.write(path, audio, _SYNTH_RATE)
    dur_ms = int(len(audio) / _SYNTH_RATE * 1000.0)
    return path, dur_ms


def synthesize(req):
    text = (req.get("text") or "").strip()
    voice_id = req.get("voice_id") or "ru-preset-1"
    target = int(req.get("target_duration_ms") or 0)
    if not text:
        return {"wav_path": "", "duration_ms": 0}
    if ARGS.mock:
        path, dur = synth_mock(text, target)
    else:
        path, dur = synth_real(text, voice_id, target)
    return {"wav_path": path, "duration_ms": dur}


# --------------------------------------------------------------------------------------------------
# Assembly: place + time-stretch clips on a silence bed, duck the original under the dub, encode.
# --------------------------------------------------------------------------------------------------
def assemble(req):
    if ARGS.mock:
        return assemble_mock(req)
    return assemble_real(req)


def assemble_mock(req):
    """Mock assembly: write the placed dub bed (no original mix) as a 16-bit WAV to output_path."""
    total_ms = float(req.get("total_ms") or 0)
    clips = req.get("clips") or []
    rate = _SYNTH_RATE
    total_n = max(1, int(rate * total_ms / 1000.0))
    bed = [0.0] * total_n
    for c in clips:
        try:
            with wave.open(c["wav_path"], "rb") as w:
                nch = w.getnchannels()
                sw = w.getsampwidth()
                fr = w.getframerate()
                n = w.getnframes()
                raw = w.readframes(n)
            # synth_mock only ever writes mono 16-bit; skip anything else rather than mis-decode it.
            if nch != 1 or sw != 2:
                continue
            vals = struct.unpack("<%dh" % n, raw)
            off = int(rate * float(c.get("start_ms") or 0) / 1000.0)
            # Mock ignores atempo + resample; placement only (enough to validate the C# pipeline).
            step = fr / rate if fr != rate else 1.0
            for i in range(total_n - off if off < total_n else 0):
                src = int(i * step)
                if src >= n:
                    break
                bed[off + i] += vals[src] / 32767.0
        except Exception:
            continue
    out = req["output_path"]
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    # Atomic write: a truncated final file would pass the C# OutputExists (length>0) check and auto-attach.
    tmp = _atomic_tmp_path(out)
    try:
        _write_wav_mono16(tmp, bed, rate)
        os.replace(tmp, out)
    except Exception:
        _safe_unlink(tmp)
        raise
    return {"output_path": out}


def assemble_real(req):
    """Real assembly via PyAV (decode original) + numpy/librosa/soundfile (stretch/mix/encode)."""
    import numpy as np  # type: ignore
    import soundfile as sf  # type: ignore
    import av  # type: ignore  # PyAV (LGPL libav wheels)

    media_path = req["media_path"]
    out = req["output_path"]
    ducking = max(1, min(100, int(req.get("ducking_percent") or 15))) / 100.0
    clips = req.get("clips") or []

    # Decode the original audio to float32 [-1,1], keep its sample rate + channel count.
    container = av.open(media_path)
    astream = next((s for s in container.streams if s.type == "audio"), None)
    if astream is None:
        raise RuntimeError("no audio stream in source")
    rate = astream.codec_context.sample_rate or 48000
    chans = astream.codec_context.channels or 2
    chunks = []
    resampler = av.audio.resampler.AudioResampler(format="fltp", layout=f"{chans}c", rate=rate)
    for frame in container.decode(audio=0):
        for rf in resampler.resample(frame):
            chunks.append(rf.to_ndarray())
    container.close()
    original = np.concatenate(chunks, axis=1).T if chunks else np.zeros((rate, chans), dtype="float32")
    total_n = original.shape[0]

    # Build the dub bed at the source rate (mono), placing each (optionally stretched) clip.
    bed = np.zeros(total_n, dtype="float32")
    for c in clips:
        clip, sr = sf.read(c["wav_path"], dtype="float32", always_2d=False)
        if clip.ndim > 1:
            clip = clip.mean(axis=1)
        atempo = float(c.get("atempo") or 1.0)
        if abs(atempo - 1.0) > 1e-3:
            import librosa  # type: ignore  # ISC; phase-vocoder time-stretch (NOT GPL rubberband)
            clip = librosa.effects.time_stretch(clip, rate=atempo)
        if sr != rate:
            import librosa  # type: ignore
            clip = librosa.resample(clip, orig_sr=sr, target_sr=rate)
        off = int(rate * float(c.get("start_ms") or 0) / 1000.0)
        end = min(total_n, off + clip.shape[0])
        if 0 <= off < total_n and end > off:
            bed[off:end] += clip[: end - off]

    # Duck the original where the dub is present, then sum. Smooth the gate to avoid clicks.
    active = (np.abs(bed) > 1e-4).astype("float32")
    win = max(1, int(rate * 0.05))
    kernel = np.ones(win, dtype="float32") / win
    env = np.convolve(active, kernel, mode="same")
    env = np.clip(env, 0.0, 1.0)
    gain = (1.0 - env) + env * ducking  # 1.0 in the clear, `ducking` under speech
    mixed = original * gain[:, None] + bed[:, None]
    peak = float(np.max(np.abs(mixed))) if mixed.size else 1.0
    if peak > 1.0:
        mixed = mixed / peak

    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    fmt = (req.get("output_format") or "flac").lower()
    subtype = "PCM_16" if fmt in ("wav", "flac") else None
    container = fmt.upper() if fmt in ("flac", "wav") else "FLAC"
    # Atomic write to avoid a truncated final file on kill/disk-full/exception (same dir => os.replace atomic).
    tmp = _atomic_tmp_path(out)
    try:
        sf.write(tmp, mixed, rate, format=container, subtype=subtype)
        os.replace(tmp, out)
    except Exception:
        _safe_unlink(tmp)
        raise
    return {"output_path": out}


# --------------------------------------------------------------------------------------------------
# HTTP plumbing
# --------------------------------------------------------------------------------------------------
def _shutdown():
    # Clean our temp clips. If the C# host passed --work-dir it owns deletion (it removes the dir after
    # reaping us), so we only rmtree a work dir WE created (manual run).
    if _OWN_WORKDIR and ARGS and ARGS.work_dir:
        shutil.rmtree(ARGS.work_dir, ignore_errors=True)
    os._exit(0)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):  # silence default stderr access logs
        pass

    def _send(self, code, obj):
        body = json.dumps(obj).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self):
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b"{}"
        return json.loads(raw.decode("utf-8") or "{}")

    def do_GET(self):
        if self.path == "/health":
            self._send(200, {"ready": True})
        elif self.path == "/voices":
            self._send(200, VOICES)
        else:
            self._send(404, {"error": "not found"})

    def do_POST(self):
        try:
            if self.path == "/synthesize":
                self._send(200, synthesize(self._read_json()))
            elif self.path == "/assemble":
                self._send(200, assemble(self._read_json()))
            elif self.path == "/shutdown":
                self._send(200, {"ok": True})
                threading.Thread(target=_shutdown, daemon=True).start()
            else:
                self._send(404, {"error": "not found"})
        except Exception as ex:  # surface a clean error to the C# host
            self._send(500, {"error": str(ex)})


def watch_parent(parent_pid):
    """Self-terminate if the parent (LLPlayer) disappears — redundant with the host's Job Object."""
    if not parent_pid:
        return

    def _loop():
        import time
        while True:
            alive = True
            try:
                if os.name == "nt":
                    import ctypes
                    SYNCHRONIZE = 0x00100000
                    h = ctypes.windll.kernel32.OpenProcess(SYNCHRONIZE, False, int(parent_pid))
                    if not h:
                        # Only a clearly-gone code (ERROR_INVALID_PARAMETER = no such PID) means dead;
                        # access-denied etc. => assume alive (the Job Object is the primary reaper).
                        err = ctypes.windll.kernel32.GetLastError()
                        alive = (err != 87)
                    else:
                        ctypes.windll.kernel32.CloseHandle(h)
                else:
                    os.kill(int(parent_pid), 0)
            except Exception:
                alive = False
            if not alive:
                os._exit(0)
            time.sleep(2.0)

    threading.Thread(target=_loop, daemon=True).start()


def main():
    global ARGS, _OWN_WORKDIR
    parser = argparse.ArgumentParser(description="LLPlayer dubbing sidecar")
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--model", default="cosyvoice2-0.5b")
    parser.add_argument("--models-dir", default=".")
    parser.add_argument("--parent-pid", type=int, default=0)
    parser.add_argument("--work-dir", default="")
    parser.add_argument("--mock", action="store_true")
    ARGS = parser.parse_args()

    # Per-run temp dir for synth clips so they never leak into %TEMP%. The C# host normally passes one it
    # owns + deletes; a manual run gets its own (cleaned on /shutdown).
    if not ARGS.work_dir:
        ARGS.work_dir = tempfile.mkdtemp(prefix="llp_dub_")
        _OWN_WORKDIR = True
    else:
        os.makedirs(ARGS.work_dir, exist_ok=True)

    watch_parent(ARGS.parent_pid)

    server = ThreadingHTTPServer(("127.0.0.1", ARGS.port), Handler)
    # Report the actual bound port so the C# host can connect (port 0 => OS-assigned).
    print("DUB_PORT=%d" % server.server_address[1], flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()

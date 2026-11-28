"""Prepare a local HTDemucs FT vocal stem on the original media timeline."""

import argparse
import hashlib
import io
import json
import math
import os
from fractions import Fraction
from pathlib import Path
import random
import subprocess
import sys
import tempfile
import wave

MODEL_SHA256 = "f3cf25b222c4eed7cd49dd8b2c9597d50c18bd154090f7b919cfa5f93cf22c49"
MODEL_BYTES = 84_141_271
RATE = 44_100
CHANNELS = 2
FRAME_BYTES = CHANNELS * 4
DURATION_TOLERANCE = 0.1
# Demucs holds the full mixture and four stereo float stems, even when only vocals is saved.
MAX_DURATION = 30 * 60
ASR_RATE = 16_000
MAX_ASR_EVIDENCE_SAMPLES = 9 * 60 * ASR_RATE
LOCAL_FORMATS = "aiff,asf,avi,flac,matroska,webm,mov,mp4,m4a,3gp,3g2,mj2,mp3,mpeg,mpegts,ogg,wav"
LOCAL_INPUT = ("-protocol_whitelist", "file", "-format_whitelist", LOCAL_FORMATS)


class UnsupportedSource(Exception):
    """No stem can be used safely for this source."""


def run(command, timeout):
    result = subprocess.run(command, capture_output=True, timeout=timeout)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip()[-2000:]
                           or f"Media tool failed with exit code {result.returncode}.")
    return result.stdout


def finite(value):
    try:
        value = float(value)
    except (TypeError, ValueError):
        return None
    return value if math.isfinite(value) else None


def inspect_source(ffprobe, source, index, duration):
    if not math.isfinite(duration) or not 0 < duration <= MAX_DURATION:
        raise UnsupportedSource("Speech separation requires media of finite duration up to 30 minutes.")
    try:
        probe = json.loads(run([
            ffprobe, "-v", "error", *LOCAL_INPUT, "-select_streams", str(index),
            "-show_packets", "-show_streams", "-show_format", "-read_intervals", "%+#256",
            "-show_entries",
            "packet=stream_index,pts_time:stream=index,codec_type,channels:format=start_time,duration",
            "-of", "json", "-i", source,
        ], timeout=30))
    except (RuntimeError, json.JSONDecodeError) as error:
        raise UnsupportedSource("Cannot verify the local audio track: " + str(error)) from error

    tracks = probe.get("streams", [])
    if (len(tracks) != 1 or tracks[0].get("index") != index
            or tracks[0].get("codec_type") != "audio" or tracks[0].get("channels") != CHANNELS):
        raise UnsupportedSource("Speech separation requires the selected track to be stereo audio.")

    container = probe.get("format", {})
    probed_duration = finite(container.get("duration"))
    if probed_duration is None or abs(probed_duration - duration) > DURATION_TOLERANCE:
        raise UnsupportedSource("The media duration cannot be matched to the player's timeline.")
    first_packet = next((packet for packet in probe.get("packets", [])
                         if packet.get("stream_index") == index), {})
    first_pts = finite(first_packet.get("pts_time"))
    container_start = finite(container.get("start_time"))
    if first_pts is None or (container_start is None and first_pts != 0):
        raise UnsupportedSource("The selected audio track's time origin cannot be verified.")


def decode_audio(ffmpeg, source, index, duration, destination):
    run([
        ffmpeg, "-v", "error", "-nostdin", "-xerror", *LOCAL_INPUT,
        "-copyts", "-start_at_zero", "-i", source, "-map", f"0:{index}",
        "-af", f"aresample={RATE}:async=1:first_pts=0:min_hard_comp=0.010",
        "-t", f"{duration + 2 * DURATION_TOLERANCE:.6f}",
        "-threads", "1", "-c:a", "pcm_f32le", "-f", "f32le", "-n", destination,
    ], timeout=180)
    frames, remainder = divmod(destination.stat().st_size, FRAME_BYTES)
    if remainder or not frames or abs(frames / RATE - duration) > DURATION_TOLERANCE:
        raise UnsupportedSource("The decoded audio length does not follow the media timeline.")
    return frames


def encode_wave(ffmpeg, raw, destination):
    run([
        ffmpeg, "-v", "error", "-nostdin", "-xerror", "-f", "f32le", "-ar", str(RATE),
        "-ac", str(CHANNELS), "-i", raw, "-c:a", "pcm_f32le", "-f", "wav", "-n", destination,
    ], timeout=180)


def resolve_source(source):
    if not source.is_absolute():
        raise UnsupportedSource("Speech separation requires an absolute local media path.")
    try:
        source = source.resolve(strict=True)
    except (OSError, RuntimeError) as error:
        raise UnsupportedSource("The selected media file cannot be resolved.") from error
    if not source.is_file() or source.as_posix().startswith("//"):
        raise UnsupportedSource("Speech separation requires a local media file.")
    return source


def require_cuda(torch):
    if not torch.cuda.is_available():
        raise RuntimeError("The configured PyTorch runtime does not provide CUDA.")


def separate_vocals(model_path, mixture_path, frames, vocal_path, device="cuda:0"):
    # Validate the same immutable bytes that will be given to the restricted checkpoint loader.
    with model_path.open("rb") as model_file:
        model_bytes = model_file.read(MODEL_BYTES + 1)
    if len(model_bytes) != MODEL_BYTES or hashlib.sha256(model_bytes).hexdigest() != MODEL_SHA256:
        raise RuntimeError("Expected the official F-02 HTDemucs FT vocal checkpoint (04573f0d).")
    checkpoint = io.BytesIO(model_bytes)
    del model_bytes

    import numpy as np
    from numpy.core.multiarray import scalar as numpy_scalar
    import torch
    from demucs.apply import apply_model
    from demucs.audio import prevent_clip
    from demucs.htdemucs import HTDemucs
    from demucs.states import load_model

    if device != "cpu":
        require_cuda(torch)
    expected_globals = {"numpy.dtype", "fractions.Fraction", "demucs.htdemucs.HTDemucs",
                        "numpy.core.multiarray.scalar"}
    if set(torch.serialization.get_unsafe_globals_in_checkpoint(checkpoint)) != expected_globals:
        raise RuntimeError("Unexpected checkpoint serialization types.")
    checkpoint.seek(0)
    dtype_classes = [type(np.dtype(name)) for name in ("float64", "float32", "int64", "int32")]
    with torch.serialization.safe_globals([
        np.dtype, Fraction, HTDemucs, (numpy_scalar, "numpy.core.multiarray.scalar"), *dtype_classes,
    ]):
        weights = torch.load(checkpoint, map_location="cpu", weights_only=True)
    checkpoint.close()
    if weights["klass"] is not HTDemucs:
        raise RuntimeError("Unexpected checkpoint model type.")
    model = load_model(weights)
    del weights
    if model.samplerate != RATE or model.audio_channels != CHANNELS or "vocals" not in model.sources:
        raise RuntimeError("Unexpected HTDemucs stem layout.")

    audio = np.fromfile(mixture_path, dtype="<f4").reshape(frames, CHANNELS)
    mixture = torch.from_numpy(audio.T.copy())
    del audio
    reference = mixture.mean(0)
    mean, deviation = reference.mean(), reference.std()
    del reference
    if not math.isfinite(float(deviation)) or not float(deviation) > 0:
        raise UnsupportedSource("The selected audio track has no usable signal for separation.")
    mixture.sub_(mean).div_(deviation)
    random.seed(0)
    torch.manual_seed(0)
    with torch.inference_mode():
        stems = apply_model(model, mixture[None], shifts=1, split=True, overlap=0.25, device=device)[0]
    if stems.shape != (len(model.sources), CHANNELS, frames):
        raise RuntimeError("HTDemucs changed the audio timeline.")
    vocal = stems[model.sources.index("vocals")].clone()
    del stems, mixture
    vocal.mul_(deviation).add_(mean)
    if not math.isfinite(float(vocal.abs().max())):
        raise RuntimeError("The separated stem contains non-finite audio.")
    prevent_clip(vocal, mode="rescale").T.contiguous().numpy().astype("<f4", copy=False).tofile(vocal_path)


def ceil_resample(samples, source_rate, target_rate):
    return (samples * target_rate + source_rate - 1) // source_rate


def validate_asr_input(source, samples):
    if not 0 < samples <= MAX_ASR_EVIDENCE_SAMPLES:
        raise UnsupportedSource("The requested evidence window is outside the allowed 16 kHz sample range.")
    try:
        with wave.open(str(source), "rb") as audio:
            if (audio.getnchannels(), audio.getsampwidth(), audio.getframerate(), audio.getnframes(),
                    audio.getcomptype()) != (1, 2, ASR_RATE, samples, "NONE"):
                raise UnsupportedSource("Evidence requires exactly the original mono PCM16 sample clock.")
    except (EOFError, wave.Error) as error:
        raise UnsupportedSource("Evidence requires a valid PCM WAV input.") from error


def decode_asr_evidence(ffmpeg, source, samples, raw):
    run([
        ffmpeg, "-v", "error", "-nostdin", "-xerror", *LOCAL_INPUT,
        "-i", source, "-map", "0:a:0", "-af", "pan=stereo|c0=c0|c1=c0", "-ar", str(RATE),
        "-c:a", "pcm_f32le", "-f", "f32le", "-n", raw,
    ], timeout=45)
    frames = ceil_resample(samples, ASR_RATE, RATE)
    if raw.stat().st_size != frames * FRAME_BYTES:
        raise UnsupportedSource("The evidence model input does not cover the original PCM clock.")
    return frames


def encode_asr_evidence(ffmpeg, vocal, frames, samples, destination):
    if vocal.stat().st_size != frames * FRAME_BYTES:
        raise UnsupportedSource("The evidence stem does not cover the original model input.")
    run([
        ffmpeg, "-v", "error", "-nostdin", "-xerror", "-f", "f32le", "-ar", str(RATE),
        "-ac", str(CHANNELS), "-i", vocal, "-af", "pan=mono|c0=0.5*c0+0.5*c1",
        "-ar", str(ASR_RATE), "-c:a", "pcm_s16le", "-fflags", "+bitexact",
        "-flags:a", "+bitexact", "-f", "s16le", "-n", destination,
    ], timeout=45)

    expected_bytes = samples * 2
    output_bytes = destination.stat().st_size
    if output_bytes == expected_bytes:
        return
    # Both resamplers have zero-origin inputs. A round-up on each rate change can
    # add just one terminal sample. Keep the exact prefix; never pad a short stem.
    if (output_bytes == expected_bytes + 2 and frames == ceil_resample(samples, ASR_RATE, RATE)
            and ceil_resample(frames, RATE, ASR_RATE) == samples + 1):
        with destination.open("r+b") as output:
            output.truncate(expected_bytes)
        return
    raise UnsupportedSource("The evidence output does not cover the original PCM exactly.")


def isolate_cpu_evidence():
    os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
    import torch
    if torch.cuda.is_available():
        raise RuntimeError("CUDA unexpectedly visible in the isolated CPU evidence process.")
    torch.set_num_threads(8)
    torch.set_num_interop_threads(1)


def separate_asr_evidence(args, source):
    validate_asr_input(source, args.asr_pcm_samples)
    if args.asr_evidence_cpu:
        isolate_cpu_evidence()
    with tempfile.TemporaryDirectory(prefix="evidence-", dir=args.output.parent) as temporary:
        directory = Path(temporary)
        raw, vocal, pcm = directory / "mixture.f32", directory / "vocals.f32", directory / "vocals.s16le"
        frames = decode_asr_evidence(args.ffmpeg, source, args.asr_pcm_samples, raw)
        if args.asr_evidence_cpu:
            separate_vocals(args.model, raw, frames, vocal, device="cpu")
        else:
            separate_vocals(args.model, raw, frames, vocal)
        encode_asr_evidence(args.ffmpeg, vocal, frames, args.asr_pcm_samples, pcm)
        pcm.rename(args.output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--stream", type=int)
    parser.add_argument("--ffmpeg", required=True, type=Path)
    parser.add_argument("--ffprobe", type=Path)
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--duration", type=float)
    parser.add_argument("--asr-pcm-samples", type=int)
    parser.add_argument("--asr-evidence-cpu", action="store_true")
    args = parser.parse_args()
    evidence = args.asr_pcm_samples is not None
    media_args = (args.stream, args.ffprobe, args.duration)
    if evidence and any(value is not None for value in media_args):
        parser.error("--asr-pcm-samples cannot be combined with media stream arguments")
    if not evidence and (args.asr_evidence_cpu or any(value is None for value in media_args)):
        parser.error("media separation requires --stream, --ffprobe and --duration")
    try:
        source = resolve_source(args.source)
        if not evidence and args.stream < 0:
            raise UnsupportedSource("Speech separation requires a selected audio track.")
        executables = (args.ffmpeg, args.model) if evidence else (args.ffmpeg, args.ffprobe, args.model)
        if any(not path.is_absolute() or not path.is_file() for path in executables):
            raise RuntimeError("Evidence requires absolute paths to FFmpeg and the local model." if evidence else
                               "Speech separation requires absolute paths to FFmpeg, ffprobe and the local model.")
        if (not args.output.is_absolute() or args.output.exists() or args.output.is_symlink()
                or not args.output.parent.is_dir()):
            raise RuntimeError("Speech separation requires a new output path in its temporary directory.")

        if evidence:
            separate_asr_evidence(args, source)
            return 0

        inspect_source(args.ffprobe, source, args.stream, args.duration)
        with tempfile.TemporaryDirectory(prefix="stem-", dir=args.output.parent) as temporary:
            directory = Path(temporary)
            raw, vocal, wave = directory / "mixture.f32", directory / "vocals.f32", directory / "vocals.wav"
            frames = decode_audio(args.ffmpeg, source, args.stream, args.duration, raw)
            separate_vocals(args.model, raw, frames, vocal)
            encode_wave(args.ffmpeg, vocal, wave)
            wave.rename(args.output)
    except UnsupportedSource as error:
        print(error, file=sys.stderr)
        return 3
    except (ImportError, OSError, RuntimeError, ValueError, subprocess.SubprocessError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""Run one LLPlayer diarization job with a local Community-1 pipeline."""

import argparse
from contextlib import redirect_stdout
import json
import math
import os
from pathlib import Path
import sys
import wave


SAMPLE_RATE = 16000
SAMPLE_BYTES = 2
MAX_TURNS = 32768
MAX_SPEAKER_LENGTH = 100
MAX_OUTPUT_BYTES = 4 * 1024 * 1024


def read_pcm(audio):
    with wave.open(str(audio), "rb") as wav:
        if (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) != (1, SAMPLE_BYTES, SAMPLE_RATE):
            raise ValueError("Audio must be mono 16 kHz signed 16-bit PCM WAV.")
        frames = wav.getnframes()
        if frames == 0:
            raise ValueError("The WAV file has no audio samples.")
        pcm = bytearray(wav.readframes(frames))
        if len(pcm) != frames * SAMPLE_BYTES:
            raise ValueError("The WAV audio is truncated.")
        return pcm, frames / SAMPLE_RATE


def serialize_turns(diarization, duration):
    turns = []
    for turn, speaker in diarization:
        start, end = float(turn.start), float(turn.end)
        if (
            not math.isfinite(start) or not math.isfinite(end) or end <= start
            or not isinstance(speaker, str) or not speaker.strip()
            or len(speaker) > MAX_SPEAKER_LENGTH
        ):
            raise ValueError("Community-1 returned an invalid speaker turn.")
        # Model frame windows can extend into the last chunk's zero padding.
        start, end = max(0.0, start), min(duration, end)
        if start >= end:
            continue
        if len(turns) == MAX_TURNS:
            raise ValueError("Community-1 returned too many speaker turns.")
        turns.append({"start": start, "end": end, "speaker": speaker})

    payload = json.dumps({"version": 1, "turns": turns}, allow_nan=False, separators=(",", ":"))
    if len(payload) + 1 > MAX_OUTPUT_BYTES:
        raise ValueError("Community-1 returned more than 4 MiB of speaker turns.")
    return payload


def diarize(model, audio, device):
    if not model.is_absolute() or not model.is_dir():
        raise ValueError("--model must name an existing absolute local model directory.")
    if not (model / "config.yaml").is_file():
        raise ValueError("The local Community-1 model directory must contain config.yaml.")

    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["PYANNOTE_METRICS_ENABLED"] = "0"
    if device == "cpu":
        os.environ["CUDA_VISIBLE_DEVICES"] = ""

    import torch

    if device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("CUDA is unavailable to the configured Python environment.")

    from pyannote.audio import Pipeline

    pcm, duration = read_pcm(audio)
    waveform = torch.frombuffer(pcm, dtype=torch.int16).to(torch.float32).div_(32768).unsqueeze(0)
    del pcm

    pipeline = Pipeline.from_pretrained(str(model), token=False)
    if pipeline is None:
        raise RuntimeError("Could not load the local Community-1 pipeline.")
    pipeline.to(torch.device(device))
    output = pipeline({"waveform": waveform, "sample_rate": SAMPLE_RATE})
    return serialize_turns(output.speaker_diarization, duration)


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--audio", type=Path, required=True)
    parser.add_argument("--device", choices=("cpu", "cuda"), required=True)
    args = parser.parse_args()

    # Reserve the result stream before importing Python or native dependencies.
    with os.fdopen(os.dup(sys.stdout.fileno()), "w", encoding="ascii", newline="\n") as result:
        os.dup2(sys.stderr.fileno(), sys.stdout.fileno())
        with redirect_stdout(sys.stderr):
            try:
                payload = diarize(args.model, args.audio, args.device)
            except Exception as error:
                print(f"Diarization failed ({type(error).__name__}): {error}", file=sys.stderr)
                return 1
        result.write(payload + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

import json
import os
from pathlib import Path
import runpy
import struct
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
import wave


WORKER = Path(__file__).with_name("sidecar.py")


class WorkerTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix="llplayer diarization ")
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        self.model = root / "local model"
        self.model.mkdir()
        (self.model / "config.yaml").touch()
        self.audio = root / "input audio.wav"
        with wave.open(str(self.audio), "wb") as wav:
            wav.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
            wav.writeframes(struct.pack("<3h", -32768, 0, 32767) * 16000)

    def run_worker(self, device="cpu", model=None, **extra_env):
        return subprocess.run(
            [
                sys.executable, "-I", "-B", __file__, "--fake-worker",
                "--model", str(model or self.model),
                "--audio", str(self.audio), "--device", device,
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=10,
            env={**os.environ, **extra_env},
        )

    def test_cpu_keeps_overlaps_and_sends_dependencies_diagnostics_to_stderr(self):
        result = self.run_worker()

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(result.stdout.splitlines()), 1)
        self.assertEqual(json.loads(result.stdout), {
            "version": 1,
            "turns": [
                {"start": 0, "end": 1.5, "speaker": "SPEAKER_00"},
                {"start": 1, "end": 3, "speaker": "SPEAKER_01"},
            ],
        })
        self.assertIn("python dependency log", result.stderr)
        self.assertIn("process dependency log", result.stderr)
        self.assertIn("pipeline on cpu", result.stderr)

    def test_cuda_fails_when_unavailable(self):
        result = self.run_worker(device="cuda")

        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertIn("CUDA", result.stderr)
        self.assertNotIn("python dependency log", result.stderr)

    def test_cuda_is_requested_explicitly(self):
        result = self.run_worker(device="cuda", FAKE_CUDA="1")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("pipeline on cuda", result.stderr)

    def test_remote_model_id_is_rejected(self):
        result = self.run_worker(model="pyannote/speaker-diarization-community-1")

        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertIn("absolute local model directory", result.stderr)

    def test_incomplete_local_model_is_rejected(self):
        (self.model / "config.yaml").unlink()
        result = self.run_worker()

        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertIn("config.yaml", result.stderr)

    def test_truncated_audio_is_rejected(self):
        with self.audio.open("r+b") as stream:
            stream.truncate(self.audio.stat().st_size - 1)
        result = self.run_worker()

        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertIn("truncated", result.stderr)

    def test_silence_returns_an_empty_result(self):
        result = self.run_worker(FAKE_TURNS="silence")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), {"version": 1, "turns": []})

    def test_invalid_model_turn_fails_without_partial_json(self):
        result = self.run_worker(FAKE_TURNS="invalid")

        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertIn("invalid speaker turn", result.stderr)


class FakeTensor:
    def __init__(self, buffer, dtype):
        assert isinstance(buffer, bytearray)
        assert struct.unpack_from("<3h", buffer) == (-32768, 0, 32767)
        assert dtype == "int16"

    def to(self, dtype):
        assert dtype == "float32"
        return self

    def div_(self, divisor):
        assert divisor == 32768
        return self

    def unsqueeze(self, dim):
        assert dim == 0
        return self


class FakePipeline:
    @classmethod
    def from_pretrained(cls, model, *, token):
        assert Path(model).is_absolute() and (Path(model) / "config.yaml").is_file()
        assert token is False
        assert os.environ["HF_HUB_OFFLINE"] == "1"
        assert os.environ["PYANNOTE_METRICS_ENABLED"] == "0"
        print("python dependency log", flush=True)
        os.write(1, b"process dependency log\n")
        return cls()

    def to(self, device):
        if device == "cpu":
            assert not os.environ["CUDA_VISIBLE_DEVICES"]
        print(f"pipeline on {device}", flush=True)

    def __call__(self, audio):
        assert isinstance(audio["waveform"], FakeTensor)
        assert audio["sample_rate"] == 16000
        turns = [(0, 1.5, "SPEAKER_00"), (1, 3.02, "SPEAKER_01"), (3, 3.5, "SPEAKER_02")]
        if os.environ.get("FAKE_TURNS") == "silence":
            turns = []
        if os.environ.get("FAKE_TURNS") == "invalid":
            turns.append((float("nan"), 2, "SPEAKER_03"))
        return SimpleNamespace(
            speaker_diarization=[
                (SimpleNamespace(start=start, end=end), speaker) for start, end, speaker in turns
            ],
            exclusive_speaker_diarization="wrong output",
        )


if __name__ == "__main__":
    if sys.argv[1:2] == ["--fake-worker"]:
        del sys.argv[1]
        sys.modules["torch"] = SimpleNamespace(
            cuda=SimpleNamespace(is_available=lambda: os.environ.get("FAKE_CUDA") == "1"),
            device=str,
            float32="float32",
            frombuffer=FakeTensor,
            int16="int16",
        )
        sys.modules["pyannote.audio"] = SimpleNamespace(Pipeline=FakePipeline)
        runpy.run_path(str(WORKER), run_name="__main__")
    else:
        unittest.main()

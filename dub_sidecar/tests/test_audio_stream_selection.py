import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest import mock


SERVER_PATH = Path(__file__).resolve().parents[1] / "server.py"
SERVER_SPEC = importlib.util.spec_from_file_location("llplayer_dub_sidecar_server", SERVER_PATH)
if SERVER_SPEC is None or SERVER_SPEC.loader is None:
    raise RuntimeError(f"Could not load dubbing sidecar module from {SERVER_PATH}")
server = importlib.util.module_from_spec(SERVER_SPEC)
SERVER_SPEC.loader.exec_module(server)


class DummyCodecContext:
    def __init__(self, sample_rate=48000, channels=2):
        self.sample_rate = sample_rate
        self.channels = channels


class DummyStream:
    def __init__(self, index, stream_type, sample_rate=48000, channels=2):
        self.index = index
        self.type = stream_type
        self.codec_context = DummyCodecContext(sample_rate, channels)


class DummyResampledFrame:
    def __init__(self, value):
        self.value = value

    def to_ndarray(self):
        return self.value


class DummyResampler:
    def resample(self, frame):
        return [DummyResampledFrame(frame)]


class DummyContainer:
    def __init__(self, streams, frames=(), decode_error=None):
        self.streams = streams
        self.frames = list(frames)
        self.decode_error = decode_error
        self.decode_args = None
        self.decode_kwargs = None
        self.closed = False

    def decode(self, *args, **kwargs):
        self.decode_args = args
        self.decode_kwargs = kwargs
        if self.decode_error is not None:
            raise self.decode_error
        return list(self.frames)

    def close(self):
        self.closed = True


class AudioStreamIndexValidationTests(unittest.TestCase):
    def test_accepts_zero_and_positive_integer(self):
        self.assertEqual(0, server._require_audio_stream_index({"audio_stream_index": 0}))
        self.assertEqual(3, server._require_audio_stream_index({"audio_stream_index": 3}))

    def test_rejects_missing_null_bool_string_float_and_negative(self):
        invalid_requests = {
            "missing": {},
            "null": {"audio_stream_index": None},
            "true": {"audio_stream_index": True},
            "false": {"audio_stream_index": False},
            "string": {"audio_stream_index": "3"},
            "float": {"audio_stream_index": 3.0},
            "negative": {"audio_stream_index": -1},
        }

        for name, request in invalid_requests.items():
            with self.subTest(name=name):
                with self.assertRaisesRegex(ValueError, "audio_stream_index"):
                    server._require_audio_stream_index(request)

    def test_assemble_validates_before_mock_or_real_branching(self):
        original_args = server.ARGS
        server.ARGS = SimpleNamespace(mock=True)
        try:
            with mock.patch.object(server, "assemble_mock") as assemble_mock:
                with mock.patch.object(server, "assemble_real") as assemble_real:
                    with self.assertRaisesRegex(ValueError, "audio_stream_index"):
                        server.assemble({})
                    assemble_mock.assert_not_called()
                    assemble_real.assert_not_called()
        finally:
            server.ARGS = original_args

    def test_assemble_preserves_zero_for_mock_request(self):
        original_args = server.ARGS
        server.ARGS = SimpleNamespace(mock=True)
        request = {"audio_stream_index": 0}
        try:
            with mock.patch.object(server, "assemble_mock", return_value={"ok": True}) as assemble_mock:
                self.assertEqual({"ok": True}, server.assemble(request))
                assemble_mock.assert_called_once_with(request)
        finally:
            server.ARGS = original_args


class AudioStreamSelectionTests(unittest.TestCase):
    @staticmethod
    def _resampler_factory(**_):
        return DummyResampler()

    def test_global_index_three_selects_second_audio_stream_not_audio_ordinal(self):
        first_audio = DummyStream(1, "audio")
        selected_audio = DummyStream(3, "audio", sample_rate=44100, channels=1)
        container = DummyContainer(
            [DummyStream(0, "video"), first_audio, DummyStream(2, "subtitle"), selected_audio],
            frames=["selected-frame"],
        )

        rate, channels, chunks = server._decode_selected_audio(
            container, 3, self._resampler_factory
        )

        self.assertEqual(44100, rate)
        self.assertEqual(1, channels)
        self.assertEqual(["selected-frame"], chunks)
        self.assertEqual((selected_audio,), container.decode_args)
        self.assertEqual({}, container.decode_kwargs)
        self.assertTrue(container.closed)

    def test_global_index_one_selects_first_audio_stream(self):
        selected_audio = DummyStream(1, "audio")
        container = DummyContainer([DummyStream(0, "video"), selected_audio, DummyStream(3, "audio")])

        server._decode_selected_audio(container, 1, self._resampler_factory)

        self.assertEqual((selected_audio,), container.decode_args)
        self.assertEqual({}, container.decode_kwargs)
        self.assertTrue(container.closed)

    def test_global_index_zero_is_valid(self):
        selected_audio = DummyStream(0, "audio")
        container = DummyContainer([selected_audio, DummyStream(1, "video")])

        server._decode_selected_audio(container, 0, self._resampler_factory)

        self.assertEqual((selected_audio,), container.decode_args)
        self.assertTrue(container.closed)

    def test_unknown_global_index_fails_closed_and_closes_container(self):
        container = DummyContainer([DummyStream(1, "audio"), DummyStream(3, "audio")])

        with self.assertRaisesRegex(RuntimeError, "audio stream index 2 was not found"):
            server._decode_selected_audio(container, 2, self._resampler_factory)

        self.assertIsNone(container.decode_args)
        self.assertTrue(container.closed)

    def test_non_audio_global_index_fails_closed_and_closes_container(self):
        container = DummyContainer([DummyStream(1, "audio"), DummyStream(3, "video")])

        with self.assertRaisesRegex(RuntimeError, "stream index 3 is not an audio stream"):
            server._decode_selected_audio(container, 3, self._resampler_factory)

        self.assertIsNone(container.decode_args)
        self.assertTrue(container.closed)

    def test_unknown_stream_type_fails_closed_and_closes_container(self):
        container = DummyContainer([DummyStream(3, None)])

        with self.assertRaisesRegex(RuntimeError, "stream index 3 is not an audio stream"):
            server._decode_selected_audio(container, 3, self._resampler_factory)

        self.assertIsNone(container.decode_args)
        self.assertTrue(container.closed)

    def test_decode_error_still_closes_container(self):
        selected_audio = DummyStream(3, "audio")
        container = DummyContainer([selected_audio], decode_error=RuntimeError("decode failed"))

        with self.assertRaisesRegex(RuntimeError, "decode failed"):
            server._decode_selected_audio(container, 3, self._resampler_factory)

        self.assertEqual((selected_audio,), container.decode_args)
        self.assertTrue(container.closed)


if __name__ == "__main__":
    unittest.main()

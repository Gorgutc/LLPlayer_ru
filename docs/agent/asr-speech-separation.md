# F-02-full speech separation

Local Avalonia batch transcription and detached ControlHost recognition can isolate vocals before ASR using the standalone vocal checkpoint from HTDemucs FT. The Recognition preference **Separate speech from music** defaults on. A saved off value bypasses the prepass. Whisper continues to use its usual decoding and VAD settings. The Lite denoiser remains a separate setting.

The first slice handles a selected stereo stream in a finite local file up to 30 minutes. It checks that the selected stream can be decoded on the original timeline. An unsupported input keeps the normal ASR route and emits a diagnostic. Batch's CPU-while-active setting also bypasses the CUDA prepass for the current file while the user is active. A processing failure in a configured runtime is reported as a job error. Direct playback ASR, WPF's recognition routes, alignment of existing SRT files, and per-job MCP overrides are outside this slice.

## Configure a runtime

Use a dedicated Python environment with Demucs 4.0.1, NumPy and a CUDA-enabled PyTorch binary compatible with the GPU. The restricted checkpoint loader requires PyTorch 2.6 or later. Install matching TorchAudio if required by that environment. Provide a local FFmpeg executable with the corresponding `ffprobe` executable beside it.

Set these variables in the environment used to start Avalonia or ControlHost. Each one takes a fully qualified path.

| Variable | File |
| --- | --- |
| `LLPLAYER_HTDEMUCS_PYTHON` | Python executable in the dedicated environment |
| `LLPLAYER_HTDEMUCS_CHECKPOINT` | Official `04573f0d-f3cf25b2.th` vocal checkpoint |
| `LLPLAYER_HTDEMUCS_FFMPEG` | FFmpeg executable |

The bundled runner accepts the [upstream vocal checkpoint](https://dl.fbaipublicfiles.com/demucs/hybrid_transformer/04573f0d-f3cf25b2.th) with SHA-256 `f3cf25b222c4eed7cd49dd8b2c9597d50c18bd154090f7b919cfa5f93cf22c49`. A runtime and checkpoint are provisioned separately from the player. With the runtime unconfigured, the selected source track goes to ASR directly. Recognition never downloads weights or packages.

The CUDA prepass uses the run's existing GPU admission. It exits before ASR reads the temporary stem. The selected input file is read-only. Intermediate and vocal files are kept in an owned temporary directory. Cancellation drains the process tree before the job releases admission and removes its files.

## Unsupported Faster Whisper cues

Local Avalonia batch and detached ControlHost jobs also apply the W-001 / W-002 v6b filter when Faster Whisper returns word timestamps on the original audio and speech separation is configured. The filter compares the engine's completed native JSON to the emitted cues and uses Silero on the original PCM, the aligned HTDemucs stem and normalized views. Speech evidence or unavailable coverage keeps the cue. A chunk boundary contributes context only when adjacent PCM from the same decode matches the sample and timestamp clocks. The filter uses bounded evidence under the existing GPU admission and skips GPU evidence while the batch is using its CPU-while-active policy.

A cue that is dropped is withheld from the new recognition document. Other cues keep the engine's order, text and timing. Omission review skips a chunk if it contains any dropped cue. A successful full-file F-02 prepass gives ASR the vocal track and leaves this post-filter inactive because the original view is unavailable on that chunk's clock. An explicit separation `OFF`, missing dependencies and disabled word timestamps also leave the original cue output in place. Existing subtitle files and profiles are outside this operation.

The accepted v6b comparison used identical full raw inputs. It dropped four of five music hallucinations in `07_06` and three of three in `07_67_02`, keeping the remaining `07_06` cue after a corroborating normalized-stem detection. It kept all 68 and 55 reference-matched words in the +10 dB and +12 dB speech-under-music controls. This residual cue is a documented limit of the filter.

## Evidence

The model choice comes from a same-source experiment on a 12:14 film with VAD on. Relative to the tested CPU MDX workflow, the CUDA vocal checkpoint reduced word deletions from 19 to 11 and caption edits from 50 to 46. The complete runs took 213.1 and 75.3 seconds, respectively. Both missed the original target cue. This experiment used one long ASR chunk and supports a model choice for initial integration.

# Local speaker diarization

LLPlayer Avalonia uses an external Python environment and a local pyannote
Community-1 model directory. Set both paths to enable diarization. The model
runs on your machine through the same worker on Windows and Linux. See the
[third-party notice](NOTICE.md) for upstream links and licenses.

## Prepare the environment

Use a separate 64-bit Python 3.12 virtual environment. Run the following from
the unpacked application directory. Choose the PyTorch wheel index that matches
your device. The CPU index works without a GPU. To use NVIDIA CUDA, substitute
a compatible CUDA index such as `https://download.pytorch.org/whl/cu128` and
use the CUDA device setting below. [PyTorch 2.8.0 wheel indexes](https://pytorch.org/get-started/previous-versions/#v280)

Windows PowerShell

```powershell
py -3.12 -m venv C:\LLPlayerDeps\diarization-venv
$env:LLPLAYER_DIARIZATION_PYTHON = 'C:\LLPlayerDeps\diarization-venv\Scripts\python.exe'
& $env:LLPLAYER_DIARIZATION_PYTHON -m pip install torch==2.8.0 torchaudio==2.8.0 --index-url https://download.pytorch.org/whl/cpu
& $env:LLPLAYER_DIARIZATION_PYTHON -m pip install -r .\Assets\Diarization\requirements.txt
& $env:LLPLAYER_DIARIZATION_PYTHON -m pip check
```

Linux shell

```sh
python3.12 -m venv "$HOME/llplayer-deps/diarization-venv"
export LLPLAYER_DIARIZATION_PYTHON="$HOME/llplayer-deps/diarization-venv/bin/python"
"$LLPLAYER_DIARIZATION_PYTHON" -m pip install torch==2.8.0 torchaudio==2.8.0 --index-url https://download.pytorch.org/whl/cpu
"$LLPLAYER_DIARIZATION_PYTHON" -m pip install -r ./Assets/Diarization/requirements.txt
"$LLPLAYER_DIARIZATION_PYTHON" -m pip check
```

The requirements file pins pyannote.audio and TorchCodec alongside the
PyTorch pair above. The worker passes decoded samples to pyannote.audio
in memory, so TorchCodec is an import dependency and LLPlayer supplies the
audio via its own media engine. [TorchCodec 0.7 compatibility](https://github.com/meta-pytorch/torchcodec/releases/tag/v0.7.0)

## Prepare the local model

Accept the access conditions on Hugging Face and follow the model card's
offline-use instructions. Provision a complete copy, including the files
managed by Git LFS, with `config.yaml` in the root. Use a trusted copy of the
official model. Record its Git revision and the virtual environment's
`pip freeze` output if you need to reproduce the setup. [Community-1 model card](https://huggingface.co/pyannote/speaker-diarization-community-1#offline-use)

Set the model path and start LLPlayer Avalonia from the configured session.
To launch from a desktop shortcut, make the same variables available in your
desktop session's environment. Set
`LLPLAYER_DIARIZATION_DEVICE=cuda` only for an environment with CUDA
available. The default is `cpu`.

Windows PowerShell

```powershell
$env:LLPLAYER_DIARIZATION_MODEL = 'C:\LLPlayerDeps\pyannote-speaker-diarization-community-1'
$env:LLPLAYER_DIARIZATION_DEVICE = 'cpu'
.\LLPlayer.Avalonia.exe
```

Linux shell

```sh
export LLPLAYER_DIARIZATION_MODEL="$HOME/llplayer-deps/pyannote-speaker-diarization-community-1"
export LLPLAYER_DIARIZATION_DEVICE=cpu
./llplayer.sh
```

The application never installs this environment or downloads your model.
The worker sets Hugging Face Hub offline mode and disables pyannote.audio
telemetry before importing the runtime. No model access token is required
when running from the local copy.

Open a local media file with a selected audio track and load text subtitles.
In **Recognition**, choose **Analyze speakers — primary** or **Analyze speakers
— secondary** for the subtitle slot to label. Assigned labels appear in the
subtitle sidebar. To export the labels, choose SRT and enable **Include
speaker labels (SRT only)** in the subtitle export dialog.

## Worker contract

LLPlayer starts the packaged `sidecar.py` with `--model <directory>`,
`--audio <WAV path>` and `--device cpu|cuda`. The WAV is 16 kHz signed
16-bit mono PCM. On success the worker prints one JSON document followed by
a newline.

```json
{"version":1,"turns":[{"start":0.5,"end":1.8,"speaker":"SPEAKER_00"}]}
```

Times are seconds from the start of the WAV. Turns from simultaneous speakers
can overlap. A successful result with no detected speech has an empty `turns`
array. The process writes diagnostics to standard error and exits with a
nonzero status on failure.

To check a provisioned runtime separately, use a local 16 kHz signed 16-bit
mono PCM WAV with known speech. Run the worker from the package directory with
network access disabled and use the configured interpreter and model. Check
for exit status zero and turns within the file's duration. The application
owns GPU coordination for its jobs. Use CPU when running this worker command
independently of the app.

```powershell
& $env:LLPLAYER_DIARIZATION_PYTHON -I -B .\Assets\Diarization\sidecar.py --model $env:LLPLAYER_DIARIZATION_MODEL --audio 'C:\Fixtures\conversation.wav' --device cpu
```

```sh
"$LLPLAYER_DIARIZATION_PYTHON" -I -B ./Assets/Diarization/sidecar.py --model "$LLPLAYER_DIARIZATION_MODEL" --audio /path/to/conversation.wav --device cpu
```

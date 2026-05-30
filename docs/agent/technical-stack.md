# Technical Stack

- Language: C# with nullable enabled where configured.
- Runtime: `.NET 10`, Windows target `net10.0-windows10.0.18362.0`.
- UI: WPF, Prism.DryIoc, MaterialDesignThemes.
- Media: FlyleafLib, FFmpeg bindings, DirectX/Vortice, MediaFoundation, XAudio2.
- ASR/OCR: Whisper.net runtimes, faster-whisper integration, TesseractOCR, Microsoft OCR paths.
- Translation: Google/Bing/Azure/DeepL/DeepLX/Ollama/LM Studio/OpenAI-like services.
- Tests: xUnit v3 and AwesomeAssertions.
- Packaging: GitHub Actions on `windows-latest`, .NET 10 SDK, publish profiles, 7-Zip archive.

No Node/web stack is part of the baseline.

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

## Local Development Environment (T-11)

The project **targets** `.NET 10` (`net10.0-windows10.0.18362.0`), and CI/release must build with the **.NET 10.0.x SDK** (see Packaging above). Local developer machines may differ, and that is expected:

- **No .NET 10 SDK is required locally to build.** The maintainer's machine has the .NET 8 and 9 SDKs plus a **.NET 11 preview SDK**, and the .NET 11 preview SDK builds the `net10.0` target fine. `scripts/codex/check-environment.ps1` warns ("`.NET SDK 10.0.x was not found`") when no 10.0.x SDK is present — this warning is non-fatal for local builds; CI is the authority for the pinned 10.0.x build.
- **Sandboxed `dotnet` can fail reading the Windows SDK.** When `dotnet build`/`restore`/`test` runs under a restricted sandbox, MSBuild may fail while reading the Windows SDK under `%LOCALAPPDATA%` / `AppData`. If a build fails for that reason, **request the approved escalation and rerun the exact same command** (this mirrors the note in `AGENTS.md` → Verification Gates). The failure is an environment/permission issue, not a code or dependency problem, so do not change project files in response to it.
- **Other local tooling quirks** (recorded so a fresh session does not rediscover them): use **PowerShell, not Bash**, for `dotnet` (PowerShell has no heredoc; pass multi-line `git commit` text via `git commit -F <file>` because the PS parser splits `-m` on embedded `"`); `dotnet publish` does **not** copy the tracked `FFmpeg/` folder, so a local publish/launch-test must `Copy-Item .\FFmpeg -Destination $publishDir -Recurse`; the Store `python` alias is a stub, so use **`py -3`**.

These are developer-environment notes only; the frozen build target and the pinned CI SDK in `dependency-baseline.md` are unchanged.

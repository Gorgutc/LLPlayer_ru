# Subagent Review Matrix

Use explicit spawned subagents. If no spawn tool is available, notify the user and do not claim `/review` has been satisfied.

## Path Scope To Agents

Rules are cumulative: a path receives every matching reviewer below. The generic extension rows are minimums for
new files; narrower product/runtime rows add domain reviewers and never replace those minimums.

- `**/*.cs`: `dotnet_quality_guardian`, `verification_reviewer`.
- `**/*.xaml`: `wpf_xaml_reviewer`, `verification_reviewer`.
- `**/*.xaml.cs`: `dotnet_quality_guardian`, `wpf_xaml_reviewer`, `verification_reviewer`.
- `LLPlayer/**/*.xaml`, `LLPlayer/Views/**`, `LLPlayer/Controls/**`, `LLPlayer/ViewModels/**`, `LLPlayer/Converters/**`, `LLPlayer/Themes/**`, `LLPlayer/Resources/**`: `wpf_xaml_reviewer`, `verification_reviewer`.
- `LLPlayer/Services/AppConfig.cs`, `FlyleafLib/Engine/Config.cs`, settings controls, key bindings: `dotnet_quality_guardian`, `instruction_drift_auditor`, `verification_reviewer`.
- `FlyleafLib/**/*.cs`, `FlyleafLib/**/*.xaml`: `media_runtime_mapper` in addition to the extension minimums.
- C#/XAML files under `FlyleafLib/Controls/WPF/**`, `FlyleafLib/Themes/**`, and `WpfColorFontDialog/**`: `wpf_xaml_reviewer` in addition to the extension and Flyleaf runtime minimums where applicable.
- `FlyleafLib/Engine/**`, `FlyleafLib/MediaPlayer/**`, `FlyleafLib/MediaFramework/**`: `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `FlyleafLib/Utils/**`: `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `FlyleafLib/Vad/**`: `media_runtime_mapper`, `dotnet_quality_guardian`, `native_dependency_auditor`, `packaging_release_reviewer`, `verification_reviewer`.
- `FlyleafLib/MediaPlayer/Translation/**`: `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `FlyleafLib/MediaPlayer/Dubbing/**`: `media_runtime_mapper`, `dotnet_quality_guardian`, `native_dependency_auditor`, `verification_reviewer`.
- `FlyleafLibTests/**`: `dotnet_quality_guardian`, `verification_reviewer`.
- `*.sln`, `*.slnx`, `*.csproj`, `Directory.Build.*`, `Directory.Packages.props`, `global.json`: `dotnet_quality_guardian`, `packaging_release_reviewer`, `verification_reviewer`.
- `dub_sidecar/**`: `media_runtime_mapper`, `native_dependency_auditor`, `packaging_release_reviewer`, `verification_reviewer`.
- `Plugins/YoutubeDL/**`: `media_runtime_mapper`, `packaging_release_reviewer`, `verification_reviewer`, plus the extension/project minimums.
- `FFmpeg/**`, `LLPlayer/lib/**`, `LLPlayer/Assets/**`, publish profiles, `.github/actions/build-package/action.yml`: `native_dependency_auditor`, `packaging_release_reviewer`, `verification_reviewer`.
- `.github/workflows/**`: `dotnet_quality_guardian`, `packaging_release_reviewer`, `verification_reviewer`.
- `docs/agent/product-behavior-contract.md`: `wpf_xaml_reviewer`, `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `docs/agent/wpf-design-contract.md`: `wpf_xaml_reviewer`, `verification_reviewer`.
- `docs/agent/media-runtime-contract.md`: `media_runtime_mapper`, `dotnet_quality_guardian`, `verification_reviewer`.
- `docs/agent/config-data-contract.md`: `dotnet_quality_guardian`, `instruction_drift_auditor`, `verification_reviewer`.
- `docs/agent/dependency-baseline.md`: `tech_stack_cartographer`, `native_dependency_auditor`, `packaging_release_reviewer`, `verification_reviewer`.
- `docs/agent/manual-smoke-matrix.md`: `wpf_xaml_reviewer`, `media_runtime_mapper`, `packaging_release_reviewer`, `verification_reviewer`.
- `docs/agent/dubbing-contract.md`, `docs/agent/dubbing/**`: `media_runtime_mapper`, `native_dependency_auditor`, `packaging_release_reviewer`, `verification_reviewer`.
- `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `RUN_INSTRUCTIONS.md`, `DO_NOT_PUSH.md`, `.codex/**`, `.agents/**`, `Plugins/llplayer-codex/**`, `docs/agent/**`, `scripts/codex/**`: `codex_infra_architect`, `instruction_drift_auditor`, `verification_reviewer`.

## Review Rules

- One agent should own one bounded question.
- Review findings are ordered by severity.
- Critical and Important findings must be fixed or explicitly accepted before handoff.
- `/review` means at least `verification_reviewer`; broad changes also need the relevant domain agent above.

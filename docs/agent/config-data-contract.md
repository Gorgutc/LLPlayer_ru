# Config And Data Contract

This document freezes current config, persistence, and local data decisions from `main`.

## Runtime Config Files

Runtime JSON files live beside the exe/base directory:

- `LLPlayer.PlayerConfig.json`
- `LLPlayer.Engine.json`
- `LLPlayer.Config.json`
- `crash.log`

They are user/local state and must not be committed.

## Load Order

1. `FlyleafLoader.StartEngine()` loads engine config and starts `Engine`.
2. `FlyleafLoader.CreateFlyleafPlayer()` loads player config and creates `Player`.
3. `FlyleafManager` loads app config and creates `AppActions`.

Do not reorder this without explicit migration work.

## Serialization

`AppConfig.GetJsonSerializerOptions()` uses indented JSON, string enums, typed interface converters for menu actions and translate settings, and color hex conversion. New polymorphic settings must be registered there or in the equivalent Flyleaf config serializer.

## Defaults And Migration

- Default engine keeps `PluginsPath`, `FFmpegPath`, HLS live seek, UI refresh, and FFmpeg filter load profile.
- Default player config passes HLS query options, leaves GPU adapter empty for later persistence, enables local subtitle search, and derives target translation language from original culture.
- Default custom key bindings are applied only for new config files. Exception: a newly-introduced custom action's default binding may be backfilled into an existing config when absent (e.g. `Ctrl+K` command palette), scoped to that single action so bindings the user removed for pre-existing actions are not re-added.
- Existing key bindings may be migrated through Flyleaf config `UpdateDefault()`.
- Batch subtitle UI defaults live in `LLPlayer.Config.json`: last folder, recursive scan, overwrite-existing policy, and the background-friendliness knobs `SerializeAsrAndTranslate` (default `true`), `RunOnCpuWhenActive` (default `true`), and `ActiveIdleThresholdSeconds` (default `45`), plus `PreferRussianAudio` (default `true`). All are additive/absent-defaulting. `PreferRussianAudio` makes batch transcribe a Russian-tagged audio track when present (so subtitles come out in Russian and translation is skipped); a per-file audio-track override in the list (`BatchSubtitleJob.AudioStreamIndexOverride`) is per-run UI state and is not persisted. Batch processing reuses ASR/Translate settings from player config snapshots and must not silently change `TranslateTargetLanguage` or Whisper translate settings in the live player config.
- Theme/appearance keys in `LLPlayer.Config.json`: `Theme.Mode` (default `Dark`; `Light`/`FollowOS` opt-in) and `AccentColorSync` (default `false`) are additive and absent-defaulting so existing configs are unchanged. `MicaBackdrop` defaults to `true` as of 0.3.2 (Win11 translucent backdrop on chrome/borders; gracefully no-ops on Windows 10 / non-Win11, never touches the DirectX video surface). New configs get it on via the property initializer; existing pre-0.3.2 configs are migrated once in `FlyleafManager.LoadAppConfig` (version-gated `< 0.3.2`, flipped on then persisted at the new version, so a user who later turns it off in Settings ▸ Themes is respected). The dark MaterialDesign2 theme remains the default appearance; Mica only affects the window backdrop.
- `AsrOnboardingShown` in `LLPlayer.Config.json` is additive and absent-defaulting (`false`); it is set once, when the first-run ASR onboarding hint is shown, and persisted via a load-modify-save of a fresh config instance so no transient live state is committed on that path.
- ASR/translation decoding defaults (player config). `WhisperCppConfig.NoContext` defaults to `true` for new configs; existing configs are migrated once through `Config.UpdateDefault()` (version gate `<= 0.3.0`, hence the `0.3.1` app version bump that makes the migration one-shot). The migration re-applies on load until the config is saved with the new version, after which a user who turns `NoContext` back off is respected. Translate LLM defaults are additive: `TemperatureManual` defaults `false` (new configs; existing saved values are preserved), local LLM backends (LM Studio/Ollama/KoboldCpp) default `TimeoutMs` to `180000` (raised from `60000` as of `0.3.9`: the overall `HttpClient` timeout is the whole-request budget, and a reasoning model can "think" well past a minute before emitting the translation, so `60000` cancelled the request mid-reasoning — a config still on the prior `60000` default is migrated once to `180000` via `Config.UpdateDefault()` (version gate `<= 0.3.8`, hence the `0.3.9` app version bump that makes the migration one-shot), while an explicit user value is preserved and new configs default to `180000` via the settings constructors; the value stays editable in Settings ▸ Translate). The `LiteLLM` and `OpenAILike` (OpenAI-compatible) providers deliberately keep the base `15000` default and are NOT migrated, because their endpoint may be a fast cloud proxy rather than a local reasoning model; a user fronting a local reasoning model through them can raise the timeout in Settings (a future task may extend the reasoning-headroom default to local-pointed endpoints). The local backends also apply a code-level, non-persisted `max_tokens` fallback cap so a degenerate reply fails fast. Frequency/presence penalties remain opt-in (`Manual=false`) and unchanged at defaults. The default chat-translation prompts (`TranslateChatConfig.PromptOneByOne`/`PromptKeepContext`) were rewritten for accuracy as of `0.3.5`; this is additive — a config whose saved prompt still equals the previous default is upgraded once via `Config.UpdateDefault()` (version gate `<= 0.3.4`), while a hand-edited prompt is preserved. As of `0.3.6` the default LLM chat method is `ContextWindow` (surrounding-line context plus an optional grammar pass): new configs default to it, and a config still on the prior default `KeepContext` is migrated once to `ContextWindow` via `Config.UpdateDefault()` (version gate `<= 0.3.5`, hence the `0.3.6` app version bump that makes the migration one-shot), while an explicit `OneByOne` is preserved. The new fields `PromptContextWindow`, `PromptGrammarCheck`, `ContextWindowBefore`/`ContextWindowAfter` (default `6`), and `GrammarCheckEnabled` (default `true`) are additive and absent-defaulting (existing configs pick up the new defaults on load; no migration). `FasterWhisperConfig.AntiHallucination` (default `true`) is likewise additive/absent-defaulting: it appends de-duplicated anti-hallucination decoding flags (`--condition_on_previous_text False`, `--no_speech_threshold 0.4`, `--vad_threshold 0.35`) to the faster-whisper command, only for flags the user has not already set in `ExtraArguments`, and needs no migration. `FasterWhisperConfig.Prompt` (default empty, since 0.3.11) is additive/absent-defaulting and passed as `--initial_prompt` (de-duplicated against `ExtraArguments`; an explicit `--initial_prompt` there wins) to bias the engine's language/script and casing.
- ASR re-segmentation defaults (player config, `Subtitles`). `ResegmentSubtitles` defaults `true` (additive/absent-defaulting, applies to interactive ASR, batch, and — as of 0.3.10 — loaded/sidecar/embedded text subtitles (split on line/character overflow only, not on duration); bitmap/PGS and styled ASS cues are excluded): a long Whisper cue is split into short, at-most-`SubtitleMaxLinesPerCue`-line cues (~`SubtitleMaxCharsPerLine` chars/line) with proportionally redistributed timings, and the translated text is wrapped to the same shape; cues that already fit are left untouched. Tunables `SubtitleMaxCharsPerLine` (**48** since 0.3.7), `SubtitleMaxLinesPerCue` (**3** since 0.3.7), `SubtitleMaxCjkCharsPerLine` (**24** since 0.3.7), `SubtitleMaxCueDurationSec` (**7.0** since 0.3.7), `SubtitleMinCueDurationSec` (1.0) are additive/absent-defaulting and exposed in Settings ▸ Subtitles ▸ Re-segmentation. The 0.3.7 cap raise (2→3 lines, slightly longer lines/duration) reduces a phrase being truncated when it spans cues; values still at the prior 0.3.5/0.3.6 default (2 / 42 / 21 / 6.0) are migrated once via `Config.UpdateDefault()` (version gate `<= 0.3.6`, hence the `0.3.7` app version bump), while a user-tuned value is preserved. Line wrapping uses a balanced multi-line packer for ≥3 lines; the 2-line path is unchanged. Line breaks are stored as `\n` in `SubtitleData.Text` and honored unless `SubsIgnoreLineBreak` is on. `FixAllCaps` (default `true`, since 0.3.11) is additive/absent-defaulting: it rewrites an ALL-CAPS generated (ASR) cue to sentence-case on the interactive ASR and batch paths only (not loaded/authored subs), before re-segmentation; only a predominantly-uppercase, 2+-word cue is changed.

## Actions And Key Bindings

- Custom app actions are registered by `ActionName`.
- Unknown action names should not crash config loading.
- Key matching requires exact key/modifier/enabled state.
- `IsKeyUp` actions are intentionally delayed until key-up, with a guard for missed key-up.
- Settings Keys edits the live key-binding list through an editable DataGrid. Preserve Add, Load, Apply, clone row, delete row, duplicate detection, Apply blocking on duplicates, grouped action selection, custom action names, key capture, and Enter commit.
- CheatSheet reads only enabled key bindings, groups them by action group, filters by action description or shortcut text, and can execute an action from the dialog.
- The Command Palette (`Ctrl+K`) reuses the same enabled-key-binding list as a flat, filterable dialog and runs the selected action's delegate (`ActionInternal`).

## User Data Directories

Current default user data paths include:

- `Recordings`
- `Snapshots`
- `whispermodels`
- `Whisper`
- `tesseractmodels/tessdata`
- `%TEMP%/LLPlayer/Subs`
- Batch-generated `video.ru.srt` files beside source videos

These are runtime/user data, not source artifacts.

## Secrets And Local Files

API keys, translation endpoints, local model paths, logs, dumps, runtime JSON, downloaded models, and downloaded `yt-dlp.exe` are not source code. Keep this aligned between `DO_NOT_PUSH.md`, `.gitignore`, and verifier scripts.

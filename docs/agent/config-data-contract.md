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
- Default custom key bindings are applied only for new config files.
- Existing key bindings may be migrated through Flyleaf config `UpdateDefault()`.

## Actions And Key Bindings

- Custom app actions are registered by `ActionName`.
- Unknown action names should not crash config loading.
- Key matching requires exact key/modifier/enabled state.
- `IsKeyUp` actions are intentionally delayed until key-up, with a guard for missed key-up.
- Settings Keys edits the live key-binding list through an editable DataGrid. Preserve Add, Load, Apply, clone row, delete row, duplicate detection, Apply blocking on duplicates, grouped action selection, custom action names, key capture, and Enter commit.
- CheatSheet reads only enabled key bindings, groups them by action group, filters by action description or shortcut text, and can execute an action from the dialog.

## User Data Directories

Current default user data paths include:

- `Recordings`
- `Snapshots`
- `whispermodels`
- `Whisper`
- `tesseractmodels/tessdata`
- `%TEMP%/LLPlayer/Subs`

These are runtime/user data, not source artifacts.

## Secrets And Local Files

API keys, translation endpoints, local model paths, logs, dumps, runtime JSON, downloaded models, and downloaded `yt-dlp.exe` are not source code. Keep this aligned between `DO_NOT_PUSH.md`, `.gitignore`, and verifier scripts.

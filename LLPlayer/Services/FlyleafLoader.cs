using System.IO;
using System.Windows;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;

namespace LLPlayer.Services;

public static class FlyleafLoader
{
    // HC-09: the command-palette (Ctrl+K) backfill is applied one-shot to configs predating this version; the version
    // stamp written on load then makes it never re-run, so a later intentional removal of the binding is respected.
    private static readonly Version PaletteBackfillVersion = new(0, 3, 45);

    public static void StartEngine()
    {
        EngineConfig engineConfig = DefaultEngineConfig();

        // Load Player's Config
        if (File.Exists(App.EngineConfigPath))
        {
            try
            {
                var opts = AppConfig.GetJsonSerializerOptions();
                engineConfig = EngineConfig.Load(App.EngineConfigPath, opts);
                if (engineConfig.Version != App.Version)
                {
                    // HC-10: non-fatal version-stamp save (own try/catch). A transient write failure must not
                    // brick startup with a false "Cannot load ..." + Environment.Exit(1); the config is valid.
                    engineConfig.Version = App.Version;
                    try
                    {
                        engineConfig.Save(App.EngineConfigPath, opts);
                    }
                    catch
                    {
                        // ignored: non-fatal — retry persist next launch
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot load EngineConfig from {Path.GetFileName(App.EngineConfigPath)}, Please review the settings or delete the config file. Error details are recorded in {Path.GetFileName(App.CrashLogPath)}.");
                try
                {
                    File.WriteAllText(App.CrashLogPath, "EngineConfig Loading Error: " + ex);
                }
                catch
                {
                    // ignored
                }

                // Fatal config-load failure: terminate immediately so execution does not continue
                // and build the engine/player on a throwaway default config during teardown.
                Environment.Exit(1);
            }
        }

        Engine.Start(engineConfig);
    }

    public static Player CreateFlyleafPlayer()
    {
        Config? config = null;
        bool useConfig = false;
        // HC-09: the config version as loaded, captured before the version-stamp below overwrites it, so the one-shot
        // command-palette backfill can tell whether this config predates the fix.
        string? loadedConfigVersion = null;

        // Load Player's Config
        if (File.Exists(App.PlayerConfigPath))
        {
            try
            {
                var opts = AppConfig.GetJsonSerializerOptions();
                config = Config.Load(App.PlayerConfigPath, opts);
                loadedConfigVersion = config.Version;

                if (config.Version != App.Version)
                {
                    // HC-10: non-fatal version-stamp save (own try/catch). A transient write failure must not
                    // brick startup with a false "Cannot load ..." + Environment.Exit(1); the config is valid.
                    config.Version = App.Version;
                    try
                    {
                        config.Save(App.PlayerConfigPath, opts);
                    }
                    catch
                    {
                        // ignored: non-fatal — retry persist next launch
                    }
                }
                useConfig = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot load PlayerConfig from {Path.GetFileName(App.PlayerConfigPath)}, Please review the settings or delete the config file. Error details are recorded in {Path.GetFileName(App.CrashLogPath)}.");
                try
                {
                    File.WriteAllText(App.CrashLogPath, "PlayerConfig Loading Error: " + ex);
                }
                catch
                {
                    // ignored
                }

                // Fatal config-load failure: terminate immediately so execution does not continue
                // and build the engine/player on a throwaway default config during teardown.
                Environment.Exit(1);
            }
        }

        config ??= DefaultConfig();
        Player player = new(config);

        if (!useConfig)
        {
            // Initialize default key bindings for custom keys for new config.
            foreach (var binding in AppActions.DefaultCustomActionsMap())
            {
                config.Player.KeyBindings.Keys.Add(binding);
            }
        }
        else
        {
            // Backfill ONLY the newly-added command-palette default (Ctrl+K) for returning users with an existing
            // config, so the shortcut works without resetting their config. HC-09 hardening:
            //  (1) One-shot version gate: the palette shipped inside 0.3.0 without a config version bump, so an
            //      un-gated backfill re-ran every launch and re-added the binding for users who intentionally removed
            //      it. Run only for configs predating this fix; the version stamp above then makes it never run again.
            //  (2) Chord-free guard: only add when the Ctrl+K chord is actually free. Otherwise, if the user reassigned
            //      Ctrl+K to another action, the backfill created a DUPLICATE chord that blocks Settings > Keys Apply.
            KeyBinding? paletteDefault = AppActions.DefaultCustomActionsMap()
                .FirstOrDefault(b => b.ActionName == nameof(CustomKeyBindingAction.OpenCommandPalette));
            if (paletteDefault != null &&
                KeyBindingBackfill.ShouldBackfill(
                    loadedConfigVersion, PaletteBackfillVersion, config.Player.KeyBindings.Keys, paletteDefault))
            {
                config.Player.KeyBindings.Keys.Add(paletteDefault);
            }
        }

        return player;
    }

    public static EngineConfig DefaultEngineConfig()
    {
        EngineConfig engineConfig = new()
        {
#if DEBUG
            PluginsPath = @":Plugins\bin\Plugins.NET10",
#else
            PluginsPath = ":Plugins",
#endif
            FFmpegPath = ":FFmpeg",
            FFmpegHLSLiveSeek = true,
            UIRefresh = true,
            FFmpegLoadProfile = Flyleaf.FFmpeg.LoadProfile.Filters,
#if DEBUG
            LogOutput = ":debug",
            LogLevel = LogLevel.Debug,
            FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Warn,
#endif
        };

        return engineConfig;
    }

    private static Config DefaultConfig()
    {
        Config config = new();
        config.Demuxer.FormatOptToUnderlying =
            true; // Mainly for HLS to pass the original query which might includes session keys
        config.Video.GPUAdapter = ""; // Set it empty so it will include it when we save it
        config.Subtitles.SearchLocal = true;
        config.Subtitles.TranslateTargetLanguage = Language.Get(Utils.OriginalCulture).ToTargetLanguage() ?? TargetLanguage.EnglishAmerican; // try to set native language

        return config;
    }
}

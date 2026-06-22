using System.IO;
using System.Windows;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;

namespace LLPlayer.Services;

public static class FlyleafLoader
{
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
                    engineConfig.Version = App.Version;
                    engineConfig.Save(App.EngineConfigPath, opts);
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

        // Load Player's Config
        if (File.Exists(App.PlayerConfigPath))
        {
            try
            {
                var opts = AppConfig.GetJsonSerializerOptions();
                config = Config.Load(App.PlayerConfigPath, opts);

                if (config.Version != App.Version)
                {
                    config.Version = App.Version;
                    config.Save(App.PlayerConfigPath, opts);
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
            // Backfill ONLY the newly-added command-palette default (Ctrl+K) for returning users with an
            // existing config, so the shortcut works without resetting their config. Scoped to this one new
            // action (reusing its exact default definition) to avoid re-adding bindings a user may have
            // intentionally removed for pre-existing actions.
            bool hasPalette = config.Player.KeyBindings.Keys.Any(k =>
                k.ActionName == nameof(CustomKeyBindingAction.OpenCommandPalette));
            if (!hasPalette)
            {
                KeyBinding? paletteDefault = AppActions.DefaultCustomActionsMap()
                    .FirstOrDefault(b => b.ActionName == nameof(CustomKeyBindingAction.OpenCommandPalette));
                if (paletteDefault != null)
                {
                    config.Player.KeyBindings.Keys.Add(paletteDefault);
                }
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

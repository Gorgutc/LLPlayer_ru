using System.IO;
using System.Windows;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using MaterialDesignThemes.Wpf;

namespace LLPlayer.Services;

public class FlyleafManager
{
    public Player Player { get; }
    public Config PlayerConfig => Player.Config;
    public FlyleafHost? FlyleafHost => Player.Host as FlyleafHost;
    public AppConfig Config { get; }
    public AppActions Action { get; }

    /// <summary>App-wide MaterialDesign snackbar queue (notifications + actionable config errors).</summary>
    public ISnackbarMessageQueue MessageQueue { get; }

    public AudioEngine AudioEngine => Engine.Audio;
    public EngineConfig ConfigEngine => Engine.Config;

    public FlyleafManager(Player player, IDialogService dialogService, ISnackbarMessageQueue messageQueue)
    {
        Player = player;
        MessageQueue = messageQueue;

        // Load app configuration at this time
        Config = LoadAppConfig();
        Action = new AppActions(Player, Config, dialogService);
    }

    private AppConfig LoadAppConfig()
    {
        AppConfig? config = null;

        if (File.Exists(App.AppConfigPath))
        {
            try
            {
                config = AppConfig.Load(App.AppConfigPath);

                if (config.Version != App.Version)
                {
                    // One-shot default migrations keyed on the config's previous version, applied before the
                    // version is stamped + saved below (so they run at most once per upgrade).
                    // 0.3.2: Mica backdrop now defaults ON. Flip it on for configs created before 0.3.2; the
                    // value is then persisted at 0.3.2, so a user who later turns it off is respected. An
                    // empty/unparseable version (configs from before version stamping was added) is treated as
                    // older than 0.3.2 so the flip is applied consistently rather than relying on the default.
                    if (!Version.TryParse(config.Version, out Version? prev) || prev < new Version(0, 3, 2))
                    {
                        config.MicaBackdrop = true;
                    }

                    config.Version = App.Version;
                    config.Save(App.AppConfigPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot load AppConfig from {Path.GetFileName(App.AppConfigPath)}, Please review the settings or delete the config file. Error details are recorded in {Path.GetFileName(App.CrashLogPath)}.");
                try
                {
                    File.WriteAllText(App.CrashLogPath, "AppConfig Loading Error: " + ex);
                }
                catch
                {
                    // ignored
                }

                // Fatal config-load failure: terminate immediately so execution does not continue
                // and initialize a throwaway default AppConfig during teardown.
                Environment.Exit(1);
            }
        }

        if (config == null)
        {
            config = new AppConfig();
        }
        config.Initialize(this);

        return config;
    }
}

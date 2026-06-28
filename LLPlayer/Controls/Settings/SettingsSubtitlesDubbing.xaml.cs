using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Extensions;
using LLPlayer.Services;

namespace LLPlayer.Controls.Settings;

public partial class SettingsSubtitlesDubbing : UserControl
{
    public SettingsSubtitlesDubbing()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<SettingsSubtitlesDubbingVM>();
    }
}

public class SettingsSubtitlesDubbingVM : Bindable
{
    public FlyleafManager FL { get; }

    /// <summary>
    /// Built-in preset bank for the voice picker. GPU-free (never starts the sidecar). Includes the
    /// currently-selected id even if it is not a known preset, so the ComboBox never blanks.
    /// </summary>
    public IReadOnlyList<TtsVoice> Voices { get; }

    public SettingsSubtitlesDubbingVM(FlyleafManager fl)
    {
        FL = fl;
        Voices = VoiceBankResolver.ForConfig(FL.PlayerConfig.Subtitles.DubbingConfig.DefaultVoiceId);
    }
}

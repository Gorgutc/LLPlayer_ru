using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FlyleafLib;
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
    /// Voice-picker ItemsSource: the built-in preset bank plus any user-declared custom voice ids, with the
    /// currently-selected id always present so the ComboBox never blanks. GPU-free (never starts the sidecar).
    /// Mutated surgically (never cleared — clearing would null the bound SelectedValue) as the list changes.
    /// </summary>
    public ObservableCollection<TtsVoice> Voices { get; } = new();

    /// <summary>Editable mirror of <see cref="DubbingConfig.CustomVoiceIds"/> shown in the list editor.</summary>
    public ObservableCollection<string> CustomVoiceIds { get; } = new();

    /// <summary>Text of the "add a custom voice id" input box.</summary>
    public string NewVoiceId { get; set => Set(ref field, value); } = "";

    /// <summary>Selected row in the custom-id list (the Remove target).</summary>
    public string? SelectedCustomVoiceId { get; set => Set(ref field, value); }

    public SettingsSubtitlesDubbingVM(FlyleafManager fl)
    {
        FL = fl;

        foreach (string id in DubbingConfig.CustomVoiceIds)
            CustomVoiceIds.Add(id);

        // Initial picker contents (runs before the view binds, so the selection is set up cleanly).
        foreach (TtsVoice voice in VoiceBankResolver.ForConfig(DubbingConfig.DefaultVoiceId, CustomVoiceIds))
            Voices.Add(voice);
    }

    private DubbingConfig DubbingConfig => FL.PlayerConfig.Subtitles.DubbingConfig;

    public DelegateCommand CmdAddVoiceId => field ??= new(() =>
    {
        string id = (NewVoiceId ?? "").Trim();
        NewVoiceId = "";

        if (id.Length == 0)
            return;

        bool alreadyDeclaredCustom = CustomVoiceIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (VoiceBankResolver.Resolve(id) is not null || alreadyDeclaredCustom)
            return;

        bool alreadyVisible = VoiceBankResolver.ContainsVoiceId(Voices, id);
        CustomVoiceIds.Add(id);
        PersistCustomVoiceIds();

        // Append to the picker without clearing it (keeps the active selection intact). If the id is already a
        // hand-edited selected placeholder, persisting it above is enough.
        if (!alreadyVisible)
            Voices.Add(VoiceBankResolver.CustomVoice(id));
    });

    public DelegateCommand CmdRemoveVoiceId => field ??= new(() =>
    {
        string? id = SelectedCustomVoiceId;
        if (id is null || !CustomVoiceIds.Remove(id))
            return;

        PersistCustomVoiceIds();

        // Drop it from the picker too, UNLESS it is the active dub voice — then keep it as a selectable
        // placeholder (mirrors VoiceBankResolver.ForConfig) so the selection never blanks.
        if (!string.Equals(id, DubbingConfig.DefaultVoiceId, StringComparison.OrdinalIgnoreCase))
        {
            TtsVoice? entry = Voices.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                Voices.Remove(entry);
        }
    });

    private void PersistCustomVoiceIds()
    {
        // Reassign so the config setter fires (NotifyPropertyChanged -> persistence).
        DubbingConfig.CustomVoiceIds = CustomVoiceIds.ToList();
    }
}

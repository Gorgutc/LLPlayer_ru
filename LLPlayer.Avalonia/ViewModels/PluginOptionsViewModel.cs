using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Detached YoutubeDL options matching WPF; Save applies through the controller mutation queue.</summary>
public sealed partial class PluginOptionsViewModel : ObservableObject
{
    readonly OnlineVideoSettings initial;
    readonly Func<OnlineVideoSettings, Task> save;
    [ObservableProperty] string extraArguments;
    [ObservableProperty] string maxVideoHeight;
    [ObservableProperty] bool preferVideoWithAudio;
    [ObservableProperty] bool busy;
    [ObservableProperty] string message = "";
    public event Action? Saved;

    public PluginOptionsViewModel(OnlineVideoSettings settings, Func<OnlineVideoSettings, Task> save)
    {
        initial = settings.Clone(); this.save = save;
        extraArguments = settings.ExtraArguments;
        maxVideoHeight = settings.MaxVideoHeight.ToString(CultureInfo.InvariantCulture);
        preferVideoWithAudio = settings.PreferVideoWithAudio;
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            if (!int.TryParse(MaxVideoHeight, NumberStyles.None, CultureInfo.InvariantCulture, out int height))
                throw new ArgumentException("Maximum video height must be a whole number of pixels; use 0 for the engine limit.");
            OnlineVideoSettings draft = initial.Clone();
            draft.ExtraArguments = ExtraArguments;
            draft.MaxVideoHeight = height;
            draft.PreferVideoWithAudio = PreferVideoWithAudio;
            draft.ValidateOptions();
            await save(draft);
            Busy = false;
            Saved?.Invoke();
        }
        catch (Exception error)
        {
            Message = error is ArgumentException or InvalidOperationException ? error.Message
                : "Plugin options could not be saved. Previous settings were preserved.";
        }
        finally { Busy = false; }
    }
}

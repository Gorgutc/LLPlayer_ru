using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.Plugins;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>A detached tool-path draft and one explicit URL admission; only saved paths authorize Open.</summary>
public sealed partial class OnlineVideoViewModel : ObservableObject
{
    readonly Func<OnlineVideoSettings, Task> save;
    readonly Action<string> open;
    OnlineVideoSettings saved;
    [ObservableProperty] string ytDlpPath;
    [ObservableProperty] string denoPath;
    [ObservableProperty] string url = "";
    [ObservableProperty] string message = "";
    [ObservableProperty] bool busy;
    [ObservableProperty] string? warning;
    public event Action? Opened;

    public OnlineVideoViewModel(OnlineVideoSettings settings, Func<OnlineVideoSettings, Task> save, Action<string> open, string? warning = null)
    {
        saved = settings.Clone();
        ytDlpPath = settings.YtDlpPath; denoPath = settings.DenoPath;
        this.save = save; this.open = open; this.warning = warning;
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var draft = saved.Clone();
            draft.YtDlpPath = YtDlpPath;
            draft.DenoPath = DenoPath;
            draft.Validate();
            await save(draft.Clone());
            saved = draft;
            Warning = null;
            Message = "Tool paths saved.";
        }
        catch (Exception e) { Message = e is ArgumentException or InvalidOperationException ? e.Message : "Tool paths could not be saved. Previous settings were preserved."; }
        finally { Busy = false; }
    }

    [RelayCommand]
    void Open()
    {
        if (Busy) return;
        try
        {
            saved.RequireTools();
            if (!OnlineVideoRequest.TryCreate(Url, out string request))
                throw new ArgumentException("Enter one absolute HTTP or HTTPS video URL.");
            open(request);
            Opened?.Invoke();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Message = e.Message; }
    }
}

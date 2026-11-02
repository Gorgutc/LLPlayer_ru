using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class PdicSettingsViewModel : ObservableObject
{
    readonly Func<PdicSettings, Task> save;
    readonly Func<string?> autoDetect;
    readonly Func<string, bool> executableExists;
    [ObservableProperty] string executablePath;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand)), NotifyCanExecuteChangedFor(nameof(AutoSetCommand)), NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    bool busy;
    [ObservableProperty] string message;
    public bool CanConfigure { get; }
    public event Action? Saved;
    public Func<Task<string?>>? PickExecutable { get; set; }
    public PdicSettingsViewModel(PdicSettings current, Func<PdicSettings, Task> save, bool? isWindows = null,
        Func<string?>? autoDetect = null, Func<string, bool>? executableExists = null)
    {
        this.save = save; this.autoDetect = autoDetect ?? FindDefaultPath;
        this.executableExists = executableExists ?? File.Exists;
        executablePath = current.ExecutablePath;
        CanConfigure = isWindows ?? OperatingSystem.IsWindows();
        message = CanConfigure ? "PDIC starts only when a single word is selected with the PDIC action. Set that action in Settings → Word."
            : "PDIC is available only on Windows. This Linux build does not start or replace the external PDIC dictionary.";
    }
    static string? FindDefaultPath()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PDIC-R Unicode for EIJIRO XI", "pdic-pipe.exe");
        return File.Exists(path) ? path : null;
    }
    bool CanEdit() => CanConfigure && !Busy;
    [RelayCommand(CanExecute = nameof(CanEdit))]
    void AutoSet()
    {
        if (!CanEdit()) return;
        try
        {
            string? found = autoDetect();
            if (found == null) Message = "pdic-pipe.exe was not found in the standard PDIC-R Unicode for EIJIRO XI folder. Choose its path manually.";
            else { ExecutablePath = found; Message = "Path found. Save to use it."; }
        }
        catch (Exception) { Message = "The standard PDIC path could not be checked. Choose its path manually."; }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    async Task BrowseAsync()
    {
        if (!CanEdit() || PickExecutable == null) return;
        Busy = true;
        try { if (await PickExecutable() is { } path) ExecutablePath = path; }
        catch (Exception) { Message = "The executable picker could not be opened."; }
        finally { Busy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    async Task SaveAsync()
    {
        if (!CanEdit()) return;
        Busy = true;
        bool completed = false;
        try
        {
            PdicSettings draft = new() { ExecutablePath = ExecutablePath.Trim() };
            draft.Validate();
            if (draft.ExecutablePath.Length > 0 && !executableExists(draft.ExecutablePath))
                throw new ArgumentException("Choose an existing pdic-pipe.exe, or leave the path empty to disable PDIC.");
            await save(draft.Clone());
            completed = true;
        }
        catch (Exception error)
        {
            Message = error is ArgumentException or InvalidOperationException ? error.Message
                : "PDIC settings could not be saved. Previous settings were preserved; retry after correcting the problem.";
        }
        finally { Busy = false; }
        if (completed) Saved?.Invoke();
    }
}

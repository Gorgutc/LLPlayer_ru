using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Session-only export choices; a pending picker never retains live cue or mutable settings references.</summary>
public sealed partial class SubtitlesExportViewModel : ObservableObject, IAsyncDisposable
{
    readonly ISubtitleExportSource source;
    CancellationTokenSource? operation;
    Task? active;
    bool stopping;
    [ObservableProperty] int selectedSlot;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SelectedTextIndex))] SubtitleExportText selectedText;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SelectedFormatIndex))] SubtitleExportFormat selectedFormat;
    [ObservableProperty] bool isUtf8Bom = true;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ExportCommand))] bool busy;
    [ObservableProperty] bool isPartial;
    [ObservableProperty] string message = "";
    public IReadOnlyList<string> TextOptions { get; } = ["Original", "Translated only", "Translated with fallback"];
    public IReadOnlyList<string> FormatOptions { get; } = ["SRT", "WebVTT", "TXT"];
    public int SelectedTextIndex { get => (int)SelectedText; set { if (value is >= 0 and <= 2) SelectedText = (SubtitleExportText)value; } }
    public int SelectedFormatIndex { get => (int)SelectedFormat; set { if (value is >= 0 and <= 2) SelectedFormat = (SubtitleExportFormat)value; } }
    public Func<SubtitleExportRequest, Task<string?>>? PickExportPath { get; set; }
    public Func<string, Task>? RevealExport { get; set; }
    public Func<string, Task>? ShowKnownError { get; set; }

    public SubtitlesExportViewModel(ISubtitleExportSource source)
    {
        this.source = source;
        RefreshPartialWarning();
    }
    partial void OnSelectedSlotChanged(int value) => RefreshPartialWarning();
    void RefreshPartialWarning()
    {
        if (!stopping && SelectedSlot is 0 or 1) IsPartial = source.Capture(SelectedSlot, SelectedText).IsLoading;
    }
    bool CanExport() => !Busy && !stopping;
    [RelayCommand(CanExecute = nameof(CanExport))]
    public Task ExportAsync()
    {
        if (!CanExport()) return Task.CompletedTask;
        Busy = true;
        operation = new();
        return active = RunAsync(operation);
    }
    async Task RunAsync(CancellationTokenSource mine)
    {
        try
        {
            int slot = SelectedSlot;
            SubtitleExportText text = SelectedText;
            SubtitleExportFormat format = SelectedFormat;
            bool bom = IsUtf8Bom;
            var snapshot = source.Capture(slot, text);
            IsPartial = snapshot.IsLoading;
            if (snapshot.Lines.Count == 0)
            {
                await KnownErrorAsync("There were no subtitles to export.");
                return;
            }
            string content = SubtitleExporter.Build(snapshot.Lines, format);
            string name = snapshot.SuggestedBaseName + (text == SubtitleExportText.Original ? "" : ".translated") + SubtitleExporter.Extension(format);
            var request = new SubtitleExportRequest(name, snapshot.InitialDirectory, format);
            if (PickExportPath == null) throw new IOException("A save dialog is not available.");
            string? path = await PickExportPath(request);
            mine.Token.ThrowIfCancellationRequested();
            if (path == null) { Message = "Export cancelled."; return; }
            await SaveAsync(path, content, bom, mine.Token);
            mine.Token.ThrowIfCancellationRequested();
            Message = snapshot.IsLoading ? "Export saved. ASR/OCR was running; only the captured cues were included." : "Export saved.";
            if (RevealExport != null)
            {
                try { await RevealExport(path); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    await KnownErrorAsync("The subtitles were saved, but the output folder could not be opened: " + e.Message);
                }
            }
        }
        catch (OperationCanceledException) { if (!stopping) Message = "Export cancelled."; }
        catch (Exception e) { if (!stopping) await KnownErrorAsync("Subtitle export failed: " + e.Message); }
        finally
        {
            mine.Dispose();
            operation = null;
            Busy = false;
        }
    }
    static async Task SaveAsync(string path, string content, bool bom, CancellationToken token)
    {
        // Stage beside the explicitly chosen output; a failed/cancelled write preserves an existing destination.
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(bom), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    async Task KnownErrorAsync(string error)
    {
        if (stopping) return;
        Message = error;
        if (ShowKnownError != null) await ShowKnownError(error);
    }
    [RelayCommand] void Cancel() => operation?.Cancel();
    public async ValueTask DisposeAsync()
    {
        stopping = true;
        ExportCommand.NotifyCanExecuteChanged();
        operation?.Cancel();
        if (active != null) await active;
    }
}
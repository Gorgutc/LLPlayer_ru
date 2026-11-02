using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Core.Batch;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>The queue has app lifetime; closing its idle window only discards the options draft.</summary>
public partial class BatchSubtitlesViewModel : ObservableObject, IAsyncDisposable
{
    readonly BatchSettingsStore store;
    readonly Func<string?, Task<string?>> pickFolder;
    readonly Func<string, Task> openMedia;
    readonly Func<string, Task<bool>> reveal;
    readonly Action? showDubbingSettings;
    readonly Func<IReadOnlyList<TtsVoice>>? getVoices;
    readonly Func<string>? getDefaultVoiceId;
    readonly Func<string, Task>? saveDefaultVoiceId;
    string? voiceBaseline;
    bool refreshingVoices;
    bool closing;
    Task pendingOptions = Task.CompletedTask;
    public BatchWorkflow Workflow { get; }
    public ObservableCollection<BatchSubtitleJob> Jobs => Workflow.Jobs;
    public ObservableCollection<BatchJobRow> Rows { get; } = [];
    [ObservableProperty] BatchSettings draft;
    [ObservableProperty] BatchSubtitleJob? selectedJob;
    [ObservableProperty] BatchJobRow? selectedRow;
    [ObservableProperty] TtsVoice? selectedVoice;
    [ObservableProperty] string warning = "";
    [ObservableProperty] string activityStatus = "CPU while active applies to faster-whisper only.";
    public ObservableCollection<TtsVoice> Voices { get; } = [];
    public bool HasVoicePicker => saveDefaultVoiceId != null;
    public string WatchButtonText => Workflow.WatchFolder ? "Stop watching" : "Start watching";
    public bool CanOpenSelection => SelectedJob != null;
    public bool? AllIncluded
    {
        get => Jobs.Count == 0 || Jobs.All(j => !j.Include) ? false : Jobs.All(j => j.Include) ? true : null;
        set { foreach (var job in Jobs) job.Include = value == true; OnPropertyChanged(); }
    }
    public BatchSubtitlesViewModel(BatchWorkflow workflow, BatchSettingsStore store,
        Func<string?, Task<string?>> pickFolder, Func<string, Task> openMedia, Func<string, Task<bool>> reveal,
        Action? showDubbingSettings = null, Func<IReadOnlyList<TtsVoice>>? getVoices = null,
        Func<string>? getDefaultVoiceId = null, Func<string, Task>? saveDefaultVoiceId = null)
    {
        Workflow = workflow; this.store = store; this.pickFolder = pickFolder; this.openMedia = openMedia; this.reveal = reveal;
        this.showDubbingSettings = showDubbingSettings; this.getVoices = getVoices; this.getDefaultVoiceId = getDefaultVoiceId;
        this.saveDefaultVoiceId = saveDefaultVoiceId; draft = workflow.Settings.Clone(); warning = store.LoadWarning ?? "";
        Workflow.Changed += OnWorkflowChanged;
        Jobs.CollectionChanged += OnJobsChanged;
        RebuildRows();
    }
    partial void OnSelectedJobChanged(BatchSubtitleJob? value) => OnPropertyChanged(nameof(CanOpenSelection));
    partial void OnSelectedRowChanged(BatchJobRow? value) => SelectedJob = value?.Job;
    bool voiceEdited;
    partial void OnSelectedVoiceChanged(TtsVoice? value)
    {
        if (!refreshingVoices) voiceEdited = value?.Id != voiceBaseline;
    }
    void OnJobsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) => RebuildRows();
    void RebuildRows()
    {
        var selected = SelectedJob;
        Rows.Clear();
        foreach (var group in Jobs.GroupBy(job => job.FolderDisplay))
        {
            bool first = true;
            foreach (var job in group)
            {
                var row = new BatchJobRow(job, first ? $"{group.Key} ({group.Count()} files)" : "");
                Rows.Add(row); first = false;
                if (ReferenceEquals(job, selected)) SelectedRow = row;
            }
        }
    }
    void OnWorkflowChanged() { OnPropertyChanged(nameof(AllIncluded)); OnPropertyChanged(nameof(WatchButtonText)); }
    public void Opened()
    {
        Draft = Workflow.Settings.Clone(); RefreshVoices(); Workflow.Open();
    }
    public void Closed() { Draft = Workflow.Settings.Clone(); Workflow.Closed(); }
    void RefreshVoices()
    {
        refreshingVoices = true;
        try
        {
        voiceBaseline = getDefaultVoiceId?.Invoke();
        Voices.Clear(); foreach (var voice in getVoices?.Invoke() ?? []) Voices.Add(voice);
        SelectedVoice = Voices.FirstOrDefault(v => v.Id == voiceBaseline);
        voiceEdited = false;
        }
        finally { refreshingVoices = false; }
    }
    Task<bool> CommitOptionsAsync(bool explicitSave = false)
    {
        Task previous = pendingOptions;
        Task<bool> operation = CommitOptionsAfterAsync(previous, explicitSave);
        pendingOptions = operation;
        return operation;
    }
    async Task<bool> CommitOptionsAfterAsync(Task previous, bool explicitSave)
    {
        await previous;
        if (closing) return false;
        return await CommitOptionsCoreAsync(explicitSave);
    }
    async Task<bool> CommitOptionsCoreAsync(bool explicitSave)
    {
        try
        {
            if (!explicitSave && !string.IsNullOrEmpty(Warning))
                throw new InvalidOperationException("Review the options and choose Save options to replace the unreadable settings file before running this operation.");
            BatchSettings candidate = Draft.Clone(); candidate.Validate();
            bool saveVoice = voiceEdited && saveDefaultVoiceId != null && SelectedVoice != null && SelectedVoice.Id != getDefaultVoiceId?.Invoke();
            // The voice owner guarantees persist-before-apply and preserves its state when its write fails.
            // Write batch first so a failed batch path never changes the voice; compensate batch if voice fails.
            BatchSettingsSaveReceipt? write = saveVoice ? store.SaveReversible(candidate) : null;
            if (!saveVoice) store.Save(candidate);
            try
            {
                if (saveVoice) await saveDefaultVoiceId!(SelectedVoice!.Id);
            }
            catch (Exception voiceError)
            {
                try { write!.Rollback(); }
                catch (Exception rollbackError) { throw new IOException($"{voiceError.Message} Batch settings rollback failed: {rollbackError.Message}", new AggregateException(voiceError, rollbackError)); }
                throw;
            }
            if (!closing) Workflow.ApplySettings(candidate);
            Warning = "";
            // Refresh from the owner after a successful commit. An untouched stale picker never writes its old value.
            RefreshVoices(); return true;
        }
        catch (Exception ex) { Workflow.SetMessage($"Cannot save batch options: {ex.Message}"); return false; }
    }
    [RelayCommand] async Task SaveOptionsAsync() { if (await CommitOptionsAsync(explicitSave: true)) Workflow.SetMessage("Batch options saved."); }
    [RelayCommand] async Task ToggleWatchAsync()
    {
        Draft.WatchFolder = !Workflow.WatchFolder; OnPropertyChanged(nameof(Draft));
        if (await CommitOptionsAsync()) Workflow.SetMessage(Workflow.WatchFolder ? "Watching for new videos. Copying files are queued after they become stable and readable." : "Folder watch stopped.");
    }
    [RelayCommand] async Task BrowseAsync()
    {
        try { if (await pickFolder(Draft.LastFolder) is { } folder) { Draft.LastFolder = folder; OnPropertyChanged(nameof(Draft)); } }
        catch (Exception ex) { Workflow.SetMessage(ex.Message); }
    }
    [RelayCommand] async Task ScanAsync() { if (await CommitOptionsAsync() && !closing) await Workflow.ScanAsync(); }
    [RelayCommand] async Task StartAsync() { if (await CommitOptionsAsync() && !closing) await Workflow.StartAsync(); }
    [RelayCommand] async Task RetryFailedAsync() { if (await CommitOptionsAsync() && !closing) await Workflow.RetryFailedAsync(); }
    [RelayCommand] async Task RetrySelectedAsync() { if (SelectedJob != null && await CommitOptionsAsync() && !closing) await Workflow.RetryAsync(SelectedJob); }
    [RelayCommand] async Task RetryJobAsync(BatchSubtitleJob? job) { if (job != null && await CommitOptionsAsync() && !closing) await Workflow.RetryAsync(job); }
    [RelayCommand] void Cancel() => Workflow.Cancel();
    [RelayCommand] void DubbingSettings() => showDubbingSettings?.Invoke();
    [RelayCommand] async Task OpenInPlayerAsync()
    { if (SelectedJob != null) try { await openMedia(SelectedJob.MediaPath); } catch (Exception ex) { Workflow.SetMessage(ex.Message); } }
    [RelayCommand] async Task OpenOutputFolderAsync()
    {
        string path = SelectedJob is { } selected ? Path.GetDirectoryName(selected.OutputPath) ?? Draft.LastFolder : Draft.LastFolder;
        try { if (!await reveal(path)) Workflow.SetMessage("The output folder could not be opened."); }
        catch (Exception ex) { Workflow.SetMessage(ex.Message); }
    }
    public async ValueTask DisposeAsync()
    {
        closing = true; Workflow.Changed -= OnWorkflowChanged; Jobs.CollectionChanged -= OnJobsChanged;
        await pendingOptions; await Workflow.DisposeAsync();
    }
}

public sealed class BatchJobRow(BatchSubtitleJob job, string folderHeader)
{
    public BatchSubtitleJob Job { get; } = job;
    public string FolderHeader { get; } = folderHeader;
    public bool HasFolderHeader => FolderHeader.Length > 0;
}

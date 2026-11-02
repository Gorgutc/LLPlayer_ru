using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Explicit, dialog-owned search/download lifetime. Credentials and provider sessions are never persisted.</summary>
public sealed partial class OpenSubtitlesDownloadViewModel : ObservableObject, IAsyncDisposable
{
    readonly IOpenSubtitlesClient client;
    readonly Func<bool> useUtf8Bom;
    readonly Func<SubtitlePrimaryLoadTarget>? capturePrimaryLoadTarget;
    readonly Func<IReadOnlyList<string>> preferredLanguages;
    readonly string? temporaryRoot;
    IPlaybackController? media;
    string? lastMediaTitle, lastMediaUrl;
    CancellationTokenSource? operation;
    Task? active;
    OpenSubtitlesCredentials? resultCredentials;
    string searchedQuery = "", searchedLanguage = "";
    bool stopping;
    Task? shutdown;

    public OpenSubtitlesDownloadViewModel(IOpenSubtitlesClient client, string initialQuery = "",
        Func<bool>? useUtf8Bom = null,
        Func<SubtitlePrimaryLoadTarget>? capturePrimaryLoadTarget = null, IPlaybackController? media = null,
        Func<IReadOnlyList<string>>? preferredLanguages = null, string? temporaryRoot = null)
    {
        this.client = client; this.useUtf8Bom = useUtf8Bom ?? (() => true);
        this.capturePrimaryLoadTarget = capturePrimaryLoadTarget;
        this.preferredLanguages = preferredLanguages ?? (() => []);
        this.temporaryRoot = temporaryRoot;
        Query = initialQuery;
        if (media != null) BindMedia(media);
    }
    [ObservableProperty] public partial string ApiKey { get; set; } = "";
    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SearchCommand))] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial string Language { get; set; } = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SearchCommand)), NotifyCanExecuteChangedFor(nameof(DownloadCommand)),
        NotifyCanExecuteChangedFor(nameof(NextPageCommand)), NotifyCanExecuteChangedFor(nameof(PreviousPageCommand)), NotifyCanExecuteChangedFor(nameof(LoadPrimaryCommand))]
    public partial bool Busy { get; private set; }
    [ObservableProperty] public partial string Message { get; private set; } = "Enter your OpenSubtitles.com account and application API key, then Search. Nothing is sent until you act.";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(DownloadCommand)), NotifyCanExecuteChangedFor(nameof(LoadPrimaryCommand))]
    public partial OpenSubtitlesResult? SelectedResult { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PageText)), NotifyCanExecuteChangedFor(nameof(PreviousPageCommand)), NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial int Page { get; private set; } = 1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PageText)), NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial int TotalPages { get; private set; } = 1;
    public string PageText => $"Page {Page} / {TotalPages}";
    public ObservableCollection<OpenSubtitlesResult> Results { get; } = [];
    public Func<string, Task<string?>>? PickSavePath { get; set; }
    public Func<string, Task>? ShowKnownError { get; set; }
    bool CanStart() => !Busy && !stopping;
    bool CanSearch() => CanStart() && !string.IsNullOrWhiteSpace(Query);
    bool CanDownload() => CanStart() && SelectedResult != null && Results.Contains(SelectedResult) && resultCredentials != null;
    bool CanLoadPrimary() => CanDownload() && capturePrimaryLoadTarget != null && (media?.IsOpened ?? true);
    bool CanPrevious() => CanStart() && Page > 1 && resultCredentials != null;
    bool CanNext() => CanStart() && Page < TotalPages && resultCredentials != null;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    public Task SearchAsync()
    {
        if (!CanSearch()) return Task.CompletedTask;
        OpenSubtitlesCredentials credentials = new(ApiKey.Trim(), Username.Trim(), Password);
        return SearchPageAsync(credentials, Query, Language, 1);
    }
    [RelayCommand(CanExecute = nameof(CanPrevious))]
    Task PreviousPageAsync() => resultCredentials == null ? Task.CompletedTask : SearchPageAsync(resultCredentials, searchedQuery, searchedLanguage, Page - 1);
    [RelayCommand(CanExecute = nameof(CanNext))]
    Task NextPageAsync() => resultCredentials == null ? Task.CompletedTask : SearchPageAsync(resultCredentials, searchedQuery, searchedLanguage, Page + 1);
    Task SearchPageAsync(OpenSubtitlesCredentials credentials, string query, string language, int page)
    {
        if (!CanStart()) return Task.CompletedTask;
        return Start(async cancellation =>
        {
            SelectedResult = null; Results.Clear(); resultCredentials = null;
            OpenSubtitlesSearchPage result = await client.SearchAsync(credentials, query, language, page, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (stopping) return;
            resultCredentials = credentials; searchedQuery = query; searchedLanguage = language;
            Page = result.Page; TotalPages = result.TotalPages;
            HashSet<string> preferred = preferredLanguages().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (OpenSubtitlesResult row in result.Results.OrderByDescending(row => preferred.Contains(row.Language))
                .ThenBy(row => row.LanguageName, StringComparer.CurrentCulture).ThenByDescending(row => row.DownloadCount)
                .ThenBy(row => row.FileName, StringComparer.CurrentCulture)) Results.Add(row);
            Message = Results.Count == 0 ? "No subtitles found. Try a different title or language." : $"{Results.Count} subtitle files found. Select a text subtitle to load or save.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanLoadPrimary))]
    public Task LoadPrimaryAsync()
    {
        if (!CanLoadPrimary() || SelectedResult is not { } result || resultCredentials is not { } credentials) return Task.CompletedTask;
        bool bom = useUtf8Bom();
        return Start(async cancellation =>
        {
            // Capture before HTTP/temp-file waits. A same-URL reopen cannot adopt the old operation's result.
            SubtitlePrimaryLoadTarget target = capturePrimaryLoadTarget!();
            OpenSubtitlesDownload downloaded = await client.DownloadAsync(credentials, result, cancellation);
            cancellation.ThrowIfCancellationRequested();
            OwnedSubtitleFile temporary = await OwnedSubtitleFile.CreateAsync(downloaded.FileName, downloaded.Utf8Bytes,
                bom, cancellation, temporaryRoot);
            bool transferred = false;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                transferred = await target(temporary, result.Language, cancellation);
                Message = transferred ? "Subtitle loaded in the Primary slot."
                    : "The media changed, so the downloaded subtitle was not loaded.";
            }
            finally
            {
                if (!transferred && !temporary.RetainedByPlayer && !temporary.TryDelete(out string? error))
                    throw new IOException("The temporary subtitle could not be removed: " + error);
            }
        });
    }

    /// <summary>Observe only media state/title. Refreshing the query never searches or probes the provider.</summary>
    public void BindMedia(IPlaybackController player)
    {
        UnbindMedia();
        if (stopping) return;
        media = player;
        media.PropertyChanged += MediaChanged;
        media.OpenCompleted += MediaOpened;
        media.ExternalMediaOpening += MediaOpening;
        RefreshMediaQuery();
    }
    void UnbindMedia()
    {
        if (media == null) return;
        media.PropertyChanged -= MediaChanged;
        media.OpenCompleted -= MediaOpened;
        media.ExternalMediaOpening -= MediaOpening;
        media = null;
    }
    void MediaChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IPlaybackController.Title) or nameof(IPlaybackController.Url) or nameof(IPlaybackController.IsOpened))
            RefreshMediaQuery();
    }
    void MediaOpened(string? url, bool subtitles, string? error)
    {
        if (!subtitles) { RetireMediaResults(); RefreshMediaQuery(); }
    }
    void MediaOpening() => RetireMediaResults();
    void RetireMediaResults()
    {
        operation?.Cancel(); SelectedResult = null; Results.Clear(); resultCredentials = null;
        LoadPrimaryCommand.NotifyCanExecuteChanged(); DownloadCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged(); NextPageCommand.NotifyCanExecuteChanged();
    }
    void RefreshMediaQuery()
    {
        if (media == null || stopping) return;
        string? title = media.Title, url = media.Url;
        if (title != lastMediaTitle || url != lastMediaUrl)
        {
            lastMediaTitle = title; lastMediaUrl = url;
            RetireMediaResults();
            if (!string.IsNullOrEmpty(title))
            {
                string extension = url != null && File.Exists(url) ? Path.GetExtension(url) : "";
                Query = extension.Length > 0 && title.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? title[..^extension.Length] : title;
            }
        }
        LoadPrimaryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    public Task DownloadAsync()
    {
        if (!CanDownload() || SelectedResult is not { } result || resultCredentials is not { } credentials) return Task.CompletedTask;
        bool bom = useUtf8Bom();
        return Start(async cancellation =>
        {
            if (PickSavePath == null) throw new IOException("A save dialog is not available on this desktop.");
            // The provider charges a download at POST /download, so a cancelled picker never consumes quota.
            string? path = await PickSavePath(result.FileName);
            cancellation.ThrowIfCancellationRequested();
            if (path == null) { Message = "Download cancelled."; return; }
            OpenSubtitlesDownload downloaded = await client.DownloadAsync(credentials, result, cancellation);
            cancellation.ThrowIfCancellationRequested();
            await SaveAsync(path, downloaded.Utf8Bytes, bom, cancellation);
            Message = "Subtitle saved: " + Path.GetFileName(path);
        });
    }
    Task Start(Func<CancellationToken, Task> action)
    {
        Busy = true; operation = new(); Message = "Working…";
        return active = RunAsync(action, operation);
    }
    async Task RunAsync(Func<CancellationToken, Task> action, CancellationTokenSource mine)
    {
        try { await action(mine.Token); }
        catch (OperationCanceledException) { if (!stopping) Message = "Operation cancelled."; }
        catch (Exception ex)
        {
            if (!stopping)
            {
                Message = ex is HttpRequestException or IOException or InvalidOperationException ? ex.Message : "The subtitle request could not finish.";
                if (ShowKnownError != null) await ShowKnownError(Message);
            }
        }
        finally
        {
            if (ReferenceEquals(operation, mine)) operation = null;
            mine.Dispose(); Busy = false;
        }
    }
    internal static async Task SaveAsync(string path, byte[] bytes, bool bom, CancellationToken cancellation)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, useAsync: true))
            {
                if (bom) await file.WriteAsync(Encoding.UTF8.GetPreamble(), cancellation);
                await file.WriteAsync(bytes, cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    [RelayCommand] void Cancel() => operation?.Cancel();
    public ValueTask DisposeAsync() => new(shutdown ??= StopAsync());
    async Task StopAsync()
    {
        stopping = true; UnbindMedia(); operation?.Cancel();
        if (active != null) await active;
        client.Dispose(); resultCredentials = null;
        ApiKey = Username = Password = "";
        SearchCommand.NotifyCanExecuteChanged(); DownloadCommand.NotifyCanExecuteChanged();
    }
}

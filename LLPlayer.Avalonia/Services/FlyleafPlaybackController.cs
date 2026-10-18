using FlyleafLib.MediaPlayer.Translation;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// <see cref="IPlaybackController"/> over a real FlyleafLib <see cref="Player"/>. Engine property changes arrive on
/// arbitrary threads and are re-raised on the UI thread (through <see cref="IUIDispatcher"/>); cross-thread engine
/// collections are read through <see cref="CollectionSyncBridge"/>.
/// </summary>
public sealed class FlyleafPlaybackController : IPlaybackController, IRecognitionController, IDisposable, IAsyncDisposable
{
    readonly Player player;
    readonly IUIDispatcher? ui;
    readonly CollectionSyncBridge bridge;
    readonly IDisposable?[] cueWatchers = new IDisposable?[2];
    readonly PropertyChangedEventHandler playerChanged, audioChanged, primaryDataChanged, secondaryDataChanged, primaryManagerChanged, secondaryManagerChanged, recognitionSlotChanged, subConfigChanged;
    readonly HashSet<SubtitleData>[] observedCues = [[], []];
    readonly int[] cueNotificationPending = new int[2];
    readonly PropertyChangedEventHandler[] cueChanged = new PropertyChangedEventHandler[2];
    bool sentenceAvailable;
    bool disposed;
    public RecognitionActions Recognition { get; }
    public RecognitionSettingsStore? RecognitionStore { get; set; }
    public string? RecognitionWarning { get; set; }
    public RecognitionSettings RecognitionSettings => RecognitionStore?.Load() ?? RecognitionSettings.FromConfig(player.Config.Subtitles);
    public string RecognitionStatus => $"ASR: {(player.IsASRRunning ? player.IsASRPaused ? "paused" : "running" : "idle")} · Primary: {(player.Subtitles[0].EnabledASR ? "ASR" : "subtitles")}, {(player.SubtitlesManager[0].IsLoading ? "loading" : "ready")}, {GetCues(0).Count} cues · Secondary: {(player.Subtitles[1].EnabledASR ? "ASR" : "subtitles")}, {(player.SubtitlesManager[1].IsLoading ? "loading" : "ready")}, {GetCues(1).Count} cues";
    public Task StartRecognitionAsync(int slot) => Recognition.StartAsync(slot);
    public Task CancelRecognitionAsync(int slot) => Recognition.CancelAsync(slot);
    public Task OcrRecognitionAsync(int slot) => Recognition.OcrAsync(slot);
    public Task ToggleRecognitionPauseAsync() => Recognition.TogglePauseAsync();

    public FlyleafPlaybackController(Player player, IUIDispatcher? ui, CollectionSyncBridge bridge)
    {
        this.player = player;
        this.ui = ui;
        this.bridge = bridge;
        Recognition = new RecognitionActions(new PlayerRecognitionBackend(player));

        playerChanged = (_, e) => OnPlayerChanged(e.PropertyName);
        audioChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(FlyleafLib.MediaPlayer.Audio.Volume) or nameof(FlyleafLib.MediaPlayer.Audio.Mute))
                Raise(e.PropertyName);
        };
        primaryDataChanged = (_, e) => OnSubsDataChanged(0, e.PropertyName);
        secondaryDataChanged = (_, e) => OnSubsDataChanged(1, e.PropertyName);
        primaryManagerChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(SubManager.CurrentIndex))
                Raise(nameof(PrimaryCueIndex));
            if (e.PropertyName == nameof(SubManager.IsLoading)) Raise(nameof(RecognitionStatus));
            if (e.PropertyName == nameof(SubManager.Language)) Raise("SubtitleLanguage");
        };
        secondaryManagerChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(SubManager.IsLoading)) Raise(nameof(RecognitionStatus));
            if (e.PropertyName == nameof(SubManager.Language)) Raise("SubtitleLanguage");
        };
        recognitionSlotChanged = (_, e) => { if (e.PropertyName == "EnabledASR") Raise(nameof(RecognitionStatus)); };
        subConfigChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(Config.SubConfig.EnabledTranslated))
            {
                if (!sentenceAvailable)
                    for (int slot = 0; slot < 2; slot++) player.Config.Subtitles[slot].EnabledTranslated = false;
                Raise(nameof(PrimaryTranslationEnabled)); Raise(nameof(SecondaryTranslationEnabled));
                RefreshOverlay(0); RefreshOverlay(1);
            }
            if (e.PropertyName == nameof(Config.SubConfig.Visible))
                Raise(nameof(SubtitlesVisible));
        };

        player.PropertyChanged += playerChanged;
        player.Audio.PropertyChanged += audioChanged;
        player.Subtitles[0].Data.PropertyChanged += primaryDataChanged;
        player.Subtitles[1].Data.PropertyChanged += secondaryDataChanged;
        player.SubtitlesManager[0].PropertyChanged += primaryManagerChanged;
        player.SubtitlesManager[1].PropertyChanged += secondaryManagerChanged;
        player.Subtitles[0].PropertyChanged += recognitionSlotChanged;
        player.Subtitles[1].PropertyChanged += recognitionSlotChanged;
        player.Config.Subtitles[0].PropertyChanged += subConfigChanged;
        player.Config.Subtitles[1].PropertyChanged += subConfigChanged;
        player.OpenCompleted += OnOpenCompleted;
        player.Opening += OnOpening;
        player.KnownErrorOccurred += OnKnownError;
        player.UnknownErrorOccurred += OnUnknownError;

        for (int i = 0; i < cueWatchers.Length; i++)
        {
            int slot = i;
            cueChanged[slot] = (_, e) =>
            {
                if (e.PropertyName is nameof(SubtitleData.DisplayText) or nameof(SubtitleData.UseTranslated)) QueueCueNotification(slot);
            };
            cueWatchers[i] = bridge.Watch(player.SubtitlesManager[slot].Subs, () => { WatchCues(slot); QueueCueNotification(slot); Raise(nameof(RecognitionStatus)); });
            WatchCues(slot);
        }
    }

    public Player Player => player;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<int>? CuesChanged;
    public event Action<string?, bool, string?>? OpenCompleted;
    public event Action<string>? ErrorOccurred;
    public event Action<string, string?, string?>? RecognitionError;

    public bool IsOpened => player.CanPlay;
    public bool IsPlaying => player.Status == Status.Playing;
    public bool IsOpening => player.Status == Status.Opening;
    public string? Title
    {
        get
        {
            string? title = player.Playlist?.Selected?.Title;
            if (!string.IsNullOrWhiteSpace(title))
                return title;
            string? url = Url;
            return url == null ? null : Path.GetFileName(url);
        }
    }
    public string? Url => player.Playlist?.Url;
    public long CurTime => player.CurTime;
    public long Duration => player.Duration;

    public double Speed
    {
        get => player.Speed;
        set => player.Speed = value;
    }

    public int VolumeMax => player.Config.Audio.VolumeMax;

    public int Volume
    {
        get => player.Audio.Volume;
        set => player.Audio.Volume = Math.Clamp(value, 0, VolumeMax);
    }

    public bool Mute
    {
        get => player.Audio.Mute;
        set => player.Audio.Mute = value;
    }

    public bool PrimaryTranslationEnabled => player.Config.Subtitles[0].EnabledTranslated;
    public bool SecondaryTranslationEnabled => player.Config.Subtitles[1].EnabledTranslated;
    public Task ToggleTranslationAsync(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        return Recognition.MutateAsync(() =>
        {
            if (!sentenceAvailable) throw new InvalidOperationException("Choose a sentence provider in Translation settings before enabling subtitle translation.");
            var manager = player.SubtitlesManager[slot];
            manager.Translator.SuspendAsync().GetAwaiter().GetResult();
            player.Config.Subtitles[slot].EnabledTranslated = !player.Config.Subtitles[slot].EnabledTranslated;
            manager.Translator.Resume();
            RefreshOverlay(slot);
        });
    }

    public Task SaveTranslationAsync(Func<Task> save) => Recognition.MutateAsync(() => save().GetAwaiter().GetResult());

    public void ApplyTranslationSettings(TranslationSettings settings)
    {
        SuspendTranslations();
        settings.Apply(player.Config.Subtitles);
        sentenceAvailable = settings.SentenceProvider != "Off";
        for (int slot = 0; slot < 2; slot++)
        {
            bool enabled = sentenceAvailable && player.Config.Subtitles[slot].EnabledTranslated;
            // Existing toggle seam resets sFramesPrev and refreshes paused overlay as well as sidebar cues.
            player.Config.Subtitles[slot].EnabledTranslated = false;
            player.Config.Subtitles[slot].EnabledTranslated = enabled;
            player.SubtitlesManager[slot].Translator.Resume();
            RefreshOverlay(slot);
        }
    }

    void SuspendTranslations()
        => Task.WhenAll(player.SubtitlesManager[0].Translator.SuspendAsync(), player.SubtitlesManager[1].Translator.SuspendAsync()).GetAwaiter().GetResult();

    Task MutateWithTranslationAsync(Action mutation, bool mediaChange = false)
        => MutateWithTranslationAsync(_ => mutation(), mediaChange);

    Task MutateWithTranslationAsync(Action<CancellationToken> mutation, bool mediaChange = false)
        => Recognition.MutateAsync(token =>
        {
            SuspendTranslations();
            try { mutation(token); }
            finally { for (int slot = 0; slot < 2; slot++) { player.SubtitlesManager[slot].Translator.Resume(); RefreshOverlay(slot); } }
        }, mediaChange);

    void WatchCues(int slot)
    {
        var current = bridge.Snapshot(player.SubtitlesManager[slot].Subs).ToHashSet();
        foreach (var cue in observedCues[slot].Except(current)) cue.PropertyChanged -= cueChanged[slot];
        foreach (var cue in current.Except(observedCues[slot])) cue.PropertyChanged += cueChanged[slot];
        observedCues[slot] = current;
    }

    void QueueCueNotification(int slot)
    {
        if (Interlocked.Exchange(ref cueNotificationPending[slot], 1) != 0) return;
        void Notify()
        {
            Interlocked.Exchange(ref cueNotificationPending[slot], 0);
            if (disposed) return;
            CuesChanged?.Invoke(slot);
            RefreshOverlay(slot);
        }
        if (ui == null) Notify(); else ui.Post(Notify);
    }

    void RefreshOverlay(int slot)
    {
        Post(() =>
        {
            if (disposed) return;
            var cue = player.SubtitlesManager[slot].GetCurrent();
            if (cue == null) return;
            if (cue.IsText) player.SubtitleDisplay(cue.DisplayText ?? "", slot, cue.UseTranslated);
        });
    }

    public bool PrimaryTextTranslated => player.Subtitles[0].Data.IsTranslated;
    public bool SecondaryTextTranslated => player.Subtitles[1].Data.IsTranslated;
    public string PrimaryText => player.Subtitles[0].Data.Text ?? "";
    public string SecondaryText => player.Subtitles[1].Data.Text ?? "";
    public SubtitleImage? PrimaryImage => ToImage(0);
    public SubtitleImage? SecondaryImage => ToImage(1);
    public int PrimaryCueIndex => player.SubtitlesManager[0].CurrentIndex;

    public bool SubtitlesVisible
    {
        get => player.Config.Subtitles[0].Visible;
        set
        {
            player.Config.Subtitles[0].Visible = value;
            player.Config.Subtitles[1].Visible = value;
        }
    }

    public IReadOnlyList<CueInfo> GetCues(int slot)
    {
        List<SubtitleData> subs = bridge.Snapshot(player.SubtitlesManager[slot].Subs);
        List<CueInfo> cues = new(subs.Count);
        foreach (SubtitleData s in subs)
            cues.Add(new CueInfo(s.Index, s.StartTime, s.EndTime, s.DisplayText ?? (s.IsBitmap ? "[image]" : ""), s.UseTranslated, player.SubtitlesManager[slot].Language?.ISO6391 ?? "", player.Config.Subtitles.TranslateTargetLanguage.ToISO6391()));
        return cues;
    }

    public IReadOnlyList<SubtitleTrack> GetSubtitleTracks()
    {
        List<SubtitleTrack> tracks = [];

        if (player.Subtitles.Streams is { } embedded)
        {
            foreach (SubtitlesStream s in bridge.Snapshot(embedded))
            {
                string label = $"#{s.StreamIndex} {LanguageName(s.Language)}{(string.IsNullOrWhiteSpace(s.Title) ? "" : " · " + s.Title)}{(s.IsBitmap ? " (image)" : "")}";
                tracks.Add(new SubtitleTrack(label, false, s.GetSubEnabled(0), s.GetSubEnabled(1), s));
            }
        }

        if (player.Playlist?.Selected?.ExternalSubtitlesStreamsAll is { } external)
        {
            foreach (ExternalSubtitlesStream s in bridge.Snapshot(external))
            {
                string name = string.IsNullOrWhiteSpace(s.Title) ? Path.GetFileName(s.Url ?? "") : s.Title;
                string lang = s.Language == null || s.Language == Language.Unknown ? "" : $" ({LanguageName(s.Language)})";
                tracks.Add(new SubtitleTrack(name + lang, true, s.GetSubEnabled(0), s.GetSubEnabled(1), s));
            }
        }

        return tracks;
    }

    public void SelectSubtitleTrack(int slot, SubtitleTrack? track)
        => Observe(MutateWithTranslationAsync(token =>
        {
            string subIndex = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (track == null) player.Commands.SubtitlesOffAction(subIndex);
            else PlayerRecognitionBackend.RunSubtitleCommand(player, track.Source, () => player.Commands.OpenSubtitlesAction(((object)subIndex, track.Source, (object)SelectSubMethod.Original)), token);
        }));

    public void Open(string url) => Observe(MutateWithTranslationAsync(() => player.Open(url), mediaChange: true));
    public void OpenSubtitles(string path) => Observe(MutateWithTranslationAsync(() => player.Open(path)));
    async void Observe(Task work)
    {
        try { await work; }
        catch (RecognitionActionCanceledException) { }
        catch (Exception e) { Post(() => { if (!disposed) ErrorOccurred?.Invoke(e.Message); }); }
    }
    public void Play() => player.Play();
    public void Pause() => player.Pause();
    public void TogglePlayPause() => player.TogglePlayPause();

    public void SeekTo(TimeSpan position)
    {
        long ms = Math.Max(0, (long)position.TotalMilliseconds);
        player.SeekAccurate((int)Math.Min(ms, int.MaxValue));
    }

    public bool KeyDown(WpfKey key) => Player.KeyDown(player, key);
    public bool KeyUp(WpfKey key) => Player.KeyUp(player, key);

    public string? GetSubtitleLanguage(int slot)
    {
        Language? lang = player.SubtitlesManager[slot].Language;
        return lang == null || lang == Language.Unknown ? null : lang.ISO6391;
    }

    public async ValueTask DisposeAsync()
    {
        await Recognition.DisposeAsync();
        await Task.WhenAll(player.SubtitlesManager[0].Translator.DisposeAsync().AsTask(), player.SubtitlesManager[1].Translator.DisposeAsync().AsTask());
        Dispose();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Recognition.Dispose();

        foreach (IDisposable? w in cueWatchers)
            w?.Dispose();

        player.PropertyChanged -= playerChanged;
        player.Audio.PropertyChanged -= audioChanged;
        player.Subtitles[0].Data.PropertyChanged -= primaryDataChanged;
        player.Subtitles[1].Data.PropertyChanged -= secondaryDataChanged;
        player.SubtitlesManager[0].PropertyChanged -= primaryManagerChanged;
        player.SubtitlesManager[1].PropertyChanged -= secondaryManagerChanged;
        player.Subtitles[0].PropertyChanged -= recognitionSlotChanged;
        player.Subtitles[1].PropertyChanged -= recognitionSlotChanged;
        player.Config.Subtitles[0].PropertyChanged -= subConfigChanged;
        player.Config.Subtitles[1].PropertyChanged -= subConfigChanged;
        for (int slot = 0; slot < 2; slot++) foreach (var cue in observedCues[slot]) cue.PropertyChanged -= cueChanged[slot];
        player.OpenCompleted -= OnOpenCompleted;
        player.Opening -= OnOpening;
        player.KnownErrorOccurred -= OnKnownError;
        player.UnknownErrorOccurred -= OnUnknownError;
    }

    static string LanguageName(Language? lang)
        => lang == null || lang == Language.Unknown ? "Unknown" : lang.TopEnglishName ?? lang.ToString();

    SubtitleImage? ToImage(int slot)
    {
        SubsBitmap? bmp = player.Subtitles[slot].Data.Bitmap;
        if (bmp?.Source == null)
            return null;

        SubManager manager = player.SubtitlesManager[slot];
        return new SubtitleImage(bmp.Source, bmp.X, bmp.Y, bmp.Width, bmp.Height, manager.Width, manager.Height);
    }

    void OnPlayerChanged(string? name)
    {
        if (name is nameof(Player.IsASRRunning) or nameof(Player.IsASRPaused)) Raise(nameof(RecognitionStatus));
        switch (name)
        {
            case nameof(Player.CanPlay):
                Raise(nameof(IsOpened));
                break;
            case nameof(Player.Status):
                Raise(nameof(IsPlaying));
                Raise(nameof(IsOpening));
                break;
            case nameof(Player.CurTime):
                Raise(nameof(CurTime));
                break;
            case nameof(Player.Duration):
                Raise(nameof(Duration));
                break;
            case nameof(Player.Speed):
                Raise(nameof(Speed));
                break;
        }
    }

    void OnSubsDataChanged(int slot, string? name)
    {
        switch (name)
        {
            case nameof(SubsData.IsTranslated):
                Raise(slot == 0 ? nameof(PrimaryTextTranslated) : nameof(SecondaryTextTranslated));
                break;
            case nameof(SubsData.Text):
                Raise(slot == 0 ? nameof(PrimaryTextTranslated) : nameof(SecondaryTextTranslated));
                Raise(slot == 0 ? nameof(PrimaryText) : nameof(SecondaryText));
                break;
            case nameof(SubsData.Bitmap):
                Raise(slot == 0 ? nameof(PrimaryImage) : nameof(SecondaryImage));
                break;
        }
    }

    void OnOpening(object? sender, OpeningArgs e)
    {
        // Native engine shortcuts open asynchronously, outside the app mutation queue. Reject the old media generation.
        if (!e.IsSubtitles) Recognition.Invalidate();
    }

    void OnOpenCompleted(object? sender, OpenCompletedArgs? e)
    {
        if (e == null)
            return;

        string? url = e.Url;
        bool isSubs = e.IsSubtitles;
        string? error = e.Error;
        Post(() =>
        {
            Raise(nameof(Title));
            Raise(nameof(Url));
            Raise(nameof(IsOpened));
            OpenCompleted?.Invoke(url, isSubs, error);
        });
    }

    void OnKnownError(object? sender, KnownErrorOccurredEventArgs e)
    {
        string message = e.Message;
        Post(() =>
        {
            if (disposed) return;
            if (e.SettingsTab is KnownErrorSettingsTab.SubtitlesASR or KnownErrorSettingsTab.SubtitlesOCR)
                RecognitionError?.Invoke(message, e.SettingsTab, e.ActionHint);
            else ErrorOccurred?.Invoke(message);
        });
    }

    void OnUnknownError(object? sender, UnknownErrorOccurredEventArgs e)
    {
        string message = e.Message;
        Post(() => ErrorOccurred?.Invoke(message));
    }

    void Post(Action action)
    {
        if (ui == null || ui.CheckAccess())
            action();
        else
            ui.Post(action);
    }

    void Raise([CallerMemberName] string? name = null)
        => Post(() =>
        {
            if (!disposed)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        });
}

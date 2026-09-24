using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using LLPlayer.Avalonia.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>A radio item of the Subtitles ▸ Primary / Secondary track menus.</summary>
public sealed class TrackMenuItem(string label, bool isSelected, IRelayCommand command)
{
    public string Label { get; } = label;
    public bool IsSelected { get; } = isSelected;
    public IRelayCommand Command { get; } = command;
}

/// <summary>A speed entry of the transport bar's speed menu.</summary>
public sealed class SpeedMenuItem(double speed, bool isSelected, IRelayCommand command)
{
    public double Speed { get; } = speed;
    public string Label { get; } = speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";
    public bool IsSelected { get; } = isSelected;
    public IRelayCommand Command { get; } = command;
}

/// <summary>
/// Main window state: transport bar, dual subtitle overlay, sidebar, word popup, menus, shortcuts, toasts and
/// preferences. Talks to the engine only through <see cref="IPlaybackController"/> and to the window through
/// <see cref="IAppShell"/>, so all of it is testable headlessly with fakes.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public static readonly double[] Speeds = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan SeekHold = TimeSpan.FromSeconds(1.5);
    const double SubtitleSizeStep = 2;
    const double SubtitlePositionStep = 8;

    readonly IPlaybackController player;
    readonly AppPrefs prefs;
    readonly Action<AppPrefs> savePrefs;
    readonly IAppShell shell;
    readonly TimeProvider time;
    readonly List<string> pendingSubtitles = [];
    DateTimeOffset lastActivity;
    bool updatingFromPlayer;
    double seekTarget = -1;
    DateTimeOffset seekHoldUntil;
    bool disposed;

    public MainWindowViewModel(IPlaybackController player, AppPrefs prefs, Action<AppPrefs> savePrefs, IWordTranslator translator,
        IAppShell shell, TimeProvider? time = null)
    {
        this.player = player;
        this.prefs = prefs;
        this.savePrefs = savePrefs;
        this.shell = shell;
        this.time = time ?? TimeProvider.System;
        lastActivity = this.time.GetUtcNow();

        Sidebar = new SubtitlesSidebarViewModel(player);
        WordPopup = new WordPopupViewModel(translator, text => shell.SetClipboardText(text));
        Shortcuts = new AppShortcutMap(new Dictionary<string, Action>
        {
            [AppShortcutMap.ToggleSidebar] = ToggleSidebar,
            [AppShortcutMap.ActivateSubsSearch] = ActivateSubsSearch,
            [AppShortcutMap.OpenWindowCheatSheet] = ShowCheatSheet,
            [AppShortcutMap.SubsSizeIncrease] = IncreaseSubtitleSize,
            [AppShortcutMap.SubsSizeDecrease] = DecreaseSubtitleSize,
            [AppShortcutMap.SubsPositionUp] = SubtitlesUp,
            [AppShortcutMap.SubsPositionDown] = SubtitlesDown,
            [AppShortcutMap.SubsPrimaryTextCopy] = CopyPrimaryText,
            [AppShortcutMap.OpenSubtitles] = () => _ = OpenSubtitlesAsync(),
            [AppShortcutMap.ToggleTheme] = ToggleTheme,
        });

        foreach (string recent in prefs.RecentFiles)
            RecentFiles.Add(recent);
        RecentFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentFiles));

        SubtitleFontSize = prefs.SubtitleFontSize;
        SidebarVisible = prefs.SidebarVisible;
        SidebarWidth = prefs.SidebarWidth;
        IsDarkTheme = prefs.Theme != "Light";

        player.Volume = Math.Min(prefs.Volume, player.VolumeMax);
        player.Mute = prefs.Mute;
        player.SubtitlesVisible = prefs.SubtitlesVisible;

        player.PropertyChanged += OnPlayerPropertyChanged;
        player.CuesChanged += OnCuesChanged;
        player.OpenCompleted += OnOpenCompleted;
        player.ErrorOccurred += OnPlayerError;

        SyncAllFromPlayer();
        RebuildSpeedItems();
    }

    public SubtitlesSidebarViewModel Sidebar { get; }
    public WordPopupViewModel WordPopup { get; }
    public AppShortcutMap Shortcuts { get; }
    public IPlaybackController Player => player;
    public AppPrefs Prefs => prefs;

    public ObservableCollection<ToastItem> Toasts { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];
    public ObservableCollection<TrackMenuItem> PrimaryTracks { get; } = [];
    public ObservableCollection<TrackMenuItem> SecondaryTracks { get; } = [];
    public ObservableCollection<SpeedMenuItem> SpeedItems { get; } = [];

    // ---- media / transport -------------------------------------------------------------------------------------

    [ObservableProperty] public partial bool IsOpened { get; private set; }
    [ObservableProperty] public partial bool IsPlaying { get; private set; }
    [ObservableProperty] public partial bool IsOpening { get; private set; }
    [ObservableProperty] public partial string WindowTitle { get; private set; } = "LLPlayer";
    [ObservableProperty] public partial string MediaTitle { get; private set; } = "";
    [ObservableProperty] public partial string CurrentTimeText { get; private set; } = "0:00";
    [ObservableProperty] public partial string DurationText { get; private set; } = "0:00";
    [ObservableProperty] public partial double SeekMaximum { get; private set; } = 1;
    [ObservableProperty] public partial string SpeedText { get; private set; } = "1×";
    [ObservableProperty] public partial bool IsMuted { get; private set; }
    [ObservableProperty] public partial bool IsFullScreen { get; private set; }
    [ObservableProperty] public partial bool ControlsVisible { get; private set; } = true;
    [ObservableProperty] public partial bool CursorHidden { get; private set; }
    [ObservableProperty] public partial bool IsDragOver { get; set; }
    [ObservableProperty] public partial bool IsDarkTheme { get; private set; }

    public bool ShowEmptyState => !IsOpened && !IsOpening;
    public bool HasRecentFiles => RecentFiles.Count > 0;
    public int VolumeMax => player.VolumeMax;
    public bool IsSeeking { get; private set; }

    /// <summary>Seek bar value in seconds. User changes seek (immediately, or on release while dragging).</summary>
    public double SeekValue
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || updatingFromPlayer)
                return;

            if (IsSeeking)
                CurrentTimeText = TimeFormat.Clock(TimeSpan.FromSeconds(value));
            else
                SeekToSeconds(value);
        }
    }

    public int Volume
    {
        get;
        set
        {
            value = Math.Clamp(value, 0, player.VolumeMax);
            if (!SetProperty(ref field, value) || updatingFromPlayer)
                return;

            player.Volume = value;
            if (value > 0 && player.Mute)
                player.Mute = false;
            prefs.Volume = value;
        }
    }

    // ---- subtitles overlay -------------------------------------------------------------------------------------

    [ObservableProperty] public partial string PrimaryText { get; private set; } = "";
    [ObservableProperty] public partial string SecondaryText { get; private set; } = "";
    [ObservableProperty] public partial bool PrimaryVisible { get; private set; }
    [ObservableProperty] public partial bool SecondaryVisible { get; private set; }
    [ObservableProperty] public partial SubtitleImage? PrimaryImage { get; private set; }
    [ObservableProperty] public partial SubtitleImage? SecondaryImage { get; private set; }
    [ObservableProperty] public partial bool SubtitlesVisible { get; private set; } = true;
    [ObservableProperty] public partial double SubtitleFontSize { get; private set; }
    [ObservableProperty] public partial double SecondaryFontSize { get; private set; }
    [ObservableProperty] public partial double SubtitleBottomOffset { get; private set; } = 24;

    // ---- sidebar -----------------------------------------------------------------------------------------------

    [ObservableProperty] public partial bool SidebarVisible { get; private set; }
    [ObservableProperty] public partial double SidebarWidth { get; set; }

    partial void OnSubtitleFontSizeChanged(double value)
    {
        SecondaryFontSize = Math.Round(value * prefs.SecondaryFontScale, 1);
        prefs.SubtitleFontSize = value;
    }

    partial void OnSidebarWidthChanged(double value)
    {
        if (double.IsFinite(value) && value > 0)
            prefs.SidebarWidth = value;
    }

    partial void OnIsOpenedChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));
    partial void OnIsOpeningChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    // ---- commands ----------------------------------------------------------------------------------------------

    [RelayCommand]
    public async Task OpenMediaAsync()
    {
        string? path = await PickAsync(shell.PickMediaFileAsync);
        if (path != null)
            Open(path);
    }

    [RelayCommand]
    public async Task OpenSubtitlesAsync()
    {
        string? path = await PickAsync(shell.PickSubtitlesFileAsync);
        if (path != null)
            OpenSubtitlesFile(path);
    }

    async Task<string?> PickAsync(Func<string?, Task<string?>> picker)
    {
        try
        {
            return await picker(InitialFolder());
        }
        catch (Exception ex)
        {
            // No portal / GTK file chooser on this system: explain the alternatives instead of failing silently.
            ShowToast("No file picker available", ex.Message + " (drop files on the window, pass a path on the command line, "
                      + "or start with LLPLAYER_MANAGED_DIALOGS=1)", ToastVariant.Destructive);
            return null;
        }
    }

    [RelayCommand]
    void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        if (!path.Contains("://", StringComparison.Ordinal) && !File.Exists(path))
        {
            ShowToast("File not found", path, ToastVariant.Destructive);
            prefs.RecentFiles.Remove(path);
            RecentFiles.Remove(path);
            SavePrefs();
            return;
        }
        Open(path);
    }

    [RelayCommand]
    void ClearRecent()
    {
        prefs.RecentFiles.Clear();
        RecentFiles.Clear();
        SavePrefs();
    }

    [RelayCommand]
    void Exit() => shell.Exit();

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (IsOpened)
            player.TogglePlayPause();
    }

    [RelayCommand]
    public void ToggleMute()
    {
        player.Mute = !player.Mute;
    }

    [RelayCommand]
    public void SetSpeed(double speed)
    {
        player.Speed = speed;
        RebuildSpeedItems();
    }

    [RelayCommand]
    public void ToggleFullScreen() => shell.IsFullScreen = !shell.IsFullScreen;

    [RelayCommand]
    public void ToggleSidebar()
    {
        SidebarVisible = !SidebarVisible;
        prefs.SidebarVisible = SidebarVisible;
        if (SidebarVisible)
            Sidebar.Reload();
    }

    [RelayCommand]
    public void ToggleSubtitles()
    {
        player.SubtitlesVisible = !player.SubtitlesVisible;
    }

    [RelayCommand]
    public void IncreaseSubtitleSize() => SetSubtitleSize(SubtitleFontSize + SubtitleSizeStep);

    [RelayCommand]
    public void DecreaseSubtitleSize() => SetSubtitleSize(SubtitleFontSize - SubtitleSizeStep);

    [RelayCommand]
    public void SubtitlesUp() => SubtitleBottomOffset = Math.Min(400, SubtitleBottomOffset + SubtitlePositionStep);

    [RelayCommand]
    public void SubtitlesDown() => SubtitleBottomOffset = Math.Max(0, SubtitleBottomOffset - SubtitlePositionStep);

    [RelayCommand]
    public void CopyPrimaryText()
    {
        if (string.IsNullOrEmpty(PrimaryText))
            return;
        shell.SetClipboardText(PrimaryText);
        ShowToast("Copied", PrimaryText.Length > 60 ? PrimaryText[..60] + "…" : PrimaryText);
    }

    [RelayCommand]
    public void ActivateSubsSearch()
    {
        if (!SidebarVisible)
            ToggleSidebar();
        Sidebar.RequestFocusFilter();
    }

    [RelayCommand]
    public void ShowCheatSheet() => shell.ShowCheatSheet();

    [RelayCommand]
    public void SetTheme(string? theme)
    {
        string t = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        IsDarkTheme = t == "Dark";
        prefs.Theme = t;
        shell.ApplyTheme(t);
        SavePrefs();
    }

    [RelayCommand]
    public void ToggleTheme() => SetTheme(IsDarkTheme ? "Light" : "Dark");

    /// <summary>Reflects a theme chosen for this run only (command line) without saving it as the preference.</summary>
    public void ShowSessionTheme(string theme) => IsDarkTheme = !string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    public void DismissToast(ToastItem? toast)
    {
        if (toast != null)
            Toasts.Remove(toast);
    }

    /// <summary>Rebuilds the Subtitles ▸ Primary / Secondary track menus (called when the menu opens).</summary>
    [RelayCommand]
    public void RefreshTracks()
    {
        IReadOnlyList<SubtitleTrack> tracks = player.GetSubtitleTracks();
        Fill(PrimaryTracks, 0);
        Fill(SecondaryTracks, 1);

        void Fill(ObservableCollection<TrackMenuItem> target, int slot)
        {
            target.Clear();
            bool anySelected = tracks.Any(t => slot == 0 ? t.IsPrimary : t.IsSecondary);
            target.Add(new TrackMenuItem("Off", !anySelected, new RelayCommand(() => player.SelectSubtitleTrack(slot, null))));
            foreach (SubtitleTrack t in tracks)
            {
                SubtitleTrack track = t;
                string label = (t.IsExternal ? "File: " : "Embedded ") + t.Label;
                target.Add(new TrackMenuItem(label, slot == 0 ? t.IsPrimary : t.IsSecondary,
                    new RelayCommand(() => player.SelectSubtitleTrack(slot, track))));
            }
        }
    }

    // ---- inputs from the view ----------------------------------------------------------------------------------

    /// <summary>Opens a media path / URL (or loads it as subtitles when it is a subtitle file). Subtitle files given in
    /// <paramref name="subtitles"/> are loaded once the media has opened (drag &amp; drop, command-line --sub).</summary>
    public void Open(string path, IEnumerable<string>? subtitles = null)
    {
        if (Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(path)))
        {
            OpenSubtitlesFile(path);
            return;
        }

        RememberFolder(path);
        pendingSubtitles.Clear();
        if (subtitles != null)
            pendingSubtitles.AddRange(subtitles);
        player.Open(path);
    }

    public void OpenSubtitlesFile(string path)
    {
        if (!IsOpened && !IsOpening)
        {
            ShowToast("Open a video first", "Subtitles are loaded into the current video.", ToastVariant.Destructive);
            return;
        }

        RememberFolder(path);
        if (IsOpening)
            pendingSubtitles.Add(path);
        else
            player.OpenSubtitles(path);
    }

    /// <summary>Drag &amp; drop: the first media file opens (subtitle files dropped with it load after it), subtitle
    /// files alone load into the current video.</summary>
    public void HandleDrop(IReadOnlyList<string> paths)
    {
        IsDragOver = false;
        string? media = paths.FirstOrDefault(p => !Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(p)));
        List<string> subs = paths.Where(p => Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(p))).ToList();

        if (media != null)
        {
            Open(media, subs);
        }
        else
        {
            foreach (string s in subs)
                OpenSubtitlesFile(s);
        }
    }

    public void BeginSeekDrag() => IsSeeking = IsOpened;

    public void EndSeekDrag()
    {
        if (!IsSeeking)
            return;
        IsSeeking = false;
        SeekToSeconds(SeekValue);
    }

    /// <summary>Key down from the window. Returns true when handled.</summary>
    public bool HandleKeyDown(WpfKey key, bool ctrl, bool alt, bool shift, bool textInputFocused)
    {
        NotifyActivity();

        if (key == WpfKey.Escape && WordPopup.IsOpen)
        {
            WordPopup.Close();
            return true;
        }

        if (textInputFocused)
            return false;

        if (Shortcuts.TryExecute(key, ctrl, alt, shift))
            return true;

        return key != WpfKey.None && player.KeyDown(key);
    }

    public bool HandleKeyUp(WpfKey key, bool textInputFocused)
        => !textInputFocused && key != WpfKey.None && player.KeyUp(key);

    public void OnWordClicked(string word, string context, int slot, double x, double y)
    {
        if (player.IsPlaying)
            player.Pause();
        _ = WordPopup.OpenAsync(word, context, slot, x, y);
    }

    /// <summary>The window's fullscreen state changed (menu, F, Esc via the engine's IHostPlayer calls).</summary>
    public void OnFullScreenChanged(bool fullScreen)
    {
        IsFullScreen = fullScreen;
        NotifyActivity();
    }

    /// <summary>Mouse move / key press: shows the bar and cursor again.</summary>
    public void NotifyActivity()
    {
        lastActivity = time.GetUtcNow();
        UpdateIdle();
    }

    /// <summary>Periodic idle check (fullscreen + playing + no input for <see cref="IdleTimeout"/> hides bar and cursor).</summary>
    public void UpdateIdle()
    {
        bool idle = IsFullScreen && IsPlaying && !IsSeeking && !WordPopup.IsOpen
                    && time.GetUtcNow() - lastActivity >= IdleTimeout;
        ControlsVisible = !idle;
        CursorHidden = idle;
    }

    public void ShowToast(string title, string? message = null, ToastVariant variant = ToastVariant.Default)
    {
        ToastItem toast = new(title, message, variant);
        Toasts.Add(toast);
        while (Toasts.Count > 3)
            Toasts.RemoveAt(0);
        _ = RemoveToastLaterAsync(toast, variant == ToastVariant.Destructive ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(4));
    }

    public void SavePrefs()
    {
        try
        {
            savePrefs(prefs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Could not save preferences", ex.Message, ToastVariant.Destructive);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        player.PropertyChanged -= OnPlayerPropertyChanged;
        player.CuesChanged -= OnCuesChanged;
        player.OpenCompleted -= OnOpenCompleted;
        player.ErrorOccurred -= OnPlayerError;
        WordPopup.Close();
    }

    // ---- internals ---------------------------------------------------------------------------------------------

    string? InitialFolder()
    {
        string? url = player.Url;
        if (url != null && File.Exists(url))
            return Path.GetDirectoryName(url);
        return prefs.LastFolder;
    }

    void RememberFolder(string path)
    {
        if (!path.Contains("://", StringComparison.Ordinal) && Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
            prefs.LastFolder = dir;
    }

    void SetSubtitleSize(double size)
        => SubtitleFontSize = Math.Clamp(size, AppPrefs.MinSubtitleFontSize, AppPrefs.MaxSubtitleFontSize);

    void SeekToSeconds(double seconds)
    {
        if (!IsOpened)
            return;
        seekTarget = seconds;
        seekHoldUntil = time.GetUtcNow() + SeekHold;
        CurrentTimeText = TimeFormat.Clock(TimeSpan.FromSeconds(seconds));
        player.SeekTo(TimeSpan.FromSeconds(seconds));
    }

    void RebuildSpeedItems()
    {
        SpeedItems.Clear();
        double current = player.Speed;
        foreach (double s in Speeds)
        {
            double speed = s;
            SpeedItems.Add(new SpeedMenuItem(speed, Math.Abs(current - speed) < 0.001, new RelayCommand(() => SetSpeed(speed))));
        }
        SpeedText = current.ToString("0.##", CultureInfo.InvariantCulture) + "×";
    }

    async Task RemoveToastLaterAsync(ToastItem toast, TimeSpan after)
    {
        try
        {
            await Task.Delay(after, time);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        Toasts.Remove(toast);
    }

    void OnCuesChanged(int slot)
    {
        if (slot == 0)
            Sidebar.Reload();
    }

    void OnOpenCompleted(string? url, bool isSubtitles, string? error)
    {
        if (error != null)
        {
            ShowToast(isSubtitles ? "Could not open subtitles" : "Could not open media", error, ToastVariant.Destructive);
            return;
        }

        if (isSubtitles)
        {
            ShowToast("Subtitles loaded", url == null ? null : Path.GetFileName(url), ToastVariant.Success);
            return;
        }

        if (!string.IsNullOrEmpty(url))
        {
            prefs.AddRecent(url);
            RecentFiles.Clear();
            foreach (string r in prefs.RecentFiles)
                RecentFiles.Add(r);
            SavePrefs();
        }

        string[] subs = [.. pendingSubtitles];
        pendingSubtitles.Clear();
        foreach (string s in subs)
            player.OpenSubtitles(s);

        Sidebar.Reload();
        SyncAllFromPlayer();
    }

    void OnPlayerError(string message) => ShowToast("Playback error", message, ToastVariant.Destructive);

    void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IPlaybackController.CurTime):
                SyncTime();
                break;
            case nameof(IPlaybackController.Duration):
                SyncDuration();
                break;
            case nameof(IPlaybackController.IsPlaying):
                IsPlaying = player.IsPlaying;
                if (IsPlaying && WordPopup.IsOpen)
                    WordPopup.Close();   // WPF: popup closes when playback resumes
                UpdateIdle();
                break;
            case nameof(IPlaybackController.IsOpening):
                IsOpening = player.IsOpening;
                break;
            case nameof(IPlaybackController.IsOpened):
                IsOpened = player.IsOpened;
                break;
            case nameof(IPlaybackController.Title):
            case nameof(IPlaybackController.Url):
                SyncTitle();
                break;
            case nameof(IPlaybackController.Volume):
            case nameof(IPlaybackController.Mute):
                SyncVolume();
                break;
            case nameof(IPlaybackController.Speed):
                RebuildSpeedItems();
                break;
            case nameof(IPlaybackController.PrimaryText):
            case nameof(IPlaybackController.SecondaryText):
            case nameof(IPlaybackController.SubtitlesVisible):
                SyncSubtitles();
                break;
            case nameof(IPlaybackController.PrimaryImage):
                PrimaryImage = player.PrimaryImage;
                break;
            case nameof(IPlaybackController.SecondaryImage):
                SecondaryImage = player.SecondaryImage;
                break;
            case nameof(IPlaybackController.PrimaryCueIndex):
                Sidebar.UpdateCurrent(player.PrimaryCueIndex);
                break;
        }
    }

    void SyncAllFromPlayer()
    {
        IsOpened = player.IsOpened;
        IsPlaying = player.IsPlaying;
        IsOpening = player.IsOpening;
        SyncTitle();
        SyncDuration();
        SyncTime();
        SyncVolume();
        SyncSubtitles();
        PrimaryImage = player.PrimaryImage;
        SecondaryImage = player.SecondaryImage;
    }

    void SyncTitle()
    {
        MediaTitle = player.Title ?? "";
        WindowTitle = string.IsNullOrEmpty(MediaTitle) ? "LLPlayer" : $"{MediaTitle} — LLPlayer";
    }

    void SyncDuration()
    {
        TimeSpan d = TimeSpan.FromTicks(Math.Max(0, player.Duration));
        // A shorter new duration makes the bound slider coerce its value and write it back: that write-back must not
        // be taken for a user seek (it would jump the new media to its end).
        updatingFromPlayer = true;
        try
        {
            SeekMaximum = Math.Max(0.001, d.TotalSeconds);
        }
        finally
        {
            updatingFromPlayer = false;
        }
        DurationText = TimeFormat.Clock(d);
    }

    void SyncTime()
    {
        if (IsSeeking)
            return;

        double seconds = TimeSpan.FromTicks(Math.Max(0, player.CurTime)).TotalSeconds;
        if (seekTarget >= 0)
        {
            if (time.GetUtcNow() < seekHoldUntil && Math.Abs(seconds - seekTarget) > 1.5)
                return;   // the engine has not reached the requested position yet: keep the thumb where the user put it
            seekTarget = -1;
        }

        updatingFromPlayer = true;
        try
        {
            SeekValue = seconds;
        }
        finally
        {
            updatingFromPlayer = false;
        }
        CurrentTimeText = TimeFormat.Clock(TimeSpan.FromSeconds(seconds));
    }

    void SyncVolume()
    {
        updatingFromPlayer = true;
        try
        {
            Volume = player.Volume;
        }
        finally
        {
            updatingFromPlayer = false;
        }
        IsMuted = player.Mute || player.Volume == 0;
        prefs.Mute = player.Mute;
    }

    void SyncSubtitles()
    {
        SubtitlesVisible = player.SubtitlesVisible;
        prefs.SubtitlesVisible = SubtitlesVisible;
        PrimaryText = player.PrimaryText;
        SecondaryText = player.SecondaryText;
        PrimaryVisible = SubtitlesVisible && !string.IsNullOrWhiteSpace(PrimaryText);
        SecondaryVisible = SubtitlesVisible && !string.IsNullOrWhiteSpace(SecondaryText);
    }
}

using LLPlayer.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
    readonly SubtitleFontCatalog fonts = new();
    readonly Action<AppPrefs> savePrefs;
    readonly IAppShell shell;
    readonly TimeProvider time;
    readonly List<string> pendingSubtitles = [];
    object? pendingMediaRequest;
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

        Sidebar = new SubtitlesSidebarViewModel(player)
        {
            MatchCase = prefs.SidebarSearchMatchCase,
            WholeWord = prefs.SidebarSearchWholeWord,
            UseRegex = prefs.SidebarSearchRegex,
        };
        // Restore before subscribing so startup never rewrites preferences or persists transient search state.
        Sidebar.PropertyChanged += OnSidebarPropertyChanged;
        WordPopup = new WordPopupViewModel(translator, text => shell.SetClipboardText(text));
        WordPopup.OpenSettings = ShowTranslation;
        InvalidateTranslation = () => { WordPopup.Invalidate(); (translator as FlyleafWordTranslator)?.Invalidate(); };

        // F-12 waveform: restore the persisted toggle and apply it to the engine before the first media open, so the
        // OFF default performs no audio decode work. Peaks are engine-owned and transient (reset on open).
        ShowWaveform = prefs.ShowWaveform;
        player.WaveformEnabled = ShowWaveform;
        WaveformPeaks = player.WaveformPeaks;
        WaveformActive = player.WaveformActive;
        Shortcuts = CreateShortcutMap();

        foreach (string recent in prefs.RecentFiles)
            RecentFiles.Add(recent);
        RecentFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentFiles));

        SubtitleFontSelection primaryFont = fonts.Resolve(prefs.SubtitleFontFamily, prefs.SubtitleFontWeight, prefs.SubtitleFontStyle);
        SubtitleFontSelection secondaryFont = prefs.SecondarySubtitleInheritsFont ? primaryFont
            : fonts.Resolve(prefs.SecondarySubtitleFontFamily, prefs.SecondarySubtitleFontWeight, prefs.SecondarySubtitleFontStyle);
        ApplySubtitleFonts(primaryFont, secondaryFont);
        SubtitleFontSize = prefs.SubtitleFontSize;
        SubtitleBottomOffset = prefs.SubtitleBottomOffset;
        SubtitleOutlineThickness = prefs.SubtitleOutlineThickness;
        SecondarySubtitleOutlineThickness = prefs.SecondarySubtitleOutlineThickness;
        SubtitleWidthPercent = prefs.SubtitleWidthPercent;
        SubtitleMaxWidth = prefs.SubtitleMaxWidth;
        SubtitleIgnoreLineBreak = prefs.SubtitleIgnoreLineBreak;
        SubtitleLineAlignment = prefs.SubtitleLineAlignment;
        SubtitlePrimarySecondaryDistance = prefs.SubtitlePrimarySecondaryDistance;
        SubtitleSeparatorMaxWidth = prefs.SubtitleSeparatorMaxWidth;
        SubtitleSeparatorOpacityPercent = prefs.SubtitleSeparatorOpacityPercent;
        SubtitlePositionAlignment = prefs.SubtitlePositionAlignment;
        SubtitlePositionAlignmentWhenDual = prefs.SubtitlePositionAlignmentWhenDual;
        SubtitleTextBrush = new(Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(prefs.SubtitleTextColor, out string primaryColor) ? primaryColor : AppPrefs.DefaultSubtitleTextColor));
        SecondarySubtitleTextBrush = new(Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(prefs.SecondarySubtitleTextColor, out string secondaryColor) ? secondaryColor : AppPrefs.DefaultSecondarySubtitleTextColor));
        SubtitleOutlineBrush = new(Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(prefs.SubtitleOutlineColor, out string outlineColor) ? outlineColor : AppPrefs.DefaultSubtitleOutlineColor));
        Color backgroundColor = Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(prefs.SubtitleBackgroundColor, out string background) ? background : AppPrefs.DefaultSubtitleBackgroundColor);
        SubtitleBackgroundBrush = new(backgroundColor, Math.Clamp(prefs.SubtitleBackgroundOpacityPercent, 0, 100) / 100d);
        SecondarySubtitleBackgroundBrush = new(backgroundColor, Math.Clamp(prefs.SecondarySubtitleBackgroundOpacityPercent, 0, 100) / 100d);
        SidebarVisible = prefs.SidebarVisible;
        SidebarWidth = prefs.SidebarWidth;
        SidebarLeft = prefs.SidebarLeft;
        SidebarTextMask = prefs.SidebarTextMask;
        SidebarShowOriginalText = prefs.SidebarShowOriginalText;
        SidebarSubPadding = prefs.SidebarSubPadding;
        SidebarFontFamily = prefs.SidebarFontFamily;
        SidebarFontWeight = prefs.SidebarFontWeight;
        WordClickAction = prefs.WordClickAction;
        AnkiDeck = prefs.AnkiDeck;
        SidebarFontSize = prefs.SidebarFontSize;
        ApplySidebarAppearance();
        // The engine owns the default chords; only an explicit replacement list is applied at start-up.
        if (prefs.EngineKeysCustomized)
            player.ApplyEngineKeyBindings(EngineKeyBindings.FromPrefs(prefs.EngineKeyBindings));
        IReadOnlyList<EngineKeyBindingInfo> activeEngineKeys = prefs.EngineKeysCustomized
            ? EngineKeyBindings.FromPrefs(prefs.EngineKeyBindings)
            : EngineKeyBindings.Defaults();
        if (!EngineKeyBindings.TryValidate(activeEngineKeys, Shortcuts.Shortcuts, out string keyConflict))
            ShowToast("Key binding conflict", keyConflict, ToastVariant.Destructive);
        IsDarkTheme = prefs.Theme != "Light";

        player.Volume = Math.Min(prefs.Volume, player.VolumeMax);
        player.Mute = prefs.Mute;
        player.SubtitlesVisible = prefs.SubtitlesVisible;

        player.PropertyChanged += OnPlayerPropertyChanged;
        player.CuesChanged += OnCuesChanged;
        player.OpenCompleted += OnOpenCompleted;
        player.ExternalMediaOpening += OnExternalMediaOpening;
        player.ErrorOccurred += OnPlayerError;
        if (player is IRecognitionController recognition) recognition.RecognitionError += OnRecognitionError;

        SyncAllFromPlayer();
        RebuildSpeedItems();
    }

    public SubtitlesSidebarViewModel Sidebar { get; }
    public WordPopupViewModel WordPopup { get; }
    public WordLibrary? WordLibrary { get; set; }
    public WordManagerViewModel? WordManager { get; private set; }
    [RelayCommand] void ShowWordManager()
    {
        if (WordLibrary != null) { WordManager ??= new WordManagerViewModel(WordLibrary, defaultDeck: AnkiDeck); shell.ShowWordManager(WordManager); }
    }
    public void WordManagerClosed() => WordManager = null;
    public ISubtitleExportSource? SubtitleExportSource { get; set; }
    public SubtitlesExportViewModel? SubtitlesExport { get; private set; }
    [RelayCommand]
    void ShowSubtitlesExport()
    {
        if (disposed || SubtitleExportSource == null) return;
        SubtitlesExport ??= new SubtitlesExportViewModel(SubtitleExportSource);
        shell.ShowSubtitlesExport(SubtitlesExport);
    }

    /// <summary>The window calls this after its export work drains; stale callbacks cannot discard a newer session.</summary>
    public void SubtitlesExportClosed(SubtitlesExportViewModel model)
    {
        if (ReferenceEquals(SubtitlesExport, model)) SubtitlesExport = null;
    }

    /// <summary>Cancel and drain before the host releases the player used by the source adapter.</summary>
    public Task DrainSubtitlesExportAsync() => SubtitlesExport?.DisposeAsync().AsTask() ?? Task.CompletedTask;
    public bool PrimaryTranslationEnabled => player.PrimaryTranslationEnabled;
    public bool SecondaryTranslationEnabled => player.SecondaryTranslationEnabled;
    [RelayCommand]
    async Task TogglePrimaryTranslationAsync() => await ToggleSentenceTranslationAsync(0);
    [RelayCommand]
    async Task ToggleSecondaryTranslationAsync() => await ToggleSentenceTranslationAsync(1);
    async Task ToggleSentenceTranslationAsync(int slot)
    {
        try { await player.ToggleTranslationAsync(slot); }
        catch (Exception e) { ShowToast("Subtitle translation", e.Message, ToastVariant.Destructive); }
    }

    [RelayCommand]
    void ShowSettings() => shell.ShowSettings(CreateSettings());

    public SettingsViewModel CreateSettings() => new(prefs, CommitSettings,
        new BitmapSubtitleRuntimeState(player.PrimaryBitmapScale, player.SecondaryBitmapScale, player.PrimaryBitmapPosition));

    void CommitSettings(SettingsViewModel draft)
    {
        // Clone the fresh current record, including unrelated values changed while the modal draft was open.
        AppPrefs candidate = JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions), PrefsStore.JsonOptions)!;
        candidate.Theme = draft.Theme;
        candidate.SubtitleFontFamily = draft.SubtitleFontFamily;
        candidate.SubtitleFontWeight = draft.SubtitleFontWeight;
        candidate.SubtitleFontStyle = draft.SubtitleFontStyle;
        candidate.SecondarySubtitleFontFamily = draft.SecondarySubtitleFontFamily;
        candidate.SecondarySubtitleFontWeight = draft.SecondarySubtitleFontWeight;
        candidate.SecondarySubtitleFontStyle = draft.SecondarySubtitleFontStyle;
        candidate.SecondarySubtitleInheritsFont = draft.SecondarySubtitleInheritsFont;
        candidate.SubtitleFontSize = draft.SubtitleFontSize;
        candidate.SecondaryFontScale = draft.SecondaryFontScale;
        candidate.SubtitleBottomOffset = draft.SubtitleBottomOffset;
        candidate.SubtitleOutlineThickness = draft.SubtitleOutlineThickness;
        candidate.SecondarySubtitleOutlineThickness = draft.SecondarySubtitleOutlineThickness;
        candidate.SubtitleTextColor = draft.SubtitleTextColor;
        candidate.SecondarySubtitleTextColor = draft.SecondarySubtitleTextColor;
        candidate.SubtitleOutlineColor = draft.SubtitleOutlineColor;
        candidate.SubtitleBackgroundColor = draft.SubtitleBackgroundColor;
        candidate.SubtitleBackgroundOpacityPercent = draft.SubtitleBackgroundOpacityPercent;
        candidate.SecondarySubtitleBackgroundOpacityPercent = draft.SecondarySubtitleBackgroundOpacityPercent;
        candidate.SubtitlesVisible = draft.SubtitlesVisible;
        candidate.SidebarVisible = draft.SidebarVisible;
        candidate.SidebarWidth = draft.SidebarWidth;
        candidate.SidebarLeft = draft.SidebarLeft;
        candidate.SidebarTextMask = draft.SidebarTextMask;
        candidate.SidebarShowOriginalText = draft.SidebarShowOriginalText;
        candidate.SidebarSubPadding = draft.SidebarSubPadding;
        candidate.SidebarFontFamily = draft.SidebarFontFamily;
        candidate.SidebarFontWeight = draft.SidebarFontWeight;
        candidate.SidebarFontSize = draft.SidebarFontSize;
        candidate.WordClickAction = draft.WordClickAction;
        candidate.AnkiDeck = draft.AnkiDeck;
        candidate.SubtitleWidthPercent = draft.SubtitleWidthPercent;
        candidate.SubtitleMaxWidth = draft.SubtitleMaxWidth;
        candidate.SubtitleIgnoreLineBreak = draft.SubtitleIgnoreLineBreak;
        candidate.SubtitleLineAlignment = draft.SubtitleLineAlignment;
        candidate.SubtitlePrimarySecondaryDistance = draft.SubtitlePrimarySecondaryDistance;
        candidate.SubtitleSeparatorMaxWidth = draft.SubtitleSeparatorMaxWidth;
        candidate.SubtitleSeparatorOpacityPercent = draft.SubtitleSeparatorOpacityPercent;
        candidate.SubtitlePositionAlignment = draft.SubtitlePositionAlignment;
        candidate.SubtitlePositionAlignmentWhenDual = draft.SubtitlePositionAlignmentWhenDual;
        candidate.AppShortcutOverrides = new Dictionary<string, string>(draft.AppShortcuts, StringComparer.Ordinal);
        candidate.EngineKeysCustomized = draft.EngineKeysCustomized;
        candidate.EngineKeyBindings = draft.EngineKeysCustomized ? [.. draft.EngineKeyBindingPrefs] : [];
        // Resolve font resources and parse brushes before persistence, so live application only assigns prepared values.
        SubtitleFontSelection primaryFont = fonts.Resolve(candidate.SubtitleFontFamily, candidate.SubtitleFontWeight, candidate.SubtitleFontStyle);
        SubtitleFontSelection secondaryFont = candidate.SecondarySubtitleInheritsFont ? primaryFont
            : fonts.Resolve(candidate.SecondarySubtitleFontFamily, candidate.SecondarySubtitleFontWeight, candidate.SecondarySubtitleFontStyle);
        if (global::Avalonia.Application.Current != null)
        {
            _ = SubtitleFontCatalog.DescribeFace(primaryFont);
            _ = SubtitleFontCatalog.DescribeFace(secondaryFont);
        }
        ImmutableSolidColorBrush primaryBrush = new(Color.Parse(candidate.SubtitleTextColor));
        ImmutableSolidColorBrush secondaryBrush = new(Color.Parse(candidate.SecondarySubtitleTextColor));
        ImmutableSolidColorBrush outlineBrush = new(Color.Parse(candidate.SubtitleOutlineColor));
        Color backgroundColor = Color.Parse(candidate.SubtitleBackgroundColor);
        ImmutableSolidColorBrush backgroundBrush = new(backgroundColor, candidate.SubtitleBackgroundOpacityPercent / 100d);
        ImmutableSolidColorBrush secondaryBackgroundBrush = new(backgroundColor, candidate.SecondarySubtitleBackgroundOpacityPercent / 100d);
        savePrefs(candidate); // may throw; nothing live has changed yet

        prefs.Theme = candidate.Theme;
        prefs.SubtitleFontFamily = candidate.SubtitleFontFamily;
        prefs.SubtitleFontWeight = candidate.SubtitleFontWeight;
        prefs.SubtitleFontStyle = candidate.SubtitleFontStyle;
        prefs.SecondarySubtitleFontFamily = candidate.SecondarySubtitleFontFamily;
        prefs.SecondarySubtitleFontWeight = candidate.SecondarySubtitleFontWeight;
        prefs.SecondarySubtitleFontStyle = candidate.SecondarySubtitleFontStyle;
        prefs.SecondarySubtitleInheritsFont = candidate.SecondarySubtitleInheritsFont;
        ApplySubtitleFonts(primaryFont, secondaryFont);
        prefs.SecondaryFontScale = candidate.SecondaryFontScale;
        prefs.SubtitleFontSize = candidate.SubtitleFontSize;
        prefs.SubtitleBottomOffset = candidate.SubtitleBottomOffset;
        prefs.SubtitleOutlineThickness = candidate.SubtitleOutlineThickness;
        prefs.SecondarySubtitleOutlineThickness = candidate.SecondarySubtitleOutlineThickness;
        prefs.SubtitleTextColor = candidate.SubtitleTextColor;
        prefs.SecondarySubtitleTextColor = candidate.SecondarySubtitleTextColor;
        prefs.SubtitleOutlineColor = candidate.SubtitleOutlineColor;
        prefs.SubtitleBackgroundColor = candidate.SubtitleBackgroundColor;
        prefs.SubtitleBackgroundOpacityPercent = candidate.SubtitleBackgroundOpacityPercent;
        prefs.SecondarySubtitleBackgroundOpacityPercent = candidate.SecondarySubtitleBackgroundOpacityPercent;
        prefs.SidebarVisible = candidate.SidebarVisible;
        prefs.SidebarWidth = candidate.SidebarWidth;
        prefs.SidebarLeft = candidate.SidebarLeft;
        prefs.SidebarTextMask = candidate.SidebarTextMask;
        prefs.SidebarShowOriginalText = candidate.SidebarShowOriginalText;
        prefs.SidebarSubPadding = candidate.SidebarSubPadding;
        prefs.SidebarFontFamily = candidate.SidebarFontFamily;
        prefs.SidebarFontWeight = candidate.SidebarFontWeight;
        prefs.SidebarFontSize = candidate.SidebarFontSize;
        prefs.WordClickAction = candidate.WordClickAction;
        prefs.AnkiDeck = candidate.AnkiDeck;
        prefs.EngineKeysCustomized = candidate.EngineKeysCustomized;
        prefs.EngineKeyBindings = candidate.EngineKeyBindings;
        prefs.SubtitlesVisible = candidate.SubtitlesVisible;
        prefs.SubtitleWidthPercent = candidate.SubtitleWidthPercent;
        prefs.SubtitleMaxWidth = candidate.SubtitleMaxWidth;
        prefs.SubtitleIgnoreLineBreak = candidate.SubtitleIgnoreLineBreak;
        prefs.SubtitleLineAlignment = candidate.SubtitleLineAlignment;
        prefs.SubtitlePrimarySecondaryDistance = candidate.SubtitlePrimarySecondaryDistance;
        prefs.SubtitleSeparatorMaxWidth = candidate.SubtitleSeparatorMaxWidth;
        prefs.SubtitleSeparatorOpacityPercent = candidate.SubtitleSeparatorOpacityPercent;
        prefs.SubtitlePositionAlignment = candidate.SubtitlePositionAlignment;
        prefs.SubtitlePositionAlignmentWhenDual = candidate.SubtitlePositionAlignmentWhenDual;
        WordClickAction = candidate.WordClickAction;
        AnkiDeck = candidate.AnkiDeck;
        // The deck pref is a default for the next Word Manager only: an open manager is the single owner of
        // its session deck, so saving Settings must not discard a deck typed there.
        ApplyAppShortcutOverrides(candidate.AppShortcutOverrides);
        player.ApplyEngineKeyBindings(candidate.EngineKeysCustomized
            ? EngineKeyBindings.FromPrefs(candidate.EngineKeyBindings)
            : EngineKeyBindings.Defaults());
        // The cheat sheet builds its engine rows from the live config; an open sheet must follow this Save too.
        OnPropertyChanged(nameof(Shortcuts));
        SubtitleFontSize = candidate.SubtitleFontSize;
        SubtitleBottomOffset = candidate.SubtitleBottomOffset;
        SubtitleOutlineThickness = candidate.SubtitleOutlineThickness;
        SecondarySubtitleOutlineThickness = candidate.SecondarySubtitleOutlineThickness;
        SubtitleWidthPercent = candidate.SubtitleWidthPercent;
        SubtitleMaxWidth = candidate.SubtitleMaxWidth;
        SubtitleIgnoreLineBreak = candidate.SubtitleIgnoreLineBreak;
        SubtitleLineAlignment = candidate.SubtitleLineAlignment;
        SubtitlePrimarySecondaryDistance = candidate.SubtitlePrimarySecondaryDistance;
        SubtitleSeparatorMaxWidth = candidate.SubtitleSeparatorMaxWidth;
        SubtitleSeparatorOpacityPercent = candidate.SubtitleSeparatorOpacityPercent;
        SubtitlePositionAlignment = candidate.SubtitlePositionAlignment;
        SubtitlePositionAlignmentWhenDual = candidate.SubtitlePositionAlignmentWhenDual;
        // Bitmap scale/position are live session state (WPF Position / Size edits the same engine values); Save applies
        // them to the engine but never writes them to AppPrefs.
        player.PrimaryBitmapScale = draft.PrimaryBitmapScale;
        player.SecondaryBitmapScale = draft.SecondaryBitmapScale;
        player.PrimaryBitmapPosition = draft.PrimaryBitmapPosition;
        SubtitleTextBrush = primaryBrush;
        SecondarySubtitleTextBrush = secondaryBrush;
        SubtitleOutlineBrush = outlineBrush;
        SubtitleBackgroundBrush = backgroundBrush;
        SecondarySubtitleBackgroundBrush = secondaryBackgroundBrush;
        // A scale-only edit does not invoke OnSubtitleFontSizeChanged.
        SecondaryFontSize = Math.Round(candidate.SubtitleFontSize * candidate.SecondaryFontScale, 1);
        SidebarWidth = candidate.SidebarWidth;
        SidebarVisible = candidate.SidebarVisible;
        SidebarLeft = candidate.SidebarLeft;
        SidebarTextMask = candidate.SidebarTextMask;
        SidebarShowOriginalText = candidate.SidebarShowOriginalText;
        SidebarSubPadding = candidate.SidebarSubPadding;
        SidebarFontFamily = candidate.SidebarFontFamily;
        SidebarFontWeight = candidate.SidebarFontWeight;
        SidebarFontSize = candidate.SidebarFontSize;
        ApplySidebarAppearance();
        if (SidebarVisible) Sidebar.Reload();
        player.SubtitlesVisible = candidate.SubtitlesVisible;
        IsDarkTheme = candidate.Theme == "Dark";
        shell.ApplyTheme(candidate.Theme);
    }

    void ApplySubtitleFonts(SubtitleFontSelection primary, SubtitleFontSelection secondary)
    {
        SubtitleFontFamily = primary.Family;
        SubtitleFontWeight = primary.Weight;
        SubtitleFontStyle = primary.Style;
        SecondarySubtitleFontFamily = secondary.Family;
        SecondarySubtitleFontWeight = secondary.Weight;
        SecondarySubtitleFontStyle = secondary.Style;
    }

    public void OpenSettingsTool(SettingsLink link)
    {
        switch (link)
        {
            case SettingsLink.Translation: ShowTranslation(); break;
            case SettingsLink.Recognition: ShowRecognition(); break;
            case SettingsLink.Shortcuts: ShowCheatSheet(); break;
            case SettingsLink.WordManager: ShowWordManager(); break;
        }
    }

    public Func<TranslationViewModel>? TranslationDialog { get; set; }
    public Action InvalidateTranslation { get; }
    [RelayCommand]
    void ShowTranslation()
    {
        if (TranslationDialog is { } create) shell.ShowTranslation(create());
    }
    public AppShortcutMap Shortcuts { get; private set; }

    /// <summary>Applies validated app-shortcut overrides live and persists them; engine key bindings are edited separately in Settings → Tools.</summary>
    public void ApplyAppShortcuts(IReadOnlyDictionary<string, string> overrides)
    {
        ApplyAppShortcutOverrides(overrides);
        SavePrefs();
    }

    void ApplyAppShortcutOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        _ = AppShortcutMap.TryNormalizeOverrides(overrides, out Dictionary<string, string> normalized);
        prefs.AppShortcutOverrides = normalized;
        Shortcuts = CreateShortcutMap();
        OnPropertyChanged(nameof(Shortcuts));
    }

    AppShortcutMap CreateShortcutMap() => new(new Dictionary<string, Action>
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
    }, prefs.AppShortcutOverrides);
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
    [ObservableProperty] public partial FontFamily SubtitleFontFamily { get; private set; } = SubtitleFontCatalog.DefaultFamily;
    [ObservableProperty] public partial FontWeight SubtitleFontWeight { get; private set; } = FontWeight.SemiBold;
    [ObservableProperty] public partial FontStyle SubtitleFontStyle { get; private set; } = FontStyle.Normal;
    [ObservableProperty] public partial FontFamily SecondarySubtitleFontFamily { get; private set; } = SubtitleFontCatalog.DefaultFamily;
    [ObservableProperty] public partial FontWeight SecondarySubtitleFontWeight { get; private set; } = FontWeight.SemiBold;
    [ObservableProperty] public partial FontStyle SecondarySubtitleFontStyle { get; private set; } = FontStyle.Normal;
    [ObservableProperty] public partial double SubtitleFontSize { get; private set; }
    [ObservableProperty] public partial double SecondaryFontSize { get; private set; }
    [ObservableProperty] public partial double SubtitleBottomOffset { get; private set; } = AppPrefs.DefaultSubtitleBottomOffset;
    [ObservableProperty] public partial double SubtitleOutlineThickness { get; private set; } = AppPrefs.DefaultSubtitleOutlineThickness;
    [ObservableProperty] public partial double SecondarySubtitleOutlineThickness { get; private set; } = AppPrefs.DefaultSecondarySubtitleOutlineThickness;
    [ObservableProperty] public partial ImmutableSolidColorBrush SubtitleTextBrush { get; private set; } = new(Color.Parse(AppPrefs.DefaultSubtitleTextColor));
    [ObservableProperty] public partial ImmutableSolidColorBrush SecondarySubtitleTextBrush { get; private set; } = new(Color.Parse(AppPrefs.DefaultSecondarySubtitleTextColor));
    [ObservableProperty] public partial ImmutableSolidColorBrush SubtitleOutlineBrush { get; private set; } = new(Color.Parse(AppPrefs.DefaultSubtitleOutlineColor));
    [ObservableProperty] public partial ImmutableSolidColorBrush SubtitleBackgroundBrush { get; private set; } = new(Color.Parse(AppPrefs.DefaultSubtitleBackgroundColor), 0);
    [ObservableProperty] public partial ImmutableSolidColorBrush SecondarySubtitleBackgroundBrush { get; private set; } = new(Color.Parse(AppPrefs.DefaultSubtitleBackgroundColor), 0);

    // ---- text subtitle layout (WPF Position / Size, text half) ---------------------------------------------------
    // The vertical base offset keeps its approved DIP model; bitmap scale/position, the dual separator and a
    // dual-specific alignment remain separate later work.

    [ObservableProperty] public partial int SubtitleWidthPercent { get; private set; } = AppPrefs.DefaultSubtitleWidthPercent;
    [ObservableProperty] public partial double SubtitleMaxWidth { get; private set; } = AppPrefs.DefaultSubtitleMaxWidth;
    [ObservableProperty] public partial bool SubtitleIgnoreLineBreak { get; private set; }
    [ObservableProperty] public partial string SubtitleLineAlignment { get; private set; } = AppPrefs.DefaultSubtitleLineAlignment;
    [ObservableProperty] public partial double SubtitlePrimarySecondaryDistance { get; private set; } = AppPrefs.DefaultSubtitlePrimarySecondaryDistance;
    [ObservableProperty] public partial double SubtitleSeparatorMaxWidth { get; private set; } = AppPrefs.DefaultSubtitleSeparatorMaxWidth;
    [ObservableProperty] public partial int SubtitleSeparatorOpacityPercent { get; private set; } = AppPrefs.DefaultSubtitleSeparatorOpacityPercent;
    [ObservableProperty] public partial string SubtitlePositionAlignment { get; private set; } = AppPrefs.DefaultSubtitlePositionAlignment;
    [ObservableProperty] public partial string SubtitlePositionAlignmentWhenDual { get; private set; } = AppPrefs.DefaultSubtitlePositionAlignment;
    public double SubtitleSeparatorOpacity => SubtitleSeparatorOpacityPercent / 100d;

    // WPF selects the dual block alignment when both subtitle slots have a loaded track, not per displayed cue, so the
    // block does not jump between single and dual anchors whenever the secondary cue changes.
    [ObservableProperty] public partial bool PrimarySlotEnabled { get; private set; }
    [ObservableProperty] public partial bool SecondarySlotEnabled { get; private set; }
    public bool SubtitleDualLayoutActive => PrimarySlotEnabled && SecondarySlotEnabled;

    public TextAlignment SubtitleTextAlignment => SubtitleLayoutOptions.ToTextAlignment(SubtitleLineAlignment);

    /// <summary>Which edge of the text block is anchored at the configured bottom distance.</summary>
    public SubtitleVerticalAlignment EffectiveSubtitleVerticalAlignment
        => SubtitleLayoutOptions.ToVerticalAlignment(SubtitleDualLayoutActive ? SubtitlePositionAlignmentWhenDual : SubtitlePositionAlignment);

    partial void OnSubtitleLineAlignmentChanged(string value) => OnPropertyChanged(nameof(SubtitleTextAlignment));
    partial void OnSubtitlePositionAlignmentChanged(string value) => OnPropertyChanged(nameof(EffectiveSubtitleVerticalAlignment));
    partial void OnSubtitlePositionAlignmentWhenDualChanged(string value) => OnPropertyChanged(nameof(EffectiveSubtitleVerticalAlignment));
    partial void OnSubtitleSeparatorOpacityPercentChanged(int value) => OnPropertyChanged(nameof(SubtitleSeparatorOpacity));
    partial void OnPrimarySlotEnabledChanged(bool value) => OnDualLayoutChanged();
    partial void OnSecondarySlotEnabledChanged(bool value) => OnDualLayoutChanged();
    void OnDualLayoutChanged()
    {
        OnPropertyChanged(nameof(SubtitleDualLayoutActive));
        OnPropertyChanged(nameof(EffectiveSubtitleVerticalAlignment));
    }

    // The box uses the approved 48-DIP side margins, then the requested percentage of that padded width, capped by an
    // optional max width (0 = no cap). A squeezed window never yields a zero/negative box.
    public const double SubtitleSideMargin = 48;

    public static double ComputeSubtitleBoxWidth(double videoWidth, int widthPercent, double maxWidth)
    {
        if (!double.IsFinite(videoWidth) || videoWidth <= 0)
            return 1;
        double padded = Math.Max(0, videoWidth - 2 * SubtitleSideMargin);
        int percent = Math.Clamp(widthPercent, AppPrefs.MinSubtitleWidthPercent, AppPrefs.MaxSubtitleWidthPercent);
        double box = padded * percent / 100d;
        if (double.IsFinite(maxWidth) && maxWidth > AppPrefs.MinSubtitleMaxWidth)
            box = Math.Min(box, Math.Clamp(maxWidth, AppPrefs.MinSubtitleMaxWidth, AppPrefs.MaxSubtitleMaxWidth));
        return Math.Max(1, box);
    }

    // ---- sidebar -----------------------------------------------------------------------------------------------

    [ObservableProperty] public partial bool SidebarVisible { get; private set; }
    [ObservableProperty] public partial double SidebarWidth { get; set; }
    /// <summary>Dock the sidebar left of the video (WPF parity); the window applies the column swap.</summary>
    [ObservableProperty] public partial bool SidebarLeft { get; set; }
    /// <summary>Mask cue text after the current one with underscores (spoiler protection).</summary>
    [ObservableProperty] public partial bool SidebarTextMask { get; set; }
    /// <summary>Show original cue text instead of the translated display text; search follows the choice.</summary>
    [ObservableProperty] public partial bool SidebarShowOriginalText { get; set; }
    /// <summary>Sidebar cue-row padding in DIP (sidebar only).</summary>
    [ObservableProperty] public partial int SidebarSubPadding { get; set; } = AppPrefs.DefaultSidebarSubPadding;
    /// <summary>Requested sidebar font family (resolved with a catalog fallback).</summary>
    [ObservableProperty] public partial string SidebarFontFamily { get; set; } = AppPrefs.DefaultSidebarFontFamily;
    /// <summary>Sidebar font weight: Normal, SemiBold or Bold.</summary>
    [ObservableProperty] public partial string SidebarFontWeight { get; set; } = AppPrefs.DefaultSidebarFontWeight;
    [ObservableProperty] public partial string WordClickAction { get; set; } = AppPrefs.DefaultWordClickAction;
    [ObservableProperty] public partial string AnkiDeck { get; set; } = AppPrefs.DefaultAnkiDeck;
    /// <summary>Sidebar cue-row font size in DIP; the video overlay font is independent.</summary>
    [ObservableProperty] public partial double SidebarFontSize { get; set; } = AppPrefs.DefaultSidebarFontSize;

    /// <summary>Pushes the persisted sidebar appearance into the sidebar rows (the only typography surface the
    /// view binds); this also restyles already-realized rows through their observable Row* properties.</summary>
    public void ApplySidebarAppearance()
        => Sidebar.ApplyAppearance(new SidebarTextAppearance(SidebarTextMask, SidebarShowOriginalText,
            SidebarSubPadding, SidebarFontFamily, SidebarFontWeight, SidebarFontSize));

    partial void OnSubtitleFontSizeChanged(double value)
    {
        SecondaryFontSize = Math.Round(value * prefs.SecondaryFontScale, 1);
        prefs.SubtitleFontSize = value;
    }

    partial void OnSubtitleBottomOffsetChanged(double value) => prefs.SubtitleBottomOffset = value;

    partial void OnSidebarWidthChanged(double value)
    {
        if (double.IsFinite(value) && value > 0)
            prefs.SidebarWidth = value;
    }

    partial void OnIsOpenedChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));
    partial void OnIsOpeningChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    // ---- A-B repeat (F-12 parity) ------------------------------------------------------------------------------

    public const string AbLoopToastTitle = "A-B repeat";

    /// <summary>F-12 waveform overlay requested by the user; persisted as <see cref="AppPrefs.ShowWaveform"/>.</summary>
    public bool ShowWaveform { get; private set; }
    /// <summary>Engine-owned peaks of the current file (null while off/not built).</summary>
    public float[]? WaveformPeaks { get; private set; }
    public bool WaveformActive { get; private set; }

    [RelayCommand]
    public void ToggleWaveform()
    {
        ShowWaveform = !ShowWaveform;
        player.WaveformEnabled = ShowWaveform;
        prefs.ShowWaveform = ShowWaveform;
        OnPropertyChanged(nameof(ShowWaveform));
        SavePrefs();
    }

    /// <summary>At least one A-B point is set: the seek-bar markers are shown and the button is lit.</summary>
    public bool AbLoopActive => player.ABLoopActive;
    /// <summary>Both points are set with A &lt; B: the engine loops playback.</summary>
    public bool AbLoopEnabled => player.ABLoopEnabled;
    public bool HasAbLoopA => player.HasABLoopA;
    public bool HasAbLoopB => player.HasABLoopB;
    /// <summary>Point A in seconds; NaN when unset (bound to the seek-bar marker).</summary>
    public double AbLoopStartSeconds => ToMarkerSeconds(player.ABLoopA);
    /// <summary>Point B in seconds; NaN when unset (bound to the seek-bar marker).</summary>
    public double AbLoopEndSeconds => ToMarkerSeconds(player.ABLoopB);

    static double ToMarkerSeconds(long ticks) => ticks < 0 ? double.NaN : TimeSpan.FromTicks(ticks).TotalSeconds;

    /// <summary>Left-click cycle of the player bar: Set A → Set B → Clear (the WPF A-B button).</summary>
    [RelayCommand]
    public void ToggleAbLoop()
    {
        if (!IsOpened)
            return;

        bool hadA = HasAbLoopA, hadB = HasAbLoopB;
        player.ToggleABLoop();
        bool hasA = HasAbLoopA, hasB = HasAbLoopB;

        if (!hadA && hasA)
            ShowToast(AbLoopToastTitle, "A set");
        else if (hadA && !hadB && hasB)
            ShowToast(AbLoopToastTitle, "B set");
        else if (hadA && !hadB)
            ShowToast(AbLoopToastTitle, "B must be after A");
        else if (hadA && hadB && !hasA && !hasB)
            ShowToast(AbLoopToastTitle, "A-B cleared");
    }

    [RelayCommand]
    public void SetAbLoopStart()
    {
        if (!IsOpened)
            return;
        player.SetABLoopStart();
        if (HasAbLoopA)
            ShowToast(AbLoopToastTitle, "A set");
    }

    [RelayCommand]
    public void SetAbLoopEnd()
    {
        if (!IsOpened)
            return;
        long previousB = player.ABLoopB;
        long sampledPosition = player.CurTime;
        bool hadA = HasAbLoopA;
        long sampledA = player.ABLoopA;
        player.SetABLoopEnd();
        // The engine keeps a previously accepted B when it rejects the new one; judge by the outcome plus the
        // pre-sampled position, so a playhead that advances past A cannot turn an accepted point into a rejection.
        bool rejected = hadA && sampledPosition <= sampledA && player.ABLoopB == previousB;
        ShowToast(AbLoopToastTitle, rejected ? "B must be after A" : "B set");
    }

    [RelayCommand]
    public void ClearAbLoop()
    {
        if (!IsOpened)
            return;
        bool had = AbLoopActive;
        player.ClearABLoop();
        if (had && !AbLoopActive)
            ShowToast(AbLoopToastTitle, "A-B cleared");
    }

    /// <summary>Mirrors every engine A-B change (commands, bound hotkeys, reset on open) into the bound properties.</summary>
    void SyncAbLoop()
    {
        OnPropertyChanged(nameof(AbLoopActive));
        OnPropertyChanged(nameof(AbLoopEnabled));
        OnPropertyChanged(nameof(HasAbLoopA));
        OnPropertyChanged(nameof(HasAbLoopB));
        OnPropertyChanged(nameof(AbLoopStartSeconds));
        OnPropertyChanged(nameof(AbLoopEndSeconds));
    }

    // ---- commands ----------------------------------------------------------------------------------------------

    public string RecognitionStatus => (player as IRecognitionController)?.RecognitionStatus ?? "Recognition idle";

    [RelayCommand]
    void ShowRecognition()
    {
        if (player is IRecognitionController controller && controller.RecognitionStore is { } store)
            shell.ShowRecognition(new RecognitionViewModel(controller.RecognitionSettings, controller.Recognition, store, controller.RecognitionWarning));
    }

    void OnRecognitionError(string message, string? settingsTab, string? actionHint)
    {
        string detail = message + (string.IsNullOrWhiteSpace(actionHint) ? "" : "\n" + actionHint);
        ShowToast("Recognition", detail, ToastVariant.Destructive);
        if (player is IRecognitionController controller && controller.RecognitionStore is { } store)
            shell.ShowRecognition(new RecognitionViewModel(controller.RecognitionSettings, controller.Recognition, store, detail));
    }

    [RelayCommand]
    async Task StartRecognitionAsync(string slot) => await RecognitionAction(controller => controller.StartRecognitionAsync(int.Parse(slot)));
    [RelayCommand]
    async Task CancelRecognitionAsync(string slot) => await RecognitionAction(controller => controller.CancelRecognitionAsync(int.Parse(slot)));
    [RelayCommand]
    async Task OcrRecognitionAsync(string slot) => await RecognitionAction(controller => controller.OcrRecognitionAsync(int.Parse(slot)));
    [RelayCommand]
    async Task ToggleRecognitionPauseAsync() => await RecognitionAction(controller => controller.ToggleRecognitionPauseAsync());
    async Task RecognitionAction(Func<IRecognitionController, Task> action)
    {
        if (player is not IRecognitionController controller) return;
        try { await action(controller); }
        catch (RecognitionActionCanceledException) { }
        catch (Exception e) { OnRecognitionError(e.Message, null, null); }
        OnPropertyChanged(nameof(RecognitionStatus));
    }

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
    public void SubtitlesUp() => SubtitleBottomOffset = Math.Min(AppPrefs.MaxSubtitleBottomOffset, SubtitleBottomOffset + SubtitlePositionStep);

    [RelayCommand]
    public void SubtitlesDown() => SubtitleBottomOffset = Math.Max(AppPrefs.MinSubtitleBottomOffset, SubtitleBottomOffset - SubtitlePositionStep);

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
        object request = new();
        pendingMediaRequest = request;
        player.Open(path, error =>
        {
            if (disposed || !ReferenceEquals(pendingMediaRequest, request))
                return;
            pendingMediaRequest = null;
            string[] subs = [.. pendingSubtitles];
            pendingSubtitles.Clear();
            if (error == null)
                foreach (string subtitle in subs)
                    player.OpenSubtitles(subtitle);
        });
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

    public bool PrimaryTextTranslated => player.PrimaryTextTranslated;
    public bool SecondaryTextTranslated => player.SecondaryTextTranslated;

    /// <summary>Word press starts a possible drag selection; pause immediately (WPF WordClickedDown parity).</summary>
    public void PauseForWordSelection()
    {
        if (player.IsPlaying)
            player.Pause();
    }

    public void OnWordClicked(string word, string context, int slot, double x, double y, bool? translated = null, string? sourceLanguage = null, string? targetLanguage = null)
    {
        if (WordClickAction == "Clipboard" || WordClickAction == "ClipboardAll")
        {
            if (player.IsPlaying)
                player.Pause();
            string text = WordClickAction == "Clipboard" ? word : context;
            if (!string.IsNullOrWhiteSpace(text))
            {
                shell.SetClipboardText(text);
                ShowToast("Copied", WordClickAction == "Clipboard" ? "Word copied to the clipboard." : "Cue copied to the clipboard.");
            }
            return;
        }
        if (player.IsPlaying)
            player.Pause();
        _ = WordPopup.OpenAsync(word, context, slot, x, y, translated ?? (slot == 0 ? player.PrimaryTextTranslated : player.SecondaryTextTranslated), sourceLanguage, targetLanguage);
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
        Sidebar.PropertyChanged -= OnSidebarPropertyChanged;
        player.PropertyChanged -= OnPlayerPropertyChanged;
        player.CuesChanged -= OnCuesChanged;
        player.OpenCompleted -= OnOpenCompleted;
        player.ExternalMediaOpening -= OnExternalMediaOpening;
        player.ErrorOccurred -= OnPlayerError;
        if (player is IRecognitionController recognition) recognition.RecognitionError -= OnRecognitionError;
        WordPopup.Invalidate();
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

    void OnExternalMediaOpening()
    {
        pendingMediaRequest = null;
        pendingSubtitles.Clear();
    }

    void OnOpenCompleted(string? url, bool isSubtitles, string? error)
    {
        if (error != null)
        {
            if (!isSubtitles && pendingMediaRequest == null)
                pendingSubtitles.Clear();
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

        // Native engine shortcuts also open media. Their pending subtitles retain the generic event path;
        // app-owned requests consume their own sidecars only from their per-request completion callback.
        if (pendingMediaRequest == null)
        {
            string[] subs = [.. pendingSubtitles];
            pendingSubtitles.Clear();
            foreach (string s in subs)
                player.OpenSubtitles(s);
        }

        Sidebar.Reload();
        SyncAllFromPlayer();
    }

    void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SubtitlesSidebarViewModel.MatchCase):
                prefs.SidebarSearchMatchCase = Sidebar.MatchCase;
                break;
            case nameof(SubtitlesSidebarViewModel.WholeWord):
                prefs.SidebarSearchWholeWord = Sidebar.WholeWord;
                break;
            case nameof(SubtitlesSidebarViewModel.UseRegex):
                prefs.SidebarSearchRegex = Sidebar.UseRegex;
                break;
            default:
                return;
        }
        SavePrefs();
    }

    void OnPlayerError(string message) => ShowToast("Playback error", message, ToastVariant.Destructive);

    void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPlaybackController.PrimaryTextTranslated) or nameof(IPlaybackController.SecondaryTextTranslated)) OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(IPlaybackController.PrimaryTranslationEnabled) or nameof(IPlaybackController.SecondaryTranslationEnabled)) OnPropertyChanged(e.PropertyName);
        if (e.PropertyName == "SubtitleLanguage") InvalidateTranslation();
        if (e.PropertyName == "RecognitionStatus") OnPropertyChanged(nameof(RecognitionStatus));
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
                if (player.IsOpening) InvalidateTranslation();
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
            // F-12 waveform: the engine raises Peaks and Active separately; mirror each notification 1:1 (the overlay
            // visibility binds to WaveformActive, like WPF). Peaks also refresh the cached active flag silently.
            case nameof(IPlaybackController.WaveformPeaks):
                WaveformPeaks = player.WaveformPeaks;
                WaveformActive = player.WaveformActive;
                OnPropertyChanged(nameof(WaveformPeaks));
                break;
            case nameof(IPlaybackController.WaveformActive):
                WaveformActive = player.WaveformActive;
                OnPropertyChanged(nameof(WaveformActive));
                break;
            case nameof(IPlaybackController.ABLoopA):
            case nameof(IPlaybackController.ABLoopB):
            case nameof(IPlaybackController.HasABLoopA):
            case nameof(IPlaybackController.HasABLoopB):
            case nameof(IPlaybackController.ABLoopEnabled):
            case nameof(IPlaybackController.ABLoopActive):
                SyncAbLoop();
                break;
            case nameof(IPlaybackController.PrimaryText):
            case nameof(IPlaybackController.SecondaryText):
            case nameof(IPlaybackController.SubtitlesVisible):
                SyncSubtitles();
                break;
            case nameof(IPlaybackController.PrimarySlotEnabled):
                PrimarySlotEnabled = player.PrimarySlotEnabled;
                break;
            case nameof(IPlaybackController.SecondarySlotEnabled):
                SecondarySlotEnabled = player.SecondarySlotEnabled;
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
        SyncAbLoop();
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
        PrimarySlotEnabled = player.PrimarySlotEnabled;
        SecondarySlotEnabled = player.SecondarySlotEnabled;
    }
}

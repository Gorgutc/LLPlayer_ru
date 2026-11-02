using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib.Controls;
using FlyleafLib.MediaFramework.MediaRenderer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

/// <summary>
/// Main player window. Implements <see cref="IAppShell"/> for the view model and FlyleafLib's
/// <see cref="IHostPlayer"/> (fullscreen requests from the engine's key bindings), routes keyboard input to the view
/// model (app shortcuts first, then the engine's bindings) and hosts the <see cref="VideoView"/>.
/// </summary>
public partial class MainWindow : Window, IAppShell, IHostPlayer
{
    const double MinimumVideoColumnWidth = 240;
    readonly KeyMapper keys;
    readonly HashSet<Key> searchKeysAwaitingRelease = [];
    readonly AvaloniaHostServices? hostServices;
    readonly DispatcherTimer idleTimer;
    readonly DoubleTransition transportOpacity = new() { Property = OpacityProperty };
    readonly List<FlyoutBase> transportFlyouts = [];
    sealed record TransportRevealPress(IPointer Pointer);
    TransportRevealPress? transportRevealPress;
    bool transportRevealSequence;
    Point? lastPointerPosition;
    bool transportPointerOver;
    MainWindowViewModel? vm;
    SubtitleText? sidebarSelectionControl;
    CueItemViewModel? sidebarSelectionRow;
    CueInfo? sidebarSelectionCue;
    sealed record CueSyncPress(Button Button, CueItemViewModel? Row, CueInfo? Cue, Key Key = Key.None);
    CueSyncPress? cueSyncPointerPress;
    CueSyncPress? cueSyncKeyboardPress;
    int sidebarColumnIndex = 2;
    Func<IReadOnlyList<ShortcutInfo>> cheatSheetRows = () => AppShortcutMap.BuildCheatSheet([]);
    WindowState stateBeforeFullScreen = WindowState.Normal;
    Task<IDisposable>? sleepInhibition;
    CheatSheetWindow? cheatSheet;
    SettingsWindow? settingsWindow;
    public void ShowSettings(SettingsViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        _ = ShowSettingsAsync(model);
    }
    async Task ShowSettingsAsync(SettingsViewModel model)
    {
        var window = new SettingsWindow(model);
        OwnKeyboardSurface(window);
        settingsWindow = window;
        try { await window.ShowDialog(this); }
        finally { settingsWindow = null; }
        // ShowDialog has finished modal cleanup; the linked dialog belongs to this main window.
        if (IsVisible && model.RequestedLink is { } link) vm?.OpenSettingsTool(link);
    }

    RecognitionWindow? recognitionWindow;
    WordManagerWindow? wordManagerWindow;
    SubtitlesExportWindow? subtitlesExportWindow;

    public void ShowSubtitlesExport(SubtitlesExportViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (subtitlesExportWindow != null) { subtitlesExportWindow.Activate(); return; }
        subtitlesExportWindow = new SubtitlesExportWindow(model);
        OwnKeyboardSurface(subtitlesExportWindow);
        subtitlesExportWindow.Closed += (_, _) =>
        {
            subtitlesExportWindow = null;
            vm?.SubtitlesExportClosed(model);
        };
        _ = subtitlesExportWindow.ShowDialog(this);
    }
    public void ShowWordManager(WordManagerViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (wordManagerWindow != null) { wordManagerWindow.Activate(); return; }
        wordManagerWindow = new WordManagerWindow(model);
        OwnKeyboardSurface(wordManagerWindow);
        wordManagerWindow.Closed += (_, _) => { wordManagerWindow = null; vm?.WordManagerClosed(); };
        wordManagerWindow.Show(this);
    }
    TranslationWindow? translationWindow;
    OnlineVideoWindow? onlineVideoWindow;
    public void ShowOnlineVideo(OnlineVideoViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (onlineVideoWindow != null) { onlineVideoWindow.Activate(); return; }
        onlineVideoWindow = new OnlineVideoWindow(model);
        OwnKeyboardSurface(onlineVideoWindow);
        onlineVideoWindow.Closed += (_, _) => onlineVideoWindow = null;
        _ = onlineVideoWindow.ShowDialog(this);
    }
    public Func<Task>? ShutdownRequested { get; set; }
    bool shutdownComplete;
    bool shutdownStarted;
    public void ShowTranslation(TranslationViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (translationWindow != null) { translationWindow.Activate(); return; }
        translationWindow = new TranslationWindow(model);
        OwnKeyboardSurface(translationWindow);
        translationWindow.Closed += (_, _) => translationWindow = null;
        _ = translationWindow.ShowDialog(this);
    }

    public void ShowRecognition(RecognitionViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (recognitionWindow != null)
        {
            if (recognitionWindow.DataContext is RecognitionViewModel current) current.Message = model.Message;
            recognitionWindow.Activate(); return;
        }
        recognitionWindow = new RecognitionWindow(model);
        OwnKeyboardSurface(recognitionWindow);
        recognitionWindow.Closed += (_, _) => recognitionWindow = null;
        _ = recognitionWindow.ShowDialog(this);
    }

    public MainWindow() : this(new KeyMapper(), null)
    {
    }

    public MainWindow(KeyMapper keys, AvaloniaHostServices? hostServices)
    {
        this.keys = keys;
        this.hostServices = hostServices;
        InitializeComponent();
        TransportBar.Transitions = [transportOpacity];
        TransportBar.PropertyChanged += OnTransportPropertyChanged;
        LayoutUpdated += OnTransportLayoutUpdated;
        TrackTransportFlyout(SpeedButton.Flyout);
        TrackTransportFlyout(AbLoopButton.ContextFlyout);

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        Deactivated += (_, _) =>
        {
            ClearCommandButtonKeys();
            cueSyncPointerPress = cueSyncKeyboardPress = null;
            transportRevealPress = null;
            transportRevealSequence = false;
            ClearTransportPointer();
            vm?.LoseKeyboardContext();
        };
        Activated += (_, _) =>
        {
            if (vm?.RestoreKeyboardContext() == true) searchKeysAwaitingRelease.Clear();
        };
        AddHandler(PointerMovedEvent, OnAnyPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnAnyPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnAnyPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => ClearTransportPointer();

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => { if (vm != null) vm.IsDragOver = false; });
        AddHandler(DragDrop.DropEvent, OnDrop);

        SeekSlider.AddHandler(PointerPressedEvent, (_, _) => vm?.BeginSeekDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, (_, _) => vm?.EndSeekDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, (_, _) => vm?.EndSeekDrag(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);

        PrimarySubtitle.WordClicked += OnWordClicked;
        SecondarySubtitle.WordClicked += OnWordClicked;
        PrimarySubtitle.WordPressed += OnWordPressed;
        SecondarySubtitle.WordPressed += OnWordPressed;
        VideoHost.DoubleTapped += OnVideoDoubleTapped;
        VideoHost.Tapped += OnVideoTapped;
        VideoHost.PointerWheelChanged += OnVideoWheel;
        VideoContextMenu.Opening += (_, _) => { vm?.CancelAppShortcuts(); vm?.RefreshTracks(); vm?.RefreshPlaybackChoices(); };
        VideoHost.AddHandler(ContextRequestedEvent, OnVideoContextRequested, RoutingStrategies.Tunnel);
        WordPopupCard.SizeChanged += (_, _) => PositionWordPopup();
        VideoHost.SizeChanged += (_, _) => PositionWordPopup();
        VideoHost.SizeChanged += (_, _) => UpdateSubtitleLayout();
        SubtitleOverlay.SizeChanged += (_, _) => UpdateSubtitleLayout();
        TransportBar.SizeChanged += (_, _) =>
        {
            TransportRevealRegion.Height = TransportBar.Bounds.Height;
            UpdateSubtitleLayout();
        };
        ContentGrid.SizeChanged += (_, _) => UpdateSidebarColumn();
        SidebarSplitter.SizeChanged += (_, _) => UpdateSidebarColumn();
        CueList.Tapped += OnCueTapped;
        SidebarSplitter.DragCompleted += (_, _) =>
        {
            double width = ContentGrid.ColumnDefinitions[sidebarColumnIndex].ActualWidth;
            if (vm != null && vm.SidebarVisible && width > 0)
                vm.SidebarWidth = width;
        };
        PlaybackMenu.SubmenuOpened += (_, e) =>
        {
            // Opening a nested choice menu bubbles too; keep its live containers and snapshot intact.
            if (e.Source == PlaybackMenu)
                vm?.RefreshPlaybackChoices();
        };
        SubtitlesMenu.SubmenuOpened += (_, e) =>
        {
            // SubmenuOpened bubbles from the nested track submenus too: rebuild only when "Subtitles" itself opens.
            if (e.Source == SubtitlesMenu)
                vm?.RefreshTracks();
        };

        idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        idleTimer.Tick += (_, _) => vm?.UpdateIdle();
    }

    public MainWindowViewModel? ViewModel => vm;
    public VideoView VideoSurface => Video;

    /// <summary>Connects the view model, the engine's renderer (null in tests) and the cheat-sheet source.</summary>
    public void Attach(MainWindowViewModel viewModel, Renderer? renderer, Func<IReadOnlyList<ShortcutInfo>>? cheatSheetSource = null)
    {
        vm = viewModel;
        DataContext = viewModel;
        if (cheatSheetSource != null)
            cheatSheetRows = cheatSheetSource;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.WordPopup.PropertyChanged += OnWordPopupPropertyChanged;
        viewModel.Sidebar.PropertyChanged += OnSidebarPropertyChanged;
        viewModel.Sidebar.FocusFilterRequested += () => Dispatcher.UIThread.Post(() => FilterBox.Focus(NavigationMethod.Tab));

        Video.Renderer = renderer;
        ApplySeekBarGeometry();
        viewModel.SetTransportPointerOver(transportPointerOver);
        UpdateTransportInteraction();
        UpdateTransportVisibility();
        UpdateSubtitleLayout();
        ApplySidebarSide();
        idleTimer.Start();
    }

    // ---- IAppShell -------------------------------------------------------------------------------------------------

    public bool IsFullScreen
    {
        get => WindowState == WindowState.FullScreen;
        set
        {
            if (value == IsFullScreen)
                return;

            if (value)
            {
                stateBeforeFullScreen = WindowState;
                WindowState = WindowState.FullScreen;
            }
            else
            {
                WindowState = stateBeforeFullScreen == WindowState.FullScreen ? WindowState.Normal : stateBeforeFullScreen;
            }
        }
    }

    public Task<string?> PickMediaFileAsync(string? initialFolder)
        => hostServices?.PickFileAsync("Open media", AvaloniaHostServices.MediaFileTypes, initialFolder) ?? Task.FromResult<string?>(null);

    public Task<string?> PickSubtitlesFileAsync(string? initialFolder)
        => hostServices?.PickFileAsync("Open subtitles", AvaloniaHostServices.SubtitleFileTypes, initialFolder) ?? Task.FromResult<string?>(null);

    public void ShowCheatSheet()
    {
        vm?.CancelAppShortcuts();
        if (cheatSheet != null)
        {
            cheatSheet.Activate();
            return;
        }

        cheatSheet = new CheatSheetWindow(new CheatSheetViewModel(cheatSheetRows()));
        OwnKeyboardSurface(cheatSheet);
        cheatSheet.Closed += (_, _) => cheatSheet = null;
        if (IsVisible)
            cheatSheet.Show(this);
        else
            cheatSheet.Show();
    }

    public async Task<bool> OpenBrowserAsync(Uri uri)
    {
        if (!WordActions.IsHttpUri(uri)) return false;
        try { return await Launcher.LaunchUriAsync(uri); }
        catch (Exception) { return false; }
    }

    void ShowWordMenu(Visual target, IReadOnlyList<WordMenuInvocation> invocations)
    {
        if (target is not Control control || vm == null || invocations.Count == 0) return;
        MainWindowViewModel owner = vm;
        owner.CancelAppShortcuts();
        ContextMenu menu = new();
        foreach (WordMenuInvocation invocation in invocations)
        {
            MenuItem item = new() { Header = invocation.Action.Title };
            item.Click += (_, _) => owner.TrackBrowserOperation(owner.InvokeWordMenuAsync(invocation));
            menu.Items.Add(item);
        }
        menu.Open(control);
    }

    void OnPopupWordContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Visual target && vm != null)
        {
            ShowWordMenu(target, vm.CreatePopupWordMenu());
            e.Handled = true;
        }
    }

    void OwnKeyboardSurface(Window window)
    {
        // A dialog may receive the release of the key which opened it. Finish the parent's ownership there,
        // and retain child-origin presses when Escape closes the child before its paired release arrives.
        window.AddHandler(KeyDownEvent, (_, e) => vm?.SuppressInputKeyDown(KeyMapper.ToWpf(e.Key)),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(KeyUpEvent, (_, e) =>
        {
            TryHandleCommandButtonKeyUp(e, fromOwnedChild: true);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(KeyUpEvent, (_, e) =>
        {
            if (vm?.HandleTrackedKeyUp(KeyMapper.ToWpf(e.Key)) == true) e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public void SetClipboardText(string text)
    {
        if (Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(text);
    }

    public void ApplyTheme(string theme) => ThemeService.Apply(theme);

    public void Exit() => Close();

    // ---- IHostPlayer (engine key bindings: F / Esc / ToggleFullScreen) ---------------------------------------------

    public bool Player_CanHideCursor() => false;   // the window hides the cursor itself (CursorHidden)

    public bool Player_GetFullScreen() => OnUI(() => IsFullScreen);

    public void Player_SetFullScreen(bool value) => OnUI(() => { IsFullScreen = value; return true; });

    public void Player_RatioChanged(double keepRatio)
    {
    }

    public bool Player_HandlesRatioResize(int width, int height) => false;

    public void Player_Disposed()
    {
    }

    static T OnUI<T>(Func<T> func)
        => Dispatcher.UIThread.CheckAccess() ? func() : Dispatcher.UIThread.Invoke(func);

    // ---- window events ---------------------------------------------------------------------------------------------

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            bool fullScreen = WindowState == WindowState.FullScreen;
            ApplyFullScreenLayout(fullScreen);
            vm?.OnFullScreenChanged(fullScreen);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || shutdownComplete) return;
        ClearCommandButtonKeys();
        vm?.BeginWordActionsShutdown();
        vm?.CancelAppShortcuts();
        if (ShutdownRequested == null) { vm?.SavePrefs(); return; }
        e.Cancel = true;
        if (!shutdownStarted) { shutdownStarted = true; IsEnabled = false; _ = DrainBeforeClosingAsync(); }
    }

    async Task DrainBeforeClosingAsync()
    {
        try
        {
            if (translationWindow?.DataContext is TranslationViewModel translation)
                await translation.DrainDiagnosticsAsync();
            if (vm != null) await vm.DrainBrowserOperationsAsync();
            await DrainParityWindowsAsync();
            await ShutdownRequested!();
        }
        finally { shutdownComplete = true; Close(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        idleTimer.Stop();
        LayoutUpdated -= OnTransportLayoutUpdated;
        TransportBar.PropertyChanged -= OnTransportPropertyChanged;
        foreach (FlyoutBase flyout in transportFlyouts)
            flyout.PropertyChanged -= OnTransportFlyoutPropertyChanged;
        settingsWindow?.Close();
        cheatSheet?.Close();
        translationWindow?.Close();
        onlineVideoWindow?.Close();
        recognitionWindow?.Close();
        subtitlesExportWindow?.Close();
        CloseParityWindows();
        ReleaseSleepInhibition();
        Video.Renderer = null;
        base.OnClosed(e);
    }

    void ApplyFullScreenLayout(bool fullScreen)
    {
        Grid.SetRowSpan(VideoHost, fullScreen ? 2 : 1);
        Grid.SetRow(TransportBar, fullScreen ? 0 : 1);
        Grid.SetRow(TransportRevealRegion, fullScreen ? 0 : 1);
        TransportBar.Classes.Set("overlay", fullScreen);
        UpdateSubtitleLayout();
    }

    /// <summary>The sidebar column gets the remembered width (the splitter resizes it); hidden = collapsed. The
    /// sidebar may be docked left or right, so the column index is derived from <see cref="MainWindowViewModel.SidebarLeft"/>.</summary>
    void UpdateSidebarColumn()
    {
        if (vm == null)
            return;

        sidebarColumnIndex = vm.SidebarLeft ? 0 : 2;
        int videoColumnIndex = vm.SidebarLeft ? 2 : 0;
        ColumnDefinition sidebarColumn = ContentGrid.ColumnDefinitions[sidebarColumnIndex];
        ColumnDefinition videoColumn = ContentGrid.ColumnDefinitions[videoColumnIndex];
        // The column that becomes the video may have been the sidebar before a dock change, so its remembered
        // Min/Max must be cleared instead of capping the video area.
        videoColumn.MinWidth = 0;
        videoColumn.MaxWidth = double.PositiveInfinity;
        videoColumn.Width = new GridLength(1, GridUnitType.Star);
        if (vm.SidebarVisible)
        {
            sidebarColumn.MinWidth = 220;
            // Keep the remembered width; cap only the arranged column so narrowing does not swallow the video.
            double splitterWidth = Math.Max(SidebarSplitter.DesiredSize.Width, ContentGrid.ColumnDefinitions[1].ActualWidth);
            sidebarColumn.MaxWidth = Math.Max(sidebarColumn.MinWidth, ContentGrid.Bounds.Width - splitterWidth - MinimumVideoColumnWidth);
            sidebarColumn.Width = new GridLength(vm.SidebarWidth, GridUnitType.Pixel);
        }
        else
        {
            sidebarColumn.MinWidth = 0;
            sidebarColumn.MaxWidth = double.PositiveInfinity;
            sidebarColumn.Width = GridLength.Auto;
        }
    }

    /// <summary>Docks the sidebar left or right of the video by swapping the grid columns; the splitter stays between them.</summary>
    void ApplySidebarSide()
    {
        if (vm == null)
            return;

        sidebarColumnIndex = vm.SidebarLeft ? 0 : 2;
        Grid.SetColumn(VideoColumn, vm.SidebarLeft ? 2 : 0);
        Grid.SetColumn(Sidebar, sidebarColumnIndex);
        Grid.SetColumn(SidebarSplitter, 1);
        ContentGrid.ColumnDefinitions[1].Width = GridLength.Auto;
        UpdateSidebarColumn();
    }

    /// <summary>Keeps the bottom clearance, box width, separator gap and block-anchor alignment on one geometry seam.</summary>
    void UpdateSubtitleLayout()
    {
        if (vm == null)
            return;

        double extra = IsFullScreen && vm.ControlsVisible ? TransportBar.Bounds.Height : 0;
        // Center/Top reduce the margin by half/full height so that edge sits at the configured distance. WPF's
        // percentage geometry allows off-screen overflow (with FixOverflowSubsPosition); the DIP model instead clamps
        // the anchor, but never below the fullscreen transport clearance: exact while it fits, otherwise the block
        // stops at the clearance/bottom edge without clipping.
        double anchorShift = vm.EffectiveSubtitleVerticalAlignment switch
        {
            SubtitleVerticalAlignment.Center => -SubtitleOverlay.Bounds.Height / 2,
            SubtitleVerticalAlignment.Top => -SubtitleOverlay.Bounds.Height,
            _ => 0,
        };
        double bottom = Math.Max(extra, vm.SubtitleBottomOffset + extra + anchorShift);
        SubtitleOverlay.Margin = new Thickness(0, 0, 0, bottom);
        SubtitleOverlay.Width = MainWindowViewModel.ComputeSubtitleBoxWidth(VideoHost.Bounds.Width, vm.SubtitleWidthPercent, vm.SubtitleMaxWidth);
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.Shortcuts):
                if (cheatSheet?.DataContext is CheatSheetViewModel current)
                    cheatSheet.DataContext = new CheatSheetViewModel(cheatSheetRows()) { FilterText = current.FilterText };
                break;
            case nameof(MainWindowViewModel.SubtitleBottomOffset):
            case nameof(MainWindowViewModel.SubtitleWidthPercent):
            case nameof(MainWindowViewModel.SubtitleMaxWidth):
            case nameof(MainWindowViewModel.SubtitlePrimarySecondaryDistance):
            case nameof(MainWindowViewModel.SubtitlePositionAlignment):
            case nameof(MainWindowViewModel.SubtitlePositionAlignmentWhenDual):
            case nameof(MainWindowViewModel.SecondaryVisible):
            case nameof(MainWindowViewModel.EffectiveSubtitleVerticalAlignment):
                UpdateSubtitleLayout();
                break;
            case nameof(MainWindowViewModel.ControlsVisible):
            case nameof(MainWindowViewModel.SeekBarFadeInTimeMs):
            case nameof(MainWindowViewModel.SeekBarFadeOutTimeMs):
                UpdateTransportVisibility();
                UpdateSubtitleLayout();
                break;
            case nameof(MainWindowViewModel.SeekBarHeight):
            case nameof(MainWindowViewModel.SeekBarThumbHeight):
            case nameof(MainWindowViewModel.SeekBarTrackHeight):
                ApplySeekBarGeometry();
                break;
            case nameof(MainWindowViewModel.SeekBarShowOnlyMouseOver):
                if (vm?.SeekBarShowOnlyMouseOver != true)
                {
                    transportRevealPress = null;
                    transportRevealSequence = false;
                }
                break;
            case nameof(MainWindowViewModel.CursorHidden):
                VideoHost.Cursor = vm!.CursorHidden ? new Cursor(StandardCursorType.None) : Cursor.Default;
                break;
            case nameof(MainWindowViewModel.IsPlaying):
                UpdateSleepInhibition(vm!.IsPlaying);
                break;
            case nameof(MainWindowViewModel.SidebarWidth):
            case nameof(MainWindowViewModel.SidebarVisible):
                UpdateSidebarColumn();
                break;
            case nameof(MainWindowViewModel.SidebarLeft):
                ApplySidebarSide();
                break;
        }
    }

    void UpdateTransportVisibility()
    {
        if (vm == null)
            return;
        // Change the direction's duration before its target. Avalonia cancels/reverses the current transition;
        // there is no delayed completion callback that can overwrite a newer visibility decision.
        transportOpacity.Duration = TimeSpan.FromMilliseconds(vm.ControlsVisible
            ? vm.SeekBarFadeInTimeMs : vm.SeekBarFadeOutTimeMs);
        TransportBar.IsHitTestVisible = vm.ControlsVisible;
        TransportBar.Opacity = vm.ControlsVisible ? 1 : 0;
    }

    void ApplySeekBarGeometry()
    {
        if (vm == null)
            return;
        SeekSlider.Height = SeekSlider.MinHeight = vm.SeekBarHeight;
        SeekSlider.Resources["SliderHorizontalHeight"] = vm.SeekBarHeight;
        SeekSlider.Resources["SliderHorizontalThumbWidth"] = vm.SeekBarThumbHeight;
        SeekSlider.Resources["SliderHorizontalThumbHeight"] = vm.SeekBarThumbHeight;
        SeekSlider.Resources["SliderThumbCornerRadius"] = new CornerRadius(vm.SeekBarThumbHeight / 2);
        SeekSlider.Resources["SliderTrackThemeHeight"] = vm.SeekBarTrackHeight;
        // Both display-only overlays share the slider thumb's travel interval.
        WaveformEnvelope.Margin = new Thickness(vm.SeekBarThumbHeight / 2, 0);
        AbLoopMarkers.Inset = vm.SeekBarThumbHeight / 2;
    }

    void TrackTransportFlyout(FlyoutBase? flyout)
    {
        if (flyout == null)
            return;
        transportFlyouts.Add(flyout);
        flyout.PropertyChanged += OnTransportFlyoutPropertyChanged;
    }

    void OnTransportFlyoutPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == FlyoutBase.IsOpenProperty)
            UpdateTransportInteraction();
    }

    void OnTransportPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsKeyboardFocusWithinProperty)
            UpdateTransportInteraction();
    }

    void UpdateTransportInteraction()
    {
        vm?.SetTransportFocusWithin(TransportBar.IsKeyboardFocusWithin);
        vm?.SetTransportPopupOpen(transportFlyouts.Any(flyout => flyout.IsOpen));
    }

    bool IsInTransportRevealRegion(Point point)
        => TransportRevealRegion.TranslatePoint(default, this) is { } origin
            && new Rect(origin, TransportRevealRegion.Bounds.Size).Contains(point);

    void OnTransportLayoutUpdated(object? sender, EventArgs e) => UpdateTransportPointer();

    void UpdateTransportPointer()
    {
        // A resize, fullscreen transition or live geometry edit can move the strip under a stationary pointer.
        // Publish only a changed result, so visibility/subtitle layout updates cannot feed back into layout.
        bool pointerOver = lastPointerPosition is { } point && IsInTransportRevealRegion(point);
        if (pointerOver == transportPointerOver)
            return;
        transportPointerOver = pointerOver;
        vm?.SetTransportPointerOver(pointerOver);
    }

    void ClearTransportPointer()
    {
        lastPointerPosition = null;
        transportPointerOver = false;
        vm?.SetTransportPointerOver(false);
    }

    void OnWordPopupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WordPopupViewModel.IsOpen) or nameof(WordPopupViewModel.AnchorX) or nameof(WordPopupViewModel.AnchorY))
            Dispatcher.UIThread.Post(PositionWordPopup, DispatcherPriority.Loaded);
    }

    void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubtitlesSidebarViewModel.CurrentCue) && vm?.Sidebar.CurrentCue is { } cue
            && vm.Sidebar.Cues.Contains(cue) && SidebarVisibleAndIdle())
            CueList.ScrollIntoView(cue);
    }

    bool SidebarVisibleAndIdle() => Sidebar.IsVisible && !CueList.IsPointerOver;

    /// <summary>Places the word popup above the clicked word, kept inside the video area.</summary>
    void PositionWordPopup()
    {
        if (vm == null || !vm.WordPopup.IsOpen)
            return;

        double w = WordPopupCard.Bounds.Width > 0 ? WordPopupCard.Bounds.Width : WordPopupCard.Width;
        double h = WordPopupCard.Bounds.Height;
        double areaW = VideoHost.Bounds.Width, areaH = VideoHost.Bounds.Height;

        WordPopupCard.MaxHeight = Math.Max(40, areaH - 16);
        WordPopupCard.Width = Math.Min(300, Math.Max(120, areaW - 16));
        double left = Math.Clamp(vm.WordPopup.AnchorX - w / 2, 8, Math.Max(8, areaW - w - 8));
        double top = vm.WordPopup.AnchorY - h - 16;
        if (top < 8)
            top = Math.Min(vm.WordPopup.AnchorY + 24, Math.Max(8, areaH - h - 8));

        Canvas.SetLeft(WordPopupCard, left);
        Canvas.SetTop(WordPopupCard, Math.Clamp(top, 8, Math.Max(8, areaH - h - 8)));
    }

    void OnWordClicked(object? sender, WordClickedEventArgs e)
    {
        // Sidebar controls can still finish a selection after a slot/source/filter change. Only a live, readable
        // displayed row may act; overlay controls keep their independent selection ownership.
        if (vm == null || sender is not SubtitleText text)
            return;
        // Inherited DataContext is cleared when a virtualized row detaches. Its immutable name still identifies
        // sidebar input, so an old event can never fall through to the overlay path after losing its row.
        bool sidebar = text.Name == "SidebarCueText";
        if (!sidebar && !ReferenceEquals(text, PrimarySubtitle) && !ReferenceEquals(text, SecondarySubtitle))
            return;
        if (sidebar)
        {
            if (text.DataContext is not CueItemViewModel row)
            {
                ClearSidebarSelection();
                return;
            }
            // A recycled control can already carry a new valid DataContext when an earlier release arrives.
            // Left selection belongs to the exact row/cue captured on press; right-click acts immediately.
            bool sameSelection = e.Button == MouseButton.Right
                || ReferenceEquals(sidebarSelectionControl, text) && ReferenceEquals(sidebarSelectionRow, row)
                    && Equals(sidebarSelectionCue, row.Cue);
            ClearSidebarSelection();
            if (!sameSelection || row.IsMasked || !vm.Sidebar.IsCurrentRow(row)
                || e.Slot != row.Slot || e.Text != row.DisplayText || e.IsTranslated != row.IsDisplayTranslated
                || (e.SourceLanguage ?? "") != row.SourceLanguage || (e.TargetLanguage ?? "") != row.TargetLanguage)
                return;
        }
        else
            ClearSidebarSelection();

        Point p = text.TranslatePoint(e.Position, VideoHost) ?? e.Position;
        WordSelectionSnapshot selection = new(e.Word, e.Text, e.Slot, e.IsWord, e.IsTranslated,
            e.SourceLanguage, e.TargetLanguage,
            sidebar ? WordSelectionOrigin.Sidebar : WordSelectionOrigin.Overlay,
            e.Button, e.Modifiers, e.WordOffset);
        if (e.Button == MouseButton.Right) ShowWordMenu(text, vm.CreateWordMenu(selection));
        else vm.OnWordClicked(selection, p.X, p.Y);
    }

    /// <summary>A word press starts a possible drag selection; pause before the release action (WPF parity).</summary>
    void OnWordPressed(object? sender, EventArgs e)
    {
        ClearSidebarSelection();
        if (vm == null || sender is not SubtitleText text)
            return;
        if (text.Name == "SidebarCueText")
        {
            if (text.DataContext is not CueItemViewModel row || row.IsMasked || !vm.Sidebar.IsCurrentRow(row))
                return;
            sidebarSelectionControl = text;
            sidebarSelectionRow = row;
            sidebarSelectionCue = row.Cue;
        }
        else if (!ReferenceEquals(text, PrimarySubtitle) && !ReferenceEquals(text, SecondarySubtitle))
            return;
        vm.PauseForWordSelection();
    }

    void ClearSidebarSelection()
    {
        sidebarSelectionControl = null;
        sidebarSelectionRow = null;
        sidebarSelectionCue = null;
    }

    void OnVideoTapped(object? sender, TappedEventArgs e)
    {
        // Subtitle selection, popup contents and controls retain their own pointer actions.
        if (vm == null || keys.Modifiers != KeyModifiers.None || !IsVideoBackground(e.Source))
            return;
        if (transportRevealPress != null)
        {
            e.Handled = true;
            return;
        }
        if (vm.WordPopup.IsOpen)
            vm.WordPopup.Close();
        else if (vm.MouseSingleClickToPlay)
            vm.TogglePlayPause();
        e.Handled = true;
    }

    bool IsVideoBackground(object? source) => ReferenceEquals(source, Video) || ReferenceEquals(source, VideoHost);

    void OnVideoContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // The attached menu belongs to the video backdrop, not to subtitle words or popup controls.
        bool popupWord = ReferenceEquals(e.Source, PopupWordActions)
            || e.Source is Visual child && child.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, PopupWordActions));
        if (!IsVideoBackground(e.Source) && !popupWord)
            e.Handled = true;
    }

    void OnVideoDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (vm == null || !vm.MouseDoubleClickToFullScreen || transportRevealPress != null)
            return;
        if (e.Source is Visual src && (src.FindAncestorOfType<Button>(true) != null || src.FindAncestorOfType<SubtitleText>(true) != null
                                       || WordPopupCard.IsPointerOver))
            return;
        vm.ToggleFullScreen();
    }

    void OnVideoWheel(object? sender, PointerWheelEventArgs e)
    {
        if (vm == null || !vm.MouseWheelToVolumeUpDown || e.Delta.Y == 0)
            return;
        vm.Volume += e.Delta.Y > 0 ? 5 : -5;
        e.Handled = true;
    }

    void OnCueTapped(object? sender, TappedEventArgs e)
    {
        // Row actions and word selection own their gestures. Tapped can still be recognized after handled
        // pointer events, so neither a sync click nor a word click may also seek to the cue.
        if (e.Source is Visual source
            && (source.FindAncestorOfType<Button>(true) != null || source.FindAncestorOfType<SubtitleText>(true) != null))
            return;
        if (vm != null && e.Source is Control c && c.DataContext is CueItemViewModel cue)
            vm.Sidebar.SeekCommand.Execute(cue);
    }

    CueSyncPress CaptureCueSyncPress(Button button, Key key = Key.None)
    {
        CueItemViewModel? row = button.CommandParameter as CueItemViewModel;
        return new(button, row != null && vm?.Sidebar.IsCurrentRow(row) == true ? row : null, row?.Cue, key);
    }

    static bool OwnsCueSync(CueSyncPress press, CueItemViewModel row)
        => ReferenceEquals(press.Row, row) && press.Cue is { } cue
            && Equals(cue.Identity, row.Cue.Identity) && cue.Index == row.Index
            && cue.Start == row.Start && cue.End == row.End;

    void OnCueSyncClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;
        CueItemViewModel? row = button.CommandParameter as CueItemViewModel;
        bool valid = row != null && vm?.Sidebar.IsCurrentRow(row) == true;
        if (ReferenceEquals(cueSyncPointerPress?.Button, button))
        {
            valid &= row != null && OwnsCueSync(cueSyncPointerPress!, row);
        }
        if (ReferenceEquals(cueSyncKeyboardPress?.Button, button))
        {
            valid &= row != null && OwnsCueSync(cueSyncKeyboardPress!, row);
        }
        // Avalonia executes the bound Command after Click only when the event remains unhandled. Automation
        // without a physical press uses the live row; physical input must also retain its original cue owner.
        // Only release cleanup ends a press: an unrelated Click must not erase a still-held input's owner.
        if (!valid)
            e.Handled = true;
    }

    void OnAnyPointerMoved(object? sender, PointerEventArgs e)
    {
        keys.Update(e.KeyModifiers);
        lastPointerPosition = e.GetPosition(this);
        bool inRevealRegion = IsInTransportRevealRegion(lastPointerPosition.Value);
        if (e is PointerPressedEventArgs revealPress
            && revealPress.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // The original hit target may be the video while the transparent bar is hidden. That press
            // reveals controls only; keep its owner until Tapped/DoubleTapped have completed on release.
            if (revealPress.ClickCount == 1)
                transportRevealSequence = false;
            bool reveal = vm is { SeekBarShowOnlyMouseOver: true, ControlsVisible: false }
                && inRevealRegion && IsVideoBackground(e.Source);
            transportRevealSequence |= reveal;
            transportRevealPress = IsVideoBackground(e.Source) && transportRevealSequence
                ? new TransportRevealPress(e.Pointer) : null;
        }
        vm?.NotifyActivity();
        UpdateTransportPointer();
        if (e is PointerPressedEventArgs press)
        {
            cueSyncPointerPress = null;
            if (press.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is Visual source
                && source.FindAncestorOfType<Button>(true) is { Name: "CueSyncButton" } button)
                cueSyncPointerPress = CaptureCueSyncPress(button);
        }
        else if (e is PointerReleasedEventArgs { InitialPressMouseButton: MouseButton.Left })
        {
            TransportRevealPress? reveal = transportRevealPress;
            if (reveal != null && ReferenceEquals(reveal.Pointer, e.Pointer))
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(transportRevealPress, reveal)) transportRevealPress = null;
                });
            // Retain ownership through the native Click, then release canceled/outside presses too. A newer
            // pointer press must survive an older release's queued cleanup.
            CueSyncPress? released = cueSyncPointerPress;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(cueSyncPointerPress, released)) cueSyncPointerPress = null;
            });
        }
    }

    /// <summary>
    /// True when the focused control consumes the keyboard itself: text boxes (typing), menus / combo boxes (arrow
    /// navigation), and sidebar row actions (Space/Enter). Those keys do not reach app or engine bindings.
    /// </summary>
    bool IsTextInputFocused() => FocusManager?.GetFocusedElement()
        is TextBox or MenuItem or ComboBox or ComboBoxItem or Button { Name: "CueSyncButton" };

    void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        keys.Update(e.KeyModifiers);
        if (vm == null)
            return;
        vm.NotifyKeyboardActivity();
        if (TryHandleCommandButtonKeyDown(e)) return;

        if (e.Key is Key.Space or Key.Enter && FocusManager?.GetFocusedElement() is Button { Name: "CueSyncButton" } sync)
        {
            // KeyDown can repeat while held; retain the initial owner until its matching key release.
            if (cueSyncKeyboardPress == null)
                cueSyncKeyboardPress = CaptureCueSyncPress(sync, e.Key);
        }
        bool textFocused = IsTextInputFocused();
        if (textFocused && e.Key == Key.Enter && FocusManager?.GetFocusedElement() == FilterBox
            && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            searchKeysAwaitingRelease.Add(e.Key);
            vm.SuppressInputKeyDown(KeyMapper.ToWpf(e.Key));
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                vm.Sidebar.PreviousMatchCommand.Execute(null);
            else
                vm.Sidebar.NextMatchCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (textFocused && e.Key == Key.Escape && FocusManager?.GetFocusedElement() == FilterBox)
        {
            // WPF sidebar: Esc clears the search and returns focus to the video.
            searchKeysAwaitingRelease.Add(e.Key);
            vm.SuppressInputKeyDown(KeyMapper.ToWpf(e.Key));
            vm.Sidebar.FilterText = "";
            Video.Focus();
            e.Handled = true;
            return;
        }

        var wpfKey = KeyMapper.ToWpf(e.Key);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (vm.HandleKeyDown(wpfKey, ctrl, alt, shift, textFocused))
            e.Handled = true;
    }

    void OnKeyUpTunnel(object? sender, KeyEventArgs e)
    {
        keys.Update(e.KeyModifiers);
        if (TryHandleCommandButtonKeyUp(e)) return;
        if (searchKeysAwaitingRelease.Remove(e.Key))
        {
            vm?.HandleTrackedKeyUp(KeyMapper.ToWpf(e.Key));
            // Esc moved focus out of the filter, but its paired release still belongs to the search action.
            e.Handled = true;
            return;
        }
        // Drain the VM's suppressed-key ownership, but a focused row button still needs its Space/Enter release
        // to complete the native button click. Consuming that release would leave keyboard sync inert.
        Button? sync = FocusManager?.GetFocusedElement() is Button { Name: "CueSyncButton" } focused ? focused : null;
        bool rowButtonRelease = e.Key is Key.Space or Key.Enter && sync != null;
        // Button.OnKeyUp invokes Space even after focus transferred from another control. Consume releases
        // without this button's keydown owner instead of mistaking that physical click for automation.
        if (e.Key == Key.Space && sync != null
            && (cueSyncKeyboardPress?.Key != Key.Space || !ReferenceEquals(cueSyncKeyboardPress.Button, sync)))
        {
            vm?.HandleKeyUp(KeyMapper.ToWpf(e.Key), true);
            if (cueSyncKeyboardPress?.Key == Key.Space) cueSyncKeyboardPress = null;
            e.Handled = true;
            return;
        }
        if (vm != null && vm.HandleKeyUp(KeyMapper.ToWpf(e.Key), IsTextInputFocused()) && !rowButtonRelease)
            e.Handled = true;
        if (e.Key is Key.Space or Key.Enter && cueSyncKeyboardPress?.Key == e.Key)
        {
            CueSyncPress? released = cueSyncKeyboardPress;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(cueSyncKeyboardPress, released)) cueSyncKeyboardPress = null;
            });
        }
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (vm != null)
            vm.IsDragOver = hasFiles;
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        if (vm == null)
            return;

        List<string> paths = [];
        if (e.DataTransfer.TryGetFiles() is { } files)
        {
            foreach (IStorageItem item in files)
                if (item.TryGetLocalPath() is { } path)
                    paths.Add(path);
        }

        vm.IsDragOver = false;
        if (paths.Count > 0)
            vm.HandleDrop(paths);
    }

    void UpdateSleepInhibition(bool playing)
    {
        if (playing)
        {
            if (sleepInhibition == null)
            {
                try
                {
                    sleepInhibition = RequestPlatformInhibition(PlatformInhibitionType.AppSleep, "Playing video");
                }
                catch (Exception)
                {
                    sleepInhibition = null;   // not supported by the platform (e.g. headless)
                }
            }
        }
        else
        {
            ReleaseSleepInhibition();
        }
    }

    void ReleaseSleepInhibition()
    {
        Task<IDisposable>? t = sleepInhibition;
        sleepInhibition = null;
        t?.ContinueWith(r =>
        {
            if (r.IsCompletedSuccessfully)
                r.Result.Dispose();
            else
                _ = r.Exception;   // observed: an unsupported inhibition is not an error
        }, TaskScheduler.Default);
    }
}

/// <summary>
/// Keeps the transport's leading controls left and trailing controls right when they fit on one row.
/// A narrow video column wraps controls at their natural sizes instead of placing them under the sidebar.
/// </summary>
public sealed class TransportPanel : Panel
{
    const int LeadingControlCount = 3;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
            child.Measure(Size.Infinity);
        return Layout(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }

    Size Layout(double width, bool arrange)
    {
        double totalWidth = Children.Sum(child => child.DesiredSize.Width);
        double maxHeight = Children.Count == 0 ? 0 : Children.Max(child => child.DesiredSize.Height);
        if (totalWidth <= width)
        {
            if (arrange)
            {
                double left = 0;
                for (int index = 0; index < Children.Count; index++)
                {
                    if (index == LeadingControlCount)
                        left += width - totalWidth;
                    Control child = Children[index];
                    child.Arrange(new Rect(left, 0, child.DesiredSize.Width, maxHeight));
                    left += child.DesiredSize.Width;
                }
            }
            return new Size(totalWidth, maxHeight);
        }

        double rowWidth = 0, rowHeight = 0, top = 0;
        int rowStart = 0;
        for (int index = 0; index < Children.Count; index++)
        {
            Size desired = Children[index].DesiredSize;
            double childWidth = Math.Min(width, desired.Width);
            if (index > rowStart && rowWidth + childWidth > width)
            {
                if (arrange) ArrangeRow(rowStart, index, width, top, rowHeight);
                top += rowHeight;
                rowStart = index;
                rowWidth = rowHeight = 0;
            }
            rowWidth += childWidth;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }
        if (arrange) ArrangeRow(rowStart, Children.Count, width, top, rowHeight);
        return new Size(width, top + rowHeight);
    }

    void ArrangeRow(int start, int end, double width, double top, double height)
    {
        double left = 0;
        for (int index = start; index < end; index++)
        {
            Control child = Children[index];
            double childWidth = Math.Min(width, child.DesiredSize.Width);
            child.Arrange(new Rect(left, top, childWidth, height));
            left += childWidth;
        }
    }
}

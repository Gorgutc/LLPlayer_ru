using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
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
    readonly AvaloniaHostServices? hostServices;
    readonly DispatcherTimer idleTimer;
    MainWindowViewModel? vm;
    int sidebarColumnIndex = 2;
    Func<IReadOnlyList<ShortcutInfo>> cheatSheetRows = () => AppShortcutMap.BuildCheatSheet([]);
    WindowState stateBeforeFullScreen = WindowState.Normal;
    Task<IDisposable>? sleepInhibition;
    CheatSheetWindow? cheatSheet;
    SettingsWindow? settingsWindow;
    public void ShowSettings(SettingsViewModel model)
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        _ = ShowSettingsAsync(model);
    }
    async Task ShowSettingsAsync(SettingsViewModel model)
    {
        var window = new SettingsWindow(model);
        settingsWindow = window;
        try { await window.ShowDialog(this); }
        finally { settingsWindow = null; }
        // ShowDialog has finished modal cleanup; the linked dialog belongs to this main window.
        if (IsVisible && model.RequestedLink is { } link) vm?.OpenSettingsTool(link);
    }

    RecognitionWindow? recognitionWindow;
    WordManagerWindow? wordManagerWindow;
    public void ShowWordManager(WordManagerViewModel model)
    {
        if (wordManagerWindow != null) { wordManagerWindow.Activate(); return; }
        wordManagerWindow = new WordManagerWindow(model);
        wordManagerWindow.Closed += (_, _) => { wordManagerWindow = null; vm?.WordManagerClosed(); };
        wordManagerWindow.Show(this);
    }
    TranslationWindow? translationWindow;
    public Func<Task>? ShutdownRequested { get; set; }
    bool shutdownComplete;
    bool shutdownStarted;
    public void ShowTranslation(TranslationViewModel model)
    {
        if (translationWindow != null) { translationWindow.Activate(); return; }
        translationWindow = new TranslationWindow(model);
        translationWindow.Closed += (_, _) => translationWindow = null;
        _ = translationWindow.ShowDialog(this);
    }

    public void ShowRecognition(RecognitionViewModel model)
    {
        if (recognitionWindow != null)
        {
            if (recognitionWindow.DataContext is RecognitionViewModel current) current.Message = model.Message;
            recognitionWindow.Activate(); return;
        }
        recognitionWindow = new RecognitionWindow(model);
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

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnAnyPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnAnyPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => { if (vm != null) vm.IsDragOver = false; });
        AddHandler(DragDrop.DropEvent, OnDrop);

        SeekSlider.AddHandler(PointerPressedEvent, (_, _) => vm?.BeginSeekDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, (_, _) => vm?.EndSeekDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, (_, _) => vm?.EndSeekDrag(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);

        PrimarySubtitle.WordClicked += OnWordClicked;
        SecondarySubtitle.WordClicked += OnWordClicked;
        VideoHost.DoubleTapped += OnVideoDoubleTapped;
        VideoHost.Tapped += OnVideoTapped;
        VideoHost.PointerWheelChanged += OnVideoWheel;
        WordPopupCard.SizeChanged += (_, _) => PositionWordPopup();
        VideoHost.SizeChanged += (_, _) => PositionWordPopup();
        VideoHost.SizeChanged += (_, _) => UpdateSubtitleLayout();
        SubtitleOverlay.SizeChanged += (_, _) => UpdateSubtitleLayout();
        TransportBar.SizeChanged += (_, _) => UpdateSubtitleLayout();
        ContentGrid.SizeChanged += (_, _) => UpdateSidebarColumn();
        SidebarSplitter.SizeChanged += (_, _) => UpdateSidebarColumn();
        CueList.Tapped += OnCueTapped;
        SidebarSplitter.DragCompleted += (_, _) =>
        {
            double width = ContentGrid.ColumnDefinitions[sidebarColumnIndex].ActualWidth;
            if (vm != null && vm.SidebarVisible && width > 0)
                vm.SidebarWidth = width;
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
        if (cheatSheet != null)
        {
            cheatSheet.Activate();
            return;
        }

        cheatSheet = new CheatSheetWindow(new CheatSheetViewModel(cheatSheetRows()));
        cheatSheet.Closed += (_, _) => cheatSheet = null;
        if (IsVisible)
            cheatSheet.Show(this);
        else
            cheatSheet.Show();
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
        if (ShutdownRequested == null) { vm?.SavePrefs(); return; }
        e.Cancel = true;
        if (!shutdownStarted) { shutdownStarted = true; IsEnabled = false; _ = DrainBeforeClosingAsync(); }
    }

    async Task DrainBeforeClosingAsync()
    {
        try { await ShutdownRequested!(); }
        finally { shutdownComplete = true; Close(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        idleTimer.Stop();
        settingsWindow?.Close();
        cheatSheet?.Close();
        translationWindow?.Close();
        recognitionWindow?.Close();
        ReleaseSleepInhibition();
        Video.Renderer = null;
        base.OnClosed(e);
    }

    void ApplyFullScreenLayout(bool fullScreen)
    {
        Grid.SetRowSpan(VideoHost, fullScreen ? 2 : 1);
        Grid.SetRow(TransportBar, fullScreen ? 0 : 1);
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
            case nameof(MainWindowViewModel.ControlsVisible):
            case nameof(MainWindowViewModel.SubtitleWidthPercent):
            case nameof(MainWindowViewModel.SubtitleMaxWidth):
            case nameof(MainWindowViewModel.SubtitlePrimarySecondaryDistance):
            case nameof(MainWindowViewModel.SubtitlePositionAlignment):
            case nameof(MainWindowViewModel.SubtitlePositionAlignmentWhenDual):
            case nameof(MainWindowViewModel.SecondaryVisible):
            case nameof(MainWindowViewModel.EffectiveSubtitleVerticalAlignment):
                UpdateSubtitleLayout();
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

    void OnWordPopupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WordPopupViewModel.IsOpen) or nameof(WordPopupViewModel.AnchorX) or nameof(WordPopupViewModel.AnchorY))
            Dispatcher.UIThread.Post(PositionWordPopup, DispatcherPriority.Loaded);
    }

    void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubtitlesSidebarViewModel.CurrentCue) && vm?.Sidebar.CurrentCue is { } cue && SidebarVisibleAndIdle())
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
        if (vm == null || sender is not Visual v)
            return;

        Point p = v.TranslatePoint(e.Position, VideoHost) ?? e.Position;
        vm.OnWordClicked(e.Word, e.Text, e.Slot, p.X, p.Y, e.IsTranslated, e.SourceLanguage, e.TargetLanguage);
    }

    void OnVideoTapped(object? sender, TappedEventArgs e)
    {
        // A click on the video (not on a subtitle word, which opened the popup, nor inside the popup) closes it.
        if (vm?.WordPopup.IsOpen != true || e.Source is not Visual src)
            return;
        if (src.FindAncestorOfType<SubtitleText>(true) != null || src == WordPopupCard || WordPopupCard.IsVisualAncestorOf(src))
            return;
        vm.WordPopup.Close();
    }

    void OnVideoDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual src && (src.FindAncestorOfType<Button>(true) != null || src.FindAncestorOfType<SubtitleText>(true) != null
                                       || WordPopupCard.IsPointerOver))
            return;
        vm?.ToggleFullScreen();
    }

    void OnVideoWheel(object? sender, PointerWheelEventArgs e)
    {
        if (vm == null || e.Delta.Y == 0)
            return;
        vm.Volume += e.Delta.Y > 0 ? 5 : -5;
        e.Handled = true;
    }

    void OnCueTapped(object? sender, TappedEventArgs e)
    {
        if (vm != null && e.Source is Control c && c.DataContext is CueItemViewModel cue)
            vm.Sidebar.SeekCommand.Execute(cue);
    }

    void OnAnyPointerMoved(object? sender, PointerEventArgs e)
    {
        keys.Update(e.KeyModifiers);
        vm?.NotifyActivity();
    }

    /// <summary>
    /// True when the focused control consumes the keyboard itself: text boxes (typing) and menus / combo boxes (arrow
    /// navigation). Those keys are not routed to the app shortcuts or the engine's bindings.
    /// </summary>
    bool IsTextInputFocused() => FocusManager?.GetFocusedElement() is TextBox or MenuItem or ComboBox or ComboBoxItem;

    void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        keys.Update(e.KeyModifiers);
        if (vm == null)
            return;

        bool textFocused = IsTextInputFocused();
        if (textFocused && e.Key == Key.Escape && FocusManager?.GetFocusedElement() == FilterBox)
        {
            // WPF sidebar: Esc clears the search and returns focus to the video.
            vm.Sidebar.FilterText = "";
            Focus();
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
        if (vm != null && vm.HandleKeyUp(KeyMapper.ToWpf(e.Key), IsTextInputFocused()))
            e.Handled = true;
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

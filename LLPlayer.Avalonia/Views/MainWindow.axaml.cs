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
    readonly KeyMapper keys;
    readonly AvaloniaHostServices? hostServices;
    readonly DispatcherTimer idleTimer;
    MainWindowViewModel? vm;
    Func<IReadOnlyList<ShortcutInfo>> cheatSheetRows = () => AppShortcutMap.BuildCheatSheet([]);
    WindowState stateBeforeFullScreen = WindowState.Normal;
    Task<IDisposable>? sleepInhibition;
    CheatSheetWindow? cheatSheet;

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
        CueList.Tapped += OnCueTapped;
        SidebarSplitter.DragCompleted += (_, _) =>
        {
            double width = ContentGrid.ColumnDefinitions[2].ActualWidth;
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
        UpdateSubtitleMargin();
        UpdateSidebarColumn();
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
        vm?.SavePrefs();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        idleTimer.Stop();
        cheatSheet?.Close();
        ReleaseSleepInhibition();
        Video.Renderer = null;
        base.OnClosed(e);
    }

    void ApplyFullScreenLayout(bool fullScreen)
    {
        Grid.SetRowSpan(VideoHost, fullScreen ? 2 : 1);
        Grid.SetRow(TransportBar, fullScreen ? 0 : 1);
        TransportBar.Classes.Set("overlay", fullScreen);
        UpdateSubtitleMargin();
    }

    /// <summary>The sidebar column gets the remembered width (the splitter resizes it); hidden = collapsed.</summary>
    void UpdateSidebarColumn()
    {
        if (vm == null)
            return;

        ColumnDefinition column = ContentGrid.ColumnDefinitions[2];
        if (vm.SidebarVisible)
        {
            column.MinWidth = 220;
            column.Width = new GridLength(vm.SidebarWidth, GridUnitType.Pixel);
        }
        else
        {
            column.MinWidth = 0;
            column.Width = GridLength.Auto;
        }
    }

    void UpdateSubtitleMargin()
    {
        if (vm == null)
            return;

        double extra = IsFullScreen && vm.ControlsVisible ? TransportBar.Bounds.Height : 0;
        SubtitleOverlay.Margin = new Thickness(48, 0, 48, vm.SubtitleBottomOffset + extra);
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SubtitleBottomOffset):
            case nameof(MainWindowViewModel.ControlsVisible):
                UpdateSubtitleMargin();
                break;
            case nameof(MainWindowViewModel.CursorHidden):
                VideoHost.Cursor = vm!.CursorHidden ? new Cursor(StandardCursorType.None) : Cursor.Default;
                break;
            case nameof(MainWindowViewModel.IsPlaying):
                UpdateSleepInhibition(vm!.IsPlaying);
                break;
            case nameof(MainWindowViewModel.SidebarVisible):
                UpdateSidebarColumn();
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

        double left = Math.Clamp(vm.WordPopup.AnchorX - w / 2, 8, Math.Max(8, areaW - w - 8));
        double top = vm.WordPopup.AnchorY - h - 16;
        if (top < 8)
            top = Math.Min(vm.WordPopup.AnchorY + 24, Math.Max(8, areaH - h - 8));

        Canvas.SetLeft(WordPopupCard, left);
        Canvas.SetTop(WordPopupCard, top);
    }

    void OnWordClicked(object? sender, WordClickedEventArgs e)
    {
        if (vm == null || sender is not Visual v)
            return;

        Point p = v.TranslatePoint(e.Position, VideoHost) ?? e.Position;
        vm.OnWordClicked(e.Word, e.Text, e.Slot, p.X, p.Y);
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

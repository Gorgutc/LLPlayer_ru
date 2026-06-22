using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Extensions;
using LLPlayer.Services;
using MaterialDesignThemes.Wpf;

namespace LLPlayer.Controls;

public partial class WordPopup : UserControl, INotifyPropertyChanged
{
    public FlyleafManager FL { get; }
    private ITranslateService? _translateService;
    // One-shot guard: the actionable word-translation config-error snackbar is enqueued at most once
    // until the user changes translation settings (reset in the settings-change handlers, NOT in Clear()).
    private bool _wordTranslateConfigErrorNotified;
    private readonly TranslateServiceFactory _translateServiceFactory;
    private PDICSender? _pdicSender;
    private readonly Lock _locker = new();

    private string _clickedWords = string.Empty;
    private string _clickedText = string.Empty;

    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, string> _translateCache = new();
    private string? _lastSearchActionUrl;

    public WordPopup()
    {
        InitializeComponent();

        FL = ((App)Application.Current).Container.Resolve<FlyleafManager>();

        _translateServiceFactory = new TranslateServiceFactory(FL.PlayerConfig.Subtitles);

        // Subscribe via weak events: Player/Config/SubtitlesManager are app-lifetime singletons, and a
        // direct += would root this control for the whole process. Weak handlers fire identically to +=
        // (empty propertyName = all PropertyChanged) but let the popup be collected once its host is gone.
        PropertyChangedEventManager.AddHandler(FL.Player, Player_OnPropertyChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(FL.PlayerConfig.Subtitles, SubtitlesOnPropertyChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(FL.PlayerConfig.Subtitles.TranslateChatConfig, ChatConfigOnPropertyChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(FL.Player.SubtitlesManager[0], SubManagerOnPropertyChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(FL.Player.SubtitlesManager[1], SubManagerOnPropertyChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(FL.Config.Subs, SubsOnPropertyChanged, string.Empty);

        InitializeContextMenu();
    }

    public bool IsLoading { get; set => Set(ref field, value); }

    public bool IsOpen { get; set => Set(ref field, value); }

    public UIElement? PopupPlacementTarget { get; set => Set(ref field, value); }

    public double PopupHorizontalOffset { get; set => Set(ref field, value); }

    public double PopupVerticalOffset { get; set => Set(ref field, value); }

    public ContextMenu? PopupContextMenu { get; set => Set(ref field, value); }
    public ContextMenu? WordContextMenu { get; set => Set(ref field, value); }

    // INPC-backed so the XAML theme triggers (Border background / translation foreground) react to it.
    public bool IsSidebar { get; set => Set(ref field, value); }

    private void SubtitlesOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Config.SubtitlesConfig.TranslateWordServiceType):
            case nameof(Config.SubtitlesConfig.TranslateTargetLanguage):
            case nameof(Config.SubtitlesConfig.LanguageFallbackPrimary):
                // Apply translating settings changes
                _wordTranslateConfigErrorNotified = false;
                Clear();
                break;
        }
    }

    private void ChatConfigOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Apply translating settings changes
        _wordTranslateConfigErrorNotified = false;
        Clear();
    }

    private void SubManagerOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubManager.Language))
        {
            // Apply source language changes
            _wordTranslateConfigErrorNotified = false;
            Clear();
        }
    }

    private void SubsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppConfigSubs.WordMenuActions))
        {
            // Apply word action settings changes
            InitializeContextMenu();
        }
    }

    private void Clear()
    {
        _translateService?.Dispose();
        _translateService = null;
        // clear cache
        _translateCache.Clear();
    }

    private void InitializeContextMenu()
    {
        var contextMenuStyle = (Style)FindResource("FlyleafContextMenu");

        ContextMenu popupMenu = new()
        {
            Placement = PlacementMode.Mouse,
            Style = contextMenuStyle
        };

        SetupContextMenu(popupMenu);
        PopupContextMenu = popupMenu;

        ContextMenu wordMenu = new()
        {
            Placement = PlacementMode.Mouse,
            Style = contextMenuStyle
        };

        SetupContextMenu(wordMenu);
        WordContextMenu = wordMenu;
    }

    private void SetupContextMenu(ContextMenu contextMenu)
    {
        IEnumerable<IMenuAction> actions = FL.Config.Subs.WordMenuActions.Where(a => a.IsEnabled);
        foreach (IMenuAction action in actions)
        {
            MenuItem menuItem = new() { Header = action.Title };

            // Initialize default action at the top
            // TODO: L: want to make the default action bold.
            if (_lastSearchActionUrl == null && action is SearchMenuAction sa)
            {
                // Only word search available
                if (sa.Url.Contains("%w") || sa.Url.Contains("%lw"))
                {
                    _lastSearchActionUrl = sa.Url;
                }
            }

            menuItem.Click += (o, args) =>
            {
                if (action is SearchMenuAction searchAction)
                {
                    // Only word search available
                    if (searchAction.Url.Contains("%w") || searchAction.Url.Contains("%lw"))
                    {
                        _lastSearchActionUrl = searchAction.Url;
                    }

                    OpenWeb(searchAction.Url, _clickedWords, _clickedText);
                }
                else if (action is ClipboardMenuAction clipboardAction)
                {
                    CopyToClipboard(_clickedWords, clipboardAction.ToLower);
                }
                else if (action is ClipboardAllMenuAction clipboardAllAction)
                {
                    CopyToClipboard(_clickedText, clipboardAllAction.ToLower);
                }
            };
            contextMenu.Items.Add(menuItem);
        }
    }

    // Click on video screen to close pop-up
    private void Player_OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FL.Player.Status) && FL.Player.Status == Status.Playing)
        {
            IsOpen = false;
        }
    }

    private async ValueTask<string> TranslateWithCache(string text, WordClickedEventArgs e, CancellationToken token)
    {
        // already translated by translator
        if (e.IsTranslated)
        {
            return text;
        }

        var srcLang = FL.Player.SubtitlesManager[e.SubIndex].Language;
        var targetLang = FL.PlayerConfig.Subtitles.TranslateTargetLanguage;

        // Not set language or same language
        if (srcLang == null || srcLang.ISO6391 == targetLang.ToISO6391())
        {
            return text;
        }

        string lower = text.ToLower();
        if (_translateCache.TryGetValue(lower, out var cache))
        {
            return cache;
        }

        if (_translateService == null)
        {
            try
            {
                var service = _translateServiceFactory.GetService(FL.PlayerConfig.Subtitles.TranslateWordServiceType, true);
                service.Initialize(srcLang, targetLang);
                _translateService = service;
            }
            catch (TranslationConfigException ex)
            {
                Clear();

                // Recoverable word-translation config error → actionable snackbar with a deep-link to the
                // translation settings, instead of a topmost modal. Enqueue once until settings change:
                // repeated word clicks keep re-entering this block (_translateService stays null), so an
                // unguarded Enqueue would stack duplicate snackbars.
                if (!_wordTranslateConfigErrorNotified)
                {
                    _wordTranslateConfigErrorNotified = true;
                    FL.MessageQueue.Enqueue(ex.Message, "OPEN SETTINGS",
                        () => FL.Action.OpenSettingsAt(KnownErrorSettingsTab.Translation));
                }

                return text;
            }
        }

        try
        {
            string result = await _translateService.TranslateAsync(text, token);
            _translateCache.TryAdd(lower, result);

            return result;
        }
        catch (TranslationException ex)
        {
            ErrorDialogHelper.ShowUnknownErrorPopup(ex.Message, UnknownErrorType.Translation, ex);

            return text;
        }
    }

    public async Task OnWordClicked(WordClickedEventArgs e)
    {
        _clickedWords = e.Words;
        _clickedText = e.Text;

        if (FL.Player.Status == Status.Playing)
        {
            FL.Player.Pause();
        }

        if (e.Mouse is MouseClick.Left or MouseClick.Middle)
        {
            switch (FL.Config.Subs.WordClickActionMethod)
            {
                case WordClickAction.Clipboard:
                    CopyToClipboard(e.Words);
                    break;

                case WordClickAction.ClipboardAll:
                    CopyToClipboard(e.Text);
                    break;

                default:
                    await Popup(e);
                    break;
            }
        }
        else if (e.Mouse == MouseClick.Right)
        {
            WordContextMenu!.IsOpen = true;
        }
    }

    private async Task Popup(WordClickedEventArgs e)
    {
        if (FL.Config.Subs.WordCopyOnSelected)
        {
            CopyToClipboard(e.Words);
        }

        if (FL.Config.Subs.WordClickActionMethod == WordClickAction.PDIC && e.IsWord)
        {
            if (_pdicSender == null)
            {
                // Initialize PDIC lazily
                lock (_locker)
                {
                    _pdicSender ??= ((App)Application.Current).Container.Resolve<PDICSender>();
                }
            }

            _ = _pdicSender.SendWithPipe(e.Text, e.WordOffset + 1);
            return;
        }

        if (_cts != null)
        {
            // Canceled if running ahead
            _cts.Cancel();
            _cts.Dispose();
        }

        _cts = new CancellationTokenSource();
        CancellationTokenSource cts = _cts;

        string source = e.Words;

        IsLoading = true;

        SourceText.Text = source;
        TranslationText.Text = "";

        if (IsSidebar && e.Sender is SelectableTextBox)
        {
            var listBoxItem = UIHelper.FindParent<ListBoxItem>(e.Sender);
            if (listBoxItem != null)
            {
                PopupPlacementTarget = listBoxItem;
            }
        }

        if (FL.Config.Subs.WordLastSearchOnSelected)
        {
            if (Keyboard.Modifiers == FL.Config.Subs.WordLastSearchOnSelectedModifier)
            {
                if (_lastSearchActionUrl != null)
                {
                    OpenWeb(_lastSearchActionUrl, source);
                }
            }
        }

        IsOpen = true;

        await UpdatePosition();

        try
        {
            string result = await TranslateWithCache(source, e, cts.Token);
            TranslationText.Text = result;
            IsLoading = false;
        }
        catch (OperationCanceledException)
        {
            // Only clear the spinner if this is still the active operation; a rapid re-trigger may have
            // started a new translation whose spinner must stay on.
            if (cts == _cts)
                IsLoading = false;
            return;
        }

        await UpdatePosition();

        return;

        async Task UpdatePosition()
        {
            // ActualWidth is updated asynchronously, so it needs to be offloaded in the Dispatcher.
            await Dispatcher.BeginInvoke(() =>
            {
                if (IsSidebar && PopupPlacementTarget != null)
                {
                    // for sidebar
                    PopupVerticalOffset = (((ListBoxItem)PopupPlacementTarget).ActualHeight - ActualHeight) / 2;
                    PopupHorizontalOffset = -ActualWidth - 10;
                }
                else
                {
                    // for subtitle
                    PopupHorizontalOffset = e.WordsX + ((e.WordsWidth - ActualWidth) / 2);
                    PopupVerticalOffset = -ActualHeight;
                }

            }, DispatcherPriority.Background);
        }
    }

    private void CloseButton_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        IsOpen = false;
    }

    // Keyboard activation (Space/Enter) of the close button; the mouse path uses the preview handler above.
    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        IsOpen = false;
    }

    // Esc dismisses the popup when keyboard focus is inside it (e.g. while selecting the translated text).
    private void Border_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsOpen = false;
            e.Handled = true;
        }
    }

    private void SourceText_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_lastSearchActionUrl != null)
        {
            OpenWeb(_lastSearchActionUrl, SourceText.Text);
        }
    }

    private static void CopyToClipboard(string text, bool toLower = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (toLower)
        {
            text = text.ToLower();
        }

        // copy word
        try
        {
            // slow (10ms)
            //Clipboard.SetText(text);

            WindowsClipboard.SetText(text);
        }
        catch
        {
            // ignored
        }
    }

    private static void OpenWeb(string url, string words, string sentence = "")
    {
        if (url.Contains("%lw"))
        {
            url = url.Replace("%lw", Uri.EscapeDataString(words.ToLower()));
        }

        if (url.Contains("%w"))
        {
            url = url.Replace("%w", Uri.EscapeDataString(words));
        }

        if (url.Contains("%s"))
        {
            url = url.Replace("%s", Uri.EscapeDataString(sentence));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    #endregion
}

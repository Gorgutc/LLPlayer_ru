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
using FlyleafLib.MediaPlayer.AI;
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
    private int _clickedSubIndex;
    private bool _clickedIsTranslated;
    private string _wordMenuWords = string.Empty;
    private string _wordMenuText = string.Empty;
    private readonly WordListStore _wordListStore;

    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, string> _translateCache = new();
    private string? _lastSearchActionUrl;

    // F-11: dictionary-definition lookup (separate from translation). Built lazily, reset in Clear() on a
    // settings/language change. _lastDefinition stashes the entry for the CURRENT word so Save can auto-fill the
    // F-10 Reading/Definition; it is cleared at the top of every Popup() so a stale entry never attaches to a
    // new Save. _definitionCache mirrors _translateCache (keyed by lower(word), caches not-found as Empty).
    private WordDefinitionService? _wordDefinitionService;
    private readonly Dictionary<string, DictionaryEntry> _definitionCache = new();
    private DictionaryEntry? _lastDefinition;
    // The in-flight definition lookup for the current word (null when none). Save waits on it so a click in the
    // gap between the translation and the definition does not persist a word with blank Reading/Definition that
    // the F-10 term-dedup (first-wins) would then refuse to update.
    private Task<DictionaryEntry>? _pendingDefinitionTask;

    public WordPopup()
    {
        InitializeComponent();

        FL = ((App)Application.Current).Container.Resolve<FlyleafManager>();
        _wordListStore = ((App)Application.Current).Container.Resolve<WordListStore>();

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

    // F-11: drives the visibility of the (third) definition row; false keeps the popup byte-identical to before.
    public bool DefinitionVisible { get; set => Set(ref field, value); }

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
            case nameof(Config.SubtitlesConfig.WordDefinitionServiceType):
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
        // Clear() disposes the translation/definition services and resets WPF UI (DefinitionText/DefinitionVisible),
        // but it can be reached off the UI thread: SubManager raises PropertyChanged(Language) on the BACKGROUND
        // subtitle-load thread — SubManager.Open sets LanguageSource on the first cue inside SubtitleReader.ReadAll,
        // which runs on a ThreadPool worker (Subtitle.Load). A direct DependencyObject touch there throws
        // "The calling thread cannot access this object because a different thread owns it" and aborts the whole
        // subtitle load (so the subtitles never appear). Marshal to the Dispatcher; UI-thread callers (settings /
        // chat-config changes) still run synchronously as before, while off-thread
        // callers get an asynchronous reset (a future off-thread caller must not rely on Clear() finishing inline).
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(Clear));
            return;
        }

        CancelCurrentLookup();
        DisposeLookupServices();

        // clear cache
        _translateCache.Clear();

        // F-11: re-resolve the definition provider after a settings/language change (a new LLM may now be
        // configured, the target language may differ) and drop cached lookups for the previous configuration.
        _definitionCache.Clear();
        ResetPopupUi();
    }

    /// <summary>
    /// Releases resources owned by this popup when its enclosing sidebar/overlay leaves the presentation tree.
    /// This is intentionally reload-safe: WPF can unload and later reload the same parent instance. Ordinary
    /// close/Esc/playback dismissal never calls this method, so per-instance lookup caches survive normal use.
    /// </summary>
    public void ReleaseOwnedResources()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ReleaseOwnedResources));
            return;
        }

        CancelCurrentLookup();
        DisposeLookupServices();
        ResetPopupUi();

        // PDICSender is an application-owned singleton and is disposed only by App.OnExit.
    }

    private void ResetPopupUi()
    {
        IsOpen = false;
        if (PopupContextMenu != null)
            PopupContextMenu.IsOpen = false;
        if (WordContextMenu != null)
            WordContextMenu.IsOpen = false;

        IsLoading = false;
        _lastDefinition = null;
        PopupPlacementTarget = null;
        SourceText.Text = "";
        TranslationText.Text = "";
        DefinitionText.Text = "";
        DefinitionVisible = false;

        // Invalidate Save/menu snapshots together with the rendered result. A provider/language change must not
        // let an old translation be stored under the new target language (the word list is first-wins).
        _clickedWords = string.Empty;
        _clickedText = string.Empty;
        _clickedSubIndex = 0;
        _clickedIsTranslated = false;
        _wordMenuWords = string.Empty;
        _wordMenuText = string.Empty;
    }

    private void CancelCurrentLookup()
    {
        CancellationTokenSource? cts = _cts;
        _cts = null;
        _pendingDefinitionTask = null;
        cts?.Cancel();
        // The async Popup operation owns and disposes its CTS after every task using the token has settled.
    }

    private void DisposeLookupServices()
    {
        ITranslateService? translateService = _translateService;
        _translateService = null;
        translateService?.Dispose();

        WordDefinitionService? definitionService = _wordDefinitionService;
        _wordDefinitionService = null;
        definitionService?.Dispose();
    }

    private void InitializeContextMenu()
    {
        var contextMenuStyle = (Style)FindResource("FlyleafContextMenu");

        ContextMenu popupMenu = new()
        {
            Placement = PlacementMode.Mouse,
            Style = contextMenuStyle
        };

        SetupContextMenu(popupMenu, isPopupMenu: true);
        PopupContextMenu = popupMenu;

        ContextMenu wordMenu = new()
        {
            Placement = PlacementMode.Mouse,
            Style = contextMenuStyle
        };

        SetupContextMenu(wordMenu, isPopupMenu: false);
        WordContextMenu = wordMenu;
    }

    private void SetupContextMenu(ContextMenu contextMenu, bool isPopupMenu)
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
                string words = isPopupMenu ? _clickedWords : _wordMenuWords;
                string text = isPopupMenu ? _clickedText : _wordMenuText;

                if (action is SearchMenuAction searchAction)
                {
                    // Only word search available
                    if (searchAction.Url.Contains("%w") || searchAction.Url.Contains("%lw"))
                    {
                        _lastSearchActionUrl = searchAction.Url;
                    }

                    OpenWeb(searchAction.Url, words, text);
                }
                else if (action is ClipboardMenuAction clipboardAction)
                {
                    CopyToClipboard(words, clipboardAction.ToLower);
                }
                else if (action is ClipboardAllMenuAction clipboardAllAction)
                {
                    CopyToClipboard(text, clipboardAllAction.ToLower);
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
                ITranslateService service =
                    _translateServiceFactory.GetService(FL.PlayerConfig.Subtitles.TranslateWordServiceType, true);
                try
                {
                    service.Initialize(srcLang, targetLang);
                }
                catch
                {
                    service.Dispose();
                    throw;
                }

                _translateService = service;
            }
            catch (TranslationConfigException ex)
            {
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

        ITranslateService translateService = _translateService;
        try
        {
            string result = await translateService.TranslateAsync(text, token);
            token.ThrowIfCancellationRequested();
            _translateCache.TryAdd(lower, result);

            return result;
        }
        catch (TranslationException ex)
        {
            token.ThrowIfCancellationRequested();

            // A looping/degenerate or token-truncated reply is a recoverable per-word failure on this
            // high-frequency interactive path; fall back to the source word silently rather than popping a
            // modal. Keep the modal for genuine (config/transport/empty-response) failures.
            if (ex.Kind is not (TranslationFailureKind.Degenerate or TranslationFailureKind.Truncated))
            {
                ErrorDialogHelper.ShowUnknownErrorPopup(ex.Message, UnknownErrorType.Translation, ex);
            }

            return text;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
    }

    public async Task OnWordClicked(WordClickedEventArgs e)
    {
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
            // Context-menu actions have their own state. They must not overwrite the word/translation pair
            // currently shown by an in-flight popup, otherwise Save could combine two different clicks.
            _wordMenuWords = e.Words;
            _wordMenuText = e.Text;
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

        // A rapid re-click invalidates the old operation. The old Popup() keeps ownership of its CTS and
        // disposes it only after its own translation/definition tasks have settled.
        CancelCurrentLookup();
        CancellationTokenSource cts = new();
        _cts = cts;
        Task<DictionaryEntry>? definitionTask = null;
        bool definitionTaskObserved = false;

        try
        {
            string source = e.Words;

            // Commit Save/context-menu state only for a real popup lookup. Clipboard, PDIC and right-click
            // actions must not replace the state belonging to an older lookup that is still visible.
            _clickedWords = e.Words;
            _clickedText = e.Text;
            _clickedSubIndex = e.SubIndex;
            _clickedIsTranslated = e.IsTranslated;
            IsLoading = true;

            SourceText.Text = source;
            TranslationText.Text = "";
            // F-11: reset the definition row + stash so a previous word's definition can never linger or be saved.
            DefinitionText.Text = "";
            DefinitionVisible = false;
            _lastDefinition = null;
            _pendingDefinitionTask = null;

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
            if (!IsCurrentLookup(cts))
                return;

            // F-11: start the (best-effort) definition lookup in parallel with the translation under the same
            // token, so a re-click cancels both. null when the feature is off / not applicable for this word.
            definitionTask = StartDefinitionLookup(source, e, cts.Token);
            _pendingDefinitionTask = definitionTask;

            string result = await TranslateWithCache(source, e, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!IsCurrentLookup(cts))
                return;

            TranslationText.Text = result;
            IsLoading = false;

            // The translation is the primary signal and is shown first; the definition fills in after it lands.
            if (definitionTask != null)
            {
                definitionTaskObserved = true;
                DictionaryEntry entry = await definitionTask;
                cts.Token.ThrowIfCancellationRequested();
                // Gate on the RENDERED text (not just IsEmpty) so a sense-with-blank-definition never shows an
                // empty row; drop a late result for a superseded word via the cts==_cts guard.
                string display = entry.IsEmpty ? string.Empty : entry.FlattenForDisplay();
                if (IsCurrentLookup(cts) && display.Length > 0)
                {
                    _lastDefinition = entry;
                    DefinitionText.Text = display;
                    DefinitionVisible = true;
                }
            }

            if (IsCurrentLookup(cts))
                await UpdatePosition();
        }
        catch (OperationCanceledException)
        {
            // Only clear the spinner if this is still the active operation; a rapid re-trigger may have
            // started a new translation whose spinner must stay on.
            if (ReferenceEquals(cts, _cts))
                IsLoading = false;
        }

        catch (Exception) when (cts.IsCancellationRequested || !ReferenceEquals(cts, _cts))
        {
            // A provider can surface ObjectDisposedException/transport errors while host teardown cancels and
            // disposes its service. Once this operation is no longer current, it must not update UI or notify.
        }
        finally
        {
            if (!definitionTaskObserved && definitionTask != null)
            {
                try
                {
                    await definitionTask;
                }
                catch
                {
                    // The abandoned best-effort definition task is observed here; its result is intentionally
                    // ignored because this request was cancelled or superseded.
                }
            }

            if (ReferenceEquals(_pendingDefinitionTask, definitionTask))
                _pendingDefinitionTask = null;

            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
                IsLoading = false;
            }

            cts.Dispose();
        }

        async Task UpdatePosition()
        {
            // ActualWidth is updated asynchronously, so it needs to be offloaded in the Dispatcher.
            await Dispatcher.BeginInvoke(() =>
            {
                if (!IsCurrentLookup(cts))
                    return;

                if (IsSidebar && PopupPlacementTarget is ListBoxItem listBoxItem)
                {
                    // for sidebar
                    PopupVerticalOffset = (listBoxItem.ActualHeight - ActualHeight) / 2;
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

    // F-11: kick off a best-effort dictionary-definition lookup for the clicked word. Returns null when the
    // feature is off or not applicable (so the caller skips it). Never throws synchronously — definition is a
    // bonus, so any setup problem degrades to "no definition" (the whole body after the trivial early returns is
    // wrapped in try). NOTE: a monolingual definition (source == target, e.g. an English word defined in English,
    // or a Russian word defined in Russian) IS useful, so — unlike translation — there is no same-language skip.
    private Task<DictionaryEntry>? StartDefinitionLookup(string source, WordClickedEventArgs e, CancellationToken token)
    {
        WordDefinitionServiceType mode = FL.PlayerConfig.Subtitles.WordDefinitionServiceType;
        if (mode == WordDefinitionServiceType.Off)
        {
            return null;
        }

        // A clicked already-translated word has no source-language dictionary entry (mirrors TranslateWithCache).
        if (e.IsTranslated)
        {
            return null;
        }

        try
        {
            // NOTE: the bare name "Language" resolves to System.Windows.Markup.XmlLanguage in this WPF file, so
            // use var (the property is FlyleafLib.Language) and fully-qualify the static.
            var srcLang = FL.Player.SubtitlesManager[e.SubIndex].Language;
            if (srcLang == null || srcLang == FlyleafLib.Language.Unknown)
            {
                return null;
            }

            string key = source.ToLower();
            if (_definitionCache.TryGetValue(key, out DictionaryEntry? cached))
            {
                return Task.FromResult(cached!);
            }

            WordDefinitionService definitionService =
                _wordDefinitionService ??= WordDefinitionService.ForConfig(FL.PlayerConfig.Subtitles);

            WordDefinitionProvider provider =
                WordDefinitionSelector.Select(mode, srcLang.ISO6391, definitionService.LlmAvailable);
            if (provider == WordDefinitionProvider.None)
            {
                return null;
            }

            // Auto on an English word: fall back to the LLM when the free dictionary has no entry (pure rule).
            bool allowLlmFallback =
                WordDefinitionSelector.AllowLlmFallback(mode, provider, definitionService.LlmAvailable);

            string? srcName = srcLang.TopEnglishName;
            string targetName = FL.PlayerConfig.Subtitles.TranslateLanguage.TopEnglishName;

            return LookupAndCacheAsync(
                definitionService, provider, allowLlmFallback, source, srcName, targetName, e.Text, key, token);
        }
        catch
        {
            // Definition is best-effort: any setup problem degrades to no definition.
            return null;
        }
    }

    private async Task<DictionaryEntry> LookupAndCacheAsync(
        WordDefinitionService definitionService, WordDefinitionProvider provider, bool allowLlmFallback, string word,
        string? sourceLangName, string targetLangName, string context, string cacheKey, CancellationToken token)
    {
        DictionaryEntry entry = await definitionService.GetDefinitionAsync(
            provider, allowLlmFallback, word, sourceLangName, targetLangName, context, token);

        // Cache the outcome (including Empty) so repeat clicks on the same word don't re-hit the network.
        token.ThrowIfCancellationRequested();
        _definitionCache[cacheKey] = entry;
        return entry;
    }

    private bool IsCurrentLookup(CancellationTokenSource cts) =>
        ReferenceEquals(_cts, cts) && !cts.IsCancellationRequested;

    private void CloseButton_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        IsOpen = false;
    }

    // Keyboard activation (Space/Enter) of the close button; the mouse path uses the preview handler above.
    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        IsOpen = false;
    }

    // F-10: save the looked-up word to the global word list (Subtitles > Word Manager). Uses the translation
    // already shown in the popup; honors IsTranslated (a clicked translated word is stored as-is in the target
    // language). F-11: Reading/Definition are auto-filled from the fetched dictionary entry when available, and
    // otherwise left blank for the user to fill in the Word Manager later.
    private void SaveWordButton_OnClick(object sender, RoutedEventArgs e)
    {
        string term = (_clickedWords ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            return;
        }

        // Don't save mid-translation: the translation text is still empty until the lookup completes. Tell the
        // user to wait rather than silently saving a word with a blank translation.
        if (IsLoading)
        {
            FL.MessageQueue.Enqueue("Translation in progress — try Save again in a moment.");
            return;
        }

        // F-11: don't save while the definition lookup is still in flight — the dictionary Reading/Definition
        // would be saved blank, and the F-10 term-dedup (first-wins) would refuse to update the row from a later
        // auto-fill. Ask the user to retry once the definition has landed (mirrors the translation guard).
        if (_pendingDefinitionTask is { IsCompleted: false })
        {
            FL.MessageQueue.Enqueue("Looking up the definition — try Save again in a moment.");
            return;
        }

        string targetLang = FL.PlayerConfig.Subtitles.TranslateTargetLanguage.ToISO6391();
        string translation;
        string sourceLanguage;
        // F-11: auto-fill Reading/Definition from the fetched dictionary entry (empty when the feature is off,
        // the lookup found nothing, or the clicked word was already translated — preserving today's behavior).
        string reading = string.Empty;
        string definition = string.Empty;
        if (_clickedIsTranslated)
        {
            // The clicked word is already translated text — keep it as the term in the target language.
            translation = string.Empty;
            sourceLanguage = targetLang;
        }
        else
        {
            translation = TranslationText.Text ?? string.Empty;
            sourceLanguage = FL.Player.SubtitlesManager[_clickedSubIndex].Language?.ISO6391 ?? string.Empty;

            if (_lastDefinition is { IsEmpty: false } entry)
            {
                (reading, definition) = entry.ToFields();
            }
        }

        SavedWord word = new(
            term, reading, translation, definition, _clickedText ?? string.Empty,
            sourceLanguage, targetLang, WordSource.WordClick, DateTime.UtcNow.ToString("O"));

        bool added = _wordListStore.TryAdd(word);
        if (added)
        {
            _wordListStore.Save();
        }
        FL.MessageQueue.Enqueue(added
            ? $"Saved \"{term}\" to the word list"
            : $"\"{term}\" is already in the word list");
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

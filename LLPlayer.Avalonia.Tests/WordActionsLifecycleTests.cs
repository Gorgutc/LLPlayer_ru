using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class WordActionsLifecycleTests
{
    static WordSelectionSnapshot Selection(string word)
        => new(word, "Cue " + word, 0, true, false, "en", "de", WordSelectionOrigin.Overlay, MouseButton.Left, KeyModifiers.Control);

    [AvaloniaFact]
    public async Task Modal_child_veto_preserves_owner_until_retry_then_word_shutdown_retires_pending_browser()
    {
        TaskCompletionSource<bool> browser = new();
        FakeShell shell = new() { BrowserHandler = _ => browser.Task };
        FakePlaybackController player = new();
        using MainWindowViewModel main = new(player, new AppPrefs { WordCopyOnSelected = true, WordLastSearchOnSelected = true, SubsAutoTextCopy = true }.Normalize(), _ => { }, new FakeTranslator(), shell);
        PendingDiagnostics diagnostics = new();
        TranslationViewModel translation = new(new() { WordProvider = "OpenAILike" }, _ => Task.CompletedTask, diagnostics: diagnostics);
        MainWindow window = new(new KeyMapper(), null) { Width = 640, Height = 420 };
        int shutdownCalls = 0, parentClosingCalls = 0;
        window.Closing += (_, _) => parentClosingCalls++;
        window.ShutdownRequested = () => { shutdownCalls++; main.Dispose(); return Task.CompletedTask; };
        window.Attach(main, null); window.Show(); window.ShowTranslation(translation);
        Task diagnostic = translation.CheckProviderCommand.ExecuteAsync(null);
        WordMenuInvocation search = main.CreateWordMenu(Selection("Before close")).First(item => item.Action.Kind == WordMenuActionKind.Search);
        Task pending = main.InvokeWordMenuAsync(search); main.TrackBrowserOperation(pending);
        try
        {
            window.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.True(diagnostics.Token.IsCancellationRequested);
            Assert.Equal(0, shutdownCalls); Assert.True(window.IsVisible);
            // Avalonia 12.1.3 asks owned children first: an active diagnostic vetoes before parent.OnClosing.
            Assert.Equal(0, parentClosingCalls);
            diagnostics.Result.TrySetResult(new(TranslationDiagnosticOutcome.Success));
            await diagnostic.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            window.Close();
            await main.DrainBrowserOperationsAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.True(parentClosingCalls > 0); Assert.Equal(1, shutdownCalls); Assert.False(window.IsVisible);
            Assert.False(browser.Task.IsCompleted);
            player.PrimaryText = "After close begins";
            main.OnWordClicked(Selection("Rejected"), 0, 0);
            await main.InvokeWordMenuAsync(search).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Empty(shell.Clipboard); Assert.Single(shell.BrowserRequests); Assert.False(main.WordPopup.IsOpen);
            browser.TrySetException(new IOException("Private late browser details"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); Assert.Empty(main.Toasts);
        }
        finally
        {
            diagnostics.Result.TrySetResult(new(TranslationDiagnosticOutcome.Success));
            browser.TrySetResult(true);
            await diagnostic.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await main.DrainBrowserOperationsAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Real_close_retires_browser_wait_before_player_shutdown_and_rejects_new_word_effects()
    {
        TaskCompletionSource<bool> browser = new();
        FakeShell shell = new() { BrowserHandler = _ => browser.Task };
        FakePlaybackController player = new();
        AppPrefs prefs = new AppPrefs { WordCopyOnSelected = true, WordLastSearchOnSelected = true, SubsAutoTextCopy = true }.Normalize();
        using MainWindowViewModel main = new(player, prefs, _ => { }, new FakeTranslator(), shell);
        MainWindow window = new(new KeyMapper(), null) { Width = 640, Height = 420 };
        int shutdownCalls = 0;
        window.ShutdownRequested = () => { shutdownCalls++; main.Dispose(); return Task.CompletedTask; };
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        WordMenuInvocation search = main.CreateWordMenu(Selection("Before close")).First(item => item.Action.Kind == WordMenuActionKind.Search);
        Task pending = main.InvokeWordMenuAsync(search); main.TrackBrowserOperation(pending);
        try
        {
            window.Close();
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, shutdownCalls);
            Assert.False(window.IsVisible);
            Assert.False(browser.Task.IsCompleted); // The external browser need not cooperate for our shutdown to finish.
            player.PrimaryText = "After close";
            main.OnWordClicked(Selection("Rejected"), 0, 0);
            await main.InvokeWordMenuAsync(search).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Empty(shell.Clipboard); Assert.Single(shell.BrowserRequests); Assert.False(main.WordPopup.IsOpen);
            browser.TrySetException(new IOException("Private late browser details"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Empty(main.Toasts);
        }
        finally
        {
            browser.TrySetResult(true);
            await main.DrainBrowserOperationsAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Popup_word_header_context_route_opens_the_captured_word_menu_without_provider_side_effects()
    {
        var (main, _, shell, _, _) = TestVm.Create(); using var owned = main;
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null); window.Show();
        main.OnWordClicked(Selection("Captured"), 200, 200);
        Dispatcher.UIThread.RunJobs();
        MenuObserver observer = new();
        using IDisposable subscription = ContextMenu.IsOpenProperty.Changed.Subscribe(observer);
        try
        {
            Control header = window.FindControl<Control>("PopupWordActions")!;
            header.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent });
            Dispatcher.UIThread.RunJobs();
            ContextMenu menu = Assert.Single(observer.Opened);
            Assert.True(menu.IsOpen);
            Assert.Equal(new[] { "Copy", "Copy All", "Search Google", "Search Wiktionary", "Search Longman" }, menu.Items.OfType<MenuItem>().Select(item => item.Header));
            MenuItem copy = menu.Items.OfType<MenuItem>().First();
            copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { "Captured" }, shell.Clipboard);
            Assert.Empty(shell.BrowserRequests);
            menu.Close();
        }
        finally { foreach (ContextMenu menu in observer.Opened) menu.Close(); window.Close(); }
    }

    sealed class MenuObserver : IObserver<AvaloniaPropertyChangedEventArgs>
    {
        public List<ContextMenu> Opened { get; } = [];
        public void OnNext(AvaloniaPropertyChangedEventArgs value)
        {
            if (value.Sender is ContextMenu menu && value.NewValue is true) Opened.Add(menu);
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    sealed class PendingDiagnostics : ITranslationDiagnostics
    {
        public TaskCompletionSource<TranslationDiagnosticResult> Result { get; } = new();
        public CancellationToken Token { get; private set; }
        public Task<TranslationDiagnosticResult> RunAsync(TranslationDiagnosticRequest request, CancellationToken token)
        { Token = token; return Result.Task; }
    }
}

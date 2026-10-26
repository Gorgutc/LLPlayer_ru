using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class TranslationDialogTests
{
    [AvaloniaFact]
    public void Invalid_timeout_switch_restores_editor_and_retry_preserves_provider_fields()
    {
        var vm = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => Task.CompletedTask);
        vm.Endpoint = "https://example.invalid/preserved"; vm.TimeoutMs = null;
        vm.EditingProvider = "Azure";
        Assert.Equal("OpenAILike", vm.EditingProvider); Assert.True(vm.HasModel); Assert.False(vm.HasRegion);
        Assert.NotEmpty(vm.Message);
        vm.TimeoutMs = 1234; vm.EditingProvider = "Azure";
        Assert.Equal("Azure", vm.EditingProvider); Assert.True(vm.HasRegion);
        vm.ApiKey = "fake-azure"; vm.EditingProvider = "OpenAILike";
        Assert.Equal("https://example.invalid/preserved", vm.Endpoint); Assert.Equal(1234, vm.TimeoutMs);
        Assert.Equal("fake-azure", ((AzureTranslateSettings)vm.Draft.Providers[TranslateServiceType.Azure]).ApiKey);
    }

    [AvaloniaFact]
    public async Task Translation_parameter_editors_save_bounded_values_and_reject_invalid_numbers()
    {
        TranslationSettings? saved = null;
        var vm = new TranslationViewModel(new() { WordProvider = "Off" }, draft => { saved = draft; return Task.CompletedTask; });
        var window = new TranslationWindow(vm); window.Show(); Dispatcher.UIThread.RunJobs();
        Assert.Equal("1", window.FindControl<TextBox>("TranslateCountBackwardBox")!.Text);
        Assert.Equal("12", window.FindControl<TextBox>("TranslateCountForwardBox")!.Text);
        Assert.Equal("2", window.FindControl<TextBox>("TranslateMaxConcurrencyBox")!.Text);

        vm.TranslateCountBackward = "0";
        vm.TranslateCountForward = "7";
        vm.TranslateMaxConcurrency = "4";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        Assert.Equal(0, saved.TranslateCountBackward);
        Assert.Equal(7, saved.TranslateCountForward);
        Assert.Equal(4, saved.TranslateMaxConcurrency);

        saved = null;
        vm.TranslateMaxConcurrency = "0";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(saved);
        Assert.Contains("1 to 64", vm.Message);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Independent_sentence_selection_and_explicit_shared_editor_keep_other_provider_drafts()
    {
        TranslationSettings? saved = null;
        var vm = new TranslationViewModel(new() { WordProvider = "GoogleV1", SentenceProvider = "OpenAILike" }, draft => { saved = draft; return Task.CompletedTask; });
        var window = new TranslationWindow(vm); window.Show(); Dispatcher.UIThread.RunJobs();
        Assert.Equal("OpenAILike", window.FindControl<ComboBox>("SentenceProviderBox")!.SelectedItem);
        vm.Endpoint = "https://example.invalid/word";
        vm.EditingProvider = "OpenAILike"; vm.Endpoint = "https://example.invalid/sentence"; vm.Model = "fake-subtitle-model";
        vm.Provider = "Bing";
        Assert.Equal("OpenAILike", vm.SentenceProvider);
        vm.EditingProvider = "GoogleV1";
        Assert.Equal("https://example.invalid/word", vm.Endpoint);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved); Assert.Equal("Bing", saved.WordProvider); Assert.Equal("OpenAILike", saved.SentenceProvider);
        Assert.Equal("https://example.invalid/sentence", ((OpenAILikeTranslateSettings)saved.Providers[TranslateServiceType.OpenAILike]).Endpoint);
        Assert.Equal("fake-subtitle-model", ((OpenAILikeTranslateSettings)saved.Providers[TranslateServiceType.OpenAILike]).Model);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Known_provider_bindings_masked_keys_cancel_save_failure_and_theme_frames()
    {
        using TempDir dir = new();
        var live = new TranslationSettings { WordProvider = "OpenAILike", TargetLanguage = TargetLanguage.Russian };
        live.Providers[TranslateServiceType.OpenAILike] = new OpenAILikeTranslateSettings
        { Endpoint = "https://example.invalid", Model = "fake-model", ApiKey = "fake-secret", TopP = 0.72, TopPManual = true };
        live.Chat.PromptOneByOne = "advanced survives";
        TranslationSettings? applied = null;
        var store = new TranslationSettingsStore(Path.Combine(dir.Path, "LLPlayer.Translation.json"));
        Task Save(TranslationSettings draft) { store.Save(draft); applied = draft; return Task.CompletedTask; }
        var model = new TranslationViewModel(live, Save, "Original file was preserved.");
        var window = new TranslationWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.FindControl<TextBox>("EndpointBox")!.Text = "https://example.invalid/edited";
        window.FindControl<TextBox>("ApiKeyBox")!.Text = "fake-changed";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("https://example.invalid/edited", model.Endpoint);
        Assert.Equal('●', window.FindControl<TextBox>("ApiKeyBox")!.PasswordChar);
        Assert.Equal("Original file was preserved.", window.FindControl<TextBlock>("LoadWarning")!.Text);
        Assert.False(model.HasRegion); Assert.True(model.HasChatPath);
        window.Close(); Assert.Null(applied);
        Assert.Equal("fake-secret", ((OpenAILikeTranslateSettings)live.Providers[TranslateServiceType.OpenAILike]).ApiKey);
        Assert.False(File.Exists(Path.Combine(dir.Path, "LLPlayer.Translation.json")));

        model = new TranslationViewModel(live, Save);
        window = new TranslationWindow(model); window.Show();
        window.FindControl<ComboBox>("ProviderBox")!.SelectedItem = "Azure";
        Dispatcher.UIThread.RunJobs(); Assert.Equal("Azure", model.Provider); Assert.True(model.HasRegion);
        model.ApiKey = "fake-azure"; model.Region = "fake-region";
        model.Provider = "OpenAILike"; Assert.Equal("fake-secret", model.ApiKey);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(applied); Assert.False(window.IsVisible);
        var loaded = store.Load("Off", null);
        Assert.Equal("advanced survives", loaded.Chat.PromptOneByOne);
        Assert.Equal(0.72, ((OpenAILikeTranslateSettings)loaded.Providers[TranslateServiceType.OpenAILike]).TopP);
        Assert.Equal("fake-azure", ((AzureTranslateSettings)loaded.Providers[TranslateServiceType.Azure]).ApiKey);

        model = new TranslationViewModel(live, draft => { new TranslationSettingsStore(dir.Path).Save(draft); return Task.CompletedTask; }, "Persistent load warning.");
        window = new TranslationWindow(model); window.Show();
        await model.SaveCommand.ExecuteAsync(null);
        Assert.True(window.IsVisible); Assert.NotEmpty(model.Message);
        Assert.Equal("Persistent load warning.", model.Warning);
        Assert.DoesNotContain("fake-secret", model.Message);
        string? output = Environment.GetEnvironmentVariable("LLPLAYER_TRANSLATION_ARTIFACTS");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            foreach (string theme in new[] { "Light", "Dark" })
            {
                ThemeService.Apply(theme); Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"translation-{theme.ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
            }
            window.FindControl<ScrollViewer>("TranslationScroll")!.Offset = new Vector(0, 1000);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "translation-lower.png"), PngBitmapEncoderOptions.Default);
            window.FindControl<ScrollViewer>("TranslationScroll")!.Offset = default;
            window.Width = 420; window.Height = 360; Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "translation-small.png"), PngBitmapEncoderOptions.Default);
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task Off_popup_renders_action_and_routes_to_detached_translation_settings()
    {
        var settings = new TranslationSettings { WordProvider = "Off", TargetLanguage = TargetLanguage.Russian };
        var translator = new FlyleafWordTranslator(_ => Language.English, settings,
            (_, _) => throw new InvalidOperationException("Off must never create a provider."));
        var shell = new TranslationShell();
        var window = new MainWindow { Width = 1000, Height = 600 };
        var vm = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs().Normalize(), _ => { }, translator, shell);
        vm.TranslationDialog = () => new TranslationViewModel(translator.Settings, _ => Task.CompletedTask);
        window.Attach(vm, renderer: null); window.Show();
        await vm.WordPopup.OpenAsync("word", "A subtitle context", 0, 350, 300);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.WordPopup.IsNotConfigured); Assert.False(vm.WordPopup.IsBusy);
        Assert.Null(vm.WordPopup.Translation);
        var card = window.FindControl<Border>("WordPopupCard")!;
        Assert.True(card.IsEffectivelyVisible);
        var action = card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Translation settings…"));
        Assert.True(action.IsEffectivelyVisible); Assert.True(action.IsEnabled);
        action.Command!.Execute(action.CommandParameter);
        Assert.NotNull(shell.Shown); Assert.Equal("Off", shell.Shown.Provider);
        Assert.Equal(TargetLanguage.Russian, shell.Shown.TargetLanguage);
        string? output = Environment.GetEnvironmentVariable("LLPLAYER_TRANSLATION_ARTIFACTS");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "translation-popup-not-configured.png"), PngBitmapEncoderOptions.Default);
        }
        window.Close(); vm.Dispose(); await vm.WordPopup.DisposeAsync(); await translator.DisposeAsync();
    }
    sealed class TranslationShell : IAppShell
    {
        public TranslationViewModel? Shown;
        public bool IsFullScreen { get; set; }
        public Task<string?> PickMediaFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public void ShowTranslation(TranslationViewModel model) => Shown = model;
        public void ShowCheatSheet() { }
        public void SetClipboardText(string text) { }
        public void ApplyTheme(string theme) { }
        public void Exit() { }
    }

    [AvaloniaFact]
    public async Task Busy_save_prevents_field_edit_and_discard_until_transaction_settles()
    {
        var finish = new TaskCompletionSource();
        var model = new TranslationViewModel(new(), _ => finish.Task);
        var window = new TranslationWindow(model); window.Show();
        Task save = model.SaveCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(model.Busy);
        Assert.False(window.FindControl<StackPanel>("TranslationFields")!.IsEnabled);
        window.Close(); Assert.True(window.IsVisible);
        finish.SetResult(); await save;
        Assert.False(window.IsVisible); Assert.False(model.Busy);
    }

    [AvaloniaFact]
    public void Main_window_close_waits_for_terminal_drain_and_duplicate_close_does_not_repeat_shutdown()
    {
        var finish = new TaskCompletionSource();
        int calls = 0; bool closed = false;
        var window = new MainWindow { ShutdownRequested = () => { calls++; return finish.Task; } };
        window.Closed += (_, _) => closed = true;
        window.Show(); window.Close();
        Assert.Equal(1, calls); Assert.False(closed); Assert.True(window.IsVisible); Assert.False(window.IsEnabled);
        window.Close(); Assert.Equal(1, calls); Assert.False(closed);
        finish.SetResult(); Dispatcher.UIThread.RunJobs();
        Assert.True(closed); Assert.False(window.IsVisible); Assert.Equal(1, calls);
    }
}

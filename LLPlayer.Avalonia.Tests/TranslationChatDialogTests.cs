using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class TranslationChatDialogTests
{
    static T Control<T>(TranslationWindow window, string name) where T : Control => window.FindControl<T>(name)!;
    static void Edit(TranslationWindow window, string name, string value)
    {
        Control<TextBox>(window, name).Text = value;
        Dispatcher.UIThread.RunJobs();
    }
    static void Mode(TranslationWindow window, ChatTranslateMethod method)
    {
        Control<ComboBox>(window, "TranslateMethodBox").SelectedItem = method;
        Dispatcher.UIThread.RunJobs();
    }
    static void Reset(TranslationWindow window, string name)
    {
        var button = Control<Button>(window, name);
        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Every_chat_field_round_trips_from_controls_without_changing_live_until_save()
    {
        using TempDir dir = new();
        var live = new TranslationSettings { WordProvider = "Off" };
        var store = new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json"));
        TranslationSettings? applied = null;
        var vm = new TranslationViewModel(live, draft => { store.Save(draft); applied = draft; return Task.CompletedTask; });
        var window = new TranslationWindow(vm); window.Show();
        Control<Expander>(window, "ChatExpander").IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ChatTranslateMethod.ContextWindow, vm.TranslateMethod);
        Assert.Equal("6", vm.ContextWindowBefore); Assert.Equal("6", vm.ContextWindowAfter); Assert.True(vm.GrammarCheckEnabled);
        Edit(window, "ContextWindowBeforeBox", "0"); Edit(window, "ContextWindowAfterBox", "11");
        Edit(window, "PromptContextWindowBox", "custom window\r\nexact text");
        Edit(window, "PromptGrammarCheckBox", "custom grammar\r\nexact text");
        Control<CheckBox>(window, "GrammarCheckEnabledBox").IsChecked = false;
        Control<CheckBox>(window, "IncludeTargetLangRegionBox").IsChecked = false;
        Mode(window, ChatTranslateMethod.KeepContext);
        Edit(window, "SubtitleContextCountBox", "3");
        Control<ComboBox>(window, "ContextRetainPolicyBox").SelectedItem = ChatContextRetainPolicy.KeepSize;
        Edit(window, "PromptKeepContextBox", "custom keep\r\nexact text");
        Mode(window, ChatTranslateMethod.OneByOne); Edit(window, "PromptOneByOneBox", "custom one\r\n{source_text}");
        Control<ComboBox>(window, "EditingProviderBox").SelectedItem = "Bing";
        Control<ComboBox>(window, "SentenceProviderBox").SelectedItem = "OpenAILike";
        Dispatcher.UIThread.RunJobs();
        Mode(window, ChatTranslateMethod.ContextWindow);
        Assert.Equal("custom window\r\nexact text", vm.PromptContextWindow);
        Assert.Equal("custom keep\r\nexact text", vm.PromptKeepContext);
        Assert.Equal("custom one\r\n{source_text}", vm.PromptOneByOne);
        Assert.Equal(6, live.Chat.ContextWindowBefore); Assert.True(live.Chat.GrammarCheckEnabled);
        Assert.Null(applied); Assert.False(File.Exists(Path.Combine(dir.Path, "translation.json")));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(applied); Assert.False(window.IsVisible);
        var chat = store.Load("Off", null).Chat;
        Assert.Equal(ChatTranslateMethod.ContextWindow, chat.TranslateMethod);
        Assert.Equal(0, chat.ContextWindowBefore); Assert.Equal(11, chat.ContextWindowAfter);
        Assert.Equal(3, chat.SubtitleContextCount); Assert.Equal(ChatContextRetainPolicy.KeepSize, chat.ContextRetainPolicy);
        Assert.False(chat.GrammarCheckEnabled); Assert.False(chat.IncludeTargetLangRegion);
        Assert.Equal("custom window\r\nexact text", chat.PromptContextWindow);
        Assert.Equal("custom grammar\r\nexact text", chat.PromptGrammarCheck);
        Assert.Equal("custom keep\r\nexact text", chat.PromptKeepContext);
        Assert.Equal("custom one\r\n{source_text}", chat.PromptOneByOne);
        Assert.Equal(6, live.Chat.ContextWindowBefore);
    }

    [AvaloniaFact]
    public async Task Close_discards_and_each_reset_changes_only_its_prompt_to_current_LF_default()
    {
        var live = new TranslationSettings { WordProvider = "OpenAILike", SentenceProvider = "Bing", TargetLanguage = TargetLanguage.Russian };
        live.Providers[TranslateServiceType.OpenAILike] = new OpenAILikeTranslateSettings { ApiKey = "fake-reset-secret", Model = "fake-reset-model", Endpoint = "https://example.invalid/reset-preserve" };
        live.Chat.PromptContextWindow = "window original"; live.Chat.PromptKeepContext = "keep original";
        live.Chat.PromptOneByOne = "one original"; live.Chat.PromptGrammarCheck = "grammar original";
        int saves = 0;
        var vm = new TranslationViewModel(live, _ => { saves++; return Task.CompletedTask; });
        var window = new TranslationWindow(vm); window.Show();
        Control<Expander>(window, "ChatExpander").IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Reset(window, "ResetContextWindowPromptButton");
        Assert.Equal(TranslateChatConfig.DefaultPromptContextWindow.ReplaceLineEndings("\n"), vm.PromptContextWindow);
        Assert.Equal("grammar original", vm.PromptGrammarCheck); Assert.Equal("keep original", vm.PromptKeepContext); Assert.Equal("one original", vm.PromptOneByOne);
        Reset(window, "ResetGrammarCheckPromptButton");
        Assert.Equal(TranslateChatConfig.DefaultPromptGrammarCheck.ReplaceLineEndings("\n"), vm.PromptGrammarCheck);
        Assert.Equal("keep original", vm.PromptKeepContext); Assert.Equal("one original", vm.PromptOneByOne);
        Mode(window, ChatTranslateMethod.KeepContext); Reset(window, "ResetKeepContextPromptButton");
        Assert.Equal(TranslateChatConfig.DefaultPromptKeepContext.ReplaceLineEndings("\n"), vm.PromptKeepContext);
        Assert.Equal("one original", vm.PromptOneByOne);
        Mode(window, ChatTranslateMethod.OneByOne); Reset(window, "ResetOneByOnePromptButton");
        Assert.Equal(TranslateChatConfig.DefaultPromptOneByOne.ReplaceLineEndings("\n"), vm.PromptOneByOne);
        Assert.Equal(TranslateChatConfig.DefaultPromptKeepContext.ReplaceLineEndings("\n"), vm.PromptKeepContext);
        Edit(window, "PromptOneByOneBox", "discard me"); window.Close();
        Assert.Equal(0, saves);
        Assert.Equal("window original", live.Chat.PromptContextWindow); Assert.Equal("grammar original", live.Chat.PromptGrammarCheck);
        Assert.Equal("keep original", live.Chat.PromptKeepContext); Assert.Equal("one original", live.Chat.PromptOneByOne);
        Assert.Equal("OpenAILike", vm.Provider); Assert.Equal("Bing", vm.SentenceProvider); Assert.Equal(TargetLanguage.Russian, vm.TargetLanguage);
        Assert.Equal("fake-reset-secret", vm.ApiKey); Assert.Equal("fake-reset-model", vm.Model); Assert.Equal("https://example.invalid/reset-preserve", vm.Endpoint);
        TranslationSettings? saved = null;
        var saveVm = new TranslationViewModel(live, draft => { saved = draft; return Task.CompletedTask; });
        saveVm.ResetContextWindowPromptCommand.Execute(null);
        saveVm.ResetGrammarCheckPromptCommand.Execute(null);
        saveVm.ResetKeepContextPromptCommand.Execute(null);
        saveVm.ResetOneByOnePromptCommand.Execute(null);
        await saveVm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        Assert.Equal("OpenAILike", saved.WordProvider); Assert.Equal("Bing", saved.SentenceProvider); Assert.Equal(TargetLanguage.Russian, saved.TargetLanguage);
        var provider = Assert.IsType<OpenAILikeTranslateSettings>(saved.Providers[TranslateServiceType.OpenAILike]);
        Assert.Equal("fake-reset-secret", provider.ApiKey); Assert.Equal("fake-reset-model", provider.Model); Assert.Equal("https://example.invalid/reset-preserve", provider.Endpoint);
        Assert.Equal(TranslateChatConfig.DefaultPromptContextWindow.ReplaceLineEndings("\n"), saved.Chat.PromptContextWindow);
        Assert.Equal(TranslateChatConfig.DefaultPromptGrammarCheck.ReplaceLineEndings("\n"), saved.Chat.PromptGrammarCheck);
        Assert.Equal(TranslateChatConfig.DefaultPromptKeepContext.ReplaceLineEndings("\n"), saved.Chat.PromptKeepContext);
        Assert.Equal(TranslateChatConfig.DefaultPromptOneByOne.ReplaceLineEndings("\n"), saved.Chat.PromptOneByOne);
    }

    [AvaloniaFact]
    public async Task Invalid_numeric_text_in_any_mode_prevents_save_and_live_mutation_then_can_be_corrected()
    {
        var live = new TranslationSettings { WordProvider = "Off" };
        int saves = 0;
        var vm = new TranslationViewModel(live, _ => { saves++; return Task.CompletedTask; });
        var window = new TranslationWindow(vm) { Width = 400, Height = 360 }; window.Show();
        Control<Expander>(window, "ChatExpander").IsExpanded = true;
        foreach (string field in new[] { "ContextWindowBeforeBox", "ContextWindowAfterBox", "SubtitleContextCountBox" })
        foreach (string value in new[] { "", "1.5", "-1", "2147483648", "invalid" })
        {
            Mode(window, field == "SubtitleContextCountBox" ? ChatTranslateMethod.KeepContext : ChatTranslateMethod.ContextWindow);
            Edit(window, field, value);
            // Invalid inactive draft must also block Save, without being erased by switching modes/providers.
            Mode(window, ChatTranslateMethod.OneByOne);
            Control<ComboBox>(window, "EditingProviderBox").SelectedItem = "Bing";
            Dispatcher.UIThread.RunJobs();
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.True(window.IsVisible); Assert.Equal(0, saves); Assert.NotEmpty(vm.Message);
            var message = Control<TextBlock>(window, "TranslationMessage");
            var messageOrigin = message.TranslatePoint(default, window)!.Value;
            Assert.InRange(messageOrigin.Y, 0, window.ClientSize.Height - message.Bounds.Height);
            Assert.True(message.IsEffectivelyVisible);
            Assert.Equal(6, live.Chat.ContextWindowBefore); Assert.Equal(6, live.Chat.ContextWindowAfter); Assert.Equal(6, live.Chat.SubtitleContextCount);
            Assert.Equal(value, Control<TextBox>(window, field).Text);
            Edit(window, field, "6");
        }
        Edit(window, "ContextWindowBeforeBox", "0"); Edit(window, "ContextWindowAfterBox", "0"); Edit(window, "SubtitleContextCountBox", "0");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, saves); Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void Active_modes_and_grammar_visibility_keep_footer_accessible_in_small_light_and_dark_frames()
    {
        var vm = new TranslationViewModel(new() { WordProvider = "Off" }, _ => Task.CompletedTask);
        var window = new TranslationWindow(vm) { Width = 400, Height = 360 }; window.Show();
        var expander = Control<Expander>(window, "ChatExpander");
        Assert.False(expander.IsExpanded); expander.IsExpanded = true;
        string? output = Environment.GetEnvironmentVariable("LLPLAYER_TRANSLATION_ARTIFACTS");
        foreach (string theme in new[] { "Light", "Dark" })
        foreach (var method in Enum.GetValues<ChatTranslateMethod>())
        {
            window.Width = 400; window.Height = 360;
            ThemeService.Apply(theme); Mode(window, method);
            Assert.Equal(method == ChatTranslateMethod.ContextWindow, Control<StackPanel>(window, "ContextWindowFields").IsVisible);
            Assert.Equal(method == ChatTranslateMethod.KeepContext, Control<StackPanel>(window, "KeepContextFields").IsVisible);
            Assert.Equal(method == ChatTranslateMethod.OneByOne, Control<StackPanel>(window, "OneByOneFields").IsVisible);
            vm.GrammarCheckEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.False(Control<StackPanel>(window, "GrammarPromptFields").IsVisible);
            vm.GrammarCheckEnabled = true; Dispatcher.UIThread.RunJobs();
            Assert.Equal(method == ChatTranslateMethod.ContextWindow, vm.ShowGrammarPrompt);
            Control<ScrollViewer>(window, "TranslationScroll").Offset = new Vector(0, 10000);
            Dispatcher.UIThread.RunJobs();
            var save = Control<Button>(window, "SaveButton");
            var origin = save.TranslatePoint(default, window)!.Value;
            Assert.True(save.IsEffectivelyVisible); Assert.True(save.IsEnabled);
            Assert.InRange(origin.Y, 0, window.ClientSize.Height - save.Bounds.Height);
            if (output != null)
            {
                Directory.CreateDirectory(output);
                window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"chat-{theme.ToLowerInvariant()}-{method}-small.png"), PngBitmapEncoderOptions.Default);
                window.Width = 600; window.Height = 640; Dispatcher.UIThread.RunJobs();
                Control<ComboBox>(window, "TranslateMethodBox").BringIntoView(); Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"chat-{theme.ToLowerInvariant()}-{method}-fields.png"), PngBitmapEncoderOptions.Default);
                Control<TextBox>(window, method switch
                {
                    ChatTranslateMethod.ContextWindow => "PromptContextWindowBox",
                    ChatTranslateMethod.KeepContext => "PromptKeepContextBox",
                    _ => "PromptOneByOneBox"
                }).BringIntoView(); Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"chat-{theme.ToLowerInvariant()}-{method}-prompt.png"), PngBitmapEncoderOptions.Default);
            }
        }
        window.Close(); ThemeService.Apply("Dark");
    }
}

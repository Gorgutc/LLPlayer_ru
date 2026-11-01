using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using LLPlayer.Avalonia.Services;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace LLPlayer.Avalonia.Tests;

public class TranslationAdvancedTests
{
    [AvaloniaFact]
    public void Advanced_editor_exposes_raw_sampling_and_nullable_token_drafts()
    {
        var vm = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => Task.CompletedTask);
        var window = new TranslationWindow(vm); window.Show();
        try
        {
            Assert.NotNull(window.FindControl<TextBox>("TemperatureBox"));
            Assert.NotNull(window.FindControl<TextBox>("MaxTokensBox"));
            Assert.NotNull(window.FindControl<Button>("CheckProviderButton"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("TemperatureBox", "2.01")]
    [InlineData("TemperatureBox", "NaN")]
    [InlineData("TemperatureBox", "0,5")]
    [InlineData("TopPBox", "-0.1")]
    [InlineData("TopPBox", "abc")]
    [InlineData("FrequencyPenaltyBox", "-2.01")]
    [InlineData("PresencePenaltyBox", "2.01")]
    [InlineData("MaxTokensBox", "0")]
    [InlineData("MaxTokensBox", "-1")]
    [InlineData("MaxTokensBox", "1.5")]
    [InlineData("MaxCompletionTokensBox", "2147483648")]
    public async Task Typed_invalid_raw_value_survives_blur_and_blocks_save_and_switch(string name, string raw)
    {
        int saves = 0;
        var vm = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => { saves++; return Task.CompletedTask; });
        var window = new TranslationWindow(vm); window.Show();
        try
        {
            window.FindControl<Expander>("SamplingExpander")!.IsExpanded = true;
            var editor = window.FindControl<TextBox>(name)!;
            editor.Focus(); editor.Text = raw; Dispatcher.UIThread.RunJobs();
            window.FindControl<Button>("SaveButton")!.Focus(); Dispatcher.UIThread.RunJobs();
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal(0, saves); Assert.Equal(raw, editor.Text); Assert.NotEmpty(vm.Message);
            vm.EditingProvider = "GoogleV1";
            Assert.Equal("OpenAILike", vm.EditingProvider); Assert.Equal(raw, editor.Text);
            Assert.True(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Manual_flags_nullable_caps_regions_and_narrow_resets_round_trip_without_touching_live()
    {
        var llm = new OpenAILikeTranslateSettings { Endpoint = "https://example.invalid/custom", Model = "custom", ApiKey = "synthetic-key", ChatPath = "/custom" };
        var google = new GoogleV1TranslateSettings(); google.Regions["xx"] = "custom-unknown"; google.Regions["zh"] = "zh-custom";
        var live = new TranslationSettings { WordProvider = "OpenAILike", Providers = { [TranslateServiceType.OpenAILike] = llm, [TranslateServiceType.GoogleV1] = google } };
        TranslationSettings? saved = null;
        var vm = new TranslationViewModel(live, value => { saved = value; return Task.CompletedTask; });
        vm.TemperatureText = "2"; vm.TopPText = "0"; vm.FrequencyPenaltyText = "-2"; vm.PresencePenaltyText = "2";
        vm.TemperatureManual = vm.TopPManual = vm.FrequencyPenaltyManual = vm.PresencePenaltyManual = true;
        vm.MaxTokensText = ""; vm.MaxCompletionTokensText = "2147483647";
        vm.ResetEndpointCommand.Execute(null); vm.ResetChatPathCommand.Execute(null);
        Assert.Equal(new OpenAILikeTranslateSettings().Endpoint, vm.Endpoint);
        Assert.Equal("synthetic-key", vm.ApiKey); Assert.Equal("custom", vm.Model);
        vm.EditingProvider = "GoogleV1"; Assert.Equal("zh-custom", vm.ChineseRegion);
        vm.FrenchRegion = "fr-custom";
        vm.EditingProvider = "Azure"; vm.ChineseRegion = "zh-Hant";
        vm.EditingProvider = "OpenAILike";
        Assert.Equal("2", vm.TemperatureText);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        var result = (OpenAILikeTranslateSettings)saved.Providers[TranslateServiceType.OpenAILike];
        Assert.Equal(2, result.Temperature); Assert.Equal(0, result.TopP); Assert.Equal(-2, result.FrequencyPenalty); Assert.Equal(2, result.PresencePenalty);
        Assert.True(result.TemperatureManual && result.TopPManual && result.FrequencyPenaltyManual && result.PresencePenaltyManual);
        Assert.Null(result.MaxTokens); Assert.Equal(int.MaxValue, result.MaxCompletionTokens);
        var regions = (GoogleV1TranslateSettings)saved.Providers[TranslateServiceType.GoogleV1];
        Assert.Equal("custom-unknown", regions.Regions["xx"]); Assert.Equal("fr-custom", regions.Regions["fr"]);
        Assert.Equal("https://example.invalid/custom", llm.Endpoint); Assert.Equal("fr-FR", google.Regions["fr"]);
    }

    [AvaloniaFact]
    public async Task Diagnostics_are_explicit_snapshot_owned_single_flight_and_preserve_custom_model()
    {
        var transport = new ControlledDiagnostics(); int saves = 0;
        var vm = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => { saves++; return Task.CompletedTask; }, diagnostics: transport);
        var window = new TranslationWindow(vm); window.Show();
        Assert.Equal(0, transport.Calls);
        vm.Endpoint = "http://127.0.0.1:1234"; vm.Model = "typed-custom"; vm.ApiKey = "synthetic-secret";
        var task = vm.GetModelsCommand.ExecuteAsync(null);
        Assert.True(vm.Busy); Assert.Equal(1, transport.Calls);
        var snapshot = (OpenAILikeTranslateSettings)transport.Request!.Settings.Providers[TranslateServiceType.OpenAILike];
        Assert.Equal("typed-custom", snapshot.Model); Assert.Equal("synthetic-secret", snapshot.ApiKey);
        await vm.CheckProviderCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, transport.Calls); Assert.Equal(0, saves);
        transport.Complete(new(TranslationDiagnosticOutcome.Success, ["discovered", "synthetic-secret", "Bearer synthetic-value"]));
        await task;
        Assert.Equal("typed-custom", vm.Model); Assert.Equal(new[] { "discovered" }, vm.AvailableModels);
        Assert.DoesNotContain("synthetic-secret", vm.DiagnosticStatus);
        await vm.SaveCommand.ExecuteAsync(null); Assert.Equal(1, saves); Assert.Equal(1, transport.Calls);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Provider_mutation_cancels_and_rejects_late_reply_while_close_drains_uncooperative_transport()
    {
        var transport = new ControlledDiagnostics();
        var vm = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => Task.CompletedTask, diagnostics: transport);
        var window = new TranslationWindow(vm); window.Show();
        var task = vm.GetModelsCommand.ExecuteAsync(null);
        vm.EditingProvider = "GoogleV1";
        Assert.True(transport.Token.IsCancellationRequested);
        Assert.Equal("OpenAILike", transport.Request!.Provider.ToString());
        window.Close(); window.Close();
        Assert.True(window.IsVisible); Assert.False(window.IsEnabled);
        transport.Complete(new(TranslationDiagnosticOutcome.Success, ["late-model"]));
        await task; Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible); Assert.Empty(vm.AvailableModels);
    }

    [AvaloniaFact]
    public async Task Error_status_never_echoes_provider_exception_response_or_credentials_and_can_retry()
    {
        var transport = new ControlledDiagnostics();
        var vm = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => Task.CompletedTask, diagnostics: transport);
        vm.ApiKey = "synthetic-secret";
        var task = vm.HelloApiCommand.ExecuteAsync(null);
        Assert.Contains("model request", vm.DiagnosticStatus);
        transport.Fail(new InvalidOperationException("response Authorization: Bearer synthetic-secret"));
        await task;
        Assert.Contains("failed", vm.DiagnosticStatus); Assert.DoesNotContain("synthetic", vm.DiagnosticStatus); Assert.False(vm.Busy);
        transport.Reset(); task = vm.CheckProviderCommand.ExecuteAsync(null);
        transport.Complete(new(TranslationDiagnosticOutcome.Unsupported)); await task;
        Assert.Contains("not supported", vm.DiagnosticStatus);
    }

    [AvaloniaFact]
    public async Task Reset_discard_failed_write_retry_and_small_window_footer_remain_owned()
    {
        bool fail = true; TranslationSettings? saved = null;
        var live = new TranslationSettings { WordProvider = "OpenAILike", Providers = { [TranslateServiceType.OpenAILike] = new OpenAILikeTranslateSettings { Endpoint = "https://example.invalid/custom", ApiKey = "synthetic-secret" } } };
        var vm = new TranslationViewModel(live, draft => { if (fail) throw new IOException("synthetic-secret"); saved = draft; return Task.CompletedTask; });
        var window = new TranslationWindow(vm) { Width = 400, Height = 360 }; window.Show();
        vm.ResetEndpointCommand.Execute(null); vm.TemperatureText = "1.2";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(window.IsVisible); Assert.DoesNotContain("synthetic-secret", vm.Message); Assert.Null(saved);
        Assert.Equal("https://example.invalid/custom", ((OpenAILikeTranslateSettings)live.Providers[TranslateServiceType.OpenAILike]).Endpoint);
        var button = window.FindControl<Button>("SaveButton")!;
        Assert.True(button.IsEffectivelyVisible); Assert.True(button.Bounds.Width > 0); Assert.True(button.Focus());
        Assert.True(window.FindControl<ScrollViewer>("TranslationScroll")!.Viewport.Height > 0);
        fail = false; await vm.SaveCommand.ExecuteAsync(null); Assert.NotNull(saved); Assert.False(window.IsVisible);
        var discard = new TranslationViewModel(live, _ => throw new InvalidOperationException());
        var second = new TranslationWindow(discard); second.Show(); discard.TemperatureText = "1.5";
        second.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(second.IsVisible); Assert.Equal("0", new TranslationViewModel(live, _ => Task.CompletedTask).TemperatureText);
    }

    [AvaloniaTheory]
    [InlineData("GoogleV1")]
    [InlineData("Bing")]
    [InlineData("Azure")]
    public async Task Partial_existing_region_map_uses_engine_fallbacks_and_preserves_unknown_keys(string name)
    {
        var type = Enum.Parse<TranslateServiceType>(name);
        var provider = type.DefaultSettings();
        var map = provider is GoogleV1TranslateSettings google ? google.Regions : ((MicrosoftTranslateSettings)provider).Regions;
        map.Clear(); map["xx"] = "unknown-custom"; map["fr"] = "fr-custom";
        TranslationSettings? saved = null;
        var vm = new TranslationViewModel(new() { WordProvider = name, Providers = { [type] = provider } }, value => { saved = value; return Task.CompletedTask; });
        Assert.NotEmpty(vm.ChineseRegion); Assert.Equal("fr-custom", vm.FrenchRegion); Assert.NotEmpty(vm.PortugueseRegion);
        vm.EditingProvider = "OpenAILike";
        Assert.Equal("OpenAILike", vm.EditingProvider);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        var actual = saved.Providers[type] is GoogleV1TranslateSettings g ? g.Regions : ((MicrosoftTranslateSettings)saved.Providers[type]).Regions;
        Assert.Equal("unknown-custom", actual["xx"]); Assert.Equal("fr-custom", actual["fr"]);
        Assert.Equal("unknown-custom", map["xx"]); Assert.False(map.ContainsKey("zh"));
    }

    [AvaloniaFact]
    public async Task Invalid_custom_region_remains_visible_and_blocks_save_without_restoring_prior_code()
    {
        int saves = 0;
        var vm = new TranslationViewModel(new() { WordProvider = "GoogleV1" }, _ => { saves++; return Task.CompletedTask; });
        var window = new TranslationWindow(vm); window.Show();
        try
        {
            window.FindControl<Expander>("RegionsExpander")!.IsExpanded = true;
            var editor = window.FindControl<TextBox>("ChineseRegionBox")!; editor.Text = "bad region"; Dispatcher.UIThread.RunJobs();
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal(0, saves); Assert.Equal("bad region", editor.Text); Assert.Contains("Region", vm.Message);
            vm.EditingProvider = "Bing"; Assert.Equal("GoogleV1", vm.EditingProvider); Assert.Equal("bad region", vm.ChineseRegion);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Minimum_window_keeps_advanced_keyboard_and_active_diagnostic_footer_inside_client_bounds(string theme)
    {
        ThemeService.Apply(theme);
        var transport = new ControlledDiagnostics();
        var vm = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => Task.CompletedTask, diagnostics: transport);
        var window = new TranslationWindow(vm) { Width = 400, Height = 360 }; window.Show();
        try
        {
            window.FindControl<Expander>("SamplingExpander")!.IsExpanded = true;
            var temperature = window.FindControl<TextBox>("TemperatureBox")!;
            temperature.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.True(temperature.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<CheckBox>("TopPManualBox")!.IsFocused);
            var task = vm.CheckProviderCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            string? frames = Environment.GetEnvironmentVariable("LLPLAYER_TRANSLATION_ARTIFACTS");
            if (frames != null)
            {
                Directory.CreateDirectory(frames);
                window.CaptureRenderedFrame()!.Save(Path.Combine(frames, $"translation-advanced-{theme.ToLowerInvariant()}-400x360.png"), PngBitmapEncoderOptions.Default);
            }
            foreach (string name in new[] { "CancelDiagnosticButton", "SaveButton" })
            {
                var control = window.FindControl<Button>(name)!;
                var origin = control.TranslatePoint(default, window)!.Value;
                Assert.InRange(origin.X, 0, window.ClientSize.Width - control.Bounds.Width);
                Assert.InRange(origin.Y, 0, window.ClientSize.Height - control.Bounds.Height);
            }
            Assert.True(window.FindControl<ScrollViewer>("TranslationScroll")!.Viewport.Height > 0);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Assert.True(window.IsVisible);
            Assert.True(transport.Token.IsCancellationRequested);
            transport.Complete(new(TranslationDiagnosticOutcome.Success)); await task; Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (!transport.Completed) transport.Complete(new(TranslationDiagnosticOutcome.Success));
            window.Close(); ThemeService.Apply("Dark");
        }
    }

    sealed class ControlledDiagnostics : ITranslationDiagnostics
    {
        TaskCompletionSource<TranslationDiagnosticResult> result = new();
        public int Calls { get; private set; }
        public TranslationDiagnosticRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool Completed => result.Task.IsCompleted;
        public Task<TranslationDiagnosticResult> RunAsync(TranslationDiagnosticRequest request, CancellationToken token)
        { Calls++; Request = request; Token = token; return result.Task; }
        public void Complete(TranslationDiagnosticResult value) => result.SetResult(value);
        public void Fail(Exception error) => result.SetException(error);
        public void Reset() => result = new();
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class SettingsPlaybackRecognitionTests
{
    [AvaloniaFact]
    public async Task Real_host_fallback_fields_are_readonly_and_callback_owns_the_detached_save()
    {
        using var temp = new TempDir(); var backend = new Backend(); using var actions = new RecognitionActions(backend);
        var store = new RecognitionSettingsStore(Path.Combine(temp.Path, "recognition.json"));
        var initial = new RecognitionSettings { OcrPrimaryLanguage = "ru", OcrSecondaryLanguage = "ja", OcrSecondarySame = false };
        RecognitionSettings? received = null;
        var model = new RecognitionViewModel(initial, actions, store, save: candidate =>
        {
            received = candidate; candidate.WhisperCpp.Prompt = "callback owns its snapshot";
            return Task.CompletedTask;
        }, fallbackLanguagesEditable: false) { ModelDirectory = temp.Path };
        Assert.False(model.CanEditFallbackLanguages);
        var window = new RecognitionWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.False(window.FindControl<TextBox>("OcrPrimaryFallbackEditor")!.IsEffectivelyEnabled);
            Assert.False(window.FindControl<TextBox>("OcrSecondaryFallbackEditor")!.IsEffectivelyEnabled);
            Assert.True(window.FindControl<TextBlock>("RecognitionFallbackManagedNote")!.IsVisible);
            model.Draft.WhisperCpp.Prompt = "draft prompt";
            await model.SaveCommand.ExecuteAsync(null);
            Assert.NotNull(received); Assert.NotSame(model.Draft, received); Assert.Equal("draft prompt", model.Draft.WhisperCpp.Prompt);
            Assert.Equal("ru", received.OcrPrimaryLanguage); Assert.Null(backend.Applied); Assert.Null(store.Load());
            Assert.False(window.IsVisible);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public async Task Callback_failure_keeps_recognition_open_and_retry_does_not_fall_back_to_another_writer()
    {
        using var temp = new TempDir(); var backend = new Backend(); using var actions = new RecognitionActions(backend);
        var store = new RecognitionSettingsStore(Path.Combine(temp.Path, "recognition.json")); int attempts = 0;
        var model = new RecognitionViewModel(new(), actions, store, save: _ =>
        {
            attempts++; if (attempts == 1) throw new IOException("callback write failure"); return Task.CompletedTask;
        }, fallbackLanguagesEditable: false) { ModelDirectory = temp.Path };
        var window = new RecognitionWindow(model); window.Show();
        try
        {
            await model.SaveCommand.ExecuteAsync(null);
            Assert.Contains("callback write failure", model.Message); Assert.True(window.IsVisible);
            Assert.False(model.Busy); Assert.Null(backend.Applied); Assert.Null(store.Load());
            await model.SaveCommand.ExecuteAsync(null);
            Assert.Equal(2, attempts); Assert.False(window.IsVisible); Assert.Null(backend.Applied); Assert.Null(store.Load());
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public void Legacy_callers_keep_editable_fallback_fields()
    {
        using var temp = new TempDir(); using var actions = new RecognitionActions(new Backend());
        var model = new RecognitionViewModel(new(), actions, new RecognitionSettingsStore(Path.Combine(temp.Path, "recognition.json")));
        Assert.True(model.CanEditFallbackLanguages);
    }

    sealed class Backend : IRecognitionBackend
    {
        public RecognitionSettings? Applied;
        public bool HasAudio => true; public bool IsLive => false; public bool IsBusy => false; public bool IsPaused => false;
        public void Start(int slot) { } public void Pause() { } public void Resume() { } public void Cancel(int slot) { } public void Ocr(int slot) { }
        public void Apply(RecognitionSettings settings) => Applied = settings.Clone();
    }
}

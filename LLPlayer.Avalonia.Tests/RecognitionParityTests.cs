using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;
using LLPlayer.Avalonia.Services;
using Avalonia.Threading;
using System.Diagnostics;
using Whisper.net.LibraryLoader;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public class RecognitionParityTests
{
    [AvaloniaFact]
    public void Dialog_exposes_engine_selection_and_missing_wpf_recognition_controls()
    {
        using TempDir temp = new();
        using RecognitionActions actions = new(new Backend());
        RecognitionViewModel model = new(new RecognitionSettings(), actions, new(Path.Combine(temp.Path, "recognition.json")));
        RecognitionWindow window = new(model);
        try
        {
            window.Show();
            Assert.NotNull(window.FindControl<ComboBox>("AsrEngineBox"));
            foreach (string name in new[] { "NoSpeechThresholdBox", "AudioContextSizeBox", "TemperatureBox", "TemperatureIncBox", "EntropyThresholdBox", "LogProbThresholdBox", "AsrChunkSizeBox", "AsrChunkSecondsBox", "FasterEnginePathBox", "FasterModelDirectoryBox", "FasterExtraArgumentsBox" })
                Assert.NotNull(window.FindControl<TextBox>(name));
            Assert.NotNull(window.FindControl<ComboBox>("OcrPrimaryEngineBox"));
            Assert.NotNull(window.FindControl<ComboBox>("OcrSecondaryEngineBox"));
            Assert.NotNull(window.FindControl<Button>("ManageModelsButton"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Bound_engine_paths_decoding_and_region_choices_save_detached_then_apply()
    {
        using TempDir temp = new();
        string executable = Path.Combine(temp.Path, "test-engine"); File.WriteAllText(executable, "fixture — never executed");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(temp.Path, "chi_sim.traineddata"), "fixture");
        File.WriteAllText(Path.Combine(temp.Path, "chi_tra.traineddata"), "fixture");
        Backend backend = new(); using RecognitionActions actions = new(backend);
        RecognitionSettingsStore store = new(Path.Combine(temp.Path, "recognition.json"));
        RecognitionSettings original = new();
        RecognitionViewModel model = new(original, actions, store, microsoftLanguages: () => ["en-US", "en-GB"], tesseractDirectory: temp.Path) { ModelDirectory = temp.Path };
        RecognitionWindow window = new(model); window.Show();
        try
        {
            window.FindControl<ComboBox>("AsrEngineBox")!.SelectedItem = SubASREngineType.FasterWhisper;
            window.FindControl<TextBox>("NoSpeechThresholdBox")!.Text = "0.4";
            window.FindControl<TextBox>("AudioContextSizeBox")!.Text = "512";
            window.FindControl<TextBox>("TemperatureBox")!.Text = "0.2";
            window.FindControl<TextBox>("TemperatureIncBox")!.Text = "0.3";
            window.FindControl<TextBox>("EntropyThresholdBox")!.Text = "2.4";
            window.FindControl<TextBox>("LogProbThresholdBox")!.Text = "-0.7";
            window.FindControl<TextBox>("AsrChunkSizeBox")!.Text = "3000";
            window.FindControl<TextBox>("AsrChunkSecondsBox")!.Text = "31";
            window.FindControl<TextBox>("FasterEnginePathBox")!.Text = executable;
            window.FindControl<TextBox>("FasterModelDirectoryBox")!.Text = temp.Path;
            window.FindControl<CheckBox>("FasterManualEngineBox")!.IsChecked = true;
            window.FindControl<CheckBox>("FasterManualModelBox")!.IsChecked = true;
            window.FindControl<CheckBox>("FasterAntiHallucinationBox")!.IsChecked = false;
            window.FindControl<ComboBox>("FasterPriorityBox")!.SelectedItem = ProcessPriorityClass.BelowNormal;
            window.FindControl<ComboBox>("OcrPrimaryEngineBox")!.SelectedItem = SubOCREngineType.MicrosoftOCR;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(model.TesseractRegions).Selected = "chi_tra";
            Assert.Single(model.MicrosoftRegions).Selected = "en-GB";
            Assert.Null(backend.Applied); Assert.False(original.FasterWhisper.UseManualEngine);
            await model.SaveCommand.ExecuteAsync(null);
            Assert.False(window.IsVisible);
            var saved = store.Load()!;
            Assert.Equal(SubASREngineType.FasterWhisper, saved.ASREngine);
            Assert.Equal(SubOCREngineType.MicrosoftOCR, saved.OcrPrimaryEngine);
            Assert.Equal(3000, saved.ASRChunkSizeMB); Assert.Equal(31, saved.ASRChunkSeconds);
            Assert.Equal(.4f, saved.WhisperCpp.NoSpeechThreshold); Assert.Equal(512, saved.WhisperCpp.AudioContextSize);
            Assert.Equal(.2f, saved.WhisperCpp.Temperature); Assert.Equal(.3f, saved.WhisperCpp.TemperatureInc);
            Assert.Equal(2.4f, saved.WhisperCpp.EntropyThreshold); Assert.Equal(-.7f, saved.WhisperCpp.LogProbThreshold);
            Assert.Equal(executable, saved.FasterWhisper.ManualEnginePath); Assert.True(saved.FasterWhisper.UseManualModel);
            Assert.False(saved.FasterWhisper.AntiHallucination); Assert.Equal(ProcessPriorityClass.BelowNormal, saved.FasterWhisper.ProcessPriority);
            Assert.Equal("chi_tra", saved.TesseractOcrRegions["zh"]); Assert.Equal("en-GB", saved.MsOcrRegions["en"]);
            Assert.Equal(saved.ASREngine, backend.Applied!.ASREngine);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("NoSpeechThresholdBox", "NaN")]
    [InlineData("NoSpeechThresholdBox", "1.1")]
    [InlineData("AudioContextSizeBox", "-1")]
    [InlineData("TemperatureBox", "-0.2")]
    [InlineData("TemperatureIncBox", "Infinity")]
    [InlineData("EntropyThresholdBox", "nonsense")]
    [InlineData("LogProbThresholdBox", "0.1")]
    [InlineData("GpuDeviceBox", "-1")]
    [InlineData("AsrChunkSizeBox", "0")]
    [InlineData("AsrChunkSecondsBox", "")]
    public async Task Invalid_extended_input_keeps_file_live_and_raw_editor_unchanged(string name, string invalid)
    {
        using TempDir temp = new(); Backend backend = new(); using RecognitionActions actions = new(backend);
        RecognitionSettingsStore store = new(Path.Combine(temp.Path, "recognition.json")); store.Save(new());
        string before = File.ReadAllText(Path.Combine(temp.Path, "recognition.json"));
        RecognitionViewModel model = new(new(), actions, store) { ModelDirectory = temp.Path };
        RecognitionWindow window = new(model); window.Show();
        try
        {
            window.FindControl<TextBox>(name)!.Text = invalid; Dispatcher.UIThread.RunJobs();
            await model.SaveCommand.ExecuteAsync(null);
            Assert.True(window.IsVisible); Assert.Null(backend.Applied); Assert.NotEmpty(model.Message);
            Assert.Equal(invalid, window.FindControl<TextBox>(name)!.Text);
            Assert.Equal(before, File.ReadAllText(Path.Combine(temp.Path, "recognition.json")));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Presets_copy_previews_runtime_order_and_all_model_manager_callbacks_are_explicit()
    {
        using TempDir temp = new(); using RecognitionActions actions = new(new Backend());
        List<string> copies = []; List<ModelKind> managed = [];
        RecognitionViewModel model = new(new(), actions, new(Path.Combine(temp.Path, "recognition.json")),
            copyText: copies.Add, manageResource: kind => { managed.Add(kind); return Task.CompletedTask; });
        Assert.Empty(copies); Assert.Empty(managed);
        model.SelectedPreset = "Use CPU device"; Assert.Equal("--device cpu", model.Draft.FasterWhisper.ExtraArguments);
        model.SelectedPromptPreset = "Chinese Traditional, with punctuation"; Assert.Equal("以下是普通話的句子。", model.Draft.WhisperCpp.Prompt);
        model.CopyHelpCommand.Execute(null); model.CopyDebugCommand.Execute(null);
        Assert.Equal(2, copies.Count); Assert.Contains("--help", copies[0]); Assert.Contains("audio.wav", copies[1]);
        model.SelectedRuntime = RuntimeLibrary.CpuNoAvx; model.MoveRuntimeUpCommand.Execute(null);
        Assert.Equal(new[] { RuntimeLibrary.CpuNoAvx, RuntimeLibrary.Cpu }, model.RuntimeLibraries);
        model.RemoveRuntimeCommand.Execute(null); Assert.Equal(new[] { RuntimeLibrary.Cpu }, model.RuntimeLibraries);
        model.AvailableRuntime = RuntimeLibrary.CpuNoAvx; model.AddRuntimeCommand.Execute(null);
        Assert.Equal(new[] { RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx }, model.RuntimeLibraries);
        foreach (ModelKind kind in Enum.GetValues<ModelKind>()) await model.ManageModelsCommand.ExecuteAsync(kind);
        Assert.Equal(Enum.GetValues<ModelKind>(), managed);
    }

    [AvaloniaFact]
    public async Task Full_WPF_runtime_preference_catalog_can_reorder_and_save_an_optional_runtime_without_changing_defaults()
    {
        using TempDir temp = new(); Backend backend = new(); using RecognitionActions actions = new(backend);
        RecognitionSettingsStore store = new(Path.Combine(temp.Path, "recognition.json"));
        RecognitionSettings original = new();
        RecognitionViewModel model = new(original, actions, store) { ModelDirectory = temp.Path };
        RuntimeLibrary[] expectedCatalog = Enum.GetValues<RuntimeLibrary>().Where(runtime => runtime != RuntimeLibrary.CoreML).ToArray();
        Assert.Equal(expectedCatalog, model.AvailableRuntimes);
        RuntimeLibrary optionalRuntime = expectedCatalog.First(runtime => runtime is not RuntimeLibrary.Cpu and not RuntimeLibrary.CpuNoAvx);
        model.AvailableRuntime = optionalRuntime;
        model.AddRuntimeCommand.Execute(null);
        model.SelectedRuntime = optionalRuntime;
        model.MoveRuntimeUpCommand.Execute(null);
        model.MoveRuntimeUpCommand.Execute(null);
        RuntimeLibrary[] expectedOrder = [optionalRuntime, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
        Assert.Equal(expectedOrder, model.RuntimeLibraries);
        Assert.Equal(new[] { RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx }, original.WhisperCpp.RuntimeLibraries);
        Assert.Null(backend.Applied);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(backend.Applied);
        Assert.Equal(expectedOrder, store.Load()!.WhisperCpp.RuntimeLibraries);
        Assert.Equal(expectedOrder, backend.Applied!.WhisperCpp.RuntimeLibraries);
        Assert.Equal(new[] { RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx }, original.WhisperCpp.RuntimeLibraries);
        Assert.Contains("GPU runtimes/devices are not supplied", model.RuntimeAvailability);
    }

    [Theory]
    [InlineData("en", null, "en")]
    [InlineData("en", "en-GB", "en-GB")]
    [InlineData("en", "en-AU", null)]
    [InlineData("en", "ru-RU", null)]
    [InlineData("zh", null, null)]
    public void Microsoft_region_selection_never_substitutes_an_unavailable_preference(string iso, string? preferred, string? expected)
        => Assert.Equal(expected, MicrosoftOcrLanguagePolicy.SelectTag(iso, preferred, ["en-US", "en-GB", "en", "ru-RU"]));

    [Fact]
    public void Microsoft_text_format_preserves_lines_and_removes_CJK_word_spaces()
    {
        (string Text, IReadOnlyList<string> Words)[] lines = [("你 好", ["你", "好"]), ("世 界", ["世", "界"])];
        Assert.Equal("你好" + Environment.NewLine + "世界", MicrosoftOcrLanguagePolicy.FormatLines("zh-Hans", lines));
        Assert.Equal("你 好" + Environment.NewLine + "世 界", MicrosoftOcrLanguagePolicy.FormatLines("en-US", lines));
    }

    sealed class Backend : IRecognitionBackend
    {
        public RecognitionSettings? Applied;
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused => false;
        public void Start(int slot) { }
        public void Pause() { }
        public void Resume() { }
        public void Cancel(int slot) { }
        public void Ocr(int slot) { }
        public void Apply(RecognitionSettings settings) { Applied = settings.Clone(); }
    }
}

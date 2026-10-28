using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Views;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class SubtitleExportTests
{
    static Player CreatePlayer()
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpeg), "Real subtitle export adapter requires existing FFmpeg shared libraries.");
        var dispatcher = new AvaloniaUIDispatcher();
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), new CollectionSyncBridge(dispatcher), new KeyMapper());
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(Path.GetTempPath(), Path.GetTempPath())));
        return new Player(EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize()));
    }

    static SubtitleData Cue(string text, string? translated = null, bool showTranslated = true) => new()
    {
        Text = text, TranslatedText = translated, EnabledTranslated = showTranslated,
        StartTime = TimeSpan.FromMilliseconds(1234), EndTime = TimeSpan.FromMilliseconds(4567),
        SubStyles = [new(SubStyles.ITALIC, 0, text.Length)]
    };

    [AvaloniaFact]
    public void Real_source_captures_selected_track_timing_and_owned_italic_styles_without_playlist_metadata()
    {
        using var player = CreatePlayer();
        player.SubtitlesManager[0].Add(Cue("primary"));
        var cue = Cue("secondary"); player.SubtitlesManager[1].Add(cue);
        var source = new FlyleafSubtitleExportSource(player);
        var snapshot = source.Capture(1, SubtitleExportText.Original);
        cue.Text = "changed"; cue.StartTime = TimeSpan.Zero; cue.SubStyles!.Clear();
        player.SubtitlesManager[1].Clear();
        Assert.Equal("LLPlayer", snapshot.SuggestedBaseName);
        Assert.Null(snapshot.InitialDirectory);
        Assert.Equal("1\r\n00:00:01,234 --> 00:00:04,567\r\n<i>secondary</i>\r\n", SubtitleExporter.Build(snapshot.Lines, SubtitleExportFormat.Srt));
    }

    [AvaloniaFact]
    public void Real_source_preserves_Wpf_translated_mode_display_semantics_and_omits_original_style_offsets()
    {
        using var player = CreatePlayer();
        player.SubtitlesManager[0].Add(Cue("visible", "shown translation"));
        player.SubtitlesManager[0].Add(Cue("hidden original", "hidden translation", false));
        player.SubtitlesManager[0].Add(Cue("fallback"));
        player.SubtitlesManager[0].Add(new() { IsBitmap = true });
        var source = new FlyleafSubtitleExportSource(player);
        var translated = source.Capture(0, SubtitleExportText.TranslatedOnly);
        Assert.Equal(new[] { "shown translation", "hidden original" }, translated.Lines.Select(line => line.Text));
        Assert.All(translated.Lines, line => Assert.Null(line.Styles));
        var fallback = source.Capture(0, SubtitleExportText.TranslatedWithFallback);
        Assert.Equal(new[] { "shown translation", "hidden original", "fallback" }, fallback.Lines.Select(line => line.Text));
        Assert.All(fallback.Lines, line => Assert.Null(line.Styles));
        Assert.Equal(new[] { "visible", "hidden original", "fallback" }, source.Capture(0, SubtitleExportText.Original).Lines.Select(line => line.Text));
    }

    [Theory]
    [InlineData(SubtitleExportFormat.Srt, "movie.srt", "1\r\n00:00:01,234 --> 00:00:04,567\r\n<i>Hello</i>\r\n")]
    [InlineData(SubtitleExportFormat.Vtt, "movie.vtt", "WEBVTT\r\n\r\n00:00:01.234 --> 00:00:04.567\r\n<i>Hello</i>\r\n")]
    [InlineData(SubtitleExportFormat.Txt, "movie.txt", "Hello")]
    public async Task Export_uses_canonical_serializer_and_only_explicit_destination(SubtitleExportFormat format, string suggested, string expected)
    {
        using TempDir dir = new(); string path = Path.Combine(dir.Path, "chosen.output");
        var vm = new SubtitlesExportViewModel(new Source()) { SelectedFormat = format, IsUtf8Bom = false };
        SubtitleExportRequest? request = null; string? revealed = null;
        vm.PickExportPath = value => { request = value; return Task.FromResult<string?>(path); };
        vm.RevealExport = value => { revealed = value; return Task.CompletedTask; };
        await vm.ExportAsync();
        Assert.Equal(expected, File.ReadAllText(path));
        Assert.Equal(suggested, request!.SuggestedFileName); Assert.Equal(format, request.Format);
        Assert.Equal(path, revealed); Assert.Single(Directory.GetFiles(dir.Path));
        Assert.False(vm.Busy); await vm.DisposeAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bom_option_controls_actual_file_bytes(bool bom)
    {
        using TempDir dir = new(); string path = Path.Combine(dir.Path, "output.txt");
        var vm = new SubtitlesExportViewModel(new Source()) { SelectedFormat = SubtitleExportFormat.Txt, IsUtf8Bom = bom };
        vm.PickExportPath = _ => Task.FromResult<string?>(path);
        await vm.ExportAsync();
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(bom, bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal("Hello", File.ReadAllText(path)); await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Picker_await_cannot_change_track_format_translation_timing_styles_or_encoding_of_captured_export()
    {
        using TempDir dir = new(); using var player = CreatePlayer(); string path = Path.Combine(dir.Path, "chosen.vtt");
        var cue = Cue("secondary"); player.SubtitlesManager[1].Add(cue);
        var picker = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new SubtitlesExportViewModel(new FlyleafSubtitleExportSource(player)) { SelectedSlot = 1, SelectedFormat = SubtitleExportFormat.Vtt };
        SubtitleExportRequest? request = null;
        vm.PickExportPath = value => { request = value; return picker.Task; };
        Task operation = vm.ExportAsync(); Assert.True(vm.Busy);
        vm.SelectedSlot = 0; vm.SelectedFormat = SubtitleExportFormat.Txt; vm.SelectedText = SubtitleExportText.TranslatedOnly; vm.IsUtf8Bom = false;
        cue.Text = "mutated"; cue.StartTime = TimeSpan.Zero; cue.SubStyles!.Clear(); player.SubtitlesManager[1].Clear();
        picker.SetResult(path); await operation;
        Assert.Equal(SubtitleExportFormat.Vtt, request!.Format);
        Assert.Equal("WEBVTT\r\n\r\n00:00:01.234 --> 00:00:04.567\r\n<i>secondary</i>\r\n", File.ReadAllText(path));
        Assert.True((await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task Picker_cancel_preserves_existing_file_and_never_reveals()
    {
        using TempDir dir = new(); string path = Path.Combine(dir.Path, "keep.srt"); File.WriteAllText(path, "existing");
        var vm = new SubtitlesExportViewModel(new Source()); int revealed = 0;
        vm.PickExportPath = _ => Task.FromResult<string?>(null);
        vm.RevealExport = _ => { revealed++; return Task.CompletedTask; };
        await vm.ExportAsync();
        Assert.Equal("existing", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(dir.Path)); Assert.Equal(0, revealed);
        Assert.Contains("cancel", vm.Message, StringComparison.OrdinalIgnoreCase); await vm.DisposeAsync();
    }

    [Fact]
    public async Task Empty_export_is_known_error_before_picker_and_partial_snapshot_is_visible()
    {
        var vm = new SubtitlesExportViewModel(new Source { Empty = true, Loading = true }); int picks = 0; string? error = null;
        vm.PickExportPath = _ => { picks++; return Task.FromResult<string?>("must-not-write"); };
        vm.ShowKnownError = value => { error = value; return Task.CompletedTask; };
        await vm.ExportAsync();
        Assert.Equal(0, picks); Assert.Contains("no subtitles", error!, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.IsPartial); Assert.False(vm.Busy); await vm.DisposeAsync();
    }

    [Fact]
    public async Task Failed_destination_keeps_existing_content_reports_known_error_and_leaves_no_staging_file()
    {
        using TempDir dir = new(); string blocked = Path.Combine(dir.Path, "existing"); Directory.CreateDirectory(blocked);
        string keep = Path.Combine(blocked, "keep.txt"); File.WriteAllText(keep, "keep");
        var vm = new SubtitlesExportViewModel(new Source()); string? error = null; int reveals = 0;
        vm.PickExportPath = _ => Task.FromResult<string?>(blocked);
        vm.ShowKnownError = value => { error = value; return Task.CompletedTask; };
        vm.RevealExport = _ => { reveals++; return Task.CompletedTask; };
        await vm.ExportAsync();
        Assert.NotNull(error); Assert.Equal("keep", File.ReadAllText(keep)); Assert.Empty(Directory.GetFiles(dir.Path)); Assert.Equal(0, reveals);
        Assert.False(vm.Busy); await vm.DisposeAsync();
    }

    [Fact]
    public async Task Closing_while_picker_is_open_drains_then_rejects_late_save_without_file_or_reveal()
    {
        using TempDir dir = new(); string path = Path.Combine(dir.Path, "late.srt");
        var picker = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new SubtitlesExportViewModel(new Source()); int revealed = 0;
        vm.PickExportPath = _ => picker.Task;
        vm.RevealExport = _ => { revealed++; return Task.CompletedTask; };
        Task export = vm.ExportAsync(); Task close = vm.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted); picker.SetResult(path); await close; await export;
        Assert.Empty(Directory.GetFiles(dir.Path)); Assert.Equal(0, revealed); Assert.False(vm.Busy);
        await vm.ExportAsync(); Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task Confirmed_existing_destination_is_replaced_atomically_and_reveal_failure_does_not_erase_saved_output()
    {
        using TempDir dir = new(); string path = Path.Combine(dir.Path, "chosen.txt"); File.WriteAllText(path, "old content");
        var vm = new SubtitlesExportViewModel(new Source()) { SelectedFormat = SubtitleExportFormat.Txt };
        string? error = null;
        vm.PickExportPath = _ => Task.FromResult<string?>(path);
        vm.RevealExport = _ => throw new IOException("No file manager");
        vm.ShowKnownError = value => { error = value; return Task.CompletedTask; };
        await vm.ExportAsync();
        Assert.Equal("Hello", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(dir.Path));
        Assert.Contains("were saved", error!, StringComparison.OrdinalIgnoreCase); await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Shared_window_controls_bind_both_slots_text_format_bom_and_export_command()
    {
        using TempDir dir = new(); string path = Path.Combine(dir.Path, "chosen.txt");
        var vm = new SubtitlesExportViewModel(new Source { Loading = true });
        var window = new SubtitlesExportWindow(vm);
        SubtitleExportRequest? request = null;
        vm.PickExportPath = value => { request = value; return Task.FromResult<string?>(path); };
        vm.RevealExport = _ => Task.CompletedTask;
        vm.ShowKnownError = value => throw new Xunit.Sdk.XunitException(value);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<TextBlock>("ExportPartialWarning")!.IsVisible);
            Assert.True(window.FindControl<CheckBox>("ExportBom")!.IsChecked);
            window.FindControl<ComboBox>("ExportSlot")!.SelectedIndex = 1;
            window.FindControl<ComboBox>("ExportText")!.SelectedIndex = 2;
            window.FindControl<ComboBox>("ExportFormat")!.SelectedIndex = 2;
            window.FindControl<CheckBox>("ExportBom")!.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, vm.SelectedSlot); Assert.Equal(SubtitleExportText.TranslatedWithFallback, vm.SelectedText);
            Assert.Equal(SubtitleExportFormat.Txt, vm.SelectedFormat); Assert.False(vm.IsUtf8Bom);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(window.FindControl<Button>("ExportButton")!.Command);
            await command.ExecuteAsync(null);
            Assert.Equal("movie.translated.txt", request!.SuggestedFileName); Assert.Equal("Hello", File.ReadAllText(path));
            Assert.Contains("ASR/OCR", vm.Message);
        }
        finally { await vm.DisposeAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_shared_export_window_cancels_and_drains_pending_picker_before_closing()
    {
        using TempDir dir = new(); string path = Path.Combine(dir.Path, "late.srt");
        var picker = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new SubtitlesExportViewModel(new Source()); var window = new SubtitlesExportWindow(vm);
        vm.PickExportPath = _ => picker.Task;
        vm.RevealExport = _ => throw new Xunit.Sdk.XunitException("Cancelled export must not open a file manager.");
        window.Show(); Dispatcher.UIThread.RunJobs();
        Task export = vm.ExportAsync(); window.Close();
        Assert.True(window.IsVisible); Assert.True(vm.Busy);
        picker.SetResult(path); await export;
        for (int i = 0; i < 100 && window.IsVisible; i++)
        {
            Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.False(window.IsVisible); Assert.False(vm.Busy); Assert.Empty(Directory.GetFiles(dir.Path));
        await vm.DisposeAsync();
    }
    sealed class Source : ISubtitleExportSource
    {
        public bool Empty { get; set; }
        public bool Loading { get; set; }
        public SubtitleExportSnapshot Capture(int slot, SubtitleExportText text) => new(
            Empty ? [] : [new(TimeSpan.FromMilliseconds(1234), TimeSpan.FromMilliseconds(4567), "Hello", [new(SubStyles.ITALIC, 0, 5)])],
            "movie", null, Loading);
    }
}
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public sealed class OnlinePlaylistSettingsTests
{
    [Fact]
    public void DefaultsAndOldSettingsKeepTheOriginalVideoPreferences()
    {
        using TempDir state = new();
        string path = Path.Combine(state.Path, "online.json");
        File.WriteAllText(path, "{\"YtDlpPath\":\"\",\"DenoPath\":\"\"}");
        var settings = new OnlineVideoSettingsStore(path).Load();
        Assert.Equal(720, settings.MaxVideoHeight);
        Assert.False(settings.PreferVideoWithAudio);
        Assert.Equal("", settings.ExtraArguments);
    }

    [Fact]
    public void EveryActualPluginOptionRoundTripsAndCloneIsDetached()
    {
        using TempDir state = new();
        var store = new OnlineVideoSettingsStore(Path.Combine(state.Path, "online.json"));
        OnlineVideoSettings settings = new() { ExtraArguments = "--cookies-from-browser firefox --playlist-items 2,4", MaxVideoHeight = 1080, PreferVideoWithAudio = true };
        store.Save(settings);
        OnlineVideoSettings loaded = store.Load();
        Assert.Null(store.LoadWarning);
        Assert.Equal(settings.ExtraArguments, loaded.ExtraArguments);
        Assert.Equal(1080, loaded.MaxVideoHeight);
        Assert.True(loaded.PreferVideoWithAudio);
        OnlineVideoSettings clone = loaded.Clone(); clone.MaxVideoHeight = 480;
        Assert.Equal(1080, loaded.MaxVideoHeight);
    }

    [Fact]
    public async Task ToolPathSavePreservesOptions_AndOptionsSavePreservesPaths()
    {
        OnlineVideoSettings? saved = null;
        OnlineVideoSettings settings = new() { ExtraArguments = "--playlist-items 1:3", MaxVideoHeight = 1080, PreferVideoWithAudio = true };
        var online = new OnlineVideoViewModel(settings, draft => { saved = draft; return Task.CompletedTask; }, _ => { });
        await online.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        Assert.Equal(settings.ExtraArguments, saved.ExtraArguments);
        Assert.Equal(1080, saved.MaxVideoHeight);
        Assert.True(saved.PreferVideoWithAudio);
        var plugin = new PluginOptionsViewModel(settings, draft => { saved = draft; return Task.CompletedTask; })
        { MaxVideoHeight = "480", ExtraArguments = "--proxy http://127.0.0.1:3128", PreferVideoWithAudio = false };
        await plugin.SaveCommand.ExecuteAsync(null);
        Assert.Equal(480, saved.MaxVideoHeight);
        Assert.Equal(settings.YtDlpPath, saved.YtDlpPath);
        Assert.Equal(settings.DenoPath, saved.DenoPath);
        Assert.Equal("--proxy http://127.0.0.1:3128", saved.ExtraArguments);
    }

    [Theory]
    [InlineData("-1", "")]
    [InlineData("32769", "")]
    [InlineData("bad", "")]
    [InlineData("720", "--exec bad")]
    [InlineData("720", "--config-locations private")]
    public async Task InvalidDraftIsVisibleAndNeverCommitted(string height, string extra)
    {
        int commits = 0;
        PluginOptionsViewModel model = new(new(), _ => { commits++; return Task.CompletedTask; })
        { MaxVideoHeight = height, ExtraArguments = extra };
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, commits);
        Assert.False(string.IsNullOrWhiteSpace(model.Message));
        Assert.False(model.Busy);
    }

    [AvaloniaFact]
    public async Task PluginDialogMasksSensitiveArgumentsAndSuccessfulSaveCloses()
    {
        PluginOptionsViewModel model = new(new() { ExtraArguments = "--password secret" }, _ => Task.CompletedTask);
        PluginOptionsWindow window = new(model);
        window.Show();
        Assert.NotEqual('\0', window.FindControl<TextBox>("ExtraArgumentsBox")!.PasswordChar);
        Assert.True(window.IsVisible);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.False(window.IsVisible);
    }
}

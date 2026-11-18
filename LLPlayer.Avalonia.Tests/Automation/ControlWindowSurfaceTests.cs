using System.Text.Json;
using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.Services.Automation;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests.Automation;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class ControlWindowSurfaceTests
{
    [Fact]
    public void Surface_operations_are_registered_for_the_target_gui()
    {
        var schemas = ControlGuiOperations.All;
        Assert.Contains(schemas, row => row.Operation.Id == "ui.surface.list"
            && row.Operation.Context == ControlOperationContext.Gui && !row.Operation.Mutates);
        Assert.Contains(schemas, row => row.Operation.Id == "ui.surface.open"
            && row.Operation.Context == ControlOperationContext.Gui
            && row.Operation.Completion == ControlOperationCompletion.Job && row.Operation.Mutates);
    }

    [AvaloniaFact]
    public async Task Surface_open_is_idempotent_and_debug_alias_observes_the_same_window()
    {
        using TempDir state = new();
        using var player = PlaybackControlOpenTests.CreatePlayer(state, out AvaloniaUIDispatcher dispatcher, out CollectionSyncBridge bridge);
        await using var controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        var window = new MainWindow(new KeyMapper(), new AvaloniaHostServices(() => null));
        await using var translator = new FlyleafWordTranslator(player, new TranslationSettings());
        string prefsPath = Path.Combine(state.Path, "window-preferences.json");
        PrefsStore prefsStore = new(prefsPath);
        AppPrefs lastUiPrefs = prefsStore.Load();
        AppPrefs? saved = null;
        bool failSave = false;
        using var model = new MainWindowViewModel(controller, lastUiPrefs.Clone(), prefs =>
        {
            if (failSave) throw new IOException("Test save failure.");
            prefsStore.SaveEdits(lastUiPrefs, prefs);
            lastUiPrefs = prefs.Clone();
            saved = prefs;
        }, translator, window);
        window.Attach(model, null);
        window.Show();
        var executor = new ControlGuiExecutor(controller, model, window);
        try
        {
            Assert.Equal("invalid_arguments", (await Invoke("ui.surface.open", new { })).Error?.Code);
            Assert.Equal("invalid_arguments", (await Invoke("ui.surface.open", new { surface = 0 })).Error?.Code);
            Assert.Equal("invalid_arguments", (await Invoke("ui.surface.open", new { surface = "madeUp" })).Error?.Code);
            Assert.False((await Status("wordManager")).GetProperty("available").GetBoolean());
            Assert.Equal("gui_unavailable", (await Invoke("ui.surface.open", new { surface = "wordManager" })).Error?.Code);

            Assert.False((await Status("commandPalette")).GetProperty("visible").GetBoolean());
            JsonElement palette = await Run("ui.surface.open", new { surface = "commandPalette" });
            Assert.True(palette.GetProperty("visible").GetBoolean());
            var first = Assert.Single(window.OwnedWindows.OfType<CommandPaletteWindow>());
            Assert.True((await Run("ui.surface.open", new { surface = "commandPalette" })).GetProperty("visible").GetBoolean());
            Assert.Same(first, Assert.Single(window.OwnedWindows.OfType<CommandPaletteWindow>()));

            Assert.True((await Run("commands.execute", new { id = "app:" + AppShortcutMap.ToggleDebugOverlay }))
                .GetProperty("surface").GetProperty("visible").GetBoolean());
            Assert.True(Assert.Single(window.OwnedWindows.OfType<PlayerDebugWindow>()).IsVisible);
            Assert.False((await Run("commands.execute", new { id = "app:" + AppShortcutMap.ToggleDebugOverlay }))
                .GetProperty("surface").GetProperty("visible").GetBoolean());
            Assert.Empty(window.OwnedWindows.OfType<PlayerDebugWindow>());

            Assert.True((await Run("commands.execute", new { id = "app:" + AppShortcutMap.OpenWindowCheatSheet }))
                .GetProperty("surface").GetProperty("visible").GetBoolean());
            Assert.True(Assert.Single(window.OwnedWindows.OfType<CheatSheetWindow>()).IsVisible);
            Assert.True((await Run("ui.surface.open", new { surface = "settings" })).GetProperty("visible").GetBoolean());
            Assert.False((await Status("about")).GetProperty("available").GetBoolean());
            Assert.Equal("gui_unavailable", (await Invoke("ui.surface.open", new { surface = "about" })).Error?.Code);
            Assert.Equal("gui_unavailable", (await Invoke("ui.sidebar.search.focus", new { })).Error?.Code);
            Assert.Empty(window.OwnedWindows.OfType<AboutWindow>());
            Assert.Single(window.OwnedWindows.OfType<SettingsWindow>()).Close();
            Assert.True((await Run("ui.surface.open", new { surface = "about" })).GetProperty("visible").GetBoolean());
            Assert.True(Assert.Single(window.OwnedWindows.OfType<AboutWindow>()).IsVisible);

            JsonElement focused = await Run("ui.sidebar.search.focus", new { });
            Assert.True(focused.GetProperty("sidebar").GetBoolean());
            Assert.True(focused.GetProperty("sidebarSearchFocused").GetBoolean());
            Assert.True((await Run("commands.execute", new { id = "app:" + AppShortcutMap.ActivateSubsSearch }))
                .GetProperty("window").GetProperty("sidebarSearchFocused").GetBoolean());

            model.ShowSessionTheme("Light");
            JsonElement topmostOnly = await Run("ui.set", new { alwaysOnTop = true });
            Assert.Equal("light", topmostOnly.GetProperty("theme").GetString());
            Assert.Equal("Dark", saved?.Theme);
            JsonElement ui = await Run("ui.set", new { alwaysOnTop = true, theme = "light" });
            Assert.True(ui.GetProperty("alwaysOnTop").GetBoolean());
            Assert.True(window.Topmost);
            Assert.Equal("light", ui.GetProperty("theme").GetString());
            Assert.Equal("Light", saved?.Theme);
            Assert.True(saved?.AlwaysOnTop);

            PrefsStore otherWindow = new(prefsPath);
            AppPrefs newer = otherWindow.Load();
            newer.AlwaysOnTop = false;
            newer.Theme = "Dark";
            newer.Volume = 23;
            otherWindow.Save(newer);
            await Run("ui.set", new { alwaysOnTop = true });
            AppPrefs reread = otherWindow.Load();
            Assert.True(reread.AlwaysOnTop);
            Assert.Equal("Dark", reread.Theme);
            Assert.Equal(23, reread.Volume);

            reread.AlwaysOnTop = false;
            otherWindow.Save(reread);
            await Run("ui.set", new { theme = "light" });
            reread = otherWindow.Load();
            Assert.Equal("Light", reread.Theme);
            Assert.False(reread.AlwaysOnTop);
            Assert.Equal(23, reread.Volume);

            failSave = true;
            Assert.Equal("file_io_failed", (await Invoke("ui.set", new { alwaysOnTop = false, theme = "dark",
                fullscreen = true, sidebar = false })).Error?.Code);
            Assert.True(JsonElement.DeepEquals(ui, await Run("ui.get", new { })));

            var keys = player.Config.Player.KeyBindings.Keys;
            foreach (var bound in keys.Where(binding => binding.Action == KeyBindingAction.ToggleMute).ToArray()) keys.Remove(bound);
            Assert.DoesNotContain(model.BuildExecutableCommands(), command => command.Id == "engine:ToggleMute");
            JsonElement mute = (await Run("commands.list", new { })).EnumerateArray()
                .Single(value => value.GetProperty("id").GetString() == "engine:ToggleMute");
            Assert.Equal(0, mute.GetProperty("keys").GetArrayLength());
            Assert.True(mute.GetProperty("automationSupported").GetBoolean());
            Assert.True((await Run("commands.execute", new { id = "engine:ToggleMute" }))
                .GetProperty("playback").GetProperty("mute").GetBoolean());
        }
        finally
        {
            foreach (var child in window.OwnedWindows.ToArray()) child.Close();
            window.Close();
        }

        async Task<JsonElement> Status(string surface) => (await Run("ui.surface.list", new { })).EnumerateArray()
            .Single(value => value.GetProperty("surface").GetString() == surface);

        async Task<JsonElement> Run(string operation, object arguments)
        {
            ControlResult result = await Invoke(operation, arguments);
            Assert.Null(result.Error);
            return result.Value!.Value;
        }

        Task<ControlResult> Invoke(string operation, object arguments) => executor.ExecuteAsync(new(Guid.NewGuid(), operation,
            new(ControlProtocol.DefaultProfileId, Guid.NewGuid()), null, ControlProtocol.Encode(arguments)),
            TestContext.Current.CancellationToken);
    }
}

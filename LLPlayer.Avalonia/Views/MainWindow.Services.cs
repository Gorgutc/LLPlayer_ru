using Avalonia.Controls;
using FlyleafLib.MediaFramework.MediaDevice;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class MainWindow
{
    CommandPaletteWindow? commandPalette;
    AboutWindow? aboutWindow;
    PlayerDebugWindow? playerDebugWindow;
    readonly Dictionary<CaptureDeviceKind, CaptureDevicesWindow> captureWindows = [];

    public void ShowCommandPalette()
    {
        vm?.CancelAppShortcuts();
        if (commandPalette != null) { commandPalette.Activate(); return; }
        if (vm == null) return;
        commandPalette = new(new CommandPaletteViewModel(vm.BuildExecutableCommands()));
        OwnKeyboardSurface(commandPalette);
        commandPalette.Closed += (_, _) => commandPalette = null;
        if (IsVisible) commandPalette.Show(this); else commandPalette.Show();
    }

    public void ShowAbout()
    {
        vm?.CancelAppShortcuts();
        if (aboutWindow != null) { aboutWindow.Activate(); return; }
        aboutWindow = new(new AboutViewModel(AboutInformation.FromAssembly(typeof(App).Assembly), SetClipboardText, OpenBrowserAsync));
        OwnKeyboardSurface(aboutWindow);
        aboutWindow.Closed += (_, _) => aboutWindow = null;
        if (IsVisible) aboutWindow.Show(this); else aboutWindow.Show();
    }

    public void TogglePlayerDebug()
    {
        vm?.CancelAppShortcuts();
        if (playerDebugWindow != null) { playerDebugWindow.Close(); return; }
        if (vm == null) return;
        playerDebugWindow = new(new PlayerDebugViewModel(vm.Player.GetDebugSnapshot()));
        OwnKeyboardSurface(playerDebugWindow);
        playerDebugWindow.Closed += (_, _) => playerDebugWindow = null;
        if (IsVisible) playerDebugWindow.Show(this); else playerDebugWindow.Show();
    }

    public void ShowCaptureDevices(CaptureDevicesViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (captureWindows.TryGetValue(model.Kind, out CaptureDevicesWindow? current)) { current.Activate(); return; }
        CaptureDevicesWindow window = new(model);
        captureWindows.Add(model.Kind, window);
        OwnKeyboardSurface(window);
        window.Closed += (_, _) => captureWindows.Remove(model.Kind);
        if (IsVisible) window.Show(this); else window.Show();
    }

    void RefreshExecutableViewers()
    {
        if (vm == null) return;
        IReadOnlyList<ExecutableCommand> rows = vm.BuildExecutableCommands();
        if (commandPalette?.DataContext is CommandPaletteViewModel palette) palette.Refresh(rows);
        if (cheatSheet?.DataContext is CheatSheetViewModel help) help.Refresh(rows, vm.Prefs);
    }

    void RefreshServiceWindows()
    {
        vm?.RefreshServiceCommandState();
        if (commandPalette?.DataContext is CommandPaletteViewModel palette) palette.RefreshAvailability();
        if (cheatSheet?.DataContext is CheatSheetViewModel help) help.RefreshAvailability();
        if (playerDebugWindow?.DataContext is PlayerDebugViewModel debug && vm != null) debug.Refresh(vm.Player.GetDebugSnapshot());
    }

    async Task DrainServiceWindowsAsync()
    {
        foreach (CaptureDevicesWindow window in captureWindows.Values.ToArray())
        {
            if (window.DataContext is not CaptureDevicesViewModel model) continue;
            window.IsEnabled = false;
            model.RequestClose();
            await Task.WhenAll(model.RefreshCommand.ExecutionTask ?? Task.CompletedTask,
                model.QueryFormatsCommand.ExecutionTask ?? Task.CompletedTask, model.OpenCommand.ExecutionTask ?? Task.CompletedTask);
        }
        CloseServiceWindows();
    }

    void CloseServiceWindows()
    {
        commandPalette?.Close(); aboutWindow?.Close(); playerDebugWindow?.Close();
        foreach (CaptureDevicesWindow window in captureWindows.Values.ToArray()) window.Close();
    }
}

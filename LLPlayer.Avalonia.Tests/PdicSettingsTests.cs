using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public sealed class PdicSettingsTests
{
    [AvaloniaFact]
    public async Task LinuxSettingsExplicitlyDisableConfigurationAndNeverProbeOrSave()
    {
        int probes = 0, saves = 0;
        var vm = new PdicSettingsViewModel(new() { ExecutablePath = "C:\\Old\\pdic-pipe.exe" },
            _ => { saves++; return Task.CompletedTask; }, false, () => { probes++; return "C:\\Found\\pdic-pipe.exe"; });
        using PdicWindowLifetime window = new(new PdicSettingsWindow(vm));
        Assert.False(vm.CanConfigure); Assert.Contains("only on Windows", vm.Message);
        Assert.False(vm.AutoSetCommand.CanExecute(null)); Assert.False(vm.SaveCommand.CanExecute(null));
        vm.AutoSetCommand.Execute(null); await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, probes); Assert.Equal(0, saves); Assert.Equal("C:\\Old\\pdic-pipe.exe", vm.ExecutablePath);
    }

    [AvaloniaFact]
    public async Task AutoSetChangesOnlyTheDetachedDraftAndSaveFailureKeepsItForRetry()
    {
        int probes = 0, exists = 0, saves = 0, closed = 0;
        PdicSettings current = new() { ExecutablePath = "C:\\Old\\pdic-pipe.exe" };
        var vm = new PdicSettingsViewModel(current, draft =>
        {
            saves++;
            if (saves == 1) throw new IOException("Denied");
            Assert.Equal("C:\\Found\\pdic-pipe.exe", draft.ExecutablePath); return Task.CompletedTask;
        }, true, () => { probes++; return "C:\\Found\\pdic-pipe.exe"; }, _ => { exists++; return true; });
        vm.Saved += () => closed++;
        Assert.Equal(0, probes); Assert.Equal(0, exists);
        vm.AutoSetCommand.Execute(null);
        Assert.Equal(1, probes); Assert.Equal("C:\\Old\\pdic-pipe.exe", current.ExecutablePath);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, closed); Assert.False(vm.Busy); Assert.Contains("could not be saved", vm.Message);
        Assert.Equal("C:\\Found\\pdic-pipe.exe", vm.ExecutablePath);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, saves); Assert.Equal(2, exists); Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public async Task MissingExecutableAndRelativePathNeverReachTheWriter()
    {
        int saves = 0;
        var vm = new PdicSettingsViewModel(new() { ExecutablePath = "pdic-pipe.exe" },
            _ => { saves++; return Task.CompletedTask; }, true, executableExists: _ => false);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, saves); Assert.Contains("absolute path", vm.Message);
        vm.ExecutablePath = "C:\\Missing\\pdic-pipe.exe";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, saves); Assert.Contains("existing", vm.Message);
        vm.ExecutablePath = "";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void StoreRoundTripDoesNotRequireTheConfiguredDriveAndPreservesMalformedSource()
    {
        string directory = Path.Combine(Path.GetTempPath(), "llplayer-pdic-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "pdic.json");
            var store = new PdicSettingsStore(path);
            Assert.Equal("", store.Load().ExecutablePath);
            store.Save(new() { ExecutablePath = "Z:\\Absent\\pdic-pipe.exe" });
            Assert.Equal("Z:\\Absent\\pdic-pipe.exe", store.Load().ExecutablePath);
            File.WriteAllText(path, "broken-pdic-preferences");
            Assert.Equal("", store.Load().ExecutablePath);
            Assert.NotNull(store.LoadWarning); Assert.Equal("broken-pdic-preferences", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    sealed class PdicWindowLifetime(PdicSettingsWindow window) : IDisposable
    {
        public void Dispose() => window.Close();
    }
}

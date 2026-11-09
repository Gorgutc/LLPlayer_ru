using System.Diagnostics;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia;

public sealed partial class AppHost
{
    /// <summary>The actual process is created only after a user invokes New, Clone or Restart.</summary>
    void ConfigureApplicationProcessCommands(MainWindow window, string ffmpegDirectory)
    {
        if (viewModel == null || controller == null) return;
        List<string> retained = ["--ffmpeg-dir", ffmpegDirectory];
        if (options.Theme is { } theme) { retained.Add("--theme"); retained.Add(theme); }
        if (options.ShowSidebar) retained.Add("--sidebar");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable.");
        string? entryAssembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? typeof(App).Assembly.Location : null;
        ApplicationLaunchContext context = new(executable, Environment.CurrentDirectory, entryAssembly, retained);
        ApplicationProcessService processes = new(plan =>
        {
            using Process? process = Process.Start(plan);
            return process != null;
        }, window.RequestApplicationExitAsync);
        viewModel.LaunchApplication = kind => processes.LaunchAsync(kind, context, controller.Url);
    }
}

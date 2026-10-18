using System.Reflection;
using Avalonia;
using Avalonia.Dialogs;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppOptions options;
        try
        {
            options = AppOptions.Parse(args);
        }
        catch (AppOptionsException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(AppOptions.Usage);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine("LLPlayer (Linux) " + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
            return 0;
        }

        CrashLog.Install(AppPaths.FromEnvironment());

        try
        {
            return BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Main", ex);
            Console.Error.WriteLine("LLPlayer crashed: " + ex.Message + (CrashLog.CurrentPath is { } p ? $" (details in {p})" : ""));
            return 1;
        }
    }

    public const string ManagedDialogsVariable = "LLPLAYER_MANAGED_DIALOGS";

    /// <summary>Linux windowing: X11 (or XWayland) through UsePlatformDetect. Native Wayland is a future opt-in.</summary>
    public static AppBuilder BuildAvaloniaApp(AppOptions options)
    {
        AppBuilder builder = AppBuilder.Configure(() => new App { Options = options })
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "LLPlayer" })
            .WithInterFont()
            .LogToTrace();

        // Native file dialogs come from xdg-desktop-portal (DBus) or GTK 3; minimal systems without either can use
        // Avalonia's built-in (managed) file chooser instead.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable(ManagedDialogsVariable) == "1")
            builder = builder.UseManagedSystemDialogs();

        return builder;
    }

    /// <summary>Designer / previewer entry point (no windows are created by App without options).</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

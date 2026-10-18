using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LLPlayer.Avalonia;

public partial class App : Application
{
    /// <summary>Command line of this run; null for design-time / headless test instances (no windows are created).</summary>
    public AppOptions? Options { get; init; }

    public AppHost? Host { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (Options != null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Host = new AppHost(Options, desktop);
            Host.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

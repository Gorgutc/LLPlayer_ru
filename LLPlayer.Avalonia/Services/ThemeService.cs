using Avalonia;
using Avalonia.Styling;

namespace LLPlayer.Avalonia.Services;

public static class ThemeService
{
    public static ThemeVariant ToVariant(string? theme)
        => string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? ThemeVariant.Light : ThemeVariant.Dark;

    /// <summary>Switches the whole app between the "LLPlayer shadcn" Dark (default) and Light theme dictionaries.</summary>
    public static void Apply(string? theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(theme);
    }
}

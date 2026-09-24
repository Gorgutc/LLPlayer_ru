using Avalonia.Data.Converters;

namespace LLPlayer.Avalonia.Controls;

public static class ViewConverters
{
    /// <summary>true -> 1, false -> 0 (auto-hiding transport bar).</summary>
    public static readonly IValueConverter BoolToOpacity =
        new FuncValueConverter<bool, double>(visible => visible ? 1.0 : 0.0);
}

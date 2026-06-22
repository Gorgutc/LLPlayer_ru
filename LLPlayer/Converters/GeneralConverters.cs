using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LLPlayer.Services;

namespace LLPlayer.Converters;
[ValueConversion(typeof(bool), typeof(bool))]
public class InvertBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BooleanToVisibilityMiscConverter : IValueConverter
{
    public Visibility FalseVisibility { get; set; } = Visibility.Collapsed;
    public bool Invert { get; set; } = false;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (Invert)
        {
            return (bool)value ? FalseVisibility : Visibility.Visible;
        }

        return (bool)value ? Visibility.Visible : FalseVisibility;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(double), typeof(double))]
public class DoubleToPercentageConverter : IValueConverter
{
    // Model → View (0.0–1.0 → 0–100)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return Math.Round(d * 100.0, 0);
        return 0.0;
    }

    // View → Model (0–100 → 0.0–1.0)
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return ToDouble(d);

        if (value is string sd)
        {
            if (double.TryParse(sd, out d))
            {
                if (d < 0)
                    d = 0;
                else if (d > 100)
                    d = 100;
            }

            return ToDouble(d);
        }

        return 0.0;

        static double ToDouble(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, Math.Round(value / 100.0, 2)));
        }
    }
}

[ValueConversion(typeof(Enum), typeof(string))]
public class EnumToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string str;
        try
        {
            str = Enum.GetName(value.GetType(), value)!;
            return str;
        }
        catch
        {
            return string.Empty;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(Enum), typeof(string))]
public class EnumToDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.GetDescription();
        }
        return value.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(Enum), typeof(bool))]
public class EnumToBooleanConverter : IValueConverter
{
    // value, parameter = Enum
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue || parameter is not Enum enumTarget)
            return false;

        return enumValue.Equals(enumTarget);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return parameter;
    }
}

[ValueConversion(typeof(Enum), typeof(Visibility))]
public class EnumToVisibilityConverter : IValueConverter
{
    // value, parameter = Enum
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue || parameter is not Enum enumTarget)
            return Visibility.Collapsed;

        return enumValue.Equals(enumTarget) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(Color), typeof(Brush))]
public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            // Freeze so the brush is immutable and cheap to share on the subtitle Fill/Stroke hot path.
            // All consumers feed it to read-only render props; colour edits rebind a new Color.
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
            return brush.Color;

        return default(Color);
    }
}

[ValueConversion(typeof(TimeSpan), typeof(string))]
public class TimeSpanShortConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TimeSpan span || span <= TimeSpan.Zero)
            return "--:--";

        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", culture)
            : span.ToString(@"m\:ss", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts from System.Windows.Input.Key to human readable string
/// </summary>
[ValueConversion(typeof(Key), typeof(string))]
public class KeyToStringConverter : IValueConverter
{
    public static readonly Dictionary<Key, string> KeyMappings = new()
    {
        { Key.D0, "0" },
        { Key.D1, "1" },
        { Key.D2, "2" },
        { Key.D3, "3" },
        { Key.D4, "4" },
        { Key.D5, "5" },
        { Key.D6, "6" },
        { Key.D7, "7" },
        { Key.D8, "8" },
        { Key.D9, "9" },
        { Key.Prior, "PageUp" },
        { Key.Next, "PageDown" },
        { Key.Return, "Enter" },
        { Key.Oem1, ";" },
        { Key.Oem2, "/" },
        { Key.Oem3, "`" },
        { Key.Oem4, "[" },
        { Key.Oem5, "\\" },
        { Key.Oem6, "]" },
        { Key.Oem7, "'" },
        { Key.OemPlus, "Plus" },
        { Key.OemMinus, "Minus" },
        { Key.OemComma, "," },
        { Key.OemPeriod, "." }
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Key key)
        {
            if (KeyMappings.TryGetValue(key, out var mappedValue))
            {
                return mappedValue;
            }

            return key.ToString();
        }

        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(long), typeof(string))]
public class FileSizeHumanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long size)
        {
            return FormatBytes(size);
        }

        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

[ValueConversion(typeof(int?), typeof(string))]
public class NullableIntConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int intValue ? intValue.ToString() : string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? str = value as string;
        if (string.IsNullOrWhiteSpace(str))
            return null;

        if (int.TryParse(str, out int result))
            return result;

        return null;
    }
}

[ValueConversion(typeof(double), typeof(string))]
[ValueConversion(typeof(int), typeof(string))]
public class HalfConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return d / 2;

        if (value is int n)
            return n / 2;

        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return d * 2;

        if (value is int n)
            return n * 2;

        return value;
    }
}

[ValueConversion(typeof(double), typeof(Visibility))]
public class WidthToVisibilityConverter : IValueConverter
{
    // Collapse the bound element below a width threshold (px) given via ConverterParameter (default 620).
    // Used so the volume slider drops out on a narrow player bar instead of clipping the edge controls.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width)
            return Visibility.Visible;

        double threshold = 620;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
            threshold = parsed;
        else if (parameter is double pd)
            threshold = pd;

        return width >= threshold ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(double), typeof(Visibility))]
public class InverseWidthToVisibilityConverter : IValueConverter
{
    // Visible when the bound width is BELOW the threshold (px) given via ConverterParameter (default 620).
    // The inverse of WidthToVisibilityConverter — used to show an overflow control on a narrow bar.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width)
            return Visibility.Collapsed;

        double threshold = 620;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
            threshold = parsed;
        else if (parameter is double pd)
            threshold = pd;

        return width < threshold ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(Brush), typeof(Brush))]
public class OnColorForegroundConverter : IValueConverter
{
    // Given a background Brush (e.g. the themed Primary), return a black or white foreground brush
    // chosen for legibility by the background's relative luminance. Keeps white-on-Primary chrome
    // readable when a synced OS accent is pale, WITHOUT distorting the accent colour itself.
    private static readonly SolidColorBrush Black = CreateFrozen(Colors.Black);
    private static readonly SolidColorBrush White = CreateFrozen(Colors.White);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SolidColorBrush brush)
            return White;

        Color c = brush.Color;
        // Perceived (BT.601-weighted) luminance, 0..1. Light backgrounds get black text, dark ones white.
        // 0.5 cutoff sits just above the default accent (pink ~0.44, Windows blue ~0.37 -> white) and flips
        // pale/grey/yellow accents to black; ~0.465 is the true neutral-grey crossover.
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance > 0.5 ? Black : White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    private static SolidColorBrush CreateFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

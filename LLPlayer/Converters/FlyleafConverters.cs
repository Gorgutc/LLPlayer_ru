using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using FlyleafLib;
using static FlyleafLib.MediaFramework.MediaDemuxer.Demuxer;

namespace LLPlayer.Converters;

[ValueConversion(typeof(long), typeof(string))]
public class TicksToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ts = TimeSpan.FromTicks((long)value);

        if (ts.TotalHours > 9)
            return ((int)ts.TotalHours) + ts.ToString(@"\:mm\:ss");
        else
            return ts.ToString(@"hh\:mm\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(int), typeof(Qualities))]
public class QualityToLevelsConverter : IValueConverter
{
    public enum Qualities
    {
        None,
        Low,
        Med,
        High,
        _4k
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int videoHeight = (int)value;

        if (videoHeight > 1080)
            return Qualities._4k;
        else if (videoHeight > 720)
            return Qualities.High;
        else if (videoHeight == 720)
            return Qualities.Med;
        else
            return Qualities.Low;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(int), typeof(Volumes))]
public class VolumeToLevelsConverter : IValueConverter
{
    public enum Volumes
    {
        Mute,
        Low,
        Med,
        High
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int volume = (int)value;

        if (volume == 0)
            return Volumes.Mute;
        else if (volume > 99)
            return Volumes.High;
        else if (volume > 49)
            return Volumes.Med;
        else
            return Volumes.Low;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SumConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double sum = 0;

        if (values == null)
            return sum;

        foreach (object value in values)
        {
            if (value == DependencyProperty.UnsetValue)
                continue;
            sum += (double)value;
        }

        return sum;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class PlaylistItemsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => $"Playlist ({values[0]}/{values[1]})";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(Color), typeof(string))]
public class ColorToHexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return null;

        string UpperHexString(int i) => i.ToString("X2").ToUpper();
        Color color = (Color)value;
        string hex = UpperHexString(color.R) +
                      UpperHexString(color.G) +
                      UpperHexString(color.B);

        return hex;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            return ColorConverter.ConvertFromString("#" + value.ToString());
        }
        catch (Exception)
        {
            // ignored
        }

        return Binding.DoNothing;
    }
}

public class Tuple3Converter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return (values[0], values[1], values[2]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SubIndexToIsEnabledConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Any(x => x == DependencyProperty.UnsetValue))
            return DependencyProperty.UnsetValue;

        // values[0] = Tag (index)
        // values[1] = IsPrimaryEnabled
        // values[2] = IsSecondaryEnabled
        int index = int.Parse((string)values[0]);
        bool isPrimaryEnabled = (bool)values[1];
        bool isSecondaryEnabled = (bool)values[2];

        return index == 0 ? isPrimaryEnabled : isSecondaryEnabled;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(IEnumerable<Chapter>), typeof(DoubleCollection))]
public class ChaptersToTicksConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<Chapter> chapters)
        {
            // Convert tick count to seconds
            List<double> secs = chapters.Select(c => c.StartTime / 10000000.0).ToList();
            if (secs.Count <= 1)
            {
                return Binding.DoNothing;
            }

            return new DoubleCollection(secs);
        }

        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(IEnumerable<Chapter>), typeof(TickPlacement))]
public class ChaptersToTickPlacementConverter : IValueConverter
{
    public TickPlacement TickPlacement { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<Chapter> chapters && chapters.Count() >= 2)
        {
            return TickPlacement;
        }

        return TickPlacement.None;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class AspectRatioIsCheckedConverter : IMultiValueConverter
{
    // values[0]: SelectedAspectRatio, values[1]: current AspectRatio
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        if (values[0] is AspectRatio selected && values[1] is AspectRatio current)
        {
            return selected.Equals(current);
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// F-12 A-B repeat: maps a point (ticks) to a Canvas.Left over the seek overlay, matching the slider's
// buffering band convention (fraction * ActualWidth). values[0]=pointTicks (long, -1 = unset),
// values[1]=durationTicks (long), values[2]=overlay ActualWidth (double). Returns a clamped DIP offset.
public class AbMarkerLeftConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values.Any(x => x == DependencyProperty.UnsetValue))
            return 0d;

        long point = System.Convert.ToInt64(values[0]);
        long dur   = System.Convert.ToInt64(values[1]);
        double w   = System.Convert.ToDouble(values[2]);
        if (point < 0 || dur <= 0 || w <= 0)
            return 0d;

        double frac = Math.Clamp(point / (double)dur, 0d, 1d);
        return frac * w;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// F-12 A-B repeat: width (DIP) of the highlighted band between A and B. values[0]=aTicks, values[1]=bTicks,
// values[2]=durationTicks, values[3]=overlay ActualWidth. Returns 0 for an unset/inverted window so the band
// (Rectangle.Width) is never negative.
public class AbBandWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 || values.Any(x => x == DependencyProperty.UnsetValue))
            return 0d;

        long a   = System.Convert.ToInt64(values[0]);
        long b   = System.Convert.ToInt64(values[1]);
        long dur = System.Convert.ToInt64(values[2]);
        double w = System.Convert.ToDouble(values[3]);
        if (a < 0 || b < 0 || dur <= 0 || w <= 0 || b <= a)
            return 0d;

        double fa = Math.Clamp(a / (double)dur, 0d, 1d);
        double fb = Math.Clamp(b / (double)dur, 0d, 1d);
        return Math.Max(0d, (fb - fa) * w);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

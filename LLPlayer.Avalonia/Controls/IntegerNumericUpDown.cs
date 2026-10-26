using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Keeps invalid integer input available for the detached draft's save validation.</summary>
public sealed class IntegerNumericUpDown : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    bool HasValidIntegerText =>
        int.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        && value >= Minimum && value <= Maximum;

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        string? input = Text;
        bool valid = HasValidIntegerText;
        // Keep NumericUpDown's focus bookkeeping, but do not let its formatter repair invalid input.
        base.OnLostFocus(e);
        if (!valid) SetCurrentValue(TextProperty, input);
    }

    protected override void OnSpin(SpinEventArgs e)
    {
        // A fractional Value can otherwise be spun and formatted into a valid-looking integer.
        if (HasValidIntegerText) base.OnSpin(e);
        else e.Handled = true;
    }
}

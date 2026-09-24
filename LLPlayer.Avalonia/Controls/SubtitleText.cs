using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace LLPlayer.Avalonia.Controls;

/// <summary>A word clicked in a <see cref="SubtitleText"/>.</summary>
public sealed class WordClickedEventArgs(RoutedEvent routedEvent, string word, string text, int slot, Point position)
    : RoutedEventArgs(routedEvent)
{
    public string Word { get; } = word;
    /// <summary>The whole subtitle line (context).</summary>
    public string Text { get; } = text;
    public int Slot { get; } = slot;
    /// <summary>Click position relative to the control.</summary>
    public Point Position { get; } = position;
}

/// <summary>
/// Subtitle line drawn over video: centred, wrapped, filled text with a solid outline and a soft drop shadow so it stays
/// readable on any frame. A left click on a word raises <see cref="WordClicked"/> (word hit-test through the same
/// <see cref="TextLayout"/> used for measuring).
/// </summary>
public sealed class SubtitleText : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SubtitleText, string?>(nameof(Text));

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<SubtitleText>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<SubtitleText>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner<SubtitleText>();

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SubtitleText, IBrush?>(nameof(Fill), Brushes.White);

    public static readonly StyledProperty<IBrush?> OutlineProperty =
        AvaloniaProperty.Register<SubtitleText, IBrush?>(nameof(Outline), Brushes.Black);

    public static readonly StyledProperty<double> OutlineThicknessProperty =
        AvaloniaProperty.Register<SubtitleText, double>(nameof(OutlineThickness), 3);

    public static readonly StyledProperty<int> SlotProperty =
        AvaloniaProperty.Register<SubtitleText, int>(nameof(Slot));

    public static readonly RoutedEvent<WordClickedEventArgs> WordClickedEvent =
        RoutedEvent.Register<SubtitleText, WordClickedEventArgs>(nameof(WordClicked), RoutingStrategies.Bubble);

    static readonly IBrush ShadowBrush = new ImmutableSolidColorBrush(Color.FromArgb(110, 0, 0, 0));

    TextLayout? layout;
    Geometry? geometry;
    double layoutWidth = -1;

    static SubtitleText()
    {
        AffectsMeasure<SubtitleText>(TextProperty, FontSizeProperty, FontFamilyProperty, FontWeightProperty, OutlineThicknessProperty);
        AffectsRender<SubtitleText>(FillProperty, OutlineProperty);
        FontWeightProperty.OverrideDefaultValue<SubtitleText>(FontWeight.SemiBold);
    }

    public SubtitleText()
    {
        // Per instance: cursors are platform objects (cannot be created in a static constructor).
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Outline
    {
        get => GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    public double OutlineThickness
    {
        get => GetValue(OutlineThicknessProperty);
        set => SetValue(OutlineThicknessProperty, value);
    }

    /// <summary>0 = primary, 1 = secondary (passed through <see cref="WordClickedEventArgs.Slot"/>).</summary>
    public int Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    public event EventHandler<WordClickedEventArgs>? WordClicked
    {
        add => AddHandler(WordClickedEvent, value);
        remove => RemoveHandler(WordClickedEvent, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FontFamilyProperty
            || change.Property == FontWeightProperty)
        {
            Invalidate();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return default;

        double pad = OutlineThickness;
        double width = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(1, availableSize.Width - 2 * pad);
        TextLayout l = EnsureLayout(width);
        double w = double.IsInfinity(availableSize.Width) ? l.WidthIncludingTrailingWhitespace + 2 * pad : availableSize.Width;
        return new Size(w, l.Height + 2 * pad);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!string.IsNullOrEmpty(Text))
            EnsureLayout(Math.Max(1, finalSize.Width - 2 * OutlineThickness));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Text) || layout == null)
            return;

        geometry ??= BuildGeometry();
        if (geometry == null)
            return;

        double pad = OutlineThickness;
        using (context.PushTransform(Matrix.CreateTranslation(pad + 2, pad + 2.5)))
            context.DrawGeometry(ShadowBrush, new Pen(ShadowBrush, OutlineThickness * 2 + 2, lineJoin: PenLineJoin.Round), geometry);

        using (context.PushTransform(Matrix.CreateTranslation(pad, pad)))
        {
            if (Outline != null && OutlineThickness > 0)
                context.DrawGeometry(null, new Pen(Outline, OutlineThickness * 2, lineJoin: PenLineJoin.Round), geometry);
            context.DrawGeometry(Fill, null, geometry);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount > 1)
            return;

        Point p = e.GetPosition(this);
        if (TryGetWordAt(p, out string word))
        {
            e.Handled = true;
            RaiseEvent(new WordClickedEventArgs(WordClickedEvent, word, Text ?? "", Slot, p));
        }
    }

    /// <summary>Word under <paramref name="point"/> (control coordinates); false when the point is not on a word.</summary>
    public bool TryGetWordAt(Point point, out string word)
    {
        word = "";
        string? text = Text;
        if (string.IsNullOrEmpty(text) || layout == null)
            return false;

        // TextLayout.HitTestPoint's IsInside ignores the line's alignment offset (centred text), so the line and the
        // horizontal extent are checked here; GetCharacterHitFromDistance does apply the offset.
        double x = point.X - OutlineThickness, y = point.Y - OutlineThickness;
        if (y < 0)
            return false;

        double top = 0;
        TextLine? line = null;
        foreach (TextLine l in layout.TextLines)
        {
            if (y < top + l.Height)
            {
                line = l;
                break;
            }
            top += l.Height;
        }

        if (line == null || x < line.Start || x > line.Start + line.WidthIncludingTrailingWhitespace)
            return false;

        CharacterHit hit = line.GetCharacterHitFromDistance(x);
        int index = Math.Clamp(hit.FirstCharacterIndex, 0, text.Length - 1);
        if (!IsWordChar(text[index]) && index > 0 && IsWordChar(text[index - 1]))
            index--;

        word = WordAt(text, index);
        return word.Length > 0;
    }

    /// <summary>The word (letters, digits, inner apostrophes / hyphens) containing <paramref name="index"/>.</summary>
    public static string WordAt(string text, int index)
    {
        if (index < 0 || index >= text.Length || !IsWordChar(text[index]))
            return "";

        int start = index, end = index;
        while (start > 0 && (IsWordChar(text[start - 1]) || (IsJoiner(text[start - 1]) && start > 1 && IsWordChar(text[start - 2]))))
            start--;
        while (end < text.Length - 1 && (IsWordChar(text[end + 1]) || (IsJoiner(text[end + 1]) && end + 2 < text.Length && IsWordChar(text[end + 2]))))
            end++;

        return text[start..(end + 1)];
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    static bool IsJoiner(char c) => c is '\'' or '’' or '-';

    void Invalidate()
    {
        layout?.Dispose();
        layout = null;
        geometry = null;
        layoutWidth = -1;
    }

    Typeface CurrentTypeface => new(FontFamily, FontStyle.Normal, FontWeight);

    TextLayout EnsureLayout(double maxWidth)
    {
        if (layout != null && Math.Abs(layoutWidth - maxWidth) < 0.5)
            return layout;

        layout?.Dispose();
        geometry = null;
        layoutWidth = maxWidth;
        layout = new TextLayout(Text ?? "", CurrentTypeface, FontSize, Fill, TextAlignment.Center, TextWrapping.Wrap,
            maxWidth: maxWidth);
        return layout;
    }

    Geometry? BuildGeometry()
    {
        if (layout == null)
            return null;

        FormattedText ft = new(Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, CurrentTypeface, FontSize, Fill)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = double.IsInfinity(layoutWidth) ? 0 : layoutWidth,
        };
        return ft.BuildGeometry(new Point(0, 0));
    }
}

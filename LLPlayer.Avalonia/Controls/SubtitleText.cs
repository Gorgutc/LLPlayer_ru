using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Rendering.SceneGraph;

namespace LLPlayer.Avalonia.Controls;

/// <summary>A word clicked in a <see cref="SubtitleText"/>.</summary>
public sealed class WordClickedEventArgs(RoutedEvent routedEvent, string word, string text, int slot, Point position, bool isTranslated = false, string? sourceLanguage = null, string? targetLanguage = null, bool isWord = true)
    : RoutedEventArgs(routedEvent)
{
    /// <summary>True for a single word, false for a dragged phrase.</summary>
    public bool IsWord { get; } = isWord;
    public bool IsTranslated { get; } = isTranslated;
    public string? SourceLanguage { get; } = sourceLanguage;
    public string? TargetLanguage { get; } = targetLanguage;
    public string Word { get; } = word;
    /// <summary>The whole subtitle line (context).</summary>
    public string Text { get; } = text;
    public int Slot { get; } = slot;
    /// <summary>Click position relative to the control.</summary>
    public Point Position { get; } = position;
}

/// <summary>
/// Subtitle line drawn over video: wrapped, filled text with a solid outline and a soft drop shadow so it stays
/// readable on any frame. Lines follow the configured horizontal alignment. A left click on a word raises
/// <see cref="WordClicked"/> (word hit-test through the same <see cref="TextLayout"/> used for measuring).
/// </summary>
public sealed class SubtitleText : Control
{
    public static readonly StyledProperty<bool> IsTranslatedProperty = AvaloniaProperty.Register<SubtitleText, bool>(nameof(IsTranslated));
    public bool IsTranslated { get => GetValue(IsTranslatedProperty); set => SetValue(IsTranslatedProperty, value); }
    public static readonly StyledProperty<string?> SourceLanguageProperty = AvaloniaProperty.Register<SubtitleText, string?>(nameof(SourceLanguage));
    public string? SourceLanguage { get => GetValue(SourceLanguageProperty); set => SetValue(SourceLanguageProperty, value); }
    public static readonly StyledProperty<string?> TargetLanguageProperty = AvaloniaProperty.Register<SubtitleText, string?>(nameof(TargetLanguage));
    public string? TargetLanguage { get => GetValue(TargetLanguageProperty); set => SetValue(TargetLanguageProperty, value); }
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SubtitleText, string?>(nameof(Text));

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<SubtitleText>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<SubtitleText>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner<SubtitleText>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        AvaloniaProperty.Register<SubtitleText, FontStyle>(nameof(FontStyle), FontStyle.Normal);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SubtitleText, IBrush?>(nameof(Fill), Brushes.White);

    public static readonly StyledProperty<IBrush?> OutlineProperty =
        AvaloniaProperty.Register<SubtitleText, IBrush?>(nameof(Outline), Brushes.Black);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<SubtitleText, IBrush?>(nameof(Background));

    public static readonly StyledProperty<double> OutlineThicknessProperty =
        AvaloniaProperty.Register<SubtitleText, double>(nameof(OutlineThickness), 3);

    public static readonly StyledProperty<int> SlotProperty =
        AvaloniaProperty.Register<SubtitleText, int>(nameof(Slot));

    // Both text-layout options participate in layout, geometry and word-hit mapping (see DisplayText and
    // EnsureLayout/BuildGeometry below); changing either invalidates the cached layout, measure and visual.
    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<SubtitleText, TextAlignment>(nameof(TextAlignment), TextAlignment.Center);

    public static readonly StyledProperty<bool> IgnoreLineBreaksProperty =
        AvaloniaProperty.Register<SubtitleText, bool>(nameof(IgnoreLineBreaks));

    public static readonly RoutedEvent<WordClickedEventArgs> WordClickedEvent =
        RoutedEvent.Register<SubtitleText, WordClickedEventArgs>(nameof(WordClicked), RoutingStrategies.Bubble);

    /// <summary>Opaque fallback; hosts bind the themed Shadcn ring brush over it.</summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<SubtitleText, IBrush?>(nameof(SelectionBrush), Brushes.Gray);
    public IBrush? SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }

    /// <summary>Highlight opacity in the range 0..1 (values outside are clamped when drawn).</summary>
    public static readonly StyledProperty<double> SelectionOpacityProperty =
        AvaloniaProperty.Register<SubtitleText, double>(nameof(SelectionOpacity), 0.35);
    public double SelectionOpacity { get => GetValue(SelectionOpacityProperty); set => SetValue(SelectionOpacityProperty, value); }

    static readonly IBrush ShadowBrush = new ImmutableSolidColorBrush(Color.FromArgb(110, 0, 0, 0));

    TextLayout? layout;
    Geometry? geometry;
    double layoutWidth = -1;

    // Drag selection: anchor word and current word as character ranges over DisplayText ([start, end)).
    bool selecting;
    bool dragActive;
    int anchorStart = -1;
    int anchorEnd = -1;
    int currentStart = -1;
    int currentEnd = -1;
    IPointer? selectionPointer;

    static SubtitleText()
    {
        AffectsMeasure<SubtitleText>(TextProperty, FontSizeProperty, FontFamilyProperty, FontWeightProperty, FontStyleProperty, OutlineThicknessProperty);
        AffectsRender<SubtitleText>(FillProperty, OutlineProperty, BackgroundProperty, SelectionBrushProperty, SelectionOpacityProperty);
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

    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
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

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
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

    /// <summary>Horizontal alignment of each drawn line inside the box (Center by default).</summary>
    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Apply the canonical portable <see cref="FlyleafLib.SubtitleTextUtil.FlattenText"/> semantics used by WPF when its
    /// equivalent option is on: dash lists keep their line breaks, other cue text collapses to one line with single
    /// spaces.
    /// </summary>
    public bool IgnoreLineBreaks
    {
        get => GetValue(IgnoreLineBreaksProperty);
        set => SetValue(IgnoreLineBreaksProperty, value);
    }

    public event EventHandler<WordClickedEventArgs>? WordClicked
    {
        add => AddHandler(WordClickedEvent, value);
        remove => RemoveHandler(WordClickedEvent, value);
    }

    /// <summary>Raised on a word press before any click action, so the host can pause playback (WPF parity).</summary>
    public event EventHandler? WordPressed;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FontFamilyProperty
            || change.Property == FontWeightProperty || change.Property == FontStyleProperty || change.Property == OutlineThicknessProperty
            || change.Property == TextAlignmentProperty || change.Property == IgnoreLineBreaksProperty)
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
        DrawBackground(context, pad);

        if (selecting && SelectionBrush != null && layout != null)
        {
            int start = Math.Min(anchorStart, currentStart);
            int end = Math.Max(anchorEnd, currentEnd);
            if (start >= 0 && end > start)
            {
                using (context.PushOpacity(Math.Clamp(SelectionOpacity, 0, 1)))
                {
                    foreach (Rect rect in GetSelectionRects(start, end))
                        context.FillRectangle(SelectionBrush, rect);
                }
            }
        }

        using (context.PushTransform(Matrix.CreateTranslation(pad + 2, pad + 2.5)))
            context.DrawGeometry(ShadowBrush, new Pen(ShadowBrush, OutlineThickness * 2 + 2, lineJoin: PenLineJoin.Round), geometry);

        using (context.PushTransform(Matrix.CreateTranslation(pad, pad)))
        {
            if (Outline != null && OutlineThickness > 0)
                context.DrawGeometry(null, new Pen(Outline, OutlineThickness * 2, lineJoin: PenLineJoin.Round), geometry);
            context.DrawGeometry(Fill, null, geometry);
        }
    }

    void DrawBackground(DrawingContext context, double pad)
    {
        IBrush? background = Background;
        if (layout == null || string.IsNullOrWhiteSpace(Text) || background == null || background.Opacity <= 0
            || background is ISolidColorBrush { Color.A: 0 })
            return;

        Rect bounds = default;
        double top = pad;
        foreach (TextLine line in layout.TextLines)
        {
            bounds = bounds.Union(new Rect(line.Start + pad, top, line.WidthIncludingTrailingWhitespace, line.Height));
            top += line.Height;
        }

        // Snapshot on the UI thread; only this paint operation ignores input, leaving the existing glyph hits intact.
        context.Custom(new BackgroundDrawOperation(bounds.Inflate(pad), background.ToImmutable()));
    }

    sealed class BackgroundDrawOperation(Rect bounds, IImmutableBrush brush) : ICustomDrawOperation
    {
        readonly IImmutableBrush backgroundBrush = brush;
        public Rect Bounds { get; } = bounds;
        public bool HitTest(Point p) => false;
        public void Render(ImmediateDrawingContext context) => context.DrawRectangle(backgroundBrush, null, Bounds);
        public bool Equals(ICustomDrawOperation? other)
            => other is BackgroundDrawOperation operation && Bounds == operation.Bounds && backgroundBrush.Equals(operation.backgroundBrush);
        public void Dispose() { }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount > 1)
            return;

        Point p = e.GetPosition(this);
        if (!TryGetWordRangeAt(p, out int start, out int end))
            return;

        e.Handled = true;
        anchorStart = currentStart = start;
        anchorEnd = currentEnd = end;
        selecting = false;
        dragActive = true;
        selectionPointer = e.Pointer;
        e.Pointer.Capture(this);
        WordPressed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragActive)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Point p = e.GetPosition(this);
        if (!TryGetWordRangeAt(p, out int start, out int end))
            return;
        if (start == currentStart && end == currentEnd)
            return;

        currentStart = start;
        currentEnd = end;
        selecting = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragActive)
            return;

        dragActive = false;
        IPointer? pointer = selectionPointer;
        selectionPointer = null;
        pointer?.Capture(null);

        bool onWord = TryGetWordRangeAt(e.GetPosition(this), out int releasedStart, out int releasedEnd);
        int aStart = anchorStart, aEnd = anchorEnd;
        ClearSelection();

        if (!onWord)
        {
            e.Handled = true;
            return;
        }

        bool isWord = releasedStart == aStart;
        int start = isWord ? aStart : Math.Min(aStart, releasedStart);
        int end = isWord ? aEnd : Math.Max(aEnd, releasedEnd);
        string text = DisplayText;
        if (start < 0 || end <= start || end > text.Length)
        {
            e.Handled = true;
            return;
        }

        string words = isWord ? text[start..end] : text[start..end].ReplaceLineEndings(" ");
        Point anchor = SelectionAnchor(start, end, e.GetPosition(this));
        e.Handled = true;
        RaiseEvent(new WordClickedEventArgs(WordClickedEvent, words, text, Slot, anchor, IsTranslated,
            SourceLanguage, TargetLanguage, isWord));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!dragActive)
            return;
        dragActive = false;
        selectionPointer = null;
        ClearSelection();
    }

    void ClearSelection()
    {
        CancelSelection();
        InvalidateVisual();
    }

    void CancelSelection()
    {
        dragActive = false;
        if (selectionPointer is { } pointer)
        {
            selectionPointer = null;
            pointer.Capture(null);
        }
        selecting = false;
        anchorStart = anchorEnd = currentStart = currentEnd = -1;
    }

    Point SelectionAnchor(int start, int end, Point fallback)
    {
        Rect union = default;
        bool first = true;
        foreach (Rect rect in GetSelectionRects(start, end))
        {
            union = first ? rect : union.Union(rect);
            first = false;
        }
        return first ? fallback : new Point(union.Center.X, union.Top);
    }

    List<Rect> GetSelectionRects(int start, int end)
    {
        List<Rect> rects = [];
        if (layout == null)
            return rects;

        double pad = OutlineThickness;
        double top = pad;
        foreach (TextLine line in layout.TextLines)
        {
            int lineStart = line.FirstTextSourceIndex;
            int lineEnd = lineStart + line.Length;
            int from = Math.Max(start, lineStart);
            int to = Math.Min(end, lineEnd);
            if (to > from)
            {
                foreach (TextBounds bounds in line.GetTextBounds(from, to - from))
                {
                    if (bounds.Rectangle.Width <= 0)
                        continue;
                    rects.Add(new Rect(pad + bounds.Rectangle.X, top + bounds.Rectangle.Y,
                        bounds.Rectangle.Width, bounds.Rectangle.Height));
                }
            }
            top += line.Height;
        }
        return rects;
    }

    /// <summary>Word character range under a point; false when the point is not on a word.</summary>
    public bool TryGetWordRangeAt(Point point, out int start, out int end)
    {
        start = end = -1;
        string text = DisplayText;
        if (string.IsNullOrEmpty(text) || layout == null)
            return false;

        // TextLayout.HitTestPoint's IsInside ignores the line's alignment offset, so the line and the horizontal extent
        // are checked here; GetCharacterHitFromDistance does apply the offset.
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

        return WordRangeAt(text, index, out start, out end);
    }

    /// <summary>Word under <paramref name="point"/> (control coordinates); false when the point is not on a word.</summary>
    public bool TryGetWordAt(Point point, out string word)
    {
        word = "";
        if (!TryGetWordRangeAt(point, out int start, out int end))
            return false;
        word = DisplayText[start..end];
        return word.Length > 0;
    }

    /// <summary>The word (letters, digits, inner apostrophes / hyphens) containing <paramref name="index"/>.</summary>
    public static string WordAt(string text, int index)
        => WordRangeAt(text, index, out int start, out int end) ? text[start..end] : "";

    static bool WordRangeAt(string text, int index, out int start, out int end)
    {
        start = end = -1;
        if (index < 0 || index >= text.Length || !IsWordChar(text[index]))
            return false;

        start = index;
        end = index;
        while (start > 0 && (IsWordChar(text[start - 1]) || (IsJoiner(text[start - 1]) && start > 1 && IsWordChar(text[start - 2]))))
            start--;
        while (end < text.Length - 1 && (IsWordChar(text[end + 1]) || (IsJoiner(text[end + 1]) && end + 2 < text.Length && IsWordChar(text[end + 2]))))
            end++;
        end++;
        return true;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    static bool IsJoiner(char c) => c is '\'' or '’' or '-';

    void Invalidate()
    {
        CancelSelection();
        layout?.Dispose();
        layout = null;
        geometry = null;
        layoutWidth = -1;
        // Alignment / line-break handling change the measured size too, and a real overlay must re-measure after an
        // option change; a cached TextLayout alone is not enough.
        InvalidateMeasure();
        InvalidateVisual();
    }

    Typeface CurrentTypeface => new(FontFamily, FontStyle, FontWeight);

    /// <summary>
    /// The string that is laid out, drawn and hit-tested. When <see cref="IgnoreLineBreaks"/> is on this reuses the
    /// canonical portable <see cref="FlyleafLib.SubtitleTextUtil.FlattenText"/> (the same helper WPF applies for the
    /// equivalent option), so dash lists and blank-line series keep the engine-defined semantics.
    /// </summary>
    string DisplayText => IgnoreLineBreaks && Text is { } raw ? FlyleafLib.SubtitleTextUtil.FlattenText(raw) : Text ?? "";

    TextLayout EnsureLayout(double maxWidth)
    {
        if (layout != null && Math.Abs(layoutWidth - maxWidth) < 0.5)
            return layout;

        layout?.Dispose();
        geometry = null;
        layoutWidth = maxWidth;
        layout = new TextLayout(DisplayText, CurrentTypeface, FontSize, Fill, TextAlignment, TextWrapping.Wrap,
            maxWidth: maxWidth);
        return layout;
    }

    Geometry? BuildGeometry()
    {
        if (layout == null)
            return null;

        FormattedText ft = new(DisplayText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, CurrentTypeface, FontSize, Fill)
        {
            TextAlignment = TextAlignment,
            MaxTextWidth = double.IsInfinity(layoutWidth) ? 0 : layoutWidth,
            // Keep the drawn geometry wrapping exactly like the measured TextLayout instead of the default ellipsis /
            // overflow behaviour of FormattedText.
            Trimming = TextTrimming.None,
        };
        return ft.BuildGeometry(new Point(0, 0));
    }
}

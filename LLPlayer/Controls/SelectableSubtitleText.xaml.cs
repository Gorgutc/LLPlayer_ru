using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FlyleafLib;
using LLPlayer.Extensions;
using NMeCab.Specialized;

namespace LLPlayer.Controls;

public partial class SelectableSubtitleText : UserControl
{
    private readonly Binding _bindFontSize;
    private readonly Binding _bindFontWeight;
    private readonly Binding _bindFontFamily;
    private readonly Binding _bindFontStyle;
    private readonly Binding _bindFill;
    private readonly Binding _bindStroke;
    private readonly Binding _bindStrokeThicknessInitial;

    private OutlinedTextBlock? _wordStart;

    public SelectableSubtitleText()
    {
        InitializeComponent();

        // DataContext is set in WrapPanel, so there is no need to use ElementName.
        //_bindFontSize = new Binding(nameof(FontSize))
        //{
        //    //ElementName = nameof(Root),
        //    Mode = BindingMode.OneWay
        //};
        _bindFontSize = new Binding(nameof(FontSize));
        _bindFontWeight = new Binding(nameof(FontWeight));
        _bindFontFamily = new Binding(nameof(FontFamily));
        _bindFontStyle = new Binding(nameof(FontStyle));
        _bindFill = new Binding(nameof(Fill));
        _bindStroke = new Binding(nameof(Stroke));
        _bindStrokeThicknessInitial = new Binding(nameof(StrokeThicknessInitial));
    }

    public static readonly RoutedEvent WordClickedEvent =
        EventManager.RegisterRoutedEvent(nameof(WordClicked), RoutingStrategy.Bubble, typeof(WordClickedEventHandler), typeof(SelectableSubtitleText));

    public event WordClickedEventHandler WordClicked
    {
        add => AddHandler(WordClickedEvent, value);
        remove => RemoveHandler(WordClickedEvent, value);
    }

    public event EventHandler? WordClickedDown;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private string _textFix = string.Empty;

    public static readonly DependencyProperty IsTranslatedProperty =
        DependencyProperty.Register(nameof(IsTranslated), typeof(bool), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(false));

    public bool IsTranslated
    {
        get => (bool)GetValue(IsTranslatedProperty);
        set => SetValue(IsTranslatedProperty, value);
    }

    public static readonly DependencyProperty TextLanguageProperty =
        DependencyProperty.Register(nameof(TextLanguage), typeof(Language), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(null, OnTextLanguageChanged));

    public Language? TextLanguage
    {
        get => (Language)GetValue(TextLanguageProperty);
        set => SetValue(TextLanguageProperty, value);
    }

    public static readonly DependencyProperty SubIndexProperty =
        DependencyProperty.Register(nameof(SubIndex), typeof(int), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(0));

    public int SubIndex
    {
        get => (int)GetValue(SubIndexProperty);
        set => SetValue(SubIndexProperty, value);
    }

    public static readonly DependencyProperty FillProperty =
        OutlinedTextBlock.FillProperty.AddOwner(typeof(SelectableSubtitleText));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty =
        OutlinedTextBlock.StrokeProperty.AddOwner(typeof(SelectableSubtitleText));

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly DependencyProperty WidthPercentageProperty =
        DependencyProperty.Register(nameof(WidthPercentage), typeof(double), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(60.0, OnWidthPercentageChanged));

    public double WidthPercentage
    {
        get => (double)GetValue(WidthPercentageProperty);
        set => SetValue(WidthPercentageProperty, value);
    }

    public static readonly DependencyProperty WidthPercentageFixProperty =
        DependencyProperty.Register(nameof(WidthPercentageFix), typeof(double), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(60.0));

    public double WidthPercentageFix
    {
        get => (double)GetValue(WidthPercentageFixProperty);
        set => SetValue(WidthPercentageFixProperty, value);
    }

    public static readonly DependencyProperty IgnoreLineBreakProperty =
        DependencyProperty.Register(nameof(IgnoreLineBreak), typeof(bool), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(false));

    public double MaxFixedWidth
    {
        get => (double)GetValue(MaxFixedWidthProperty);
        set => SetValue(MaxFixedWidthProperty, value);
    }

    public static readonly DependencyProperty MaxFixedWidthProperty =
        DependencyProperty.Register(nameof(MaxFixedWidth), typeof(double), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(0.0));

    public bool IgnoreLineBreak
    {
        get => (bool)GetValue(IgnoreLineBreakProperty);
        set => SetValue(IgnoreLineBreakProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessInitialProperty =
        DependencyProperty.Register(nameof(StrokeThicknessInitial), typeof(double), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(3.0));

    public double StrokeThicknessInitial
    {
        get => (double)GetValue(StrokeThicknessInitialProperty);
        set => SetValue(StrokeThicknessInitialProperty, value);
    }

    public static readonly DependencyProperty WordHoverBorderBrushProperty =
        DependencyProperty.Register(nameof(WordHoverBorderBrush), typeof(Brush), typeof(SelectableSubtitleText), new FrameworkPropertyMetadata(Brushes.Cyan));

    public Brush WordHoverBorderBrush
    {
        get => (Brush)GetValue(WordHoverBorderBrushProperty);
        set => SetValue(WordHoverBorderBrushProperty, value);
    }

    private static void OnWidthPercentageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (SelectableSubtitleText)d;
        ctl.WidthPercentageFix = (double)e.NewValue;
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (SelectableSubtitleText)d;
        ctl.SetText((string)e.NewValue);
    }

    private static void OnTextLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (SelectableSubtitleText)d;
        if (ctl.TextLanguage != null && ctl.TextLanguage.IsRTL)
        {
            ctl.wrapPanel.FlowDirection = FlowDirection.RightToLeft;
        }
        else
        {
            ctl.wrapPanel.FlowDirection = FlowDirection.LeftToRight;
        }
    }

    // SelectableTextBox uses char.IsPunctuation(), so use a regular expression for it.
    // TODO: L: Sharing the code with TextBox
    [GeneratedRegex(@"((?:[^\P{P}'-]+|\s))")]
    private static partial Regex WordSplitReg { get; }

    [GeneratedRegex(@"^(?:[^\P{P}'-]+|\s)$")]
    private static partial Regex WordSplitFullReg { get; }

    [GeneratedRegex(@"((?:[^\P{P}'-]+|\s|[\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}]))")]
    private static partial Regex ChineseWordSplitReg { get; }


    private static readonly Lazy<MeCabIpaDicTagger> MeCabTagger = new(() => MeCabIpaDicTagger.Create(), true);

    // Reused frozen brushes: SetText() rebuilds the whole word panel on every subtitle change, so a new
    // SolidColorBrush per word/space (and per hover) was allocated on a hot path. These colors are
    // constant, so share one frozen instance each.
    private static readonly SolidColorBrush HitTestTransparentBrush = CreateFrozenBrush(Color.FromArgb(1, 0, 0, 0));
    private static readonly SolidColorBrush HoverFillBrush = CreateFrozenBrush(Color.FromArgb(80, 127, 127, 127));

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    // Per-instance element pools. SetText() rebuilds the word panel on every subtitle change; instead of
    // discarding and re-creating every Border/OutlinedTextBlock/space (with their bindings and event
    // handlers) each time, the previous run's elements are recycled by type and reconfigured. Pools are
    // per-control (never shared) so primary/secondary subtitles and RTL/LTR flow never cross-contaminate.
    private readonly Stack<TextBlock> _spacePool = new();
    private readonly Stack<OutlinedTextBlock> _splitterPool = new();
    private readonly Stack<Border> _wordBorderPool = new();
    private readonly Stack<NewLine> _newLinePool = new();

    // Tokenization (MeCab / regex split) is deterministic for a given (language, line), so cache it to
    // avoid re-segmenting repeated subtitle lines. Bounded to keep memory flat over a long session.
    // Accessed only from SetText, which runs on the UI thread, so no synchronization is required.
    private const int TokenizeCacheMax = 256;
    private static readonly Dictionary<(string Lang, string Line), string[]> TokenizeCache = new();

    private void SetText(string text)
    {
        if (text == null)
        {
            return;
        }

        if (IgnoreLineBreak)
        {
            text = SubtitleTextUtil.FlattenText(text);
        }

        _textFix = text;

        bool containLineBreak = text.AsSpan().ContainsAny('\r', '\n');

        // If it contains line feeds, expand them to the full screen width (respecting the formatting in the SRT subtitle)
        WidthPercentageFix = containLineBreak ? 100.0 : WidthPercentage;

        // Recycle the previous run's elements into the per-type pools, then rebuild from them.
        RecycleChildren();
        _wordStart = null;

        string[] lines = text.SplitToLines().ToArray();

        var wordOffset = 0;

        // Reuse one element per token (word / space / split-char / line-break), keeping the children of
        // wrapPanel strictly 1:1 with the tokens by type and order — phrase selection relies on this
        // (see WordMouseLeftButtonUp). An OutlinedTextBlock is used to draw each word's outlined text.
        for (int i = 0; i < lines.Length; i++)
        {
            string[] words = Tokenize(lines[i]);

            foreach (string word in words)
            {
                // skip empty string because Split includes
                if (word.Length == 0)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(word))
                {
                    // Blanks are inserted with a (hit-testable) TextBlock so clicking between words does
                    // not toggle playback.
                    TextBlock space = RentSpace();
                    space.Text = word;
                    wrapPanel.Children.Add(space);
                    wordOffset += word.Length;
                    continue;
                }

                bool isSplitChar = WordSplitFullReg.IsMatch(word);

                if (isSplitChar)
                {
                    OutlinedTextBlock splitter = RentSplitter();
                    splitter.Text = word;
                    splitter.WordOffset = wordOffset;
                    wrapPanel.Children.Add(splitter);
                }
                else
                {
                    Border border = RentWordBorder();
                    var textBlock = (OutlinedTextBlock)border.Child;
                    textBlock.Text = word;
                    textBlock.WordOffset = wordOffset;
                    wrapPanel.Children.Add(border);
                }

                wordOffset += word.Length;
            }

            if (containLineBreak && i != lines.Length - 1)
            {
                // Add line breaks except at the end when there are two or more lines
                wrapPanel.Children.Add(RentNewLine());
            }
        }
    }

    // Tokenize a single line, memoized by (language, line). The returned array is read-only to callers.
    private string[] Tokenize(string line)
    {
        string lang = TextLanguage?.ISO6391 ?? string.Empty;
        var key = (lang, line);
        if (TokenizeCache.TryGetValue(key, out string[]? cached))
        {
            return cached;
        }

        string[] words;
        if (lang == "ja")
        {
            // word segmentation for Japanese
            // TODO: L: Also do word segmentation in sidebar
            var nodes = MeCabTagger.Value.Parse(line);
            List<string> wordsList = new(nodes.Length);
            foreach (var node in nodes)
            {
                // If there are space-separated characters, such as English, add them manually since they are not on the Surface
                if (char.IsWhiteSpace(line[node.BPos]))
                {
                    wordsList.Add(" ");
                }
                wordsList.Add(node.Surface);
            }

            words = wordsList.ToArray();
        }
        else if (lang == "zh")
        {
            words = ChineseWordSplitReg.Split(line);
        }
        else
        {
            words = WordSplitReg.Split(line);
        }

        if (TokenizeCache.Count >= TokenizeCacheMax)
        {
            // Evict the oldest entry (FIFO) instead of flushing the whole cache, so a miss at
            // capacity stays O(1) and never triggers a burst of re-segmentation (incl. MeCab).
            TokenizeCache.Remove(TokenizeCache.Keys.First());
        }
        TokenizeCache[key] = words;
        return words;
    }

    // Move the current children back into their per-type pools (resetting any hover state) and detach
    // them so they can be re-added on the next rebuild.
    private void RecycleChildren()
    {
        foreach (UIElement child in wrapPanel.Children)
        {
            switch (child)
            {
                case Border border:
                    _wordBorderPool.Push(border);
                    break;
                case OutlinedTextBlock splitter:
                    _splitterPool.Push(splitter);
                    break;
                case TextBlock space:
                    _spacePool.Push(space);
                    break;
                case NewLine newLine:
                    _newLinePool.Push(newLine);
                    break;
            }
        }

        wrapPanel.Children.Clear();
    }

    private TextBlock RentSpace()
    {
        if (_spacePool.Count > 0)
        {
            return _spacePool.Pop();
        }

        TextBlock space = new()
        {
            Background = HitTestTransparentBrush,
        };
        space.SetBinding(TextBlock.FontSizeProperty, _bindFontSize);
        space.SetBinding(TextBlock.FontWeightProperty, _bindFontWeight);
        space.SetBinding(TextBlock.FontStyleProperty, _bindFontStyle);
        space.SetBinding(TextBlock.FontFamilyProperty, _bindFontFamily);
        return space;
    }

    private OutlinedTextBlock RentSplitter()
    {
        return _splitterPool.Count > 0 ? _splitterPool.Pop() : CreateOutlinedTextBlock();
    }

    private Border RentWordBorder()
    {
        Border border;
        if (_wordBorderPool.Count > 0)
        {
            border = _wordBorderPool.Pop();
        }
        else
        {
            // Set brush to Border because OutlinedTextBlock's character click judgment is only on the character.
            //ref: https://stackoverflow.com/questions/50653308/hit-testing-a-transparent-element-in-a-transparent-window
            border = new Border
            {
                BorderThickness = new Thickness(1),
                IsHitTestVisible = true,
                Child = CreateOutlinedTextBlock(),
                Cursor = Cursors.Hand,
                // Lets the shared static hover handlers reach this control's WordHoverBorderBrush.
                Tag = this,
            };

            // Handlers are attached once, at creation only — never on reuse — to avoid double subscriptions.
            border.MouseLeftButtonDown += WordMouseLeftButtonDown;
            border.MouseLeftButtonUp += WordMouseLeftButtonUp;
            border.MouseRightButtonUp += WordMouseRightButtonUp;
            border.MouseUp += WordMouseMiddleButtonUp;
            border.MouseEnter += OnWordBorderMouseEnter;
            border.MouseLeave += OnWordBorderMouseLeave;
        }

        // Reset hover visuals before the border is shown again (it may have been recycled mid-hover).
        border.BorderBrush = null;
        border.Background = HitTestTransparentBrush;
        return border;
    }

    private NewLine RentNewLine()
    {
        return _newLinePool.Count > 0 ? _newLinePool.Pop() : new NewLine();
    }

    private OutlinedTextBlock CreateOutlinedTextBlock()
    {
        OutlinedTextBlock textBlock = new()
        {
            ClipToBounds = false,
            TextWrapping = TextWrapping.Wrap,
            StrokePosition = StrokePosition.Outside,
            IsHitTestVisible = false,
            // Fixed because the word itself is inverted
            FlowDirection = FlowDirection.LeftToRight
        };

        textBlock.SetBinding(OutlinedTextBlock.FontSizeProperty, _bindFontSize);
        textBlock.SetBinding(OutlinedTextBlock.FontWeightProperty, _bindFontWeight);
        textBlock.SetBinding(OutlinedTextBlock.FontStyleProperty, _bindFontStyle);
        textBlock.SetBinding(OutlinedTextBlock.FontFamilyProperty, _bindFontFamily);
        textBlock.SetBinding(OutlinedTextBlock.FillProperty, _bindFill);
        textBlock.SetBinding(OutlinedTextBlock.StrokeProperty, _bindStroke);
        textBlock.SetBinding(OutlinedTextBlock.StrokeThicknessInitialProperty, _bindStrokeThicknessInitial);
        return textBlock;
    }

    // Shared static hover handlers (one delegate instance for all words) replace the previous per-word
    // closures. The owning control is reached via Border.Tag to read the current WordHoverBorderBrush.
    private static void OnWordBorderMouseEnter(object sender, MouseEventArgs e)
    {
        var border = (Border)sender;
        if (border.Tag is SelectableSubtitleText owner)
        {
            border.BorderBrush = owner.WordHoverBorderBrush;
        }
        border.Background = HoverFillBrush;
    }

    private static void OnWordBorderMouseLeave(object sender, MouseEventArgs e)
    {
        var border = (Border)sender;
        border.BorderBrush = null;
        border.Background = HitTestTransparentBrush;
    }

    private void WordMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Child: OutlinedTextBlock word })
        {
            _wordStart = word;
            WordClickedDown?.Invoke(this, EventArgs.Empty);

            e.Handled = true;
        }
    }

    private void WordMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Child: OutlinedTextBlock word })
        {
            if (_wordStart == word)
            {
                // word clicked
                Point wordPoint = word.TranslatePoint(default, Root);

                WordClickedEventArgs args = new(WordClickedEvent)
                {
                    Mouse = MouseClick.Left,
                    Words = word.Text,
                    IsWord = true,
                    Text = _textFix,
                    IsTranslated = IsTranslated,
                    SubIndex = SubIndex,
                    WordOffset = word.WordOffset,
                    WordsX = wordPoint.X,
                    WordsWidth = word.ActualWidth
                };
                RaiseEvent(args);
            }
            else if (_wordStart != null)
            {
                // phrase selected
                var wordStart = _wordStart!;
                var wordEnd = word;

                // support right to left drag
                if (wordStart.WordOffset > wordEnd.WordOffset)
                {
                    (wordStart, wordEnd) = (wordEnd, wordStart);
                }

                List<FrameworkElement> elements = wrapPanel.Children.OfType<FrameworkElement>().ToList();

                int startIndex = elements.IndexOf((FrameworkElement)wordStart.Parent);
                int endIndex = elements.IndexOf((FrameworkElement)wordEnd.Parent);

                if (startIndex == -1 || endIndex == -1)
                {
                    Debug.Assert(startIndex >= 0);
                    Debug.Assert(endIndex >= 0);
                    return;
                }

                var selectedElements = elements[startIndex..(endIndex + 1)];
                var selectedWords = selectedElements.Select(fe =>
                {
                    switch (fe)
                    {
                        case Border { Child: OutlinedTextBlock word }:
                            return word.Text;
                        case OutlinedTextBlock splitter:
                            return splitter.Text;
                        case TextBlock space:
                            return space.Text;
                        case NewLine:
                            // convert to space
                            return " ";
                        default:
                            throw new InvalidOperationException();
                    }
                }).ToList();
                string selectedText = string.Join(string.Empty, selectedWords);

                Point startPoint = wordStart.TranslatePoint(default, Root);
                Point endPoint = wordEnd.TranslatePoint(default, Root);

                // if different Y axis, then line break or text wrapping
                double wordsX = 0;
                double wordsWidth = wrapPanel.ActualWidth;

                if (startPoint.Y == endPoint.Y)
                {
                    // selection in one line

                    if (wrapPanel.FlowDirection == FlowDirection.LeftToRight)
                    {
                        wordsX = startPoint.X;
                        wordsWidth = endPoint.X + wordEnd.ActualWidth - startPoint.X;
                    }
                    else
                    {
                        wordsX = endPoint.X;
                        wordsWidth = startPoint.X + wordStart.ActualWidth - endPoint.X;
                    }
                }

                WordClickedEventArgs args = new(WordClickedEvent)
                {
                    Mouse = MouseClick.Left,
                    Words = selectedText,
                    IsWord = false,
                    Text = _textFix,
                    IsTranslated = IsTranslated,
                    SubIndex = SubIndex,
                    WordOffset = wordStart.WordOffset,
                    WordsX = wordsX,
                    WordsWidth = wordsWidth
                };
                RaiseEvent(args);
            }

            _wordStart = null;
            e.Handled = true;
        }
    }

    private void WordMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Child: OutlinedTextBlock word })
        {
            Point wordPoint = word.TranslatePoint(default, Root);

            WordClickedEventArgs args = new(WordClickedEvent)
            {
                Mouse = MouseClick.Right,
                Words = word.Text,
                IsWord = true,
                Text = _textFix,
                IsTranslated = IsTranslated,
                SubIndex = SubIndex,
                WordOffset = word.WordOffset,
                WordsX = wordPoint.X,
                WordsWidth = word.ActualWidth
            };
            RaiseEvent(args);
            e.Handled = true;
        }
    }

    private void WordMouseMiddleButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            WordClickedEventArgs args = new(WordClickedEvent)
            {
                Mouse = MouseClick.Middle,
                Words = _textFix,
                IsWord = false,
                Text = _textFix,
                IsTranslated = IsTranslated,
                SubIndex = SubIndex,
                WordOffset = 0,
                WordsX = 0,
                WordsWidth = wrapPanel.ActualWidth
            };
            RaiseEvent(args);
            e.Handled = true;
        }
    }
}

public class NewLine : FrameworkElement
{
}

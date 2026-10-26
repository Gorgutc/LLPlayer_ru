using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LLPlayer.Avalonia.Services;

/// <summary>Resolves plain requested names without changing the persisted selection or loading arbitrary font assets.</summary>
public sealed class SubtitleFontCatalog
{
    // The same bundled font and fallback expression as Shadcn.FontSans; no installed Inter font is required.
    public static FontFamily DefaultFamily { get; } = new("fonts:Inter#Inter, $Default");
    public static FuncValueConverter<string?, string> DisplayNameConverter { get; } = new(value =>
        string.IsNullOrEmpty(value) ? "Default (Inter)" : value);
    public static IReadOnlyList<string> Weights { get; } = ["Normal", "SemiBold", "Bold"];
    public static IReadOnlyList<string> Styles { get; } = ["Normal", "Italic"];

    string[]? installedFamilies;
    string[] InstalledFamilies => installedFamilies ??= FontManager.Current.SystemFonts
        .Select(family => family.Name)
        .Where(name => AppPrefs.TryNormalizeSubtitleFontFamily(name, out string normalized) && normalized.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();

    public IReadOnlyList<string> GetFamilies(params string?[] requested)
        => new[] { "" }.Concat(InstalledFamilies).Concat(requested.Where(name =>
                AppPrefs.TryNormalizeSubtitleFontFamily(name, out string normalized) && normalized.Length > 0)
            .Select(name => name!.Trim()))
            .Distinct(StringComparer.Ordinal).ToArray();

    public SubtitleFontSelection Resolve(string? requestedFamily, string? requestedWeight, string? requestedStyle)
    {
        bool validFamily = AppPrefs.TryNormalizeSubtitleFontFamily(requestedFamily, out string name);
        string weightName = AppPrefs.TryNormalizeSubtitleFontWeight(requestedWeight, out string weight) ? weight : AppPrefs.DefaultSubtitleFontWeight;
        string styleName = AppPrefs.TryNormalizeSubtitleFontStyle(requestedStyle, out string style) ? style : AppPrefs.DefaultSubtitleFontStyle;
        FontWeight fontWeight = weightName switch { "Normal" => FontWeight.Normal, "Bold" => FontWeight.Bold, _ => FontWeight.SemiBold };
        FontStyle fontStyle = styleName == "Italic" ? FontStyle.Italic : FontStyle.Normal;
        FontFamily family = DefaultFamily;
        string status = "Default app font: Inter.";
        if (!validFamily)
            status = "Invalid family name; preview fallback: Inter. Save requires a plain family name.";
        else if (name.Length > 0)
        {
            string? installed = InstalledFamilies.FirstOrDefault(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
            if (installed != null)
            {
                family = new FontFamily(installed);
                status = "Font: " + name + ".";
            }
            else
                status = $"Requested font '{name}' is unavailable on this system; fallback: Inter. The requested name is retained.";
        }
        return new SubtitleFontSelection(family, fontWeight, fontStyle, status);
    }

    /// <summary>
    /// Like <see cref="Resolve"/>, but safe before the Avalonia font platform exists (view-model construction in
    /// headless/non-UI hosts): the requested plain family is kept and the renderer falls back. The real desktop host
    /// always resolves through <see cref="Resolve"/> and reports the catalog fallback.
    /// </summary>
    public SubtitleFontSelection ResolveOrRequested(string? requestedFamily, string? requestedWeight, string? requestedStyle)
    {
        try
        {
            return Resolve(requestedFamily, requestedWeight, requestedStyle);
        }
        catch (InvalidOperationException)
        {
            bool validFamily = AppPrefs.TryNormalizeSubtitleFontFamily(requestedFamily, out string name);
            string weightName = AppPrefs.TryNormalizeSubtitleFontWeight(requestedWeight, out string weight) ? weight : AppPrefs.DefaultSubtitleFontWeight;
            string styleName = AppPrefs.TryNormalizeSubtitleFontStyle(requestedStyle, out string style) ? style : AppPrefs.DefaultSubtitleFontStyle;
            FontWeight fontWeight = weightName switch { "Normal" => FontWeight.Normal, "Bold" => FontWeight.Bold, _ => FontWeight.SemiBold };
            FontStyle fontStyle = styleName == "Italic" ? FontStyle.Italic : FontStyle.Normal;
            FontFamily family = validFamily && name.Length > 0 ? new FontFamily(name) : DefaultFamily;
            return new SubtitleFontSelection(family, fontWeight, fontStyle, "Font platform unavailable; requested name retained.");
        }
    }

    public static string DescribeFace(SubtitleFontSelection selection)
    {
        Typeface typeface = new(selection.Family, selection.Style, selection.Weight);
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyph))
            return selection.Status + " The requested face is unavailable; the renderer uses its font fallback.";
        bool italicSynthesis = selection.Style == FontStyle.Italic && (glyph.FontSimulations & FontSimulations.Oblique) != 0;
        bool boldSynthesis = (glyph.FontSimulations & FontSimulations.Bold) != 0;
        if ((glyph.Style != selection.Style && !italicSynthesis) || (glyph.Weight != selection.Weight && !boldSynthesis))
            return selection.Status + $" Requested {selection.Weight} {selection.Style} face is unavailable; renderer fallback: {glyph.Weight} {glyph.Style}.";
        if (italicSynthesis || boldSynthesis)
            return selection.Status + " The requested face is synthesized by the renderer.";
        return selection.Status;
    }
}

public sealed record SubtitleFontSelection(FontFamily Family, FontWeight Weight, FontStyle Style, string Status);

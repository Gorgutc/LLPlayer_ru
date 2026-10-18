using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using AwesomeAssertions;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// "LLPlayer shadcn" tokens: every token resolves in the Dark and the Light theme to exactly the colour the converter
/// produced (Themes/tools/shadcn-neutral.tokens.json), and the converter output matches an independent C# OKLCH
/// conversion of the shadcn source values.
/// </summary>
public class ThemeTokenTests
{
    static readonly string TokensFile = Path.Combine(AppContext.BaseDirectory, "TestData", "shadcn-neutral.tokens.json");

    public static TheoryData<string, string> TokenThemePairs()
    {
        TheoryData<string, string> data = [];
        foreach (string token in LoadTokens().RootElement.GetProperty("tokens").EnumerateObject().Select(p => p.Name))
        {
            data.Add(token, "Dark");
            data.Add(token, "Light");
        }
        return data;
    }

    static JsonDocument LoadTokens() => JsonDocument.Parse(File.ReadAllText(TokensFile));

    static string Pascal(string token) => string.Concat(token.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    static uint ParseArgb(string hex) => uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    static object Resolve(string key, string theme)
    {
        ThemeVariant variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Application.Current!.TryGetResource(key, variant, out object? value).Should().BeTrue($"resource '{key}' must exist in the {theme} theme");
        return value!;
    }

    [Fact]
    public void TokenFile_ListsTheRequiredShadcnTokens()
    {
        string[] required =
        [
            "background", "foreground", "card", "card-foreground", "popover", "popover-foreground", "primary",
            "primary-foreground", "secondary", "secondary-foreground", "muted", "muted-foreground", "accent",
            "accent-foreground", "destructive", "destructive-foreground", "border", "input", "ring",
        ];

        using JsonDocument doc = LoadTokens();
        var tokens = doc.RootElement.GetProperty("tokens").EnumerateObject().Select(p => p.Name).ToList();
        tokens.Should().Contain(required);
        doc.RootElement.GetProperty("source").GetProperty("commit").GetString().Should().MatchRegex("^[0-9a-f]{40}$");
    }

    [AvaloniaTheory]
    [MemberData(nameof(TokenThemePairs))]
    public void Token_ResolvesInTheme_ToTheConverterOutput(string token, string theme)
    {
        using JsonDocument doc = LoadTokens();
        string hex = doc.RootElement.GetProperty("tokens").GetProperty(token).GetProperty(theme).GetProperty("hex").GetString()!;

        object color = Resolve($"Shadcn.{Pascal(token)}Color", theme);
        color.Should().BeOfType<Color>().Which.ToUInt32().Should().Be(ParseArgb(hex), $"{token} ({theme})");

        object brush = Resolve($"Shadcn.{Pascal(token)}Brush", theme);
        brush.Should().BeAssignableTo<ISolidColorBrush>().Which.Color.ToUInt32().Should().Be(ParseArgb(hex));
    }

    [Fact]
    public void ConverterOutput_MatchesIndependentOklchConversion()
    {
        using JsonDocument doc = LoadTokens();
        foreach (JsonProperty token in doc.RootElement.GetProperty("tokens").EnumerateObject())
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                JsonElement entry = token.Value.GetProperty(theme);
                string expected = OklchToArgbHex(entry.GetProperty("oklch").GetString()!);
                entry.GetProperty("hex").GetString().Should().Be(expected, $"{token.Name} ({theme})");
            }
        }
    }

    [Theory]
    [InlineData("oklch(1 0 0)", "#FFFFFFFF")]
    [InlineData("oklch(0.145 0 0)", "#FF0A0A0A")]          // Tailwind neutral-950
    [InlineData("oklch(0.922 0 0)", "#FFE5E5E5")]          // neutral-200
    [InlineData("oklch(0.577 0.245 27.325)", "#FFE7000B")] // red-600
    [InlineData("oklch(1 0 0 / 10%)", "#1AFFFFFF")]
    public void OklchConversion_KnownValues(string oklch, string argb)
        => OklchToArgbHex(oklch).Should().Be(argb);

    [AvaloniaFact]
    public void FluentPalettes_UseTheTokens()
    {
        using JsonDocument doc = LoadTokens();
        JsonElement tokens = doc.RootElement.GetProperty("tokens");
        foreach (string theme in new[] { "Dark", "Light" })
        {
            ((Color)Resolve("SystemAccentColor", theme)).ToUInt32()
                .Should().Be(ParseArgb(tokens.GetProperty("primary").GetProperty(theme).GetProperty("hex").GetString()!));
            ((Color)Resolve("SystemRegionColor", theme)).ToUInt32()
                .Should().Be(ParseArgb(tokens.GetProperty("background").GetProperty(theme).GetProperty("hex").GetString()!));
        }
    }

    [AvaloniaFact]
    public void ShapeAndTypographyTokens_FollowShadcnRatios()
    {
        ((double)Resolve("Shadcn.Radius", "Dark")).Should().Be(10);
        ((CornerRadius)Resolve("Shadcn.RadiusSm", "Dark")).TopLeft.Should().Be(6);
        ((CornerRadius)Resolve("Shadcn.RadiusMd", "Dark")).TopLeft.Should().Be(8);
        ((CornerRadius)Resolve("Shadcn.RadiusLg", "Dark")).TopLeft.Should().Be(10);
        ((CornerRadius)Resolve("Shadcn.RadiusXl", "Dark")).TopLeft.Should().Be(14);
        ((CornerRadius)Resolve("ControlCornerRadius", "Light")).TopLeft.Should().Be(8);
        foreach (var (key, size) in new[] { ("Shadcn.TextXs", 12.0), ("Shadcn.TextSm", 14.0), ("Shadcn.TextBase", 16.0), ("Shadcn.TextLg", 18.0), ("Shadcn.TextXl", 20.0), ("Shadcn.Text2xl", 24.0) })
            ((double)Resolve(key, "Dark")).Should().Be(size, key);
    }

    /// <summary>Independent OKLCH -> sRGB (Björn Ottosson's matrices, IEC 61966-2-1 transfer, clip, round half up).</summary>
    public static string OklchToArgbHex(string text)
    {
        Match m = Regex.Match(text, @"^oklch\(\s*([0-9.]+)(%?)\s+([0-9.]+)\s+([0-9.]+)\s*(?:/\s*([0-9.]+)(%?))?\s*\)$");
        m.Success.Should().BeTrue(text);
        double l = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / (m.Groups[2].Value == "%" ? 100 : 1);
        double c = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        double h = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * Math.PI / 180;
        double alpha = m.Groups[5].Success ? double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) / (m.Groups[6].Value == "%" ? 100 : 1) : 1;

        double a = c * Math.Cos(h), b = c * Math.Sin(h);
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double L = l_ * l_ * l_, M = m_ * m_ * m_, S = s_ * s_ * s_;
        double[] rgb =
        [
            4.0767416621 * L - 3.3077115913 * M + 0.2309699292 * S,
            -1.2684380046 * L + 2.6097574011 * M - 0.3413193965 * S,
            -0.0041960863 * L - 0.7034186147 * M + 1.7076147010 * S,
        ];

        static int Byte(double v) => (int)Math.Floor(Math.Clamp(v, 0, 1) * 255 + 0.5);
        static double Encode(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

        return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}",
            Byte(alpha), Byte(Encode(rgb[0])), Byte(Encode(rgb[1])), Byte(Encode(rgb[2])));
    }
}

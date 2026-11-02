namespace LLPlayer.Avalonia.Services;

/// <summary>Deterministic region selection shared by the Windows adapter and platform-neutral tests.</summary>
internal static class MicrosoftOcrLanguagePolicy
{
    public static string? SelectTag(string iso, string? preferred, IReadOnlyList<string> available)
    {
        bool Matches(string tag) => string.Equals(tag, iso, StringComparison.OrdinalIgnoreCase) || tag.StartsWith(iso + "-", StringComparison.OrdinalIgnoreCase);
        if (preferred != null)
            return available.FirstOrDefault(tag => string.Equals(tag, preferred, StringComparison.OrdinalIgnoreCase) && Matches(tag));
        return available.FirstOrDefault(tag => string.Equals(tag, iso, StringComparison.OrdinalIgnoreCase)) ?? available.FirstOrDefault(Matches);
    }

    public static string FormatLines(string languageTag, IEnumerable<(string Text, IReadOnlyList<string> Words)> lines)
    {
        bool cjk = languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, lines.Select(line => cjk ? string.Concat(line.Words) : line.Text));
    }
}

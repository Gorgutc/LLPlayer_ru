using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LLPlayer.Avalonia.Services;

[JsonConverter(typeof(WordMenuActionKindConverter))]
public enum WordMenuActionKind { Unknown, Search, Clipboard, ClipboardAll }

[JsonConverter(typeof(WordSearchModifierConverter))]
public enum WordSearchModifier { Control, Shift, Alt, None }

[JsonConverter(typeof(SubtitleAutoCopyTargetConverter))]
public enum SubtitleAutoCopyTarget { Primary, Secondary, All }

public enum WordSelectionOrigin { Overlay, Sidebar, Popup }

/// <summary>A mutable, detached menu row; preferences and each editor own separate instances.</summary>
public sealed partial class WordMenuActionPref : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSearch)), NotifyPropertyChangedFor(nameof(IsClipboard))]
    public partial WordMenuActionKind Kind { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial bool IsEnabled { get; set; } = true;
    [ObservableProperty] public partial string Url { get; set; } = "";
    [ObservableProperty] public partial bool ToLower { get; set; }

    public bool IsSearch => Kind == WordMenuActionKind.Search;
    public bool IsClipboard => Kind is WordMenuActionKind.Clipboard or WordMenuActionKind.ClipboardAll;

    public WordMenuActionPref Clone() => new()
    {
        Kind = Kind, Title = Title, IsEnabled = IsEnabled, Url = Url, ToLower = ToLower,
    };
}

/// <summary>Input provenance and subtitle metadata captured before another selection can change the popup.</summary>
public sealed record WordSelectionSnapshot(string Word, string Text, int Slot, bool IsWord, bool IsTranslated,
    string? SourceLanguage, string? TargetLanguage, WordSelectionOrigin Origin, MouseButton Button, KeyModifiers Modifiers, int WordOffset = -1);

/// <summary>A menu command owns its row snapshot as well as the immutable selected text.</summary>
public sealed record WordMenuInvocation
{
    readonly WordMenuActionPref action;
    public WordMenuInvocation(WordMenuActionPref action, WordSelectionSnapshot selection)
    {
        this.action = action.Clone();
        Selection = selection;
    }
    public WordMenuActionPref Action => action.Clone();
    public WordSelectionSnapshot Selection { get; }
}

/// <summary>Pure word-action policy. Clipboard/browser effects belong to the app host.</summary>
public static class WordActions
{
    const string SearchError = "Enter an absolute HTTP or HTTPS search URL with a valid host.";

    public static List<WordMenuActionPref> Defaults() =>
    [
        new() { Kind = WordMenuActionKind.Clipboard, Title = "Copy" },
        new() { Kind = WordMenuActionKind.ClipboardAll, Title = "Copy All" },
        new() { Kind = WordMenuActionKind.Search, Title = "Search Google", Url = "https://www.google.com/search?q=%w" },
        new() { Kind = WordMenuActionKind.Search, Title = "Search Wiktionary", Url = "https://en.wiktionary.org/wiki/Special:Search?search=%w&go=Look+up" },
        new() { Kind = WordMenuActionKind.Search, Title = "Search Longman", Url = "https://www.ldoceonline.com/search/english/direct/?q=%w" },
    ];

    /// <summary>Old files get defaults; explicit empty lists stay empty, and malformed rows remain inert.</summary>
    public static List<WordMenuActionPref> NormalizeRows(IEnumerable<WordMenuActionPref>? raw)
    {
        if (raw == null) return Defaults();
        List<WordMenuActionPref> rows = [];
        foreach (WordMenuActionPref? source in raw)
        {
            if (source == null) continue;
            WordMenuActionPref row = source.Clone();
            row.Title = row.Title?.Trim() ?? "";
            row.Url = row.Url?.Trim() ?? "";
            if (!Enum.IsDefined(row.Kind)) row.Kind = WordMenuActionKind.Unknown;
            if (row.Kind == WordMenuActionKind.Unknown || string.IsNullOrWhiteSpace(row.Title)
                || (row.Kind == WordMenuActionKind.Search && !TryTemplateUri(row.Url, out _)))
                row.IsEnabled = false;
            if (row.Kind is not (WordMenuActionKind.Clipboard or WordMenuActionKind.ClipboardAll)) row.ToLower = false;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Validate even disabled draft rows before persistence and return a canonical independent candidate.</summary>
    public static bool TryValidateRows(IEnumerable<WordMenuActionPref> raw, out List<WordMenuActionPref> rows, out string error)
    {
        rows = [];
        error = "";
        if (raw == null) { error = "The word menu draft could not be read."; return false; }
        foreach (WordMenuActionPref? source in raw)
        {
            if (source == null || source.Kind == WordMenuActionKind.Unknown || !Enum.IsDefined(source.Kind))
            {
                error = "Choose Search, Clipboard or ClipboardAll for every word menu action.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(source.Title))
            {
                error = "Give every word menu action a title.";
                return false;
            }
            WordMenuActionPref row = source.Clone();
            row.Title = row.Title.Trim();
            row.Url = row.Url?.Trim() ?? "";
            if (row.Kind == WordMenuActionKind.Search && !TryTemplateUri(row.Url, out _))
            {
                error = SearchError;
                return false;
            }
            if (row.Kind is not (WordMenuActionKind.Clipboard or WordMenuActionKind.ClipboardAll)) row.ToLower = false;
            rows.Add(row);
        }
        return true;
    }

    public static bool TryExpandSearch(string template, WordSelectionSnapshot selection, out Uri? uri, out string error)
    {
        uri = null;
        error = SearchError;
        if (!TryTemplateUri(template, out _)) return false;
        // One pass recognizes %lw before %w; inserted text can never be interpreted as another template token.
        string expanded = Regex.Replace(template.Trim(), "%lw|%w|%s", match => Uri.EscapeDataString(match.Value switch
        {
            "%lw" => selection.Word.ToLower(),
            "%w" => selection.Word,
            _ => selection.Text,
        }));
        if (!Uri.TryCreate(expanded, UriKind.Absolute, out Uri? candidate) || !IsHttpUri(candidate)) return false;
        uri = candidate;
        error = "";
        return true;
    }

    public static bool IsHttpUri(Uri uri)
        => uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host);

    static bool TryTemplateUri(string? template, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(template)) return false;
        string value = template.Trim();
        if (value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) return false;
        string probe = Regex.Replace(value, "%lw|%w|%s", "word");
        // Uri tolerates malformed percent escapes by escaping the percent itself; reject those raw drafts.
        for (int i = 0; i < probe.Length; i++)
        {
            if (probe[i] != '%') continue;
            if (i + 2 >= probe.Length || !Uri.IsHexDigit(probe[i + 1]) || !Uri.IsHexDigit(probe[i + 2])) return false;
            i += 2;
        }
        return Uri.TryCreate(probe, UriKind.Absolute, out uri) && IsHttpUri(uri);
    }

    public static bool IsWordSearch(WordMenuActionPref row)
        => row.IsEnabled && row.Kind == WordMenuActionKind.Search && !string.IsNullOrWhiteSpace(row.Title)
            && (row.Url.Contains("%w", StringComparison.Ordinal) || row.Url.Contains("%lw", StringComparison.Ordinal))
            && TryTemplateUri(row.Url, out _);

    public static bool MatchesModifier(WordSearchModifier expected, KeyModifiers actual) => expected switch
    {
        WordSearchModifier.Control => actual == KeyModifiers.Control,
        WordSearchModifier.Shift => actual == KeyModifiers.Shift,
        WordSearchModifier.Alt => actual == KeyModifiers.Alt,
        WordSearchModifier.None => actual == KeyModifiers.None,
        _ => false,
    };

    public static string BuildAutoCopyText(SubtitleAutoCopyTarget target, string primary, string secondary)
    {
        bool hasPrimary = !string.IsNullOrWhiteSpace(primary), hasSecondary = !string.IsNullOrWhiteSpace(secondary);
        return target switch
        {
            SubtitleAutoCopyTarget.Primary => hasPrimary ? primary : "",
            SubtitleAutoCopyTarget.Secondary => hasSecondary ? secondary : "",
            SubtitleAutoCopyTarget.All when hasPrimary && hasSecondary => primary + Environment.NewLine + "( " + secondary + " )",
            SubtitleAutoCopyTarget.All when hasPrimary => primary,
            SubtitleAutoCopyTarget.All when hasSecondary => secondary,
            _ => "",
        };
    }
}

static class WordEnumJson
{
    public static T Read<T>(ref Utf8JsonReader reader, T fallback) where T : struct, Enum
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? raw = reader.GetString();
            string? name = Enum.GetNames<T>().FirstOrDefault(value => string.Equals(value, raw, StringComparison.OrdinalIgnoreCase));
            if (name != null) return Enum.Parse<T>(name);
        }
        reader.Skip();
        return fallback;
    }
    public static void Write<T>(Utf8JsonWriter writer, T value, T fallback) where T : struct, Enum
        => writer.WriteStringValue((Enum.IsDefined(value) ? value : fallback).ToString());
}

public sealed class WordMenuActionKindConverter : JsonConverter<WordMenuActionKind>
{
    public override WordMenuActionKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => WordEnumJson.Read(ref reader, WordMenuActionKind.Unknown);
    public override void Write(Utf8JsonWriter writer, WordMenuActionKind value, JsonSerializerOptions options)
        => WordEnumJson.Write(writer, value, WordMenuActionKind.Unknown);
}

public sealed class WordSearchModifierConverter : JsonConverter<WordSearchModifier>
{
    public override WordSearchModifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => WordEnumJson.Read(ref reader, WordSearchModifier.Control);
    public override void Write(Utf8JsonWriter writer, WordSearchModifier value, JsonSerializerOptions options)
        => WordEnumJson.Write(writer, value, WordSearchModifier.Control);
}

public sealed class SubtitleAutoCopyTargetConverter : JsonConverter<SubtitleAutoCopyTarget>
{
    public override SubtitleAutoCopyTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => WordEnumJson.Read(ref reader, SubtitleAutoCopyTarget.Primary);
    public override void Write(Utf8JsonWriter writer, SubtitleAutoCopyTarget value, JsonSerializerOptions options)
        => WordEnumJson.Write(writer, value, SubtitleAutoCopyTarget.Primary);
}

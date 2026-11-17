using System.Text.Json;
using LLPlayer.Avalonia.Services;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;

namespace LLPlayer.Core.Automation;

public sealed record ControlSettingsDraft(ControlSettingsPatch Patch, IReadOnlyDictionary<string, JsonElement> Original,
    Dictionary<string, string?>? CredentialChanges = null)
{
    public bool HasChanges => Patch.Fields.Count != 0 || CredentialChanges is { Count: > 0 };
}

public sealed partial class ControlSettingsService
{
    public static ControlSettingsDraft CreateDraft(RecognitionSettings original, RecognitionSettings edited)
        => CreateDraft("recognition", RecognitionFields(), original, edited);

    public static ControlSettingsDraft CreateDraft(DubbingSettings original, DubbingSettings edited)
        => CreateDraft("dubbing", DubbingFields(), original, edited);

    public static ControlSettingsDraft CreateDraft(BatchSettings original, BatchSettings edited)
        => CreateDraft("batch", BatchFields(), original, edited);

    public static ControlSettingsDraft CreateDraft(PdicSettings original, PdicSettings edited)
        => CreateDraft("pdic", PdicFields(), original, edited);

    public static ControlSettingsDraft CreateDraft(TranslationSettings original, TranslationSettings edited)
        => CreateDraft("translation", TranslationFields(), original, edited, includePrivate: true);

#if !WINDOWS
    public static ControlSettingsDraft CreateDraft(PlaybackSettings original, PlaybackSettings edited)
        => CreateDraft("playback", PlaybackFields(), original, edited);

    public static ControlSettingsDraft CreateDraft(OnlineVideoSettings original, OnlineVideoSettings edited)
        => CreateDraft("online_video", OnlineVideoFields(), original, edited, includePrivate: true);
#endif

    static ControlSettingsDraft CreateDraft<T>(string family, SettingsFields<T> fields, T original, T edited, bool includePrivate = false)
    {
        var baseline = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var changes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, field) in fields.Options)
        {
            var read = includePrivate ? field.ReadPrivate : field.Read;
            var before = read(original);
            var after = read(edited);
            if (JsonElement.DeepEquals(before, after)) continue;
            baseline.Add(name, before);
            changes.Add(name, after);
        }
        Dictionary<string, string?>? credentials = null;
        if (includePrivate)
        {
            credentials = new(StringComparer.Ordinal);
            foreach (var (name, field) in fields.Credentials)
            {
                string? before = field.Read(original), after = field.Read(edited);
                if (!string.Equals(before, after, StringComparison.Ordinal))
                    credentials[name] = string.IsNullOrWhiteSpace(after) ? null : after;
            }
        }
        return new(new(family, changes), baseline, credentials);
    }
}

using FlyleafLib;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SettingsPlaybackLanguageIdentityTests
{
    [Fact]
    public void Regional_and_alias_codes_share_the_engine_language_identity_in_the_ordered_editor()
    {
        var editor = new PlaybackLanguageEditor(["en-US", "ja", "eng"]);
        Assert.Equal(["en", "ja"], editor.SelectedLanguages.Select(language => language.Code));
        Assert.DoesNotContain(editor.AvailableLanguages, language => Language.Get(language.Code) == Language.English);
    }

    [Fact]
    public void Loading_legacy_aliases_canonicalizes_and_deduplicates_in_priority_order_without_rewriting()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "playback.json");
        const string json = "{\"AudioLanguages\":[\"en-US\",\"eng\",\"ja\",\"en\"],\"SubtitleLanguages\":[\"ru-RU\",\"rus\",\"ja\"]}";
        File.WriteAllText(path, json); var store = new PlaybackSettingsStore(path); PlaybackSettings settings = store.Load();
        Assert.Null(store.LoadWarning); Assert.Equal(["en", "ja"], settings.AudioLanguages);
        Assert.Equal(["ru", "ja"], settings.SubtitleLanguages); Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void Direct_duplicate_engine_languages_are_rejected_before_save()
        => Assert.Throws<ArgumentException>(() => new PlaybackSettings { AudioLanguages = ["en-US", "en"] }.Validate());

    [Fact]
    public async Task Regional_fallback_codes_remain_selected_and_are_preserved_by_an_unrelated_save()
    {
        PlaybackSettings? saved = null;
        var initial = new PlaybackSettings { LanguageFallbackPrimary = "en-US", LanguageFallbackSecondary = "zh-Hant", LanguageFallbackSecondarySame = false };
        var model = new PlaybackSettingsViewModel(initial, new(), (edited, _, _, _) => { saved = edited; return Task.CompletedTask; });
        Assert.Equal("en-US", model.PrimaryFallbackLanguage!.Code);
        Assert.Equal("zh-Hant", model.SecondaryFallbackLanguage!.Code);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Null(model.Error); Assert.Equal("en-US", saved!.LanguageFallbackPrimary);
        Assert.Equal("zh-Hant", saved.LanguageFallbackSecondary);
    }

    [Fact]
    public async Task Unchanged_legacy_editor_preserves_a_fresh_live_language_priority()
    {
        PlaybackSettings? merged = null;
        var initial = new PlaybackSettings { AudioLanguages = ["en-US"] };
        var fresh = new PlaybackSettings { AudioLanguages = ["ru", "ja"] };
        var model = new PlaybackSettingsViewModel(initial, new(), (edited, baseline, _, _) =>
        { merged = edited.MergeEdits(baseline, fresh); return Task.CompletedTask; });
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Null(model.Error); Assert.Equal(["ru", "ja"], merged!.AudioLanguages);
    }
}

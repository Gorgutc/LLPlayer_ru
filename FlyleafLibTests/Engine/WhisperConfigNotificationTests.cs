using System.Collections.Generic;
using AwesomeAssertions;

namespace FlyleafLib;

// WhisperConfig exposes a derived UI string LanguageName ("Auto, Trans" / "English" …). Its Language,
// LanguageDetection and Translate setters must raise PropertyChanged for "LanguageName" so the ASR menu
// title re-renders (HC-07). The setters previously called Raise(LanguageName) — passing the property VALUE
// as the name — so the notification went out under the wrong name and the UI never refreshed.
public class WhisperConfigNotificationTests
{
    static List<string> RaisedNames(WhisperConfig cfg, System.Action mutate)
    {
        var names = new List<string>();
        cfg.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
        mutate();
        return names;
    }

    [Fact]
    public void SettingLanguage_RaisesLanguageName()
    {
        var cfg = new WhisperConfig { LanguageDetection = false };
        var names = RaisedNames(cfg, () => cfg.Language = "fr");
        names.Should().Contain("LanguageName");
    }

    [Fact]
    public void SettingLanguageDetection_RaisesLanguageName()
    {
        var cfg = new WhisperConfig { LanguageDetection = false };
        var names = RaisedNames(cfg, () => cfg.LanguageDetection = true);
        names.Should().Contain("LanguageName");
    }

    [Fact]
    public void SettingTranslate_RaisesLanguageName()
    {
        var cfg = new WhisperConfig();
        var names = RaisedNames(cfg, () => cfg.Translate = true);
        names.Should().Contain("LanguageName");
    }
}

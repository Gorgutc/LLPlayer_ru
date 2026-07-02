using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib;

// Characterization of the config Clone surface (HC-40). These pin the CURRENT behavior of the hand-written
// Clone methods so any later change — a real fix or an accidental regression — is caught. Two document a known
// shared-state issue (SubtitlesConfig.Clone MemberwiseClone's the SubConfigs array, so cloned and source
// SubConfig instances alias); HC-40 tracks the fix-vs-deprecate decision (owner). One documents the intentional
// KeysConfig.Clone Keys=null (repopulated later in Player.SetPlayer/LoadDefault). All are Engine-free: they call
// the nested Clone directly, never Config.Clone (which rebuilds plugin options via the full Config ctor).
public class ConfigCloneTests
{
    [Fact]
    public void SubtitlesConfig_Clone_SharesSubConfigsArray()
    {
        // CURRENT (HC-40, shared-state): Clone MemberwiseClone's + only deep-copies Languages, so the SubConfigs
        // array reference is shared with the source. (Seed Languages so Clone's lazy getter doesn't hit
        // GetSystemLanguages(), which NREs in headless runs.)
        Config.SubtitlesConfig src = new() { Languages = [Language.English] };
        Config.SubtitlesConfig clone = src.Clone();

        ReferenceEquals(clone.SubConfigs, src.SubConfigs).Should().BeTrue();
    }

    [Fact]
    public void SubtitlesConfig_Clone_SubConfigMutationLeaksToSource()
    {
        // CURRENT (HC-40, shared-state): the SubConfigs array (and its elements) being shared means mutating the
        // clone's per-track SubConfig bleeds into the source.
        Config.SubtitlesConfig src = new() { Languages = [Language.English] };
        Config.SubtitlesConfig clone = src.Clone();

        clone.SubConfigs[0].Visible = false; // default true
        src.SubConfigs[0].Visible.Should().BeFalse();
    }

    [Fact]
    public void SubtitlesConfig_Clone_LanguagesIsSeparateList()
    {
        // Contrast: Languages IS deep-copied, so mutating the clone's list does not touch the source.
        Config.SubtitlesConfig src = new() { Languages = [Language.English] };
        Config.SubtitlesConfig clone = src.Clone();

        clone.Languages.Add(Language.Get("de"));
        src.Languages.Should().ContainSingle().Which.Should().Be(Language.English);
    }

    [Fact]
    public void KeysConfig_Clone_NullsKeys()
    {
        // BY DESIGN (not a bug): KeysConfig.Clone nulls Keys; it is repopulated in Player.SetPlayer/LoadDefault.
        KeysConfig src = new() { Keys = [new KeyBinding()] };

        src.Clone().Keys.Should().BeNull();
    }
}

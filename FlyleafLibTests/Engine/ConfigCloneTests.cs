using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib;

// Config Clone surface tests (HC-40, variant A applied session #21). Two assert the SubtitlesConfig.Clone deep-copy
// fix: the SubConfigs array and its elements are now cloned, so per-track mutations on the clone no longer alias the
// source. One confirms Languages is a separate list; one documents the intentional KeysConfig.Clone Keys=null
// (repopulated later in Player.SetPlayer/LoadDefault). All are Engine-free: they call the nested Clone directly, never
// Config.Clone (which rebuilds plugin options via the full Config ctor; its Version-carry + deprecation note live in
// code, not unit-tested here to avoid Engine init).
public class ConfigCloneTests
{
    [Fact]
    public void SubtitlesConfig_Clone_DeepCopiesSubConfigsArray()
    {
        // HC-40 (fixed): Clone deep-copies the SubConfigs array, so the clone owns a distinct array instance.
        // (Seed Languages so Clone's lazy getter doesn't hit GetSystemLanguages(), which NREs in headless runs.)
        Config.SubtitlesConfig src = new() { Languages = [Language.English] };
        Config.SubtitlesConfig clone = src.Clone();

        ReferenceEquals(clone.SubConfigs, src.SubConfigs).Should().BeFalse();
    }

    [Fact]
    public void SubtitlesConfig_Clone_SubConfigMutationDoesNotLeakToSource()
    {
        // HC-40 (fixed): the SubConfigs array and its elements are deep-copied, so mutating the clone's per-track
        // SubConfig no longer bleeds into the source.
        Config.SubtitlesConfig src = new() { Languages = [Language.English] };
        Config.SubtitlesConfig clone = src.Clone();

        clone.SubConfigs[0].Visible = false; // default true
        src.SubConfigs[0].Visible.Should().BeTrue();
        clone.SubConfigs[0].Visible.Should().BeFalse();
    }

    [Fact]
    public void SubtitlesConfig_Clone_SubConfigElementsAreDistinctInstances()
    {
        // HC-40 (fixed): each SubConfig element is cloned, not just the array — clone[i] and src[i] are distinct
        // references, which is what stops per-track mutations from aliasing (guards element-level deep copy).
        Config.SubtitlesConfig src = new() { Languages = [Language.English] };
        Config.SubtitlesConfig clone = src.Clone();

        for (int i = 0; i < src.SubConfigs.Length; i++)
            ReferenceEquals(clone.SubConfigs[i], src.SubConfigs[i]).Should().BeFalse();
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

using AwesomeAssertions;

namespace FlyleafLib;

// HC-16: these two subtitle toggles are declared a frozen "default false -> byte-identical" boundary
// (T-10 per-segment language since 0.3.32; F-16 per-line voice persistence since 0.3.37). verify-frozen.ps1
// greps the source literal `= false`; these pin the RUNTIME default too, so an accidental flip to true fails a
// unit test as well as the frozen gate — the belt-and-suspenders the audit (HC-16) asked for.
public class FrozenConfigDefaultsTests
{
    [Fact]
    public void ASRPerSegmentLanguage_DefaultsToFalse()
    {
        new Config.SubtitlesConfig().ASRPerSegmentLanguage.Should().BeFalse();
    }

    [Fact]
    public void PersistPerLineVoices_DefaultsToFalse()
    {
        new Config.SubtitlesConfig().PersistPerLineVoices.Should().BeFalse();
    }
}

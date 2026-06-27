using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using FlyleafLib;

namespace FlyleafLib;

public class VcRedistCheckerTests
{
    // ---- EvaluateMissing (pure) -------------------------------------------------------------

    [Fact]
    public void EvaluateMissing_ReturnsEmpty_WhenEveryModuleLoads()
    {
        var missing = VcRedistChecker.EvaluateMissing(
            VcRedistChecker.RequiredModules, _ => true);

        missing.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateMissing_ReturnsAll_InInputOrder_WhenNoneLoad()
    {
        var missing = VcRedistChecker.EvaluateMissing(
            VcRedistChecker.RequiredModules, _ => false);

        missing.Should().Equal(VcRedistChecker.RequiredModules);
    }

    [Fact]
    public void EvaluateMissing_ReturnsOnlyTheUnloadableModules_PreservingOrder()
    {
        string[] modules = ["a.dll", "b.dll", "c.dll", "d.dll"];
        var unloadable = new HashSet<string>(["b.dll", "d.dll"]);

        var missing = VcRedistChecker.EvaluateMissing(modules, m => !unloadable.Contains(m));

        missing.Should().Equal("b.dll", "d.dll");
    }

    [Fact]
    public void EvaluateMissing_IsolatesEachModule_OnlyTheMissingCrtRuntimeIsReported()
    {
        // Realistic VC++-too-old scenario: the 2015-era CRT (vcruntime140 / msvcp140) is present but the
        // 2019+ vcruntime140_1 — required by modern whisper.cpp / Tesseract — is not.
        var missing = VcRedistChecker.EvaluateMissing(
            VcRedistChecker.RequiredModules, m => m != "vcruntime140_1.dll");

        missing.Should().ContainSingle().Which.Should().Be("vcruntime140_1.dll");
    }

    [Fact]
    public void EvaluateMissing_Throws_OnNullArguments()
    {
        Action nullModules = () => VcRedistChecker.EvaluateMissing(null!, _ => true);
        Action nullProbe = () => VcRedistChecker.EvaluateMissing(VcRedistChecker.RequiredModules, null!);

        nullModules.Should().Throw<ArgumentNullException>();
        nullProbe.Should().Throw<ArgumentNullException>();
    }

    // ---- IsRuntimePresent (platform probe) --------------------------------------------------

    [Fact]
    public void IsRuntimePresent_ReturnValue_AgreesWithMissingCount()
    {
        // Environment-independent invariant: "present" is exactly "nothing missing", whether or not the
        // redistributable happens to be installed on this machine. Must not throw.
        bool present = VcRedistChecker.IsRuntimePresent(out var missing);

        missing.Should().NotBeNull();
        present.Should().Be(missing.Count == 0);
        // Any reported module must come from the required set.
        missing.Should().OnlyContain(m => VcRedistChecker.RequiredModules.Contains(m));
    }

    // ---- Metadata / message -----------------------------------------------------------------

    [Fact]
    public void RequiredModules_IncludeThe2019PlusDiscriminator()
    {
        VcRedistChecker.RequiredModules.Should().NotBeEmpty();
        // vcruntime140_1.dll (added in VC++ 2019) distinguishes a modern redistributable from an absent/legacy
        // one; its absence is the signal the preflight relies on.
        VcRedistChecker.RequiredModules.Should().Contain("vcruntime140_1.dll");
    }

    [Fact]
    public void DownloadUrl_IsTheMicrosoftPermalink()
    {
        VcRedistChecker.DownloadUrl.Should().Be("https://aka.ms/vs/17/release/vc_redist.x64.exe");
    }

    [Theory]
    [InlineData("Speech-to-text (whisper.cpp)")]
    [InlineData("Tesseract OCR")]
    public void BuildMissingMessage_NamesTheFeature_AndCarriesTheDownloadUrl(string feature)
    {
        string message = VcRedistChecker.BuildMissingMessage(feature);

        message.Should().Contain(feature);
        message.Should().Contain("Visual C++");
        message.Should().Contain(VcRedistChecker.DownloadUrl);
    }
}

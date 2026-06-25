using AwesomeAssertions;
using FlyleafLib;

namespace FlyleafLib;

public class InformationalVersionTests
{
    [Theory]
    [InlineData("0.3.7+abc123", "0.3.7", "abc123")]  // short placeholder SHA
    [InlineData("0.3.7+1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b", "0.3.7", "1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b")] // realistic 40-char git SHA
    [InlineData("0.3.7", "0.3.7", "")]               // SHA-less publish build (the B-01 crash case)
    [InlineData("0.3.7+a+b", "0.3.7", "a+b")]        // build metadata may itself contain '+'
    [InlineData("0.3.7+", "0.3.7", "")]              // trailing '+' with empty metadata
    [InlineData("+abc", "", "abc")]                  // empty version, non-empty hash
    [InlineData(" 0.3.7 ", " 0.3.7 ", "")]           // verbatim — the parser does not trim
    [InlineData("", "", "")]                         // degenerate-but-non-null: still must not throw
    public void Parse_SplitsVersionAndCommitHash(string productVersion, string expectedVersion, string expectedCommit)
    {
        var (version, commitHash) = InformationalVersion.Parse(productVersion);

        version.Should().Be(expectedVersion);
        commitHash.Should().Be(expectedCommit);
    }

    [Fact]
    public void Parse_DoesNotThrow_WhenNoCommitHashSuffix()
    {
        // Regression for B-01: a SHA-less publish build (ProductVersion "0.3.7", no "+sha") must not throw,
        // or the app hard-exits at startup — FlyleafLoader reads App.Version inside a try/catch that calls
        // Environment.Exit(1) ("Cannot load ... delete the config file"), and batch-defaults save / About break.
        Action act = () => InformationalVersion.Parse("0.3.7");

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_Throws_OnNull()
    {
        Action act = () => InformationalVersion.Parse(null);

        act.Should().Throw<ArgumentNullException>();
    }
}

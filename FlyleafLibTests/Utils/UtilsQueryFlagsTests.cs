using AwesomeAssertions;

namespace FlyleafLib;

// Coverage for the remaining pure Utils.cs helpers: ParseQueryString (hand-rolled '&'/'=' splitter),
// GetFlagsAsList/GetFlagsAsString ([Flags] enumeration), GetRecInnerException (InnerException walk capped at 4),
// and GetDumpMetadata (null/empty/single-entry only - multi-entry layout depends on dictionary iteration order
// and is intentionally left unasserted to avoid brittleness).
public class UtilsQueryFlagsTests
{
    [Fact]
    public void ParseQueryString_KeyValuePairs()
    {
        var dict = Utils.ParseQueryString("a=1&b=2");
        dict.Should().HaveCount(2);
        dict["a"].Should().Be("1");
        dict["b"].Should().Be("2");
    }

    [Fact]
    public void ParseQueryString_KeyWithoutEquals_GetsNullValue()
    {
        var dict = Utils.ParseQueryString("a&b=2");
        dict["a"].Should().BeNull();
        dict["b"].Should().Be("2");
    }

    [Fact]
    public void ParseQueryString_EmptyValue_IsEmptyStringNotNull()
    {
        var dict = Utils.ParseQueryString("a=&b=3");
        dict["a"].Should().Be("");
        dict["b"].Should().Be("3");
    }

    [Fact]
    public void ParseQueryString_Empty_ReturnsEmptyDictionary()
    {
        Utils.ParseQueryString("").Should().BeEmpty();
    }

    [Fact]
    public void ParseQueryString_TrailingAmpersand_IgnoresEmptyTail()
    {
        var dict = Utils.ParseQueryString("a=1&");
        dict.Should().HaveCount(1);
        dict["a"].Should().Be("1");
        dict.Should().NotContainKey("");
    }

    [Flags]
    private enum Flag
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4
    }

    [Fact]
    public void GetFlagsAsList_ReturnsSetFlagsExcludingNone()
    {
        Utils.GetFlagsAsList(Flag.A | Flag.C).Should().Equal(Flag.A, Flag.C);
        Utils.GetFlagsAsList(Flag.B).Should().Equal(Flag.B);
        Utils.GetFlagsAsList(Flag.None).Should().BeEmpty(); // None is filtered out by name
    }

    [Fact]
    public void GetFlagsAsString_JoinsWithSeparator()
    {
        Utils.GetFlagsAsString(Flag.A | Flag.B).Should().Be("A | B");           // default separator " | "
        Utils.GetFlagsAsString(Flag.C).Should().Be("C");
        Utils.GetFlagsAsString(Flag.A | Flag.B | Flag.C, "/").Should().Be("A/B/C");
        Utils.GetFlagsAsString(Flag.None).Should().BeNull();                    // empty list -> null
    }

    [Fact]
    public void GetRecInnerException_NoInner_ReturnsEmpty()
    {
        Utils.GetRecInnerException(new Exception("outer")).Should().Be("");
    }

    [Fact]
    public void GetRecInnerException_WalksChain()
    {
        var ex = new Exception("outer", new Exception("i1", new Exception("i2")));
        Utils.GetRecInnerException(ex).Should().Be("\r\n - i1\r\n - i2");
    }

    [Fact]
    public void GetRecInnerException_CapsAtFourLevels()
    {
        // Build outer -> i1 -> i2 -> i3 -> i4 -> i5; only the first four inner messages are emitted.
        var ex = new Exception("outer",
            new Exception("i1", new Exception("i2", new Exception("i3",
                new Exception("i4", new Exception("i5"))))));
        Utils.GetRecInnerException(ex).Should().Be("\r\n - i1\r\n - i2\r\n - i3\r\n - i4");
    }

    [Fact]
    public void GetDumpMetadata_NullOrEmpty_ReturnsEmpty()
    {
        Utils.GetDumpMetadata(null).Should().Be("");
        Utils.GetDumpMetadata(new Dictionary<string, string>()).Should().Be("");
    }

    [Fact]
    public void GetDumpMetadata_SingleEntry()
    {
        var meta = new Dictionary<string, string> { ["A"] = "1" };
        Utils.GetDumpMetadata(meta).Should().Be("\t[Metadata] A: 1");
    }
}

using MemLeakInspector.Utils;
using Xunit;

namespace MemLeakInspector.Tests;

public class AsciiGraphTests
{
    [Fact]
    public void Bar_FullWidth_WhenValueEqualsMax()
    {
        string bar = AsciiGraph.Bar(100, 100, 10);
        Assert.Equal("[##########]", bar);
    }

    [Fact]
    public void Bar_Empty_WhenValueIsZero()
    {
        string bar = AsciiGraph.Bar(0, 100, 10);
        Assert.Equal("[          ]", bar);
    }

    [Fact]
    public void Bar_HalfWidth_WhenValueIsHalfMax()
    {
        string bar = AsciiGraph.Bar(50, 100, 10);
        Assert.Equal("[#####     ]", bar);
    }

    [Fact]
    public void Bar_HandlesZeroMax_Gracefully()
    {
        string bar = AsciiGraph.Bar(0, 0, 10);
        Assert.Equal("[          ]", bar);
    }
}

public class SafeFileNameTests
{
    [Fact]
    public void Sanitize_RemovesInvalidChars()
    {
        string result = SafeFileName.Sanitize("file<>name:test");
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain(":", result);
    }

    [Fact]
    public void Sanitize_CollapsesUnderscores()
    {
        string result = SafeFileName.Sanitize("a///b");
        Assert.DoesNotContain("__", result);
    }

    [Fact]
    public void Sanitize_TruncatesLongNames()
    {
        string input = new string('a', 200);
        string result = SafeFileName.Sanitize(input);
        Assert.True(result.Length <= 100);
    }

    [Fact]
    public void Sanitize_ReturnsUnnamed_ForEmpty()
    {
        Assert.Equal("unnamed", SafeFileName.Sanitize(""));
        Assert.Equal("unnamed", SafeFileName.Sanitize("   "));
    }

    [Fact]
    public void Sanitize_PreservesValidChars()
    {
        string result = SafeFileName.Sanitize("snapshot-2025_01_01");
        Assert.Equal("snapshot-2025_01_01", result);
    }
}

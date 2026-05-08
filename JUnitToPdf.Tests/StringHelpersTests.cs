using JUnitToPdf;
using Xunit;

namespace JUnitToPdf.Tests;

public class StringHelpersTests
{
    // ---- Clip ----

    [Fact]
    public void Clip_ShortString_ReturnsUnchanged()
        => Assert.Equal("hello", StringHelpers.Clip("hello", 10));

    [Fact]
    public void Clip_ExactLength_ReturnsUnchanged()
        => Assert.Equal("hello", StringHelpers.Clip("hello", 5));

    [Fact]
    public void Clip_TooLong_AppendsEllipsis()
        => Assert.Equal("hel…", StringHelpers.Clip("hello", 3));

    [Fact]
    public void Clip_NullInput_TreatsAsEmpty()
        => Assert.Equal("", StringHelpers.Clip(null, 5));

    // ---- NormalizeWhitespace ----

    [Fact]
    public void NormalizeWhitespace_CollapsesTabs()
        => Assert.Equal("a b", StringHelpers.NormalizeWhitespace("a\t\tb"));

    [Fact]
    public void NormalizeWhitespace_TrimsEnds()
        => Assert.Equal("hello", StringHelpers.NormalizeWhitespace("  hello  "));

    [Fact]
    public void NormalizeWhitespace_AllWhitespace_ReturnsEmpty()
        => Assert.Equal("", StringHelpers.NormalizeWhitespace("   \t\n  "));

    [Fact]
    public void NormalizeWhitespace_CollapsesNewlines()
        => Assert.Equal("line1 line2", StringHelpers.NormalizeWhitespace("line1\r\nline2"));

    // ---- PrettyIdentifier ----

    [Fact]
    public void PrettyIdentifier_UnderscoresToSpaces()
        => Assert.Equal("My Test Method", StringHelpers.PrettyIdentifier("My_Test_Method", spaceAfterDots: false));

    [Fact]
    public void PrettyIdentifier_CamelCaseSplit()
        => Assert.Equal("My Test Method", StringHelpers.PrettyIdentifier("MyTestMethod", spaceAfterDots: false));

    [Fact]
    public void PrettyIdentifier_SpaceAfterDots()
        => Assert.Contains(". ", StringHelpers.PrettyIdentifier("My.Namespace.Class", spaceAfterDots: true));

    [Fact]
    public void PrettyIdentifier_NoSpaceAfterDots()
        => Assert.DoesNotContain(". ", StringHelpers.PrettyIdentifier("My.Namespace.Class", spaceAfterDots: false));

    [Fact]
    public void PrettyIdentifier_EmptyString_ReturnsEmpty()
        => Assert.Equal("", StringHelpers.PrettyIdentifier("", spaceAfterDots: false));

    [Fact]
    public void PrettyIdentifier_WhitespaceOnly_ReturnsEmpty()
        => Assert.Equal("", StringHelpers.PrettyIdentifier("   ", spaceAfterDots: false));

    // ---- ShortClassName ----

    [Fact]
    public void ShortClassName_ReturnsLastDotSegment()
        => Assert.Equal("MyTests", StringHelpers.ShortClassName("My.Long.Namespace.MyTests"));

    [Fact]
    public void ShortClassName_HandlesPlusForNestedClass()
        => Assert.Equal("Inner", StringHelpers.ShortClassName("Outer+Inner"));

    [Fact]
    public void ShortClassName_NullOrEmpty_ReturnsFallback()
        => Assert.Equal("(no classname)", StringHelpers.ShortClassName(null));

    [Fact]
    public void ShortClassName_NoSeparator_ReturnsWhole()
        => Assert.Equal("Plain", StringHelpers.ShortClassName("Plain"));
}

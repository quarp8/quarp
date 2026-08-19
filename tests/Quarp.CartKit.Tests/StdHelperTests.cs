using System.Reflection;
using Quarp.Api;
using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// Direct, isolated tests for the pure and near-pure members of <see cref="Std"/> — no
/// compile-and-run pipeline needed here, unlike <see cref="StdEquivalenceTests"/>, because
/// nothing below draws a full frame. M4 stage 4.1 adversary review (fix wave, cards В3/В6):
/// <list type="bullet">
///   <item>the two font-advance constants <see cref="Std"/> carries privately (it cannot
///     reference <c>Quarp.Core.SystemFont</c>/<c>SystemFontLarge</c> — <c>Quarp.Api</c> has no
///     project reference to <c>Quarp.Core</c>) never silently drift from the real metrics this
///     test project can see both of (card В3);</item>
///   <item><see cref="Std.PrintCentered(IConsoleApi,string,int,byte,Font)"/> and
///     <see cref="Std.PrintRight(IConsoleApi,string,int,byte,Font)"/> are exercised with
///     <see cref="Font.Large"/> for the first time anywhere in this test suite — every existing
///     demo and stand cartridge only ever asks for the default small font — against a hand
///     computed expectation built from <c>SystemFontLarge.CellWidth</c> (card В3);</item>
///   <item>every clamp and min/max, both the <c>int</c> and <see cref="Fix"/> overloads, gets a
///     direct table-driven test rather than being exercised only incidentally through a demo
///     cartridge, including the inverted-range (<c>min &gt; max</c>) case
///     <see cref="Std.Clamp(int,int,int)"/>'s doc comment now documents as a soft rule
///     (card В6).</item>
/// </list>
/// </summary>
public class StdHelperTests
{
    // --- В3: the two private font-advance constants must not drift from the real font metrics ---

    /// <summary>
    /// <see cref="Std"/>'s private <c>SmallAdvance</c> constant is read by reflection
    /// (<see cref="FieldInfo.GetRawConstantValue"/> — it is a <c>const</c>, so there is no
    /// storage to read any other way) rather than exercised only indirectly through a drawing
    /// call: a drift here is exactly the kind of two-copies-of-one-fact bug PLAYBOOK §3 calls
    /// out, and it would silently mis-center or mis-right-align every small-font call Std makes
    /// without ever showing up as a compile error.
    /// </summary>
    [Fact]
    public void SmallFontAdvanceMatchesSystemFontCellWidth()
    {
        Assert.Equal(SystemFont.CellWidth, ReadStdConstant("SmallAdvance"));
    }

    /// <summary>Same guard as <see cref="SmallFontAdvanceMatchesSystemFontCellWidth"/>, for <see cref="Font.Large"/>.</summary>
    [Fact]
    public void LargeFontAdvanceMatchesSystemFontLargeCellWidth()
    {
        Assert.Equal(SystemFontLarge.CellWidth, ReadStdConstant("LargeAdvance"));
    }

    private static int ReadStdConstant(string fieldName)
    {
        FieldInfo field = typeof(Std).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Std.{fieldName} not found by reflection -- was it renamed?");
        return (int)field.GetRawConstantValue()!;
    }

    // --- В3: Font.Large branch of PrintCentered/PrintRight, exercised for the first time ------

    /// <summary>
    /// No demo or existing test ever calls <see cref="Std.PrintCentered(IConsoleApi,string,int,byte,Font)"/>
    /// with <see cref="Font.Large"/> before this test — every real call site uses the default
    /// small-font overload. This runs it against a real <see cref="VirtualConsole"/> (which
    /// implements <see cref="IConsoleApi"/> directly, so no cartridge compile is needed) and
    /// checks the returned cursor position against the formula worked out by hand from
    /// <c>SystemFontLarge.CellWidth</c>, independent of whatever <see cref="Std"/>'s own private
    /// <c>Advance</c> helper computes.
    /// </summary>
    [Fact]
    public void PrintCenteredWithLargeFontMatchesManualFormula()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        const string text = "HELLO";
        int expectedX = (console.ScreenWidth - (text.Length * SystemFontLarge.CellWidth)) / 2;
        int expectedReturn = expectedX + (text.Length * SystemFontLarge.CellWidth);

        int actualReturn = Std.PrintCentered(console, text, 10, 7, Font.Large);

        Assert.Equal(expectedReturn, actualReturn);
    }

    /// <summary>Same idea as <see cref="PrintCenteredWithLargeFontMatchesManualFormula"/>, for the right-aligned formula (the "-1 bias" <see cref="Std.PrintRight(IConsoleApi,string,int,byte,Font)"/>'s doc comment now names explicitly).</summary>
    [Fact]
    public void PrintRightWithLargeFontMatchesManualFormula()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        const string text = "BEST";
        int expectedX = console.ScreenWidth - 1 - (text.Length * SystemFontLarge.CellWidth);
        int expectedReturn = expectedX + (text.Length * SystemFontLarge.CellWidth);

        int actualReturn = Std.PrintRight(console, text, 10, 7, Font.Large);

        Assert.Equal(expectedReturn, actualReturn);
    }

    // --- В6: direct tests of all six Clamp/Min/Max overloads, including the inverted range ----

    [Theory]
    [InlineData(5, 0, 10, 5)]        // inside the range: unchanged
    [InlineData(-5, 0, 10, 0)]       // below min: floors to min
    [InlineData(15, 0, 10, 10)]      // above max: ceilings to max
    [InlineData(0, 0, 10, 0)]        // exactly min
    [InlineData(10, 0, 10, 10)]      // exactly max
    [InlineData(5, 10, 0, 10)]       // inverted range (min > max): soft rule returns min
    [InlineData(-50, 10, 0, 10)]     // inverted range, value far below both bounds: still min
    [InlineData(50, 10, 0, 10)]      // inverted range, value far above both bounds: still min
    public void IntClampMatchesExpected(int value, int min, int max, int expected)
    {
        Assert.Equal(expected, Std.Clamp(value, min, max));
    }

    [Fact]
    public void IntMinAndMax()
    {
        Assert.Equal(3, Std.Min(3, 7));
        Assert.Equal(3, Std.Min(7, 3));
        Assert.Equal(7, Std.Max(3, 7));
        Assert.Equal(7, Std.Max(7, 3));
    }

    public static readonly TheoryData<Fix, Fix, Fix, Fix> FixClampCases = new()
    {
        { (Fix)5, (Fix)0, (Fix)10, (Fix)5 },                            // inside the range
        { (Fix)(-5), (Fix)0, (Fix)10, (Fix)0 },                         // below min
        { (Fix)15, (Fix)0, (Fix)10, (Fix)10 },                          // above max
        { Fix.Ratio(1, 2), (Fix)0, (Fix)1, Fix.Ratio(1, 2) },           // fractional value, inside
        { (Fix)5, (Fix)10, (Fix)0, (Fix)10 },                           // inverted range: soft rule returns min
        { (Fix)(-50), (Fix)10, (Fix)0, (Fix)10 },                       // inverted range, far below
    };

    [Theory]
    [MemberData(nameof(FixClampCases))]
    public void FixClampMatchesExpected(Fix value, Fix min, Fix max, Fix expected)
    {
        Assert.Equal(expected, Std.Clamp(value, min, max));
    }

    [Fact]
    public void FixMinAndMax()
    {
        Fix a = (Fix)3;
        Fix b = (Fix)7;
        Assert.Equal(a, Std.Min(a, b));
        Assert.Equal(a, Std.Min(b, a));
        Assert.Equal(b, Std.Max(a, b));
        Assert.Equal(b, Std.Max(b, a));
    }
}

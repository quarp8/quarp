using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// Fix edge semantics as drafted for API-8.md: overflow wraps (unchecked), division by
/// zero throws — both deterministic — plus invariant formatting.
/// </summary>
public class FixEdgeTests
{
    [Fact]
    public void OverflowWrapsDeterministically()
    {
        Assert.Equal(Fix.MinValue, Fix.MaxValue + Fix.FromRaw(1));
        Assert.Equal(Fix.MaxValue, Fix.MinValue - Fix.FromRaw(1));
        Assert.Equal(Fix.MinValue, -Fix.MinValue);      // two's-complement asymmetry wraps
        var big = Fix.FromRaw(int.MaxValue);
        Assert.Equal(Fix.FromRaw(unchecked((int)(((long)int.MaxValue * int.MaxValue) >> 16))), big * big);
    }

    [Fact]
    public void DivisionByZeroThrows()
    {
        Assert.Throws<DivideByZeroException>(() => Fix.One / Fix.Zero);
        Assert.Throws<DivideByZeroException>(() => Fix.Ratio(1, 0));
    }

    [Fact]
    public void RatioHandlesNegativesAndExactValues()
    {
        Assert.Equal(Fix.FromRaw(-32768), Fix.Ratio(-1, 2));
        Assert.Equal(Fix.FromRaw(-32768), Fix.Ratio(1, -2));
        Assert.Equal(Fix.Half, Fix.Ratio(-1, -2));
        Assert.Equal(Fix.FromRaw(21845), Fix.Ratio(1, 3));  // truncated toward zero
    }

    [Fact]
    public void ToStringIsInvariantAndCompact()
    {
        Assert.Equal("0.5", Fix.Half.ToString());
        Assert.Equal("-1.5", Fix.Ratio(-3, 2).ToString());
        Assert.Equal("3", ((Fix)3).ToString());
    }

    [Fact]
    public void EqualityAndHashAgree()
    {
        var a = Fix.Ratio(7, 4);
        var b = Fix.FromRaw(7 * 16384);
        Assert.True(a == b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a != b);
        Assert.Equal(0, a.CompareTo(b));
        Assert.True(Fix.Zero.CompareTo(Fix.One) < 0);
    }
}

using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

public class FixTests
{
    [Fact]
    public void HalfPlusHalfIsOne()
    {
        Assert.Equal(Fix.One, Fix.Ratio(1, 2) + Fix.Ratio(1, 2));
        Assert.Equal(Fix.Half, Fix.Ratio(1, 2));
    }

    [Fact]
    public void MultiplicationWorks()
    {
        Assert.Equal(Fix.Ratio(3, 2), (Fix)3 * Fix.Ratio(1, 2));
        Assert.Equal((Fix)6, (Fix)2 * (Fix)3);
    }

    [Fact]
    public void DivisionWorks()
    {
        Assert.Equal(Fix.Ratio(1, 4), Fix.Half / 2);
        Assert.Equal((Fix)2, (Fix)6 / (Fix)3);
    }

    [Fact]
    public void CastFloorsTowardNegativeInfinity()
    {
        Assert.Equal(1, (int)Fix.Ratio(3, 2));
        Assert.Equal(-2, (int)Fix.Ratio(-3, 2));
        Assert.Equal(-1, (int)Fix.Ratio(-1, 65536));
    }

    [Fact]
    public void ComparisonsWork()
    {
        Assert.True(Fix.Half < Fix.One);
        Assert.True((Fix)(-1) < Fix.Zero);
        Assert.True(Fix.Ratio(1, 3) > Fix.Ratio(1, 4));
    }
}

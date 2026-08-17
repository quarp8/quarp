using Xunit;

namespace Quarp.Core.Tests;

public class TestPatternTests
{
    [Fact]
    public void PatternFillsEveryPixelWithValidIndex()
    {
        var fb = new Framebuffer(ConsoleProfile.Profile8);
        TestPattern.Render(fb);
        Assert.All(fb.Pixels, p => Assert.True(p < Palette.MasterCount));
    }

    [Fact]
    public void VisibleAndSecretRowsAreAligned()
    {
        var fb = new Framebuffer(ConsoleProfile.Profile8);
        TestPattern.Render(fb);

        Assert.Equal(0, fb.Pixels[0]);                       // top-left = color 0
        Assert.Equal(15, fb.Pixels[127]);                    // top-right = color 15
        Assert.Equal(16, fb.Pixels[30 * fb.Width]);          // secret row under color 0
        Assert.Equal(31, fb.Pixels[30 * fb.Width + 127]);    // secret row under color 15
    }
}

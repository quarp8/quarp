using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The boot image is the console's first claim about its own screen, so these tests are written
/// against <c>fb.Width - 1</c> and <c>fb.Height - 1</c> and never against 127 or 71. The M4
/// move to 160x90 showed why: a pattern laid out in literals still filled a 128x72 rectangle on
/// the new console and left an L-shaped black margin down the right and bottom edges — and the
/// old tests, which read <c>Pixels[127]</c>, stayed green through all of it. Every assertion
/// below therefore names an edge rather than a coordinate.
/// <para>The second console needed for "one build, two answers" is now built here (ADR-021
/// retired the 8w spike): <see cref="ConsoleProfile"/> is data, so a test can construct sizes
/// that divide badly on purpose and check that the layout still tiles them exactly.</para>
/// </summary>
public class TestPatternTests
{
    /// <summary>
    /// Screens the pattern must fill: the real console, the historic M0 one, and two sizes
    /// chosen because nothing about them is round — 200 is not a multiple of 16 columns, 123 is
    /// odd and not divisible by 3, and 61x37 is small enough that whole ramp blocks collapse to
    /// zero width.
    /// </summary>
    public static TheoryData<int, int> Screens => new()
    {
        { 160, 90 },
        { 128, 72 },
        { 200, 123 },
        { 61, 37 },
    };

    private static Framebuffer RenderOn(int width, int height)
    {
        var fb = new Framebuffer(new ConsoleProfile { Name = "test", Width = width, Height = height });
        TestPattern.Render(fb);
        return fb;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void PatternFillsEveryPixelWithValidIndex(int width, int height)
    {
        Framebuffer fb = RenderOn(width, height);
        Assert.All(fb.Pixels, p => Assert.True(p < Palette.MasterCount));
    }

    /// <summary>
    /// The four corners carry the ends of the bands, so they are exactly where a layout pinned
    /// to a smaller screen stops painting: the top-right corner is the last visible color, the
    /// bottom-left the first blue step, the bottom-right the last skin/wood step.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void BothFarEdgesAreReachedOnAnyScreen(int width, int height)
    {
        Framebuffer fb = RenderOn(width, height);
        int last = fb.Width * fb.Height - 1;

        Assert.Equal(0, fb.Pixels[0]);                          // top-left: color 0
        Assert.Equal(15, fb.Pixels[fb.Width - 1]);              // top-right: color 15
        Assert.Equal(20, fb.Pixels[last - (fb.Width - 1)]);     // bottom-left: first blue step
        Assert.Equal(31, fb.Pixels[last]);                      // bottom-right: last skin/wood step
    }

    /// <summary>
    /// The direct statement of "no black L-band": an unpainted margin is a full column or a full
    /// row of color 0, and the image has neither — color 0 appears in the leftmost band and at
    /// the foot of the neutral ramp, but never for a whole line.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void NoWholeRowOrColumnIsLeftUnpainted(int width, int height)
    {
        Framebuffer fb = RenderOn(width, height);

        for (int y = 0; y < fb.Height; y++)
        {
            bool painted = false;
            for (int x = 0; x < fb.Width && !painted; x++)
            {
                painted = fb.Pixels[(y * fb.Width) + x] != 0;
            }
            Assert.True(painted, $"row {y} of {fb.Height} is entirely color 0");
        }

        for (int x = 0; x < fb.Width; x++)
        {
            bool painted = false;
            for (int y = 0; y < fb.Height && !painted; y++)
            {
                painted = fb.Pixels[(y * fb.Width) + x] != 0;
            }
            Assert.True(painted, $"column {x} of {fb.Width} is entirely color 0");
        }
    }

    /// <summary>
    /// Each secret color sits directly under its visible parent, edge to edge. The band is found
    /// by looking at the picture (the first row of column 0 that is no longer color 0) rather
    /// than by recomputing the layout arithmetic here — a test that recomputes the formula it
    /// checks agrees with itself, not with the image.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void SecretColorsStayAlignedUnderTheirParents(int width, int height)
    {
        Framebuffer fb = RenderOn(width, height);

        int secretRow = -1;
        for (int y = 1; y < fb.Height; y++)
        {
            if (fb.Pixels[y * fb.Width] == 16)
            {
                secretRow = y;
                break;
            }
        }
        Assert.True(secretRow > 0, "no row of secret colors under the visible ones");

        Assert.Equal(15, fb.Pixels[fb.Width - 1]);
        for (int x = 0; x < fb.Width; x++)
        {
            byte parent = fb.Pixels[x];
            byte secret = fb.Pixels[(secretRow * fb.Width) + x];
            Assert.Equal(parent + 16, secret);
        }
    }

    /// <summary>
    /// The M4 relayout generalizes the M0 image, it does not redesign it: on the original 128x72
    /// screen the band fractions land on exactly the rows the literals used to name (0-27 visible,
    /// 28-47 secret, 48-59 neutral ramp, 60-71 blue and skin/wood). Wrong fractions would still
    /// fill the screen and pass every test above; this one pins the picture.
    /// </summary>
    [Fact]
    public void HistoricScreenKeepsTheExactM0Layout()
    {
        Framebuffer fb = RenderOn(128, 72);

        Assert.Equal(0, fb.Pixels[(27 * 128) + 0]);     // last row of the visible band
        Assert.Equal(16, fb.Pixels[(28 * 128) + 0]);    // first row of the secret band
        Assert.Equal(31, fb.Pixels[(47 * 128) + 127]);  // last pixel of the secret band
        Assert.Equal(16, fb.Pixels[(48 * 128) + 16]);   // second block of the neutral ramp
        Assert.Equal(3, fb.Pixels[(59 * 128) + 127]);   // last neutral step, last row of its band
        Assert.Equal(20, fb.Pixels[(60 * 128) + 0]);    // blue ramp starts the bottom band
        Assert.Equal(29, fb.Pixels[(60 * 128) + 64]);   // skin/wood ramp starts at the half
    }
}

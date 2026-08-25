using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The window-to-console transform, both ways (wave R1). This is the test that could not have
/// been written before the shell had a console of its own: while every screen measured the
/// window and hit-tested in window pixels, there was no second coordinate space to be wrong
/// about. Now there is, and a one-pixel error in the inverse is a cursor that points at the
/// wrong row of a seven-pixel list.
///
/// <para>The window sizes below are deliberately awkward. 1280x720 is the exact multiple, but
/// 1281x721 (a pixel of letterbox), 1000x700 (a scale the height decides, with 40 px of
/// letterbox top and bottom), 137x149 (smaller than the canvas in one dimension) and 1x1 are
/// where an asymmetric rounding rule shows itself.</para>
/// </summary>
public class FramePlacementTests
{
    private const int CanvasW = 160;
    private const int CanvasH = 90;

    public static TheoryData<int, int> Windows => new()
    {
        { 1280, 720 },      // x8 exactly, no letterbox at all
        { 1281, 721 },      // one odd pixel each way — the centring divides an odd number
        { 1000, 700 },      // height decides the scale (x6), wide letterbox above and below
        { 1920, 1080 },     // x12
        { 333, 222 },       // x2, both letterboxes odd
        { 161, 91 },        // x1 with a single pixel to spare
        { 137, 149 },       // narrower than the canvas: floor 1 and a negative origin
        { 1, 1 },           // degenerate, must still not divide by zero
    };

    /// <summary>
    /// Console to window and back is the identity for every pixel of the screen. This is the
    /// direction that must be exact: it is how a layout's rectangle becomes a rectangle on
    /// glass, and how a test knows the two agree.
    /// </summary>
    [Theory]
    [MemberData(nameof(Windows))]
    public void ConsoleToWindowAndBackIsTheIdentity(int windowWidth, int windowHeight)
    {
        FramePlacement placement = FramePlacement.Compute(windowWidth, windowHeight, CanvasW, CanvasH);
        for (int y = 0; y < CanvasH; y++)
        {
            for (int x = 0; x < CanvasW; x++)
            {
                Assert.True(placement.TryToCanvas(placement.X(x), placement.Y(y), out int bx, out int by));
                Assert.Equal(x, bx);
                Assert.Equal(y, by);
            }
        }
    }

    /// <summary>
    /// Window to console and back is not the identity — it cannot be, one console pixel is a
    /// square of many window pixels — but it is idempotent: the second trip lands where the
    /// first one did. That is the property a caller actually relies on, and stating it this way
    /// keeps the test from pretending the transform is lossless.
    /// </summary>
    [Theory]
    [MemberData(nameof(Windows))]
    public void WindowToConsoleIsIdempotent(int windowWidth, int windowHeight)
    {
        FramePlacement placement = FramePlacement.Compute(windowWidth, windowHeight, CanvasW, CanvasH);
        // Every fourth window pixel: enough to cross cell boundaries at every scale in the
        // table without walking two million points.
        for (int wy = 0; wy < windowHeight; wy += 4)
        {
            for (int wx = 0; wx < windowWidth; wx += 4)
            {
                if (!placement.TryToCanvas(wx, wy, out int cx, out int cy))
                {
                    continue;
                }
                Assert.True(placement.TryToCanvas(placement.X(cx), placement.Y(cy), out int cx2, out int cy2));
                Assert.Equal(cx, cx2);
                Assert.Equal(cy, cy2);
            }
        }
    }

    /// <summary>
    /// A click in the letterbox is rejected, not clamped onto the nearest edge pixel. Clamping
    /// would turn a miss into a hit on the border row, which on the library means launching a
    /// cartridge nobody pointed at.
    /// </summary>
    [Fact]
    public void PointsOutsideTheFrameAreRejected()
    {
        // 1000x700: scale 6, picture 960x540, origin (20, 80).
        FramePlacement placement = FramePlacement.Compute(1000, 700, CanvasW, CanvasH);
        Assert.Equal(6, placement.Scale);
        Assert.Equal(20, placement.OriginX);
        Assert.Equal(80, placement.OriginY);

        Assert.False(placement.TryToCanvas(19, 300, out _, out _));       // one pixel left of the frame
        Assert.False(placement.TryToCanvas(500, 79, out _, out _));       // one pixel above it
        Assert.False(placement.TryToCanvas(980, 300, out _, out _));      // one past the right edge
        Assert.False(placement.TryToCanvas(500, 620, out _, out _));      // one past the bottom
        Assert.False(placement.TryToCanvas(-1, -1, out _, out _));        // outside the window entirely
        Assert.False(placement.TryToCanvas(0, 0, out _, out _));          // the window's own corner is letterbox

        // And the four corners of the picture itself are inside.
        Assert.True(placement.TryToCanvas(20, 80, out int x0, out int y0));
        Assert.Equal(0, x0);
        Assert.Equal(0, y0);
        Assert.True(placement.TryToCanvas(979, 619, out int x1, out int y1));
        Assert.Equal(CanvasW - 1, x1);
        Assert.Equal(CanvasH - 1, y1);
    }

    /// <summary>
    /// Rejection must survive the sign: C# integer division truncates toward zero, so a naive
    /// <c>(wx - originX) / scale</c> reports -3 / 8 as column 0. Three pixels of letterbox is
    /// exactly where that bug lives, and it is invisible at the exact multiple where most
    /// manual testing happens.
    /// </summary>
    [Fact]
    public void APointJustLeftOfTheFrameIsNotColumnZero()
    {
        FramePlacement placement = FramePlacement.Compute(1288, 720, CanvasW, CanvasH);
        Assert.Equal(8, placement.Scale);
        Assert.Equal(4, placement.OriginX);
        for (int wx = 0; wx < placement.OriginX; wx++)
        {
            Assert.False(placement.TryToCanvas(wx, 360, out _, out _));
        }
        Assert.True(placement.TryToCanvas(placement.OriginX, 360, out int cx, out _));
        Assert.Equal(0, cx);
    }

    /// <summary>
    /// A window smaller than the canvas still shows x1 and crops, rather than dividing by zero
    /// or picking scale 0 and vanishing. The origin goes negative, which is what "crops" means.
    /// </summary>
    [Fact]
    public void AWindowSmallerThanTheCanvasStillScalesToOne()
    {
        FramePlacement placement = FramePlacement.Compute(80, 45, CanvasW, CanvasH);
        Assert.Equal(1, placement.Scale);
        Assert.Equal(-40, placement.OriginX);
        Assert.Equal(-22, placement.OriginY);       // (45 - 90) / 2 truncates toward zero
        Assert.True(placement.TryToCanvas(0, 0, out int cx, out int cy));
        Assert.Equal(40, cx);
        Assert.Equal(22, cy);
    }

    /// <summary>
    /// The picture is centred: the two letterboxes differ by at most one pixel. It is "at most
    /// one" and not "equal" because the halving truncates, and the sign of that truncation
    /// flips once the origin goes negative (a window smaller than the canvas) — which is why
    /// the assertion is on the magnitude and the table includes 137x149 and 1x1.
    /// </summary>
    [Theory]
    [MemberData(nameof(Windows))]
    public void TheFrameIsCentred(int windowWidth, int windowHeight)
    {
        FramePlacement placement = FramePlacement.Compute(windowWidth, windowHeight, CanvasW, CanvasH);
        int leftOver = windowWidth - placement.OriginX - placement.DestWidth;
        int belowOver = windowHeight - placement.OriginY - placement.DestHeight;
        Assert.InRange(Math.Abs(leftOver - placement.OriginX), 0, 1);
        Assert.InRange(Math.Abs(belowOver - placement.OriginY), 0, 1);
    }

    /// <summary>
    /// The destination rectangle and the two coordinate helpers are the same fact. If they ever
    /// disagree, the presenter draws in one place and the mouse answers about another.
    /// </summary>
    [Theory]
    [MemberData(nameof(Windows))]
    public void TheDestinationRectangleAgreesWithTheTransform(int windowWidth, int windowHeight)
    {
        FramePlacement placement = FramePlacement.Compute(windowWidth, windowHeight, CanvasW, CanvasH);
        var dest = placement.Destination;
        Assert.Equal(placement.X(0), dest.X);
        Assert.Equal(placement.Y(0), dest.Y);
        Assert.Equal(placement.X(CanvasW) - placement.X(0), dest.Width);
        Assert.Equal(placement.Y(CanvasH) - placement.Y(0), dest.Height);
    }

    /// <summary>
    /// The boot screen's placement is the shell screen's placement, full stop.
    ///
    /// <para><b>Re-pinned in wave R6, and this paragraph is why.</b> The version this replaces
    /// asserted that <c>MainMenuLayout.Compute(windowWidth, windowHeight)</c> agreed with
    /// <see cref="FramePlacement"/> on scale, origin and two sample coordinates — a real claim
    /// while the menu was host UI, because the menu then owned a <em>window</em> placement of
    /// its own and the danger was a second copy of the same three lines drifting from the
    /// first. Wave R6 removed the thing that claim was about: the menu is drawn into the
    /// shell's framebuffer like every other screen, so it has no placement of its own to agree
    /// or disagree with, and <c>MainMenuLayout</c> takes a screen size rather than a window
    /// size. The claim that survives is the one that still has two sides — the boot screen and
    /// a running cartridge land on the same glass, at the same scale, through the same owner —
    /// and it is asserted here against <see cref="ShellScreen.Placement"/>, which is the object
    /// the shell actually uses. The old assertion is not weakened, it is re-aimed: what it
    /// guarded (one owner of "where the picture is") is exactly what is guarded below.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Windows))]
    public void TheBootMenuLandsOnTheGlassLikeEveryOtherScreen(int windowWidth, int windowHeight)
    {
        var screen = new ShellScreen();
        FramePlacement placement = FramePlacement.Compute(
            windowWidth, windowHeight, screen.Width, screen.Height);
        FramePlacement shell = screen.Placement(windowWidth, windowHeight);

        Assert.Equal(placement.Scale, shell.Scale);
        Assert.Equal(placement.OriginX, shell.OriginX);
        Assert.Equal(placement.OriginY, shell.OriginY);
        Assert.Equal(placement.X(37), shell.X(37));
        Assert.Equal(placement.Y(61), shell.Y(61));

        // And the menu's own geometry is measured against that screen, not against the window:
        // the canvas the doors are laid out on is the console, whatever size the window is.
        MainMenuLayout menu = MainMenuRenderer.LayoutFor(screen);
        Assert.Equal(screen.Width, menu.ScreenWidth);
        Assert.Equal(screen.Height, menu.ScreenHeight);
    }
}

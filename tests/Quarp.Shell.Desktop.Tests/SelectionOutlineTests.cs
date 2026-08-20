using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The marching-ants geometry (wave 2g, owner's third review) — <see cref="SelectionOutline"/>
/// proven headless, because the animation itself must be provable, not just claimed: the
/// wave's named negative control (г) is "freeze the phase and watch which tests go red", and
/// the answer is <see cref="ThePhaseShiftsTheDashLayout"/> plus
/// <see cref="ThePhaseIsAMarchOverTime"/> — a Phase() hardwired to 0 (or a Collect() that
/// ignores its phase) reddens them. The boundary tests pin the owner's other words: edges
/// between selected and unselected pixels, not a bounding box.
/// </summary>
public class SelectionOutlineTests
{
    /// <summary>A mask predicate over an explicit point set, false out of range like the session's IsSelected.</summary>
    private static Func<int, int, bool> Mask(params (int X, int Y)[] cells)
    {
        var set = new HashSet<(int, int)>(cells);
        return (x, y) => set.Contains((x, y));
    }

    /// <summary>Every collect here uses this thickness, so <see cref="ArcLength"/> can tell the arc side from the cross side.</summary>
    private const int Thickness = 2;

    private static List<AntDash> Collect(Func<int, int, bool> mask, int side, int scale, int dashLength, int phase)
    {
        var output = new List<AntDash>();
        SelectionOutline.Collect(mask, side, scale, dashLength, Thickness, phase, output);
        return output;
    }

    /// <summary>
    /// A dash is thickness x arc — and the arc can be SHORTER than the thickness (a 1 px cut
    /// at a pattern boundary), so "the long side" would lie. Horizontal dashes carry the arc
    /// in Width (their Height is the thickness); when Height is not the thickness the dash is
    /// vertical and the arc is Height. A square thickness-sized dash answers the same number
    /// either way.
    /// </summary>
    private static int ArcLength(AntDash dash) => dash.Height == Thickness ? dash.Width : dash.Height;

    private static int TotalLength(IEnumerable<AntDash> dashes) => dashes.Sum(ArcLength);

    // ---- boundary, not bounding box ----

    /// <summary>One selected pixel: four edges, the full perimeter, dark and light in equal halves, every dash hugging that pixel's own border.</summary>
    [Fact]
    public void ASingleCellIsOutlinedOnItsOwnPerimeter()
    {
        List<AntDash> dashes = Collect(Mask((2, 3)), side: 8, scale: 10, dashLength: 4, phase: 0);

        Assert.Equal(4 * 10, TotalLength(dashes));
        Assert.Equal(2 * 10, dashes.Where(d => d.Bright).Sum(ArcLength));
        foreach (AntDash dash in dashes)
        {
            // Inset into the selected side: inside the cell's rect [20,30)x[30,40) and touching one of its borders.
            Assert.True(dash.X >= 20 && dash.Y >= 30 && dash.X + dash.Width <= 30 && dash.Y + dash.Height <= 40);
            Assert.True(
                dash.X == 20 || dash.Y == 30 || dash.X + dash.Width == 30 || dash.Y + dash.Height == 40,
                $"dash at ({dash.X},{dash.Y}) floats away from the boundary");
        }
    }

    /// <summary>
    /// The named boundary law: edges between two SELECTED pixels do not exist. A 2x2 block's
    /// outline is its outer ring only — 8 edges — where a per-pixel frame (the bug class this
    /// pins out) would have produced 16 edges of arc.
    /// </summary>
    [Fact]
    public void InteriorEdgesBetweenSelectedPixelsAreNotOutlined()
    {
        List<AntDash> dashes = Collect(Mask((1, 1), (2, 1), (1, 2), (2, 2)), side: 8, scale: 4, dashLength: 3, phase: 0);

        Assert.Equal(8 * 4, TotalLength(dashes));
        foreach (AntDash dash in dashes)
        {
            Assert.True(dash.X >= 4 && dash.Y >= 4 && dash.X + dash.Width <= 12 && dash.Y + dash.Height <= 12);
        }
    }

    /// <summary>A hole in the mask is boundary too — the silhouette is true, not a bbox: a 3x3 ring outlines both its outside (12 edges) and its hole (4 edges).</summary>
    [Fact]
    public void AHoleInsideTheMaskGetsItsOwnOutline()
    {
        var ring = new List<(int, int)>();
        for (int y = 1; y <= 3; y++)
        {
            for (int x = 1; x <= 3; x++)
            {
                if ((x, y) != (2, 2))
                {
                    ring.Add((x, y));
                }
            }
        }
        List<AntDash> dashes = Collect(Mask(ring.ToArray()), side: 8, scale: 5, dashLength: 4, phase: 0);

        Assert.Equal((12 + 4) * 5, TotalLength(dashes));
    }

    /// <summary>The region border counts as unselected: a fully selected region is outlined along the region's own edge, inset inward — nothing escapes the canvas.</summary>
    [Fact]
    public void AFullMaskIsOutlinedAlongTheRegionBorder()
    {
        List<AntDash> dashes = Collect((x, y) => x is >= 0 and < 4 && y is >= 0 and < 4, side: 4, scale: 3, dashLength: 2, phase: 0);

        Assert.Equal(16 * 3, TotalLength(dashes));
        foreach (AntDash dash in dashes)
        {
            Assert.True(dash.X >= 0 && dash.Y >= 0 && dash.X + dash.Width <= 12 && dash.Y + dash.Height <= 12);
        }
    }

    /// <summary>Two pixels touching only diagonally: the right-turn tie-break walks two separate loops — total arc is both perimeters and the walk terminates.</summary>
    [Fact]
    public void ADiagonalTouchResolvesIntoTwoLoops()
    {
        List<AntDash> dashes = Collect(Mask((0, 0), (1, 1)), side: 8, scale: 2, dashLength: 2, phase: 0);

        Assert.Equal(8 * 2, TotalLength(dashes));
    }

    // ---- the march ----

    /// <summary>The named negative control (г): a Collect that ignores its phase — frozen ants — makes these two layouts identical and this test red.</summary>
    [Fact]
    public void ThePhaseShiftsTheDashLayout()
    {
        Func<int, int, bool> mask = Mask((2, 2));

        List<AntDash> atZero = Collect(mask, side: 8, scale: 10, dashLength: 4, phase: 0);
        List<AntDash> shifted = Collect(mask, side: 8, scale: 10, dashLength: 4, phase: 2);

        Assert.Equal(TotalLength(atZero), TotalLength(shifted));    // the boundary itself stands still…
        Assert.False(atZero.SequenceEqual(shifted));                // …but the dashes have marched
    }

    /// <summary>
    /// The other half of the same control: Phase() is a real function of time — monotone
    /// within a period, wrapping at the period's end. Hardwiring it to a constant reddens
    /// every line here.
    /// </summary>
    [Fact]
    public void ThePhaseIsAMarchOverTime()
    {
        const int dashLength = 4;   // period = 8 window px

        Assert.Equal(0, SelectionOutline.Phase(0.0, dashLength));
        Assert.Equal(2, SelectionOutline.Phase(SelectionOutline.PeriodSeconds / 4, dashLength));
        Assert.Equal(4, SelectionOutline.Phase(SelectionOutline.PeriodSeconds / 2, dashLength));
        Assert.Equal(0, SelectionOutline.Phase(SelectionOutline.PeriodSeconds, dashLength));    // a full period wraps
        for (double t = 0; t < 2; t += 1.0 / 60)
        {
            Assert.InRange(SelectionOutline.Phase(t, dashLength), 0, 2 * dashLength - 1);
        }
    }

    /// <summary>An empty mask draws nothing, and the caller's list is cleared, not appended to — the per-frame reuse contract.</summary>
    [Fact]
    public void AnEmptyMaskClearsTheOutput()
    {
        var output = new List<AntDash> { new(0, 0, 1, 1, true) };   // stale garbage from a previous frame

        SelectionOutline.Collect(static (_, _) => false, side: 8, scale: 10, dashLength: 4, thickness: 2, phase: 0, output);

        Assert.Empty(output);
    }
}

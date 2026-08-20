using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The editor screen's geometry contract: whole-integer scales, no overlapping panels at the
/// shell's real window sizes, and — the part that actually bites — hit tests that agree with
/// the rectangles, because <see cref="SpriteEditorLayout"/> is the single owner both the
/// renderer draws from and the mouse routing asks. A drift between "where the swatch is" and
/// "what a click on it means" is exactly the bug class this file exists to make impossible.
/// </summary>
public class SpriteEditorLayoutTests
{
    /// <summary>The shell's default window (8x the console) — where the editor will actually be used.</summary>
    private static SpriteEditorLayout Default() => SpriteEditorLayout.Compute(1280, 720, regionCells: 1);

    [Theory]
    [InlineData(320, 180)]      // the UiScale anchor, the smallest sensible window
    [InlineData(640, 360)]
    [InlineData(1280, 720)]     // the default
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void ScalesAreWholeAndAtLeastOne(int width, int height)
    {
        var layout = SpriteEditorLayout.Compute(width, height, regionCells: 1);

        Assert.True(layout.CanvasScale >= 1);
        Assert.True(layout.SheetScale >= 1);
        // Whole-integer scaling is checked through the rectangles being exact multiples —
        // a fractional scale could not produce these sizes.
        Assert.Equal(layout.RegionPixels * layout.CanvasScale, layout.Canvas.Width);
        Assert.Equal(layout.Canvas.Width, layout.Canvas.Height);        // the region is square, so is its view
        Assert.Equal(VirtualConsole.SheetWidth * layout.SheetScale, layout.Sheet.Width);
    }

    [Fact]
    public void AtTheDefaultWindowNothingOverlapsAndEverythingFits()
    {
        var layout = Default();
        var window = new Rectangle(0, 0, 1280, 720);

        Assert.True(window.Contains(layout.Canvas));
        Assert.True(window.Contains(layout.Sheet));
        Assert.True(window.Contains(layout.Swatches));
        Assert.False(layout.Canvas.Intersects(layout.Sheet));
        Assert.False(layout.Canvas.Intersects(layout.Swatches));
        Assert.False(layout.Sheet.Intersects(layout.Swatches));
        // The footer line is below all panels — the prompt must never hide under the sheet.
        Assert.True(layout.Canvas.Bottom <= layout.FooterY);
        Assert.True(layout.Sheet.Bottom <= layout.FooterY);
    }

    [Fact]
    public void SwatchHitTestsRoundTripThroughTheirRectangles()
    {
        var layout = Default();
        for (int i = 0; i < Palette.VisibleCount; i++)
        {
            Rectangle rect = layout.SwatchRect(i);

            Assert.True(layout.TrySwatch(rect.Center.X, rect.Center.Y, out int color));
            Assert.Equal(i, color);
        }
    }

    [Fact]
    public void CanvasCornersMapToTheFirstAndLastPixel()
    {
        var layout = Default();

        Assert.True(layout.TryCanvasPixel(layout.Canvas.X, layout.Canvas.Y, out int x0, out int y0));
        Assert.Equal((0, 0), (x0, y0));

        Assert.True(layout.TryCanvasPixel(layout.Canvas.Right - 1, layout.Canvas.Bottom - 1, out int x1, out int y1));
        Assert.Equal((layout.RegionPixels - 1, layout.RegionPixels - 1), (x1, y1));
    }

    [Fact]
    public void SheetHitTestFindsTheClickedCell()
    {
        var layout = Default();
        int cell = layout.SheetScale * VirtualConsole.SpriteSize;

        Assert.True(layout.TrySheetCell(
            layout.Sheet.X + 5 * cell + cell / 2, layout.Sheet.Y + 9 * cell + cell / 2, out int cellX, out int cellY));

        Assert.Equal((5, 9), (cellX, cellY));
    }

    [Fact]
    public void APointOutsideEveryPanelHitsNothing()
    {
        var layout = Default();

        // The window's corner: header space, owned by no panel.
        Assert.False(layout.TryCanvasPixel(0, 0, out _, out _));
        Assert.False(layout.TrySheetCell(0, 0, out _, out _));
        Assert.False(layout.TrySwatch(0, 0, out _));
    }

    /// <summary>The drag clamp: a stroke wandering off the canvas keeps painting along its edge.</summary>
    [Fact]
    public void TheDragClampPullsOutsidePointsToTheNearestEdgePixel()
    {
        var layout = Default();

        layout.ClampCanvasPixel(0, layout.Canvas.Center.Y, out int leftX, out _);
        Assert.Equal(0, leftX);

        layout.ClampCanvasPixel(layout.Canvas.Right + 500, layout.Canvas.Bottom + 500, out int farX, out int farY);
        Assert.Equal((layout.RegionPixels - 1, layout.RegionPixels - 1), (farX, farY));
    }
}

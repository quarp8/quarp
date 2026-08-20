using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The sheet window's horizontal scroll (wave 2h): the slider geometry lives in
/// <see cref="SpriteEditorLayout"/> (the single owner the renderer draws from and the drag
/// inverts), the surviving state in <see cref="SheetScroll"/>. The wave's named negative
/// control (г) lives here: remove any clamp — the drag's, the wheel's or the resize
/// re-clamp — and the matching test frames pixels past the sheet's border and goes red.
///
/// <para>Geometry note: the sheet window is palette-wide and the sheet is square, so at the
/// default 1280x720 nothing overflows and the slider honestly rests (full-track thumb, drags
/// are no-ops). Overflow needs a window taller than it is wide — 640x1400 here: the ui scale
/// follows the narrow width while the half-strip sheet window follows the tall height, giving
/// scale 5 and a 640-px sheet in a ~206-px window.</para>
/// </summary>
public class SheetScrollTests
{
    /// <summary>A tall narrow window where the sheet genuinely overflows — see the type comment.</summary>
    private static SpriteEditorLayout Overflowing() => SpriteEditorLayout.Compute(640, 1400, regionCells: 1);

    /// <summary>The shell's default window: everything fits, the slider rests.</summary>
    private static SpriteEditorLayout Default() => SpriteEditorLayout.Compute(1280, 720, regionCells: 1);

    [Fact]
    public void TheChosenWindowsReallyDoAndDoNotOverflow()
    {
        // The premise of every test below, pinned so a layout change cannot quietly turn
        // the overflow cases into no-op cases that pass green while testing nothing.
        Assert.True(Overflowing().SheetMaxScroll > 0);
        Assert.Equal(0, Default().SheetMaxScroll);
    }

    // ---- geometry: thumb and drag agree ----

    [Fact]
    public void TheThumbSpansTheTrackAtRestAndHugsTheEndsAtTheExtremes()
    {
        var fits = Default();
        Assert.Equal(fits.SheetSlider.Width, fits.SheetThumb(0).Width);     // "everything is on screen"

        var over = Overflowing();
        Assert.True(over.SheetThumb(0).Width < over.SheetSlider.Width);
        Assert.Equal(over.SheetSlider.X, over.SheetThumb(0).X);
        Assert.Equal(over.SheetSlider.Right, over.SheetThumb(over.SheetMaxScroll).Right);
    }

    /// <summary>Control (г): a drag past either end of the track parks at the sheet's border, never beyond.</summary>
    [Fact]
    public void TheSliderDragClampsAtTheSheetBorder()
    {
        var layout = Overflowing();

        Assert.Equal(0, layout.SheetScrollForSliderX(layout.SheetSlider.X - 500));
        Assert.Equal(layout.SheetMaxScroll, layout.SheetScrollForSliderX(layout.SheetSlider.Right + 500));
        int mid = layout.SheetScrollForSliderX(layout.SheetSlider.X + layout.SheetSlider.Width / 2);
        Assert.InRange(mid, 1, layout.SheetMaxScroll - 1);      // the middle of the track is a real middle
    }

    /// <summary>On a resting slider a drag means nothing — zero offset wherever the pointer goes.</summary>
    [Fact]
    public void DraggingARestingSliderIsANoOp()
    {
        var layout = Default();

        Assert.Equal(0, layout.SheetScrollForSliderX(layout.SheetSlider.Right + 500));
    }

    /// <summary>The thumb the renderer draws and the offset a drag computes invert each other, across the whole range.</summary>
    [Fact]
    public void ThumbAndDragRoundTrip()
    {
        var layout = Overflowing();
        for (int offset = 0; offset <= layout.SheetMaxScroll; offset += 7)
        {
            Rectangle thumb = layout.SheetThumb(offset);
            int back = layout.SheetScrollForSliderX(thumb.X + thumb.Width / 2);
            // Integer quantization may lose a pixel of offset, never more than the pixels-per-step ratio.
            Assert.InRange(Math.Abs(back - offset), 0, Math.Max(1, layout.SheetMaxScroll / Math.Max(1, layout.SheetSlider.Width)));
        }
    }

    // ---- the surviving state ----

    [Fact]
    public void ADragJumpsMovesAndEnds()
    {
        var layout = Overflowing();
        var scroll = new SheetScroll();

        scroll.BeginDrag(layout, layout.SheetSlider.Right);     // press at the far end
        Assert.True(scroll.Dragging);
        Assert.Equal(layout.SheetMaxScroll, scroll.Offset);     // the thumb jumped under the pointer

        scroll.DragTo(layout, layout.SheetSlider.X);
        Assert.Equal(0, scroll.Offset);

        scroll.EndDrag();
        Assert.False(scroll.Dragging);
        int parked = scroll.Offset;
        scroll.DragTo(layout, layout.SheetSlider.Right);        // a drag without a press moves nothing
        Assert.Equal(parked, scroll.Offset);
    }

    /// <summary>The wheel's and the [ ] keys' path: stepped, clamped at both borders (the other half of control г).</summary>
    [Fact]
    public void WheelAndKeyStepsClampAtBothBorders()
    {
        var layout = Overflowing();
        var scroll = new SheetScroll();

        scroll.ScrollBy(layout, -VirtualConsole.SpriteSize);
        Assert.Equal(0, scroll.Offset);                         // already at the left border

        scroll.ScrollBy(layout, 10_000);
        Assert.Equal(layout.SheetMaxScroll, scroll.Offset);     // one huge step parks at the right border

        scroll.ScrollBy(layout, -VirtualConsole.SpriteSize);
        Assert.Equal(layout.SheetMaxScroll - VirtualConsole.SpriteSize, scroll.Offset);
    }

    /// <summary>A window resize that shrinks the ceiling pulls a standing offset back inside — the per-frame re-clamp.</summary>
    [Fact]
    public void AResizeReclampsAStandingOffset()
    {
        var scroll = new SheetScroll();
        scroll.ScrollBy(Overflowing(), 10_000);
        Assert.True(scroll.Offset > 0);

        scroll.Clamp(Default());                                // the grown window fits the whole sheet

        Assert.Equal(0, scroll.Offset);
    }
}

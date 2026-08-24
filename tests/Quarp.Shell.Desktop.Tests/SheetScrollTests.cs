using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The strip contract: one reversible PICO-8 page mapping and one clamped horizontal scroll
/// state. These tests pin the premises as well as the answers, because a resting slider would
/// make the interaction assertions pass without exercising their reason to exist.
///
/// <para><b>Re-pinned in wave 2k</b>, deliberately: the owner's sixth review gave the sheet
/// window the whole freed half of the right column and rejected the four-row strip by name
/// ("лист показывает четыре ряда ... где мог бы показать больше"), so the strip is now
/// <see cref="SheetStrip.Rows"/> = 8 tall and <see cref="SheetStrip.Columns"/> = 32 wide —
/// the same 256 sprites, the same page arithmetic, re-cut as two 16x8 lanes instead of four
/// 16x4 ones. What these tests pin is unchanged in KIND (round-trip, page order, bounds,
/// live slider); only the numbers moved, and they are all derived from SheetStrip's own
/// constants except the ones that state the shape itself, which is the point of stating it.</para>
/// </summary>
public class SheetScrollTests
{
    private static SpriteEditorLayout Default() => SpriteEditorLayout.Compute(1280, 720, regionCells: 1);

    [Fact]
    public void StripMappingRoundTripsEverySprite()
    {
        for (int sprite = 0; sprite < 256; sprite++)
        {
            SheetStrip.SpriteToStripCell(sprite, out int stripColumn, out int stripRow);

            Assert.True(SheetStrip.TryStripCellToSheetCell(
                stripColumn, stripRow, out int sheetX, out int sheetY));
            Assert.Equal(sprite, sheetY * 16 + sheetX);
        }
    }

    /// <summary>
    /// The lane band, cell by cell: PICO-8 pages 0 and 1 stack inside the first lane (sprite
    /// 63 is its bottom-right of row 3, sprite 64 starts row 4 back at column 0), and the
    /// second lane lies end to end beside it starting at column 16 with sprite 128. A strip
    /// re-cut back to four rows makes rows 4-7 impossible and turns every row-4+ case red.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(63, 15, 3)]         // page 0's last sprite: bottom-right of the lane's top half
    [InlineData(64, 0, 4)]          // page 1 stacks UNDER page 0 inside the same lane
    [InlineData(127, 15, 7)]        // lane 0 ends here — 128 sprites in one screenful
    [InlineData(128, 16, 0)]        // lane 1 starts end to end beside lane 0
    [InlineData(191, 31, 3)]
    [InlineData(192, 16, 4)]
    [InlineData(255, 31, 7)]
    public void PicoPagesStackInPairsAndTheLanesLieEndToEnd(int sprite, int expectedColumn, int expectedRow)
    {
        SheetStrip.SpriteToStripCell(sprite, out int column, out int row);

        Assert.Equal((expectedColumn, expectedRow), (column, row));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(SheetStrip.Columns, 0)]
    [InlineData(0, -1)]
    [InlineData(0, SheetStrip.Rows)]
    public void InverseMappingRejectsCellsOutsideTheStrip(int column, int row)
    {
        Assert.False(SheetStrip.TryStripCellToSheetCell(column, row, out _, out _));
    }

    /// <summary>
    /// The default window's own numbers, which are the sixth review's answer: 16 whole sprite
    /// columns by 8 rows = 128 of the 256 sprites visible at once (it was 12 x 4 = 48), no
    /// sliced cell at either edge, and a slider with exactly half the strip still to reach.
    /// </summary>
    [Fact]
    public void DefaultWindowPinsALiveSliderAndUsefulColumnCount()
    {
        var layout = Default();
        int completeColumns = layout.SheetVisiblePixels / VirtualConsole.SpriteSize;

        Assert.Equal(SheetStrip.PixelWidth - layout.SheetVisiblePixels, layout.SheetMaxScroll);
        Assert.True(layout.SheetMaxScroll > 0);
        Assert.Equal(16, completeColumns);                              // 16 x 8 = 128 sprites, no partial column
        Assert.Equal(completeColumns * VirtualConsole.SpriteSize * layout.SheetScale, layout.Sheet.Width);
        Assert.Equal(SheetStrip.PixelHeight * layout.SheetScale, layout.Sheet.Height);
        Assert.True(layout.SheetThumb(0).Width < layout.SheetSlider.Width);
    }

    [Fact]
    public void TheThumbHugsTheTrackEndsAndDragInvertsIt()
    {
        var layout = Default();

        Assert.Equal(layout.SheetSlider.X, layout.SheetThumb(0).X);
        Assert.Equal(layout.SheetSlider.Right, layout.SheetThumb(layout.SheetMaxScroll).Right);
        int thumbTravel = layout.SheetSlider.Width - layout.SheetThumb(0).Width;
        int quantization = Math.Max(1, (layout.SheetMaxScroll + thumbTravel - 1) / thumbTravel);
        for (int offset = 0; offset <= layout.SheetMaxScroll; offset += 7)
        {
            Rectangle thumb = layout.SheetThumb(offset);
            int back = layout.SheetScrollForSliderX(thumb.X + thumb.Width / 2);
            Assert.InRange(Math.Abs(back - offset), 0, quantization);
        }
    }

    [Fact]
    public void SliderAndSteppedScrollClampAtBothStripBorders()
    {
        var layout = Default();
        var scroll = new SheetScroll();

        Assert.Equal(0, layout.SheetScrollForSliderX(layout.SheetSlider.X - 500));
        Assert.Equal(layout.SheetMaxScroll, layout.SheetScrollForSliderX(layout.SheetSlider.Right + 500));

        scroll.ScrollBy(layout, -VirtualConsole.SpriteSize);
        Assert.Equal(0, scroll.Offset);
        scroll.ScrollBy(layout, 10_000);
        Assert.Equal(layout.SheetMaxScroll, scroll.Offset);
        scroll.ScrollBy(layout, -VirtualConsole.SpriteSize);
        Assert.Equal(layout.SheetMaxScroll - VirtualConsole.SpriteSize, scroll.Offset);
    }

    [Fact]
    public void ADragJumpsMovesAndEnds()
    {
        var layout = Default();
        var scroll = new SheetScroll();

        scroll.BeginDrag(layout, layout.SheetSlider.Right);
        Assert.True(scroll.Dragging);
        Assert.Equal(layout.SheetMaxScroll, scroll.Offset);

        scroll.DragTo(layout, layout.SheetSlider.X);
        Assert.Equal(0, scroll.Offset);

        scroll.EndDrag();
        Assert.False(scroll.Dragging);
        scroll.DragTo(layout, layout.SheetSlider.Right);
        Assert.Equal(0, scroll.Offset);
    }

    [Fact]
    public void ClampReactsWhenAResizeWidensTheVisibleSlice()
    {
        var narrow = SpriteEditorLayout.Compute(200, 180, regionCells: 1);
        var widened = Default();
        var scroll = new SheetScroll();
        Assert.True(narrow.SheetMaxScroll > widened.SheetMaxScroll);      // pins the resize premise

        scroll.ScrollBy(narrow, 10_000);
        scroll.Clamp(widened);

        Assert.Equal(widened.SheetMaxScroll, scroll.Offset);
    }

    /// <summary>
    /// A window wide enough to show the whole strip — the case wave 2k created and the session
    /// audit caught the renderer still denying ("the strip overflows at every window size the
    /// shell is used at"). With 32 columns instead of 64 that stopped being true: at 1920x720
    /// the strip fits, the scroll ceiling is zero, the thumb honestly fills the track and a
    /// drag moves nothing. The branch was live and untested.
    ///
    /// <para>Negative control: give <c>SheetMaxScroll</c> a floor of one pixel and the first
    /// assertion goes red; make <c>SheetThumb</c> return a fraction of the track when the
    /// ceiling is zero and the second does.</para>
    /// </summary>
    [Fact]
    public void AWindowWideEnoughForTheWholeStripHasADeadButHonestSlider()
    {
        var layout = SpriteEditorLayout.Compute(1920, 720, regionCells: 1);

        Assert.True(
            layout.SheetVisiblePixels >= SheetStrip.PixelWidth,
            $"1920x720 should show the whole {SheetStrip.PixelWidth}px strip, shows {layout.SheetVisiblePixels}");
        Assert.Equal(0, layout.SheetMaxScroll);
        Assert.Equal(layout.SheetSlider.Width, layout.SheetThumb(0).Width);

        var scroll = new SheetScroll();
        scroll.BeginDrag(layout, layout.SheetSlider.X);
        scroll.DragTo(layout, layout.SheetSlider.Right);
        scroll.EndDrag();

        Assert.Equal(0, scroll.Offset);
    }
}

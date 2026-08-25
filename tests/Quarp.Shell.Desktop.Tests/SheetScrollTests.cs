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
///
/// <para><b>Wave R2 moved the window, not the strip.</b> The sprite editor now lays itself out
/// on the console (ADR-029), so the sheet WINDOW is 56x64 console pixels — seven columns of the
/// strip at scale 1 — instead of eighteen columns of a 1280x720 window. <see cref="SheetStrip"/>
/// itself is untouched: <see cref="SheetStrip.Rows"/> is still 8, the lanes are still two, and
/// the map editor's tile picker, which reads the very same constants, is still exactly where it
/// was. That restraint was deliberate. Re-cutting the strip to four rows would have made the
/// console's window taller in sprites, and it would also have silently re-laid-out a screen this
/// wave was told to leave alone.</para>
/// </summary>
public class SheetScrollTests
{
    /// <summary>The console — the sprite screen's one surface since wave R2.</summary>
    private static SpriteEditorLayout Default() => SpriteEditorLayout.Compute(160, 90, regionCells: 1);

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
    /// The console's own numbers, and <b>this is the re-pin the wave owes an explanation for</b>:
    /// eighteen columns became seven, and 144 visible sprites became 56.
    ///
    /// <para>The arithmetic, because it is the design and not an accident. The console is 160
    /// pixels across. Twenty go to the two-wide tool column, sixty-four to the canvas — an 8x8
    /// sprite at zoom 8, which is what the order asks the canvas to be and what both PICO-8 and
    /// TIC-80 give it — and twenty to the palette / flags / layer-tab column. That leaves
    /// fifty-six, which is seven whole sprite cells, and the window is trimmed to them so its
    /// edge can never show a sliced sprite. Vertically it gets all sixty-four rows of content,
    /// which is exactly <see cref="SheetStrip.PixelHeight"/> at scale 1, so all eight strip rows
    /// are on screen and nothing needs a second, vertical scroll.</para>
    ///
    /// <para>So the trade is stated rather than remembered: TIC-80 shows its whole 256-sprite
    /// sheet at once on a screen 80 pixels wider than ours, PICO-8 shows 64 in four pages, and we
    /// show 56 with a live horizontal scroll across the whole 256. That is the price of a 160-px
    /// console keeping a 64-px canvas, and it is the number a future review should argue with.</para>
    ///
    /// <para>Negative control: drop the whole-cell trim in the layout and the width assertion
    /// goes red; give the tool column zero width and the count climbs, which is how this test
    /// says out loud what that column costs.</para>
    /// </summary>
    [Fact]
    public void TheConsolePinsALiveSliderAndSevenWholeColumns()
    {
        var layout = Default();
        int completeColumns = layout.SheetVisiblePixels / VirtualConsole.SpriteSize;

        Assert.Equal(SheetStrip.PixelWidth - layout.SheetVisiblePixels, layout.SheetMaxScroll);
        Assert.True(layout.SheetMaxScroll > 0);
        Assert.Equal(7, completeColumns);                               // 7 x 8 = 56 sprites, no partial column
        Assert.Equal(1, layout.SheetScale);                             // 64 rows of content is exactly the strip
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

    /// <summary>
    /// The re-clamp still matters, and it is still reachable — just not through a window resize
    /// any more. The layout is a function of the console's size, and the console's size is a
    /// function of its profile (<c>ConsoleProfile</c>), which is a parameter of
    /// <see cref="ShellScreen"/> for exactly one reason: the day a QUARP-16 exists, the shell's
    /// screen becomes a 16. A narrower console gives a narrower window and a higher ceiling, and
    /// a standing offset taken on one must not survive onto the other. 120x90 is not a console we
    /// ship; it is the smallest thing that makes the two ceilings differ, which is all the
    /// premise needs.
    /// </summary>
    [Fact]
    public void ClampReactsWhenTheSurfaceWidensTheVisibleSlice()
    {
        var narrow = SpriteEditorLayout.Compute(120, 90, regionCells: 1);
        var widened = Default();
        var scroll = new SheetScroll();
        Assert.True(narrow.SheetMaxScroll > widened.SheetMaxScroll);      // pins the resize premise

        scroll.ScrollBy(narrow, 10_000);
        scroll.Clamp(widened);

        Assert.Equal(widened.SheetMaxScroll, scroll.Offset);
    }

    /// <summary>
    /// A surface wide enough to show the whole strip: the ceiling is zero, the thumb honestly
    /// fills the track and a drag moves nothing. On the 160-px console this branch is
    /// unreachable — the window is seven columns of thirty-two, so the slider is always live —
    /// but the branch is live CODE, written against the profile rather than against 160, and
    /// code no test can reach is code nobody notices breaking. 360x90 is the narrowest surface
    /// that reaches it: 104 pixels of chrome and canvas plus the strip's own 256.
    ///
    /// <para>Negative control: give <c>SheetMaxScroll</c> a floor of one pixel and the first
    /// assertion goes red; make <c>SheetThumb</c> return a fraction of the track when the
    /// ceiling is zero and the second does.</para>
    /// </summary>
    [Fact]
    public void ASurfaceWideEnoughForTheWholeStripHasADeadButHonestSlider()
    {
        var layout = SpriteEditorLayout.Compute(360, 90, regionCells: 1);

        Assert.True(
            layout.SheetVisiblePixels >= SheetStrip.PixelWidth,
            $"360x90 should show the whole {SheetStrip.PixelWidth}px strip, shows {layout.SheetVisiblePixels}");
        Assert.Equal(0, layout.SheetMaxScroll);
        Assert.Equal(layout.SheetSlider.Width, layout.SheetThumb(0).Width);

        var scroll = new SheetScroll();
        scroll.BeginDrag(layout, layout.SheetSlider.X);
        scroll.DragTo(layout, layout.SheetSlider.Right);
        scroll.EndDrag();

        Assert.Equal(0, scroll.Offset);
    }
}

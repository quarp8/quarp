using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The wave 2i strip contract: one reversible PICO-8 page mapping and one clamped horizontal
/// scroll state. These tests pin the premises as well as the answers, because a resting
/// slider would make the interaction assertions pass without exercising their reason to exist.
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

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(63, 15, 3)]
    [InlineData(64, 16, 0)]
    [InlineData(127, 31, 3)]
    [InlineData(128, 32, 0)]
    [InlineData(191, 47, 3)]
    [InlineData(192, 48, 0)]
    [InlineData(255, 63, 3)]
    public void PicoPagesAreLaidEndToEnd(int sprite, int expectedColumn, int expectedRow)
    {
        SheetStrip.SpriteToStripCell(sprite, out int column, out int row);

        Assert.Equal((expectedColumn, expectedRow), (column, row));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(64, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 4)]
    public void InverseMappingRejectsCellsOutsideTheStrip(int column, int row)
    {
        Assert.False(SheetStrip.TryStripCellToSheetCell(column, row, out _, out _));
    }

    [Fact]
    public void DefaultWindowPinsALiveSliderAndUsefulColumnCount()
    {
        var layout = Default();
        int completeColumns = layout.SheetVisiblePixels / VirtualConsole.SpriteSize;

        Assert.Equal(SheetStrip.PixelWidth - layout.SheetVisiblePixels, layout.SheetMaxScroll);
        Assert.True(layout.SheetMaxScroll > 0);
        Assert.Equal(12, completeColumns);                              // chosen default: 12 whole + part of 13th
        Assert.InRange(completeColumns, 12, 16);
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
}

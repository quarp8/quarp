using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// Structural pixel checks of the rasterizer: camera/clip/pal/palt semantics, soft edge
/// handling and the system font, asserted pixel by pixel (independent of the goldens).
/// </summary>
public class RasterizerPixelTests
{
    private static VirtualConsole NewConsole() => new(ConsoleProfile.Profile8);

    private static byte Pixel(VirtualConsole c, int x, int y) =>
        c.Framebuffer.Pixels[y * c.Framebuffer.Width + x];

    // --- Pset/Pget, camera ---

    [Fact]
    public void PsetWritesAndPgetReadsBack()
    {
        var c = NewConsole();
        c.Pset(10, 20, 7);
        Assert.Equal(7, Pixel(c, 10, 20));
        Assert.Equal(7, c.Pget(10, 20));
    }

    [Fact]
    public void CameraShiftsWritesAndReads()
    {
        var c = NewConsole();
        c.Camera(5, 3);
        c.Pset(10, 10, 9);
        Assert.Equal(9, Pixel(c, 5, 7));    // screen position = world - camera
        Assert.Equal(9, c.Pget(10, 10));    // Pget applies the same shift
        c.Camera();
        Assert.Equal(9, c.Pget(5, 7));
    }

    [Fact]
    public void PgetOffScreenReadsZero()
    {
        var c = NewConsole();
        int width = c.ScreenWidth;      // 160 since ADR-021; the reads below follow the console
        int height = c.ScreenHeight;    // 90

        // Painted first, and the last real pixel asserted, so the three soft zeros mean something:
        // on an empty framebuffer every read answers 0 for the boring reason, and this test would
        // have stayed green even if Pget started reading past the end of a smaller buffer.
        c.Cls(5);
        Assert.Equal(5, c.Pget(width - 1, height - 1));
        Assert.Equal(0, c.Pget(-1, 0));
        Assert.Equal(0, c.Pget(width, 0));
        Assert.Equal(0, c.Pget(0, height));
    }

    // --- Cls, clip ---

    [Fact]
    public void ClsIgnoresClipAndCamera()
    {
        var c = NewConsole();
        c.Clip(0, 0, 4, 4);
        c.Camera(50, 50);
        c.Cls(2);
        Assert.All(c.Framebuffer.Pixels, p => Assert.Equal(2, p));
    }

    [Fact]
    public void ClsHonorsPalRemap()
    {
        var c = NewConsole();
        c.Pal(3, 19);
        c.Cls(3);
        Assert.All(c.Framebuffer.Pixels, p => Assert.Equal(19, p));
    }

    [Fact]
    public void ClipRestrictsWrites()
    {
        var c = NewConsole();
        c.Clip(10, 10, 4, 4);
        c.Pset(9, 10, 7);       // left of the window
        c.Pset(10, 10, 7);      // top-left corner: inside
        c.Pset(13, 13, 7);      // bottom-right corner: inside
        c.Pset(14, 10, 7);      // right edge is exclusive
        c.Pset(10, 14, 7);      // bottom edge is exclusive
        Assert.Equal(0, Pixel(c, 9, 10));
        Assert.Equal(7, Pixel(c, 10, 10));
        Assert.Equal(7, Pixel(c, 13, 13));
        Assert.Equal(0, Pixel(c, 14, 10));
        Assert.Equal(0, Pixel(c, 10, 14));
    }

    [Fact]
    public void EmptyClipClipsEverythingAndResetRestores()
    {
        var c = NewConsole();
        c.Clip(5, 5, 0, 0);
        c.Pset(5, 5, 7);
        c.Pset(60, 40, 7);
        Assert.All(c.Framebuffer.Pixels, p => Assert.Equal(0, p));
        c.Clip();
        c.Pset(60, 40, 7);
        Assert.Equal(7, Pixel(c, 60, 40));
    }

    [Fact]
    public void ClipIsClampedToScreen()
    {
        var c = NewConsole();
        // The far corner is taken from the console, not spelled out: the point of the test is that
        // a clip window far larger than the screen still lets the last real pixel through, and the
        // last real pixel is (Width-1, Height-1) = (159, 89) on QUARP-8 since ADR-021.
        int lastX = c.ScreenWidth - 1;
        int lastY = c.ScreenHeight - 1;
        c.Clip(-100, -100, 1000, 1000);
        c.Pset(0, 0, 5);
        c.Pset(lastX, lastY, 5);
        Assert.Equal(5, Pixel(c, 0, 0));
        Assert.Equal(5, Pixel(c, lastX, lastY));
    }

    // --- Pal / Palt ---

    [Fact]
    public void PalRemapsOnWriteAndResets()
    {
        var c = NewConsole();
        c.Pal(7, 23);
        c.Pset(0, 0, 7);
        Assert.Equal(23, Pixel(c, 0, 0));
        c.Pal();
        c.Pset(1, 0, 7);
        Assert.Equal(7, Pixel(c, 1, 0));
    }

    [Fact]
    public void PaltDefaultOnlyColorZeroTransparent()
    {
        var c = NewConsole();
        c.Sset(0, 0, 5);
        c.Sset(1, 0, 0);
        c.Cls(1);
        c.Spr(0, 0, 0);
        Assert.Equal(5, Pixel(c, 0, 0));
        Assert.Equal(1, Pixel(c, 1, 0));    // color 0 skipped, background shows
    }

    [Fact]
    public void PaltOverridesAndResets()
    {
        var c = NewConsole();
        c.Sset(0, 0, 5);
        c.Sset(1, 0, 0);
        c.Palt(0, false);
        c.Palt(5, true);
        c.Cls(1);
        c.Spr(0, 0, 0);
        Assert.Equal(1, Pixel(c, 0, 0));    // 5 is now transparent
        Assert.Equal(0, Pixel(c, 1, 0));    // 0 is now opaque
        c.Palt();
        c.Cls(1);
        c.Spr(0, 0, 0);
        Assert.Equal(5, Pixel(c, 0, 0));
        Assert.Equal(1, Pixel(c, 1, 0));
    }

    // --- shapes: soft edge cases ---

    [Fact]
    public void RectDrawsOutlineOnly()
    {
        var c = NewConsole();
        c.Rect(10, 10, 5, 4, 7);
        Assert.Equal(7, Pixel(c, 10, 10));
        Assert.Equal(7, Pixel(c, 14, 10));
        Assert.Equal(7, Pixel(c, 10, 13));
        Assert.Equal(7, Pixel(c, 14, 13));
        Assert.Equal(7, Pixel(c, 12, 10));  // top edge
        Assert.Equal(7, Pixel(c, 10, 11));  // left edge
        Assert.Equal(0, Pixel(c, 12, 11));  // interior stays empty
    }

    [Fact]
    public void RectFillFillsInterior()
    {
        var c = NewConsole();
        c.RectFill(10, 10, 5, 4, 7);
        for (int y = 10; y < 14; y++)
        {
            for (int x = 10; x < 15; x++)
            {
                Assert.Equal(7, Pixel(c, x, y));
            }
        }
        Assert.Equal(0, Pixel(c, 15, 10));
        Assert.Equal(0, Pixel(c, 10, 14));
    }

    [Fact]
    public void NonPositiveRectSizesDrawNothing()
    {
        var c = NewConsole();
        c.Rect(10, 10, 0, 5, 7);
        c.Rect(10, 10, 5, -1, 7);
        c.RectFill(10, 10, -5, 5, 7);
        c.RectFill(10, 10, 5, 0, 7);
        Assert.All(c.Framebuffer.Pixels, p => Assert.Equal(0, p));
    }

    [Fact]
    public void LineDrawsBothEndpoints()
    {
        var c = NewConsole();
        c.Line(3, 4, 20, 15, 7);
        Assert.Equal(7, Pixel(c, 3, 4));
        Assert.Equal(7, Pixel(c, 20, 15));
        c.Line(50, 50, 50, 50, 9);          // degenerate: single pixel
        Assert.Equal(9, Pixel(c, 50, 50));
    }

    [Fact]
    public void CircRadiusZeroIsOnePixelNegativeIsNothing()
    {
        var c = NewConsole();
        c.Circ(30, 30, 0, 7);
        Assert.Equal(7, Pixel(c, 30, 30));
        c.CircFill(40, 30, 0, 8);
        Assert.Equal(8, Pixel(c, 40, 30));
        var untouched = NewConsole();
        untouched.Circ(30, 30, -1, 7);
        untouched.CircFill(30, 30, -5, 7);
        Assert.All(untouched.Framebuffer.Pixels, p => Assert.Equal(0, p));
    }

    [Fact]
    public void CircFillCoversCardinalPointsAndCenter()
    {
        var c = NewConsole();
        c.CircFill(64, 36, 5, 7);
        Assert.Equal(7, Pixel(c, 64, 36));
        Assert.Equal(7, Pixel(c, 69, 36));
        Assert.Equal(7, Pixel(c, 59, 36));
        Assert.Equal(7, Pixel(c, 64, 41));
        Assert.Equal(7, Pixel(c, 64, 31));
        Assert.Equal(0, Pixel(c, 70, 36));  // just outside
    }

    // --- sprites and map ---

    [Fact]
    public void SprFlipXMirrorsPixels()
    {
        var c = NewConsole();
        c.Sset(0, 0, 9);                    // marker at the sprite's top-left
        c.Spr(0, 20, 20, flipX: true);
        Assert.Equal(9, Pixel(c, 27, 20));  // mirrored to the right column
        Assert.Equal(0, Pixel(c, 20, 20));
    }

    [Fact]
    public void SprFlipYMirrorsPixels()
    {
        var c = NewConsole();
        c.Sset(0, 0, 9);
        c.Spr(0, 20, 20, flipY: true);
        Assert.Equal(9, Pixel(c, 20, 27));
        Assert.Equal(0, Pixel(c, 20, 20));
    }

    [Fact]
    public void SprOutOfRangeIndexDrawsNothing()
    {
        var c = NewConsole();
        c.Sset(0, 0, 9);
        c.Spr(-1, 20, 20);
        c.Spr(256, 20, 20);
        c.Spr(0, 20, 20, 0, 1);
        c.Spr(0, 20, 20, 1, -2);
        Assert.All(c.Framebuffer.Pixels, p => Assert.Equal(0, p));
    }

    [Fact]
    public void SprMultiCellClampsAtSheetEdge()
    {
        var c = NewConsole();
        c.Sset(120, 0, 9);                  // sprite 15's cell (sheet x 120-127)
        c.Spr(15, 20, 20, 2, 1);            // asks for 16 px, sheet has 8 left
        Assert.Equal(9, Pixel(c, 20, 20));
        for (int dx = 8; dx < 16; dx++)
        {
            Assert.Equal(0, Pixel(c, 20 + dx, 20));
        }
    }

    [Fact]
    public void MapFlagFilterDrawsOnlyMatchingTiles()
    {
        var c = NewConsole();
        // Two tiles, both fully colored 9 in the sheet; only sprite 1 gets flag bit 0.
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                c.Sset(8 + x, y, 9);        // sprite 1
                c.Sset(16 + x, y, 9);       // sprite 2
            }
        }
        c.Mset(0, 0, 1);
        c.Mset(1, 0, 2);
        c.Fset(1, 0, true);
        c.Map(0, 0, 0, 0, 2, 1, flagFilter: 1);
        Assert.Equal(9, Pixel(c, 0, 0));    // tile with the flag drew
        Assert.Equal(0, Pixel(c, 8, 0));    // tile without it was skipped
        c.Map(0, 0, 0, 16, 2, 1);           // no filter: both draw
        Assert.Equal(9, Pixel(c, 0, 16));
        Assert.Equal(9, Pixel(c, 8, 16));
    }

    [Fact]
    public void MapCellsOutsideTheMapAreSkipped()
    {
        var c = NewConsole();
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                c.Sset(8 + x, y, 9);
            }
        }
        c.Mset(0, 0, 1);
        c.Map(-1, -1, 0, 0, 2, 2);          // only cell (0,0) exists; it lands at (8,8)
        Assert.Equal(0, Pixel(c, 0, 0));
        Assert.Equal(9, Pixel(c, 8, 8));
    }

    // --- sheet / map / flags accessors ---

    [Fact]
    public void SsetSgetRoundtripMasksAndBounds()
    {
        var c = NewConsole();
        c.Sset(5, 6, 9);
        Assert.Equal(9, c.Sget(5, 6));
        c.Sset(0, 0, 0x1F);
        Assert.Equal(0x0F, c.Sget(0, 0));   // sheet stores 4-bit colors
        Assert.Equal(0, c.Sget(-1, 0));
        Assert.Equal(0, c.Sget(128, 0));
        c.Sset(-1, 0, 9);                   // out of bounds: soft no-op
        c.Sset(0, 128, 9);
    }

    [Fact]
    public void MsetMgetRoundtripAndBounds()
    {
        var c = NewConsole();
        c.Mset(255, 71, 42);
        Assert.Equal(42, c.Mget(255, 71));
        Assert.Equal(0, c.Mget(256, 0));
        Assert.Equal(0, c.Mget(0, 72));
        Assert.Equal(0, c.Mget(-1, -1));
        c.Mset(256, 0, 1);                  // soft no-op
        c.Mset(0, -1, 1);
    }

    [Fact]
    public void FsetFgetRoundtripAndBounds()
    {
        var c = NewConsole();
        c.Fset(10, 3, true);
        Assert.True(c.Fget(10, 3));
        Assert.False(c.Fget(10, 2));
        c.Fset(10, 3, false);
        Assert.False(c.Fget(10, 3));
        Assert.False(c.Fget(-1, 0));
        Assert.False(c.Fget(256, 0));
        Assert.False(c.Fget(0, 8));
        c.Fset(256, 0, true);               // soft no-op
        c.Fset(0, 8, true);
    }

    // --- Print and the system font ---

    [Fact]
    public void PrintReturnsAdvancePerGlyph()
    {
        var c = NewConsole();
        Assert.Equal(14, c.Print("A", 10, 10, 7));
        Assert.Equal(10 + 3 * SystemFont.CellWidth, c.Print("ABC", 10, 20, 7));
        Assert.Equal(10, c.Print("", 10, 30, 7));
    }

    [Fact]
    public void PrintDrawsGlyphAExactly()
    {
        // 'A' is 0b010_101_111_101_101: rows .X. / X.X / XXX / X.X / X.X.
        var c = NewConsole();
        c.Print("A", 10, 10, 7);
        bool[,] expected =
        {
            { false, true, false },
            { true, false, true },
            { true, true, true },
            { true, false, true },
            { true, false, true },
        };
        for (int row = 0; row < SystemFont.GlyphHeight; row++)
        {
            for (int col = 0; col < SystemFont.GlyphWidth; col++)
            {
                byte want = expected[row, col] ? (byte)7 : (byte)0;
                Assert.Equal(want, Pixel(c, 10 + col, 10 + row));
            }
        }
        // The 1 px spacing column stays empty.
        for (int row = 0; row < SystemFont.GlyphHeight; row++)
        {
            Assert.Equal(0, Pixel(c, 13, 10 + row));
        }
    }

    [Fact]
    public void PrintNewlineReturnsToOriginalX()
    {
        var c = NewConsole();
        int end = c.Print("A\nA", 10, 10, 7);
        Assert.Equal(14, end);
        Assert.Equal(7, Pixel(c, 11, 10));                          // row 0 of the first 'A'
        Assert.Equal(7, Pixel(c, 11, 10 + SystemFont.CellHeight));  // second line, same x
    }

    [Fact]
    public void PrintSkipsControlCharsAndBoxesUnknownOnes()
    {
        var c = NewConsole();
        int end = c.Print("\tA", 10, 10, 7);
        Assert.Equal(14, end);              // tab is skipped without advancing
        var c2 = NewConsole();
        c2.Print("é", 10, 10, 7);      // é: outside ASCII, draws the hollow box
        Assert.Equal(7, Pixel(c2, 10, 10));
        Assert.Equal(7, Pixel(c2, 11, 10));
        Assert.Equal(7, Pixel(c2, 12, 10));
        Assert.Equal(0, Pixel(c2, 11, 12)); // hollow center
        Assert.Equal(7, Pixel(c2, 11, 14)); // bottom edge
    }

    [Fact]
    public void PrintHonorsCameraAndClip()
    {
        var c = NewConsole();
        c.Clip(0, 0, 12, c.ScreenHeight);   // a full-height column 12 px wide
        c.Print("AA", 10, 10, 7);           // second glyph starts at x=14: fully clipped
        Assert.Equal(7, Pixel(c, 11, 10));
        for (int x = 12; x < 20; x++)
        {
            for (int y = 10; y < 16; y++)
            {
                Assert.Equal(0, Pixel(c, x, y));
            }
        }
    }

    // --- Print with the second font (Font.Large, 4x6 ink in a 5x7 cell) ---

    /// <summary>
    /// The invariant the whole second font was built under: a Print call that does not name a
    /// font draws <em>exactly</em> what it drew before there was a second one. Compared frame
    /// against frame rather than by eye, over text that exercises lowercase, digits, the
    /// newline path and an unsupported character — every branch of the cursor rule the two
    /// fonts now share. If this ever fails, every recorded frame hash in the repository moved.
    /// </summary>
    [Fact]
    public void PrintWithoutAFontIsPixelIdenticalToTheSmallFont()
    {
        const string text = "Quarp-8 gjpqy 0123\nsecond line\té";
        var byDefault = NewConsole();
        byDefault.Print(text, 3, 4, 7);
        var explicitly = NewConsole();
        explicitly.Print(text, 3, 4, 7, Font.Small);
        Assert.Equal(explicitly.Framebuffer.Pixels, byDefault.Framebuffer.Pixels);

        // ...and the large font really is a different picture, or the comparison above proves nothing.
        var large = NewConsole();
        large.Print(text, 3, 4, 7, Font.Large);
        Assert.NotEqual(explicitly.Framebuffer.Pixels, large.Framebuffer.Pixels);
    }

    [Fact]
    public void PrintReturnsTheAdvanceOfTheFontItWasGiven()
    {
        var c = NewConsole();
        Assert.Equal(10 + 3 * SystemFont.CellWidth, c.Print("ABC", 10, 10, 7));
        Assert.Equal(10 + 3 * SystemFont.CellWidth, c.Print("ABC", 10, 20, 7, Font.Small));
        Assert.Equal(10 + 3 * SystemFontLarge.CellWidth, c.Print("ABC", 10, 30, 7, Font.Large));
        Assert.Equal(10, c.Print("", 10, 40, 7, Font.Large));
        Assert.Equal(10, c.Print(null!, 10, 50, 7, Font.Large));
        // The two fonts must not accidentally agree: 5 px per character is the whole trade-off.
        Assert.NotEqual(SystemFont.CellWidth, SystemFontLarge.CellWidth);
    }

    [Fact]
    public void PrintLargeDrawsGlyphAExactly()
    {
        // 'A' is 0b0110_1001_1111_1001_1001_0000: .##. / #..# / #### / #..# / #..# / ....
        var c = NewConsole();
        c.Print("A", 10, 10, 7, Font.Large);
        string[] expected = { ".##.", "#..#", "####", "#..#", "#..#", "...." };
        for (int row = 0; row < SystemFontLarge.GlyphHeight; row++)
        {
            for (int col = 0; col < SystemFontLarge.GlyphWidth; col++)
            {
                byte want = expected[row][col] == '#' ? (byte)7 : (byte)0;
                Assert.Equal(want, Pixel(c, 10 + col, 10 + row));
            }
        }
        // The cell's spacing column and row stay empty: 4x6 of ink inside 5x7.
        for (int row = 0; row < SystemFontLarge.CellHeight; row++)
        {
            Assert.Equal(0, Pixel(c, 10 + SystemFontLarge.CellWidth - 1, 10 + row));
        }
        for (int col = 0; col < SystemFontLarge.CellWidth; col++)
        {
            Assert.Equal(0, Pixel(c, 10 + col, 10 + SystemFontLarge.CellHeight - 1));
        }
    }

    /// <summary>
    /// A descender really hangs below the baseline — the thing the 3x5 cell had no room for.
    /// 'g' inks row 5, which is one row below the row every x-height letter ends on.
    /// </summary>
    [Fact]
    public void PrintLargeDrawsDescendersBelowTheBaseline()
    {
        var c = NewConsole();
        c.Print("og", 10, 10, 7, Font.Large);
        int baseline = 10 + 4;
        bool tail = false;
        for (int col = 0; col < SystemFontLarge.GlyphWidth; col++)
        {
            Assert.Equal(0, Pixel(c, 10 + col, baseline + 1));           // 'o' stops at the baseline
            tail |= Pixel(c, 10 + SystemFontLarge.CellWidth + col, baseline + 1) == 7;
        }
        Assert.True(tail, "'g' has no ink below the baseline — the tail row is what the 4x6 cell is for");
    }

    [Fact]
    public void PrintNewlineUsesTheLineHeightOfTheChosenFont()
    {
        var small = NewConsole();
        Assert.Equal(10 + SystemFont.CellWidth, small.Print("A\nA", 10, 10, 7));
        Assert.Equal(7, Pixel(small, 11, 10 + SystemFont.CellHeight));

        var large = NewConsole();
        Assert.Equal(10 + SystemFontLarge.CellWidth, large.Print("A\nA", 10, 10, 7, Font.Large));
        Assert.Equal(0, Pixel(large, 11, 10 + SystemFont.CellHeight));       // not the small pitch
        Assert.Equal(7, Pixel(large, 11, 10 + SystemFontLarge.CellHeight));  // the large one
    }

    /// <summary>
    /// Out-of-range characters draw the large font's own box, which — unlike the 3x5 one — is
    /// not any glyph in the table (tasks/open/debt-fallback-zero.md). Asserted as pixels rather
    /// than through GetGlyph so the check covers the drawing path too.
    /// </summary>
    [Fact]
    public void PrintLargeBoxesUnknownCharacters()
    {
        var c = NewConsole();
        c.Print("é", 10, 10, 7, Font.Large);
        for (int col = 0; col < SystemFontLarge.GlyphWidth; col++)
        {
            Assert.Equal(7, Pixel(c, 10 + col, 10));      // solid top edge
            Assert.Equal(7, Pixel(c, 10 + col, 14));      // solid bottom edge
        }
        Assert.Equal(0, Pixel(c, 11, 12));                // hollow centre
        Assert.Equal(0, Pixel(c, 12, 12));
    }
}

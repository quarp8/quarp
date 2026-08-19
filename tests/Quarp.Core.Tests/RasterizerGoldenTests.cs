using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// Golden-master tests of the rasterizer: each scene draws a fixed sequence of calls and
/// the framebuffer's FNV-1a hash must match a recorded value. Any rasterizer change that
/// flips a hash is a conscious decision with an eyes-open golden update (CODESTYLE §Тесты).
/// </summary>
public class RasterizerGoldenTests
{
    private static VirtualConsole NewConsole() => new(ConsoleProfile.Profile8);

    /// <summary>Deterministic sheet fill so sprite/map scenes have distinctive pixels.</summary>
    private static void FillSheet(VirtualConsole c)
    {
        for (int y = 0; y < VirtualConsole.SheetHeight; y++)
        {
            for (int x = 0; x < VirtualConsole.SheetWidth; x++)
            {
                c.Sset(x, y, (byte)((x * 7 + y * 13 + (x >> 3) + (y >> 3)) & 0x0F));
            }
        }
    }

    private static void SceneLines(VirtualConsole c)
    {
        c.Cls(0);
        // Fan from the middle of the screen — Width/2, Height/2 = (80, 45) — so all octants of the
        // Bresenham walker are exercised. The 16 endpoints sweep the full width in steps of 10 px
        // and the last of them lands on x = 160 = Width, one column past the screen, so the sweep
        // ends on a clipped ray. The alternating y values are 2 and Height-2 = 88.
        for (int i = 0; i < 16; i++)
        {
            c.Line(80, 45, 10 + i * 10, i % 2 == 0 ? 2 : 88, (byte)(1 + i % 15));
        }
        // Endpoints far off screen in every direction: every one of these four is outside 160x90
        // on both axes, so the clipper has to invent both ends of both lines.
        c.Line(-60, -25, 250, 115, 7);
        c.Line(175, -12, -38, 100, 10);
        // Horizontal 4 px above the bottom edge (86 = Height-4) and 4 px in from either side
        // (156 = Width-4); vertical hugging the left edge; single point.
        c.Line(4, 86, 156, 86, 3);
        c.Line(2, 4, 2, 84, 5);
        c.Line(100, 10, 100, 10, 8);
    }

    private static void SceneRects(VirtualConsole c)
    {
        c.Cls(1);
        c.Rect(2, 2, 30, 20, 7);
        c.RectFill(10, 10, 40, 25, 10);
        c.Rect(45, 30, 130, 70, 8);        // spills past both far edges: 45+130 = 175 > 160, 30+70 = 100 > 90
        c.RectFill(-10, -5, 25, 15, 5);    // spills past the top-left corner
        c.Rect(60, 5, 1, 1, 3);            // degenerate 1x1 outline
        c.RectFill(70, 50, 1, 8, 12);      // 1-wide column
        c.Rect(20, 40, 0, 10, 14);         // zero width: no-op
        c.RectFill(20, 40, 10, -3, 14);    // negative height: no-op
    }

    private static void SceneCircles(VirtualConsole c)
    {
        c.Cls(0);
        c.Circ(80, 45, 30, 7);             // centred: (Width/2, Height/2)
        c.CircFill(80, 45, 12, 10);
        c.Circ(0, 0, 10, 8);               // clipped at the corner
        c.CircFill(159, 89, 15, 5);        // clipped at the opposite corner (Width-1, Height-1)
        c.Circ(30, 60, 0, 3);              // radius 0: one pixel
        c.CircFill(90, 15, 0, 3);
        c.Circ(50, 50, -4, 14);            // negative: no-op
        c.CircFill(200, 36, 20, 14);       // fully off screen: leftmost pixel 200-20 = 180 > 159
    }

    private static void SceneSprites(VirtualConsole c)
    {
        FillSheet(c);
        c.Cls(0);
        c.Spr(0, 4, 4);
        c.Spr(0, 14, 4, flipX: true);
        c.Spr(0, 24, 4, flipY: true);
        c.Spr(0, 34, 4, flipX: true, flipY: true);
        c.Spr(17, 4, 20, 2, 2);            // multi-cell block
        c.Spr(17, 30, 20, 2, 2, flipX: true, flipY: true);
        c.Spr(15, 60, 20, 3, 1);           // clamped at the sheet's right edge
        c.Spr(100, 152, 84);               // flush right (152 = Width-8), 2 rows past the bottom (84+8 = 92 > 90)
        c.Palt(3, true);                   // extra transparent color changes the blit
        c.Spr(34, 90, 8, 2, 2);
        c.Palt();
    }

    private static void SceneMap(VirtualConsole c)
    {
        FillSheet(c);
        for (int i = 0; i < 40; i++)
        {
            c.Mset(i % 16, i / 16, (byte)(i * 3 + 1));
        }
        c.Fset(4, 0, true);                // tile sprite 4 carries flag bit 0
        c.Fset(7, 0, true);
        c.Fset(7, 1, true);
        c.Cls(0);
        c.Map(0, 0, 0, 0, 16, 3);
        c.Camera(4, -2);
        c.Map(0, 0, 8, 40, 8, 2);          // camera-shifted layer
        c.Camera();
        // Only tiles whose sprite has flag bit 0, in a band flush with the bottom edge:
        // 74 + 2 cells of 8 px = 90 = Height.
        c.Map(0, 0, 0, 74, 16, 2, flagFilter: 1);
        // Negative cell start (soft edge skip), and the 6x6 block of cells — 48 px each way —
        // also runs off both far edges: 125+48 = 173 > 160, 75+48 = 123 > 90.
        c.Map(-3, -3, 125, 75, 6, 6);
    }

    private static void SceneClipCamera(VirtualConsole c)
    {
        c.Cls(2);
        c.Clip(20, 10, 60, 40);
        c.Camera(-5, 3);
        c.RectFill(0, 0, 160, 90, 4);      // the whole screen asked for; fills the clip window only
        c.Line(0, 0, 159, 89, 8);          // corner to corner: (0,0) to (Width-1, Height-1)
        c.Circ(50, 30, 25, 10);
        c.Print("CLIPPED", 18, 12, 3);
        c.Camera();
        c.Clip(70, 30, 200, 200);          // clamped to the screen: 70+200 > 160, 30+200 > 90
        c.RectFill(0, 0, 160, 90, 6);
        c.Clip();
        c.Rect(0, 0, 160, 90, 7);          // full-screen border after reset
    }

    private static void ScenePalette(VirtualConsole c)
    {
        FillSheet(c);
        c.Pal(7, 23);                      // slot 7 -> secret forest green
        c.Pal(10, 26);                     // slot 10 -> secret maroon
        c.Cls(7);                          // Cls honors the remap
        c.RectFill(10, 10, 30, 20, 10);
        c.Spr(1, 60, 10, 2, 2);            // sheet colors go through the same remap
        c.Pal();
        c.RectFill(10, 40, 30, 20, 10);    // same slot, back to the master color
        c.Palt(0, false);                  // opaque color 0 now draws from the sheet
        c.Spr(0, 90, 40);
        c.Palt();
    }

    private static void SceneText(VirtualConsole c)
    {
        c.Cls(0);
        int x = c.Print("QUARP-8", 2, 2, 7);
        c.Print("!", x + 2, 2, 10);        // chained from the returned x
        c.Print("line one\nline two\nline 3", 2, 12, 3);
        c.Print("éЖ", 2, 34, 8); // outside ASCII 32-126: fallback boxes
        c.Print("edge", 152, 40, 5);       // spills past the right edge: 4 cells of 4 px end at 168 > 160
        c.Print("low", 4, 87, 6);          // spills past the bottom: 5 ink rows from 87 end at 91 > 89
        c.Print("", 50, 50, 9);            // empty string: no-op
    }

    /// <summary>
    /// The same shape of scene as "text", drawn with <see cref="Font.Large"/> — and one line of
    /// each font side by side, because the mixed frame (prose large, labels small) is what the
    /// dialogue cart actually ships and what a font regression would break first.
    /// </summary>
    private static void SceneTextLarge(VirtualConsole c)
    {
        c.Cls(0);
        int x = c.Print("QUARP-8", 2, 2, 7, Font.Large);
        c.Print("!", x + 2, 2, 10, Font.Large);            // chained from the returned x
        c.Print("line one\nline two\nline 3", 2, 11, 3, Font.Large);
        c.Print("éЖ", 2, 34, 8, Font.Large);               // outside ASCII 32-126: fallback boxes
        c.Print("edge", 150, 42, 5, Font.Large);           // right edge: 4 cells of 5 px end at 170 > 160
        c.Print("gjpqy", 4, 86, 6, Font.Large);            // tails past the bottom: 6 ink rows end at 91 > 89
        c.Print("", 50, 50, 9, Font.Large);                // empty string: no-op
        c.Print("MARA", 2, 52, 6);                         // small font in the same frame
        c.Print("Burn the drum", 2, 59, 3, Font.Large);    // m, w-family and the round letters
    }

    private static void SceneCombo(VirtualConsole c)
    {
        FillSheet(c);
        for (int i = 0; i < 32; i++)
        {
            c.Mset(i % 16, 4 + i / 16, (byte)(64 + i));
        }
        c.Cls(1);
        c.Camera(3, 2);
        c.Clip(4, 4, 152, 82);             // a 4 px margin all round: 4+152 = 156 = Width-4, 4+82 = 86 = Height-4
        c.Map(0, 4, 0, 8, 16, 2);
        c.Pal(8, 24);
        c.CircFill(80, 50, 18, 8);         // just below the screen centre (80, 45)
        c.Palt(1, true);
        c.Spr(5, 40, 30, 2, 1, flipX: true);
        c.Print("SCORE 042", 30, 75, 3);   // a HUD line low in the clip window, camera and all
        c.Line(0, 0, 159, 89, 11);         // corner to corner (Width-1, Height-1), camera-shifted
        c.Camera();
        c.Clip();
        c.Pal();
        c.Palt();
        c.Rect(0, 0, 160, 90, 2);          // full-screen border
    }

    public static TheoryData<string> SceneNames()
    {
        var data = new TheoryData<string>();
        foreach (string name in Scenes.Keys)
        {
            data.Add(name);
        }
        return data;
    }

    private static readonly Dictionary<string, Action<VirtualConsole>> Scenes = new()
    {
        ["lines"] = SceneLines,
        ["rects"] = SceneRects,
        ["circles"] = SceneCircles,
        ["sprites"] = SceneSprites,
        ["map"] = SceneMap,
        ["clipcam"] = SceneClipCamera,
        ["palette"] = ScenePalette,
        ["text"] = SceneText,
        ["textlarge"] = SceneTextLarge,
        ["combo"] = SceneCombo,
    };

    // Golden FNV-1a hashes of each scene's framebuffer. An intentional rasterizer change must
    // update these consciously.
    //
    // ALL TEN were re-recorded in M4 stage 4.0 for ADR-021 (the screen moved from 128x72 to
    // 160x90), and every one of them had to move: the hash covers the whole framebuffer, which
    // grew from 9216 to 14400 bytes, so even a scene whose calls are untouched — "palette" is
    // exactly that — hashes differently. The scenes themselves were RE-AUTHORED, not merely
    // re-pinned (work order Р25): every coordinate that encoded an edge, a centre or a spill
    // follows the new edges, and each one now states its arithmetic in a comment next to it, so a
    // reviewer can check "clipped at the far corner" is still at the far corner without running
    // anything. The previous values, recorded on 128x72, were:
    //   lines b3d1ebd67c610371 · rects e4e69af28b8cf7ae · circles c018364e10fdfecc
    //   sprites 93efd947d2b0fdfb · map 2e911b814e5ef34b · clipcam c3e382cde046344b
    //   palette e50a7b4ebb0452eb · text 456fbad923d44569 · textlarge 96e90a4def524024
    //   combo 8c4abe5f22ba1527
    // Their own histories are kept below, because a pin's history is the only thing that tells a
    // later reader whether a hash ever moved for a reason nobody would guess.
    private static readonly Dictionary<string, string> Goldens = new()
    {
        ["lines"] = "90fe52acb1ede20a",
        ["rects"] = "0c23bf565c8387ce",
        ["circles"] = "68c4b2af303ffc24",
        ["sprites"] = "1c415d0d0d9e747b",
        // Once re-pinned from 0341afdc417686cb (on 128x72) when Map adopted the ratified
        // "tile 0 is empty" rule (API-8.md §Map): the 8 unset cells of row 2 used to blit
        // sprite 0 and now draw nothing.
        ["map"] = "9bb9877ec497cb25",
        ["clipcam"] = "8b68c1ecebe561df",
        ["palette"] = "5b3f5044156dcfeb",
        // This pin has a history: on 128x72 it was re-pinned to c1870e56815f1682 by the
        // 2026-08-18 lowercase redraw, then re-pinned BACK the same day when the owner's eye
        // rejected the redraw (common letters like 'e' collapsed into blobs; the original's four
        // weak glyphs were the lesser evil). The original lowercase set is what the value below
        // was recorded from; point-fixes to individual glyphs go through the owner's eye before
        // landing — see tasks/DONE.md.
        ["text"] = "c602143927cd30e9",
        // The youngest scene: the 4x6 font arrived with tasks/open/08-second-font.md and this is
        // the first thing that draws it.
        ["textlarge"] = "48d9a85488ddd324",
        ["combo"] = "b9dac2a087c8aa93",
    };

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void SceneMatchesGolden(string name)
    {
        var console = NewConsole();
        Scenes[name](console);
        string actual = FrameHash.Of(console.Framebuffer);
        Assert.Equal(Goldens[name], actual);
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void SceneIsDeterministicAcrossRuns(string name)
    {
        var first = NewConsole();
        Scenes[name](first);
        var second = NewConsole();
        Scenes[name](second);
        Assert.Equal(FrameHash.Compute(first.Framebuffer), FrameHash.Compute(second.Framebuffer));
    }

    private static byte Pixel(VirtualConsole c, int x, int y) =>
        c.Framebuffer.Pixels[y * c.Framebuffer.Width + x];

    /// <summary>Is there any pixel of <paramref name="color"/> in the given box? Used where the exact glyph shape is not the point.</summary>
    private static bool AnyPixel(VirtualConsole c, int x0, int y0, int width, int height, byte color)
    {
        for (int y = y0; y < y0 + height; y++)
        {
            for (int x = x0; x < x0 + width; x++)
            {
                if (Pixel(c, x, y) == color)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Same question, for "did anything at all land here".</summary>
    private static bool AnyInk(VirtualConsole c, int x0, int y0, int width, int height)
    {
        for (int y = y0; y < y0 + height; y++)
        {
            for (int x = x0; x < x0 + width; x++)
            {
                if (Pixel(c, x, y) != 0)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The scenes that claim an edge actually touch it, asserted in pixels and in coordinates
    /// derived from the console.
    ///
    /// <para><b>Why this exists.</b> A golden hash cannot tell "clipped at the far corner" from
    /// "clipped at a corner that used to be far": when the screen moved to 160x90 (ADR-021) every
    /// one of the ten hashes had to be re-recorded anyway, so a scene left at its old coordinates
    /// — a corner circle now floating in open playfield, a border drawn around a rectangle two
    /// thirds the size of the screen — would have been re-pinned green without a murmur. That is
    /// the failure work order Р25 names, and this test is what makes it red instead of invisible.
    /// Each assertion below is the intent of one commented call above, spelled as pixels.</para>
    /// </summary>
    [Fact]
    public void TheEdgeScenesReachTheEdgesTheyClaim()
    {
        var probe = NewConsole();
        int width = probe.ScreenWidth;
        int height = probe.ScreenHeight;
        int lastX = width - 1;
        int lastY = height - 1;

        // circles: the disk centred on the far corner covers it, and the outline centred on the
        // origin still reaches its own two on-screen cardinal points.
        var circles = NewConsole();
        SceneCircles(circles);
        Assert.Equal(5, Pixel(circles, lastX, lastY));
        Assert.Equal(5, Pixel(circles, lastX - 15, lastY));   // its leftmost pixel, radius 15
        Assert.Equal(8, Pixel(circles, 10, 0));
        Assert.Equal(8, Pixel(circles, 0, 10));

        // rects: the outline that spills past both far edges keeps its top edge running into the
        // last column and its left edge running into the last row, and has no right or bottom edge
        // on screen at all.
        var rects = NewConsole();
        SceneRects(rects);
        Assert.Equal(8, Pixel(rects, lastX, 30));
        Assert.Equal(8, Pixel(rects, 45, lastY));

        // lines: the horizontal sits four rows above the bottom and runs to four columns short of
        // the right edge; the fan's centre is the centre of the screen.
        var lines = NewConsole();
        SceneLines(lines);
        Assert.Equal(3, Pixel(lines, 4, height - 4));
        Assert.Equal(3, Pixel(lines, width - 4, height - 4));
        Assert.Equal(3, Pixel(lines, width / 2, height - 4));    // and it is one unbroken run
        Assert.True(AnyInk(lines, width / 2 - 1, height / 2 - 1, 3, 3), "the fan lost its centre");

        // sprites: the last sprite is flush with the right edge and hangs two rows off the bottom,
        // so the bottom-right 8x6 corner of the screen is the only place its ink can be.
        var sprites = NewConsole();
        SceneSprites(sprites);
        Assert.True(AnyInk(sprites, width - 8, height - 6, 8, 6), "the corner sprite is not at the corner");

        // map: the flag-filtered band is flush with the bottom edge — 16 rows of it — and the
        // sixteen rows above it are empty, which is what says the band followed the edge instead
        // of staying where a 72-row screen used to put it.
        var map = NewConsole();
        SceneMap(map);
        Assert.True(AnyInk(map, 0, height - 16, width, 16), "the bottom map band vanished");
        Assert.False(AnyInk(map, 0, height - 32, width, 16), "something is drawing above the bottom band");

        // clipcam and combo: the last thing each draws is a border around the whole screen, so all
        // four corners carry it and the pixel just inside does not (it is still the Cls colour).
        var clipcam = NewConsole();
        SceneClipCamera(clipcam);
        foreach ((int x, int y) in new[] { (0, 0), (lastX, 0), (0, lastY), (lastX, lastY) })
        {
            Assert.Equal(7, Pixel(clipcam, x, y));
        }
        Assert.Equal(7, Pixel(clipcam, width / 2, lastY));     // the rails are continuous, not just corners
        Assert.Equal(2, Pixel(clipcam, 1, 1));                 // Cls(2) shows through: a border, not a fill

        var combo = NewConsole();
        SceneCombo(combo);
        foreach ((int x, int y) in new[] { (0, 0), (lastX, 0), (0, lastY), (lastX, lastY) })
        {
            Assert.Equal(2, Pixel(combo, x, y));
        }
        Assert.Equal(1, Pixel(combo, 1, 1));                   // Cls(1) shows through

        // text and textlarge: the two strings that are meant to fall off the edge have ink in the
        // last columns and in the last rows of the screen.
        var text = NewConsole();
        SceneText(text);
        Assert.True(AnyPixel(text, width - 8, 40, 8, 5, 5), "\"edge\" no longer reaches the right edge");
        Assert.True(AnyPixel(text, 0, height - 3, 16, 3, 6), "\"low\" no longer reaches the bottom");

        var textlarge = NewConsole();
        SceneTextLarge(textlarge);
        Assert.True(AnyPixel(textlarge, width - 10, 42, 10, 6, 5), "large \"edge\" no longer reaches the right edge");
        Assert.True(AnyPixel(textlarge, 0, height - 4, 25, 4, 6), "the descender tails no longer reach the bottom");
    }

    [Fact]
    public void ScenesProduceDistinctImages()
    {
        var seen = new Dictionary<ulong, string>();
        foreach (KeyValuePair<string, Action<VirtualConsole>> scene in Scenes)
        {
            var console = NewConsole();
            scene.Value(console);
            ulong hash = FrameHash.Compute(console.Framebuffer);
            Assert.False(seen.TryGetValue(hash, out string? clash),
                $"scenes '{scene.Key}' and '{clash}' hash identically — a scene is not drawing");
            seen[hash] = scene.Key;
        }
    }
}

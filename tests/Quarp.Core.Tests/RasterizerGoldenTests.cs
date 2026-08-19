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
        // Fan from center: all octants of the Bresenham walker.
        for (int i = 0; i < 16; i++)
        {
            c.Line(64, 36, 8 + i * 8, i % 2 == 0 ? 2 : 70, (byte)(1 + i % 15));
        }
        // Endpoints far off screen in every direction.
        c.Line(-50, -20, 200, 90, 7);
        c.Line(140, -10, -30, 80, 10);
        // Horizontal, vertical, single point.
        c.Line(4, 68, 124, 68, 3);
        c.Line(2, 4, 2, 66, 5);
        c.Line(100, 10, 100, 10, 8);
    }

    private static void SceneRects(VirtualConsole c)
    {
        c.Cls(1);
        c.Rect(2, 2, 30, 20, 7);
        c.RectFill(10, 10, 40, 25, 10);
        c.Rect(45, 30, 100, 60, 8);        // spills past the right/bottom edge
        c.RectFill(-10, -5, 25, 15, 5);    // spills past the top-left corner
        c.Rect(60, 5, 1, 1, 3);            // degenerate 1x1 outline
        c.RectFill(70, 50, 1, 8, 12);      // 1-wide column
        c.Rect(20, 40, 0, 10, 14);         // zero width: no-op
        c.RectFill(20, 40, 10, -3, 14);    // negative height: no-op
    }

    private static void SceneCircles(VirtualConsole c)
    {
        c.Cls(0);
        c.Circ(64, 36, 30, 7);
        c.CircFill(64, 36, 12, 10);
        c.Circ(0, 0, 10, 8);               // clipped at the corner
        c.CircFill(127, 71, 15, 5);        // clipped at the opposite corner
        c.Circ(30, 60, 0, 3);              // radius 0: one pixel
        c.CircFill(90, 15, 0, 3);
        c.Circ(50, 50, -4, 14);            // negative: no-op
        c.CircFill(200, 36, 20, 14);       // fully off screen
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
        c.Spr(100, 120, 66);               // partially off screen
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
        c.Map(0, 0, 0, 56, 16, 2, flagFilter: 1); // only tiles whose sprite has flag bit 0
        c.Map(-3, -3, 100, 60, 6, 6);      // negative cell start: soft edge skip
    }

    private static void SceneClipCamera(VirtualConsole c)
    {
        c.Cls(2);
        c.Clip(20, 10, 60, 40);
        c.Camera(-5, 3);
        c.RectFill(0, 0, 128, 72, 4);      // fills the clip window only
        c.Line(0, 0, 127, 71, 8);
        c.Circ(50, 30, 25, 10);
        c.Print("CLIPPED", 18, 12, 3);
        c.Camera();
        c.Clip(70, 30, 200, 200);          // clip rectangle clamped to the screen
        c.RectFill(0, 0, 128, 72, 6);
        c.Clip();
        c.Rect(0, 0, 128, 72, 7);          // full-screen border after reset
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
        c.Print("edge", 120, 40, 5);       // spills past the right edge
        c.Print("low", 4, 69, 6);          // spills past the bottom
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
        c.Print("edge", 118, 42, 5, Font.Large);           // spills past the right edge
        c.Print("gjpqy", 4, 68, 6, Font.Large);            // tails spill past the bottom
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
        c.Clip(4, 4, 120, 64);
        c.Map(0, 4, 0, 8, 16, 2);
        c.Pal(8, 24);
        c.CircFill(64, 40, 18, 8);
        c.Palt(1, true);
        c.Spr(5, 40, 30, 2, 1, flipX: true);
        c.Print("SCORE 042", 30, 60, 3);
        c.Line(0, 0, 127, 71, 11);
        c.Camera();
        c.Clip();
        c.Pal();
        c.Palt();
        c.Rect(0, 0, 128, 72, 2);
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

    // Golden FNV-1a hashes of each scene's framebuffer. Recorded from the M1 rasterizer;
    // an intentional rasterizer change must update these consciously.
    private static readonly Dictionary<string, string> Goldens = new()
    {
        ["lines"] = "b3d1ebd67c610371",
        ["rects"] = "e4e69af28b8cf7ae",
        ["circles"] = "c018364e10fdfecc",
        ["sprites"] = "93efd947d2b0fdfb",
        // Re-pinned from 0341afdc417686cb when Map adopted the ratified "tile 0 is empty"
        // rule (API-8.md §Map): the 8 unset cells of row 2 used to blit sprite 0, and now
        // draw nothing. Verified the delta is exactly those cells — replaying the scene with
        // the old tile-0-draws-sprite-0 walk still reproduces 0341afdc417686cb.
        ["map"] = "2e911b814e5ef34b",
        ["clipcam"] = "c3e382cde046344b",
        ["palette"] = "e50a7b4ebb0452eb",
        // Re-pinned from 456fbad923d44569 by the lowercase redraw of 2026-08-18
        // (tasks/open/bug-font-readability.md): this is the only scene here that prints lowercase
        // ("line one/two/3", "edge", "low"), and 20 of the 26 lowercase glyphs changed shape.
        // Verified the delta is the font and nothing else — "clipcam" and "combo" print too, in
        // capitals and digits only, and both kept their hashes, as did every other scene.
        ["text"] = "c1870e56815f1682",
        // New pin, not a re-pin: the 4x6 font arrived with tasks/open/08-second-font.md and this
        // scene is the first thing that draws it. Nothing above moved — the small font path is
        // byte-for-byte what it was, which the eight anchors and all nine older scenes say too.
        ["textlarge"] = "96e90a4def524024",
        ["combo"] = "8c4abe5f22ba1527",
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

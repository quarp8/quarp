using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The PICO-8 symbol block in the small font (ADR-038): 32 glyphs behind the Unicode
/// codepoints a C# string literal naturally carries — "♥", "❎", "⬅" — plus the three Print
/// rules that came with them (mapped surrogate pairs are one glyph, U+FE0F is invisible,
/// everything unmapped keeps its historical box). The other half of the contract — that no
/// pixel of ASCII printing moved — is held by the snake's anchor hashes and the twelve demo
/// goldens, which this wave reran unchanged.
/// </summary>
public class SystemFontP8Tests
{
    private static VirtualConsole NewConsole() => new(ConsoleProfile.Profile8);

    private static byte Pixel(VirtualConsole c, int x, int y) =>
        c.Framebuffer.Pixels[y * c.Framebuffer.Width + x];

    /// <summary>Every codepoint the block maps, exactly the 32 of ADR-038.</summary>
    private static readonly int[] Mapped =
    {
        0x2588, 0x2592, 0x1F431, 0x2B07, 0x2591, 0x273D, 0x25CF, 0x2665,
        0x2609, 0xC6C3, 0x2302, 0x2B05, 0x1F610, 0x266A, 0x1F17E, 0x25C6,
        0x2026, 0x27A1, 0x2605, 0x29D7, 0x2B06, 0x02C7, 0x2227, 0x274E,
        0x25A4, 0x25A5, 0x25AE, 0x25A0, 0x25A1, 0x2059, 0x2058, 0x25B6,
    };

    private static int Ink(uint glyph)
    {
        int ink = 0;
        for (int row = 0; row < SystemFont.GlyphHeight; row++)
        {
            for (int col = 0; col < SystemFont.GlyphWidth; col++)
            {
                if (SystemFont.IsSet(glyph, col, row))
                {
                    ink++;
                }
            }
        }
        return ink;
    }

    /// <summary>
    /// The roll call: all 32 codepoints answer, none with an empty cell and none with the
    /// fallback box — a symbol that "maps" to the box would be a symbol that silently does not
    /// exist. The fallback is read through the unmapped-codepoint path rather than spelled out,
    /// so this test cannot drift from the real box.
    /// </summary>
    [Fact]
    public void EverySymbolOfTheBlockHasARealGlyph()
    {
        uint fallback = SystemFont.GetGlyph((int)'é');   // any unmapped codepoint is the box
        foreach (int codepoint in Mapped)
        {
            Assert.True(SystemFont.TryGetP8Glyph(codepoint, out uint glyph),
                $"U+{codepoint:X4} is not mapped");
            Assert.NotEqual(0u, glyph);
            Assert.NotEqual(fallback, glyph);
            Assert.Equal(glyph, SystemFont.GetGlyph(codepoint));
        }
        Assert.False(SystemFont.TryGetP8Glyph('A', out _), "ASCII does not go through this table");
        Assert.False(SystemFont.TryGetP8Glyph(0x1F480, out _), "unmapped emoji stay unmapped");
    }

    /// <summary>
    /// One symbol traced to its pixels: Print("♥") draws the heart's exact 3x5 bitmap and
    /// advances one small-font cell. If the loop, the table or the bit packing disagree about
    /// anything, some pixel here is wrong.
    /// </summary>
    [Fact]
    public void TheHeartPrintsItsPixels()
    {
        var c = NewConsole();
        int end = c.Print("♥", 10, 10, 7);
        Assert.Equal(14, end);

        string[] rows = { "101", "111", "111", "010", "000" };
        for (int row = 0; row < rows.Length; row++)
        {
            for (int col = 0; col < rows[row].Length; col++)
            {
                Assert.Equal(rows[row][col] == '1' ? 7 : 0, Pixel(c, 10 + col, 10 + row));
            }
        }
    }

    /// <summary>
    /// POOM's title ramp " ⁘⁙□■▮" is an <em>ordered</em> ramp: each glyph must carry strictly
    /// more ink than the one before, or the ramp reads backwards at some step and the fade
    /// flickers. Asserted over the actual glyph bitmaps rather than promised in a comment.
    ///
    /// <para>Break recipe: in <c>SystemFont.TryGetP8Glyph</c> swap the bitmaps of ⁘ (U+2058)
    /// and ⁙ (U+2059) — four dots and five change places and the strict inequality goes red
    /// between steps 1 and 2.</para>
    /// </summary>
    [Fact]
    public void PoomsDensityRampIsStrictlyIncreasing()
    {
        int[] ramp = { ' ', 0x2058, 0x2059, 0x25A1, 0x25A0, 0x25AE };   // " ⁘⁙□■▮"
        for (int i = 1; i < ramp.Length; i++)
        {
            int previous = Ink(SystemFont.GetGlyph(ramp[i - 1]));
            int current = Ink(SystemFont.GetGlyph(ramp[i]));
            Assert.True(previous < current,
                $"ramp step {i}: U+{ramp[i]:X4} has {current} ink pixels, not more than the {previous} before it");
        }
    }

    /// <summary>
    /// The three astral symbols (🅾, 🐱, 😐) are surrogate <em>pairs</em> in a C# string, and a
    /// mapped pair is one glyph and one advance. An unmapped pair keeps its historical two
    /// boxes — the consume happens only when the console actually knows the symbol, so nothing
    /// printed before ADR-038 moved.
    ///
    /// <para>Break recipe: in <c>VirtualConsole.Print</c> drop the <c>i++</c> after a mapped
    /// pair — the low surrogate is then printed again as a box and the advance assertion goes
    /// red at 18 instead of 14.</para>
    /// </summary>
    [Fact]
    public void AMappedSurrogatePairIsOneGlyph()
    {
        var c = NewConsole();
        Assert.Equal(14, c.Print("🅾", 10, 10, 7));       // one cell
        Assert.Equal(18, c.Print("💀", 10, 30, 7));       // unmapped: still two boxes
        Assert.Equal(14, c.Print("O", 10, 50, 7));        // and the button is not the letter:

        bool identical = true;
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                if (Pixel(c, 10 + col, 10 + row) != Pixel(c, 10 + col, 50 + row))
                {
                    identical = false;
                }
            }
        }
        Assert.False(identical, "🅾 must be a button, not a bitmap-sharing alias of 'O'");
    }

    /// <summary>
    /// U+FE0F, the emoji variation selector that "🅾️" and "⬅️" carry invisibly, draws nothing
    /// and advances nothing — so the emoji-style spelling and the bare one print pixel for
    /// pixel the same cell.
    /// </summary>
    [Fact]
    public void TheVariationSelectorIsInvisible()
    {
        var bare = NewConsole();
        var emoji = NewConsole();
        Assert.Equal(bare.Print("🅾X", 10, 10, 7), emoji.Print("🅾️X", 10, 10, 7));
        Assert.Equal(bare.Framebuffer.Pixels, emoji.Framebuffer.Pixels);

        var arrow = NewConsole();
        Assert.Equal(14, arrow.Print("⬅️", 10, 10, 7));
    }

    /// <summary>
    /// What did not change: an unmapped character draws exactly the box it always drew — which
    /// in the small font is still bitwise the digit '0', the known debt of
    /// tasks/open/debt-fallback-zero.md, asserted here so that the day it is repaid this test
    /// is the one that names the change.
    /// </summary>
    [Fact]
    public void UnmappedCharactersKeepTheOldBox()
    {
        var unmapped = NewConsole();
        var zero = NewConsole();
        unmapped.Print("é", 10, 10, 7);
        zero.Print("0", 10, 10, 7);
        Assert.Equal(zero.Framebuffer.Pixels, unmapped.Framebuffer.Pixels);
    }

    /// <summary>
    /// The large font stays out of it, deliberately: its glyph work is parked until the
    /// owner's post-release v2 pass (tasks/open/later-large-font-glyphs.md), so a symbol
    /// printed large draws that font's own fallback box — the marker for "no such glyph
    /// here", not a hastily scaled heart nobody accepted.
    /// </summary>
    [Fact]
    public void TheLargeFontDrawsItsFallbackForSymbols()
    {
        var c = NewConsole();
        c.Print("♥", 10, 10, 7, Font.Large);
        for (int row = 0; row < SystemFontLarge.GlyphHeight; row++)
        {
            for (int col = 0; col < SystemFontLarge.GlyphWidth; col++)
            {
                bool ink = SystemFontLarge.IsSet(SystemFontLarge.Fallback, col, row);
                Assert.Equal(ink ? 7 : 0, Pixel(c, 10 + col, 10 + row));
            }
        }
    }

    /// <summary>
    /// Porklike's separator "……………………" becomes a dotted line: the ellipsis glyph is two
    /// baseline dots per cell, so a chain of them alternates dot, gap, dot, gap across the
    /// screen instead of collapsing into the underscore's solid bar.
    /// </summary>
    [Fact]
    public void EllipsisChainsIntoADottedSeparator()
    {
        var c = NewConsole();
        c.Print("……", 10, 10, 7);
        // Each cell: ink at columns 0 and 2 of the bottom glyph row, nothing above.
        foreach (int cell in new[] { 10, 14 })
        {
            Assert.Equal(7, Pixel(c, cell, 14));
            Assert.Equal(0, Pixel(c, cell + 1, 14));
            Assert.Equal(7, Pixel(c, cell + 2, 14));
            for (int row = 10; row < 14; row++)
            {
                Assert.Equal(0, Pixel(c, cell, row));
            }
        }
    }

    /// <summary>
    /// The symbols the ports asked for by name, checked off one by one against their sources:
    /// Dank Tomb's title glyphs (arrows and both buttons), POOM's ▶, Porklike's …, and the
    /// suit of P8 icons every cart leans on. Redundant with the roll call above on purpose —
    /// this is the list a port author will grep for.
    /// </summary>
    [Fact]
    public void ThePortRequestedSymbolsAreAllThere()
    {
        foreach (int cp in new[]
        {
            0x2B05, 0x27A1, 0x2B06, 0x2B07,   // ⬅ ➡ ⬆ ⬇   Dank Tomb key rows, Terra title
            0x1F17E, 0x274E,                  // 🅾 ❎        both button badges
            0x2665, 0x2605, 0x25CF, 0x25C6,   // ♥ ★ ● ◆    HUDs and pickups
            0x25B6,                           // ▶            POOM's \23 pointer
            0x2026,                           // …            Porklike's separator
        })
        {
            Assert.True(SystemFont.TryGetP8Glyph(cp, out _), $"U+{cp:X4} missing");
        }

        // And the four arrows are four different pictures, not one rotated claim.
        uint left = SystemFont.GetGlyph(0x2B05);
        uint right = SystemFont.GetGlyph(0x27A1);
        uint up = SystemFont.GetGlyph(0x2B06);
        uint down = SystemFont.GetGlyph(0x2B07);
        Assert.NotEqual(left, right);
        Assert.NotEqual(up, down);
        Assert.NotEqual(left, up);
        Assert.NotEqual(right, down);
    }
}

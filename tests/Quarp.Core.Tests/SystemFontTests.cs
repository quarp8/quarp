using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The font as a system, not as 95 opinions. The lowercase redraw of 2026-08-18
/// (tasks/open/bug-font-readability.md) replaced a set that had no single x-height — 'n' and 'o'
/// were four rows tall, the bowls of 'b' and 'd' three — and the rules it settled on are worth
/// more than the pixels: a later hand editing one glyph "just a bit" is exactly how the old set
/// drifted. These tests hold the rules (which rows each class of letter may use), the identity
/// (no two characters may draw the same picture) and the four shapes the owner named as
/// unreadable, so that regressing any of the three is a red test and not a playtest report.
/// </summary>
public class SystemFontTests
{
    private const string XHeight = "acemnorsuvwxz";  // body in rows 2-4, nothing above
    private const string Ascenders = "bdfhklt";      // rows 0-1 carry the stem above that body
    private const string Descenders = "gpqy";        // body lifted to rows 1-3, row 4 is the tail
    private const string Dotted = "ij";              // dot on row 0, row 1 blank so it stays a dot

    /// <summary>True if the glyph has any ink in the given row; the row indices are the ones the
    /// class rules above are written in, so the assertions read like the rules.</summary>
    private static bool RowHasInk(uint glyph, int row)
    {
        for (int col = 0; col < SystemFont.GlyphWidth; col++)
        {
            if (SystemFont.IsSet(glyph, col, row))
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<char> AllChars()
    {
        for (char c = SystemFont.FirstChar; c <= SystemFont.LastChar; c++)
        {
            yield return c;
        }
    }

    /// <summary>
    /// Every printable character draws a different picture. This is the cheapest guard against
    /// the failure mode a 3x5 cell invites — running out of room and quietly reusing a shape,
    /// e.g. 'l' falling back onto 'I' or 'm' onto 'n' — and it covers the whole ASCII range, so
    /// a lowercase letter cannot collide with a digit or a punctuation mark either.
    /// </summary>
    [Fact]
    public void EveryPrintableCharacterHasItsOwnShape()
    {
        var seen = new Dictionary<uint, char>();
        foreach (char c in AllChars())
        {
            uint glyph = SystemFont.GetGlyph(c);
            Assert.False(seen.TryGetValue(glyph, out char clash),
                $"'{c}' and '{clash}' draw the same 3x5 shape — one of them is unreadable by definition");
            seen[glyph] = c;
        }
        Assert.Equal(SystemFont.LastChar - SystemFont.FirstChar + 1, seen.Count);
    }

    /// <summary>
    /// A lowercase letter must not be its capital with the same pixels: that is the PICO-8 answer
    /// (render everything in caps) and the project deliberately did not take it — mixed-case text
    /// in the dialogue cart is the reason the font exists in this shape at all.
    /// </summary>
    [Fact]
    public void EveryLowercaseLetterDiffersFromItsCapital()
    {
        for (char lower = 'a'; lower <= 'z'; lower++)
        {
            char upper = char.ToUpperInvariant(lower);
            Assert.True(SystemFont.GetGlyph(lower) != SystemFont.GetGlyph(upper),
                $"'{lower}' is drawn exactly like '{upper}' — lowercase text would read as shouting");
        }
    }

    /// <summary>
    /// The row rules of the redraw, one assertion per class. Their point is uniformity: a bowl
    /// three rows tall next to an arch four rows tall was the actual defect, not any single glyph.
    /// </summary>
    [Fact]
    public void EachClassOfLetterUsesTheRowsItsRuleAllows()
    {
        foreach (char c in XHeight)
        {
            uint g = SystemFont.GetGlyph(c);
            Assert.False(RowHasInk(g, 0), $"'{c}' is an x-height letter but has ink on row 0");
            Assert.False(RowHasInk(g, 1), $"'{c}' is an x-height letter but has ink on row 1");
            Assert.True(RowHasInk(g, 2), $"'{c}' does not reach the x-height line (row 2)");
        }

        foreach (char c in Ascenders)
        {
            uint g = SystemFont.GetGlyph(c);
            Assert.True(RowHasInk(g, 0), $"'{c}' is an ascender but row 0 is empty");
            Assert.True(RowHasInk(g, 1), $"'{c}' is an ascender but row 1 is empty");
        }

        foreach (char c in Descenders)
        {
            uint g = SystemFont.GetGlyph(c);
            Assert.False(RowHasInk(g, 0), $"'{c}' is a descender: its body is lifted, row 0 must stay clear");
            Assert.True(RowHasInk(g, 1), $"'{c}' is a descender but its body does not start on row 1");
            Assert.True(RowHasInk(g, 3), $"'{c}' is a descender but its body does not close on row 3");
        }

        foreach (char c in Dotted)
        {
            uint g = SystemFont.GetGlyph(c);
            Assert.True(RowHasInk(g, 0), $"'{c}' lost its dot (row 0)");
            Assert.False(RowHasInk(g, 1), $"'{c}' has ink on row 1 — the dot fuses with the stem");
        }

        // Nothing floats: every lowercase letter touches the bottom row, either with its body
        // (x-height and ascenders) or with its tail (descenders, j).
        for (char c = 'a'; c <= 'z'; c++)
        {
            Assert.True(RowHasInk(SystemFont.GetGlyph(c), 4), $"'{c}' does not touch the baseline row");
        }
    }

    /// <summary>
    /// The four letters the owner named as unrecognisable, pinned as pictures. A hash would not
    /// tell a reviewer what the glyph looks like, and the whole complaint was about looks; the
    /// art below is the deliverable of the redraw, so it is written down where a diff shows it.
    /// </summary>
    [Theory]
    [InlineData('d', "..#", "..#", "###", "#.#", "###")]  // box bowl + a stem that reads as a stem
    [InlineData('g', "...", ".##", "#.#", "###", "##.")]  // 'a' floated one row up, then a tail hooking left
    [InlineData('m', "...", "...", "###", "###", "#.#")]  // the heaviest arch: two feet, no third stem to give
    [InlineData('w', "...", "...", "#.#", "###", ".#.")]  // two arms merging into one foot
    public void TheGlyphsTheOwnerCalledOutLookLikeThis(char c, params string[] rows)
    {
        Assert.Equal(SystemFont.GlyphHeight, rows.Length);
        uint glyph = SystemFont.GetGlyph(c);
        for (int row = 0; row < SystemFont.GlyphHeight; row++)
        {
            Assert.Equal(SystemFont.GlyphWidth, rows[row].Length);
            for (int col = 0; col < SystemFont.GlyphWidth; col++)
            {
                Assert.Equal(rows[row][col] == '#', SystemFont.IsSet(glyph, col, row));
            }
        }
    }

    /// <summary>
    /// Every glyph fits the low 15 bits the encoding promises. A fourth digit slipped into one of
    /// the 3-bit groups compiles fine and silently shifts every row of that glyph.
    /// </summary>
    [Fact]
    public void NoGlyphOverflowsTheFifteenBitPacking()
    {
        foreach (char c in AllChars())
        {
            Assert.True(SystemFont.GetGlyph(c) < 1u << (SystemFont.GlyphWidth * SystemFont.GlyphHeight),
                $"'{c}' has bits outside the 5x3 packing");
        }
    }
}

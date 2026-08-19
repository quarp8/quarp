using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The 4x6 font as a system, the way <see cref="SystemFontTests"/> holds the 3x5 one. Same
/// three questions — do the classes of letter use the rows their rule allows, does every
/// character draw its own picture, and do the shapes that were hard at 3x5 look the way the
/// redesign says they look — plus the one rule that only exists here: <b>row 5 is for tails</b>.
/// The 3x5 font had to fake a descender by floating the body up; if a later hand "tidies" a
/// tail back into the body, these tests are what says no.
/// <para>Distance, not just difference, is asserted for letters and digits: a one-pixel
/// difference is a collision to a reader even though it passes an equality check. Punctuation
/// is exempt on purpose — '.', ':' and ';' are nested by design, and a semicolon that is not a
/// colon plus a tail would be the strange one.</para>
/// </summary>
public class SystemFontLargeTests
{
    private const string XHeight = "acemnorsuvwxz";  // body in rows 2-4, nothing above, no tail
    private const string Ascenders = "bdfhklt";      // rows 0-1 carry the stem above that body
    private const string Descenders = "gpqy";        // body in rows 2-4, row 5 is the tail
    private const string Dotted = "ij";              // dot on row 0, row 1 blank so it stays a dot

    private static bool RowHasInk(uint glyph, int row)
    {
        for (int col = 0; col < SystemFontLarge.GlyphWidth; col++)
        {
            if (SystemFontLarge.IsSet(glyph, col, row))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Number of pixels in which two glyphs differ — how far apart a reader sees them.</summary>
    private static int InkDistance(uint a, uint b) => System.Numerics.BitOperations.PopCount(a ^ b);

    private static IEnumerable<char> AllChars()
    {
        for (char c = SystemFontLarge.FirstChar; c <= SystemFontLarge.LastChar; c++)
        {
            yield return c;
        }
    }

    /// <summary>
    /// Every printable character — and the fallback box with them — draws a different picture.
    /// The 3x5 test has to leave its fallback out of this check because it collides with '0'
    /// (tasks/open/debt-fallback-zero.md); this font was drawn so the check can include it, and
    /// including it is the only thing that keeps it that way.
    /// </summary>
    [Fact]
    public void EveryPrintableCharacterAndTheFallbackHaveTheirOwnShape()
    {
        var seen = new Dictionary<uint, string>();
        foreach (char c in AllChars())
        {
            uint glyph = SystemFontLarge.GetGlyph(c);
            Assert.False(seen.TryGetValue(glyph, out string? clash),
                $"'{c}' and {clash} draw the same 4x6 shape — one of them is unreadable by definition");
            seen[glyph] = $"'{c}'";
        }
        Assert.False(seen.TryGetValue(SystemFontLarge.Fallback, out string? boxClash),
            $"the fallback box is pixel-identical to {boxClash} — an unsupported character would read as it");
        Assert.Equal(SystemFontLarge.LastChar - SystemFontLarge.FirstChar + 1, seen.Count);
    }

    /// <summary>
    /// No two letters or digits are within one pixel of each other. This is the check that
    /// caught the first draft's 'a' (the 3x5 box shape, which came out as 'n' plus a bottom
    /// bar) and 'N' (two diagonal pixels, one pixel away from 'H'); both were redrawn, and
    /// the closest pairs left are the ones that are close in any font — 0/O, 1/l, 5/S, D/O.
    /// </summary>
    [Fact]
    public void NoTwoLettersOrDigitsAreWithinOnePixel()
    {
        char[] alnum = AllChars().Where(char.IsLetterOrDigit).ToArray();
        for (int i = 0; i < alnum.Length; i++)
        {
            for (int j = i + 1; j < alnum.Length; j++)
            {
                int d = InkDistance(SystemFontLarge.GetGlyph(alnum[i]), SystemFontLarge.GetGlyph(alnum[j]));
                Assert.True(d >= 2,
                    $"'{alnum[i]}' and '{alnum[j]}' differ by {d} pixel(s) — that is a collision, not a distinction");
            }
        }
    }

    /// <summary>
    /// The pairs the 3x5 font could not separate, held at the distance the redraw bought them:
    /// four columns make 'm' the wide arch against a narrow 'n' (2 px apart at 3x5, 5 here) and
    /// 'w' two arms merging into one foot. Written as numbers rather than pictures because the
    /// claim is about how far apart they are, which a picture does not state.
    /// </summary>
    [Theory]
    [InlineData('m', 'n', 5)]
    [InlineData('w', 'v', 5)]
    [InlineData('w', 'u', 3)]   // the honest floor: four strokes do not fit in four columns
    [InlineData('v', 'u', 6)]
    [InlineData('a', 'o', 3)]
    [InlineData('l', '1', 2)]
    public void TheShapesThatWereHardAtThreeByFiveStayFarApart(char a, char b, int minimum)
    {
        int d = InkDistance(SystemFontLarge.GetGlyph(a), SystemFontLarge.GetGlyph(b));
        Assert.True(d >= minimum, $"'{a}' and '{b}' are {d} px apart, the design says at least {minimum}");
    }

    /// <summary>
    /// The row rules, one assertion per class — uniform x-height, ascenders above it, tails
    /// below the baseline, and nothing floating. Capitals and digits are held to the cap band
    /// as well: a digit that dips into the tail row would sit lower than the letters beside it.
    /// </summary>
    [Fact]
    public void EachClassOfLetterUsesTheRowsItsRuleAllows()
    {
        foreach (char c in XHeight)
        {
            uint g = SystemFontLarge.GetGlyph(c);
            Assert.False(RowHasInk(g, 0), $"'{c}' is an x-height letter but has ink on row 0");
            Assert.False(RowHasInk(g, 1), $"'{c}' is an x-height letter but has ink on row 1");
            Assert.True(RowHasInk(g, 2), $"'{c}' does not reach the x-height line (row 2)");
            Assert.False(RowHasInk(g, 5), $"'{c}' has no descender but inks the tail row");
        }

        foreach (char c in Ascenders)
        {
            uint g = SystemFontLarge.GetGlyph(c);
            Assert.True(RowHasInk(g, 0), $"'{c}' is an ascender but row 0 is empty");
            Assert.True(RowHasInk(g, 1), $"'{c}' is an ascender but row 1 is empty");
            Assert.False(RowHasInk(g, 5), $"'{c}' is an ascender, not a descender: the tail row must stay clear");
        }

        foreach (char c in Descenders)
        {
            uint g = SystemFontLarge.GetGlyph(c);
            Assert.False(RowHasInk(g, 0), $"'{c}' is a descender: its body sits at x-height, row 0 must stay clear");
            Assert.False(RowHasInk(g, 1), $"'{c}' is a descender: its body sits at x-height, row 1 must stay clear");
            Assert.True(RowHasInk(g, 2), $"'{c}' does not reach the x-height line");
            Assert.True(RowHasInk(g, 3), $"'{c}' has a hole in the middle of its body");
            Assert.True(RowHasInk(g, 4), $"'{c}' does not close its body on the baseline");
            Assert.True(RowHasInk(g, 5), $"'{c}' is a descender with no tail — the whole point of the 6th row");
        }

        foreach (char c in Dotted)
        {
            uint g = SystemFontLarge.GetGlyph(c);
            Assert.True(RowHasInk(g, 0), $"'{c}' lost its dot (row 0)");
            Assert.False(RowHasInk(g, 1), $"'{c}' has ink on row 1 — the dot fuses with the stem");
        }

        // Nothing floats: every letter touches the baseline row with body or stem.
        for (char c = 'a'; c <= 'z'; c++)
        {
            Assert.True(RowHasInk(SystemFontLarge.GetGlyph(c), 4), $"'{c}' does not touch the baseline row");
        }

        // Capitals and digits span the cap band exactly: top row inked, tail row clear.
        foreach (char c in AllChars().Where(ch => char.IsUpper(ch) || char.IsDigit(ch)))
        {
            uint g = SystemFontLarge.GetGlyph(c);
            Assert.True(RowHasInk(g, 0), $"'{c}' does not reach the cap line (row 0)");
            Assert.True(RowHasInk(g, 4), $"'{c}' does not sit on the baseline (row 4)");
            Assert.False(RowHasInk(g, 5), $"'{c}' dips into the tail row and would sit low in a word");
        }
    }

    /// <summary>
    /// Eight glyphs pinned as pictures — the four the owner called out at 3x5 (a, g, m, w) plus
    /// the four that carry the rest of the design (e, s, r, t). A hash would not show a reviewer
    /// what changed, and what this font delivers is exactly how the letters look.
    /// </summary>
    [Theory]
    [InlineData('a', "....", "....", ".###", "#..#", "####", "....")]  // arc, counter, flat foot
    [InlineData('e', "....", "....", ".##.", "###.", ".##.", "....")]  // bowl with the bar reaching left
    [InlineData('g', "....", "....", ".###", "#..#", ".###", ".##.")]  // real tail under the baseline
    [InlineData('m', "....", "....", "####", "#..#", "#..#", "....")]  // the four-column arch
    [InlineData('w', "....", "....", "#..#", "####", ".##.", "....")]  // two arms merging into one foot
    [InlineData('v', "....", "....", "#.#.", "#.#.", ".#..", "....")]  // narrow, so 'w' can be the wide one
    [InlineData('s', "....", "....", ".###", ".##.", "###.", "....")]  // spine on the diagonal
    [InlineData('r', "....", "....", "###.", "#...", "#...", "....")]  // stem and shoulder, no right leg
    [InlineData('t', ".#..", ".#..", "####", ".#..", ".##.", "....")]  // crossbar sits on the x-height line
    public void TheGlyphsThatCarryTheDesignLookLikeThis(char c, params string[] rows)
    {
        Assert.Equal(SystemFontLarge.GlyphHeight, rows.Length);
        uint glyph = SystemFontLarge.GetGlyph(c);
        for (int row = 0; row < SystemFontLarge.GlyphHeight; row++)
        {
            Assert.Equal(SystemFontLarge.GlyphWidth, rows[row].Length);
            for (int col = 0; col < SystemFontLarge.GlyphWidth; col++)
            {
                Assert.Equal(rows[row][col] == '#', SystemFontLarge.IsSet(glyph, col, row));
            }
        }
    }

    /// <summary>
    /// Every glyph fits the low 24 bits the encoding promises. A fifth digit slipped into one of
    /// the 4-bit groups compiles fine and silently shifts every row of that glyph one place.
    /// </summary>
    [Fact]
    public void NoGlyphOverflowsTheTwentyFourBitPacking()
    {
        foreach (char c in AllChars())
        {
            Assert.True(SystemFontLarge.GetGlyph(c) < 1u << (SystemFontLarge.GlyphWidth * SystemFontLarge.GlyphHeight),
                $"'{c}' has bits outside the 6x4 packing");
        }
        Assert.True(SystemFontLarge.Fallback < 1u << (SystemFontLarge.GlyphWidth * SystemFontLarge.GlyphHeight));
    }

    /// <summary>
    /// Anything outside ASCII 32-126 is the fallback box, on both ends of the range.
    /// </summary>
    [Fact]
    public void CharactersOutsideTheRangeDrawTheFallbackBox()
    {
        Assert.Equal(SystemFontLarge.Fallback, SystemFontLarge.GetGlyph('\n'));
        Assert.Equal(SystemFontLarge.Fallback, SystemFontLarge.GetGlyph('é'));
        Assert.Equal(SystemFontLarge.Fallback, SystemFontLarge.GetGlyph('Ж'));
    }

    /// <summary>
    /// The metrics, stated once as numbers: ink 4x6 in a 5x7 cell, which is what every layout
    /// downstream multiplies by. 128/5 = 25 columns and 72/7 = 10 rows on QUARP-8, against the
    /// small font's 32 x 12 — the trade the second font exists to offer.
    /// </summary>
    [Fact]
    public void MetricsAreFourBySixInkInAFiveBySevenCell()
    {
        Assert.Equal(4, SystemFontLarge.GlyphWidth);
        Assert.Equal(6, SystemFontLarge.GlyphHeight);
        Assert.Equal(5, SystemFontLarge.CellWidth);
        Assert.Equal(7, SystemFontLarge.CellHeight);
        Assert.Equal(SystemFontLarge.GlyphWidth + 1, SystemFontLarge.CellWidth);
        Assert.Equal(SystemFontLarge.GlyphHeight + 1, SystemFontLarge.CellHeight);
        Assert.Equal(25, ConsoleProfile.Profile8.Width / SystemFontLarge.CellWidth);
        Assert.Equal(10, ConsoleProfile.Profile8.Height / SystemFontLarge.CellHeight);
        Assert.Equal(32, ConsoleProfile.Profile8Wide.Width / SystemFontLarge.CellWidth);
    }
}

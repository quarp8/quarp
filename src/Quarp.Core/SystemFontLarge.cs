namespace Quarp.Core;

/// <summary>
/// The second system font: 4x6 pixel glyphs in a 5x7 cell (1 px of air right and below),
/// ASCII 32-126 — 32 columns x 12 rows of text on the 160x90 screen (12 whole cells are 84 px;
/// the 6 px left over still hold a 13th line's glyphs, only its trailing air row is off-screen).
/// A cartridge picks it per call through <see cref="Quarp.Api.Font"/>; the small
/// <see cref="SystemFont"/> stays the default, and neither font can be switched by the player
/// (Print writes the framebuffer, and the frame hash is the determinism contract).
/// Every glyph is drawn from scratch for Quarp — original data under the project's own license,
/// no borrowed fonts, the same rule the 3x5 set carries.
/// <para>Encoding: 6 rows x 4 columns packed into the low 24 bits of a uint as
/// (row0 &lt;&lt; 20) | (row1 &lt;&lt; 16) | ... | row5, the most significant bit of each
/// 4-bit row being the leftmost pixel. One nibble per row, so the binary literals below read
/// as the picture they draw.</para>
///
/// <para><b>What the extra row and column buy.</b> The 3x5 font had to fake descenders by
/// floating the body of g, p, q and y one row up — there was no room under the baseline
/// (see <see cref="SystemFont"/>). Here the cap band is rows 0-4, the x-height band is rows
/// 2-4, and <em>row 5 exists for tails only</em>: g, j, p, q, y and the comma finally hang
/// below the baseline like letters instead of like compromises, and every other letter keeps
/// the same body a reader's eye can line up. That single rule — one x-height, one baseline,
/// tails below it — is what the class tests in SystemFontLargeTests hold, and it is worth more
/// than any individual glyph: the 3x5 set drifted precisely because nothing held it.</para>
///
/// <para><b>What four columns still do not buy.</b> A true 'm' needs three separated stems,
/// which costs five columns (stem, gap, stem, gap, stem); at four it is still a silhouette
/// decision. What changed is the room to make that silhouette unmistakable: 'n' is the narrow
/// three-column arch and 'm' the four-column one, so the pair now differs by 5 pixels instead
/// of the 2 it differs by at 3x5. 'w' is likewise two arms merging into one foot — 5 pixels
/// from the three-column 'v', but only 3 from 'u', which is the honest floor for a letter that
/// wants four strokes in four columns. Ink distance, not intent, is what a reader sees across
/// a room, and the numbers above are asserted in SystemFontLargeTests rather than believed:
/// the first draft claimed 5 for 'w' against 'u' and the test said 3.</para>
///
/// <para><b>Fallback.</b> Unknown characters draw a solid 4x5 hollow box that no real glyph
/// resembles. The 3x5 font's fallback is pixel-identical to its '0' (tasks/open/debt-fallback-zero.md),
/// which turns "no such character" into a digit the author then hunts for in their data; this
/// table simply does not repeat that, and the tests here include the fallback in the
/// pairwise-distinctness check that the 3x5 tests have to exclude it from.</para>
/// </summary>
public static class SystemFontLarge
{
    public const int GlyphWidth = 4;
    public const int GlyphHeight = 6;

    /// <summary>Horizontal advance per character: glyph plus 1 px spacing.</summary>
    public const int CellWidth = 5;

    /// <summary>Line height: glyph plus 1 px spacing.</summary>
    public const int CellHeight = 7;

    public const char FirstChar = ' ';
    public const char LastChar = '~';

    /// <summary>
    /// Hollow box shown for any character outside ASCII 32-126. Deliberately not any glyph in
    /// the table — a marker that reads as "this character does not exist here".
    /// </summary>
    public const uint Fallback = 0b1111_1001_1001_1001_1111_0000;

    /// <summary>Packed 4x6 glyph for the character; unknown characters get <see cref="Fallback"/>.</summary>
    public static uint GetGlyph(char c) =>
        c is >= FirstChar and <= LastChar ? _glyphs[c - FirstChar] : Fallback;

    /// <summary>True if the glyph has an ink pixel at (col, row); col 0..3 left to right, row 0..5 top to bottom.</summary>
    public static bool IsSet(uint glyph, int col, int row) =>
        ((glyph >> ((GlyphHeight - 1 - row) * GlyphWidth + (GlyphWidth - 1 - col))) & 1) != 0;

    // Rows, top to bottom: 0-1 ascender band, 2-4 x-height band (4 is the baseline),
    // 5 descender tails. Capitals and digits span rows 0-4 and never touch row 5.
    private static readonly uint[] _glyphs =
    {
        0b0000_0000_0000_0000_0000_0000, // ' '
        0b0100_0100_0100_0000_0100_0000, // '!'
        0b1010_1010_0000_0000_0000_0000, // '"'
        0b1010_1111_1010_1111_1010_0000, // '#'
        0b0111_1100_0110_0011_1110_0000, // '$'
        0b1001_0001_0110_1000_1001_0000, // '%'
        0b0110_1001_0110_1010_0111_0000, // '&'
        0b0100_0100_0000_0000_0000_0000, // '\''
        0b0010_0100_0100_0100_0010_0000, // '('
        0b0100_0010_0010_0010_0100_0000, // ')'
        0b0000_0000_1010_0100_1010_0000, // '*'
        0b0000_0000_0100_1110_0100_0000, // '+'
        0b0000_0000_0000_0000_0100_1100, // ','  the one punctuation mark with a real tail
        0b0000_0000_0000_1110_0000_0000, // '-'
        0b0000_0000_0000_0000_0100_0000, // '.'
        0b0001_0010_0010_0100_1000_0000, // '/'
        0b0110_1011_1101_1001_0110_0000, // '0'  slashed, so it cannot be read as 'O'
        0b0100_1100_0100_0100_1110_0000, // '1'
        0b0110_1001_0010_0100_1111_0000, // '2'
        0b1110_0001_0110_0001_1110_0000, // '3'
        0b0010_0110_1010_1111_0010_0000, // '4'
        0b1111_1000_1110_0001_1110_0000, // '5'
        0b0010_0100_1110_1001_0110_0000, // '6'
        0b1111_0001_0010_0100_0100_0000, // '7'
        0b0110_1001_0110_1001_0110_0000, // '8'
        0b0110_1001_0111_0001_1110_0000, // '9'
        0b0000_0000_0100_0000_0100_0000, // ':'
        0b0000_0000_0100_0000_0100_1100, // ';'
        0b0010_0100_1000_0100_0010_0000, // '<'
        0b0000_0000_1110_0000_1110_0000, // '='
        0b1000_0100_0010_0100_1000_0000, // '>'
        0b0110_1001_0010_0000_0010_0000, // '?'
        0b0110_1001_1011_1000_0111_0000, // '@'
        0b0110_1001_1111_1001_1001_0000, // 'A'
        0b1110_1001_1110_1001_1110_0000, // 'B'
        0b0111_1000_1000_1000_0111_0000, // 'C'
        0b1110_1001_1001_1001_1110_0000, // 'D'
        0b1111_1000_1110_1000_1111_0000, // 'E'
        0b1111_1000_1110_1000_1000_0000, // 'F'
        0b0111_1000_1011_1001_0111_0000, // 'G'
        0b1001_1001_1111_1001_1001_0000, // 'H'
        0b1110_0100_0100_0100_1110_0000, // 'I'
        0b0111_0010_0010_1010_0110_0000, // 'J'
        0b1001_1010_1100_1010_1001_0000, // 'K'
        0b1000_1000_1000_1000_1111_0000, // 'L'
        0b1001_1111_1001_1001_1001_0000, // 'M'  bar under the cap line; 'W' is its mirror
        0b1001_1101_1101_1011_1001_0000, // 'N'  three-pixel diagonal: two would leave it 2 px from 'H'
        0b0110_1001_1001_1001_0110_0000, // 'O'
        0b1110_1001_1110_1000_1000_0000, // 'P'
        0b0110_1001_1001_1010_0111_0000, // 'Q'
        0b1110_1001_1110_1010_1001_0000, // 'R'
        0b0111_1000_0110_0001_1110_0000, // 'S'
        0b1111_0100_0100_0100_0100_0000, // 'T'
        0b1001_1001_1001_1001_0110_0000, // 'U'
        0b1001_1001_1001_0110_0100_0000, // 'V'
        0b1001_1001_1001_1111_1001_0000, // 'W'
        0b1001_0110_0110_0110_1001_0000, // 'X'  waisted over three rows: a one-row waist sits 2 px from 'H'
        0b1001_1001_0110_0100_0100_0000, // 'Y'
        0b1111_0001_0110_1000_1111_0000, // 'Z'
        0b0111_0100_0100_0100_0111_0000, // '['
        0b1000_0100_0100_0010_0001_0000, // '\\'
        0b1110_0010_0010_0010_1110_0000, // ']'
        0b0100_1010_0000_0000_0000_0000, // '^'
        0b0000_0000_0000_0000_0000_1111, // '_'  below the baseline, where an underscore belongs
        0b1000_0100_0000_0000_0000_0000, // '`'
        0b0000_0000_0111_1001_1111_0000, // 'a'  arc, counter, flat foot: 3 px from 'c', 3 from 'o'
        0b1000_1000_1110_1001_1110_0000, // 'b'
        0b0000_0000_0111_1000_0111_0000, // 'c'
        0b0001_0001_0111_1001_0111_0000, // 'd'
        0b0000_0000_0110_1110_0110_0000, // 'e'  bowl with the crossbar reaching left
        0b0111_0100_1111_0100_0100_0000, // 'f'
        0b0000_0000_0111_1001_0111_0110, // 'g'  the bowl of 'd' plus a tail that hooks left
        0b1000_1000_1110_1010_1010_0000, // 'h'
        0b0100_0000_0100_0100_0100_0000, // 'i'  dot on row 0, row 1 blank so it stays a dot
        0b0010_0000_0010_0010_0010_1100, // 'j'
        0b1000_1000_1010_1100_1010_0000, // 'k'
        0b0100_0100_0100_0100_0110_0000, // 'l'  foot, so it is 2 px from '1' and from '|'
        0b0000_0000_1111_1001_1001_0000, // 'm'  the four-column arch against 'n', the three-column one
        0b0000_0000_1110_1010_1010_0000, // 'n'
        0b0000_0000_0110_1001_0110_0000, // 'o'
        0b0000_0000_1110_1001_1110_1000, // 'p'  the bowl of 'b' plus a straight tail
        0b0000_0000_0111_1001_0111_0001, // 'q'
        0b0000_0000_1110_1000_1000_0000, // 'r'
        0b0000_0000_0111_0110_1110_0000, // 's'
        0b0100_0100_1111_0100_0110_0000, // 't'
        0b0000_0000_1001_1001_0111_0000, // 'u'
        0b0000_0000_1010_1010_0100_0000, // 'v'  three columns, taper to a point: 6 px from 'u'
        0b0000_0000_1001_1111_0110_0000, // 'w'  two arms merging into one foot: 5 px from 'v', 3 from 'u'
        0b0000_0000_1001_0110_1001_0000, // 'x'
        0b0000_0000_1001_1001_0111_0110, // 'y'
        0b0000_0000_1111_0110_1111_0000, // 'z'
        0b0011_0100_1100_0100_0011_0000, // '{'
        0b0100_0100_0100_0100_0100_0100, // '|'  full height, tail row included: that is what separates it from 'l'
        0b1100_0010_0011_0010_1100_0000, // '}'
        0b0000_0000_0011_1100_0000_0000, // '~'
    };
}

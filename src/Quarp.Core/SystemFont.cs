namespace Quarp.Core;

/// <summary>
/// The built-in 4x6 system font: 3x5 pixel glyphs plus 1 px spacing to the right and
/// below, ASCII 32-126 — 40 columns x 15 rows of text on the 160x90 screen (SPEC-8 §1).
/// Every glyph is drawn from scratch for Quarp; the data below is original and carries
/// the project's own license — no borrowed fonts.
/// Encoding: 5 rows x 3 columns packed into the low 15 bits of a uint as
/// (row0 &lt;&lt; 12) | (row1 &lt;&lt; 9) | (row2 &lt;&lt; 6) | (row3 &lt;&lt; 3) | row4,
/// where the most significant bit of each 3-bit row is the leftmost pixel.
/// </summary>
public static class SystemFont
{
    public const int GlyphWidth = 3;
    public const int GlyphHeight = 5;

    /// <summary>Horizontal advance per character: glyph plus 1 px spacing.</summary>
    public const int CellWidth = 4;

    /// <summary>Line height: glyph plus 1 px spacing.</summary>
    public const int CellHeight = 6;

    public const char FirstChar = ' ';
    public const char LastChar = '~';

    /// <summary>Hollow box shown for any character outside ASCII 32-126.</summary>
    private const uint Fallback = 0b111_101_101_101_111;

    /// <summary>
    /// Packed 3x5 glyph for the character; unknown characters get a hollow box. This is the
    /// call the shell's own overlay text uses and it deliberately answers for ASCII only —
    /// the PICO-8 symbol block lives behind <see cref="GetGlyph(int)"/> and
    /// <see cref="TryGetP8Glyph"/>, which is what <c>Print</c> goes through (ADR-038).
    /// </summary>
    public static uint GetGlyph(char c) =>
        c is >= FirstChar and <= LastChar ? _glyphs[c - FirstChar] : Fallback;

    /// <summary>
    /// Packed 3x5 glyph for a Unicode codepoint: ASCII 32-126 from the table below, the PICO-8
    /// symbol block (ADR-038) from <see cref="TryGetP8Glyph"/>, everything else the hollow box.
    /// The codepoint form exists because three of the PICO-8 symbols (🐱, 😐, 🅾) live outside
    /// the BMP and cannot be spelled as a single <c>char</c>.
    /// </summary>
    public static uint GetGlyph(int codepoint)
    {
        if (codepoint is >= FirstChar and <= LastChar)
        {
            return _glyphs[codepoint - FirstChar];
        }
        return TryGetP8Glyph(codepoint, out uint p8) ? p8 : Fallback;
    }

    /// <summary>
    /// The PICO-8 symbol block (ADR-038): the 26 glyphs PICO-8 keeps at 0x80-0x99 plus the six
    /// low-charset shapes the ports actually print (▮ ■ □ ⁙ ⁘ ▶ — POOM's title ramp and its
    /// button pointer), addressed by the Unicode codepoint a C# string literal naturally
    /// carries ("♥", "❎", "⬅"). 32 glyphs, drawn from scratch for Quarp in the same original
    /// 3x5 grid as the rest of this file — no borrowed font data.
    ///
    /// <para>Two deliberate bitmap collisions at this size, both harmless: █ and ▮ are each
    /// the full 15-pixel block (a full cell is a full cell), and no others — POOM's density
    /// ramp " ⁘⁙□■▮" stays strictly increasing in ink, which SystemFontP8Tests assert rather
    /// than trust. Nothing here answers for ASCII, so the 95 glyphs above are untouchable
    /// through this table by construction.</para>
    /// </summary>
    public static bool TryGetP8Glyph(int codepoint, out uint glyph)
    {
        uint found = codepoint switch
        {
            0x2588 => 0b111_111_111_111_111,  // █ full block (P8 0x80)
            0x2592 => 0b101_010_101_010_101,  // ▒ medium shade (P8 0x81)
            0x1F431 => 0b101_111_101_111_000, // 🐱 cat face (P8 0x82)
            0x2B07 => 0b010_010_111_111_010,  // ⬇ down arrow (P8 0x83)
            0x2591 => 0b100_001_000_100_001,  // ░ light shade (P8 0x84)
            0x273D => 0b101_010_111_010_101,  // ✽ heavy asterisk (P8 0x85)
            0x25CF => 0b010_111_111_111_010,  // ● filled circle (P8 0x86)
            0x2665 => 0b101_111_111_010_000,  // ♥ heart (P8 0x87)
            0x2609 => 0b010_101_111_101_010,  // ☉ sun/orb (P8 0x88)
            0xC6C3 => 0b010_111_010_101_101,  // 웃 figure (P8 0x89)
            0x2302 => 0b010_101_101_111_000,  // ⌂ house (P8 0x8A)
            0x2B05 => 0b010_110_111_110_010,  // ⬅ left arrow (P8 0x8B)
            0x1F610 => 0b000_101_000_111_000, // 😐 neutral face (P8 0x8C)
            0x266A => 0b011_010_010_110_110,  // ♪ note (P8 0x8D)
            0x1F17E => 0b010_111_101_111_010, // 🅾 O button (P8 0x8E)
            0x25C6 => 0b000_010_111_010_000,  // ◆ diamond (P8 0x8F)
            0x2026 => 0b000_000_000_000_101,  // … ellipsis (P8 0x90)
            0x27A1 => 0b010_011_111_011_010,  // ➡ right arrow (P8 0x91)
            0x2605 => 0b010_111_010_101_000,  // ★ star (P8 0x92)
            0x29D7 => 0b111_010_010_101_111,  // ⧗ hourglass (P8 0x93)
            0x2B06 => 0b010_111_111_010_010,  // ⬆ up arrow (P8 0x94)
            0x02C7 => 0b101_010_000_000_000,  // ˇ caron (P8 0x95)
            0x2227 => 0b000_010_101_101_000,  // ∧ wedge (P8 0x96)
            0x274E => 0b111_101_010_101_111,  // ❎ X button (P8 0x97)
            0x25A4 => 0b111_000_111_000_111,  // ▤ horizontal fill (P8 0x98)
            0x25A5 => 0b101_101_101_101_101,  // ▥ vertical fill (P8 0x99)
            0x25AE => 0b111_111_111_111_111,  // ▮ full bar (P8 0x10; = █ at this size)
            0x25A0 => 0b000_111_111_111_000,  // ■ filled square (P8 0x11)
            0x25A1 => 0b000_111_101_111_000,  // □ hollow square (P8 0x12)
            0x2059 => 0b101_000_010_000_101,  // ⁙ five dots (P8 0x13)
            0x2058 => 0b000_101_000_101_000,  // ⁘ four dots (P8 0x14)
            0x25B6 => 0b100_110_111_110_100,  // ▶ pointer (POOM's \23)
            _ => uint.MaxValue,
        };
        if (found == uint.MaxValue)
        {
            glyph = Fallback;
            return false;
        }
        glyph = found;
        return true;
    }

    /// <summary>True if the glyph has an ink pixel at (col, row); col 0..2 left to right, row 0..4 top to bottom.</summary>
    public static bool IsSet(uint glyph, int col, int row) =>
        ((glyph >> ((GlyphHeight - 1 - row) * GlyphWidth + (GlyphWidth - 1 - col))) & 1) != 0;

    private static readonly uint[] _glyphs =
    {
        0b000_000_000_000_000, // ' '
        0b010_010_010_000_010, // '!'
        0b101_101_000_000_000, // '"'
        0b101_111_101_111_101, // '#'
        0b011_110_010_011_110, // '$'
        0b101_001_010_100_101, // '%'
        0b010_101_010_101_011, // '&'
        0b010_010_000_000_000, // '\''
        0b001_010_010_010_001, // '('
        0b100_010_010_010_100, // ')'
        0b000_101_010_101_000, // '*'
        0b000_010_111_010_000, // '+'
        0b000_000_000_010_100, // ','
        0b000_000_111_000_000, // '-'
        0b000_000_000_000_010, // '.'
        0b001_001_010_100_100, // '/'
        0b111_101_101_101_111, // '0'
        0b010_110_010_010_111, // '1'
        0b111_001_111_100_111, // '2'
        0b111_001_011_001_111, // '3'
        0b101_101_111_001_001, // '4'
        0b111_100_111_001_111, // '5'
        0b111_100_111_101_111, // '6'
        0b111_001_001_010_010, // '7'
        0b111_101_111_101_111, // '8'
        0b111_101_111_001_111, // '9'
        0b000_010_000_010_000, // ':'
        0b000_010_000_010_100, // ';'
        0b001_010_100_010_001, // '<'
        0b000_111_000_111_000, // '='
        0b100_010_001_010_100, // '>'
        0b110_001_010_000_010, // '?'
        0b010_101_111_100_011, // '@'
        0b010_101_111_101_101, // 'A'
        0b110_101_110_101_110, // 'B'
        0b011_100_100_100_011, // 'C'
        0b110_101_101_101_110, // 'D'
        0b111_100_110_100_111, // 'E'
        0b111_100_110_100_100, // 'F'
        0b011_100_101_101_011, // 'G'
        0b101_101_111_101_101, // 'H'
        0b111_010_010_010_111, // 'I'
        0b001_001_001_101_010, // 'J'
        0b101_110_100_110_101, // 'K'
        0b100_100_100_100_111, // 'L'
        0b101_111_101_101_101, // 'M'
        0b110_101_101_101_101, // 'N'
        0b010_101_101_101_010, // 'O'
        0b110_101_110_100_100, // 'P'
        0b010_101_101_010_001, // 'Q'
        0b110_101_110_101_101, // 'R'
        0b011_100_010_001_110, // 'S'
        0b111_010_010_010_010, // 'T'
        0b101_101_101_101_111, // 'U'
        0b101_101_101_010_010, // 'V'
        0b101_101_101_111_101, // 'W'
        0b101_101_010_101_101, // 'X'
        0b101_101_010_010_010, // 'Y'
        0b111_001_010_100_111, // 'Z'
        0b110_100_100_100_110, // '['
        0b100_100_010_001_001, // '\\'
        0b011_001_001_001_011, // ']'
        0b010_101_000_000_000, // '^'
        0b000_000_000_000_111, // '_'
        0b100_010_000_000_000, // '`'
        0b000_011_101_101_011, // 'a'
        0b100_100_110_101_110, // 'b'
        0b000_011_100_100_011, // 'c'
        0b001_001_011_101_011, // 'd'
        0b000_011_101_110_011, // 'e'
        0b011_010_111_010_010, // 'f'
        0b000_011_101_011_110, // 'g'
        0b100_100_110_101_101, // 'h'
        0b010_000_010_010_010, // 'i'
        0b001_000_001_001_110, // 'j'
        0b100_100_101_110_101, // 'k'
        0b010_010_010_010_011, // 'l'
        0b000_110_111_101_101, // 'm'
        0b000_110_101_101_101, // 'n'
        0b000_010_101_101_010, // 'o'
        0b000_110_101_110_100, // 'p'
        0b000_011_101_011_001, // 'q'
        0b000_110_100_100_100, // 'r'
        0b000_011_100_001_110, // 's'
        0b010_111_010_010_001, // 't'
        0b000_101_101_101_011, // 'u'
        0b000_101_101_101_010, // 'v'
        0b000_101_101_111_011, // 'w'
        0b000_101_010_010_101, // 'x'
        0b000_101_101_011_110, // 'y'
        0b000_111_001_010_111, // 'z'
        0b011_010_110_010_011, // '{'
        0b010_010_010_010_010, // '|'
        0b110_010_011_010_110, // '}'
        0b000_011_110_000_000, // '~'
    };
}

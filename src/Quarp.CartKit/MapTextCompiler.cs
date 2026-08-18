using System.Globalization;

namespace Quarp.CartKit;

/// <summary>
/// Compiles the author-facing map text — <c>map.csv</c> in the cart folder — into the bytes of
/// <c>map.bin</c>. The full specification is docs/MAP-FORMAT.md; this type is that document in
/// code, and the two are meant to be read together.
///
/// <para><b>Shape.</b> <c>map.bin</c> has no header at all: it <i>is</i> the payload, 256 x 72
/// tile bytes row-major, 18432 of them, exactly what <see cref="CartData.Map"/> holds and what
/// the console indexes as <c>map[y * 256 + x]</c>. The audio banks carry a magic and a version
/// because they have internal structure that could be misread; a flat byte grid has none, its
/// size is its own check (<c>CartSource</c> loads it with the fixed-size rule), and giving it a
/// header now would change bytes that shipped in M1.</para>
///
/// <para><b>Source.</b> The text side is deliberately the format Tiled writes from
/// <c>File -&gt; Export As... -&gt; CSV</c>: one line per map row, comma-separated decimal tile
/// ids counted from 0, <c>-1</c> for an empty cell, no trailing comma, CRLF on Windows. Reading
/// that file as-is is the whole reason the format is what it is — "import from Tiled" is the
/// same code path as "written by hand", not a second pipeline. Two consequences the author feels:
/// <c>-1</c> is accepted as a synonym of <c>0</c> (the one deliberate exception to this
/// project's "one value, one spelling" rule — see docs/MAP-FORMAT.md §4), and a cell carrying
/// Tiled's flip flags is a hard error rather than a silently un-flipped tile.</para>
///
/// <para>Compilation is a pure function of the text — no clock, no culture, no dictionary
/// iteration — which is what lets <c>quarp map build</c> be rerun and compared byte for byte.</para>
/// </summary>
public static class MapTextCompiler
{
    /// <summary>Cells across, from SPEC-8 §3; the same constant the console and CartData use.</summary>
    public const int Width = CartData.MapWidth;

    /// <summary>Cells down, from SPEC-8 §3.</summary>
    public const int Height = CartData.MapHeight;

    /// <summary>Bytes of <c>map.bin</c>: one byte per cell, and the file is nothing else. 18432.</summary>
    public const int PayloadSize = Width * Height;

    /// <summary>
    /// Highest tile index a cell can hold. Profile 8 has 256 sprites (SPEC-8 §3) and a cell is
    /// one byte, so the two limits are the same number and neither can move without the other.
    /// </summary>
    public const int MaxTile = 255;

    /// <summary>
    /// How Tiled's CSV export spells an empty cell. Accepted as a synonym of tile 0, which is
    /// what an empty cell already is for us (<c>VirtualConsole.Map</c> skips tile 0, API-8 §3).
    /// </summary>
    public const int EmptyCell = -1;

    // The top four bits of a Tiled global tile id are flags, not part of the index. They are
    // named here because the diagnostics name them: an author who sees 3221225474 in a file has
    // no way to know it means "tile 2, flipped both ways" unless we say so.

    /// <summary>Tiled GID bit 31: the cell is mirrored left-to-right.</summary>
    public const uint FlippedHorizontally = 0x8000_0000;

    /// <summary>Tiled GID bit 30: the cell is mirrored top-to-bottom.</summary>
    public const uint FlippedVertically = 0x4000_0000;

    /// <summary>Tiled GID bit 29: the cell is mirrored along its diagonal (this is how Tiled spells rotation).</summary>
    public const uint FlippedDiagonally = 0x2000_0000;

    /// <summary>Tiled GID bit 28: the 120-degree rotation of hexagonal maps.</summary>
    public const uint RotatedHexagonal120 = 0x1000_0000;

    /// <summary>All four flag bits. Checked whole — bit 29 included, which is the one people forget.</summary>
    public const uint FlipFlagMask =
        FlippedHorizontally | FlippedVertically | FlippedDiagonally | RotatedHexagonal120;

    /// <summary>What is left of a GID once the flags are stripped: the tile index.</summary>
    private const uint TileIdMask = ~FlipFlagMask;

    /// <summary>
    /// The largest stripped index still worth reporting as "a flipped tile". This picks the
    /// <b>wording</b> of a diagnostic and never whether a file is accepted: both branches below
    /// throw. Without it a typo like <c>-5</c> would be reinterpreted as 0xfffffffb, whose top
    /// four bits are all set, and the author would be told about flip flags they never used.
    /// 0xffff is far above our 255 on purpose — a tileset in Tiled may legitimately be that
    /// large, and "tile 900 flipped horizontally, and 900 is out of range anyway" is a better
    /// message than "-1610612436 is negative".
    /// </summary>
    private const uint MaxPlausibleTiledId = 0xFFFF;

    /// <summary>An all-zero map: every cell empty, which is exactly what a missing map.bin loads as.</summary>
    public static byte[] EmptyPayload() => new byte[PayloadSize];

    /// <summary>One cell of a map payload; the layout is row-major and lives here and nowhere else.</summary>
    public static byte Tile(ReadOnlySpan<byte> payload, int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return payload[(y * Width) + x];
    }

    /// <summary>
    /// Compiles <c>map.csv</c> into the <see cref="PayloadSize"/> bytes of <c>map.bin</c>.
    /// <paramref name="sourceName"/> is the name that prefixes diagnostics — the file name as the
    /// author typed it, not a temp path — and every message reads
    /// <c>&lt;source&gt;:&lt;line&gt;: &lt;what and what to do&gt;</c>, the same shape
    /// <see cref="AudioTextCompiler"/> produces, so the CLI can print it unchanged.
    /// </summary>
    /// <exception cref="CartLoadException">
    /// On anything the author has to fix. This method never writes to the console and never
    /// returns a status: it either hands back a valid map or throws one message about the first
    /// broken cell.
    /// </exception>
    public static byte[] CompileMap(string text, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] payload = EmptyPayload();
        string[] lines = SplitLines(text);
        int row = 0;
        int lastRowLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            int line = i + 1;
            string content = StripComment(lines[i]);
            if (string.IsNullOrWhiteSpace(content))
            {
                // Blank lines and comment-only lines are not map rows, so they do not advance the
                // row counter — but they do advance the line counter, which is what diagnostics name.
                continue;
            }
            if (row >= Height)
            {
                throw Error(sourceName, line,
                    $"this would be map row {row}, and the map is {Height} rows tall (rows 0..{Height - 1}). "
                    + "Extra rows are not ignored: a taller map is a different map. In Tiled use "
                    + $"Map -> Resize Map to make the layer {Width}x{Height}, and check that the map is not infinite.");
            }
            ParseRow(payload, content, row, sourceName, line);
            lastRowLine = line;
            row++;
        }

        if (row != Height)
        {
            throw Error(sourceName, lastRowLine > 0 ? lastRowLine : 1, row == 0
                ? $"no map rows at all: a map is {Height} lines of {Width} comma-separated tile indices "
                    + "(blank lines and '#' comments do not count as rows)."
                : $"the file ends after {row} map row(s), and the map is {Height} rows tall. The missing rows are "
                    + "not filled in with empty cells: a short file is a truncated export far more often than a "
                    + $"deliberately short map. In Tiled use Map -> Resize Map to make the layer {Width}x{Height}.");
        }

        // The compiler's output must be something the loader accepts — if this ever fires it is a
        // bug here, not in the author's file, and it is much better found now than at load time.
        // (AudioFormat.WriteSfxFile plays the same trick with the bank it is handed.)
        ValidateMapPayload(payload, sourceName);
        return payload;
    }

    /// <summary>
    /// Every rule a map payload has to obey, which is exactly one: the length. That is not
    /// laziness, it is the shape of the format — every one of the 256 byte values is a legal
    /// tile, so there is no such thing as an out-of-range cell <i>once the value is a byte</i>.
    /// Which is precisely why the range check has to happen in the parser, while the number
    /// coming out of the text is still wider than a byte and <c>256</c> can still be told apart
    /// from <c>0</c>. There is also nothing to canonicalise: unlike a step word or a music
    /// channel byte, a map cell has no unused bits that two files could spell differently.
    /// </summary>
    public static void ValidateMapPayload(ReadOnlySpan<byte> payload, string sourceName)
    {
        if (payload.Length != PayloadSize)
        {
            throw new CartLoadException(
                $"{sourceName}: map payload is {payload.Length} bytes, must be exactly {PayloadSize} "
                + $"({Width}x{Height} cells, one byte per cell).");
        }
    }

    /// <summary>Parses one line of the CSV into one map row.</summary>
    private static void ParseRow(byte[] payload, string content, int row, string sourceName, int line)
    {
        string[] cells = content.Split(',');
        if (cells.Length > 1 && string.IsNullOrWhiteSpace(cells[^1]))
        {
            throw Error(sourceName, line,
                $"map row {row} ends with a separator and no value after it. Tiled's CSV export puts a comma "
                + "*between* cells and never after the last one; an empty cell is written -1 (or 0), never nothing.");
        }
        if (cells.Length != Width)
        {
            throw Error(sourceName, line,
                $"map row {row} has {cells.Length} value(s), and the map is {Width} cells wide. A short row is not "
                + "padded with empty cells and a long one is not truncated: the map cannot be checked by eye, so a "
                + "miscount has to be said out loud. In Tiled use Map -> Resize Map, export a single tile layer, "
                + "and check that the map is not infinite.");
        }

        int offset = row * Width;
        for (int x = 0; x < Width; x++)
        {
            payload[offset + x] = ParseCell(cells[x], x, row, sourceName, line);
        }
    }

    /// <summary>
    /// Parses one cell. The order of the checks is the specification: <c>-1</c> is recognised
    /// <b>before</b> the flags are looked at, because as 32 bits <c>-1</c> is 0xffffffff — every
    /// flag set on tile 0x0fffffff — and an empty cell would otherwise be reported as a rotated
    /// one. The number is parsed as <see cref="long"/> because a flagged GID does not fit an
    /// <see cref="int"/> either way it is spelled (3221225474 overflows it, -2147483646 is the
    /// same bits with the sign), and "that is not a number" is the one thing this cell is not.
    /// </summary>
    private static byte ParseCell(string token, int x, int y, string sourceName, int line)
    {
        string value = token.Trim();
        if (value.Length == 0)
        {
            throw Error(sourceName, line,
                $"cell ({x},{y}) is blank — two separators with nothing between them. An empty cell is written -1 "
                + "(Tiled's spelling) or 0 (ours); nothing at all is neither.");
        }

        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number))
        {
            throw Error(sourceName, line, LooksNumeric(value)
                ? $"cell ({x},{y}): {value} is far outside the range of a tile index (0..{MaxTile}); one map cell "
                    + "is one byte."
                : $"cell ({x},{y}): '{value}' is not a decimal number. A map row is {Width} tile indices separated "
                    + "by commas; a word here usually means the file is not the CSV of a tile layer — check that "
                    + "the export was File -> Export As... -> CSV on a map with exactly one tile layer, and that "
                    + "no tile name or property got in.");
        }

        if (number == EmptyCell)
        {
            // Checked first, and this is the only value where the text differs from the bytes:
            // an empty cell is tile 0 in map.bin whichever way the author spelled it (§4).
            return 0;
        }

        if (number >= int.MinValue && number <= uint.MaxValue)
        {
            // Both spellings of the same 32 bits reach here: Tiled writes a flagged GID unsigned
            // (2147483650) or signed (-2147483646) depending on version and platform, and the
            // author must get the same explanation either way.
            uint raw = number < 0 ? unchecked((uint)(int)number) : (uint)number;
            uint flags = raw & FlipFlagMask;
            uint id = raw & TileIdMask;
            if (flags != 0 && id <= MaxPlausibleTiledId)
            {
                throw Error(sourceName, line,
                    $"cell ({x},{y}): {value} (0x{raw:x8}) is tile {id} {DescribeFlags(flags)}"
                    + (id > MaxTile ? $", and tile {id} is outside 0..{MaxTile} anyway" : string.Empty)
                    + ". A map cell is one byte with no room for the flags, and Map() cannot draw a mirrored "
                    + "tile, so the flip has nowhere to go — and dropping it silently would hand you back a map "
                    + "that does not look like the one you drew. Un-flip the cell in Tiled and export again, or "
                    + "draw the mirrored tile into gfx.png and use its index.");
            }
        }

        if (number < 0)
        {
            throw Error(sourceName, line,
                $"cell ({x},{y}): {value} is negative, and the only negative value a map may hold is -1, which "
                + $"means an empty cell (the same byte as 0). Tile indices are 0..{MaxTile}.");
        }
        if (number > MaxTile)
        {
            throw Error(sourceName, line,
                $"cell ({x},{y}): tile {value} is out of range 0..{MaxTile}. One cell is one byte and profile 8 "
                + "has 256 sprites (SPEC-8 §3); tile 0 is the empty cell, not sprite 0.");
        }
        return (byte)number;
    }

    /// <summary>Names the flags an author never meant to export, in the order Tiled's bits run.</summary>
    private static string DescribeFlags(uint flags)
    {
        var parts = new List<string>(4);
        if ((flags & FlippedHorizontally) != 0)
        {
            parts.Add("flipped horizontally");
        }
        if ((flags & FlippedVertically) != 0)
        {
            parts.Add("flipped vertically");
        }
        if ((flags & FlippedDiagonally) != 0)
        {
            parts.Add("flipped diagonally (Tiled spells rotation this way)");
        }
        if ((flags & RotatedHexagonal120) != 0)
        {
            parts.Add("rotated 120 degrees (a hexagonal-map flag)");
        }
        return string.Join(" and ", parts);
    }

    /// <summary>
    /// True when the token is a plain decimal integer that simply did not fit a <see cref="long"/>.
    /// It separates "you wrote a number too big to be a tile" from "you wrote a word", so neither
    /// author is told the other one's problem.
    /// </summary>
    private static bool LooksNumeric(string value)
    {
        int start = value[0] is '-' or '+' ? 1 : 0;
        if (start >= value.Length)
        {
            return false;
        }
        for (int i = start; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Splits into lines the same way on every platform, and for the same reason
    /// <see cref="AudioTextCompiler"/> does: <see cref="string.ReplaceLineEndings(string)"/> folds
    /// CRLF, CR and the exotic Unicode terminators into "\n" first, so the CRLF file Tiled writes
    /// on Windows and the LF file it writes on Linux compile to the same bytes and report the same
    /// line numbers.
    /// </summary>
    private static string[] SplitLines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// Cuts the comment off a line. Unlike <c>sfx.txt</c>, where '#' only starts a comment at the
    /// start of a token because <c>C#4</c> is a note, here '#' is a comment wherever it appears:
    /// no map token can legally contain one, and the simpler rule is the one an author can guess.
    /// Comments are our dialect, not Tiled's — see docs/MAP-FORMAT.md §5.
    /// </summary>
    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    private static CartLoadException Error(string sourceName, int line, string message) =>
        new($"{sourceName}:{line}: {message}");
}

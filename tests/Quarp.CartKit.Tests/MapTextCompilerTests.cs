using System.Globalization;
using System.Text;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The author-facing map text (docs/MAP-FORMAT.md): a Tiled CSV export compiles to the 18432
/// bytes of <c>map.bin</c>, the same text compiles to the same bytes twice, and every way of
/// getting it wrong produces a sentence that names the file, the line, the cell and what to do
/// about it — because a map cannot be checked by eye, so a bad diagnostic here is more expensive
/// than a bad diagnostic anywhere else in the pipeline.
///
/// <para>Each test that asserts equality carries its own negative control: a round trip over a
/// compiler that dropped its input on the floor would pass with flying colours (the lesson of
/// M2), so wherever two compilations are compared, a third one is made to differ on purpose.</para>
/// </summary>
public class MapTextCompilerTests
{
    private const string SourceName = "map.csv";

    /// <summary>How an empty cell is spelled in a generated file; both spellings are legal (§4).</summary>
    private enum EmptySpelling
    {
        /// <summary>What Tiled's CSV export writes.</summary>
        MinusOne,

        /// <summary>What the map actually stores, and what a hand-written map usually says.</summary>
        Zero,

        /// <summary>Both, alternating — the file a human edits after exporting.</summary>
        Mixed,
    }

    private static byte[] Compile(string text) => MapTextCompiler.CompileMap(text, SourceName);

    private static CartLoadException Fails(string text) =>
        Assert.Throws<CartLoadException>(() => Compile(text));

    /// <summary>Reads a cell as an int, so the assertions read as numbers and not as bytes.</summary>
    private static int TileAt(byte[] payload, int x, int y) => MapTextCompiler.Tile(payload, x, y);

    // --- the round trip on a map shaped like a real one ---

    [Fact]
    public void ATiledExportCompilesToAWholeMapAndBuildingItTwiceGivesTheSameBytes()
    {
        string csv = Export(SampleMap);          // CRLF, -1 for empty: what Windows Tiled writes

        byte[] first = Compile(csv);
        byte[] second = Compile(csv);

        // The number the work order and SPEC-8 §3 both name: 256 x 72 cells, one byte each.
        Assert.Equal(18432, first.Length);
        Assert.Equal(MapTextCompiler.PayloadSize, first.Length);
        // The property `quarp map build` is judged on: rebuilding changes nothing.
        Assert.Equal(first, second);

        // The cells landed where the CSV put them, row-major.
        Assert.Equal(1, TileAt(first, 0, 0));                        // top-left wall
        Assert.Equal(1, TileAt(first, 255, 71));                     // bottom-right wall
        Assert.Equal(2, TileAt(first, 128, 70));                     // the floor row
        Assert.Equal(MapTextCompiler.MaxTile, TileAt(first, 37, 11));// tile 255 survives as itself
        Assert.Equal(0, TileAt(first, 5, 5));                        // -1 became the empty byte

        // Negative control: if the compiler were writing a constant map the comparison above
        // would still be green, so one changed cell has to move the bytes.
        byte[] altered = Compile(Export((x, y) => x == 5 && y == 5 ? 7 : SampleMap(x, y)));
        Assert.NotEqual(first, altered);
        Assert.Equal(7, TileAt(altered, 5, 5));
    }

    [Fact]
    public void AMapOfNothingButEmptyCellsIsAMapOfZeroes()
    {
        byte[] payload = Compile(Export((_, _) => 0));

        Assert.Equal(MapTextCompiler.PayloadSize, payload.Length);
        Assert.Equal(MapTextCompiler.EmptyPayload(), payload);
    }

    // --- Tiled's flip flags: eight combinations of H/V/D, fifteen counting the hexagonal bit ---

    /// <summary>
    /// Every non-empty combination of the four flag bits Tiled puts in the top nibble of a GID,
    /// worked out by hand for <b>tile 2</b>, in both spellings a CSV export can use — unsigned
    /// (the value overflows int32) and signed (the same 32 bits with the sign bit read as one):
    ///
    /// <code>
    ///   H = 0x80000000   V = 0x40000000   D = 0x20000000   hex120 = 0x10000000
    ///
    ///   flags      hex        unsigned      signed
    ///   H          80000002   2147483650    -2147483646
    ///   V          40000002   1073741826    (positive)
    ///   D          20000002    536870914    (positive)
    ///   hex120     10000002    268435458    (positive)
    ///   H+V        c0000002   3221225474    -1073741822
    ///   H+D        a0000002   2684354562    -1610612734
    ///   H+hex      90000002   2415919106    -1879048190
    ///   V+D        60000002   1610612738    (positive)
    ///   V+hex      50000002   1342177282    (positive)
    ///   D+hex      30000002    805306370    (positive)
    ///   H+V+D      e0000002   3758096386     -536870910
    ///   H+V+hex    d0000002   3489660930     -805306366
    ///   H+D+hex    b0000002   2952790018    -1342177278
    ///   V+D+hex    70000002   1879048194    (positive)
    ///   H+V+D+hex  f0000002   4026531842     -268435454
    /// </code>
    ///
    /// The assertion is not merely "it throws": xUnit's Assert.Throws matches the exception type
    /// exactly, so a <see cref="FormatException"/> or an <see cref="OverflowException"/> from
    /// parsing 3221225474 into a width that cannot hold it fails the test — which is the whole
    /// point of the case.
    /// </summary>
    [Theory]
    [InlineData("2147483650", "80000002", "flipped horizontally")]
    [InlineData("1073741826", "40000002", "flipped vertically")]
    [InlineData("536870914", "20000002", "flipped diagonally")]
    [InlineData("268435458", "10000002", "rotated 120 degrees")]
    [InlineData("3221225474", "c0000002", "flipped horizontally and flipped vertically")]
    [InlineData("2684354562", "a0000002", "flipped horizontally and flipped diagonally")]
    [InlineData("2415919106", "90000002", "flipped horizontally and rotated 120 degrees")]
    [InlineData("1610612738", "60000002", "flipped vertically and flipped diagonally")]
    [InlineData("1342177282", "50000002", "flipped vertically and rotated 120 degrees")]
    [InlineData("805306370", "30000002", "flipped diagonally")]
    [InlineData("3758096386", "e0000002", "flipped horizontally and flipped vertically and flipped diagonally")]
    [InlineData("3489660930", "d0000002", "flipped horizontally and flipped vertically and rotated 120 degrees")]
    [InlineData("2952790018", "b0000002", "flipped horizontally and flipped diagonally")]
    [InlineData("1879048194", "70000002", "flipped vertically and flipped diagonally")]
    [InlineData("4026531842", "f0000002", "flipped horizontally and flipped vertically and flipped diagonally")]
    [InlineData("-2147483646", "80000002", "flipped horizontally")]
    [InlineData("-1073741822", "c0000002", "flipped horizontally and flipped vertically")]
    [InlineData("-1610612734", "a0000002", "flipped horizontally and flipped diagonally")]
    [InlineData("-1879048190", "90000002", "flipped horizontally and rotated 120 degrees")]
    [InlineData("-536870910", "e0000002", "flipped horizontally and flipped vertically and flipped diagonally")]
    [InlineData("-805306366", "d0000002", "flipped horizontally and flipped vertically and rotated 120 degrees")]
    [InlineData("-1342177278", "b0000002", "flipped horizontally and flipped diagonally")]
    [InlineData("-268435454", "f0000002", "flipped horizontally and flipped vertically and flipped diagonally")]
    public void AFlippedCellIsRefusedWithAMessageThatNamesTheFlip(string token, string hex, string description)
    {
        CartLoadException e = Fails(ExportWithCell(3, 0, token));

        Assert.StartsWith($"{SourceName}:1: cell (3,0)", e.Message, StringComparison.Ordinal);
        Assert.Contains(token, e.Message, StringComparison.Ordinal);
        Assert.Contains("0x" + hex, e.Message, StringComparison.Ordinal);
        Assert.Contains("is tile 2 ", e.Message, StringComparison.Ordinal);
        Assert.Contains(description, e.Message, StringComparison.Ordinal);
        // And it says what to do about it, which is the half of the job the author cares about.
        Assert.Contains("Un-flip the cell in Tiled", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACellWithNoFlagBitsIsJustATile()
    {
        // The negative control for the theory above: the same cell, the same tile, no flags, and
        // the compiler is perfectly happy — so the theory is catching flags, not catching cells.
        byte[] payload = Compile(ExportWithCell(3, 0, "2"));

        Assert.Equal(2, TileAt(payload, 3, 0));
    }

    [Fact]
    public void MinusOneIsAnEmptyCellAndNotFourFlagsOnTileFifteenMillion()
    {
        // -1 is 0xffffffff, which is also "every flag set on tile 0x0fffffff". Checking -1 first
        // is what keeps the commonest value in a real export out of the flag branch.
        byte[] payload = Compile(ExportWithCell(3, 0, "-1"));

        Assert.Equal(0, TileAt(payload, 3, 0));
    }

    // --- -1 and 0 are the same empty cell ---

    [Fact]
    public void MinusOneAndZeroCompileToTheSameMap()
    {
        byte[] tiledSpelling = Compile(Export(SampleMap, empty: EmptySpelling.MinusOne));
        byte[] ourSpelling = Compile(Export(SampleMap, empty: EmptySpelling.Zero));
        byte[] mixedInOneFile = Compile(Export(SampleMap, empty: EmptySpelling.Mixed));

        // The one deliberate exception to "one value, one spelling" costs nothing, and this is
        // the proof: canonicity lives in map.bin, and map.bin cannot tell the two texts apart.
        Assert.Equal(tiledSpelling, ourSpelling);
        Assert.Equal(tiledSpelling, mixedInOneFile);

        // Negative control: the three sources really are three different texts.
        Assert.NotEqual(
            Export(SampleMap, empty: EmptySpelling.MinusOne),
            Export(SampleMap, empty: EmptySpelling.Zero));
        Assert.NotEqual(
            Export(SampleMap, empty: EmptySpelling.MinusOne),
            Export(SampleMap, empty: EmptySpelling.Mixed));
    }

    // --- line endings ---

    [Fact]
    public void TheCrlfFileTiledWritesOnWindowsCompilesLikeAnLfOne()
    {
        string crlf = Export(SampleMap, newLine: "\r\n");
        string lf = Export(SampleMap, newLine: "\n");

        // Guard: the two texts must actually differ in the way the test claims they do.
        Assert.Contains("\r\n", crlf, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", lf, StringComparison.Ordinal);

        Assert.Equal(Compile(crlf), Compile(lf));
    }

    [Fact]
    public void LineNumbersAreTheSameWhicheverWayTheFileEndsItsLines()
    {
        // A diagnostic that counted CRLF as two lines would send the author to the wrong place.
        Assert.StartsWith($"{SourceName}:10:", Fails(ExportWithCell(0, 9, "256", "\r\n")).Message,
            StringComparison.Ordinal);
        Assert.StartsWith($"{SourceName}:10:", Fails(ExportWithCell(0, 9, "256", "\n")).Message,
            StringComparison.Ordinal);
    }

    // --- geometry ---

    [Fact]
    public void ARowWithTooFewValuesIsAnErrorAndNotAPaddedRow()
    {
        CartLoadException e = Fails(ExportWithRowWidth(3, MapTextCompiler.Width - 1));

        Assert.StartsWith($"{SourceName}:4:", e.Message, StringComparison.Ordinal);
        Assert.Contains("map row 3 has 255 value(s)", e.Message, StringComparison.Ordinal);
        Assert.Contains("256 cells wide", e.Message, StringComparison.Ordinal);
        Assert.Contains("not padded", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowWithTooManyValuesIsAnError()
    {
        CartLoadException e = Fails(ExportWithRowWidth(3, MapTextCompiler.Width + 1));

        Assert.StartsWith($"{SourceName}:4:", e.Message, StringComparison.Ordinal);
        Assert.Contains("map row 3 has 257 value(s)", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithTooFewRowsIsAnErrorAndNotAShortMap()
    {
        CartLoadException e = Fails(ExportRows(MapTextCompiler.Height - 1));

        Assert.StartsWith($"{SourceName}:71:", e.Message, StringComparison.Ordinal);
        Assert.Contains("ends after 71 map row(s)", e.Message, StringComparison.Ordinal);
        Assert.Contains("72 rows tall", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithTooManyRowsIsAnError()
    {
        CartLoadException e = Fails(ExportRows(MapTextCompiler.Height + 1));

        // Reported on the first extra line, not at the end of the file: that is where to look.
        Assert.StartsWith($"{SourceName}:73:", e.Message, StringComparison.Ordinal);
        Assert.Contains("map row 72", e.Message, StringComparison.Ordinal);
        Assert.Contains("72 rows tall", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithNoRowsAtAllSaysSo()
    {
        CartLoadException e = Fails("# a map I never got round to drawing\n\n");

        Assert.StartsWith($"{SourceName}:1:", e.Message, StringComparison.Ordinal);
        Assert.Contains("no map rows at all", e.Message, StringComparison.Ordinal);
    }

    // --- cell values ---

    [Fact]
    public void TileTwoHundredFiftySixDoesNotFitACell()
    {
        CartLoadException e = Fails(ExportWithCell(3, 0, "256"));

        Assert.Contains("tile 256 is out of range 0..255", e.Message, StringComparison.Ordinal);
        Assert.Contains("256 sprites", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TileTwoHundredFiftyFiveFits()
    {
        // The boundary from the other side, so the range check cannot be off by one.
        Assert.Equal(255, TileAt(Compile(ExportWithCell(3, 0, "255")), 3, 0));
    }

    [Fact]
    public void ANegativeOtherThanMinusOneIsNotAnEmptyCell()
    {
        CartLoadException e = Fails(ExportWithCell(3, 0, "-5"));

        // -5 is 0xfffffffb, whose top four bits are all set; the author gets told about the sign
        // they typed, not about flip flags they never used.
        Assert.Contains("-5 is negative", e.Message, StringComparison.Ordinal);
        Assert.Contains("only negative value a map may hold is -1", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("flipped", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWordWhereANumberShouldBeSaysWhatWentWrong()
    {
        CartLoadException e = Fails(ExportWithCell(3, 0, "grass"));

        Assert.Contains("'grass' is not a decimal number", e.Message, StringComparison.Ordinal);
        Assert.Contains("Export As", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberTooBigForAnyIntegerIsStillReportedAsARange()
    {
        // Parsing overflow is the compiler's problem, never the author's: they get "not a tile",
        // not "the input string was not in a correct format".
        CartLoadException e = Fails(ExportWithCell(3, 0, "99999999999999999999999999"));

        Assert.Contains("0..255", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not a decimal number", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankCellBetweenTwoSeparatorsIsAnError()
    {
        CartLoadException e = Fails(ExportWithCell(3, 0, string.Empty));

        Assert.Contains("cell (3,0) is blank", e.Message, StringComparison.Ordinal);
        Assert.Contains("written -1", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowThatEndsWithASeparatorSaysSoRatherThanCountingTheGhostCell()
    {
        string[] lines = ExportRows(MapTextCompiler.Height).Split('\n');
        lines[5] += ",";

        CartLoadException e = Fails(string.Join('\n', lines));

        Assert.StartsWith($"{SourceName}:6:", e.Message, StringComparison.Ordinal);
        Assert.Contains("ends with a separator", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SpacesAroundValuesAreAllowed()
    {
        string[] lines = ExportRows(MapTextCompiler.Height).Split('\n');
        lines[4] = PaddedRow(MapTextCompiler.Width);

        Assert.Equal(Compile(ExportRows(MapTextCompiler.Height)), Compile(string.Join('\n', lines)));
    }

    // --- comments and blank lines ---

    [Fact]
    public void CommentsAndBlankLinesAreNotMapRows()
    {
        string plain = Export(SampleMap, newLine: "\n");
        string commented =
            "# The quarp dialect: a map.csv with comments will not open in Tiled again.\n"
            + "\n"
            + plain
            + "\n"
            + "# ...and a comment after the last row.\n";

        Assert.Equal(Compile(plain), Compile(commented));
    }

    [Fact]
    public void ACommentMayFollowACompleteRow()
    {
        string plain = Export(SampleMap, newLine: "\n");
        string[] lines = plain.Split('\n');
        lines[0] += "   # the top wall";

        Assert.Equal(Compile(plain), Compile(string.Join('\n', lines)));
    }

    [Fact]
    public void ACommentInsideARowCutsTheRowThereRatherThanBeingIgnored()
    {
        // '#' is not magic: it ends the line, so a comment dropped into the middle of a data row
        // leaves the row short and the row is reported short. Silently completing it would be the
        // one behaviour a map author cannot check by eye.
        string[] lines = ExportRows(MapTextCompiler.Height).Split('\n');
        lines[2] = Row(100) + " # oops, mid-row";

        CartLoadException e = Fails(string.Join('\n', lines));

        Assert.StartsWith($"{SourceName}:3:", e.Message, StringComparison.Ordinal);
        Assert.Contains("map row 2 has 100 value(s)", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLineNumberCountsCommentsAndBlankLines()
    {
        CartLoadException e = Fails("# header\n\n" + ExportWithCell(0, 0, "256"));

        Assert.StartsWith($"{SourceName}:3:", e.Message, StringComparison.Ordinal);
    }

    // --- the compiler checks its own output ---

    [Fact]
    public void TheCompilerRunsItsOwnOutputThroughTheLoadersRule()
    {
        // The trick AudioFormat.WriteSfxFile plays: whatever the compiler produces is put through
        // the same check the loader applies, so a bug in here surfaces at build time with a
        // sentence instead of at load time with a broken cart.
        byte[] payload = Compile(Export(SampleMap));
        MapTextCompiler.ValidateMapPayload(payload, SourceName);

        // Negative control: that check is able to fail, and says both numbers when it does.
        CartLoadException e = Assert.Throws<CartLoadException>(
            () => MapTextCompiler.ValidateMapPayload(new byte[MapTextCompiler.PayloadSize - 1], "map.bin"));
        Assert.Contains("18431", e.Message, StringComparison.Ordinal);
        Assert.Contains("18432", e.Message, StringComparison.Ordinal);
    }

    // --- every diagnostic is addressed to a person ---

    [Theory]
    [InlineData("256")]
    [InlineData("-5")]
    [InlineData("grass")]
    [InlineData("")]
    [InlineData("2147483650")]
    [InlineData("99999999999999999999999999")]
    public void EveryCellDiagnosticNamesTheFileTheLineAndTheCell(string token)
    {
        CartLoadException e = Fails(ExportWithCell(7, 9, token));

        Assert.StartsWith($"{SourceName}:10: cell (7,9)", e.Message, StringComparison.Ordinal);
    }

    // --- fixtures ---

    /// <summary>
    /// A map shaped like a real one: a wall around the edge, a floor two rows up, a scattering of
    /// decoration, and most cells empty — which is what makes -1 the common case in a real export
    /// and the reason it has to be accepted at all.
    /// </summary>
    private static int SampleMap(int x, int y)
    {
        if (x == 0 || y == 0 || x == MapTextCompiler.Width - 1 || y == MapTextCompiler.Height - 1)
        {
            return 1;
        }
        if (y == MapTextCompiler.Height - 2)
        {
            return 2;
        }
        if (x % 37 == 0 && y % 11 == 0)
        {
            return MapTextCompiler.MaxTile;
        }
        return 0;
    }

    /// <summary>
    /// Writes a map the way Tiled's CSV export does: one line per row, values separated by
    /// commas with no trailing one, an empty cell written -1, a final line terminator, and CRLF
    /// unless the test asks otherwise.
    /// </summary>
    private static string Export(
        Func<int, int, int> tile,
        string newLine = "\r\n",
        EmptySpelling empty = EmptySpelling.MinusOne,
        int rows = MapTextCompiler.Height)
    {
        var text = new StringBuilder();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < MapTextCompiler.Width; x++)
            {
                if (x > 0)
                {
                    text.Append(',');
                }
                int value = tile(x, y);
                text.Append(value == 0
                    ? EmptyToken(empty, x, y)
                    : value.ToString(CultureInfo.InvariantCulture));
            }
            text.Append(newLine);
        }
        return text.ToString();
    }

    private static string EmptyToken(EmptySpelling empty, int x, int y) => empty switch
    {
        EmptySpelling.Zero => "0",
        EmptySpelling.Mixed => (x + y) % 2 == 0 ? "0" : "-1",
        _ => "-1",
    };

    /// <summary>An empty map of <paramref name="rows"/> rows, LF-terminated.</summary>
    private static string ExportRows(int rows) => Export((_, _) => 0, "\n", EmptySpelling.MinusOne, rows);

    /// <summary>An empty map with exactly one cell replaced by <paramref name="token"/>.</summary>
    private static string ExportWithCell(int badX, int badY, string token, string newLine = "\n")
    {
        var text = new StringBuilder();
        for (int y = 0; y < MapTextCompiler.Height; y++)
        {
            for (int x = 0; x < MapTextCompiler.Width; x++)
            {
                if (x > 0)
                {
                    text.Append(',');
                }
                text.Append(x == badX && y == badY ? token : "-1");
            }
            text.Append(newLine);
        }
        return text.ToString();
    }

    /// <summary>An empty map in which one row has the wrong number of values.</summary>
    private static string ExportWithRowWidth(int row, int width)
    {
        var text = new StringBuilder();
        for (int y = 0; y < MapTextCompiler.Height; y++)
        {
            text.Append(Row(y == row ? width : MapTextCompiler.Width));
            text.Append('\n');
        }
        return text.ToString();
    }

    private static string Row(int columns)
    {
        var text = new StringBuilder();
        for (int x = 0; x < columns; x++)
        {
            if (x > 0)
            {
                text.Append(',');
            }
            text.Append("-1");
        }
        return text.ToString();
    }

    /// <summary>The same row, spaced out the way someone lining up columns by hand would write it.</summary>
    private static string PaddedRow(int columns)
    {
        var text = new StringBuilder();
        for (int x = 0; x < columns; x++)
        {
            if (x > 0)
            {
                text.Append(" , ");
            }
            text.Append("  -1 ");
        }
        return text.ToString();
    }
}

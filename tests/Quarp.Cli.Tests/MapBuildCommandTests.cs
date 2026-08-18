using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// <c>quarp map build &lt;cart&gt; [--check]</c> end to end: the real tool, a real cart folder, a
/// real <c>map.csv</c> (M4 Р11, Р14; stage-2 acceptance items 3 and 4).
///
/// <para>Child processes rather than a direct call to <see cref="MapBuildCommand.Invoke"/>,
/// because what is being asserted is a file on disk and an exit code — the two things a CI step
/// and a build script consume — and because half the interesting failures live in the seam
/// between the command and the file system rather than inside either.</para>
///
/// <para>Every size here is written out rather than read from <c>CartData.MapWidth *
/// MapHeight</c>: 18432 is the number the console's loader insists on, and a test that restates
/// it from the same constant the code uses would keep passing while both moved together.</para>
/// </summary>
public sealed class MapBuildCommandTests : IDisposable
{
    /// <summary>256 x 72 tile bytes — <c>CartSource</c> rejects a <c>map.bin</c> of any other size.</summary>
    private const int MapBankBytes = 18432;

    private readonly BuildTestCart _cart =
        new("map-build", BuildTestCart.HealthyMainCs);

    public void Dispose() => _cart.Dispose();

    /// <summary>
    /// The bank is exactly the 18432 bytes the loader accepts, and it is the map that was
    /// written: row-major, tile ids as exported (0-based), <c>-1</c> landing on the empty cell
    /// 0. Catches a transposed map, an off-by-one row count, a bank padded with a header, and a
    /// compiler that shifted every tile by one — the last of which is the specific mistake M4
    /// Р11 was rewritten to prevent, and which no byte-count assertion can see.
    /// </summary>
    [Fact]
    public void BuildingAMapWritesTheBankTheConsoleLoads()
    {
        _cart.Write("map.csv", BuildTestCart.MapCsv());

        CliResult result = CliProcess.Run("map", "build", _cart.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Contains("map.csv -> map.bin", result.StdOut, StringComparison.Ordinal);

        byte[] bank = _cart.Read("map.bin");
        Assert.Equal(MapBankBytes, bank.Length);
        // The fixture's corners: a border tile at (0,0), the diagonal tile at (5,5), and an
        // exported -1 at (2,1), which is the empty cell and therefore byte 0.
        Assert.Equal(1, (int)bank[0]);
        Assert.Equal(2, (int)bank[(5 * 256) + 5]);
        Assert.Equal(0, (int)bank[(1 * 256) + 2]);
    }

    /// <summary>
    /// Byte-for-byte reproducible, which is the property <c>--check</c> and the whole
    /// generated-file-in-git arrangement rest on. A compiler that hashed a dictionary, stamped a
    /// timestamp, or enumerated a directory would pass every other test in this file.
    /// </summary>
    [Fact]
    public void BuildingTheSameMapTwiceWritesTheSameBytes()
    {
        _cart.Write("map.csv", BuildTestCart.MapCsv());
        Assert.Equal(0, CliProcess.Run("map", "build", _cart.Root).ExitCode);
        byte[] first = _cart.Read("map.bin");

        Assert.Equal(0, CliProcess.Run("map", "build", _cart.Root).ExitCode);

        Assert.Equal(first, _cart.Read("map.bin"));
    }

    /// <summary>
    /// The CI form: 0 when the committed bank is what the text compiles to, non-zero when a
    /// single byte has moved — and, just as load-bearing, <c>--check</c> repairs nothing. A check
    /// that quietly rewrote the file would return 0 forever after its first run and CI would
    /// never see a stale bank again.
    /// </summary>
    [Fact]
    public void CheckPassesOnAMatchingBankAndFailsOnAChangedByteWithoutRepairingIt()
    {
        _cart.Write("map.csv", BuildTestCart.MapCsv());
        Assert.Equal(0, CliProcess.Run("map", "build", _cart.Root).ExitCode);

        CliResult agreeing = CliProcess.Run("map", "build", _cart.Root, "--check");
        Assert.Equal(0, agreeing.ExitCode);
        Assert.Contains("up to date", agreeing.StdOut, StringComparison.Ordinal);

        byte[] tampered = _cart.Read("map.bin");
        tampered[9000] ^= 0xFF;
        _cart.Write("map.bin", tampered);

        CliResult disagreeing = CliProcess.Run("map", "build", _cart.Root, "--check");

        Assert.NotEqual(0, disagreeing.ExitCode);
        Assert.Contains("does not match", disagreeing.StdErr, StringComparison.Ordinal);
        Assert.Equal(tampered, _cart.Read("map.bin"));
    }

    /// <summary>A bank that was never built is the same failure as a stale one, and says so.</summary>
    [Fact]
    public void CheckFailsWhenTheBankWasNeverBuilt()
    {
        _cart.Write("map.csv", BuildTestCart.MapCsv());

        CliResult result = CliProcess.Run("map", "build", _cart.Root, "--check");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("map.bin is missing", result.StdErr, StringComparison.Ordinal);
        Assert.False(_cart.Has("map.bin"), "--check wrote the bank it was asked only to compare");
    }

    /// <summary>
    /// A cartridge with no map is a whole cartridge, so this is a report and an exit code of 0 —
    /// not the "nothing to build" failure <c>quarp audio build</c> answers with. The caller that
    /// makes this matter is <c>quarp build</c>, which runs over carts before anyone knows whether
    /// they have a map, and every cart in this repository today does not.
    /// </summary>
    [Fact]
    public void ACartridgeWithoutAMapIsNotAFailure()
    {
        CliResult result = CliProcess.Run("map", "build", _cart.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Contains("no map.csv", result.StdOut, StringComparison.Ordinal);
        Assert.False(_cart.Has("map.bin"), "a cart with no map.csv had a map.bin invented for it");
    }

    /// <summary>
    /// The compiler's message reaches the author intact — <c>map.csv:&lt;line&gt;: ...</c>, the
    /// line being the row that is wrong — and never as a .NET stack trace. A short row is the
    /// mistake M4 Р11 singles out as the one that must not be silently padded with zeros: a map
    /// is not something an author can proofread by eye, so the only place the typo can be caught
    /// is here, by its line number.
    ///
    /// <para>The line number is asserted as 1-based, which is the convention
    /// <c>AudioTextCompiler</c> already uses and the one the stage-2 contract for
    /// <c>MapTextCompiler.CompileMap</c> spells out ("map.csv:14: ...").</para>
    /// </summary>
    [Fact]
    public void ACompilerErrorArrivesWithItsLineNumberAndNotAsAStackTrace()
    {
        string[] rows = BuildTestCart.MapCsv().Split('\n');
        // Row 3 of the file loses one of its 256 values, and nothing else changes.
        rows[2] = string.Join(',', rows[2].Split(',')[..255]);
        _cart.Write("map.csv", string.Join('\n', rows));

        CliResult result = CliProcess.Run("map", "build", _cart.Root);

        Assert.Equal(1, result.ExitCode);
        Assert.Matches(@"map\.csv:\d+:", result.StdErr);
        Assert.Contains("map.csv:3:", result.StdErr, StringComparison.Ordinal);
        // A stack trace means the exception escaped instead of being reported.
        Assert.DoesNotContain("   at ", result.StdErr, StringComparison.Ordinal);
        Assert.False(_cart.Has("map.bin"), "a bank was written from a source that does not compile");
    }

    /// <summary>
    /// Everything the command refuses. The point is not the individual spellings but that each
    /// one ends in exit code 1 with a sentence naming what went wrong — a build script that got
    /// a 0 out of a mistyped command line would report a map it never built as current.
    /// </summary>
    [Theory]
    [InlineData(new[] { "map" }, "usage: quarp map <build>")]
    [InlineData(new[] { "map", "check" }, "unknown subcommand 'check'")]
    [InlineData(new[] { "map", "build" }, "usage: quarp map build")]
    [InlineData(new[] { "map", "build", "$CART", "--force" }, "unknown argument '--force'")]
    [InlineData(new[] { "map", "build", "$CART/nowhere" }, "cart folder not found")]
    [InlineData(new[] { "map", "build", "$CART/manifest.json" }, "is a file")]
    public void EveryBadSpellingFailsWithAnExplanation(string[] args, string expected)
    {
        string[] resolved = Array.ConvertAll(
            args, argument => argument.Replace("$CART", _cart.Root, StringComparison.Ordinal));

        CliResult result = CliProcess.Run(resolved);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expected, result.StdErr, StringComparison.Ordinal);
    }
}

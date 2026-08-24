using Quarp.CartKit;
using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// <c>quarp gfx dump &lt;cart&gt; [-o &lt;file.png&gt;] [--force]</c> end to end: the real tool, a
/// real cart folder, a real file on disk (M9 wave A1).
///
/// <para>Child processes rather than a direct call to <see cref="GfxDumpCommand.Invoke"/>, for
/// the reason <see cref="MapBuildCommandTests"/> gives: what is asserted here is a file and an
/// exit code, and half of the interesting failures live in the seam between the command and the
/// file system. It matters twice as much here, because the command loads a cartridge assembly
/// into a collectible context and runs the author's code — behaviour that only exists in a real
/// process.</para>
///
/// <para>Every size is written out — 128, 16384 — rather than read from <c>CartData.GfxWidth</c>.
/// 128x128 is the sheet the console's loader insists on and the number these tests exist to
/// hold; restating it from the constant under test would keep them green while both moved.</para>
/// </summary>
public sealed class GfxDumpCommandTests : IDisposable
{
    private const int SheetSide = 128;

    private const int SheetBytes = SheetSide * SheetSide;

    /// <summary>
    /// A cartridge that paints a known sheet in <c>Init</c> and leaves two witnesses behind.
    ///
    /// <para>The pattern <c>(x * 7 + y * 3) % 16</c> is asymmetric in x and y and uses all 16
    /// visible colors, so a dump that transposed the sheet, mirrored it, dropped a scanline
    /// filter byte or quantized to fewer colors cannot match it. <see cref="ExpectedSheet"/>
    /// restates the formula rather than importing it — the test and the cartridge agreeing by
    /// coincidence is the whole point of the comparison.</para>
    ///
    /// <para><b>Witness one:</b> <c>_inits</c> counts calls to <c>Init</c> and lands in pixel
    /// (0,0), which the pattern paints as 0. A dump that attached the cartridge twice writes 2
    /// there; one that never ran <c>Init</c> at all writes 0.</para>
    ///
    /// <para><b>Witness two:</b> <c>Update</c> writes 9 into pixel (1,0), which the pattern
    /// paints as 7. One tick of simulation and the dump is a screenshot of a game in progress
    /// instead of the cartridge's art. There is no <c>Draw</c> witness because there cannot be
    /// one — <c>Sset</c> from <c>Draw</c> is QRP1004, an error — and none is needed: a tick runs
    /// <c>Update</c> before it ever reaches <c>Draw</c>.</para>
    /// </summary>
    private const string SheetCartMainCs = """
        using Quarp.Api;

        namespace Fixture;

        public sealed class SheetCart : Cartridge
        {
            private int _inits;

            public override void Init()
            {
                _inits++;
                for (int y = 0; y < 128; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        Sset(x, y, (byte)(((x * 7) + (y * 3)) % 16));
                    }
                }
                Sset(0, 0, (byte)_inits);
            }

            public override void Update()
            {
                Sset(1, 0, 9);
            }
        }
        """;

    private readonly BuildTestCart _cart = new("gfx-dump", SheetCartMainCs);

    public void Dispose() => _cart.Dispose();

    /// <summary>
    /// The dump is the sheet the running console holds after <c>Init</c>, and the file it writes
    /// is one our own decoder reads back into exactly those pixels — the round trip ADR-026 is
    /// about, run against art that only ever existed inside a running cartridge.
    ///
    /// <para>Catches, in one comparison: a dump taken before <c>Init</c> (all zeros), a dump
    /// taken after a tick (pixel (1,0) is 9), a second attach (pixel (0,0) is 2), a transposed
    /// or mirrored read loop, and an encoder writing anything other than these indices.</para>
    /// </summary>
    [Fact]
    public void DumpWritesTheSheetTheConsoleHoldsAfterInit()
    {
        CliResult result = CliProcess.Run("gfx", "dump", _cart.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Contains(_cart.At("gfx.png"), result.StdOut, StringComparison.Ordinal);
        Assert.Contains("sha256 ", result.StdOut, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^sha256 [0-9a-f]{64}$", result.StdOut.ReplaceLineEndings("\n"));

        byte[] pixels = PngDecoder.DecodeToPaletteIndices(
            _cart.Read("gfx.png"), SheetSide, SheetSide, "gfx.png");

        Assert.Equal(SheetBytes, pixels.Length);
        Assert.Equal(ExpectedSheet(), pixels);
    }

    /// <summary>
    /// The same cartridge dumped twice gives byte-identical files, and the tool says so with the
    /// same digest twice. This is the property the whole errand rests on: the art is about to be
    /// committed, and a dump that carried a timestamp, a compressor's mood or an enumeration
    /// order would turn every later run into a spurious diff.
    ///
    /// <para>What the second run is NOT, corrected by the session audit of 2026-08-24: a
    /// fixed-point check. This fixture's <c>Init</c> repaints all 16 384 pixels, so the sheet
    /// the console loaded from the first run's file changes nothing and the test would stay
    /// green even if <c>CartSource</c> stopped reading <c>gfx.png</c> at all. The real
    /// fixed point — a cart whose committed art equals what its console holds after
    /// <c>Init</c> — is pinned on the shipped demos by
    /// <c>Quarp.CartKit.Tests.DemoSheetInvariantTests</c>, where the art is not repainted.</para>
    /// </summary>
    [Fact]
    public void DumpingTheSameCartridgeTwiceWritesTheSameBytes()
    {
        CliResult first = CliProcess.Run("gfx", "dump", _cart.Root);
        Assert.Equal(0, first.ExitCode);
        byte[] before = _cart.Read("gfx.png");

        CliResult second = CliProcess.Run("gfx", "dump", _cart.Root, "--force");

        Assert.Equal(0, second.ExitCode);
        Assert.Equal(before, _cart.Read("gfx.png"));
        Assert.Equal(DigestLine(first), DigestLine(second));
    }

    /// <summary>
    /// Somebody else's art is not overwritten by accident. Without <c>--force</c> the existing
    /// file survives byte for byte and the exit code is non-zero; with <c>--force</c> the dump
    /// replaces it. The pre-existing file is a valid sheet of solid color 3, so the cartridge
    /// still loads either way and the only thing under test is the refusal.
    /// </summary>
    [Fact]
    public void WithoutForceTheExistingFileSurvivesAndWithForceItIsReplaced()
    {
        byte[] existing = PngEncoder.EncodeFromPaletteIndices(SolidSheet(3), SheetSide, SheetSide);
        _cart.Write("gfx.png", existing);

        CliResult refused = CliProcess.Run("gfx", "dump", _cart.Root);

        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("already exists", refused.StdErr, StringComparison.Ordinal);
        Assert.Contains("--force", refused.StdErr, StringComparison.Ordinal);
        Assert.Equal(existing, _cart.Read("gfx.png"));

        CliResult forced = CliProcess.Run("gfx", "dump", _cart.Root, "--force");

        Assert.Equal(0, forced.ExitCode);
        Assert.Equal(
            ExpectedSheet(),
            PngDecoder.DecodeToPaletteIndices(_cart.Read("gfx.png"), SheetSide, SheetSide, "gfx.png"));
    }

    /// <summary>
    /// The refusal is decided before the cartridge is loaded, and that ordering is worth a test
    /// of its own: the file standing in the way here is not a PNG at all, so a command that
    /// loaded the cart first would report <c>gfx.png: not a PNG file</c> — the loader's complaint
    /// about the very file it was asked to replace — and never mention the overwrite. It would
    /// also spend half a minute in Roslyn to say it.
    /// </summary>
    [Fact]
    public void TheRefusalToOverwriteIsDecidedBeforeTheCartridgeIsLoaded()
    {
        byte[] notAPng = "this is not a PNG"u8.ToArray();
        _cart.Write("gfx.png", notAPng);

        CliResult result = CliProcess.Run("gfx", "dump", _cart.Root);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("already exists", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("not a PNG", result.StdErr, StringComparison.Ordinal);
        Assert.Equal(notAPng, _cart.Read("gfx.png"));
    }

    /// <summary>
    /// <c>-o</c> puts the sheet where it was told and leaves the cartridge folder alone — the
    /// form the next wave needs to compare a dump against committed art without touching it.
    /// </summary>
    [Fact]
    public void MinusOWritesWhereItIsToldAndLeavesTheCartridgeUntouched()
    {
        string target = Path.Combine(
            Path.GetTempPath(), $"quarp-gfx-dump-{Guid.NewGuid():N}.png");
        try
        {
            CliResult result = CliProcess.Run("gfx", "dump", _cart.Root, "-o", target);

            Assert.Equal(0, result.ExitCode);
            Assert.False(_cart.Has("gfx.png"), "-o was given and the cart folder got a gfx.png anyway");
            Assert.Equal(
                ExpectedSheet(),
                PngDecoder.DecodeToPaletteIndices(File.ReadAllBytes(target), SheetSide, SheetSide, "out.png"));
        }
        finally
        {
            File.Delete(target);
        }
    }

    /// <summary>
    /// Everything the command refuses, each ending in exit code 1 with a sentence naming what
    /// went wrong and with no file written. A script that got a 0 out of a mistyped command line
    /// would report art it never dumped as current — the same reasoning
    /// <see cref="MapBuildCommandTests"/> applies to <c>map build</c>.
    /// </summary>
    [Theory]
    [InlineData(new[] { "gfx" }, "usage: quarp gfx <dump>")]
    [InlineData(new[] { "gfx", "dumpp" }, "unknown subcommand 'dumpp'")]
    [InlineData(new[] { "gfx", "dump" }, "usage: quarp gfx dump <cart>")]
    [InlineData(new[] { "gfx", "dump", "$CART", "--overwrite" }, "unknown argument '--overwrite'")]
    [InlineData(new[] { "gfx", "dump", "$CART", "-o" }, "-o needs a file path")]
    [InlineData(new[] { "gfx", "dump", "$CART/nowhere" }, "cartridge not found")]
    [InlineData(new[] { "gfx", "dump", "$CART", "-o", "$CART" }, "is a directory")]
    public void EveryBadSpellingFailsWithAnExplanationAndWritesNothing(string[] args, string expected)
    {
        string[] resolved = Array.ConvertAll(
            args, argument => argument.Replace("$CART", _cart.Root, StringComparison.Ordinal));

        CliResult result = CliProcess.Run(resolved);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expected, result.StdErr, StringComparison.Ordinal);
        Assert.False(_cart.Has("gfx.png"), $"a rejected command line still wrote a sheet: {result}");
    }

    /// <summary>
    /// What the fixture cartridge's <c>Init</c> leaves in the console's sheet, derived from the
    /// formula rather than from the cartridge's source.
    /// </summary>
    private static byte[] ExpectedSheet()
    {
        var sheet = new byte[SheetBytes];
        for (int y = 0; y < SheetSide; y++)
        {
            for (int x = 0; x < SheetSide; x++)
            {
                sheet[(y * SheetSide) + x] = (byte)(((x * 7) + (y * 3)) % 16);
            }
        }
        sheet[0] = 1;   // Init ran exactly once; pixel (1,0) stays 7, so no Update ever did.
        return sheet;
    }

    private static byte[] SolidSheet(byte color)
    {
        var sheet = new byte[SheetBytes];
        Array.Fill(sheet, color);
        return sheet;
    }

    /// <summary>The <c>sha256 ...</c> line of a run's stdout — the digest the organiser compares.</summary>
    private static string DigestLine(CliResult result)
    {
        foreach (string line in result.OutLines())
        {
            if (line.StartsWith("sha256 ", StringComparison.Ordinal))
            {
                return line;
            }
        }
        Assert.Fail($"no 'sha256 ...' line in the output of a successful dump:\n{result}");
        return string.Empty;
    }
}

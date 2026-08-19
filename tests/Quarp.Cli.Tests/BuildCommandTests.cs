using System.Text.RegularExpressions;
using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// <c>quarp build &lt;cart&gt;</c> end to end (M4 Р14; stage-2 acceptance item 7): the command
/// that replaces <c>quarp sim --ticks 0</c> as the build task behind F5.
///
/// <para><b>The claim this file exists to hold</b> is the one the old build task could not make:
/// a build runs no tick. <c>sim --ticks 0</c> attaches the cartridge to a console, and attaching
/// runs <c>Init</c> — so the thing VS Code ran before opening the debugger executed the author's
/// code, and a cartridge that crashed on startup reported itself as a failed build. The proof
/// here is a cartridge that throws from both <c>Init</c> and <c>Update</c>: if either ever runs,
/// the marker appears and the assertion fails. Its negative control is
/// <see cref="TheSameCartridgeExplodesTheMomentAnythingTicksIt"/>, which shows the marker is
/// reachable — without it, "the marker did not appear" would be satisfied by a cartridge that
/// simply never says anything (M2's lesson: a green test is worth nothing until it has been seen
/// to redden).</para>
/// </summary>
public sealed class BuildCommandTests
{
    /// <summary>What a diagnostic must look like for the generated <c>tasks.json</c> matcher.</summary>
    private const string DiagnosticPattern = @"^src/broken\.cs\(\d+,\d+\): error CS\d+: ";

    [Fact]
    public void AHealthyCartridgeBuildsAndSaysSo()
    {
        using var cart = new BuildTestCart("build-ok", BuildTestCart.HealthyMainCs);

        CliResult result = CliProcess.Run("build", cart.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Contains("compile ok", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("build ok", result.StdOut, StringComparison.Ordinal);
        // No hash lines: this is a diagnosis, not a determinism probe. `sim --ticks 0` printed
        // two hashes here and the generated tasks.json had to apologise for them in a comment.
        // Line by line, because ^ and $ anchor to the whole string otherwise and the assertion
        // would be true of any multi-line output, including one full of hashes.
        foreach (string line in result.OutLines())
        {
            Assert.DoesNotMatch("^[0-9a-f]{16}$", line);
        }
    }

    /// <summary>
    /// A C# error comes out in exactly the shape the <c>problemMatcher</c> in the generated
    /// <c>.vscode/tasks.json</c> parses, <b>with the path still relative to the cart folder</b>.
    /// That relativeness is the load-bearing half and the one nothing else would notice: the
    /// matcher resolves <c>fileLocation: ["relative", "${workspaceFolder}"]</c>, so an absolute
    /// path here does not fail the build — it silently produces Problems entries that link
    /// nowhere.
    /// </summary>
    [Fact]
    public void ASyntaxErrorIsReportedInTheShapeTheProblemMatcherReads()
    {
        using var cart = new BuildTestCart("build-syntax", BuildTestCart.HealthyMainCs);
        cart.Write("src/broken.cs", BuildTestCart.BrokenSourceCs);

        CliResult result = CliProcess.Run("build", cart.Root);

        Assert.Equal(1, result.ExitCode);
        string[] diagnostics = Array.FindAll(
            result.StdErr.ReplaceLineEndings("\n").Split('\n'),
            line => Regex.IsMatch(line, DiagnosticPattern));
        Assert.NotEmpty(diagnostics);
        Assert.DoesNotContain(cart.Root, diagnostics[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build failed", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The 256 KB code budget (SPEC-8 §6), which is a load-time limit rather than a compiler
    /// error and therefore arrives without a file or a line. It still has to say what happened
    /// in words an author can act on.
    /// </summary>
    [Fact]
    public void AnOversizedCartridgeFailsOnTheCodeBudget()
    {
        using var cart = new BuildTestCart("build-budget", BuildTestCart.HealthyMainCs);
        cart.Write("src/bulk.cs", BuildTestCart.OversizedSource());

        CliResult result = CliProcess.Run("build", cart.Root);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("code budget exceeded", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(BuildTestCart.TickMarker, result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The milestone claim: whatever the build decides, the cartridge's <c>Init</c> and
    /// <c>Update</c> never run. Three cartridges, all of them explosive, differing only in what
    /// else is wrong with them — a healthy one that builds, one that fails to compile, one over
    /// the code budget — because "no tick" has to hold on the success path (where a build could
    /// plausibly decide to smoke-test what it just produced) and on both failure paths (where an
    /// error handler could plausibly try to run the cart to say more about it).
    /// </summary>
    [Fact]
    public void NeitherInitNorUpdateRunsWhateverTheBuildDecides()
    {
        using var healthy = new BuildTestCart("build-tick-ok", BuildTestCart.ExplosiveMainCs);
        using var broken = new BuildTestCart("build-tick-broken", BuildTestCart.ExplosiveMainCs);
        broken.Write("src/broken.cs", BuildTestCart.BrokenSourceCs);
        using var oversized = new BuildTestCart("build-tick-big", BuildTestCart.ExplosiveMainCs);
        oversized.Write("src/bulk.cs", BuildTestCart.OversizedSource());

        AssertNoTick(healthy, 0, "a cartridge that builds");
        AssertNoTick(broken, 1, "a cartridge that does not compile");
        AssertNoTick(oversized, 1, "a cartridge over the code budget");

        static void AssertNoTick(BuildTestCart cart, int expectedExit, string label)
        {
            CliResult result = CliProcess.Run("build", cart.Root);

            Assert.True(
                expectedExit == result.ExitCode,
                $"{label}: expected exit {expectedExit}, got {result}");
            Assert.True(
                !result.StdOut.Contains(BuildTestCart.TickMarker, StringComparison.Ordinal)
                    && !result.StdErr.Contains(BuildTestCart.TickMarker, StringComparison.Ordinal),
                $"{label}: quarp build ran Init or Update. {result}");
        }
    }

    /// <summary>
    /// The negative control for <see cref="NeitherInitNorUpdateRunsWhateverTheBuildDecides"/>:
    /// the very same cartridge, handed to the very command <c>build</c> replaced, does shout.
    /// Without this, the assertion above would pass for a cartridge that had nothing to say,
    /// for a marker nothing ever prints, or for a tool that had stopped writing to its streams.
    /// </summary>
    [Fact]
    public void TheSameCartridgeExplodesTheMomentAnythingTicksIt()
    {
        using var cart = new BuildTestCart("build-tick-control", BuildTestCart.ExplosiveMainCs);

        // --ticks 0 runs no Update at all; attaching the cart is enough, because AttachCart runs
        // Init. This is exactly what the old .vscode/tasks.json build task did.
        CliResult zeroTicks = CliProcess.Run("sim", cart.Root, "--ticks", "0");
        Assert.NotEqual(0, zeroTicks.ExitCode);
        Assert.Contains(BuildTestCart.TickMarker, zeroTicks.StdErr, StringComparison.Ordinal);
        Assert.Contains("in Init", zeroTicks.StdErr, StringComparison.Ordinal);
    }

    // --- the banks: sound and map checked against the text they were generated from ---

    /// <summary>
    /// A map that has been built is reported as current, and the bank it built is one the loader
    /// accepts — <c>CartSource</c> refuses a <c>map.bin</c> that is not exactly 18432 bytes, so a
    /// build that reaches "ok" here is also a statement about <c>quarp map build</c>'s output.
    /// </summary>
    [Fact]
    public void AMapThatHasBeenBuiltPassesTheBuild()
    {
        using var cart = new BuildTestCart("build-map-ok", BuildTestCart.HealthyMainCs);
        cart.Write("map.csv", BuildTestCart.MapCsv());
        Assert.Equal(0, CliProcess.Run("map", "build", cart.Root).ExitCode);

        CliResult result = CliProcess.Run("build", cart.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("map.bin is up to date with map.csv", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// A map source with no bank beside it. Without a refusal somewhere the console loads 18432
    /// zero bytes for a missing <c>map.bin</c> — every cell empty — and the cartridge runs, draws
    /// no world, and nothing anywhere connects that to the <c>map.csv</c> sitting next to it.
    ///
    /// <para>The refusal is <c>CartSource.RequireBuiltAsset</c>, which covers <c>map.csv</c>
    /// alongside <c>sfx.txt</c> and <c>music.txt</c>, so the build fails while loading rather
    /// than while checking banks. What is asserted here is what a caller can see: exit 1, and a
    /// message that names both the missing file and the command that produces it. Which of the
    /// two checks fired is an implementation detail; that one of them did is not.</para>
    /// </summary>
    [Fact]
    public void AMapSourceWithNoBankFailsTheBuild()
    {
        using var cart = new BuildTestCart("build-map-missing", BuildTestCart.HealthyMainCs);
        cart.Write("map.csv", BuildTestCart.MapCsv());

        CliResult result = CliProcess.Run("build", cart.Root);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("map.bin is not", result.StdErr, StringComparison.Ordinal);
        Assert.Contains("quarp map build", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>A bank that stopped matching its source is a stale bank, and stale is an error.</summary>
    [Fact]
    public void AStaleMapBankFailsTheBuild()
    {
        using var cart = new BuildTestCart("build-map-stale", BuildTestCart.HealthyMainCs);
        string csv = BuildTestCart.MapCsv();
        cart.Write("map.csv", csv);
        Assert.Equal(0, CliProcess.Run("map", "build", cart.Root).ExitCode);

        // One empty cell becomes tile 5: the token count is unchanged, so the file is still
        // valid — only the bank on disk is now a version of the map nobody is editing.
        int at = csv.IndexOf("-1", StringComparison.Ordinal);
        cart.Write("map.csv", string.Concat(csv.AsSpan(0, at), "5", csv.AsSpan(at + 2)));

        CliResult result = CliProcess.Run("build", cart.Root);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("map.bin does not match map.csv", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule for sound, through the banks that already exist. <c>CartSource</c> catches a
    /// <em>missing</em> <c>sfx.bin</c> at load time; nothing until now caught one that is merely
    /// out of date, which is the version of the bug an author actually hits — they edit
    /// <c>sfx.txt</c>, forget the compiler, and hear yesterday's sound.
    /// </summary>
    [Fact]
    public void AStaleSoundBankFailsTheBuild()
    {
        using var cart = new BuildTestCart("build-sfx-stale", BuildTestCart.HealthyMainCs);
        cart.Write("sfx.txt", "sfx 0\n  speed 3\n  00 C-6 sqr 6 -\n");
        Assert.Equal(0, CliProcess.Run("audio", "build", cart.Root).ExitCode);
        Assert.Equal(0, CliProcess.Run("build", cart.Root).ExitCode);

        cart.Write("sfx.txt", "sfx 0\n  speed 7\n  00 C-6 sqr 6 -\n");

        CliResult result = CliProcess.Run("build", cart.Root);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("sfx.bin does not match sfx.txt", result.StdErr, StringComparison.Ordinal);
        Assert.Contains("quarp audio build", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both cartridge shapes build. A <c>.quarp8</c> has no text sources inside it by design
    /// (SPEC-8 §6), so the bank comparison has nothing to compare and must not invent a failure
    /// out of that — a package that cannot be built would be a package CI cannot check.
    /// </summary>
    [Fact]
    public void APackagedCartridgeBuildsToo()
    {
        using var cart = new BuildTestCart("build-package", BuildTestCart.HealthyMainCs);
        string package = cart.Root + ".quarp8";
        try
        {
            Assert.Equal(0, CliProcess.Run("pack", cart.Root, "-o", package).ExitCode);

            CliResult result = CliProcess.Run("build", package);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("build ok", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(package);
        }
    }

    [Theory]
    [InlineData(new[] { "build" }, "usage: quarp build <cart>")]
    [InlineData(new[] { "build", "--check" }, "unknown argument '--check'")]
    [InlineData(new[] { "build", "$CART", "--ticks", "10" }, "unknown argument '--ticks'")]
    [InlineData(new[] { "build", "$CART/nowhere" }, "cartridge not found")]
    public void EveryBadSpellingFailsWithAnExplanation(string[] args, string expected)
    {
        using var cart = new BuildTestCart("build-args", BuildTestCart.HealthyMainCs);
        string[] resolved = Array.ConvertAll(
            args, argument => argument.Replace("$CART", cart.Root, StringComparison.Ordinal));

        CliResult result = CliProcess.Run(resolved);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expected, result.StdErr, StringComparison.Ordinal);
    }
}

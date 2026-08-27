using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// <c>quarp sim</c> with a scripted button track.
///
/// <para><b>Why the option exists.</b> A golden hash taken from an untouched run pins only what
/// the cartridge does when nobody plays it, and a cartridge that opens on a title screen and
/// waits does exactly one thing forever. The POOM port made that concrete: its goldens were
/// pinning a menu. <c>shot</c> and <c>replay record</c> already took the same track; <c>sim</c>
/// is where the golden numbers come from, so it needed it most.</para>
///
/// <para>The assertions below are all differences and equalities rather than literal hashes:
/// a literal here would be a second copy of the demo goldens, and it would go stale for
/// reasons that have nothing to do with the argument being tested.</para>
/// </summary>
public class SimInputTests
{
    private static string SnakeCart()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "carts", "snake");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/snake not found above the test directory");
    }

    /// <summary>The last line of stdout is the bare final frame hash — every consumer greps it.</summary>
    private static string FinalHash(CliResult result)
    {
        Assert.Equal(0, result.ExitCode);
        string[] lines = result.OutLines();
        return lines[^1];
    }

    /// <summary>
    /// The point of the option in one assertion: the same cartridge over the same number of
    /// ticks ends on a different frame when a button is pressed. If this ever passes with the
    /// two equal, the track is not reaching the cartridge and every golden taken with one is
    /// worthless.
    /// </summary>
    [Fact]
    public void AScriptedPressChangesTheFrame()
    {
        string cart = SnakeCart();

        string idle = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40"));
        string turned = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40", "--input", "2:U,4:"));

        Assert.NotEqual(idle, turned);
    }

    /// <summary>Same script, same hash — the run is still deterministic with input in it.</summary>
    [Fact]
    public void TheSameScriptGivesTheSameFrameTwice()
    {
        string cart = SnakeCart();

        string once = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40", "--input", "2:U,4:"));
        string twice = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40", "--input", "2:U,4:"));

        Assert.Equal(once, twice);
    }

    /// <summary>A file and a command line holding the same track are the same track. This is
    /// the form a golden actually uses: a track long enough to keep a game running does not fit
    /// on a command line.</summary>
    [Fact]
    public void AnInputFileIsTheSameAsTheScriptItHolds()
    {
        string cart = SnakeCart();
        string path = Path.Combine(Path.GetTempPath(), "quarp-sim-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "# a comment\n2:U\n4:\n");
        try
        {
            string inline = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40", "--input", "2:U,4:"));
            string fromFile = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40", "--input-file", path));

            Assert.Equal(inline, fromFile);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An empty track is the old behaviour, exactly: every golden taken before this
    /// option existed has to keep its number.</summary>
    [Fact]
    public void AnEmptyScriptIsTheSameAsNoScript()
    {
        string cart = SnakeCart();

        string none = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40"));
        string empty = FinalHash(CliProcess.Run("sim", cart, "--ticks", "40", "--input", ""));

        Assert.Equal(none, empty);
    }

    /// <summary>The audio digest line is still printed, and it moves with the input too.</summary>
    [Fact]
    public void TheAudioDigestIsStillPrinted()
    {
        CliResult result = CliProcess.Run("sim", SnakeCart(), "--ticks", "40", "--input", "2:U,4:");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.OutLines(), line => line.StartsWith("audio ", StringComparison.Ordinal));
    }
}

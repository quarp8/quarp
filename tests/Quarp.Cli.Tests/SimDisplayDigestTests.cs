using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// The <c>display</c> line <c>quarp sim</c> prints beside the audio one.
///
/// <para><b>Why it exists.</b> <c>Pald</c> and <c>Palr</c> are the output stage: they change
/// what the frame's colours <em>look like</em> without touching a single index in the frame
/// buffer. So a fade to black, a hit flash, a whole screen tinted red — none of them move the
/// frame hash by a byte, and until this line existed a cartridge could ship an effect that no
/// golden number could have caught breaking. The POOM port did exactly that with two of
/// them.</para>
///
/// <para>The digest is cumulative over ticks for the sharper half of the same reason: the
/// stage's whole purpose is to change between frames without redrawing, so comparing only the
/// final state would compare the state after the effect ended — which is the state it started
/// from.</para>
/// </summary>
public class SimDisplayDigestTests
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

    private static string LineWithPrefix(CliResult result, string prefix)
    {
        Assert.Equal(0, result.ExitCode);
        foreach (string line in result.OutLines())
        {
            if (line.StartsWith(prefix + " ", StringComparison.Ordinal))
            {
                return line[(prefix.Length + 1)..];
            }
        }
        throw new InvalidOperationException($"no '{prefix}' line in:\n{result}");
    }

    [Fact]
    public void SimPrintsADisplayDigestBesideTheAudioOne()
    {
        CliResult result = CliProcess.Run("sim", SnakeCart(), "--ticks", "40");

        string display = LineWithPrefix(result, "display");
        Assert.Equal(16, display.Length);
        Assert.All(display, c => Assert.True(Uri.IsHexDigit(c), $"not hex: {display}"));
    }

    /// <summary>
    /// The bare final line still means the frame. Eight determinism anchors, twelve pinned demo
    /// hashes and the cross-architecture job all grep <c>^[0-9a-f]{16}$</c> for it, so a second
    /// unlabelled hash would silently break every one of them.
    /// </summary>
    [Fact]
    public void TheLastLineIsStillTheBareFrameHash()
    {
        CliResult result = CliProcess.Run("sim", SnakeCart(), "--ticks", "40");

        string[] lines = result.OutLines();
        string last = lines[^1];
        Assert.Equal(16, last.Length);
        Assert.All(last, c => Assert.True(Uri.IsHexDigit(c), $"not hex: {last}"));
        Assert.DoesNotContain(" ", last, StringComparison.Ordinal);
    }

    /// <summary>Deterministic like everything else the command prints.</summary>
    [Fact]
    public void TheDisplayDigestIsTheSameOnASecondRun()
    {
        string cart = SnakeCart();

        string once = LineWithPrefix(CliProcess.Run("sim", cart, "--ticks", "40"), "display");
        string twice = LineWithPrefix(CliProcess.Run("sim", cart, "--ticks", "40"), "display");

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// It is a different quantity from the frame hash, not a copy of it. A cart that never
    /// touches the output stage still has a display digest, and it must not be the frame's.
    /// </summary>
    [Fact]
    public void TheDisplayDigestIsNotTheFrameHash()
    {
        CliResult result = CliProcess.Run("sim", SnakeCart(), "--ticks", "40");

        string display = LineWithPrefix(result, "display");
        string frame = result.OutLines()[^1];

        Assert.NotEqual(frame, display);
    }
}

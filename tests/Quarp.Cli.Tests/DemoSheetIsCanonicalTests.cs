using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// The committed sheet of every demo is CANONICAL for our own encoder: dumping the cart writes
/// bytes identical to the file already there.
///
/// <para>What this does and does not prove — measured, not assumed. Written first as an "the art
/// is still what the console holds" gate, it was run against a deliberately swapped sheet
/// (shmup given platformer's art) and stayed green, because since wave A2 the console's sheet
/// comes FROM this file: dump reads it and writes it back. So the claim is narrowed to what the
/// run actually tests — decode, re-encode, byte equality — which is worth pinning on its own:
/// a sheet edited by any other tool, or by a future encoder with different settings, would make
/// every later save or dump a spurious diff in a repository where art is reviewed as bytes.</para>
///
/// <para>Whether the art is RIGHT is a different question with a different guard:
/// <c>Quarp.CartKit.Tests.DemoSheetInvariantTests</c> states the rules the deleted code used to
/// enforce (digger's sprite 8 is pixel-for-pixel sprite 3, the start marker is empty, a sprite a
/// cart names is not blank), and the twelve pinned walkthrough hashes cover everything a
/// playthrough actually draws.</para>
///
/// <para>Negative control: change the encoder's filter or compression settings and all four
/// cases go red at once; that is the drift this exists to catch.</para>
/// </summary>
public class DemoSheetIsCanonicalTests : IDisposable
{
    private readonly string _out;

    public DemoSheetIsCanonicalTests()
    {
        _out = Path.Combine(Path.GetTempPath(), "quarp-art-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_out);
    }

    public void Dispose() => Directory.Delete(_out, recursive: true);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "carts", "snake", "manifest.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/snake not found above the test directory");
    }

    [Theory]
    [InlineData("dialogue")]
    [InlineData("digger")]
    [InlineData("platformer")]
    [InlineData("shmup")]
    public void TheCommittedSheetIsCanonicalForOurEncoder(string cart)
    {
        string cartFolder = Path.Combine(RepoRoot(), "carts", cart);
        string committed = Path.Combine(cartFolder, "gfx.png");
        Assert.True(File.Exists(committed), $"{cart} lost its gfx.png — wave A2 put one there");
        string dumped = Path.Combine(_out, cart + ".png");

        CliResult result = CliProcess.Run("gfx", "dump", cartFolder, "-o", dumped);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(File.ReadAllBytes(committed), File.ReadAllBytes(dumped));
    }
}

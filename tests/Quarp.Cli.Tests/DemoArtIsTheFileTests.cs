using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// The gate the art migration deserved and did not have (M9 wave A2, gap named by the session
/// audit of 2026-08-24): the sheet committed next to a demo must still be the sheet its console
/// holds after <c>Init</c>.
///
/// <para>The twelve pinned walkthrough hashes cannot say this. They compare the last frame of a
/// scripted run, so a sprite the run never puts on screen — the second enemy of a two-row wave,
/// the open exit before it opens — can drift in the file without moving a single hash. This test
/// asks the console itself, through the same <c>quarp gfx dump</c> that extracted the art in the
/// first place, and compares bytes.</para>
///
/// <para>Negative control: flip one pixel of <c>carts/shmup/gfx.png</c> and this goes red while
/// every hash stays green — which is the whole reason it exists. Break the encoder's determinism
/// and it goes red for all four carts at once.</para>
/// </summary>
public class DemoArtIsTheFileTests : IDisposable
{
    private readonly string _out;

    public DemoArtIsTheFileTests()
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
    public void TheCommittedSheetIsWhatTheConsoleHoldsAfterInit(string cart)
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

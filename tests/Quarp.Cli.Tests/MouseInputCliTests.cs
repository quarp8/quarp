using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// The pointer's whole headless path (ADR-030): the <c>tick:mX.Y[LRM][wN]</c> grammar drives a
/// cartridge through <c>quarp sim</c>, a recording of it becomes a version-2 <c>.qrpr</c>
/// that plays back to the same hashes, and recording the same script twice gives the same
/// bytes — the canonicity claim REPLAY-FORMAT §3 makes for every stream, proved end to end.
///
/// <para>Like <see cref="SimInputTests"/>, assertions are differences and equalities rather
/// than literal hashes: a literal would just be a private golden that goes stale for reasons
/// unrelated to the pointer.</para>
/// </summary>
public class MouseInputCliTests : IDisposable
{
    /// <summary>
    /// A cartridge whose frame is a pure function of the pointer: position echoed as a pixel,
    /// clicks counted by <c>MouseBtnp</c> (the edge, so a held button is one click), a held
    /// right button and the wheel accumulated into bars. Every one of the five ADR-030 calls
    /// participates, so a hash move proves the whole surface reached the simulation.
    /// </summary>
    private const string MouseCartCs = """
        using Quarp.Api;

        namespace Fixture;

        public sealed class MousePrbCart : Cartridge
        {
            private int _clicks;
            private int _held;
            private int _scroll;

            public override void Update()
            {
                if (MouseBtnp(MouseButton.Left))
                {
                    _clicks++;
                }
                if (MouseBtn(MouseButton.Right))
                {
                    _held++;
                }
                _scroll += MouseWheel;
            }

            public override void Draw()
            {
                Cls(0);
                Pset(MouseX, MouseY, 7);
                RectFill(0, 0, _clicks, 2, 8);
                RectFill(0, 4, _held % 100, 2, 11);
                RectFill(40, 8, _scroll % 40, 2, 12);
            }
        }
        """;

    private const string PointerScript = "10:m80.45,30:m100.60L,31:m100.60,60:m100.60w1,61:m100.60,90:m250.200R";

    private readonly BuildTestCart _cart = new("mouse", MouseCartCs);

    public void Dispose() => _cart.Dispose();

    private static string FinalHash(CliResult result)
    {
        Assert.True(result.ExitCode == 0, result.ToString());
        return result.OutLines()[^1];
    }

    private static string AudioLine(CliResult result) =>
        Assert.Single(result.OutLines(), line => line.StartsWith("audio ", StringComparison.Ordinal));

    private string TempReplay() =>
        Path.Combine(Path.GetTempPath(), "quarp-mouse-" + Guid.NewGuid().ToString("N") + ".qrpr");

    /// <summary>The option's point in one assertion: the pointer reaches the cartridge.</summary>
    [Fact]
    public void AScriptedPointerChangesTheFrame()
    {
        string idle = FinalHash(CliProcess.Run("sim", _cart.Root, "--ticks", "120"));
        string moved = FinalHash(CliProcess.Run("sim", _cart.Root, "--ticks", "120", "--input", PointerScript));

        Assert.NotEqual(idle, moved);
    }

    /// <summary>
    /// Record with a pointer, play the file back: same final frame, same audio digest. This is
    /// the machine-time promise for the new stream — what was recorded is what resimulates.
    /// </summary>
    [Fact]
    public void ARecordedPointerReplayPlaysBackToTheSameHashes()
    {
        string replay = TempReplay();
        try
        {
            CliResult recorded = CliProcess.Run(
                "replay", "record", _cart.Root, "-o", replay, "--ticks", "120", "--input", PointerScript);
            CliResult played = CliProcess.Run("replay", "play", replay, "--cart", _cart.Root);
            CliResult simmed = CliProcess.Run("sim", _cart.Root, "--ticks", "120", "--input", PointerScript);

            Assert.Equal(FinalHash(recorded), FinalHash(played));
            Assert.Equal(AudioLine(recorded), AudioLine(played));
            // And the recording changed nothing about the run it recorded.
            Assert.Equal(FinalHash(simmed), FinalHash(recorded));
        }
        finally
        {
            File.Delete(replay);
        }
    }

    /// <summary>
    /// Every recording is a version 0 file, pointer or no pointer: since ADR-041 the format has
    /// one living version and one layout, and the difference between the two files below is the
    /// contents of the pointer stream, not the shape of the file.
    /// </summary>
    [Fact]
    public void EveryRecordingIsAVersionZeroFileWithOrWithoutAPointer()
    {
        string withMouse = TempReplay();
        string withoutMouse = TempReplay();
        try
        {
            Assert.Equal(0, CliProcess.Run(
                "replay", "record", _cart.Root, "-o", withMouse, "--ticks", "30",
                "--input", "5:m10.10L,6:m10.10").ExitCode);
            Assert.Equal(0, CliProcess.Run(
                "replay", "record", _cart.Root, "-o", withoutMouse, "--ticks", "30").ExitCode);

            Assert.Equal(0, BitConverter.ToUInt16(File.ReadAllBytes(withMouse), 4));
            Assert.Equal(0, BitConverter.ToUInt16(File.ReadAllBytes(withoutMouse), 4));

            // The control: the two files are still different files — the pointer stream of the
            // first carries runs the second's does not.
            Assert.NotEqual(File.ReadAllBytes(withMouse), File.ReadAllBytes(withoutMouse));
        }
        finally
        {
            File.Delete(withMouse);
            File.Delete(withoutMouse);
        }
    }

    /// <summary>
    /// The §3 canonicity claim, end to end: the encoder is a function of the input track, so
    /// two recordings of one script are one file. This is what lets a mouse golden live in a
    /// repository the way carts/snake's does.
    /// </summary>
    [Fact]
    public void RecordingTheSamePointerScriptTwiceIsByteIdentical()
    {
        string first = TempReplay();
        string second = TempReplay();
        try
        {
            Assert.Equal(0, CliProcess.Run(
                "replay", "record", _cart.Root, "-o", first, "--ticks", "120", "--input", PointerScript).ExitCode);
            Assert.Equal(0, CliProcess.Run(
                "replay", "record", _cart.Root, "-o", second, "--ticks", "120", "--input", PointerScript).ExitCode);

            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    /// <summary>
    /// The wheel entry repeats every tick until the next pointer entry — the trap the usage
    /// text warns about, pinned: one notch needs a release entry, exactly like a Btnp tap.
    /// </summary>
    [Fact]
    public void AWheelEntryRepeatsUntilReleased()
    {
        string oneNotch = FinalHash(CliProcess.Run(
            "sim", _cart.Root, "--ticks", "80", "--input", "10:m5.5w1,11:m5.5"));
        string heldWheel = FinalHash(CliProcess.Run(
            "sim", _cart.Root, "--ticks", "80", "--input", "10:m5.5w1"));
        string idle = FinalHash(CliProcess.Run("sim", _cart.Root, "--ticks", "80"));

        Assert.NotEqual(idle, oneNotch);
        Assert.NotEqual(oneNotch, heldWheel);
    }

    /// <summary>The two tracks are independent: a button entry and a pointer entry may share a tick.</summary>
    [Fact]
    public void PadAndPointerEntriesMayShareATick()
    {
        CliResult result = CliProcess.Run(
            "sim", _cart.Root, "--ticks", "40", "--input", "10:m50.30L,10:X,11:m50.30,11:");

        Assert.True(result.ExitCode == 0, result.ToString());
    }

    /// <summary>The grammar's rejections come with the fix in the message, like the button track's.</summary>
    [Theory]
    [InlineData("10:m5", "'.'")]
    [InlineData("10:m300.5", "0..255")]
    [InlineData("10:m5.5Z", "pointer flag")]
    [InlineData("10:m5.5w", "wheel")]
    [InlineData("10:m5.5,5:m1.1", "backwards")]
    public void ABrokenPointerEntryIsRefusedWithAReadableMessage(string script, string expected)
    {
        CliResult result = CliProcess.Run("sim", _cart.Root, "--ticks", "10", "--input", script);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expected, result.StdErr);
    }
}

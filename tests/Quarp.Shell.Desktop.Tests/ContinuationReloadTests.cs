using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The M2 milestone criterion, driven through the real shell path rather than through
/// <see cref="TimeMachine"/> directly: edit a cartridge's code while it is running and the
/// player must end up <b>in the same place in the game</b> with the new code, not back at the
/// start (ROADMAP M2; ARCHITECTURE §4).
///
/// <para>Everything on that path is real — a cartridge folder on disk, the
/// <see cref="CartWatcher"/> debounce, a Roslyn compile, a fresh collectible load context, and
/// <c>TimeMachine.Rebuild</c>. The only thing missing compared to <c>quarp run</c> is the
/// window, and <see cref="CartSession"/> deliberately owns no graphics: it is the session, not
/// the renderer. That is what makes this testable at all.</para>
///
/// <para>These tests compile cartridges, so they cost seconds rather than milliseconds. That
/// is the right trade for the one claim the whole milestone is named after.</para>
/// </summary>
public class ContinuationReloadTests : IDisposable
{
    /// <summary>
    /// A cartridge with state that only exists if every tick before it ran: <c>_x</c> walks,
    /// <c>_step</c> counts, and <c>Init</c> reseeds both — including <c>_step</c>, because a
    /// rewind reuses the same instance and nothing zeroes fields it does not assign.
    /// <c>{COLOR}</c> stands in for the one thing an author edits mid-game in this story.
    /// </summary>
    private const string CartSource = """
        using Quarp.Api;

        public sealed class ReloadCart : Cartridge
        {
            private int _x;
            private int _step;

            public override void Init()
            {
                _x = 4;
                _step = 0;
            }

            public override void Update()
            {
                _step++;
                _x = (_x + 1) % 110;
            }

            public override void Draw()
            {
                Cls(0);
                RectFill(_x, 20, 8, 8, {COLOR});
                Pset(_step % 128, 60, 3);
            }
        }
        """;

    private readonly string _folder;

    public ContinuationReloadTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "quarp-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "src"));
        File.WriteAllText(Path.Combine(_folder, "manifest.json"),
            "{\"name\":\"reload\",\"author\":\"\",\"profile\":8}");
        WriteCart(7);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteCart(byte color) =>
        File.WriteAllText(
            Path.Combine(_folder, "src", "main.cs"),
            CartSource.Replace("{COLOR}", color.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The frame a straight run of this exact source reaches at that tick.</summary>
    private static byte[] ReferenceFrame(string source, int ticks)
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", source) }, "reference");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        using CartHost host = CartHost.Load(result.AssemblyBytes);

        var machine = new TimeMachine(
            ConsoleProfile.Profile8,
            host.Cartridge,
            new ReplayHeader(CartIdentity.Unknown, seed: 0, ReadOnlySpan<int>.Empty),
            new ReplayLog());
        machine.Boot();
        machine.Advance(ticks, default);
        return machine.Framebuffer.Pixels.ToArray();
    }

    /// <summary>
    /// Runs the shell's per-frame poll for long enough that a real
    /// <see cref="Quarp.CartKit.CartWatcher"/> debounce window has closed and the reload it
    /// triggered has finished compiling. Zero ticks per frame: this is about the reload, not
    /// about advancing the simulation.
    ///
    /// <para>Wall clock rather than a signal, because the thing under test is the real
    /// FileSystemWatcher path a player actually uses. Two seconds is roughly ten debounce
    /// windows plus a warm Roslyn compile; a reload that has not landed by then is a failure
    /// worth reporting as one.</para>
    /// </summary>
    private static void PumpFrames(CartSession session)
    {
        for (int frame = 0; frame < 200; frame++)
        {
            session.Update(0, default, rewinding: false);
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void EditingTheCodeMidGameContinuesInTheSamePlace()
    {
        byte[] newCodeAt300 = ReferenceFrame(CartSource.Replace("{COLOR}", "11"), 300);
        byte[] oldCodeAt300 = ReferenceFrame(CartSource.Replace("{COLOR}", "7"), 300);
        byte[] newCodeAtZero = ReferenceFrame(CartSource.Replace("{COLOR}", "11"), 0);

        using CartSession session = CartSession.Start(_folder);
        session.Update(300, default, rewinding: false);
        Assert.Equal(300, session.Tick);
        Assert.Equal(oldCodeAt300, session.Framebuffer.Pixels);

        // The author changes the colour and saves.
        WriteCart(11);
        PumpFrames(session);

        // The milestone, in three assertions: same tick, the new build's frame, and provably
        // not a restart.
        Assert.Equal(300, session.Tick);
        Assert.Equal(newCodeAt300, session.Framebuffer.Pixels);
        Assert.NotEqual(newCodeAtZero, session.Framebuffer.Pixels);
    }

    /// <summary>
    /// A compile error must not disturb the running session at all — not its code, not its
    /// tick. M1 promised the old cartridge keeps running; M2 must not have traded that away
    /// for continuation.
    /// </summary>
    [Fact]
    public void ABrokenEditLeavesTheRunningSessionAlone()
    {
        byte[] oldCodeAt120 = ReferenceFrame(CartSource.Replace("{COLOR}", "7"), 120);

        using CartSession session = CartSession.Start(_folder);
        session.Update(120, default, rewinding: false);

        File.WriteAllText(Path.Combine(_folder, "src", "main.cs"), "public sealed class Broken : { not C#");
        PumpFrames(session);

        Assert.Equal(120, session.Tick);
        Assert.Equal(oldCodeAt120, session.Framebuffer.Pixels);
    }

    /// <summary>
    /// Banned code is a compile failure like any other: the session survives it, which matters
    /// because the analyzer now runs inside the compile and an author will trip QRP1001 while
    /// typing a number.
    /// </summary>
    [Fact]
    public void AnEditThatTripsTheDeterminismBanLeavesTheSessionAlone()
    {
        using CartSession session = CartSession.Start(_folder);
        session.Update(60, default, rewinding: false);

        File.WriteAllText(Path.Combine(_folder, "src", "main.cs"),
            CartSource.Replace("{COLOR}", "7").Replace("_x = (_x + 1) % 110;", "_x = (int)(_x + 1.5);"));
        PumpFrames(session);

        Assert.Equal(60, session.Tick);
    }

    /// <summary>
    /// New code that cannot survive the recorded past falls back to restart mode with the
    /// session still usable — not to a dead console and not to an exception in the frame loop.
    /// </summary>
    [Fact]
    public void CodeThatCannotReplayThePastFallsBackToRestart()
    {
        using CartSession session = CartSession.Start(_folder);
        session.Update(300, default, rewinding: false);
        Assert.Equal(300, session.Tick);

        File.WriteAllText(Path.Combine(_folder, "src", "main.cs"), """
            using Quarp.Api;

            public sealed class ReloadCart : Cartridge
            {
                public override void Update()
                {
                    if (Ticks == 150)
                    {
                        throw new System.InvalidOperationException("cannot replay tick 150");
                    }
                }

                public override void Draw() => Cls(0);
            }
            """);
        PumpFrames(session);

        // Restart mode: back at the start on the new code, and still running.
        Assert.True(session.Tick < 300);
        session.Update(10, default, rewinding: false);
        Assert.True(session.Tick <= 160);
    }

    /// <summary>
    /// Rewind through the shell: holding Backspace spends the frame's ticks going backwards,
    /// and the landing frame is the one a straight run would have produced.
    /// </summary>
    [Fact]
    public void RewindingThroughTheSessionLandsOnADirectRunsFrame()
    {
        byte[] at250 = ReferenceFrame(CartSource.Replace("{COLOR}", "7"), 250);

        using CartSession session = CartSession.Start(_folder);
        session.Update(300, default, rewinding: false);
        session.Update(50, default, rewinding: true);

        Assert.Equal(250, session.Tick);
        Assert.Equal(at250, session.Framebuffer.Pixels);
    }

    /// <summary>Rewinding past tick 0 stops at tick 0 rather than throwing or wrapping.</summary>
    [Fact]
    public void RewindingPastTheStartStopsAtTheStart()
    {
        using CartSession session = CartSession.Start(_folder);
        session.Update(100, default, rewinding: false);
        session.Update(5000, default, rewinding: true);

        Assert.Equal(0, session.Tick);
    }

    /// <summary>
    /// F5 then F8: the replay written next to the cartridge is readable, plays back, and
    /// leaving playback puts the live session back exactly where it was — the two share one
    /// cartridge instance, so "back where it was" is a claim that has to be checked.
    /// </summary>
    [Fact]
    public void SavingAndPlayingAReplayLeavesTheLiveSessionIntact()
    {
        byte[] at200 = ReferenceFrame(CartSource.Replace("{COLOR}", "7"), 200);

        using CartSession session = CartSession.Start(_folder);
        session.Update(200, default, rewinding: false);

        session.ApplyCommands(new ShellCommands { SaveReplay = true });
        string[] replays = Directory.GetFiles(Path.Combine(_folder, "replays"), "*.qrpr");
        Assert.Single(replays);

        session.ApplyCommands(new ShellCommands { PlayReplay = true });
        session.Update(60, default, rewinding: false);
        Assert.Equal(60, session.Tick);      // the playback machine, at its own tick

        session.ApplyCommands(new ShellCommands { PlayReplay = true });

        Assert.Equal(200, session.Tick);
        Assert.Equal(at200, session.Framebuffer.Pixels);
    }

    /// <summary>The speed ladder and pause, as the keys drive them.</summary>
    [Fact]
    public void PauseAndSpeedKeysMoveTheSessionState()
    {
        using CartSession session = CartSession.Start(_folder);
        Assert.False(session.IsPaused);
        Assert.True(session.Speed.IsNormal);

        session.ApplyCommands(new ShellCommands { TogglePause = true });
        Assert.True(session.IsPaused);

        session.ApplyCommands(new ShellCommands { Slower = true });
        Assert.Equal("0.5x", session.Speed.Label);
        session.ApplyCommands(new ShellCommands { Faster = true });
        session.ApplyCommands(new ShellCommands { Faster = true });
        Assert.Equal("2x", session.Speed.Label);

        // Stepping forward and back moves exactly one tick and keeps the session paused.
        session.ApplyCommands(new ShellCommands { StepForward = true });
        Assert.Equal(1, session.Tick);
        session.ApplyCommands(new ShellCommands { StepBack = true });
        Assert.Equal(0, session.Tick);
        Assert.True(session.IsPaused);
    }

    /// <summary>Home rewinds to tick 0 and pauses there, whatever the session was doing.</summary>
    [Fact]
    public void HomeReturnsToTheStartAndPauses()
    {
        using CartSession session = CartSession.Start(_folder);
        session.Update(180, default, rewinding: false);

        session.ApplyCommands(new ShellCommands { ToStart = true });

        Assert.Equal(0, session.Tick);
        Assert.True(session.IsPaused);
    }
}

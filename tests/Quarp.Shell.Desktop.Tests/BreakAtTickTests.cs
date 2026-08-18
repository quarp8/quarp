using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <c>quarp run &lt;cart&gt; --break-at N</c> — the debugger-free half of "debugging in time"
/// (ADR-019; M4 work order, stage 1). The console catches up to the named tick and stops
/// <b>before</b> that tick's <c>Update</c>, so the author is looking at the state the suspect
/// tick is about to be handed, not at what it did.
///
/// <para><b>Where the off-by-one lives, and why it is where it is.</b> The console counts a
/// tick before running it, so <c>Ticks</c> already reads N inside tick N's <c>Update</c>
/// (API-8 §8). "Before tick N's Update" is therefore the state after N-1 ticks, and
/// <see cref="CartSession.Tick"/> reads <c>N - 1</c> at the break. That is the same instant a
/// conditional breakpoint <c>Ticks == N</c> stops at, which is the whole point: the two ways of
/// reaching one moment have to agree, or <c>DEBUGGING.md</c> would be documenting two different
/// moments under one name.</para>
///
/// <para>Nothing here needs a window: <see cref="CartSession"/> owns the simulation and
/// <c>QuarpGame</c> owns the graphics device, the same split
/// <see cref="ContinuationReloadTests"/> relies on. Frames are compared against a straight run
/// of the same cartridge through <see cref="TimeMachine"/>, and every comparison is flanked by
/// its neighbours — a frame that equals tick 99 but also equals ticks 98 and 100 proves
/// nothing, and that is exactly the trap the M2 determinism test fell into.</para>
/// </summary>
public class BreakAtTickTests : IDisposable
{
    /// <summary>
    /// A cartridge whose picture changes on every single tick — one pixel walking with
    /// <c>Ticks</c>, one block walking with an accumulator that only holds the right value if
    /// every tick before it ran. Both are needed: the pixel alone would repeat every 128 ticks,
    /// and a test that cannot tell tick 99 from tick 227 is not measuring anything.
    /// </summary>
    private const string CartSourceText = """
        using Quarp.Api;

        public sealed class BreakCart : Cartridge
        {
            private int _sum;

            public override void Init()
            {
                _sum = 0;
            }

            public override void Update()
            {
                _sum = (_sum + Ticks) % 997;
            }

            public override void Draw()
            {
                Cls(0);
                Pset(Ticks % 128, 10, 7);
                RectFill(_sum % 118, 30, 8, 8, 11);
            }
        }
        """;

    private readonly string _folder;

    public BreakAtTickTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "quarp-breakat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "src"));
        File.WriteAllText(Path.Combine(_folder, "manifest.json"),
            "{\"name\":\"breakat\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(Path.Combine(_folder, "src", "main.cs"), CartSourceText);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>The frame a straight, uninterrupted run of this cartridge reaches at that tick.</summary>
    private static byte[] ReferenceFrame(int ticks)
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", CartSourceText) }, "reference");
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
    /// Drives the session the way the shell does — a handful of ticks per frame — until the
    /// break trips. <paramref name="ticksPerFrame"/> deliberately does not divide the distance
    /// to the break: a frame budget that landed exactly on the target would hide an
    /// implementation that only checks the tick after spending the whole budget.
    /// </summary>
    private static void PumpUntilBreak(CartSession session, int ticksPerFrame = 7)
    {
        for (int frame = 0; frame < 2000 && !session.IsAtBreak; frame++)
        {
            session.Update(ticksPerFrame, default, rewinding: false);
        }
        Assert.True(session.IsAtBreak, "the break never fired");
    }

    /// <summary>
    /// The headline claim: exactly N, from a frame fat enough to sail past it ten times over.
    /// </summary>
    [Fact]
    public void TheSessionStopsBeforeUpdateOfTheNamedTick()
    {
        using CartSession session = CartSession.Start(_folder);
        session.BreakAt = 100;

        session.Update(1000, default, rewinding: false);

        Assert.True(session.IsAtBreak);
        Assert.True(session.IsPaused);
        Assert.Equal(99, session.Tick);     // tick 100 has not run yet — that is the point

        // And it stays there: further frames of the shell's clock spend nothing.
        session.Update(60, default, rewinding: false);
        session.Update(60, default, rewinding: false);
        Assert.Equal(99, session.Tick);
    }

    /// <summary>
    /// Arriving one tick at a time, or seven at a time, must land on the same tick as arriving
    /// in one thousand-tick leap. This is where an "overshoot, then notice" implementation dies.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(1000)]
    public void TheFrameBudgetDoesNotMoveTheBreak(int ticksPerFrame)
    {
        using CartSession session = CartSession.Start(_folder);
        session.BreakAt = 250;

        PumpUntilBreak(session, ticksPerFrame);

        Assert.Equal(249, session.Tick);
    }

    /// <summary>
    /// The state at the break is the state a run that never stopped passed through — the flag
    /// changes the pacing and nothing else. The two neighbour frames are asserted to be
    /// different pictures first, so the equality below cannot be satisfied by a cartridge that
    /// draws the same thing forever (the M2 lesson).
    /// </summary>
    [Fact]
    public void TheFrameAtTheBreakIsTheFrameAStraightRunProduces()
    {
        byte[] at98 = ReferenceFrame(98);
        byte[] at99 = ReferenceFrame(99);
        byte[] at100 = ReferenceFrame(100);
        Assert.NotEqual(at98, at99);
        Assert.NotEqual(at100, at99);

        using CartSession session = CartSession.Start(_folder);
        session.BreakAt = 100;
        session.Update(1000, default, rewinding: false);

        Assert.Equal(at99, session.Framebuffer.Pixels);
    }

    /// <summary>
    /// One press of <c>.</c> runs exactly the tick the break named. This is what makes the flag
    /// worth having: the suspect tick executes under the author's eye, once.
    /// </summary>
    [Fact]
    public void SteppingOnceRunsExactlyTheTickTheBreakNamed()
    {
        byte[] at100 = ReferenceFrame(100);

        using CartSession session = CartSession.Start(_folder);
        session.BreakAt = 100;
        session.Update(1000, default, rewinding: false);

        session.ApplyCommands(new ShellCommands { StepForward = true });

        Assert.Equal(100, session.Tick);
        Assert.Equal(at100, session.Framebuffer.Pixels);
        Assert.True(session.IsPaused);      // stepping does not resume the session
        Assert.False(session.IsAtBreak);    // ...and we are no longer standing at the break
    }

    /// <summary>
    /// Twice in a row, from a cold start: same tick, same pixels. Cheap to state, and the only
    /// reason the flag is usable for chasing a bug at all.
    /// </summary>
    [Fact]
    public void TwoRunsWithTheSameBreakLandOnTheSameState()
    {
        byte[] first;
        int firstTick;
        using (CartSession a = CartSession.Start(_folder))
        {
            a.BreakAt = 250;
            PumpUntilBreak(a);
            firstTick = a.Tick;
            first = a.Framebuffer.Pixels.ToArray();
        }

        using CartSession b = CartSession.Start(_folder);
        b.BreakAt = 250;
        PumpUntilBreak(b, ticksPerFrame: 13);   // a different pacing, deliberately

        Assert.Equal(249, firstTick);
        Assert.Equal(firstTick, b.Tick);
        Assert.Equal(first, b.Framebuffer.Pixels);
    }

    /// <summary>
    /// N = 0 is defined rather than clamped away: tick 0 is <c>Init</c> and has no
    /// <c>Update</c> (API-8 §2), so <c>--break-at 0</c> means "stop after Init, before the first
    /// Update" — the same instant <c>--break-at 1</c> names. Both stand at tick 0, and that tick
    /// is provably not tick 1.
    /// </summary>
    [Fact]
    public void BreakAtZeroAndBreakAtOneBothStopBeforeTheFirstUpdate()
    {
        byte[] atInit = ReferenceFrame(0);
        byte[] afterOneTick = ReferenceFrame(1);
        Assert.NotEqual(atInit, afterOneTick);

        using (CartSession zero = CartSession.Start(_folder))
        {
            zero.BreakAt = 0;
            zero.Update(60, default, rewinding: false);

            Assert.True(zero.IsAtBreak);
            Assert.Equal(0, zero.Tick);
            Assert.Equal(atInit, zero.Framebuffer.Pixels);
        }

        using CartSession one = CartSession.Start(_folder);
        one.BreakAt = 1;
        one.Update(60, default, rewinding: false);

        Assert.True(one.IsAtBreak);
        Assert.Equal(0, one.Tick);
        Assert.Equal(atInit, one.Framebuffer.Pixels);
    }

    /// <summary>
    /// A break is one-shot. Space means "carry on", not "stop again next frame" — otherwise the
    /// session would be unusable the moment the author resumed it.
    /// </summary>
    [Fact]
    public void ResumingAfterTheBreakRunsOnAndDoesNotStopAgain()
    {
        using CartSession session = CartSession.Start(_folder);
        session.BreakAt = 60;
        session.Update(1000, default, rewinding: false);
        Assert.Equal(59, session.Tick);

        session.ApplyCommands(new ShellCommands { TogglePause = true });
        Assert.False(session.IsPaused);
        Assert.False(session.IsAtBreak);

        session.Update(120, default, rewinding: false);

        Assert.Equal(179, session.Tick);
        Assert.False(session.IsPaused);
        Assert.False(session.IsAtBreak);
    }

    /// <summary>
    /// The control that gives the assertions above their meaning: with no break armed the very
    /// same session, cartridge and frame budget run straight through. Whatever stopped the runs
    /// above was the flag, not the shell.
    /// </summary>
    [Fact]
    public void WithoutTheFlagNothingStops()
    {
        using CartSession session = CartSession.Start(_folder);

        session.Update(1000, default, rewinding: false);

        Assert.Equal(1000, session.Tick);
        Assert.False(session.IsPaused);
        Assert.False(session.IsAtBreak);
        Assert.Null(session.BreakAt);
    }

    /// <summary>
    /// A break already behind the console cannot be honoured by running forward, so it fires at
    /// once instead of quietly never firing. Reachable only by setting
    /// <see cref="CartSession.BreakAt"/> mid-session; pinned because "silently never" is the
    /// failure mode nobody notices.
    /// </summary>
    [Fact]
    public void ABreakSetBehindTheConsoleFiresImmediately()
    {
        using CartSession session = CartSession.Start(_folder);
        session.Update(500, default, rewinding: false);
        Assert.Equal(500, session.Tick);

        session.BreakAt = 100;
        session.Update(60, default, rewinding: false);

        Assert.True(session.IsAtBreak);
        Assert.Equal(500, session.Tick);    // it did not rewind, and it did not run on
    }
}

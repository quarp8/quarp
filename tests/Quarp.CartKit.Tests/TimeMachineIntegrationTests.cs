using Quarp.Api;
using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The M2 milestone criteria, checked end to end against really compiled cartridges rather
/// than against hand-built stubs: rewinding lands on the frame a straight run would have
/// produced, and recompiling mid-game continues instead of restarting.
///
/// <para>These live in the CartKit tests because they need the real pipeline — Roslyn, the
/// collectible load context, two independently loaded builds of the same cartridge. Core's
/// own tests cannot compile a cartridge, and the shell's cannot run without a window.</para>
///
/// <para>Frames are compared as whole pixel arrays, not as hashes. A hash tells you two
/// frames differ; the array tells xUnit which pixel, which is the difference between a
/// five-minute diagnosis and an afternoon.</para>
/// </summary>
public class TimeMachineIntegrationTests
{
    /// <summary>
    /// A cartridge whose frame depends on accumulated state, so "the same place" is a claim
    /// with teeth: <c>_x</c> and <c>_y</c> only mean anything if every prior tick ran, and the
    /// RNG in Init only agrees if the seed was restored. <c>{0}</c> is the body colour — the
    /// one thing an "edit the snake's colour" test is allowed to change.
    /// </summary>
    private const string CounterCartFormat = """
        using Quarp.Api;

        public sealed class CounterCart : Cartridge
        {
            private int _x;
            private int _y;
            private int _step;

            public override void Init()
            {
                // _step is reset here on purpose, and it is not decoration. A rewind reuses
                // the SAME cartridge instance: the console resets its own state and calls
                // Init again, but nothing zeroes the cartridge's fields. A field left out of
                // Init keeps its value from the run being rewound, and the resimulation then
                // diverges silently. See InitMustResetEveryFieldOrRewindDiverges below.
                _step = 0;
                _x = RndInt(100);
                _y = RndInt(60);
            }

            public override void Update()
            {
                _step++;
                _x = (_x + 1) % 120;
                if (_step % 7 == 0)
                {
                    _y = (_y + 3) % 64;
                }
            }

            public override void Draw()
            {
                Cls(0);
                RectFill(_x, _y, 8, 8, {0});
                Pset(_step % 128, 70, 3);
            }
        }
        """;

    /// <summary>A build that survives Init and the early game, then throws on a known tick.</summary>
    private const string CrashingCart = """
        using Quarp.Api;

        public sealed class CounterCart : Cartridge
        {
            private int _x;

            public override void Update()
            {
                if (Ticks == 150)
                {
                    throw new System.InvalidOperationException("the new code cannot replay tick 150");
                }
                _x++;
            }

            public override void Draw()
            {
                Cls(0);
                RectFill(_x % 120, 30, 8, 8, 7);
            }
        }
        """;

    private static CartHost Load(string source)
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", source) }, "timemachinecart");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return CartHost.Load(result.AssemblyBytes);
    }

    private static CartHost LoadCounter(byte color) =>
        Load(CounterCartFormat.Replace("{0}", color.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static TimeMachine Machine(CartHost host)
    {
        var machine = new TimeMachine(
            ConsoleProfile.Profile8,
            host.Cartridge,
            new ReplayHeader(CartIdentity.Unknown, seed: 0, ReadOnlySpan<int>.Empty),
            new ReplayLog());
        machine.Boot();
        return machine;
    }

    /// <summary>A copy, because the framebuffer keeps being overwritten by the next tick.</summary>
    private static byte[] Snapshot(TimeMachine machine) => machine.Framebuffer.Pixels.ToArray();

    private static byte[] RunFresh(CartHost host, int ticks)
    {
        TimeMachine machine = Machine(host);
        machine.Advance(ticks, default);
        return Snapshot(machine);
    }

    // --- acceptance item 4: the time machine, both directions ---

    /// <summary>
    /// Rewind: after 600 ticks, <c>SeekTo(300)</c> must give the frame a straight 300-tick run
    /// gives. This is resimulation from tick 0 with Draw suppressed until the landing tick
    /// (ADR-006), so it also proves the suppression is invisible.
    /// </summary>
    [Fact]
    public void SeekingBackwardLandsOnTheFrameADirectRunProduces()
    {
        using CartHost host = LoadCounter(7);
        using CartHost reference = LoadCounter(7);

        byte[] expected = RunFresh(reference, 300);

        TimeMachine machine = Machine(host);
        machine.Advance(600, default);
        byte[] at600 = Snapshot(machine);

        machine.SeekTo(300);

        Assert.Equal(300, machine.Tick);
        Assert.Equal(expected, machine.Framebuffer.Pixels);
        // Non-vacuous: the two frames really are different, so equality above means something.
        Assert.NotEqual(expected, at600);
    }

    /// <summary>Forward: continuing from a rewound tick must reach exactly where it left.</summary>
    [Fact]
    public void SeekingForwardLandsOnTheFrameADirectRunProduces()
    {
        using CartHost host = LoadCounter(7);
        using CartHost reference = LoadCounter(7);

        byte[] expected = RunFresh(reference, 600);

        TimeMachine machine = Machine(host);
        machine.Advance(600, default);
        machine.SeekTo(300);
        machine.SeekTo(600);

        Assert.Equal(600, machine.Tick);
        Assert.Equal(expected, machine.Framebuffer.Pixels);
    }

    /// <summary>The ',' key: one tick back, and back again, with no drift.</summary>
    [Fact]
    public void SteppingBackUndoesExactlyOneTick()
    {
        using CartHost host = LoadCounter(7);
        using CartHost reference = LoadCounter(7);

        byte[] at198 = RunFresh(reference, 198);

        TimeMachine machine = Machine(host);
        machine.Advance(200, default);
        Assert.True(machine.StepBack());
        Assert.True(machine.StepBack());

        Assert.Equal(198, machine.Tick);
        Assert.Equal(at198, machine.Framebuffer.Pixels);
    }

    [Fact]
    public void SteppingBackFromTickZeroReportsThatItCannot()
    {
        using CartHost host = LoadCounter(7);
        TimeMachine machine = Machine(host);

        Assert.Equal(0, machine.Tick);
        Assert.False(machine.StepBack());
    }

    // --- acceptance item 5: continuation, the milestone's headline feature ---

    /// <summary>
    /// Edit the colour mid-game: the session must land on the frame the <em>new</em> build
    /// would have drawn at that same tick — same position, same counter, new colour — and not
    /// on the new build's tick 0.
    /// </summary>
    [Fact]
    public void RebuildingContinuesInTheSamePlaceWithTheNewCode()
    {
        using CartHost oldBuild = LoadCounter(7);
        using CartHost newBuild = LoadCounter(11);
        using CartHost reference = LoadCounter(11);

        byte[] newCodeAt300 = RunFresh(reference, 300);
        byte[] oldCodeAt300 = RunFresh(LoadCounter(7), 300);

        TimeMachine machine = Machine(oldBuild);
        machine.Advance(300, default);
        RebuildResult result = machine.Rebuild(newBuild.Cartridge);

        Assert.True(result.Success, result.Error?.ToString());
        Assert.Equal(300, result.Tick);
        Assert.Equal(300, machine.Tick);
        // The whole claim in two lines: it is the new build's frame...
        Assert.Equal(newCodeAt300, machine.Framebuffer.Pixels);
        // ...and it is not the old build's, so the colour really did change under us.
        Assert.NotEqual(oldCodeAt300, machine.Framebuffer.Pixels);
    }

    /// <summary>
    /// A rebuild must not land on tick 0 by accident, which is what a restart would look like
    /// to everything except this assertion.
    /// </summary>
    [Fact]
    public void RebuildingIsNotARestart()
    {
        using CartHost oldBuild = LoadCounter(7);
        using CartHost newBuild = LoadCounter(11);
        using CartHost reference = LoadCounter(11);

        byte[] newCodeAtZero = RunFresh(reference, 0);

        TimeMachine machine = Machine(oldBuild);
        machine.Advance(300, default);
        machine.Rebuild(newBuild.Cartridge);

        Assert.NotEqual(newCodeAtZero, machine.Framebuffer.Pixels);
    }

    /// <summary>
    /// New code that cannot survive the recorded past reports the tick that killed it — the
    /// only clue the author gets — instead of throwing into the shell's frame loop.
    /// </summary>
    [Fact]
    public void RebuildingReportsTheTickWhereTheNewCodeCrashed()
    {
        using CartHost oldBuild = LoadCounter(7);
        using CartHost broken = Load(CrashingCart);

        TimeMachine machine = Machine(oldBuild);
        machine.Advance(300, default);
        RebuildResult result = machine.Rebuild(broken.Cartridge);

        Assert.False(result.Success);
        Assert.Equal(150, result.Tick);
        Assert.IsType<InvalidOperationException>(result.Error);

        // And the session recovers: restart mode is a working session on the new code.
        machine.Restart();
        Assert.Equal(0, machine.Tick);
        machine.Advance(100, default);
        Assert.Equal(100, machine.Tick);
    }

    /// <summary>
    /// The shell's escape hatch when a rebuild fails: rebuild back onto the previous
    /// cartridge, whose load context is still alive.
    ///
    /// <para>Pinning the exact behaviour because it is one step short of what team A's handoff
    /// promises. <c>Rebuild</c> aims at <c>min(console.Tick, log.TickCount)</c>, and a failed
    /// rebuild leaves the console standing on the tick that crashed — so rebuilding back lands
    /// on <em>that</em> tick, not on where the player was. The log is untouched, so one
    /// <c>SeekTo</c> finishes the job; a shell that wants a true rollback has to do both.</para>
    /// </summary>
    [Fact]
    public void RebuildingBackOntoTheOldCartridgeNeedsASeekToFinishTheRollback()
    {
        using CartHost oldBuild = LoadCounter(7);
        using CartHost broken = Load(CrashingCart);
        using CartHost reference = LoadCounter(7);

        byte[] expected = RunFresh(reference, 300);

        TimeMachine machine = Machine(oldBuild);
        machine.Advance(300, default);
        Assert.False(machine.Rebuild(broken.Cartridge).Success);

        RebuildResult back = machine.Rebuild(oldBuild.Cartridge);
        Assert.True(back.Success, back.Error?.ToString());
        // Not 300: the failed rebuild left the console on tick 150, and that is what the
        // rollback aimed at. The recorded timeline is still 300 ticks long, though.
        Assert.Equal(150, machine.Tick);
        Assert.Equal(300, machine.Log.TickCount);

        machine.SeekTo(300);

        Assert.Equal(300, machine.Tick);
        Assert.Equal(expected, machine.Framebuffer.Pixels);
    }

    /// <summary>
    /// The trap this milestone hands every cartridge author, pinned so it cannot be discovered
    /// the hard way: a rewind calls <c>Init</c> again on the <b>same instance</b>, and nothing
    /// zeroes the cartridge's fields between runs. A field that <c>Init</c> forgets keeps the
    /// value the rewound run left in it, so the resimulation quietly stops reproducing.
    ///
    /// <para>This is a property of the design, not a defect to fix here — the core cannot reset
    /// fields it knows nothing about — but it belongs in API-8 next to "Init runs again when
    /// you rewind", and it is a strong candidate for a future analyzer rule.</para>
    /// </summary>
    [Fact]
    public void InitMustResetEveryFieldOrRewindDiverges()
    {
        const string ForgetfulCart = """
            using Quarp.Api;

            public sealed class ForgetfulCart : Cartridge
            {
                private int _step;

                public override void Init()
                {
                    // _step deliberately not reset — that is the whole point of this fixture.
                }

                public override void Update() => _step++;

                public override void Draw()
                {
                    Cls(0);
                    Pset(_step % 128, 40, 7);
                }
            }
            """;
        using CartHost host = Load(ForgetfulCart);
        using CartHost reference = Load(ForgetfulCart);

        byte[] expected = RunFresh(reference, 300);

        TimeMachine machine = Machine(host);
        machine.Advance(600, default);
        machine.SeekTo(300);

        Assert.Equal(300, machine.Tick);
        Assert.NotEqual(expected, machine.Framebuffer.Pixels);   // the divergence, in one line
    }

    // --- the replay file, from the angle the CI depends on ---

    /// <summary>
    /// The CI compares the sha256 of a <c>.qrpr</c> between architectures (REPLAY-FORMAT §6),
    /// which only proves anything if serializing a given log is a function of the log. Reading
    /// a file and writing it back must reproduce it byte for byte.
    /// </summary>
    [Fact]
    public void ReplayRoundTripsByteForByte()
    {
        using CartHost host = LoadCounter(7);

        byte[] identity = CartIdentity.Compute(new CartData
        {
            Manifest = new CartManifest { Name = "counter", Author = "", Profile = 8 },
            Sources = new[] { new CartSourceFile("src/main.cs", CounterCartFormat) },
            Gfx = new byte[CartData.GfxWidth * CartData.GfxHeight],
            Map = new byte[CartData.MapWidth * CartData.MapHeight],
            Flags = new byte[CartData.FlagCount],
        });
        var header = new ReplayHeader(identity, seed: 12345, stackalloc int[] { 7, -3, 0, 99 });

        var log = new ReplayLog();
        for (int tick = 0; tick < 500; tick++)
        {
            // Something that actually run-length encodes: long holds broken by short taps.
            byte mask = tick % 97 < 3 ? (byte)(1 << (int)Button.Right) : (byte)0;
            log.Record(tick, new InputState(mask, 0));
        }

        byte[] written = log.ToBytes(header);
        ReplayLog reread = ReplayLog.FromBytes(written, out ReplayHeader rereadHeader);
        byte[] rewritten = reread.ToBytes(rereadHeader);

        Assert.Equal(written, rewritten);
        Assert.Equal(log.TickCount, reread.TickCount);
        Assert.Equal(header.Seed, rereadHeader.Seed);
        Assert.True(rereadHeader.IdentityMatches(identity));
        for (int tick = 0; tick < log.TickCount; tick++)
        {
            Assert.Equal(log.InputAt(tick).Player0, reread.InputAt(tick).Player0);
        }
    }

    /// <summary>
    /// The acceptance list's "corrupt one byte and the hash must move": a replay that does not
    /// actually drive the simulation would survive the corruption unchanged.
    /// </summary>
    [Fact]
    public void CorruptingTheInputLogChangesTheOutcome()
    {
        const string InputCart = """
            using Quarp.Api;

            public sealed class InputCart : Cartridge
            {
                private int _x = 60;

                public override void Update()
                {
                    if (Btn(Button.Right)) { _x++; }
                    if (Btn(Button.Left)) { _x--; }
                }

                public override void Draw()
                {
                    Cls(0);
                    RectFill(_x % 120, 30, 8, 8, 7);
                }
            }
            """;
        using CartHost host = Load(InputCart);
        using CartHost reference = Load(InputCart);

        var log = new ReplayLog();
        for (int tick = 0; tick < 100; tick++)
        {
            log.Record(tick, new InputState((byte)(1 << (int)Button.Right), 0));
        }
        var header = new ReplayHeader(CartIdentity.Unknown, seed: 0, ReadOnlySpan<int>.Empty);

        byte[] bytes = log.ToBytes(header);
        var clean = new TimeMachine(
            ConsoleProfile.Profile8, host.Cartridge, header, ReplayLog.FromBytes(bytes, out _));
        clean.Boot();
        clean.ReplayForward(100);
        byte[] cleanFrame = Snapshot(clean);

        // One byte of the first run record: swap Right for Left. The button stream starts after
        // the header and its own stream header (REPLAY-FORMAT §2, §3), and +0 within a record is
        // player 0's mask.
        bytes[ReplayLog.HeaderSize + ReplayLog.StreamHeaderSize] = (byte)(1 << (int)Button.Left);
        var corrupted = new TimeMachine(
            ConsoleProfile.Profile8, reference.Cartridge, header, ReplayLog.FromBytes(bytes, out _));
        corrupted.Boot();
        corrupted.ReplayForward(100);

        Assert.NotEqual(cleanFrame, corrupted.Framebuffer.Pixels);
    }
}

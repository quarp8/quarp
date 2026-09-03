using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;
using Xunit.Abstractions;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>M9 stage 5a: the scrubber and the paused game's top band.</b> The owner looked at the
/// seven-row pause menu in a live window and cut it to three: the header went, the four rows that
/// moved the tick became one row with the tick between two arrows, and the top band the five
/// editor screens wear appeared over the paused frame.
///
/// <para><b>The measurement is the point of this file.</b> The owner's acceptance for the arrows
/// is a pair of numbers, not a feeling (Р5): a held arrow must carry the session from tick
/// 100 000 to tick 0 in no more than ten seconds, and no frame of that travel may take longer
/// than about a hundred milliseconds. Both are measured here, on a cartridge deliberately built
/// to cost a real cartridge's work per tick, through the production router — see
/// <see cref="AHeldArrowCrossesAHundredThousandTicksInsideTheOwnersBudget"/>, which prints what
/// it measured.</para>
///
/// <para><b>Why that is hard, in one paragraph, because it decides the whole design.</b> Going
/// backwards in this console is a cold boot and a resimulation from tick 0 (ADR-006 — no
/// snapshots in v1), so a backward move costs its <em>destination</em> and not its distance:
/// <c>quarp bench carts/breakout</c> reports 560 000 resimulated ticks a second, which makes one
/// step back from tick 100 000 cost about 180 milliseconds — eleven frames — while travelling all
/// the way to tick 0 from there costs nothing but the boot. So the aim
/// (<see cref="TickScrubber"/>) and the travel (<see cref="CartSession.ScrubTo"/>) are separate:
/// the aim moves every frame, and the session follows when it can afford to.</para>
/// </summary>
public class PauseScrubTests : IDisposable
{
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside every rectangle the layouts place — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    /// <summary>
    /// A cartridge that costs a <b>real</b> cartridge's work per resimulated tick. The inner loop
    /// is there on purpose and its size was measured, not chosen: at nine hundred iterations this
    /// cart resimulates at about 470 000 ticks a second on the machine this was written on, which
    /// is slightly <em>dearer</em> per tick than <c>carts/breakout</c> (561 000 a second, most of
    /// that cart's cost being the sound synthesis this one has none of). A toy cartridge would
    /// make the measurement below flatter — and the whole question is what happens when a tick is
    /// expensive: with this cart, one naive step back from tick 100 000 costs 212 ms.
    ///
    /// <para><c>_walk</c> makes the picture depend on every tick before it, so a rewind that
    /// silently skipped work would show.</para>
    /// </summary>
    private const string HeavyCartSource = """
        using Quarp.Api;

        public sealed class HeavyCart : Cartridge
        {
            private int _walk;
            private int _mix;

            public override void Init()
            {
                _walk = 0;
                _mix = 1;
            }

            public override void Update()
            {
                for (int i = 0; i < 900; i++)
                {
                    _mix = ((_mix * 1103515245) + 12345 + i) & 0x3FFFFFFF;
                }
                _walk = (_walk + 1) % 150;
            }

            public override void Draw()
            {
                Cls(0);
                RectFill(_walk, 40, 6, 6, 7);
            }
        }
        """;

    /// <summary>
    /// The same cartridge with a fatal tick in it: dear per tick like <see cref="HeavyCartSource"/>,
    /// and then <c>Update</c> throws on tick <see cref="CrashTick"/> and never runs again. This is
    /// the cartridge the scrubber exists for — "the game died, wind back and see why" — and it is
    /// also the one state in which every forward request is <b>declined</b>: a crashed tick leaves
    /// the cartridge half-updated, so <see cref="CartSession.JumpTicks"/> refuses to run the next
    /// one until a rewind has re-booted it (that refusal is M2's, not this stage's).
    /// </summary>
    private const string CrashCartSource = """
        using Quarp.Api;

        public sealed class CrashCart : Cartridge
        {
            private int _walk;
            private int _mix;

            public override void Init()
            {
                _walk = 0;
                _mix = 1;
            }

            public override void Update()
            {
                for (int i = 0; i < 900; i++)
                {
                    _mix = ((_mix * 1103515245) + 12345 + i) & 0x3FFFFFFF;
                }
                _walk = (_walk + 1) % 150;
                if (Ticks >= 100000)
                {
                    throw new System.InvalidOperationException("the tick the author rewinds to see");
                }
            }

            public override void Draw()
            {
                Cls(0);
                RectFill(_walk, 40, 6, 6, 7);
            }
        }
        """;

    /// <summary>The tick <see cref="CrashCartSource"/> dies on — deep, because depth is the price of a rewind.</summary>
    private const int CrashTick = 100_000;

    /// <summary>
    /// How much cheaper than the naive variant a frame of scrubbing has to be for this suite to
    /// call the mechanism present. Eight, against the forty-five the machine this was written on
    /// actually measures: the gate has to survive a machine of any speed and a Debug run under a
    /// coverage tool, and what it must never survive is the mechanism being gone — delete the
    /// budget guard in <see cref="CartSession.ScrubTo"/> and every frame IS the naive variant,
    /// which is a factor of one.
    /// </summary>
    private const double NaiveCommitFactor = 8.0;

    private readonly ITestOutputHelper _output;
    private readonly List<string> _roots = new();
    private readonly string _root;

    public PauseScrubTests(ITestOutputHelper output)
    {
        _output = output;
        _root = WriteCart("heavy", HeavyCartSource);
    }

    public void Dispose()
    {
        foreach (string root in _roots)
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A library of exactly one cartridge, in a temporary folder of its own. One folder per cart
    /// because <see cref="Playing"/> launches the library's <em>selected</em> entry, and a second
    /// entry beside the first would silently change which cartridge every other test in this file
    /// is measuring.
    /// </summary>
    private string WriteCart(string name, string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "quarp-scrub-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        string cart = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(cart, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(cart, "manifest.json"),
            "{\"name\":\"" + name + "\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(
            Path.Combine(cart, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName),
            source);
        return root;
    }

    // ==================================================================================
    // 1. Р5 — the numbers.
    // ==================================================================================

    /// <summary>
    /// <b>The owner's acceptance, measured.</b> A session is played to tick 100 000 (half an hour
    /// at 60 Hz), the menu is raised, and the left arrow is held down until the session stands on
    /// tick 0. Two numbers are taken and both are asserted: how long the arrow had to be held,
    /// and the longest single frame of the whole travel. Both are printed, so a run of this test
    /// is the report as well as the check.
    ///
    /// <para><b>The two clocks are different on purpose, and only one of them is a gate.</b> "How
    /// long did the author hold the arrow" is <em>console</em> time — the frames' own lengths, fed
    /// in — so it is a fact about the ramp and not about this machine, and ten seconds of it is
    /// asserted exactly as the owner wrote it. "How long did a frame take" is <em>wall</em> time,
    /// and wall time is the machine's: the first cut of this test demanded a hundred milliseconds
    /// a frame, which the 4 ms measured here clears by a factor of twenty-four and a Debug run
    /// under a coverage tool on a loaded CI box need not. So the frame gate is a <b>ratio</b>
    /// against the naive variant timed on the same machine in the same run
    /// (<see cref="NaiveCommitMs"/>): a frame of scrubbing must be at least
    /// <see cref="NaiveCommitFactor"/> times cheaper than committing the aim outright. That is a
    /// statement about the mechanism, and the mechanism is what the owner asked for. The absolute
    /// milliseconds are still printed, because they are the observation the ADR quotes.</para>
    ///
    /// <para><b>Break recipe (measured, not guessed).</b> Delete the affordability guard in
    /// <see cref="CartSession.ScrubTo"/> — commit every aim the moment it moves — and this goes
    /// red with <c>a frame of scrubbing cost 220.4 ms against 212.6 ms for one naive commit</c>,
    /// a factor of one, and the whole run takes a minute instead of a third of a second. Flatten
    /// the ramp instead (make <see cref="TickScrubber.RateAt"/> return the base rate) and it goes
    /// red on the tick: ten seconds of holding at one tick a frame leaves the session on 99 400.</para>
    /// </summary>
    [Fact]
    public void AHeldArrowCrossesAHundredThousandTicksInsideTheOwnersBudget()
    {
        const int start = 100_000;
        const double heldSecondsAllowed = 10.0;

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, start);
        Assert.Equal(start, session.Tick);
        double naiveMs = NaiveCommitMs(session);

        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);
        Assert.Equal(PauseMenuItem.Scrub, modes.PauseMenu.Current);

        var clock = new Stopwatch();
        double heldSeconds = 0;
        double worstFrameMs = 0;
        int frames = 0;
        var arrow = new[] { Keys.Left };
        while (session.Tick > 0 && heldSeconds <= heldSecondsAllowed)
        {
            clock.Restart();
            Frame(modes, keys, pointer, arrow);
            worstFrameMs = Math.Max(worstFrameMs, clock.Elapsed.TotalMilliseconds);
            heldSeconds += FrameSeconds;
            frames++;
        }

        _output.WriteLine(
            $"held {heldSeconds:F2} s ({frames} frames) to travel {start} -> {session.Tick}; "
            + $"worst frame {worstFrameMs:F1} ms against {naiveMs:F1} ms for one naive commit "
            + $"({naiveMs / Math.Max(worstFrameMs, 0.0001):F0}x)");

        Assert.Equal(0, session.Tick);
        Assert.True(
            heldSeconds <= heldSecondsAllowed,
            $"the arrow had to be held {heldSeconds:F2} s to reach tick 0 from {start}");
        Assert.True(
            worstFrameMs * NaiveCommitFactor <= naiveMs,
            $"a frame of scrubbing cost {worstFrameMs:F1} ms against {naiveMs:F1} ms for one naive "
            + $"commit at tick {start} — a factor of {naiveMs / Math.Max(worstFrameMs, 0.0001):F1}");
    }

    /// <summary>
    /// <b>The scenario the whole feature is for: the game died, wind back and see why.</b> A
    /// cartridge that throws at tick 100 000 is played into its crash, the right arrow is held for
    /// two seconds against the end of the recording — which a crashed cartridge cannot run past,
    /// so every one of those frames is a move that does not happen — and only then is the left
    /// arrow held. The rewind has to stay inside the frame budget.
    ///
    /// <para><b>Why this is a test and not a corner case.</b> The frame budget is spent against a
    /// measured price per resimulated tick, and that measurement used to be taken on the travel
    /// the frame <em>asked</em> for rather than the travel it made. A declined forward move
    /// returns in microseconds, so each of those 120 frames priced six hundred ticks at nothing
    /// and dragged the estimate down by a tenth; after a second of holding, the session believed
    /// a resimulated tick cost about a hundred-millionth of a millisecond, no backward move could
    /// ever fail the budget check again, and the window went back to spending a fifth of a second
    /// per frame — on precisely the cartridge state the author reaches by crashing.</para>
    ///
    /// <para><b>Break recipe.</b> Price the forward travel by the step requested instead of the
    /// distance moved (<c>RecordScrubCost(step, ...)</c> in <see cref="CartSession.ScrubTo"/>) and
    /// this goes red on the frame gate with about 190 ms against the same naive commit — a factor
    /// of one — because the estimate has been argued down to nothing.</para>
    /// </summary>
    [Fact]
    public void AHeldArrowOnACrashedCartridgeDoesNotMakeTheNextRewindFree()
    {
        ShellModeMachine modes = Playing(WriteCart("crashy", CrashCartSource));
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayToCrash(session, CrashTick);
        Assert.Equal("CRASHED", session.Status);
        Assert.Equal(CrashTick, session.Tick);
        Assert.Equal(session.LogTickCount, session.Tick);        // standing on the end of the recording
        double naiveMs = NaiveCommitMs(session);

        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);
        Assert.Equal(PauseMenuItem.Scrub, modes.PauseMenu.Current);

        // Two seconds of pushing against a wall: forward from a crashed tick is refused (M2's
        // "REWIND TO RECOVER"), so the session must not move a single tick here.
        var forward = new[] { Keys.Right };
        for (int frame = 0; frame < 120; frame++)
        {
            Frame(modes, keys, pointer, forward);
        }
        Assert.Equal(CrashTick, session.Tick);

        var clock = new Stopwatch();
        double heldSeconds = 0;
        double worstFrameMs = 0;
        int firstMove = -1;
        var back = new[] { Keys.Left };
        while (session.Tick > 0 && heldSeconds <= 20.0)
        {
            int stood = session.Tick;
            clock.Restart();
            Frame(modes, keys, pointer, back);
            worstFrameMs = Math.Max(worstFrameMs, clock.Elapsed.TotalMilliseconds);
            if (firstMove < 0 && session.Tick != stood)
            {
                firstMove = session.Tick;
            }
            heldSeconds += FrameSeconds;
        }

        _output.WriteLine(
            $"after 120 frames of a refused forward hold: rewound {CrashTick} -> {session.Tick} in "
            + $"{heldSeconds:F2} s, first commit at tick {firstMove}, worst frame {worstFrameMs:F1} ms "
            + $"against {naiveMs:F1} ms for one naive commit "
            + $"({naiveMs / Math.Max(worstFrameMs, 0.0001):F0}x)");

        Assert.Equal(0, session.Tick);
        // The sharp end of this test, and the one that does not depend on how fast the machine is:
        // the session still believes a resimulated tick costs what it costs, so the first move it
        // is willing to make is a cheap one, deep in the session rather than at its end. With the
        // estimate argued down to nothing by the refused forward hold, the first move is the very
        // first frame of the hold — a full resimulation from tick 0, at the crash tick.
        Assert.True(
            firstMove >= 0 && firstMove < CrashTick / 10,
            $"the first backward move of the hold landed on tick {firstMove}: the frame budget let a "
            + $"commit through at almost the full depth of the session ({CrashTick})");
        Assert.True(
            worstFrameMs * NaiveCommitFactor <= naiveMs,
            $"a frame of the rewind out of a crash cost {worstFrameMs:F1} ms against {naiveMs:F1} ms "
            + $"for one naive commit — a factor of {naiveMs / Math.Max(worstFrameMs, 0.0001):F1}");
    }

    /// <summary>
    /// The round trip: hold the left arrow to the start of the session, hold the right one back,
    /// and land on the very frame that was on screen. This is M9 stage 5's reversibility promise
    /// surviving the stage-5a controls — the thing the exact REWIND 60 / AHEAD 60 rows used to
    /// prove and an accelerating hold cannot promise by itself, because a hold does not stop on a
    /// number. What closes the last few ticks is therefore taps, which are exactly one tick each.
    ///
    /// <para>It also pins Р3 in passing: the forward hold is <b>allowed</b> to run past the end of
    /// the recording (the aim's ceiling is the tip plus a short lead), and what happens out there
    /// is fresh simulation with no buttons held, which extends the log. Stepping back into the
    /// recorded part then resimulates the recorded input and lands on the recorded frame.</para>
    ///
    /// <para><b>Break recipe.</b> Make <see cref="CartSession.ScrubTo"/>'s forward branch call
    /// <c>SeekTo</c> with the aim instead of going through <see cref="CartSession.JumpTicks"/> and
    /// the forward hold throws past the end of the log (<c>TimeMachine.SeekTo</c> refuses a tick
    /// the log does not hold, by its own contract). Drop the frame comparison's cart state — make
    /// <c>HeavyCart.Init</c> leave <c>_walk</c> alone — and the comparison stops meaning anything,
    /// which is why the fixture reseeds it.</para>
    /// </summary>
    [Fact]
    public void AHoldOutAndAHoldBackLandOnTheFrameItLeft()
    {
        const int start = 4_000;

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, start);
        byte[] frameAtStart = session.Framebuffer.Pixels.ToArray();

        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);

        Hold(modes, keys, pointer, Keys.Left, () => session.Tick == 0);
        Assert.Equal(0, session.Tick);
        Assert.NotEqual(frameAtStart, session.Framebuffer.Pixels);

        Hold(modes, keys, pointer, Keys.Right, () => session.Tick >= start);
        int overshoot = session.Tick - start;
        Assert.True(overshoot < TickScrubber.FreshLeadTicks, $"the hold overshot by {overshoot} ticks");
        for (int i = 0; i < overshoot; i++)
        {
            Tap(modes, keys, pointer, Keys.Left);
        }

        Assert.Equal(start, session.Tick);
        Assert.Equal(frameAtStart, session.Framebuffer.Pixels);
    }

    // ==================================================================================
    // 2. The ramp itself, as a model.
    // ==================================================================================

    /// <summary>
    /// The ramp, without a session: a tap is exactly one tick, holding accelerates, reversing
    /// starts the ramp over, and the aim stops at tick 0 rather than going negative. The first of
    /// those is the promise that took the STEP -1 / STEP +1 rows away without taking their
    /// distance away (Р2); the third is what keeps a three-second hold one way from becoming a
    /// three-second-fast jump the other way the instant a thumb slips.
    ///
    /// <para><b>Break recipes, all three measured — and two guesses that were wrong are named
    /// here rather than quietly dropped.</b> Make the press edge move two ticks
    /// (<c>direction * 2</c> in <see cref="TickScrubber.Frame"/>) and the tap assertion goes red
    /// with 998 for 999. Make <see cref="TickScrubber.RateAt"/> return the base rate and the
    /// acceleration assertion goes red on the first comparison ("second 1 covered 60 ticks, no
    /// more than the 60 before it"). Delete the <c>direction != _direction</c> branch and only the
    /// <b>reversal</b> assertion goes red, with 64 ticks instead of one.
    ///
    /// <para>The two that do <em>not</em> work, checked before being written down: raising
    /// <see cref="TickScrubber.BaseTicksPerSecond"/> leaves the tap alone (the press edge moves one
    /// tick by construction, not by rate), and raising
    /// <see cref="TickScrubber.DoublingSeconds"/> leaves this test alone as well — a slower ramp is
    /// still a growing one. What catches a ramp that is merely too slow is the measured
    /// acceptance above, and it does: with a flat rate it never reaches tick 0 at all.</para></para>
    /// </summary>
    [Fact]
    public void ATapIsOneTickAndAHoldAccelerates()
    {
        var scrubber = new TickScrubber();
        scrubber.Sync(1_000);

        // A tap: one frame down, one frame up.
        scrubber.Frame(-1, FrameSeconds, 1_000, 1_000);
        Assert.Equal(999, scrubber.Target);
        scrubber.Frame(0, FrameSeconds, 999, 1_000);
        Assert.Equal(999, scrubber.Target);

        // A hold from the same place: each successive second covers more than the one before it.
        scrubber.Sync(1_000_000);
        int lastSecond = 0;
        for (int second = 0; second < 4; second++)
        {
            int before = scrubber.Target;
            for (int frame = 0; frame < 60; frame++)
            {
                scrubber.Frame(+1, FrameSeconds, 1_000_000, int.MaxValue);
            }
            int covered = scrubber.Target - before;
            Assert.True(
                covered > lastSecond,
                $"second {second} covered {covered} ticks, no more than the {lastSecond} before it");
            lastSecond = covered;
        }

        // Reversing mid-hold starts the ramp over: after three seconds of one arrow, the first
        // frame of the other one moves exactly one tick and not three seconds' worth of speed.
        scrubber.Sync(1_000_000);
        for (int frame = 0; frame < 180; frame++)
        {
            scrubber.Frame(-1, FrameSeconds, 1_000_000, int.MaxValue);
        }
        int atReversal = scrubber.Target;
        scrubber.Frame(+1, FrameSeconds, atReversal, int.MaxValue);
        Assert.Equal(atReversal + 1, scrubber.Target);

        // And the aim stops at the start of the session rather than running past it.
        scrubber.Sync(10);
        for (int frame = 0; frame < 600; frame++)
        {
            scrubber.Frame(-1, FrameSeconds, 10, 10);
        }
        Assert.Equal(0, scrubber.Target);
    }

    /// <summary>
    /// Forward past the end of the recording is fresh simulation with no buttons held (Р3), and it
    /// runs at the cartridge's real speed — tens of times slower than a resimulation. So the aim
    /// is not allowed to accelerate away into a future nobody has computed: past the tip it leads
    /// the session by at most <see cref="TickScrubber.FreshLeadTicks"/>, and the ramp waits for
    /// the simulation instead of building a debt it would then spend minutes paying off.
    ///
    /// <para><b>Break recipe.</b> Clamp to <c>logTickCount</c> alone in
    /// <see cref="TickScrubber"/>'s <c>Clamp</c> and the aim stops dead at the tip — the forward
    /// assertion goes red because the session never moves past it. Drop the clamp entirely and the
    /// lead assertion goes red with an aim millions of ticks past anything simulated.</para>
    /// </summary>
    [Fact]
    public void TheAimNeverRunsAwayPastTheEndOfTheRecording()
    {
        var scrubber = new TickScrubber();
        int tick = 500;
        const int recorded = 500;
        for (int frame = 0; frame < 600; frame++)
        {
            scrubber.Frame(+1, FrameSeconds, tick, recorded);
            Assert.True(
                scrubber.Target - tick <= TickScrubber.FreshLeadTicks,
                $"the aim led the session by {scrubber.Target - tick} ticks past the recording");
            // The session simulates as much of the lead as it can, the way ScrubTo does.
            tick = Math.Min(scrubber.Target, tick + 40);
        }
        Assert.True(tick > recorded, "the aim never let the session leave the end of the recording");
    }

    // ==================================================================================
    // 3. The band over the paused frame.
    // ==================================================================================

    /// <summary>
    /// The band the owner asked for after seeing the window: while the game is paused, the exit
    /// arrow at the left, the cartridge's <b>name</b> beside it, and the six tabs off the right
    /// corner — the same band the five editor screens wear.
    ///
    /// <para>The name is checked from the ink, not from a string: the band is drawn twice, once
    /// with the real title and once with a blank one, and every pixel that differs must lie inside
    /// the band's own label field. So a title printed in the wrong place, or not printed at all,
    /// fails here.</para>
    ///
    /// <para><b>Break recipe.</b> Pass <c>""</c> instead of the title in
    /// <see cref="GameTabBar.Draw"/> and the ink assertion goes red (nothing differs between the
    /// two renders). Print it at <c>0, 0</c> instead of into the field and the containment
    /// assertion goes red, because those pixels land on the exit button.</para>
    /// </summary>
    [Fact]
    public void ThePausedBandCarriesTheExitArrowTheCartridgeNameAndSixTabs()
    {
        GameTabBar bar = GameTabBar.Compute(ConsoleWidth, ConsoleHeight);

        // The exit arrow hugs the top-left corner; the six tabs hug the right one.
        Assert.Equal(
            new Rectangle(0, 0, ConsoleChrome.ButtonSize, ConsoleChrome.ButtonSize),
            ConsoleChrome.ButtonRect(bar.Buttons, EditorButton.ExitTab));
        Assert.Equal(6, ConsoleChrome.RightTabs.Count);
        foreach (EditorButton tab in ConsoleChrome.RightTabs)
        {
            Rectangle rect = ConsoleChrome.ButtonRect(bar.Buttons, tab);
            Assert.True(bar.Holds(tab));
            Assert.True(rect.Right <= ConsoleWidth && rect.Y == 0);
            Assert.True(bar.TryButton(rect.Center.X, rect.Center.Y, out EditorButton hit));
            Assert.Equal(tab, hit);
        }

        var named = new ShellScreen();
        var blank = new ShellScreen();
        bar.Draw(named, "HEAVY", hover: null, tooltipVisible: false);
        bar.Draw(blank, "", hover: null, tooltipVisible: false);

        int different = 0;
        for (int y = 0; y < bar.Rows; y++)
        {
            for (int x = 0; x < ConsoleWidth; x++)
            {
                if (named.Console.Pget(x, y) == blank.Console.Pget(x, y))
                {
                    continue;
                }
                different++;
                Assert.True(
                    bar.Chrome.TooltipField.Contains(x, y),
                    $"the cartridge's name printed a pixel at {x},{y}, outside the band's label field");
            }
        }
        Assert.True(different > 0, "the cartridge's name printed no ink at all");
    }

    /// <summary>
    /// <b>The band is drawn over the frame, never into it.</b> The picture underneath is the
    /// cartridge's own framebuffer — the golden master <c>quarp sim</c> hashes and CI compares
    /// across architectures — so a band stamped into it would put every paused player outside
    /// everyone else's hashes, and a band that pushed the frame down would not be the frame the
    /// player was looking at when they pressed Esc.
    ///
    /// <para><b>It asks the production composition, not a copy of it.</b> The choice under test is
    /// exactly two objects — which surface the band is painted on and which surface the window
    /// presents — and that choice lives in <see cref="QuarpGame.ComposeGameScreen"/>, the half of
    /// <c>RenderFrame</c> that needs no graphics device. The first version of this test made both
    /// surfaces itself, drew the band on one of them and then compared the cartridge's frame with
    /// a copy of itself: it passed with the band's draw commented out, which is the one thing a
    /// test may never do. Here the test hands over a session and a shell screen and is <em>told</em>
    /// which surface went where.</para>
    ///
    /// <para><b>Break recipe.</b> In <c>ComposeGameScreen</c>, return the presented framebuffer as
    /// the band's surface and blit the band's rows into it — the regression this guards, spelled
    /// out — and three assertions go red at once: the two surfaces are the same object, the
    /// cartridge's frame has changed under the band, and its first eleven rows are no longer the
    /// pixels the player was looking at.</para>
    /// </summary>
    [Fact]
    public void ThePausedBandLeavesTheCartridgesOwnFrameUntouched()
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, 120);
        var shell = new ShellScreen();

        // While the game runs there is no band at all, and the cartridge's frame is the picture.
        GameScreenLayers playing = QuarpGame.ComposeGameScreen(modes, shell, null, false);
        Assert.Null(playing.Band);
        Assert.Null(playing.BandSurface);
        Assert.Same(session.Framebuffer, playing.Presented);

        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);
        byte[] paused = session.Framebuffer.Pixels.ToArray();

        GameScreenLayers layers = QuarpGame.ComposeGameScreen(modes, shell, null, false);

        // The window presents the cartridge's own framebuffer, the object itself: not a copy, not
        // the shell's console, and not a surface eleven rows shorter.
        Assert.Same(session.Framebuffer, layers.Presented);
        Assert.Same(session.Display, layers.Display);
        Assert.Equal(paused, session.Framebuffer.Pixels);

        // And the band was painted on a different surface entirely.
        Assert.NotNull(layers.BandSurface);
        Assert.NotSame(layers.Presented, layers.BandSurface);
        Assert.NotSame(layers.Presented.Pixels, layers.BandSurface!.Pixels);
        GameTabBar bar = layers.Band!.Value;
        Assert.Equal("heavy", modes.GameTitle);
        // The band did draw something on that surface, so "untouched" above means untouched
        // rather than "nothing happened anywhere".
        Assert.Contains(layers.BandSurface.Pixels.Take(ConsoleWidth * bar.Rows), pixel => pixel != 0);
    }

    // ==================================================================================
    // 4. Leaving the scrub row, and the one number the screen prints.
    // ==================================================================================

    /// <summary>
    /// <b>A road out of the menu commits the travel the author was holding.</b> From tick 4 000
    /// the left arrow is held long enough for the move to be deferred — the menu prints one tick,
    /// the session still stands on another — and then the author leaves: <c>Esc</c> back to the
    /// game, or <c>F2</c> to the code screen. Either way the session has to land on the number
    /// they were looking at.
    ///
    /// <para>This was live behaviour and not a hypothetical: <c>Esc</c> and the six tab keys both
    /// returned from the game screen's router before the scrubber ran, so the deferred move was
    /// dropped and the cartridge carried on from where the author had already scrubbed away from.
    /// The router's own comment claimed the opposite ("closes that hole by construction"), which
    /// is how it survived a review.</para>
    ///
    /// <para><b>Why the two rows expect different ticks.</b> <c>Esc</c> resumes the cartridge, and
    /// the frame that resumes it then runs its tick like any other frame the game is playing — so
    /// the session lands on the aim and immediately plays one tick from there, which is the whole
    /// claim: it carried on from the number the author was looking at. <c>F2</c> leaves the game
    /// screen, so no tick runs and the session stands exactly on the aim.</para>
    ///
    /// <para><b>Break recipe.</b> Delete the <c>CommitScrub()</c> call from
    /// <c>ShellModeMachine.ResumeFromPauseMenu</c> and the Esc row goes red (the session carries on
    /// from 4 000 while the menu printed 3 98x); delete it from <c>SwitchEditorTab</c> and the F2
    /// row goes red the same way.</para>
    /// </summary>
    [Theory]
    [InlineData(Keys.Escape, ShellMode.Game, 1)]
    [InlineData(Keys.F2, ShellMode.CodeEditor, 0)]
    public void LeavingTheScrubRowCommitsTheMoveTheMenuWasPrinting(
        Keys exit, ShellMode expected, int ticksAfter)
    {
        const int start = 4_000;

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, start);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);

        var arrow = new[] { Keys.Left };
        for (int frame = 0; frame < 10; frame++)
        {
            Frame(modes, keys, pointer, arrow);
        }

        int aim = modes.MenuTick!.Value;
        Assert.Equal(start, session.Tick);          // the move really is still waiting
        Assert.True(aim < start, $"the menu is printing {aim}, which is not a deferred move");

        Frame(modes, keys, pointer, new[] { exit });

        Assert.Equal(expected, modes.Mode);
        Assert.False(modes.PauseMenu.Shown);
        Assert.Equal(aim + ticksAfter, session.Tick);
    }

    /// <summary>
    /// <b>One tick, one owner.</b> While a backward move waits for a frame it fits in, the pause
    /// menu prints the aim and the session's status line prints — the same number. It used to
    /// print the session's own tick, so for up to five and a half seconds the console showed two
    /// different ticks on one screen, one of them in the menu the author was operating and the
    /// other three rows below it. The header <c>PAUSED  T 93</c> was deleted this very stage
    /// because "the tick is already on the status line"; that made the duplicate a disagreement
    /// instead of removing it.
    ///
    /// <para><b>Break recipe.</b> Put <c>Tick</c> back in place of <c>ShownTick</c> in
    /// <c>CartSession.RefreshStatus</c> and this goes red with <c>PAUSE 4000</c> against a menu
    /// printing 3 98x.</para>
    /// </summary>
    [Fact]
    public void TheMenuAndTheStatusLinePrintTheSameTick()
    {
        const int start = 4_000;

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, start);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);

        var arrow = new[] { Keys.Left };
        for (int frame = 0; frame < 10; frame++)
        {
            Frame(modes, keys, pointer, arrow);
            Assert.Equal($"PAUSE {modes.MenuTick}", session.Status);
        }

        // ...and the two really were free to disagree: the session has not moved at all yet.
        Assert.Equal(start, session.Tick);
        Assert.NotEqual(session.Tick, modes.MenuTick!.Value);
        Assert.Equal(modes.Scrub.Target, modes.MenuTick!.Value);

        // Once the arrow is let go and the session arrives, the same one number is the tick again.
        Frame(modes, keys, pointer, NoKeys);
        Assert.Equal(session.Tick, modes.MenuTick!.Value);
        Assert.Equal($"PAUSE {session.Tick}", session.Status);
    }

    /// <summary>
    /// A click chooses the row under the pointer even while an arrow <b>key</b> is held. The guard
    /// that stops a press on <c>&lt;</c> or <c>&gt;</c> from also activating the row it sits on
    /// used to ask "is anything scrubbing this frame", which is a question about the keyboard: an
    /// author holding Left and clicking RESUME with the other hand had the click swallowed, on
    /// every row of the menu. The question is where the pointer is.
    ///
    /// <para><b>Break recipe.</b> Change the condition back to <c>mouse.LeftPressed &amp;&amp;
    /// scrub == 0</c> and this goes red: the menu is still up.</para>
    /// </summary>
    [Fact]
    public void AClickChoosesItsRowWhileAnArrowKeyIsHeld()
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, 600);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);
        Assert.Equal(PauseMenuItem.Scrub, modes.PauseMenu.Current);

        Rectangle resume = modes.PauseMenu.ItemRect(0, ConsoleWidth, ConsoleHeight);
        Frame(
            modes, keys, pointer, new[] { Keys.Left },
            resume.Center.X, resume.Center.Y, ButtonState.Pressed);

        Assert.False(modes.PauseMenu.Shown);
        Assert.False(session.IsPaused);
    }

    /// <summary>
    /// The band's buttons work with the pointer, exactly as they do on the five editor screens
    /// (the owner's UX law: full keyboard and pointer parity). A click on a tab travels; a click
    /// on the exit arrow is the same door as the menu's EXIT row (Р1) — back to the library the
    /// author came from.
    ///
    /// <para><b>Break recipe.</b> Delete the <c>BandFrame</c> call from
    /// <c>GameScreenInput.MenuFrame</c> and both rows go red: the clicks reach nothing, the shell
    /// stays on the game screen. Route the exit arrow to <c>ReturnToLibrary</c> directly instead
    /// of to <c>LeaveGame</c> and the session would leave without being asked about unsaved work —
    /// which is the reason the two doors are one method and not two.</para>
    /// </summary>
    [Theory]
    [InlineData(EditorButton.CodeTab, ShellMode.CodeEditor)]
    [InlineData(EditorButton.SpritesTab, ShellMode.Editor)]
    [InlineData(EditorButton.ExitTab, ShellMode.Library)]
    public void ClickingTheBandTravelsTheSameWayItsKeyDoes(EditorButton button, ShellMode expected)
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();

        PlayTo(modes.Session!, 60);
        Tap(modes, keys, pointer, Keys.Escape);

        GameTabBar bar = GameTabBar.Compute(ConsoleWidth, ConsoleHeight);
        Rectangle rect = ConsoleChrome.ButtonRect(bar.Buttons, button);
        Frame(modes, keys, pointer, NoKeys, rect.Center.X, rect.Center.Y, ButtonState.Pressed);

        Assert.Equal(expected, modes.Mode);
        if (expected == ShellMode.Library)
        {
            Assert.Null(modes.Session);
        }
        else
        {
            Assert.NotNull(modes.Session);      // travelling to an editor keeps the cartridge alive
        }
    }

    // ==================================================================================
    // The harness: one frame of the shell, minus the window.
    // ==================================================================================

    /// <summary>A machine standing on a running game, reached the road an author walks.</summary>
    private ShellModeMachine Playing() => Playing(_root);

    /// <summary>The same, on a named library folder — the crashing cartridge lives in its own.</summary>
    private static ShellModeMachine Playing(string root)
    {
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        Assert.NotNull(machine.LaunchSelected());
        Assert.Equal(ShellMode.Game, machine.Mode);
        return machine;
    }

    /// <summary>
    /// Runs the session until the cartridge takes itself down, and reports where that left it.
    /// Stops on <see cref="CartSession.IsPaused"/> as well as on the tick, because a crash pauses
    /// and a loop that only watched the tick would spin forever against a stopped session.
    /// </summary>
    private static void PlayToCrash(CartSession session, int limit)
    {
        while (!session.IsPaused && session.Tick < limit)
        {
            session.Update(Math.Min(1_000, limit - session.Tick), default, rewinding: false);
        }
    }

    /// <summary>
    /// What one <b>naive</b> commit costs on this machine, at the depth the session is standing
    /// at: a single step back, which is a cold boot and a resimulation from tick 0 (ADR-006).
    /// That is the number a per-frame <c>SeekTo</c> would pay on every frame of a held arrow, so
    /// it is the number the measured acceptance below compares against — a ratio survives a
    /// machine twenty times slower, an absolute millisecond does not.
    ///
    /// <para>The session is put back where it was found, crash and all: stepping back re-boots
    /// and clears the crash flag, and stepping forward through the recorded log runs the fatal
    /// tick again, which crashes it again on the very tick it crashed on before.</para>
    /// </summary>
    private static double NaiveCommitMs(CartSession session)
    {
        int stood = session.Tick;
        session.JumpTicks(-1);              // untimed: the first seek of a run pays for the JIT
        session.JumpTicks(stood - session.Tick);
        Assert.Equal(stood, session.Tick);

        session.JumpTicks(-1);
        var clock = Stopwatch.StartNew();
        session.JumpTicks(-1);
        double ms = clock.Elapsed.TotalMilliseconds;
        session.JumpTicks(stood - session.Tick);
        Assert.Equal(stood, session.Tick);
        return ms;
    }

    /// <summary>
    /// Plays the session to a tick, in chunks rather than a frame at a time. A hundred thousand
    /// shell frames would measure the harness rather than the thing under test; the session's own
    /// Update is the same call the window makes, with a bigger budget.
    /// </summary>
    private static void PlayTo(CartSession session, int tick)
    {
        while (session.Tick < tick)
        {
            session.Update(Math.Min(1_000, tick - session.Tick), default, rewinding: false);
        }
    }

    /// <summary>Holds an arrow until a condition comes true, or gives up after twenty seconds of console time.</summary>
    private static void Hold(
        ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, Keys arrow,
        Func<bool> until)
    {
        var down = new[] { arrow };
        for (int frame = 0; frame < 20 * 60 && !until(); frame++)
        {
            Frame(modes, keys, pointer, down);
        }
        Frame(modes, keys, pointer, NoKeys);        // the release, which commits a deferred move
        Assert.True(until(), "the held arrow never got where it was going");
    }

    /// <summary>
    /// One frame of the game screen, minus the window — the same mirror of <c>QuarpGame.Update</c>
    /// the rest of this suite uses, cut down to the one mode these tests stand in.
    /// </summary>
    private static void Frame(
        ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer,
        Keys[] down, int mouseX = Off, int mouseY = Off, ButtonState left = ButtonState.Released)
    {
        modes.PollSessionReload();
        ShellCommands commands = keys.Read(new KeyboardState(down));
        EditorMouse mouse = pointer.Read(new MouseState(
            mouseX, mouseY, 0, left, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released));
        var shell = new EditorShell(
            modes, new ToolbarFlyout(), new IconHoverTracker(), new SheetScroll(),
            ConsoleWidth, ConsoleHeight);
        if (modes.Mode == ShellMode.Game)
        {
            GameScreenInput.Update(shell, commands, mouse, FrameSeconds);
            if (modes.Mode == ShellMode.Game && modes.Session is CartSession session)
            {
                session.Update(session.IsPaused ? 0 : 1, default, rewinding: false);
            }
        }
    }

    /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
    private static void Tap(
        ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, params Keys[] down)
    {
        Frame(modes, keys, pointer, down);
        Frame(modes, keys, pointer, NoKeys);
    }
}

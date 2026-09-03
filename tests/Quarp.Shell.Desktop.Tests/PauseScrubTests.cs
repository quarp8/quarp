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
/// <b>M9 stages 5a and 5b: the scrubber and the paused game's top band.</b> The owner looked at
/// the seven-row pause menu in a live window and cut it to three (5a): the header went, the four
/// rows that moved the tick became one row with the tick between two arrows, and the top band the
/// five editor screens wear appeared over the paused frame. Then he looked at <em>that</em> and
/// returned three more findings (5b), two of which were about numbers the organizer had invented
/// rather than been given — the ramp and the aim's ceiling.
///
/// <para><b>What this file pins, and what it stopped pinning.</b> The ramp is now the owner's own
/// table, dictated second by second (<see cref="TickScrubber"/>), and it is checked as a table:
/// <see cref="AHeldArrowFollowsTheOwnersRampSecondBySecond"/> asserts how far a hold has carried
/// the aim at every boundary in it, so substituting any one row goes red. The stage-5a acceptance
/// gate that went with the invented ramp — "100 000 ticks in under ten seconds" — is cancelled by
/// the owner's order: the same curve now needs about thirteen and a half seconds, and that is the
/// named price of being able to aim. What survives from 5a is the <b>frame</b> measurement, which
/// was never about the ramp — see
/// <see cref="AHeldRewindCostsFarLessPerFrameThanCommittingTheAim"/>, which prints what it
/// measured.</para>
///
/// <para><b>And what the ramp is for:</b>
/// <see cref="ASeriesOfSingleClicksLandsExactlyOnTheTickTheOwnerNamed"/> — the owner's own
/// example, tick 1164, reached by clicking, with no overshoot at all.</para>
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
    // 1. The frame budget — the half of Р5 that survived stage 5b.
    // ==================================================================================

    /// <summary>
    /// <b>No frame of a held rewind may cost what committing the aim would.</b> A session is
    /// played to tick 100 000 (half an hour at 60 Hz), the menu is raised, and the left arrow is
    /// held down until the session stands on tick 0. Two numbers are taken and printed — how long
    /// the arrow had to be held, and the longest single frame of the travel — and one of them is
    /// a gate.
    ///
    /// <para><b>Why the console-time number is printed and not asserted any more.</b> Stage 5a
    /// demanded "100 000 ticks in under ten seconds", and this test enforced it. That demand was
    /// part of the ramp the organizer invented, and the owner cancelled both in stage 5b: his own
    /// table spends its first three seconds moving three ticks, needs about thirteen and a half
    /// seconds to cross 100 000, and is right — a control that cannot be aimed is not made useful
    /// by being fast. The ramp is now checked as a table by
    /// <see cref="AHeldArrowFollowsTheOwnersRampSecondBySecond"/>; the number here is left in the
    /// output because it is the honest cost of that decision and the loop bound below is a
    /// runaway guard, not an acceptance.</para>
    ///
    /// <para><b>The gate is a ratio, and that is deliberate.</b> "How long did a frame take" is
    /// <em>wall</em> time, and wall time is the machine's: the first cut of this test demanded a
    /// hundred milliseconds a frame, which the ~4 ms measured here clears by a factor of
    /// twenty-four and a Debug run under a coverage tool on a loaded CI box need not. So the
    /// frame gate is measured against the naive variant timed on the same machine in the same run
    /// (<see cref="NaiveCommitMs"/>): a frame of scrubbing must be at least
    /// <see cref="NaiveCommitFactor"/> times cheaper than committing the aim outright. That is a
    /// statement about the mechanism, and the mechanism is what the owner asked for. The absolute
    /// milliseconds are still printed, because they are the observation the ADR quotes.</para>
    ///
    /// <para><b>Break recipe (run on the owner's ramp, not inherited from stage 5a).</b> Delete
    /// the affordability guard in <see cref="CartSession.ScrubTo"/> — commit every aim the moment
    /// it moves — and this goes red with <c>a frame of scrubbing cost 219.8 ms against 208.7 ms
    /// for one naive commit at tick 100000 — a factor of 0.9</c>, and the one test takes 83
    /// seconds instead of two.</para>
    /// </summary>
    [Fact]
    public void AHeldRewindCostsFarLessPerFrameThanCommittingTheAim()
    {
        const int start = 100_000;

        // Not a gate: the owner's ramp crosses 100 000 in about 13.5 s, and a hold that has not
        // arrived in twice that has stopped moving rather than moved slowly.
        const double runawaySeconds = 30.0;

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
        while (session.Tick > 0 && heldSeconds <= runawaySeconds)
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
            worstFrameMs * NaiveCommitFactor <= naiveMs,
            $"a frame of scrubbing cost {worstFrameMs:F1} ms against {naiveMs:F1} ms for one naive "
            + $"commit at tick {start} — a factor of {naiveMs / Math.Max(worstFrameMs, 0.0001):F1}");
    }

    /// <summary>
    /// <b>The scenario the whole feature is for: the game died, wind back and see why.</b> A
    /// cartridge that throws at tick 100 000 is played into its crash, the right arrow is held for
    /// two seconds against the end of the recording — where, since stage 5b, there is nothing to
    /// the right at all — and only then is the left arrow held. The session must not move a tick
    /// during the forward hold, and the rewind that follows has to stay inside the frame budget.
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
    /// <para><b>What stage 5b changed about this test, said plainly.</b> The aim's ceiling is now
    /// the end of the recording, so those 120 frames no longer ask the session for anything: the
    /// aim cannot leave the tip, <c>ScrubTo</c> is called with the tick it already stands on, and
    /// the poisoning path is closed a layer earlier than the guard that used to close it. The
    /// old break recipe here — price the forward travel by the step requested instead of the
    /// distance moved — therefore no longer turns this red, and saying so is the point: the guard
    /// in <c>ScrubToCore</c> is now belt to the ceiling's braces, and this test proves the
    /// braces. What still turns it red, run rather than reasoned about: restore the stage-5a
    /// ceiling and it fails with the menu printing 100 002 for a crashed session standing on
    /// 100 000 — an aim two ticks past a tick that will never run again. The other half is the
    /// frame gate: delete the affordability guard in <see cref="CartSession.ScrubTo"/>.</para>
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

        // Two seconds of pushing against a wall: the aim's ceiling is the end of the recording
        // and the session is standing on it, so there is nothing to the right — and behind that,
        // forward from a crashed tick would be refused anyway (M2's "REWIND TO RECOVER").
        var forward = new[] { Keys.Right };
        for (int frame = 0; frame < 120; frame++)
        {
            Frame(modes, keys, pointer, forward);
        }
        Assert.Equal(CrashTick, session.Tick);
        Assert.Equal(CrashTick, modes.MenuTick);        // and the menu is not printing a tick it cannot reach

        var clock = new Stopwatch();
        double heldSeconds = 0;
        double worstFrameMs = 0;
        int firstMove = -1;
        var back = new[] { Keys.Left };
        while (session.Tick > 0 && heldSeconds <= 30.0)
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
    /// prove and an accelerating hold could not promise by itself, because a hold does not stop
    /// on a number.
    ///
    /// <para><b>Since stage 5b it stops on one anyway, and that is the new part.</b> The way back
    /// used to overshoot into fresh simulation past the tip and needed taps to close the last few
    /// ticks; the owner's ceiling is the end of the recording, so the hold cannot leave the
    /// recorded part at all and comes to rest exactly on the tick the session was paused at. No
    /// taps, no overshoot, and the assertion is an equality rather than a tolerance.</para>
    ///
    /// <para><b>Break recipe.</b> Make <see cref="CartSession.ScrubTo"/>'s forward branch call
    /// <c>SeekTo</c> with the aim instead of going through <see cref="CartSession.JumpTicks"/> and
    /// the forward travel stops being a replay of the recorded input. Drop the frame comparison's
    /// cart state — make <c>HeavyCart.Init</c> leave <c>_walk</c> alone — and the comparison stops
    /// meaning anything, which is why the fixture reseeds it.</para>
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

        Assert.Equal(start, session.Tick);
        Assert.Equal(frameAtStart, session.Framebuffer.Pixels);
    }

    /// <summary>
    /// <b>The owner's own example, and the whole reason stage 5b exists: tick 1164.</b> He tried
    /// the stage-5a scrubber in the window and could not land on that tick — "промах 30–60 тиков
    /// за клик", because the invented ramp started at sixty ticks a second and a single frame of
    /// hold was already most of a second's worth of speed. Here the session is paused on tick
    /// 1200 and clicked back onto 1164, one click at a time, through the production router; the
    /// tick after every single click is asserted, so a click that moved anything other than one
    /// tick fails on the click that did it rather than at the end.
    ///
    /// <para>Then the same walk with the <b>pointer</b> on the row's <c>&gt;</c> arrow, because a
    /// click and a key press are one gesture by the owner's UX law (Р4) and "a click is one tick"
    /// has to be true of the thing actually called a click. It lands on 1185 — a second named
    /// number, and one on the other side of the first.</para>
    ///
    /// <para><b>Break recipe.</b> Make the press edge in <see cref="TickScrubber.Frame"/> move
    /// <c>direction * 2</c> and the first assertion goes red with 1198 for 1199. Take the edge
    /// branch out altogether — let a press be an ordinary frame of the ramp — and the clicks stop
    /// moving anything at all: at one tick a second, a two-frame click is a thirtieth of a tick,
    /// and the session never leaves 1200.</para>
    /// </summary>
    [Fact]
    public void ASeriesOfSingleClicksLandsExactlyOnTheTickTheOwnerNamed()
    {
        const int start = 1_200;
        const int owners = 1_164;
        const int andBack = 1_185;

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, start);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);
        Assert.Equal(PauseMenuItem.Scrub, modes.PauseMenu.Current);

        for (int clicks = 1; clicks <= start - owners; clicks++)
        {
            Tap(modes, keys, pointer, Keys.Left);
            Assert.Equal(start - clicks, session.Tick);
            Assert.Equal(session.Tick, modes.MenuTick);
        }
        Assert.Equal(owners, session.Tick);

        Rectangle arrow = modes.PauseMenu.ScrubArrowRect(+1, ConsoleWidth, ConsoleHeight);
        for (int clicks = 1; clicks <= andBack - owners; clicks++)
        {
            Click(modes, keys, pointer, arrow);
            Assert.Equal(owners + clicks, session.Tick);
        }
        Assert.Equal(andBack, session.Tick);
        Assert.Equal(andBack, modes.MenuTick);
    }

    // ==================================================================================
    // 2. The ramp itself, as a model.
    // ==================================================================================

    /// <summary>
    /// <b>The owner's ramp, dictated row by row, checked row by row.</b> Speed is ticks per
    /// second and time runs from the moment of the press: 0–3 s at 1, 3–5 at 5, 5–7 at 20, 7–8 at
    /// 50, 8–8.5 at 100, 8.5–9 at 200, and from there a doubling every half second with no
    /// ceiling. The table below is that dictation turned into distances — how far the aim has
    /// travelled by the end of each row, counting the one tick the press itself is worth — and
    /// nothing in it is computed from the class under test, which is the whole point: substitute
    /// any one speed and every row from that boundary on is wrong.
    ///
    /// <para><b>Why it is stated as distance and not as speed.</b> Speed is what the owner
    /// dictated, but distance is what the author sees, and the two are only the same claim if
    /// the shell integrates the ramp correctly — which is exactly where the previous version was
    /// wrong in a way nobody could see (it accumulated per frame, so the answer depended on the
    /// window's refresh rate). A distance table catches an integration bug and a substituted row
    /// with the same numbers.</para>
    ///
    /// <para><b>Break recipes, all four run rather than reasoned about.</b> Change the first row
    /// from 1 tick/s to 2 and eight of the nine rows go red, the 3 s one with 7 for 4; the row
    /// that stays green is the press itself, which the table does not own. Change the last tabled
    /// row (8.5–9 s) from 200 to 100 and the six rows below it stay green while the last three go
    /// red, the 9 s one with 204 for 254 — the test fails exactly where the change is. Move the
    /// doubling tail's period from half a second to a whole one and only the 10 s row goes red,
    /// with 654 for 854 (the first tail block is 400 ticks/s either way, so 9.5 s cannot see the
    /// difference and this row is what covers the tail). Make the press edge move two ticks and
    /// every row but the first is out by one, starting with 5 for 4.</para>
    /// </summary>
    [Theory]
    // held seconds, where the aim stands after being held that long from tick 0
    [InlineData(0.0, 1)]            // the press itself: one tick, and a click is exactly this
    [InlineData(3.0, 4)]            // three seconds at 1/s — the range an exact tick is set in
    [InlineData(5.0, 14)]           // + two seconds at 5/s
    [InlineData(7.0, 54)]           // + two at 20/s
    [InlineData(8.0, 104)]          // + one at 50/s
    [InlineData(8.5, 154)]          // + half at 100/s
    [InlineData(9.0, 254)]          // + half at 200/s — the last row of the table
    [InlineData(9.5, 454)]          // and from here the tail doubles every half second: 400/s
    [InlineData(10.0, 854)]         // 800/s
    public void AHeldArrowFollowsTheOwnersRampSecondBySecond(double heldSeconds, int expected)
    {
        var scrubber = new TickScrubber();
        scrubber.Sync(0);

        // The press edge, then one frame of the shell's own length per sixtieth of a second.
        scrubber.Frame(+1, FrameSeconds, int.MaxValue);
        int frames = (int)Math.Round(heldSeconds * 60.0);
        for (int frame = 0; frame < frames; frame++)
        {
            scrubber.Frame(+1, FrameSeconds, int.MaxValue);
        }

        Assert.Equal(expected, scrubber.Target);
    }

    /// <summary>
    /// The three promises the table above does not make: a tap is one tick and stays one tick
    /// after the arrow is let go, a reversal mid-hold starts the ramp over from one tick, and the
    /// aim stops at tick 0 rather than going negative. The first is what took the STEP -1 /
    /// STEP +1 rows away without taking their distance away (Р2); the second is what keeps a
    /// nine-second hold one way from becoming a nine-second-fast jump the other way the instant a
    /// thumb slips; the third is the floor of the timeline.
    ///
    /// <para><b>Break recipes, all run.</b> Make the press edge move two ticks
    /// (<c>direction * 2</c> in <see cref="TickScrubber.Frame"/>) and the tap assertion goes red
    /// with 998 for 999; take the whole <c>direction != _direction</c> branch out and it goes red
    /// the other way, with 1000 for 999 — at one tick a second a two-frame click covers a
    /// thirtieth of a tick, so without that branch a click moves nothing at all. Keep the branch
    /// but drop its two ramp resets and the tap stays green while the <b>second</b> of the
    /// reversal goes red with 1 000 347 for 999 748: the reversal inherits nine seconds of ramp
    /// and covers six hundred ticks in the second that should have covered one. Drop the lower
    /// bound in <c>Clamp</c> and the floor assertion goes red with -805 590.</para>
    /// </summary>
    [Fact]
    public void ATapIsOneTickAndAReversalStartsTheRampOver()
    {
        var scrubber = new TickScrubber();
        scrubber.Sync(1_000);

        // A tap: one frame down, one frame up. Letting go moves nothing more.
        scrubber.Frame(-1, FrameSeconds, 1_000);
        Assert.Equal(999, scrubber.Target);
        scrubber.Frame(0, FrameSeconds, 1_000);
        Assert.Equal(999, scrubber.Target);

        // Nine seconds one way, then the other arrow: exactly one tick, not nine seconds of speed.
        scrubber.Sync(1_000_000);
        scrubber.Frame(-1, FrameSeconds, int.MaxValue);      // the press edge, worth one tick
        for (int frame = 0; frame < 9 * 60; frame++)
        {
            scrubber.Frame(-1, FrameSeconds, int.MaxValue);
        }
        int atReversal = scrubber.Target;
        Assert.Equal(1_000_000 - 254, atReversal);           // the ramp table's 9-second row
        scrubber.Frame(+1, FrameSeconds, int.MaxValue);
        Assert.Equal(atReversal + 1, scrubber.Target);

        // And the second the reversal is the ramp's own first second, not its tenth: one tick,
        // the way the table's first row says. Asserting only the frame above would leave the
        // ramp free to carry on from where it was and fly on the very next frame.
        for (int frame = 0; frame < 60; frame++)
        {
            scrubber.Frame(+1, FrameSeconds, int.MaxValue);
        }
        Assert.Equal(atReversal + 2, scrubber.Target);

        // And the aim stops at the start of the session rather than running past it.
        scrubber.Sync(10);
        for (int frame = 0; frame < 15 * 60; frame++)
        {
            scrubber.Frame(-1, FrameSeconds, 10);
        }
        Assert.Equal(0, scrubber.Target);
    }

    /// <summary>
    /// <b>Forward stops at the end of the recording — the owner's stage-5b ruling.</b> Stage 5a
    /// let the aim lead the session past the tip of the log, where forward is not a seek at all
    /// but fresh simulation with no buttons held; the owner refused the idea outright: a pause is
    /// a look at what has already been played, not playing on blind. So the ceiling is the
    /// recording's own length, with nothing added to it.
    ///
    /// <para>The second half of the test is the other side of the same rule: when the recording
    /// <em>does</em> grow — the author's own <c>.</c> is the one road that grows it — the ceiling
    /// grows with it, because there is only one owner of where the timeline ends and this class
    /// is not it.</para>
    ///
    /// <para><b>Break recipe, run.</b> Put stage 5a's lead back — add the 600 to the ceiling in
    /// <see cref="TickScrubber"/>'s <c>Clamp</c> — and this goes red on its very first frame:
    /// <c>the aim reached 501, past a recording that ends at 500</c>.</para>
    /// </summary>
    [Fact]
    public void TheAimStopsAtTheEndOfTheRecording()
    {
        const int recorded = 500;
        var scrubber = new TickScrubber();
        scrubber.Sync(recorded);

        for (int frame = 0; frame < 15 * 60; frame++)
        {
            scrubber.Frame(+1, FrameSeconds, recorded);
            Assert.True(
                scrubber.Target <= recorded,
                $"the aim reached {scrubber.Target}, past a recording that ends at {recorded}");
        }
        Assert.Equal(recorded, scrubber.Target);

        // The recording grew — a step key ran one more tick — and the ceiling grew with it.
        scrubber.Frame(0, FrameSeconds, recorded + 1);
        scrubber.Frame(+1, FrameSeconds, recorded + 1);
        Assert.Equal(recorded + 1, scrubber.Target);
    }

    /// <summary>
    /// The same ruling on the production road, where it is a claim about the <b>simulation</b>
    /// and not about a number: a right arrow held for five seconds on a session paused at the end
    /// of its recording must not run a single tick of the cartridge. That is what "forward past
    /// the tip" used to do — the old <c>AHEAD</c> row's fresh simulation with no buttons held —
    /// and it is the half of the owner's complaint that a model test cannot see, because the
    /// damage is a log that grew while the author was looking at a paused frame.
    ///
    /// <para><b>Break recipe, run.</b> Restore the stage-5a ceiling (see
    /// <see cref="TheAimStopsAtTheEndOfTheRecording"/>) and this goes red with the session
    /// standing on tick 1213 — thirteen ticks of a game nobody played, simulated under a paused
    /// frame, and the recording thirteen ticks longer to match. Thirteen and not six hundred
    /// because the ramp spends five seconds crawling; the number is small and the defect is
    /// not.</para>
    /// </summary>
    [Fact]
    public void AHeldRightArrowAtTheEndOfTheRecordingSimulatesNothing()
    {
        const int start = 1_200;

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        PlayTo(session, start);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.Down);
        Assert.Equal(start, session.LogTickCount);

        var forward = new[] { Keys.Right };
        for (int frame = 0; frame < 5 * 60; frame++)
        {
            Frame(modes, keys, pointer, forward);
        }
        Frame(modes, keys, pointer, NoKeys);

        Assert.Equal(start, session.Tick);
        Assert.Equal(start, session.LogTickCount);
        Assert.Equal(start, modes.MenuTick);
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
    /// <b>Under the menu there is no status line, and outside it there still is.</b> Stage 5a put
    /// the tick on screen twice — between the scrub row's arrows, and again three rows lower as
    /// <c>PAUSE 4000</c> — and then spent a whole mechanism (a scrub aim published on the session,
    /// expiring by itself when the tick moved) on making the two copies agree. The owner's answer
    /// to a duplicate was to delete it: with a menu on screen the word "pause" and a second copy
    /// of the tick say nothing the menu is not already saying. Outside the menu the line is the
    /// only indicator there is — Space pauses without raising anything, the speed rungs print
    /// there — so it stays exactly as it has been since M2.
    ///
    /// <para><b>It asks the production composition</b> (<see cref="QuarpGame.ComposeGameScreen"/>),
    /// which is the half of the render that needs no graphics device and the one place that
    /// decides what the overlay is told. A test that asked <see cref="CartSession.Status"/>
    /// instead would be asking the wrong object: the session still formats its line, and the
    /// decision under test is whether anybody is shown it.</para>
    ///
    /// <para><b>Break recipe.</b> Return <c>session?.Status</c> from the paused branch of
    /// <c>ComposeGameScreen</c> — the stage-5a behaviour, restored — and the first assertion goes
    /// red with <c>PAUSE 600</c> for null. Return null unconditionally instead and the Space and
    /// speed rows go red, which is the other half: the owner asked for one line to go, not for
    /// the indicator to.</para>
    /// </summary>
    [Fact]
    public void TheStatusLineIsGoneUnderTheMenuAndStaysOutsideIt()
    {
        const int start = 600;

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;
        var shell = new ShellScreen();

        PlayTo(session, start);

        // Paused by Space, with no menu: the line is the only thing that says so.
        Tap(modes, keys, pointer, Keys.Space);
        Assert.True(session.IsPaused);
        Assert.False(modes.PauseMenu.Shown);
        Assert.Equal($"PAUSE {start}", QuarpGame.ComposeGameScreen(modes, shell, null, false).Status);

        // Esc raises the menu over the same paused frame, and the line goes.
        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);
        GameScreenLayers underMenu = QuarpGame.ComposeGameScreen(modes, shell, null, false);
        Assert.NotNull(underMenu.Band);
        Assert.Null(underMenu.Status);
        Assert.Equal(-1, underMenu.StatusPercent);

        // The session did not stop formatting it — nobody is being shown it.
        Assert.Equal($"PAUSE {start}", session.Status);
        Assert.Equal(start, modes.MenuTick);

        // Esc again lowers the menu and the game runs on; a speed rung has the line back.
        Tap(modes, keys, pointer, Keys.Escape);
        Assert.False(modes.PauseMenu.Shown);
        Assert.False(session.IsPaused);
        Tap(modes, keys, pointer, Keys.OemOpenBrackets);
        Assert.Equal("<< 0.5x", QuarpGame.ComposeGameScreen(modes, shell, null, false).Status);
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

    /// <summary>
    /// The pointer's version of <see cref="Tap"/>: the button goes down on a rectangle for one
    /// frame and up on the next, without the pointer moving. The release frame matters as much as
    /// the press — it is the one a deferred backward move is committed on.
    /// </summary>
    private static void Click(
        ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, Rectangle target)
    {
        Frame(
            modes, keys, pointer, NoKeys,
            target.Center.X, target.Center.Y, ButtonState.Pressed);
        Frame(
            modes, keys, pointer, NoKeys,
            target.Center.X, target.Center.Y, ButtonState.Released);
    }
}

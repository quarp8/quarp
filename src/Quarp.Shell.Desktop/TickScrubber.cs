namespace Quarp.Shell.Desktop;

/// <summary>
/// The pause menu's scrubber: where in time the author is <b>aiming</b> while an arrow is held,
/// and how fast that aim travels. A pure model — no session, no window, no clock of its own — so
/// the ramp is a function of held seconds and can be measured in a headless test instead of at a
/// window with a stopwatch.
///
/// <para><b>The ramp is the owner's, dictated after a live look at stage 5a's (M9 stage 5b,
/// 2026-09-02).</b> The first cut doubled a 60 ticks-a-second rate every half second, which the
/// organizer invented; the owner tried it in the window and named the failure exactly — "нельзя
/// попасть в тик 1164, промах 30–60 тиков за клик". A ramp that starts fast cannot be aimed, and
/// aiming is the entire reason this control exists. So the curve below is <b>precision first,
/// speed after</b>: the first three seconds of a hold buy three ticks, which is the range an
/// author sets an exact number in, and the speed only becomes a travelling speed once the hold
/// has clearly stopped being an adjustment. See <see cref="Ramp"/> for the table itself.</para>
///
/// <para><b>What this class deliberately does not know: what a move costs.</b> Travelling
/// backwards is a resimulation from tick 0 (ADR-006 — no snapshots in v1), so its price is the
/// <em>destination</em>, not the distance, and at tick 100 000 a single step back costs about
/// 180 ms on the machine this was measured on — eleven frames. Whether the session can afford to
/// follow the aim this frame is <see cref="CartSession.ScrubTo"/>'s judgement, and it is a
/// judgement worth stating here because it is the reason the two are separate: the aim must keep
/// moving at 60 Hz even on the frames the simulation cannot follow it, or the number under the
/// author's finger would freeze exactly when they asked to travel far.</para>
/// </summary>
public sealed class TickScrubber
{
    /// <summary>
    /// The owner's ramp, dictated (M9 stage 5b): from the second in the left column, a held
    /// arrow travels at the speed in the right one, in ticks per second. A press that is not
    /// held at all is one tick and does not consult this table at all — see
    /// <see cref="Frame"/>.
    ///
    /// <para>Read as intervals: 0–3 s at 1 tick/s, 3–5 at 5, 5–7 at 20, 7–8 at 50, 8–8.5 at 100,
    /// 8.5–9 at 200, and past <see cref="RunawayFromSeconds"/> the last speed doubles every
    /// <see cref="DoublingSeconds"/> for ever. The last two rows are already that doubling, which
    /// is why the tail continues them rather than starting a second rule.</para>
    ///
    /// <para><b>The named price of this shape.</b> Crossing 100 000 ticks now takes about
    /// thirteen and a half seconds of holding rather than the five and a half the invented ramp
    /// managed, and the owner accepted that trade explicitly when dictating the table: the
    /// control's job is to land on a tick, and a control that cannot land is not made useful by
    /// being fast. The stage-5a acceptance gate "100 000 ticks in under ten seconds" was
    /// cancelled with the ramp that inspired it.</para>
    /// </summary>
    private static readonly (double FromSeconds, double TicksPerSecond)[] Ramp =
    [
        (0.0, 1.0),
        (3.0, 5.0),
        (5.0, 20.0),
        (7.0, 50.0),
        (8.0, 100.0),
        (8.5, 200.0),
    ];

    /// <summary>Where the table ends and the doubling tail begins — the last row's own end.</summary>
    public const double RunawayFromSeconds = 9.0;

    /// <summary>How long the tail takes to double: half a second, the step the last two rows already are.</summary>
    public const double DoublingSeconds = 0.5;

    /// <summary>
    /// Slack for the floor below, in ticks. The distance a hold has covered is
    /// <see cref="TicksIn"/> — an integral of held time — and held time is a sum of frame
    /// lengths, so a boundary the table states exactly (three seconds) is reached as
    /// 2.999999999999 or 3.000000000001 depending on how the host chopped those seconds up.
    /// Ten seconds of sixtieths accumulate about a picosecond of that error, which at the 800
    /// ticks a second the ramp is doing by then is a billionth of a tick — so the slack is a
    /// millionth, a thousand times the worst drift and a millionth of anything anyone can see.
    /// What it buys is the property being claimed: how far a hold travels is a fact about how
    /// long it was held, not about the window's refresh rate.
    /// </summary>
    private const double BoundarySlackTicks = 1e-6;

    private double _held;
    private long _covered;
    private int _direction;

    /// <summary>The tick the author is aiming at — what the menu prints between its arrows.</summary>
    public int Target { get; private set; }

    /// <summary>
    /// True from the first frame an arrow moves the aim until the session has caught up with it
    /// and <see cref="Sync"/> is called. It is <b>not</b> the same question as
    /// "<see cref="Target"/> differs from the session's tick", and the difference is a bug that
    /// was found by a test rather than reasoned about: a hot reload whose new code cannot replay
    /// the recorded past falls back to a restart, which moves the session's tick with nobody
    /// touching a key. An aim inferred from that difference would then have travelled the session
    /// back to where the author used to be, on an idle frame, under an open menu.
    /// </summary>
    public bool Aiming { get; private set; }

    /// <summary>Which way the arrow is being held: -1 back, +1 forward, 0 for nothing.</summary>
    public int Direction => _direction;

    /// <summary>True while an arrow is down.</summary>
    public bool Holding => _direction != 0;

    /// <summary>How long the current hold has lasted, in seconds. Zero on the frame it began.</summary>
    public double HeldSeconds => _held;

    /// <summary>
    /// Ticks per second at a given depth into a hold: <see cref="Ramp"/> read as speed, and the
    /// only place the table's numbers are turned into one. Past the table the last row doubles
    /// every <see cref="DoublingSeconds"/> with no ceiling, which is the owner's "дальше —
    /// удвоение каждые полсекунды".
    /// </summary>
    public static double RateAt(double heldSeconds)
    {
        if (heldSeconds >= RunawayFromSeconds)
        {
            double doublings = Math.Floor((heldSeconds - RunawayFromSeconds) / DoublingSeconds) + 1.0;
            return Ramp[^1].TicksPerSecond * Math.Pow(2.0, doublings);
        }
        for (int i = Ramp.Length - 1; i > 0; i--)
        {
            if (heldSeconds >= Ramp[i].FromSeconds)
            {
                return Ramp[i].TicksPerSecond;
            }
        }
        return Ramp[0].TicksPerSecond;
    }

    /// <summary>
    /// How many ticks a hold of this length has covered — the area under <see cref="RateAt"/>,
    /// not counting the one tick the press itself is worth.
    ///
    /// <para><b>Why the distance is computed from the total held time and not accumulated per
    /// frame.</b> A carry that adds <c>rate * frame</c> every frame makes the distance depend on
    /// how the host sliced the hold: a 144 Hz window and a 60 Hz one would cross a ramp step at
    /// different moments and end up on different ticks. Here the frames only measure time, and
    /// the answer at a given second is the same on every machine — which is what lets a test pin
    /// the owner's table to exact numbers.</para>
    /// </summary>
    private static double TicksIn(double heldSeconds)
    {
        if (heldSeconds <= 0.0)
        {
            return 0.0;
        }
        double total = 0.0;
        for (int i = 0; i < Ramp.Length; i++)
        {
            double from = Ramp[i].FromSeconds;
            if (heldSeconds <= from)
            {
                return total;
            }
            double to = i + 1 < Ramp.Length ? Ramp[i + 1].FromSeconds : RunawayFromSeconds;
            total += (Math.Min(heldSeconds, to) - from) * RateAt(from);
        }
        // Past the table: the last speed doubles every half second, for ever. The loop stops
        // itself when doubling has run out of double — an author who holds an arrow for nine
        // minutes gets "as far as you like", not an overflow.
        for (double from = RunawayFromSeconds; from < heldSeconds; from += DoublingSeconds)
        {
            double rate = RateAt(from);
            if (double.IsInfinity(rate))
            {
                return double.PositiveInfinity;
            }
            total += (Math.Min(heldSeconds, from + DoublingSeconds) - from) * rate;
        }
        return total;
    }

    /// <summary>
    /// Puts the aim back on a known tick and forgets the hold — used when the menu goes up, and
    /// whenever the session moves by some other road (<c>,</c>, <c>Home</c>, held Backspace),
    /// because an aim left pointing at where the author used to be would fight the next arrow.
    /// </summary>
    public void Sync(int tick)
    {
        Target = tick;
        Aiming = false;
        _direction = 0;
        _held = 0;
        _covered = 0;
    }

    /// <summary>
    /// One frame of the scrubber. <paramref name="direction"/> is the arrow currently held
    /// (keyboard or pointer, they are the same gesture — Р4), <paramref name="seconds"/> the
    /// frame's own length, and <paramref name="logTickCount"/> the end of the recording, which
    /// is as far forward as this control goes — see <see cref="Clamp"/>.
    ///
    /// <para>Releasing does <b>not</b> reset the aim: the session may still be travelling to it,
    /// and dropping the target on release would strand it half way. The caller syncs when the
    /// two agree again — see <see cref="ShellModeMachine.ScrubFrame"/>.</para>
    /// </summary>
    public void Frame(int direction, double seconds, int logTickCount)
    {
        direction = Math.Sign(direction);
        if (direction == 0)
        {
            _direction = 0;
            _held = 0;
            _covered = 0;
            return;
        }
        if (direction != _direction)
        {
            // The press edge, and a reversal mid-hold, are the same event: exactly one tick from
            // the number on screen, and the ramp starts over. A reversal counts from the aim and
            // not from the session's tick, because the number the author is watching is the aim.
            // This is also the whole of the owner's "один клик — ровно 1 тик": a click is a press
            // edge and nothing else, so a series of clicks walks one tick at a time and lands on
            // the tick that was asked for.
            _direction = direction;
            _held = 0;
            _covered = 0;
            Aiming = true;
            Target = Clamp((long)Target + direction, logTickCount);
            return;
        }
        _held += seconds;
        // Clamped to an int's worth of ticks before it becomes one: the aim itself is an int and
        // the ceiling below is at most int.MaxValue, so a hold long enough to overflow a long
        // would otherwise wrap and send the aim the other way.
        long covered = (long)(Math.Min(TicksIn(_held), int.MaxValue) + BoundarySlackTicks);
        long step = covered - _covered;
        if (step <= 0)
        {
            return;         // slower than a tick a frame: the fraction is time, and time is kept
        }
        _covered = covered;
        Aiming = true;
        Target = Clamp((long)Target + (direction * step), logTickCount);
    }

    /// <summary>
    /// The two ends of the timeline: tick 0, and <b>the end of the recording</b>.
    ///
    /// <para><b>Forward stops at the tip, and that is the owner's rule (M9 stage 5b).</b> Stage 5a
    /// let the aim lead the session past the end of the log, where forward is not a seek at all
    /// but fresh simulation with no buttons held; the owner saw it and refused the whole idea —
    /// a pause is a look at what has already been played, not playing on blind. The one road
    /// that still extends the recording is the author's own deliberate <c>.</c>, and when it
    /// does, <paramref name="logTickCount"/> grows and this ceiling grows with it. So the tip has
    /// exactly one owner and this class is not it.</para>
    /// </summary>
    private static int Clamp(long target, int logTickCount) =>
        (int)Math.Clamp(target, 0L, Math.Max(0L, logTickCount));
}

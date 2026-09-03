namespace Quarp.Shell.Desktop;

/// <summary>
/// The pause menu's scrubber: where in time the author is <b>aiming</b> while an arrow is held,
/// and how fast that aim travels (M9 stage 5a). A pure model — no session, no window, no clock
/// of its own — so the ramp is a function of held seconds and can be measured in a headless
/// test instead of at a window with a stopwatch.
///
/// <para><b>Why an accelerating hold and not a bigger fixed step.</b> The first cut of this menu
/// offered <c>REWIND 60</c>: exact, repeatable, and seventeen presses to travel a thousand
/// ticks. The owner's requirement after seeing it (stage 5a, Р5) is a number, not a feel — a
/// held arrow must be able to cross 100 000 ticks in well under ten seconds — and no fixed step
/// can be both "one tick" for the author debugging a collision and "twenty thousand" for the
/// author who wants the start of the level. So the step is time: the rate doubles every
/// <see cref="DoublingSeconds"/>, starting at <see cref="BaseTicksPerSecond"/>, which makes a tap
/// exactly one tick (the <c>,</c> and <c>.</c> keys' own distance, Р2) and a five-second hold
/// about a hundred thousand.</para>
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
    /// Where the ramp starts: one console second of ticks per real second, which at 60 Hz is
    /// exactly one tick per frame. So the first frame of a hold — and therefore a tap — moves
    /// one tick, the same distance <c>,</c> and <c>.</c> move, and the author who wanted a
    /// single step gets a single step without having to let go quickly.
    /// </summary>
    public const double BaseTicksPerSecond = 60.0;

    /// <summary>
    /// How long the held rate takes to double. Half a second: five and a half seconds of holding
    /// then covers about 100 000 ticks (half an hour of play at 60 Hz), which is the owner's
    /// acceptance number with headroom, and ten seconds covers about 45 million — "as far as you
    /// like", which is the other half of the requirement.
    /// </summary>
    public const double DoublingSeconds = 0.5;

    /// <summary>
    /// How far the aim may lead the <b>session</b> once it is out past the recording. Forward
    /// beyond the recorded log is not a seek at all — it is fresh simulation with no buttons held
    /// (Р3), the old <c>AHEAD</c>'s own behaviour — and fresh simulation runs at the cartridge's
    /// real speed, tens of times slower than a resimulation. Letting the aim accelerate away into
    /// a future nobody has computed would build a debt the session then spends minutes paying
    /// off, so out there the aim leads by at most ten console seconds and the ramp waits for the
    /// simulation instead of the other way round.
    ///
    /// <para><b>Ten seconds ahead of the session, not ten seconds past the end of the
    /// recording</b> — see <see cref="Clamp"/>. The two read alike and are not the same rule: the
    /// debt this bounds is the work the session still owes, and the session is where it is, not
    /// at the tip. Measured from the tip, an aim would be allowed to lead by 600 ticks
    /// <em>plus</em> however far the session is currently behind the tip, which is exactly the
    /// unbounded debt the constant exists to forbid. The wording "конец записи + 600" in the
    /// stage 5a work order is therefore the description that is wrong, not the code.</para>
    /// </summary>
    public const int FreshLeadTicks = 600;

    private double _carry;
    private double _held;
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
    /// Ticks per second at a given depth into a hold. The whole ramp, in one expression, so
    /// nothing else in the shell can hold a second opinion about how fast a held arrow travels.
    /// </summary>
    public static double RateAt(double heldSeconds) =>
        BaseTicksPerSecond * Math.Pow(2.0, heldSeconds / DoublingSeconds);

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
        _carry = 0;
    }

    /// <summary>
    /// One frame of the scrubber. <paramref name="direction"/> is the arrow currently held
    /// (keyboard or pointer, they are the same gesture — Р4), <paramref name="seconds"/> the
    /// frame's own length, and the last two are where the session actually stands and how much
    /// of the timeline is recorded.
    ///
    /// <para>Releasing does <b>not</b> reset the aim: the session may still be travelling to it,
    /// and dropping the target on release would strand it half way. The caller syncs when the
    /// two agree again — see <see cref="ShellModeMachine.ScrubFrame"/>.</para>
    /// </summary>
    public void Frame(int direction, double seconds, int tick, int logTickCount)
    {
        direction = Math.Sign(direction);
        if (direction == 0)
        {
            _direction = 0;
            _held = 0;
            _carry = 0;
            return;
        }
        if (direction != _direction)
        {
            // The press edge, and a reversal mid-hold, are the same event: exactly one tick from
            // the number on screen, and the ramp starts over. A reversal counts from the aim and
            // not from the session's tick, because the number the author is watching is the aim.
            _direction = direction;
            _held = 0;
            _carry = 0;
            Aiming = true;
            Target = Clamp((long)Target + direction, tick, logTickCount);
            return;
        }
        _held += seconds;
        _carry += RateAt(_held) * seconds;
        long step = (long)_carry;
        if (step <= 0)
        {
            return;         // slower than a tick a frame: the fraction is banked, not truncated
        }
        _carry -= step;
        Aiming = true;
        Target = Clamp((long)Target + (direction * step), tick, logTickCount);
    }

    /// <summary>
    /// The two ends of the timeline: tick 0, and either the end of the recording or a short lead
    /// past wherever the session has actually reached — see <see cref="FreshLeadTicks"/>.
    /// </summary>
    private static int Clamp(long target, int tick, int logTickCount)
    {
        long ceiling = Math.Max(logTickCount, (long)tick + FreshLeadTicks);
        return (int)Math.Clamp(target, 0L, ceiling);
    }
}

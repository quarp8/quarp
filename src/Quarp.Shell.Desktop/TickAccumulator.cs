namespace Quarp.Shell.Desktop;

/// <summary>
/// The strict fixed-step accumulator that replaces MonoGame's <c>IsFixedTimeStep</c>
/// (ARCHITECTURE §4, M2 work order). It banks real time and hands back whole simulation
/// ticks; the caller draws exactly once per frame regardless of how many ticks came out.
///
/// <para>Why not the built-in fixed step: it catches a backlog up without limit, and on
/// hardware where a tick costs more than a tick's worth of wall clock that is a death
/// spiral — every frame falls further behind than the last, and the window stops
/// responding. Here the backlog is capped: whatever does not fit in one frame is written
/// off. An overloaded machine runs the game in slow motion, which is visible and
/// survivable; a frozen window is neither.</para>
///
/// <para><b>All integer arithmetic, deliberately.</b> Real elapsed time arrives in 100 ns
/// units (<see cref="TimeSpan.Ticks"/>) and the speed is an exact fraction, so the
/// accumulator carries its remainder without rounding: 60 ticks land in every real second
/// forever, with no drift accumulating from a truncated period. A period expressed as
/// <c>TimeSpan.TicksPerSecond / 60</c> would be 166666 instead of 166666.66…, which is a
/// tick lost every 25 minutes — small, and exactly the kind of small that shows up in a
/// golden-master project as an unexplained off-by-one.</para>
///
/// <para>Not a simulation type: it never touches the console and knows nothing about
/// determinism. What it produces — the tick count — is an input to the simulation, and the
/// simulation's own behaviour does not depend on how the ticks were paced.</para>
/// </summary>
public sealed class TickAccumulator
{
    /// <summary>Simulation ticks in one game second (SPEC-8 §7).</summary>
    public const int TicksPerSecond = 60;

    /// <summary>
    /// Ticks a frame may run <em>beyond its own quota</em> to work off a backlog. Five, per
    /// the work order. At x1 that is a budget of six ticks in a frame; at x8, thirteen.
    /// </summary>
    public const int MaxCatchUpTicks = 5;

    /// <summary>
    /// Longest real interval a single frame may bank. A frame gap longer than this is not a
    /// slow frame — it is a dragged window, a sleeping laptop or a debugger breakpoint — and
    /// banking it would only produce a backlog that the cap below discards anyway.
    /// </summary>
    private const long MaxFrameHundredNanoseconds = TimeSpan.TicksPerSecond / 4;

    /// <summary>
    /// Banked time, scaled by the current speed's numerator so the remainder stays exact.
    /// Units: 100 ns x <see cref="TicksPerSecond"/> x numerator.
    /// </summary>
    private long _accumulator;

    /// <summary>Ticks dropped since the last <see cref="Reset"/> because a frame ran out of budget.</summary>
    public long DroppedTicks { get; private set; }

    /// <summary>
    /// Forgets the banked remainder. Called whenever the mapping from real time to ticks
    /// changes under it — a speed change, a pause, a seek — because a remainder measured in
    /// one rung's units means nothing in another's, and carrying it over would produce a
    /// burst of ticks the player never asked for.
    /// </summary>
    public void Reset() => _accumulator = 0;

    /// <summary>
    /// Banks <paramref name="elapsedHundredNanoseconds"/> of real time and returns how many
    /// ticks are due now, never more than the frame's budget.
    /// </summary>
    public int Advance(long elapsedHundredNanoseconds, TimeSpeed speed)
    {
        if (elapsedHundredNanoseconds <= 0)
        {
            return 0;
        }
        if (elapsedHundredNanoseconds > MaxFrameHundredNanoseconds)
        {
            elapsedHundredNanoseconds = MaxFrameHundredNanoseconds;
        }

        _accumulator += elapsedHundredNanoseconds * TicksPerSecond * speed.Numerator;
        long period = TimeSpan.TicksPerSecond * speed.Denominator;
        long due = _accumulator / period;
        _accumulator -= due * period;

        int budget = speed.TicksPerFrame + MaxCatchUpTicks;
        if (due > budget)
        {
            // Write off the backlog instead of chasing it. Dropping the remainder too is
            // deliberate: keeping it would guarantee the next frame is over budget as well.
            DroppedTicks += due - budget;
            _accumulator = 0;
            return budget;
        }
        return (int)due;
    }
}

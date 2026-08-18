namespace Quarp.Shell.Desktop;

/// <summary>
/// One rung of the shell's speed ladder (ARCHITECTURE §4, API-8 §8):
/// x1/8, x1/4, x1/2, <b>x1</b>, x2, x4, x8.
///
/// A speed is a <em>tick frequency</em>, never a change to the simulation. At x8 the shell
/// hands the console eight ticks in one frame; at x1/4, one tick every fourth frame. The
/// cartridge still sees exactly 60 ticks per game second and cannot tell which rung is
/// selected — which is the only reason a replay recorded while fast-forwarding is still a
/// valid replay.
///
/// The ratio is kept as an exact integer fraction rather than a scale factor, so the
/// accumulator can stay in integer arithmetic: no float reaches the code that decides how
/// many ticks the simulation runs.
/// </summary>
public readonly struct TimeSpeed
{
    private static readonly TimeSpeed[] LadderRungs =
    {
        new(1, 8, "0.125x"),
        new(1, 4, "0.25x"),
        new(1, 2, "0.5x"),
        new(1, 1, "1x"),
        new(2, 1, "2x"),
        new(4, 1, "4x"),
        new(8, 1, "8x"),
    };

    private TimeSpeed(int numerator, int denominator, string label)
    {
        Numerator = numerator;
        Denominator = denominator;
        Label = label;
    }

    /// <summary>Every rung, slowest first.</summary>
    public static ReadOnlySpan<TimeSpeed> Ladder => LadderRungs;

    /// <summary>Index of x1 — where a session starts and where <c>Normal</c> puts it back.</summary>
    public static int NormalIndex => 3;

    /// <summary>Real time is multiplied by Numerator/Denominator to get simulation time.</summary>
    public int Numerator { get; }

    public int Denominator { get; }

    /// <summary>What the on-screen indicator shows: "0.25x", "1x", "8x".</summary>
    public string Label { get; }

    /// <summary>True for the default rung, the one that needs no indicator at all.</summary>
    public bool IsNormal => Numerator == 1 && Denominator == 1;

    /// <summary>
    /// Ticks this rung asks for in a single 60 Hz frame, at least one. This is the frame's own
    /// quota; the catch-up allowance in <see cref="TickAccumulator"/> is added on top of it, so
    /// that "at most 5 catch-up ticks" means the same thing at x1 and at x8.
    /// </summary>
    public int TicksPerFrame => Math.Max(1, Numerator / Denominator);

    /// <summary>The rung at <paramref name="index"/>, clamped into the ladder.</summary>
    public static TimeSpeed At(int index) => LadderRungs[Math.Clamp(index, 0, LadderRungs.Length - 1)];

    /// <summary>Clamps a ladder index — what the <c>[</c> and <c>]</c> keys produce.</summary>
    public static int ClampIndex(int index) => Math.Clamp(index, 0, LadderRungs.Length - 1);
}

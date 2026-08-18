namespace Quarp.Shell.Desktop;

/// <summary>
/// The whole of the shell's audio policy, as two arithmetic questions about one number —
/// how many blocks the device still holds.
///
/// <para>It lives apart from <see cref="AudioOutput"/> because it is the part that can be
/// wrong quietly. Whether a byte array reaches OpenAL is loud: either sound comes out or it
/// does not. Whether the right <em>blocks</em> reach it at ×8, at ×1/8 and while paused is a
/// question about ratios that a listener would call "sounds a bit odd" and never report, so
/// it is answered here, in code with no device attached, and pinned by
/// <c>AudioQueueTests</c> against a modelled device.</para>
///
/// <para>The two numbers are the M3 work order's "queue of 2-3 blocks", read literally: pad
/// up to <see cref="Target"/>, never exceed <see cref="Max"/>. At 800 samples and 48 kHz a
/// block is 16.667 ms, so the queue holds 33 ms and never more than 50.</para>
/// </summary>
public static class AudioQueue
{
    /// <summary>Blocks the queue is topped up to when the simulation did not supply enough: 2.</summary>
    public const int Target = 2;

    /// <summary>Blocks never exceeded, so a stall or a fast-forward cannot grow latency: 3.</summary>
    public const int Max = 3;

    /// <summary>One block of samples as wall time: 16.667 ms.</summary>
    public const double BlockMilliseconds = 1000.0 / Quarp.Core.Audio.AudioBlock.TicksPerSecond;

    /// <summary>
    /// True when a freshly rendered block should be handed to the device. False means drop
    /// it: the device cannot drain faster than one block per 16.667 ms of wall time, and a
    /// fast-forward produces up to eight in that time. Dropping the surplus is what makes ×8
    /// sound like the soundtrack sampled rather than like the queue running minutes behind
    /// the picture.
    /// </summary>
    public static bool HasRoom(int pending) => pending < Max;

    /// <summary>
    /// Blocks of silence to append at the end of a frame. Non-zero when the simulation
    /// produced fewer blocks than the device ate — slow motion, a pause, a rewind, or a
    /// machine that missed its frame — and zero the rest of the time.
    /// </summary>
    public static int PadNeeded(int pending) => pending >= Target ? 0 : Target - pending;
}

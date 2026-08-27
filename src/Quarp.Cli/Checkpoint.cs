using Quarp.Core;

namespace Quarp.Cli;

/// <summary>
/// The <em>checkpoint</em> line every headless command prints when asked for one:
/// <c>tick &lt;n&gt; &lt;frame&gt; &lt;audio&gt;</c>, for example
/// <c>tick 300 53d26aa6d246a3b1 9f2c0f1a44b7e3d5</c>.
///
/// <para><b>M3 added the third column.</b> A tick produces a frame and 800 samples, and the
/// milestone's claim is that both are bit-identical across architectures — so both are
/// compared, on the same line, naming the same tick. The two columns are not the same kind of
/// measurement and are not meant to be: the frame hash is that tick's frame, the audio hash is
/// every sample the run has produced so far (<see cref="FrameHash.Combine(ulong, AudioBlock)"/>
/// says why). At <c>--every 20</c> the frame column therefore checks one frame in twenty and
/// the audio column checks all twenty blocks; anything less would leave 95% of the PCM
/// compared against nothing while the report claimed sound was covered.</para>
///
/// <para><b>Why a single final hash is not enough.</b> A run's last frame answers "did these
/// two machines end up in the same place", and nothing more. It cannot tell "identical the
/// whole way" from "diverged at tick 40 and converged again", and for a cartridge that
/// reaches a terminal screen it barely asks anything at all: carts/snake left alone walks
/// into a wall around tick 88, after which every run of 100, 300 or 600 ticks prints the same
/// hash. A sequence of checkpoints is the difference between "the frames agree" and "the
/// frames agree, here is where they would have stopped agreeing".</para>
///
/// <para><b>The shape is a contract with <c>.github/workflows/ci.yml</c></b>, which lifts the
/// sequence out of stdout with <c>^tick [0-9]+ [0-9a-f]{16} [0-9a-f]{16}$</c> and compares the
/// whole block between windows-x64 and linux-arm64. Two properties matter and neither is
/// negotiable: the tick number is the tick the frame belongs to (so a mismatch names the tick),
/// and a checkpoint never matches <c>^[0-9a-f]{16}$</c> — the bare 16-digit line stays reserved
/// for the final frame hash, which is what every M1/M2 consumer already greps for. The final
/// audio hash gets a labelled line of its own, <see cref="AudioLine"/>, for the same reason:
/// there is exactly one bare-hash line in a run's output and it means the frame.</para>
/// </summary>
public static class Checkpoint
{
    /// <summary>The prefix of the final audio line: <c>audio &lt;hash&gt;</c>.</summary>
    public const string AudioPrefix = "audio";

    /// <summary>The prefix of the final output-state line: <c>display &lt;hash&gt;</c>.</summary>
    public const string DisplayPrefix = "display";

    /// <summary>
    /// Formats the checkpoint line for <paramref name="tick"/>: that tick's frame, then the
    /// running audio digest over every block up to and including it.
    /// </summary>
    public static string Line(int tick, Framebuffer framebuffer, ulong audioDigest) =>
        $"tick {tick} {FrameHash.Of(framebuffer)} {FrameHash.Format(audioDigest)}";

    /// <summary>
    /// The run's final audio line, <c>audio &lt;hash&gt;</c> — labelled rather than bare so it
    /// cannot be mistaken for the final frame hash by anything grepping
    /// <c>^[0-9a-f]{16}$</c>.
    /// </summary>
    public static string AudioLine(ulong audioDigest) =>
        $"{AudioPrefix} {FrameHash.Format(audioDigest)}";

    /// <summary>
    /// The run's final output-state line, <c>display &lt;hash&gt;</c> — labelled for the same
    /// reason as the audio one. It answers "how was all of that coloured", which the frame
    /// hash deliberately does not: <c>Pald</c> and <c>Palr</c> never touch the index buffer, so
    /// a fade, a hit flash or a whole screen tinted red leaves every frame hash unmoved. Before
    /// this line existed those effects were unpinnable, and a port shipped two of them that no
    /// golden could have caught breaking.
    /// </summary>
    public static string DisplayLine(ulong displayDigest) =>
        $"{DisplayPrefix} {FrameHash.Format(displayDigest)}";

    /// <summary>
    /// True when a checkpoint is due after <paramref name="tick"/>: every
    /// <paramref name="every"/> ticks, and always on <paramref name="lastTick"/> so the
    /// sequence ends on the frame the final bare hash belongs to. <paramref name="every"/>
    /// of zero disables checkpoints entirely, which is the default and the M1 behaviour.
    /// </summary>
    public static bool IsDue(int tick, int every, int lastTick) =>
        every > 0 && (tick % every == 0 || tick == lastTick);
}

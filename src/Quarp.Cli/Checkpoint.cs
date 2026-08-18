using Quarp.Core;

namespace Quarp.Cli;

/// <summary>
/// The <em>checkpoint</em> line every headless command prints when asked for one:
/// <c>tick &lt;n&gt; &lt;hash&gt;</c>, for example <c>tick 300 53d26aa6d246a3b1</c>.
///
/// <para><b>Why a single final hash is not enough.</b> A run's last frame answers "did these
/// two machines end up in the same place", and nothing more. It cannot tell "identical the
/// whole way" from "diverged at tick 40 and converged again", and for a cartridge that
/// reaches a terminal screen it barely asks anything at all: carts/snake left alone walks
/// into a wall around tick 64, after which every run of 100, 300 or 600 ticks prints the same
/// hash. A sequence of checkpoints is the difference between "the frames agree" and "the
/// frames agree, here is where they would have stopped agreeing".</para>
///
/// <para><b>The shape is a contract with <c>.github/workflows/ci.yml</c></b>, which lifts the
/// sequence out of stdout with <c>^tick [0-9]+ [0-9a-f]{16}$</c> and compares the whole block
/// between windows-x64 and linux-arm64. Two properties matter and neither is negotiable:
/// the tick number is the tick the frame belongs to (so a mismatch names the tick), and a
/// checkpoint never matches <c>^[0-9a-f]{16}$</c> — the bare 16-digit line stays reserved for
/// the final hash, which is what every M1/M2 consumer already greps for.</para>
/// </summary>
public static class Checkpoint
{
    /// <summary>Formats the checkpoint line for a frame at <paramref name="tick"/>.</summary>
    public static string Line(int tick, Framebuffer framebuffer) =>
        $"tick {tick} {FrameHash.Of(framebuffer)}";

    /// <summary>
    /// True when a checkpoint is due after <paramref name="tick"/>: every
    /// <paramref name="every"/> ticks, and always on <paramref name="lastTick"/> so the
    /// sequence ends on the frame the final bare hash belongs to. <paramref name="every"/>
    /// of zero disables checkpoints entirely, which is the default and the M1 behaviour.
    /// </summary>
    public static bool IsDue(int tick, int every, int lastTick) =>
        every > 0 && (tick % every == 0 || tick == lastTick);
}

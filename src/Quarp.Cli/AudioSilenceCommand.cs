using System.Globalization;
using Quarp.Core;
using Quarp.Core.Audio;

namespace Quarp.Cli;

/// <summary>
/// <c>quarp audio silence --ticks N</c> — prints the PCM digest a run of <c>N</c> ticks
/// produces when nothing ever sounds, as the same labelled <c>audio &lt;hash&gt;</c> line
/// <c>quarp sim</c>, <c>quarp replay record</c> and <c>quarp replay play</c> end their runs with.
///
/// <para><b>What it is for.</b> The determinism job in <c>.github/workflows/ci.yml</c> asserts
/// that the cartridge is not mute, and the only way to state "not mute" about a cumulative
/// digest is to compare it with the digest of the same number of <em>silent</em> blocks. A
/// cumulative digest changes on every tick even in dead silence, so "all checkpoints are
/// distinct" — the guard that keeps the frame column honest — proves exactly nothing about
/// sound (docs/REPLAY-FORMAT.md §6).</para>
///
/// <para><b>Why this is a command rather than two constants in the workflow.</b> The silent
/// digest is a function of the tick count and of nothing else, so it is tempting to paste it
/// into <c>env:</c> and never think about it again — which is precisely how it rots. Written
/// down, it is correct for one value of <c>GOLDEN_TICKS</c> and one value of <c>SIM_TICKS</c>;
/// the day either number moves, the constant stays behind, the comparison becomes "two
/// unrelated hashes are not equal", and that comparison passes forever, including on the day
/// the banks stop reaching the console. Computed, the two values move with the tick counts by
/// construction and there is nothing left to keep in sync by discipline.</para>
///
/// <para><b>And why it runs the chip instead of evaluating a closed form.</b> FNV-1a over
/// <c>N × 800</c> zero samples has a one-line closed form
/// (<c>offsetBasis × prime^(1600·N) mod 2⁶⁴</c>), and writing that line here would produce a
/// second implementation of the digest — one that agrees with <see cref="FrameHash"/> until the
/// day it does not, and whose disagreement would surface as "the cartridge went mute" or, worse,
/// as "the cartridge is fine" while it is not. So this runs the real <see cref="Apu"/>, folds
/// its real <see cref="AudioBlock"/> through the real
/// <see cref="FrameHash.Combine(ulong, AudioBlock)"/>, and differs from a real run in one
/// respect only: nothing ever asks it to play. If silence ever stops being 800 zero samples,
/// this command changes its answer along with every run in the console, which is the whole
/// point of computing it here.</para>
///
/// <para><b>Naming.</b> <c>silence</c> is the noun, not the imperative — it names the thing
/// being measured, the way <c>quarp pattern</c> names the test pattern it writes. It reads
/// oddly next to the verbs (<c>audio build</c>, <c>replay record</c>, <c>replay play</c>), and
/// the alternative <c>audio silence-digest</c> was rejected as the only unambiguous verb-free
/// form worth its length: nothing in this tool mutates the chip outside a running cartridge, so
/// "silence the audio" is not a thing a reader can act on by mistake.</para>
/// </summary>
public static class AudioSilenceCommand
{
    private const string Usage = "usage: quarp audio silence --ticks N";

    /// <summary>
    /// Entry point for the <c>silence</c> subcommand of the <c>audio</c> group;
    /// <paramref name="args"/> starts at the subcommand itself, exactly like
    /// <see cref="AudioBuildCommand.Invoke"/>, so the group's dispatcher in
    /// <c>Program.cs</c> stays a one-line choice between two commands that each own their
    /// arguments, their error text and their exit codes.
    /// </summary>
    public static int Invoke(string[] args)
    {
        if (args.Length == 0 || args[0] != "silence")
        {
            Console.Error.WriteLine("quarp audio: expected the 'silence' subcommand.");
            Console.Error.WriteLine("  " + Usage);
            return 1;
        }

        // --ticks is required and deliberately has no default. Every other headless command can
        // afford one, because a missing count there costs a wrong measurement you can see; here
        // it would cost a confidently printed digest for a tick count nobody asked about, and a
        // caller comparing against it would get a green result out of a mute cartridge.
        int ticks = -1;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] != "--ticks")
            {
                Console.Error.WriteLine($"quarp audio silence: unknown argument '{args[i]}' ({Usage})");
                return 1;
            }
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"quarp audio silence: --ticks needs a value ({Usage})");
                return 1;
            }
            // NumberStyles.None on purpose: it rejects a sign, whitespace and separators, so
            // `--ticks -5` and `--ticks 1,000` fail here with a message about the value instead
            // of being read as something else. Invariant culture for the same reason FrameHash
            // pins it — the answer must not depend on the machine that asked.
            if (!int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                Console.Error.WriteLine(
                    $"quarp audio silence: --ticks wants a non-negative whole number, got '{args[i + 1]}' ({Usage})");
                return 1;
            }
            ticks = parsed;
            i++;
        }
        if (ticks < 0)
        {
            Console.Error.WriteLine($"quarp audio silence: --ticks N is required ({Usage})");
            return 1;
        }

        // The labelled line, never a bare hash: across this whole tool exactly one line of
        // output matches ^[0-9a-f]{16}$ and it means a *framebuffer* hash (Checkpoint,
        // FrameHash, REPLAY-FORMAT §6). A silent PCM digest is not a frame hash, so it wears
        // the same "audio " label a real run's digest wears — and CI lifts it out of stdout
        // with the very same grep it already uses on `quarp sim`.
        Console.WriteLine(Checkpoint.AudioLine(Digest(ticks)));
        return 0;
    }

    /// <summary>
    /// The running PCM digest of <paramref name="ticks"/> ticks during which nothing plays:
    /// a freshly constructed <see cref="Apu"/> — no bank, no cartridge, every channel idle —
    /// rendered one tick at a time and folded into the digest exactly as <c>quarp sim</c> folds
    /// a real run's blocks (<c>Program.RunSim</c>: seed the digest with
    /// <see cref="FrameHash.Empty"/>, then one <see cref="FrameHash.Combine(ulong, AudioBlock)"/>
    /// per tick after the chip has rendered it). Returns <see cref="FrameHash.Empty"/> for zero
    /// ticks, which is the one answer here that is a property of the hash rather than of the
    /// console — the workflow uses it as a rot-proof anchor on this command.
    /// </summary>
    public static ulong Digest(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        var apu = new Apu();
        ulong digest = FrameHash.Empty;
        for (int i = 0; i < ticks; i++)
        {
            apu.RenderTick();
            digest = FrameHash.Combine(digest, apu.Block);
        }
        return digest;
    }
}

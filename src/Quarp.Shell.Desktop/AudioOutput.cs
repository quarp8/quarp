using System.Diagnostics;
using Microsoft.Xna.Framework.Audio;
using Quarp.Core.Audio;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The speaker end of the console: takes the 800 samples the core produced for a tick and
/// hands them to the sound card, keeping a queue two to three blocks deep (ARCHITECTURE §2,
/// M3 work order).
///
/// <para><b>The shell never generates audio and never asks for a tick.</b> The core makes
/// exactly one block per tick and this type decides only what reaches the device. Nothing here
/// can change the simulation, which is the whole reason the PCM is allowed into the
/// golden master: if the sound card's clock could pull ticks, the recording would depend on
/// the sound card.</para>
///
/// <para><b>One rule covers every time control</b>, and the rule itself is
/// <see cref="AudioQueue"/>: submit a block while the device holds fewer than
/// <see cref="AudioQueue.Max"/>, otherwise drop it; at the end of a frame, pad with silence up
/// to <see cref="AudioQueue.Target"/>. What the player hears follows from that rule and from
/// how many blocks each mode produces:</para>
/// <list type="bullet">
///   <item><b>×1</b> — one tick per frame, one block per frame, everything is heard. The
///     queue absorbs the jitter of a frame that ran two ticks.</item>
///   <item><b>×2 … ×8</b> — two to eight blocks a frame against a device that drains one.
///     The queue fills, the rest are dropped, and the player hears roughly one block in N:
///     right pitch, chopped. Playing them all would need the sound card to run N times
///     faster; resampling them into one block is a real feature and it is not this
///     milestone's.</item>
///   <item><b>×1/2 … ×1/8</b> — fewer blocks than the device eats, so most frames pad with
///     silence. Slow motion sounds like the sound sliced by gaps, at the correct pitch.</item>
///   <item><b>pause, single-step, crash</b> — no ticks, so no blocks; the queue drains and
///     the padding takes over. Silence, and the chip is untouched: nothing is stopped or
///     reset, so resuming continues the note that was playing.</item>
///   <item><b>rewind</b> — a resimulation from tick 0 produces thousands of blocks in a few
///     milliseconds and only the last belongs to the tick the player lands on. None of them
///     are submitted (<see cref="CartSession"/> silences the sink for the duration), so
///     rewinding is silent.</item>
/// </list>
///
/// <para><b>Two clocks, and the crackle that follows from it.</b> The tick clock is the
/// shell's accumulator, the sample clock is the sound card's crystal; both are nominally
/// 60 blocks a second and neither is the other. When they have drifted a whole block apart
/// this drops one block or pads one, which is a click every so often at ×1. Curing it means
/// resampling to the device's clock, which needs an interpolator the milestone does not have
/// a budget for. <see cref="Dropped"/> and <see cref="Padded"/> count the events so the cost
/// is a number and not a rumour.</para>
///
/// <para><b>No sound card is not an error.</b> A machine with no audio device (a CI runner, a
/// remote session) constructs an unavailable output, says so once, and the game runs.</para>
/// </summary>
public sealed class AudioOutput : IDisposable
{
    /// <summary>
    /// Scratch buffers cycled through on submission. MonoGame's OpenAL backend copies the
    /// bytes into a device buffer inside <c>SubmitBuffer</c>, so one array would do; a ring
    /// costs 8 × 1600 bytes once and removes the need to be right about that.
    /// </summary>
    private const int ScratchCount = 8;

    private readonly byte[][] _scratch = new byte[ScratchCount][];
    private readonly byte[] _silence = new byte[AudioBlock.ByteLength];

    /// <summary>Submission time of the last <see cref="ScratchCount"/> blocks, for the latency probe.</summary>
    private readonly long[] _submittedAt = new long[ScratchCount];

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DynamicSoundEffectInstance? _instance;

    private int _scratchIndex;
    private long _submitted;        // blocks handed to the device since start, silence included
    private long _started;          // blocks the device has begun playing, as far as we have seen
    private bool _playing;

    private long _latencySum;
    private long _latencyCount;
    private long _depthSum;
    private long _depthCount;

    /// <summary>
    /// Opens the device. Never throws: a machine without audio yields an unavailable output,
    /// because a console that refuses to start because the speakers are missing is worse than
    /// one that runs quietly.
    /// </summary>
    public AudioOutput()
    {
        for (int i = 0; i < ScratchCount; i++)
        {
            _scratch[i] = new byte[AudioBlock.ByteLength];
        }

        try
        {
            _instance = new DynamicSoundEffectInstance(AudioBlock.SampleRate, AudioChannels.Mono);
        }
        catch (NoAudioHardwareException e)
        {
            Console.Error.WriteLine($"[quarp] no audio device — running silently ({e.Message})");
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or DllNotFoundException)
        {
            // A broken or absent OpenAL is the same situation as no hardware, and it must not
            // take the window down with it.
            Console.Error.WriteLine($"[quarp] audio unavailable — running silently ({e.Message})");
        }
    }

    /// <summary>False when there is no device; every other member is then a no-op.</summary>
    public bool IsAvailable => _instance is not null;

    /// <summary>Blocks the device is holding, 0 when unavailable.</summary>
    public int Queued => _instance?.PendingBufferCount ?? 0;

    /// <summary>Blocks dropped because the queue was full — the fast-forward and clock-drift count.</summary>
    public long Dropped { get; private set; }

    /// <summary>Blocks of silence inserted because the simulation produced none in time.</summary>
    public long Padded { get; private set; }

    /// <summary>
    /// Blocks handed to the device since start, padding included. With <see cref="Queued"/>
    /// it gives the rate the device is really draining at, which is the number the drift
    /// paragraph above is about — it is nominally 60 a second and never exactly that.
    /// </summary>
    public long Submitted => _submitted;

    /// <summary>
    /// Mean blocks already queued at the moment a block is handed over — the exact,
    /// clock-free measure of how long that block waits. Its wait is between
    /// <c>(depth - 1)</c> and <c>depth</c> block times of 16.667 ms, the uncertainty being
    /// how much of the head block has already played, which no MonoGame API reports.
    /// -1 before the first block. This is the number to quote.
    /// </summary>
    public double MeanDepthAtSubmit => _depthCount == 0 ? -1 : (double)_depthSum / _depthCount;

    /// <summary>
    /// Mean observed delay between handing a block over and seeing that the device has begun
    /// it, in milliseconds; -1 before the first measurement. A cross-check on
    /// <see cref="MeanDepthAtSubmit"/> and not a better number than it: the observation
    /// happens once a frame, so this reads up to one frame (16.7 ms) high.
    /// </summary>
    public double MeanObservedStartMs => _latencyCount == 0 ? -1 : (double)_latencySum / _latencyCount / 10_000.0;



    /// <summary>
    /// Offers one tick's block. Accepted while the queue has room, dropped when it is full —
    /// which is what turns eight blocks a frame at ×8 into the one block a frame the device
    /// can actually drain. The block is copied, so the caller may reuse it immediately.
    /// </summary>
    public void Submit(AudioBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (_instance is null)
        {
            return;
        }
        int depth = _instance.PendingBufferCount;
        if (!AudioQueue.HasRoom(depth))
        {
            Dropped++;
            return;
        }
        _depthSum += depth;
        _depthCount++;
        byte[] buffer = _scratch[_scratchIndex];
        block.CopyBytesTo(buffer);
        SubmitRaw(buffer);
    }

    /// <summary>
    /// Ends the frame: tops the queue up to <see cref="TargetQueued"/> with silence and keeps
    /// the source running. Called once per frame whatever the simulation did, so a pause, a
    /// rewind and a stalled machine all sound like silence rather than like a stuttering
    /// device draining dry.
    /// </summary>
    public void EndFrame()
    {
        if (_instance is null)
        {
            return;
        }
        for (int pad = AudioQueue.PadNeeded(_instance.PendingBufferCount); pad > 0; pad--)
        {
            Padded++;
            SubmitRaw(_silence);
        }

        // An underrun stops the OpenAL source; without this the first block after a long
        // pause would queue up and never be heard.
        if (!_playing || _instance.State != SoundState.Playing)
        {
            _instance.Play();
            _playing = true;
        }
        Observe();
    }

    /// <summary>
    /// Discards everything the device is still holding and stops the source. This is the
    /// game → library transition's half-frame of honesty (M9 stage 1): the queue is two to
    /// three blocks deep, which is up to 50 ms of the game the player just left, and without
    /// this the library would open to the tail of its soundtrack. The device stays usable —
    /// the next game's first <see cref="EndFrame"/> restarts the source, exactly the underrun
    /// path it already handles.
    /// </summary>
    public void Drain()
    {
        if (_instance is null)
        {
            return;
        }
        _instance.Stop(immediate: true);
        _playing = false;
        // The discarded blocks never played, so they must not enter the latency figures:
        // Observe() infers "started" from submitted-minus-pending, and a drained queue would
        // otherwise book them all as begun at this instant.
        _started = _submitted;
    }

    public void Dispose()
    {
        if (_instance is null)
        {
            return;
        }
        _instance.Stop(immediate: true);
        _instance.Dispose();
    }

    private void SubmitRaw(byte[] buffer)
    {
        Debug.Assert(_instance is not null, "SubmitRaw is only reached with a device open.");
        _submittedAt[_scratchIndex] = _clock.ElapsedTicks;
        _instance!.SubmitBuffer(buffer);
        _submitted++;
        _scratchIndex = (_scratchIndex + 1) % ScratchCount;
    }

    /// <summary>
    /// Reads how far the device has got and books the delay of every block that started since
    /// the last look. <c>PendingBufferCount</c> is the only window MonoGame gives onto the
    /// device's progress: submitted minus pending is the number of blocks it has begun, so the
    /// difference between now and the moment block <c>n</c> was handed over is how long it
    /// spent in the queue. Blocks older than the scratch ring are not measurable and are
    /// skipped rather than guessed at.
    /// </summary>
    private void Observe()
    {
        long started = _submitted - _instance!.PendingBufferCount;
        if (started <= _started)
        {
            return;
        }
        long now = _clock.ElapsedTicks;
        long first = Math.Max(_started, started - ScratchCount);
        for (long n = first; n < started; n++)
        {
            long submittedAt = _submittedAt[(int)(n % ScratchCount)];
            _latencySum += now - submittedAt;
            _latencyCount++;
        }
        _started = started;
    }
}

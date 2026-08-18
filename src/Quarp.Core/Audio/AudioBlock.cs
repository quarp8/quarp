namespace Quarp.Core.Audio;

/// <summary>
/// One tick of sound: exactly <see cref="SamplesPerTick"/> mono samples of signed 16-bit PCM
/// at <see cref="SampleRate"/> Hz. The audio half of a tick's output, and the exact counterpart
/// of <see cref="Framebuffer"/> — the console hands out one of each per tick, both of them pure
/// functions of the simulation (ARCHITECTURE §2, SPEC-8 §4).
///
/// <para><b>Why 16-bit signed integers.</b> The milestone's whole point is that PCM joins the
/// cross-architecture hash, so the sample format has to be bit-identical by construction, not
/// by luck. A signed 16-bit integer is exact — every synthesis step that produces it is integer
/// arithmetic, there is no rounding mode to disagree about and no denormal to flush — and it is
/// simultaneously the one PCM format every backend takes without conversion: WASAPI/XAudio2 and
/// MonoGame's <c>DynamicSoundEffectInstance.SubmitBuffer</c> want interleaved 16-bit signed LE
/// bytes, ALSA calls it <c>S16_LE</c>, and the Web Audio path divides by 32768 — which is exact
/// in binary floating point, so even the float backend cannot smear the bytes. Handing the
/// shell 32-bit floats instead would put a float in the middle of the chain the hash runs
/// through, which is the one thing this milestone forbids.</para>
///
/// <para><b>Why 800 samples.</b> 48000 Hz / 60 ticks divides exactly, so a tick is a whole
/// number of samples and no fractional-sample carry has to be tracked between ticks. That
/// exact division is the reason the sample rate is 48000 and not 44100 (which would be
/// 735 samples per tick — also exact, but 48000 is what every modern device runs natively,
/// so nothing resamples behind our back).</para>
///
/// <para><b>"Channel" is overloaded on purpose, so read carefully.</b> <see cref="OutputChannels"/>
/// is 1 — profile 8 is mono (ADR-013). The four <em>channels</em> of SPEC-8 §4 are synthesis
/// voices (<see cref="Apu.ChannelCount"/>), and they are all mixed into this one mono stream.</para>
///
/// <para>The instance is created once and reused: its identity never changes, so a shell may
/// cache it exactly the way it caches the framebuffer, and the tick path allocates nothing.</para>
/// </summary>
public sealed class AudioBlock
{
    /// <summary>Samples per second. Exactly divisible by the tick rate, which is why it is 48000.</summary>
    public const int SampleRate = 48000;

    /// <summary>Simulation ticks per second (SPEC-8 §7).</summary>
    public const int TicksPerSecond = 60;

    /// <summary>Samples in one tick's block: exactly 800, never approximately 800.</summary>
    public const int SamplesPerTick = SampleRate / TicksPerSecond;

    /// <summary>Interleaved output channels: 1, profile 8 is mono (ADR-013).</summary>
    public const int OutputChannels = 1;

    /// <summary>Bytes per sample in the interchange form: 2, signed little-endian.</summary>
    public const int BytesPerSample = 2;

    /// <summary>Bytes one block occupies once written out as 16-bit LE PCM: 1600.</summary>
    public const int ByteLength = SamplesPerTick * OutputChannels * BytesPerSample;

    /// <summary>
    /// The tick's samples, always <see cref="SamplesPerTick"/> long. Exposed as the array
    /// itself, like <see cref="Framebuffer.Pixels"/>: the shell copies it out every tick and a
    /// wrapper would cost an indirection on the hottest buffer in the console.
    /// </summary>
    public short[] Samples { get; } = new short[SamplesPerTick];

    /// <summary>Silence. Also what a freshly reset console holds before its first tick.</summary>
    public void Clear() => Array.Clear(Samples);

    /// <summary>
    /// Writes the block as interleaved signed 16-bit <b>little-endian</b> PCM — the byte layout
    /// every audio backend on every platform expects, and the one this console promises
    /// regardless of what the host CPU prefers.
    ///
    /// <para>The shift-and-mask is not clumsiness: <c>MemoryMarshal.AsBytes</c> or a
    /// <c>BitConverter</c> round-trip would emit big-endian bytes on a big-endian host, so a
    /// .wav dumped from a replay would compare unequal across architectures while the
    /// simulation itself was perfectly fine — a determinism bug in the file writer wearing
    /// the costume of a determinism bug in the console.</para>
    /// </summary>
    /// <param name="destination">At least <see cref="ByteLength"/> bytes.</param>
    public void CopyBytesTo(Span<byte> destination)
    {
        if (destination.Length < ByteLength)
        {
            throw new ArgumentException(
                $"An audio block needs {ByteLength} bytes, got {destination.Length}.", nameof(destination));
        }
        short[] samples = Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            int sample = samples[i];
            destination[i * 2] = (byte)(sample & 0xFF);
            destination[(i * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }
    }
}

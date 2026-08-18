namespace Quarp.Core.Audio;

/// <summary>
/// The live state of one of the four synthesis channels — everything a rewind has to
/// reproduce for the channel to sound the same the second time through.
///
/// <para>A mutable struct held in an array and reached through <c>ref</c>, per CODESTYLE's
/// struct-first rule for hot data: four of these are the whole register file of the chip, and
/// a class per channel would add four dereferences to the innermost loop of the console for
/// nothing. Copying one by accident is the usual hazard of mutable structs, which is why every
/// method here takes it by <c>ref</c> and nothing hands one out.</para>
///
/// <para>Every field is integer and every field is simulation state (SPEC-8 §7). The phase and
/// the noise register in particular: a channel that carried its phase in a float, or its noise
/// in a <c>Random</c>, would drift apart between two machines within a second and take the PCM
/// hash with it.</para>
/// </summary>
internal struct AudioChannel
{
    /// <summary>The value <see cref="SfxId"/> holds when the channel is playing nothing.</summary>
    public const int Idle = -1;

    /// <summary>
    /// Starting value of the noise register. Any non-zero value works — a 15-bit LFSR with
    /// xor feedback is stuck forever at zero, which is why it may never be seeded with one.
    /// </summary>
    public const ushort NoiseSeed = 1;

    /// <summary>SFX slot being played, or <see cref="Idle"/>.</summary>
    public int SfxId;

    /// <summary>Step of that slot currently sounding, 0-31.</summary>
    public int Step;

    /// <summary>Ticks already spent inside the current step, 0..speed-1. Drives every effect.</summary>
    public int StepTick;

    /// <summary>
    /// Ticks since the SFX started, masked to stay well inside <c>int</c> forever. Vibrato and
    /// arpeggio run off this rather than off <see cref="StepTick"/>, so they stay continuous
    /// when a step boundary passes underneath them. The mask is a multiple of both their
    /// periods, so even the wrap is seamless.
    /// </summary>
    public int Age;

    /// <summary>Pitch the previous step ended on, in 1/256 semitones — what <see cref="NoteEffect.Slide"/> slides from.</summary>
    public int PreviousPitch;

    /// <summary>Waveform phase; wraps at 2^32, one wrap per cycle.</summary>
    public uint Phase;

    /// <summary>15-bit noise LFSR. Never zero (see <see cref="NoiseSeed"/>).</summary>
    public ushort Noise;

    /// <summary>True when the music sequencer started this SFX, false when the cartridge did.</summary>
    public bool FromMusic;

    /// <summary>Nothing is playing here.</summary>
    public readonly bool IsIdle => SfxId < 0;

    /// <summary>
    /// Silences the channel and puts every register back where a freshly reset console has it.
    /// Resetting the phase matters: it is what lets the mixer prove that "all four channels
    /// idle" and "800 zero samples" are the same thing, which is the fast path a rewind spends
    /// nearly all of its time in.
    /// </summary>
    public void Stop()
    {
        SfxId = Idle;
        Step = 0;
        StepTick = 0;
        Age = 0;
        PreviousPitch = 0;
        Phase = 0;
        Noise = NoiseSeed;
        FromMusic = false;
    }

    /// <summary>
    /// Starts an SFX from its first step. The phase restarts at zero — a note that always
    /// begins at the same point of the waveform is what gives a chip its attack, and it also
    /// means the sound of an effect cannot depend on when it was triggered.
    /// </summary>
    public void Start(int sfxId, int firstPitch, bool fromMusic)
    {
        SfxId = sfxId;
        Step = 0;
        StepTick = 0;
        Age = 0;
        PreviousPitch = firstPitch;
        Phase = 0;
        Noise = NoiseSeed;
        FromMusic = fromMusic;
    }
}

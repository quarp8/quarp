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
    /// when a step boundary passes underneath them. The mask spans a power of two, which is a
    /// multiple of the vibrato period and of an arpeggio over four or two sounding steps, so
    /// for those the wrap is seamless; an arpeggio over three steps takes one step at the wrap,
    /// identically on every machine, 77 hours into a single uninterrupted sound.
    /// </summary>
    public int Age;

    /// <summary>Pitch the previous step ended on, in 1/256 semitones — what <see cref="NoteEffect.Slide"/> slides from.</summary>
    public int PreviousPitch;

    /// <summary>
    /// One past the last step this channel is allowed to play, or 0 when the channel plays the
    /// whole slot the ordinary way (loop and all). Non-zero only for a segment started by
    /// <c>Sfx(id, channel, offsetSteps, lengthSteps)</c> (ADR-037): the sequencer stops the
    /// channel when the next step would reach this bound, and the slot's own loop is not
    /// consulted — the caller named the exact steps to play, and a loop would make that count
    /// a lie. Zero is unambiguous as "no segment" because a real segment always ends at least
    /// one step past its first. Simulation state like every other field here: a segment
    /// replayed from tick 0 must stop on the same tick.
    /// </summary>
    public int SegmentEnd;

    /// <summary>Waveform phase; wraps at 2^32, one wrap per cycle.</summary>
    public uint Phase;

    /// <summary>15-bit noise LFSR. Never zero (see <see cref="NoiseSeed"/>).</summary>
    public ushort Noise;

    /// <summary>True when the music sequencer started this SFX, false when the cartridge did.</summary>
    public bool FromMusic;

    // --- what a version 2 cell can put on a voice (ADR-040) ---
    //
    // Eight more integers, and every one of them holds its neutral value for the whole life of a
    // version 1 run: PitchOffset 0, Gain GainUnity, SpeedOverride 0, CellEffect 0. That is not a
    // hope, it is the proof that v1 PCM is untouched — the render path adds PitchOffset (zero),
    // skips the gain multiply behind a `!= GainUnity` guard, and reads the slot's own speed when
    // the override is 0. A tracker cell is the only thing that ever writes them.

    /// <summary>
    /// Pitch added to every step of the slot, in 1/256 semitones — the cell's note minus the
    /// instrument's root, plus the order entry's transpose. This is what makes an SFX slot an
    /// instrument instead of a fixed sound.
    /// </summary>
    public int PitchOffset;

    /// <summary>Where a <see cref="MusicEffect.Slide"/> glide started, in 1/256 semitones.</summary>
    public int GlideFrom;

    /// <summary>Where a <see cref="MusicEffect.Slide"/> glide is going, in 1/256 semitones.</summary>
    public int GlideTo;

    /// <summary>
    /// The voice's level in the fade's fixed-point scale, 0..<see cref="Apu.GainUnity"/> — what
    /// a cell's volume column sets. <see cref="Apu.GainUnity"/> is "no attenuation", and it is
    /// the value every channel holds unless a version 2 cell says otherwise.
    /// </summary>
    public int Gain;

    /// <summary>Ticks per SFX step the instrument overrides the slot with; 0 means the slot's own speed.</summary>
    public int SpeedOverride;

    /// <summary>The <see cref="MusicEffect"/> a cell armed on this voice, or 0 for none.</summary>
    public int CellEffect;

    /// <summary>The armed effect's parameter, 1-255.</summary>
    public int CellParam;

    /// <summary>Ticks since the cell armed the effect. Masked like <see cref="Age"/>, and for the same reason.</summary>
    public int EffectTick;

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
        SegmentEnd = 0;
        Phase = 0;
        Noise = NoiseSeed;
        FromMusic = false;
        ClearCellState();
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
        SegmentEnd = 0;
        Phase = 0;
        Noise = NoiseSeed;
        FromMusic = fromMusic;
        ClearCellState();
    }

    /// <summary>
    /// Puts the version 2 columns back to their neutral values: no transposition, full level, the
    /// slot's own speed, no effect. Called from both <see cref="Stop"/> and <see cref="Start"/>,
    /// so a voice can never inherit the pitch, the level or the effect of the note before it —
    /// the cell that wants any of those says so, and the tracker's latching lives in the
    /// sequencer where it can be read, not in the register file where it would be a surprise.
    /// </summary>
    public void ClearCellState()
    {
        PitchOffset = 0;
        GlideFrom = 0;
        GlideTo = 0;
        Gain = Apu.GainUnity;
        SpeedOverride = 0;
        CellEffect = 0;
        CellParam = 0;
        EffectTick = 0;
    }

    /// <summary>
    /// Starts a segment of an SFX: steps <paramref name="firstStep"/> up to but not including
    /// <paramref name="endStep"/> (ADR-037). Everything else — phase, noise, age — restarts
    /// exactly as <see cref="Start"/> restarts it, so a segment attacks the same way a whole
    /// slot does; only the starting step and the stopping bound differ. Never from music: the
    /// pattern sequencer plays whole slots only.
    /// </summary>
    public void StartSegment(int sfxId, int firstPitch, int firstStep, int endStep)
    {
        Start(sfxId, firstPitch, fromMusic: false);
        Step = firstStep;
        SegmentEnd = endStep;
    }
}

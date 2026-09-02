namespace Quarp.Core.Audio;

/// <summary>
/// One of the 64 instruments of a version 2 song: an SFX slot used as a <em>timbre</em> rather
/// than as a fixed sound (docs/AUDIO-FORMAT.md §4, ADR-040).
///
/// <para><b>Why the table exists at all.</b> Version 1 had no way to say "play this slot, but a
/// fifth higher": a pattern channel named a slot and the slot played at the pitch it was written
/// at, so a melody meant one slot per phrase and the bank filled up with sixty-four copies of the
/// same square wave. That is exactly the hole UnDUNE II reported (PROBLEMS §1.11: PICO-8's waves
/// 8-15 are its first eight SFX used as instruments, "мелодия остаётся, тембр умирает") and the
/// hole this table closes: the slot supplies the waveform, the envelope, the per-step effects and
/// the loop; the pattern cell supplies the pitch, the level and the moment.</para>
///
/// <para><b>The root note is what makes transposition mean something.</b> The slot's steps are
/// written at some pitch; <see cref="Root"/> says which. A cell asking for note N plays the slot
/// with every step shifted by N - <see cref="Root"/> semitones, so an instrument written around
/// C-4 answers a C-4 cell with exactly the sound its author heard. The default — an all-zero
/// record — is slot 0 rooted at note 0, which is a legal instrument and not a special case.</para>
/// </summary>
public readonly struct MusicInstrument : IEquatable<MusicInstrument>
{
    /// <summary>Instruments a song has: 64, one per SFX slot.</summary>
    public const int Count = 64;

    /// <summary>The default instrument: SFX slot 0, rooted at note 0, looping as its slot says, at its slot's speed.</summary>
    public static readonly MusicInstrument Default = default;

    private readonly byte _slot;
    private readonly byte _root;
    private readonly byte _flags;
    private readonly byte _speed;

    /// <summary>Builds an instrument, clamping every field the way the rest of the audio model does.</summary>
    public MusicInstrument(int slot, int root, bool once = false, int speed = 0)
    {
        _slot = (byte)Math.Clamp(slot, 0, AudioBank.SfxCount - 1);
        _root = (byte)Math.Clamp(root, 0, NoteTable.MaxNote);
        _flags = once ? OnceFlag : (byte)0;
        _speed = (byte)Math.Clamp(speed, 0, SfxSlot.MaxSpeed);
    }

    /// <summary>Bit 0 of the flag byte: play the slot once, ignoring the loop it carries.</summary>
    public const byte OnceFlag = 0x01;

    /// <summary>The SFX slot this instrument takes its waveform, envelope, effects and loop from.</summary>
    public int Slot => _slot;

    /// <summary>The semitone the slot is written at; a cell's note transposes the slot by the difference.</summary>
    public int Root => _root;

    /// <summary>
    /// True when the instrument plays its slot once and ignores the slot's loop — the same rule
    /// a segment follows (ADR-037), and what turns a looping pad into a one-shot hit without a
    /// second copy of the slot.
    /// </summary>
    public bool Once => (_flags & OnceFlag) != 0;

    /// <summary>
    /// Ticks per SFX step this instrument overrides the slot with, or 0 for the slot's own speed.
    /// The same slot can be a short blip in one instrument and a long pad in another.
    /// </summary>
    public int Speed => _speed;

    /// <summary>The flag byte exactly as <c>music.bin</c> stores it.</summary>
    public byte Flags => _flags;

    /// <inheritdoc/>
    public bool Equals(MusicInstrument other) =>
        _slot == other._slot && _root == other._root && _flags == other._flags && _speed == other._speed;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MusicInstrument other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _slot | (_root << 8) | (_flags << 16) | (_speed << 24);

    /// <summary>Field-by-field equality.</summary>
    public static bool operator ==(MusicInstrument a, MusicInstrument b) => a.Equals(b);

    /// <summary>Field-by-field inequality.</summary>
    public static bool operator !=(MusicInstrument a, MusicInstrument b) => !a.Equals(b);
}

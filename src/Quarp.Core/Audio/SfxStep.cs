namespace Quarp.Core.Audio;

/// <summary>
/// One of the 32 steps of an SFX slot: note, waveform, volume and effect (SPEC-8 §4).
/// A step lasts <see cref="SfxSlot.Speed"/> ticks, and everything about it is re-evaluated
/// every tick, so a step is a small program rather than a single snapshot of state.
///
/// <para><b>A note is a semitone index 0-63 counting up from C2 = 65.406 Hz</b>, which is MIDI
/// 36 for anyone converting; note 33 is A4 = 440 Hz and note 63 is D#7. The range is not a
/// preference — the step word of <c>sfx.bin</c> spends exactly six bits on the note. The pitch
/// a note turns into is a lookup in <see cref="NoteTable"/>: a precomputed integer table, never
/// <c>Math.Pow</c> at runtime.</para>
///
/// <para><b>Volume 0 is a rest</b>, not "quiet": there is no separate note-off, because a step
/// that plays nothing is exactly a step at volume 0. That is also why <c>default</c> is
/// silence, which makes an all-zero sfx bank mean "no sound" without a special case anywhere.</para>
///
/// <para>The constructor clamps instead of throwing. Steps arrive from a cartridge file, that
/// is to say from an untrusted source, and the console answers bad data by doing something
/// harmless and defined everywhere else — a note of 200 becoming 127 is a defined outcome, an
/// exception mid-tick is not. The loader is still expected to reject a malformed file loudly;
/// this is the second line of defence, not the first.</para>
/// </summary>
public readonly struct SfxStep : IEquatable<SfxStep>
{
    /// <summary>Highest note the console can play: 63, D#7, 2489.02 Hz. Six bits on disk.</summary>
    public const int MaxNote = NoteTable.MaxNote;

    /// <summary>Loudest volume level. 0 is a rest, 7 is full.</summary>
    public const int MaxVolume = 7;

    /// <summary>A rest: no sound for the step's duration. Identical to <c>default</c>.</summary>
    public static readonly SfxStep Rest = default;

    /// <summary>
    /// Builds a step, clamping every field into range: note to 0-63, volume to 0-7, and an
    /// unknown waveform or effect to <see cref="Waveform.Pulse50"/> and
    /// <see cref="NoteEffect.None"/> respectively.
    /// </summary>
    public SfxStep(int note, Waveform wave, int volume, NoteEffect effect = NoteEffect.None)
    {
        Note = (byte)Math.Clamp(note, 0, MaxNote);
        Volume = (byte)Math.Clamp(volume, 0, MaxVolume);
        Wave = wave <= Waveform.Noise ? wave : Waveform.Pulse50;
        Effect = effect <= NoteEffect.Arpeggio ? effect : NoteEffect.None;
    }

    /// <summary>Semitone index 0-63 above C2; note 33 is A4 = 440 Hz.</summary>
    public byte Note { get; }

    /// <summary>The waveform this step is played with.</summary>
    public Waveform Wave { get; }

    /// <summary>Volume 0-7; 0 is a rest.</summary>
    public byte Volume { get; }

    /// <summary>What happens to the note and the volume over the step's ticks.</summary>
    public NoteEffect Effect { get; }

    /// <summary>True when the step makes no sound at all, which is exactly volume 0.</summary>
    public bool IsRest => Volume == 0;

    /// <inheritdoc/>
    public bool Equals(SfxStep other) =>
        Note == other.Note && Wave == other.Wave && Volume == other.Volume && Effect == other.Effect;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SfxStep other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Note | ((int)Wave << 8) | (Volume << 16) | ((int)Effect << 24);

    /// <summary>Field-by-field equality.</summary>
    public static bool operator ==(SfxStep a, SfxStep b) => a.Equals(b);

    /// <summary>Field-by-field inequality.</summary>
    public static bool operator !=(SfxStep a, SfxStep b) => !a.Equals(b);
}

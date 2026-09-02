namespace Quarp.Core.Audio;

/// <summary>What a cell's note column does.</summary>
public enum MusicNoteKind
{
    /// <summary>Nothing: the channel keeps playing whatever it was playing.</summary>
    None = 0,

    /// <summary>Start the channel's instrument at the cell's note.</summary>
    On = 1,

    /// <summary>Silence the channel.</summary>
    Off = 2,
}

/// <summary>
/// The effect column of a cell. Every one of these is a closed formula of (cell, ticks since the
/// cell) — never an accumulation — for the reason the per-step effects give: no drift can build
/// up and two runs cannot end a long note a fraction apart.
/// </summary>
public enum MusicEffect
{
    /// <summary>No effect; the parameter must be zero.</summary>
    None = 0,

    /// <summary>
    /// Arpeggio over the cell's note and the two semitone offsets in the parameter's nibbles:
    /// note, note+high, note+low, every <see cref="Apu.ArpeggioTicksPerNote"/> ticks. The chord
    /// on one channel, written the way every tracker since Soundtracker writes it.
    /// </summary>
    Arpeggio = 1,

    /// <summary>
    /// Glide to the cell's note over <c>param</c> ticks instead of restarting the instrument —
    /// tracker portamento. The voice keeps its phase, its envelope and its place in the slot;
    /// only the pitch moves.
    /// </summary>
    Slide = 2,

    /// <summary>Silence the voice <c>param</c> ticks into the row.</summary>
    Cut = 3,
}

/// <summary>
/// One cell of a version 2 song: the note, instrument, volume and effect that row plays on that
/// channel (docs/AUDIO-FORMAT.md §4, ADR-040). This is what the pattern list that came before
/// could not hold — its cell was six bits of SFX slot and a bit saying "this channel plays" —
/// and it is the whole reason the format was rewritten.
///
/// <para><b>Every column is optional, and that is the point of a tracker.</b> A cell that names
/// only a volume changes the level of a note that is already sounding; a cell that names only an
/// instrument re-timbres the next note; a cell that names only an effect arms it on the voice
/// under it. An empty cell — <c>default</c>, the zero word — does nothing at all, which is why
/// an all-zero pattern is a bar of rest and an all-zero song is silence with no special case
/// anywhere.</para>
///
/// <para>The constructor clamps instead of throwing, exactly as <see cref="SfxStep"/> does and
/// for the same reason: cells arrive from a cartridge file, and since ADR-036 they can arrive
/// from bytes nobody validated. A reserved note kind and an unknown effect both fall back to
/// "nothing", and a field the format requires to be zero is read as zero. The loader is still
/// expected to reject a malformed <c>music.bin</c> loudly (docs/AUDIO-FORMAT.md §5a); this is
/// the second line of defence, not the first.</para>
/// </summary>
public readonly struct MusicCell : IEquatable<MusicCell>
{
    /// <summary>Highest semitone a cell can name: 63, the same six-bit range a step word has.</summary>
    public const int MaxNote = NoteTable.MaxNote;

    /// <summary>Loudest volume a cell can name.</summary>
    public const int MaxVolume = SfxStep.MaxVolume;

    /// <summary>Instruments a song has: 64, one per SFX slot.</summary>
    public const int InstrumentCount = 64;

    /// <summary>The empty cell — nothing happens. Identical to <c>default</c>.</summary>
    public static readonly MusicCell Empty = default;

    private readonly uint _word;

    /// <summary>
    /// Rebuilds a cell from the u32 <c>music.bin</c> stores (docs/AUDIO-FORMAT.md §4):
    /// note 0-5, note kind 6-7, instrument 8-13, has-instrument 14, volume 15-17, has-volume 18,
    /// effect 19-23, parameter 24-31. Anything the format forbids is dropped rather than
    /// believed, so a bad bank cannot reach the synthesizer.
    /// </summary>
    public MusicCell(uint word)
    {
        int kind = (int)((word >> 6) & 0x03);
        int note = kind == (int)MusicNoteKind.On ? (int)(word & 0x3F) : 0;
        if (kind > (int)MusicNoteKind.Off)
        {
            kind = (int)MusicNoteKind.None;
        }
        bool hasInstrument = (word & (1u << 14)) != 0;
        int instrument = hasInstrument ? (int)((word >> 8) & 0x3F) : 0;
        bool hasVolume = (word & (1u << 18)) != 0;
        int volume = hasVolume ? (int)((word >> 15) & 0x07) : 0;
        int effect = (int)((word >> 19) & 0x1F);
        int param = (int)((word >> 24) & 0xFF);
        if (effect > (int)MusicEffect.Cut || param == 0)
        {
            // An unknown effect and an effect with no parameter are both "nothing happens": the
            // parameter is the whole effect for all three of ours, and 0 would mean a zero-tick
            // cut, a zero-tick glide and a unison arpeggio.
            effect = (int)MusicEffect.None;
            param = 0;
        }
        if (kind == (int)MusicNoteKind.Off)
        {
            // A note off is one spelling and one only (docs/AUDIO-FORMAT.md §5a): it silences the
            // voice, so a volume or an effect beside it would have nothing to act on.
            hasVolume = false;
            volume = 0;
            hasInstrument = false;
            instrument = 0;
            effect = (int)MusicEffect.None;
            param = 0;
        }
        if (effect == (int)MusicEffect.Slide && kind != (int)MusicNoteKind.On)
        {
            effect = (int)MusicEffect.None;
            param = 0;
        }
        _word = (uint)note
            | ((uint)kind << 6)
            | ((uint)instrument << 8)
            | (hasInstrument ? 1u << 14 : 0u)
            | ((uint)volume << 15)
            | (hasVolume ? 1u << 18 : 0u)
            | ((uint)effect << 19)
            | ((uint)param << 24);
    }

    /// <summary>Builds a cell column by column; every argument is clamped into range.</summary>
    public MusicCell(
        MusicNoteKind kind,
        int note,
        int instrument,
        int volume,
        MusicEffect effect = MusicEffect.None,
        int param = 0)
        : this(Pack(kind, note, instrument, volume, effect, param))
    {
    }

    /// <summary>The cell exactly as <c>music.bin</c> stores it, canonical by construction.</summary>
    public uint Word => _word;

    /// <summary>What the note column does.</summary>
    public MusicNoteKind Kind => (MusicNoteKind)((_word >> 6) & 0x03);

    /// <summary>Semitone 0-63; meaningful only when <see cref="Kind"/> is <see cref="MusicNoteKind.On"/>.</summary>
    public int Note => (int)(_word & 0x3F);

    /// <summary>True when the cell names an instrument; otherwise the channel keeps the one it has.</summary>
    public bool HasInstrument => (_word & (1u << 14)) != 0;

    /// <summary>Instrument 0-63; meaningful only when <see cref="HasInstrument"/>.</summary>
    public int Instrument => (int)((_word >> 8) & 0x3F);

    /// <summary>True when the cell names a volume; otherwise the channel keeps the level it has.</summary>
    public bool HasVolume => (_word & (1u << 18)) != 0;

    /// <summary>Volume 0-7; meaningful only when <see cref="HasVolume"/>. Zero is silence, not "no change".</summary>
    public int Volume => (int)((_word >> 15) & 0x07);

    /// <summary>The effect armed by this cell.</summary>
    public MusicEffect Effect => (MusicEffect)((_word >> 19) & 0x1F);

    /// <summary>The effect's parameter, 1-255; zero exactly when there is no effect.</summary>
    public int Param => (int)((_word >> 24) & 0xFF);

    /// <summary>True when the cell does nothing at all — the zero word.</summary>
    public bool IsEmpty => _word == 0;

    private static uint Pack(MusicNoteKind kind, int note, int instrument, int volume, MusicEffect effect, int param)
    {
        uint word = 0;
        int k = (int)kind;
        if ((uint)k > (int)MusicNoteKind.Off)
        {
            k = (int)MusicNoteKind.None;
        }
        word |= (uint)k << 6;
        if (k == (int)MusicNoteKind.On)
        {
            word |= (uint)Math.Clamp(note, 0, MaxNote);
        }
        if (instrument >= 0)
        {
            word |= (1u << 14) | ((uint)Math.Clamp(instrument, 0, InstrumentCount - 1) << 8);
        }
        if (volume >= 0)
        {
            word |= (1u << 18) | ((uint)Math.Clamp(volume, 0, MaxVolume) << 15);
        }
        int e = (int)effect;
        if ((uint)e <= (int)MusicEffect.Cut && e != 0)
        {
            word |= ((uint)e << 19) | ((uint)Math.Clamp(param, 0, 255) << 24);
        }
        return word;
    }

    /// <inheritdoc/>
    public bool Equals(MusicCell other) => _word == other._word;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MusicCell other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (int)_word;

    /// <summary>Field-by-field equality — which, since the encoding is canonical, is word equality.</summary>
    public static bool operator ==(MusicCell a, MusicCell b) => a._word == b._word;

    /// <summary>Field-by-field inequality.</summary>
    public static bool operator !=(MusicCell a, MusicCell b) => a._word != b._word;
}

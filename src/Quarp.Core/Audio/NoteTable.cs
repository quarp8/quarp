namespace Quarp.Core.Audio;

/// <summary>
/// The tuning of the console: note index in, phase-accumulator increment out, by table lookup
/// and integer interpolation. No <c>Math.Pow</c>, no <c>float</c>, nothing evaluated at runtime
/// that one CPU could round differently from another — which is the entire reason this type
/// exists instead of a two-line formula.
///
/// <para><b>What an increment is.</b> A channel holds a 32-bit unsigned phase and adds an
/// increment once per sample; the phase wraps at 2^32, and one wrap is one cycle of the
/// waveform. So increment = round(frequency × 2^32 / 48000), and the values below are exactly
/// that, computed once against equal temperament and pasted in. Two constants generate the
/// whole table — A4 = 440 Hz and the twelfth root of two — and <c>NoteTableTests</c> checks
/// every entry against a double-precision reference. That test is the only place a double is
/// allowed anywhere near this chain, because it runs on a build server and never in a tick.</para>
///
/// <para><b>The numbering: 0-63, semitones above C2 = 65.406 Hz</b> (the note a MIDI sequencer
/// calls 36). Two facts fix it. The step word of <c>sfx.bin</c> spends exactly 6 bits on the
/// note, so the range is 64 semitones and not one more; and 65.4 Hz is where a 4-channel chip
/// with no filters starts sounding like a note instead of a rattle. The top of the range,
/// note 63 = D#7 = 2489 Hz, is where a square wave stops being musical and starts being an
/// alarm. Five and a third octaves is the same span PICO-8 gives, for the same reasons.
/// Entry 64 is not a playable note: it is there so interpolation at note 63 has a right-hand
/// neighbour and needs no special case.</para>
///
/// <para><b>Fractional pitch.</b> Slide, vibrato, drop and arpeggio all need pitches between
/// the semitones, so pitch is carried in 1/256 of a semitone (<see cref="FractionBits"/>) and
/// <see cref="Increment"/> interpolates linearly between two entries. Linear interpolation of
/// an exponential is not exact: across one semitone the worst case is about 0.04%, well under
/// a cent, some hundred times finer than a listener can hear and finer than the hardware this
/// imitates ever held its tuning. Exactness of the <em>bytes</em> is untouched — the same
/// integers come out on every machine, which is the property the milestone is about.</para>
/// </summary>
public static class NoteTable
{
    /// <summary>Lowest note: index 0, C2, 65.406 Hz.</summary>
    public const int MinNote = 0;

    /// <summary>Highest note: index 63, D#7, 2489.016 Hz. Fixed by the 6-bit note field of a step word.</summary>
    public const int MaxNote = 63;

    /// <summary>Playable notes: 64.</summary>
    public const int NoteCount = MaxNote + 1;

    /// <summary>MIDI note number of note 0, for tools that speak MIDI: 36, i.e. C2.</summary>
    public const int MidiOfNoteZero = 36;

    /// <summary>Bits of fraction in a pitch value: 8, so a pitch is in 1/256 of a semitone.</summary>
    public const int FractionBits = 8;

    /// <summary>Pitch units per semitone: 256.</summary>
    public const int PitchesPerSemitone = 1 << FractionBits;

    /// <summary>Lowest pitch value, the same sound as <see cref="MinNote"/>.</summary>
    public const int MinPitch = MinNote << FractionBits;

    /// <summary>Highest pitch value, the same sound as <see cref="MaxNote"/>.</summary>
    public const int MaxPitch = MaxNote << FractionBits;

    /// <summary>The sample rate these increments were computed for; changing one without the other detunes the console.</summary>
    public const int SampleRate = AudioBlock.SampleRate;

    // Phase increments for notes 0..64 in 32-bit phase units:
    //     Increments[n] = round(440 * 2^((n - 33) / 12) * 2^32 / 48000)
    // Note 33 is A4, so Increments[33] = 39370534 is 440 Hz exactly. Each entry is rounded on
    // its own, so an octave is double the entry twelve places below it to within one unit and
    // not exactly (5852465 -> 11704930 -> 23409859, where exact doubling would say 23409860).
    // That is not slack in the table: exact octaves and a correctly rounded A4 cannot both
    // hold, since anchoring the doubling anywhere moves every note in the other direction.
    // One unit is 1.1e-5 Hz, 1.4e-4 cents. Entry 64 is the interpolation sentinel described
    // in the type comment, not a playable note.
    private static readonly uint[] Increments =
    {
        5852465, 6200470, 6569170, 6959793, 7373644, 7812103, 8276635, 8768789,
        9290209, 9842633, 10427907, 11047982, 11704930, 12400941, 13138339, 13919586,
        14747287, 15624207, 16553270, 17537579, 18580418, 19685267, 20855814, 22095965,
        23409859, 24801882, 26276679, 27839171, 29494575, 31248413, 33106541, 35075158,
        37160835, 39370534, 41711627, 44191930, 46819719, 49603764, 52553357, 55678342,
        58989149, 62496826, 66213081, 70150316, 74321671, 78741067, 83423255, 88383859,
        93639437, 99207528, 105106715, 111356685, 117978298, 124993653, 132426162, 140300631,
        148643341, 157482134, 166846509, 176767719, 187278874, 198415056, 210213429, 222713370,
        235956596,
    };

    /// <summary>Entries in the table: 64 playable notes plus the interpolation sentinel.</summary>
    public static int Length => Increments.Length;

    /// <summary>A note as a pitch value; notes outside 0-63 are clamped into range.</summary>
    public static int ToPitch(int note) => Math.Clamp(note, MinNote, MaxNote) << FractionBits;

    /// <summary>The phase increment of a whole note; notes outside 0-63 are clamped into range.</summary>
    public static uint NoteIncrement(int note) => Increments[Math.Clamp(note, MinNote, MaxNote)];

    /// <summary>
    /// The phase increment of a pitch in 1/256 semitones, interpolated between the two
    /// neighbouring table entries. Pitches outside the range are clamped, so no effect can
    /// slide, drop or wobble a channel out of the table.
    /// </summary>
    public static uint Increment(int pitch)
    {
        pitch = Math.Clamp(pitch, MinPitch, MaxPitch);
        int index = pitch >> FractionBits;
        int fraction = pitch & (PitchesPerSemitone - 1);
        uint low = Increments[index];
        if (fraction == 0)
        {
            return low;
        }
        // The gap between neighbouring entries reaches 1.3e7 at the top of the table, and
        // multiplying that by 255 overflows a uint. The widening to ulong is free on both
        // target architectures and is not a rounding decision.
        uint high = Increments[index + 1];
        return low + (uint)(((ulong)(high - low) * (uint)fraction) >> FractionBits);
    }
}

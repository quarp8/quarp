using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The tuning table, checked against the arithmetic that generated it rather than against
/// itself. A test that asserted <c>NoteIncrement(33) == Increments[33]</c> would pass no matter
/// what was pasted into the file; these recompute equal temperament in <c>double</c> and demand
/// the table match to the unit, which is exactly the check the table cannot do for itself.
///
/// <para>The double arithmetic here is deliberate and is the only place it is allowed near the
/// audio chain: it runs on a build machine, once, and its output is compared against constants
/// — it never touches a tick, so it cannot make PCM differ between architectures. That is the
/// whole trade the table exists to make: floating point at authoring time, integers at runtime.</para>
/// </summary>
public class NoteTableTests
{
    private const double PhaseUnits = 4294967296.0; // 2^32
    private const double A4Hz = 440.0;
    private const int A4Note = 33;

    private static double Frequency(double note) => A4Hz * Math.Pow(2.0, (note - A4Note) / 12.0);

    private static uint Expected(int note) =>
        (uint)Math.Round(Frequency(note) * PhaseUnits / NoteTable.SampleRate, MidpointRounding.AwayFromZero);

    [Fact]
    public void EveryEntryMatchesEqualTemperament()
    {
        for (int note = NoteTable.MinNote; note <= NoteTable.MaxNote; note++)
        {
            Assert.Equal(Expected(note), NoteTable.NoteIncrement(note));
        }
    }

    [Fact]
    public void TheInterpolationSentinelIsOneSemitonePastTheTop()
    {
        // Increment() reads index+1, so the table must hold 65 entries for 64 notes. A table
        // one entry short would pass every other test here and throw on the highest note only
        // when an effect pushed it a fraction sharp.
        Assert.Equal(NoteTable.NoteCount + 1, NoteTable.Length);
        Assert.Equal(NoteTable.MaxPitch, NoteTable.ToPitch(NoteTable.MaxNote));
        Assert.True(NoteTable.Increment(NoteTable.MaxPitch) > 0);
    }

    [Fact]
    public void A4IsExactlyFourHundredAndFortyHertz()
    {
        double hz = NoteTable.NoteIncrement(A4Note) * (double)NoteTable.SampleRate / PhaseUnits;
        Assert.Equal(A4Hz, hz, 3);
        Assert.Equal(69, NoteTable.MidiOfNoteZero + A4Note); // the note MIDI calls 69 is A4
    }

    [Fact]
    public void AnOctaveIsDoubleTheIncrementToWithinOneUnitOfRounding()
    {
        // This asked for exact equality until M3 acceptance and 32 of the 53 pairs failed it,
        // because the table is 65 independent roundings of an exponential and not a chain of
        // doublings. Exact octaves are unreachable together with the entry above: anchoring
        // the doubling at C2 moves A4 to 39370532 and A4 stops being the correctly rounded
        // 440 Hz. Anchoring it at A4 moves C2 instead. Rounding every note on its own is the
        // only rule with no arbitrary anchor, so that is the rule, and the octave holds to
        // one unit of a 32-bit increment. One unit is 48000/2^32 = 1.1e-5 Hz of detuning,
        // 1.4e-4 cents: two channels an octave apart drift a cycle apart in about a day.
        for (int note = NoteTable.MinNote; note + 12 <= NoteTable.MaxNote; note++)
        {
            long doubled = 2L * NoteTable.NoteIncrement(note);
            long actual = NoteTable.NoteIncrement(note + 12);
            Assert.InRange(actual, doubled - 1, doubled + 1);
        }
    }

    [Fact]
    public void TheTableRisesStrictlyAndSpansTheDocumentedRange()
    {
        for (int note = NoteTable.MinNote; note < NoteTable.MaxNote; note++)
        {
            Assert.True(NoteTable.NoteIncrement(note) < NoteTable.NoteIncrement(note + 1),
                $"note {note} is not below note {note + 1}");
        }
        Assert.Equal(65.406, Frequency(NoteTable.MinNote), 3);  // C2
        Assert.Equal(2489.016, Frequency(NoteTable.MaxNote), 3); // D#7
    }

    [Fact]
    public void WholeNotePitchesHitTheTableExactly()
    {
        for (int note = NoteTable.MinNote; note <= NoteTable.MaxNote; note++)
        {
            Assert.Equal(NoteTable.NoteIncrement(note), NoteTable.Increment(NoteTable.ToPitch(note)));
        }
    }

    [Fact]
    public void FractionalPitchesLieBetweenTheirNeighboursAndNeverOverflow()
    {
        // The interpolation multiplies a gap of up to 1.3e7 by up to 255; done in uint that
        // wraps and produces a pitch below the note it started from. This walks every fraction
        // of the widest gap in the table and would catch exactly that.
        for (int note = NoteTable.MinNote; note < NoteTable.MaxNote; note++)
        {
            uint low = NoteTable.NoteIncrement(note);
            uint high = NoteTable.NoteIncrement(note + 1);
            uint previous = low;
            for (int fraction = 0; fraction < NoteTable.PitchesPerSemitone; fraction++)
            {
                uint value = NoteTable.Increment(NoteTable.ToPitch(note) + fraction);
                Assert.InRange(value, low, high);
                Assert.True(value >= previous, $"note {note} fraction {fraction} went backwards");
                previous = value;
            }
        }
    }

    [Fact]
    public void InterpolationStaysWithinACentOfTrueEqualTemperament()
    {
        // The claim in NoteTable's comment, measured. A cent is 1/100 of a semitone; the worst
        // case sits at the middle of a semitone, so this samples every quarter of every one.
        double worstCents = 0;
        for (int note = NoteTable.MinNote; note < NoteTable.MaxNote; note++)
        {
            for (int fraction = 0; fraction < NoteTable.PitchesPerSemitone; fraction += 16)
            {
                uint actual = NoteTable.Increment(NoteTable.ToPitch(note) + fraction);
                double ideal = Frequency(note + (fraction / (double)NoteTable.PitchesPerSemitone))
                    * PhaseUnits / NoteTable.SampleRate;
                double cents = Math.Abs(1200.0 * Math.Log2(actual / ideal));
                worstCents = Math.Max(worstCents, cents);
            }
        }
        Assert.True(worstCents < 1.0, $"worst interpolation error was {worstCents:F3} cents");
    }

    [Fact]
    public void OutOfRangeNotesAndPitchesClampInsteadOfThrowing()
    {
        Assert.Equal(NoteTable.NoteIncrement(NoteTable.MinNote), NoteTable.NoteIncrement(-1000));
        Assert.Equal(NoteTable.NoteIncrement(NoteTable.MaxNote), NoteTable.NoteIncrement(1000));
        Assert.Equal(NoteTable.Increment(NoteTable.MinPitch), NoteTable.Increment(int.MinValue));
        Assert.Equal(NoteTable.Increment(NoteTable.MaxPitch), NoteTable.Increment(int.MaxValue));
    }

    [Fact]
    public void EveryNoteStaysFarBelowTheNyquistWrapTheNoiseClockAssumes()
    {
        // RenderNoise clocks the LFSR once per phase wrap and detects a wrap by comparing the
        // new phase with the old. That is only correct while an increment is under 2^31 — two
        // wraps in one sample would be invisible. The top note is 2489 Hz, so there is a factor
        // of ten thousand in hand, and this test is what would notice if the table were ever
        // retuned upward.
        Assert.True(NoteTable.NoteIncrement(NoteTable.MaxNote) < uint.MaxValue / 2);
    }
}

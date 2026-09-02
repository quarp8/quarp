using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The four-argument <c>Sfx(id, channel, offsetSteps, lengthSteps)</c> (ADR-037): playing a
/// named run of steps out of one slot — the call Terra's <c>ssfx</c> packs four sounds into a
/// full bank with. The rules under test, each with its red state demonstrated somewhere in the
/// file: the segment starts at its offset, plays exactly its steps, ignores the slot's loop,
/// clips an overhang, refuses an offset outside the slot and a non-positive length softly, and
/// leaves the two-argument call bit-for-bit alone.
/// </summary>
public class SfxSegmentTests
{
    private const int Note = 33;   // A4, 440 Hz

    /// <summary>A slot whose step i plays at volume (i % 7) + 1 — every step audibly its own.</summary>
    private static SfxSlot RampSlot(int length = 8, int speed = 2)
    {
        var slot = new SfxSlot { Speed = speed, Length = length };
        for (int i = 0; i < length; i++)
        {
            slot[i] = new SfxStep(Note, Waveform.Pulse50, (i % 7) + 1, NoteEffect.None);
        }
        return slot;
    }

    private static Apu ChipWith(SfxSlot slot, int id = 0)
    {
        var bank = new AudioBank();
        bank.GetSfx(id).CopyFrom(slot);
        var apu = new Apu();
        apu.LoadBank(bank);
        return apu;
    }

    private static int Peak(AudioBlock block)
    {
        int peak = 0;
        foreach (short sample in block.Samples)
        {
            peak = Math.Max(peak, Math.Abs((int)sample));
        }
        return peak;
    }

    /// <summary>Amplitude a pulse step of volume <paramref name="volume"/> peaks at.</summary>
    private static int Amp(int volume) => volume * Apu.VolumeStep;

    /// <summary>
    /// The core promise: [offset, offset + length) and nothing else. The slot's steps carry
    /// distinct volumes, so the PCM itself testifies which step each tick played — the peak of
    /// every tick is compared against the step the segment is supposed to be on, and after the
    /// last step the channel is idle.
    ///
    /// <para>Break recipe: in <c>AudioChannel.StartSegment</c> drop the <c>Step = firstStep</c>
    /// line — the first tick then peaks at step 0's volume instead of step 2's. In
    /// <c>Apu.AdvanceChannel</c> replace the <c>next &gt;= channel.SegmentEnd</c> stop with the
    /// ordinary <c>next &gt;= slot.Length</c> — the channel then sails past the segment's end
    /// and the "idle afterwards" assertion goes red.</para>
    /// </summary>
    [Fact]
    public void ASegmentPlaysExactlyItsStepsAndStops()
    {
        const int speed = 2;
        var apu = ChipWith(RampSlot(length: 8, speed: speed));

        apu.PlaySfx(0, 0, 2, 3);   // steps 2, 3, 4 — volumes 3, 4, 5
        for (int step = 2; step < 5; step++)
        {
            for (int tick = 0; tick < speed; tick++)
            {
                apu.RenderTick();
                Assert.Equal(Amp((step % 7) + 1), Peak(apu.Block));
            }
        }
        Assert.False(apu.IsChannelBusy(0), "the segment played its three steps; the channel must be free");

        apu.RenderTick();
        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// The loop is a property of whole-slot playback, not of a segment: the caller named an
    /// exact run of steps, and a loop crossing the window would make the count a lie and hold
    /// the channel forever. The control right beside it proves the loop itself works — the
    /// same slot through the two-argument call is still playing long after the segment died.
    ///
    /// <para>Break recipe: in <c>Apu.AdvanceChannel</c> apply the loop jump before the segment
    /// bound check (move the <c>slot.Loops</c> block out of the <c>else</c>) — the segment then
    /// loops forever and the first assertion goes red.</para>
    /// </summary>
    [Fact]
    public void ASegmentIgnoresTheSlotsLoop()
    {
        var looping = RampSlot(length: 4, speed: 1);
        looping.LoopStart = 0;
        looping.LoopEnd = 4;

        var segment = ChipWith(looping);
        segment.PlaySfx(0, 0, 0, 4);
        for (int tick = 0; tick < 10; tick++)
        {
            segment.RenderTick();
        }
        Assert.False(segment.IsChannelBusy(0), "a segment plays once; the loop must not catch it");

        var whole = ChipWith(looping);
        whole.PlaySfx(0, 0);
        for (int tick = 0; tick < 10; tick++)
        {
            whole.RenderTick();
        }
        Assert.True(whole.IsChannelBusy(0), "control: the two-argument call still honors the loop");
    }

    /// <summary>
    /// Soft edges, the same policy as every call on the surface (API-8 §1): an offset outside
    /// the played steps is nothing, a non-positive length is nothing, and nothing means the
    /// channel is not even claimed. An empty slot falls out of the same comparison, because no
    /// offset is inside a length of zero.
    /// </summary>
    [Fact]
    public void OffsetsOutsideTheSlotAndNonPositiveLengthsPlayNothing()
    {
        var apu = ChipWith(RampSlot(length: 4));

        apu.PlaySfx(0, 0, 4, 1);       // first step past the end
        apu.PlaySfx(0, 0, -1, 1);      // before the start
        apu.PlaySfx(0, 0, 0, 0);       // zero length
        apu.PlaySfx(0, 0, 0, -5);      // negative length
        apu.PlaySfx(1, 0, 0, 4);       // slot 1 is empty: length 0 has no inside
        Assert.False(apu.IsChannelBusy(0));

        apu.PlaySfx(0, 0, 3, 1);       // control: the last step is still inside
        Assert.True(apu.IsChannelBusy(0));
    }

    /// <summary>
    /// An overhanging segment is clipped to the slot, the way <c>DataToGfx</c> clips a copy:
    /// the steps that exist still play, the request for more is not an error. The length is
    /// int.MaxValue on purpose — the clip arithmetic must not overflow either.
    /// </summary>
    [Fact]
    public void AnOverhangingSegmentIsClippedToTheSlotsEnd()
    {
        var apu = ChipWith(RampSlot(length: 4, speed: 1));
        apu.PlaySfx(0, 0, 2, int.MaxValue);

        apu.RenderTick();   // step 2
        Assert.True(apu.IsChannelBusy(0));
        apu.RenderTick();   // step 3, the last one that exists
        Assert.False(apu.IsChannelBusy(0), "steps 2 and 3 exist, step 4 does not — two ticks and done");
    }

    /// <summary>
    /// One rule for -1, not one per overload (ADR-020): <c>Sfx(-1, ch, o, l)</c> stops the
    /// channel exactly as <c>Sfx(-1, ch)</c> does, the segment arguments ignored — and -2 stays
    /// a reserved no-op through this door too.
    /// </summary>
    [Fact]
    public void MinusOneStopsThroughTheSegmentOverloadToo()
    {
        var apu = ChipWith(RampSlot());
        apu.PlaySfx(0, 2);
        Assert.True(apu.IsChannelBusy(2));

        apu.PlaySfx(-2, 2, 0, 8);      // reserved: still a silent no-op
        Assert.True(apu.IsChannelBusy(2));

        apu.PlaySfx(-1, 2, 0, 8);
        Assert.False(apu.IsChannelBusy(2));
    }

    /// <summary>
    /// Auto-allocation is the two-argument call's, verbatim: lowest idle channel first. The
    /// assertion pins the channel the segment landed on, not merely that something plays.
    /// </summary>
    [Fact]
    public void ASegmentAutoAllocatesLikeThePlainCall()
    {
        var apu = ChipWith(RampSlot());
        apu.PlaySfx(0, 0);             // occupy channel 0
        apu.PlaySfx(0, -1, 2, 2);      // -1 must pick channel 1
        Assert.True(apu.IsChannelBusy(1));
        Assert.Equal(0, apu.ChannelSfx(1));
    }

    /// <summary>
    /// The equivalence that guards the old call: over a non-looping slot, a segment covering
    /// the whole slot renders the same PCM, tick for tick, as the two-argument call — same
    /// attack, same steps, same end. Anything the segment path did differently on its way
    /// through the synthesizer would surface here as a one-sample diff.
    /// </summary>
    [Fact]
    public void AWholeSlotSegmentSoundsExactlyLikeThePlainCall()
    {
        var plain = ChipWith(RampSlot(length: 6, speed: 3));
        var segmented = ChipWith(RampSlot(length: 6, speed: 3));

        plain.PlaySfx(0, 0);
        segmented.PlaySfx(0, 0, 0, 6);

        for (int tick = 0; tick < 6 * 3 + 4; tick++)
        {
            plain.RenderTick();
            segmented.RenderTick();
            Assert.Equal(plain.Block.Samples, segmented.Block.Samples);
        }
        Assert.False(plain.IsChannelBusy(0));
        Assert.False(segmented.IsChannelBusy(0));
    }
}

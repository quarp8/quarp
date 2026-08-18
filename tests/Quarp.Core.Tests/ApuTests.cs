using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The sound chip on its own: synthesis, the two sequencers, and the channel allocation rule.
///
/// <para>Nearly every assertion here comes with its opposite somewhere nearby — a silence test
/// paired with a "and here it is not silent", an equality paired with an inequality after one
/// byte of the bank changes. That is deliberate. The M2 post-mortem found a determinism check
/// that had been green for a milestone because the thing it compared had stopped changing;
/// a green assertion whose red state was never demonstrated is not evidence.</para>
/// </summary>
public class ApuTests
{
    private const int Note = 33;   // A4, 440 Hz

    private static AudioBank BankWith(SfxSlot template, int id = 0)
    {
        var bank = new AudioBank();
        bank.GetSfx(id).CopyFrom(template);
        return bank;
    }

    /// <summary>A slot of <paramref name="length"/> identical steps.</summary>
    private static SfxSlot Slot(
        Waveform wave = Waveform.Pulse50,
        int volume = 7,
        int note = Note,
        int speed = 1,
        int length = 1,
        NoteEffect effect = NoteEffect.None)
    {
        var slot = new SfxSlot { Speed = speed, Length = length };
        for (int i = 0; i < length; i++)
        {
            slot[i] = new SfxStep(note, wave, volume, effect);
        }
        return slot;
    }

    private static Apu ChipWith(SfxSlot slot, int id = 0)
    {
        var apu = new Apu();
        apu.LoadBank(BankWith(slot, id));
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

    /// <summary>Sign changes across the block — a cheap frequency probe that needs no FFT.</summary>
    private static int SignChanges(AudioBlock block)
    {
        int changes = 0;
        short[] s = block.Samples;
        for (int i = 1; i < s.Length; i++)
        {
            if ((s[i] < 0) != (s[i - 1] < 0))
            {
                changes++;
            }
        }
        return changes;
    }

    // --- silence, and the proof that the silence test can fail ---

    [Fact]
    public void AFreshChipRendersEightHundredZeroSamples()
    {
        var apu = new Apu();
        apu.RenderTick();
        Assert.Equal(800, apu.Block.Samples.Length);
        Assert.All(apu.Block.Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void AndTheSameChipIsNotSilentOnceSomethingPlays()
    {
        // The negative control for the test above: without it, "renders silence" would also
        // pass for a chip that renders silence unconditionally.
        Apu apu = ChipWith(Slot());
        apu.PlaySfx(0);
        apu.RenderTick();
        Assert.NotEqual(0, Peak(apu.Block));
    }

    [Fact]
    public void AnEmptySlotAndAnOutOfRangeIdBothDoNothing()
    {
        Apu apu = ChipWith(Slot(), id: 5);
        apu.PlaySfx(0);   // slot 0 was never filled: length 0
        apu.PlaySfx(64);
        apu.PlaySfx(-1);
        Assert.False(apu.IsChannelBusy(0));
        apu.PlaySfx(5);   // the slot that does exist
        Assert.True(apu.IsChannelBusy(0));
    }

    [Fact]
    public void AnOutOfRangeChannelIsIgnoredRatherThanWrappingToAValidOne()
    {
        Apu apu = ChipWith(Slot());
        apu.PlaySfx(0, 4);
        apu.PlaySfx(0, -2);
        for (int channel = 0; channel < Apu.ChannelCount; channel++)
        {
            Assert.False(apu.IsChannelBusy(channel));
        }
    }

    // --- synthesis ---

    [Fact]
    public void AFullVolumeChannelPeaksAtTheDocumentedAmplitude()
    {
        Apu apu = ChipWith(Slot(volume: 7));
        apu.PlaySfx(0);
        apu.RenderTick();
        Assert.Equal(Apu.PeakAmplitude, Peak(apu.Block));
        Assert.Equal(8190, Apu.PeakAmplitude);
    }

    [Fact]
    public void FourChannelsAtFullVolumeSumWithoutClipping()
    {
        // The reason VolumeStep is 1170 and not something rounder: four channels have to fit
        // inside a 16-bit sample, or the mix would clip depending on what else was playing.
        Apu apu = ChipWith(Slot());
        for (int channel = 0; channel < Apu.ChannelCount; channel++)
        {
            apu.PlaySfx(0, channel);
        }
        apu.RenderTick();
        Assert.Equal(4 * Apu.PeakAmplitude, Peak(apu.Block));
        Assert.True(4 * Apu.PeakAmplitude <= short.MaxValue);
    }

    [Fact]
    public void VolumeZeroIsARestEvenWithEverythingElseSet()
    {
        // Two steps, not one: a one-step slot at speed 1 has finished by the end of the very
        // tick it was rendered in, so the busy assertion below would fail on a chip that is
        // behaving perfectly. It did, at M3 acceptance.
        Apu apu = ChipWith(Slot(volume: 0, length: 2));
        apu.PlaySfx(0);
        apu.RenderTick();
        Assert.True(apu.IsChannelBusy(0));  // the channel is running...
        Assert.Equal(0, Peak(apu.Block));   // ...and making no sound
    }

    [Theory]
    [InlineData(Waveform.Pulse12, 14, 15)]
    [InlineData(Waveform.Pulse25, 14, 15)]
    [InlineData(Waveform.Pulse50, 14, 15)]
    [InlineData(Waveform.Triangle, 14, 15)]
    [InlineData(Waveform.Saw, 14, 15)]
    public void APitchedWaveRunsAtTheNotesFrequency(Waveform wave, int minChanges, int maxChanges)
    {
        // A4 = 440 Hz, one tick = 1/60 s, so 7.33 cycles per block, and every one of these
        // waves is symmetric about zero and therefore changes sign twice a cycle: 14-15.
        // The saw row said 7-8 until M3 acceptance measured 14, on the theory that a rising
        // ramp crosses zero once. It crosses twice — once climbing through zero mid-cycle and
        // once falling from +full to -full at the wrap. This probe reads frequency, not shape;
        // shape is PulseDutiesDifferFromEachOtherInTheObviousDirection's job.
        Apu apu = ChipWith(Slot(wave));
        apu.PlaySfx(0);
        apu.RenderTick();
        Assert.InRange(SignChanges(apu.Block), minChanges, maxChanges);
    }

    [Fact]
    public void AnOctaveUpDoublesTheNumberOfCycles()
    {
        // Independent of the table test: this checks the increment actually reaches the phase
        // accumulator, not just that the table holds the right number.
        Apu low = ChipWith(Slot(note: Note));
        Apu high = ChipWith(Slot(note: Note + 12));
        low.PlaySfx(0);
        high.PlaySfx(0);
        low.RenderTick();
        high.RenderTick();
        Assert.InRange(SignChanges(high.Block), (2 * SignChanges(low.Block)) - 2, (2 * SignChanges(low.Block)) + 2);
    }

    [Fact]
    public void PulseDutiesDifferFromEachOtherInTheObviousDirection()
    {
        // Time spent high: 12.5%, 25%, 50% of each cycle. Anything that swapped two duty
        // constants would pass a "makes a sound" test and fail this one.
        Assert.True(HighSamples(Waveform.Pulse12) < HighSamples(Waveform.Pulse25));
        Assert.True(HighSamples(Waveform.Pulse25) < HighSamples(Waveform.Pulse50));
        Assert.InRange(HighSamples(Waveform.Pulse50), 380, 420);   // half of 800
        Assert.InRange(HighSamples(Waveform.Pulse25), 180, 220);
        Assert.InRange(HighSamples(Waveform.Pulse12), 80, 120);

        static int HighSamples(Waveform wave)
        {
            Apu apu = ChipWith(Slot(wave));
            apu.PlaySfx(0);
            apu.RenderTick();
            int high = 0;
            foreach (short sample in apu.Block.Samples)
            {
                if (sample > 0)
                {
                    high++;
                }
            }
            return high;
        }
    }

    [Fact]
    public void NoiseIsDeterministicAndNeverDegeneratesToSilenceOrAConstant()
    {
        Apu first = ChipWith(Slot(Waveform.Noise, speed: 60, length: 1, note: 63));
        Apu second = ChipWith(Slot(Waveform.Noise, speed: 60, length: 1, note: 63));
        first.PlaySfx(0);
        second.PlaySfx(0);

        for (int tick = 0; tick < 30; tick++)
        {
            first.RenderTick();
            second.RenderTick();
            Assert.Equal(FrameHash.Compute(first.Block), FrameHash.Compute(second.Block));
        }

        // An LFSR seeded with zero is stuck at zero forever and would render a constant.
        Assert.True(SignChanges(first.Block) > 5, "the noise register looks stuck");
    }

    [Fact]
    public void NoiseAtALowerNoteChangesStateLessOften()
    {
        // Noise is clocked once per phase wrap, so the note controls how bright it is. If the
        // clock were free-running, these two would be indistinguishable.
        Assert.True(NoiseChanges(10) < NoiseChanges(60));

        static int NoiseChanges(int note)
        {
            Apu apu = ChipWith(Slot(Waveform.Noise, note: note, speed: 60));
            apu.PlaySfx(0);
            apu.RenderTick();
            return SignChanges(apu.Block);
        }
    }

    // --- step sequencing and effects ---

    [Fact]
    public void AnSfxEndsAfterLengthTimesSpeedTicks()
    {
        Apu apu = ChipWith(Slot(speed: 3, length: 4));
        apu.PlaySfx(0);
        for (int tick = 0; tick < 12; tick++)
        {
            Assert.True(apu.IsChannelBusy(0), $"channel went idle early, at tick {tick}");
            apu.RenderTick();
        }
        Assert.False(apu.IsChannelBusy(0));
    }

    [Fact]
    public void ALoopingSlotNeverEndsOnItsOwn()
    {
        var slot = Slot(speed: 1, length: 4);
        slot.LoopStart = 1;
        slot.LoopEnd = 3;
        Apu apu = ChipWith(slot);
        apu.PlaySfx(0);
        for (int tick = 0; tick < 500; tick++)
        {
            apu.RenderTick();
        }
        Assert.True(apu.IsChannelBusy(0));
    }

    [Fact]
    public void FadeInRisesAndFadeOutReachesExactlySilence()
    {
        int[] fadeIn = AmplitudeOverStep(NoteEffect.FadeIn);
        int[] fadeOut = AmplitudeOverStep(NoteEffect.FadeOut);

        Assert.True(fadeIn[0] < fadeIn[^1], "fade in did not rise");
        Assert.Equal(Apu.PeakAmplitude, fadeIn[^1]);
        Assert.True(fadeOut[0] > fadeOut[^1], "fade out did not fall");
        Assert.Equal(0, fadeOut[^1]);   // silence on the last tick, so chained steps do not click

        static int[] AmplitudeOverStep(NoteEffect effect)
        {
            Apu apu = ChipWith(Slot(speed: 8, length: 1, effect: effect));
            apu.PlaySfx(0);
            int[] peaks = new int[8];
            for (int tick = 0; tick < peaks.Length; tick++)
            {
                apu.RenderTick();
                peaks[tick] = Peak(apu.Block);
            }
            return peaks;
        }
    }

    [Fact]
    public void SlideMovesFromThePreviousStepsNoteTowardsThisOne()
    {
        var slot = new SfxSlot { Speed = 8, Length = 2 };
        slot[0] = new SfxStep(Note, Waveform.Pulse50, 7);
        slot[1] = new SfxStep(Note + 12, Waveform.Pulse50, 7, NoteEffect.Slide);
        Apu apu = ChipWith(slot);
        apu.PlaySfx(0);

        int[] cycles = new int[16];
        for (int tick = 0; tick < cycles.Length; tick++)
        {
            apu.RenderTick();
            cycles[tick] = SignChanges(apu.Block);
        }

        // 7.33 cycles fit in a block, so the crossing count alternates between 14 and 15 as the
        // phase walks — hence the tolerance of one on the two "same note" comparisons.
        Assert.InRange(cycles[7], cycles[0] - 1, cycles[0] + 1);   // step 0 holds its note
        Assert.InRange(cycles[8], cycles[0] - 1, cycles[0] + 1);   // the slide starts where it left off
        Assert.True(cycles[15] > cycles[8] + 4, "the slide did not climb");
    }

    [Fact]
    public void DropFallsAwayFromTheNote()
    {
        Apu apu = ChipWith(Slot(speed: 8, length: 1, effect: NoteEffect.Drop));
        apu.PlaySfx(0);
        apu.RenderTick();
        int first = SignChanges(apu.Block);
        for (int tick = 0; tick < 6; tick++)
        {
            apu.RenderTick();
        }
        Assert.True(SignChanges(apu.Block) < first, "drop did not fall");
    }

    [Fact]
    public void VibratoWobblesAroundTheNoteInsteadOfLeavingIt()
    {
        // The wobble is +/- a quarter tone: audible, but nowhere near a semitone. A vibrato
        // implemented by accumulating an offset would drift away instead of returning.
        Apu plain = ChipWith(Slot(speed: 64, length: 1));
        Apu wobbly = ChipWith(Slot(speed: 64, length: 1, effect: NoteEffect.Vibrato));
        plain.PlaySfx(0);
        wobbly.PlaySfx(0);

        bool differed = false;
        for (int tick = 0; tick < 32; tick++)
        {
            plain.RenderTick();
            wobbly.RenderTick();
            if (FrameHash.Compute(plain.Block) != FrameHash.Compute(wobbly.Block))
            {
                differed = true;
            }
            Assert.InRange(SignChanges(wobbly.Block), SignChanges(plain.Block) - 2, SignChanges(plain.Block) + 2);
        }
        Assert.True(differed, "vibrato changed nothing at all");
    }

    [Fact]
    public void ArpeggioCyclesTheFourStepsOfItsGroup()
    {
        var slot = new SfxSlot { Speed = 16, Length = 4 };
        slot[0] = new SfxStep(Note, Waveform.Pulse50, 7, NoteEffect.Arpeggio);
        slot[1] = new SfxStep(Note + 4, Waveform.Pulse50, 7, NoteEffect.Arpeggio);
        slot[2] = new SfxStep(Note + 7, Waveform.Pulse50, 7, NoteEffect.Arpeggio);
        slot[3] = new SfxStep(Note + 12, Waveform.Pulse50, 7, NoteEffect.Arpeggio);
        Apu apu = ChipWith(slot);
        apu.PlaySfx(0);

        var distinct = new HashSet<int>();
        for (int tick = 0; tick < 8; tick++)
        {
            apu.RenderTick();
            distinct.Add(SignChanges(apu.Block));
        }
        // Four notes, two ticks each: eight ticks must show more than one pitch.
        Assert.True(distinct.Count >= 3, $"arpeggio produced {distinct.Count} distinct pitches");
    }

    // --- channel allocation ---

    [Fact]
    public void AutomaticAllocationFillsChannelsLowestFirstThenGivesUp()
    {
        Apu apu = ChipWith(Slot(speed: 255, length: 32));
        for (int expected = 0; expected < Apu.ChannelCount; expected++)
        {
            apu.PlaySfx(0);
            Assert.True(apu.IsChannelBusy(expected), $"channel {expected} should have been chosen");
        }

        // All four busy with the cartridge's own sounds: the fifth call does nothing rather
        // than cutting one of them off in an order nobody could predict.
        int[] before = Snapshot(apu);
        apu.PlaySfx(0);
        Assert.Equal(before, Snapshot(apu));

        static int[] Snapshot(Apu apu)
        {
            int[] state = new int[Apu.ChannelCount];
            for (int i = 0; i < state.Length; i++)
            {
                state[i] = apu.ChannelSfx(i);
            }
            return state;
        }
    }

    [Fact]
    public void AnExplicitChannelTakesItFromWhateverWasThere()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 255, length: 32));
        bank.GetSfx(2).CopyFrom(Slot(speed: 255, length: 32, note: Note + 5));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlaySfx(1, 2);
        Assert.Equal(1, apu.ChannelSfx(2));
        apu.PlaySfx(2, 2);
        Assert.Equal(2, apu.ChannelSfx(2));
    }

    // --- music ---

    [Fact]
    public void APatternPlaysChannelNOnChannelN()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 8, length: 32));
        bank.GetSfx(2).CopyFrom(Slot(speed: 8, length: 32, note: Note + 3));
        bank.SetPattern(0, new MusicPattern(-1, 1, -1, 2, MusicFlags.Stop));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlayMusic(0);
        Assert.Equal(-1, apu.ChannelSfx(0));
        Assert.Equal(1, apu.ChannelSfx(1));
        Assert.Equal(-1, apu.ChannelSfx(2));
        Assert.Equal(2, apu.ChannelSfx(3));
        Assert.True(apu.IsChannelMusic(1));
    }

    [Fact]
    public void MusicSkipsAChannelTheCartridgeIsUsingAndTakesItBackNextPattern()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 1, length: 8));    // the song
        bank.GetSfx(2).CopyFrom(Slot(speed: 1, length: 2));    // a short cartridge beep
        bank.SetPattern(0, new MusicPattern(1, -1, -1, -1));
        bank.SetPattern(1, new MusicPattern(1, -1, -1, -1, MusicFlags.Stop));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlaySfx(2, 0);            // the cartridge grabs channel 0 first
        apu.PlayMusic(0);
        Assert.Equal(2, apu.ChannelSfx(0));      // music did not evict it

        // Pattern 0 lasts MinPatternTicks (its 8-tick slot is shorter than the floor).
        for (int tick = 0; tick < Apu.MinPatternTicks; tick++)
        {
            apu.RenderTick();
        }
        Assert.Equal(1, apu.CurrentPattern);
        Assert.Equal(1, apu.ChannelSfx(0));      // the beep ended, pattern 1 took the channel
        Assert.True(apu.IsChannelMusic(0));
    }

    [Fact]
    public void SfxWithoutAChannelBorrowsFromMusicRatherThanFallingSilent()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 255, length: 32));
        bank.GetSfx(2).CopyFrom(Slot(speed: 255, length: 32));
        bank.SetPattern(0, new MusicPattern(1, 1, 1, 1));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlayMusic(0);
        apu.PlaySfx(2);      // every channel is the music's; rule 2 says take the lowest
        Assert.Equal(2, apu.ChannelSfx(0));
        Assert.False(apu.IsChannelMusic(0));
        Assert.True(apu.IsChannelMusic(1));
    }

    [Fact]
    public void MusicWalksPatternsInOrderUntilAStopFlag()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 1, length: 4));
        bank.SetPattern(0, new MusicPattern(1, -1, -1, -1));
        bank.SetPattern(1, new MusicPattern(1, -1, -1, -1));
        bank.SetPattern(2, new MusicPattern(1, -1, -1, -1, MusicFlags.Stop));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlayMusic(0);
        Assert.Equal(0, apu.CurrentPattern);
        RunTicks(apu, Apu.MinPatternTicks);
        Assert.Equal(1, apu.CurrentPattern);
        RunTicks(apu, Apu.MinPatternTicks);
        Assert.Equal(2, apu.CurrentPattern);
        RunTicks(apu, Apu.MinPatternTicks);
        Assert.False(apu.IsMusicPlaying);
        Assert.Equal(Apu.NoPattern, apu.CurrentPattern);
    }

    [Fact]
    public void LoopEndReturnsToTheRememberedLoopStart()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 1, length: 4));
        bank.SetPattern(0, new MusicPattern(1, -1, -1, -1));                        // intro
        bank.SetPattern(1, new MusicPattern(1, -1, -1, -1, MusicFlags.LoopStart));
        bank.SetPattern(2, new MusicPattern(1, -1, -1, -1, MusicFlags.LoopEnd));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlayMusic(0);
        RunTicks(apu, Apu.MinPatternTicks);
        Assert.Equal(1, apu.CurrentPattern);
        RunTicks(apu, Apu.MinPatternTicks);
        Assert.Equal(2, apu.CurrentPattern);
        RunTicks(apu, Apu.MinPatternTicks);
        Assert.Equal(1, apu.CurrentPattern);   // back to the loop start, not to the intro
        Assert.True(apu.IsMusicPlaying);
    }

    [Fact]
    public void MusicStopsSilencesItsChannelsButNotTheCartridges()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 255, length: 32));
        bank.GetSfx(2).CopyFrom(Slot(speed: 255, length: 32));
        bank.SetPattern(0, new MusicPattern(1, 1, -1, -1));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlayMusic(0);
        apu.PlaySfx(2, 3);
        apu.PlayMusic(-1);

        Assert.False(apu.IsChannelBusy(0));
        Assert.False(apu.IsChannelBusy(1));
        Assert.Equal(2, apu.ChannelSfx(3));
        Assert.False(apu.IsMusicPlaying);
    }

    [Fact]
    public void AnEmptyPatternIsARestOfTheDocumentedLengthRatherThanAZeroLengthSpin()
    {
        // If a pattern of nothing had length 0 the sequencer would walk all 64 patterns inside
        // one tick. The floor is what makes that impossible.
        var bank = new AudioBank();
        bank.SetPattern(0, default);
        bank.SetPattern(1, new MusicPattern(-1, -1, -1, -1, MusicFlags.Stop));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlayMusic(0);
        RunTicks(apu, Apu.MinPatternTicks - 1);
        Assert.Equal(0, apu.CurrentPattern);
        apu.RenderTick();
        Assert.Equal(1, apu.CurrentPattern);
    }

    // --- determinism ---

    [Fact]
    public void TwoChipsFedTheSameCallsProduceIdenticalSampleBlocks()
    {
        var hashesA = new List<ulong>();
        var hashesB = new List<ulong>();
        Run(hashesA);
        Run(hashesB);
        Assert.Equal(hashesA, hashesB);
        Assert.True(hashesA.Count > 1);

        static void Run(List<ulong> hashes)
        {
            var bank = new AudioBank();
            bank.GetSfx(1).CopyFrom(Slot(Waveform.Noise, speed: 4, length: 8));
            bank.GetSfx(2).CopyFrom(Slot(Waveform.Saw, speed: 3, length: 6, effect: NoteEffect.Slide));
            bank.SetPattern(0, new MusicPattern(1, 2, -1, -1, MusicFlags.LoopEnd));
            var apu = new Apu();
            apu.LoadBank(bank);
            apu.PlayMusic(0);
            for (int tick = 0; tick < 120; tick++)
            {
                if (tick % 37 == 0)
                {
                    apu.PlaySfx(2);
                }
                apu.RenderTick();
                hashes.Add(FrameHash.Compute(apu.Block));
            }
        }
    }

    [Fact]
    public void ChangingOneStepOfOneSlotChangesTheSamples()
    {
        // The negative control for the test above, and the same shape as the milestone's
        // acceptance criterion: break one step on purpose and watch the audio hash move.
        ulong original = HashRun(Note);
        ulong altered = HashRun(Note + 1);
        Assert.NotEqual(original, altered);

        static ulong HashRun(int note)
        {
            Apu apu = ChipWith(Slot(note: note, speed: 4, length: 8));
            apu.PlaySfx(0);
            ulong hash = 0;
            for (int tick = 0; tick < 32; tick++)
            {
                apu.RenderTick();
                hash ^= FrameHash.Compute(apu.Block);
            }
            return hash;
        }
    }

    [Fact]
    public void ResetPutsTheChipBackWhereABootStartsIt()
    {
        Apu apu = ChipWith(Slot(speed: 8, length: 32));
        apu.PlaySfx(0);
        RunTicks(apu, 5);
        Assert.NotEqual(0, Peak(apu.Block));

        apu.Reset();
        Assert.False(apu.IsChannelBusy(0));
        Assert.False(apu.IsMusicPlaying);
        Assert.All(apu.Block.Samples, s => Assert.Equal(0, s));

        // And the bank survives the reset — it is cartridge data, not state.
        apu.PlaySfx(0);
        Assert.True(apu.IsChannelBusy(0));
    }

    [Fact]
    public void AChipReplayedFromResetRepeatsItselfExactly()
    {
        Apu apu = ChipWith(Slot(Waveform.Noise, speed: 2, length: 16));
        Assert.Equal(Pass(apu), Pass(apu));

        static ulong Pass(Apu apu)
        {
            apu.Reset();
            apu.PlaySfx(0);
            ulong hash = 0;
            for (int tick = 0; tick < 40; tick++)
            {
                apu.RenderTick();
                hash = (hash * 31) ^ FrameHash.Compute(apu.Block);
            }
            return hash;
        }
    }

    private static void RunTicks(Apu apu, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            apu.RenderTick();
        }
    }
}

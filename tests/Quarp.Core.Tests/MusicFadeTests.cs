using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// <c>Music(pattern, fadeTicks, channelMask)</c> (ADR-037): the linear music fade and the
/// channel reservation mask. Two invariants stand over everything here: with fade 0 and mask 0
/// the call is the old <c>Music(pattern)</c> to the bit — the guard every recorded audio hash
/// stands on — and the fade itself is in the PCM, because a fade the hash cannot see is a fade
/// a replay cannot prove.
/// </summary>
public class MusicFadeTests
{
    private const int Note = 33;   // A4, 440 Hz

    private static SfxSlot Slot(int speed = 8, int length = 32, int volume = 7, int note = Note)
    {
        var slot = new SfxSlot { Speed = speed, Length = length };
        for (int i = 0; i < length; i++)
        {
            slot[i] = new SfxStep(note, Waveform.Pulse50, volume, NoteEffect.None);
        }
        return slot;
    }

    /// <summary>A one-pattern song: slot 1 on channel 0, held forever by a loop-end flag.</summary>
    private static Apu SongChip()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot());
        bank.SetPattern(0, new MusicPattern(1, -1, -1, -1, MusicFlags.LoopEnd));
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

    private const int FullPeak = 7 * Apu.VolumeStep;

    /// <summary>
    /// The compatibility anchor stated as PCM rather than as a claim: <c>PlayMusic(p)</c> and
    /// <c>PlayMusic(p, 0, 0)</c> render identical samples for a hundred ticks. The one-argument
    /// call forwards through the three-argument one, so this is the test that the forwarding —
    /// and the gain guard at unity — cost nothing.
    ///
    /// <para>Break recipe: in <c>Apu.ClearFade</c> set <c>_musicGain</c> to
    /// <c>GainUnity - 1</c> instead of <c>GainUnity</c> — every music amplitude is then scaled
    /// by 255/256 and the sample comparison goes red on the first sounding tick. (The
    /// three-argument chip drifts identically, so it is the no-fade <em>level</em> that reddens
    /// first, in <see cref="AFadeInRampsUpToFullVolume"/>'s final equality.)</para>
    /// </summary>
    [Fact]
    public void MusicWithZeroFadeAndZeroMaskIsTheOldCallBitForBit()
    {
        var old = SongChip();
        var extended = SongChip();
        old.PlayMusic(0);
        extended.PlayMusic(0, 0, 0);

        for (int tick = 0; tick < 100; tick++)
        {
            old.RenderTick();
            extended.RenderTick();
            Assert.Equal(old.Block.Samples, extended.Block.Samples);
        }
        Assert.Equal(FullPeak, Peak(old.Block));   // and it is not a comparison of two silences
    }

    /// <summary>
    /// The fade-in: gain climbs the closed formula (t+1)/F from near silence to exactly full
    /// volume, monotonically, and from the moment the ramp lands the chip is bit-identical to
    /// one that never faded — same gain, same sequencer position, so the fade leaves no scar.
    ///
    /// <para>Break recipe: in <c>Apu.FadeGain</c> replace <c>_fadeTick + 1</c> with
    /// <c>_fadeTick</c> — the first tick is then fully silent and the first-tick assertion
    /// goes red; replace the fade-in branch with <c>GainUnity</c> — the ramp vanishes and the
    /// monotonic-strict assertion at the midpoint goes red.</para>
    /// </summary>
    [Fact]
    public void AFadeInRampsUpToFullVolume()
    {
        const int fade = 64;
        var faded = SongChip();
        var plain = SongChip();
        faded.PlayMusic(0, fade);
        plain.PlayMusic(0);

        int previous = -1;
        var peaks = new int[fade];
        for (int tick = 0; tick < fade; tick++)
        {
            faded.RenderTick();
            plain.RenderTick();
            peaks[tick] = Peak(faded.Block);
            Assert.True(peaks[tick] >= previous, $"tick {tick}: the ramp went down");
            previous = peaks[tick];
        }
        Assert.True(peaks[0] > 0, "the first tick is quiet, not absent — the pattern starts now");
        Assert.True(peaks[0] < peaks[fade / 2], "the midpoint must be louder than the start");
        Assert.True(peaks[fade / 2] < peaks[fade - 1], "the end must be louder than the midpoint");
        Assert.Equal(FullPeak, peaks[fade - 1]);

        for (int tick = 0; tick < 32; tick++)
        {
            faded.RenderTick();
            plain.RenderTick();
            Assert.Equal(plain.Block.Samples, faded.Block.Samples);
        }
    }

    /// <summary>
    /// The fade-out: <c>Music(-1, F)</c> ramps the playing song from its current gain to
    /// silence over F ticks and only then stops it — the song keeps sequencing under the ramp,
    /// the last faded tick is already silent, and after it the music is gone and its channel
    /// free. Celeste's big chest, at last.
    ///
    /// <para>Break recipe: in <c>Apu.Advance</c> delete the <c>StopMusic(); return;</c> arm —
    /// the gain sits at 0 but <c>IsMusicPlaying</c> never falls and the final assertions go
    /// red. In <c>Apu.PlayMusic</c> drop the <c>_musicPlaying</c> condition from the fade-out
    /// branch — <see cref="AFadeOutWithNoMusicJustStops"/> goes red instead.</para>
    /// </summary>
    [Fact]
    public void AFadeOutRampsToSilenceAndThenStops()
    {
        const int fade = 32;
        var apu = SongChip();
        apu.PlayMusic(0);
        for (int tick = 0; tick < 16; tick++)
        {
            apu.RenderTick();
        }

        apu.PlayMusic(-1, fade);
        int previous = int.MaxValue;
        for (int tick = 0; tick < fade; tick++)
        {
            Assert.True(apu.IsMusicPlaying, $"tick {tick}: the song must survive its own fade");
            apu.RenderTick();
            int peak = Peak(apu.Block);
            Assert.True(peak <= previous, $"tick {tick}: the ramp went up");
            Assert.True(peak < FullPeak, $"tick {tick}: the ramp must already be below full volume");
            previous = peak;
        }
        Assert.Equal(0, previous);   // the last faded tick is silence
        Assert.False(apu.IsMusicPlaying, "the ramp landed; the song is over");
        Assert.False(apu.IsChannelBusy(0), "and the voice it was driving is free");
    }

    /// <summary>A fade-out with nothing playing is a stop of nothing: no throw, no state.</summary>
    [Fact]
    public void AFadeOutWithNoMusicJustStops()
    {
        var apu = SongChip();
        apu.PlayMusic(-1, 100);
        Assert.False(apu.IsMusicPlaying);
        apu.RenderTick();
        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// The fade scales the music's voices and nothing else. With F = 1 the music's gain is 0 on
    /// the very tick of the call, so a chip playing "cartridge SFX plus instantly-faded music"
    /// must render the same samples as a chip playing the SFX alone — if the fade so much as
    /// grazed a cartridge channel, these blocks would differ.
    /// </summary>
    [Fact]
    public void TheFadeLeavesCartridgeChannelsAlone()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot());
        bank.GetSfx(2).CopyFrom(Slot(note: Note + 7, volume: 5));
        bank.SetPattern(0, new MusicPattern(1, -1, -1, -1, MusicFlags.LoopEnd));

        var sfxOnly = new Apu();
        sfxOnly.LoadBank(bank);
        sfxOnly.PlaySfx(2, 3);

        var both = new Apu();
        both.LoadBank(bank);
        both.PlaySfx(2, 3);
        both.PlayMusic(0);
        both.PlayMusic(-1, 1);   // gain 0 immediately: the song's last tick is already silent

        sfxOnly.RenderTick();
        both.RenderTick();
        Assert.Equal(sfxOnly.Block.Samples, both.Block.Samples);
        Assert.True(Peak(sfxOnly.Block) > 0, "control: the cartridge sound is actually sounding");
    }

    /// <summary>A new song started mid-fade-out replaces the fade with its own start.</summary>
    [Fact]
    public void ANewSongCancelsAFadeOut()
    {
        var apu = SongChip();
        apu.PlayMusic(0);
        apu.RenderTick();
        apu.PlayMusic(-1, 100);
        apu.RenderTick();
        Assert.True(Peak(apu.Block) < FullPeak, "control: the fade-out was really under way");

        apu.PlayMusic(0);
        apu.RenderTick();
        Assert.True(apu.IsMusicPlaying);
        Assert.Equal(FullPeak, Peak(apu.Block));
    }

    /// <summary>Same calls, same ticks, same PCM — the fade is a function of the tick counter, not of anything else.</summary>
    [Fact]
    public void TheSameFadeTwiceRendersTheSamePcm()
    {
        var first = SongChip();
        var second = SongChip();
        foreach (var apu in new[] { first, second })
        {
            apu.PlayMusic(0, 24);
            for (int tick = 0; tick < 30; tick++)
            {
                apu.RenderTick();
            }
            apu.PlayMusic(-1, 24);
        }
        for (int tick = 0; tick < 30; tick++)
        {
            first.RenderTick();
            second.RenderTick();
            Assert.Equal(first.Block.Samples, second.Block.Samples);
        }
    }

    // --- the channel mask ---

    /// <summary>A two-voice song on channels 0 and 1, held forever.</summary>
    private static Apu TwoVoiceChip()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot());
        bank.GetSfx(2).CopyFrom(Slot(note: Note + 4));
        bank.GetSfx(3).CopyFrom(LoopingBeep());
        bank.SetPattern(0, new MusicPattern(1, 2, -1, -1, MusicFlags.LoopEnd));
        var apu = new Apu();
        apu.LoadBank(bank);
        return apu;
    }

    /// <summary>An endless cartridge sound, for proving the music never takes a channel back.</summary>
    private static SfxSlot LoopingBeep()
    {
        var slot = Slot(speed: 1, length: 4, volume: 3, note: Note - 5);
        slot.LoopStart = 0;
        slot.LoopEnd = 4;
        return slot;
    }

    /// <summary>
    /// The mask reserves channels for the music: bit i = channel i, and a voice on a channel
    /// outside the mask is never started — not at the first pattern and, the half that matters,
    /// not at any later pattern either, where the unmasked sequencer would have taken the
    /// channel back from the cartridge.
    ///
    /// <para>Break recipe: in <c>Apu.StartPattern</c> delete the mask block — channel 1 gets
    /// the music voice at the first pattern and the second assertion goes red; keep the block
    /// but drop its <c>continue</c> — same effect one pattern later, caught by the post-turnover
    /// assertions.</para>
    /// </summary>
    [Fact]
    public void TheMaskKeepsMusicOffTheCartridgesChannels()
    {
        var apu = TwoVoiceChip();
        apu.PlayMusic(0, 0, 0b0001);
        Assert.True(apu.IsChannelMusic(0));
        Assert.False(apu.IsChannelBusy(1), "channel 1 is outside the mask: its voice must not start");

        apu.PlaySfx(3, -1);   // the cartridge's endless beep auto-picks the freed channel 1
        Assert.Equal(3, apu.ChannelSfx(1));
        Assert.False(apu.IsChannelMusic(1));

        for (int tick = 0; tick < 300; tick++)   // across a pattern turnover (256-tick slots)
        {
            apu.RenderTick();
        }
        Assert.True(apu.IsMusicPlaying);
        Assert.Equal(3, apu.ChannelSfx(1));
        Assert.False(apu.IsChannelMusic(1), "the next pattern must not steal a masked-out channel");
    }

    /// <summary>
    /// The skipped voice still counts toward the pattern's length: song timing depends on the
    /// score, never on who happens to be playing — the same rule a voice skipped for a busy
    /// channel already follows, and the rule that keeps a rewound run turning its patterns on
    /// the same ticks.
    ///
    /// <para>Break recipe: in <c>Apu.StartPattern</c> move the mask block above the
    /// <c>slot.LengthTicks</c> comparison — the masked 256-tick voice stops counting, the
    /// pattern collapses to the 32-tick floor, and the "still on pattern 0" assertion goes red
    /// at tick 100.</para>
    /// </summary>
    [Fact]
    public void AMaskedVoiceStillCountsTowardPatternLength()
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: 1, length: 4));   // 4 audible ticks on channel 0
        bank.GetSfx(2).CopyFrom(Slot());                      // 256 silent-by-mask ticks on channel 1
        bank.SetPattern(0, new MusicPattern(1, 2, -1, -1));
        bank.SetPattern(1, new MusicPattern(1, -1, -1, -1, MusicFlags.Stop));
        var apu = new Apu();
        apu.LoadBank(bank);

        apu.PlayMusic(0, 0, 0b0001);
        Assert.False(apu.IsChannelBusy(1), "control: the mask really kept channel 1 silent");
        for (int tick = 0; tick < 100; tick++)
        {
            apu.RenderTick();
        }
        Assert.Equal(0, apu.CurrentPattern);   // 100 > the 32-tick floor: only the masked slot explains this
        for (int tick = 100; tick < 256; tick++)
        {
            apu.RenderTick();
        }
        Assert.Equal(1, apu.CurrentPattern);   // and the turnover lands exactly where the score says
    }

    /// <summary>
    /// The mask is an argument of the song, not a mode of the chip: the next
    /// <c>Music(pattern, ...)</c> replaces it, releasing what the old mask held and taking what
    /// the new one names. Masking also survives the documented masking rule — bits above the
    /// four channels are dropped, so 0b10001 is 0b0001, not a fifth channel.
    /// </summary>
    [Fact]
    public void TheNextSongReplacesTheMask()
    {
        var apu = TwoVoiceChip();
        apu.PlayMusic(0, 0, 0b10001);          // upper bit dropped: mask is channel 0
        Assert.True(apu.IsChannelMusic(0));
        Assert.False(apu.IsChannelBusy(1));

        apu.PlayMusic(0, 0, 0b0010);           // the same song, now reserved to channel 1
        Assert.False(apu.IsChannelBusy(0), "the old mask's channel must be let go");
        Assert.True(apu.IsChannelMusic(1));

        apu.PlayMusic(0, 0, 0);                // and zero is all four again
        Assert.True(apu.IsChannelMusic(0));
        Assert.True(apu.IsChannelMusic(1));
    }
}

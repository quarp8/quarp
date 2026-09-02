using System.Text;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The binary audio banks (docs/AUDIO-FORMAT.md §2-§5). Two things are pinned here: the exact
/// bytes the format promises — the worked example of §9 is asserted byte for byte, so the
/// document cannot drift away from the code — and every rejection rule.
///
/// <para>Every rejection case starts from a payload that is asserted to be <b>valid</b> and then
/// breaks exactly one thing. Without that negative control a validation test proves only that
/// some byte array throws, which a typo in the fixture would satisfy just as well.</para>
/// </summary>
public class AudioFormatTests
{
    /// <summary>The §9 example: slot 0, speed 3, three steps, no loop.</summary>
    private static byte[] SamplePayload()
    {
        byte[] payload = AudioFormat.EmptySfxPayload();
        AudioFormat.WriteSlotHeader(payload, 0, speed: 3, length: 3, loopStart: 0, loopEnd: 0);
        // C-6 sqr 6 -    E-6 sqr 5 -    G-6 sqr 4 fadeout
        AudioFormat.WriteStep(payload, 0, 0, AudioFormat.PackStep(48, AudioFormat.WavePulse50, 6, AudioFormat.EffectNone));
        AudioFormat.WriteStep(payload, 0, 1, AudioFormat.PackStep(52, AudioFormat.WavePulse50, 5, AudioFormat.EffectNone));
        AudioFormat.WriteStep(payload, 0, 2, AudioFormat.PackStep(55, AudioFormat.WavePulse50, 4, AudioFormat.EffectFadeOut));
        return payload;
    }

    /// <summary>A small song: one instrument, two order entries, one pattern with a note in it.</summary>
    private static byte[] SampleMusicPayload()
    {
        byte[] payload = MusicFormat.EmptyPayload();
        MusicFormat.WriteInstrument(payload, 0, slot: 2, root: 24, flags: 0, speed: 0);
        MusicFormat.WriteOrder(payload, 0, pattern: 0, flags: MusicFormat.OrderLoopStart, target: 0, transpose: 0);
        MusicFormat.WriteOrder(payload, 1, pattern: 0, flags: MusicFormat.OrderLoopBack, target: 0, transpose: 5);
        MusicFormat.WritePattern(payload, 0, speed: MusicFormat.DefaultRowSpeed, rows: 4);
        MusicFormat.WriteCell(payload, 0, 0, 0, MusicFormat.PackCell(
            24, MusicFormat.NoteOn, 0, true, 7, true, MusicFormat.EffectNone, 0));
        MusicFormat.WritePreamble(payload, 2);
        return payload;
    }

    // --- sizes and the documented bytes ---

    [Fact]
    public void SizesAreTheOnesTheSpecPromises()
    {
        Assert.Equal(4352, AudioFormat.SfxPayloadSize);
        Assert.Equal(4360, AudioFormat.SfxFileSize);
        Assert.Equal(33800, MusicFormat.PayloadSize);
        Assert.Equal(33808, MusicFormat.FileSize);
    }

    [Fact]
    public void SfxFileMatchesTheWorkedExampleByteForByte()
    {
        byte[] file = AudioFormat.WriteSfxFile(SamplePayload());

        Assert.Equal(4360, file.Length);
        Assert.Equal("QSFX", Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal(new byte[] { 0x00, 0x00 }, file[4..6]);
        Assert.Equal(64, file[6]);
        Assert.Equal(32, file[7]);
        Assert.Equal(new byte[] { 0x03, 0x03, 0x00, 0x00 }, file[8..12]);
        // Step table starts at 8 + 256 = 0x108; three little-endian words.
        Assert.Equal(new byte[] { 0xb0, 0x0c, 0xb4, 0x0a, 0xb7, 0x58 }, file[0x108..0x10e]);
        // Everything else is zero: 252 bytes of empty slot headers and the unused steps.
        Assert.All(file[12..0x108], b => Assert.Equal(0, b));
        Assert.All(file[0x10e..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void MusicFileCarriesTheSharedHeaderAndThenTheSong()
    {
        byte[] file = AudioFormat.WriteMusicFile(SampleMusicPayload());

        Assert.Equal(33808, file.Length);
        Assert.Equal("QMUS", Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal(new byte[] { 0x00, 0x00 }, file[4..6]);
        Assert.Equal(64, file[6]);
        Assert.Equal(4, file[7]);

        // The payload's own preamble: layout 0, 32 rows, 64 instruments, order length 2.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x20, 0x40, 0x02, 0x00, 0x00, 0x00 }, file[8..16]);
    }

    [Fact]
    public void StepFieldsSurviveEveryCombination()
    {
        for (int note = 0; note <= AudioFormat.MaxNote; note++)
        {
            for (int wave = 0; wave < AudioFormat.WaveCount; wave++)
            {
                for (int volume = 0; volume <= AudioFormat.MaxVolume; volume++)
                {
                    for (int effect = 0; effect < AudioFormat.EffectCount; effect++)
                    {
                        ushort word = AudioFormat.PackStep(note, wave, volume, effect);
                        Assert.Equal(note, AudioFormat.Note(word));
                        Assert.Equal(wave, AudioFormat.Wave(word));
                        Assert.Equal(volume, AudioFormat.Volume(word));
                        Assert.Equal(effect, AudioFormat.Effect(word));
                        Assert.Equal(0, word & 0x8000);
                    }
                }
            }
        }
    }

    [Fact]
    public void StepWordsAreLittleEndianWhateverTheMachineIs()
    {
        byte[] payload = AudioFormat.EmptySfxPayload();
        AudioFormat.WriteSlotHeader(payload, 5, speed: 1, length: 1, loopStart: 0, loopEnd: 0);
        AudioFormat.WriteStep(payload, 5, 0, 0x1234);
        int offset = AudioFormat.StepOffset(5, 0);
        Assert.Equal(0x34, payload[offset]);
        Assert.Equal(0x12, payload[offset + 1]);
        Assert.Equal(0x1234, AudioFormat.Step(payload, 5, 0));
    }

    // --- the empty bank is a valid bank ---

    [Fact]
    public void AnAllZeroBankIsValidAndMeansSilence()
    {
        byte[] sfx = AudioFormat.EmptySfxPayload();
        AudioFormat.ValidateSfxPayload(sfx, "sfx.bin");
        Assert.Equal(0, AudioFormat.SlotLength(sfx, 0));
        Assert.Equal(0, AudioFormat.SlotLength(sfx, 63));

        byte[] music = AudioFormat.EmptyMusicPayload();
        MusicFormat.ValidatePayload(music, "music.bin");
        Assert.Equal(0, MusicFormat.OrderLength(music));
        Assert.Equal(0, MusicFormat.PatternRows(music, 0));
    }

    [Fact]
    public void FilesRoundTripThroughWriteAndParse()
    {
        byte[] sfx = SamplePayload();
        Assert.Equal(sfx, AudioFormat.ParseSfxFile(AudioFormat.WriteSfxFile(sfx), "sfx.bin"));

        byte[] music = SampleMusicPayload();
        Assert.Equal(music, AudioFormat.ParseMusicFile(AudioFormat.WriteMusicFile(music), "music.bin"));
    }

    // --- header rejections (each starts from a file that parses) ---

    [Fact]
    public void RejectsAForeignMagic()
    {
        byte[] file = AudioFormat.WriteSfxFile(SamplePayload());
        AudioFormat.ParseSfxFile(file, "sfx.bin");           // control: this file is fine

        file[1] = (byte)'M';
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ParseSfxFile(file, "sfx.bin"));
        Assert.Contains("QSFX", e.Message);
    }

    /// <summary>
    /// One living version and no other (ADR-041): the numbers this project itself once wrote (1
    /// for the SFX bank and the pattern list, 2 for the song) are refused exactly like a number
    /// from the future.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public void RejectsEveryVersionButZero(byte version)
    {
        byte[] file = AudioFormat.WriteSfxFile(SamplePayload());
        AudioFormat.ParseSfxFile(file, "sfx.bin");           // control: this file is fine

        file[4] = version;
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ParseSfxFile(file, "sfx.bin"));
        Assert.Contains($"version {version}", e.Message);
    }

    [Fact]
    public void RejectsForeignGeometry()
    {
        byte[] file = AudioFormat.WriteSfxFile(SamplePayload());
        AudioFormat.ParseSfxFile(file, "sfx.bin");

        file[6] = 32;   // a bank of 32 slots: some other profile, not this one
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ParseSfxFile(file, "sfx.bin"));
        Assert.Contains("32 slots", e.Message);
    }

    [Fact]
    public void RejectsATruncatedFile()
    {
        byte[] file = AudioFormat.WriteSfxFile(SamplePayload());
        AudioFormat.ParseSfxFile(file, "sfx.bin");

        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ParseSfxFile(file[..4359], "sfx.bin"));
        Assert.Contains("4360", e.Message);
        Assert.Throws<CartLoadException>(() => AudioFormat.ParseSfxFile(file[..3], "sfx.bin"));
    }

    // --- payload rejections ---

    [Fact]
    public void RejectsTheReservedStepBit()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");    // control

        payload[AudioFormat.StepOffset(0, 1) + 1] |= 0x80;
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("slot 0 step 1", e.Message);
        Assert.Contains("bit 15", e.Message);
    }

    [Fact]
    public void RejectsAWaveProfileEightDoesNotHave()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");

        // Wave 6 in bits 6-8 of step 0; the pack helper refuses to build it, which is the point.
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioFormat.PackStep(0, 6, 1, 0));
        AudioFormat.WriteStep(payload, 0, 0, (ushort)(AudioFormat.Step(payload, 0, 0) | (0b110 << 6)));
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("wave 6", e.Message);
    }

    [Fact]
    public void RejectsAnEffectProfileEightDoesNotHave()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");

        AudioFormat.WriteStep(payload, 0, 0, (ushort)(AudioFormat.Step(payload, 0, 0) | (0b111 << 12)));
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("effect 7", e.Message);
    }

    [Fact]
    public void RejectsASilentStepThatStillNamesANote()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");    // control

        // Volume 0 silences the step, so the note, wave and effect left in the word can never be
        // heard — and what cannot be heard must not be free to change the bytes, exactly as with
        // a music channel that is off but still remembers a slot. Otherwise two banks that sound
        // identical compare unequal and the cartridge identity moves for an inaudible edit.
        AudioFormat.WriteStep(payload, 0, 1,
            AudioFormat.PackStep(52, AudioFormat.WavePulse50, 0, AudioFormat.EffectNone));
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("slot 0 step 1", e.Message);
        Assert.Contains("volume 0", e.Message);

        // Control the other way round: the canonical spelling of that same rest loads.
        AudioFormat.WriteStep(payload, 0, 1, 0);
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");
    }

    [Fact]
    public void RejectsAStepTheSlotNeverPlays()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");    // control

        // Slot 0 plays three steps. Step 3 is stored and never heard — not by the sequencer and
        // not through an arpeggio group either, since those are clipped to the slot's length —
        // so it obeys the same canonicity rule.
        AudioFormat.WriteStep(payload, 0, 3,
            AudioFormat.PackStep(52, AudioFormat.WavePulse50, 5, AudioFormat.EffectNone));
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("slot 0 step 3", e.Message);

        // An unused slot is the same rule with a length of zero: all 32 of its steps are dead.
        AudioFormat.WriteStep(payload, 0, 3, 0);
        AudioFormat.WriteStep(payload, 9, 0,
            AudioFormat.PackStep(52, AudioFormat.WavePulse50, 5, AudioFormat.EffectNone));
        var unused = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("slot 9 step 0", unused.Message);

        // Control: with both words back to zero the payload is legal again.
        AudioFormat.WriteStep(payload, 9, 0, 0);
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");
    }

    [Fact]
    public void ARestInsideASlotIsLegalAndSurvivesTheFileRoundTrip()
    {
        // The other half of the two rules above: a rest written the one legal way is ordinary
        // data, and a bank holding one goes through write and parse unchanged.
        byte[] payload = SamplePayload();
        AudioFormat.WriteSlotHeader(payload, 0, speed: 3, length: 4, loopStart: 0, loopEnd: 0);
        AudioFormat.WriteStep(payload, 0, 3, 0);

        byte[] file = AudioFormat.WriteSfxFile(payload);
        Assert.Equal(payload, AudioFormat.ParseSfxFile(file, "sfx.bin"));
        Assert.Equal(0, AudioFormat.Step(payload, 0, 3));
    }

    [Fact]
    public void RejectsAnEmptySlotThatIsNotEmpty()
    {
        byte[] payload = AudioFormat.EmptySfxPayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");

        payload[AudioFormat.SlotHeaderOffset(7)] = 4;   // speed set, length still 0
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("slot 7", e.Message);
    }

    [Fact]
    public void RejectsSpeedZeroOnASlotThatPlays()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");

        payload[AudioFormat.SlotHeaderOffset(0)] = 0;
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("speed 0", e.Message);
    }

    [Fact]
    public void RejectsALengthOverThirtyTwo()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");

        payload[AudioFormat.SlotHeaderOffset(0) + 1] = 33;
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("length 33", e.Message);
    }

    [Theory]
    [InlineData(0, 4)]      // loop end past the slot's length of 3
    [InlineData(2, 2)]      // empty range
    [InlineData(2, 1)]      // inverted
    public void RejectsALoopOutsideTheSlot(int loopStart, int loopEnd)
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");

        payload[AudioFormat.SlotHeaderOffset(0) + 2] = (byte)loopStart;
        payload[AudioFormat.SlotHeaderOffset(0) + 3] = (byte)loopEnd;
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("slot 0", e.Message);
    }

    [Fact]
    public void AcceptsALoopInsideTheSlot()
    {
        byte[] payload = SamplePayload();
        payload[AudioFormat.SlotHeaderOffset(0) + 2] = 1;
        payload[AudioFormat.SlotHeaderOffset(0) + 3] = 3;
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");
        Assert.Equal(1, AudioFormat.SlotLoopStart(payload, 0));
        Assert.Equal(3, AudioFormat.SlotLoopEnd(payload, 0));
    }

    [Fact]
    public void RejectsALoopStartWithoutAnEnd()
    {
        byte[] payload = SamplePayload();
        AudioFormat.ValidateSfxPayload(payload, "sfx.bin");

        payload[AudioFormat.SlotHeaderOffset(0) + 2] = 1;
        var e = Assert.Throws<CartLoadException>(() => AudioFormat.ValidateSfxPayload(payload, "sfx.bin"));
        Assert.Contains("loop start 1", e.Message);
    }
}

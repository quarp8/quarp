using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The music bank as bytes (docs/AUDIO-FORMAT.md §4, §5, §9; ADR-040, ADR-041): the layout
/// the document promises, the worked hexdump asserted byte for byte, and every rule
/// <see cref="MusicFormat.ValidatePayload"/> enforces.
///
/// <para>Every rejection case starts from <see cref="SampleSong"/>, which is asserted valid in
/// <see cref="TheSampleSongIsValid"/> and re-asserted at the top of each test through
/// <see cref="Broken"/>, then breaks exactly one byte. Without that negative control a
/// validation test proves only that <em>some</em> array throws — a typo in the fixture would
/// satisfy it just as well. This is the same discipline
/// <see cref="AudioFormatTests"/> applies to the SFX bank and the shared file header.</para>
/// </summary>
public class MusicFormatTests
{
    /// <summary>
    /// The song of §9: two instruments, a two-entry order that loops, and one 4-row pattern at
    /// 7.5 ticks a row — a tempo the old pattern list could not express at all.
    /// </summary>
    private static byte[] SampleSong()
    {
        byte[] payload = MusicFormat.EmptyPayload();
        MusicFormat.WriteInstrument(payload, 0, slot: 2, root: 24, flags: 0, speed: 0);
        MusicFormat.WriteInstrument(payload, 1, slot: 3, root: 36, flags: MusicFormat.InstrumentOnce, speed: 4);

        MusicFormat.WriteOrder(payload, 0, pattern: 0, MusicFormat.OrderLoopStart, target: 0, transpose: 0);
        MusicFormat.WriteOrder(payload, 1, pattern: 0, MusicFormat.OrderLoopBack, target: 0, transpose: 5);

        MusicFormat.WritePattern(payload, 0, speed: 240, rows: 4);
        MusicFormat.WriteCell(payload, 0, 0, 0, MusicFormat.PackCell(
            24, MusicFormat.NoteOn, 0, true, 7, true, MusicFormat.EffectNone, 0));
        MusicFormat.WriteCell(payload, 0, 0, 1, MusicFormat.PackCell(
            36, MusicFormat.NoteOn, 1, true, 0, false, MusicFormat.EffectArpeggio, 0x47));
        MusicFormat.WriteCell(payload, 0, 2, 0, MusicFormat.PackCell(
            31, MusicFormat.NoteOn, 0, false, 0, false, MusicFormat.EffectSlide, 6));
        MusicFormat.WriteCell(payload, 0, 3, 1, MusicFormat.PackCell(
            0, MusicFormat.NoteOff, 0, false, 0, false, MusicFormat.EffectNone, 0));

        MusicFormat.WritePreamble(payload, orderLength: 2);
        return payload;
    }

    /// <summary>
    /// The sample, valid, with one mutation applied — the negative control every rejection test
    /// is built on. The assert before the mutation is the point: it fails loudly if the fixture
    /// itself rots, instead of letting a broken fixture pass as a rejection.
    /// </summary>
    private static byte[] Broken(Action<byte[]> mutate)
    {
        byte[] payload = SampleSong();
        MusicFormat.ValidatePayload(payload, "control");
        mutate(payload);
        return payload;
    }

    private static string Rejected(byte[] payload) =>
        Assert.Throws<CartLoadException>(() => MusicFormat.ValidatePayload(payload, "music.bin")).Message;

    // --- the layout the document promises ---

    [Fact]
    public void SizesAndOffsetsAreTheOnesTheSpecPromises()
    {
        Assert.Equal(0, MusicFormat.Version);
        Assert.Equal(32, MusicFormat.RowCount);
        Assert.Equal(64, MusicFormat.InstrumentCount);
        Assert.Equal(128, MusicFormat.OrderCount);

        Assert.Equal(8, MusicFormat.InstrumentTableOffset);
        Assert.Equal(264, MusicFormat.OrderTableOffset);
        Assert.Equal(776, MusicFormat.PatternTableOffset);
        Assert.Equal(1032, MusicFormat.CellTableOffset);
        Assert.Equal(32768, MusicFormat.CellTableSize);

        Assert.Equal(33800, MusicFormat.PayloadSize);
        Assert.Equal(33808, MusicFormat.FileSize);
    }

    /// <summary>
    /// One pattern row is four cells = 16 bytes and starts on a 16-byte boundary <em>of the
    /// file</em> — the 8-byte header plus the 1032-byte offset of the cell table land the first
    /// cell at 1040 — so a song reads in a hex editor the way it reads in the tracker (§4). It
    /// is a claim about the layout, so it is asserted rather than trusted.
    ///
    /// <para>Break recipe: change <see cref="MusicFormat.CellSize"/> to 2, or add a byte to the
    /// preamble without adding one elsewhere — a row stops being one hexdump line and this
    /// fails.</para>
    /// </summary>
    [Fact]
    public void OneRowIsExactlyOneHexdumpLine()
    {
        Assert.Equal(16, AudioFormat.MusicChannelCount * MusicFormat.CellSize);
        Assert.Equal(
            MusicFormat.CellOffset(0, 0, 0) + 16,
            MusicFormat.CellOffset(0, 1, 0));
        Assert.Equal(1040, AudioFormat.HeaderSize + MusicFormat.CellTableOffset);
        Assert.Equal(0, (AudioFormat.HeaderSize + MusicFormat.CellTableOffset) % 16);
    }

    /// <summary>
    /// The cell is a single little-endian u32, and it is read back through
    /// <see cref="BinaryPrimitives"/> rather than through a struct overlay — the promise that a
    /// bank means the same thing on windows-x64 and on linux-arm64.
    /// </summary>
    [Fact]
    public void ACellIsWrittenLittleEndian()
    {
        byte[] payload = MusicFormat.EmptyPayload();
        MusicFormat.WriteCell(payload, 5, 7, 2, 0x11223344u);

        int at = MusicFormat.CellOffset(5, 7, 2);
        Assert.Equal(new byte[] { 0x44, 0x33, 0x22, 0x11 }, payload[at..(at + 4)]);
        Assert.Equal(0x11223344u, MusicFormat.Cell(payload, 5, 7, 2));
    }

    /// <summary>
    /// Every one of the 32 bits of a cell survives the round trip through the packer, and each
    /// field lands where §4 says it lands. Pinned field by field because the bit budget is
    /// exactly 32: there is no spare bit to absorb an off-by-one shift.
    /// </summary>
    [Fact]
    public void EveryCellFieldRoundTripsThroughItsOwnBits()
    {
        uint cell = MusicFormat.PackCell(
            note: 63, noteKind: MusicFormat.NoteOn, instrument: 63, hasInstrument: true,
            volume: 7, hasVolume: true, effect: 3, param: 255);

        Assert.Equal(63, MusicFormat.CellNote(cell));
        Assert.Equal(MusicFormat.NoteOn, MusicFormat.CellNoteKind(cell));
        Assert.Equal(63, MusicFormat.CellInstrument(cell));
        Assert.True(MusicFormat.CellHasInstrument(cell));
        Assert.Equal(7, MusicFormat.CellVolume(cell));
        Assert.True(MusicFormat.CellHasVolume(cell));
        Assert.Equal(3, MusicFormat.CellEffect(cell));
        Assert.Equal(255, MusicFormat.CellParam(cell));

        // Every bit of the word is spoken for, and no two fields share one: the eight field masks
        // sum to 2^32 exactly. A spare bit would be a field a later version starts using quietly,
        // which is what the version number exists to prevent; an overlap would be a packing bug
        // no single-field round trip could see.
        long note = 0x3F, kind = 0x03L << 6, instrument = 0x3FL << 8, hasInstrument = 1L << 14;
        long volume = 0x07L << 15, hasVolume = 1L << 18, effect = 0x1FL << 19, param = 0xFFL << 24;
        Assert.Equal(
            1L << 32,
            note + kind + instrument + hasInstrument + volume + hasVolume + effect + param + 1);
    }

    /// <summary>An empty cell is the zero word and nothing else — the whole canonicity argument in one assert.</summary>
    [Fact]
    public void TheEmptyCellIsTheZeroWord()
    {
        uint empty = MusicFormat.PackCell(0, MusicFormat.NoteNone, 0, false, 0, false, 0, 0);

        Assert.Equal(0u, empty);
        Assert.Equal(MusicFormat.NoteNone, MusicFormat.CellNoteKind(0u));
    }

    /// <summary>
    /// An all-zero payload is <b>not</b> a song: its geometry echoes are zero. The empty song is built,
    /// which is why <see cref="MusicFormat.EmptyPayload"/> exists instead of <c>new byte[N]</c>.
    /// </summary>
    [Fact]
    public void TheEmptyPayloadIsBuiltRatherThanZeroed()
    {
        byte[] empty = MusicFormat.EmptyPayload();
        MusicFormat.ValidatePayload(empty, "empty.bin");

        Assert.Equal(MusicFormat.Version, MusicFormat.Layout(empty));
        Assert.Equal(0, MusicFormat.OrderLength(empty));

        // The geometry echoes are what an all-zero array gets wrong: layout 0 is now legal, the
        // rows and instruments echoes are not zero, and that is what EmptyPayload is for.
        Assert.Contains("0 rows x 0 instruments", Rejected(new byte[MusicFormat.PayloadSize]));
    }

    // --- the worked example of §9, byte for byte ---

    [Fact]
    public void TheSampleSongIsValid() => MusicFormat.ValidatePayload(SampleSong(), "sample.bin");

    /// <summary>
    /// §9 asserted against the code, so the document cannot drift away from the format. Every
    /// number here is one a reader can find in the hexdump of that section.
    /// </summary>
    [Fact]
    public void TheFileMatchesTheWorkedExampleByteForByte()
    {
        byte[] file = AudioFormat.WriteMusicFile(SampleSong());

        Assert.Equal(33808, file.Length);
        Assert.Equal("QMUS", Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal(new byte[] { 0x00, 0x00 }, file[4..6]);
        Assert.Equal(64, file[6]);
        Assert.Equal(4, file[7]);

        // preamble: layout 0, 32 rows, 64 instruments, order length 2, reserved 0
        Assert.Equal(new byte[] { 0x00, 0x00, 0x20, 0x40, 0x02, 0x00, 0x00, 0x00 }, file[8..16]);
        // instrument 0: slot 2, root 24 (C-4), no flags, slot's own speed
        Assert.Equal(new byte[] { 0x02, 0x18, 0x00, 0x00 }, file[16..20]);
        // instrument 1: slot 3, root 36, "once", speed 4
        Assert.Equal(new byte[] { 0x03, 0x24, 0x01, 0x04 }, file[20..24]);

        int order = AudioFormat.HeaderSize + MusicFormat.OrderTableOffset;
        // entry 0: pattern 0, loop-start; entry 1: pattern 0, loop-back, +5 semitones
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, file[order..(order + 4)]);
        Assert.Equal(new byte[] { 0x00, 0x02, 0x00, 0x05 }, file[(order + 4)..(order + 8)]);

        int pattern = AudioFormat.HeaderSize + MusicFormat.PatternTableOffset;
        // pattern 0: speed 240/32 = 7.5 ticks a row, 4 rows, reserved 0
        Assert.Equal(new byte[] { 0xF0, 0x00, 0x04, 0x00 }, file[pattern..(pattern + 4)]);

        int cells = AudioFormat.HeaderSize + MusicFormat.CellTableOffset;
        // row 0, one hexdump line: C-4 00 7 --- | C-5 01 - arp:47 | . | .
        Assert.Equal(
            new byte[]
            {
                0x58, 0xC0, 0x07, 0x00,
                0x64, 0x41, 0x08, 0x47,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            },
            file[cells..(cells + 16)]);
    }

    /// <summary>
    /// The cell of §9 taken apart bit by bit, the way the document takes it apart. Pinned so the
    /// prose and the packer cannot disagree about which bit is which.
    /// </summary>
    [Fact]
    public void TheDocumentedCellDecodesFieldByField()
    {
        uint cell = 0x47084164u;

        Assert.Equal(36, MusicFormat.CellNote(cell));
        Assert.Equal(MusicFormat.NoteOn, MusicFormat.CellNoteKind(cell));
        Assert.Equal(1, MusicFormat.CellInstrument(cell));
        Assert.True(MusicFormat.CellHasInstrument(cell));
        Assert.False(MusicFormat.CellHasVolume(cell));
        Assert.Equal(MusicFormat.EffectArpeggio, MusicFormat.CellEffect(cell));
        Assert.Equal(0x47, MusicFormat.CellParam(cell));
    }

    // --- the file header, and the one door behind it ---

    /// <summary>
    /// One 8-byte header, one version word, one body: a song round-trips through
    /// <see cref="AudioFormat.WriteMusicFile"/> and <see cref="AudioFormat.ParseMusicFile"/>
    /// unchanged, and the version it carries is 0 (ADR-041).
    /// </summary>
    [Fact]
    public void ASongGoesThroughTheDoorAndComesBackUnchanged()
    {
        byte[] file = AudioFormat.WriteMusicFile(SampleSong());

        Assert.Equal(MusicFormat.FileSize, file.Length);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4, 2)));
        Assert.Equal(SampleSong(), AudioFormat.ParseMusicFile(file, "music.bin"));
    }

    /// <summary>
    /// A well-formed header over a body of the old 320-byte pattern list is a truncated file, not
    /// a song read leniently. §7's promise is a phrase, not an <c>IndexOutOfRangeException</c>.
    /// </summary>
    [Fact]
    public void AHeaderOverAPatternListBodyIsRefusedByLength()
    {
        byte[] file = new byte[AudioFormat.HeaderSize + 320];
        "QMUS"u8.CopyTo(file);
        file[6] = AudioFormat.MusicPatternCount;
        file[7] = AudioFormat.MusicChannelCount;

        string message = Assert.Throws<CartLoadException>(
            () => AudioFormat.ParseMusicFile(file, "music.bin")).Message;
        Assert.Contains("328", message);
        Assert.Contains("33808", message);
    }

    /// <summary>Any version but 0 is refused with both numbers, not guessed at (§7, ADR-041).</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AVersionThatIsNotZeroIsRefusedWithBothNumbers(int version)
    {
        byte[] file = AudioFormat.WriteMusicFile(SampleSong());
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4, 2), (ushort)version);

        string message = Assert.Throws<CartLoadException>(
            () => AudioFormat.ParseMusicFile(file, "music.bin")).Message;
        Assert.Contains($"version {version}", message);
        Assert.Contains("version 0", message);
    }

    // --- preamble ---

    [Fact]
    public void ThePayloadLayoutWordHasToAgreeWithTheFileHeader()
    {
        string message = Rejected(Broken(p => BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0, 2), 2)));
        Assert.Contains("layout 2", message);
    }

    [Fact]
    public void ThePayloadGeometryEchoIsCheckedAgainstTheProfile()
    {
        Assert.Contains("16 rows", Rejected(Broken(p => p[2] = 16)));
        Assert.Contains("instrument", Rejected(Broken(p => p[3] = 32)));
    }

    [Fact]
    public void ThePreambleReservedWordMustBeZero()
    {
        Assert.Contains("reserved", Rejected(Broken(p => p[6] = 1)));
    }

    [Fact]
    public void AnOrderLongerThanTheTableIsRefused()
    {
        Assert.Contains("129", Rejected(Broken(p => BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), 129))));
    }

    // --- instruments ---

    [Fact]
    public void AnInstrumentPointingPastTheSfxBankIsRefused()
    {
        Assert.Contains("instrument 0", Rejected(Broken(p => p[MusicFormat.InstrumentOffset(0)] = 64)));
    }

    [Fact]
    public void AnInstrumentRootedOutsideTheNoteRangeIsRefused()
    {
        Assert.Contains("root note 64", Rejected(Broken(p => p[MusicFormat.InstrumentOffset(0) + 1] = 64)));
    }

    [Fact]
    public void ReservedInstrumentFlagBitsMustBeZero()
    {
        Assert.Contains("reserved", Rejected(Broken(p => p[MusicFormat.InstrumentOffset(0) + 2] = 0x02)));
    }

    // --- order ---

    /// <summary>
    /// The rule the whole format turns on, in its order-table form: what nobody can hear must not
    /// be able to change the bytes. An entry past the song's length is never played, so it has to
    /// be zeros, or two identical-sounding banks would compare as different and a cartridge's
    /// identity would move when an author trimmed an order they had already stopped using.
    ///
    /// <para>Break recipe: delete the <c>i &gt;= orderLength</c> arm of
    /// <c>MusicFormat.ValidateOrder</c> — a leftover entry stops being an error and this
    /// fails. Run to red 2026-09-01.</para>
    /// </summary>
    [Fact]
    public void AnOrderEntryPastTheSongMustBeAllZeros()
    {
        Assert.Contains("never played", Rejected(Broken(p => p[MusicFormat.OrderOffset(2)] = 1)));
        Assert.Contains("never played", Rejected(Broken(p => p[MusicFormat.OrderOffset(9) + 3] = 0xFF)));
    }

    [Fact]
    public void AnOrderEntryNamingAPatternThatDoesNotExistIsRefused()
    {
        Assert.Contains("pattern 64", Rejected(Broken(p => p[MusicFormat.OrderOffset(0)] = 64)));
    }

    [Fact]
    public void ReservedOrderFlagBitsMustBeZero()
    {
        Assert.Contains("reserved", Rejected(Broken(p => p[MusicFormat.OrderOffset(0) + 1] = 0x10)));
    }

    /// <summary>
    /// An entry that does not jump carries no target — the same canonicity rule as the SFX bank's silent
    /// channel that still remembers a slot (§4).
    /// </summary>
    [Fact]
    public void AnEntryThatDoesNotJumpMustCarryNoTarget()
    {
        Assert.Contains("target", Rejected(Broken(p => p[MusicFormat.OrderOffset(0) + 2] = 1)));
    }

    [Fact]
    public void AJumpOutsideTheSongIsRefused()
    {
        string message = Rejected(Broken(p =>
        {
            p[MusicFormat.OrderOffset(0) + 1] = MusicFormat.OrderJump;
            p[MusicFormat.OrderOffset(0) + 2] = 7;
        }));
        Assert.Contains("jumps to 7", message);
    }

    /// <summary>A jump inside the song is legal — the negative control for the test above.</summary>
    [Fact]
    public void AJumpInsideTheSongIsAccepted()
    {
        byte[] payload = SampleSong();
        payload[MusicFormat.OrderOffset(1) + 1] = MusicFormat.OrderJump;
        payload[MusicFormat.OrderOffset(1) + 2] = 0;

        MusicFormat.ValidatePayload(payload, "music.bin");
        Assert.Equal(MusicFormat.OrderJump, MusicFormat.OrderFlags(payload, 1));
    }

    /// <summary>Transpose is a signed byte, and both ends of it survive the trip through the file.</summary>
    [Fact]
    public void TransposeIsSignedAndSurvivesBothEnds()
    {
        byte[] payload = SampleSong();
        MusicFormat.WriteOrder(payload, 0, 0, MusicFormat.OrderLoopStart, 0, MusicFormat.MinTranspose);
        MusicFormat.WriteOrder(payload, 1, 0, MusicFormat.OrderLoopBack, 0, MusicFormat.MaxTranspose);
        MusicFormat.ValidatePayload(payload, "music.bin");

        Assert.Equal(-64, MusicFormat.OrderTranspose(payload, 0));
        Assert.Equal(63, MusicFormat.OrderTranspose(payload, 1));
        Assert.Equal(0xC0, payload[MusicFormat.OrderOffset(0) + 3]);
    }

    // --- patterns ---

    [Fact]
    public void APatternLongerThanTheRowTableIsRefused()
    {
        Assert.Contains("33 rows", Rejected(Broken(p => p[MusicFormat.PatternOffset(0) + 2] = 33)));
    }

    [Fact]
    public void TheReservedByteOfAPatternHeaderMustBeZero()
    {
        Assert.Contains("reserved", Rejected(Broken(p => p[MusicFormat.PatternOffset(0) + 3] = 1)));
    }

    /// <summary>An unused pattern is a zero record: no rows means no speed either (canonicity again).</summary>
    [Fact]
    public void AnUnusedPatternMustCarryNoSpeed()
    {
        Assert.Contains(
            "0 rows",
            Rejected(Broken(p => BinaryPrimitives.WriteUInt16LittleEndian(
                p.AsSpan(MusicFormat.PatternOffset(5), 2), 256))));
    }

    /// <summary>
    /// A row shorter than a tick cannot be heard as a row — registers land on tick boundaries
    /// (SPEC-8 §7) — so the fraction buys an exact average tempo, not sub-tick events.
    /// </summary>
    [Fact]
    public void ARowShorterThanOneTickIsRefused()
    {
        string message = Rejected(Broken(p => BinaryPrimitives.WriteUInt16LittleEndian(
            p.AsSpan(MusicFormat.PatternOffset(0), 2), 31)));
        Assert.Contains("at least one tick", message);
    }

    // --- cells ---

    /// <summary>
    /// A row past the pattern's length is never heard, so it must be the zero word — the same
    /// argument the SFX bank makes about a step past a slot's <c>length</c> (§2).
    ///
    /// <para>Break recipe: delete the <c>row &gt;= rows</c> arm of
    /// <c>MusicFormat.ValidateCells</c> — an unplayed row stops being checked and this fails.
    /// Run to red 2026-09-01.</para>
    /// </summary>
    [Fact]
    public void ARowPastThePatternsLengthMustBeZero()
    {
        string message = Rejected(Broken(p => MusicFormat.WriteCell(p, 0, 4, 0, 1u)));
        Assert.Contains("never heard", message);
        Assert.Contains("row 4", message);
    }

    [Fact]
    public void TheReservedNoteKindIsRefused()
    {
        Assert.Contains("note kind 3", Rejected(Broken(p => MusicFormat.WriteCell(p, 0, 1, 0, 3u << 6))));
    }

    /// <summary>A cell that plays no note carries no note number — canonicity in the cell word.</summary>
    [Fact]
    public void ACellWithoutANoteMustCarryNoNoteNumber()
    {
        Assert.Contains("note field must be 0", Rejected(Broken(p => MusicFormat.WriteCell(p, 0, 1, 0, 5u))));
        // A note off is the same rule: it silences, so its note field means nothing.
        Assert.Contains(
            "note field must be 0",
            Rejected(Broken(p => MusicFormat.WriteCell(p, 0, 1, 0, ((uint)MusicFormat.NoteOff << 6) | 5u))));
    }

    [Fact]
    public void ACellWithoutAnInstrumentMustCarryNoInstrumentNumber()
    {
        Assert.Contains(
            "instrument field must be 0",
            Rejected(Broken(p => MusicFormat.WriteCell(p, 0, 1, 0, 7u << 8))));
    }

    [Fact]
    public void ACellWithoutAVolumeMustCarryNoVolume()
    {
        Assert.Contains(
            "volume field must be 0",
            Rejected(Broken(p => MusicFormat.WriteCell(p, 0, 1, 0, 3u << 15))));
    }

    /// <summary>
    /// Effects 4..31 are refused rather than ignored. That refusal is what lets a later build add
    /// effect 4 without a version bump: no bank in the wild can already contain one, so nothing
    /// silently changes meaning (§7).
    /// </summary>
    [Fact]
    public void AnEffectThisBuildDoesNotDefineIsRefused()
    {
        Assert.Contains("effect 4", Rejected(Broken(p => MusicFormat.WriteCell(
            p, 0, 1, 0, (4u << 19) | (1u << 24)))));
        Assert.Contains("effect 31", Rejected(Broken(p => MusicFormat.WriteCell(
            p, 0, 1, 0, (31u << 19) | (1u << 24)))));
    }

    [Fact]
    public void NoEffectMeansNoParameter()
    {
        Assert.Contains("parameter must be 0", Rejected(Broken(p => MusicFormat.WriteCell(p, 0, 1, 0, 9u << 24))));
    }

    /// <summary>
    /// An effect with parameter 0 does nothing, so it has exactly one spelling — no effect. Two
    /// ways to write silence would be two byte patterns that sound alike.
    /// </summary>
    [Fact]
    public void AnEffectWithAZeroParameterIsRefused()
    {
        Assert.Contains("does nothing", Rejected(Broken(p => MusicFormat.WriteCell(
            p, 0, 1, 0, (uint)MusicFormat.EffectCut << 19))));
    }

    /// <summary>A slide glides to the cell's own note, so a cell carrying one has to carry a note.</summary>
    [Fact]
    public void ASlideWithoutANoteIsRefused()
    {
        Assert.Contains("has to carry one", Rejected(Broken(p => MusicFormat.WriteCell(
            p, 0, 1, 0, ((uint)MusicFormat.EffectSlide << 19) | (4u << 24)))));
    }

    // --- canonicity and determinism, end to end ---

    /// <summary>
    /// Two files that sound the same are the same file. Built twice from the same description,
    /// the payload is byte-identical; and every rejection rule above exists to make the converse
    /// true as well.
    /// </summary>
    [Fact]
    public void BuildingTheSameSongTwiceGivesTheSameBytes()
    {
        Assert.Equal(SampleSong(), SampleSong());
        Assert.Equal(AudioFormat.WriteMusicFile(SampleSong()), AudioFormat.WriteMusicFile(SampleSong()));
    }

    /// <summary>
    /// The payload survives a trip through a file and back with every byte in place — the property
    /// the loader, the packager and <c>--check</c> all lean on.
    /// </summary>
    [Fact]
    public void APayloadSurvivesAFileRoundTrip()
    {
        byte[] payload = SampleSong();

        Assert.Equal(payload, AudioFormat.ParseMusicFile(AudioFormat.WriteMusicFile(payload), "music.bin"));
    }

    /// <summary>
    /// A truncated or overlong file is refused by length before anything reads a field, so a
    /// short read can never be mistaken for a song of zeros.
    /// </summary>
    [Fact]
    public void AFileOfTheWrongLengthIsRefusedBeforeAnyFieldIsRead()
    {
        byte[] file = AudioFormat.WriteMusicFile(SampleSong());

        Assert.Throws<CartLoadException>(() => AudioFormat.ParseMusicFile(file[..^1], "music.bin"));
        Assert.Throws<CartLoadException>(() => AudioFormat.ParseMusicFile([.. file, 0], "music.bin"));
    }
}

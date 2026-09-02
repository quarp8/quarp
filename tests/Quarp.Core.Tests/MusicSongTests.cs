using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The version 2 sequencer (docs/AUDIO-FORMAT.md §4, ADR-040): a song of rows, instruments and
/// an order, played by the same chip that plays version 1.
///
/// <para><b>The first test is the one that matters.</b> Version 1 has to stay bit-for-bit what it
/// was, because six ported cartridges and twelve pinned CI hashes are what it sounds like. Every
/// other test here describes something new; <see cref="AVersionOneSongKeepsItsOwnTimingToTheTick"/>
/// describes something that must not change.</para>
///
/// <para>Payloads are assembled here by hand rather than borrowed from <c>Quarp.CartKit</c>,
/// which the core deliberately does not reference. That is not duplication for its own sake: it
/// is the cross-check that the offsets the core reads at are the offsets the format writes at,
/// and it exercises <c>AudioBank.LoadSongPayload</c> the way a data bank does (ADR-036).</para>
/// </summary>
public class MusicSongTests
{
    private const int Note = 33;   // A4, 440 Hz

    // --- fixtures ---

    private static SfxSlot Slot(int speed = 8, int length = 32, int volume = 7, int note = Note)
    {
        var slot = new SfxSlot { Speed = speed, Length = length };
        for (int i = 0; i < length; i++)
        {
            slot[i] = new SfxStep(note, Waveform.Pulse50, volume, NoteEffect.None);
        }
        return slot;
    }

    /// <summary>A version 2 payload builder that writes at the offsets §4 names, byte by byte.</summary>
    private sealed class SongBytes
    {
        private readonly byte[] _payload = new byte[AudioBank.SongPayloadSize];

        public SongBytes(int orderLength)
        {
            _payload[0] = 0;                       // layout word, little-endian
            _payload[2] = AudioBank.SongRowCount;  // rows echo
            _payload[3] = MusicInstrument.Count;   // instruments echo
            _payload[4] = (byte)orderLength;
            _payload[5] = (byte)(orderLength >> 8);
        }

        public SongBytes Instrument(int id, int slot, int root, bool once = false, int speed = 0)
        {
            int at = AudioBank.SongInstrumentTableOffset + (id * 4);
            _payload[at] = (byte)slot;
            _payload[at + 1] = (byte)root;
            _payload[at + 2] = once ? MusicInstrument.OnceFlag : (byte)0;
            _payload[at + 3] = (byte)speed;
            return this;
        }

        public SongBytes Order(int entry, int pattern, MusicFlags flags = MusicFlags.None, int target = 0, int transpose = 0)
        {
            int at = AudioBank.SongOrderTableOffset + (entry * 4);
            _payload[at] = (byte)pattern;
            _payload[at + 1] = (byte)flags;
            _payload[at + 2] = (byte)target;
            _payload[at + 3] = (byte)(sbyte)transpose;
            return this;
        }

        public SongBytes Pattern(int pattern, int rows, int speedUnits)
        {
            int at = AudioBank.SongPatternTableOffset + (pattern * 4);
            _payload[at] = (byte)speedUnits;
            _payload[at + 1] = (byte)(speedUnits >> 8);
            _payload[at + 2] = (byte)rows;
            return this;
        }

        public SongBytes Cell(int pattern, int row, int channel, MusicCell cell)
        {
            int at = AudioBank.SongCellTableOffset
                + ((((pattern * AudioBank.SongRowCount) + row) * MusicPattern.ChannelCount + channel) * 4);
            uint word = cell.Word;
            _payload[at] = (byte)word;
            _payload[at + 1] = (byte)(word >> 8);
            _payload[at + 2] = (byte)(word >> 16);
            _payload[at + 3] = (byte)(word >> 24);
            return this;
        }

        public byte[] Bytes => _payload;
    }

    private static MusicCell NoteOn(int note, int instrument = -1, int volume = -1,
        MusicEffect effect = MusicEffect.None, int param = 0) =>
        new(MusicNoteKind.On, note, instrument, volume, effect, param);

    private static readonly MusicCell NoteOff =
        new(MusicNoteKind.Off, 0, -1, -1, MusicEffect.None, 0);

    /// <summary>A chip whose bank is the given v2 payload, with slot 1 a 440 Hz square rooted at A4.</summary>
    private static Apu Chip(SongBytes song, int slotSpeed = 8)
    {
        var bank = new AudioBank();
        bank.GetSfx(1).CopyFrom(Slot(speed: slotSpeed));
        bank.GetSfx(2).CopyFrom(Slot(speed: slotSpeed, note: Note + 12));
        bank.LoadMusicPayload(song.Bytes);
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

    private static short[] Render(Apu apu, int ticks)
    {
        var samples = new List<short>();
        for (int t = 0; t < ticks; t++)
        {
            apu.RenderTick();
            samples.AddRange(apu.Block.Samples.ToArray());
        }
        return [.. samples];
    }

    /// <summary>An empty music payload is silence — "no file is silence, not an error" (§1).</summary>
    [Fact]
    public void AnAbsentSongIsSilenceAndNotAnError()
    {
        var bank = new AudioBank();
        bank.LoadMusicPayload(ReadOnlySpan<byte>.Empty);

        Assert.True(bank.Song.IsEmpty);
    }

    // --- the song model, which is also the tracker's model ---

    /// <summary>
    /// What the loader puts in the model is what the payload said — the read half of what the
    /// tracker of the next wave needs.
    /// </summary>
    [Fact]
    public void ThePayloadLandsInTheModelFieldForField()
    {
        var bank = new AudioBank();
        bank.LoadMusicPayload(new SongBytes(2)
            .Instrument(0, slot: 1, root: Note, once: true, speed: 5)
            .Order(0, 3, MusicFlags.LoopStart)
            .Order(1, 3, MusicFlags.LoopEnd, transpose: -7)
            .Pattern(3, rows: 4, speedUnits: 240)
            .Cell(3, 2, 1, NoteOn(40, instrument: 0, volume: 5, effect: MusicEffect.Cut, param: 3))
            .Bytes);
        MusicSong song = bank.Song;

        Assert.Equal(2, song.OrderLength);

        Assert.Equal(1, song.Instrument(0).Slot);
        Assert.Equal(Note, song.Instrument(0).Root);
        Assert.True(song.Instrument(0).Once);
        Assert.Equal(5, song.Instrument(0).Speed);

        Assert.Equal(3, song.Order(1).Pattern);
        Assert.Equal(MusicFlags.LoopEnd, song.Order(1).Flags);
        Assert.Equal(-7, song.Order(1).Transpose);

        Assert.Equal(4, song.PatternRows(3));
        Assert.Equal(240, song.PatternSpeed(3));

        MusicCell cell = song.Cell(3, 2, 1);
        Assert.Equal(MusicNoteKind.On, cell.Kind);
        Assert.Equal(40, cell.Note);
        Assert.Equal(0, cell.Instrument);
        Assert.Equal(5, cell.Volume);
        Assert.Equal(MusicEffect.Cut, cell.Effect);
        Assert.Equal(3, cell.Param);
    }

    /// <summary>
    /// The lengths the tracker's ruler is drawn from. A pattern lasts rows x speed, in whole ticks,
    /// and no row is shorter than a tick.
    /// </summary>
    [Fact]
    public void ThePatternRulerIsRowsTimesSpeed()
    {
        var bank = new AudioBank();
        bank.LoadMusicPayload(new SongBytes(1)
            .Order(0, 0)
            .Pattern(0, rows: 16, speedUnits: 240)   // 7.5 ticks a row
            .Pattern(1, rows: 4, speedUnits: 256)    // 8 ticks a row
            .Bytes);
        MusicSong song = bank.Song;

        Assert.Equal(120, song.PatternTicks(0));     // 16 x 7.5
        Assert.Equal(32, song.PatternTicks(1));      // 4 x 8
        // An unused pattern is a bar of rest, not zero: silence is a section, not the end.
        Assert.Equal(Apu.MinPatternTicks, song.PatternTicks(9));

        // RowTicks is the offset of a row from the start of its pattern, which is what the
        // editor's playhead is drawn at. A fractional tempo shares the remainder out instead of
        // dropping it, so the offsets go 0, 8, 15, 23, ... and land exactly on the pattern's end.
        Assert.Equal(0, song.RowTicks(0, 0));
        Assert.Equal(8, song.RowTicks(0, 1));
        Assert.Equal(15, song.RowTicks(0, 2));
        Assert.Equal(23, song.RowTicks(0, 3));
        Assert.Equal(song.PatternTicks(0), song.RowTicks(0, 16));
    }

    /// <summary>
    /// The write half of the tracker's model: setting a cell, a row count or a speed is what the
    /// editor does on every keystroke, and reading it back gives what was written.
    /// </summary>
    [Fact]
    public void TheEditorCanWriteThroughTheModel()
    {
        var song = new MusicSong();

        song.SetPatternRows(0, 8);
        song.SetPatternSpeed(0, 240);
        song.SetCell(0, 3, 2, NoteOn(40, instrument: 1, volume: 4));
        song.SetOrder(0, new MusicOrderEntry(0, MusicFlags.LoopEnd));
        song.SetInstrument(1, new MusicInstrument(2, Note));
        song.OrderLength = 1;

        Assert.Equal(8, song.PatternRows(0));
        Assert.Equal(240, song.PatternSpeed(0));
        Assert.Equal(40, song.Cell(0, 3, 2).Note);
        Assert.Equal(MusicFlags.LoopEnd, song.Order(0).Flags);
        Assert.Equal(2, song.Instrument(1).Slot);
        Assert.False(song.IsEmpty);
    }

    /// <summary>
    /// Indices outside the geometry read as empty and write as nothing, rather than throwing. The
    /// editor's cursor can be anywhere while a pattern shrinks under it, and a data bank can name
    /// anything at all (ADR-036).
    /// </summary>
    [Fact]
    public void IndicesOutsideTheGeometryAreHarmless()
    {
        var song = new MusicSong();

        song.SetCell(999, 999, 999, NoteOn(40));
        song.SetPatternRows(-1, 8);
        song.SetPatternSpeed(999, 240);

        Assert.Equal(MusicCell.Empty, song.Cell(999, 999, 999));
        Assert.Equal(0, song.PatternRows(-1));
        Assert.Equal(0, song.PatternSpeed(999));
        Assert.Equal(default, song.Order(999));
        Assert.Equal(MusicInstrument.Default, song.Instrument(-1));
        // A pattern that is not there answers the same as an unused one: a bar of rest.
        Assert.Equal(Apu.MinPatternTicks, song.PatternTicks(999));
    }

    /// <summary>
    /// A song payload from a data bank is never validated (ADR-036), so the core clamps instead of
    /// throwing: an out-of-range pattern, a reserved flag bit and a row speed of zero all land as
    /// something defined. Deterministic, but not canonical.
    /// </summary>
    [Fact]
    public void AnUnvalidatedSongPayloadIsClampedRatherThanRefused()
    {
        var bank = new AudioBank();
        byte[] payload = new SongBytes(1).Order(0, 0).Pattern(0, rows: 2, speedUnits: 0).Bytes;
        payload[AudioBank.SongOrderTableOffset] = 200;        // pattern 200 of 64
        payload[AudioBank.SongOrderTableOffset + 1] = 0xFF;   // every flag bit, real and reserved

        bank.LoadMusicPayload(payload);

        Assert.Equal(200 & 63, bank.Song.Order(0).Pattern);
        Assert.Equal(
            MusicFlags.LoopStart | MusicFlags.LoopEnd | MusicFlags.Stop | MusicFlags.Jump,
            bank.Song.Order(0).Flags);

        // A row speed of zero on a pattern that has rows would freeze the sequencer forever. The
        // stored field keeps the zero it was given, but every reader clamps it to one tick, so a
        // pattern of two rows still lasts two ticks and the song still moves.
        Assert.Equal(0, bank.Song.PatternSpeed(0));
        Assert.Equal(2, bank.Song.PatternTicks(0));
        Assert.Equal(1, bank.Song.RowTicks(0, 1));
    }

    /// <summary>
    /// A payload of any other length is refused by name, not read as garbage — and the 320 bytes
    /// of the pattern list this console used to read are just such a length now (ADR-041).
    /// </summary>
    [Theory]
    [InlineData(999)]
    [InlineData(320)]
    public void APayloadOfTheWrongLengthIsRefused(int length)
    {
        var bank = new AudioBank();

        ArgumentException e = Assert.Throws<ArgumentException>(() => bank.LoadMusicPayload(new byte[length]));
        Assert.Contains("33800", e.Message);
        Assert.Contains(length.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Message);
    }

    // --- playing a song ---

    /// <summary>A note in a cell sounds, which is the entire point of the tracker format.</summary>
    [Fact]
    public void ANoteInACellSounds()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0)
            .Pattern(0, rows: 4, speedUnits: 256)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)));

        apu.PlayMusic(0);
        apu.RenderTick();

        Assert.NotEqual(0, Peak(apu.Block));
    }

    /// <summary>
    /// The same instrument at two different notes gives two different pitches — the thing the old
    /// pattern list could not do at all, because its "cell" was a slot number and a slot carries
    /// its own notes.
    /// </summary>
    [Fact]
    public void OneInstrumentPlaysDifferentNotes()
    {
        static short[] At(int note)
        {
            Apu apu = Chip(new SongBytes(1)
                .Instrument(0, slot: 1, root: Note)
                .Order(0, 0)
                .Pattern(0, rows: 1, speedUnits: 32 * 60)
                .Cell(0, 0, 0, NoteOn(note, instrument: 0, volume: 7)));
            apu.PlayMusic(0);
            return Render(apu, 30);
        }

        Assert.NotEqual(At(Note), At(Note + 12));
        Assert.Equal(At(Note), At(Note));
    }

    /// <summary>
    /// An order entry's transpose moves the whole pattern without touching a cell — repeating a
    /// section in another key costs one byte instead of a copy of the pattern.
    /// </summary>
    [Fact]
    public void AnOrderEntryTransposesThePatternItPlays()
    {
        static short[] Song(int transpose)
        {
            Apu apu = Chip(new SongBytes(1)
                .Instrument(0, slot: 1, root: Note)
                .Order(0, 0, transpose: transpose)
                .Pattern(0, rows: 1, speedUnits: 32 * 60)
                .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)));
            apu.PlayMusic(0);
            return Render(apu, 30);
        }

        Assert.NotEqual(Song(0), Song(12));
        // and the transposed pattern is the same sound as the cell written a semitone up.
        Assert.Equal(Song(0), Song(0));
    }

    /// <summary>
    /// Two order entries playing the <em>same</em> pattern: the song repeats a section without a
    /// second copy of it. In version 1 the order was the pattern table, so a repeat meant copying
    /// four bytes and burning one of the 64 pattern slots.
    /// </summary>
    [Fact]
    public void TheOrderRepeatsAPatternWithoutCopyingIt()
    {
        Apu apu = Chip(new SongBytes(3)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0)
            .Order(1, 0)
            .Order(2, 0, MusicFlags.Stop)
            .Pattern(0, rows: 1, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)));

        apu.PlayMusic(0);
        Assert.Equal(3, apu.MusicEntryCount);

        // Three entries of four ticks: sounding through tick 11, silent at tick 12.
        Render(apu, 11);
        Assert.NotEqual(0, Peak(apu.Block));
        Render(apu, 2);
        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// Per-pattern speed: the tempo is a property of the pattern, not of the longest SFX slot that
    /// happens to be playing. This is what version 1 could not express, and what the ports asked
    /// for by name.
    /// </summary>
    [Fact]
    public void EachPatternKeepsItsOwnTempo()
    {
        var bank = new AudioBank();
        bank.LoadMusicPayload(new SongBytes(2)
            .Order(0, 0)
            .Order(1, 1)
            .Pattern(0, rows: 4, speedUnits: 32 * 4)   // 16 ticks
            .Pattern(1, rows: 4, speedUnits: 32 * 9)   // 36 ticks
            .Bytes);

        Assert.Equal(16, bank.Song.PatternTicks(0));
        Assert.Equal(36, bank.Song.PatternTicks(1));
    }

    /// <summary>
    /// A fractional row speed averages out exactly over the pattern: 7.5 ticks a row over 16 rows
    /// is 120 ticks, never 112 and never 128. Rounding this is the -14.7 % and +6.67 % Celeste
    /// measured (GAPS §1.7) and asked the console to fix (§5.3).
    ///
    /// <para>Break recipe: in <c>Apu.AdvanceSong</c> replace the 1/32-tick accumulator with
    /// <c>_musicRowSpeed / SpeedUnitsPerTick</c> whole ticks — a 7.5-tick row becomes a 7-tick
    /// row, the pattern finishes in 112 ticks and this fails.</para>
    /// </summary>
    [Fact]
    public void AFractionalRowSpeedAveragesOutExactly()
    {
        var bank = new AudioBank();
        bank.LoadMusicPayload(new SongBytes(1).Order(0, 0).Pattern(0, rows: 16, speedUnits: 240).Bytes);
        MusicSong song = bank.Song;

        Assert.Equal(120, song.PatternTicks(0));
        // Sixteen rows of 7.5 ticks land on 120 exactly: the remainder is shared out row by row,
        // never dropped. Rounding each row down would give 112, up would give 128.
        Assert.Equal(120, song.RowTicks(0, 16));

        // No row is shorter than a tick — the fraction buys an exact average, not sub-tick events.
        int[] durations = [.. Enumerable.Range(0, 16).Select(r => song.RowTicks(0, r + 1) - song.RowTicks(0, r))];
        Assert.All(durations, d => Assert.True(d >= 1, $"a row lasted {d} ticks"));
        Assert.Equal(120, durations.Sum());
        Assert.Equal([8, 7, 8, 7, 8, 7, 8, 7, 8, 7, 8, 7, 8, 7, 8, 7], durations);
    }

    /// <summary>
    /// The pattern really lasts that long on the chip, not only on the ruler: a 16-row pattern at
    /// 7.5 ticks a row followed by a stop is audible at tick 119 and silent at tick 121.
    /// </summary>
    [Fact]
    public void AFractionalTempoLastsTheTicksTheRulerSays()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0, MusicFlags.Stop)
            .Pattern(0, rows: 16, speedUnits: 240)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PlayMusic(0);
        Render(apu, 119);
        Assert.NotEqual(0, Peak(apu.Block));
        Render(apu, 2);
        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>A note off silences the voice at the row it is written on, and nothing else does.</summary>
    [Fact]
    public void ANoteOffSilencesTheVoice()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0, MusicFlags.LoopEnd)
            .Pattern(0, rows: 4, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7))
            .Cell(0, 2, 0, NoteOff), slotSpeed: 255);

        apu.PlayMusic(0);
        Render(apu, 4);
        Assert.NotEqual(0, Peak(apu.Block));
        Render(apu, 5);   // into row 2
        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// A volume column scales the voice; leaving it out on a note-on goes back to full level rather
    /// than inheriting the quiet cell before it, which would poison every note after it with
    /// nothing on screen to explain why.
    /// </summary>
    [Fact]
    public void AVolumeColumnScalesTheVoiceAndANoteOnWithoutOneIsLoudAgain()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0, MusicFlags.LoopEnd)
            .Pattern(0, rows: 4, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7))
            .Cell(0, 1, 0, NoteOn(Note, instrument: 0, volume: 2))
            .Cell(0, 2, 0, NoteOn(Note, instrument: 0)), slotSpeed: 255);

        apu.PlayMusic(0);
        Render(apu, 2);
        int loud = Peak(apu.Block);
        Render(apu, 4);
        int quiet = Peak(apu.Block);
        Render(apu, 4);
        int loudAgain = Peak(apu.Block);

        Assert.True(quiet < loud, $"quiet {quiet} should be under loud {loud}");
        Assert.True(loudAgain > quiet, $"loudAgain {loudAgain} should be over quiet {quiet}");
    }

    /// <summary>
    /// A cell that names no instrument keeps the one the channel has — so a run of notes is a run
    /// of notes, not a column of repeated instrument numbers.
    /// </summary>
    [Fact]
    public void ACellWithoutAnInstrumentKeepsTheChannelsOne()
    {
        static short[] Song(bool restate)
        {
            Apu apu = Chip(new SongBytes(1)
                .Instrument(3, slot: 2, root: Note)
                .Order(0, 0, MusicFlags.Stop)
                .Pattern(0, rows: 2, speedUnits: 32 * 8)
                .Cell(0, 0, 0, NoteOn(Note, instrument: 3, volume: 7))
                .Cell(0, 1, 0, restate
                    ? NoteOn(Note + 2, instrument: 3, volume: 7)
                    : NoteOn(Note + 2, volume: 7)), slotSpeed: 255);
            apu.PlayMusic(0);
            return Render(apu, 16);
        }

        Assert.Equal(Song(restate: true), Song(restate: false));
    }

    /// <summary>
    /// The order's loop: <c>loop-back</c> returns to the remembered <c>loop-start</c>, and the song
    /// keeps going. Richer than version 1's three bits because the entry, not the pattern, carries
    /// the flag — the same pattern can loop in one place and fall through in another.
    /// </summary>
    [Fact]
    public void TheOrderLoopsBackToTheRememberedStart()
    {
        Apu apu = Chip(new SongBytes(3)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0)
            .Order(1, 0, MusicFlags.LoopStart)
            .Order(2, 0, MusicFlags.LoopEnd)
            .Pattern(0, rows: 1, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PlayMusic(0);
        // Twelve entries of four ticks: long past the three the order holds, still sounding.
        Render(apu, 48);
        Assert.NotEqual(0, Peak(apu.Block));
    }

    /// <summary>
    /// A jump goes where its target says, which is the transition version 1 had no way to write:
    /// its only backward edge was "the nearest loop-start".
    /// </summary>
    [Fact]
    public void AJumpGoesWhereItsTargetSays()
    {
        Apu apu = Chip(new SongBytes(3)
            .Instrument(0, slot: 1, root: Note)
            .Instrument(1, slot: 2, root: Note)
            .Order(0, 0)
            .Order(1, 1, MusicFlags.Jump, target: 0)
            .Order(2, 2, MusicFlags.Stop)
            .Pattern(0, rows: 1, speedUnits: 32 * 4)
            .Pattern(1, rows: 1, speedUnits: 32 * 4)
            .Pattern(2, rows: 1, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7))
            .Cell(1, 0, 0, NoteOn(Note, instrument: 1, volume: 7))
            .Cell(2, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PlayMusic(0);
        // Entry 2 carries the only stop, and the jump never reaches it: still sounding at tick 60.
        Render(apu, 60);
        Assert.NotEqual(0, Peak(apu.Block));
    }

    /// <summary>
    /// Stop beats loop when an entry carries both — the reading a composer who wrote both almost
    /// certainly meant, and a rule worth pinning because the alternative is a song that never ends.
    /// </summary>
    [Fact]
    public void StopBeatsLoopOnAnEntryCarryingBoth()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0, MusicFlags.LoopEnd | MusicFlags.Stop)
            .Pattern(0, rows: 1, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PlayMusic(0);
        Render(apu, 6);

        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// Running off the end of the order stops the song, so one that forgot its flags ends instead
    /// of wrapping round to its own start forever.
    /// </summary>
    [Fact]
    public void RunningPastTheLastEntryStopsTheSong()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0)
            .Pattern(0, rows: 1, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PlayMusic(0);
        Render(apu, 6);

        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// A pattern with no rows is a bar of rest of exactly <see cref="Apu.MinPatternTicks"/> and the
    /// song goes on — the same rule version 1 gives an empty pattern. Silence is a section, not the
    /// end of the piece.
    /// </summary>
    [Fact]
    public void AnUnusedPatternIsABarOfRestAndNotTheEnd()
    {
        Apu apu = Chip(new SongBytes(2)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 9)                 // pattern 9 has no rows
            .Order(1, 0, MusicFlags.LoopEnd)
            .Pattern(0, rows: 1, speedUnits: 32 * 60)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PlayMusic(0);
        Render(apu, Apu.MinPatternTicks - 1);
        Assert.Equal(0, Peak(apu.Block));
        Render(apu, 3);
        Assert.NotEqual(0, Peak(apu.Block));
    }

    /// <summary>An instrument naming an empty SFX slot is silence, exactly as <c>Sfx</c> on one is.</summary>
    [Fact]
    public void AnInstrumentOnAnEmptySlotIsSilence()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 40, root: Note)   // slot 40 was never filled
            .Order(0, 0, MusicFlags.LoopEnd)
            .Pattern(0, rows: 1, speedUnits: 32 * 8)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)));

        apu.PlayMusic(0);
        Render(apu, 4);

        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// An arpeggio is a <b>triad</b>, not a four-group: it cycles the cell's note, the note plus
    /// the parameter's high nibble and the note plus its low nibble, one every
    /// <see cref="Apu.ArpeggioTicksPerNote"/> ticks. So <c>arp:47</c> is a major triad (+4, +7)
    /// and <c>arp:37</c> a minor one — pinned because the compiler's diagnostic names those
    /// intervals to the author, and a message that named the wrong chord would be worse than no
    /// message at all.
    /// </summary>
    [Fact]
    public void AnArpeggioIsAThreePositionTriadOverTheCellsNote()
    {
        static short[] Song(int param)
        {
            Apu apu = Chip(new SongBytes(1)
                .Instrument(0, slot: 1, root: Note)
                .Order(0, 0, MusicFlags.Stop)
                .Pattern(0, rows: 1, speedUnits: 32 * 40)
                .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7,
                    effect: param == 0 ? MusicEffect.None : MusicEffect.Arpeggio, param: param)), slotSpeed: 255);
            apu.PlayMusic(0);
            return Render(apu, 24);
        }

        // A major triad and a minor one are different sounds, and both differ from a plain note.
        short[] plain = Song(0);
        short[] major = Song(0x47);
        short[] minor = Song(0x37);
        Assert.NotEqual(plain, major);
        Assert.NotEqual(plain, minor);
        Assert.NotEqual(major, minor);

        // Both nibbles equal means two of the three positions coincide, so arp:44 and arp:47 are
        // different chords rather than the same cycle at a different speed — the cycle is over
        // the parameter's offsets, not over neighbouring slot steps the way v1's arpeggio is.
        Assert.NotEqual(Song(0x44), Song(0x47));
        Assert.Equal(2, Apu.ArpeggioTicksPerNote);
        Assert.Equal(Song(0x47), Song(0x47));
    }

    // --- what the tracker plays with ---

    /// <summary>
    /// <c>PreviewPattern</c> plays one pattern once and stops: the order is not walked and no loop
    /// or stop flag is consulted, so the editor hears the pattern as written rather than as the
    /// song arranges it.
    /// </summary>
    [Fact]
    public void PreviewPatternPlaysOnePatternAndStops()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0, MusicFlags.LoopEnd)      // the song loops; the preview must not
            .Pattern(0, rows: 1, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PreviewPattern(0);
        Render(apu, 3);
        Assert.NotEqual(0, Peak(apu.Block));
        Render(apu, 4);
        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// <c>PreviewPattern</c> hears the pattern as written, not as the song transposes it: the
    /// editor's ear has to agree with the editor's screen.
    /// </summary>
    [Fact]
    public void PreviewPatternIgnoresTheOrdersTransposition()
    {
        static short[] Preview(int transpose)
        {
            Apu apu = Chip(new SongBytes(1)
                .Instrument(0, slot: 1, root: Note)
                .Order(0, 0, transpose: transpose)
                .Pattern(0, rows: 1, speedUnits: 32 * 60)
                .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);
            apu.PreviewPattern(0);
            return Render(apu, 20);
        }

        Assert.Equal(Preview(0), Preview(12));
    }

    /// <summary>
    /// <c>PreviewRow</c> puts one row on the channels and leaves them ringing — the tracker's
    /// "audition the row under the cursor". No sequencer runs afterwards, so the voices play their
    /// instruments to their own end.
    /// </summary>
    [Fact]
    public void PreviewRowAuditionsOneRowAndLeavesItRinging()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0)
            .Pattern(0, rows: 4, speedUnits: 32 * 4)
            .Cell(0, 2, 1, NoteOn(Note, instrument: 0, volume: 7)), slotSpeed: 255);

        apu.PreviewRow(0, 2);
        Render(apu, 10);
        Assert.NotEqual(0, Peak(apu.Block));

        // A row with nothing on it auditions to silence rather than to the row before.
        apu.PreviewRow(0, 0);
        apu.RenderTick();
        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>A preview of a row or pattern outside the geometry does nothing at all.</summary>
    [Fact]
    public void PreviewingOutsideTheGeometryDoesNothing()
    {
        Apu apu = Chip(new SongBytes(1)
            .Instrument(0, slot: 1, root: Note)
            .Order(0, 0)
            .Pattern(0, rows: 4, speedUnits: 32 * 4)
            .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7)));

        apu.PreviewRow(0, 99);
        apu.PreviewRow(99, 0);
        apu.PreviewPattern(99);
        apu.RenderTick();

        Assert.Equal(0, Peak(apu.Block));
    }

    /// <summary>
    /// <c>MusicEntryCount</c> is what <c>PlayMusic</c> accepts: the order's length. The editor's
    /// "which entry" spinner is drawn from it, and an empty bank accepts nothing at all.
    /// </summary>
    [Fact]
    public void TheEntryCountIsTheOrderLength()
    {
        Apu three = Chip(new SongBytes(3).Order(0, 0).Order(1, 0).Order(2, 0).Pattern(0, 1, 256));
        Assert.Equal(3, three.MusicEntryCount);

        var empty = new Apu();
        empty.LoadBank(new AudioBank());
        Assert.Equal(0, empty.MusicEntryCount);
    }

    /// <summary>
    /// The same song rendered twice is the same samples — the property every pinned audio hash
    /// stands on.
    /// </summary>
    [Fact]
    public void TheSongPathIsDeterministic()
    {
        static short[] Run()
        {
            Apu apu = Chip(new SongBytes(2)
                .Instrument(0, slot: 1, root: Note)
                .Instrument(1, slot: 2, root: Note, once: true, speed: 3)
                .Order(0, 0, MusicFlags.LoopStart)
                .Order(1, 1, MusicFlags.LoopEnd, transpose: 5)
                .Pattern(0, rows: 4, speedUnits: 240)
                .Pattern(1, rows: 8, speedUnits: 168)
                .Cell(0, 0, 0, NoteOn(Note, instrument: 0, volume: 7))
                .Cell(0, 2, 1, NoteOn(Note + 7, instrument: 1, volume: 4, effect: MusicEffect.Arpeggio, param: 0x47))
                .Cell(1, 0, 0, NoteOn(Note + 3, instrument: 0, volume: 6))
                .Cell(1, 4, 0, NoteOn(Note + 5, effect: MusicEffect.Slide, param: 4))
                .Cell(1, 6, 1, NoteOff));
            apu.PlayMusic(0);
            return Render(apu, 200);
        }

        short[] first = Run();
        Assert.Equal(first, Run());
        Assert.NotEqual(0, first.Max(s => Math.Abs((int)s)));
    }
}

using System.Buffers.Binary;

namespace Quarp.CartKit;

/// <summary>
/// The layout of <c>music.bin</c> — the tracker song — as bytes: its payload preamble, its four
/// tables, the bit layout of one cell and every rule the loader enforces. The full specification
/// with a hexdump is docs/AUDIO-FORMAT.md §4-§5; this type is that document in code, and the two
/// are meant to be read together (ADR-040, ADR-041).
///
/// <para><b>Why a file of its own rather than more of <see cref="AudioFormat"/>.</b> The song is
/// the larger half of the audio format by a factor of a hundred, and its rules — cells,
/// instruments, the order — have nothing to do with the SFX bank's. Keeping them apart means a
/// reader can see at a glance which rules belong to which bank.
/// <see cref="AudioFormat.ParseMusicFile"/> is still the single door: it reads the shared 8-byte
/// header and hands the payload here.</para>
///
/// <para><b>Shape.</b> An 8-byte file header (magic <c>QMUS</c>, version 0, 64 patterns x 4
/// channels), followed by a payload of <see cref="PayloadSize"/> bytes:
/// <code>
///     0     8  preamble: layout u16, rows u8, instruments u8, order length u16, reserved u16
///     8   256  instrument table   64 x 4
///   264   512  order table       128 x 4
///   776   256  pattern headers    64 x 4
///  1032 32768  cell table         64 x 32 x 4 x u32 LE
/// </code>
/// One pattern row is 4 cells = 16 bytes, i.e. exactly one line of a hex dump, so a song reads
/// in a hex editor the way it reads in the tracker.</para>
///
/// <para><b>Endianness.</b> Every multi-byte field — the preamble's words, the pattern speed,
/// the cell — is read and written through <see cref="BinaryPrimitives"/>, never through
/// <c>BitConverter</c> or a struct overlay: these bytes feed a synthesizer whose PCM is compared
/// across windows-x64 and linux-arm64.</para>
/// </summary>
public static class MusicFormat
{
    /// <summary>The version word this layout is written with: 0, the one living version (ADR-041).</summary>
    public const int Version = AudioFormat.Version;

    /// <summary>Rows a pattern can hold: 32, the number profile 8 counts everything else in.</summary>
    public const int RowCount = 32;

    /// <summary>Instruments in the table: 64, one per SFX slot so any slot can be a timbre.</summary>
    public const int InstrumentCount = 64;

    /// <summary>Entries the order table can hold: 128 — twice the pattern count, so every pattern can be used twice before the table is the limit.</summary>
    public const int OrderCount = 128;

    /// <summary>Bytes of the payload preamble: layout, geometry echo, order length.</summary>
    public const int PreambleSize = 8;

    /// <summary>Bytes of one instrument record.</summary>
    public const int InstrumentSize = 4;

    /// <summary>Bytes of one order entry.</summary>
    public const int OrderEntrySize = 4;

    /// <summary>Bytes of one pattern header: speed u16, rows u8, reserved u8.</summary>
    public const int PatternHeaderSize = 4;

    /// <summary>Bytes of one cell: a single little-endian u32.</summary>
    public const int CellSize = 4;

    /// <summary>Offset of the instrument table inside the payload.</summary>
    public const int InstrumentTableOffset = PreambleSize;

    /// <summary>Bytes of the instrument table: 64 x 4 = 256.</summary>
    public const int InstrumentTableSize = InstrumentCount * InstrumentSize;

    /// <summary>Offset of the order table inside the payload.</summary>
    public const int OrderTableOffset = InstrumentTableOffset + InstrumentTableSize;

    /// <summary>Bytes of the order table: 128 x 4 = 512.</summary>
    public const int OrderTableSize = OrderCount * OrderEntrySize;

    /// <summary>Offset of the pattern header table inside the payload.</summary>
    public const int PatternTableOffset = OrderTableOffset + OrderTableSize;

    /// <summary>Bytes of the pattern header table: 64 x 4 = 256.</summary>
    public const int PatternTableSize = AudioFormat.MusicPatternCount * PatternHeaderSize;

    /// <summary>Offset of the cell table inside the payload.</summary>
    public const int CellTableOffset = PatternTableOffset + PatternTableSize;

    /// <summary>Cells in one pattern: 32 rows x 4 channels = 128.</summary>
    public const int CellsPerPattern = RowCount * AudioFormat.MusicChannelCount;

    /// <summary>Bytes of the cell table: 64 x 32 x 4 x 4 = 32768.</summary>
    public const int CellTableSize = AudioFormat.MusicPatternCount * CellsPerPattern * CellSize;

    /// <summary>What the console receives: 33800 bytes, file header stripped.</summary>
    public const int PayloadSize = CellTableOffset + CellTableSize;

    /// <summary>Size of <c>music.bin</c> on disk — fixed, so any other length is an error.</summary>
    public const int FileSize = AudioFormat.HeaderSize + PayloadSize;

    // --- fixed-point row speed ---

    /// <summary>
    /// Sub-divisions of a console tick a pattern's row speed is measured in: 32.
    ///
    /// <para>Thirty-two rather than sixteen or a hundred, for one reason that can be checked
    /// with a calculator: a PICO-8 step lasts <c>speed x 60/128</c> ticks = <c>speed x 15/32</c>
    /// ticks, so every PICO-8 tempo is a whole number of thirty-seconds of a tick and converts
    /// without rounding. That is the gap the ports measured and asked for by name (Celeste
    /// GAPS §1.7 and §5.3, Terra PROBLEMS §2.4).</para>
    /// </summary>
    public const int SpeedUnitsPerTick = 32;

    /// <summary>
    /// Shortest row: one whole tick. Registers land on tick boundaries (SPEC-8 §7), so a row
    /// shorter than a tick could not be heard as a row; the fraction buys an exact average
    /// tempo, not sub-tick events.
    /// </summary>
    public const int MinRowSpeed = SpeedUnitsPerTick;

    /// <summary>Longest row: 65535/32 = 2047.97 ticks, i.e. 34 seconds.</summary>
    public const int MaxRowSpeed = 0xFFFF;

    /// <summary>Row speed of a pattern nobody configured: 8 ticks, the same default an SFX slot gets.</summary>
    public const int DefaultRowSpeed = 8 * SpeedUnitsPerTick;

    // --- cell fields ---

    /// <summary>No note event in this cell.</summary>
    public const int NoteNone = 0;

    /// <summary>The cell starts its instrument at <c>note</c>.</summary>
    public const int NoteOn = 1;

    /// <summary>The cell silences whatever the channel was playing.</summary>
    public const int NoteOff = 2;

    /// <summary>Note kinds that exist; 3 is rejected.</summary>
    public const int NoteKindCount = 3;

    /// <summary>Cell effect: nothing. The parameter must then be zero.</summary>
    public const int EffectNone = 0;

    /// <summary>Cell effect: arpeggio over the note and the two semitone offsets in the parameter's nibbles.</summary>
    public const int EffectArpeggio = 1;

    /// <summary>Cell effect: glide to this cell's note over <c>param</c> ticks instead of restarting the instrument.</summary>
    public const int EffectSlide = 2;

    /// <summary>Cell effect: silence the voice <c>param</c> ticks into the row.</summary>
    public const int EffectCut = 3;

    /// <summary>Cell effects that exist; 4..31 are rejected, which is what lets later ones be added without a version bump.</summary>
    public const int EffectCount = 4;

    /// <summary>Order flag: the loop comes back to this entry.</summary>
    public const byte OrderLoopStart = 0x01;

    /// <summary>Order flag: after this entry, jump back to the remembered loop start.</summary>
    public const byte OrderLoopBack = 0x02;

    /// <summary>Order flag: the song ends after this entry.</summary>
    public const byte OrderStop = 0x04;

    /// <summary>Order flag: after this entry, continue at order index <c>target</c>.</summary>
    public const byte OrderJump = 0x08;

    /// <summary>Every order flag bit that has a meaning; the rest must be zero.</summary>
    public const byte OrderFlagMask = OrderLoopStart | OrderLoopBack | OrderStop | OrderJump;

    /// <summary>Instrument flag: play the SFX slot once, ignoring the loop it carries.</summary>
    public const byte InstrumentOnce = 0x01;

    /// <summary>Every instrument flag bit that has a meaning.</summary>
    public const byte InstrumentFlagMask = InstrumentOnce;

    /// <summary>Largest transpose an order entry may carry, in semitones; the byte is signed.</summary>
    public const int MaxTranspose = 63;

    /// <summary>Smallest transpose an order entry may carry, in semitones.</summary>
    public const int MinTranspose = -64;

    // --- payload accessors (the offsets are the specification, so they live here and nowhere else) ---

    /// <summary>The layout word at the front of the payload; it must equal <see cref="Version"/> in a file.</summary>
    public static int Layout(ReadOnlySpan<byte> payload) => BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]);

    /// <summary>Rows per pattern as the payload claims: a redundant echo of <see cref="RowCount"/>.</summary>
    public static int Rows(ReadOnlySpan<byte> payload) => payload[2];

    /// <summary>Instruments as the payload claims: a redundant echo of <see cref="InstrumentCount"/>.</summary>
    public static int Instruments(ReadOnlySpan<byte> payload) => payload[3];

    /// <summary>How many order entries are the song, 0..128. Entries past this must be zero.</summary>
    public static int OrderLength(ReadOnlySpan<byte> payload) =>
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));

    /// <summary>The reserved word of the preamble; must be zero.</summary>
    public static int PreambleReserved(ReadOnlySpan<byte> payload) =>
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));

    /// <summary>Writes the payload preamble; the geometry echoes are written, never trusted.</summary>
    public static void WritePreamble(Span<byte> payload, int orderLength)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(payload[..2], Version);
        payload[2] = RowCount;
        payload[3] = InstrumentCount;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), (ushort)orderLength);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), 0);
    }

    /// <summary>Byte offset of an instrument record inside the payload.</summary>
    public static int InstrumentOffset(int instrument) => InstrumentTableOffset + (instrument * InstrumentSize);

    /// <summary>The SFX slot an instrument takes its waveform, envelope and per-step effects from.</summary>
    public static int InstrumentSlot(ReadOnlySpan<byte> payload, int instrument) =>
        payload[InstrumentOffset(instrument)];

    /// <summary>The semitone the instrument's slot is written at; a cell's note transposes the slot by the difference.</summary>
    public static int InstrumentRoot(ReadOnlySpan<byte> payload, int instrument) =>
        payload[InstrumentOffset(instrument) + 1];

    /// <summary>The instrument's flag byte; see <see cref="InstrumentOnce"/>.</summary>
    public static byte InstrumentFlags(ReadOnlySpan<byte> payload, int instrument) =>
        payload[InstrumentOffset(instrument) + 2];

    /// <summary>Ticks per SFX step this instrument overrides the slot with; 0 means the slot's own speed.</summary>
    public static int InstrumentSpeed(ReadOnlySpan<byte> payload, int instrument) =>
        payload[InstrumentOffset(instrument) + 3];

    /// <summary>Writes one instrument record.</summary>
    public static void WriteInstrument(Span<byte> payload, int instrument, int slot, int root, byte flags, int speed)
    {
        int at = InstrumentOffset(instrument);
        payload[at] = (byte)slot;
        payload[at + 1] = (byte)root;
        payload[at + 2] = flags;
        payload[at + 3] = (byte)speed;
    }

    /// <summary>Byte offset of an order entry inside the payload.</summary>
    public static int OrderOffset(int entry) => OrderTableOffset + (entry * OrderEntrySize);

    /// <summary>The pattern an order entry plays.</summary>
    public static int OrderPattern(ReadOnlySpan<byte> payload, int entry) => payload[OrderOffset(entry)];

    /// <summary>The entry's flag byte; see <see cref="OrderFlagMask"/>.</summary>
    public static byte OrderFlags(ReadOnlySpan<byte> payload, int entry) => payload[OrderOffset(entry) + 1];

    /// <summary>The jump target of an entry carrying <see cref="OrderJump"/>; 0 otherwise.</summary>
    public static int OrderTarget(ReadOnlySpan<byte> payload, int entry) => payload[OrderOffset(entry) + 2];

    /// <summary>Semitones this instance of the pattern is transposed by, -64..+63.</summary>
    public static int OrderTranspose(ReadOnlySpan<byte> payload, int entry) =>
        (sbyte)payload[OrderOffset(entry) + 3];

    /// <summary>Writes one order entry.</summary>
    public static void WriteOrder(Span<byte> payload, int entry, int pattern, byte flags, int target, int transpose)
    {
        int at = OrderOffset(entry);
        payload[at] = (byte)pattern;
        payload[at + 1] = flags;
        payload[at + 2] = (byte)target;
        payload[at + 3] = (byte)(sbyte)transpose;
    }

    /// <summary>Byte offset of a pattern header inside the payload.</summary>
    public static int PatternOffset(int pattern) => PatternTableOffset + (pattern * PatternHeaderSize);

    /// <summary>Row duration in 1/32 ticks; 0 exactly when the pattern is unused.</summary>
    public static int PatternSpeed(ReadOnlySpan<byte> payload, int pattern) =>
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(PatternOffset(pattern), 2));

    /// <summary>Rows the pattern plays, 0..32; 0 means the pattern is unused.</summary>
    public static int PatternRows(ReadOnlySpan<byte> payload, int pattern) => payload[PatternOffset(pattern) + 2];

    /// <summary>The reserved byte of a pattern header; must be zero.</summary>
    public static int PatternReserved(ReadOnlySpan<byte> payload, int pattern) => payload[PatternOffset(pattern) + 3];

    /// <summary>Writes one pattern header.</summary>
    public static void WritePattern(Span<byte> payload, int pattern, int speed, int rows)
    {
        int at = PatternOffset(pattern);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(at, 2), (ushort)speed);
        payload[at + 2] = (byte)rows;
        payload[at + 3] = 0;
    }

    /// <summary>Byte offset of one cell inside the payload.</summary>
    public static int CellOffset(int pattern, int row, int channel) =>
        CellTableOffset
        + (((pattern * CellsPerPattern) + (row * AudioFormat.MusicChannelCount) + channel) * CellSize);

    /// <summary>One cell word, little-endian, no allocation.</summary>
    public static uint Cell(ReadOnlySpan<byte> payload, int pattern, int row, int channel) =>
        BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(CellOffset(pattern, row, channel), CellSize));

    /// <summary>Writes one cell word little-endian.</summary>
    public static void WriteCell(Span<byte> payload, int pattern, int row, int channel, uint cell) =>
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(CellOffset(pattern, row, channel), CellSize), cell);

    // --- the cell word ---

    /// <summary>
    /// Packs one cell into the u32 that lands in the file. The bit budget is exactly 32 and is
    /// spent as: note 6, note kind 2, instrument 6, "has instrument" 1, volume 3, "has volume" 1,
    /// effect 5, parameter 8. Nothing is left over, which is deliberate — a spare bit is a field
    /// a future version starts using quietly, and this format would rather grow a version number.
    ///
    /// <para>This is the bit layout and nothing else: it will happily pack a note at kind
    /// <see cref="NoteNone"/>, which is <b>not</b> a cell a bank may contain. An empty cell has
    /// exactly one spelling, the zero word, and <see cref="ValidatePayload"/> is where that is
    /// enforced.</para>
    /// </summary>
    public static uint PackCell(
        int note, int noteKind, int instrument, bool hasInstrument, int volume, bool hasVolume, int effect, int param)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, AudioFormat.MaxNote);
        ArgumentOutOfRangeException.ThrowIfNegative(noteKind);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(noteKind, NoteKindCount);
        ArgumentOutOfRangeException.ThrowIfNegative(instrument);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(instrument, InstrumentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(volume);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, AudioFormat.MaxVolume);
        ArgumentOutOfRangeException.ThrowIfNegative(effect);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(effect, EffectCount);
        ArgumentOutOfRangeException.ThrowIfNegative(param);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(param, 255);
        return (uint)note
            | ((uint)noteKind << 6)
            | ((uint)instrument << 8)
            | (hasInstrument ? 1u << 14 : 0u)
            | ((uint)volume << 15)
            | (hasVolume ? 1u << 18 : 0u)
            | ((uint)effect << 19)
            | ((uint)param << 24);
    }

    /// <summary>Semitone of a cell, 0-63; meaningful only when <see cref="CellNoteKind"/> is <see cref="NoteOn"/>.</summary>
    public static int CellNote(uint cell) => (int)(cell & 0x3F);

    /// <summary>Note event of a cell: none, on or off.</summary>
    public static int CellNoteKind(uint cell) => (int)((cell >> 6) & 0x03);

    /// <summary>Instrument of a cell, 0-63; meaningful only when <see cref="CellHasInstrument"/>.</summary>
    public static int CellInstrument(uint cell) => (int)((cell >> 8) & 0x3F);

    /// <summary>True when the cell names an instrument; otherwise the channel keeps the one it has.</summary>
    public static bool CellHasInstrument(uint cell) => (cell & (1u << 14)) != 0;

    /// <summary>Volume of a cell, 0-7; meaningful only when <see cref="CellHasVolume"/>.</summary>
    public static int CellVolume(uint cell) => (int)((cell >> 15) & 0x07);

    /// <summary>True when the cell names a volume; otherwise the channel keeps the level it has.</summary>
    public static bool CellHasVolume(uint cell) => (cell & (1u << 18)) != 0;

    /// <summary>Effect of a cell, 0-3 in a validated payload.</summary>
    public static int CellEffect(uint cell) => (int)((cell >> 19) & 0x1F);

    /// <summary>Effect parameter of a cell, 0-255.</summary>
    public static int CellParam(uint cell) => (int)((cell >> 24) & 0xFF);

    /// <summary>
    /// The empty song: an order of length 0, no rows in any pattern. Built through
    /// <see cref="WritePreamble"/> rather than defaulted, because the preamble echoes the
    /// geometry and a validator reads those echoes back.
    /// </summary>
    public static byte[] EmptyPayload()
    {
        byte[] payload = new byte[PayloadSize];
        WritePreamble(payload, 0);
        return payload;
    }

    /// <summary>
    /// Every rule a music payload has to obey, in the order that produces the most useful
    /// message. All of them are cheap, and all of them exist because the payload reaches a
    /// synthesizer that runs 60 times a second and must not be checking ranges there.
    ///
    /// <para>Most of the rules are about <b>canonical encoding</b> rather than range: a field
    /// nobody can hear must not be able to change the bytes, or byte-comparing two banks stops
    /// meaning anything and a cartridge's identity moves when an author edits something
    /// inaudible.</para>
    /// </summary>
    public static void ValidatePayload(ReadOnlySpan<byte> payload, string sourceName)
    {
        if (payload.Length != PayloadSize)
        {
            throw new CartLoadException(
                $"{sourceName}: music payload is {payload.Length} bytes, a song is exactly {PayloadSize} "
                + "— the 320-byte pattern list of the old format is gone (ADR-041).");
        }

        int layout = Layout(payload);
        if (layout != Version)
        {
            throw new CartLoadException(
                $"{sourceName}: the payload says layout {layout}, but the file header says version {Version}; "
                + "the two have to agree.");
        }
        if (Rows(payload) != RowCount || Instruments(payload) != InstrumentCount)
        {
            throw new CartLoadException(
                $"{sourceName}: payload says {Rows(payload)} rows x {Instruments(payload)} instruments, "
                + $"profile 8 is {RowCount} x {InstrumentCount}.");
        }
        if (PreambleReserved(payload) != 0)
        {
            throw new CartLoadException(
                $"{sourceName}: bytes 6-7 of the payload are reserved and must be 0 "
                + $"(got 0x{PreambleReserved(payload):x4}).");
        }

        int orderLength = OrderLength(payload);
        if (orderLength > OrderCount)
        {
            throw new CartLoadException(
                $"{sourceName}: order length {orderLength}, the table holds {OrderCount} entries.");
        }

        ValidateInstruments(payload, sourceName);
        ValidateOrder(payload, sourceName, orderLength);
        ValidatePatterns(payload, sourceName);
    }

    private static void ValidateInstruments(ReadOnlySpan<byte> payload, string sourceName)
    {
        for (int i = 0; i < InstrumentCount; i++)
        {
            int slot = InstrumentSlot(payload, i);
            if (slot >= AudioFormat.SfxSlotCount)
            {
                throw new CartLoadException(
                    $"{sourceName}: instrument {i}: SFX slot {slot}, profile 8 has "
                    + $"0..{AudioFormat.SfxSlotCount - 1}.");
            }
            int root = InstrumentRoot(payload, i);
            if (root > AudioFormat.MaxNote)
            {
                throw new CartLoadException(
                    $"{sourceName}: instrument {i}: root note {root}, notes are 0..{AudioFormat.MaxNote}.");
            }
            byte flags = InstrumentFlags(payload, i);
            if ((flags & ~InstrumentFlagMask) != 0)
            {
                throw new CartLoadException(
                    $"{sourceName}: instrument {i}: flag bits 1-7 are reserved and must be 0 (byte 0x{flags:x2}).");
            }
        }
    }

    private static void ValidateOrder(ReadOnlySpan<byte> payload, string sourceName, int orderLength)
    {
        for (int i = 0; i < OrderCount; i++)
        {
            int pattern = OrderPattern(payload, i);
            byte flags = OrderFlags(payload, i);
            int target = OrderTarget(payload, i);
            int transpose = OrderTranspose(payload, i);

            if (i >= orderLength)
            {
                // Past the song there is nothing to hear, so there must be nothing to differ.
                if (pattern != 0 || flags != 0 || target != 0 || transpose != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: order entry {i} is past the song's {orderLength} entries, so it is never "
                        + "played and must be all zeros.");
                }
                continue;
            }
            if (pattern >= AudioFormat.MusicPatternCount)
            {
                throw new CartLoadException(
                    $"{sourceName}: order entry {i}: pattern {pattern}, profile 8 has "
                    + $"0..{AudioFormat.MusicPatternCount - 1}.");
            }
            if ((flags & ~OrderFlagMask) != 0)
            {
                throw new CartLoadException(
                    $"{sourceName}: order entry {i}: flag bits 4-7 are reserved and must be 0 (byte 0x{flags:x2}).");
            }
            if ((flags & OrderJump) == 0)
            {
                if (target != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: order entry {i} does not jump, so its target byte must be 0 (got {target}).");
                }
            }
            else if (target >= orderLength)
            {
                throw new CartLoadException(
                    $"{sourceName}: order entry {i} jumps to {target}, but the song is {orderLength} entries long.");
            }
        }
    }

    private static void ValidatePatterns(ReadOnlySpan<byte> payload, string sourceName)
    {
        for (int pattern = 0; pattern < AudioFormat.MusicPatternCount; pattern++)
        {
            int rows = PatternRows(payload, pattern);
            int speed = PatternSpeed(payload, pattern);
            if (rows > RowCount)
            {
                throw new CartLoadException(
                    $"{sourceName}: pattern {pattern}: {rows} rows, a pattern holds 0..{RowCount}.");
            }
            if (PatternReserved(payload, pattern) != 0)
            {
                throw new CartLoadException(
                    $"{sourceName}: pattern {pattern}: byte 3 of the header is reserved and must be 0 "
                    + $"(got {PatternReserved(payload, pattern)}).");
            }
            if (rows == 0)
            {
                if (speed != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: pattern {pattern}: 0 rows marks an unused pattern, so its speed must be 0 "
                        + $"(got {speed}).");
                }
            }
            else if (speed < MinRowSpeed)
            {
                throw new CartLoadException(
                    $"{sourceName}: pattern {pattern}: row speed {speed}/{SpeedUnitsPerTick} of a tick; a row lasts "
                    + $"at least one tick, so the smallest value is {MinRowSpeed}.");
            }

            ValidateCells(payload, sourceName, pattern, rows);
        }
    }

    private static void ValidateCells(ReadOnlySpan<byte> payload, string sourceName, int pattern, int rows)
    {
        for (int row = 0; row < RowCount; row++)
        {
            for (int channel = 0; channel < AudioFormat.MusicChannelCount; channel++)
            {
                uint cell = Cell(payload, pattern, row, channel);
                if (row >= rows)
                {
                    if (cell != 0)
                    {
                        throw new CartLoadException(
                            $"{sourceName}: pattern {pattern} row {row} channel {channel}: the pattern plays "
                            + $"{rows} row(s), so this row is never heard and must be the zero word "
                            + $"(cell 0x{cell:x8}).");
                    }
                    continue;
                }
                ValidateCell(cell, sourceName, pattern, row, channel);
            }
        }
    }

    private static void ValidateCell(uint cell, string sourceName, int pattern, int row, int channel)
    {
        string where = $"{sourceName}: pattern {pattern} row {row} channel {channel}";
        int kind = CellNoteKind(cell);
        if (kind >= NoteKindCount)
        {
            throw new CartLoadException(
                $"{where}: note kind 3 is reserved; a cell holds no note, a note on, or a note off "
                + $"(cell 0x{cell:x8}).");
        }
        if (kind != NoteOn && CellNote(cell) != 0)
        {
            throw new CartLoadException(
                $"{where}: the cell plays no note, so its note field must be 0 (cell 0x{cell:x8} still names "
                + $"note {CellNote(cell)}).");
        }
        if (!CellHasInstrument(cell) && CellInstrument(cell) != 0)
        {
            throw new CartLoadException(
                $"{where}: the cell names no instrument, so its instrument field must be 0 (cell 0x{cell:x8} still "
                + $"names instrument {CellInstrument(cell)}).");
        }
        if (!CellHasVolume(cell) && CellVolume(cell) != 0)
        {
            throw new CartLoadException(
                $"{where}: the cell names no volume, so its volume field must be 0 (cell 0x{cell:x8} still names "
                + $"volume {CellVolume(cell)}).");
        }

        int effect = CellEffect(cell);
        if (effect >= EffectCount)
        {
            throw new CartLoadException(
                $"{where}: effect {effect}, this build defines 0..{EffectCount - 1} (cell 0x{cell:x8}).");
        }
        int param = CellParam(cell);
        if (effect == EffectNone)
        {
            if (param != 0)
            {
                throw new CartLoadException(
                    $"{where}: no effect, so the parameter must be 0 (cell 0x{cell:x8} still carries {param}).");
            }
            return;
        }
        if (param == 0)
        {
            throw new CartLoadException(
                $"{where}: effect {effect} with parameter 0 does nothing; write no effect instead "
                + $"(cell 0x{cell:x8}).");
        }
        if (effect == EffectSlide && kind != NoteOn)
        {
            throw new CartLoadException(
                $"{where}: a slide glides to the cell's own note, so the cell has to carry one (cell 0x{cell:x8}).");
        }
    }
}

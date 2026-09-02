using System.Buffers.Binary;
using Quarp.Api;

namespace Quarp.Core;

/// <summary>
/// The input log of a replay: what the player held on every tick, stored run-length encoded
/// because real play holds the same buttons for dozens of ticks in a row. Since ADR-030 the
/// log carries two streams with independent RLE — the button masks and the pointer (position,
/// held mouse buttons, wheel) — so a moving pointer cannot break the button stream's runs, and
/// a recording with no pointer activity costs one run in a stream nobody reads.
/// Recording in the steady state allocates nothing at all — another tick of the same input is
/// one increment per stream's tail run — and a tail only grows when its own stream's input
/// actually changes, which is amortized array growth.
/// Also owns the <c>.qrpr</c> byte format (see docs/REPLAY-FORMAT.md). One version, 0, and the
/// prototype reads no other (ADR-041):
/// <code>
/// offset size field
/// 0      4    magic "QRPR"
/// 4      2    u16  format version = 0
/// 6      32   cartridge identity (SHA-256)
/// 38     4    i32  RNG seed
/// 42     256  64 x i32 persistent snapshot (raw Fix)
/// 298    4    u32  tick count
/// 302    4    u32  engine version
/// 306    4    u32  stream mask: bit 0 buttons, bit 1 pointer, the rest reserved
/// 310    ...  streams in ascending id order, each: u8 id, u32 run count, then its runs
/// </code>
/// A pointer run is 6 bytes — two position bytes (a per-tick delta, or the absolute position
/// when the jump between ticks does not fit a signed byte), a flags byte (held buttons plus
/// the absolute bit), the tick's wheel delta and a u16 repeat. Delta-plus-RLE is what makes
/// the stream affordable: an idle pointer costs the same two records the buttons cost, and a
/// steady drag merges into one record per direction change instead of one per tick
/// (ADR-030 п.5 named raw coordinates' price and called delta coding a requirement).
/// Every multi-byte field is written little-endian <b>explicitly</b>, never through the host's
/// byte order: this file is the one artifact that has to mean the same thing on Windows-x64
/// and linux-arm64, which is the whole point of the milestone.
/// Not thread-safe: <see cref="InputAt"/> keeps a cursor per stream so sequential
/// resimulation is O(1).
/// </summary>
public sealed class ReplayLog
{
    /// <summary>File magic, ASCII "QRPR".</summary>
    public static ReadOnlySpan<byte> Magic => "QRPR"u8;

    /// <summary>Bytes of the fixed head: magic, version, identity, seed, persistent snapshot, tick count.</summary>
    public const int PrologueSize = 302;

    /// <summary>Bytes of the whole header: the fixed head plus engine version and stream mask.</summary>
    public const int HeaderSize = PrologueSize + 8;

    /// <summary>Bytes of a stream header: u8 stream id plus u32 run count.</summary>
    public const int StreamHeaderSize = 5;

    /// <summary>Bytes per button run record: p0, p1, u16 repeat.</summary>
    public const int RunSize = 4;

    /// <summary>Bytes per pointer run record: dx/x, dy/y, flags, wheel, u16 repeat.</summary>
    public const int PointerRunSize = 6;

    /// <summary>Stream id of the button stream.</summary>
    public const byte ButtonsStreamId = 0;

    /// <summary>Stream id of the pointer stream.</summary>
    public const byte PointerStreamId = 1;

    /// <summary>
    /// The stream-mask bits this build understands. A mask naming any other stream is refused
    /// outright — the reader cannot know an unknown stream's record size, and guessing is how
    /// somebody's game gets replayed wrongly. A future input kind (touch gestures, analog
    /// sticks — their own ADRs) takes the next bit and its own tagged stream <em>without</em> a
    /// version bump; builds from before that ADR refuse its files while the refusal is cheap.
    /// </summary>
    public const uint KnownStreams = (1u << ButtonsStreamId) | (1u << PointerStreamId);

    /// <summary>Bit 7 of a pointer run's flags byte: the position bytes are absolute, not a delta.</summary>
    public const byte PointerAbsoluteFlag = 0x80;

    /// <summary>
    /// The bits a pointer run's flags byte may set: one per <see cref="MouseButton"/> (bits
    /// 0-2) plus <see cref="PointerAbsoluteFlag"/>. Bits 3-6 are reserved and must be zero,
    /// for the same reason button bit 7 is (REPLAY-FORMAT §3).
    /// </summary>
    public const byte KnownPointerFlags = InputState.KnownMouseButtons | PointerAbsoluteFlag;

    /// <summary>Ticks a single run can cover before a new one starts — the u16 repeat field's ceiling.</summary>
    public const int MaxRepeat = ushort.MaxValue;

    /// <summary>
    /// The bits a button mask is allowed to set: one per <see cref="Button"/>, which today
    /// means bits 0 to <see cref="Button.Start"/> and a reserved bit 7.
    /// Derived from the enum instead of spelled out, so an eighth button cannot quietly
    /// disagree with the file format — adding one widens this constant, the reserved bit
    /// disappears, and that is a format version bump, not a patch (REPLAY-FORMAT §3, §7).
    /// </summary>
    public const byte KnownButtons = (1 << ((int)Button.Start + 1)) - 1;

    /// <summary>
    /// Ceiling on a log's length. The file's tick count is a u32, so it can name more ticks
    /// than a log can hold; anything above this is rejected instead of silently truncated.
    /// int.MaxValue ticks is roughly 414 days of game time at 60 Hz.
    /// </summary>
    public const int MaxTicks = int.MaxValue;

    private const int VersionOffset = 4;
    private const int IdentityOffset = 6;
    private const int SeedOffset = 38;
    private const int PersistentOffset = 42;
    private const int TickCountOffset = 298;
    private const int EngineVersionOffset = 302;
    private const int StreamMaskOffset = 306;

    /// <summary>Runs streamed to a Stream in batches, so a long log is not one syscall per record.</summary>
    private const int WriteBatchRuns = 64;

    private struct Run
    {
        public int StartTick;
        public byte Player0;
        public byte Player1;
        public ushort Repeat;
    }

    /// <summary>
    /// One pointer run. The recorded state on tick t inside the run is
    /// (StartX + (t - StartTick) * DeltaX, StartY + ... , Buttons, Wheel); an absolute run
    /// keeps zero deltas, so the same formula answers for both kinds. <see cref="Absolute"/>
    /// is kept so a decoded run re-serializes byte-identically even when the file was not
    /// canonical — the same "kept exactly as recorded" promise the button runs make.
    /// </summary>
    private struct PointerRun
    {
        public int StartTick;
        public byte StartX;
        public byte StartY;
        public sbyte DeltaX;
        public sbyte DeltaY;
        public byte Buttons;
        public sbyte Wheel;
        public bool Absolute;
        public ushort Repeat;
    }

    private Run[] _runs;
    private int _runCount;
    private PointerRun[] _pointerRuns;
    private int _pointerRunCount;
    private int _tickCount;

    // Index of the run the last InputAt landed in, one per stream. Resimulation walks ticks in
    // order, so this turns the common case into a bounds check instead of a binary search.
    private int _cursor;
    private int _pointerCursor;

    /// <summary>Creates an empty log; <paramref name="capacity"/> is a hint in runs, not in ticks.</summary>
    public ReplayLog(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _runs = new Run[capacity];
        _pointerRuns = [];
    }

    /// <summary>Ticks recorded so far; valid tick indices are 0 .. TickCount - 1. Both streams always cover exactly this range.</summary>
    public int TickCount => _tickCount;

    /// <summary>Run-length records in the button stream — the usual bulk of a recording.</summary>
    public int RunCount => _runCount;

    /// <summary>Run-length records in the pointer stream.</summary>
    public int PointerRunCount => _pointerRunCount;

    /// <summary>The format version <see cref="WriteTo(Stream, ReplayHeader)"/> will emit: always <see cref="ReplayHeader.CurrentVersion"/>.</summary>
    public ushort SerializedVersion => ReplayHeader.CurrentVersion;

    /// <summary>Bytes this log occupies once serialized, header included.</summary>
    public long ByteLength =>
        HeaderSize + (2L * StreamHeaderSize)
            + ((long)_runCount * RunSize) + ((long)_pointerRunCount * PointerRunSize);

    // --- recording ---

    /// <summary>
    /// Records the input consumed by the tick that runs from <paramref name="tick"/> to
    /// <paramref name="tick"/> + 1. Ticks must arrive in order: <paramref name="tick"/> equal to
    /// <see cref="TickCount"/> appends, and anything smaller means play resumed after a rewind,
    /// which drops the recorded future first — that timeline did not happen any more.
    /// A gap (a tick beyond <see cref="TickCount"/>) is a programming error and throws.
    /// </summary>
    public void Record(int tick, InputState input)
    {
        if (tick < 0 || tick > _tickCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick,
                $"A replay log is written in order: expected tick {_tickCount} or an earlier one to branch from.");
        }
        if (tick < _tickCount)
        {
            Truncate(tick);
        }
        if (_tickCount == MaxTicks)
        {
            throw new InvalidOperationException($"Replay log is full at {MaxTicks} ticks.");
        }

        RecordButtons(input);
        RecordPointer(input);
        _tickCount++;
    }

    /// <summary>The button half of a tick. Merging looks at the masks alone, so the pointer cannot break a button run.</summary>
    private void RecordButtons(InputState input)
    {
        if (_runCount > 0)
        {
            ref Run tail = ref _runs[_runCount - 1];
            if (tail.Player0 == input.Player0 && tail.Player1 == input.Player1 && tail.Repeat < MaxRepeat)
            {
                // The steady state — the player is still holding the same buttons. One
                // increment, no allocation, no growth. This is the path that runs 60 times
                // a second for the whole session.
                tail.Repeat++;
                return;
            }
        }

        if (_runCount == _runs.Length)
        {
            Grow();
        }
        _runs[_runCount++] = new Run
        {
            StartTick = _tickCount,
            Player0 = input.Player0,
            Player1 = input.Player1,
            Repeat = 1,
        };
    }

    /// <summary>
    /// The pointer half of a tick, delta-encoded. The rule is a pure function of the state
    /// sequence, which is what canonical means here: a tick joins the tail run iff the tail is
    /// a delta run, this tick's step from the previous position equals the tail's delta, the
    /// buttons and wheel match, and the repeat has room; otherwise a new run starts — a delta
    /// run when the step fits a signed byte per axis, an absolute one when it does not
    /// (a teleport across the screen, or the very first tick of a pointer far from 0,0).
    /// The position before tick 0 is defined as (0,0), same as the console's boot snapshot.
    /// </summary>
    // Break recipe: drop "input.MouseWheel == tail.Wheel" from the merge condition below —
    // a one-notch scroll then merges into the surrounding stillness, the wheel tick is lost,
    // and PointerStreamRoundTripsEveryTick plus the CLI wheel test go red.
    private void RecordPointer(InputState input)
    {
        int x = input.MouseX;
        int y = input.MouseY;
        int prevX = 0;
        int prevY = 0;
        if (_pointerRunCount > 0)
        {
            (prevX, prevY) = PointerPositionAt(_tickCount - 1);
        }
        int dx = x - prevX;
        int dy = y - prevY;
        bool fits = dx is >= sbyte.MinValue and <= sbyte.MaxValue
            && dy is >= sbyte.MinValue and <= sbyte.MaxValue;

        if (_pointerRunCount > 0 && fits)
        {
            ref PointerRun tail = ref _pointerRuns[_pointerRunCount - 1];
            if (!tail.Absolute
                && dx == tail.DeltaX && dy == tail.DeltaY
                && input.MouseButtons == tail.Buttons && input.MouseWheel == tail.Wheel
                && tail.Repeat < MaxRepeat)
            {
                tail.Repeat++;
                return;
            }
        }

        if (_pointerRunCount == _pointerRuns.Length)
        {
            GrowPointer();
        }
        _pointerRuns[_pointerRunCount++] = new PointerRun
        {
            StartTick = _tickCount,
            StartX = (byte)x,
            StartY = (byte)y,
            DeltaX = fits ? (sbyte)dx : (sbyte)0,
            DeltaY = fits ? (sbyte)dy : (sbyte)0,
            Buttons = input.MouseButtons,
            Wheel = input.MouseWheel,
            Absolute = !fits,
            Repeat = 1,
        };
    }

    /// <summary>Drops everything from <paramref name="tickCount"/> onwards; the log keeps ticks 0 .. tickCount - 1.</summary>
    public void Truncate(int tickCount)
    {
        if (tickCount < 0 || tickCount > _tickCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tickCount), tickCount,
                $"Cannot truncate a {_tickCount}-tick log to {tickCount} ticks.");
        }
        if (tickCount == _tickCount)
        {
            return;
        }
        if (tickCount == 0)
        {
            Clear();
            return;
        }

        int index = FindRun(tickCount - 1);
        ref Run run = ref _runs[index];
        run.Repeat = (ushort)(tickCount - run.StartTick);
        _runCount = index + 1;
        if (_cursor >= _runCount)
        {
            _cursor = _runCount - 1;
        }

        int pointerIndex = FindPointerRun(tickCount - 1);
        ref PointerRun pointerRun = ref _pointerRuns[pointerIndex];
        pointerRun.Repeat = (ushort)(tickCount - pointerRun.StartTick);
        _pointerRunCount = pointerIndex + 1;
        if (_pointerCursor >= _pointerRunCount)
        {
            _pointerCursor = _pointerRunCount - 1;
        }

        _tickCount = tickCount;
    }

    /// <summary>Empties the log without releasing its buffers — a fresh timeline on the same session.</summary>
    public void Clear()
    {
        _runCount = 0;
        _pointerRunCount = 0;
        _tickCount = 0;
        _cursor = 0;
        _pointerCursor = 0;
    }

    // --- playback ---

    /// <summary>
    /// The input recorded for the tick that runs from <paramref name="tick"/> to
    /// <paramref name="tick"/> + 1, both streams combined. Sequential reads are O(1); a jump
    /// costs one binary search per stream.
    /// </summary>
    public InputState InputAt(int tick)
    {
        if ((uint)tick >= (uint)_tickCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick,
                $"Tick is outside the recorded range 0 .. {_tickCount - 1}.");
        }

        (byte player0, byte player1) = ButtonsAt(tick);
        if (_pointerRunCount == 0)
        {
            return new InputState(player0, player1);
        }

        int index = SeekPointerRun(tick);
        ref PointerRun run = ref _pointerRuns[index];
        int offset = tick - run.StartTick;
        return new InputState(
            player0,
            player1,
            (byte)(run.StartX + (offset * run.DeltaX)),
            (byte)(run.StartY + (offset * run.DeltaY)),
            run.Buttons,
            run.Wheel);
    }

    private (byte Player0, byte Player1) ButtonsAt(int tick)
    {
        int index = _cursor;
        if (index < _runCount)
        {
            ref Run current = ref _runs[index];
            if (tick >= current.StartTick && tick - current.StartTick < current.Repeat)
            {
                return (current.Player0, current.Player1);
            }
            int next = index + 1;
            if (next < _runCount)
            {
                ref Run following = ref _runs[next];
                if (tick >= following.StartTick && tick - following.StartTick < following.Repeat)
                {
                    _cursor = next;
                    return (following.Player0, following.Player1);
                }
            }
        }

        index = FindRun(tick);
        _cursor = index;
        ref Run found = ref _runs[index];
        return (found.Player0, found.Player1);
    }

    /// <summary>The pointer-run index covering <paramref name="tick"/>, cursor-accelerated like the button stream.</summary>
    private int SeekPointerRun(int tick)
    {
        int index = _pointerCursor;
        if (index < _pointerRunCount)
        {
            ref PointerRun current = ref _pointerRuns[index];
            if (tick >= current.StartTick && tick - current.StartTick < current.Repeat)
            {
                return index;
            }
            int next = index + 1;
            if (next < _pointerRunCount)
            {
                ref PointerRun following = ref _pointerRuns[next];
                if (tick >= following.StartTick && tick - following.StartTick < following.Repeat)
                {
                    _pointerCursor = next;
                    return next;
                }
            }
        }

        index = FindPointerRun(tick);
        _pointerCursor = index;
        return index;
    }

    /// <summary>The recorded pointer position on <paramref name="tick"/> — the encoder's "where was the hand a tick ago".</summary>
    private (int X, int Y) PointerPositionAt(int tick)
    {
        int index = SeekPointerRun(tick);
        ref PointerRun run = ref _pointerRuns[index];
        int offset = tick - run.StartTick;
        return (run.StartX + (offset * run.DeltaX), run.StartY + (offset * run.DeltaY));
    }

    /// <summary>Index of the button run covering <paramref name="tick"/>: the last run whose start is not past it.</summary>
    private int FindRun(int tick)
    {
        int low = 0;
        int high = _runCount - 1;
        while (low < high)
        {
            int mid = low + ((high - low + 1) >> 1);
            if (_runs[mid].StartTick <= tick)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }
        return low;
    }

    private int FindPointerRun(int tick)
    {
        int low = 0;
        int high = _pointerRunCount - 1;
        while (low < high)
        {
            int mid = low + ((high - low + 1) >> 1);
            if (_pointerRuns[mid].StartTick <= tick)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }
        return low;
    }

    private void Grow()
    {
        int capacity = _runs.Length == 0 ? 16 : _runs.Length * 2;
        Array.Resize(ref _runs, capacity);
    }

    private void GrowPointer()
    {
        int capacity = _pointerRuns.Length == 0 ? 16 : _pointerRuns.Length * 2;
        Array.Resize(ref _pointerRuns, capacity);
    }

    // --- serialization (.qrpr) ---

    /// <summary>Serializes header and runs into a fresh array — the shell writes it to disk.</summary>
    public byte[] ToBytes(ReplayHeader header)
    {
        long length = ByteLength;
        if (length > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"Replay is {length} bytes, too large for a single array; write it to a Stream instead.");
        }
        var bytes = new byte[length];
        WriteTo(bytes, header);
        return bytes;
    }

    /// <summary>Serializes into a caller-owned buffer and returns the number of bytes written.</summary>
    public int WriteTo(Span<byte> destination, ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        long needed = ByteLength;
        if (destination.Length < needed)
        {
            throw new ArgumentException(
                $"Replay needs {needed} bytes, the destination holds {destination.Length}.", nameof(destination));
        }

        WritePrologue(destination[..PrologueSize], header, ReplayHeader.CurrentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[EngineVersionOffset..], header.EngineVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[StreamMaskOffset..], KnownStreams);
        int offset = HeaderSize;

        destination[offset] = ButtonsStreamId;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[(offset + 1)..], (uint)_runCount);
        offset += StreamHeaderSize;
        for (int i = 0; i < _runCount; i++)
        {
            WriteRun(destination.Slice(offset, RunSize), in _runs[i]);
            offset += RunSize;
        }

        destination[offset] = PointerStreamId;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[(offset + 1)..], (uint)_pointerRunCount);
        offset += StreamHeaderSize;
        for (int i = 0; i < _pointerRunCount; i++)
        {
            WritePointerRun(destination.Slice(offset, PointerRunSize), in _pointerRuns[i]);
            offset += PointerRunSize;
        }
        return offset;
    }

    /// <summary>Streams header and runs out; the stream is neither flushed nor closed.</summary>
    public void WriteTo(Stream destination, ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(header);

        Span<byte> prologue = stackalloc byte[HeaderSize];
        prologue.Clear();
        WritePrologue(prologue, header, ReplayHeader.CurrentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(prologue[EngineVersionOffset..], header.EngineVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(prologue[StreamMaskOffset..], KnownStreams);
        destination.Write(prologue);

        Span<byte> batch = stackalloc byte[WriteBatchRuns * PointerRunSize];
        batch.Clear();
        WriteStreamHeader(destination, batch, ButtonsStreamId, (uint)_runCount);
        int filled = 0;
        for (int i = 0; i < _runCount; i++)
        {
            WriteRun(batch.Slice(filled, RunSize), in _runs[i]);
            filled += RunSize;
            if (filled + RunSize > batch.Length)
            {
                destination.Write(batch[..filled]);
                filled = 0;
            }
        }
        if (filled > 0)
        {
            destination.Write(batch[..filled]);
            filled = 0;
        }

        WriteStreamHeader(destination, batch, PointerStreamId, (uint)_pointerRunCount);
        for (int i = 0; i < _pointerRunCount; i++)
        {
            WritePointerRun(batch.Slice(filled, PointerRunSize), in _pointerRuns[i]);
            filled += PointerRunSize;
            if (filled + PointerRunSize > batch.Length)
            {
                destination.Write(batch[..filled]);
                filled = 0;
            }
        }
        if (filled > 0)
        {
            destination.Write(batch[..filled]);
        }
    }

    private static void WriteStreamHeader(Stream destination, Span<byte> scratch, byte streamId, uint runCount)
    {
        scratch[0] = streamId;
        BinaryPrimitives.WriteUInt32LittleEndian(scratch[1..], runCount);
        destination.Write(scratch[..StreamHeaderSize]);
    }

    /// <summary>
    /// Parses a whole .qrpr image. Rejects a bad magic, any version but
    /// <see cref="ReplayHeader.CurrentVersion"/>, a truncated body, a mask that names a button or
    /// a stream this build does not have, a run that overruns the declared tick count, a stream
    /// whose ticks do not sum to the declared count, and trailing bytes after the last run.
    /// </summary>
    public static ReplayLog FromBytes(ReadOnlySpan<byte> data, out ReplayHeader header)
    {
        if (data.Length < PrologueSize)
        {
            throw new ReplayFormatException(
                $"Replay is truncated: {data.Length} bytes, the header alone needs {PrologueSize}.");
        }
        int tickCount = ParsePrologue(data[..PrologueSize], out ushort version, out int seed);

        if (data.Length < HeaderSize)
        {
            throw new ReplayFormatException(
                $"Replay is truncated: {data.Length} bytes, the header needs {HeaderSize}.");
        }
        uint engineVersion = BinaryPrimitives.ReadUInt32LittleEndian(data[EngineVersionOffset..]);
        uint streamMask = ReadStreamMask(data[StreamMaskOffset..]);
        header = BuildHeader(version, engineVersion, data, seed);

        var log = new ReplayLog(EstimateCapacity(tickCount));
        int cursor = HeaderSize;
        if ((streamMask & (1u << ButtonsStreamId)) != 0)
        {
            cursor = log.ParseButtonsStream(data, cursor, tickCount);
        }
        log._tickCount = 0;   // AppendRun counted button ticks; the pointer parse counts its own.
        if ((streamMask & (1u << PointerStreamId)) != 0)
        {
            cursor = log.ParsePointerStream(data, cursor, tickCount);
        }
        if (cursor != data.Length)
        {
            throw new ReplayFormatException(
                $"Replay has {data.Length - cursor} trailing bytes after the last stream.");
        }
        log.FinishStreams(tickCount, streamMask);
        return log;
    }

    /// <summary>
    /// Reads a replay from a stream, consuming exactly as many bytes as the declared content
    /// needs — so a .qrpr may sit inside a larger container. Same rejections as
    /// <see cref="FromBytes"/>, except that trailing bytes belong to the container, not to us.
    /// </summary>
    public static ReplayLog ReadFrom(Stream source, out ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(source);

        Span<byte> prologue = stackalloc byte[HeaderSize];
        prologue.Clear();
        try
        {
            source.ReadExactly(prologue[..PrologueSize]);
        }
        catch (EndOfStreamException e)
        {
            throw new ReplayFormatException(
                $"Replay is truncated: the header alone needs {PrologueSize} bytes.", e);
        }
        int tickCount = ParsePrologue(prologue[..PrologueSize], out ushort version, out int seed);

        if (source.ReadAtLeast(prologue[PrologueSize..], HeaderSize - PrologueSize, throwOnEndOfStream: false)
            < HeaderSize - PrologueSize)
        {
            throw new ReplayFormatException(
                $"Replay is truncated: the header needs {HeaderSize} bytes.");
        }
        uint engineVersion = BinaryPrimitives.ReadUInt32LittleEndian(prologue[EngineVersionOffset..]);
        uint streamMask = ReadStreamMask(prologue[StreamMaskOffset..]);
        header = BuildHeader(version, engineVersion, prologue, seed);

        var log = new ReplayLog(EstimateCapacity(tickCount));
        if ((streamMask & (1u << ButtonsStreamId)) != 0)
        {
            log.ReadButtonsStream(source, tickCount);
        }
        log._tickCount = 0;
        if ((streamMask & (1u << PointerStreamId)) != 0)
        {
            log.ReadPointerStream(source, tickCount);
        }
        log.FinishStreams(tickCount, streamMask);
        return log;
    }

    // --- stream parsing ---

    private static uint ReadStreamMask(ReadOnlySpan<byte> source)
    {
        uint streamMask = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if ((streamMask & ~KnownStreams) != 0)
        {
            // The same refusal the reserved button bit gets, for the same reason: a stream this
            // build cannot even size is a stream it must not guess past (REPLAY-FORMAT §4).
            throw new ReplayFormatException(
                $"Replay names an input stream this build does not know: mask 0x{streamMask:x8}, "
                + $"known streams 0x{KnownStreams:x8}.");
        }
        return streamMask;
    }

    /// <summary>Reads a stream header and validates its id and run-count sanity.</summary>
    private static uint ParseStreamHeader(ReadOnlySpan<byte> source, byte expectedId, string streamName, int tickCount)
    {
        if (source[0] != expectedId)
        {
            throw new ReplayFormatException(
                $"Replay stream out of order: expected the {streamName} stream (id {expectedId}), found id {source[0]}.");
        }
        uint runCount = BinaryPrimitives.ReadUInt32LittleEndian(source[1..]);
        if (runCount > (uint)tickCount)
        {
            // Every run covers at least one tick, so more runs than ticks is corruption — and
            // the check also caps what a hostile header can make the reader allocate.
            throw new ReplayFormatException(
                $"Replay {streamName} stream declares {runCount} runs for {tickCount} ticks.");
        }
        return runCount;
    }

    private int ParseButtonsStream(ReadOnlySpan<byte> data, int cursor, int tickCount)
    {
        if (cursor + StreamHeaderSize > data.Length)
        {
            throw new ReplayFormatException("Replay is truncated inside the buttons stream header.");
        }
        uint runCount = ParseStreamHeader(data[cursor..], ButtonsStreamId, "buttons", tickCount);
        cursor += StreamHeaderSize;
        for (uint i = 0; i < runCount; i++)
        {
            if (cursor + RunSize > data.Length)
            {
                throw new ReplayFormatException(
                    $"Replay is truncated: the buttons stream declares {runCount} runs, byte {cursor} is past the end.");
            }
            AppendRun(data.Slice(cursor, RunSize), tickCount);
            cursor += RunSize;
        }
        if (_tickCount != tickCount)
        {
            throw new ReplayFormatException(
                $"Replay buttons stream covers {_tickCount} ticks of the declared {tickCount}.");
        }
        return cursor;
    }

    private void ReadButtonsStream(Stream source, int tickCount)
    {
        Span<byte> scratch = stackalloc byte[StreamHeaderSize];
        if (source.ReadAtLeast(scratch, StreamHeaderSize, throwOnEndOfStream: false) < StreamHeaderSize)
        {
            throw new ReplayFormatException("Replay is truncated inside the buttons stream header.");
        }
        uint runCount = ParseStreamHeader(scratch, ButtonsStreamId, "buttons", tickCount);
        Span<byte> run = stackalloc byte[RunSize];
        for (uint i = 0; i < runCount; i++)
        {
            if (source.ReadAtLeast(run, RunSize, throwOnEndOfStream: false) < RunSize)
            {
                throw new ReplayFormatException(
                    $"Replay is truncated: the buttons stream declares {runCount} runs, run {i} is past the end.");
            }
            AppendRun(run, tickCount);
        }
        if (_tickCount != tickCount)
        {
            throw new ReplayFormatException(
                $"Replay buttons stream covers {_tickCount} ticks of the declared {tickCount}.");
        }
    }

    private int ParsePointerStream(ReadOnlySpan<byte> data, int cursor, int tickCount)
    {
        if (cursor + StreamHeaderSize > data.Length)
        {
            throw new ReplayFormatException("Replay is truncated inside the pointer stream header.");
        }
        uint runCount = ParseStreamHeader(data[cursor..], PointerStreamId, "pointer", tickCount);
        cursor += StreamHeaderSize;
        int ticks = 0;
        int prevX = 0;
        int prevY = 0;
        for (uint i = 0; i < runCount; i++)
        {
            if (cursor + PointerRunSize > data.Length)
            {
                throw new ReplayFormatException(
                    $"Replay is truncated: the pointer stream declares {runCount} runs, byte {cursor} is past the end.");
            }
            AppendPointerRun(data.Slice(cursor, PointerRunSize), tickCount, ref ticks, ref prevX, ref prevY);
            cursor += PointerRunSize;
        }
        if (ticks != tickCount)
        {
            throw new ReplayFormatException(
                $"Replay pointer stream covers {ticks} ticks of the declared {tickCount}.");
        }
        return cursor;
    }

    private void ReadPointerStream(Stream source, int tickCount)
    {
        Span<byte> scratch = stackalloc byte[StreamHeaderSize];
        if (source.ReadAtLeast(scratch, StreamHeaderSize, throwOnEndOfStream: false) < StreamHeaderSize)
        {
            throw new ReplayFormatException("Replay is truncated inside the pointer stream header.");
        }
        uint runCount = ParseStreamHeader(scratch, PointerStreamId, "pointer", tickCount);
        Span<byte> run = stackalloc byte[PointerRunSize];
        int ticks = 0;
        int prevX = 0;
        int prevY = 0;
        for (uint i = 0; i < runCount; i++)
        {
            if (source.ReadAtLeast(run, PointerRunSize, throwOnEndOfStream: false) < PointerRunSize)
            {
                throw new ReplayFormatException(
                    $"Replay is truncated: the pointer stream declares {runCount} runs, run {i} is past the end.");
            }
            AppendPointerRun(run, tickCount, ref ticks, ref prevX, ref prevY);
        }
        if (ticks != tickCount)
        {
            throw new ReplayFormatException(
                $"Replay pointer stream covers {ticks} ticks of the declared {tickCount}.");
        }
    }

    /// <summary>A stream absent from the mask means "neutral the whole way"; this closes the books after both parses.</summary>
    private void FinishStreams(int tickCount, uint streamMask)
    {
        if ((streamMask & (1u << ButtonsStreamId)) == 0)
        {
            FillIdleButtons(tickCount);
        }
        if ((streamMask & (1u << PointerStreamId)) == 0)
        {
            FillNeutralPointer(tickCount);
        }
        _tickCount = tickCount;
    }

    /// <summary>
    /// Appends a button run straight from the file. Runs are kept exactly as recorded —
    /// adjacent runs with equal input are legal (that is what the 65535 ceiling produces,
    /// REPLAY-FORMAT §3) and are not merged, so read-then-write is byte-identical.
    /// </summary>
    private void AppendRun(ReadOnlySpan<byte> source, int declaredTicks)
    {
        byte player0 = source[0];
        byte player1 = source[1];
        ushort repeat = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]);
        if (repeat == 0)
        {
            throw new ReplayFormatException($"Replay run {_runCount} has a repeat count of 0.");
        }
        if (((player0 | player1) & ~KnownButtons) != 0)
        {
            // Bit 7 is reserved for an eighth button (REPLAY-FORMAT §3). Reading it as "nothing
            // held" would look harmless today and replay somebody else's game wrongly the day
            // the bit means something, so the file is refused while the refusal is still cheap.
            throw new ReplayFormatException(
                $"Replay run {_runCount} names a button this build does not have: masks "
                + $"0x{player0:x2} and 0x{player1:x2}, only 0x{KnownButtons:x2} is defined.");
        }
        if (repeat > declaredTicks - _tickCount)
        {
            throw new ReplayFormatException(
                $"Replay run {_runCount} covers {repeat} ticks but only {declaredTicks - _tickCount} "
                + $"of the declared {declaredTicks} are left.");
        }
        if (_runCount == _runs.Length)
        {
            Grow();
        }
        _runs[_runCount++] = new Run
        {
            StartTick = _tickCount,
            Player0 = player0,
            Player1 = player1,
            Repeat = repeat,
        };
        _tickCount += repeat;
    }

    /// <summary>
    /// Appends a pointer run straight from the file, validating what a hostile or corrupt file
    /// could hide there: reserved flag bits, a zero repeat, an overrun of the declared ticks,
    /// and a delta walking the position out of the byte range — recorded positions always fit,
    /// so one that does not is corruption, not geometry.
    /// </summary>
    private void AppendPointerRun(
        ReadOnlySpan<byte> source, int declaredTicks, ref int ticks, ref int prevX, ref int prevY)
    {
        byte flags = source[2];
        if ((flags & ~KnownPointerFlags) != 0)
        {
            throw new ReplayFormatException(
                $"Replay pointer run {_pointerRunCount} sets reserved flag bits: 0x{flags:x2}, "
                + $"only 0x{KnownPointerFlags:x2} is defined.");
        }
        ushort repeat = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]);
        if (repeat == 0)
        {
            throw new ReplayFormatException($"Replay pointer run {_pointerRunCount} has a repeat count of 0.");
        }
        if (repeat > declaredTicks - ticks)
        {
            throw new ReplayFormatException(
                $"Replay pointer run {_pointerRunCount} covers {repeat} ticks but only "
                + $"{declaredTicks - ticks} of the declared {declaredTicks} are left.");
        }

        bool absolute = (flags & PointerAbsoluteFlag) != 0;
        int startX;
        int startY;
        sbyte deltaX = 0;
        sbyte deltaY = 0;
        if (absolute)
        {
            startX = source[0];
            startY = source[1];
        }
        else
        {
            deltaX = (sbyte)source[0];
            deltaY = (sbyte)source[1];
            startX = prevX + deltaX;
            startY = prevY + deltaY;
            int endX = startX + ((repeat - 1) * deltaX);
            int endY = startY + ((repeat - 1) * deltaY);
            // The run's positions are linear between its endpoints, so the endpoints are the
            // whole range check. A position outside 0..255 can never have been recorded.
            if (startX is < 0 or > byte.MaxValue || startY is < 0 or > byte.MaxValue
                || endX is < 0 or > byte.MaxValue || endY is < 0 or > byte.MaxValue)
            {
                throw new ReplayFormatException(
                    $"Replay pointer run {_pointerRunCount} walks the position out of range: "
                    + $"({startX},{startY}) to ({endX},{endY}).");
            }
        }

        if (_pointerRunCount == _pointerRuns.Length)
        {
            GrowPointer();
        }
        _pointerRuns[_pointerRunCount++] = new PointerRun
        {
            StartTick = ticks,
            StartX = (byte)startX,
            StartY = (byte)startY,
            DeltaX = deltaX,
            DeltaY = deltaY,
            Buttons = (byte)(flags & InputState.KnownMouseButtons),
            Wheel = (sbyte)source[3],
            Absolute = absolute,
            Repeat = repeat,
        };
        ticks += repeat;
        prevX = startX + ((repeat - 1) * deltaX);
        prevY = startY + ((repeat - 1) * deltaY);
    }

    /// <summary>
    /// Covers the whole recording with the neutral pointer — parked at (0,0), nothing held.
    /// This is what a file whose mask omits the stream means: nothing was recorded because there
    /// was nothing to record. Kept as real runs so recording more ticks
    /// onto a loaded log (continuation mode) finds both streams in step.
    /// </summary>
    private void FillNeutralPointer(int tickCount)
    {
        int covered = 0;
        while (covered < tickCount)
        {
            int repeat = Math.Min(MaxRepeat, tickCount - covered);
            if (_pointerRunCount == _pointerRuns.Length)
            {
                GrowPointer();
            }
            _pointerRuns[_pointerRunCount++] = new PointerRun
            {
                StartTick = covered,
                Repeat = (ushort)repeat,
            };
            covered += repeat;
        }
    }

    /// <summary>The button twin of <see cref="FillNeutralPointer"/>, for a mask without the buttons stream.</summary>
    private void FillIdleButtons(int tickCount)
    {
        int covered = 0;
        while (covered < tickCount)
        {
            int repeat = Math.Min(MaxRepeat, tickCount - covered);
            if (_runCount == _runs.Length)
            {
                Grow();
            }
            _runs[_runCount++] = new Run
            {
                StartTick = covered,
                Repeat = (ushort)repeat,
            };
            covered += repeat;
        }
    }

    private static int ParsePrologue(ReadOnlySpan<byte> prologue, out ushort version, out int seed)
    {
        if (!prologue[..Magic.Length].SequenceEqual(Magic))
        {
            throw new ReplayFormatException("Not a Quarp replay: the stream does not start with the magic 'QRPR'.");
        }
        version = BinaryPrimitives.ReadUInt16LittleEndian(prologue[VersionOffset..]);
        if (version != ReplayHeader.CurrentVersion)
        {
            throw new ReplayFormatException(
                $"Replay format version {version} is not supported; this build reads version "
                + $"{ReplayHeader.CurrentVersion} and no other.");
        }

        seed = BinaryPrimitives.ReadInt32LittleEndian(prologue[SeedOffset..]);

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(prologue[TickCountOffset..]);
        if (declared > (uint)MaxTicks)
        {
            throw new ReplayFormatException(
                $"Replay declares {declared} ticks, past the {MaxTicks} tick ceiling.");
        }
        return (int)declared;
    }

    private static ReplayHeader BuildHeader(ushort version, uint engineVersion, ReadOnlySpan<byte> prologue, int seed)
    {
        Span<int> persistent = stackalloc int[ReplayHeader.PersistentSlots];
        persistent.Clear();
        for (int i = 0; i < ReplayHeader.PersistentSlots; i++)
        {
            persistent[i] = BinaryPrimitives.ReadInt32LittleEndian(prologue[(PersistentOffset + (i * 4))..]);
        }
        return ReplayHeader.ForVersion(
            version, engineVersion, prologue.Slice(IdentityOffset, ReplayHeader.IdentitySize), seed, persistent);
    }

    private void WritePrologue(Span<byte> destination, ReplayHeader header, ushort version)
    {
        Magic.CopyTo(destination);
        // The writer always emits the newest format the log's content needs — v1 while the
        // pointer stream is neutral, so every pre-ADR-030 recording keeps its bytes — whatever
        // version the header was read as; readers reject anything they do not understand.
        BinaryPrimitives.WriteUInt16LittleEndian(destination[VersionOffset..], version);
        header.CartIdentity.CopyTo(destination[IdentityOffset..]);
        BinaryPrimitives.WriteInt32LittleEndian(destination[SeedOffset..], header.Seed);
        ReadOnlySpan<int> persistent = header.Persistent;
        for (int i = 0; i < ReplayHeader.PersistentSlots; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination[(PersistentOffset + (i * 4))..], persistent[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(destination[TickCountOffset..], (uint)_tickCount);
    }

    private static void WriteRun(Span<byte> destination, in Run run)
    {
        destination[0] = run.Player0;
        destination[1] = run.Player1;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], run.Repeat);
    }

    private static void WritePointerRun(Span<byte> destination, in PointerRun run)
    {
        if (run.Absolute)
        {
            destination[0] = run.StartX;
            destination[1] = run.StartY;
        }
        else
        {
            destination[0] = (byte)run.DeltaX;
            destination[1] = (byte)run.DeltaY;
        }
        destination[2] = (byte)(run.Buttons | (run.Absolute ? PointerAbsoluteFlag : 0));
        destination[3] = (byte)run.Wheel;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], run.Repeat);
    }

    /// <summary>
    /// Runs to preallocate for a file declaring this many ticks. Deliberately not derived from
    /// the tick count alone: a hostile header can name two billion ticks with eight bytes of
    /// body, and the reader must not allocate on its word. Growth is amortized anyway, and the
    /// run loop stops the moment the declared count is reached or the stream runs out.
    /// </summary>
    private static int EstimateCapacity(int tickCount) => tickCount <= 0 ? 0 : Math.Min(tickCount, 1024);
}

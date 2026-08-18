using System.Buffers.Binary;
using Quarp.Api;

namespace Quarp.Core;

/// <summary>
/// The button log of a replay: what the player held on every tick, stored run-length encoded
/// because real play holds the same buttons for dozens of ticks in a row.
/// Recording in the steady state allocates nothing at all — another tick of the same input is
/// one increment of the tail run's counter — and the tail only grows when the input actually
/// changes, which is amortized array growth.
/// Also owns the <c>.qrpr</c> byte format (see docs/REPLAY-FORMAT.md):
/// <code>
/// offset size field
/// 0      4    magic "QRPR"
/// 4      2    u16  format version
/// 6      32   cartridge identity (SHA-256)
/// 38     4    i32  RNG seed
/// 42     256  64 x i32 persistent snapshot (raw Fix)
/// 298    4    u32  tick count
/// 302    4*n  runs: u8 player0, u8 player1, u16 repeat
/// </code>
/// Every multi-byte field is written little-endian **explicitly**, never through the host's
/// byte order: this file is the one artifact that has to mean the same thing on Windows-x64
/// and linux-arm64, which is the whole point of the milestone.
/// Not thread-safe: <see cref="InputAt"/> keeps a cursor so sequential resimulation is O(1).
/// </summary>
public sealed class ReplayLog
{
    /// <summary>File magic, ASCII "QRPR".</summary>
    public static ReadOnlySpan<byte> Magic => "QRPR"u8;

    /// <summary>Bytes before the first run: magic, version, identity, seed, persistent snapshot, tick count.</summary>
    public const int PrologueSize = 302;

    /// <summary>Bytes per run record: p0, p1, u16 repeat.</summary>
    public const int RunSize = 4;

    /// <summary>Ticks a single run can cover before a new one starts — the u16 repeat field's ceiling.</summary>
    public const int MaxRepeat = ushort.MaxValue;

    /// <summary>
    /// The bits a v1 button mask is allowed to set: one per <see cref="Button"/>, which today
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

    /// <summary>Runs streamed to a Stream in batches, so a long log is not one syscall per 4 bytes.</summary>
    private const int WriteBatchRuns = 64;

    private struct Run
    {
        public int StartTick;
        public byte Player0;
        public byte Player1;
        public ushort Repeat;
    }

    private Run[] _runs;
    private int _runCount;
    private int _tickCount;

    // Index of the run the last InputAt landed in. Resimulation walks ticks in order, so this
    // turns the common case into a bounds check instead of a binary search.
    private int _cursor;

    /// <summary>Creates an empty log; <paramref name="capacity"/> is a hint in runs, not in ticks.</summary>
    public ReplayLog(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _runs = new Run[capacity];
    }

    /// <summary>Ticks recorded so far; valid tick indices are 0 .. TickCount - 1.</summary>
    public int TickCount => _tickCount;

    /// <summary>Run-length records in use — the log's real memory cost, and its size on disk.</summary>
    public int RunCount => _runCount;

    /// <summary>Bytes this log occupies once serialized, header included.</summary>
    public long ByteLength => PrologueSize + (long)_runCount * RunSize;

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

        if (_runCount > 0)
        {
            ref Run tail = ref _runs[_runCount - 1];
            if (tail.Player0 == input.Player0 && tail.Player1 == input.Player1 && tail.Repeat < MaxRepeat)
            {
                // The steady state — the player is still holding the same buttons. One
                // increment, no allocation, no growth. This is the path that runs 60 times
                // a second for the whole session.
                tail.Repeat++;
                _tickCount++;
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
        _tickCount++;
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
        _tickCount = tickCount;
        if (_cursor >= _runCount)
        {
            _cursor = _runCount - 1;
        }
    }

    /// <summary>Empties the log without releasing its buffer — a fresh timeline on the same session.</summary>
    public void Clear()
    {
        _runCount = 0;
        _tickCount = 0;
        _cursor = 0;
    }

    // --- playback ---

    /// <summary>
    /// The input recorded for the tick that runs from <paramref name="tick"/> to
    /// <paramref name="tick"/> + 1. Sequential reads are O(1); a jump costs one binary search.
    /// </summary>
    public InputState InputAt(int tick)
    {
        if ((uint)tick >= (uint)_tickCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick,
                $"Tick is outside the recorded range 0 .. {_tickCount - 1}.");
        }

        int index = _cursor;
        if (index < _runCount)
        {
            ref Run current = ref _runs[index];
            if (tick >= current.StartTick && tick - current.StartTick < current.Repeat)
            {
                return new InputState(current.Player0, current.Player1);
            }
            int next = index + 1;
            if (next < _runCount)
            {
                ref Run following = ref _runs[next];
                if (tick >= following.StartTick && tick - following.StartTick < following.Repeat)
                {
                    _cursor = next;
                    return new InputState(following.Player0, following.Player1);
                }
            }
        }

        index = FindRun(tick);
        _cursor = index;
        ref Run found = ref _runs[index];
        return new InputState(found.Player0, found.Player1);
    }

    /// <summary>Index of the run covering <paramref name="tick"/>: the last run whose start is not past it.</summary>
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

    private void Grow()
    {
        int capacity = _runs.Length == 0 ? 16 : _runs.Length * 2;
        Array.Resize(ref _runs, capacity);
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
        WritePrologue(destination[..PrologueSize], header);
        int offset = PrologueSize;
        for (int i = 0; i < _runCount; i++)
        {
            WriteRun(destination.Slice(offset, RunSize), in _runs[i]);
            offset += RunSize;
        }
        return offset;
    }

    /// <summary>Streams header and runs out; the stream is neither flushed nor closed.</summary>
    public void WriteTo(Stream destination, ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(header);

        Span<byte> prologue = stackalloc byte[PrologueSize];
        prologue.Clear();
        WritePrologue(prologue, header);
        destination.Write(prologue);

        Span<byte> batch = stackalloc byte[WriteBatchRuns * RunSize];
        batch.Clear();
        int filled = 0;
        for (int i = 0; i < _runCount; i++)
        {
            WriteRun(batch.Slice(filled, RunSize), in _runs[i]);
            filled += RunSize;
            if (filled == batch.Length)
            {
                destination.Write(batch);
                filled = 0;
            }
        }
        if (filled > 0)
        {
            destination.Write(batch[..filled]);
        }
    }

    /// <summary>
    /// Parses a whole .qrpr image. Rejects a bad magic, an unknown version, a truncated body,
    /// a mask that names a button this build does not have, a run that overruns the declared
    /// tick count, and trailing bytes after the last run.
    /// </summary>
    public static ReplayLog FromBytes(ReadOnlySpan<byte> data, out ReplayHeader header)
    {
        if (data.Length < PrologueSize)
        {
            throw new ReplayFormatException(
                $"Replay is truncated: {data.Length} bytes, the header alone needs {PrologueSize}.");
        }
        int tickCount = ParsePrologue(data[..PrologueSize], out header);

        ReadOnlySpan<byte> body = data[PrologueSize..];
        if (body.Length % RunSize != 0)
        {
            throw new ReplayFormatException(
                $"Replay body is {body.Length} bytes, not a whole number of {RunSize}-byte runs.");
        }

        var log = new ReplayLog(EstimateCapacity(tickCount));
        int offset = 0;
        while (log._tickCount < tickCount)
        {
            if (offset + RunSize > body.Length)
            {
                throw new ReplayFormatException(
                    $"Replay is truncated: the header declares {tickCount} ticks, "
                    + $"the runs end after {log._tickCount}.");
            }
            log.AppendRun(body.Slice(offset, RunSize), tickCount);
            offset += RunSize;
        }
        if (offset != body.Length)
        {
            throw new ReplayFormatException(
                $"Replay has {body.Length - offset} trailing bytes after the last run.");
        }
        return log;
    }

    /// <summary>
    /// Reads a replay from a stream, consuming exactly as many bytes as the declared tick count
    /// needs — so a .qrpr may sit inside a larger container. Same rejections as
    /// <see cref="FromBytes"/>, except that trailing bytes belong to the container, not to us.
    /// </summary>
    public static ReplayLog ReadFrom(Stream source, out ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(source);

        Span<byte> prologue = stackalloc byte[PrologueSize];
        prologue.Clear();
        try
        {
            source.ReadExactly(prologue);
        }
        catch (EndOfStreamException e)
        {
            throw new ReplayFormatException(
                $"Replay is truncated: the header alone needs {PrologueSize} bytes.", e);
        }
        int tickCount = ParsePrologue(prologue, out header);

        var log = new ReplayLog(EstimateCapacity(tickCount));
        Span<byte> run = stackalloc byte[RunSize];
        run.Clear();
        while (log._tickCount < tickCount)
        {
            if (source.ReadAtLeast(run, RunSize, throwOnEndOfStream: false) < RunSize)
            {
                throw new ReplayFormatException(
                    $"Replay is truncated: the header declares {tickCount} ticks, "
                    + $"the runs end after {log._tickCount}.");
            }
            log.AppendRun(run, tickCount);
        }
        return log;
    }

    private static int ParsePrologue(ReadOnlySpan<byte> prologue, out ReplayHeader header)
    {
        if (!prologue[..Magic.Length].SequenceEqual(Magic))
        {
            throw new ReplayFormatException("Not a Quarp replay: the stream does not start with the magic 'QRPR'.");
        }
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(prologue[VersionOffset..]);
        if (version != ReplayHeader.CurrentVersion)
        {
            throw new ReplayFormatException(
                $"Replay format version {version} is not supported; this build reads version "
                + $"{ReplayHeader.CurrentVersion}.");
        }

        int seed = BinaryPrimitives.ReadInt32LittleEndian(prologue[SeedOffset..]);
        Span<int> persistent = stackalloc int[ReplayHeader.PersistentSlots];
        persistent.Clear();
        for (int i = 0; i < ReplayHeader.PersistentSlots; i++)
        {
            persistent[i] = BinaryPrimitives.ReadInt32LittleEndian(prologue[(PersistentOffset + i * 4)..]);
        }

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(prologue[TickCountOffset..]);
        if (declared > (uint)MaxTicks)
        {
            throw new ReplayFormatException(
                $"Replay declares {declared} ticks, past the {MaxTicks} tick ceiling.");
        }

        header = ReplayHeader.ForVersion(
            version, prologue.Slice(IdentityOffset, ReplayHeader.IdentitySize), seed, persistent);
        return (int)declared;
    }

    /// <summary>
    /// Appends a run straight from the file. Runs are kept exactly as recorded — adjacent runs
    /// with equal input are legal (that is what the 65535 ceiling produces, REPLAY-FORMAT §3)
    /// and are not merged, so read-then-write is byte-identical.
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

    private void WritePrologue(Span<byte> destination, ReplayHeader header)
    {
        Magic.CopyTo(destination);
        // The writer always emits the format it knows how to emit, whatever version the header
        // was read as; readers reject anything they do not understand.
        BinaryPrimitives.WriteUInt16LittleEndian(destination[VersionOffset..], ReplayHeader.CurrentVersion);
        header.CartIdentity.CopyTo(destination[IdentityOffset..]);
        BinaryPrimitives.WriteInt32LittleEndian(destination[SeedOffset..], header.Seed);
        ReadOnlySpan<int> persistent = header.Persistent;
        for (int i = 0; i < ReplayHeader.PersistentSlots; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination[(PersistentOffset + i * 4)..], persistent[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(destination[TickCountOffset..], (uint)_tickCount);
    }

    private static void WriteRun(Span<byte> destination, in Run run)
    {
        destination[0] = run.Player0;
        destination[1] = run.Player1;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], run.Repeat);
    }

    /// <summary>
    /// Runs to preallocate for a file declaring this many ticks. Deliberately not derived from
    /// the tick count alone: a hostile header can name two billion ticks with eight bytes of
    /// body, and the reader must not allocate on its word. Growth is amortized anyway, and the
    /// run loop stops the moment the declared count is reached or the stream runs out.
    /// </summary>
    private static int EstimateCapacity(int tickCount) => tickCount <= 0 ? 0 : Math.Min(tickCount, 1024);
}

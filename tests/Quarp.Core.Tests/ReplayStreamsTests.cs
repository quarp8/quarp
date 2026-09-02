using System.Buffers.Binary;
using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// REPLAY-FORMAT v2 (ADR-030): the split-stream layout, the pointer stream's delta encoding,
/// the version switch that keeps every pre-mouse recording byte-identical, and every new
/// rejection the decoder owes a hostile file. Same discipline as <see cref="ReplayFormatTests"/>:
/// each negative test starts from one valid file and corrupts exactly one field, and the
/// control test proves that file is accepted, so "it threw" means "because of that field".
/// </summary>
public class ReplayStreamsTests
{
    // Layout constants written out, not borrowed from ReplayLog: these numbers are a published
    // promise (REPLAY-FORMAT §2, §3), and a test restated from the constants it checks holds
    // nothing.
    private const int PrologueSize = 302;
    private const int HeaderSize = 310;
    private const int StreamHeaderSize = 5;
    private const int RunSize = 4;
    private const int PointerRunSize = 6;
    private const int VersionOffset = 4;
    private const int TickCountOffset = 298;
    private const int EngineVersionOffset = 302;
    private const int StreamMaskOffset = 306;

    private const int Seed = 4242;

    /// <summary>Ticks the valid v2 file covers; see <see cref="ValidFile"/>.</summary>
    private const int ValidTicks = 3;

    /// <summary>Buttons: 1 idle run. Pointer: 3 runs — still, a (10,5) step with L and wheel, a zero step with L and wheel.</summary>
    private const int ValidPointerRuns = 3;

    private const int ButtonsStreamOffset = HeaderSize;
    private const int PointerStreamOffset = ButtonsStreamOffset + StreamHeaderSize + RunSize;
    private const int PointerRunsOffset = PointerStreamOffset + StreamHeaderSize;

    private static readonly bool[] BothReaders = new[] { false, true };

    private static ReplayHeader Header() =>
        new(ReplayHeader.UnknownIdentity, Seed, stackalloc int[] { 7, -3 });

    /// <summary>
    /// The file every rejection test corrupts by one field: three ticks whose pointer moves,
    /// presses and scrolls, so all three pointer-run shapes short of the absolute one exist.
    /// </summary>
    private static ReplayLog ValidLog()
    {
        var log = new ReplayLog();
        log.Record(0, new InputState(0, 0));
        log.Record(1, new InputState(0, 0).WithMouse(10, 5, 1 << (int)MouseButton.Left, 2));
        log.Record(2, new InputState(0, 0).WithMouse(10, 5, 1 << (int)MouseButton.Left, 2));
        return log;
    }

    private static byte[] ValidFile() => ValidLog().ToBytes(Header());

    private static ReplayLog Read(byte[] bytes, bool fromStream, out ReplayHeader header)
    {
        if (fromStream)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return ReplayLog.ReadFrom(stream, out header);
        }
        return ReplayLog.FromBytes(bytes, out header);
    }

    // --- one version, one layout (ADR-041) ---

    [Fact]
    public void ARecordingWithoutPointerActivityStillCarriesBothStreams()
    {
        // Since ADR-041 there is no second layout to fall back to: every file carries the
        // extended header and both stream headers, whether the pointer moved or not.
        var log = new ReplayLog();
        for (int tick = 0; tick < 10; tick++)
        {
            log.Record(tick, new InputState(tick < 5 ? (byte)0 : (byte)2, 0));
        }

        Assert.Equal(ReplayHeader.CurrentVersion, log.SerializedVersion);
        byte[] file = log.ToBytes(Header());
        Assert.Equal(
            HeaderSize + (2 * StreamHeaderSize) + (2 * RunSize) + PointerRunSize, file.Length);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(VersionOffset)));
    }

    [Fact]
    public void PointerActivityIsWrittenInTheDocumentedLayout()
    {
        byte[] file = ValidFile();

        Assert.Equal(
            HeaderSize + (2 * StreamHeaderSize) + RunSize + (ValidPointerRuns * PointerRunSize),
            file.Length);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(VersionOffset)));
        Assert.Equal(
            ReplayHeader.CurrentEngineVersion,
            BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(EngineVersionOffset)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(StreamMaskOffset)));
        Assert.Equal(ReplayLog.ButtonsStreamId, file[ButtonsStreamOffset]);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ButtonsStreamOffset + 1)));
        Assert.Equal(ReplayLog.PointerStreamId, file[PointerStreamOffset]);
        Assert.Equal(
            (uint)ValidPointerRuns, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(PointerStreamOffset + 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFileEveryRejectionTestStartsFromIsAccepted(bool fromStream)
    {
        ReplayLog log = Read(ValidFile(), fromStream, out ReplayHeader header);

        Assert.Equal(ValidTicks, log.TickCount);
        Assert.Equal(ValidPointerRuns, log.PointerRunCount);
        Assert.Equal(Seed, header.Seed);
        Assert.Equal(ReplayHeader.CurrentVersion, header.Version);
        Assert.Equal(ReplayHeader.CurrentEngineVersion, header.EngineVersion);
        Assert.Equal(10, log.InputAt(1).MouseX);
        Assert.Equal(5, log.InputAt(1).MouseY);
    }

    // --- round trips ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerStreamRoundTripsEveryTick(bool fromStream)
    {
        // Every shape the encoder can emit in one recording: stillness, small steps that merge,
        // a button press and release, a wheel tick, and a teleport past the ±127 delta range.
        // Tick 7 changes ONLY the wheel against tick 6 — position and buttons identical — so
        // the wheel is the sole thing keeping those ticks in different runs; a merge rule that
        // forgot the wheel would swallow the notch, and this is the test that goes red for it
        // (the Break recipe on ReplayLog.RecordPointer).
        var states = new InputState[]
        {
            default,
            new InputState(0, 0).WithMouse(3, 2, 0, 0),
            new InputState(0, 0).WithMouse(6, 4, 0, 0),
            new InputState(0, 0).WithMouse(9, 6, 0, 0),
            new InputState(4, 0).WithMouse(9, 6, 1 << (int)MouseButton.Left, 0),
            new InputState(4, 0).WithMouse(9, 6, 1 << (int)MouseButton.Left, 0),
            new InputState(0, 0).WithMouse(9, 6, 0, 0),
            new InputState(0, 0).WithMouse(9, 6, 0, -3),
            new InputState(0, 0).WithMouse(250, 89, 1 << (int)MouseButton.Middle, 0),
            new InputState(0, 0).WithMouse(250, 89, 1 << (int)MouseButton.Middle, 0),
            new InputState(0, 0).WithMouse(2, 88, 0, 0),
        };
        var log = new ReplayLog();
        for (int tick = 0; tick < states.Length; tick++)
        {
            log.Record(tick, states[tick]);
        }

        byte[] file = log.ToBytes(Header());
        ReplayLog reread = Read(file, fromStream, out ReplayHeader header);

        Assert.Equal(states.Length, reread.TickCount);
        for (int tick = 0; tick < states.Length; tick++)
        {
            Assert.Equal(log.InputAt(tick), reread.InputAt(tick));
            Assert.Equal(states[tick], reread.InputAt(tick));
        }
        // §3: reading and writing back is a function — the file is its own canonical form.
        Assert.Equal(file, reread.ToBytes(header));
    }

    [Fact]
    public void AMovingPointerDoesNotBreakTheButtonStream()
    {
        // The arithmetic reason ADR-030 split the log into streams: buttons hold for dozens of
        // ticks while the hand moves every tick. One held button over a wandering pointer must
        // stay ONE button run, or the whole point of the split is lost.
        var log = new ReplayLog();
        int x = 0;
        for (int tick = 0; tick < 100; tick++)
        {
            x += (tick % 3) + 1;   // A varying step, so pointer runs cannot merge either.
            log.Record(tick, new InputState(2, 0).WithMouse(x % 160, 45, 0, 0));
        }

        Assert.Equal(1, log.RunCount);
        Assert.True(log.PointerRunCount > 10, $"expected many pointer runs, got {log.PointerRunCount}");
    }

    [Fact]
    public void AConstantVelocityDragMergesIntoOnePointerRun()
    {
        // The delta encoding's payoff, and the claim REPLAY-FORMAT §8 prices: a steady drag is
        // one run per direction change, not one per tick.
        var log = new ReplayLog();
        for (int tick = 0; tick < 50; tick++)
        {
            log.Record(tick, new InputState(0, 0).WithMouse(10 + tick, 20, 1 << (int)MouseButton.Left, 0));
        }

        // Run 1: tick 0's jump to (10,20) plus the press; run 2: 49 ticks of dx=1.
        Assert.Equal(2, log.PointerRunCount);
    }

    [Fact]
    public void RecordingOntoALoadedButtonOnlyLogKeepsBothStreamsInStep()
    {
        // Continuation mode: play a replay recorded without a mouse, rewind, keep playing with
        // one. The loaded log's neutral pointer stream has to accept new ticks like a recorded one.
        byte[] file;
        {
            var buttonsOnly = new ReplayLog();
            for (int tick = 0; tick < 5; tick++)
            {
                buttonsOnly.Record(tick, new InputState(2, 0));
            }
            file = buttonsOnly.ToBytes(Header());
        }
        ReplayLog log = ReplayLog.FromBytes(file, out _);

        log.Record(3, new InputState(0, 0).WithMouse(80, 45, 1 << (int)MouseButton.Left, 0));

        Assert.Equal(4, log.TickCount);
        Assert.Equal(0, log.InputAt(2).MouseX);
        Assert.Equal(80, log.InputAt(3).MouseX);
        Assert.Equal(ReplayHeader.CurrentVersion, log.SerializedVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALongStillPointerSplitsAtTheRepeatCeilingAndRoundTrips(bool fromStream)
    {
        // The button stream's 65535 rule, verbatim on the new stream (ADR-030 п.5: canonicity
        // extends to each stream word for word).
        const int Ticks = 100_000;
        var log = new ReplayLog();
        for (int tick = 0; tick < Ticks; tick++)
        {
            log.Record(tick, new InputState(0, 0).WithMouse(80, 45, 0, 0));
        }

        // Run 1: the jump to (80,45); run 2: 65535 still ticks; run 3: the remainder.
        Assert.Equal(3, log.PointerRunCount);

        byte[] file = log.ToBytes(Header());
        ReplayLog reread = Read(file, fromStream, out ReplayHeader header);
        Assert.Equal(Ticks, reread.TickCount);
        Assert.Equal(80, reread.InputAt(Ticks - 1).MouseX);
        Assert.Equal(file, reread.ToBytes(header));
    }

    [Fact]
    public void TruncateCutsThePointerStreamWithTheButtons()
    {
        var log = ValidLog();

        // A rewind branch: re-record tick 1 with a different pointer.
        log.Record(1, new InputState(0, 0).WithMouse(100, 50, 0, 0));

        Assert.Equal(2, log.TickCount);
        Assert.Equal(100, log.InputAt(1).MouseX);
        Assert.Equal(0, log.InputAt(0).MouseX);
    }

    // --- the streams a mask may omit ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMaskWithoutAStreamMeansNeutralAllTheWay(bool fromStream)
    {
        // Hand-built: a file whose mask names only the pointer stream. The buttons read as
        // idle for every tick — absence is "nothing to record", not corruption. This is what
        // keeps the mask a real extension point instead of a constant.
        var file = new byte[HeaderSize + StreamHeaderSize + PointerRunSize];
        "QRPR"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(VersionOffset), ReplayHeader.CurrentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(TickCountOffset), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(StreamMaskOffset), 1u << ReplayLog.PointerStreamId);
        file[HeaderSize] = ReplayLog.PointerStreamId;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HeaderSize + 1), 1);
        int run = HeaderSize + StreamHeaderSize;
        file[run + 0] = 15;   // dx from (0,0)
        file[run + 1] = 8;
        file[run + 2] = 1 << (int)MouseButton.Left;
        file[run + 3] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(run + 4), 4);

        ReplayLog log = Read(file, fromStream, out _);

        Assert.Equal(4, log.TickCount);
        for (int tick = 0; tick < 4; tick++)
        {
            InputState state = log.InputAt(tick);
            Assert.Equal(0, state.Player0);
            Assert.Equal(15 * (tick + 1), state.MouseX);
            Assert.True(state.MouseIsDown(MouseButton.Left));
        }
    }

    // --- the §4 additions: what the decoder must refuse ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMaskNamingAnUnknownStreamIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(StreamMaskOffset), ReplayLog.KnownStreams | (1u << 2));

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("stream", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStreamOutOfOrderIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        file[ButtonsStreamOffset] = ReplayLog.PointerStreamId;

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("buttons", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedPointerFlagBitsAreRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        file[PointerRunsOffset + 2] |= 0x08;

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("reserved", e.Message);
        Assert.Contains("pointer run 0", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APointerRepeatOfZeroIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(PointerRunsOffset + 4), 0);

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("pointer run 0", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APointerStreamOverrunningTheTickCountIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        int lastRun = PointerRunsOffset + ((ValidPointerRuns - 1) * PointerRunSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(lastRun + 4), 2);

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains($"pointer run {ValidPointerRuns - 1}", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APointerStreamCoveringTooFewTicksIsRefused(bool fromStream)
    {
        // Dropping the run count is the cheap way to shorten the stream without moving bytes;
        // the per-stream "sum of repeats equals tickCount" check has to catch it.
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(PointerStreamOffset + 1), ValidPointerRuns - 1);

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("pointer stream covers", e.Message);
        Assert.Contains($"{ValidTicks}", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeltaWalkingThePositionOutOfRangeIsRefused(bool fromStream)
    {
        // A recorded position always fits 0..255, so a delta that leaves the range can only be
        // corruption — and letting it through would materialize as a byte wraparound somewhere
        // on the other side of the screen.
        byte[] file = ValidFile();
        file[PointerRunsOffset] = unchecked((byte)-1);   // First step from (0,0) goes to x = -1.

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("out of range", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARunCountLargerThanTheTickCountIsRefused(bool fromStream)
    {
        // Every run covers at least a tick, so this is corruption — and it is also the cap on
        // what a hostile header can make the reader allocate.
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(PointerStreamOffset + 1), 100);

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("100 runs", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFileCutBeforeItsExtendedHeaderIsRefused(bool fromStream)
    {
        byte[] valid = ValidFile();
        var stub = new byte[HeaderSize - 1];
        Array.Copy(valid, stub, stub.Length);

        var e = Assert.Throws<ReplayFormatException>(() => { Read(stub, fromStream, out _); });
        Assert.Contains("the header needs", e.Message);
    }

    [Fact]
    public void TrailingBytesAfterTheLastStreamAreRefused()
    {
        byte[] valid = ValidFile();
        var file = new byte[valid.Length + 3];
        valid.CopyTo(file, 0);

        var e = Assert.Throws<ReplayFormatException>(() => { ReplayLog.FromBytes(file, out _); });
        Assert.Contains("trailing", e.Message);

        // The stream reader is different here by design, not by omission: a .qrpr may sit
        // inside a larger container, so bytes past the last stream belong to the container.
        using var stream = new MemoryStream(file, writable: false);
        Assert.Equal(ValidTicks, ReplayLog.ReadFrom(stream, out _).TickCount);
    }

    // --- the engine version rides along ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEngineVersionSurvivesARewriteVerbatim(bool fromStream)
    {
        // A rewritten replay still names the engine that recorded it, not the one holding the
        // pen — otherwise the field answers the wrong question (REPLAY-FORMAT §7).
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(EngineVersionOffset), 7);

        ReplayLog log = Read(file, fromStream, out ReplayHeader header);

        Assert.Equal(7u, header.EngineVersion);
        Assert.Equal(file, log.ToBytes(header));
    }
}

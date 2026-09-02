using System.Buffers.Binary;
using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// REPLAY-FORMAT §4 turned into code: every condition the decoder is obliged to reject, plus
/// the half that is easy to skip — proof that each rejection is caused by the corruption under
/// test and not by the test's own file being unreadable for some unrelated reason.
///
/// <para>That is what <see cref="TheFileEveryRejectionTestStartsFromIsAccepted"/> is for. Every
/// negative test below starts from the same builder and changes exactly one field, so "it
/// threw" means "it threw because of that field". A rejection test without this control is the
/// M2 determinism check all over again: green, and green for no reason.</para>
///
/// <para>These live in Core's tests rather than CartKit's because the format is Core's and
/// needs nothing else — no Roslyn, no cartridge, no window. The integration angle (a replay
/// actually driving a compiled cartridge) stays in <c>TimeMachineIntegrationTests</c>.</para>
/// </summary>
public class ReplayFormatTests
{
    // Layout constants are written out here instead of borrowed from ReplayLog on purpose: a
    // test that reused the decoder's own offsets would follow the layout wherever it drifted,
    // and these numbers are a published promise (REPLAY-FORMAT §2, §7 — any change is v2).
    private const int PrologueSize = 302;
    private const int ExtendedHeaderSize = 8;      // engine version + stream mask
    private const int StreamHeaderSize = 5;        // u8 stream id + u32 run count
    private const int RunSize = 4;
    private const int VersionOffset = 4;
    private const int TickCountOffset = 298;

    /// <summary>Where the first button run starts: 302 + 8 of extended header + 5 of stream header.</summary>
    private const int ButtonRunsOffset = PrologueSize + ExtendedHeaderSize + StreamHeaderSize;

    /// <summary>Bytes a whole file of <paramref name="runs"/> button runs and <paramref name="pointerRuns"/> pointer runs takes.</summary>
    private static int FileSize(int runs, int pointerRuns = 1) =>
        ButtonRunsOffset + (runs * RunSize) + StreamHeaderSize + (pointerRuns * PointerRunSize);

    /// <summary>Bytes per pointer run: dx, dy, flags, wheel, u16 repeat.</summary>
    private const int PointerRunSize = 6;

    /// <summary>Bit 7: reserved for an eighth button, and the reason files that set it are refused.</summary>
    private const byte ReservedBit = 0x80;

    private const byte RightMask = 1 << (int)Button.Right;

    private const int Seed = 12345;

    /// <summary>Ticks the file from <see cref="ValidFile"/> covers: a 3-tick run and a 4-tick one.</summary>
    private const int ValidTicks = 7;

    /// <summary>Both entry points share <c>AppendRun</c>, so every check is asserted through both.</summary>
    private static readonly bool[] BothReaders = new[] { false, true };

    /// <summary>
    /// The file every rejection test corrupts by exactly one field. Two runs, not one, so that
    /// "the message names the offending run" is a claim with something to be wrong about.
    /// </summary>
    private static byte[] ValidFile()
    {
        var log = new ReplayLog();
        int tick = 0;
        for (int i = 0; i < 3; i++)
        {
            log.Record(tick++, new InputState(0, 0));
        }
        for (int i = 0; i < 4; i++)
        {
            log.Record(tick++, new InputState(RightMask, 0));
        }
        return log.ToBytes(Header());
    }

    private static ReplayHeader Header()
    {
        return new ReplayHeader(ReplayHeader.UnknownIdentity, Seed, stackalloc int[] { 7, -3, 0, 99 });
    }

    private static ReplayLog Read(byte[] bytes, bool fromStream, out ReplayHeader header)
    {
        if (fromStream)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return ReplayLog.ReadFrom(stream, out header);
        }
        return ReplayLog.FromBytes(bytes, out header);
    }

    private static int MaskOffset(int run, int player) => ButtonRunsOffset + (run * RunSize) + player;

    private static int RepeatOf(byte[] file, int run) =>
        BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(ButtonRunsOffset + (run * RunSize) + 2));

    // --- the control every negative test below leans on ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFileEveryRejectionTestStartsFromIsAccepted(bool fromStream)
    {
        ReplayLog log = Read(ValidFile(), fromStream, out ReplayHeader header);

        Assert.Equal(ValidTicks, log.TickCount);
        Assert.Equal(2, log.RunCount);
        Assert.Equal(0, log.InputAt(0).Player0);
        Assert.Equal(RightMask, log.InputAt(3).Player0);
        Assert.Equal(Seed, header.Seed);
    }

    [Fact]
    public void TheLayoutIsTheOneTheDocumentDescribes()
    {
        // §2 and §8: a 302-byte fixed head, eight bytes of engine version and stream mask, then
        // the two streams, each behind a five-byte header. One version, one layout (ADR-041).
        Assert.Equal(PrologueSize, ReplayLog.PrologueSize);
        Assert.Equal(PrologueSize + ExtendedHeaderSize, ReplayLog.HeaderSize);
        Assert.Equal(StreamHeaderSize, ReplayLog.StreamHeaderSize);
        Assert.Equal(RunSize, ReplayLog.RunSize);
        Assert.Equal(PointerRunSize, ReplayLog.PointerRunSize);
        Assert.Equal(65535, ReplayLog.MaxRepeat);
        Assert.Equal(FileSize(2), ValidFile().Length);
    }

    // --- the reserved bit (REPLAY-FORMAT §3, §4 row "бит 7") ---

    [Theory]
    [InlineData(0, 0, "run 0")]
    [InlineData(0, 1, "run 0")]
    [InlineData(1, 0, "run 1")]
    [InlineData(1, 1, "run 1")]
    public void AMaskWithTheReservedBitIsRefusedAndTheOffendingRunIsNamed(int run, int player, string named)
    {
        byte[] file = ValidFile();
        int offset = MaskOffset(run, player);
        var corrupted = (byte)(file[offset] | ReservedBit);
        file[offset] = corrupted;

        for (int i = 0; i < BothReaders.Length; i++)
        {
            bool fromStream = BothReaders[i];
            var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
            Assert.Contains(named, e.Message);
            Assert.Contains($"0x{corrupted:x2}", e.Message);
        }
    }

    [Fact]
    public void EveryBitThatIsAButtonIsStillAccepted()
    {
        // The control for the test above. Same one-byte edit, seven times, with a real button
        // in it each time: without this, "the decoder threw" would only prove that the test
        // built a file it dislikes, not that bit 7 is what it disliked.
        for (int bit = 0; bit <= (int)Button.Start; bit++)
        {
            byte[] file = ValidFile();
            var mask = (byte)(1 << bit);
            file[MaskOffset(0, 0)] = mask;

            ReplayLog log = ReplayLog.FromBytes(file, out _);
            Assert.Equal(mask, log.InputAt(0).Player0);
        }

        byte[] all = ValidFile();
        all[MaskOffset(0, 0)] = ReplayLog.KnownButtons;
        all[MaskOffset(0, 1)] = ReplayLog.KnownButtons;
        Assert.Equal(ReplayLog.KnownButtons, ReplayLog.FromBytes(all, out _).InputAt(0).Player0);
    }

    [Fact]
    public void TheMaskCoversEveryButtonTheApiDefinesAndBitSevenStaysReserved()
    {
        foreach (Button button in Enum.GetValues<Button>())
        {
            Assert.InRange((int)button, 0, 7);
            Assert.True(
                (ReplayLog.KnownButtons & (1 << (int)button)) != 0,
                $"{button} = {(int)button} has no bit in a replay mask.");
        }

        // Deliberately brittle. An eighth button takes bit 7, and on that day a file that sets
        // it stops being corrupt and starts being a change to the one living version (§7) — whoever adds the
        // button has to come here and decide that, rather than find out from a user's replay.
        Assert.Equal(0, ReplayLog.KnownButtons & ReservedBit);
        Assert.Equal(0x7f, ReplayLog.KnownButtons);
    }

    // --- splitting a long run (REPLAY-FORMAT §3): adjacent equal masks are legal ---

    [Fact]
    public void ALongRunOfUnchangedInputSplitsIntoAdjacentEqualRunsAndStillRoundTrips()
    {
        // The case §4 used to call corrupt. Holding one direction for 100 000 ticks cannot be
        // written as a single run — repeat is a u16 — so a canonical encoder is *required* to
        // emit two neighbours with identical masks, and the decoder must take them.
        const int Ticks = 100_000;
        var log = new ReplayLog();
        for (int tick = 0; tick < Ticks; tick++)
        {
            log.Record(tick, new InputState(RightMask, 0));
        }

        Assert.Equal(2, log.RunCount);
        Assert.Equal(Ticks, log.TickCount);

        ReplayHeader header = Header();
        byte[] file = log.ToBytes(header);

        // The still pointer splits at the same u16 ceiling the buttons do, so both streams hold
        // two runs — the canonicity rule is per stream (ADR-030 п.5).
        Assert.Equal(FileSize(2, pointerRuns: 2), file.Length);
        Assert.Equal(RightMask, file[MaskOffset(0, 0)]);
        Assert.Equal(RightMask, file[MaskOffset(1, 0)]);
        Assert.Equal(ReplayLog.MaxRepeat, RepeatOf(file, 0));
        Assert.Equal(Ticks - ReplayLog.MaxRepeat, RepeatOf(file, 1));

        for (int i = 0; i < BothReaders.Length; i++)
        {
            ReplayLog reread = Read(file, BothReaders[i], out ReplayHeader rereadHeader);

            Assert.Equal(Ticks, reread.TickCount);
            Assert.Equal(2, reread.RunCount);
            // Both sides of the seam and the seam itself: a decoder that dropped or merged the
            // second run would still answer correctly for tick 0.
            Assert.Equal(RightMask, reread.InputAt(0).Player0);
            Assert.Equal(RightMask, reread.InputAt(ReplayLog.MaxRepeat - 1).Player0);
            Assert.Equal(RightMask, reread.InputAt(ReplayLog.MaxRepeat).Player0);
            Assert.Equal(RightMask, reread.InputAt(Ticks - 1).Player0);
            // §6: reading and writing back is a function, split runs included.
            Assert.Equal(file, reread.ToBytes(rereadHeader));
        }
    }

    // --- the rest of §4, so the whole table is executable and stays that way ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFileShorterThanTheHeaderIsRefused(bool fromStream)
    {
        byte[] valid = ValidFile();
        var stub = new byte[PrologueSize - 1];
        Array.Copy(valid, stub, stub.Length);

        Assert.Throws<ReplayFormatException>(() => { Read(stub, fromStream, out _); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFileThatDoesNotStartWithQrprIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        file[3] = (byte)'X';

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("QRPR", e.Message);
    }

    // Since ADR-041 the prototype reads version 0 and nothing else: the numbers below are the
    // versions this project itself once wrote (1 and 2) and one that does not exist yet (3).
    // All four are refused the same way, because "an old file" and "a file from the future" are
    // the same answer when there is one living version.
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void AVersionThisBuildDoesNotUnderstandIsRefused(bool fromStream, int version)
    {
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(VersionOffset), (ushort)version);

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains($"version {version}", e.Message);
    }

    [Fact]
    public void TrailingBytesAfterTheLastStreamAreRefused()
    {
        byte[] valid = ValidFile();
        var file = new byte[valid.Length + RunSize];   // bytes the stream headers never promised
        valid.CopyTo(file, 0);

        var e = Assert.Throws<ReplayFormatException>(() => { ReplayLog.FromBytes(file, out _); });
        Assert.Contains("trailing", e.Message);

        // The stream reader is different here by design, not by omission: a .qrpr may sit
        // inside a larger container, so bytes past the last stream belong to the container.
        using var stream = new MemoryStream(file, writable: false);
        Assert.Equal(ValidTicks, ReplayLog.ReadFrom(stream, out _).TickCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARepeatCountOfZeroIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(ButtonRunsOffset + 2), 0);

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("run 0", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHeaderPromisingMoreTicksThanTheRunsCoverIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(TickCountOffset), (uint)(ValidTicks + 1));

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains($"{ValidTicks} ticks", e.Message);
        Assert.Contains($"{ValidTicks + 1}", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARunThatOverrunsTheDeclaredTickCountIsRefused(bool fromStream)
    {
        byte[] file = ValidFile();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(TickCountOffset), (uint)(ValidTicks - 2));

        var e = Assert.Throws<ReplayFormatException>(() => { Read(file, fromStream, out _); });
        Assert.Contains("run 1", e.Message);
        Assert.Contains($"{ValidTicks - 2}", e.Message);
    }
}

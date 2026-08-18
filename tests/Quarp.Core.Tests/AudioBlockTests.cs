using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The block's shape and its byte form. Two things are being pinned: that a tick is exactly
/// 800 samples, and that the bytes leaving the console are little-endian because the code says
/// so and not because the test machine happens to be.
/// </summary>
public class AudioBlockTests
{
    [Fact]
    public void ATickIsExactlyEightHundredSamples()
    {
        // 48000 / 60, and the division has to come out whole or ticks would need a fractional
        // sample carried between them.
        Assert.Equal(800, AudioBlock.SamplesPerTick);
        Assert.Equal(0, AudioBlock.SampleRate % AudioBlock.TicksPerSecond);
        Assert.Equal(1600, AudioBlock.ByteLength);
        Assert.Equal(800, new AudioBlock().Samples.Length);
    }

    [Fact]
    public void BytesComeOutLittleEndianWhateverTheHostPrefers()
    {
        var block = new AudioBlock();
        block.Samples[0] = 0x1234;
        block.Samples[1] = -1;
        block.Samples[2] = short.MinValue;
        block.Samples[3] = short.MaxValue;

        byte[] bytes = new byte[AudioBlock.ByteLength];
        block.CopyBytesTo(bytes);

        // Hand-written expectations, not a round-trip through BitConverter: a round-trip
        // through the platform would agree with a big-endian writer on a big-endian host,
        // which is the one case worth testing for.
        Assert.Equal(new byte[] { 0x34, 0x12 }, bytes[0..2]);
        Assert.Equal(new byte[] { 0xFF, 0xFF }, bytes[2..4]);
        Assert.Equal(new byte[] { 0x00, 0x80 }, bytes[4..6]);
        Assert.Equal(new byte[] { 0xFF, 0x7F }, bytes[6..8]);
    }

    [Fact]
    public void ATooSmallDestinationIsRejected()
    {
        var block = new AudioBlock();
        Assert.Throws<ArgumentException>(() => block.CopyBytesTo(new byte[AudioBlock.ByteLength - 1]));
    }

    [Fact]
    public void ClearIsSilence()
    {
        var block = new AudioBlock();
        block.Samples[500] = 1234;
        block.Clear();
        Assert.All(block.Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void TheAudioHashEqualsTheHashOfTheBytesTheShellWouldWrite()
    {
        // The two paths out of a block — hashed for the golden master, written to a device or a
        // .wav — have to see the same bytes, or a mismatch found in CI would not be reproducible
        // by listening to the file that CI compared.
        var block = new AudioBlock();
        for (int i = 0; i < block.Samples.Length; i++)
        {
            block.Samples[i] = unchecked((short)((i * 7919) - 30000));
        }

        byte[] bytes = new byte[AudioBlock.ByteLength];
        block.CopyBytesTo(bytes);

        Assert.Equal(FrameHash.Compute(bytes), FrameHash.Compute(block));
        Assert.Equal(FrameHash.Of(bytes), FrameHash.Of(block));
    }

    [Fact]
    public void OneChangedSampleChangesTheHash()
    {
        // The negative control for every audio-hash assertion elsewhere: if this failed, a hash
        // that "matched" would be proving nothing at all.
        var block = new AudioBlock();
        ulong silence = FrameHash.Compute(block);
        block.Samples[799] = 1;
        Assert.NotEqual(silence, FrameHash.Compute(block));
    }

    [Fact]
    public void SampleOrderMattersToTheHash()
    {
        // A hash folding whole words at a time would give these two the same answer on one
        // architecture and different answers on the other — the M2 lesson, applied to audio.
        var a = new AudioBlock();
        var b = new AudioBlock();
        a.Samples[0] = 0x0102;
        b.Samples[0] = 0x0201;
        Assert.NotEqual(FrameHash.Compute(a), FrameHash.Compute(b));
    }

    [Fact]
    public void TheHashIsSixteenLowercaseHexDigitsLikeAFrameHash()
    {
        string hash = FrameHash.Of(new AudioBlock());
        Assert.Equal(FrameHash.HexLength, hash.Length);
        Assert.Matches("^[0-9a-f]{16}$", hash);
    }

    [Fact]
    public void HashingAFrameStillGivesTheM2Answer()
    {
        // The frame text form is a contract with .github/workflows/ci.yml and with every golden
        // constant in this suite. Extending FrameHash to reach audio must not have moved it.
        Assert.Equal("f3fb6a6deb5af325", FrameHash.Of(new Framebuffer(ConsoleProfile.Profile8)));
    }
}

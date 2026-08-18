using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// What the player hears at each rung of the time ladder, decided by arithmetic instead of by
/// listening.
///
/// <para>The device is modelled, not mocked: <see cref="Device"/> is a queue that loses one
/// block per frame, which is exactly what a 48 kHz sound card does to 800-sample blocks at
/// 60 frames a second. Every claim in <see cref="AudioOutput"/>'s documentation about ×8,
/// ×1/4 and pause is a number this file computes, and every one of them is stated as a ratio
/// with its opposite nearby — "×8 drops seven blocks in eight" is only worth reading next to
/// "×1 drops none".</para>
///
/// <para>Why this is not a test of <c>AudioOutput</c> itself: that type needs a sound card,
/// and on a machine without one every assertion about it passes trivially. A test that goes
/// green because the thing it tests was switched off is the M2 post-mortem's finding, and the
/// answer here is to test the half that has no hardware in it.</para>
/// </summary>
public sealed class AudioQueueTests
{
    /// <summary>A sound card as far as the policy can tell: holds blocks, retires one a frame.</summary>
    private sealed class Device
    {
        public int Pending { get; private set; }

        public int Heard { get; private set; }

        public int Dropped { get; private set; }

        public int Padded { get; private set; }

        public void Frame(int blocksProduced)
        {
            // The device retires the block it finished playing since the last frame, exactly
            // as MonoGame's PendingBufferCount reports it, before anything new is offered.
            if (Pending > 0)
            {
                Pending--;
            }

            for (int i = 0; i < blocksProduced; i++)
            {
                if (AudioQueue.HasRoom(Pending))
                {
                    Pending++;
                    Heard++;
                }
                else
                {
                    Dropped++;
                }
            }

            for (int pad = AudioQueue.PadNeeded(Pending); pad > 0; pad--)
            {
                Pending++;
                Padded++;
            }
        }

        /// <summary>Runs <paramref name="frames"/> frames producing one tick every <paramref name="everyNFrames"/>.</summary>
        public void Slow(int frames, int everyNFrames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                Frame(frame % everyNFrames == 0 ? 1 : 0);
            }
        }

        public void Fast(int frames, int ticksPerFrame)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                Frame(ticksPerFrame);
            }
        }
    }

    private const int Frames = 600;     // ten seconds

    [Fact]
    public void TheQueueNeverExceedsThreeBlocksAndIsNeverEmptied()
    {
        // The bound that makes the latency number in ARCHITECTURE §2 true. Checked on the
        // worst case for each direction at once: eight blocks a frame, then none at all.
        var device = new Device();
        for (int frame = 0; frame < Frames; frame++)
        {
            device.Frame(frame < Frames / 2 ? 8 : 0);
            Assert.InRange(device.Pending, AudioQueue.Target, AudioQueue.Max);
        }
    }

    [Fact]
    public void AtNormalSpeedEveryBlockIsHeardAndNothingIsDropped()
    {
        var device = new Device();
        device.Fast(Frames, ticksPerFrame: 1);
        Assert.Equal(Frames, device.Heard);
        Assert.Equal(0, device.Dropped);
        // One pad, on the very first frame, before the queue has filled — and never again.
        Assert.Equal(AudioQueue.Target - 1, device.Padded);
    }

    [Fact]
    public void AFrameThatRanTwoTicksIsAbsorbedRatherThanClipped()
    {
        // The reason Max is 3 and not 2. The accumulator hands out a second tick whenever a
        // frame ran long, which at ×1 happens constantly; if the queue had no headroom, ×1
        // would drop a block every time the machine hiccuped.
        var device = new Device();
        for (int frame = 0; frame < Frames; frame++)
        {
            device.Frame(frame % 2 == 0 ? 2 : 0);   // two ticks, then none: still 60 a second
        }

        Assert.Equal(Frames, device.Heard);
        Assert.Equal(0, device.Dropped);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void FastForwardIsHeardAsOneBlockInNAndNeverBacksUp(int speed)
    {
        var device = new Device();
        device.Fast(Frames, ticksPerFrame: speed);

        // One block a frame reaches the device however fast the simulation runs, because that
        // is all a 48 kHz device can take. The two extra are the queue filling up on the very
        // first frame, when it is empty and has room for three; from then on it is strictly
        // one in, one out. The rest are dropped, and the count says so exactly.
        const int startupFill = AudioQueue.Max - 1;
        Assert.Equal(Frames + startupFill, device.Heard);
        Assert.Equal((Frames * speed) - Frames - startupFill, device.Dropped);
        Assert.Equal(0, device.Padded);     // fast-forward never has to invent silence
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void SlowMotionIsHeardAsRealBlocksSpacedWithSilence(int divisor)
    {
        var device = new Device();
        device.Slow(Frames, everyNFrames: divisor);

        Assert.Equal(Frames / divisor, device.Heard);
        Assert.Equal(0, device.Dropped);
        // Everything the simulation did not supply, the padding did: the device is fed one
        // block per frame no matter what, which is the difference between "slow motion" and
        // "a source that keeps stopping". The one extra is the first frame, where the queue
        // starts empty and is topped up to Target.
        Assert.Equal(Frames - (Frames / divisor) + AudioQueue.Target - 1, device.Padded);
    }

    [Fact]
    public void PauseIsSilenceAndNotAStalledDevice()
    {
        var device = new Device();
        device.Fast(Frames, ticksPerFrame: 0);
        Assert.Equal(0, device.Heard);
        Assert.Equal(Frames + AudioQueue.Target - 1, device.Padded);
        Assert.Equal(AudioQueue.Target, device.Pending);
    }

    [Fact]
    public void ResumingAfterAPauseStartsFeedingImmediately()
    {
        // The pair to the test above: silence while paused is only correct if sound comes
        // back on the first tick after it. A policy that pinned the queue full would look
        // identical while paused and take three frames to wake up.
        var device = new Device();
        device.Fast(60, ticksPerFrame: 0);
        int paddedWhilePaused = device.Padded;
        device.Fast(60, ticksPerFrame: 1);

        Assert.Equal(60, device.Heard);
        Assert.Equal(paddedWhilePaused, device.Padded);  // not one further pad once ticks resume
        Assert.Equal(0, device.Dropped);
    }

    [Fact]
    public void TheTwoConstantsAreTheWorkOrdersTwoToThreeBlocks()
    {
        Assert.Equal(2, AudioQueue.Target);
        Assert.Equal(3, AudioQueue.Max);
        Assert.Equal(16.667, AudioQueue.BlockMilliseconds, 3);
        Assert.Equal(50.0, AudioQueue.Max * AudioQueue.BlockMilliseconds, 1);
    }
}

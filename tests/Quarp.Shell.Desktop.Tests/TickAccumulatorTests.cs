using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The strict fixed-step accumulator that replaced MonoGame's <c>IsFixedTimeStep</c>
/// (ARCHITECTURE §4). It decides how many ticks the simulation runs, so getting it wrong
/// does not look like a timing bug — it looks like the game running at the wrong speed, or
/// like a replay that does not line up with what the player remembers doing.
///
/// <para>Nothing here touches MonoGame: the accumulator takes elapsed 100 ns units and a
/// speed, and returns an integer.</para>
/// </summary>
public class TickAccumulatorTests
{
    /// <summary>
    /// One 60 Hz frame in 100 ns units, rounded <b>up</b>: a second is 10,000,000 units and
    /// does not divide by 60, so the honest frame length is 166666.66… and any constant is
    /// either slightly short or slightly long. Rounding down would make 60 frames add up to
    /// less than a second and produce 59 ticks — correctly, which is exactly the drift the
    /// accumulator exists to keep out of the simulation.
    /// </summary>
    private const long Frame60Hz = (TimeSpan.TicksPerSecond / 60) + 1;

    private static TimeSpeed Normal => TimeSpeed.At(TimeSpeed.NormalIndex);

    private static TimeSpeed Rung(string label)
    {
        foreach (TimeSpeed speed in TimeSpeed.Ladder)
        {
            if (speed.Label == label)
            {
                return speed;
            }
        }
        throw new ArgumentException($"no ladder rung labelled '{label}'", nameof(label));
    }

    [Fact]
    public void SixtyFramesAtSixtyHertzProduceSixtyTicks()
    {
        var accumulator = new TickAccumulator();

        int total = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            total += accumulator.Advance(Frame60Hz, Normal);
        }

        Assert.Equal(60, total);
        Assert.Equal(0, accumulator.DroppedTicks);
    }

    /// <summary>
    /// The reason the accumulator keeps an exact remainder instead of a truncated period.
    /// A period of <c>TimeSpan.TicksPerSecond / 60</c> is 166666 rather than 166666.66…,
    /// which gains a tick roughly every 25 minutes — small enough to survive review and
    /// large enough to desynchronise a golden master.
    /// </summary>
    [Fact]
    public void TenMinutesOfRealTimeDoesNotDrift()
    {
        var accumulator = new TickAccumulator();

        long total = 0;
        // Feed real time in whole milliseconds so the frame length is not a tick multiple.
        const long OneMillisecond = TimeSpan.TicksPerMillisecond;
        for (int ms = 0; ms < 600_000; ms++)
        {
            total += accumulator.Advance(OneMillisecond, Normal);
        }

        Assert.Equal(600 * 60, total);
        Assert.Equal(0, accumulator.DroppedTicks);
    }

    [Fact]
    public void PartialFramesBankUpInsteadOfBeingLost()
    {
        var accumulator = new TickAccumulator();
        long third = Frame60Hz / 3;

        Assert.Equal(0, accumulator.Advance(third, Normal));
        Assert.Equal(0, accumulator.Advance(third, Normal));
        // Two thirds plus a third is a whole tick — as long as nothing was rounded away.
        Assert.Equal(1, accumulator.Advance(third + 3, Normal));
    }

    [Theory]
    [InlineData("2x", 2)]
    [InlineData("4x", 4)]
    [InlineData("8x", 8)]
    public void FastForwardRunsMoreTicksPerFrame(string label, int expected)
    {
        var accumulator = new TickAccumulator();

        Assert.Equal(expected, accumulator.Advance(Frame60Hz, Rung(label)));
    }

    [Theory]
    [InlineData("0.5x", 2)]
    [InlineData("0.25x", 4)]
    [InlineData("0.125x", 8)]
    public void SlowMotionRunsOneTickEveryNthFrame(string label, int framesPerTick)
    {
        var accumulator = new TickAccumulator();
        TimeSpeed speed = Rung(label);

        for (int frame = 0; frame < framesPerTick - 1; frame++)
        {
            Assert.Equal(0, accumulator.Advance(Frame60Hz, speed));
        }
        Assert.Equal(1, accumulator.Advance(Frame60Hz, speed));
    }

    /// <summary>
    /// The death-spiral guard. A frame that fell far behind gets the frame's own quota plus
    /// five catch-up ticks and no more; the rest is written off, so the game slows down
    /// visibly instead of the window locking up.
    /// </summary>
    [Fact]
    public void ABacklogIsCappedAndWrittenOff()
    {
        var accumulator = new TickAccumulator();

        // A whole second of real time arriving in one frame: 60 ticks are due at x1.
        int ticks = accumulator.Advance(TimeSpan.TicksPerSecond, Normal);

        Assert.Equal(Normal.TicksPerFrame + TickAccumulator.MaxCatchUpTicks, ticks);
        Assert.True(accumulator.DroppedTicks > 0);
        // The remainder went with it: keeping it would put the next frame over budget too.
        Assert.Equal(0, accumulator.Advance(0, Normal));
    }

    /// <summary>The cap scales with the rung, or x8 could never actually reach x8.</summary>
    [Fact]
    public void TheCatchUpAllowanceSitsOnTopOfTheFramesOwnQuota()
    {
        var accumulator = new TickAccumulator();
        TimeSpeed fast = Rung("8x");

        int ticks = accumulator.Advance(TimeSpan.TicksPerSecond, fast);

        Assert.Equal(8 + TickAccumulator.MaxCatchUpTicks, ticks);
    }

    /// <summary>
    /// A dragged window or a debugger breakpoint hands over an arbitrary gap. It must not
    /// bank into a backlog that the cap then discards frame after frame.
    /// </summary>
    [Fact]
    public void AnAbsurdFrameGapIsClampedNotBanked()
    {
        var accumulator = new TickAccumulator();

        accumulator.Advance(TimeSpan.TicksPerSecond * 30, Normal);

        Assert.Equal(0, accumulator.Advance(0, Normal));
        Assert.Equal(1, accumulator.Advance(Frame60Hz, Normal));
    }

    [Fact]
    public void NonPositiveElapsedProducesNothing()
    {
        var accumulator = new TickAccumulator();

        Assert.Equal(0, accumulator.Advance(0, Normal));
        Assert.Equal(0, accumulator.Advance(-5, Normal));
    }

    /// <summary>
    /// Resetting is what the shell does on a speed change: the banked remainder was measured
    /// in the old rung's units, and spending it in the new rung would produce a burst of
    /// ticks on the frame the player pressed the key.
    /// </summary>
    [Fact]
    public void ResetDropsTheBankedRemainder()
    {
        var accumulator = new TickAccumulator();
        accumulator.Advance(Frame60Hz - 10, Normal);   // almost a tick

        accumulator.Reset();

        Assert.Equal(0, accumulator.Advance(10, Normal));
    }
}

/// <summary>The speed ladder itself: the rungs ARCHITECTURE §4 and API-8 §8 promise, in order.</summary>
public class TimeSpeedTests
{
    [Fact]
    public void LadderIsTheDocumentedSevenRungsSlowestFirst()
    {
        string[] labels = new string[TimeSpeed.Ladder.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = TimeSpeed.Ladder[i].Label;
        }

        Assert.Equal(new[] { "0.125x", "0.25x", "0.5x", "1x", "2x", "4x", "8x" }, labels);
        Assert.True(TimeSpeed.At(TimeSpeed.NormalIndex).IsNormal);
    }

    [Fact]
    public void SteppingOffEitherEndOfTheLadderStaysOnIt()
    {
        Assert.Equal(0, TimeSpeed.ClampIndex(-3));
        Assert.Equal(TimeSpeed.Ladder.Length - 1, TimeSpeed.ClampIndex(99));
        Assert.Equal("0.125x", TimeSpeed.At(-1).Label);
        Assert.Equal("8x", TimeSpeed.At(99).Label);
    }

    /// <summary>Slow rungs still ask for one tick a frame, so the catch-up budget is never zero.</summary>
    [Fact]
    public void EveryRungAsksForAtLeastOneTickPerFrame()
    {
        foreach (TimeSpeed speed in TimeSpeed.Ladder)
        {
            Assert.True(speed.TicksPerFrame >= 1, speed.Label);
        }
    }
}

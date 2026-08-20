using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The flyout's press-meaning state machine (M9 stage 2.5 wave 2e). The named negative
/// controls live here: a flyout that opens on a SHORT press turns
/// <see cref="AShortPressIsAClickNotAFlyout"/> red, and one that opens without the full hold
/// turns <see cref="TheHoldOpensOnlyAtTheThreshold"/> red — the same clock discipline as the
/// tooltip tracker, proven without a stopwatch at a window.
/// </summary>
public class ToolbarFlyoutTests
{
    [Fact]
    public void AShortPressIsAClickNotAFlyout()
    {
        var flyout = new ToolbarFlyout();
        flyout.Arm(EditorButton.ToolTransform);

        Assert.False(flyout.Hold(0.2));                     // released well under the threshold
        Assert.Null(flyout.OpenSlot);
        Assert.True(flyout.CompleteClick(out EditorButton clicked));
        Assert.Equal(EditorButton.ToolTransform, clicked);  // the press was a click after all
        Assert.Null(flyout.OpenSlot);
        Assert.Null(flyout.ArmedSlot);
    }

    [Fact]
    public void TheHoldOpensOnlyAtTheThreshold()
    {
        var flyout = new ToolbarFlyout();
        flyout.Arm(EditorButton.ToolShape);

        Assert.False(flyout.Hold(0.4));                     // 0.4 s — not yet
        Assert.Null(flyout.OpenSlot);
        Assert.False(flyout.Hold(0.05));                    // 0.45 s — still not
        Assert.True(flyout.Hold(0.05));                     // 0.5 s — the owner's photoshop moment
        Assert.Equal(EditorButton.ToolShape, flyout.OpenSlot);
        Assert.Null(flyout.ArmedSlot);                      // the press stopped being undecided
        Assert.False(flyout.CompleteClick(out _));          // and can no longer become a click
    }

    [Fact]
    public void RightClickOpensWithNoClock()
    {
        var flyout = new ToolbarFlyout();

        flyout.Open(EditorButton.ToolTransform);

        Assert.Equal(EditorButton.ToolTransform, flyout.OpenSlot);
    }

    [Fact]
    public void CloseClearsEveryState()
    {
        var flyout = new ToolbarFlyout();
        flyout.Open(EditorButton.ToolShape);

        flyout.Close();

        Assert.Null(flyout.OpenSlot);
        Assert.Null(flyout.ArmedSlot);
        Assert.False(flyout.Hold(10));                      // no armed press means no reopening by time
    }

    [Fact]
    public void ArmingAnotherSlotClosesAnOpenFlyout()
    {
        var flyout = new ToolbarFlyout();
        flyout.Open(EditorButton.ToolShape);

        flyout.Arm(EditorButton.ToolTransform);             // the author moved on

        Assert.Null(flyout.OpenSlot);
        Assert.Equal(EditorButton.ToolTransform, flyout.ArmedSlot);
    }

    /// <summary>Arming zeroes the clock — a leftover hold from a previous press must not shorten this one.</summary>
    [Fact]
    public void EachPressGetsAFreshClock()
    {
        var flyout = new ToolbarFlyout();
        flyout.Arm(EditorButton.ToolShape);
        flyout.Hold(0.4);
        flyout.CompleteClick(out _);                        // released as a click at 0.4 s

        flyout.Arm(EditorButton.ToolShape);
        Assert.False(flyout.Hold(0.4));                     // the new press starts from zero
        Assert.Null(flyout.OpenSlot);
    }
}

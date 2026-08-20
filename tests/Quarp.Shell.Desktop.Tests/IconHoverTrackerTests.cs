using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The tooltip clock (M9 stage 2.5). The wave's named negative-control target lives here:
/// a tooltip that appears <b>instantly</b> instead of after the owner's three seconds turns
/// <see cref="AFreshHoverShowsNoTooltipYet"/> red — while the hover highlight itself
/// (<see cref="IconHoverTracker.Target"/>) must be live from the very first frame.
/// </summary>
public class IconHoverTrackerTests
{
    private static readonly HoverTarget Pencil = HoverTarget.OfButton(EditorButton.ToolPencil);
    private static readonly HoverTarget Fill = HoverTarget.OfButton(EditorButton.ToolFill);

    [Fact]
    public void AFreshHoverShowsNoTooltipYet()
    {
        var tracker = new IconHoverTracker();

        // Even a pathologically long first frame must not flash the label: the delay is
        // measured from arrival, so the arrival frame banks nothing.
        tracker.Update(Pencil, 10.0);

        Assert.Equal(Pencil, tracker.Target);       // the frame highlight IS immediate
        Assert.False(tracker.TooltipVisible);       // the label is not
    }

    [Fact]
    public void TheTooltipAppearsAfterThreeSteadySeconds()
    {
        var tracker = new IconHoverTracker();
        tracker.Update(Pencil, 0.0);

        for (int frame = 0; frame < 29; frame++)
        {
            tracker.Update(Pencil, 0.1);            // 2.9 s of steady hover
        }
        Assert.False(tracker.TooltipVisible);

        tracker.Update(Pencil, 0.2);                // 3.1 s — past the owner's number
        Assert.True(tracker.TooltipVisible);
    }

    [Fact]
    public void MovingToAnotherIconRestartsTheClock()
    {
        var tracker = new IconHoverTracker();
        tracker.Update(Pencil, 0.0);
        tracker.Update(Pencil, IconHoverTracker.TooltipDelaySeconds);
        Assert.True(tracker.TooltipVisible);

        tracker.Update(Fill, 0.1);                  // a pointer crossing the toolbar

        Assert.Equal(Fill, tracker.Target);
        Assert.False(tracker.TooltipVisible);       // no label strobing from icon to icon
    }

    [Fact]
    public void LeavingAllIconsHidesEverything()
    {
        var tracker = new IconHoverTracker();
        tracker.Update(Pencil, 0.0);
        tracker.Update(Pencil, IconHoverTracker.TooltipDelaySeconds);

        tracker.Update(null, 0.1);

        Assert.Null(tracker.Target);
        Assert.False(tracker.TooltipVisible);
    }

    /// <summary>A swatch and a button are different targets even if hovered in succession — the clock restarts.</summary>
    [Fact]
    public void SwatchesAreTargetsOfTheirOwn()
    {
        var tracker = new IconHoverTracker();
        tracker.Update(HoverTarget.OfSwatch(5), 0.0);
        tracker.Update(HoverTarget.OfSwatch(5), IconHoverTracker.TooltipDelaySeconds);
        Assert.True(tracker.TooltipVisible);

        tracker.Update(HoverTarget.OfSwatch(6), 0.1);
        Assert.False(tracker.TooltipVisible);
    }
}

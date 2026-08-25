using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The crash of 2026-08-25, and the two locks that answer it.
///
/// <para><b>What happened.</b> The console was driven by hand — sprite screen, Alt+Right to the
/// tilemap, Alt+Right to the sound screen, and Alt+Right again to the music screen — with the
/// pointer resting on the sound screen's OCTAVE stepper the whole time. The window died on the
/// last press with <c>System.ArgumentOutOfRangeException: MusicRegion.None is not a control and
/// has no label</c>, thrown out of <c>MusicEditorRenderer.TooltipText</c> inside <c>Draw</c>.
/// It was found by looking at the screen, not by a test, which is why the two tests below
/// exist: a defect that only a human eye can see is a defect that comes back.</para>
///
/// <para><b>Why it happened.</b> A frame is input-then-draw. The sound screen's reader wrote
/// <c>HoverTarget.OfSfxRegion(Octave)</c> into the shared tracker; the SAME frame's Alt+Right
/// moved the shell to the music screen; the music screen's <c>Draw</c> then asked the tracker
/// what was under the pointer and got a target with no button and no music region — a target
/// measured against a layout that was no longer on screen. One frame was the whole window of
/// exposure, and one frame was enough.</para>
///
/// <para><b>The two locks.</b> <see cref="IconHoverTracker.Clear"/> is the rule — a pointer
/// target belongs to the screen it was measured on and does not outlive it — and
/// <c>QuarpGame.Update</c> calls it the moment the mode moves under the pointer. The renderers
/// are the second lock: a target naming none of their controls is "no label" instead of an
/// exception, because a throw inside <c>Draw</c> reaches nothing that can recover and takes the
/// author's unsaved work with it.</para>
/// </summary>
public class StaleHoverAcrossScreensTests
{
    /// <summary>
    /// The exact shape that killed the window: no button, and a region belonging to another
    /// screen. Both renderers must answer "no label" and neither may throw.
    ///
    /// <para>Break recipe: give <c>MusicEditorRenderer.TooltipText</c> its old body back
    /// (<c>: EditorIcons.MusicRegionTooltip(target.Music)</c> with no None arm) and this goes
    /// red with the very exception that crashed the console. Same for the sound screen's twin.
    /// </para>
    /// </summary>
    [Fact]
    public void AHoverTargetFromAnotherScreenAsksForNoLabelInsteadOfKillingTheFrame()
    {
        // The sound screen's own target, seen by the MUSIC screen — Button null, Music None.
        HoverTarget foreignToMusic = HoverTarget.OfSfxRegion(SfxRegion.Octave);
        Assert.Null(foreignToMusic.Button);
        Assert.Equal(MusicRegion.None, foreignToMusic.Music);
        Assert.Null(MusicEditorRenderer.TooltipText(foreignToMusic));

        // And the mirror: the music screen's own target, seen by the SOUND screen.
        HoverTarget foreignToSfx = HoverTarget.OfMusicRegion(MusicRegion.Song);
        Assert.Null(foreignToSfx.Button);
        Assert.Equal(SfxRegion.None, foreignToSfx.Sfx);
        Assert.Null(SfxEditorRenderer.TooltipText(foreignToSfx));

        // The negative control: a target each screen DOES own still gets its label, so the
        // nulls above are the None arm speaking and not a method that answers null always.
        Assert.NotNull(MusicEditorRenderer.TooltipText(HoverTarget.OfMusicRegion(MusicRegion.Song)));
        Assert.NotNull(SfxEditorRenderer.TooltipText(HoverTarget.OfSfxRegion(SfxRegion.Octave)));
        Assert.NotNull(MusicEditorRenderer.TooltipText(HoverTarget.OfButton(EditorButton.Save)));
        Assert.NotNull(SfxEditorRenderer.TooltipText(HoverTarget.OfButton(EditorButton.Save)));

        // The map screen joined the three-screen rotation when its buttonless controls got
        // labels of their own (MapRegion), and it was built with this rule rather than repaired
        // into it: a target from either neighbour is "no label" here too, and the map's own
        // target is "no label" over there.
        Assert.Null(MapEditorRenderer.TooltipText(foreignToMusic));
        Assert.Null(MapEditorRenderer.TooltipText(HoverTarget.OfMusicRegion(MusicRegion.Song)));
        Assert.NotNull(MapEditorRenderer.TooltipText(HoverTarget.OfMapRegion(MapRegion.Canvas)));
        Assert.Null(SfxEditorRenderer.TooltipText(HoverTarget.OfMapRegion(MapRegion.Canvas)));
        Assert.Null(MusicEditorRenderer.TooltipText(HoverTarget.OfMapRegion(MapRegion.Canvas)));
    }

    /// <summary>
    /// The first lock, on its own: forgetting takes the clock with it. A tracker that dropped
    /// the target but kept the seconds would show the next screen's first target as an
    /// already-ripe tooltip — a label with no hover behind it.
    ///
    /// <para>Break recipe: delete the <c>_hoverSeconds = 0</c> line from
    /// <see cref="IconHoverTracker.Clear"/> and the last assertion goes red.</para>
    /// </summary>
    [Fact]
    public void ClearingTheTrackerForgetsTheTargetAndTheClockTogether()
    {
        var tracker = new IconHoverTracker();
        HoverTarget target = HoverTarget.OfSfxRegion(SfxRegion.Octave);

        tracker.Update(target, 0.0);
        for (int frame = 0; frame < 40; frame++)
        {
            tracker.Update(target, 0.1);            // four seconds: the label is ripe
        }
        Assert.True(tracker.TooltipVisible);

        tracker.Clear();
        Assert.Null(tracker.Target);
        Assert.False(tracker.TooltipVisible);

        // One frame on the new screen must NOT be enough to show a label.
        tracker.Update(HoverTarget.OfMusicRegion(MusicRegion.Song), 0.1);
        Assert.False(tracker.TooltipVisible);
    }
}

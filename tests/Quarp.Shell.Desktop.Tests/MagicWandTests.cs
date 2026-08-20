using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The magic wand — the select group's third variant (wave 2g, owner's third review: "владелец
/// имел в виду её с самого начала"). The wand marks the 4-connected area of ONE color around
/// the click, walled by the region, through the very flood the bucket repaints with — one
/// owner of the connectivity, so the wave's named negative control (в) is a single cut:
/// remove the color guard (or the wall) in the shared walk and
/// <see cref="TheWandSelectsOnlyTheConnectedAreaOfOneColor"/> goes red (alongside the fill
/// tests, which is the point of sharing). Everything downstream — move, Delete, stamp —
/// treats the wand's mask like any other, proven here against real sessions.
/// </summary>
public class MagicWandTests : IDisposable
{
    private readonly string _root;

    public MagicWandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-wand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SpriteEditorSession Session()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var session = new SpriteEditorSession(folder);
        session.SelectSelectionVariant(SelectionVariant.Wand);
        return session;
    }

    private static byte PixelAt(SpriteEditorSession session, int sheetX, int sheetY) =>
        session.Pixels[sheetY * CartData.GfxWidth + sheetX];

    private static void PaintPixel(SpriteEditorSession session, int color, int x, int y)
    {
        session.SelectColor(color);
        session.BeginStroke();
        session.Paint(x, y);
        session.EndStroke();
    }

    private static int SelectedCount(SpriteEditorSession session)
    {
        int count = 0;
        for (int y = 0; y < session.RegionPixels; y++)
        {
            for (int x = 0; x < session.RegionPixels; x++)
            {
                if (session.IsSelected(x, y))
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>One complete wand gesture: press, release.</summary>
    private static void Wand(SpriteEditorSession session, int x, int y)
    {
        session.BeginSelect(x, y);
        session.CommitSelect();
    }

    // ---- what the wand takes: one color, 4-connected, walled ----

    /// <summary>
    /// The named negative control (в): an L of color 7 plus a DISCONNECTED pixel of the same
    /// color. The wand must take exactly the L — flowing across the color border (onto the
    /// zeros) or across the gap (onto the far 7) reddens this.
    /// </summary>
    [Fact]
    public void TheWandSelectsOnlyTheConnectedAreaOfOneColor()
    {
        var session = Session();
        PaintPixel(session, 7, 1, 1);
        PaintPixel(session, 7, 1, 2);
        PaintPixel(session, 7, 2, 2);   // the L, 4-connected
        PaintPixel(session, 7, 5, 5);   // same color, no path to it

        Wand(session, 1, 1);

        Assert.Equal(3, SelectedCount(session));
        Assert.True(session.IsSelected(1, 1));
        Assert.True(session.IsSelected(1, 2));
        Assert.True(session.IsSelected(2, 2));
        Assert.False(session.IsSelected(5, 5));     // connectivity, not color equality
        Assert.False(session.IsSelected(0, 0));     // the zeros around the L are another color
    }

    /// <summary>Diagonal neighbours are NOT connected — the same 4-connectivity the bucket has always had, now provably shared.</summary>
    [Fact]
    public void TheWandDoesNotLeakDiagonally()
    {
        var session = Session();
        PaintPixel(session, 7, 2, 2);
        PaintPixel(session, 7, 3, 3);   // touches only at the corner

        Wand(session, 2, 2);

        Assert.Equal(1, SelectedCount(session));
        Assert.False(session.IsSelected(3, 3));
    }

    /// <summary>The region border is the wall: on an all-zero region the wand takes the whole region — all 64 pixels of it and not one more, no wrap, no throw.</summary>
    [Fact]
    public void TheWandStopsAtTheRegionBorder()
    {
        var session = Session();

        Wand(session, 0, 0);

        Assert.Equal(session.RegionPixels * session.RegionPixels, SelectedCount(session));
    }

    /// <summary>Marking with the wand is not an edit: no sheet change, no dirt, no undo step, no re-upload asked of the renderer.</summary>
    [Fact]
    public void TheWandMarksNothingIntoTheSheet()
    {
        var session = Session();
        PaintPixel(session, 9, 3, 3);
        int version = session.Version;

        Wand(session, 3, 3);

        Assert.True(session.HasSelection);
        Assert.Equal(version, session.Version);
        Assert.Equal(9, PixelAt(session, 3, 3));
        Assert.True(session.CanUndo);           // exactly the paint stroke, nothing from the wand
        session.Undo();
        Assert.False(session.CanUndo);
    }

    // ---- the gesture ----

    /// <summary>"Повторный клик в другом месте — новое выделение": the second press lands outside the mask, so the old area dies at that press.</summary>
    [Fact]
    public void ASecondWandClickReplacesTheSelection()
    {
        var session = Session();
        PaintPixel(session, 7, 1, 1);
        PaintPixel(session, 9, 5, 5);
        Wand(session, 1, 1);
        Assert.True(session.IsSelected(1, 1));

        Wand(session, 5, 5);

        Assert.False(session.IsSelected(1, 1));
        Assert.True(session.IsSelected(5, 5));
        Assert.Equal(1, SelectedCount(session));
    }

    /// <summary>A wand drag re-picks live, like the box re-marks its corner: the release commits the area under the cursor, never the stale press.</summary>
    [Fact]
    public void DraggingTheWandRepicksAtTheCursor()
    {
        var session = Session();
        PaintPixel(session, 7, 1, 1);

        session.BeginSelect(1, 1);                  // the lone 7
        Assert.Equal(1, SelectedCount(session));
        session.UpdateSelect(4, 4);                 // slid onto the zeros
        session.CommitSelect();

        Assert.False(session.IsSelected(1, 1));
        Assert.Equal(63, SelectedCount(session));   // the whole zero area: 64 minus the 7
    }

    // ---- the mask is a mask: move, Delete, stamp ----

    /// <summary>A press inside a wand selection grabs and moves it, exactly like any mask — the drop is one undo step with zeros beneath.</summary>
    [Fact]
    public void AWandSelectionMovesLikeAnyMask()
    {
        var session = Session();
        PaintPixel(session, 7, 1, 1);
        PaintPixel(session, 7, 2, 1);
        Wand(session, 1, 1);

        session.BeginSelect(1, 1);                  // over the mask: the grab
        Assert.True(session.MoveActive);
        session.UpdateSelect(1, 4);                 // three down
        session.CommitSelect();

        Assert.Equal(7, PixelAt(session, 1, 4));
        Assert.Equal(7, PixelAt(session, 2, 4));
        Assert.Equal(0, PixelAt(session, 1, 1));
        session.Undo();                             // ONE step rolls the whole move back
        Assert.Equal(7, PixelAt(session, 1, 1));
        Assert.Equal(0, PixelAt(session, 1, 4));
    }

    /// <summary>Delete under a wand selection clears exactly the picked area; the committed selection also loads the stamp, like every commit does.</summary>
    [Fact]
    public void DeleteAndStampTreatTheWandMaskLikeAnyOther()
    {
        var session = Session();
        PaintPixel(session, 7, 1, 1);
        PaintPixel(session, 7, 2, 1);   // the connected bar the wand picks
        PaintPixel(session, 9, 5, 5);   // a bystander in another color
        Wand(session, 1, 1);
        Assert.True(session.HasStampSource);

        session.ClearRegion();                      // Del's session verb, selection alive

        Assert.Equal(0, PixelAt(session, 1, 1));    // the picked bar died
        Assert.Equal(0, PixelAt(session, 2, 1));
        Assert.Equal(9, PixelAt(session, 5, 5));    // the bystander stands

        session.SelectTool(SpriteEditorTool.Stamp);
        session.StampAt(4, 4);                      // the captured 2x1 bar, centered: origin x=3
        Assert.Equal(7, PixelAt(session, 3, 4));
        Assert.Equal(7, PixelAt(session, 4, 4));
    }
}

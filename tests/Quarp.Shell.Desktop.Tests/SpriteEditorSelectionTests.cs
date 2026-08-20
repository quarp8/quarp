using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Wave 2f's select tool, proven headless through <see cref="SpriteEditorSession"/> — a file
/// of its own so the pinned 2b-2e contracts stay in untouched files. The wave's named
/// negative-control targets live here: a move that loses pixels at the region border turns
/// <see cref="AMoveClampsAtTheRegionBorderAndLosesNothing"/> red, a mask leaking into the
/// sheet (and from there into a saved PNG) turns <see cref="TheMaskNeverEntersTheSheet"/> and
/// <see cref="SelectingWritesNothingToTheSavedPng"/> red, and a Delete that wipes the whole
/// region despite a selection turns <see cref="DeleteWithASelectionClearsOnlyIt"/> red.
/// </summary>
public class SpriteEditorSelectionTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorSelectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-sel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CartFolder()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private SpriteEditorSession Session() => new(CartFolder());

    private static byte PixelAt(SpriteEditorSession session, int sheetX, int sheetY) =>
        session.Pixels[sheetY * CartData.GfxWidth + sheetX];

    private static int ColoredCount(SpriteEditorSession session, byte color)
    {
        int count = 0;
        foreach (byte pixel in session.Pixels)
        {
            if (pixel == color)
            {
                count++;
            }
        }
        return count;
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

    /// <summary>One complete pencil gesture — a single pixel, so tests can place exact content.</summary>
    private static void PaintPixel(SpriteEditorSession session, int x, int y)
    {
        session.BeginStroke();
        session.Paint(x, y);
        session.EndStroke();
    }

    /// <summary>A complete rectangle-select gesture over the inclusive box.</summary>
    private static void SelectBox(SpriteEditorSession session, int x0, int y0, int x1, int y1)
    {
        session.SelectSelectionVariant(SelectionVariant.Rectangle);
        session.BeginSelect(x0, y0);
        session.UpdateSelect(x1, y1);
        session.CommitSelect();
    }

    // ---- marking: the mask lives beside the sheet, never in it ----

    /// <summary>The named negative control: a mask pixel reaching the sheet is the bug class this pins out.</summary>
    [Fact]
    public void TheMaskNeverEntersTheSheet()
    {
        var session = Session();
        session.SelectTool(SpriteEditorTool.Select);
        session.SelectSelectionVariant(SelectionVariant.Brush);
        // A visible ink, so a leak that writes CurrentColor cannot hide as 0-over-0 — the
        // negative control's first run caught exactly that blind spot.
        session.SelectColor(9);
        int version = session.Version;

        session.BeginSelect(1, 1);
        session.UpdateSelect(4, 4);
        session.CommitSelect();

        Assert.True(session.HasSelection);
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);  // the sheet holds none of it
        Assert.Equal(version, session.Version);                     // the renderer is not asked to re-upload
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);                              // marking is not an edit
    }

    /// <summary>
    /// The file-level half of the same control: after selecting, a save leaves the PNG holding
    /// exactly the painted content — a leaked mask would surface here as extra ink.
    /// </summary>
    [Fact]
    public void SelectingWritesNothingToTheSavedPng()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);
        session.SelectColor(7);
        PaintPixel(session, 0, 0);
        Assert.True(session.Save());

        session.SelectTool(SpriteEditorTool.Select);
        session.SelectSelectionVariant(SelectionVariant.Brush);
        session.BeginSelect(2, 2);
        session.UpdateSelect(5, 5);
        session.CommitSelect();

        Assert.False(session.IsDirty);              // selecting dirtied nothing
        Assert.True(session.Save());                // and this save is the contract's no-op
        byte[] disk = PngDecoder.DecodeToPaletteIndices(
            File.ReadAllBytes(Path.Combine(folder, "gfx.png")), CartData.GfxWidth, CartData.GfxHeight, "gfx.png");
        Assert.Equal(7, disk[0]);                   // the painted pixel
        Assert.True(disk.AsSpan(1).IndexOfAnyExcept((byte)0) < 0);  // and nothing else, mask included
    }

    [Fact]
    public void TheBrushMarksTheSameLineThePencilWouldDraw()
    {
        var session = Session();
        session.SelectSelectionVariant(SelectionVariant.Brush);
        session.BeginSelect(0, 0);
        session.UpdateSelect(3, 3);
        session.CommitSelect();

        Assert.Equal(4, SelectedCount(session));    // the exact Bresenham diagonal
        for (int i = 0; i < 4; i++)
        {
            Assert.True(session.IsSelected(i, i));
        }
    }

    /// <summary>The rectangle mask is remade every drag frame, like the shape preview — shrinking the drag shrinks the box.</summary>
    [Fact]
    public void TheBoxFollowsTheDragInsteadOfAccumulating()
    {
        var session = Session();
        session.SelectSelectionVariant(SelectionVariant.Rectangle);
        session.BeginSelect(2, 2);
        session.UpdateSelect(5, 4);
        session.UpdateSelect(3, 3);
        session.CommitSelect();

        Assert.Equal(4, SelectedCount(session));    // the final 2x2 box only
        Assert.True(session.IsSelected(2, 2));
        Assert.True(session.IsSelected(3, 3));
        Assert.False(session.IsSelected(5, 4));
    }

    /// <summary>"Клик новым выделением снимает старое" — and at the press, not the release.</summary>
    [Fact]
    public void ANewSelectionDropsTheOldAtThePress()
    {
        var session = Session();
        SelectBox(session, 0, 0, 1, 1);

        session.BeginSelect(5, 5);                  // outside the mask: a fresh marking gesture

        Assert.False(session.IsSelected(0, 0));     // the old mask died with the press
        Assert.True(session.IsSelected(5, 5));
        session.CommitSelect();
        Assert.Equal(1, SelectedCount(session));
    }

    /// <summary>Esc's verb: the mask drops, the sheet stands, and the stamp keeps its memory of the last selection.</summary>
    [Fact]
    public void ClearSelectionDropsTheMaskButKeepsTheStampSource()
    {
        var session = Session();
        session.SelectColor(7);
        PaintPixel(session, 3, 3);
        SelectBox(session, 3, 3, 3, 3);

        session.ClearSelection();

        Assert.False(session.HasSelection);
        Assert.True(session.HasStampSource);
        Assert.Equal(7, PixelAt(session, 3, 3));    // nothing about the sheet changed
    }

    // ---- moving: grab, float, drop ----

    /// <summary>The wave's core contract: the whole grab-drag-drop lands as exactly ONE undo step, holes filled with 0.</summary>
    [Fact]
    public void AMoveIsOneUndoStepAndLeavesZeroBehind()
    {
        var session = Session();
        session.SelectColor(7);
        PaintPixel(session, 1, 1);
        session.SelectColor(9);
        PaintPixel(session, 2, 1);
        SelectBox(session, 1, 1, 2, 1);

        session.BeginSelect(1, 1);                  // over the mask: the grab
        Assert.True(session.MoveActive);
        session.UpdateSelect(1, 4);                 // three down
        session.CommitSelect();

        Assert.Equal(7, PixelAt(session, 1, 4));    // the pixels rode together, colors intact
        Assert.Equal(9, PixelAt(session, 2, 4));
        Assert.Equal(0, PixelAt(session, 1, 1));    // color 0 under where they were
        Assert.Equal(0, PixelAt(session, 2, 1));
        Assert.True(session.IsSelected(1, 4));      // the selection followed its pixels
        Assert.False(session.IsSelected(1, 1));

        session.Undo();                             // ONE Ctrl+Z rolls the whole move back
        Assert.Equal(7, PixelAt(session, 1, 1));
        Assert.Equal(9, PixelAt(session, 2, 1));
        Assert.Equal(0, PixelAt(session, 1, 4));
        session.Undo();                             // the two paint strokes remain, nothing between
        session.Undo();
        Assert.False(session.CanUndo);
    }

    /// <summary>
    /// The named negative control: a drag past the border parks the fragment against it —
    /// with the clamp gone, the outer pixel would fall off the region and vanish.
    /// </summary>
    [Fact]
    public void AMoveClampsAtTheRegionBorderAndLosesNothing()
    {
        var session = Session();
        session.SelectColor(6);
        PaintPixel(session, 5, 3);
        PaintPixel(session, 6, 3);
        SelectBox(session, 5, 3, 6, 3);

        session.BeginSelect(5, 3);
        session.UpdateSelect(7, 3);                 // asks for +2; the box's edge allows +1
        session.CommitSelect();

        Assert.Equal(2, ColoredCount(session, 6));  // both pixels alive
        Assert.Equal(6, PixelAt(session, 6, 3));
        Assert.Equal(6, PixelAt(session, 7, 3));    // parked against the border
        Assert.Equal(0, PixelAt(session, 5, 3));
    }

    /// <summary>A grab that goes nowhere never happened — no undo step, no dirt beyond what was already there.</summary>
    [Fact]
    public void AGrabThatGoesNowhereIsInvisible()
    {
        var session = Session();
        session.SelectColor(7);
        PaintPixel(session, 2, 2);
        SelectBox(session, 2, 2, 2, 2);
        int version = session.Version;

        session.BeginSelect(2, 2);
        session.CommitSelect();                     // dropped where it was picked up

        Assert.Equal(version, session.Version);
        session.Undo();                             // one step back is the PAINT, not the grab
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.CanUndo);
    }

    /// <summary>Undo mid-float cancels the move (its pixels never left the sheet) and undoes the last REAL step.</summary>
    [Fact]
    public void UndoMidMoveCancelsTheFloatThenUndoesThePixels()
    {
        var session = Session();
        session.SelectColor(7);
        PaintPixel(session, 2, 2);
        SelectBox(session, 2, 2, 2, 2);
        session.BeginSelect(2, 2);
        session.UpdateSelect(5, 5);                 // floating at +3,+3

        session.Undo();

        Assert.False(session.MoveActive);
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0); // the paint was undone, the move never was
        Assert.True(session.IsSelected(2, 2));      // the committed mask survives, at its source
        Assert.False(session.IsSelected(5, 5));
    }

    /// <summary>An interrupt mid-marking discards the half-made mask — it never was the selection.</summary>
    [Fact]
    public void AnInterruptDiscardsAnOpenMarkingGesture()
    {
        var session = Session();
        session.BeginSelect(1, 1);
        session.UpdateSelect(4, 4);

        session.CycleRegionSize();                  // Tab mid-gesture

        Assert.False(session.SelectionGestureActive);
        Assert.False(session.HasSelection);
    }

    // ---- Delete under a selection ----

    /// <summary>The named negative control: Delete with a selection must clear ONLY it — a whole-region wipe reddens this.</summary>
    [Fact]
    public void DeleteWithASelectionClearsOnlyIt()
    {
        var session = Session();
        session.SelectColor(5);
        PaintPixel(session, 1, 1);
        PaintPixel(session, 6, 6);
        SelectBox(session, 0, 0, 2, 2);

        session.ClearRegion();                      // the Del key's session verb

        Assert.Equal(0, PixelAt(session, 1, 1));    // the selected pixel died
        Assert.Equal(5, PixelAt(session, 6, 6));    // the unselected one stands
        session.Undo();                             // and it was ONE step
        Assert.Equal(5, PixelAt(session, 1, 1));
    }

    // ---- the region owns the mask ----

    /// <summary>The mask is region-local: moving or resizing the region drops it rather than re-aiming it at foreign pixels.</summary>
    [Fact]
    public void RegionChangesDropTheSelection()
    {
        var session = Session();
        SelectBox(session, 1, 1, 2, 2);
        session.SelectRegionCell(0, 0);             // the anchor did not move —
        Assert.True(session.HasSelection);          // — so the mask stands

        session.SelectRegionCell(3, 3);
        Assert.False(session.HasSelection);

        SelectBox(session, 1, 1, 2, 2);
        session.CycleRegionSize();
        Assert.False(session.HasSelection);
    }

    [Fact]
    public void SelectCoordinatesOutsideTheRegionThrow()
    {
        var session = Session();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginSelect(8, 0));
        Assert.Throws<InvalidOperationException>(() => session.UpdateSelect(0, 0));
        session.BeginSelect(0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.UpdateSelect(0, -1));
    }

    // ---- the wave's direct question, at the session level ----

    /// <summary>
    /// The work order's scenario verbatim: brush-select 7 pixels, move them 3 right, print two
    /// stamps, then Ctrl+Z three times. Each undo peels exactly one of the three snapshots the
    /// sequence made (stamp 2, stamp 1, the move) — selecting itself made none — and the
    /// fourth proves the setup stroke was the only other step in history.
    /// </summary>
    [Fact]
    public void TheDirectQuestionScenario()
    {
        var session = Session();
        session.SelectColor(7);
        session.BeginStroke();
        session.Paint(2, 0);
        session.Paint(2, 6);                        // one stroke: the 7-pixel column x=2, y=0..6
        session.EndStroke();

        session.SelectTool(SpriteEditorTool.Select);
        session.SelectSelectionVariant(SelectionVariant.Brush);
        session.BeginSelect(2, 0);                  // the brush marks the same 7 pixels
        session.UpdateSelect(2, 6);
        session.CommitSelect();
        Assert.Equal(7, SelectedCount(session));

        session.BeginSelect(2, 3);                  // grab inside, carry 3 right, drop
        session.UpdateSelect(5, 3);
        session.CommitSelect();

        session.SelectTool(SpriteEditorTool.Stamp);
        session.StampAt(0, 3);                      // source is 1x7 — centering puts it at x=0, y=0..6
        session.StampAt(7, 3);
        Assert.Equal(21, ColoredCount(session, 7)); // three columns: x=0, x=5, x=7

        session.Undo();                             // 1: the second stamp vanishes
        Assert.Equal(14, ColoredCount(session, 7));
        Assert.Equal(0, PixelAt(session, 7, 3));
        Assert.Equal(7, PixelAt(session, 0, 3));

        session.Undo();                             // 2: the first stamp vanishes
        Assert.Equal(7, ColoredCount(session, 7));
        Assert.Equal(0, PixelAt(session, 0, 3));
        Assert.Equal(7, PixelAt(session, 5, 3));    // only the moved column remains

        session.Undo();                             // 3: the move rolls back whole
        Assert.Equal(7, ColoredCount(session, 7));
        Assert.Equal(7, PixelAt(session, 2, 3));    // the column is home
        Assert.Equal(0, PixelAt(session, 5, 3));

        Assert.True(session.CanUndo);               // exactly the setup stroke is left…
        session.Undo();
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.CanUndo);              // …and nothing else ever existed
    }
}

using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Wave 2e's shape tool, proven headless through <see cref="SpriteEditorSession"/> — in a file
/// of its own so the pinned 2b/2c/2d contracts stay in untouched files. Three of the wave's
/// named negative-control targets live here: a preview that writes into the sheet turns
/// <see cref="ThePreviewNeverEntersTheSheet"/> red, a shape escaping the region turns
/// <see cref="ShapesAreWalledByTheRegionBorder"/> red (it reads sentinels one pixel outside),
/// and a fill that ignores the Ctrl modifier turns
/// <see cref="AnOutlineOvalLeavesItsInteriorEmpty"/> red (it demands an EMPTY interior).
/// </summary>
public class SpriteEditorShapeTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorShapeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-sh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SpriteEditorSession Session()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return new SpriteEditorSession(folder);
    }

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

    // ---- the preview lives outside the sheet ----

    /// <summary>The named negative control: a preview pixel reaching the sheet before the release is the bug class this pins out.</summary>
    [Fact]
    public void ThePreviewNeverEntersTheSheet()
    {
        var session = Session();
        session.SelectTool(SpriteEditorTool.Shape);
        session.SelectColor(7);
        int version = session.Version;

        session.BeginShape(0, 0);
        session.UpdateShape(4, 2, filled: true);            // a real filled oval, previewed

        Assert.True(session.ShapeActive);
        Assert.Equal(11, session.ShapePreview.Count);       // the preview exists...
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);  // ...and the sheet has none of it
        Assert.Equal(version, session.Version);             // the renderer is not asked to re-upload
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void BeginShapeStartsAsASinglePointPreview()
    {
        var session = Session();
        session.BeginShape(3, 5);

        Assert.Equal(new[] { (3, 5) }, session.ShapePreview);
    }

    // ---- committed geometry ----

    /// <summary>The Ctrl contract (PICO-8's pattern): NO Ctrl means outline — a filled commit here is the named negative control.</summary>
    [Fact]
    public void AnOutlineOvalLeavesItsInteriorEmpty()
    {
        var session = Session();
        session.SelectColor(7);
        session.BeginShape(0, 0);
        session.UpdateShape(4, 2, filled: false);           // a 5x3 oval outline

        session.CommitShape();

        Assert.False(session.ShapeActive);
        Assert.Equal(8, ColoredCount(session, 7));          // ring only: 2 + 3 + 3 pixels
        Assert.Equal(0, PixelAt(session, 2, 1));            // the interior stayed empty
        Assert.Equal(7, PixelAt(session, 0, 1));            // left and right extremes
        Assert.Equal(7, PixelAt(session, 4, 1));
        Assert.Equal(7, PixelAt(session, 2, 0));            // top and bottom arcs
        Assert.Equal(7, PixelAt(session, 2, 2));
        Assert.Equal(0, PixelAt(session, 0, 0));            // the box corners are outside the oval
        Assert.Equal(0, PixelAt(session, 4, 2));
    }

    [Fact]
    public void CtrlCommitsAFilledOval()
    {
        var session = Session();
        session.SelectColor(9);
        session.BeginShape(0, 0);
        session.UpdateShape(4, 2, filled: true);

        session.CommitShape();

        Assert.Equal(11, ColoredCount(session, 9));         // ring + the 3-pixel interior row
        Assert.Equal(9, PixelAt(session, 2, 1));            // the interior is paint now
    }

    [Fact]
    public void ARectangleCommitsAsItsPerimeter()
    {
        var session = Session();
        session.SelectColor(5);
        session.SelectShape(ShapeVariant.Rectangle);
        session.BeginShape(1, 1);
        session.UpdateShape(4, 4, filled: false);           // a 4x4 box

        session.CommitShape();

        Assert.Equal(12, ColoredCount(session, 5));         // 4x4 perimeter
        Assert.Equal(5, PixelAt(session, 1, 1));
        Assert.Equal(5, PixelAt(session, 4, 4));
        Assert.Equal(0, PixelAt(session, 2, 2));            // hollow inside
    }

    [Fact]
    public void AFilledRectangleIsTheWholeBox()
    {
        var session = Session();
        session.SelectColor(5);
        session.SelectShape(ShapeVariant.Rectangle);
        session.BeginShape(1, 1);
        session.UpdateShape(4, 4, filled: true);

        session.CommitShape();

        Assert.Equal(16, ColoredCount(session, 5));
        Assert.Equal(5, PixelAt(session, 2, 2));
    }

    /// <summary>The order's words: an empty 1x1 gesture is an honest point — in both variants.</summary>
    [Theory]
    [InlineData(ShapeVariant.Oval)]
    [InlineData(ShapeVariant.Rectangle)]
    public void A1x1GestureIsAnHonestPoint(ShapeVariant variant)
    {
        var session = Session();
        session.SelectColor(3);
        session.SelectShape(variant);
        session.BeginShape(4, 4);

        session.CommitShape();

        Assert.Equal(1, ColoredCount(session, 3));
        Assert.Equal(3, PixelAt(session, 4, 4));
        Assert.True(session.CanUndo);                       // a real point is a real step
    }

    /// <summary>A degenerate 1-wide box is a line, not a crash — the oval inclusion test covers it with no special case.</summary>
    [Fact]
    public void ADegenerateOvalIsALine()
    {
        var session = Session();
        session.SelectColor(2);
        session.BeginShape(2, 0);
        session.UpdateShape(2, 7, filled: false);

        session.CommitShape();

        Assert.Equal(8, ColoredCount(session, 2));
        for (int y = 0; y < 8; y++)
        {
            Assert.Equal(2, PixelAt(session, 2, y));
        }
    }

    // ---- undo discipline ----

    /// <summary>The wave's direct question, at the session level: a committed shape is ONE undo step and undo erases it whole.</summary>
    [Fact]
    public void TheFilledOvalScenarioCommitsOnceAndUndoesWhole()
    {
        var session = Session();
        session.SelectTool(SpriteEditorTool.Shape);
        session.SelectColor(7);
        session.BeginShape(0, 0);
        for (int step = 1; step <= 4; step++)
        {
            session.UpdateShape(step, Math.Min(step, 2), filled: false);    // arrows, one at a time
        }
        session.UpdateShape(4, 2, filled: true);            // Ctrl arrives: the preview flips to filled

        session.CommitShape();
        Assert.Equal(11, ColoredCount(session, 7));
        Assert.True(session.IsDirty);

        session.Undo();                                     // Ctrl+Z
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.CanUndo);                      // exactly one step existed
        Assert.False(session.IsDirty);
    }

    /// <summary>A shape whose every pixel already has the current color never happened — no undo step, no dirt, no version.</summary>
    [Fact]
    public void ACommitThatChangesNothingIsInvisible()
    {
        var session = Session();
        session.SelectColor(3);
        session.Fill(0, 0);                                 // the whole region is 3 now: one real step
        int version = session.Version;

        session.SelectShape(ShapeVariant.Rectangle);
        session.BeginShape(1, 1);
        session.UpdateShape(5, 5, filled: true);
        session.CommitShape();                              // paints 3 over 3

        Assert.Equal(version, session.Version);
        session.Undo();                                     // one step back is the FILL, not the shape
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.CanUndo);
    }

    // ---- region clamping ----

    /// <summary>
    /// The named negative control: the whole region painted edge to edge must stop AT the
    /// region — sentinels one pixel outside on all four sides catch any tear.
    /// </summary>
    [Fact]
    public void ShapesAreWalledByTheRegionBorder()
    {
        var session = Session();
        session.SelectRegionCell(5, 5);                     // sheet x/y 40-47
        session.SelectColor(6);
        session.SelectShape(ShapeVariant.Rectangle);
        session.BeginShape(0, 0);
        session.UpdateShape(7, 7, filled: true);

        session.CommitShape();

        Assert.Equal(64, ColoredCount(session, 6));         // exactly the 8x8 region
        Assert.Equal(6, PixelAt(session, 40, 40));
        Assert.Equal(6, PixelAt(session, 47, 47));
        Assert.Equal(0, PixelAt(session, 39, 43));          // one pixel out, each side
        Assert.Equal(0, PixelAt(session, 48, 43));
        Assert.Equal(0, PixelAt(session, 43, 39));
        Assert.Equal(0, PixelAt(session, 43, 48));
    }

    [Fact]
    public void ShapeCoordinatesOutsideTheRegionThrow()
    {
        var session = Session();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginShape(8, 0));
        session.BeginShape(0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.UpdateShape(0, -1, filled: false));
    }

    // ---- interruptions ----

    /// <summary>Anything that cuts across the gesture discards the preview — nothing was in the sheet, so nothing may land.</summary>
    [Fact]
    public void AnInterruptDiscardsThePreviewWithoutTouchingTheSheet()
    {
        var session = Session();
        session.SelectColor(7);
        session.BeginShape(0, 0);
        session.UpdateShape(5, 5, filled: true);
        int version = session.Version;

        session.CycleRegionSize();                          // Tab mid-gesture

        Assert.False(session.ShapeActive);
        Assert.Empty(session.ShapePreview);
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.CanUndo);
        Assert.Equal(version, session.Version);
    }

    /// <summary>Undo mid-preview eats the preview first, then undoes the last REAL step — a half-shape must never survive.</summary>
    [Fact]
    public void UndoMidPreviewDiscardsThePreviewThenUndoesThePixels()
    {
        var session = Session();
        session.SelectColor(7);
        session.BeginStroke();
        session.Paint(0, 0);
        session.EndStroke();                                // one real step
        session.BeginShape(2, 2);
        session.UpdateShape(5, 5, filled: false);

        session.Undo();

        Assert.False(session.ShapeActive);
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0); // the stroke was undone, the shape never existed
    }

    /// <summary>Picking the other variant mid-drag redraws the same box in the new shape — the gesture is the author's, the look is the variant's.</summary>
    [Fact]
    public void SelectShapeRedrawsAnOpenPreview()
    {
        var session = Session();
        session.BeginShape(0, 0);
        session.UpdateShape(4, 2, filled: false);
        Assert.Equal(8, session.ShapePreview.Count);        // the oval ring

        session.SelectShape(ShapeVariant.Rectangle);

        Assert.Equal(12, session.ShapePreview.Count);       // the same 5x3 box as a perimeter
        Assert.Contains((0, 0), session.ShapePreview);      // corners exist now
    }
}

using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Wave 2c of the sprite editor: the growable region (8/16/32 px), the four region edits
/// (flip H/V, rotate 90° CW, clear) and the bucket fill — proven headless through
/// <see cref="SpriteEditorSession"/>, in a file of its own so wave 2b's contracts stay pinned
/// by an untouched <see cref="SpriteEditorSessionTests"/>.
///
/// <para>Three tests here are the wave's named negative-control targets: a rotation with the
/// region deliberately allowed to clip at the sheet's edge must corrupt neighbours or throw
/// (<see cref="ARotationAtTheSheetCornerTouchesOnlyTheRegion"/>), a fill whose wall moves past
/// the region border must leak (<see cref="FillIsWalledByTheRegionBorder"/>,
/// <see cref="FillFloodsTheWholeEmptyRegionFromOneClick"/> — which counts colored pixels
/// sheet-wide, not just inside), and a transform that changes nothing must stay invisible to
/// undo and dirt (<see cref="ATransformThatChangesNothingIsInvisibleToUndoAndDirt"/>).</para>
/// </summary>
public class SpriteEditorRegionToolTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorRegionToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder, optionally with a gfx.png encoded from the given sheet — same scaffolding as the 2b tests.</summary>
    private string CartFolder(byte[]? sheet = null)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (sheet is not null)
        {
            File.WriteAllBytes(
                Path.Combine(folder, "gfx.png"),
                PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight));
        }
        return folder;
    }

    private static byte PixelAt(SpriteEditorSession session, int sheetX, int sheetY) =>
        session.Pixels[sheetY * CartData.GfxWidth + sheetX];

    /// <summary>One complete pencil gesture: press, samples, release.</summary>
    private static void Stroke(SpriteEditorSession session, params (int X, int Y)[] points)
    {
        session.BeginStroke();
        foreach ((int x, int y) in points)
        {
            session.Paint(x, y);
        }
        session.EndStroke();
    }

    // ---- region size ----

    [Fact]
    public void TheRegionCycles8_16_32AndBackTo8()
    {
        var session = new SpriteEditorSession(CartFolder());
        Assert.Equal(8, session.RegionPixels);      // wave 2b's pinned start

        session.CycleRegionSize();
        Assert.Equal((2, 16), (session.RegionCells, session.RegionPixels));

        session.CycleRegionSize();
        Assert.Equal((4, 32), (session.RegionCells, session.RegionPixels));

        session.CycleRegionSize();
        Assert.Equal((1, 8), (session.RegionCells, session.RegionPixels));
    }

    /// <summary>
    /// The invariant every transform relies on: growing the region at the sheet's edge pulls
    /// the anchor back so the square never hangs off the sheet.
    /// </summary>
    [Fact]
    public void GrowingTheRegionAtTheSheetEdgeReclampsTheAnchor()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectRegionCell(15, 15);           // the bottom-right corner cell

        session.CycleRegionSize();                  // 16x16 no longer fits at (15,15)
        Assert.Equal((14, 14), (session.RegionCellX, session.RegionCellY));

        session.CycleRegionSize();                  // 32x32 fits at (12,12) at most
        Assert.Equal((12, 12), (session.RegionCellX, session.RegionCellY));
        Assert.True(session.RegionCellX + session.RegionCells <= SpriteEditorSession.GridCells);
        Assert.True(session.RegionCellY + session.RegionCells <= SpriteEditorSession.GridCells);
    }

    [Fact]
    public void RegionSelectionClampsAgainstTheCurrentSize()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.CycleRegionSize();
        session.CycleRegionSize();                  // 4 cells a side

        session.SelectRegionCell(15, 15);

        Assert.Equal((12, 12), (session.RegionCellX, session.RegionCellY));
        Assert.Equal(12 * SpriteEditorSession.GridCells + 12, session.SpriteIndex);
    }

    [Fact]
    public void PaintReachesEveryCellOfAGrownRegion()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectRegionCell(1, 1);
        session.CycleRegionSize();                  // 2x2 cells anchored at (1,1)
        session.SelectColor(6);

        Stroke(session, (0, 0), (15, 15));          // a diagonal through all four cells

        Assert.Equal(6, PixelAt(session, 8, 8));    // cell (1,1) — region-local origin
        Assert.Equal(6, PixelAt(session, 23, 23));  // cell (2,2) — region-local far corner
    }

    [Fact]
    public void PaintOutsideTheGrownRegionStillThrows()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.CycleRegionSize();                  // 16 px a side: 0-15 are legal
        session.BeginStroke();

        session.Paint(15, 15);                      // the new far corner is in range now
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Paint(16, 0));
    }

    [Fact]
    public void CyclingTheRegionMidStrokeCommitsTheGesture()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        session.BeginStroke();
        session.Paint(0, 0);

        session.CycleRegionSize();                  // Tab arrives while the button is held

        Assert.False(session.StrokeActive);         // the gesture closed as its own undo step,
        Assert.True(session.CanUndo);               // so no line can be joined across the size change
        Assert.Equal(2, session.RegionCells);
    }

    // ---- flips, rotation, clear ----

    [Fact]
    public void FlipHorizontalMirrorsLeftToRight()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        Stroke(session, (0, 3));

        session.FlipHorizontal();

        Assert.Equal(7, PixelAt(session, 7, 3));
        Assert.Equal(0, PixelAt(session, 0, 3));
    }

    [Fact]
    public void FlipVerticalMirrorsTopToBottom()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        Stroke(session, (3, 0));

        session.FlipVertical();

        Assert.Equal(7, PixelAt(session, 3, 7));
        Assert.Equal(0, PixelAt(session, 3, 0));
    }

    [Fact]
    public void RotateClockwiseSendsTheTopRowToTheRightColumn()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        Stroke(session, (2, 0));                    // top row, third pixel

        session.RotateClockwise();

        Assert.Equal(7, PixelAt(session, 7, 2));    // right column, third pixel — clockwise
        Assert.Equal(0, PixelAt(session, 2, 0));
    }

    /// <summary>
    /// Dirt is content, not history: four quarter-turns are four undo steps, but the sheet is
    /// back to what the disk holds, so the session is honestly clean again.
    /// </summary>
    [Fact]
    public void FourRotationsComeBackAroundAndTheDirtFlagIsHonest()
    {
        var sheet = new byte[CartData.GfxWidth * CartData.GfxHeight];
        sheet[0] = 7;                               // one asymmetric pixel, loaded from disk
        var session = new SpriteEditorSession(CartFolder(sheet));

        for (int i = 0; i < 4; i++)
        {
            session.RotateClockwise();
        }

        Assert.True(session.Pixels.SequenceEqual(sheet));
        Assert.False(session.IsDirty);
        Assert.True(session.CanUndo);               // the four steps really happened
    }

    /// <summary>
    /// The wave's direct question: a 16x16 region "selected at the corner" cannot clip,
    /// because the size cycle re-clamps the anchor — so the rotation only ever permutes the
    /// clamped square and the neighbouring sprites' pixels are unreachable by construction.
    /// Sentinels sit one pixel outside the clamped region on both sides to catch any leak.
    /// </summary>
    [Fact]
    public void ARotationAtTheSheetCornerTouchesOnlyTheRegion()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(5);
        session.SelectRegionCell(13, 14);
        Stroke(session, (7, 0));                    // sheet (111, 112): just left of the future region
        session.SelectRegionCell(14, 13);
        Stroke(session, (0, 7));                    // sheet (112, 111): just above it

        session.SelectRegionCell(15, 15);
        session.CycleRegionSize();                  // 16x16 at the corner → anchor clamps to (14,14)
        session.SelectColor(9);
        Stroke(session, (1, 0));                    // sheet (113, 112)

        session.RotateClockwise();

        Assert.Equal(9, PixelAt(session, 127, 113));    // local (1,0) → (15,1): inside the clamped square
        Assert.Equal(0, PixelAt(session, 113, 112));
        Assert.Equal(5, PixelAt(session, 111, 112));    // the neighbours never moved
        Assert.Equal(5, PixelAt(session, 112, 111));
    }

    [Fact]
    public void ClearRegionZeroesTheRegionAndOnlyTheRegion()
    {
        // Pattern value depends on x alone ((y*128+x) % 16 == x % 16), so cell (3,3) —
        // sheet x 24-31 — holds the nonzero values 8-15 everywhere.
        var sheet = new byte[CartData.GfxWidth * CartData.GfxHeight];
        for (int i = 0; i < sheet.Length; i++)
        {
            sheet[i] = (byte)(i % Palette.VisibleCount);
        }
        var session = new SpriteEditorSession(CartFolder(sheet));
        session.SelectRegionCell(3, 3);

        session.ClearRegion();

        Assert.Equal(0, PixelAt(session, 24, 24));
        Assert.Equal(0, PixelAt(session, 31, 31));
        Assert.Equal(7, PixelAt(session, 23, 24));      // left neighbour: 23 % 16
        Assert.Equal(1, PixelAt(session, 33, 24));      // right neighbour: 33 % 16
        Assert.Equal(8, PixelAt(session, 24, 23));      // top neighbour: 24 % 16

        session.Undo();                                 // and it was exactly one step
        Assert.True(session.Pixels.SequenceEqual(sheet));
        Assert.False(session.CanUndo);
    }

    /// <summary>The negative-control target for "a no-op edit dirties the session": it must not.</summary>
    [Fact]
    public void ATransformThatChangesNothingIsInvisibleToUndoAndDirt()
    {
        var session = new SpriteEditorSession(CartFolder());    // an all-zero sheet is symmetric under everything
        int version = session.Version;

        session.FlipHorizontal();
        session.FlipVertical();
        session.RotateClockwise();
        session.ClearRegion();                                  // clearing zeros to zero

        Assert.False(session.CanUndo);          // Ctrl+Z must not appear alive with nothing to undo
        Assert.False(session.IsDirty);
        Assert.Equal(version, session.Version); // and the renderer is not asked to re-upload
    }

    [Fact]
    public void ATransformIsExactlyOneUndoStep()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        Stroke(session, (0, 0), (2, 0));        // three pixels, one gesture

        session.FlipHorizontal();

        session.Undo();                         // one step back: the flip
        Assert.Equal(7, PixelAt(session, 0, 0));
        Assert.Equal(0, PixelAt(session, 7, 0));

        session.Undo();                         // second step: the stroke
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ATransformMidStrokeCommitsTheGestureFirst()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        session.BeginStroke();
        session.Paint(0, 0);

        session.FlipHorizontal();               // F arrives while the button is held

        Assert.False(session.StrokeActive);
        Assert.Equal(7, PixelAt(session, 7, 0));

        session.Undo();                         // the flip, whole
        Assert.Equal(7, PixelAt(session, 0, 0));
        session.Undo();                         // the gesture, whole
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact]
    public void ATransformClearsTheRedoFuture()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(2);
        Stroke(session, (0, 0));
        Stroke(session, (1, 1));
        session.Undo();
        Assert.True(session.CanRedo);

        session.FlipHorizontal();               // history branched: (0,0) → (7,0) is a real change

        Assert.False(session.CanRedo);
    }

    // ---- bucket fill ----

    /// <summary>Counts sheet-wide, not region-wide: a fill that leaks past the region inflates the count.</summary>
    [Fact]
    public void FillFloodsTheWholeEmptyRegionFromOneClick()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectRegionCell(5, 5);
        session.SelectColor(3);

        session.Fill(3, 3);

        int colored = 0;
        foreach (byte pixel in session.Pixels)
        {
            if (pixel == 3)
            {
                colored++;
            }
        }
        Assert.Equal(64, colored);              // exactly the 8x8 region, nowhere else
        Assert.True(session.IsDirty);

        session.Undo();                         // and the flood was one undo step
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void FillStopsAtOtherColors()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(9);
        Stroke(session, (4, 0), (4, 7));        // a vertical wall through the region

        session.SelectColor(5);
        session.Fill(0, 0);

        Assert.Equal(5, PixelAt(session, 0, 7));
        Assert.Equal(5, PixelAt(session, 3, 4));
        Assert.Equal(9, PixelAt(session, 4, 3));    // the wall survives
        Assert.Equal(0, PixelAt(session, 5, 5));    // the far side is unreached
    }

    /// <summary>
    /// The negative-control target for "fill escapes the region": the whole sheet is one
    /// connected color-0 area, so only the region border stops the flood.
    /// </summary>
    [Fact]
    public void FillIsWalledByTheRegionBorder()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectRegionCell(5, 5);         // sheet x/y 40-47
        session.SelectColor(3);

        session.Fill(0, 0);

        Assert.Equal(3, PixelAt(session, 40, 40));
        Assert.Equal(3, PixelAt(session, 47, 47));
        Assert.Equal(0, PixelAt(session, 39, 43));  // one pixel out, each side
        Assert.Equal(0, PixelAt(session, 48, 43));
        Assert.Equal(0, PixelAt(session, 43, 39));
        Assert.Equal(0, PixelAt(session, 43, 48));
    }

    [Fact]
    public void FillRespectsTheGrownRegion()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectRegionCell(2, 2);
        session.CycleRegionSize();              // 16x16: sheet x/y 16-31
        session.SelectColor(4);

        session.Fill(8, 8);

        int colored = 0;
        foreach (byte pixel in session.Pixels)
        {
            if (pixel == 4)
            {
                colored++;
            }
        }
        Assert.Equal(256, colored);
        Assert.Equal(4, PixelAt(session, 16, 16));
        Assert.Equal(0, PixelAt(session, 32, 20));  // just right of the region
    }

    [Fact]
    public void FillingAColorWithItselfIsInvisibleToUndoAndDirt()
    {
        var session = new SpriteEditorSession(CartFolder());    // region is all 0
        int version = session.Version;
        Assert.Equal(0, session.CurrentColor);

        session.Fill(2, 2);

        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
        Assert.Equal(version, session.Version);
    }

    /// <summary>4-connectivity: two touching corners are not a passage.</summary>
    [Fact]
    public void FillDoesNotLeakDiagonally()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(9);
        Stroke(session, (1, 0), (0, 1));        // Bresenham paints exactly these two — a diagonal gate

        session.SelectColor(5);
        session.Fill(0, 0);

        Assert.Equal(5, PixelAt(session, 0, 0));    // the sealed corner
        Assert.Equal(0, PixelAt(session, 1, 1));    // diagonally adjacent, not 4-connected
        Assert.Equal(0, PixelAt(session, 7, 7));
    }

    [Fact]
    public void FillOutsideTheRegionThrows()
    {
        var session = new SpriteEditorSession(CartFolder());

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Fill(session.RegionPixels, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Fill(0, -1));
    }

    // ---- tool state ----

    [Fact]
    public void TheToolTogglesBetweenPencilAndFill()
    {
        var session = new SpriteEditorSession(CartFolder());
        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);    // the pencil is the opening tool

        session.ToggleTool();
        Assert.Equal(SpriteEditorTool.Fill, session.Tool);

        session.ToggleTool();
        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);
    }

    /// <summary>The eyedropper is tool-agnostic (work order: RMB picks in both tools) — the session must not gate it.</summary>
    [Fact]
    public void TheEyedropperPicksWhileTheFillToolIsActive()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(11);
        Stroke(session, (3, 3));
        session.ToggleTool();

        session.PickColor(3, 3);

        Assert.Equal(11, session.CurrentColor);
        Assert.Equal(SpriteEditorTool.Fill, session.Tool);      // picking must not switch tools
    }
}

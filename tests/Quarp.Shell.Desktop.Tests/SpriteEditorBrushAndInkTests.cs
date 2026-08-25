using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The three gaps REFERENCES-EDITORS §8 named against the sprite editor, proven headless where
/// each of them actually lives: the brush ladder (item 12), the two inks and the eyedropper's
/// choice of which one to fill (item 7), and the bucket's replace-everywhere half (item 6).
/// All three are <see cref="SpriteEditorSession"/>'s facts — the router only says which button
/// was pressed — so all three are provable without a window, in the shape
/// <see cref="SpriteEditorSessionTests"/> established: a claim per test, and a break recipe on
/// every claim that says what to damage in production code to see it go red.
///
/// <para>The router's own half of the same three rules — which physical button reaches which
/// verb — lives in <see cref="SpriteEditorSecondButtonRouterTests"/>, next door, because a
/// dispatch fact and a policy fact fail for different reasons and should not share a file.</para>
/// </summary>
public class SpriteEditorBrushAndInkTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorBrushAndInkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-brush-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with an optional gfx.png — the shape <see cref="SpriteEditorSessionTests"/> uses.</summary>
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

    /// <summary>A sheet painted one flat colour — so anything a verb touches shows up as a difference.</summary>
    private static byte[] FlatSheet(byte color)
    {
        var sheet = new byte[CartData.GfxWidth * CartData.GfxHeight];
        Array.Fill(sheet, color);
        return sheet;
    }

    private static byte PixelAt(SpriteEditorSession session, int sheetX, int sheetY) =>
        session.Pixels[sheetY * CartData.GfxWidth + sheetX];

    /// <summary>Region-local pixel → its absolute sheet pixel, so a test can watch the NEIGHBOURING sprite too.</summary>
    private static byte RegionPixelAt(SpriteEditorSession session, int localX, int localY) =>
        PixelAt(
            session,
            session.RegionCellX * VirtualConsole.SpriteSize + localX,
            session.RegionCellY * VirtualConsole.SpriteSize + localY);

    /// <summary>One complete gesture: press, samples, release — the shell's own three calls.</summary>
    private static void Stroke(
        SpriteEditorSession session, SpriteEditorInk ink, params (int X, int Y)[] points)
    {
        session.BeginStroke(ink);
        foreach ((int x, int y) in points)
        {
            session.Paint(x, y);
        }
        session.EndStroke();
    }

    // ==================================================================================
    // §8 item 12 — the brush ladder.
    // ==================================================================================

    /// <summary>
    /// The ladder is TIC-80's: four steps (<c>BRUSH_SIZES</c>), opening on the single pixel, and
    /// <c>-</c>/<c>=</c> walk it <b>cyclically</b> the way <c>updateBrushSize</c> does — so
    /// neither end of the ladder is a dead key.
    ///
    /// <para>Break recipe: clamp instead of wrapping in
    /// <see cref="SpriteEditorSession.CycleBrushSize"/> (drop the double modulo for a
    /// <c>Math.Clamp</c>) and the two wrap assertions go red while the plain walk stays green —
    /// which is exactly the half a clamp would silently take away. Drop one entry from the
    /// ladder and the count assertion names it before any screen does.</para>
    /// </summary>
    [Fact]
    public void TheBrushLadderIsFourStepsWideAndItsKeysWrapAtBothEnds()
    {
        var session = new SpriteEditorSession(CartFolder());

        Assert.Equal(4, SpriteEditorSession.BrushSizeCount);
        Assert.Equal(new[] { 1, 2, 3, 4 }, SpriteEditorSession.BrushSizes.ToArray());
        Assert.Equal(1, session.BrushSize);         // the pencil opens on one pixel, as it always did

        session.CycleBrushSize(1);
        Assert.Equal(2, session.BrushSize);
        session.CycleBrushSize(1);
        session.CycleBrushSize(1);
        Assert.Equal(4, session.BrushSize);
        session.CycleBrushSize(1);
        Assert.Equal(1, session.BrushSize);         // the wrap forward
        session.CycleBrushSize(-1);
        Assert.Equal(4, session.BrushSize);         // and back, which C#'s % would get wrong alone
    }

    /// <summary>
    /// <see cref="SpriteEditorSession.SelectBrushSize"/> is the ladder's one door and it slams:
    /// a side that is not on the ladder is a caller bug, and clamping it would hand the author a
    /// brush they never picked. A rejected value must not half-apply.
    ///
    /// <para>Break recipe: replace the throw with a <c>Math.Clamp</c> — every
    /// <c>Assert.Throws</c> here goes red at once, and the flyout could then ship a fifth entry
    /// that quietly paints as the fourth.</para>
    /// </summary>
    [Fact]
    public void ABrushSideOffTheLadderIsRefusedOutright()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectBrushSize(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectBrushSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectBrushSize(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectBrushSize(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteEditorSession.BrushSizeAt(4));
        Assert.Equal(3, session.BrushSize);
    }

    /// <summary>
    /// One dab of a wide brush is a square <b>centred</b> under the cursor, TIC-80's
    /// <c>paintPoint</c> offset (<c>x + i - brushSize / 2</c>): at side 3 the cursor sits in the
    /// middle and the square reaches one pixel each way.
    ///
    /// <para>Break recipe: drop the <c>- origin</c> from <see cref="SpriteEditorSession"/>'s
    /// <c>Dab</c> and the square lands down and right of the cursor — the (3,3) and (4,3)
    /// assertions go red while (4,4) and (5,5) stay green, which is precisely the shape of the
    /// bug an author would report as "the brush is off by one".</para>
    /// </summary>
    [Fact]
    public void AWideBrushLaysASquareCentredOnTheCursor()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        session.SelectBrushSize(3);

        Stroke(session, SpriteEditorInk.Primary, (4, 4));

        for (int y = 3; y <= 5; y++)
        {
            for (int x = 3; x <= 5; x++)
            {
                Assert.Equal(7, RegionPixelAt(session, x, y));
            }
        }
        // And exactly that square: one ring out is untouched on all four sides.
        Assert.Equal(0, RegionPixelAt(session, 2, 4));
        Assert.Equal(0, RegionPixelAt(session, 6, 4));
        Assert.Equal(0, RegionPixelAt(session, 4, 2));
        Assert.Equal(0, RegionPixelAt(session, 4, 6));
    }

    /// <summary>
    /// A wide brush at the canvas edge lays the part of itself that is on the canvas and drops
    /// the rest — it must not wrap, and above all it must not reach the NEIGHBOURING sprite,
    /// which shares the sheet row with this one and is one <c>SheetOffset</c> away. The region
    /// here is deliberately not the sheet's corner, so there is a neighbour on every side to
    /// leak into.
    ///
    /// <para>Break recipe: delete either bounds test from <c>Dab</c> and this goes red twice
    /// over — the left neighbour picks up ink at its right edge (a negative <c>x</c> walks
    /// backwards into it through <c>SheetOffset</c>) and the sprite above picks it up from a
    /// negative <c>y</c>. Nothing else in the suite watches that seam, because before the brush
    /// no verb could address a pixel outside the region at all.</para>
    /// </summary>
    [Fact]
    public void AWideBrushIsClippedByTheCanvasAndNeverReachesTheNeighbourSprite()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectRegionCell(1, 1);         // sprite 17 — neighbours on every side
        session.SelectColor(9);
        session.SelectBrushSize(4);             // origin 2: the square starts at local -2

        Stroke(session, SpriteEditorInk.Primary, (0, 0));

        // What survived the clip: the quarter of the square that is inside the region.
        Assert.Equal(9, RegionPixelAt(session, 0, 0));
        Assert.Equal(9, RegionPixelAt(session, 1, 0));
        Assert.Equal(9, RegionPixelAt(session, 0, 1));
        Assert.Equal(9, RegionPixelAt(session, 1, 1));
        Assert.Equal(0, RegionPixelAt(session, 2, 0));      // and no further: origin 2, side 4
        Assert.Equal(0, RegionPixelAt(session, 0, 2));

        // What must NOT have happened: the neighbours are still blank. Sheet coordinates, so
        // the assertion is about the file, not about the region's own arithmetic.
        int size = VirtualConsole.SpriteSize;
        for (int i = 0; i < size; i++)
        {
            Assert.Equal(0, PixelAt(session, size - 1, size + i));   // the sprite to the left
            Assert.Equal(0, PixelAt(session, size + i, size - 1));   // the sprite above
        }
    }

    /// <summary>
    /// A whole brush stroke — many dabs, each many pixels — is <b>one</b> undo step, exactly as
    /// a one-pixel stroke always was. The brush changed how much a stroke paints and nothing
    /// about what a stroke IS.
    ///
    /// <para>Break recipe: make <c>Dab</c> push its own snapshot (or call
    /// <c>BeginStroke</c>/<c>EndStroke</c> around itself) and the single undo below stops
    /// restoring the sheet — the second assertion, not the first, is the one that catches it,
    /// which is why the sheet is compared whole rather than at one pixel.</para>
    /// </summary>
    [Fact]
    public void AWideBrushStrokeIsStillExactlyOneUndoStep()
    {
        var session = new SpriteEditorSession(CartFolder());
        byte[] before = session.Pixels.ToArray();
        session.SelectColor(5);
        session.SelectBrushSize(4);

        Stroke(session, SpriteEditorInk.Primary, (2, 2), (5, 2), (5, 5), (2, 5));

        Assert.True(session.CanUndo);
        Assert.True(session.IsDirty);
        session.Undo();
        Assert.True(session.Pixels.SequenceEqual(before));
        Assert.False(session.CanUndo);          // one step, not four and not sixty-four
        Assert.False(session.IsDirty);
    }

    /// <summary>
    /// The ladder is the pencil's alone. TIC-80 keeps <c>brushSize</c> inside <c>paintPoint</c>,
    /// which only <c>SPRITE_DRAW_MODE</c> reaches, so the bucket, the shapes and the selection
    /// have no stroke width to be — and all three are checked here at brush 4, where a leak
    /// would be sixteen times too big to miss.
    ///
    /// <para>Break recipe: point <c>CommitShape</c>'s loop at <c>Dab</c> instead of
    /// <c>Plot</c> and the shape assertion goes red; route the selection brush's
    /// <c>MarkSelected</c> through a square and the mask assertion goes red; widen
    /// <c>VisitConnectedColor</c>'s visit and the bucket assertion goes red. Each names its own
    /// tool, which is the point of testing the three together rather than trusting one.</para>
    /// </summary>
    [Fact]
    public void TheBrushLeavesTheBucketTheShapesAndTheSelectionAlone()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectBrushSize(4);

        // The shape: a one-point gesture is one pixel, whatever the brush is.
        session.SelectTool(SpriteEditorTool.Shape);
        session.SelectColor(7);
        session.BeginShape(4, 4);
        session.UpdateShape(4, 4, filled: false);
        session.CommitShape();
        Assert.Equal(7, RegionPixelAt(session, 4, 4));
        Assert.Equal(0, RegionPixelAt(session, 3, 3));

        // The selection brush: one marked pixel, not a marked square.
        session.SelectTool(SpriteEditorTool.Select);
        session.SelectSelectionVariant(SelectionVariant.Brush);
        session.BeginSelect(1, 1);
        session.CommitSelect();
        Assert.True(session.IsSelected(1, 1));
        Assert.False(session.IsSelected(0, 0));
        session.ClearSelection();

        // The bucket: a one-pixel island stays a one-pixel island. Pixel (4,4) is the shape's
        // colour 7 in a field of 0, so its 4-connected area is exactly itself.
        session.SelectTool(SpriteEditorTool.Fill);
        session.SelectColor(3);
        session.Fill(4, 4);
        Assert.Equal(3, RegionPixelAt(session, 4, 4));
        Assert.Equal(0, RegionPixelAt(session, 3, 4));
        Assert.Equal(0, RegionPixelAt(session, 5, 4));
    }

    // ==================================================================================
    // §8 item 7 — two inks and the eyedropper.
    // ==================================================================================

    /// <summary>
    /// Two inks, remembered apart: TIC-80's <c>color</c> and <c>color2</c>. Each swatch door
    /// writes one of them and leaves the other exactly where it was — which is the whole value
    /// of the pair, since an author keeps an outline under one button and a fill under the other.
    ///
    /// <para>Break recipe: drop the <c>ink</c> branch in
    /// <see cref="SpriteEditorSession.SelectColor"/> so both writes land on
    /// <see cref="SpriteEditorSession.CurrentColor"/> — the second ink's assertions go red and,
    /// worse in production, the right button would silently start painting the left colour.</para>
    /// </summary>
    [Fact]
    public void TheTwoInksAreRememberedApartAndNeitherDoorTouchesTheOther()
    {
        var session = new SpriteEditorSession(CartFolder());

        session.SelectColor(7);
        session.SelectColor(3, SpriteEditorInk.Secondary);

        Assert.Equal(7, session.CurrentColor);
        Assert.Equal(3, session.SecondaryColor);
        Assert.Equal(7, session.InkColor(SpriteEditorInk.Primary));
        Assert.Equal(3, session.InkColor(SpriteEditorInk.Secondary));

        session.SelectColor(12);
        Assert.Equal(3, session.SecondaryColor);            // the left door left the right ink alone
        session.SelectColor(1, SpriteEditorInk.Secondary);
        Assert.Equal(12, session.CurrentColor);             // and the right door the left one
    }

    /// <summary>
    /// The 0-15 invariant has two doors now and both slam. A second ink would be a fine place to
    /// smuggle index 16 into the sheet, since it reaches <c>Plot</c> by the same road.
    ///
    /// <para>Break recipe: move the range check inside the primary branch of
    /// <see cref="SpriteEditorSession.SelectColor"/> — the first three assertions stay green and
    /// the secondary ones go red, which is the exact asymmetry a hand-written second door would
    /// have introduced.</para>
    /// </summary>
    [Fact]
    public void BothInksRefuseAnIndexOutsideTheVisiblePalette()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(15);
        session.SelectColor(15, SpriteEditorInk.Secondary);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectColor(Palette.VisibleCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectColor(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.SelectColor(Palette.VisibleCount, SpriteEditorInk.Secondary));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.SelectColor(-1, SpriteEditorInk.Secondary));

        Assert.Equal(15, session.CurrentColor);
        Assert.Equal(15, session.SecondaryColor);
    }

    /// <summary>
    /// A stroke lays the ink of the button that opened it — and a shape the ink of the press
    /// that anchored it, which is also what its preview promises through
    /// <see cref="SpriteEditorSession.ShapeInk"/>.
    ///
    /// <para>Break recipe: make <c>Plot</c> read <see cref="SpriteEditorSession.CurrentColor"/>
    /// directly again instead of <c>InkColor(_strokeInk)</c> — both halves go red with the
    /// left ink's colour in them, which is the failure an author would see as "the right button
    /// paints the wrong colour". Make <c>CommitShape</c> call the parameterless
    /// <c>BeginStroke</c> and only the shape half goes red, naming the one line at fault.</para>
    /// </summary>
    [Fact]
    public void AStrokeAndAShapeLayTheInkOfThePressThatOpenedThem()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        session.SelectColor(3, SpriteEditorInk.Secondary);

        Stroke(session, SpriteEditorInk.Secondary, (0, 0), (2, 0));
        Assert.Equal(3, RegionPixelAt(session, 0, 0));
        Assert.Equal(3, RegionPixelAt(session, 1, 0));
        Assert.Equal(3, RegionPixelAt(session, 2, 0));

        session.SelectTool(SpriteEditorTool.Shape);
        session.SelectShape(ShapeVariant.Rectangle);
        session.BeginShape(4, 4, SpriteEditorInk.Secondary);
        Assert.Equal(SpriteEditorInk.Secondary, session.ShapeInk);
        session.UpdateShape(6, 6, filled: true);
        session.CommitShape();
        Assert.Equal(3, RegionPixelAt(session, 5, 5));

        // And the left button is untouched by any of it — the first ink still paints the first.
        session.SelectTool(SpriteEditorTool.Pencil);
        Stroke(session, SpriteEditorInk.Primary, (0, 7));
        Assert.Equal(7, RegionPixelAt(session, 0, 7));
    }

    /// <summary>
    /// The eyedropper fills <b>the ink that asked for it</b> (REFERENCES-EDITORS §8 item 7):
    /// the middle button and the bare X fill the first, the second ink's paths fill the second,
    /// and neither disturbs the other.
    ///
    /// <para>Break recipe: drop the <c>ink</c> parameter's branch in
    /// <see cref="SpriteEditorSession.PickColor"/> so it always writes the first — the second
    /// assertion pair goes red, and in production the second colour would have no eyedropper at
    /// all, only the palette.</para>
    /// </summary>
    [Fact]
    public void TheEyedropperFillsTheInkThatAskedForIt()
    {
        var session = new SpriteEditorSession(CartFolder(FlatSheet(11)));
        session.SelectColor(2);
        session.SelectColor(4, SpriteEditorInk.Secondary);

        session.PickColor(3, 3, SpriteEditorInk.Secondary);
        Assert.Equal(11, session.SecondaryColor);
        Assert.Equal(2, session.CurrentColor);              // the left ink never moved

        session.PickColor(3, 3);                            // the default IS the first ink
        Assert.Equal(11, session.CurrentColor);
    }

    // ==================================================================================
    // §8 item 6 — the bucket's replace-everywhere half.
    // ==================================================================================

    /// <summary>
    /// <see cref="SpriteEditorSession.ReplaceColor"/> is the bucket without the walls: every
    /// pixel of that colour in the region changes, connected to the click or not. The test puts
    /// two islands of one colour on the canvas and shows the difference against
    /// <see cref="SpriteEditorSession.Fill"/> in the same breath — the fill reaches one island,
    /// the replace reaches both, which IS the feature.
    ///
    /// <para>Break recipe: make <c>ReplaceColor</c> delegate to <c>VisitConnectedColor</c> (the
    /// easy "reuse the walk" mistake) and the far island's assertion goes red while everything
    /// else stays green — the two verbs would then be the same verb under two names.</para>
    /// </summary>
    [Fact]
    public void ReplaceColorReachesEveryIslandWhereTheFillReachesOne()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(6);
        Stroke(session, SpriteEditorInk.Primary, (0, 0));    // island A
        Stroke(session, SpriteEditorInk.Primary, (7, 7));    // island B, nowhere near it

        session.SelectColor(9);
        session.Fill(0, 0);
        Assert.Equal(9, RegionPixelAt(session, 0, 0));
        Assert.Equal(6, RegionPixelAt(session, 7, 7));       // the flood never got there

        session.SelectColor(1);
        session.ReplaceColor(7, 7);
        Assert.Equal(1, RegionPixelAt(session, 7, 7));
        Assert.Equal(9, RegionPixelAt(session, 0, 0));       // and it only took colour 6
    }

    /// <summary>
    /// The replace's border is the fill's border, to the pixel: the region, and nothing outside
    /// it. The sheet here is one flat colour, so a replace that used the sheet's bounds instead
    /// of the region's would repaint all 256 sprites — the loudest possible version of the bug.
    ///
    /// <para>Break recipe: swap <c>RegionPixels</c> for <c>CartData.GfxWidth</c> in
    /// <c>ReplaceColor</c>'s loop (or drop <c>SheetOffset</c> for a flat index) and the
    /// neighbour assertions go red immediately. Region size 2 is deliberate: at size 1 a loop
    /// that forgot the anchor would still look right on sprite 0.</para>
    /// </summary>
    [Fact]
    public void ReplaceColorStopsAtTheRegionBorderExactlyWhereTheFillDoes()
    {
        var session = new SpriteEditorSession(CartFolder(FlatSheet(4)));
        session.SelectRegionSize(2);            // a 16x16 region, so the border is not the sprite's
        session.SelectRegionCell(2, 2);
        session.SelectColor(10);

        session.ReplaceColor(0, 0);

        int size = VirtualConsole.SpriteSize;
        int originX = 2 * size;
        int originY = 2 * size;
        for (int y = 0; y < session.RegionPixels; y++)
        {
            for (int x = 0; x < session.RegionPixels; x++)
            {
                Assert.Equal(10, PixelAt(session, originX + x, originY + y));
            }
        }
        // One pixel out on every side is still the sheet's original colour.
        Assert.Equal(4, PixelAt(session, originX - 1, originY));
        Assert.Equal(4, PixelAt(session, originX, originY - 1));
        Assert.Equal(4, PixelAt(session, originX + session.RegionPixels, originY));
        Assert.Equal(4, PixelAt(session, originX, originY + session.RegionPixels));
    }

    /// <summary>
    /// One replace is one undo step, and a replace that would change nothing never happened —
    /// the same two promises the fill and the stroke make, because an author's Ctrl+Z must not
    /// have to know which of the three they used.
    ///
    /// <para>Break recipe: delete the <c>target == color</c> guard from <c>ReplaceColor</c> and
    /// the no-op half goes red — <see cref="SpriteEditorSession.CanUndo"/> would report a step
    /// for a click that painted nothing, which is the "Ctrl+Z appears dead" defect the stroke's
    /// own guard was written against.</para>
    /// </summary>
    [Fact]
    public void ReplaceColorIsOneUndoStepAndANoOpNeverHappened()
    {
        var session = new SpriteEditorSession(CartFolder(FlatSheet(4)));
        byte[] before = session.Pixels.ToArray();
        session.SelectColor(4);

        session.ReplaceColor(0, 0);             // colour 4 with colour 4
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);

        session.SelectColor(13);
        session.ReplaceColor(0, 0);
        Assert.True(session.CanUndo);
        session.Undo();
        Assert.True(session.Pixels.SequenceEqual(before));
        Assert.False(session.CanUndo);
    }

    /// <summary>
    /// The replace takes the ink it was asked with, like every other verb that lays colour —
    /// TIC-80's Ctrl+right-click over the bucket replaces with <c>color2</c>.
    ///
    /// <para>Break recipe: hard-code <see cref="SpriteEditorSession.CurrentColor"/> in
    /// <c>ReplaceColor</c> instead of <c>InkColor(ink)</c> and this goes red alone, without
    /// touching the fill's or the stroke's tests — which is what tells the reader that the three
    /// really do read one lookup.</para>
    /// </summary>
    [Fact]
    public void ReplaceColorHonoursTheInkItWasAskedWith()
    {
        var session = new SpriteEditorSession(CartFolder(FlatSheet(4)));
        session.SelectColor(7);
        session.SelectColor(2, SpriteEditorInk.Secondary);

        session.ReplaceColor(0, 0, SpriteEditorInk.Secondary);

        Assert.Equal(2, RegionPixelAt(session, 0, 0));
        Assert.Equal(2, RegionPixelAt(session, 7, 7));
    }
}

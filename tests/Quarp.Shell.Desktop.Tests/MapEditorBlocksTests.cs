using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The map draws in blocks and can copy (wave 3e) — items 3 and 4 of REFERENCES-EDITORS §8,
/// "главная жалоба на слабые редакторы". Four claims, each driven through the <b>production</b>
/// router (<see cref="MapEditorInput.Update"/>) rather than a mirror of it:
///
/// <list type="number">
///   <item><description>a drag across the tile picker marks a block of any size N x M
///   (TIC-80 <c>map->sheet.rect</c>, PICO-8's "shift+drag in sprite navigator"), and a drag the
///   other way marks the same one;</description></item>
///   <item><description>the pencil stamps the whole block — <b>only</b> on the lattice of its
///   own size, TIC-80's <c>if(w % sheet.rect.w == 0 &amp;&amp; h % sheet.rect.h == 0)</c>, which
///   is the difference between a block and a smear of half-blocks;</description></item>
///   <item><description>Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste a rectangle of the map,
///   one undo step each, the paste floating until a press puts it down
///   (TIC-80 <c>drawPasteData</c>);</description></item>
///   <item><description>both input channels reach all of it, which the parity law makes
///   non-optional.</description></item>
/// </list>
///
/// <para><b>The harness is the neighbours'</b> — <see cref="MapEditorToolsTests"/>' "window,
/// minus the window": the same four shell objects <c>QuarpGame</c> owns, the same two
/// production readers, a back buffer that is a pair of constants, and the same road into the
/// editor (menu → library → editor → tilemap tab), because anything shorter leaves the machine
/// in <c>Menu</c> and every assertion below meaningless.</para>
/// </summary>
public class MapEditorBlocksTests : IDisposable
{
    private readonly string _root;

    public MapEditorBlocksTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-mapblocks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    /// <summary>One frame at 60 Hz — the router spends it on the tooltip clock only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    // ==================================================================================
    // The window, minus the window — MapEditorToolsTests' shape, verbatim.
    // ==================================================================================

    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        internal MapEditorSession Map => Modes.MapEditor!;

        internal MapEditorView View => Modes.MapView!;

        internal MapEditorLayout Layout => MapEditorLayout.Compute(WindowWidth, WindowHeight);

        internal EditorShell Context =>
            new(Modes, Flyout, Hover, SheetScroll, WindowWidth, WindowHeight);

        internal void Frame(
            Keys[] down, int mouseX, int mouseY, ButtonState left, ButtonState middle, ButtonState right)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, 0, left, middle, right, ButtonState.Released, ButtonState.Released));
            MapEditorInput.Update(Context, commands, mouse, FrameSeconds);
        }

        internal void Idle() =>
            Frame(NoKeys, Off, Off, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        internal void Tap(params Keys[] down)
        {
            Frame(down, Off, Off, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Idle();
        }

        internal void LeftDown(int x, int y, params Keys[] down) =>
            Frame(down, x, y, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);

        internal void LeftHeld(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);

        internal void LeftUp(int x, int y, params Keys[] down) =>
            Frame(down, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        internal void Click(int x, int y)
        {
            LeftDown(x, y);
            LeftUp(x, y);
        }

        /// <summary>A press, one held sample somewhere else, a release — the drag every claim here needs.</summary>
        internal void Drag((int X, int Y) from, (int X, int Y) to)
        {
            LeftDown(from.X, from.Y);
            LeftHeld(to.X, to.Y);
            LeftUp(to.X, to.Y);
        }

        /// <summary>Far outside any rectangle the layout places — an idle pointer must hit nothing.</summary>
        private const int Off = -1000;
    }

    /// <summary>The neighbours' fixture, verbatim: a one-cart library of its own, opened on the map tab.</summary>
    private Harness OpenMapEditor()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"blocks\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();
        machine.SwitchEditorTab(ShellMode.MapEditor);
        Assert.Equal(ShellMode.MapEditor, machine.Mode);
        return new Harness(machine);
    }

    private static (int X, int Y) Centre(Rectangle rect) =>
        (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>The window point in the middle of a map cell, at the camera the view stands at.</summary>
    private static (int X, int Y) CellPoint(Harness harness, int cellX, int cellY) =>
        Centre(harness.Layout.MapCellRect(cellX, cellY, harness.View.CameraX, harness.View.CameraY));

    /// <summary>
    /// The sprite in one cell of the picker, derived through <see cref="SheetStrip"/> — the one
    /// owner of the strip mapping — rather than typed, exactly as
    /// <c>MapEditorViewTests.LastStripCellSprite</c> does. <see cref="TheStripAnchorsThisFileUses"/>
    /// pins the two numbers this file leans on, so a re-cut strip goes red there by name instead
    /// of quietly changing what every test below means.
    /// </summary>
    private static int SpriteAtStripCell(int column, int row)
    {
        Assert.True(SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY));
        return sheetY * SheetStrip.LaneColumns + sheetX;
    }

    /// <summary>The window point in the middle of one picker cell, named by its strip coordinates.</summary>
    private static (int X, int Y) StripPoint(Harness harness, int column, int row) =>
        Centre(harness.Layout.TileCellRect(SpriteAtStripCell(column, row)));

    /// <summary>Paint one cell with one tile through the router: pick it in the picker, click the cell.</summary>
    private static void PaintOne(Harness harness, int cellX, int cellY, int tile)
    {
        (int tileX, int tileY) = Centre(harness.Layout.TileCellRect(tile));
        harness.Click(tileX, tileY);
        (int x, int y) = CellPoint(harness, cellX, cellY);
        harness.Click(x, y);
        Assert.Equal(tile, harness.Map.TileAt(cellX, cellY));
    }

    /// <summary>The four-cell asymmetric patch every clipboard test copies — no two tiles alike, so a transposed or mirrored paste is visible.</summary>
    private static void PaintTheSourcePatch(Harness harness)
    {
        PaintOne(harness, 1, 1, 5);
        PaintOne(harness, 2, 1, 6);
        PaintOne(harness, 1, 2, 7);
        PaintOne(harness, 2, 2, 8);
    }

    /// <summary>Mark the source patch with the select tool's mouse drag.</summary>
    private static void MarkTheSourcePatch(Harness harness)
    {
        harness.Tap(Keys.D3);
        harness.Drag(CellPoint(harness, 1, 1), CellPoint(harness, 2, 2));
        Assert.Equal(
            (1, 1, 2, 2),
            (harness.View.SelectionX, harness.View.SelectionY,
             harness.View.SelectionWidth, harness.View.SelectionHeight));
    }

    // ==================================================================================
    // 0. The two strip anchors this file leans on.
    // ==================================================================================

    /// <summary>
    /// Everything below marks the block at strip cells (2,1)..(3,2), which on today's strip is
    /// sprites 18/19/34/35 — the first lane is the sheet's own 16-wide rows, so a strip cell is
    /// <c>row * 16 + column</c>. Pinning it here once means a future re-cut of
    /// <see cref="SheetStrip.Rows"/> turns <b>this</b> test red with the real reason instead of
    /// leaving the others red for a reason that reads like a block bug.
    /// </summary>
    [Fact]
    public void TheStripAnchorsThisFileUses()
    {
        Assert.Equal(18, SpriteAtStripCell(2, 1));
        Assert.Equal(19, SpriteAtStripCell(3, 1));
        Assert.Equal(34, SpriteAtStripCell(2, 2));
        Assert.Equal(35, SpriteAtStripCell(3, 2));
    }

    // ==================================================================================
    // 1. The picker marks a block N x M.
    // ==================================================================================

    /// <summary>
    /// A drag across the picker marks a rectangle of tiles of any size — TIC-80's
    /// <c>map->sheet.rect</c>, "любой размер N×M, не только 2×2/4×4" (REFERENCES-EDITORS §3.1) —
    /// and the drag the other way round marks the same rectangle, because the block is
    /// normalized out of its anchor exactly as the map's own selection is. A 3x2 block is asked
    /// for on purpose: a square would pass a width/height swap.
    ///
    /// <para>Break recipe: replace either <c>Math.Min</c> in
    /// <see cref="MapEditorView.UpdateTileBlock"/> with the anchor — the backwards drag reports
    /// a 1x1 block at the wrong tile and both halves of the equality go red; swap the width and
    /// height arguments of <c>SelectSpriteBlock</c> there and the 3x2 assertions go red while
    /// the anchor still passes.</para>
    /// </summary>
    [Fact]
    public void ADragAcrossThePickerMarksABlockAndABackwardsDragMarksTheSameOne()
    {
        Harness forward = OpenMapEditor();
        Assert.Equal((1, 1), (forward.Map.BlockWidth, forward.Map.BlockHeight));   // TIC-80 opens at 1x1

        forward.Drag(StripPoint(forward, 2, 1), StripPoint(forward, 4, 2));

        Assert.Equal(SpriteAtStripCell(2, 1), forward.Map.SelectedSprite);
        Assert.Equal((3, 2), (forward.Map.BlockWidth, forward.Map.BlockHeight));
        Assert.False(forward.View.TileBlockGestureActive);          // the release closed it

        Harness backward = OpenMapEditor();
        backward.Drag(StripPoint(backward, 4, 2), StripPoint(backward, 2, 1));

        Assert.Equal(forward.Map.SelectedSprite, backward.Map.SelectedSprite);
        Assert.Equal(forward.Map.BlockWidth, backward.Map.BlockWidth);
        Assert.Equal(forward.Map.BlockHeight, backward.Map.BlockHeight);
    }

    /// <summary>
    /// Every path that names a <b>single</b> tile drops the block — the click on one picker
    /// cell, Shift+arrows, the middle-button eyedropper, the empty-tile button. That is the
    /// "one owner" rule made visible: the size lives beside the tile in
    /// <see cref="MapEditorSession"/> and <see cref="MapEditorSession.SelectSprite"/> is the one
    /// door that resets it, so no path can leave a 3x2 block armed behind a one-tile choice.
    ///
    /// <para>Break recipe: delete the two resetting lines from
    /// <see cref="MapEditorSession.SelectSprite"/> — every assertion after the first goes red;
    /// make <see cref="MapEditorSession.PickTile"/> assign the field directly again and only the
    /// eyedropper's does.</para>
    /// </summary>
    [Fact]
    public void EveryOneTilePathDropsTheBlock()
    {
        Harness harness = OpenMapEditor();
        harness.Drag(StripPoint(harness, 2, 1), StripPoint(harness, 4, 2));
        Assert.Equal((3, 2), (harness.Map.BlockWidth, harness.Map.BlockHeight));

        (int oneTileX, int oneTileY) = StripPoint(harness, 5, 0);
        harness.Click(oneTileX, oneTileY);
        Assert.Equal((1, 1), (harness.Map.BlockWidth, harness.Map.BlockHeight));

        harness.Drag(StripPoint(harness, 2, 1), StripPoint(harness, 4, 2));
        harness.Tap(Keys.LeftShift, Keys.Right);
        Assert.Equal((1, 1), (harness.Map.BlockWidth, harness.Map.BlockHeight));

        harness.Drag(StripPoint(harness, 2, 1), StripPoint(harness, 4, 2));
        (int cellX, int cellY) = CellPoint(harness, 9, 9);
        harness.Frame(NoKeys, cellX, cellY, ButtonState.Released, ButtonState.Pressed, ButtonState.Released);
        harness.Idle();
        Assert.Equal((1, 1), (harness.Map.BlockWidth, harness.Map.BlockHeight));

        harness.Drag(StripPoint(harness, 2, 1), StripPoint(harness, 4, 2));
        (int eraserX, int eraserY) = Centre(harness.Layout.ButtonRect(EditorButton.ToolEraser));
        harness.Click(eraserX, eraserY);
        Assert.Equal((1, 1), (harness.Map.BlockWidth, harness.Map.BlockHeight));
    }

    // ==================================================================================
    // 2. The pencil stamps the block.
    // ==================================================================================

    /// <summary>
    /// A 2x2 block puts four tiles in four places, in the arrangement the picker showed them:
    /// the top-left tile lands on the clicked cell and the rest go right and down from it.
    /// The four sprites differ, so a transposed or rotated stamp cannot pass.
    ///
    /// <para>Break recipe: swap <c>row</c> and <c>column</c> in
    /// <see cref="MapEditorPaint.BlockTiles"/> and the two off-diagonal cells trade values;
    /// drop the <c>+ column</c> from <c>BlitBlock</c>'s x and the whole block collapses into one
    /// column.</para>
    /// </summary>
    [Fact]
    public void ThePencilStampsTheWholeBlockInTheRightPlaces()
    {
        Harness harness = OpenMapEditor();
        harness.Drag(StripPoint(harness, 2, 1), StripPoint(harness, 3, 2));
        Assert.Equal((2, 2), (harness.Map.BlockWidth, harness.Map.BlockHeight));

        (int x, int y) = CellPoint(harness, 4, 2);
        harness.Click(x, y);

        Assert.Equal(SpriteAtStripCell(2, 1), harness.Map.TileAt(4, 2));
        Assert.Equal(SpriteAtStripCell(3, 1), harness.Map.TileAt(5, 2));
        Assert.Equal(SpriteAtStripCell(2, 2), harness.Map.TileAt(4, 3));
        Assert.Equal(SpriteAtStripCell(3, 2), harness.Map.TileAt(5, 3));

        // And nothing around it: a block is a rectangle, not a splash.
        Assert.Equal(0, harness.Map.TileAt(3, 2));
        Assert.Equal(0, harness.Map.TileAt(6, 2));
        Assert.Equal(0, harness.Map.TileAt(4, 1));
        Assert.Equal(0, harness.Map.TileAt(4, 4));

        // The whole stamp is one undo step, the rule a one-cell gesture already carried.
        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.Equal(0, harness.Map.TileAt(4, 2));
        Assert.Equal(0, harness.Map.TileAt(5, 3));
        Assert.False(harness.Map.CanUndo);
    }

    /// <summary>
    /// <b>The lattice rule</b>, TIC-80's <c>processMouseDrawMode</c>:
    /// <c>if(w % sheet.rect.w == 0 &amp;&amp; h % sheet.rect.h == 0) setMapSprite(...)</c>.
    /// A 2x2 block dragged along a row lands on columns 4 and 6 and is <b>skipped</b> at 5 —
    /// which the map itself shows: cell (5,2) still holds the <em>right</em> half of the first
    /// stamp (sprite 19) rather than the left half of a stamp at 5 (sprite 18). Without the
    /// rule every intermediate position would overwrite the previous block's right half with
    /// its own left half and the author's two-by-two tree would come out as a column of
    /// tree-halves.
    ///
    /// <para>Break recipe: delete the modulo guard from <c>MapEditorPaint.StampBlock</c> — the
    /// (5,2) assertion goes red naming 18 where 19 belongs, and (7,2) goes red too because the
    /// last stamp then starts at 6 and not at 7... every assertion here is a different symptom
    /// of the same one line. Replace the skip with a snap (<c>cellX -= cellX % width</c>) and
    /// the map comes out right for this drag but the block is drawn where the pointer is not —
    /// which is why the rule is a skip.</para>
    /// </summary>
    [Fact]
    public void TheBlockOnlyLandsOnTheLatticeOfItsOwnSize()
    {
        Harness harness = OpenMapEditor();
        harness.Drag(StripPoint(harness, 2, 1), StripPoint(harness, 3, 2));

        harness.LeftDown(CellPoint(harness, 4, 2).X, CellPoint(harness, 4, 2).Y);
        harness.LeftHeld(CellPoint(harness, 5, 2).X, CellPoint(harness, 5, 2).Y);
        harness.LeftHeld(CellPoint(harness, 6, 2).X, CellPoint(harness, 6, 2).Y);
        harness.LeftUp(CellPoint(harness, 6, 2).X, CellPoint(harness, 6, 2).Y);

        // Column 4: written. Column 5: NOT written — the cell still holds the first stamp's
        // right half. Column 6: written, and its right half reaches column 7.
        Assert.Equal(SpriteAtStripCell(2, 1), harness.Map.TileAt(4, 2));
        Assert.Equal(SpriteAtStripCell(3, 1), harness.Map.TileAt(5, 2));
        Assert.Equal(SpriteAtStripCell(2, 1), harness.Map.TileAt(6, 2));
        Assert.Equal(SpriteAtStripCell(3, 1), harness.Map.TileAt(7, 2));
        Assert.Equal(0, harness.Map.TileAt(8, 2));

        // The odd ROW is refused the same way: a press at (4,3) with a 2x2 block writes nothing.
        Harness rows = OpenMapEditor();
        rows.Drag(StripPoint(rows, 2, 1), StripPoint(rows, 3, 2));
        rows.Click(CellPoint(rows, 4, 3).X, CellPoint(rows, 4, 3).Y);
        Assert.False(rows.Map.IsDirty);
        Assert.False(rows.Map.CanUndo);
    }

    // ==================================================================================
    // 3. Copy, cut, paste.
    // ==================================================================================

    /// <summary>
    /// Ctrl+C, Ctrl+V and the click that places the block reproduce the marked rectangle
    /// somewhere else <b>byte for byte</b> — the four tiles differ from one another, so a
    /// mirrored, transposed or offset paste cannot pass. The source survives: copying is not
    /// moving.
    ///
    /// <para>Break recipe: swap the row and column indices in
    /// <see cref="MapEditorPaint.CopySelection"/>'s read loop and the paste comes out
    /// transposed; drop the <c>view.EndPaste()</c> from <see cref="MapEditorPaint.PasteAt"/> and
    /// the next click pastes a second time, which the "the pencil works again" assertion
    /// names.</para>
    /// </summary>
    [Fact]
    public void CopyThenPasteReproducesTheRectangleSomewhereElse()
    {
        Harness harness = OpenMapEditor();
        PaintTheSourcePatch(harness);
        MarkTheSourcePatch(harness);

        harness.Tap(Keys.LeftControl, Keys.C);
        Assert.True(harness.View.Clipboard.HasBlock);
        Assert.Equal((2, 2), (harness.View.Clipboard.Width, harness.View.Clipboard.Height));

        harness.Tap(Keys.LeftControl, Keys.V);
        Assert.True(harness.View.PasteFloating);
        Assert.Equal(0, harness.Map.TileAt(10, 5));         // nothing is written while it floats

        harness.Click(CellPoint(harness, 10, 5).X, CellPoint(harness, 10, 5).Y);

        Assert.False(harness.View.PasteFloating);
        Assert.Equal(5, harness.Map.TileAt(10, 5));
        Assert.Equal(6, harness.Map.TileAt(11, 5));
        Assert.Equal(7, harness.Map.TileAt(10, 6));
        Assert.Equal(8, harness.Map.TileAt(11, 6));

        // The source is untouched — copy, not cut.
        Assert.Equal(5, harness.Map.TileAt(1, 1));
        Assert.Equal(8, harness.Map.TileAt(2, 2));

        // And the click after the paste is an ordinary click again: the float is spent.
        harness.Tap(Keys.D1);
        (int tileX, int tileY) = Centre(harness.Layout.TileCellRect(12));
        harness.Click(tileX, tileY);
        harness.Click(CellPoint(harness, 20, 5).X, CellPoint(harness, 20, 5).Y);
        Assert.Equal(12, harness.Map.TileAt(20, 5));
    }

    /// <summary>
    /// Ctrl+X empties the source and does it in <b>one</b> undo step: after Ctrl+Z the map is
    /// byte-identical to what it was before the cut. The whole map is compared, not the four
    /// cells, because "one step" is a claim about everything the step could have moved.
    ///
    /// <para>Break recipe: make <see cref="MapEditorPaint.CutSelection"/> clear the cells one
    /// by one through <c>ClearArea</c> per cell — the byte comparison after a single Ctrl+Z goes
    /// red with three cells still empty; make it clear without copying and the paste at the end
    /// puts nothing down.</para>
    /// </summary>
    [Fact]
    public void CutEmptiesTheSourceInOneUndoStepAndStillCopies()
    {
        Harness harness = OpenMapEditor();
        PaintTheSourcePatch(harness);
        MarkTheSourcePatch(harness);
        byte[] beforeTheCut = harness.Map.Map.ToArray();

        harness.Tap(Keys.LeftControl, Keys.X);

        Assert.Equal(0, harness.Map.TileAt(1, 1));
        Assert.Equal(0, harness.Map.TileAt(2, 1));
        Assert.Equal(0, harness.Map.TileAt(1, 2));
        Assert.Equal(0, harness.Map.TileAt(2, 2));
        Assert.True(harness.View.Clipboard.HasBlock);

        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.True(harness.Map.Map.SequenceEqual(beforeTheCut));

        // The clipboard survived the undo — it is not part of the map's history.
        harness.Tap(Keys.LeftControl, Keys.V);
        harness.Click(CellPoint(harness, 30, 8).X, CellPoint(harness, 30, 8).Y);
        Assert.Equal(5, harness.Map.TileAt(30, 8));
        Assert.Equal(8, harness.Map.TileAt(31, 9));
    }

    /// <summary>
    /// The paste is one undo step too (TIC-80 pushes exactly one history entry in
    /// <c>drawPasteData</c>): one Ctrl+Z and the map is byte-identical to the moment before the
    /// block landed. Break recipe: give <see cref="MapEditorSession.PasteBlock"/> a
    /// <c>BeginStroke</c>/<c>EndStroke</c> pair per row and the comparison goes red with the
    /// last row still pasted.
    /// </summary>
    [Fact]
    public void ThePasteIsOneUndoStep()
    {
        Harness harness = OpenMapEditor();
        PaintTheSourcePatch(harness);
        MarkTheSourcePatch(harness);
        harness.Tap(Keys.LeftControl, Keys.C);
        byte[] beforeThePaste = harness.Map.Map.ToArray();

        harness.Tap(Keys.LeftControl, Keys.V);
        harness.Click(CellPoint(harness, 12, 4).X, CellPoint(harness, 12, 4).Y);
        Assert.Equal(5, harness.Map.TileAt(12, 4));

        harness.Tap(Keys.LeftControl, Keys.Z);

        Assert.True(harness.Map.Map.SequenceEqual(beforeThePaste));
    }

    /// <summary>
    /// Esc over a floating block drops it and writes nothing — no cell, no undo step, no dirt.
    /// That is what makes Ctrl+V safe to press by accident, and it is only true because nothing
    /// is written while the block floats.
    ///
    /// <para>Break recipe: delete the <c>view.PasteFloating</c> branch from
    /// <c>MapEditorInput</c>'s Esc block — Esc then drops the <em>selection</em> instead and the
    /// still-floating assertion goes red; make <see cref="MapEditorView.BeginPaste"/> write the
    /// block immediately and the version assertion names it.</para>
    /// </summary>
    [Fact]
    public void EscapeCancelsTheFloatingPasteAndWritesNothing()
    {
        Harness harness = OpenMapEditor();
        PaintTheSourcePatch(harness);
        MarkTheSourcePatch(harness);
        harness.Tap(Keys.LeftControl, Keys.C);
        int version = harness.Map.Version;
        byte[] before = harness.Map.Map.ToArray();

        harness.Tap(Keys.LeftControl, Keys.V);
        Assert.True(harness.View.PasteFloating);

        harness.Tap(Keys.Escape);

        Assert.False(harness.View.PasteFloating);
        Assert.Equal(version, harness.Map.Version);
        Assert.True(harness.Map.Map.SequenceEqual(before));
        Assert.Equal(ShellMode.MapEditor, harness.Modes.Mode);      // and it did not leave the screen

        // The next click is an ordinary one — the cancelled block does not land late.
        harness.Tap(Keys.D1);
        harness.Click(CellPoint(harness, 20, 9).X, CellPoint(harness, 20, 9).Y);
        Assert.Equal(0, harness.Map.TileAt(21, 10));
    }

    /// <summary>
    /// A paste whose block hangs off the map is <b>clipped</b>, not refused and not thrown on:
    /// the cells that are on the map are written and the rest are dropped. The alternative —
    /// <c>MapEditorSession.ValidateCell</c>'s throw — would make the map's own corner
    /// unpasteable, and snapping the block back inside would put tiles where the author did not
    /// point.
    ///
    /// <para>Break recipe: delete either bounds test from <c>MapEditorSession.BlitBlock</c> and
    /// this test fails with an <c>IndexOutOfRangeException</c> — or worse, silently wraps a tile
    /// onto the next row, which the "the row below is empty" assertion catches.</para>
    /// </summary>
    [Fact]
    public void APasteAtTheMapEdgeIsClippedAndDoesNotThrow()
    {
        Harness harness = OpenMapEditor();
        PaintTheSourcePatch(harness);
        MarkTheSourcePatch(harness);
        harness.Tap(Keys.LeftControl, Keys.C);

        // Travel to the far corner by the mouse's road, then paste onto the last cell itself.
        MapEditorLayout layout = harness.Layout;
        harness.Click(layout.Minimap.Right - 1, layout.Minimap.Bottom - 1);
        int lastColumn = MapEditorSession.MapColumns - 1;
        int lastRow = MapEditorSession.MapRows - 1;

        harness.Tap(Keys.LeftControl, Keys.V);
        harness.Click(CellPoint(harness, lastColumn, lastRow).X, CellPoint(harness, lastColumn, lastRow).Y);

        Assert.Equal(5, harness.Map.TileAt(lastColumn, lastRow));
        // The three cells that fell off the map were dropped, not wrapped: the map's last row
        // holds nothing else, and neither does the column before it.
        Assert.Equal(0, harness.Map.TileAt(lastColumn - 1, lastRow));
        Assert.Equal(0, harness.Map.TileAt(0, lastRow));
        Assert.Equal(0, harness.Map.TileAt(lastColumn, lastRow - 1));
    }

    // ==================================================================================
    // 4. Both channels.
    // ==================================================================================

    /// <summary>
    /// The wave's parity claim, proved the way this project proves parity: two carts, two
    /// channels, one byte comparison. The keyboard run marks its block with Shift+arrows and
    /// Ctrl+Shift+arrows and stamps it with Z; the mouse run drags the picker and clicks. Then
    /// both copy a rectangle, paste it — the keyboard by Z, the mouse by a click — and their
    /// maps must come out identical.
    ///
    /// <para>Break recipes, all against production code. (a) Delete the
    /// <c>EditorBlockDx/Dy</c> lines from <see cref="ShellCommandReader"/>: the keyed block
    /// stays 1x1 and the byte comparison goes red. (b) Delete the
    /// <see cref="MapEditorPaint.PasteAt"/> call from <c>MapEditorInput.KeyboardAct</c>: the
    /// keyed paste never lands while the clicked one does. (c) Give
    /// <c>MapEditorInput</c>'s <c>MousePressOnCanvas</c> the paste check but not
    /// <c>KeyboardAct</c> — same red, the other way round.</para>
    /// </summary>
    [Fact]
    public void TheKeyboardPathAndTheMousePathProduceTheSameMap()
    {
        Harness keyed = OpenMapEditor();
        // Pick strip cell (2,1) — two steps right, one down — then grow the block to 2x2.
        keyed.Tap(Keys.LeftShift, Keys.Right);
        keyed.Tap(Keys.LeftShift, Keys.Right);
        keyed.Tap(Keys.LeftShift, Keys.Down);
        Assert.Equal(SpriteAtStripCell(2, 1), keyed.Map.SelectedSprite);
        keyed.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Right);
        keyed.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Down);
        Assert.Equal((2, 2), (keyed.Map.BlockWidth, keyed.Map.BlockHeight));
        // Walk the map cursor to (4,2) and stamp with Z.
        for (int i = 0; i < 4; i++)
        {
            keyed.Tap(Keys.Right);
        }
        keyed.Tap(Keys.Down);
        keyed.Tap(Keys.Down);
        Assert.Equal((4, 2), (keyed.View.CursorX, keyed.View.CursorY));
        keyed.Tap(Keys.Z);

        Harness clicked = OpenMapEditor();
        clicked.Drag(StripPoint(clicked, 2, 1), StripPoint(clicked, 3, 2));
        clicked.Click(CellPoint(clicked, 4, 2).X, CellPoint(clicked, 4, 2).Y);

        Assert.True(keyed.Map.Map.SequenceEqual(clicked.Map.Map));
        Assert.Equal(SpriteAtStripCell(3, 2), keyed.Map.TileAt(5, 3));      // not a vacuous pass

        // Now the clipboard, one channel each. Both mark the same rectangle first: the keyed run
        // with held Z and the arrows, the clicked run with a drag.
        keyed.Tap(Keys.D3);
        keyed.Frame(new[] { Keys.Z }, -1000, -1000, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(
            new[] { Keys.Z, Keys.Right }, -1000, -1000,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(new[] { Keys.Z }, -1000, -1000, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(
            new[] { Keys.Z, Keys.Down }, -1000, -1000,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Idle();
        Assert.Equal(
            (4, 2, 2, 2),
            (keyed.View.SelectionX, keyed.View.SelectionY,
             keyed.View.SelectionWidth, keyed.View.SelectionHeight));

        clicked.Tap(Keys.D3);
        clicked.Drag(CellPoint(clicked, 4, 2), CellPoint(clicked, 5, 3));

        keyed.Tap(Keys.LeftControl, Keys.C);
        clicked.Tap(Keys.LeftControl, Keys.C);

        // The keyed run places its paste by walking the cursor and pressing Z; the clicked run
        // by pointing and clicking. No Keys value decides where the clicked one lands, and no
        // mouse coordinate decides where the keyed one does.
        keyed.Tap(Keys.LeftControl, Keys.V);
        // The marking drag left the cursor on (5,3); nine steps right put it on (14,3).
        for (int i = 0; i < 9; i++)
        {
            keyed.Tap(Keys.Right);
        }
        Assert.Equal((14, 3), (keyed.View.CursorX, keyed.View.CursorY));
        keyed.Tap(Keys.Z);

        clicked.Tap(Keys.LeftControl, Keys.V);
        clicked.Click(CellPoint(clicked, 14, 3).X, CellPoint(clicked, 14, 3).Y);

        Assert.True(keyed.Map.Map.SequenceEqual(clicked.Map.Map));
        Assert.Equal(SpriteAtStripCell(2, 1), keyed.Map.TileAt(14, 3));
        Assert.Equal(SpriteAtStripCell(3, 2), keyed.Map.TileAt(15, 4));
    }

    // ==================================================================================
    // 5. The read-only map keeps the new verbs out.
    // ==================================================================================

    /// <summary>
    /// A cart with <c>map.csv</c> owns its map (MAP-FORMAT §4). The three verbs this wave adds
    /// have to be as refused as the pencil is — and copying, which writes no map byte, has to
    /// stay allowed: being unable to lift a piece of someone else's level would be a pointless
    /// second punishment, the same reasoning that keeps the eyedropper open.
    ///
    /// <para>Break recipe: delete the <c>session.MapReadOnly</c> test from
    /// <see cref="MapEditorPaint.PasteAt"/> or <see cref="MapEditorPaint.BeginBlock"/> — the
    /// call throws instead of doing nothing and the test fails naming the verb; add a
    /// <c>MapReadOnly</c> guard to <see cref="MapEditorPaint.CopySelection"/> and the clipboard
    /// assertion goes red.</para>
    /// </summary>
    [Fact]
    public void AReadOnlyMapRefusesTheBlockAndThePasteButStillCopies()
    {
        string folder = Path.Combine(_root, "readonly-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, MapEditorSession.MapSourceFileName), "# the text source owns this map\n");
        var map = new MapEditorSession(folder);
        var view = new MapEditorView();
        Assert.True(map.MapReadOnly);

        view.BeginSelection(1, 1);
        view.UpdateSelection(2, 2);
        view.EndSelection();

        MapEditorPaint.CutSelection(map, view);         // copies; the emptying half is refused
        Assert.True(view.Clipboard.HasBlock);
        Assert.Equal((2, 2), (view.Clipboard.Width, view.Clipboard.Height));

        map.SelectSpriteBlock(18, 2, 2);
        MapEditorPaint.BeginBlock(map, 0, 0);
        MapEditorPaint.ContinueBlock(map, 2, 2);
        view.BeginPaste();
        Assert.True(MapEditorPaint.PasteAt(map, view, 4, 4));   // the click is consumed
        Assert.False(view.PasteFloating);

        Assert.False(map.IsDirty);
        Assert.False(map.CanUndo);
        Assert.Equal(0, map.Version);
        // The model itself still throws — the door is a courtesy, not the lock.
        Assert.Throws<InvalidOperationException>(
            () => map.PasteBlock(0, 0, 1, 1, new byte[] { 3 }));
    }
}

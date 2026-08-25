using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>A free N×M rectangle in the sprite sheet</b> — REFERENCES-EDITORS §8 item 3, which this
/// shell had on the map ("любой размер N×M") and not on the sheet, where PICO-8 puts it
/// ("shift+drag in the sprite navigator", §2.3). The gesture is deliberately the map's own,
/// method for method: <c>BeginTileBlock</c> / <c>UpdateTileBlock</c> / <c>EndTileBlock</c> for
/// the drag and <c>StepTileBlock</c> under Ctrl+Shift+arrows for the keyboard, so the author
/// learns one rule for two palettes rather than two.
///
/// <para><b>What the block is, and what it is not.</b> It is a SECOND fact beside
/// <see cref="SpriteEditorSession.RegionCells"/>, not a widening of it — the square region is
/// the canvas (its pixels are validated against it, the 64x64 box is divided by it, the
/// selection mask is sized by it, the rotate turns it in place), and a rectangle cannot be any
/// of those things. The two are held together by one invariant, which this file pins first
/// because everything else leans on it: <b>the block always contains the region</b>, and every
/// door that names a single cell resets it to the square.</para>
///
/// <para>Driven through the production router (<see cref="SpriteEditorInput.Update"/>) with the
/// production readers, in the shape <see cref="MapEditorBlocksTests"/> established for the same
/// gesture one screen over.</para>
/// </summary>
public class SpriteSheetBlockTests : IDisposable
{
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private readonly string _root;

    public SpriteSheetBlockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-sheetblock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The window, minus the window — the four shell objects <c>QuarpGame</c> owns and the two production readers.</summary>
    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        internal SpriteEditorSession Editor => Modes.Editor!;

        internal SpriteEditorView View => Modes.SpriteView;

        internal SpriteEditorLayout Layout =>
            SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, Editor.RegionCells);

        internal EditorShell Context =>
            new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight);

        internal void Frame(Keys[] down, int mouseX, int mouseY, ButtonState left)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, 0, left, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
            SpriteEditorInput.Update(Context, commands, mouse, FrameSeconds);
        }

        internal void Idle() => Frame(NoKeys, Off, Off, ButtonState.Released);

        internal void Tap(params Keys[] down)
        {
            Frame(down, Off, Off, ButtonState.Released);
            Idle();
        }

        internal void Drag((int X, int Y) from, (int X, int Y) to)
        {
            Frame(NoKeys, from.X, from.Y, ButtonState.Pressed);
            Frame(NoKeys, to.X, to.Y, ButtonState.Pressed);
            Frame(NoKeys, to.X, to.Y, ButtonState.Released);
        }

        internal void Click(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Pressed);
            Frame(NoKeys, x, y, ButtonState.Released);
        }
    }

    private Harness OpenSpriteEditor()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"block\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        return new Harness(machine);
    }

    /// <summary>The sprite in one cell of the strip, derived through <see cref="SheetStrip"/> — the one owner of that mapping — rather than typed.</summary>
    private static int SpriteAtStripCell(int column, int row)
    {
        Assert.True(SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY));
        return sheetY * SheetStrip.LaneColumns + sheetX;
    }

    /// <summary>The console point in the middle of one sheet cell, named by its strip coordinates.</summary>
    private static (int X, int Y) StripPoint(Harness harness, int column, int row)
    {
        Assert.True(SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY));
        Rectangle cell =
            harness.Layout.SheetBlockHighlights(sheetX, sheetY, 1, 1, harness.SheetScroll.Offset)[0];
        return (cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
    }

    private static (int X, int Y, int W, int H) Block(Harness harness) =>
        (harness.Editor.RegionCellX, harness.Editor.RegionCellY,
         harness.Editor.BlockWidth, harness.Editor.BlockHeight);

    /// <summary>
    /// The strip anchors this file leans on, pinned once. Everything below drags across strip
    /// cells (2,1)..(4,3), which on today's strip is sprites 18..20 / 34..36 / 50..52 — the first
    /// lane is the sheet's own 16-wide rows. A future re-cut of <see cref="SheetStrip.Rows"/>
    /// turns <b>this</b> test red with the real reason instead of leaving the others red for a
    /// reason that reads like a block bug.
    /// </summary>
    [Fact]
    public void TheStripAnchorsThisFileUses()
    {
        Assert.Equal(18, SpriteAtStripCell(2, 1));
        Assert.Equal(20, SpriteAtStripCell(4, 1));
        Assert.Equal(52, SpriteAtStripCell(4, 3));
        // All four are inside the columns the window shows at rest, so no test below has to
        // scroll to reach them — a block dragged past the window's edge is a different claim.
        int visibleColumns = SpriteEditorLayout
            .Compute(ConsoleWidth, ConsoleHeight, 1).SheetVisiblePixels / VirtualConsole.SpriteSize;
        Assert.True(visibleColumns > 4);
    }

    /// <summary>
    /// The screen an author meets: one cell in hand, which is exactly what it was before the
    /// block existed. This is the "nothing moved" half of adding a second fact, and it is what
    /// makes every golden master of this screen still true.
    ///
    /// <para>Break recipe: give <see cref="SpriteEditorSession.BlockWidth"/> an initializer of 2
    /// and this goes red, together with the flag-panel tests that assume one sprite.</para>
    /// </summary>
    [Fact]
    public void AFreshSheetHasExactlyOneCellInHand()
    {
        Harness harness = OpenSpriteEditor();

        Assert.Equal((0, 0, 1, 1), Block(harness));
        Assert.Equal(1, harness.Editor.RegionCells);
    }

    /// <summary>
    /// A drag across the sheet marks a rectangle of any size, and the drag the other way round
    /// marks the same one — the block is normalized out of its anchor, the rule the map's picker
    /// already carries. 3x2 rather than a square on purpose: a square would pass a width/height
    /// swap.
    ///
    /// <para>Break recipe: replace either <c>Math.Min</c> in
    /// <see cref="SpriteEditorView.UpdateTileBlock"/> with the anchor and the backwards drag
    /// reports a 1x1 block at the wrong sprite; swap the width and height arguments of
    /// <c>SelectRegionBlock</c> there and the 3x2 halves go red while the anchor still passes.</para>
    /// </summary>
    [Fact]
    public void ADragAcrossTheSheetMarksAnyRectangleEitherWayRound()
    {
        Harness forward = OpenSpriteEditor();
        forward.Drag(StripPoint(forward, 2, 1), StripPoint(forward, 4, 2));

        Assert.Equal((2, 1, 3, 2), Block(forward));
        Assert.Equal(18, forward.Editor.SpriteIndex);
        Assert.False(forward.View.TileBlockGestureActive);       // the release closed it

        Harness backward = OpenSpriteEditor();
        backward.Drag(StripPoint(backward, 4, 2), StripPoint(backward, 2, 1));

        Assert.Equal(Block(forward), Block(backward));
    }

    /// <summary>
    /// The keyboard reaches the same block (the input-parity law, M9 stage 2.5): Ctrl+Shift+Right
    /// and Ctrl+Shift+Down grow it a cell at a time and Ctrl+Shift+Left shrinks it, landing on
    /// the very rectangle the drag lands on. Same chord as the map's, for the same reason it was
    /// chosen there: Shift+arrows already steps the sprite itself, and a chord must not double as
    /// its bare key.
    ///
    /// <para>Break recipe: delete the <c>EditorBlockDx</c>/<c>EditorBlockDy</c> block from
    /// <see cref="SpriteEditorInput"/> and the keyed rectangle stays 1x1 while the dragged one
    /// still passes — which is exactly the mouse-only feature the parity law forbids.</para>
    /// </summary>
    [Fact]
    public void BothChannelsMarkTheSameSheetBlock()
    {
        Harness dragged = OpenSpriteEditor();
        dragged.Drag(StripPoint(dragged, 2, 1), StripPoint(dragged, 4, 2));

        Harness keyed = OpenSpriteEditor();
        keyed.Click(StripPoint(keyed, 2, 1).X, StripPoint(keyed, 2, 1).Y);
        Assert.Equal((2, 1, 1, 1), Block(keyed));
        keyed.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Right);
        keyed.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Right);
        keyed.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Down);

        Assert.Equal(Block(dragged), Block(keyed));

        keyed.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Left);
        Assert.Equal((2, 1, 2, 2), Block(keyed));
    }

    /// <summary>
    /// A marked block cannot go stale: the next single cell named by any channel puts the sheet
    /// back to one cell in hand. The map screen's own rule (<c>SelectSprite</c> resets its block
    /// to 1x1) — without it a rectangle marked once would silently widen every later flag click.
    ///
    /// <para>Break recipe: delete the two <c>BlockWidth</c>/<c>BlockHeight</c> assignments at the
    /// end of <see cref="SpriteEditorSession.SelectRegionCell"/> and both halves go red.</para>
    /// </summary>
    [Fact]
    public void NamingOneCellPutsTheBlockBackToOne()
    {
        Harness clicked = OpenSpriteEditor();
        clicked.Drag(StripPoint(clicked, 2, 1), StripPoint(clicked, 4, 3));
        Assert.Equal((2, 1, 3, 3), Block(clicked));

        (int x, int y) = StripPoint(clicked, 1, 0);
        clicked.Click(x, y);
        Assert.Equal((1, 0, 1, 1), Block(clicked));

        Harness stepped = OpenSpriteEditor();
        stepped.Drag(StripPoint(stepped, 2, 1), StripPoint(stepped, 4, 3));
        stepped.Tap(Keys.LeftShift, Keys.Right);        // the keyboard's single-cell step
        Assert.Equal(1, stepped.Editor.BlockWidth);
        Assert.Equal(1, stepped.Editor.BlockHeight);
    }

    /// <summary>
    /// What the block is FOR today: the flag row folds over it, which is TIC-80's own rule
    /// ("клик переключает флаг сразу у всех спрайтов текущего выделения", REFERENCES-EDITORS
    /// §2.1) applied to a rectangle instead of a square. One click, one undo step, three sprites.
    ///
    /// <para>Break recipe: change <see cref="SpriteEditorSession.ToggleRegionFlag"/> back to
    /// <c>WriteRegionFlags(RegionCells, ...)</c> and sprites 19 and 20 keep their flag clear
    /// while sprite 18 passes — the failure a square-only fold produces.</para>
    /// </summary>
    [Fact]
    public void TheFlagRowFoldsOverTheWholeMarkedBlock()
    {
        Harness harness = OpenSpriteEditor();
        harness.Drag(StripPoint(harness, 2, 1), StripPoint(harness, 4, 1));
        Assert.Equal((2, 1, 3, 1), Block(harness));

        harness.Editor.ToggleRegionFlag(0);

        foreach (int cell in new[] { 2, 3, 4 })
        {
            harness.Editor.SelectRegionCell(cell, 1);
            Assert.True(harness.Editor.IsFlagSet(0));
        }
        // One step, not three: the undo that follows a single click must put all three back.
        harness.Editor.Undo();
        foreach (int cell in new[] { 2, 3, 4 })
        {
            harness.Editor.SelectRegionCell(cell, 1);
            Assert.False(harness.Editor.IsFlagSet(0));
        }
    }

    /// <summary>
    /// The negative controls, three of them, because a rectangle is a shape with three ways to
    /// be wrong.
    ///
    /// <list type="number">
    ///   <item><b>It may not run off the sheet.</b> Ctrl+Shift+Right held past the edge stops at
    ///   the edge; a block whose cells had no sprites behind them would index the flag bank out
    ///   of range. Break recipe: drop the <c>Math.Clamp</c> on <c>width</c> in
    ///   <see cref="SpriteEditorSession.SelectRegionBlock"/>.</item>
    ///   <item><b>It may not be smaller than the canvas region.</b> At region size 2 the block is
    ///   2x2, and Ctrl+Shift+Left cannot shave it to 1 — otherwise the flag row would miss
    ///   sprites the author is drawing on. Break recipe: change that clamp's floor from
    ///   <c>RegionCells</c> to 1.</item>
    ///   <item><b>The bare chord is not the chord.</b> Shift+Right without Ctrl steps the sprite
    ///   and leaves the block at one cell — the rule that keeps a chord from doubling as its bare
    ///   key. Break recipe: drop the <c>!ctrl</c> guard on <c>EditorSheetDx</c> in
    ///   <see cref="ShellCommandReader"/> and the sprite steps on the sizing chord too.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void TheBlockStaysInsideTheSheetAndNeverSmallerThanTheRegion()
    {
        Harness edge = OpenSpriteEditor();
        (int x, int y) = StripPoint(edge, 6, 0);
        edge.Click(x, y);
        for (int i = 0; i < 40; i++)
        {
            edge.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Right);
            edge.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Down);
        }
        Assert.True(edge.Editor.RegionCellX + edge.Editor.BlockWidth <= 16);
        Assert.True(edge.Editor.RegionCellY + edge.Editor.BlockHeight <= 16);

        Harness zoomed = OpenSpriteEditor();
        zoomed.Editor.SelectRegionSize(2);
        Assert.Equal((0, 0, 2, 2), Block(zoomed));
        zoomed.Tap(Keys.LeftControl, Keys.LeftShift, Keys.Left);
        Assert.Equal((0, 0, 2, 2), Block(zoomed));

        Harness bare = OpenSpriteEditor();
        int before = bare.Editor.SpriteIndex;
        bare.Tap(Keys.LeftShift, Keys.Right);
        Assert.NotEqual(before, bare.Editor.SpriteIndex);
        Assert.Equal(1, bare.Editor.BlockWidth);
        Assert.Equal(1, bare.Editor.BlockHeight);
    }
}

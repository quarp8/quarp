using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The tests that could not exist before wave 3c: <b>whole frames of the real editor routers,
/// driven by real keys and a real mouse, with no window anywhere</b>.
///
/// <para><b>What changed and why it matters.</b> Until this wave the routing lived in
/// <c>QuarpGame.UpdateEditor</c> and <c>QuarpGame.UpdateMapEditor</c>, inside a class that
/// cannot be constructed without a <c>GraphicsDevice</c>. Everything reachable from those two
/// methods was therefore tested through <em>mirrors</em>: <see cref="MapEditorParityTests"/>
/// re-writes the map dispatch, <see cref="InputParityInstrumentTests"/> re-writes the canvas
/// gesture dispatch, <see cref="MapEditorModeTests"/> and
/// <see cref="EditorButtonContractTests"/> each re-write the press-kind dispatch. Every one of
/// those mirrors says so in its own comment, and every one of them is a second copy of a fact:
/// a mirror stays green when the thing it mirrors is deleted. Wave 3c moved the dispatch into
/// <see cref="SpriteEditorInput"/> and <see cref="MapEditorInput"/> and handed it the window's
/// size as two integers (<see cref="EditorShell"/>), so this file calls the production router
/// itself. Delete a branch from either <c>Update</c> and something here goes red — which is
/// precisely what no mirror can promise.</para>
///
/// <para><b>The one thing still mirrored, and it is two lines.</b> <see cref="Harness.Frame"/>
/// picks which router to call from <see cref="ShellModeMachine.Mode"/>, exactly as
/// <c>QuarpGame.Update</c>'s switch does. That mirror exists because the mode can change
/// <em>inside</em> a frame (a click on the exit tab, a Discard on the prompt) and the next
/// frame must land on the other screen; it consults the same single owner of "which mode is
/// on screen" the shell does, so it cannot drift about that. Nothing else here re-implements
/// a rule: the geometry is the production layouts, the edges are the production readers, and
/// every verb is the production session's.</para>
/// </summary>
public class EditorInputRouterTests : IDisposable
{
    private readonly string _root;

    public EditorInputRouterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-router-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A window size that never existed: the point is that the router only ever sees two ints.</summary>
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    /// <summary>One frame at 60 Hz — the routers spend it on the tooltip and long-press clocks only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    // ==================================================================================
    // The window, minus the window.
    // ==================================================================================

    /// <summary>
    /// What <c>QuarpGame</c> is to the routers, with the graphics device subtracted: the same
    /// four shell objects it owns, the same two production readers it polls, and a back buffer
    /// that is a pair of constants instead of a presentation parameter.
    /// </summary>
    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        /// <summary>Rebuilt per frame, like the window's — a resize would change the two numbers.</summary>
        internal EditorShell Context =>
            new(Modes, Flyout, Hover, SheetScroll, WindowWidth, WindowHeight);

        internal SpriteEditorLayout SpriteLayout =>
            SpriteEditorLayout.Compute(WindowWidth, WindowHeight, Modes.Editor!.RegionCells);

        internal MapEditorLayout MapLayout => MapEditorLayout.Compute(WindowWidth, WindowHeight);

        /// <summary>
        /// One whole frame through the production router for whichever editor is on screen.
        /// The two-line mode switch is the shell's own (see the type comment); everything it
        /// hands over — the edge-detected keys, the edge-detected mouse, the elapsed seconds —
        /// is what the window would have handed over.
        /// </summary>
        internal void Frame(Keys[] down, int mouseX, int mouseY, ButtonState left, ButtonState right)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, 0, left, ButtonState.Released, right,
                ButtonState.Released, ButtonState.Released));
            switch (Modes.Mode)
            {
                case ShellMode.Editor:
                    SpriteEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
                case ShellMode.MapEditor:
                    MapEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
            }
        }

        /// <summary>A frame with nothing held: the release half of every press below.</summary>
        internal void Idle() => Frame(NoKeys, Off, Off, ButtonState.Released, ButtonState.Released);

        /// <summary>One key down for one frame, pointer parked off-screen so no hit test can see it.</summary>
        internal void KeyDown(params Keys[] down) =>
            Frame(down, Off, Off, ButtonState.Released, ButtonState.Released);

        /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
        internal void Tap(params Keys[] down)
        {
            KeyDown(down);
            Idle();
        }

        internal void LeftDown(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Pressed, ButtonState.Released);

        internal void LeftUp(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released);

        internal void Click(int x, int y)
        {
            LeftDown(x, y);
            LeftUp(x, y);
        }

        internal void RightClick(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Pressed);
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released);
        }

        /// <summary>Far outside any rectangle either layout places — an idle pointer must hit nothing.</summary>
        private const int Off = -1000;
    }

    // ==================================================================================
    // Fixtures — the same shapes the neighbouring tests use, never a factory of my own.
    // ==================================================================================

    /// <summary>A mode machine standing in the sprite editor over a one-cart library of its own.</summary>
    private Harness OpenSpriteEditor(out string cartFolder)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        cartFolder = Path.Combine(root, "cart");
        Directory.CreateDirectory(cartFolder);
        File.WriteAllText(
            Path.Combine(cartFolder, "manifest.json"), "{\"name\":\"router\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        return new Harness(machine);
    }

    private Harness OpenMapEditor(out string cartFolder)
    {
        Harness harness = OpenSpriteEditor(out cartFolder);
        harness.Modes.SwitchEditorTab(ShellMode.MapEditor);
        Assert.Equal(ShellMode.MapEditor, harness.Modes.Mode);
        return harness;
    }

    private static (int X, int Y) Centre(Rectangle rect) =>
        (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>Region-local pixel → the window point that lands on its middle.</summary>
    private static (int X, int Y) CanvasPoint(in SpriteEditorLayout layout, int localX, int localY) =>
        (layout.Canvas.X + (localX * layout.CanvasScale) + (layout.CanvasScale / 2),
         layout.Canvas.Y + (localY * layout.CanvasScale) + (layout.CanvasScale / 2));

    // ==================================================================================
    // The mouse reaches the session — through the router, not through a mirror of it.
    // ==================================================================================

    /// <summary>
    /// A click on a tool button changes the tool the session is holding. Trivial to state and
    /// impossible to write before this wave: the press dispatch that decides "this point is a
    /// button, this button is neither stub nor group slot, therefore route it" lived where no
    /// test could call it, and <see cref="EditorButtonContractTests"/> had to re-write those
    /// three decisions to test what came after them.
    ///
    /// <para>Break recipe: delete the <c>ToolFill</c> case from
    /// <see cref="EditorIcons.ClickButton"/>, or make <see cref="SpriteEditorInput"/>'s press
    /// dispatch skip <c>HandleEditorButton</c> — either way the tool stays the pencil and the
    /// second assertion names it.</para>
    /// </summary>
    [Fact]
    public void AClickOnAToolButtonChangesTheToolTheSessionHolds()
    {
        Harness harness = OpenSpriteEditor(out _);
        SpriteEditorSession editor = harness.Modes.Editor!;
        Assert.Equal(SpriteEditorTool.Pencil, editor.Tool);

        (int x, int y) = Centre(harness.SpriteLayout.ButtonRect(EditorButton.ToolFill));
        harness.Click(x, y);

        Assert.Equal(SpriteEditorTool.Fill, editor.Tool);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);      // a tool click never leaves the screen
    }

    /// <summary>
    /// A click on a swatch and then a press-drag-release on the canvas paints, through the
    /// router's own press / drag / release ordering. This is the frame shape
    /// <see cref="InputParityInstrumentTests"/> mirrors; here it is the real one.
    ///
    /// <para>Break recipe: in <see cref="SpriteEditorInput"/>'s press chain, move the swatch
    /// branch after the canvas branch — the swatch click starts landing on nothing and the
    /// painted colour assertion goes red.</para>
    /// </summary>
    [Fact]
    public void TheMousePaintsThroughTheRouterAndTheDragKeepsPainting()
    {
        Harness harness = OpenSpriteEditor(out _);
        SpriteEditorSession editor = harness.Modes.Editor!;

        (int swatchX, int swatchY) = Centre(harness.SpriteLayout.SwatchRect(7));
        harness.Click(swatchX, swatchY);
        Assert.Equal(7, editor.CurrentColor);

        SpriteEditorLayout layout = harness.SpriteLayout;
        (int fromX, int fromY) = CanvasPoint(layout, 1, 1);
        (int toX, int toY) = CanvasPoint(layout, 3, 1);
        harness.LeftDown(fromX, fromY);
        harness.Frame(NoKeys, toX, toY, ButtonState.Pressed, ButtonState.Released);
        harness.LeftUp(toX, toY);

        Assert.Equal(7, editor.Pixels[(1 * CartData.GfxWidth) + 1]);
        Assert.Equal(7, editor.Pixels[(1 * CartData.GfxWidth) + 3]);
        Assert.True(editor.IsDirty);
        Assert.False(editor.StrokeActive);      // the release closed it: one stroke, one undo step
    }

    // ==================================================================================
    // Escape's three meanings, in the order the router gives them.
    // ==================================================================================

    /// <summary>
    /// The order's rule, now provable: <b>Esc under an open flyout closes the flyout and goes
    /// no further</b> — leaving the editor from under a flyout would punish exploration — and
    /// the Esc after that acts normally. Both halves matter: a router that ate the second Esc
    /// as well would trap the author in the editor, and one that ate neither would drop them
    /// out of it mid-exploration.
    ///
    /// <para>Break recipe: delete the <c>return;</c> from the <c>Flyout.OpenSlot is not null</c>
    /// branch of <see cref="SpriteEditorInput"/>'s <c>commands.Quit</c> block. The first Esc
    /// then closes the flyout AND leaves, and <c>StaysInTheEditor</c> below goes red while the
    /// flyout assertion still passes — which is exactly the bug shape.</para>
    /// </summary>
    [Fact]
    public void EscapeUnderAnOpenFlyoutClosesOnlyTheFlyoutAndTheNextEscapeLeaves()
    {
        Harness harness = OpenSpriteEditor(out _);

        (int x, int y) = Centre(harness.SpriteLayout.ButtonRect(EditorButton.ToolShape));
        harness.RightClick(x, y);       // the no-clock way into the flyout, next to the long press
        Assert.Equal(EditorButton.ToolShape, harness.Flyout.OpenSlot);

        harness.Tap(Keys.Escape);

        Assert.Null(harness.Flyout.OpenSlot);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);      // StaysInTheEditor
        Assert.NotNull(harness.Modes.Editor);

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);     // the next Esc acts further
        Assert.Null(harness.Modes.Editor);
    }

    /// <summary>
    /// The same rule one rung down (wave 2f): a live selection eats the next Esc — the mask
    /// drops and the editor stays. The selection here is made by the router's own canvas
    /// gesture, so what is being tested is the whole chain (press → BeginSelect, drag →
    /// UpdateSelect, release → CommitSelect, Esc → ClearSelection), not four session calls.
    ///
    /// <para>Break recipe: drop the <c>editor.HasSelection || editor.SelectionGestureActive</c>
    /// branch from <see cref="SpriteEditorInput"/>'s <c>commands.Quit</c> block — the mode
    /// assertion goes red and Esc starts throwing the author out of the editor with a
    /// selection still up.</para>
    /// </summary>
    [Fact]
    public void EscapeOverALiveSelectionDropsTheMaskAndStays()
    {
        Harness harness = OpenSpriteEditor(out _);
        SpriteEditorSession editor = harness.Modes.Editor!;
        editor.SelectTool(SpriteEditorTool.Select);

        SpriteEditorLayout layout = harness.SpriteLayout;
        (int fromX, int fromY) = CanvasPoint(layout, 2, 2);
        (int toX, int toY) = CanvasPoint(layout, 5, 5);
        harness.LeftDown(fromX, fromY);
        harness.Frame(NoKeys, toX, toY, ButtonState.Pressed, ButtonState.Released);
        harness.LeftUp(toX, toY);

        Assert.True(editor.HasSelection);
        Assert.False(editor.SelectionGestureActive);     // the release committed the mask
        Assert.False(editor.IsDirty);                    // marking a mask writes no pixels

        harness.Tap(Keys.Escape);

        Assert.False(editor.HasSelection);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        Assert.Same(editor, harness.Modes.Editor);

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
    }

    // ==================================================================================
    // The dirty exit, decided with the mouse.
    // ==================================================================================

    /// <summary>
    /// The prompt owns the screen, and its verbs are clickable: with unsaved pixels, Esc raises
    /// the question and a click on <c>X DISCARD</c> leaves — the disk untouched. Before this
    /// wave the prompt's click path existed in exactly one place (the windowed method) and had
    /// no test at all; only its keyboard twin was covered, through
    /// <see cref="ShellModeMachine"/> directly.
    ///
    /// <para>Break recipe: change the <c>EditorPromptVerb.Discard</c> case in
    /// <see cref="SpriteEditorInput"/>'s prompt block to fall into the default (<c>Stay</c>) —
    /// the mode stays Editor and the last two assertions go red, which is the shape of a
    /// prompt whose Discard button silently means "stay".</para>
    /// </summary>
    [Fact]
    public void OnTheExitPromptAClickOnDiscardLeavesTheEditor()
    {
        Harness harness = OpenSpriteEditor(out string folder);
        SpriteEditorSession editor = harness.Modes.Editor!;

        (int swatchX, int swatchY) = Centre(harness.SpriteLayout.SwatchRect(9));
        harness.Click(swatchX, swatchY);
        (int paintX, int paintY) = CanvasPoint(harness.SpriteLayout, 4, 4);
        harness.Click(paintX, paintY);
        Assert.True(editor.IsDirty);

        harness.Tap(Keys.Escape);

        Assert.True(editor.ExitPromptShown);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);      // the prompt is a question, not an exit

        (int discardX, int discardY) = Centre(
            harness.SpriteLayout.PromptVerbRect(EditorPromptVerb.Discard));
        harness.Click(discardX, discardY);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Null(harness.Modes.Editor);
        Assert.Empty(Directory.GetFiles(folder, "*.png"));      // discard writes nothing at all
    }

    // ==================================================================================
    // The other screen, through the other router.
    // ==================================================================================

    /// <summary>
    /// The map's whole mouse story in one frame sequence: pick a tile out of the picker, put it
    /// on a cell of the canvas, and read it back. <see cref="MapEditorParityTests"/> proves the
    /// two input channels agree; it does so through a re-written dispatch, because the real one
    /// was unreachable. This calls the real one.
    ///
    /// <para>Break recipe: swap the <c>TryTileCell</c> and <c>TryMapCell</c> branches in
    /// <see cref="MapEditorInput"/>'s press chain, or delete the <c>MapEditorPaint.Begin</c>
    /// call from it — the placed-tile assertion goes red.</para>
    /// </summary>
    [Fact]
    public void TheMapRouterPicksATileWithTheMouseAndPlacesIt()
    {
        Harness harness = OpenMapEditor(out _);
        MapEditorSession map = harness.Modes.MapEditor!;
        MapEditorView view = harness.Modes.MapView!;
        MapEditorLayout layout = harness.MapLayout;

        (int tileX, int tileY) = Centre(layout.TileCellRect(3));
        harness.Click(tileX, tileY);
        Assert.Equal(3, map.SelectedSprite);

        (int cellX, int cellY) = Centre(layout.MapCellRect(2, 1, view.CameraX, view.CameraY));
        harness.Click(cellX, cellY);

        Assert.Equal(3, map.TileAt(2, 1));
        Assert.True(map.IsDirty);
        Assert.False(map.StrokeActive);         // the release ended the gesture: one undo step
        Assert.Equal(ShellMode.MapEditor, harness.Modes.Mode);
    }

    /// <summary>
    /// The map's exit prompt, decided with the mouse — the twin of the sheet's, and unreachable
    /// for the same reason until this wave. Discard must leave <c>map.bin</c> uncreated: an
    /// absent file is a valid empty map (MAP-FORMAT §1), and writing one on the way out would
    /// be the quietest possible way to break that.
    ///
    /// <para>Break recipe: make <see cref="MapEditorInput"/>'s prompt block call
    /// <c>SaveMapAndClose</c> for <c>Discard</c> — the file assertion goes red while the mode
    /// assertion still passes.</para>
    /// </summary>
    [Fact]
    public void OnTheMapsExitPromptAClickOnDiscardLeavesAndWritesNothing()
    {
        Harness harness = OpenMapEditor(out string folder);
        MapEditorLayout layout = harness.MapLayout;
        MapEditorView view = harness.Modes.MapView!;

        (int tileX, int tileY) = Centre(layout.TileCellRect(5));
        harness.Click(tileX, tileY);
        (int cellX, int cellY) = Centre(layout.MapCellRect(0, 0, view.CameraX, view.CameraY));
        harness.Click(cellX, cellY);
        Assert.True(harness.Modes.MapEditor!.IsDirty);

        harness.Tap(Keys.Escape);

        Assert.True(view.ExitPromptShown);
        Assert.Equal(ShellMode.MapEditor, harness.Modes.Mode);

        (int discardX, int discardY) = Centre(layout.PromptVerbRect(EditorPromptVerb.Discard));
        harness.Click(discardX, discardY);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Null(harness.Modes.MapEditor);
        Assert.False(File.Exists(Path.Combine(folder, MapEditorSession.MapFileName)));
    }

    /// <summary>
    /// The tab strip's mouse half, end to end: a click on the tilemap tab moves the shell to
    /// the other screen, and the very next frame is routed by the other router — which is the
    /// one thing the harness's mode switch is there to make true, and the one thing a
    /// per-router test could not have shown.
    ///
    /// <para>Break recipe: make <c>HandleEditorButton</c> return false after
    /// <c>SwitchEditorTab</c> — the router keeps touching the sprite session for the rest of a
    /// frame that has already left the sprite screen; the tile assertion below is what catches
    /// it, because the following click would still be routed as a sprite-editor click.</para>
    /// </summary>
    [Fact]
    public void ClickingTheTilemapTabHandsTheNextFrameToTheMapRouter()
    {
        Harness harness = OpenSpriteEditor(out _);

        (int tabX, int tabY) = Centre(harness.SpriteLayout.ButtonRect(EditorButton.TilemapTab));
        harness.Click(tabX, tabY);

        Assert.Equal(ShellMode.MapEditor, harness.Modes.Mode);
        Assert.NotNull(harness.Modes.MapEditor);

        MapEditorLayout layout = harness.MapLayout;
        (int tileX, int tileY) = Centre(layout.TileCellRect(6));
        harness.Click(tileX, tileY);

        Assert.Equal(6, harness.Modes.MapEditor!.SelectedSprite);
    }
}

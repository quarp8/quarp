using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The map editor's toolbox (wave 3d): the four tools of TIC-80's <c>map->mode</c>, the three
/// mouse buttons, the pan modifier, the grid switch and <c>Del</c> over a marked rectangle —
/// each driven through the <b>production</b> router (<see cref="MapEditorInput.Update"/>) or the
/// production model (<see cref="MapEditorSession"/>), never a mirror of either.
///
/// <para><b>Why the harness looks like the neighbours'.</b> <see cref="EditorInputRouterTests"/>
/// built the "window, minus the window" shape first — the same four shell objects
/// <c>QuarpGame</c> owns, the same two production readers, a back buffer that is a pair of
/// constants — and this file repeats it rather than inventing a second way to feed a frame.
/// The one thing it adds is a middle mouse button, because this wave gives the middle button a
/// meaning (the tile eyedropper) and <see cref="EditorMouseReader"/> now reports it.</para>
///
/// <para>The fill and the delete are model tests, driven through
/// <see cref="MapEditorSession"/> alone the way <see cref="MapEditorSessionTests"/> drives
/// everything else about the map: what "connected run" means and what counts as one undo step
/// are the model's rules, and proving them through a window would prove them about the window.</para>
/// </summary>
public class MapEditorToolsTests : IDisposable
{
    /// <summary>
    /// The parked pointer, spelled once for the outer class too: <c>Harness</c> keeps its own
    /// copy, and a nested type's private member is out of the enclosing type's reach.
    /// </summary>
    private const int Off = -1000;

    private readonly string _root;

    public MapEditorToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-maptools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The console's own screen — the only surface a tool screen is laid out on since wave R3.</summary>
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    /// <summary>One frame at 60 Hz — the router spends it on the tooltip clock only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    // ==================================================================================
    // The window, minus the window — EditorInputRouterTests' shape, plus a middle button.
    // ==================================================================================

    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        /// <summary>The wheel's cumulative value, as MonoGame reports it — the reader owns the delta.</summary>
        private int _wheel;

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        internal MapEditorSession Map => Modes.MapEditor!;

        internal MapEditorView View => Modes.MapView!;

        internal MapEditorLayout Layout =>
            MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight, View.Overlay, Map.SelectedSprite);

        /// <summary>
        /// Rebuilt per frame, like the window's. Since wave R2 the two numbers are <b>the size
        /// of the surface the screen on show is laid out on</b>, and the sprite editor's surface
        /// is the console itself (ADR-029): 160x90, not the back buffer. <c>QuarpGame</c> makes
        /// exactly this switch — see <c>ConsoleEditorContext</c> — so a frame here means what a
        /// frame there means. The consequence for whoever writes a test against the sprite
        /// screen: <b>its mouse points are console pixels</b>, taken straight off the layout's
        /// own rectangles. Production reaches the same numbers by putting the window's point
        /// through <see cref="EditorMouse.ToConsole"/>, whose own arithmetic is pinned in
        /// <c>EditorMouseReaderTests</c> rather than re-run here.
        /// </summary>
        internal EditorShell Context =>
            new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight);


        /// <summary>
        /// Wave R3: the tile palette is an overlay now (ADR-029's arithmetic —
        /// <see cref="MapEditorLayout"/>'s type comment), so a test that wants to click a tile
        /// has to raise it first, and lower it again before touching the map underneath.
        /// Latched rather than held, because a latch survives the two frames a click costs while
        /// a held Shift would have to be re-asserted in every one of them.
        /// </summary>
        internal void PickTile(int sprite)
        {
            if (View.Overlay != MapEditorOverlay.Tiles)
            {
                View.ToggleTiles();
            }
            Rectangle cell = Layout.TileCellRect(sprite);
            Click(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
            View.CloseOverlay();
        }

        /// <summary>One whole frame through the production map router — the mode is never anything else here.</summary>
        internal void Frame(
            Keys[] down, int mouseX, int mouseY, ButtonState left, ButtonState middle, ButtonState right)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, 0, left, middle, right, ButtonState.Released, ButtonState.Released));
            MapEditorInput.Update(Context, commands, mouse, FrameSeconds);
        }

        /// <summary>
        /// One frame with the wheel turned by <paramref name="delta"/> detents at a point. The
        /// value handed to <see cref="MouseState"/> is cumulative, like MonoGame's, so the
        /// production <see cref="EditorMouseReader"/> does the differencing here exactly as it
        /// does in the window.
        /// </summary>
        internal void Wheel(int x, int y, int delta)
        {
            _wheel += delta;
            ShellCommands commands = _keys.Read(new KeyboardState(NoKeys));
            EditorMouse mouse = _pointer.Read(new MouseState(
                x, y, _wheel, ButtonState.Released, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
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

        internal void LeftUp(int x, int y, params Keys[] down) =>
            Frame(down, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        internal void Click(int x, int y)
        {
            LeftDown(x, y);
            LeftUp(x, y);
        }

        internal void RightClick(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Pressed);
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }

        internal void MiddleClick(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Pressed, ButtonState.Released);
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);
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
            Path.Combine(folder, "manifest.json"), "{\"name\":\"tools\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();
        machine.SwitchEditorTab(ShellMode.MapEditor);
        Assert.Equal(ShellMode.MapEditor, machine.Mode);
        return new Harness(machine);
    }

    /// <summary>A bare session on an empty cart folder — the model tests' fixture, MapEditorSessionTests' own.</summary>
    private MapEditorSession BareSession()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return new MapEditorSession(folder);
    }

    private static (int X, int Y) Centre(Rectangle rect) =>
        (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>The window point in the middle of a map cell, at the camera the view stands at.</summary>
    private static (int X, int Y) CellPoint(Harness harness, int cellX, int cellY) =>
        Centre(harness.Layout.MapCellRect(cellX, cellY, harness.View.CameraX, harness.View.CameraY));

    private static void Stroke(MapEditorSession map, int x, int y, int tile)
    {
        map.SelectSprite(tile);
        map.BeginStroke();
        map.PaintTile(x, y);
        map.EndStroke();
    }

    // ==================================================================================
    // 1. Four tools, two channels.
    // ==================================================================================

    /// <summary>
    /// Every one of the four tools is reachable by clicking its button and by pressing its
    /// digit, and both land on the same value of <see cref="MapEditorView.Tool"/> — the parity
    /// law applied to the thing this wave adds. The digit table is TIC-80's own numbering
    /// (REFERENCES-EDITORS §3.1: 1 draw, 2 drag map, 3 select, 4 fill).
    ///
    /// <para>Break recipe: swap two entries in <see cref="EditorIcons.MapButtonForDigit"/> and
    /// the keyboard half goes red naming both tools; drop an entry from
    /// <see cref="EditorIcons.MapToolOf"/> and the mouse half goes red for that one button.</para>
    /// </summary>
    [Theory]
    [InlineData(1, EditorButton.ToolPencil, MapEditorTool.Pencil)]
    [InlineData(2, EditorButton.ToolHand, MapEditorTool.Hand)]
    [InlineData(3, EditorButton.ToolSelect, MapEditorTool.Select)]
    [InlineData(4, EditorButton.ToolFill, MapEditorTool.Fill)]
    public void EachToolIsChosenByItsButtonAndByItsDigit(int digit, EditorButton button, MapEditorTool tool)
    {
        Harness clicked = OpenMapEditor();
        Assert.Equal(MapEditorTool.Pencil, clicked.View.Tool);      // TIC-80 opens on DRAW; so do we
        (int x, int y) = Centre(clicked.Layout.ButtonRect(button));
        clicked.Click(x, y);
        Assert.Equal(tool, clicked.View.Tool);

        Harness keyed = OpenMapEditor();
        keyed.Tap(Keys.D1 + (digit - 1));
        Assert.Equal(tool, keyed.View.Tool);

        // And the two channels agree, which is the whole point of asking twice.
        Assert.Equal(clicked.View.Tool, keyed.View.Tool);
    }

    /// <summary>
    /// The sprite editor's digits 5 and 6 (shapes, transforms) name tools this screen does not
    /// have, and must do nothing here rather than silently landing on a map tool.
    /// Break recipe: give <see cref="EditorIcons.MapButtonForDigit"/> a 5 or 6 case.
    /// </summary>
    [Fact]
    public void DigitsBeyondTheFourMapToolsDoNothing()
    {
        Harness harness = OpenMapEditor();
        harness.Tap(Keys.D4);
        Assert.Equal(MapEditorTool.Fill, harness.View.Tool);

        harness.Tap(Keys.D5);
        harness.Tap(Keys.D6);

        Assert.Equal(MapEditorTool.Fill, harness.View.Tool);
        Assert.Null(EditorIcons.MapButtonForDigit(5));
        Assert.Null(EditorIcons.MapButtonForDigit(6));
    }

    // ==================================================================================
    // 2. Fill.
    // ==================================================================================

    /// <summary>
    /// The fill changes exactly the connected run of the value it started on, and stops at
    /// every neighbour that holds something else — the property that separates a flood fill
    /// from "replace this value everywhere". The fixture is a wall: column 4 is painted with
    /// tile 9 from top to bottom, so the map is cut into a left half and a right half of zeros
    /// that touch nowhere, and a fill started on the left must leave the right alone.
    ///
    /// <para>Break recipe: drop the <c>_map[offset - 1] == target</c> guard from any one
    /// direction of <c>MapEditorSession.FloodFill</c> — the fill then overwrites the wall and
    /// pours into the far side, and both the wall assertion and the far-side one go red.
    /// Replace the flood with "set every cell holding <c>target</c>" (TIC-80's <c>Ctrl</c>
    /// variant, which we did not build) and the far-side assertion alone goes red, which is
    /// exactly the difference between the two operations.</para>
    /// </summary>
    [Fact]
    public void FillChangesTheConnectedRunAndStopsAtEveryOtherValue()
    {
        MapEditorSession map = BareSession();
        map.SelectSprite(9);
        map.BeginStroke();
        for (int y = 0; y < MapEditorSession.MapRows; y++)
        {
            map.PaintTile(4, y);
        }
        map.EndStroke();

        map.Fill(0, 0, 3);

        // The left half — columns 0..3, every row — is now tile 3, and nothing else is.
        for (int y = 0; y < MapEditorSession.MapRows; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(3, map.TileAt(x, y));
            }
            Assert.Equal(9, map.TileAt(4, y));           // the wall itself is untouched
            Assert.Equal(0, map.TileAt(5, y));           // and the far side never heard of it
            Assert.Equal(0, map.TileAt(MapEditorSession.MapColumns - 1, y));
        }
    }

    /// <summary>
    /// The stack question, asked the only way that answers it: a map of one value, filled with
    /// another, must change all 18 432 cells and come back. Recursion dies here (that is the
    /// depth TIC-80 sizes <c>FILL_STACK_SIZE</c> against), and a stack sized by hope overruns
    /// and throws.
    ///
    /// <para>Break recipe: size <c>FloodFill</c>'s stack at anything below
    /// <see cref="MapEditorSession.MapPayloadSize"/> — this throws IndexOutOfRange; rewrite it
    /// as a recursive helper and it dies with a StackOverflow the runner cannot even catch,
    /// which is exactly why the order forbade recursion.</para>
    /// </summary>
    [Fact]
    public void FillingAUniformMapTouchesEveryCellAndDoesNotOverrunItsStack()
    {
        MapEditorSession map = BareSession();

        map.Fill(0, 0, 7);

        int filled = 0;
        foreach (byte tile in map.Map)
        {
            if (tile == 7)
            {
                filled++;
            }
        }
        Assert.Equal(MapEditorSession.MapPayloadSize, filled);
        Assert.True(map.CanUndo);

        map.Undo();
        foreach (byte tile in map.Map)
        {
            Assert.Equal(0, tile);
        }
    }

    /// <summary>
    /// The whole fill is <b>one</b> undo step however many cells it crossed — the same rule a
    /// pencil gesture carries. Break recipe: move the <c>BeginStroke</c>/<c>EndStroke</c> pair
    /// inside <c>FloodFill</c>'s loop and undo starts walking back one cell at a time.
    /// </summary>
    [Fact]
    public void AWholeFillIsOneUndoStep()
    {
        MapEditorSession map = BareSession();
        Stroke(map, 0, 0, 5);                    // one step, so there is a floor to undo down to

        map.Fill(10, 10, 2);
        Assert.Equal(2, map.TileAt(10, 10));

        map.Undo();
        Assert.Equal(0, map.TileAt(10, 10));
        Assert.Equal(5, map.TileAt(0, 0));       // the stroke before it survived: exactly one step came back
        Assert.True(map.CanUndo);
    }

    /// <summary>
    /// Filling with the tile that is already there is not a change: no undo step, no dirt, no
    /// version bump. A mis-click on an already-green field must not make Ctrl+Z look dead —
    /// the same promise <c>EndStroke</c> makes for an idle pencil click.
    ///
    /// <para>Break recipe: delete the <c>if (target == replacement) return;</c> guard from
    /// <see cref="MapEditorSession.Fill"/> — the fill then opens and closes a stroke that
    /// changed nothing, and while <c>EndStroke</c> still refuses the empty step, the
    /// <c>Version</c> assertion catches the wasted whole-map walk.</para>
    /// </summary>
    [Fact]
    public void FillingWithTheTileAlreadyThereIsNotAStepAndNotDirt()
    {
        MapEditorSession map = BareSession();
        map.Fill(0, 0, 4);                       // the map is now all 4s: one step, dirty
        int version = map.Version;
        map.Save();
        Assert.False(map.IsDirty);

        map.Fill(120, 40, 4);

        Assert.False(map.IsDirty);
        Assert.Equal(version, map.Version);
        map.Undo();                              // the ONE step is the first fill, not a second one
        Assert.False(map.CanUndo);
    }

    /// <summary>
    /// The fill's mouse and keyboard halves reach the same cells — the tool selected either
    /// way, the click and the Z press landing on the same run. Break recipe: delete the
    /// <c>MapEditorTool.Fill</c> case from <c>MapEditorInput</c>'s <c>KeyboardAct</c> and the
    /// keyed run stops filling while the clicked one still does.
    /// </summary>
    [Fact]
    public void BothChannelsFillTheSameArea()
    {
        Harness clicked = OpenMapEditor();
        (int fillButtonX, int fillButtonY) = Centre(clicked.Layout.ButtonRect(EditorButton.ToolFill));
        clicked.Click(fillButtonX, fillButtonY);
        clicked.PickTile(6);
        (int cellX, int cellY) = CellPoint(clicked, 3, 2);
        clicked.Click(cellX, cellY);

        Harness keyed = OpenMapEditor();
        keyed.Tap(Keys.D4);
        for (int i = 0; i < 6; i++)
        {
            keyed.Tap(Keys.LeftShift, Keys.Right);
        }
        Assert.Equal(6, keyed.Map.SelectedSprite);
        keyed.Tap(Keys.Z);

        Assert.Equal(MapEditorTool.Fill, clicked.View.Tool);
        Assert.True(clicked.Map.Map.SequenceEqual(keyed.Map.Map));
        Assert.Equal(6, clicked.Map.TileAt(0, 0));
        Assert.Equal(6, clicked.Map.TileAt(MapEditorSession.MapColumns - 1, MapEditorSession.MapRows - 1));
    }

    // ==================================================================================
    // 3. The three mouse buttons.
    // ==================================================================================

    /// <summary>
    /// The right button puts tile 0 exactly where the left button puts the selected tile —
    /// LIKO-12's <c>tile.lua</c> rule (REFERENCES-EDITORS §7.3), and the map's only eraser.
    /// The picker is left alone by it, which is the half a "select tile 0 on right-click"
    /// implementation would get wrong.
    ///
    /// <para>Break recipe: change <c>MapEditorSession.EmptyTile</c> to
    /// <c>map.SelectedSprite</c> in <c>MapEditorInput</c>'s right-button block — the cleared
    /// cell assertion goes red; make the block call <c>SelectSprite(0)</c> instead and the
    /// "the picker survives" assertion goes red while the first still passes.</para>
    /// </summary>
    [Fact]
    public void TheRightButtonPutsTileZeroWhereTheLeftPutsTheSelectedTile()
    {
        Harness harness = OpenMapEditor();
        harness.PickTile(11);

        (int cellX, int cellY) = CellPoint(harness, 7, 3);
        harness.Click(cellX, cellY);
        Assert.Equal(11, harness.Map.TileAt(7, 3));

        harness.RightClick(cellX, cellY);

        Assert.Equal(0, harness.Map.TileAt(7, 3));
        Assert.Equal(11, harness.Map.SelectedSprite);       // the picker survives the erase
        Assert.False(harness.Map.StrokeActive);             // the release closed the erase gesture
    }

    /// <summary>
    /// The middle button takes the tile under the cursor into the picker — TIC-80's
    /// <c>processMouseDrawMode</c>. Break recipe: delete the <c>mouse.MiddlePressed</c> block
    /// from <c>MapEditorInput</c>, or drop <c>MiddlePressed</c> from
    /// <see cref="EditorMouseReader"/>, and the picker stays where it was.
    /// </summary>
    [Fact]
    public void TheMiddleButtonTakesTheTileUnderTheCursor()
    {
        Harness harness = OpenMapEditor();
        harness.PickTile(19);
        (int cellX, int cellY) = CellPoint(harness, 2, 4);
        harness.Click(cellX, cellY);
        int version = harness.Map.Version;

        // Wave R3 shrank the visible canvas to 17x8 cells (ADR-029): a point outside it is
        // no longer a map click at all, so the cells these paths use had to come inside.
        (int emptyX, int emptyY) = CellPoint(harness, 9, 6);
        harness.MiddleClick(emptyX, emptyY);
        Assert.Equal(0, harness.Map.SelectedSprite);        // an empty cell reads back as tile 0

        harness.MiddleClick(cellX, cellY);

        Assert.Equal(19, harness.Map.SelectedSprite);
        Assert.Equal(version, harness.Map.Version);         // sampling writes nothing
        Assert.False(harness.Map.StrokeActive);             // and opens no gesture
    }

    // ==================================================================================
    // 4. Panning.
    // ==================================================================================

    /// <summary>
    /// Space plus a left drag moves the camera and paints nothing — TIC-80's <c>Space</c>
    /// modifier, and the reason the map screen's keyboard pencil is bare Z: Space cannot both
    /// modify the drag and open a stroke. The map is deliberately given a non-zero selected
    /// tile first, so a stray paint would be visible rather than hidden by an all-zero map
    /// painting zeros over zeros.
    ///
    /// <para>Break recipe: drop the <c>!panning</c> guard from <c>MapEditorInput</c>'s
    /// <c>EditorPaintPressed</c> line and the "nothing was painted" assertions go red while the
    /// camera still moves; drop the <c>panning ||</c> from <c>MousePressOnCanvas</c> and the
    /// camera assertion goes red while the map stays clean, because the press then paints.</para>
    /// </summary>
    [Fact]
    public void SpaceAndALeftDragPanTheViewAndPaintNothing()
    {
        Harness harness = OpenMapEditor();
        harness.PickTile(13);
        Assert.Equal(0, harness.View.CameraX);

        MapEditorLayout layout = harness.Layout;
        // Wave R3 shrank the visible canvas to 17x8 cells (ADR-029): a point outside it is
        // no longer a map click at all, so the cells these paths use had to come inside.
        (int fromX, int fromY) = CellPoint(harness, 14, 6);
        int cell = layout.MapCell;
        harness.LeftDown(fromX, fromY, Keys.Space);
        // Drag six cells left and two up: the grabbed cell follows the pointer, so the camera
        // goes the other way by the same amount.
        harness.Frame(
            new[] { Keys.Space }, fromX - 6 * cell, fromY - 2 * cell,
            ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
        harness.LeftUp(fromX - 6 * cell, fromY - 2 * cell, Keys.Space);

        Assert.Equal(6, harness.View.CameraX);
        Assert.Equal(2, harness.View.CameraY);
        Assert.False(harness.Map.IsDirty);
        Assert.False(harness.Map.CanUndo);
        Assert.Equal(13, harness.Map.SelectedSprite);
    }

    /// <summary>
    /// The hand tool does the same without a modifier (TIC-80 <c>MAP_DRAG_MODE</c>), and — the
    /// half that matters — a left drag under it does not paint either. Break recipe: delete the
    /// <c>view.Tool == MapEditorTool.Hand</c> clause from <c>MousePressOnCanvas</c>: the drag
    /// starts painting a line across the map and the dirt assertion names it.
    /// </summary>
    [Fact]
    public void TheHandToolPansOnAPlainLeftDrag()
    {
        Harness harness = OpenMapEditor();
        harness.PickTile(2);
        (int handX, int handY) = Centre(harness.Layout.ButtonRect(EditorButton.ToolHand));
        harness.Click(handX, handY);

        int cell = harness.Layout.MapCell;
        // Wave R3 shrank the visible canvas to 17x8 cells (ADR-029): a point outside it is
        // no longer a map click at all, so the cells these paths use had to come inside.
        (int fromX, int fromY) = CellPoint(harness, 12, 5);
        harness.LeftDown(fromX, fromY);
        harness.Frame(
            NoKeys, fromX - 4 * cell, fromY, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
        harness.LeftUp(fromX - 4 * cell, fromY);

        Assert.Equal(4, harness.View.CameraX);
        Assert.False(harness.Map.IsDirty);
        Assert.False(harness.View.PanActive);       // the release closed the gesture
    }

    // ==================================================================================
    // 5. Select and Delete.
    // ==================================================================================

    /// <summary>
    /// A left drag under the select tool marks a rectangle — shown, not acted on — and
    /// <c>Del</c> then empties exactly it, in one undo step (TIC-80's <c>deleteSelection</c>).
    /// The map is painted solid first so "exactly it" has something to be exact about.
    ///
    /// <para>Break recipe: change <c>MapEditorSession.ClearArea</c>'s inner bound to
    /// <c>&lt;=</c> and the neighbour-cell assertion goes red; move its <c>EndStroke</c> inside
    /// the loop and the single-undo assertion goes red.</para>
    /// </summary>
    [Fact]
    public void DeleteEmptiesTheMarkedRectangleInOneUndoStep()
    {
        Harness harness = OpenMapEditor();
        harness.PickTile(8);
        harness.Tap(Keys.D4);                       // fill, so the whole map is tile 8 in one step
        (int anyCellX, int anyCellY) = CellPoint(harness, 0, 0);
        harness.Click(anyCellX, anyCellY);
        Assert.Equal(8, harness.Map.TileAt(30, 9));

        harness.Tap(Keys.D3);                       // select
        (int fromX, int fromY) = CellPoint(harness, 2, 1);
        (int toX, int toY) = CellPoint(harness, 4, 3);
        harness.LeftDown(fromX, fromY);
        harness.Frame(NoKeys, toX, toY, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
        harness.LeftUp(toX, toY);

        Assert.True(harness.View.HasSelection);
        Assert.False(harness.View.SelectionGestureActive);
        Assert.Equal((2, 1, 3, 3),
            (harness.View.SelectionX, harness.View.SelectionY,
             harness.View.SelectionWidth, harness.View.SelectionHeight));
        Assert.Equal(8, harness.Map.TileAt(3, 2));      // marking writes nothing

        harness.Tap(Keys.Delete);

        for (int y = 1; y <= 3; y++)
        {
            for (int x = 2; x <= 4; x++)
            {
                Assert.Equal(0, harness.Map.TileAt(x, y));
            }
        }
        Assert.Equal(8, harness.Map.TileAt(1, 1));      // one column left of the mark
        Assert.Equal(8, harness.Map.TileAt(5, 3));      // one column right
        Assert.Equal(8, harness.Map.TileAt(2, 0));      // one row above
        Assert.Equal(8, harness.Map.TileAt(4, 4));      // one row below

        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.Equal(8, harness.Map.TileAt(3, 2));      // one step brought the whole rectangle back
    }

    /// <summary>
    /// A drag up and to the left marks the same rectangle as the same drag the other way —
    /// the normalization every selection needs. Break recipe: replace
    /// <see cref="MapEditorView.UpdateSelection"/>'s <c>Math.Min</c> with the anchor and the
    /// backwards drag reports a width of 1 (or a negative one).
    /// </summary>
    [Fact]
    public void ABackwardsDragMarksTheSameRectangle()
    {
        Harness harness = OpenMapEditor();
        harness.Tap(Keys.D3);
        (int fromX, int fromY) = CellPoint(harness, 9, 6);
        (int toX, int toY) = CellPoint(harness, 6, 4);
        harness.LeftDown(fromX, fromY);
        harness.Frame(NoKeys, toX, toY, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
        harness.LeftUp(toX, toY);

        Assert.Equal((6, 4, 4, 3),
            (harness.View.SelectionX, harness.View.SelectionY,
             harness.View.SelectionWidth, harness.View.SelectionHeight));
    }

    /// <summary>
    /// The selection has a keyboard path too, which the parity law makes non-optional: digit 3
    /// arms the tool, held Z plus the arrows drags the rectangle, and the release commits it —
    /// the same rectangle the mouse drag of the test above marks. Both runs then press Del and
    /// their maps come out byte-identical, which is the proof that does not compare two
    /// coordinate calculations.
    ///
    /// <para>Break recipe: delete the <c>MapEditorTool.Select</c> branch from the held-key
    /// block in <c>MapEditorInput</c> (the one beside <c>MapEditorPaint.Continue</c>) — the
    /// keyed rectangle collapses to one cell and the byte comparison names it.</para>
    /// </summary>
    [Fact]
    public void BothChannelsMarkAndEmptyTheSameRectangle()
    {
        Harness keyed = OpenMapEditor();
        FillWholeMapWithTileEight(keyed);
        keyed.Tap(Keys.D3);
        // The cursor opens at (0,0); walk it to (2,1) before marking, so the two runs start
        // from the same cell.
        keyed.Tap(Keys.Right);
        keyed.Tap(Keys.Right);
        keyed.Tap(Keys.Down);
        Assert.Equal((2, 1), (keyed.View.CursorX, keyed.View.CursorY));
        keyed.Frame(new[] { Keys.Z }, -1000, -1000, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(
            new[] { Keys.Z, Keys.Right }, -1000, -1000,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(new[] { Keys.Z }, -1000, -1000, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(
            new[] { Keys.Z, Keys.Right }, -1000, -1000,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(new[] { Keys.Z }, -1000, -1000, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(
            new[] { Keys.Z, Keys.Down }, -1000, -1000,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(new[] { Keys.Z }, -1000, -1000, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Frame(
            new[] { Keys.Z, Keys.Down }, -1000, -1000,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);
        keyed.Idle();       // Z up: the mark commits

        Assert.Equal((2, 1, 3, 3),
            (keyed.View.SelectionX, keyed.View.SelectionY,
             keyed.View.SelectionWidth, keyed.View.SelectionHeight));

        Harness clicked = OpenMapEditor();
        FillWholeMapWithTileEight(clicked);
        clicked.Tap(Keys.D3);
        (int fromX, int fromY) = CellPoint(clicked, 2, 1);
        (int toX, int toY) = CellPoint(clicked, 4, 3);
        clicked.LeftDown(fromX, fromY);
        clicked.Frame(NoKeys, toX, toY, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
        clicked.LeftUp(toX, toY);

        keyed.Tap(Keys.Delete);
        clicked.Tap(Keys.Delete);

        Assert.True(keyed.Map.Map.SequenceEqual(clicked.Map.Map));
        Assert.Equal(0, keyed.Map.TileAt(3, 2));
        Assert.Equal(8, keyed.Map.TileAt(5, 2));
    }

    /// <summary>The two runs' shared ground: one fill, one undo step, every cell tile 8.</summary>
    private static void FillWholeMapWithTileEight(Harness harness)
    {
        harness.PickTile(8);
        harness.Tap(Keys.D4);
        (int cellX, int cellY) = CellPoint(harness, 0, 0);
        harness.Click(cellX, cellY);
        Assert.Equal(8, harness.Map.TileAt(30, 9));
    }

    /// <summary>
    /// With nothing marked, <c>Del</c> keeps its older meaning — select tile 0, the empty-tile
    /// button's keyboard twin — and touches no cell. Break recipe: drop the
    /// <c>view.HasSelection</c> test from <c>MapEditorInput</c>'s <c>EditorClear</c> block and
    /// one of the two halves of this stops being true.
    /// </summary>
    [Fact]
    public void DeleteWithNothingMarkedSelectsTileZeroAndChangesNoCell()
    {
        Harness harness = OpenMapEditor();
        harness.PickTile(21);
        (int cellX, int cellY) = CellPoint(harness, 5, 5);
        harness.Click(cellX, cellY);
        int version = harness.Map.Version;

        harness.Tap(Keys.Delete);

        Assert.False(harness.View.HasSelection);
        Assert.Equal(0, harness.Map.SelectedSprite);
        Assert.Equal(version, harness.Map.Version);
        Assert.Equal(21, harness.Map.TileAt(5, 5));
    }

    // ==================================================================================
    // 6. The grid switch.
    // ==================================================================================

    /// <summary>
    /// The grid is on when the screen opens (TIC-80's default) and the switch moves from the
    /// backtick key and from the button alike, into the same one bit.
    ///
    /// <para>Break recipe: delete the <c>EditorGridToggle</c> line from
    /// <see cref="ShellCommandReader"/> and the key half goes red; delete the
    /// <c>GridToggle</c> case from <see cref="EditorIcons.ClickMapButton"/> and the button half
    /// does; change <see cref="MapEditorView.GridShown"/>'s initializer to false and the first
    /// assertion names it.</para>
    /// </summary>
    [Fact]
    public void TheGridSwitchStartsOnAndMovesFromTheKeyAndTheButton()
    {
        Harness harness = OpenMapEditor();
        Assert.True(harness.View.GridShown);

        harness.Tap(Keys.OemTilde);
        Assert.False(harness.View.GridShown);

        harness.Tap(Keys.OemTilde);
        Assert.True(harness.View.GridShown);

        (int x, int y) = Centre(harness.Layout.ButtonRect(EditorButton.GridToggle));
        harness.Click(x, y);
        Assert.False(harness.View.GridShown);

        harness.Click(x, y);
        Assert.True(harness.View.GridShown);
    }

    // ==================================================================================
    // 7. The eraser button, made honest.
    // ==================================================================================

    /// <summary>
    /// The eraser button selects tile 0 — it is not a mode and never was one
    /// (REFERENCES-EDITORS §7.3: none of the three references has an eraser tool). What this
    /// wave changed is the story around it, not the wiring: the tooltip now says what the click
    /// does, and the pencil stays the tool in hand.
    ///
    /// <para>Break recipe: change <c>SelectSprite(MapEditorSession.EmptyTile)</c> in
    /// <see cref="EditorIcons.ClickMapButton"/> to any other number — the first assertion goes
    /// red; make it also switch the tool and the last one does.</para>
    /// </summary>
    [Fact]
    public void TheEraserButtonSelectsTileZeroAndLeavesTheToolAlone()
    {
        Harness harness = OpenMapEditor();
        harness.PickTile(30);
        Assert.Equal(30, harness.Map.SelectedSprite);
        (int cellX, int cellY) = CellPoint(harness, 1, 1);
        harness.Click(cellX, cellY);
        Assert.Equal(30, harness.Map.TileAt(1, 1));

        (int x, int y) = Centre(harness.Layout.ButtonRect(EditorButton.ToolEraser));
        harness.Click(x, y);

        Assert.Equal(MapEditorSession.EmptyTile, harness.Map.SelectedSprite);
        Assert.Equal(MapEditorTool.Pencil, harness.View.Tool);      // it is a tile, not a mode
        Assert.Contains("TILE 000", EditorIcons.MapTooltip(EditorButton.ToolEraser), StringComparison.Ordinal);

        // And it really erases: the pencil now stamps emptiness over what was there.
        harness.Click(cellX, cellY);
        Assert.Equal(0, harness.Map.TileAt(1, 1));
    }

    // ==================================================================================
    // 7b. Wave R3's two overlays: the tile palette and the whole-map view.
    // ==================================================================================

    /// <summary>
    /// <b>The palette is reachable from both channels and blocks the map while it is up.</b>
    /// Holding Shift raises it (TIC-80's "SHOW TILES [shift]"), its button latches it, and while
    /// it stands a click in the middle of the viewport paints nothing — which is the whole point
    /// of the overlay having its own rectangle rather than the map staying live underneath it.
    ///
    /// <para>Break recipe: (a) drop the <c>view.SetTilesHeld</c> line from
    /// <see cref="MapEditorInput"/> and the held case goes red while the latched one passes,
    /// naming the channel that broke; (b) drop the <c>CanvasLive</c> test from
    /// <see cref="MapEditorLayout.TryMapCell"/> and the "paints nothing" assertion goes red — a
    /// stroke landing on a map the author cannot see.</para>
    /// </summary>
    [Fact]
    public void ShiftHoldsThePaletteOpenAndItsButtonLatchesIt()
    {
        Harness harness = OpenMapEditor();
        Assert.Equal(MapEditorOverlay.None, harness.View.Overlay);

        // Channel A: the key. A frame with Shift down raises it; the frame after it falls.
        harness.Frame(
            new[] { Keys.LeftShift }, Off, Off,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Assert.Equal(MapEditorOverlay.Tiles, harness.View.Overlay);
        harness.Idle();
        Assert.Equal(MapEditorOverlay.None, harness.View.Overlay);

        // Channel B: the button. It latches, so it survives frames with nothing held.
        (int x, int y) = Centre(harness.Layout.ButtonRect(EditorButton.TilesToggle));
        harness.Click(x, y);
        Assert.True(harness.View.TilesLatched);
        harness.Idle();
        Assert.Equal(MapEditorOverlay.Tiles, harness.View.Overlay);

        // And the map underneath is deaf: a press in the middle of the viewport writes nothing.
        // The point is a strip of viewport the palette does NOT cover, so this is the map's own
        // pixel being asked and refused — not the palette swallowing the click.
        MapEditorLayout working = MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        Assert.False(harness.Layout.Sheet.Contains(working.Canvas.X + 1, working.Canvas.Y + 32));
        harness.Click(working.Canvas.X + 1, working.Canvas.Y + 32);
        Assert.False(harness.Map.IsDirty);
        Assert.False(harness.Map.CanUndo);

        // Esc puts it away before it means anything else — and the map is live again.
        harness.Tap(Keys.Escape);
        Assert.Equal(MapEditorOverlay.None, harness.View.Overlay);
        Assert.Equal(ShellMode.MapEditor, harness.Modes.Mode);
    }

    /// <summary>
    /// <b>The whole-map view is reachable from both channels, travels, and hides the viewport.</b>
    /// Tab is TIC-80's key for it (<c>processKeyboard</c>: "Tab — WORLD MODE") and the button is
    /// its mouse half; a click on the thumbnail is "take me there", through the very
    /// <see cref="MapEditorView.JumpTo"/> the position bar uses.
    ///
    /// <para>Break recipe: (a) delete the <c>EditorRegionCycle</c> arm from
    /// <see cref="MapEditorInput"/> and the key half goes red while the button half passes;
    /// (b) give <see cref="MapEditorLayout.Minimap"/> its rectangle unconditionally and the
    /// "deaf while down" assertion goes red — a thumbnail nobody can see would still be
    /// swallowing clicks aimed at the map.</para>
    /// </summary>
    [Fact]
    public void TabAndItsButtonOpenTheWholeMapViewAndAClickOnItTravels()
    {
        Harness harness = OpenMapEditor();

        harness.Tap(Keys.Tab);
        Assert.True(harness.View.WorldShown);
        Assert.Equal(MapEditorOverlay.World, harness.View.Overlay);

        // A click at the thumbnail's far corner takes the viewport to the map's far corner.
        MapEditorLayout world = harness.Layout;
        Assert.Equal(MapEditorOverlay.World, world.Overlay);
        harness.Click(world.Minimap.Right - 1, world.Minimap.Bottom - 1);
        Assert.Equal((world.MaxCameraX, world.MaxCameraY), (harness.View.CameraX, harness.View.CameraY));

        // Tab again puts it away; the button is the same switch from the other channel.
        harness.Tap(Keys.Tab);
        Assert.False(harness.View.WorldShown);
        (int x, int y) = Centre(harness.Layout.ButtonRect(EditorButton.WorldToggle));
        harness.Click(x, y);
        Assert.True(harness.View.WorldShown);

        // And while it is down, the pixels it used to occupy belong to nobody.
        harness.View.ToggleWorld();
        Assert.True(harness.Layout.Minimap.IsEmpty);
        Assert.False(
            harness.Layout.TryMinimapCell(world.Minimap.X + 4, world.Minimap.Y + 4, out _, out _));
    }

    /// <summary>
    /// The palette's second page, reached by the wheel over it — the mouse's half of walking off
    /// the lane's edge with Shift+arrows. The page is <em>derived</em> from the tile in hand
    /// (<see cref="MapEditorLayout.PaletteLane"/>), so "show me the other page" has to be "hold
    /// the same cell on the other page", and that is what the wheel does; the block in hand
    /// travels with it.
    ///
    /// <para>Break recipe: make <see cref="MapEditorTileStep.Page"/> call
    /// <c>SelectSprite</c> instead of <c>SelectSpriteBlock</c> — the block assertion goes red
    /// while the page one passes, which is the shape of a page flip that quietly drops what the
    /// pencil was carrying.</para>
    /// </summary>
    [Fact]
    public void TheWheelOverThePaletteFlipsItsPageAndKeepsTheBlock()
    {
        Harness harness = OpenMapEditor();
        harness.Map.SelectSpriteBlock(3, 2, 2);
        harness.View.ToggleTiles();
        MapEditorLayout page = harness.Layout;
        Assert.Equal(0, page.PaletteLane);

        harness.Wheel(page.Sheet.X + 4, page.Sheet.Y + 4, 120);

        Assert.Equal(3 + SheetStrip.LaneColumns * SheetStrip.Rows, harness.Map.SelectedSprite);
        Assert.Equal((2, 2), (harness.Map.BlockWidth, harness.Map.BlockHeight));
        Assert.Equal(1, harness.Layout.PaletteLane);

        harness.Wheel(page.Sheet.X + 4, page.Sheet.Y + 4, 120);
        Assert.Equal(3, harness.Map.SelectedSprite);        // two pages, so it comes back round
        Assert.Equal(0, harness.Layout.PaletteLane);
    }

    // ==================================================================================
    // 8. The read-only map keeps every new tool out.
    // ==================================================================================

    /// <summary>
    /// A cart with <c>map.csv</c> owns its map (MAP-FORMAT §4), and the two new writing verbs
    /// have to be as refused as the pencil is. The guard is one place —
    /// <c>MapEditorPaint</c>'s door — so this is the test that it was not forgotten for the
    /// tools that arrived after it.
    ///
    /// <para>Break recipe: delete the <c>session.MapReadOnly</c> early return from
    /// <see cref="MapEditorPaint.Fill"/> or <see cref="MapEditorPaint.ClearSelection"/> — the
    /// call throws instead of doing nothing, and the test fails naming the verb.</para>
    /// </summary>
    [Fact]
    public void AReadOnlyMapRefusesTheFillAndTheDeleteAtTheDoor()
    {
        string folder = Path.Combine(_root, "readonly-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, MapEditorSession.MapSourceFileName), "# the text source owns this map\n");
        var map = new MapEditorSession(folder);
        var view = new MapEditorView();
        view.BeginSelection(1, 1);
        view.UpdateSelection(3, 3);
        view.EndSelection();
        Assert.True(map.MapReadOnly);

        MapEditorPaint.Fill(map, 0, 0, 5);
        MapEditorPaint.ClearSelection(map, view);

        Assert.False(map.IsDirty);
        Assert.False(map.CanUndo);
        Assert.Equal(0, map.Version);
        // The model itself still throws — the door is a courtesy, not the lock.
        Assert.Throws<InvalidOperationException>(() => map.Fill(0, 0, 5));
        Assert.Throws<InvalidOperationException>(() => map.ClearArea(0, 0, 2, 2));
    }
}

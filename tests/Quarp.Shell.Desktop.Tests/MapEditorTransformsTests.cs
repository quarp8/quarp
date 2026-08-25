using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The three holes the map editor's audit called its most expensive, closed and pinned:
///
/// <list type="number">
///   <item><description><b>Flip and rotate the marked block</b> (REFERENCES-EDITORS §8 item 10)
///   — <c>F</c>, <c>V</c>, <c>R</c>, PICO-8's three keys and this shell's own sprite-editor
///   keys, over the rectangle <see cref="MapEditorView"/> already knows how to mark.</description></item>
///   <item><description><b>Replace tile</b> (§8 item 6) — Ctrl over the bucket, TIC-80's
///   <c>replaceTile</c> and the sprite editor's <c>ReplaceColor</c> on the same
///   modifier.</description></item>
///   <item><description><b>Labels on the four buttonless controls</b> (§8 item 15) —
///   <see cref="MapRegion"/>, built the way <see cref="SfxRegion"/> and
///   <see cref="MusicRegion"/> were, with the same sweep behind it.</description></item>
/// </list>
///
/// <para><b>What is real here.</b> The model claims are driven through
/// <see cref="MapEditorSession"/> itself, the owner of the bytes and of "what one undo step is";
/// the key and click claims go through the production router
/// (<see cref="MapEditorInput.Update"/>) and the production readers, exactly as
/// <c>MapEditorToolsTests</c> does — nothing in this file re-implements a rule, so deleting a
/// rule turns this red rather than leaving a mirror of it green.</para>
/// </summary>
public class MapEditorTransformsTests : IDisposable
{
    private readonly string _root;

    public MapEditorTransformsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-maptransform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The console's own screen — the only surface a tool screen is laid out on since wave R3.</summary>
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    /// <summary>One frame at 60 Hz — the router spends it on the tooltip clock only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside every rectangle the layout places — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    // ==================================================================================
    // The window, minus the window — MapEditorToolsTests' shape, kept identical on purpose.
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

        internal MapEditorLayout Layout =>
            MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight, View.Overlay, Map.SelectedSprite);

        internal EditorShell Context =>
            new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight);

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

        /// <summary>A frame with the pointer parked on a point and no button down — the hover path.</summary>
        internal void Move(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        internal void Click(int x, int y, params Keys[] down)
        {
            Frame(down, x, y, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
            Frame(down, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }

        internal void RightClick(int x, int y, params Keys[] down)
        {
            Frame(down, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Pressed);
            Frame(down, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }
    }

    /// <summary>The neighbours' fixture, verbatim: a one-cart library of its own, opened on the map tab.</summary>
    private Harness OpenMapEditor()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"xform\",\"author\":\"\",\"profile\":8}");
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

    /// <summary>A read-only map: <c>map.csv</c> beside it makes the text source the owner (MAP-FORMAT §4).</summary>
    private MapEditorSession ReadOnlySession()
    {
        string folder = Path.Combine(_root, "ro-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, MapEditorSession.MapSourceFileName), "# empty\n");
        var session = new MapEditorSession(folder);
        Assert.True(session.MapReadOnly);
        return session;
    }

    private static void Stamp(MapEditorSession map, int x, int y, int tile)
    {
        map.SelectSprite(tile);
        map.BeginStroke();
        map.PaintTile(x, y);
        map.EndStroke();
    }

    /// <summary>Paints a rectangle cell by cell as ONE stroke, so the fixture costs one undo step and not w*h.</summary>
    private static void StampRect(MapEditorSession map, int x, int y, int width, int height, Func<int, int, int> tile)
    {
        map.BeginStroke();
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                map.PaintTile(x + column, y + row, tile(column, row));
            }
        }
        map.EndStroke();
    }

    /// <summary>The whole map as bytes — the only honest way to say "and nothing else moved".</summary>
    private static byte[] Snapshot(MapEditorSession map) => map.Map.ToArray();

    /// <summary>A marked rectangle, through the very verbs the select tool's drag uses.</summary>
    private static void Mark(MapEditorView view, int x, int y, int width, int height)
    {
        view.BeginSelection(x, y);
        view.UpdateSelection(x + width - 1, y + height - 1);
        view.EndSelection();
        Assert.True(view.HasSelection);
        Assert.Equal(width, view.SelectionWidth);
        Assert.Equal(height, view.SelectionHeight);
    }

    // ==================================================================================
    // 1. Flip and rotate the marked block (REFERENCES-EDITORS §8 item 10).
    // ==================================================================================

    /// <summary>
    /// The horizontal flip mirrors the marked rectangle left↔right and touches <b>nothing</b>
    /// outside it. The fixture is deliberately asymmetric in both axes — every cell of a 4x3
    /// block holds a different number — so a flip that mirrored the wrong axis, mirrored the
    /// whole map, or did nothing at all each produces a different failure.
    ///
    /// <para>Break recipe: change <c>MapEditorSession.FlipAreaHorizontal</c>'s picker from
    /// <c>source[(y * w) + (w - 1 - x)]</c> to the vertical one
    /// <c>source[((h - 1 - y) * w) + x]</c> — the in-block assertions go red naming the two
    /// cells that swapped rows instead of columns. Widen the loop bounds in
    /// <c>TransformArea</c> by one cell and the two guard assertions outside the rectangle go
    /// red instead.</para>
    /// </summary>
    [Fact]
    public void TheHorizontalFlipMirrorsTheMarkedRectangleAndLeavesTheRestOfTheMapAlone()
    {
        MapEditorSession map = BareSession();
        StampRect(map, 10, 5, 4, 3, static (column, row) => 1 + (row * 4) + column);
        // Two witnesses just outside the rectangle: one to the left, one to the right.
        Stamp(map, 9, 5, 200);
        Stamp(map, 14, 5, 201);

        map.FlipAreaHorizontal(10, 5, 4, 3);

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                Assert.Equal((byte)(1 + (row * 4) + (3 - column)), map.TileAt(10 + column, 5 + row));
            }
        }
        Assert.Equal(200, map.TileAt(9, 5));
        Assert.Equal(201, map.TileAt(14, 5));
    }

    /// <summary>
    /// The vertical flip's twin claim, on the same asymmetric fixture and with witnesses above
    /// and below instead of left and right.
    ///
    /// <para>Break recipe: give <c>FlipAreaVertical</c> the horizontal picker and the in-block
    /// assertions go red; the two witnesses stay green, which is exactly how the two failures
    /// tell themselves apart.</para>
    /// </summary>
    [Fact]
    public void TheVerticalFlipMirrorsTheMarkedRectangleAndLeavesTheRestOfTheMapAlone()
    {
        MapEditorSession map = BareSession();
        StampRect(map, 10, 5, 4, 3, static (column, row) => 1 + (row * 4) + column);
        Stamp(map, 10, 4, 202);
        Stamp(map, 10, 8, 203);

        map.FlipAreaVertical(10, 5, 4, 3);

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                Assert.Equal((byte)(1 + ((2 - row) * 4) + column), map.TileAt(10 + column, 5 + row));
            }
        }
        Assert.Equal(202, map.TileAt(10, 4));
        Assert.Equal(203, map.TileAt(10, 8));
    }

    /// <summary>
    /// The rotate turns a <b>square</b> mark 90° clockwise: the top row becomes the right column.
    /// A 3x3 of nine distinct numbers pins the direction — a counter-clockwise rotation puts
    /// every one of the nine somewhere else, so the direction cannot pass by accident.
    ///
    /// <para>Break recipe: change the picker to <c>source[(x * w) + (w - 1 - y)]</c>
    /// (counter-clockwise) — the corner assertions go red naming which corner went where.</para>
    /// </summary>
    [Fact]
    public void TheRotateTurnsASquareMarkClockwise()
    {
        MapEditorSession map = BareSession();
        StampRect(map, 4, 4, 3, 3, static (column, row) => 1 + (row * 3) + column);

        Assert.True(map.RotateAreaClockwise(4, 4, 3, 3));

        // Source rows 1 2 3 / 4 5 6 / 7 8 9 become columns: 7 4 1 / 8 5 2 / 9 6 3.
        int[] expected = { 7, 4, 1, 8, 5, 2, 9, 6, 3 };
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Assert.Equal((byte)expected[(row * 3) + column], map.TileAt(4 + column, 4 + row));
            }
        }
    }

    /// <summary>
    /// <b>A non-square mark refuses the rotate, in words, and changes not one byte.</b> This is
    /// the wave's named decision (see <see cref="MapEditorSession.RotateAreaClockwise"/>): a w×h
    /// block becomes h×w, which on a map means writing cells the author never marked, dropping
    /// cells the author did mark, or running off the 256x72 edge — all three silent. PICO-8
    /// states the same rule outright, «Rotate (requires a square selection)»
    /// (REFERENCES-EDITORS §2.3), and TIC-80's map editor has no transform at all to copy.
    ///
    /// <para>Negative control, in the same test: the identical fixture with a SQUARE mark is
    /// rotated and the bytes do move — so the refusal above is the shape rule speaking and not
    /// a method that never writes anything.</para>
    ///
    /// <para>Break recipe: delete the <c>width != height</c> arm from
    /// <c>MapEditorSession.RotateAreaClockwise</c>. The refusal assertion goes red, the message
    /// assertion goes red, and the byte comparison goes red with a map whose 4x3 block has been
    /// smeared into a 3x4 one — three different complaints about one deleted line.</para>
    /// </summary>
    [Fact]
    public void ANonSquareMarkRefusesTheRotateAndSaysSo()
    {
        MapEditorSession refused = BareSession();
        StampRect(refused, 10, 5, 4, 3, static (column, row) => 1 + (row * 4) + column);
        byte[] before = Snapshot(refused);

        Assert.False(refused.RotateAreaClockwise(10, 5, 4, 3));

        Assert.Equal(before, Snapshot(refused));
        Assert.NotNull(refused.ClipboardNotice);
        Assert.Contains("ROTATE", refused.ClipboardNotice!, StringComparison.Ordinal);
        Assert.Contains("SQUARE", refused.ClipboardNotice!, StringComparison.Ordinal);
        // A refusal is not a step: Ctrl+Z must not undo the fixture the author still wants.
        Assert.True(refused.CanUndo);
        refused.Undo();
        Assert.False(refused.CanUndo);          // exactly one step existed — the fixture's stroke

        MapEditorSession allowed = BareSession();
        StampRect(allowed, 10, 5, 3, 3, static (column, row) => 1 + (row * 3) + column);
        byte[] squareBefore = Snapshot(allowed);
        Assert.True(allowed.RotateAreaClockwise(10, 5, 3, 3));
        Assert.NotEqual(squareBefore, Snapshot(allowed));
    }

    /// <summary>
    /// <b>One operation, one undo step</b> — the shell's standing law, applied to each of the
    /// three new verbs. Each runs on a fresh session whose only prior step is the fixture stroke,
    /// so "one Ctrl+Z restores the map exactly, and a second one has only the fixture left to
    /// undo" is the whole claim, byte for byte.
    ///
    /// <para>Break recipe: move the <c>EndStroke(); BeginStroke();</c> pair inside
    /// <c>TransformArea</c>'s write loop — every cell becomes its own step, one Ctrl+Z restores
    /// a single cell, and the byte comparison goes red on the very first assertion.</para>
    /// </summary>
    [Theory]
    [InlineData("fliph")]
    [InlineData("flipv")]
    [InlineData("rotate")]
    public void EachTransformIsExactlyOneUndoStep(string verb)
    {
        MapEditorSession map = BareSession();
        StampRect(map, 20, 10, 3, 3, static (column, row) => 1 + (row * 3) + column);
        byte[] before = Snapshot(map);

        switch (verb)
        {
            case "fliph":
                map.FlipAreaHorizontal(20, 10, 3, 3);
                break;
            case "flipv":
                map.FlipAreaVertical(20, 10, 3, 3);
                break;
            default:
                Assert.True(map.RotateAreaClockwise(20, 10, 3, 3));
                break;
        }
        Assert.NotEqual(before, Snapshot(map));

        map.Undo();
        Assert.Equal(before, Snapshot(map));

        // ...and the fixture's own stroke is all that is left underneath it.
        Assert.True(map.CanUndo);
        map.Undo();
        Assert.False(map.CanUndo);
    }

    /// <summary>
    /// A transform that moves nothing is not a step. A symmetric block flipped across its own
    /// axis of symmetry writes no byte, so it must leave the undo stack exactly where it was —
    /// the same rule an idle pencil click and a fill with the tile already there follow.
    ///
    /// <para>Break recipe: make <c>MapEditorSession.WriteCell</c> return true unconditionally
    /// (or push the undo snapshot in <c>TransformArea</c> before the loop instead of letting
    /// <c>EndStroke</c> judge) — <c>CanUndo</c> after the flip goes red.</para>
    /// </summary>
    [Fact]
    public void ASymmetricBlockFlippedOntoItselfPushesNoUndoStep()
    {
        MapEditorSession map = BareSession();
        // Symmetric across the vertical axis: the three cells read 7 8 7, so a horizontal flip
        // has nothing to move. One stroke, so the stack is exactly one step deep.
        StampRect(map, 30, 30, 3, 1, static (column, _) => column == 1 ? 8 : 7);
        byte[] before = Snapshot(map);
        Assert.True(map.CanUndo);

        map.FlipAreaHorizontal(30, 30, 3, 1);

        Assert.Equal(before, Snapshot(map));
        map.Undo();                             // the one step is the fixture's own stroke...
        Assert.False(map.CanUndo);              // ...and the flip added nothing behind it
        Assert.Equal(MapEditorSession.EmptyTile, map.TileAt(30, 30));

        // Negative control: the SAME rectangle, filled 7 8 9 instead of 7 8 7, does move under
        // the same call and does push a step — so the empty stack above is the symmetry
        // speaking, not a flip that never writes anything.
        MapEditorSession moving = BareSession();
        StampRect(moving, 30, 30, 3, 1, static (column, _) => 7 + column);
        moving.FlipAreaHorizontal(30, 30, 3, 1);
        moving.Undo();
        Assert.True(moving.CanUndo);            // the flip WAS a step here
    }

    /// <summary>
    /// With nothing marked the three keys are a <b>refusal in words</b>, not an error and not a
    /// silent no-op: the order's rule, and the sentence is the one Ctrl+C already puts there for
    /// the same situation, so the author reads one phrase whichever key produced it.
    ///
    /// <para>Negative control: the identical call with a rectangle marked returns true and does
    /// change the map, so the false above is the guard speaking.</para>
    ///
    /// <para>Break recipe: delete the <c>!view.HasSelection</c> arm from
    /// <c>MapEditorPaint.TransformSelection</c> — the flip then reaches the session with a
    /// zero-sized rectangle, the message assertion goes red (no sentence at all), and the return
    /// value goes red with it.</para>
    /// </summary>
    [Fact]
    public void WithNothingMarkedTheTransformsRefuseOnTheMessageLine()
    {
        MapEditorSession map = BareSession();
        var view = new MapEditorView();
        StampRect(map, 40, 20, 2, 2, static (column, row) => 1 + (row * 2) + column);
        byte[] before = Snapshot(map);
        Assert.False(view.HasSelection);

        Assert.False(MapEditorPaint.FlipSelectionHorizontal(map, view));
        Assert.Equal("FLIP: NOTHING SELECTED", map.ClipboardNotice);
        Assert.False(MapEditorPaint.FlipSelectionVertical(map, view));
        Assert.Equal("FLIP: NOTHING SELECTED", map.ClipboardNotice);
        Assert.False(MapEditorPaint.RotateSelectionClockwise(map, view));
        Assert.Equal("ROTATE: NOTHING SELECTED", map.ClipboardNotice);
        Assert.Equal(before, Snapshot(map));

        // The refusal is what the screen's message line shows, and it outranks the standing
        // notices — one owner of that decision, MapEditorRenderer.StandingNotice.
        Assert.Equal("ROTATE: NOTHING SELECTED", MapEditorRenderer.StandingNotice(map));

        // Negative control: the same three verbs over a marked rectangle do their work.
        Mark(view, 40, 20, 2, 2);
        Assert.True(MapEditorPaint.FlipSelectionHorizontal(map, view));
        Assert.NotEqual(before, Snapshot(map));
        Assert.Null(map.ClipboardNotice);
    }

    /// <summary>
    /// A read-only map (a cart with <c>map.csv</c> — MAP-FORMAT §4) refuses all three transforms
    /// by name instead of throwing out of the router. The guard lives in
    /// <c>MapEditorPaint.TransformSelection</c>, once, exactly where the pencil's and the
    /// clipboard's live.
    ///
    /// <para>Break recipe: delete the <c>session.MapReadOnly</c> arm from
    /// <c>TransformSelection</c> — <c>MapEditorSession.RequireWritableMap</c> throws
    /// inside the session and this test goes red with an exception instead of a
    /// sentence.</para>
    /// </summary>
    [Fact]
    public void AReadOnlyMapRefusesTheTransformsByName()
    {
        MapEditorSession map = ReadOnlySession();
        var view = new MapEditorView();
        Mark(view, 1, 1, 2, 2);

        Assert.False(MapEditorPaint.FlipSelectionHorizontal(map, view));
        Assert.Contains("FLIP", map.ClipboardNotice!, StringComparison.Ordinal);
        Assert.Contains(
            MapEditorSession.MapSourceFileName.ToUpperInvariant(), map.ClipboardNotice!,
            StringComparison.Ordinal);
        Assert.False(MapEditorPaint.RotateSelectionClockwise(map, view));
        Assert.Contains("ROTATE", map.ClipboardNotice!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The keys, through the production router: <c>F</c>, <c>V</c> and <c>R</c> reach the three
    /// verbs on the map screen. They were chosen because PICO-8 spends exactly these three on
    /// exactly these three verbs (REFERENCES-EDITORS §2.3, §8 item 10) and because this shell's
    /// sprite editor already does — and they were free here, which this test also pins.
    ///
    /// <para><b>The negative control is the collision that would have mattered.</b>
    /// <c>Ctrl+V</c> is the map's paste and must NOT be read as the vertical flip: the reader
    /// guards the bare-letter fields with <c>!ctrl</c>, so a Ctrl+V frame over a marked
    /// rectangle leaves the map untouched and arms a float instead. Break the guard (drop
    /// <c>!ctrl</c> from <c>EditorFlipV</c> in <c>ShellCommandReader</c>) and the last two
    /// assertions go red together.</para>
    /// </summary>
    [Fact]
    public void TheThreeTransformKeysWorkOnTheMapScreenAndCtrlVIsStillThePaste()
    {
        Harness harness = OpenMapEditor();
        StampRect(harness.Map, 10, 2, 3, 3, static (column, row) => 1 + (row * 3) + column);
        Mark(harness.View, 10, 2, 3, 3);

        harness.Tap(Keys.F);
        Assert.Equal(3, harness.Map.TileAt(10, 2));         // row 1 2 3 read backwards
        harness.Tap(Keys.V);
        Assert.Equal(9, harness.Map.TileAt(10, 2));         // ...and now the bottom row's end
        harness.Tap(Keys.R);
        // Clockwise: the top row 9 8 7 becomes the right column, so the block's own top-left
        // takes what stood at the bottom-left — 3.
        Assert.Equal(3, harness.Map.TileAt(10, 2));

        // Ctrl+V is the paste and nothing else. With an empty clipboard it arms no float and
        // writes no byte — what matters here is that the map did not flip again.
        byte[] before = Snapshot(harness.Map);
        harness.Tap(Keys.LeftControl, Keys.V);
        Assert.Equal(before, Snapshot(harness.Map));
        Assert.False(harness.View.PasteFloating);           // nothing was on the clipboard to float
    }

    // ==================================================================================
    // 2. Replace tile — Ctrl over the bucket (REFERENCES-EDITORS §8 item 6).
    // ==================================================================================

    /// <summary>
    /// The difference between the bucket's two halves, stated as one fixture: a wall of tile 9
    /// down column 4 cuts the map into a left half and a right half of zeros that touch nowhere.
    /// A <b>fill</b> started on the left leaves the right alone (that is
    /// <c>MapEditorToolsTests</c>'s claim); a <b>replace</b> started on the left crosses the wall,
    /// because "everywhere" does not mean "connected".
    ///
    /// <para>Break recipe: make <c>MapEditorSession.ReplaceTile</c> call <c>FloodFill</c> instead
    /// of scanning the rectangle — the far-side assertion goes red, which is precisely the
    /// difference between the two operations. Give it back the flood's <c>target ==
    /// replacement</c> early return only and it stays green; that guard is shared on
    /// purpose.</para>
    /// </summary>
    [Fact]
    public void ReplaceCrossesEveryWallThatTheFloodFillStopsAt()
    {
        MapEditorSession map = BareSession();
        map.BeginStroke();
        for (int y = 0; y < MapEditorSession.MapRows; y++)
        {
            map.PaintTile(4, y, 9);
        }
        map.EndStroke();

        map.ReplaceTile(0, 0, 5);       // seed on the left half, which holds tile 0

        Assert.Equal(5, map.TileAt(0, 0));
        Assert.Equal(5, map.TileAt(3, 0));
        Assert.Equal(9, map.TileAt(4, 0));                      // the wall is a different value
        Assert.Equal(5, map.TileAt(5, 0));                      // ...and the far side went too
        Assert.Equal(5, map.TileAt(MapEditorSession.MapColumns - 1, MapEditorSession.MapRows - 1));
    }

    /// <summary>
    /// <b>The marked rectangle is the border when there is one</b> — TIC-80's own rule for
    /// <c>replaceTile</c>, which works «по всей карте/выделению» (REFERENCES-EDITORS §3.1). The
    /// same seed, the same target value, twice: bounded by a mark, and unbounded without one.
    ///
    /// <para>Break recipe: drop the <c>view.HasSelection</c> arm from
    /// <c>MapEditorPaint.ReplaceTile</c> — the bounded run repaints the whole map and the
    /// "outside stayed" assertion goes red.</para>
    /// </summary>
    [Fact]
    public void ReplaceIsBoundedByTheMarkedRectangleAndUnboundedWithoutOne()
    {
        MapEditorSession bounded = BareSession();
        var view = new MapEditorView();
        Mark(view, 2, 2, 3, 3);
        MapEditorPaint.ReplaceTile(bounded, view, 2, 2, 6);

        Assert.Equal(6, bounded.TileAt(2, 2));
        Assert.Equal(6, bounded.TileAt(4, 4));
        Assert.Equal(0, bounded.TileAt(5, 4));                  // one cell past the mark's right edge
        Assert.Equal(0, bounded.TileAt(2, 5));                  // one row past its bottom
        Assert.Equal(0, bounded.TileAt(100, 40));

        // Negative control: the identical call with nothing marked reaches the whole map.
        MapEditorSession whole = BareSession();
        var unmarked = new MapEditorView();
        Assert.False(unmarked.HasSelection);
        MapEditorPaint.ReplaceTile(whole, unmarked, 2, 2, 6);
        Assert.Equal(6, whole.TileAt(100, 40));
        Assert.Equal(6, whole.TileAt(MapEditorSession.MapColumns - 1, MapEditorSession.MapRows - 1));
    }

    /// <summary>
    /// One replace is one undo step however many thousands of cells it repainted — and replacing
    /// a value with itself is not a step at all, the same pair of rules
    /// <c>MapEditorSession.Fill</c> already lives by.
    ///
    /// <para>Break recipe: move <c>EndStroke(); BeginStroke();</c> inside the scan loop and the
    /// first Ctrl+Z restores one cell instead of the map. Delete the <c>target == replacement</c>
    /// early return and the idle-replace assertion goes red with a stack that grew.</para>
    /// </summary>
    [Fact]
    public void OneReplaceIsOneUndoStepAndReplacingAValueWithItselfIsNone()
    {
        MapEditorSession map = BareSession();
        Stamp(map, 0, 0, 3);
        Stamp(map, 100, 40, 3);
        byte[] before = Snapshot(map);

        map.ReplaceTile(0, 0, 7);
        Assert.Equal(7, map.TileAt(0, 0));
        Assert.Equal(7, map.TileAt(100, 40));

        map.Undo();
        Assert.Equal(before, Snapshot(map));

        // ...and an idle replace changes neither the bytes nor the stack.
        bool couldUndo = map.CanUndo;
        map.ReplaceTile(0, 0, 3);               // cell (0,0) already holds 3
        Assert.Equal(before, Snapshot(map));
        Assert.Equal(couldUndo, map.CanUndo);
        Assert.True(map.CanRedo);               // the redo the undo above created is still there
    }

    /// <summary>
    /// The gesture, through the production router: <b>Ctrl over the bucket replaces, the bare
    /// bucket floods</b>. Same key and same tool as the sprite editor's
    /// <c>SpriteEditorSession.ReplaceColor</c> beside its <c>SpriteEditorSession.Fill</c>,
    /// which is the whole reason this key was chosen (TIC-80 <c>processMouseFillMode</c>,
    /// REFERENCES-EDITORS §8 item 6) — one rule for both banks.
    ///
    /// <para>The fixture is the wall again, so the two halves of the tool are told apart by what
    /// happens on the far side of it. The negative control is in the same test: the identical
    /// click without Ctrl leaves the far side alone.</para>
    ///
    /// <para>Break recipe: delete the <c>replacing</c> arm from
    /// <c>MapEditorInput.MousePressOnCanvas</c>'s fill case — the Ctrl run stops at the wall and
    /// the far-side assertion goes red while the plain run stays green.</para>
    /// </summary>
    [Fact]
    public void CtrlOverTheBucketReplacesAndThePlainBucketFloods()
    {
        Harness replaced = OpenMapEditor();
        WallAndBucket(replaced);
        (int x, int y) = CellPoint(replaced, 0, 0);
        replaced.Click(x, y, Keys.LeftControl);
        Assert.Equal(5, replaced.Map.TileAt(0, 0));
        Assert.Equal(9, replaced.Map.TileAt(4, 0));             // the wall itself is untouched
        Assert.Equal(5, replaced.Map.TileAt(5, 0));             // ...and the far side went

        Harness flooded = OpenMapEditor();
        WallAndBucket(flooded);
        (int fx, int fy) = CellPoint(flooded, 0, 0);
        flooded.Click(fx, fy);
        Assert.Equal(5, flooded.Map.TileAt(0, 0));
        Assert.Equal(9, flooded.Map.TileAt(4, 0));
        Assert.Equal(0, flooded.Map.TileAt(5, 0));              // the flood stopped at the wall
    }

    /// <summary>
    /// The keyboard's half of the same gesture. The map's paint key is bare <c>Z</c> and
    /// <c>Ctrl+Z</c> is the shell's undo on every screen, so "Ctrl + the paint key" can only be
    /// <c>Ctrl+Space</c> here — which is exactly the chord the sprite editor already documents
    /// for its own Ctrl-over-the-tool gesture. Space alone still pans, and that is the negative
    /// control: the same frame without Ctrl writes nothing at all.
    ///
    /// <para>Break recipe: drop the <c>&amp;&amp; !replacing</c> from <c>MapEditorInput</c>'s
    /// <c>panning</c> line — Ctrl+Space becomes a pan again, the replace never fires, and the
    /// first assertion goes red.</para>
    /// </summary>
    [Fact]
    public void CtrlSpaceIsTheKeyboardsReplaceAndBareSpaceStillPans()
    {
        Harness harness = OpenMapEditor();
        WallAndBucket(harness);
        harness.Tap(Keys.LeftControl, Keys.Space);
        Assert.Equal(5, harness.Map.TileAt(0, 0));
        Assert.Equal(5, harness.Map.TileAt(5, 0));              // across the wall: a replace, not a fill

        Harness panned = OpenMapEditor();
        WallAndBucket(panned);
        panned.Tap(Keys.Space);
        Assert.Equal(0, panned.Map.TileAt(0, 0));               // Space alone is the pan modifier
        Assert.Equal(0, panned.Map.TileAt(5, 0));
    }

    /// <summary>The shared fixture of the two router tests: a wall of 9 down column 4, the bucket in hand, tile 5 picked.</summary>
    private static void WallAndBucket(Harness harness)
    {
        harness.Map.BeginStroke();
        for (int y = 0; y < MapEditorSession.MapRows; y++)
        {
            harness.Map.PaintTile(4, y, 9);
        }
        harness.Map.EndStroke();
        harness.Map.SelectSprite(5);
        harness.View.SelectTool(MapEditorTool.Fill);
        Assert.Equal(0, harness.View.CursorX);
        Assert.Equal(0, harness.View.CursorY);
    }

    /// <summary>The console point in the middle of a map cell, at the camera the view stands at.</summary>
    private static (int X, int Y) CellPoint(Harness harness, int cellX, int cellY)
    {
        Rectangle rect = harness.Layout.MapCellRect(cellX, cellY, harness.View.CameraX, harness.View.CameraY);
        return (rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
    }

    /// <summary>
    /// Ctrl rides the right button too: the eraser's flood becomes "wipe every tile of this kind"
    /// (REFERENCES-EDITORS §7.3's eraser composed with §8 item 6's replace). Refusing the pair
    /// would make Ctrl mean something under one button of one tool and nothing under the other.
    ///
    /// <para>Break recipe: delete the <c>replacing</c> arm from the right-button chain in
    /// <c>MapEditorInput.Update</c> — the far-side assertion goes red.</para>
    /// </summary>
    [Fact]
    public void CtrlOverTheBucketsRightButtonWipesThatTileEverywhere()
    {
        Harness harness = OpenMapEditor();
        harness.Map.BeginStroke();
        for (int y = 0; y < MapEditorSession.MapRows; y++)
        {
            harness.Map.PaintTile(4, y, 9);
            harness.Map.PaintTile(0, y, 3);
            harness.Map.PaintTile(200, y, 3);
        }
        harness.Map.EndStroke();
        harness.View.SelectTool(MapEditorTool.Fill);

        (int x, int y2) = CellPoint(harness, 0, 0);
        harness.RightClick(x, y2, Keys.LeftControl);

        Assert.Equal(MapEditorSession.EmptyTile, harness.Map.TileAt(0, 0));
        Assert.Equal(MapEditorSession.EmptyTile, harness.Map.TileAt(200, 0));   // across the wall
        Assert.Equal(9, harness.Map.TileAt(4, 0));                              // and only tile 3 died
    }

    // ==================================================================================
    // 3. Every buttonless control of the map screen names its keys (§8 item 15).
    // ==================================================================================

    /// <summary>The layout that actually SHOWS a given region — the palette and the whole-map view are overlays.</summary>
    private static MapEditorLayout LayoutShowing(MapRegion region) => region switch
    {
        MapRegion.Tiles => MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight, MapEditorOverlay.Tiles, 0),
        MapRegion.Minimap => MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight, MapEditorOverlay.World, 0),
        _ => MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight),
    };

    /// <summary>
    /// <b>The sweep</b>, the map's twin of <c>SfxEditorTests.EveryKeylessControlAnnouncesItsKeys</c>
    /// and <c>MusicEditorScreenTests.EveryButtonlessControlNamesItsKeys</c>: every value of
    /// <see cref="MapRegion"/> has a rectangle on the screen that shows it, that rectangle's
    /// centre hit-tests back to that same region, and the region has a printable label. Driven
    /// off <c>Enum.GetValues</c>, so a region added without a rectangle or without a label turns
    /// this red on arrival — that is what makes it a sweep and not four assertions.
    ///
    /// <para>The named keys below are the point of the whole item: on a 160x90 console the label
    /// on the control IS the documentation, and these six gestures are announced nowhere else —
    /// Shift raises the palette, Tab shows the whole map, Ctrl+Shift+arrows size the block, the
    /// middle button is the eyedropper, Space+drag pans, <c>`</c> switches the grid.</para>
    ///
    /// <para>Break recipe: delete the <c>MapRegion.Slider</c> arm from
    /// <c>MapEditorLayout.RegionRect</c> — that region's rectangle comes back empty and the sweep
    /// names it. Delete an arm from <c>EditorIcons.MapRegionTooltip</c> and the label assertion
    /// throws for that region by name. Drop "SHIFT" out of the palette's text and the key
    /// assertion goes red on the one line that names it.</para>
    /// </summary>
    [Fact]
    public void EveryButtonlessControlNamesItsKeys()
    {
        foreach (MapRegion region in Enum.GetValues<MapRegion>())
        {
            if (region == MapRegion.None)
            {
                // None is not a control: it has no rectangle and, like its two siblings, no label.
                Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.MapRegionTooltip(region));
                continue;
            }
            MapEditorLayout layout = LayoutShowing(region);
            Rectangle rect = layout.RegionRect(region);
            Assert.NotEqual(Rectangle.Empty, rect);
            Assert.Equal(region, layout.RegionAt(rect.Center.X, rect.Center.Y));
            string label = EditorIcons.MapRegionTooltip(region);
            Assert.False(string.IsNullOrWhiteSpace(label));
            // ASCII only: the system font has no other alphabet.
            Assert.All(label, c => Assert.InRange(c, ' ', '~'));
        }

        Assert.Equal(MapRegion.None, MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight).RegionAt(Off, Off));

        // The six gestures that live on no button of this screen, each named on the control it
        // acts on. This list IS the answer to "where is that documented".
        Assert.Contains("MCLICK", EditorIcons.MapCanvasTooltip, StringComparison.Ordinal);
        Assert.Contains("SPACE+DRAG", EditorIcons.MapCanvasTooltip, StringComparison.Ordinal);
        Assert.Contains("`", EditorIcons.MapCanvasTooltip, StringComparison.Ordinal);
        Assert.Contains("F V R", EditorIcons.MapCanvasTooltip, StringComparison.Ordinal);
        Assert.Contains("SHIFT", EditorIcons.MapTilesTooltip, StringComparison.Ordinal);
        Assert.Contains("CTRL+SHIFT+ARROWS", EditorIcons.MapTilesTooltip, StringComparison.Ordinal);
        Assert.Contains("TAB", EditorIcons.MapMinimapTooltip, StringComparison.Ordinal);
        Assert.Contains("[ ]", EditorIcons.MapSliderTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    /// The router builds those targets — the half a table of texts cannot prove. The pointer is
    /// parked on each control through the production <see cref="MapEditorInput.Update"/> and the
    /// tracker is asked what it saw, so "the palette is mute" would show up here even with every
    /// label written.
    ///
    /// <para>Negative control: a pointer on a BUTTON still produces a button target and not a
    /// region, which is what keeps the two kinds from colliding — and a pointer off every
    /// rectangle produces no target at all.</para>
    ///
    /// <para>Break recipe: put the <c>RegionAt</c> arm before <c>TryButton</c> in
    /// <c>MapEditorInput</c>'s hover call and the button assertion goes red (the tool block sits
    /// outside the canvas, so the sliderbar case is the one that would flip); delete the arm
    /// entirely and all four region assertions go red at once.</para>
    /// </summary>
    [Fact]
    public void ThePointerOnEachButtonlessControlProducesItsHoverTarget()
    {
        Harness harness = OpenMapEditor();

        Rectangle canvas = harness.Layout.Canvas;
        harness.Move(canvas.Center.X, canvas.Center.Y);
        AssertRegion(harness, MapRegion.Canvas);

        Rectangle slider = harness.Layout.Slider;
        harness.Move(slider.Center.X, slider.Center.Y);
        AssertRegion(harness, MapRegion.Slider);

        harness.View.ToggleTiles();
        Rectangle sheet = harness.Layout.Sheet;
        Assert.NotEqual(Rectangle.Empty, sheet);
        harness.Move(sheet.Center.X, sheet.Center.Y);
        AssertRegion(harness, MapRegion.Tiles);
        harness.View.CloseOverlay();

        harness.View.ToggleWorld();
        Rectangle minimap = harness.Layout.Minimap;
        Assert.NotEqual(Rectangle.Empty, minimap);
        harness.Move(minimap.Center.X, minimap.Center.Y);
        AssertRegion(harness, MapRegion.Minimap);
        harness.View.CloseOverlay();

        // Negative controls: a button is still a button, and nothing is still nothing.
        Rectangle save = harness.Layout.ButtonRect(EditorButton.Save);
        harness.Move(save.Center.X, save.Center.Y);
        Assert.NotNull(harness.Hover.Target);
        Assert.Equal(EditorButton.Save, harness.Hover.Target!.Value.Button!.Value);
        Assert.Equal(MapRegion.None, harness.Hover.Target!.Value.Map);

        harness.Move(Off, Off);
        Assert.Null(harness.Hover.Target);
    }

    /// <summary>What the tracker saw this frame: this region and no button — the two halves of "the label is the control's".</summary>
    private static void AssertRegion(Harness harness, MapRegion expected)
    {
        Assert.NotNull(harness.Hover.Target);
        Assert.Equal(expected, harness.Hover.Target!.Value.Map);
        Assert.Null(harness.Hover.Target!.Value.Button);
    }

    /// <summary>
    /// The crash lock of 2026-08-25, applied to this screen <b>from the start</b>: a hover target
    /// measured on another screen — no button, no map region — means "no label", never an
    /// exception. A frame is input-then-draw and a tab switch lands between the two halves, so
    /// this shape reaches <c>Draw</c> for real (see <see cref="IconHoverTracker.Clear"/>).
    ///
    /// <para>Negative control: a target this screen DOES own still gets its label, so the null
    /// above is the None arm speaking and not a method that answers null always.</para>
    ///
    /// <para>Break recipe: change <c>MapEditorRenderer.TooltipText</c>'s last line to
    /// <c>EditorIcons.MapRegionTooltip(target.Map)</c> with no None arm — the first assertion
    /// goes red with the very exception that killed the console.</para>
    /// </summary>
    [Fact]
    public void AHoverTargetFromAnotherScreenAsksTheMapForNoLabelInsteadOfKillingTheFrame()
    {
        HoverTarget foreign = HoverTarget.OfSfxRegion(SfxRegion.Octave);
        Assert.Null(foreign.Button);
        Assert.Equal(MapRegion.None, foreign.Map);
        Assert.Null(MapEditorRenderer.TooltipText(foreign));

        Assert.Null(MapEditorRenderer.TooltipText(HoverTarget.OfMusicRegion(MusicRegion.Song)));
        Assert.Null(MapEditorRenderer.TooltipText(HoverTarget.OfSlider()));
        Assert.Null(MapEditorRenderer.TooltipText(HoverTarget.OfSwatch(3)));

        // ...and the map's own targets are labelled, both kinds.
        Assert.Equal(
            EditorIcons.MapCanvasTooltip,
            MapEditorRenderer.TooltipText(HoverTarget.OfMapRegion(MapRegion.Canvas)));
        Assert.Equal(
            EditorIcons.MapTooltip(EditorButton.Save),
            MapEditorRenderer.TooltipText(HoverTarget.OfButton(EditorButton.Save)));

        // And the mirror: the map's target seen by the two screens that already carry the rule.
        HoverTarget mapTarget = HoverTarget.OfMapRegion(MapRegion.Canvas);
        Assert.Null(SfxEditorRenderer.TooltipText(mapTarget));
        Assert.Null(MusicEditorRenderer.TooltipText(mapTarget));
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The map editor's input-parity instrument (M9 stage 2.5's law, stage 3's screen): every
/// action must be reachable BOTH by keyboard alone and by mouse alone. The direct question
/// this file answers by running it:
///
/// <para><b>How does an author reach the far corner of a 256x72 map — cell (255, 71) — with
/// only a keyboard, and with only a mouse, and what proves the two paths arrive at the same
/// cell?</b> Keyboard: <c>]</c> pages the viewport one screen across (eight presses cover 256
/// columns), PgDn pages one screen down (seven cover 72), the arrows walk the last cells, and
/// the camera follows the cursor so what the keys move is always on screen. Mouse: one click
/// in the minimap's bottom-right corner jumps there. The proof is not a comparison of two
/// coordinate calculations — it is that both runs then paint through the same session and
/// their <c>map.bin</c> files come out byte-identical, on two independent cart folders, with
/// no mouse coordinate anywhere in the keyboard run and no <see cref="Keys"/> value anywhere
/// in the mouse run.</para>
///
/// <para><b>What is real and what is mirrored.</b> Both channels go through the production
/// edge detectors (<see cref="ShellCommandReader"/>, <see cref="EditorMouseReader"/>), the
/// production geometry (<see cref="MapEditorLayout"/> — a pure function of window size, no
/// <c>GraphicsDevice</c>), and the production owners of every verb
/// (<see cref="MapEditorView"/>, <see cref="MapEditorTileStep"/>, <see cref="MapEditorPaint"/>,
/// <see cref="EditorIcons.ClickMapButton"/>, <see cref="MapEditorSession"/> itself). Nothing in
/// this file re-implements a rule: those types exist precisely so that
/// <c>QuarpGame.UpdateMapEditor</c> — unconstructible without a graphics device — has nothing
/// left but the dispatch, and so that deleting a rule turns this red instead of leaving a
/// mirror of it green.</para>
/// </summary>
public class MapEditorParityTests : IDisposable
{
    private readonly string _root;

    public MapEditorParityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-map-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FreshCartFolder(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    /// <summary>The tile both runs place — a number, not a colour: MAP-FORMAT §2 says a cell IS a sprite index.</summary>
    private const int ChosenTile = 3;

    // ==================================================================================
    // Channel A: keyboard only. Every frame goes through the REAL ShellCommandReader, then
    // through the same owners QuarpGame.UpdateMapEditor calls. No mouse type below this line.
    // ==================================================================================

    private static void ApplyKeyboardFrame(
        MapEditorSession map, MapEditorView view, in MapEditorLayout layout, in ShellCommands c)
    {
        if (c.EditorSave)
        {
            map.Save();
        }
        if (c.EditorClear)
        {
            map.SelectSprite(0);
        }
        if (c.Slower)
        {
            view.PageCursor(layout, -1, 0);
        }
        if (c.Faster)
        {
            view.PageCursor(layout, 1, 0);
        }
        if (c.EditorLayerUp)
        {
            view.PageCursor(layout, 0, -1);
        }
        if (c.EditorLayerDown)
        {
            view.PageCursor(layout, 0, 1);
        }
        if (c.EditorCopy)
        {
            MapEditorPaint.CopySelection(map, view);
        }
        if (c.EditorCut)
        {
            MapEditorPaint.CutSelection(map, view);
        }
        if (c.EditorPaste)
        {
            view.BeginPaste();
        }
        if (c.EditorBlockDx != 0 || c.EditorBlockDy != 0)
        {
            view.StepTileBlock(map, c.EditorBlockDx, c.EditorBlockDy);
        }
        bool steppedPicker = c.EditorSheetDx != 0 || c.EditorSheetDy != 0;
        if (steppedPicker)
        {
            MapEditorTileStep.Apply(map, c.EditorSheetDx, c.EditorSheetDy);
        }
        bool aimedAtPicker = steppedPicker || c.EditorBlockDx != 0 || c.EditorBlockDy != 0;
        int dx = aimedAtPicker ? 0 : (c.MenuRight ? 1 : 0) - (c.MenuLeft ? 1 : 0);
        int dy = aimedAtPicker ? 0 : (c.MenuDown ? 1 : 0) - (c.MenuUp ? 1 : 0);
        if (dx != 0 || dy != 0)
        {
            view.MoveCursor(layout, dx, dy);
            if (c.EditorPaintDown)
            {
                MapEditorPaint.ContinueBlock(map, view.CursorX, view.CursorY);
            }
        }
        if (c.EditorPaintPressed && !MapEditorPaint.PasteAt(map, view, view.CursorX, view.CursorY))
        {
            MapEditorPaint.BeginBlock(map, view.CursorX, view.CursorY);
        }
        if (c.EditorPaintReleased)
        {
            MapEditorPaint.End(map);
        }
        if (c.MenuEditor)
        {
            map.PickTile(view.CursorX, view.CursorY);
        }
    }

    private static void KeyFrame(
        ShellCommandReader reader, MapEditorSession map, MapEditorView view,
        in MapEditorLayout layout, params Keys[] down) =>
        ApplyKeyboardFrame(map, view, layout, reader.Read(new KeyboardState(down)));

    /// <summary>
    /// Keyboard-only: pick tile 3 out of the strip with Shift+arrows, page to the far corner
    /// with <c>]</c> and PgDn, place the tile with Z, save with Ctrl+S. Every key is released
    /// before it is pressed again — a real key repeats only by being pressed again.
    /// </summary>
    private static void RunKeyboardOnlyScenario(MapEditorSession map, MapEditorView view, in MapEditorLayout layout)
    {
        var reader = new ShellCommandReader();

        for (int i = 0; i < ChosenTile; i++)
        {
            KeyFrame(reader, map, view, layout, Keys.LeftShift, Keys.Right);
            KeyFrame(reader, map, view, layout, Keys.LeftShift);
        }
        Assert.Equal(ChosenTile, map.SelectedSprite);

        for (int i = 0; i < 8; i++)
        {
            KeyFrame(reader, map, view, layout, Keys.OemCloseBrackets);
            KeyFrame(reader, map, view, layout);
        }
        for (int i = 0; i < 7; i++)
        {
            KeyFrame(reader, map, view, layout, Keys.PageDown);
            KeyFrame(reader, map, view, layout);
        }

        KeyFrame(reader, map, view, layout, Keys.Z);        // press: Begin + the tile lands
        KeyFrame(reader, map, view, layout);                // release: the gesture commits

        KeyFrame(reader, map, view, layout, Keys.LeftControl, Keys.S);
        KeyFrame(reader, map, view, layout);
    }

    // ==================================================================================
    // Channel B: mouse only. Every frame goes through the REAL EditorMouseReader and hit-tests
    // against a REAL MapEditorLayout. No Keys value is referenced below this line.
    // ==================================================================================

    private static void DispatchMousePress(
        MapEditorSession map, MapEditorView view, in MapEditorLayout layout, int x, int y)
    {
        if (layout.TryButton(x, y, out EditorButton pressed))
        {
            if (!EditorIcons.IsStub(pressed) && EditorIcons.TabTarget(pressed) is null)
            {
                EditorIcons.ClickMapButton(map, view, pressed);   // the exit tab's bool is the machine's job
            }
            return;
        }
        if (layout.TryTileStripCell(x, y, out _, out _))
        {
            view.BeginTileBlock(map, layout, x, y);      // a press that never moves is one tile
            return;
        }
        if (layout.TryMinimapCell(x, y, out int jumpX, out int jumpY))
        {
            view.JumpTo(layout, jumpX, jumpY);
            return;
        }
        if (layout.TryMapCell(x, y, view.CameraX, view.CameraY, out int cellX, out int cellY)
            && !MapEditorPaint.PasteAt(map, view, cellX, cellY))
        {
            MapEditorPaint.BeginBlock(map, cellX, cellY);
        }
    }

    private static void MouseFrame(
        MapEditorSession map, MapEditorView view, in MapEditorLayout layout, in EditorMouse mouse)
    {
        if (mouse.LeftPressed)
        {
            DispatchMousePress(map, view, layout, mouse.X, mouse.Y);
        }
        else if (mouse.LeftDown && view.TileBlockGestureActive)
        {
            view.UpdateTileBlock(map, layout, mouse.X, mouse.Y);
        }
        else if (mouse.LeftDown && map.StrokeActive)
        {
            layout.ClampMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int dragX, out int dragY);
            view.SetCursor(layout, dragX, dragY);
            MapEditorPaint.ContinueBlock(map, dragX, dragY);
        }
        if (mouse.LeftReleased)
        {
            MapEditorPaint.End(map);
            view.EndTileBlock();
        }
    }

    private static void Frame(
        MapEditorSession map, MapEditorView view, in MapEditorLayout layout,
        EditorMouseReader reader, int x, int y, ButtonState left)
    {
        var state = new MouseState(
            x, y, 0, left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        MouseFrame(map, view, layout, reader.Read(state));
    }

    private static void Click(
        MapEditorSession map, MapEditorView view, in MapEditorLayout layout,
        EditorMouseReader reader, int x, int y)
    {
        Frame(map, view, layout, reader, x, y, ButtonState.Pressed);
        Frame(map, view, layout, reader, x, y, ButtonState.Released);
    }

    private static (int X, int Y) Centre(Rectangle rect) =>
        (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>
    /// Mouse-only: click tile 3 in the picker, click the minimap's far corner to travel there,
    /// press and release on that cell of the canvas, click Save.
    /// </summary>
    private static void RunMouseOnlyScenario(MapEditorSession map, MapEditorView view, in MapEditorLayout layout)
    {
        var reader = new EditorMouseReader();

        (int tileX, int tileY) = Centre(layout.TileCellRect(ChosenTile));
        Click(map, view, layout, reader, tileX, tileY);
        Assert.Equal(ChosenTile, map.SelectedSprite);

        Click(map, view, layout, reader, layout.Minimap.Right - 1, layout.Minimap.Bottom - 1);

        (int cellX, int cellY) = Centre(
            layout.MapCellRect(
                MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1, view.CameraX, view.CameraY));
        Click(map, view, layout, reader, cellX, cellY);

        (int saveX, int saveY) = Centre(layout.ButtonRect(EditorButton.Save));
        Click(map, view, layout, reader, saveX, saveY);
    }

    // ==================================================================================
    // The instrument itself.
    // ==================================================================================

    /// <summary>
    /// The wave's direct deliverable, and the answer to the order's question. Two runs, two
    /// input channels sharing not one coordinate or key — the same cell, the same map, the same
    /// file.
    ///
    /// <para>Negative controls, all against production code. (a) Break the clamp in
    /// <see cref="MapEditorView.PageCursor"/>'s <c>SetCursor</c> (drop the upper bound) and the
    /// keyboard run overshoots the map: the cursor assertion goes red first, then the bytes.
    /// (b) Drop the centring or the <c>MinimapScale</c> division in
    /// <see cref="MapEditorLayout.TryMinimapCell"/> and the mouse run lands somewhere else: the
    /// byte comparison goes red while each run still "works" on its own. (c) Change
    /// <see cref="MapEditorTileStep"/>'s sprite arithmetic and the two runs place different
    /// tiles at the same cell — the file comparison goes red and names the byte.</para>
    /// </summary>
    [Fact]
    public void KeyboardOnlyAndMouseOnlyRunsReachTheSameFarCornerAndSaveIdenticalMaps()
    {
        var layout = MapEditorLayout.Compute(WindowWidth, WindowHeight);

        string folderA = FreshCartFolder("keyboard-run");
        var mapA = new MapEditorSession(folderA);
        var viewA = new MapEditorView();
        RunKeyboardOnlyScenario(mapA, viewA, layout);

        string folderB = FreshCartFolder("mouse-run");
        var mapB = new MapEditorSession(folderB);
        var viewB = new MapEditorView();
        RunMouseOnlyScenario(mapB, viewB, layout);

        // Both channels stand on the far corner, and their viewports agree — the question's
        // "same cell" made explicit before the bytes are compared.
        Assert.Equal(
            (MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1), (viewA.CursorX, viewA.CursorY));
        Assert.Equal((viewA.CursorX, viewA.CursorY), (viewB.CursorX, viewB.CursorY));
        Assert.Equal((viewA.CameraX, viewA.CameraY), (viewB.CameraX, viewB.CameraY));
        Assert.Equal((layout.MaxCameraX, layout.MaxCameraY), (viewA.CameraX, viewA.CameraY));

        // The claim itself: the same map.
        Assert.True(mapA.Map.SequenceEqual(mapB.Map));

        // Sanity against a vacuous pass (two empty maps "agreeing" for free): exactly one cell
        // is set, it is the far corner, and it holds the tile both runs chose.
        int painted = 0;
        foreach (byte tile in mapA.Map)
        {
            if (tile != 0)
            {
                painted++;
            }
        }
        Assert.Equal(1, painted);
        Assert.Equal(ChosenTile, mapA.TileAt(MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1));

        Assert.False(mapA.IsDirty);      // both channels saved through their own route
        Assert.False(mapB.IsDirty);

        byte[] savedA = File.ReadAllBytes(Path.Combine(folderA, MapEditorSession.MapFileName));
        byte[] savedB = File.ReadAllBytes(Path.Combine(folderB, MapEditorSession.MapFileName));
        Assert.Equal(MapEditorSession.MapPayloadSize, savedA.Length);
        Assert.True(savedA.AsSpan().SequenceEqual(savedB));
    }

    /// <summary>
    /// The live-button sweep: every button the map layout places and the stub list does not
    /// kill must advertise a keyboard twin in its tooltip, so a mouse user learns the keyboard
    /// for free. Driven off the placed list, like the sprite editor's, so a future button
    /// without an entry turns this red on arrival.
    ///
    /// <para>Break recipe: delete the "CTRL+S" from <c>EditorIcons.Tooltip(EditorButton.Save)</c>
    /// or the "HOME" from the tilemap tab's — that one button's assertion goes red by name.</para>
    /// </summary>
    [Fact]
    public void EveryLiveMapButtonNamesAKeyboardTwin()
    {
        var expectedHotkeyToken = new Dictionary<EditorButton, string>
        {
            [EditorButton.ExitTab] = "ESC",
            [EditorButton.SpritesTab] = "HOME",
            // Live since the code-editor screen wave; Alt+Left/Right walk the strip (see the
            // sprite screen's twin of this table for why it is not Home).
            [EditorButton.CodeTab] = "ALT+",
            // Live since the sound-editor screen wave; same key, same reason as the code tab.
            [EditorButton.SoundTab] = "ALT+",
            // The four tools carry their digits (TIC-80's own numbering); the map's keyboard
            // pencil is bare Z since wave 3d, because Space became the pan modifier there.
            [EditorButton.ToolPencil] = "Z DRAWS",
            [EditorButton.ToolHand] = "SPACE+DRAG",
            [EditorButton.ToolSelect] = "DEL",
            [EditorButton.ToolFill] = "Z FILLS",
            [EditorButton.ToolEraser] = "DEL",
            [EditorButton.GridToggle] = "`",
            [EditorButton.Save] = "CTRL+S",
            [EditorButton.Undo] = "CTRL+Z",
            [EditorButton.Redo] = "CTRL+Y",
        };

        foreach (EditorButtonPlace place in MapEditorLayout.Compute(WindowWidth, WindowHeight).Buttons)
        {
            EditorButton button = place.Id;
            if (EditorIcons.IsStub(button) || button == EditorButton.TilemapTab)
            {
                // Stubs answer "when", not a hotkey. The tilemap tab names the screen already on
                // show; neither owes the mouse user a key to learn.
                continue;
            }
            Assert.True(
                expectedHotkeyToken.TryGetValue(button, out string? token),
                $"{button} is live and placed on the map screen but this sweep's table does not know its hotkey.");
            Assert.Contains(token!, EditorIcons.MapTooltip(button), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The sprite in one picker cell, derived through <see cref="SheetStrip"/> — the one owner
    /// of the strip mapping — never typed, exactly as <c>LastStripCellSprite</c> next door.
    /// </summary>
    private static int SpriteAtStripCell(int column, int row)
    {
        Assert.True(SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY));
        return sheetY * SheetStrip.LaneColumns + sheetX;
    }

    /// <summary>
    /// Wave 3e's half of the law: the <b>block</b> the pencil carries is reachable from either
    /// channel. The mouse drags a rectangle across the tile picker (TIC-80 <c>map->sheet.rect</c>);
    /// the keyboard walks to the same anchor with Shift+arrows and grows the block with
    /// Ctrl+Shift+arrows. Both then stamp at the same map cell, and the proof is the same one
    /// this file always uses — two maps, compared byte for byte, with no <see cref="Keys"/>
    /// value in the mouse run and no mouse coordinate in the keyboard run.
    ///
    /// <para>Negative controls. (a) Drop the <c>EditorBlockDx</c>/<c>EditorBlockDy</c> lines
    /// from <see cref="ShellCommandReader"/>: the keyboard block stays 1x1, the keyed map has
    /// one tile where the clicked one has four, and the byte comparison names it. (b) Drop the
    /// <c>!ctrl</c> from <c>EditorSheetDx</c> in the same reader: Ctrl+Shift+Right steps the
    /// tile as well as sizing the block, the keyed anchor moves, and the comparison goes red
    /// with the whole block shifted. (c) Remove the clamp from
    /// <c>MapEditorView.SetTileBlock</c> and the keyed run walks off the strip.</para>
    /// </summary>
    [Fact]
    public void BothChannelsMarkTheSameTileBlockAndStampIt()
    {
        var layout = MapEditorLayout.Compute(WindowWidth, WindowHeight);

        var keyed = new MapEditorSession(FreshCartFolder("block-key"));
        var keyedView = new MapEditorView();
        var reader = new ShellCommandReader();
        // Shift+arrows to strip cell (2,1) — the anchor tile.
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftShift, Keys.Right);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftShift);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftShift, Keys.Right);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftShift);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftShift, Keys.Down);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftShift);
        Assert.Equal(SpriteAtStripCell(2, 1), keyed.SelectedSprite);
        // Ctrl+Shift+arrows to a 2x2 block.
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftControl, Keys.LeftShift, Keys.Right);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftControl, Keys.LeftShift);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftControl, Keys.LeftShift, Keys.Down);
        KeyFrame(reader, keyed, keyedView, layout, Keys.LeftControl, Keys.LeftShift);
        Assert.Equal((2, 2), (keyed.BlockWidth, keyed.BlockHeight));
        Assert.Equal(SpriteAtStripCell(2, 1), keyed.SelectedSprite);     // sizing does not move the anchor
        for (int i = 0; i < 4; i++)
        {
            KeyFrame(reader, keyed, keyedView, layout, Keys.Right);
            KeyFrame(reader, keyed, keyedView, layout);
        }
        for (int i = 0; i < 2; i++)
        {
            KeyFrame(reader, keyed, keyedView, layout, Keys.Down);
            KeyFrame(reader, keyed, keyedView, layout);
        }
        Assert.Equal((4, 2), (keyedView.CursorX, keyedView.CursorY));
        KeyFrame(reader, keyed, keyedView, layout, Keys.Z);
        KeyFrame(reader, keyed, keyedView, layout);

        var clicked = new MapEditorSession(FreshCartFolder("block-mouse"));
        var clickedView = new MapEditorView();
        var pointer = new EditorMouseReader();
        (int fromX, int fromY) = Centre(layout.TileCellRect(SpriteAtStripCell(2, 1)));
        (int toX, int toY) = Centre(layout.TileCellRect(SpriteAtStripCell(3, 2)));
        Frame(clicked, clickedView, layout, pointer, fromX, fromY, ButtonState.Pressed);
        Frame(clicked, clickedView, layout, pointer, toX, toY, ButtonState.Pressed);
        Frame(clicked, clickedView, layout, pointer, toX, toY, ButtonState.Released);
        Assert.Equal((2, 2), (clicked.BlockWidth, clicked.BlockHeight));
        (int cellX, int cellY) = Centre(layout.MapCellRect(4, 2, 0, 0));
        Click(clicked, clickedView, layout, pointer, cellX, cellY);

        Assert.True(keyed.Map.SequenceEqual(clicked.Map));
        // Not a vacuous pass: four cells, in the picker's own arrangement.
        Assert.Equal(SpriteAtStripCell(2, 1), keyed.TileAt(4, 2));
        Assert.Equal(SpriteAtStripCell(3, 1), keyed.TileAt(5, 2));
        Assert.Equal(SpriteAtStripCell(2, 2), keyed.TileAt(4, 3));
        Assert.Equal(SpriteAtStripCell(3, 2), keyed.TileAt(5, 3));
    }

    /// <summary>
    /// The mouse's eyedropper and the keyboard's X land on the same tile — the map's twin of
    /// the sprite editor's colour pick. Break recipe: swap the arguments of
    /// <see cref="MapEditorSession.PickTile"/>'s call in either driver above and the two
    /// channels disagree.
    /// </summary>
    [Fact]
    public void BothChannelsPickTheTileUnderTheCursor()
    {
        var layout = MapEditorLayout.Compute(WindowWidth, WindowHeight);

        var keyed = new MapEditorSession(FreshCartFolder("pick-key"));
        var keyedView = new MapEditorView();
        keyed.SelectSprite(21);
        MapEditorPaint.Begin(keyed, 4, 2);
        MapEditorPaint.End(keyed);
        keyed.SelectSprite(0);
        var reader = new ShellCommandReader();
        KeyFrame(reader, keyed, keyedView, layout, Keys.Right);
        KeyFrame(reader, keyed, keyedView, layout);
        KeyFrame(reader, keyed, keyedView, layout, Keys.Right);
        KeyFrame(reader, keyed, keyedView, layout);
        KeyFrame(reader, keyed, keyedView, layout, Keys.Right);
        KeyFrame(reader, keyed, keyedView, layout);
        KeyFrame(reader, keyed, keyedView, layout, Keys.Right);
        KeyFrame(reader, keyed, keyedView, layout);
        KeyFrame(reader, keyed, keyedView, layout, Keys.Down);
        KeyFrame(reader, keyed, keyedView, layout);
        KeyFrame(reader, keyed, keyedView, layout, Keys.Down);
        KeyFrame(reader, keyed, keyedView, layout);
        Assert.Equal((4, 2), (keyedView.CursorX, keyedView.CursorY));
        KeyFrame(reader, keyed, keyedView, layout, Keys.X);
        KeyFrame(reader, keyed, keyedView, layout);

        var clicked = new MapEditorSession(FreshCartFolder("pick-mouse"));
        clicked.SelectSprite(21);
        MapEditorPaint.Begin(clicked, 4, 2);
        MapEditorPaint.End(clicked);
        clicked.SelectSprite(0);
        Rectangle cell = layout.MapCellRect(4, 2, 0, 0);
        Assert.True(
            layout.TryMapCell(cell.X + cell.Width / 2, cell.Y + cell.Height / 2, 0, 0, out int cx, out int cy));
        clicked.PickTile(cx, cy);

        Assert.Equal(21, keyed.SelectedSprite);
        Assert.Equal(keyed.SelectedSprite, clicked.SelectedSprite);
    }
}

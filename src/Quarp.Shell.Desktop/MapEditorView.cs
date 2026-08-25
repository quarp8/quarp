namespace Quarp.Shell.Desktop;

/// <summary>
/// What the left button on the map canvas does — TIC-80's <c>map->mode</c>, member for member
/// (REFERENCES-EDITORS §3.1: <c>MAP_DRAW_MODE</c>, <c>MAP_DRAG_MODE</c>, <c>MAP_SELECT_MODE</c>,
/// <c>MAP_FILL_MODE</c>), in the order their digit keys run: 1 draw, 2 drag, 3 select, 4 fill.
///
/// <para>It is a <b>view</b> enum, not a session one, by the wave's own rule: nothing here is
/// written to <c>map.bin</c>. The two tools that change the map do so through the session's
/// verbs (<see cref="MapEditorSession.Fill"/>, <see cref="MapEditorSession.ClearArea"/>) —
/// which tool asked is the screen's business and the model's never.</para>
/// </summary>
public enum MapEditorTool
{
    /// <summary>Stamp the selected tile — the wave-3a behaviour, unchanged and still the default.</summary>
    Pencil,

    /// <summary>Left-drag pans the viewport (TIC-80 <c>MAP_DRAG_MODE</c>). Space+drag does the same under any tool.</summary>
    Hand,

    /// <summary>Left-drag marks a rectangle. This wave it is shown and <c>Del</c> empties it; copy/paste is a later one.</summary>
    Select,

    /// <summary>Left click floods the connected run of one tile value (TIC-80 <c>fillMap</c>, LIKO-12 <c>queuedFill</c>).</summary>
    Fill,
}

/// <summary>
/// Who owns the fact "this block of map cells was copied" (wave 3e). An interface with exactly
/// one implementation today — <see cref="MapMemoryClipboard"/>, which keeps the bytes in the
/// shell and never touches the machine's clipboard — and that is the whole point of it existing
/// rather than three fields on the view.
///
/// <para><b>Why not the system clipboard yet.</b> TIC-80 puts the block into the operating
/// system's clipboard as a hex string with a two-byte <c>[w][h]</c> header
/// (REFERENCES-EDITORS §3.1, <c>copySelectionToClipboard</c>), which is what lets an author
/// paste a piece of map into a forum post. That is a decision with a format in it, and it has
/// to be taken once for the sprite editor and the map editor together (§8 item 2) — not
/// smuggled in behind a map wave. Until it is taken, this seam is where it lands: an
/// implementation that reads and writes the OS clipboard replaces the one below, the view is
/// handed it in its constructor, and not one caller of <see cref="MapEditorPaint.CopySelection"/>
/// or <see cref="MapEditorPaint.PasteAt"/> changes.</para>
/// </summary>
public interface IMapClipboard
{
    /// <summary>True when something has been copied and a paste has data to place.</summary>
    bool HasBlock { get; }

    /// <summary>Width of the copied block in map cells; 0 when nothing was copied.</summary>
    int Width { get; }

    /// <summary>Height of the copied block in map cells.</summary>
    int Height { get; }

    /// <summary>The copied tiles, row-major, <see cref="Width"/> bytes per row; empty when nothing was copied.</summary>
    ReadOnlySpan<byte> Tiles { get; }

    /// <summary>Replace the contents. A width or height below one clears the clipboard rather than storing an empty block.</summary>
    void Write(int width, int height, ReadOnlySpan<byte> tiles);
}

/// <summary>
/// The internal clipboard: a copy of the bytes, living as long as the shell does. It survives
/// closing and reopening the map tab only if its owner does — this wave hangs it on
/// <see cref="MapEditorView"/>, so it lives exactly as long as the screen, the same rule the
/// undo history follows (a fresh session opens with Ctrl+Z honestly dead).
/// </summary>
public sealed class MapMemoryClipboard : IMapClipboard
{
    private byte[] _tiles = Array.Empty<byte>();

    public bool HasBlock => _tiles.Length > 0;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public ReadOnlySpan<byte> Tiles => _tiles;

    public void Write(int width, int height, ReadOnlySpan<byte> tiles)
    {
        if (width < 1 || height < 1 || tiles.Length < width * height)
        {
            _tiles = Array.Empty<byte>();
            Width = 0;
            Height = 0;
            return;
        }
        _tiles = tiles[..(width * height)].ToArray();
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Where the author is looking and standing on the map, plus the footer prompt's one bit —
/// the state of the map editor screen that must survive between frames, headless like
/// <see cref="SheetScroll"/> and <see cref="ToolbarFlyout"/> and for the same reason: the
/// shell feeds it keys and clicks, and every claim about it ("paging right eleven times
/// reaches column 255", "the camera never scrolls past the map") is a plain unit test instead
/// of a mouse at a window.
///
/// <para><b>One position, two numbers, one rule.</b> <see cref="CursorX"/>/<see cref="CursorY"/>
/// is the cell the keyboard pencil acts on; <see cref="CameraX"/>/<see cref="CameraY"/> is the
/// top-left cell the viewport shows. They are kept consistent by one private helper: after any
/// move, the camera scrolls the least amount that puts the cursor inside the viewport, and
/// after any camera move the cursor is pulled back inside it. A cursor the author cannot see
/// would paint invisibly — the map is eighty screens of content, so that is not a theoretical
/// worry — and a camera that ignores the cursor would make the keyboard path a lie.</para>
///
/// <para><b>Why the exit prompt lives here and not in the session.</b>
/// <see cref="MapEditorSession"/> is the model of two payloads and deliberately has no screen
/// state (compare <see cref="SpriteEditorSession.ExitPromptShown"/>, which predates that
/// split). The decision itself is not duplicated: <see cref="RequestClose"/> asks the session
/// the one question it owns — <see cref="MapEditorSession.IsDirty"/> — and applies exactly
/// the sprite editor's answer table, so unsaved cells leave only through an explicit Z or X.</para>
/// </summary>
public sealed class MapEditorView
{
    /// <summary>
    /// The screen opens with the internal clipboard. The overload below is the seam the system
    /// clipboard will arrive through (see <see cref="IMapClipboard"/>).
    /// </summary>
    public MapEditorView()
        : this(new MapMemoryClipboard())
    {
    }

    /// <summary>The clipboard named outright — what a test, and one day the system clipboard, hands in.</summary>
    public MapEditorView(IMapClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        Clipboard = clipboard;
    }

    /// <summary>
    /// What has been copied — one owner for both the paste verb and the renderer's floating
    /// block, so "what is on the clipboard" and "what the ghost under the cursor shows" cannot
    /// be two different answers.
    /// </summary>
    public IMapClipboard Clipboard { get; }

    /// <summary>The map cell the keyboard pencil acts on, and what the status bar reads.</summary>
    public int CursorX { get; private set; }

    /// <summary>The cursor's row.</summary>
    public int CursorY { get; private set; }

    /// <summary>Leftmost map column the viewport shows.</summary>
    public int CameraX { get; private set; }

    /// <summary>Topmost map row the viewport shows.</summary>
    public int CameraY { get; private set; }

    /// <summary>True while the dirty-exit question is on the footer line; the shell then gives it the input.</summary>
    public bool ExitPromptShown { get; private set; }

    /// <summary>
    /// The tool the left button holds — one owner, read by the router, the renderer's active
    /// highlight and the button table alike. Opens on the pencil, as TIC-80's map does
    /// (<c>.mode = MAP_DRAW_MODE</c>).
    /// </summary>
    public MapEditorTool Tool { get; private set; } = MapEditorTool.Pencil;

    /// <summary>
    /// Whether the tile grid is drawn over the canvas. <b>On by default</b>, TIC-80's own
    /// choice (<c>.canvas = {.grid = true}</c>, REFERENCES-EDITORS §3.1). The renderer still
    /// refuses to draw it at the smallest map scale — see <c>MapEditorRenderer</c> — because
    /// one line per eight pixels there is more grid than picture.
    /// </summary>
    public bool GridShown { get; private set; } = true;

    /// <summary>True while a marked rectangle exists — what <c>Del</c> asks before emptying anything.</summary>
    public bool HasSelection { get; private set; }

    /// <summary>True between the press and the release of a selection drag; the rectangle is live but not committed.</summary>
    public bool SelectionGestureActive { get; private set; }

    /// <summary>Left column of the marked rectangle. Meaningless while <see cref="HasSelection"/> is false.</summary>
    public int SelectionX { get; private set; }

    /// <summary>Top row of the marked rectangle.</summary>
    public int SelectionY { get; private set; }

    /// <summary>Width of the marked rectangle in cells; 0 when nothing is marked.</summary>
    public int SelectionWidth { get; private set; }

    /// <summary>Height of the marked rectangle in cells; 0 when nothing is marked.</summary>
    public int SelectionHeight { get; private set; }

    /// <summary>True while a pan gesture is open (the hand tool's drag, or Space+drag under any tool).</summary>
    public bool PanActive { get; private set; }

    /// <summary>
    /// True while a copied block is waiting for the click that places it — TIC-80's
    /// <c>drawPasteData</c> state (REFERENCES-EDITORS §3.1: <c>copyFromClipboard</c> leaves the
    /// editor holding a block that lands on the next left button). It is screen state and not
    /// model state: nothing about it reaches <c>map.bin</c> until the block is put down, and
    /// Esc must be able to make it never have happened.
    ///
    /// <para><b>Where the floating block hangs</b> is not a second field: it hangs on
    /// <see cref="CursorX"/>/<see cref="CursorY"/>, the one cursor this screen already has.
    /// The pointer parks that cursor as it moves and the arrows move it too, so the ghost
    /// follows the mouse and the keyboard without either channel owning a position of its
    /// own — which is also why the two channels cannot paste in different places.</para>
    /// </summary>
    public bool PasteFloating { get; private set; }

    /// <summary>True between the press and the release of a drag across the tile picker (wave 3e).</summary>
    public bool TileBlockGestureActive { get; private set; }

    // The map cell the pan gesture grabbed — the camera is moved so this cell stays under the
    // pointer, which is what makes a drag feel like dragging paper rather than a slider.
    private int _panMapX;
    private int _panMapY;

    // The cell the selection drag started on. The rectangle is normalized out of this and the
    // current cell on every sample, so dragging up and left works exactly like down and right.
    private int _selectAnchorX;
    private int _selectAnchorY;

    // The strip cell the picker drag started on — the block is normalized out of this and the
    // current strip cell on every sample, exactly as the map's rectangle is out of its anchor.
    private int _tileAnchorColumn;
    private int _tileAnchorRow;

    /// <summary>
    /// Re-clamp against the current layout — called once per frame, because a window resize
    /// can shrink the camera's ceiling under a standing position and a stale camera would
    /// hit-test cells that are no longer drawn (<see cref="SheetScroll.Clamp"/>'s reason,
    /// one dimension more).
    /// </summary>
    public void Clamp(in MapEditorLayout layout)
    {
        CursorX = Math.Clamp(CursorX, 0, MapEditorLayout.MapColumns - 1);
        CursorY = Math.Clamp(CursorY, 0, MapEditorLayout.MapRows - 1);
        CameraX = Math.Clamp(CameraX, 0, layout.MaxCameraX);
        CameraY = Math.Clamp(CameraY, 0, layout.MaxCameraY);
        FollowCursor(layout);
    }

    /// <summary>
    /// Put the cursor on an absolute map cell — the mouse's hover and press path, so a
    /// following keyboard stroke starts where the pointer left it (the sprite editor's "one
    /// cursor" rule). Out-of-map values are clamped rather than thrown on: the callers are hit
    /// tests, and a clamp here is what lets the session keep throwing.
    /// </summary>
    public void SetCursor(in MapEditorLayout layout, int cellX, int cellY)
    {
        CursorX = Math.Clamp(cellX, 0, MapEditorLayout.MapColumns - 1);
        CursorY = Math.Clamp(cellY, 0, MapEditorLayout.MapRows - 1);
        FollowCursor(layout);
    }

    /// <summary>One arrow press: the cursor by one cell, the camera behind it. The map's ends are ends, not a wrap.</summary>
    public void MoveCursor(in MapEditorLayout layout, int dx, int dy) =>
        SetCursor(layout, CursorX + dx, CursorY + dy);

    /// <summary>
    /// One page of the keyboard's long-distance travel: the cursor moves by a whole viewport,
    /// so the camera follows by exactly one screen. This is the half of the parity law that a
    /// 256x72 map makes non-negotiable — at one cell per press the far corner is 326 presses
    /// away, which is a path that exists on paper and not in a hand.
    /// </summary>
    public void PageCursor(in MapEditorLayout layout, int dxScreens, int dyScreens) =>
        SetCursor(
            layout,
            CursorX + dxScreens * layout.VisibleColumns,
            CursorY + dyScreens * layout.VisibleRows);

    /// <summary>
    /// The minimap's verb: go to this cell. The cursor lands on it and the viewport is centred
    /// on it (then clamped at the map's borders), which is what makes one click anywhere on
    /// the minimap the mouse's whole answer to "take me to the far corner".
    /// </summary>
    public void JumpTo(in MapEditorLayout layout, int cellX, int cellY)
    {
        CursorX = Math.Clamp(cellX, 0, MapEditorLayout.MapColumns - 1);
        CursorY = Math.Clamp(cellY, 0, MapEditorLayout.MapRows - 1);
        CameraX = Math.Clamp(CursorX - layout.VisibleColumns / 2, 0, layout.MaxCameraX);
        CameraY = Math.Clamp(CursorY - layout.VisibleRows / 2, 0, layout.MaxCameraY);
        FollowCursor(layout);
    }

    /// <summary>
    /// The wheel over the canvas: the camera by whole rows, the cursor pulled back inside the
    /// viewport so it never drifts off screen. A convenience over the minimap, not a second
    /// way to reach anything — every writer of the camera ends in the same clamp.
    /// </summary>
    public void ScrollRows(in MapEditorLayout layout, int rows)
    {
        CameraY = Math.Clamp(CameraY + rows, 0, layout.MaxCameraY);
        PullCursorIntoView(layout);
    }

    /// <summary>
    /// Escape, or the exit tab. The exact answer table <see cref="SpriteEditorSession.RequestClose"/>
    /// uses: a prompt already up comes down ("stay"), a dirty map raises it, a clean map lets
    /// the shell leave. The stroke is closed first so an Esc mid-drag judges the map as it
    /// stands rather than half-way through a gesture.
    /// </summary>
    /// <returns>True when the caller may leave this screen.</returns>
    public bool RequestClose(MapEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EndStroke();
        if (ExitPromptShown)
        {
            ExitPromptShown = false;
            return false;
        }
        if (session.IsDirty)
        {
            ExitPromptShown = true;
            return false;
        }
        return true;
    }

    /// <summary>Lowers the prompt after Z or X have been executed — the mode machine's half of the verb.</summary>
    public void CloseExitPrompt() => ExitPromptShown = false;

    /// <summary>
    /// The one door into <see cref="Tool"/> — the digit keys and the tool buttons both come
    /// through here (<see cref="EditorIcons.PressMapToolDigit"/> and
    /// <see cref="EditorIcons.ClickMapButton"/>), so the two channels cannot choose differently.
    /// Any open gesture is closed first: switching tools mid-drag must not leave a pan or a
    /// half-marked rectangle running under the new tool.
    /// </summary>
    public void SelectTool(MapEditorTool tool)
    {
        PanActive = false;
        SelectionGestureActive = false;
        TileBlockGestureActive = false;
        Tool = tool;
    }

    /// <summary>
    /// The picker drag's press: the block starts as the single strip cell under the pointer,
    /// which is exactly today's behaviour for a click that never moves (TIC-80's own start
    /// state, <c>.sheet.rect = {0, 0, 1, 1}</c>). Every sample writes the block through
    /// <paramref name="session"/>, the one owner of "what the pencil puts down".
    /// </summary>
    public void BeginTileBlock(MapEditorSession session, in MapEditorLayout layout, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(session);
        layout.ClampTileStripCell(x, y, out _tileAnchorColumn, out _tileAnchorRow);
        TileBlockGestureActive = true;
        UpdateTileBlock(session, layout, x, y);
    }

    /// <summary>
    /// One more sample of an open picker drag; a no-op without one. The rectangle is normalized
    /// out of the anchor and the current cell, so dragging up and left selects the same block as
    /// dragging down and right — the rule the map's own selection already carries.
    /// </summary>
    public void UpdateTileBlock(MapEditorSession session, in MapEditorLayout layout, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!TileBlockGestureActive)
        {
            return;
        }
        layout.ClampTileStripCell(x, y, out int column, out int row);
        SetTileBlock(
            session,
            Math.Min(_tileAnchorColumn, column),
            Math.Min(_tileAnchorRow, row),
            Math.Abs(column - _tileAnchorColumn) + 1,
            Math.Abs(row - _tileAnchorRow) + 1);
    }

    /// <summary>The picker drag's release: the block stands until a single tile replaces it.</summary>
    public void EndTileBlock() => TileBlockGestureActive = false;

    /// <summary>
    /// The keyboard's half of the picker drag (the parity law): grow or shrink the block by one
    /// strip cell, keeping its top-left tile where it is — Ctrl+Shift+arrows, chosen because
    /// Shift+arrows already steps the tile itself and a chord must not double as its bare key.
    /// The block is clamped to the strip's far edges here, once, so no caller can size a block
    /// that runs off the sheet.
    /// </summary>
    public void StepTileBlock(MapEditorSession session, int deltaWidth, int deltaHeight)
    {
        ArgumentNullException.ThrowIfNull(session);
        SheetStrip.SpriteToStripCell(session.SelectedSprite, out int column, out int row);
        SetTileBlock(
            session,
            column,
            row,
            session.BlockWidth + deltaWidth,
            session.BlockHeight + deltaHeight);
    }

    /// <summary>
    /// The one place a block reaches the session: the strip cell of its top-left corner becomes
    /// the selected sprite and the size travels with it. Clamped to the strip in both axes,
    /// because a block that hung off the sheet would have cells with no sprite behind them.
    /// </summary>
    private static void SetTileBlock(MapEditorSession session, int column, int row, int width, int height)
    {
        column = Math.Clamp(column, 0, SheetStrip.Columns - 1);
        row = Math.Clamp(row, 0, SheetStrip.Rows - 1);
        width = Math.Clamp(width, 1, SheetStrip.Columns - column);
        height = Math.Clamp(height, 1, SheetStrip.Rows - row);
        if (SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY))
        {
            session.SelectSpriteBlock(sheetY * SheetStrip.LaneColumns + sheetX, width, height);
        }
    }

    /// <summary>
    /// Ctrl+V: the copied block starts floating and lands on the next paint press — the click
    /// under the mouse, Z under the keyboard. Refused with an empty clipboard, so a stray Ctrl+V
    /// cannot arm a paste of nothing that then eats the next click.
    /// </summary>
    /// <returns>True when a block is now floating.</returns>
    public bool BeginPaste()
    {
        PasteFloating = Clipboard.HasBlock;
        return PasteFloating;
    }

    /// <summary>
    /// Esc over a floating block: it never happened. Nothing was written while it floated, so
    /// there is nothing to undo — which is the property that makes Esc safe to press.
    /// </summary>
    public void CancelPaste() => PasteFloating = false;

    /// <summary>The block has landed — the state <see cref="MapEditorPaint.PasteAt"/> closes after writing.</summary>
    public void EndPaste() => PasteFloating = false;

    /// <summary>The grid switch, for both the <c>`</c> key and the button — TIC-80's "SHOW/HIDE GRID".</summary>
    public void ToggleGrid() => GridShown = !GridShown;

    /// <summary>
    /// The select tool's press: the rectangle starts as the single cell under the pointer.
    /// A press replaces whatever was marked before, the way a new drag does in every editor.
    /// </summary>
    public void BeginSelection(int cellX, int cellY)
    {
        _selectAnchorX = Math.Clamp(cellX, 0, MapEditorLayout.MapColumns - 1);
        _selectAnchorY = Math.Clamp(cellY, 0, MapEditorLayout.MapRows - 1);
        SelectionGestureActive = true;
        UpdateSelection(_selectAnchorX, _selectAnchorY);
    }

    /// <summary>One more sample of an open selection drag; safe (and a no-op) without one.</summary>
    public void UpdateSelection(int cellX, int cellY)
    {
        if (!SelectionGestureActive)
        {
            return;
        }
        int x = Math.Clamp(cellX, 0, MapEditorLayout.MapColumns - 1);
        int y = Math.Clamp(cellY, 0, MapEditorLayout.MapRows - 1);
        SelectionX = Math.Min(_selectAnchorX, x);
        SelectionY = Math.Min(_selectAnchorY, y);
        SelectionWidth = Math.Abs(x - _selectAnchorX) + 1;
        SelectionHeight = Math.Abs(y - _selectAnchorY) + 1;
        HasSelection = true;
    }

    /// <summary>The selection drag's release: the rectangle stands until something drops it.</summary>
    public void EndSelection() => SelectionGestureActive = false;

    /// <summary>Drops the mark — Esc's meaning over a selection, and what a new tool implies.</summary>
    public void ClearSelection()
    {
        SelectionGestureActive = false;
        HasSelection = false;
        SelectionWidth = 0;
        SelectionHeight = 0;
    }

    /// <summary>
    /// A pan gesture opens on the map cell the pointer grabbed. The camera is not moved here:
    /// the grab only records what must stay under the pointer.
    /// </summary>
    public void BeginPan(int mapColumn, int mapRow)
    {
        _panMapX = mapColumn;
        _panMapY = mapRow;
        PanActive = true;
    }

    /// <summary>
    /// One sample of an open pan: the pointer is now this many cells right of and below the
    /// viewport's top-left corner, so the camera goes where the grabbed cell lands there again.
    /// Clamped at the map's edges like every other writer of the camera, and the cursor is
    /// pulled back into view afterwards, exactly as <see cref="ScrollRows"/> does.
    /// </summary>
    public void PanTo(in MapEditorLayout layout, int columnOffset, int rowOffset)
    {
        if (!PanActive)
        {
            return;
        }
        CameraX = Math.Clamp(_panMapX - columnOffset, 0, layout.MaxCameraX);
        CameraY = Math.Clamp(_panMapY - rowOffset, 0, layout.MaxCameraY);
        PullCursorIntoView(layout);
    }

    /// <summary>The pan gesture's release. Safe without one — releases arrive from off the canvas.</summary>
    public void EndPan() => PanActive = false;

    /// <summary>The least scroll that puts the cursor inside the viewport — both edges, both axes.</summary>
    private void FollowCursor(in MapEditorLayout layout)
    {
        CameraX = Math.Clamp(
            Math.Clamp(CameraX, CursorX - layout.VisibleColumns + 1, CursorX), 0, layout.MaxCameraX);
        CameraY = Math.Clamp(
            Math.Clamp(CameraY, CursorY - layout.VisibleRows + 1, CursorY), 0, layout.MaxCameraY);
    }

    /// <summary>The inverse of <see cref="FollowCursor"/> — used when the camera moved on its own.</summary>
    private void PullCursorIntoView(in MapEditorLayout layout)
    {
        CursorX = Math.Clamp(CursorX, CameraX, CameraX + layout.VisibleColumns - 1);
        CursorY = Math.Clamp(CursorY, CameraY, CameraY + layout.VisibleRows - 1);
    }
}

/// <summary>
/// One keyboard step of the tile picker — Shift+arrows, the keyboard twin of clicking a cell
/// in the map editor's strip. It exists for the reason <see cref="EditorSheetStep"/> exists
/// (the audit lesson of wave 2k): the shell's map dispatch cannot be constructed without a
/// <c>GraphicsDevice</c>, so anything written inline there can only be tested by a copy of
/// itself, and a test that mirrors production code stays green when the original is deleted.
/// Both the shell and the parity instrument call this.
///
/// <para>It is a sibling of <see cref="EditorSheetStep"/> rather than a reuse of it because
/// that one is typed to <see cref="SpriteEditorSession"/> and moves a region anchor, while
/// this one moves a tile number. What must not be duplicated — the strip's lane arithmetic —
/// is not: both walk in strip space through <see cref="SheetStrip"/>, the one owner of the
/// presentation mapping, so the two editors' pickers and both their mouse hit tests aim at the
/// same cell by construction.</para>
/// </summary>
public static class MapEditorTileStep
{
    /// <summary>
    /// Moves the selected tile by one strip cell. A step that would leave the strip is
    /// clamped, not wrapped: the ends of the sheet are ends, the way they are for the mouse.
    /// </summary>
    public static void Apply(MapEditorSession session, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(session);

        SheetStrip.SpriteToStripCell(session.SelectedSprite, out int column, out int row);
        column = Math.Clamp(column + dx, 0, SheetStrip.Columns - 1);
        row = Math.Clamp(row + dy, 0, SheetStrip.Rows - 1);
        if (SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY))
        {
            session.SelectSprite(sheetY * SheetStrip.LaneColumns + sheetX);
        }
    }
}

/// <summary>
/// What the paint button means on the map canvas — keyboard and mouse alike, one dispatch, so
/// the two input worlds cannot drift (the parity law). Same precedent and same reason as
/// <see cref="MapEditorTileStep"/>: this is called by the shell AND by the parity instrument,
/// never mirrored in a test.
///
/// <para>It also owns the one guard the model demands: <see cref="MapEditorSession.PaintTile"/>
/// throws on a read-only map (a cart with map.csv — MAP-FORMAT §4, the text source owns the
/// map), so the door is closed here, once, instead of at every call site that could forget.
/// Picking a tile is not guarded: reading a cell out of a read-only map is exactly what an
/// author needs to do to copy digger's ground into their own cart.</para>
/// </summary>
public static class MapEditorPaint
{
    /// <summary>The button went down on a cell: open the gesture and stamp the selected tile.</summary>
    public static void Begin(MapEditorSession session, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(session);
        Begin(session, cellX, cellY, session.SelectedSprite);
    }

    /// <summary>
    /// The same gesture with the tile named outright — the right button's erase stroke
    /// (LIKO-12 <c>tile.lua</c>: <c>if isMDown(2) then selectedTile = 0 end</c>, applied to the
    /// tool rather than to the picker, so the author's chosen tile survives an erase).
    /// </summary>
    public static void Begin(MapEditorSession session, int cellX, int cellY, int tile)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.MapReadOnly)
        {
            return;
        }
        session.BeginStroke();
        session.PaintTile(cellX, cellY, tile);
    }

    /// <summary>One more sample of an open gesture. Safe without one — a drag can start off the canvas.</summary>
    public static void Continue(MapEditorSession session, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(session);
        Continue(session, cellX, cellY, session.SelectedSprite);
    }

    /// <summary>One more sample of an open gesture, with the tile named — the erase drag's half.</summary>
    public static void Continue(MapEditorSession session, int cellX, int cellY, int tile)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.MapReadOnly || !session.StrokeActive)
        {
            return;
        }
        session.PaintTile(cellX, cellY, tile);
    }

    /// <summary>The button came up: the whole gesture commits as one undo step.</summary>
    public static void End(MapEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EndStroke();
    }

    /// <summary>
    /// The fill tool's click. Same door and same guard as the pencil's: the read-only map is
    /// refused here once rather than at every call site, and the session decides everything
    /// else — including that filling with the tile already there is not an undo step.
    /// </summary>
    public static void Fill(MapEditorSession session, int cellX, int cellY, int tile)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.MapReadOnly)
        {
            return;
        }
        session.Fill(cellX, cellY, tile);
    }

    /// <summary>
    /// <c>Del</c> over a marked rectangle — TIC-80's <c>deleteSelection</c>, one undo step for
    /// the whole rectangle. Reads the mark from the view, which owns it, and writes through the
    /// session, which owns the bytes.
    /// </summary>
    public static void ClearSelection(MapEditorSession session, MapEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        if (session.MapReadOnly || !view.HasSelection)
        {
            return;
        }
        session.ClearArea(view.SelectionX, view.SelectionY, view.SelectionWidth, view.SelectionHeight);
    }

    /// <summary>
    /// The pencil carrying a block (wave 3e): the press stamps the whole
    /// <see cref="MapEditorSession.BlockWidth"/>x<see cref="MapEditorSession.BlockHeight"/>
    /// rectangle instead of one cell. Same door and same guard as <see cref="Begin(MapEditorSession, int, int)"/>,
    /// which it does not replace — the right button's eraser still puts down exactly one tile
    /// (none of the three references gives the eraser a block, and an eraser that wiped four
    /// cells per click would be a different tool).
    /// </summary>
    public static void BeginBlock(MapEditorSession session, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.MapReadOnly)
        {
            return;
        }
        session.BeginStroke();
        StampBlock(session, cellX, cellY);
    }

    /// <summary>One more sample of an open block gesture — the drag's half. Safe without one.</summary>
    public static void ContinueBlock(MapEditorSession session, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.MapReadOnly || !session.StrokeActive)
        {
            return;
        }
        StampBlock(session, cellX, cellY);
    }

    /// <summary>
    /// <b>The block only lands on the lattice of its own size</b> — TIC-80's
    /// <c>processMouseDrawMode</c>, copied deliberately:
    /// <code>if(w % sheet.rect.w == 0 &amp;&amp; h % sheet.rect.h == 0) setMapSprite(...)</code>
    /// where <c>w</c>/<c>h</c> are the map cell under the pointer. A cell that is not a whole
    /// number of blocks from the map's origin is <b>skipped</b>, not snapped and not drawn.
    ///
    /// <para><b>Why a rule that refuses to draw is the right one.</b> Take a 2x2 block dragged
    /// across a row. Without the rule the block would be stamped at columns 4, 5, 6, 7 …, and
    /// each stamp would overwrite the right half of the one before with its own left half: the
    /// author draws a two-by-two tree and gets a column of tree-halves, the block's tiles
    /// shuffled out of their arrangement. Snapping the block to the lattice instead of skipping
    /// would draw a tile the pointer is not on — the pointer says "here" and the editor would
    /// answer "near here" — and would stamp the same block twice as the pointer crossed the
    /// cell boundary. Skipping keeps the invariant that matters: whatever is on the map, the
    /// block's cells sit in the block's own arrangement. A 1x1 block divides everything, so the
    /// ordinary pencil is exactly what it was before this wave.</para>
    /// </summary>
    private static void StampBlock(MapEditorSession session, int cellX, int cellY)
    {
        int width = session.BlockWidth;
        int height = session.BlockHeight;
        if (cellX % width != 0 || cellY % height != 0)
        {
            return;
        }
        Span<byte> tiles = stackalloc byte[SheetStrip.Columns * SheetStrip.Rows];
        BlockTiles(session, tiles);
        session.PaintBlock(cellX, cellY, width, height, tiles);
    }

    /// <summary>
    /// The picker's rectangle resolved into sprite numbers, row-major. This is the one place
    /// the strip mapping and the block meet: the session keeps the block as two numbers of
    /// <em>picker</em> cells (it must not know <see cref="SheetStrip"/>, which lives a layer
    /// above it), and here each cell of that rectangle becomes the sprite the author sees in
    /// it. Cells the strip does not have cannot occur — the view clamps the block inside it —
    /// and are written as the empty tile if they somehow do, rather than throwing at the
    /// author mid-stroke.
    /// </summary>
    /// <returns>How many bytes of <paramref name="destination"/> were filled.</returns>
    public static int BlockTiles(MapEditorSession session, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(session);
        SheetStrip.SpriteToStripCell(session.SelectedSprite, out int anchorColumn, out int anchorRow);
        int width = session.BlockWidth;
        int height = session.BlockHeight;
        if (destination.Length < width * height)
        {
            // Unreachable while the view clamps every block inside the strip (the strip's own
            // cell count is the ceiling, and callers size their buffer by it). Stated rather
            // than trusted: silently filling half a block would stamp a scrambled one.
            throw new ArgumentException(
                $"a {width}x{height} block needs {width * height} bytes, got {destination.Length}.",
                nameof(destination));
        }
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                destination[row * width + column] =
                    SheetStrip.TryStripCellToSheetCell(
                        anchorColumn + column, anchorRow + row, out int sheetX, out int sheetY)
                        ? (byte)(sheetY * SheetStrip.LaneColumns + sheetX)
                        : (byte)MapEditorSession.EmptyTile;
            }
        }
        return width * height;
    }

    /// <summary>
    /// <c>Ctrl+C</c>: the marked rectangle's tiles go to the clipboard the view owns. Allowed on
    /// a read-only map for the same reason the eyedropper is — reading a cell writes nothing,
    /// and copying a piece of someone else's level is exactly what an author needs to do.
    /// </summary>
    /// <returns>True when something was copied.</returns>
    public static bool CopySelection(MapEditorSession session, MapEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        if (!view.HasSelection)
        {
            return false;
        }
        int width = view.SelectionWidth;
        int height = view.SelectionHeight;
        byte[] tiles = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                tiles[row * width + column] = session.TileAt(view.SelectionX + column, view.SelectionY + row);
            }
        }
        view.Clipboard.Write(width, height, tiles);
        return true;
    }

    /// <summary>
    /// <c>Ctrl+X</c>: copy, then empty the same rectangle — TIC-80's own composition
    /// (<c>copy + delete</c>). <b>One</b> undo step, because the emptying is
    /// <see cref="MapEditorSession.ClearArea"/>'s single step and the copy writes no map bytes
    /// at all: Ctrl+Z after a cut restores the map exactly as it stood. On a read-only map the
    /// copy still happens and the emptying does not, which is the same door
    /// <see cref="ClearSelection"/> keeps.
    /// </summary>
    public static void CutSelection(MapEditorSession session, MapEditorView view)
    {
        if (!CopySelection(session, view))
        {
            return;
        }
        ClearSelection(session, view);
    }

    /// <summary>
    /// The floating block lands here (TIC-80 <c>drawPasteData</c>): one undo step, clipped at
    /// the map's borders by <see cref="MapEditorSession.PasteBlock"/> rather than refused, and
    /// the float is over whether or not the map would take it — a read-only map answers the
    /// click by putting the ghost down, which is the honest way to say "not here".
    /// </summary>
    /// <returns>True when the click was consumed by the paste.</returns>
    public static bool PasteAt(MapEditorSession session, MapEditorView view, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        if (!view.PasteFloating)
        {
            return false;
        }
        view.EndPaste();
        IMapClipboard clipboard = view.Clipboard;
        if (!clipboard.HasBlock || session.MapReadOnly)
        {
            return true;
        }
        session.PasteBlock(cellX, cellY, clipboard.Width, clipboard.Height, clipboard.Tiles);
        return true;
    }
}

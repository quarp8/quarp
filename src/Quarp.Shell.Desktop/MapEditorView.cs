namespace Quarp.Shell.Desktop;

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
    /// <summary>The button went down on a cell: open the gesture and stamp the first tile.</summary>
    public static void Begin(MapEditorSession session, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.MapReadOnly)
        {
            return;
        }
        session.BeginStroke();
        session.PaintTile(cellX, cellY);
    }

    /// <summary>One more sample of an open gesture. Safe without one — a drag can start off the canvas.</summary>
    public static void Continue(MapEditorSession session, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.MapReadOnly || !session.StrokeActive)
        {
            return;
        }
        session.PaintTile(cellX, cellY);
    }

    /// <summary>The button came up: the whole gesture commits as one undo step.</summary>
    public static void End(MapEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EndStroke();
    }
}

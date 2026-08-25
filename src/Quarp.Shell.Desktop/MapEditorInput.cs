namespace Quarp.Shell.Desktop;

/// <summary>
/// The map editor's input router — the tilemap twin of <see cref="SpriteEditorInput"/>, moved
/// out of <c>QuarpGame</c> in the same wave (3c) and for the same reason: a dispatch that
/// cannot be constructed in a test is a dispatch nothing can check. It reads the same
/// <see cref="EditorShell"/> the sprite router does, and touches only the two of its members
/// this screen has (the mode machine and the tooltip clock) plus the back buffer's size; the
/// flyout and the sheet's scroll belong to the other face of the editor.
/// </summary>
public static class MapEditorInput
{
    /// <summary>
    /// One frame of the map editor (M9 stage 3). Same law as its sheet-side sibling
    /// <see cref="SpriteEditorInput.Update"/>: every live action has a key path and a click
    /// path, and both funnel into the same owner so neither can drift —
    /// <see cref="MapEditorPaint"/> for the paint gesture,
    /// <see cref="MapEditorTileStep"/> for the picker's keyboard step, <see cref="MapEditorView"/>
    /// for travel, <see cref="EditorIcons.ClickMapButton"/> for the button router. Those types
    /// exist precisely because this method used to live in the window class, where it could
    /// not be constructed in a test at all: what is written only here can only be tested by a
    /// copy of itself (the wave-2k lesson), so what is written here is the dispatch and nothing
    /// else. Wave 3c moved the dispatch itself out here, where a test can call it — the rule
    /// that kept the rules out of it stands unchanged.
    ///
    /// <para><b>Reaching the far corner of a 256x72 map.</b> Keyboard: arrows walk the cursor a
    /// cell at a time, <c>[</c> and <c>]</c> page a whole viewport across, PgUp/PgDn page one
    /// down — the camera follows the cursor, so what the keys move is always on screen. Mouse:
    /// a click anywhere on the minimap jumps there (a drag keeps travelling), and the wheel
    /// rolls the view down. Both channels end in the same <see cref="MapEditorView"/> methods,
    /// which is why the two paths cannot land on different cells.</para>
    ///
    /// <para><b>Four tools and three mouse buttons (wave 3d).</b> Keys 1-4 and the left column
    /// choose pencil / hand / select / fill, TIC-80's <c>map->mode</c> in TIC-80's own order
    /// (REFERENCES-EDITORS §3.1). The buttons then differ from that reference on purpose,
    /// because our right button is already spoken for:
    /// <list type="bullet">
    ///   <item><description><b>Left</b> — the tool.</description></item>
    ///   <item><description><b>Right</b> — erase: stamp tile 0 under the pencil, flood 0 under
    ///   the bucket. LIKO-12's rule (<c>tile.lua</c>: <c>if isMDown(2) then selectedTile = 0
    ///   end</c>), and REFERENCES-EDITORS §7.3's answer to "where is the eraser" — there is
    ///   none, there is a button and a tile numbered zero.</description></item>
    ///   <item><description><b>Middle</b> — the tile eyedropper (TIC-80
    ///   <c>processMouseDrawMode</c>).</description></item>
    ///   <item><description><b>Space + left drag</b> — pan, under any tool. TIC-80 also pans
    ///   with the right button and LIKO-12 with the middle one; both of those are taken here,
    ///   so the one modifier all three references agree on is the one we keep.</description></item>
    /// </list></para>
    ///
    /// <para>While the exit prompt is up it owns the input — Z saves and leaves, X discards,
    /// Esc stays, and the same three verbs are clickable on the prompt line — and everything
    /// else, the pencil included, is deliberately deaf.</para>
    /// </summary>
    public static void Update(
        in EditorShell shell, in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        MapEditorSession map = shell.Modes.MapEditor!;
        MapEditorView view = shell.Modes.MapView!;
        // The same layout the renderer will draw this frame — geometry has one owner.
        var layout = MapEditorLayout.Compute(shell.BackBufferWidth, shell.BackBufferHeight);
        // A resize can shrink the camera's ceiling under a standing position; re-clamping here
        // keeps every hit test below inside the drawn viewport.
        view.Clamp(layout);

        if (view.ExitPromptShown)
        {
            shell.Hover.Update(null, elapsedSeconds);        // dead buttons must not grow tooltips
            if (commands.Quit)
            {
                shell.Modes.HandleEscape();                  // Esc lowers the prompt: "stay"
            }
            else if (commands.MenuConfirm)
            {
                shell.Modes.SaveMapAndClose();
            }
            else if (commands.MenuEditor)
            {
                shell.Modes.DiscardMapAndClose();
            }
            else if (mouse.LeftPressed && layout.TryPromptVerb(mouse.X, mouse.Y, out EditorPromptVerb verb))
            {
                switch (verb)
                {
                    case EditorPromptVerb.SaveAndExit:
                        shell.Modes.SaveMapAndClose();
                        break;
                    case EditorPromptVerb.Discard:
                        shell.Modes.DiscardMapAndClose();
                        break;
                    default:
                        shell.Modes.HandleEscape();
                        break;
                }
            }
            return;
        }

        if (commands.ToStart)
        {
            shell.Modes.ToggleEditorTab();       // Home: back to the sprites of the same cart
            return;
        }
        if (commands.Quit)
        {
            if (view.HasSelection)
            {
                // The sprite screen's rung, one map over: a live mark eats the next Esc. Being
                // thrown out of the editor because the last thing you did was draw a rectangle
                // is the bug that rule exists to prevent.
                view.ClearSelection();
                return;
            }
            shell.Modes.HandleEscape();          // clean → the sheet or the library; dirty → the prompt
            return;
        }

        if (commands.EditorUndo)
        {
            map.Undo();
        }
        if (commands.EditorRedo)
        {
            map.Redo();
        }
        if (commands.EditorSave)
        {
            map.Save();
        }
        if (commands.EditorGridToggle)
        {
            view.ToggleGrid();              // ` — the grid button's keyboard twin (TIC-80's key)
        }
        if (commands.EditorToolDigit != 0)
        {
            // 1-4 pick a tool, through the map's own digit table; 5 and 6 are the sprite
            // editor's alone and do nothing here.
            EditorIcons.PressMapToolDigit(view, commands.EditorToolDigit);
        }
        if (commands.EditorClear)
        {
            // Del has TIC-80's meaning first (deleteSelection — empty the marked rectangle in
            // one undo step) and falls back to the older one (select tile 0, the empty-tile
            // button's twin) only when nothing is marked. Two readings, one key, in the order
            // an author expects: the thing on screen wins.
            if (view.HasSelection)
            {
                MapEditorPaint.ClearSelection(map, view);
            }
            else
            {
                map.SelectSprite(MapEditorSession.EmptyTile);
            }
        }

        // Long-distance travel, the half of the parity law a map this size makes non-optional.
        if (commands.Slower)
        {
            view.PageCursor(layout, -1, 0);
        }
        if (commands.Faster)
        {
            view.PageCursor(layout, 1, 0);
        }
        if (commands.EditorLayerUp)
        {
            view.PageCursor(layout, 0, -1);
        }
        if (commands.EditorLayerDown)
        {
            view.PageCursor(layout, 0, 1);
        }
        if (mouse.WheelDelta != 0 && layout.Canvas.Contains(mouse.X, mouse.Y))
        {
            view.ScrollRows(layout, -mouse.WheelDelta / 120);
        }

        // Space is the pan modifier on this screen (TIC-80's map.c), and it is also half of
        // EditorPaintDown — so here it takes the key, the way Ctrl takes Z in the reader: no
        // paint gesture opens while it is held, and any gesture already open is closed rather
        // than smeared across the pan.
        bool panning = commands.EditorPanModifier;

        // Shift+arrows pick the tile, bare arrows steer the map cursor: one frame means one of
        // the two, the same guard (and the same reason) as the sprite editor's.
        bool steppedPicker = commands.EditorSheetDx != 0 || commands.EditorSheetDy != 0;
        if (steppedPicker)
        {
            MapEditorTileStep.Apply(map, commands.EditorSheetDx, commands.EditorSheetDy);
        }
        int dx = steppedPicker ? 0 : (commands.MenuRight ? 1 : 0) - (commands.MenuLeft ? 1 : 0);
        int dy = steppedPicker ? 0 : (commands.MenuDown ? 1 : 0) - (commands.MenuUp ? 1 : 0);
        if (dx != 0 || dy != 0)
        {
            view.MoveCursor(layout, dx, dy);
            if (commands.EditorPaintDown && !panning)
            {
                // Held pencil + arrows = a dragged stroke; held Z + arrows under the select
                // tool = a dragged rectangle. One held key, whatever the tool means by it.
                if (view.Tool == MapEditorTool.Select)
                {
                    view.UpdateSelection(view.CursorX, view.CursorY);
                }
                else
                {
                    MapEditorPaint.Continue(map, view.CursorX, view.CursorY);
                }
            }
        }
        if (commands.EditorPaintPressed && !panning)
        {
            KeyboardAct(map, view);
        }
        if (commands.EditorPaintReleased || panning)
        {
            MapEditorPaint.End(map);
            view.EndSelection();
        }
        if (commands.MenuEditor)
        {
            map.PickTile(view.CursorX, view.CursorY);    // X — the keyboard eyedropper
        }

        // Hover: buttons only on this screen. The picker and the minimap have no HoverTarget
        // kind of their own — that type is shared chrome and grows by the organizer's hand,
        // not by a fork here; their key paths are named on the pencil's tooltip meanwhile.
        shell.Hover.Update(
            layout.TryButton(mouse.X, mouse.Y, out EditorButton hovered)
                ? HoverTarget.OfButton(hovered)
                : null,
            elapsedSeconds);

        // The one cursor: the pointer over the canvas parks it, so the status bar reads the
        // pointer and a following keyboard stroke starts there.
        if (layout.TryMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int overX, out int overY))
        {
            view.SetCursor(layout, overX, overY);
        }

        // The middle button is the tile eyedropper (TIC-80 processMouseDrawMode: the tile under
        // the cursor goes back into the picker). It is checked before the left button's chain
        // because it is not part of it — a wheel-click is its own gesture and never a drag.
        if (mouse.MiddlePressed
            && layout.TryMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int sampleX, out int sampleY))
        {
            map.PickTile(sampleX, sampleY);
        }

        if (mouse.LeftPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton pressed))
            {
                if (!EditorIcons.IsStub(pressed) && HandleMapButton(shell, map, view, pressed))
                {
                    return;                 // the exit or a tab may have left this mode
                }
            }
            else if (layout.TryTileCell(mouse.X, mouse.Y, out int sprite))
            {
                map.SelectSprite(sprite);
            }
            else if (layout.TryMinimapCell(mouse.X, mouse.Y, out int jumpX, out int jumpY))
            {
                view.JumpTo(layout, jumpX, jumpY);
            }
            else if (layout.TryMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int pressX, out int pressY))
            {
                MousePressOnCanvas(map, view, layout, mouse, panning, pressX, pressY);
            }
        }
        else if (mouse.LeftDown && view.PanActive)
        {
            view.PanTo(layout, layout.CanvasColumnOffset(mouse.X), layout.CanvasRowOffset(mouse.Y));
        }
        else if (mouse.LeftDown && view.SelectionGestureActive)
        {
            layout.ClampMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int markX, out int markY);
            view.SetCursor(layout, markX, markY);
            view.UpdateSelection(markX, markY);
        }
        else if (mouse.LeftDown && map.StrokeActive)
        {
            // Drags are clamped to the viewport: a stroke that wanders off the edge keeps
            // painting along it instead of tearing, which is what upholds PaintTile's contract.
            layout.ClampMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int dragX, out int dragY);
            view.SetCursor(layout, dragX, dragY);
            MapEditorPaint.Continue(map, dragX, dragY);
        }
        else if (mouse.LeftDown && layout.TryMinimapCell(mouse.X, mouse.Y, out int dragToX, out int dragToY))
        {
            view.JumpTo(layout, dragToX, dragToY);      // a held button on the minimap keeps travelling
        }
        if (mouse.LeftReleased)
        {
            MapEditorPaint.End(map);
            view.EndPan();
            view.EndSelection();
        }

        // The right button erases (REFERENCES-EDITORS §7.3 — LIKO-12's tile.lua forces the
        // tile to 0 under button 2, for the pencil and the bucket alike). It does NOT pan:
        // TIC-80 pans with it because it has no eraser on it, and one button cannot honestly
        // do both. Under the hand and the select tools it does nothing at all — there is no
        // drawing there for the modifier to modify.
        if (mouse.RightPressed
            && layout.TryMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int eraseX, out int eraseY))
        {
            if (view.Tool == MapEditorTool.Fill)
            {
                MapEditorPaint.Fill(map, eraseX, eraseY, MapEditorSession.EmptyTile);
            }
            else if (view.Tool == MapEditorTool.Pencil)
            {
                MapEditorPaint.Begin(map, eraseX, eraseY, MapEditorSession.EmptyTile);
            }
        }
        else if (mouse.RightDown && !mouse.LeftDown && map.StrokeActive
            && view.Tool == MapEditorTool.Pencil)
        {
            // Only while the left button is up: both buttons held is not a gesture this editor
            // defines, and letting the erase drag ride on a left stroke would wipe the very
            // cells that stroke is painting.
            layout.ClampMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int wipeX, out int wipeY);
            view.SetCursor(layout, wipeX, wipeY);
            MapEditorPaint.Continue(map, wipeX, wipeY, MapEditorSession.EmptyTile);
        }
        if (mouse.RightReleased)
        {
            MapEditorPaint.End(map);
        }
    }

    /// <summary>
    /// What the keyboard's paint key means under each tool — the twin of
    /// <see cref="MousePressOnCanvas"/>, and the reason both exist as named methods: the parity
    /// law is only a law if the two channels reach the same verbs, and two switch statements in
    /// one method drift the moment a tool is added to one of them.
    /// </summary>
    private static void KeyboardAct(MapEditorSession map, MapEditorView view)
    {
        switch (view.Tool)
        {
            case MapEditorTool.Fill:
                MapEditorPaint.Fill(map, view.CursorX, view.CursorY, map.SelectedSprite);
                break;
            case MapEditorTool.Select:
                view.BeginSelection(view.CursorX, view.CursorY);
                break;
            case MapEditorTool.Hand:
                break;      // travel is the arrows' and [ ]'s job; the key has nothing to add
            default:
                MapEditorPaint.Begin(map, view.CursorX, view.CursorY);
                break;
        }
    }

    /// <summary>
    /// What a left press on the canvas means: a pan when Space is held or the hand is the tool
    /// (TIC-80 gives both the same <c>MAP_DRAG_MODE</c> behaviour), otherwise the tool's own
    /// verb. The pan grabs a map cell rather than a pixel, so the camera can move in the whole
    /// cells it is measured in and the grabbed cell still lands back under the pointer.
    /// </summary>
    private static void MousePressOnCanvas(
        MapEditorSession map, MapEditorView view, in MapEditorLayout layout, in EditorMouse mouse,
        bool panning, int cellX, int cellY)
    {
        if (panning || view.Tool == MapEditorTool.Hand)
        {
            view.BeginPan(
                view.CameraX + layout.CanvasColumnOffset(mouse.X),
                view.CameraY + layout.CanvasRowOffset(mouse.Y));
            return;
        }
        switch (view.Tool)
        {
            case MapEditorTool.Fill:
                MapEditorPaint.Fill(map, cellX, cellY, map.SelectedSprite);
                break;
            case MapEditorTool.Select:
                view.BeginSelection(cellX, cellY);
                break;
            default:
                MapEditorPaint.Begin(map, cellX, cellY);
                break;
        }
    }

    /// <summary>
    /// The map screen's twin of <see cref="SpriteEditorInput"/>'s own <c>HandleEditorButton</c>:
    /// tabs first (travel is the mode machine's verb), then
    /// <see cref="EditorIcons.ClickMapButton"/>, the headless routing table the map's
    /// button-contract test clicks every placed button through.
    /// Returns true when the click left this mode, so the caller stops touching a session that
    /// may no longer be on screen.
    /// </summary>
    private static bool HandleMapButton(
        in EditorShell shell, MapEditorSession map, MapEditorView view, EditorButton button)
    {
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            shell.Modes.SwitchEditorTab(tab);
            return shell.Modes.Mode != ShellMode.MapEditor;
        }
        if (EditorIcons.ClickMapButton(map, view, button))
        {
            shell.Modes.HandleEscape();
            return true;
        }
        return false;
    }
}

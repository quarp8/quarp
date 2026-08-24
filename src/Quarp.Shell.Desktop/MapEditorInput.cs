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
        if (commands.EditorClear)
        {
            map.SelectSprite(0);            // Del — the empty-tile button's keyboard twin
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
            if (commands.EditorPaintDown)
            {
                MapEditorPaint.Continue(map, view.CursorX, view.CursorY);   // held pencil + arrows = a dragged stroke
            }
        }
        if (commands.EditorPaintPressed)
        {
            MapEditorPaint.Begin(map, view.CursorX, view.CursorY);
        }
        if (commands.EditorPaintReleased)
        {
            MapEditorPaint.End(map);
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

        if (mouse.LeftPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton pressed))
            {
                if (!EditorIcons.IsStub(pressed) && HandleMapButton(shell, map, pressed))
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
                MapEditorPaint.Begin(map, pressX, pressY);
            }
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
        }
        if (mouse.RightPressed
            && layout.TryMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int pickX, out int pickY))
        {
            map.PickTile(pickX, pickY);
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
    private static bool HandleMapButton(in EditorShell shell, MapEditorSession map, EditorButton button)
    {
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            shell.Modes.SwitchEditorTab(tab);
            return shell.Modes.Mode != ShellMode.MapEditor;
        }
        if (EditorIcons.ClickMapButton(map, button))
        {
            shell.Modes.HandleEscape();
            return true;
        }
        return false;
    }
}

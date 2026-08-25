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
    /// <para><b>Blocks and the clipboard (wave 3e).</b> A drag across the tile picker selects a
    /// rectangle of tiles instead of one (TIC-80 <c>map->sheet.rect</c>, "любой размер N×M"),
    /// Ctrl+Shift+arrows are its keyboard twin, and the pencil then stamps the whole block —
    /// only on the lattice of its own size, which is <see cref="MapEditorPaint"/>'s rule and is
    /// explained there. Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste the marked rectangle of the
    /// map; a pasted block floats under the cursor until the next paint press puts it down (the
    /// click under the mouse, Z under the keyboard — one verb, <see cref="MapEditorPaint.PasteAt"/>,
    /// reached by both), and Esc drops it without writing anything.</para>
    ///
    /// <para><b>Two overlays and one bar (wave R3, ADR-029).</b> The screen moved onto the
    /// console, where 64 rows of content hold either a map worth looking at or a tile palette
    /// but never both — <see cref="MapEditorLayout"/> carries the whole arithmetic. So the
    /// palette became an overlay (hold Shift, or latch it with its button; the wheel over it
    /// flips its two pages) and the minimap became a mode (<c>Tab</c>, or its button; a click on
    /// it travels), and the three rows under the viewport became a position bar whose press
    /// travels through the very <see cref="MapEditorView.JumpTo"/> the minimap uses. Esc's first
    /// meaning is now "put away whatever is over the map". None of that changed a rule: the
    /// dispatch below asks the layout which rectangle a point fell in exactly as it did, and the
    /// layout answers <c>Rectangle.Empty</c> for a panel that is not up, so a lowered
    /// palette is deaf without a branch.</para>
    ///
    /// <para><b>Transforms, replace and the mute controls (the tooltip wave).</b> Three keys and
    /// one modifier, all four taken from the references and all four checked against this screen
    /// before they were taken:
    /// <list type="bullet">
    ///   <item><description><b>F / V / R</b> — flip the marked rectangle across, flip it down,
    ///   rotate it 90°. PICO-8's three keys for these three verbs (REFERENCES-EDITORS §2.3, §8
    ///   item 10) and this shell's own sprite-editor keys for them. Free here: all three are
    ///   read under a <c>!ctrl</c> guard, so Ctrl+V stays the paste, and nothing on the map
    ///   screen read F, V or R before. A non-square rectangle refuses the rotate in words — see
    ///   <see cref="MapEditorSession.RotateAreaClockwise"/> for why refusing is the answer.</description></item>
    ///   <item><description><b>Ctrl over the bucket</b> — replace that tile everywhere
    ///   (TIC-80 <c>replaceTile</c>, §3.1 and §8 item 6), bounded by the marked rectangle when
    ///   there is one and by the whole map when there is not. Same key and same tool as the
    ///   sprite editor's <c>ReplaceColor</c>. On the keyboard the chord is <b>Ctrl+Space</b>,
    ///   because bare Z is this screen's paint key and Ctrl+Z is the shell's undo.</description></item>
    /// </list>
    /// The same wave gave the four buttonless controls a hover label of their own
    /// (<see cref="MapRegion"/>), which is where the gestures that belong to no button — Shift
    /// for the palette, Tab for the whole map, Ctrl+Shift+arrows for the block, the middle
    /// button, Space+drag, <c>`</c> — are now announced.</para>
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
        // Wave R3: the held half of "SHOW TILES [shift]" is written into the view BEFORE the
        // layout is measured, so the frame that draws the palette is the frame that hit-tests
        // it. The latched half is the button's and survives frames on its own.
        view.SetTilesHeld(commands.EditorTilesModifier);
        // The same layout the renderer will draw this frame — geometry has one owner. Since
        // wave R3 that geometry also depends on what stands over the map and on which palette
        // page the selected tile is on, and both come from the same two objects the renderer is
        // handed, so the picture and the clicks cannot be measured differently.
        var layout = MapEditorLayout.Compute(
            shell.BackBufferWidth, shell.BackBufferHeight, view.Overlay, map.SelectedSprite);
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

        // F1..F5 jump straight to a named editor — TIC-80's own five keys for exactly this
        // (REFERENCES-EDITORS §8 item 16). Read beside the tab strip's Alt+arrows and before
        // everything else for the same reason those are: travel is a question about WHICH
        // SCREEN, and it must not be answered by a screen that is already being left. The five
        // routers carry the same four lines because Alt+Left/Right is routed the same way — one
        // line per screen — and a key that worked on one editor and not on its neighbour is
        // worse than a key that does not exist. <see cref="EditorIcons.EditorTabForNumber"/> is
        // the single owner of "which number is which tab"; nothing here counts tabs.
        if (EditorIcons.EditorTabForNumber(commands.EditorTabJump) is ShellMode named)
        {
            shell.Modes.SwitchEditorTab(named);
            return;
        }
        if (commands.EditorTabPrev || commands.EditorTabNext)
        {
            // Alt+Left/Right walk the whole strip, code tab included (REFERENCES-EDITORS §8
            // item 16). Home still flips between the two graphics faces — the key the author's
            // hand already knows — and this is the road to the third one.
            shell.Modes.CycleEditorTab(commands.EditorTabNext ? 1 : -1);
            return;
        }
        if (commands.ToStart)
        {
            shell.Modes.ToggleEditorTab();       // Home: back to the sprites of the same cart
            return;
        }
        if (commands.EditorRegionCycle)
        {
            // Tab is the whole-map view's key on this screen (TIC-80 processKeyboard: "Tab —
            // WORLD MODE"). It is the sprite editor's region-size cycle in the same frame
            // struct; the map has no region to size, so the two screens read one key as their
            // own verb, the way Home already does.
            view.ToggleWorld();
            return;
        }
        if (commands.Quit)
        {
            if (view.CloseOverlay())
            {
                // Esc's first meaning while something stands over the map: put it away. The
                // same rung the floating block and the live selection already stand on, and
                // above them because it is the thing the author is looking at.
                return;
            }
            if (view.PasteFloating)
            {
                // Esc's first meaning while a block floats: drop it (TIC-80's paste is armed
                // until it lands or is dismissed). Nothing was written while it floated, so
                // this leaves no undo step and no dirt — the property that makes it safe.
                view.CancelPaste();
                return;
            }
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
        // The clipboard chords (wave 3e), now through the MACHINE's clipboard as hex text
        // (REFERENCES-EDITORS §8 item 2). This router is the only piece of the map screen that
        // knows a clipboard exists: it takes the string the paint verbs hand back and gives it
        // to the device, and hands the device's string back on a paste. Everything below —
        // session, view, paint — is still headless and still speaks in plain strings.
        // A copy of nothing writes nothing: an empty string must not silently wipe whatever the
        // author had on the clipboard from another program.
        if (commands.EditorCopy)
        {
            shell.CopyText(MapEditorPaint.CopySelectionToText(map, view));
        }
        if (commands.EditorCut)
        {
            shell.CopyText(MapEditorPaint.CutSelectionToText(map, view));
        }
        if (commands.EditorPaste)
        {
            MapEditorPaint.PasteText(map, view, shell.PasteText());
        }
        if (commands.EditorBlockDx != 0 || commands.EditorBlockDy != 0)
        {
            // Ctrl+Shift+arrows size the picker's block — the keyboard twin of dragging a
            // rectangle across the tile picker.
            view.StepTileBlock(map, commands.EditorBlockDx, commands.EditorBlockDy);
        }
        if (commands.EditorGridToggle)
        {
            view.ToggleGrid();              // ` — the grid button's keyboard twin (TIC-80's key)
        }
        // F / V / R over the marked rectangle (REFERENCES-EDITORS §8 item 10). The three keys
        // PICO-8 spends on exactly these three verbs and the three this shell's sprite editor
        // already spends on them — checked one by one against this screen before they were
        // taken: F, V and R are read by ShellCommandReader under a !ctrl guard, and nothing on
        // the map screen read any of the three (Ctrl+V is the paste and stays the paste, because
        // the bare-V field is guarded against it at the reader). Refusals — nothing marked, a
        // non-square rectangle under R — are sentences on the message line, not exceptions, and
        // MapEditorPaint owns which one is said.
        if (commands.EditorFlipH)
        {
            MapEditorPaint.FlipSelectionHorizontal(map, view);
        }
        if (commands.EditorFlipV)
        {
            MapEditorPaint.FlipSelectionVertical(map, view);
        }
        if (commands.EditorRotate)
        {
            MapEditorPaint.RotateSelectionClockwise(map, view);
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
        if (mouse.WheelDelta != 0 && layout.Sheet.Contains(mouse.X, mouse.Y))
        {
            // The wheel over the palette flips its page — the mouse's road to the other 128
            // tiles. It moves the SELECTION and not a page counter, which is what keeps the
            // palette's page derived (MapEditorLayout.PaletteLane) instead of stored: the same
            // cell of the other page, with the block in hand unchanged.
            MapEditorTileStep.Page(map);
        }
        else if (mouse.WheelDelta != 0 && layout.Canvas.Contains(mouse.X, mouse.Y))
        {
            view.ScrollRows(layout, -mouse.WheelDelta / 120);
        }

        // Ctrl over the bucket is "replace this tile" (REFERENCES-EDITORS §8 item 6) — the same
        // key, the same tool and the same verb the sprite editor's bucket already carries, so
        // the author learns one rule for both banks.
        bool replacing = commands.EditorReplaceModifier;

        // Space is the pan modifier on this screen (TIC-80's map.c), and it is also half of
        // EditorPaintDown — so here it takes the key, the way Ctrl takes Z in the reader: no
        // paint gesture opens while it is held, and any gesture already open is closed rather
        // than smeared across the pan.
        //
        // ...unless Ctrl is down with it, and that exception is what gives the replace a
        // KEYBOARD path at all. The map's paint key is bare Z, and Ctrl+Z is the undo on every
        // screen in this shell, so "Ctrl + the paint key" can only be Ctrl+Space here — which is
        // precisely the chord the sprite editor already documents for its own Ctrl-over-the-tool
        // gesture (ShellCommands.EditorShapeFill: "the keyboard's filled-shape gesture is
        // Space+Ctrl"). Nothing is lost: a pan is a drag, and holding Ctrl through a drag was
        // never a gesture this screen defined.
        bool panning = commands.EditorPanModifier && !replacing;

        // Shift+arrows pick the tile, bare arrows steer the map cursor: one frame means one of
        // the two, the same guard (and the same reason) as the sprite editor's.
        bool steppedPicker = commands.EditorSheetDx != 0 || commands.EditorSheetDy != 0;
        if (steppedPicker)
        {
            MapEditorTileStep.Apply(map, commands.EditorSheetDx, commands.EditorSheetDy);
        }
        // An arrow that sized the block is spent on the block, exactly as one that stepped the
        // picker is spent on the picker: the bare-arrow fields fire on any modifier, so the
        // frame that means "grow the block" must not also walk the map cursor.
        bool aimedAtPicker = steppedPicker
            || commands.EditorBlockDx != 0 || commands.EditorBlockDy != 0;
        int dx = aimedAtPicker ? 0 : (commands.MenuRight ? 1 : 0) - (commands.MenuLeft ? 1 : 0);
        int dy = aimedAtPicker ? 0 : (commands.MenuDown ? 1 : 0) - (commands.MenuUp ? 1 : 0);
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
                    MapEditorPaint.ContinueBlock(map, view.CursorX, view.CursorY);
                }
            }
        }
        if (commands.EditorPaintPressed && !panning && layout.CanvasLive)
        {
            // Deaf while an overlay owns the screen, for the reason the mouse is (the layout's
            // CanvasLive): a stroke the author cannot see land is a stroke he did not aim.
            KeyboardAct(map, view, replacing);
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

        // Hover: buttons first, then this screen's four buttonless controls — the same order the
        // press chain below tests them in, so the label and the click always name the same thing
        // (SfxEditorInput.Pointer's rule, one screen over). Until the tooltip wave this screen
        // could only build OfButton and the canvas, the palette, the whole-map view and the
        // position bar were mute; MapRegion is what gave them a hover kind of their own
        // (REFERENCES-EDITORS §8 item 15), and MapEditorLayout.RegionAt is the one hit test.
        shell.Hover.Update(
            layout.TryButton(mouse.X, mouse.Y, out EditorButton hovered)
                ? HoverTarget.OfButton(hovered)
                : layout.RegionAt(mouse.X, mouse.Y) is MapRegion region and not MapRegion.None
                    ? HoverTarget.OfMapRegion(region)
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
            else if (layout.TryTileStripCell(mouse.X, mouse.Y, out _, out _))
            {
                // A press on the picker opens a block drag (wave 3e). A press that never moves
                // ends as a 1x1 block, which is the single-tile click this replaced — the drag
                // is a generalization of it, not a second way to choose a tile.
                view.BeginTileBlock(map, layout, mouse.X, mouse.Y);
            }
            else if (layout.TryMinimapCell(mouse.X, mouse.Y, out int jumpX, out int jumpY))
            {
                view.JumpTo(layout, jumpX, jumpY);
            }
            else if (layout.TrySliderColumn(mouse.X, mouse.Y, out int barColumn))
            {
                view.JumpTo(layout, barColumn, view.CursorY);   // the same verb the minimap uses
            }
            else if (layout.TryMapCell(mouse.X, mouse.Y, view.CameraX, view.CameraY, out int pressX, out int pressY))
            {
                MousePressOnCanvas(map, view, layout, mouse, panning, replacing, pressX, pressY);
            }
        }
        else if (mouse.LeftDown && view.TileBlockGestureActive)
        {
            // Checked before the canvas gestures: a picker drag owns the button until it is
            // released, even when the pointer wanders over the map (the strip cell is clamped,
            // so the block keeps sizing along the picker's edge).
            view.UpdateTileBlock(map, layout, mouse.X, mouse.Y);
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
            MapEditorPaint.ContinueBlock(map, dragX, dragY);
        }
        else if (mouse.LeftDown && layout.TryMinimapCell(mouse.X, mouse.Y, out int dragToX, out int dragToY))
        {
            view.JumpTo(layout, dragToX, dragToY);      // a held button on the minimap keeps travelling
        }
        else if (mouse.LeftDown && layout.TrySliderColumn(mouse.X, mouse.Y, out int dragColumn))
        {
            view.JumpTo(layout, dragColumn, view.CursorY);      // and on the position bar
        }
        if (mouse.LeftReleased)
        {
            MapEditorPaint.End(map);
            view.EndPan();
            view.EndSelection();
            view.EndTileBlock();
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
                // Ctrl rides the right button too: "wipe every tile of this kind" is the erase
                // and the replace composed, and refusing the pair here would make Ctrl mean
                // something under one button of one tool and nothing under the other.
                if (replacing)
                {
                    MapEditorPaint.ReplaceTile(map, view, eraseX, eraseY, MapEditorSession.EmptyTile);
                }
                else
                {
                    MapEditorPaint.Fill(map, eraseX, eraseY, MapEditorSession.EmptyTile);
                }
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
    private static void KeyboardAct(MapEditorSession map, MapEditorView view, bool replacing)
    {
        // A floating block outranks every tool, in both channels: the paste key and the paste
        // click mean "put it here" whatever is in hand, which is what makes Ctrl+V's promise
        // ("the next press places it") true under the hand tool and the bucket alike.
        if (MapEditorPaint.PasteAt(map, view, view.CursorX, view.CursorY))
        {
            return;
        }
        switch (view.Tool)
        {
            case MapEditorTool.Fill:
                if (replacing)
                {
                    MapEditorPaint.ReplaceTile(map, view, view.CursorX, view.CursorY, map.SelectedSprite);
                }
                else
                {
                    MapEditorPaint.Fill(map, view.CursorX, view.CursorY, map.SelectedSprite);
                }
                break;
            case MapEditorTool.Select:
                view.BeginSelection(view.CursorX, view.CursorY);
                break;
            case MapEditorTool.Hand:
                break;      // travel is the arrows' and [ ]'s job; the key has nothing to add
            default:
                MapEditorPaint.BeginBlock(map, view.CursorX, view.CursorY);
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
        bool panning, bool replacing, int cellX, int cellY)
    {
        if (MapEditorPaint.PasteAt(map, view, cellX, cellY))
        {
            return;         // the floating block lands on this click — KeyboardAct's twin
        }
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
                if (replacing)
                {
                    MapEditorPaint.ReplaceTile(map, view, cellX, cellY, map.SelectedSprite);
                }
                else
                {
                    MapEditorPaint.Fill(map, cellX, cellY, map.SelectedSprite);
                }
                break;
            case MapEditorTool.Select:
                view.BeginSelection(cellX, cellY);
                break;
            default:
                MapEditorPaint.BeginBlock(map, cellX, cellY);
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

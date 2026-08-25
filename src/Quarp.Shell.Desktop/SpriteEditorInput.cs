using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Everything the two editor routers are allowed to know about the window they run in (M9 stage
/// 3 wave 3c): four long-lived objects the window owns, by reference, and the back buffer as
/// <b>two plain numbers</b> — which is the whole point of the type.
///
/// <para><b>Why the size is numbers and not a device.</b> Until this wave the routing lived in
/// <c>QuarpGame</c>, which cannot be constructed without a graphics device, and three comments
/// there said so while explaining why one rule after another had to be evicted into a type of
/// its own. The eviction had no end: the dispatch itself was the last thing that could
/// only be tested by a copy of itself. Handing the routers the two integers they actually used
/// ends it — <see cref="SpriteEditorInput"/> and <see cref="MapEditorInput"/> name no MonoGame
/// type at all, so a headless test builds this struct from a window size it made up and drives
/// a whole frame of editing through the production router.</para>
/// </summary>
public readonly record struct EditorShell(
    ShellModeMachine Modes, ToolbarFlyout Flyout, IconHoverTracker Hover, SheetScroll SheetScroll,
    int BackBufferWidth, int BackBufferHeight)
{
    /// <summary>
    /// A copy verb's result onto the machine's clipboard — the one door all four editor routers
    /// use, so the rule below is stated once instead of four times.
    ///
    /// <para><b>An empty string is not written.</b> A copy that had nothing to copy (no
    /// selection, an out-of-range rectangle) must not silently replace whatever the author has
    /// on the clipboard from another program — that would be a Ctrl+C that <em>destroys</em>
    /// data, which is the one thing a copy may never do. The same rule the code editor already
    /// keeps in <see cref="CodeEditorView.Copy"/> ("nothing selected copies nothing").</para>
    /// </summary>
    public void CopyText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Modes.TextClipboard.Write(text);
        }
    }

    /// <summary>What the machine holds, as text — the other half of the same door.</summary>
    public string PasteText() => Modes.TextClipboard.Read();
}

// Wave R2 footnote on those last two numbers: they are the size of the SURFACE the screen is
// laid out on, which for the three host-resolution editors is still the back buffer and for the
// sprite editor is the console itself — 160x90. Nothing in either router had to change for that,
// which is the point: a router lays out and hit-tests in the surface it is handed, and the one
// place that knows which surface a screen lives on is the window class that hands it over
// (QuarpGame.ConsoleEditorContext), together with the matching pointer translation
// (EditorMouse.ToConsole, through FramePlacement). The name stays BackBufferWidth until the last
// screen has moved and there is only one answer left to give it.

/// <summary>
/// The sprite editor's input router: one frame of keys and mouse hits turned into calls on
/// <see cref="SpriteEditorSession"/>, <see cref="ShellModeMachine"/> and the editor's view
/// state. Moved out of <c>QuarpGame</c> in wave 3c with its bodies unchanged; what it gained by
/// moving is that it can be called at all without a window — see <see cref="EditorShell"/> for
/// why, and <c>EditorInputRouterTests</c> for what that buys. It owns dispatch and nothing else:
/// geometry belongs to <see cref="SpriteEditorLayout"/>, the button table to
/// <see cref="EditorIcons"/>, the step to <see cref="EditorSheetStep"/>, editing policy to the
/// session, mode policy to the machine — the law the file it came from stated about itself.
///
/// <para><b>Wave R2 changed nothing in this file's dispatch, on purpose.</b> The screen moved
/// onto the console (ADR-029): its layout is now measured in console pixels and the pointer
/// arrives already translated. Both facts reach this router as the two numbers and the two
/// coordinates it already took, so not one branch below had to learn about scale — which is the
/// evidence that the geometry really does have a single owner. Where it does ask the layout
/// about pixels (<see cref="SpriteEditorLayout.TryCanvasPixel"/>, <c>Sheet.Contains</c>,
/// <c>TrySheetCell</c>, the flyout and prompt hit tests) it asks in the surface it was given,
/// and the surface is whatever the caller drew on.</para>
/// </summary>
public static class SpriteEditorInput
{
    /// <summary>
    /// One frame of the sprite editor: routes keys and mouse hits into the session, whose
    /// policy the headless tests own. Input parity is the law of this frame (M9 stage 2.5) —
    /// every live action has a key path and a click path, and both funnel into the same
    /// session method so neither can drift. While the exit prompt is up it owns the input —
    /// Z saves and leaves, X discards and leaves, Esc stays, and the same three verbs are
    /// clickable on the prompt line — and everything else (including the pencil) is
    /// deliberately deaf, so a stray click cannot change the sheet mid-decision.
    /// </summary>
    public static void Update(
        in EditorShell shell, in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        SpriteEditorSession editor = shell.Modes.Editor!;
        // The screen's view state — the canvas grid switch and the sheet-block drag. Never null
        // (see ShellModeMachine.SpriteView), so no path below has to guard it.
        SpriteEditorView view = shell.Modes.SpriteView;
        // The same layout the renderer will draw this frame — geometry has one owner.
        var layout = SpriteEditorLayout.Compute(
            shell.BackBufferWidth, shell.BackBufferHeight, editor.RegionCells);
        // A resize can shrink the scroll ceiling under a standing offset; re-clamping here
        // keeps every hit test below inside the drawn slice.
        shell.SheetScroll.Clamp(layout);

        if (editor.ExitPromptShown)
        {
            shell.Flyout.Close();                        // the prompt owns the screen; a stale flyout under it would ghost-click
            shell.Hover.Update(null, elapsedSeconds);    // dead buttons must not grow tooltips
            if (commands.Quit)
            {
                shell.Modes.HandleEscape();          // Esc lowers the prompt: "stay" — see SpriteEditorSession.RequestClose
            }
            else if (commands.MenuConfirm)
            {
                shell.Modes.SaveEditorAndClose();
            }
            else if (commands.MenuEditor)
            {
                shell.Modes.DiscardEditorAndClose();
            }
            else if (mouse.LeftPressed && layout.TryPromptVerb(mouse.X, mouse.Y, out EditorPromptVerb verb))
            {
                switch (verb)
                {
                    case EditorPromptVerb.SaveAndExit:
                        shell.Modes.SaveEditorAndClose();
                        break;
                    case EditorPromptVerb.Discard:
                        shell.Modes.DiscardEditorAndClose();
                        break;
                    default:
                        shell.Modes.HandleEscape();  // Stay — lowers the prompt, exactly like Esc
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
            // item 16). Home keeps its two-face flip below — the key the author's hand already
            // knows — and this is the road to the third face.
            shell.Modes.CycleEditorTab(commands.EditorTabNext ? 1 : -1);
            return;
        }
        if (commands.ToStart)
        {
            // Home flips to the map of the same cart and back — the keyboard half of the tab
            // strip (M9 stage 3). The mode machine owns the door; this line only delivers the
            // key, and the tab icons' tooltips advertise it.
            shell.Modes.ToggleEditorTab();
            return;
        }
        if (commands.Quit)
        {
            // The order's "Esc-подобная клавиша": with a flyout up, Esc closes it and goes no
            // further — leaving the editor from under an open flyout would punish exploration.
            if (shell.Flyout.OpenSlot is not null)
            {
                shell.Flyout.Close();
                return;
            }
            // A selection eats the next Esc the same way (wave 2f): the mask — or the open
            // grab, whose pixels never left the sheet — drops, and the editor stays. Leaving
            // is what the Esc after that is for.
            if (editor.HasSelection || editor.SelectionGestureActive)
            {
                editor.ClearSelection();
                return;
            }
            shell.Modes.HandleEscape();              // clean → library; dirty → the prompt above
            return;
        }
        // The keyboard's ink: Shift held is the second colour, which is LIKO-12's arrangement to
        // the letter (sprite.lua reads lshift/rshift and mouse button 2 as one and the same
        // "b = 2", REFERENCES-EDITORS §2.2). It is what makes the two-ink rule obey the parity
        // law — every mouse verb below has this key path, the palette walk and the eyedropper's
        // choice of which ink to fill included. Computed once, at the top, because three
        // unrelated blocks read it and a second copy is a second thing to get wrong.
        SpriteEditorInk keyInk =
            commands.EditorSecondaryInk ? SpriteEditorInk.Secondary : SpriteEditorInk.Primary;
        // Ctrl+H: the shell-wide hex/dec switch for bank indexes (REFERENCES-EDITORS §8 item 20,
        // PICO-8's CTRL-H). Every one of the five routers carries this same line, and that is the
        // point of the feature rather than duplication to be tidied away: the state has ONE owner
        // (ShellModeMachine.Indexes, an IndexFormat) and the key must reach it from wherever the
        // author happens to be standing, the same shape F1..F5 and Alt+arrows already have here.
        if (commands.EditorHexToggle)
        {
            shell.Modes.ToggleIndexFormat();
        }
        if (commands.EditorUndo)
        {
            editor.Undo();
        }
        if (commands.EditorRedo)
        {
            editor.Redo();
        }
        // The clipboard chords, on the very keys TIC-80 and LIKO-12 give this screen
        // (REFERENCES-EDITORS §2.1 "Ctrl+X/C/V | буфер", §2.2 "ctrl-c / ctrl-v"). This router is
        // the only piece of the sprite screen that knows a clipboard exists: the session takes
        // and returns a plain string and stays headless, and what lies between the string and
        // the operating system is EditorShell's two doors.
        if (commands.EditorCopy)
        {
            shell.CopyText(editor.CopyToText());
        }
        if (commands.EditorCut)
        {
            shell.CopyText(editor.CutToText());
        }
        if (commands.EditorPaste)
        {
            editor.PasteFromText(shell.PasteText());
        }
        if (commands.EditorSave)
        {
            editor.Save();                       // failure lands in SaveError; the prompt line shows it
        }
        if (commands.EditorToolToggle)
        {
            editor.ToggleTool();
        }
        // The whole digit policy (select / repeat-cycles-variant / stubs stay dead) is
        // EditorIcons.PressToolDigit's — this line only delivers the key.
        EditorIcons.PressToolDigit(editor, commands.EditorToolDigit);
        if (commands.EditorGridToggle)
        {
            // ` — the very key the map screen answers for the very same verb, one gesture over
            // two panels (REFERENCES-EDITORS §8 item 11). PICO-8 spends CTRL-G on its own canvas
            // grid, and that chord is not free here: Ctrl+G is the code screen's find-next.
            // There is no button to go with it — the tool column's twelve slots are twelve
            // buttons already — so on this screen the grid is key-only, and its tooltip has
            // nowhere to be announced.
            view.ToggleGrid();
        }
        if (commands.EditorBlockDx != 0 || commands.EditorBlockDy != 0)
        {
            // Ctrl+Shift+arrows size the marked block — the keyboard twin of dragging a
            // rectangle across the sheet window, and the map screen's chord for the map screen's
            // version of the same gesture (REFERENCES-EDITORS §8 item 3).
            view.StepTileBlock(editor, commands.EditorBlockDx, commands.EditorBlockDy);
        }
        if (commands.EditorRegionCycle)
        {
            editor.CycleRegionSize();
            // The canvas must resize this same frame, so the mouse hits below test against
            // the geometry the renderer is about to draw.
            layout = SpriteEditorLayout.Compute(
                shell.BackBufferWidth, shell.BackBufferHeight, editor.RegionCells);
        }
        // The brush ladder's keyboard half — TIC-80's own two keys for its own cyclic
        // updateBrushSize (REFERENCES-EDITORS §2.1). No layout recompute follows, unlike the
        // region cycle above: a brush is a property of the stroke, not of the canvas, so not one
        // rectangle on this screen moves when it changes.
        if (commands.EditorBrushSmaller)
        {
            editor.CycleBrushSize(-1);
        }
        if (commands.EditorBrushBigger)
        {
            editor.CycleBrushSize(1);
        }
        if (commands.EditorFlipH)
        {
            editor.FlipHorizontal();
        }
        if (commands.EditorFlipV)
        {
            editor.FlipVertical();
        }
        if (commands.EditorRotate)
        {
            editor.RotateClockwise();
        }
        if (commands.EditorClear)
        {
            editor.ClearRegion();
        }
        // The palette walk, on whichever ink the modifier names — LIKO-12 gives q/e to colsL and
        // shift+q/shift+e to colsR (REFERENCES-EDITORS §2.2), which is the same rule under the
        // same modifier. Without this the second ink would be pointer-only.
        if (commands.EditorColorPrev)
        {
            editor.SelectColor(
                (editor.InkColor(keyInk) + Palette.VisibleCount - 1) % Palette.VisibleCount, keyInk);
        }
        if (commands.EditorColorNext)
        {
            editor.SelectColor((editor.InkColor(keyInk) + 1) % Palette.VisibleCount, keyInk);
        }
        if (commands.EditorLayerUp)
        {
            editor.SelectLayer(editor.ActiveLayerIndex + 1);    // the session clamps at the top layer
        }
        if (commands.EditorLayerDown)
        {
            editor.SelectLayer(editor.ActiveLayerIndex - 1);
        }
        if (commands.EditorFlagDigit != 0)
        {
            // Shift+1..8: the keyboard half of clicking a flag toggle (wave 3b-2, the parity
            // law). Same session verb as the click, so the two channels cannot drift about what
            // a toggle means — including the region rule, which lives in the session.
            editor.ToggleRegionFlag(commands.EditorFlagDigit - 1);
        }
        // The PICO-8-style sheet strip's keyboard and wheel scroll (wave 2i): [ ] keep their
        // one-sprite step, while a vertical wheel naturally advances the horizontal-only
        // strip. All paths share the scroll state's boundary clamp.
        if (commands.Slower)
        {
            shell.SheetScroll.ScrollBy(layout, -VirtualConsole.SpriteSize);
        }
        if (commands.Faster)
        {
            shell.SheetScroll.ScrollBy(layout, VirtualConsole.SpriteSize);
        }
        if (mouse.WheelDelta != 0 && layout.Sheet.Contains(mouse.X, mouse.Y))
        {
            shell.SheetScroll.ScrollBy(layout, -mouse.WheelDelta / 120 * VirtualConsole.SpriteSize);
        }

        // Keyboard drawing: arrows steer the canvas cursor, Z/Space is the paint button
        // (pencil stroke, bucket click or shape anchor by tool), X the eyedropper — the whole
        // mouse vocabulary without a mouse. The session clamps the cursor, so acting at it is
        // in-range by construction.
        // A frame that stepped the sheet does not also steer the cursor: Shift+arrow means one
        // thing, not both. The guard sits here, in the editor, and not in the shared reader —
        // see ShellCommandReader's note about the library losing Shift+Down to that mistake.
        bool steppedSheet = commands.EditorSheetDx != 0 || commands.EditorSheetDy != 0;
        // An arrow that sized the block is spent on the block, exactly as one that stepped the
        // sheet is spent on the sheet: the bare-arrow fields fire under any modifier, so the
        // frame that means "grow the block" must not also walk the canvas cursor. The map
        // screen's guard, word for word, because it is the same gesture.
        bool aimedAtSheet = steppedSheet
            || commands.EditorBlockDx != 0 || commands.EditorBlockDy != 0;
        int dx = aimedAtSheet ? 0 : (commands.MenuRight ? 1 : 0) - (commands.MenuLeft ? 1 : 0);
        int dy = aimedAtSheet ? 0 : (commands.MenuDown ? 1 : 0) - (commands.MenuUp ? 1 : 0);
        if (dx != 0 || dy != 0)
        {
            editor.MoveCursor(dx, dy);
            if (editor.StrokeActive && commands.EditorPaintDown)
            {
                editor.Paint(editor.CursorX, editor.CursorY);   // held pencil + arrows = a dragged stroke
            }
        }
        // Shift+arrows: the keyboard half of clicking a cell in the sheet strip. The step
        // itself lives in EditorSheetStep, the one owner of that arithmetic, because until
        // wave 3c this method lived in the window class and could not be constructed in a
        // test at all, and a rule written only here can only be tested by a copy of itself.
        // Scrolling the new sprite into view stays here: it is view state, and the session
        // neither has it nor needs it.
        if (commands.EditorSheetDx != 0 || commands.EditorSheetDy != 0)
        {
            int column = EditorSheetStep.Apply(editor, commands.EditorSheetDx, commands.EditorSheetDy);
            ScrollSheetTo(shell, layout, column);
        }
        if (commands.EditorPaintPressed)
        {
            BeginCanvasGesture(editor, editor.CursorX, editor.CursorY, keyInk, commands.EditorShapeFill);
        }
        // The keyboard half of the gesture refresh and the release: the shape corner and the
        // select mask/offset follow the cursor (and the Ctrl modifier) every frame, and only
        // then may the release commit — otherwise the very last arrow step (or a Ctrl
        // arriving with the release) would be missing from the committed gesture.
        RefreshGestures(editor, commands);
        if (commands.EditorPaintReleased)
        {
            EndCanvasGesture(editor);
        }
        if (commands.MenuEditor)
        {
            editor.PickColor(editor.CursorX, editor.CursorY, keyInk);   // X picks left, Shift+X right
        }

        // Hover: an open flyout's variants first (they float over everything), then buttons,
        // then swatches, then the slider (the one buttonless control — its tooltip is where
        // the wheel and [ ] are announced). The tracker shows the frame highlight immediately
        // and holds the label back for its three seconds — variants included, per the order.
        HoverTarget? hover = null;
        if (shell.Flyout.OpenSlot is EditorButton openHover
            && layout.TryFlyoutVariant(mouse.X, mouse.Y, openHover, out int variantHover))
        {
            hover = HoverTarget.OfFlyoutVariant(openHover, variantHover);
        }
        else if (layout.TryButton(mouse.X, mouse.Y, out EditorButton hoveredButton))
        {
            hover = HoverTarget.OfButton(hoveredButton);
        }
        else if (layout.TrySwatch(mouse.X, mouse.Y, out int hoveredSwatch))
        {
            hover = HoverTarget.OfSwatch(hoveredSwatch);
        }
        else if (layout.TryFlag(mouse.X, mouse.Y, out int hoveredFlag))
        {
            hover = HoverTarget.OfFlag(hoveredFlag);
        }
        else if (layout.SheetSlider.Contains(mouse.X, mouse.Y))
        {
            hover = HoverTarget.OfSlider();
        }
        shell.Hover.Update(hover, elapsedSeconds);

        // An open flyout owns the mouse: a press picks a variant or dismisses, and a release
        // over a variant supports the photoshop gesture (hold to open, slide, let go). The
        // keyboard above stayed live on purpose — a digit press visibly walks the highlight.
        if (shell.Flyout.OpenSlot is EditorButton open)
        {
            if (mouse.LeftPressed)
            {
                if (layout.TryFlyoutVariant(mouse.X, mouse.Y, open, out int chosen))
                {
                    EditorIcons.ChooseVariant(editor, open, chosen);
                }
                shell.Flyout.Close();        // chosen or clicked away — the click never falls through
            }
            else if (mouse.LeftReleased && layout.TryFlyoutVariant(mouse.X, mouse.Y, open, out int slid))
            {
                EditorIcons.ChooseVariant(editor, open, slid);
                shell.Flyout.Close();
            }
            return;
        }

        // An armed long-press: the press's meaning is not decided yet, so the mouse belongs
        // to the slot — held long enough it becomes the flyout, released early it was a click.
        if (shell.Flyout.ArmedSlot is not null)
        {
            if (mouse.LeftDown)
            {
                shell.Flyout.Hold(elapsedSeconds);
                return;
            }
            if (shell.Flyout.CompleteClick(out EditorButton clicked))
            {
                // The size toggle's click IS "open the list" (EditorIcons.ClickOpensFlyout —
                // one owner, the contract test mirrors the same consult); the tool groups act.
                if (EditorIcons.ClickOpensFlyout(clicked))
                {
                    shell.Flyout.Open(clicked);
                }
                else
                {
                    EditorIcons.ClickGroupSlot(editor, clicked);
                }
                return;
            }
        }

        // The one cursor: mouse hover parks it where the mouse is, so the status bar's
        // coordinates read the pointer and a following keyboard stroke starts there.
        if (layout.TryCanvasPixel(mouse.X, mouse.Y, out int overX, out int overY))
        {
            editor.SetCursor(overX, overY);
        }

        if (mouse.LeftPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton pressed))
            {
                if (!EditorIcons.IsStub(pressed))
                {
                    if (EditorIcons.IsGroupSlot(pressed))
                    {
                        shell.Flyout.Arm(pressed);   // click or flyout — the release/hold decides
                    }
                    else if (HandleEditorButton(shell, editor, pressed))
                    {
                        return;                 // the exit tab may have left the mode
                    }
                }
            }
            else if (layout.TrySwatch(mouse.X, mouse.Y, out int color))
            {
                editor.SelectColor(color, SpriteEditorInk.Primary);
            }
            else if (layout.TryFlag(mouse.X, mouse.Y, out int flagBit))
            {
                // The flag row's click. The whole policy — which way a mixed block goes, how
                // many sprites move, that it is one undo step — is the session's; this line
                // only says which bit the pointer landed on.
                editor.ToggleRegionFlag(flagBit);
            }
            else if (layout.TrySheetCell(mouse.X, mouse.Y, shell.SheetScroll.Offset, out _, out _))
            {
                // A press on the sheet opens a block drag (REFERENCES-EDITORS §8 item 3). A
                // press that never moves ends as a 1x1 block — the single-cell click this
                // replaced — so the drag is a generalization of it and not a second way to
                // choose a sprite. The view reverses the presentation-strip mapping; the session
                // still receives canonical 16x16 sheet cells and stays view-agnostic.
                view.BeginTileBlock(editor, layout, mouse.X, mouse.Y, shell.SheetScroll.Offset);
            }
            else if (layout.SheetSlider.Contains(mouse.X, mouse.Y))
            {
                // The thumb jumps under the pointer and the drag owns the button until
                // release — a press on the track never falls through to the canvas.
                shell.SheetScroll.BeginDrag(layout, mouse.X);
            }
            else if (layout.TryCanvasPixel(mouse.X, mouse.Y, out int pressX, out int pressY))
            {
                BeginCanvasGesture(
                    editor, pressX, pressY, SpriteEditorInk.Primary, commands.EditorShapeFill);
            }
        }
        else if (mouse.LeftDown && view.TileBlockGestureActive)
        {
            // Checked before the slider and the canvas: a sheet drag owns the button until it is
            // released, even when the pointer wanders off the window (the strip cell is clamped,
            // so the block keeps sizing along the sheet's edge).
            view.UpdateTileBlock(editor, layout, mouse.X, mouse.Y, shell.SheetScroll.Offset);
        }
        else if (mouse.LeftDown && shell.SheetScroll.Dragging)
        {
            shell.SheetScroll.DragTo(layout, mouse.X);
        }
        else if (mouse.LeftDown && editor.StrokeActive)
        {
            // Drags are clamped to the canvas: a stroke that wanders off the edge keeps
            // painting along it instead of tearing, and the clamp is what upholds Paint's
            // in-range contract. The cursor follows so the readout stays truthful mid-drag.
            layout.ClampCanvasPixel(mouse.X, mouse.Y, out int dragX, out int dragY);
            editor.SetCursor(dragX, dragY);
            editor.Paint(dragX, dragY);
        }
        else if (mouse.LeftDown && (editor.ShapeActive || editor.SelectionGestureActive))
        {
            // The shape and select drags only steer the cursor under the same clamp; the
            // refresh below turns the cursor into the preview's corner, the brush's next
            // point or the float's offset. This is why none of them can tear the region
            // from the mouse either.
            layout.ClampCanvasPixel(mouse.X, mouse.Y, out int dragToX, out int dragToY);
            editor.SetCursor(dragToX, dragToY);
        }
        // ---- the right button: the SECOND ink, everywhere the left one lays the first ----
        //
        // TIC-80 (processDrawCanvasMouse) and LIKO-12 (sprite.lua's b = 2) both do exactly this
        // and nothing more: the right button is not a menu and not a mode, it is the same verb
        // holding the other colour (REFERENCES-EDITORS §8 item 7). So the branches below mirror
        // the left button's, minus the controls the right button has no second meaning over —
        // the flag row, the sheet strip and the slider keep one meaning each.
        //
        // What the right button no longer is: the eyedropper. It was one before this wave,
        // because there was only one ink for it to fill. The eyedropper moved to the middle
        // button (TIC-80's own place for it) and to Shift+X, and it survives on the right button
        // under the two tools that lay no ink at all — see BeginCanvasGesture.
        if (mouse.RightPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton rightButton)
                && EditorIcons.IsGroupSlot(rightButton) && !EditorIcons.IsStub(rightButton))
            {
                shell.Flyout.Open(rightButton);      // the no-clock way in, next to the long press
            }
            else if (layout.TrySwatch(mouse.X, mouse.Y, out int rightColor))
            {
                editor.SelectColor(rightColor, SpriteEditorInk.Secondary);
            }
            else if (layout.TryCanvasPixel(mouse.X, mouse.Y, out int rightX, out int rightY))
            {
                BeginCanvasGesture(
                    editor, rightX, rightY, SpriteEditorInk.Secondary, commands.EditorShapeFill);
            }
        }
        else if (mouse.RightDown && editor.StrokeActive)
        {
            // A right-drag draws through, under the same clamp the left one uses — the clamp is
            // what upholds Paint's in-range contract, and it must not depend on which button.
            layout.ClampCanvasPixel(mouse.X, mouse.Y, out int rightDragX, out int rightDragY);
            editor.SetCursor(rightDragX, rightDragY);
            editor.Paint(rightDragX, rightDragY);
        }
        else if (mouse.RightDown && editor.ShapeActive)
        {
            layout.ClampCanvasPixel(mouse.X, mouse.Y, out int rightToX, out int rightToY);
            editor.SetCursor(rightToX, rightToY);
        }
        else if (mouse.MiddlePressed && layout.TryCanvasPixel(mouse.X, mouse.Y, out int pickX, out int pickY))
        {
            // The always-available eyedropper, on the button TIC-80 puts it on and into the ink
            // TIC-80 sends it to — the first (drawCanvasVBank1's tic_mouse_middle → color).
            editor.PickColor(pickX, pickY, SpriteEditorInk.Primary);
        }

        // The mouse half of the gesture refresh and release — same ordering law as the keyboard's.
        RefreshGestures(editor, commands);
        if (mouse.LeftReleased)
        {
            shell.SheetScroll.EndDrag();     // wherever the pointer wandered, the drag dies with the button
            view.EndTileBlock();             // and so does the sheet block's
            EndCanvasGesture(editor);
        }
        if (mouse.RightReleased)
        {
            EndCanvasGesture(editor);        // a second-ink stroke commits as one step, like the first's
        }
    }

    /// <summary>
    /// Scrolls the strip by the least amount that puts the given strip column fully inside the
    /// sheet window. Both edges are handled, and the clamp lives in <see cref="SheetScroll"/>
    /// as it does for every other writer of the offset — no path may scroll past the sheet.
    /// </summary>
    private static void ScrollSheetTo(in EditorShell shell, in SpriteEditorLayout layout, int column)
    {
        int size = VirtualConsole.SpriteSize;
        int left = column * size;
        int right = left + size;
        int visible = layout.SheetVisiblePixels;
        if (left < shell.SheetScroll.Offset)
        {
            shell.SheetScroll.ScrollBy(layout, left - shell.SheetScroll.Offset);
        }
        else if (right > shell.SheetScroll.Offset + visible)
        {
            shell.SheetScroll.ScrollBy(layout, right - (shell.SheetScroll.Offset + visible));
        }
    }

    /// <summary>
    /// What a paint button means on the canvas, keyboard and mouse alike — one dispatch so
    /// the two input worlds cannot drift (the parity law): the bucket and the stamp are
    /// clicks, the shape and the select open preview gestures (a select press over the mask
    /// is the grab — the session decides), the pencil opens a stroke.
    ///
    /// <para><paramref name="ink"/> is which of the two colours the button in question holds —
    /// left/plain keys the first, right/Shift the second. It is passed down and never decided
    /// here: <see cref="SpriteEditorSession"/> owns what an ink IS, this method only owns which
    /// verb the tool makes of a press.</para>
    ///
    /// <para><paramref name="replace"/> is Ctrl, and it means one thing on one tool: over the
    /// bucket it swaps the flood for <see cref="SpriteEditorSession.ReplaceColor"/> — TIC-80's
    /// <c>processFillCanvasMouse</c> branching to <c>replaceColor</c>, PICO-8's "Hold CTRL to
    /// search and replace colour" (REFERENCES-EDITORS §8 item 6). The shape tool reads the same
    /// physical key as its "filled" flag, but through the gesture refresh rather than here,
    /// because filled-ness can change mid-drag and this is a press.</para>
    ///
    /// <para><b>The two inkless tools.</b> A marquee and a stamp have no colour, so the second
    /// ink has nothing to say to them — and rather than making the right button dead over half
    /// the toolbar, it keeps the job it held before the second ink existed: the eyedropper, into
    /// the ink that asked (TIC-80's picker tool sends its right button to <c>color2</c> for the
    /// same reason). One rule, stated once: the right button lays the second colour where there
    /// is colour to lay, and picks it up where there is not.</para>
    /// </summary>
    private static void BeginCanvasGesture(
        SpriteEditorSession editor, int localX, int localY, SpriteEditorInk ink, bool replace)
    {
        if (ink == SpriteEditorInk.Secondary
            && editor.Tool is SpriteEditorTool.Select or SpriteEditorTool.Stamp)
        {
            editor.PickColor(localX, localY, ink);
            return;
        }
        switch (editor.Tool)
        {
            case SpriteEditorTool.Fill:
                if (replace)
                {
                    editor.ReplaceColor(localX, localY, ink);
                }
                else
                {
                    editor.Fill(localX, localY, ink);
                }
                break;
            case SpriteEditorTool.Shape:
                editor.BeginShape(localX, localY, ink);
                break;
            case SpriteEditorTool.Select:
                editor.BeginSelect(localX, localY);
                break;
            case SpriteEditorTool.Stamp:
                editor.StampAt(localX, localY);
                break;
            default:
                editor.BeginStroke(ink);
                editor.Paint(localX, localY);
                break;
        }
    }

    /// <summary>The paint button's release: a shape commits its preview, a select gesture its mask or drop, a stroke its pixels — one undo step at most, either way.</summary>
    private static void EndCanvasGesture(SpriteEditorSession editor)
    {
        if (editor.ShapeActive)
        {
            editor.CommitShape();
        }
        else if (editor.SelectionGestureActive)
        {
            editor.CommitSelect();
        }
        else
        {
            editor.EndStroke();
        }
    }

    /// <summary>Open previews follow the cursor every frame: the shape's corner (with its Ctrl-held filled flag) and the select tool's box, brush track or floating fragment.</summary>
    private static void RefreshGestures(SpriteEditorSession editor, in ShellCommands commands)
    {
        if (editor.ShapeActive)
        {
            editor.UpdateShape(editor.CursorX, editor.CursorY, commands.EditorShapeFill);
        }
        if (editor.SelectionGestureActive)
        {
            editor.UpdateSelect(editor.CursorX, editor.CursorY);
        }
    }

    /// <summary>
    /// A click on a live, non-group icon-button (group slots go through
    /// <see cref="ToolbarFlyout"/>'s arm/click path instead, because their press has two
    /// possible meanings). The routing table itself is <see cref="EditorIcons.ClickButton"/> —
    /// moved there in wave 2g after the stamp shipped placed-but-unwired, so the contract test
    /// can click every placed button without a window; this wrapper owns the one verb a
    /// session cannot perform: leaving the mode. Returns true when the button may have changed
    /// the shell mode (the exit tab), telling the caller to stop touching the editor this frame.
    /// </summary>
    private static bool HandleEditorButton(in EditorShell shell, SpriteEditorSession editor, EditorButton button)
    {
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            // A live editor tab: the travel between the two faces of one cart is the mode
            // machine's verb, not the session's — the same split the exit tab already makes.
            shell.Modes.SwitchEditorTab(tab);
            return shell.Modes.Mode != ShellMode.Editor;
        }
        if (EditorIcons.ClickButton(editor, button))
        {
            shell.Modes.HandleEscape();               // clean → library; dirty → the prompt, same as Esc
            return true;
        }
        return false;
    }
}

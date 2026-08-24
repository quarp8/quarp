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
    int BackBufferWidth, int BackBufferHeight);

/// <summary>
/// The sprite editor's input router: one frame of keys and mouse hits turned into calls on
/// <see cref="SpriteEditorSession"/>, <see cref="ShellModeMachine"/> and the editor's view
/// state. Moved out of <c>QuarpGame</c> in wave 3c with its bodies unchanged; what it gained by
/// moving is that it can be called at all without a window — see <see cref="EditorShell"/> for
/// why, and <c>EditorInputRouterTests</c> for what that buys. It owns dispatch and nothing else:
/// geometry belongs to <see cref="SpriteEditorLayout"/>, the button table to
/// <see cref="EditorIcons"/>, the step to <see cref="EditorSheetStep"/>, editing policy to the
/// session, mode policy to the machine — the law the file it came from stated about itself.
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
        if (commands.EditorUndo)
        {
            editor.Undo();
        }
        if (commands.EditorRedo)
        {
            editor.Redo();
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
        if (commands.EditorRegionCycle)
        {
            editor.CycleRegionSize();
            // The canvas must resize this same frame, so the mouse hits below test against
            // the geometry the renderer is about to draw.
            layout = SpriteEditorLayout.Compute(
                shell.BackBufferWidth, shell.BackBufferHeight, editor.RegionCells);
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
        if (commands.EditorColorPrev)
        {
            editor.SelectColor((editor.CurrentColor + Palette.VisibleCount - 1) % Palette.VisibleCount);
        }
        if (commands.EditorColorNext)
        {
            editor.SelectColor((editor.CurrentColor + 1) % Palette.VisibleCount);
        }
        if (commands.EditorLayerUp)
        {
            editor.SelectLayer(editor.ActiveLayerIndex + 1);    // the session clamps at the top layer
        }
        if (commands.EditorLayerDown)
        {
            editor.SelectLayer(editor.ActiveLayerIndex - 1);
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
        int dx = steppedSheet ? 0 : (commands.MenuRight ? 1 : 0) - (commands.MenuLeft ? 1 : 0);
        int dy = steppedSheet ? 0 : (commands.MenuDown ? 1 : 0) - (commands.MenuUp ? 1 : 0);
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
            BeginCanvasGesture(editor, editor.CursorX, editor.CursorY);
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
            editor.PickColor(editor.CursorX, editor.CursorY);
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
                editor.SelectColor(color);
            }
            else if (layout.TrySheetCell(mouse.X, mouse.Y, shell.SheetScroll.Offset, out int cellX, out int cellY))
            {
                // Layout reverses the presentation-strip mapping; the session still receives
                // its canonical 16x16 sheet cell and therefore remains view-agnostic.
                editor.SelectRegionCell(cellX, cellY);
            }
            else if (layout.SheetSlider.Contains(mouse.X, mouse.Y))
            {
                // The thumb jumps under the pointer and the drag owns the button until
                // release — a press on the track never falls through to the canvas.
                shell.SheetScroll.BeginDrag(layout, mouse.X);
            }
            else if (layout.TryCanvasPixel(mouse.X, mouse.Y, out int pressX, out int pressY))
            {
                BeginCanvasGesture(editor, pressX, pressY);
            }
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
        // The mouse half of the gesture refresh and release — same ordering law as the keyboard's.
        RefreshGestures(editor, commands);
        if (mouse.LeftReleased)
        {
            shell.SheetScroll.EndDrag();     // wherever the pointer wandered, the drag dies with the button
            EndCanvasGesture(editor);
        }
        if (mouse.RightPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton rightButton)
                && EditorIcons.IsGroupSlot(rightButton) && !EditorIcons.IsStub(rightButton))
            {
                shell.Flyout.Open(rightButton);      // the no-clock way in, next to the long press
            }
            else if (layout.TryCanvasPixel(mouse.X, mouse.Y, out int pickX, out int pickY))
            {
                editor.PickColor(pickX, pickY);
            }
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
    /// What the paint button means on the canvas, keyboard and mouse alike — one dispatch so
    /// the two input worlds cannot drift (the parity law): the bucket and the stamp are
    /// clicks, the shape and the select open preview gestures (a select press over the mask
    /// is the grab — the session decides), the pencil opens a stroke.
    /// </summary>
    private static void BeginCanvasGesture(SpriteEditorSession editor, int localX, int localY)
    {
        switch (editor.Tool)
        {
            case SpriteEditorTool.Fill:
                editor.Fill(localX, localY);
                break;
            case SpriteEditorTool.Shape:
                editor.BeginShape(localX, localY);
                break;
            case SpriteEditorTool.Select:
                editor.BeginSelect(localX, localY);
                break;
            case SpriteEditorTool.Stamp:
                editor.StampAt(localX, localY);
                break;
            default:
                editor.BeginStroke();
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

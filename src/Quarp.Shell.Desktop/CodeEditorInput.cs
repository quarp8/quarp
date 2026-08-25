namespace Quarp.Shell.Desktop;

/// <summary>
/// The code editor's input router — the third member of the family
/// <see cref="SpriteEditorInput"/> and <see cref="MapEditorInput"/> started, written where a
/// test can call it (see <see cref="EditorShell"/> for why the window is two integers). It owns
/// dispatch and nothing else: geometry belongs to <see cref="CodeEditorLayout"/>, the scroll and
/// the footer fields to <see cref="CodeEditorView"/>, the text and the caret to
/// <see cref="CodeEditorSession"/>, the button table to <see cref="EditorIcons"/>, mode policy
/// to <see cref="ShellModeMachine"/>.
///
/// <para><b>Two channels, one keyboard, no second owner.</b> Every editor before this one needed
/// only <em>which keys are down</em>, and <see cref="ShellCommandReader"/> is the single owner of
/// that fact — one previous-frame state, one set of edges, shared by every mode. A text editor
/// needs something that frame cannot express: <em>which characters the person typed</em>, with
/// their keyboard layout, their dead keys and their auto-repeat applied. That is a different
/// fact with a different owner (the OS, through <c>Window.TextInput</c>), and it arrives here as
/// its own parameter — <paramref name="typed"/> — rather than as more fields on
/// <see cref="ShellCommands"/>, which would have meant re-deriving characters from key codes and
/// getting a second, wrong owner of the layout. See the wave report for the whole argument; the
/// rule it comes down to is the one below.</para>
///
/// <para><b>Each key belongs to exactly one channel.</b> The character stream is filtered here,
/// once, to <em>printable</em> characters: everything <see cref="char.IsControl(char)"/> calls a
/// control character is dropped, which is precisely the set the OS also delivers as a key edge
/// (Enter, Tab, Backspace, and the 0x01-0x1A a Ctrl chord produces on some platforms). Enter,
/// Tab, Backspace, Delete and every chord therefore come from <see cref="ShellCommands"/> and
/// only from there; letters, digits, punctuation and space come from <paramref name="typed"/> and
/// only from there. Nothing is handled twice, and nothing needs a guard invented for this file.</para>
///
/// <para><b>Determinism is untouched.</b> A cartridge never sees this: <see cref="InputMapper"/>
/// reads a <c>KeyboardState</c> and nothing else, the code screen draws no framebuffer, and no
/// frame hash can observe a keystroke here. The buffer is filled by the window's event and
/// emptied by the window at the end of every <c>Update</c>, so a frame sees exactly the
/// characters that arrived since the previous frame — in order, once.</para>
///
/// <para><b>The keyboard map</b> (REFERENCES-EDITORS §4, the parts all three consoles share):
/// arrows, Home/End, PageUp/PageDown; Ctrl+Left/Right by word; Ctrl+Home/End to the ends of the
/// file; Shift with any of them extends the selection; Ctrl+A select all; Ctrl+Z / Ctrl+Y undo
/// and redo; Ctrl+C / Ctrl+X / Ctrl+V through the view's clipboard; Ctrl+F opens the find line,
/// Ctrl+G walks to the next occurrence, Ctrl+L opens go-to-line; Tab indents to the next stop,
/// Enter breaks the line, Backspace and Delete eat a character or the selection; Ctrl+S saves;
/// Esc drops a selection, then closes a field, then <b>brings the chrome back</b>, then leaves.
/// Alt+Left/Right walk the tab strip and F1..F5 jump straight to a named editor (TIC-80's own
/// five keys) — the tab strip cannot be Home here, because Home is the start of the line.
/// Alt+Up/Down walk the buffer's declarations, the other two arrows of the same modifier.
/// F11 takes the chrome off entirely (15x40 instead of 11x36 — see
/// <see cref="ShellCommands.CodeFullscreen"/> for why it is TIC-80's key and not the TAB ADR-029
/// cites) and Shift+F11 summons that mode's one status row.</para>
///
/// <para>Inside fullscreen the parity law's mouse half is suspended by construction — there is
/// no chrome, so there is no button to click. <see cref="CodeEditorView.Fullscreen"/> carries
/// that argument.</para>
/// </summary>
public static class CodeEditorInput
{
    /// <summary>Lines per wheel notch — three, the desktop convention every toolkit ships.</summary>
    private const int WheelLines = 3;

    /// <summary>
    /// One frame of the code editor. Input parity is the law of this frame as it is of its two
    /// siblings: every live action has a key path and a click path, and both funnel into the same
    /// owner — <see cref="EditorIcons.ClickCodeButton"/> for the buttons,
    /// <see cref="CodeEditorView"/> for the scroll and the fields, the session for every edit —
    /// so neither channel can drift.
    ///
    /// <para>While the exit prompt is up it owns the input (Z saves and leaves, X discards, Esc
    /// stays, and the same three verbs are clickable on the prompt line) and everything else,
    /// typing included, is deliberately deaf: a stray keystroke must not change the buffer the
    /// author is being asked about.</para>
    /// </summary>
    /// <param name="typed">
    /// The printable characters that arrived since the previous frame, in order — the window's
    /// <c>Window.TextInput</c> buffer. Read only by this router and only for the buffer and the
    /// footer fields.
    /// </param>
    public static void Update(
        in EditorShell shell, in ShellCommands commands, in EditorMouse mouse,
        IReadOnlyList<char> typed, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(typed);
        CodeEditorSession session = shell.Modes.CodeEditor!;
        CodeEditorView view = shell.Modes.CodeView!;
        // The same layout the renderer will draw this frame — geometry has one owner. Since
        // wave R4 the two numbers are CONSOLE pixels (160x90), because the shell hands this
        // screen its own surface exactly as it hands the sprite and map screens theirs; the
        // router cannot tell the difference and must not. Sync re-clamps the scroll against it
        // before any hit test below divides by it. Since the fullscreen wave Compute takes two
        // more inputs; both are read from the view, which the renderer also asks.
        var layout = CodeEditorLayout.Compute(
            shell.BackBufferWidth, shell.BackBufferHeight, view.Fullscreen, view.StatusBandShown(session));
        view.Sync(layout, session);

        if (view.ExitPromptShown)
        {
            shell.Hover.Update(null, elapsedSeconds);        // dead buttons must not grow tooltips
            if (commands.Quit)
            {
                shell.Modes.HandleEscape();                  // Esc lowers the prompt: "stay"
            }
            else if (commands.MenuConfirm)
            {
                shell.Modes.SaveCodeAndClose();
            }
            else if (commands.MenuEditor)
            {
                shell.Modes.DiscardCodeAndClose();
            }
            else if (mouse.LeftPressed && layout.TryPromptVerb(mouse.X, mouse.Y, out EditorPromptVerb verb))
            {
                switch (verb)
                {
                    case EditorPromptVerb.SaveAndExit:
                        shell.Modes.SaveCodeAndClose();
                        break;
                    case EditorPromptVerb.Discard:
                        shell.Modes.DiscardCodeAndClose();
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
        // everything else for the same reason those are: travel is a question about WHICH SCREEN,
        // and it must not be answered by a screen that is already being left. The return is what
        // makes that true — the rest of this method may then assume the mode did not move.
        if (EditorIcons.EditorTabForNumber(commands.EditorTabJump) is ShellMode named)
        {
            shell.Modes.SwitchEditorTab(named);
            return;
        }

        // Alt+Left/Right walk the tab strip on every editor screen (REFERENCES-EDITORS §8 item
        // 16: LIKO-12's and PICO-8's own key). Home cannot do that job here — it is the start of
        // the line — which is exactly why the shell has this key at all.
        if (commands.EditorTabPrev || commands.EditorTabNext)
        {
            shell.Modes.CycleEditorTab(commands.EditorTabNext ? 1 : -1);
            return;
        }

        // F11 and Shift+F11 are about the SURFACE, not the text, so they are read before the
        // fork below and work with a find line open — the reasoning that puts the tab strip
        // above them. The layout is re-measured on the spot rather than next frame: a frame that
        // drew fifteen lines while clicking as if there were eleven is the exact disagreement
        // CodeEditorLayout exists to prevent.
        if (commands.CodeFullscreen || commands.CodeFullscreenStatus)
        {
            if (commands.CodeFullscreen)
            {
                view.ToggleFullscreen();
            }
            if (commands.CodeFullscreenStatus)
            {
                view.ToggleStatusPeek();
            }
            layout = CodeEditorLayout.Compute(
                shell.BackBufferWidth, shell.BackBufferHeight,
                view.Fullscreen, view.StatusBandShown(session));
            view.Sync(layout, session);
            shell.Hover.Update(null, elapsedSeconds);   // the buttons just left the screen
            return;
        }

        if (view.FieldShown)
        {
            FieldKeys(session, view, commands, typed);
        }
        else
        {
            EditKeys(shell, session, view, layout, commands, typed);
        }

        // Esc, the exit tab's key twin and a discard all leave through the mode machine; the
        // session on this screen may be gone by now, so nothing below may touch it.
        if (shell.Modes.Mode != ShellMode.CodeEditor)
        {
            return;
        }

        Pointer(shell, session, view, layout, commands, mouse, elapsedSeconds);

        if (shell.Modes.Mode != ShellMode.CodeEditor)
        {
            return;                             // a click on the exit or another tab left this screen
        }

        // The caret may have moved by key or by click this frame; the view catches it before the
        // renderer draws, so a keystroke is never one frame ahead of the page it lands on.
        view.Sync(layout, session);
    }

    /// <summary>
    /// The keyboard while the find or go-to line is up: it owns typed characters, Backspace,
    /// Enter and Esc, and the buffer underneath is deaf. The two fields are TIC-80's
    /// <c>TEXT_FIND_MODE</c> and <c>TEXT_GOTO_MODE</c>, one footer line at a time.
    /// </summary>
    private static void FieldKeys(
        CodeEditorSession session, CodeEditorView view, in ShellCommands commands, IReadOnlyList<char> typed)
    {
        if (commands.Quit)
        {
            view.CloseFind();
            view.CloseGoTo();
            return;
        }
        for (int i = 0; i < typed.Count; i++)
        {
            view.TypeIntoField(typed[i]);
        }
        if (commands.CodeBackspace)
        {
            view.BackspaceField();
        }
        if (commands.CodeNewLine)
        {
            if (view.GoToShown)
            {
                view.CommitGoTo(session);
            }
            else if (commands.CodeExtend)
            {
                view.FindPrevious(session);     // Shift+Enter walks back, the mirror of Enter
            }
            else
            {
                view.FindNext(session);
            }
        }
        if (commands.CodeFindNext)
        {
            view.FindNext(session);             // Ctrl+G works with the line open too
        }
        if (commands.CodeFind)
        {
            view.OpenFind();
        }
        if (commands.CodeGoToLine)
        {
            view.OpenGoTo();
        }
    }

    /// <summary>
    /// The keyboard while the buffer has it: chords first (so Ctrl+V is a paste and never a
    /// letter), then movement, then edits. The order matters exactly once — a chord and a bare
    /// key can arrive on the same frame only when the chord's own key is also a character, and
    /// the character stream has already had control characters filtered out of it by the caller.
    /// </summary>
    private static void EditKeys(
        in EditorShell shell, CodeEditorSession session, CodeEditorView view,
        in CodeEditorLayout layout, in ShellCommands commands, IReadOnlyList<char> typed)
    {
        if (commands.Quit)
        {
            // The sprite and map screens' rung, one editor over: a live selection eats the next
            // Esc, then an open field does, and only then does Esc mean "leave".
            if (session.HasSelection)
            {
                session.ClearSelection();
                return;
            }
            // The third rung: Esc brings the chrome back before it means "leave", or a dirty
            // buffer would raise its prompt onto a message line that is not on the surface.
            if (view.LeaveFullscreen())
            {
                return;
            }
            shell.Modes.HandleEscape();         // clean → the library; dirty → the prompt
            return;
        }

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
            session.Undo();
        }
        if (commands.EditorRedo)
        {
            session.Redo();
        }
        if (commands.EditorSave)
        {
            session.Save();
        }
        if (commands.CodeSelectAll)
        {
            session.SelectAll();
        }
        if (commands.CodeCopy)
        {
            view.Copy(session);
        }
        if (commands.CodeCut)
        {
            view.Cut(session);
        }
        if (commands.CodePaste)
        {
            view.Paste(session);
        }
        if (commands.CodeFind)
        {
            view.OpenFind();
        }
        if (commands.CodeGoToLine)
        {
            view.OpenGoTo();
        }
        if (commands.CodeFindNext)
        {
            view.FindNext(session);
        }

        Movement(session, layout, commands);

        if (commands.CodeNewLine)
        {
            session.InsertNewLine();
        }
        if (commands.EditorRegionCycle)
        {
            session.InsertTab();                // Tab: spaces to the next stop — the session owns the width
        }
        if (commands.CodeBackspace)
        {
            session.Backspace();
        }
        if (commands.EditorClear)
        {
            session.Delete();
        }
        for (int i = 0; i < typed.Count; i++)
        {
            char c = typed[i];
            if (!char.IsControl(c))
            {
                session.Insert(c.ToString());
            }
        }
    }

    /// <summary>
    /// Every caret move, in one method so the Shift flag is applied once. Each chorded move is
    /// checked before the bare key it shares an arrow with: Ctrl+Left fires both
    /// <see cref="ShellCommands.CodeWordLeft"/> and <see cref="ShellCommands.MenuLeft"/> — one
    /// physical press, one owner of what it means, decided here rather than in the reader, so the
    /// other two screens keep their bare arrows unchanged.
    /// </summary>
    private static void Movement(
        CodeEditorSession session, in CodeEditorLayout layout, in ShellCommands commands)
    {
        bool extend = commands.CodeExtend;
        if (commands.CodeDocumentStart)
        {
            session.Move(CodeMove.DocumentStart, extend);
        }
        else if (commands.ToStart)
        {
            session.Move(CodeMove.LineStart, extend);
        }
        if (commands.CodeDocumentEnd)
        {
            session.Move(CodeMove.DocumentEnd, extend);
        }
        else if (commands.CodeLineEnd)
        {
            session.Move(CodeMove.LineEnd, extend);
        }
        if (commands.CodeWordLeft)
        {
            session.Move(CodeMove.WordLeft, extend);
        }
        else if (commands.MenuLeft)
        {
            session.Move(CodeMove.Left, extend);
        }
        if (commands.CodeWordRight)
        {
            session.Move(CodeMove.WordRight, extend);
        }
        else if (commands.MenuRight)
        {
            session.Move(CodeMove.Right, extend);
        }
        // Alt+Up/Down walk DECLARATIONS (REFERENCES-EDITORS §8 item 14: LIKO-12's
        // searchPreviousFunction/searchNextFunction and PICO-8's "ALT-UP, DOWN to navigate to the
        // previous, next function"). Checked before the bare arrow it shares a key with, exactly
        // as Ctrl+Left is checked before Left three branches up — one physical press, one owner
        // of what it means. Never extends the selection: a jump through structure is travel, and
        // the two references that have it do not extend either.
        if (commands.CodeDeclarationPrev)
        {
            session.MoveToPreviousDeclaration();
        }
        else if (commands.MenuUp)
        {
            session.Move(CodeMove.Up, extend);
        }
        if (commands.CodeDeclarationNext)
        {
            session.MoveToNextDeclaration();
        }
        else if (commands.MenuDown)
        {
            session.Move(CodeMove.Down, extend);
        }
        // A page is what the WINDOW shows, minus one line of overlap so the eye keeps a
        // landmark — the number the document cannot know, which is why the session takes it as
        // an argument (CodeEditorSession.MovePage).
        int page = Math.Max(1, layout.VisibleLines - 1);
        if (commands.EditorLayerUp)
        {
            session.MovePage(-page, extend);
        }
        if (commands.EditorLayerDown)
        {
            session.MovePage(page, extend);
        }
    }

    /// <summary>
    /// The mouse: hover, wheel, then the press chain — buttons, the scrollbar, the text — and
    /// the drags that follow it. Every one of these has the keyboard twin the parity law
    /// demands: the buttons their hotkeys, the bar and the wheel PageUp/PageDown and the arrows,
    /// the text click the arrows, the drag Shift+arrows.
    ///
    /// <para>The chain lost a link in wave R4: the line-number gutter, and with it the click
    /// that put the caret at a line's start. The gutter is not on the console screen at all
    /// (<see cref="CodeEditorLayout"/> spends its six columns on code, as all three references
    /// do), so there is nothing left to hit-test; the verb it carried is Home, which it always
    /// also was.</para>
    /// </summary>
    private static void Pointer(
        in EditorShell shell, CodeEditorSession session, CodeEditorView view,
        in CodeEditorLayout layout, in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        // Hover: buttons first, then this screen's two buttonless controls — the same order the
        // press chain below tests them in, so the label and the click always name the same thing
        // (MapEditorInput.Pointer's rule, one screen over). Until the closing wave this screen
        // could only build OfButton and the scrollbar and the page itself were mute; CodeRegion
        // is what gave them a hover kind of their own (REFERENCES-EDITORS §8 item 15), and
        // CodeEditorLayout.RegionAt is the one hit test.
        shell.Hover.Update(
            layout.TryButton(mouse.X, mouse.Y, out EditorButton hovered)
                ? HoverTarget.OfButton(hovered)
                : layout.RegionAt(mouse.X, mouse.Y) is CodeRegion region and not CodeRegion.None
                    ? HoverTarget.OfCodeRegion(region)
                    : null,
            elapsedSeconds);

        // Over the text field only. The gutter used to count too; wave R4 took the gutter off
        // this screen entirely (CodeEditorLayout's type note carries the six columns it bought),
        // so the field is the whole of what the wheel answers to.
        if (mouse.WheelDelta != 0 && layout.Text.Contains(mouse.X, mouse.Y))
        {
            // The wheel moves the WINDOW and leaves the caret where it is — the one place the
            // view is allowed to part company with the caret, and the reason Sync follows the
            // caret only when the caret is what moved.
            view.ScrollLines(layout, session, -mouse.WheelDelta / 120 * WheelLines);
        }

        if (mouse.LeftPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton pressed))
            {
                if (!EditorIcons.IsStub(pressed))
                {
                    HandleCodeButton(shell, session, view, pressed);
                }
                return;                         // a tab or the exit may have left this mode
            }
            if (layout.TryScrollBarLine(mouse.X, mouse.Y, session.LineCount, out int barLine))
            {
                view.BeginScrollDrag();
                view.ScrollTo(layout, session, barLine);
            }
            else if (layout.TryTextCell(
                mouse.X, mouse.Y, view.FirstLine, view.FirstColumn, out int line, out int column))
            {
                // Shift+click extends, exactly as Shift+arrow does (TIC-80's processMouse).
                session.SetCursor(line, column, commands.CodeExtend);
                view.BeginTextDrag();
            }
        }
        else if (mouse.LeftDown && view.ScrollDragActive)
        {
            // The pointer is allowed to wander off the bar mid-drag — a slider you must stay
            // inside of is a slider that drops what you are carrying.
            int y = Math.Clamp(mouse.Y, layout.ScrollBar.Y, layout.ScrollBar.Bottom - 1);
            if (layout.TryScrollBarLine(layout.ScrollBar.X, y, session.LineCount, out int dragLine))
            {
                view.ScrollTo(layout, session, dragLine);
            }
        }
        else if (mouse.LeftDown && view.TextDragActive)
        {
            // Clamped to the visible page: a drag that leaves the text field keeps selecting
            // along its edge instead of tearing, the way the map's strokes do.
            layout.ClampTextCell(
                mouse.X, mouse.Y, view.FirstLine, view.FirstColumn, out int dragLine, out int dragColumn);
            session.SetCursor(dragLine, dragColumn, extend: true);
        }

        if (mouse.LeftReleased)
        {
            view.EndTextDrag();
            view.EndScrollDrag();
        }
    }

    /// <summary>
    /// The code screen's twin of <see cref="MapEditorInput"/>'s own button handler: tabs first
    /// (travel is the mode machine's verb), then <see cref="EditorIcons.ClickCodeButton"/>, the
    /// headless routing table the code screen's button-contract test clicks every placed button
    /// through.
    /// </summary>
    private static void HandleCodeButton(
        in EditorShell shell, CodeEditorSession session, CodeEditorView view, EditorButton button)
    {
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            shell.Modes.SwitchEditorTab(tab);
            return;
        }
        if (EditorIcons.ClickCodeButton(session, view, button))
        {
            shell.Modes.HandleEscape();
        }
    }
}

namespace Quarp.Shell.Desktop;

/// <summary>
/// The music editor's input router — the fifth and last member of the family
/// <see cref="SpriteEditorInput"/>, <see cref="MapEditorInput"/>, <see cref="CodeEditorInput"/>
/// and <see cref="SfxEditorInput"/> built, written where a test can call it (see
/// <see cref="EditorShell"/> for why the window is two integers). It owns dispatch and nothing
/// else: geometry belongs to <see cref="MusicEditorLayout"/>, the window, the mute table and the
/// playback request to <see cref="MusicEditorView"/>, the 320 bytes and the cursor to
/// <see cref="MusicEditorSession"/>, the button table to <see cref="EditorIcons"/>, mode policy
/// to <see cref="ShellModeMachine"/>, and the speaker to <c>QuarpGame</c> — which is a different
/// half of the frame and deliberately not this file's business.
///
/// <para><b>The whole map, and its mouse twin for every entry</b> (the input-parity law of M9
/// stage 2.5, which is two-way: no key without a click, no click without a key):</para>
/// <list type="table">
///   <item><term>0-9</term><description>two digits set the slot at the cursor — the wheel over a cell steps it</description></item>
///   <item><term>Del</term><description>the cell falls silent (the marked block, when there is one) — a right click on a cell</description></item>
///   <item><term>arrows</term><description>the cursor — a click on a cell puts it there</description></item>
///   <item><term>Shift+arrows</term><description>grow the marked rectangle — a drag across the grid</description></item>
///   <item><term>PgUp / PgDn</term><description>one screenful of song — a click on the overview, or the wheel over it</description></item>
///   <item><term>`</term><description>loop start on the cursor's pattern — a click on its <c>[</c> marker</description></item>
///   <item><term>Tab</term><description>loop end — a click on its <c>]</c> marker</description></item>
///   <item><term>X</term><description>stop — a click on its <c>X</c> marker</description></item>
///   <item><term>Shift+1..4</term><description>mute a channel — a click on its M toggle</description></item>
///   <item><term>Shift+5..8</term><description>solo a channel — a click on its S toggle</description></item>
///   <item><term>[ / ]</term><description>step every sounding cell of the marking by one slot — the wheel, cell by cell</description></item>
///   <item><term>Ctrl+C / X / V</term><description>copy, cut, paste the marked block</description></item>
///   <item><term>Ctrl+A</term><description>mark the whole song</description></item>
///   <item><term>Space</term><description>play from the cursor / stop — the play button</description></item>
///   <item><term>Ctrl+Z / Y / S</term><description>undo, redo, save — the three tool buttons</description></item>
///   <item><term>Esc, Alt+Left/Right</term><description>leave, walk the tabs — the exit tab and the tab strip</description></item>
/// </list>
///
/// <para><b>Why X means "stop flag" on this screen and "pick a colour" on the sprite screen.</b>
/// One physical press has one owner in the reader (<see cref="ShellCommands.MenuEditor"/>), and
/// which <em>field</em> a screen reads is the screen's business — the same resolution
/// <see cref="ShellCommands.EditorSheetDx"/>, <see cref="ShellCommands.MenuLeft"/> and
/// <see cref="ShellCommands.SfxPianoKey"/> have carried since wave 2h: <b>the gate belongs where
/// the meaning differs</b>. The same sentence covers Shift+digits, which are the sprite screen's
/// flag row and this screen's mute and solo rows, and the digits themselves, which reach
/// <see cref="ShellCommands.MusicSlotDigit"/> here and
/// <see cref="ShellCommands.EditorToolDigit"/> there.</para>
///
/// <para>While the exit prompt is up it owns the input (Z saves and leaves, X discards, Esc
/// stays, and the same three verbs are clickable on the prompt line) and everything else — the
/// digits included — is deliberately deaf: a stray keystroke must not change the bank the author
/// is being asked about.</para>
/// </summary>
public static class MusicEditorInput
{
    /// <summary>Patterns a wheel notch walks over the overview — four, a bar at a time rather than a row.</summary>
    private const int WheelPatterns = 4;

    /// <summary>
    /// One frame of the music editor. Input parity is the law of this frame as it is of its four
    /// siblings: every live action has a key path and a click path, and both funnel into the same
    /// owner — <see cref="EditorIcons.ClickMusicButton"/> for the buttons,
    /// <see cref="MusicEditorView"/> for the window and the listening state, the session for every
    /// byte — so neither channel can drift.
    /// </summary>
    public static void Update(
        in EditorShell shell, in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        MusicEditorSession session = shell.Modes.MusicEditor!;
        MusicEditorView view = shell.Modes.MusicView!;
        // The same layout the renderer will draw this frame — geometry has one owner. The two
        // numbers EditorShell carries are the size of the surface this screen is laid out on, and
        // that surface is the console itself (ADR-029): 160x90, not the back buffer. The
        // conversions happen in QuarpGame, through FramePlacement, the single owner of
        // window-to-console coordinates; every pointer coordinate below is already a console
        // pixel.
        var layout = MusicEditorLayout.Compute(shell.BackBufferWidth, shell.BackBufferHeight);

        if (view.ExitPromptShown)
        {
            shell.Hover.Update(null, elapsedSeconds);        // dead buttons must not grow tooltips
            if (commands.Quit)
            {
                shell.Modes.HandleEscape();                  // Esc lowers the prompt: "stay"
            }
            else if (commands.MenuConfirm)
            {
                shell.Modes.SaveMusicAndClose();
            }
            else if (commands.MenuEditor)
            {
                shell.Modes.DiscardMusicAndClose();
            }
            else if (mouse.LeftPressed && layout.TryPromptVerb(mouse.X, mouse.Y, out EditorPromptVerb verb))
            {
                switch (verb)
                {
                    case EditorPromptVerb.SaveAndExit:
                        shell.Modes.SaveMusicAndClose();
                        break;
                    case EditorPromptVerb.Discard:
                        shell.Modes.DiscardMusicAndClose();
                        break;
                    default:
                        shell.Modes.HandleEscape();
                        break;
                }
            }
            view.Sync(layout, session);
            return;
        }

        // Alt+Left/Right walk the tab strip on every editor screen (REFERENCES-EDITORS §8 item
        // 16). Home is deliberately unbound here: it flips two graphics faces on the sprite and
        // map screens, and a fifth stop cannot be reached by a two-way toggle — the ring is what
        // Alt is for.
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
            shell.Modes.CycleEditorTab(commands.EditorTabNext ? 1 : -1);
            return;
        }

        if (commands.Quit)
        {
            shell.Modes.HandleEscape();          // clean → the library; dirty → the prompt
            return;
        }

        Keys(shell, session, view, layout, commands);
        Pointer(shell, session, view, layout, mouse, elapsedSeconds);
        view.Sync(layout, session);
    }

    /// <summary>
    /// The keyboard while the song has it: chords first (so Ctrl+Z is an undo and never a stop
    /// flag), then the digits, then everything that moves the cursor or the window.
    /// </summary>
    private static void Keys(
        in EditorShell shell, MusicEditorSession session, MusicEditorView view,
        in MusicEditorLayout layout, in ShellCommands commands)
    {
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
        // The clipboard chords, now through the MACHINE's clipboard as hex text
        // (REFERENCES-EDITORS §8 item 2; TIC-80's music editor routes its own tracker copy
        // through the same system hex buffer, §6.1). The read-only guard moved down into the
        // session, where it can say WHY on the message line instead of the chord silently doing
        // nothing — the refusal is the feature this wave owes the author.
        if (commands.EditorCopy)
        {
            shell.CopyText(session.CopySelectionToText());
        }
        if (commands.EditorCut)
        {
            shell.CopyText(session.CutSelectionToText());
        }
        if (commands.EditorPaste)
        {
            session.PasteFromText(session.CursorPattern, session.CursorChannel, shell.PasteText());
        }
        if (commands.CodeSelectAll)
        {
            session.SelectAll();                 // Ctrl+A, the one chord every text-shaped screen shares
        }
        if (commands.TogglePause)
        {
            view.TogglePlay(session);            // Space, the key all three references spend on this
        }
        if (commands.MusicSlotDigit != 0)
        {
            // 1-based in the frame, 0-based here — see the field's own comment.
            view.TypeDigit(session, commands.MusicSlotDigit - 1);
        }
        if (commands.EditorClear)
        {
            // Del over a marking empties the block, as it does on the map screen; over a bare
            // cursor it rests one cell and steps on, as a digit pair does.
            if (session.HasSelection && !session.BankReadOnly)
            {
                session.ClearSelectedCells();
            }
            else
            {
                view.EnterRest(session);
            }
        }
        if (commands.EditorGridToggle)
        {
            view.ToggleFlag(session, session.CursorPattern, MusicFlagColumn.LoopStart);      // `
        }
        if (commands.EditorRegionCycle)
        {
            view.ToggleFlag(session, session.CursorPattern, MusicFlagColumn.LoopEnd);        // Tab
        }
        if (commands.MenuEditor)
        {
            view.ToggleFlag(session, session.CursorPattern, MusicFlagColumn.Stop);           // X
        }
        if (commands.Slower && !session.BankReadOnly)
        {
            session.ShiftSelectionSlots(-1);     // [ and ] — the shell's own "one rung down/up" pair
        }
        if (commands.Faster && !session.BankReadOnly)
        {
            session.ShiftSelectionSlots(1);
        }
        if (commands.EditorLayerUp)
        {
            PageCursor(session, view, layout, -1);   // PgUp/PgDn walk the song a screenful at a time
        }
        if (commands.EditorLayerDown)
        {
            PageCursor(session, view, layout, 1);
        }

        // Shift+digits are the mute and solo rows; the reader hands them over as a 1-based digit
        // 1-8, which is the sprite screen's flag row on the sprite screen and this on this one.
        int flagDigit = commands.EditorFlagDigit;
        if (flagDigit > 0)
        {
            int index = flagDigit - 1;
            if (index < MusicEditorLayout.ChannelCount)
            {
                view.ToggleMute(index);
            }
            else
            {
                view.ToggleSolo(index - MusicEditorLayout.ChannelCount);
            }
        }

        // The arrows, in the one place the Shift chord and the bare key have to be told apart:
        // the reader emits both on a Shift+arrow frame (see ShellCommands.EditorSheetDx), and
        // this screen obeys the chord when there is one — exactly as the code and sound screens
        // choose between their own chords and the bare arrows.
        if (commands.EditorSheetDx != 0 || commands.EditorSheetDy != 0)
        {
            view.ExtendMark(
                session,
                Math.Clamp(session.CursorPattern + commands.EditorSheetDy, 0, MusicEditorLayout.PatternCount - 1),
                Math.Clamp(session.CursorChannel + commands.EditorSheetDx, 0, MusicEditorLayout.ChannelCount - 1));
            return;
        }
        if (commands.MenuUp)
        {
            view.MoveCursor(session, -1, 0);
        }
        if (commands.MenuDown)
        {
            view.MoveCursor(session, 1, 0);
        }
        if (commands.MenuLeft)
        {
            view.MoveCursor(session, 0, -1);
        }
        if (commands.MenuRight)
        {
            view.MoveCursor(session, 0, 1);
        }
    }

    /// <summary>One screenful of song, cursor and window together — the keyboard's twin of clicking the overview.</summary>
    private static void PageCursor(
        MusicEditorSession session, MusicEditorView view, in MusicEditorLayout layout, int direction)
    {
        view.MoveCursor(session, direction * layout.VisibleRows, 0);
        view.ScrollBy(layout, direction * layout.VisibleRows);
    }

    /// <summary>
    /// The mouse: hover, wheel, then the press chain — buttons, the channel header, the section
    /// markers, the grid, the overview — and the drag that follows it. Every one of these has the
    /// keyboard twin the parity law demands; the table in this file's type comment is the list.
    /// </summary>
    private static void Pointer(
        in EditorShell shell, MusicEditorSession session, MusicEditorView view,
        in MusicEditorLayout layout, in EditorMouse mouse, double elapsedSeconds)
    {
        // Buttons first, then this screen's buttonless controls — the same order the press chain
        // below tests them in, so the label and the click always name the same thing.
        shell.Hover.Update(
            layout.TryButton(mouse.X, mouse.Y, out EditorButton hovered)
                ? HoverTarget.OfButton(hovered)
                : layout.RegionAt(mouse.X, mouse.Y) is MusicRegion region and not MusicRegion.None
                    ? HoverTarget.OfMusicRegion(region)
                    : null,
            elapsedSeconds);

        if (mouse.WheelDelta != 0)
        {
            int notches = mouse.WheelDelta / 120;
            if (layout.TryChannelCell(mouse.X, mouse.Y, view.FirstPattern, out int wheelPattern, out int wheelChannel))
            {
                // The wheel over a cell steps its slot — the mouse's whole answer to typing a
                // number, and the reason this screen needs no spinner widget.
                view.StepSlot(session, wheelPattern, wheelChannel, notches);
            }
            else
            {
                view.ScrollBy(layout, -notches * WheelPatterns);
            }
        }

        if (mouse.LeftPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton pressed))
            {
                if (!EditorIcons.IsStub(pressed))
                {
                    HandleMusicButton(shell, session, view, pressed);
                }
                return;                         // a tab or the exit may have left this mode
            }
            if (layout.TryChannelToggle(mouse.X, mouse.Y, out int channel, out bool solo))
            {
                if (solo)
                {
                    view.ToggleSolo(channel);
                }
                else
                {
                    view.ToggleMute(channel);
                }
            }
            else if (layout.TryFlagCell(mouse.X, mouse.Y, view.FirstPattern, out int flagPattern, out MusicFlagColumn flag))
            {
                view.PlaceCursor(session, flagPattern, session.CursorChannel);
                view.ToggleFlag(session, flagPattern, flag);
            }
            else if (layout.TryChannelCell(mouse.X, mouse.Y, view.FirstPattern, out int pattern, out int cell))
            {
                view.BeginMark(session, pattern, cell);
            }
            else if (layout.TryOverviewPattern(mouse.X, mouse.Y, out int jump))
            {
                // The overview is the scroll control: the pattern clicked comes to the middle of
                // the window and the cursor lands on it, so one click both travels and points.
                view.PlaceCursor(session, jump, session.CursorChannel);
                view.ScrollTo(layout, jump - layout.VisibleRows / 2);
            }
        }
        else if (mouse.LeftDown && view.MarkDragActive)
        {
            // Clamped to the grid: a drag that leaves it keeps marking along its edge instead of
            // tearing, the way the map's strokes and the sound screen's do.
            layout.ClampCell(mouse.X, mouse.Y, view.FirstPattern, out int dragPattern, out int dragChannel);
            view.ExtendMark(session, dragPattern, dragChannel);
        }

        if (mouse.RightPressed
            && layout.TryChannelCell(mouse.X, mouse.Y, view.FirstPattern, out int restPattern, out int restChannel)
            && !session.BankReadOnly)
        {
            // The cell's second button: silence. Right-click rather than a second widget, because
            // "no sound here" is the other half of "this sound here" and Del is its key.
            view.PlaceCursor(session, restPattern, restChannel);
            session.ClearChannel(restPattern, restChannel);
        }

        if (mouse.LeftReleased)
        {
            view.EndMark();
            session.EndStroke();                // the gesture commits as one undo step
        }
    }

    /// <summary>
    /// The music screen's twin of its four siblings' own button handlers: tabs first (travel is
    /// the mode machine's verb), then <see cref="EditorIcons.ClickMusicButton"/>, the headless
    /// routing table the music screen's button-contract test clicks every placed button through.
    /// </summary>
    private static void HandleMusicButton(
        in EditorShell shell, MusicEditorSession session, MusicEditorView view, EditorButton button)
    {
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            shell.Modes.SwitchEditorTab(tab);
            return;
        }
        if (EditorIcons.ClickMusicButton(session, view, button))
        {
            shell.Modes.HandleEscape();
        }
    }
}

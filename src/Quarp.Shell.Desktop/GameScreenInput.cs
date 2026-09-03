namespace Quarp.Shell.Desktop;

/// <summary>
/// The <b>game</b> screen's input router — the sixth of its kind, and the one that did not exist
/// until M9 stage 5 made the running cartridge a tab like the five that edit it. Same shape as
/// its siblings and for the same reason (<see cref="EditorShell"/>): it names no MonoGame type
/// beyond the pointer struct, so a headless test drives a whole frame of the game screen through
/// the production code instead of through a copy of it. What stays in <c>QuarpGame.UpdateGame</c>
/// is what genuinely needs a window: the frame clock, the speaker, and the cartridge's own input
/// snapshot.
///
/// <para><b>The two keyboards of this screen.</b> While the pause menu is down the keys mean what
/// they have meant since M2 — Space pauses, <c>,</c> and <c>.</c> step, <c>[</c> and <c>]</c>
/// change speed, F5 and F8 write and play a replay — and Esc, which used to destroy the session,
/// now raises the menu. While the menu is up the same frame means something else: the arrows walk
/// the rows, Enter or Z chooses, and F1..F6 leave for the tabs. Exactly two commands are withheld
/// from the session in that state, and both are withheld because the key has a second meaning
/// here:
/// <list type="bullet">
///   <item><c>Space</c> — <c>TogglePause</c> would let the simulation run out from under an open
///     menu, which is the one thing the menu exists to prevent;</item>
///   <item><c>F5</c> and <c>F8</c> — F5 is <see cref="ShellCommands.SaveReplay"/> <em>and</em>
///     the fifth tab key (<see cref="ShellCommands.EditorTabJump"/>); one physical edge cannot
///     write a file and travel at the same time, so on an open menu the tab wins and the replay
///     keys are not offered. F8 goes with it rather than being the odd survivor of a pair.</item>
/// </list>
/// Everything else — the two step keys, Home, the speed ladder, held Backspace — reaches the
/// session unchanged, so the shorter menu of stage 5a took away rows and not verbs (Р2).</para>
///
/// <para><b>What stage 5a added here.</b> Two things the menu's rows used to be: the scrubber
/// (the left and right arrows, <b>held</b>, on the STEP row — and the same two arrows under the
/// pointer, which is the identical gesture by Р4), and the top band, whose exit arrow and six
/// tabs are clickable while the game stands paused. Both are routed here rather than in the
/// window for the reason the rest of this file exists: a headless test can then press them.</para>
/// </summary>
public static class GameScreenInput
{
    /// <summary>
    /// One frame of the game screen. <paramref name="mouse"/> is in <b>console</b> pixels — the
    /// window converts through <see cref="FramePlacement"/> before calling, exactly as it does
    /// for the five editor screens, so this router does no scale arithmetic and neither may any
    /// other reader. <paramref name="elapsedSeconds"/> is the frame's own length: the hover
    /// clock and the scrubber's ramp are both measured in real time, not in frames.
    /// </summary>
    public static void Update(
        in EditorShell shell, in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        ShellModeMachine modes = shell.Modes;
        if (modes.Mode != ShellMode.Game)
        {
            return;
        }
        if (commands.Quit)
        {
            modes.TogglePauseMenu();
            return;         // the frame that opens or closes the menu chooses nothing else
        }
        if (!modes.PauseMenu.Shown)
        {
            // Playing. Every time-control key straight through, the way it has been since M2.
            modes.Session?.ApplyCommands(commands);
            shell.Hover.Update(null, elapsedSeconds);   // no band while the game runs, so nothing to hover
            return;
        }
        MenuFrame(shell, commands, mouse, elapsedSeconds);
    }

    /// <summary>
    /// One frame with the menu up: a tab key first (leaving outranks moving a cursor on a screen
    /// you are leaving), then <b>the scrubber</b>, then the band, then the pointer on the rows,
    /// then the keyboard's own walk-and-choose, then whatever time control survives the filter
    /// above.
    ///
    /// <para><b>Where the deferred move is committed, and why it is not here.</b> A backward move
    /// the frame could not afford is held until the arrow is released
    /// (<see cref="CartSession.ScrubTo"/>), so on any frame the author may be holding a travel
    /// that has not happened yet. Every road off this screen therefore has to make that move
    /// first — and there are more of them than this method has statements: Esc (which returns
    /// from <see cref="Update"/> before ever reaching here), the six tab keys and Alt+arrows
    /// above, the band's tab buttons inside <see cref="BandFrame"/>, a click or Enter on RESUME.
    /// An earlier draft of this comment claimed the ordering below closed that hole "by
    /// construction"; it did not — three of those roads returned before the scrubber ran, and
    /// pressing Esc on a menu printing tick 99 870 resumed the cartridge on 100 000. The rule
    /// now has one owner and it is not a statement order:
    /// <see cref="ShellModeMachine.CommitScrub"/>, called by the machine's own exits.</para>
    /// </summary>
    private static void MenuFrame(
        in EditorShell shell, in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        ShellModeMachine modes = shell.Modes;
        if (EditorIcons.EditorTabForNumber(commands.EditorTabJump) is ShellMode named)
        {
            modes.SwitchEditorTab(named);
            return;
        }
        if (commands.EditorTabPrev || commands.EditorTabNext)
        {
            modes.CycleEditorTab(commands.EditorTabNext ? 1 : -1);
            return;
        }
        PauseMenu menu = modes.PauseMenu;
        int scrub = ScrubDirection(menu, commands, mouse, shell);
        modes.ScrubFrame(scrub, elapsedSeconds);
        if (BandFrame(shell, mouse, elapsedSeconds))
        {
            return;         // the pointer is on the band: it travels, or it hovers, and nothing else
        }

        if (menu.TryItem(mouse.X, mouse.Y, shell.BackBufferWidth, shell.BackBufferHeight, out int row))
        {
            // Hovering moves the cursor, so the row that lights up and the row a click takes are
            // never two different rows — the same promise the editors' swatches and tool slots
            // make with their hover highlight.
            menu.Select(row);
            bool onArrow = menu.TryScrubArrow(
                mouse.X, mouse.Y, shell.BackBufferWidth, shell.BackBufferHeight, out _);
            if (mouse.LeftPressed && !onArrow)
            {
                // A press on one of the scrub row's arrows is that arrow's verb, not the row's;
                // the row has no Enter verb anyway (see ShellModeMachine.ActivatePauseMenuItem).
                // The question is where the POINTER is, not whether anything is scrubbing: the
                // earlier "scrub == 0" swallowed a click on any row at all while an arrow KEY was
                // held, so an author holding Left and clicking RESUME got nothing.
                Activate(shell);
                return;
            }
        }
        if (commands.MenuUp)
        {
            menu.Move(-1);
        }
        if (commands.MenuDown)
        {
            menu.Move(+1);
        }
        if (commands.MenuConfirm)
        {
            Activate(shell);
            return;
        }
        // The time keys the menu no longer prints but still answers to (Р2). Any of them means
        // the author moved time by another road, so the scrubber's aim goes back on the session
        // afterwards rather than staying where the arrows left it — afterwards and not before, so
        // the number the menu prints is the tick the key just landed on.
        modes.Session?.ApplyCommands(commands with { TogglePause = false, SaveReplay = false, PlayReplay = false });
        if (commands.StepBack || commands.StepForward || commands.ToStart || commands.Rewinding)
        {
            modes.CancelScrub();
        }
    }

    /// <summary>
    /// The pointer on the paused game's top band: hover (with the shell's three-second clock) and
    /// clicks on the exit arrow and the six tabs.
    /// </summary>
    /// <returns>True when the pointer is on a button of the band, so the rest of the frame stops.</returns>
    private static bool BandFrame(in EditorShell shell, in EditorMouse mouse, double elapsedSeconds)
    {
        ShellModeMachine modes = shell.Modes;
        GameTabBar bar = GameTabBar.Compute(shell.BackBufferWidth, shell.BackBufferHeight);
        if (!bar.TryButton(mouse.X, mouse.Y, out EditorButton id))
        {
            shell.Hover.Update(null, elapsedSeconds);
            return false;
        }
        shell.Hover.Update(HoverTarget.OfButton(id), elapsedSeconds);
        if (!mouse.LeftPressed)
        {
            return true;
        }
        if (EditorIcons.TabTarget(id) is ShellMode target)
        {
            modes.SwitchEditorTab(target);
            return true;
        }
        if (id == EditorButton.ExitTab)
        {
            // The band's arrow and the menu's EXIT row are one door, by the owner's decision
            // (stage 5a, Р1): back to the library the author came from, and the unsaved-work
            // question on the way, because LeaveGame owns both.
            modes.LeaveGame();
        }
        return true;
    }

    /// <summary>
    /// Which way the scrubber is being pushed this frame: the pointer holding one of the row's
    /// two arrows, or — with the cursor standing on that row — the left or right arrow key held.
    /// Zero when neither, which is the frame a deferred backward move is finally made on.
    ///
    /// <para>The pointer is checked first and does not care which row is selected: a click is a
    /// direct verb on the thing under it. The keys are gated on the selection because on a menu
    /// the arrows belong to the row the cursor is on — the owner's own words for this row,
    /// "стоя на этой строке".</para>
    /// </summary>
    private static int ScrubDirection(
        PauseMenu menu, in ShellCommands commands, in EditorMouse mouse, in EditorShell shell)
    {
        if (mouse.LeftDown
            && menu.TryScrubArrow(
                mouse.X, mouse.Y, shell.BackBufferWidth, shell.BackBufferHeight, out int arrow))
        {
            return arrow;
        }
        if (menu.Current != PauseMenuItem.Scrub)
        {
            return 0;
        }
        return (commands.MenuRightHeld ? 1 : 0) - (commands.MenuLeftHeld ? 1 : 0);
    }

    /// <summary>
    /// Choosing a row. The session the machine may have just started is handed to the window
    /// through <see cref="ShellModeMachine.Session"/> rather than returned, because this router
    /// returns nothing to anybody; <c>QuarpGame.UpdateGame</c> notices the new instance and wires
    /// its speaker and title, the same bookkeeping every other launch road gets.
    /// </summary>
    private static void Activate(in EditorShell shell) => shell.Modes.ActivatePauseMenuItem();
}

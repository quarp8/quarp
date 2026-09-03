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
/// session unchanged, so the menu's rows and the keys it advertises are the same verbs.</para>
/// </summary>
public static class GameScreenInput
{
    /// <summary>
    /// One frame of the game screen. <paramref name="mouse"/> is in <b>console</b> pixels — the
    /// window converts through <see cref="FramePlacement"/> before calling, exactly as it does
    /// for the five editor screens, so this router does no scale arithmetic and neither may any
    /// other reader.
    /// </summary>
    public static void Update(in EditorShell shell, in ShellCommands commands, in EditorMouse mouse)
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
            return;
        }
        MenuFrame(shell, commands, mouse);
    }

    /// <summary>
    /// One frame with the menu up: travel first (a tab key means "leave", and leaving outranks
    /// moving a cursor on a screen you are leaving), then the pointer, then the keyboard's own
    /// walk-and-choose, then whatever time control survives the filter above.
    /// </summary>
    private static void MenuFrame(in EditorShell shell, in ShellCommands commands, in EditorMouse mouse)
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
        if (menu.TryItem(mouse.X, mouse.Y, shell.BackBufferWidth, shell.BackBufferHeight, out int row))
        {
            // Hovering moves the cursor, so the row that lights up and the row a click takes are
            // never two different rows — the same promise the editors' swatches and tool slots
            // make with their hover highlight.
            menu.Select(row);
            if (mouse.LeftPressed)
            {
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
        // The time keys the menu itself advertises, minus the two the type comment names.
        modes.Session?.ApplyCommands(commands with { TogglePause = false, SaveReplay = false, PlayReplay = false });
    }

    /// <summary>
    /// Choosing a row. The session the machine may have just started is handed to the window
    /// through <see cref="ShellModeMachine.Session"/> rather than returned, because this router
    /// returns nothing to anybody; <c>QuarpGame.UpdateGame</c> notices the new instance and wires
    /// its speaker and title, the same bookkeeping every other launch road gets.
    /// </summary>
    private static void Activate(in EditorShell shell) => shell.Modes.ActivatePauseMenuItem();
}

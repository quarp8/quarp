namespace Quarp.Shell.Desktop;

/// <summary>
/// The sound editor's input router — the fourth member of the family
/// <see cref="SpriteEditorInput"/>, <see cref="MapEditorInput"/> and
/// <see cref="CodeEditorInput"/> started, written where a test can call it (see
/// <see cref="EditorShell"/> for why the window is two integers). It owns dispatch and nothing
/// else: geometry belongs to <see cref="SfxEditorLayout"/>, the slot, the cursor, the pen and the
/// playback request to <see cref="SfxEditorView"/>, the 4352 bytes to
/// <see cref="SfxEditorSession"/>, the button table to <see cref="EditorIcons"/>, mode policy to
/// <see cref="ShellModeMachine"/>, and the speaker to <c>QuarpGame</c> — which is a different
/// half of the frame and deliberately not this file's business.
///
/// <para><b>The keyboard, and why it reads letters other screens read as verbs.</b> The piano
/// rows <c>zsxdcvgbhnjm</c> and <c>q2w3er5t6y7ui</c> are TIC-80's and PICO-8's letter for letter
/// (REFERENCES-EDITORS §8 item 17: a de-facto standard, and drifting from it is forbidden), so on
/// this screen Z is the note C and not "confirm", V is F# and not "flip vertically", and 2 is C#
/// and not "the pencil". Those keys still arrive in <see cref="ShellCommands"/> under their old
/// names, because one physical press has one owner in the reader; what differs is which field
/// this router reads. That is the same resolution <see cref="ShellCommands.EditorSheetDx"/> and
/// <see cref="ShellCommands.MenuLeft"/> have had since wave 2h — <b>the gate belongs where the
/// meaning differs</b> — and the list of fields this screen deliberately ignores is written out
/// in <see cref="ShellCommands.SfxPianoKey"/>.</para>
///
/// <para><b>The whole map, and its mouse twin for every entry</b> (the input-parity law of M9
/// stage 2.5, which is two-way: no key without a click, no click without a key):</para>
/// <list type="table">
///   <item><term>piano rows</term><description>note at the cursor, cursor moves on — a cell of the pitch grid</description></item>
///   <item><term>Left / Right</term><description>the step cursor — every grid click puts it on the column it landed in</description></item>
///   <item><term>Up / Down</term><description>the pen's volume, applied to the cursor's step — a cell of the volume grid</description></item>
///   <item><term>PgUp / PgDn</term><description>previous / next slot — a cell of the slot selector</description></item>
///   <item><term>[ / ]</term><description>octave — the octave field's two steppers</description></item>
///   <item><term>Shift+Left / Right</term><description>speed — the speed field's steppers</description></item>
///   <item><term>Shift+Up / Down</term><description>length — the length field's steppers</description></item>
///   <item><term>, / .</term><description>the pen's waveform — a cell of the wave row</description></item>
///   <item><term>F</term><description>the pen's effect — a cell of the effect row</description></item>
///   <item><term>`</term><description>loop start at the cursor — a left click on the loop row</description></item>
///   <item><term>Tab</term><description>loop end after the cursor — a right click on the loop row</description></item>
///   <item><term>Del</term><description>the cursor's step becomes a rest — the volume grid's bottom row</description></item>
///   <item><term>Space</term><description>play / stop — the play button</description></item>
///   <item><term>Ctrl+Z / Y / S</term><description>undo, redo, save — the three status buttons</description></item>
///   <item><term>Esc, Alt+Left/Right</term><description>leave, walk the tabs — the exit tab and the tab strip</description></item>
/// </list>
///
/// <para>While the exit prompt is up it owns the input (Z saves and leaves, X discards, Esc
/// stays, and the same three verbs are clickable on the prompt line) and everything else — the
/// piano included — is deliberately deaf: a stray keystroke must not change the bank the author
/// is being asked about.</para>
/// </summary>
public static class SfxEditorInput
{
    /// <summary>Slots a wheel notch walks over the selector — one, because a slot is a whole sound and not a line of text.</summary>
    private const int WheelSlots = 1;

    /// <summary>
    /// One frame of the sound editor. Input parity is the law of this frame as it is of its three
    /// siblings: every live action has a key path and a click path, and both funnel into the same
    /// owner — <see cref="EditorIcons.ClickSfxButton"/> for the buttons,
    /// <see cref="SfxEditorView"/> for the pen and the cursor, the session for every byte — so
    /// neither channel can drift.
    /// </summary>
    public static void Update(
        in EditorShell shell, in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        SfxEditorSession session = shell.Modes.SfxEditor!;
        SfxEditorView view = shell.Modes.SfxView!;
        // The same layout the renderer will draw this frame — geometry has one owner. Since
        // wave R5 the two numbers <see cref="EditorShell"/> carries are the size of the surface
        // this screen is laid out on, and that surface is the console itself (ADR-029): 160x90,
        // not the back buffer. Nothing in this file changed to say so — the router lays out and
        // hit-tests in whatever surface it is handed, which is exactly why the same type served
        // this screen before the move and after it. The conversions happen in QuarpGame, through
        // FramePlacement, the single owner of window-to-console coordinates; every pointer
        // coordinate below is therefore already a console pixel.
        var layout = SfxEditorLayout.Compute(shell.BackBufferWidth, shell.BackBufferHeight);

        if (view.ExitPromptShown)
        {
            shell.Hover.Update(null, elapsedSeconds);        // dead buttons must not grow tooltips
            if (commands.Quit)
            {
                shell.Modes.HandleEscape();                  // Esc lowers the prompt: "stay"
            }
            else if (commands.MenuConfirm)
            {
                shell.Modes.SaveSfxAndClose();
            }
            else if (commands.MenuEditor)
            {
                shell.Modes.DiscardSfxAndClose();
            }
            else if (mouse.LeftPressed && layout.TryPromptVerb(mouse.X, mouse.Y, out EditorPromptVerb verb))
            {
                switch (verb)
                {
                    case EditorPromptVerb.SaveAndExit:
                        shell.Modes.SaveSfxAndClose();
                        break;
                    case EditorPromptVerb.Discard:
                        shell.Modes.DiscardSfxAndClose();
                        break;
                    default:
                        shell.Modes.HandleEscape();
                        break;
                }
            }
            return;
        }

        // Alt+Left/Right walk the tab strip on every editor screen (REFERENCES-EDITORS §8 item
        // 16). Home is deliberately unbound here: it flips two graphics faces on the sprite and
        // map screens, and a third stop cannot be reached by a two-way toggle — the ring is what
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

        Keys(shell, session, view, commands);

        Pointer(shell, session, view, layout, mouse, elapsedSeconds);
    }

    /// <summary>
    /// The keyboard while the bank has it: chords first (so Ctrl+Z is an undo and never the note
    /// C), then the piano, then everything that moves a number. The order matters exactly once,
    /// and it is the piano: <see cref="ShellCommands.SfxPianoKey"/> is already Ctrl- and
    /// Shift-guarded by the reader, so a chord and a note can never arrive on the same frame.
    /// </summary>
    private static void Keys(
        in EditorShell shell, SfxEditorSession session, SfxEditorView view, in ShellCommands commands)
    {
        // The clipboard chords, on TIC-80's own three keys for this screen (REFERENCES-EDITORS
        // §5.1 "Ctrl+X/C/V | буфер"). The unit is the WHOLE SLOT the author is standing on —
        // TIC-80's toClipboard(effect, sizeof(tic_sample)), "весь сэмпл целиком" — and the slot
        // is the selection this screen already has (SfxEditorView.SelectedSlot). This router is
        // the only piece of the sound screen that knows a clipboard exists; the session takes
        // and returns a plain string and stays headless.
        if (commands.EditorCopy)
        {
            shell.CopyText(session.CopySlotToText(view.SelectedSlot));
        }
        if (commands.EditorCut)
        {
            shell.CopyText(session.CutSlotToText(view.SelectedSlot));
        }
        if (commands.EditorPaste)
        {
            session.PasteSlotFromText(view.SelectedSlot, shell.PasteText());
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
        if (commands.TogglePause)
        {
            view.TogglePlay();                   // Space, the key all three references spend on this
        }
        if (commands.SfxPianoKey != 0)
        {
            // 1-based in the frame, 0-based in the layout — see the field's own comment.
            view.PlayPianoKey(session, commands.SfxPianoKey - 1);
        }
        if (commands.EditorLayerUp)
        {
            view.StepSlot(-1);                   // PgUp/PgDn walk the bank, the layer tabs' keys one screen over
        }
        if (commands.EditorLayerDown)
        {
            view.StepSlot(1);
        }
        if (commands.Slower)
        {
            view.StepOctave(-1);                 // [ and ], the shell's own "one rung down/up" pair
        }
        if (commands.Faster)
        {
            view.StepOctave(1);
        }
        if (commands.EditorColorPrev)
        {
            view.CycleWave(-1);                  // , and . — the sheet editor's colour keys, and a wave IS the pen's colour here
            view.ApplyPenToCursor(session);
        }
        if (commands.EditorColorNext)
        {
            view.CycleWave(1);
            view.ApplyPenToCursor(session);
        }
        if (commands.EditorFlipH)
        {
            view.CycleEffect(1);                 // F: the one piano-free letter with a field already on it
            view.ApplyPenToCursor(session);
        }
        if (commands.EditorGridToggle)
        {
            view.ToggleLoopStart(session, view.CursorStep);      // `
        }
        if (commands.EditorRegionCycle)
        {
            view.ToggleLoopEnd(session, view.CursorStep);        // Tab
        }
        if (commands.EditorClear)
        {
            view.EraseCursorStep(session);       // Del
        }

        // The arrows, in the one place the Shift chord and the bare key have to be told apart:
        // the reader emits both on a Shift+arrow frame (see ShellCommands.EditorSheetDx), and
        // this screen obeys the chord when there is one — exactly as the code screen chooses
        // between Ctrl+Left and Left.
        if (commands.EditorSheetDx != 0 || commands.EditorSheetDy != 0)
        {
            if (commands.EditorSheetDx != 0)
            {
                view.StepField(session, SfxField.Speed, commands.EditorSheetDx);
            }
            if (commands.EditorSheetDy != 0)
            {
                // EditorSheetDy counts DOWN as positive (it walks a sheet's rows); a length grows
                // upward on this screen, so the sign flips here and nowhere else.
                view.StepField(session, SfxField.Length, -commands.EditorSheetDy);
            }
            return;
        }
        if (commands.MenuLeft)
        {
            view.StepCursor(-1);
        }
        if (commands.MenuRight)
        {
            view.StepCursor(1);
        }
        if (commands.MenuUp)
        {
            view.StepVolume(session, 1);
        }
        if (commands.MenuDown)
        {
            view.StepVolume(session, -1);
        }
    }

    /// <summary>
    /// The mouse: hover, wheel, then the press chain — buttons, the three grids, the selector,
    /// the two cell rows, the stepper fields — and the drags that follow it. Every one of these
    /// has the keyboard twin the parity law demands; the table in this file's type comment is the
    /// list, and <c>SfxEditorTests</c> walks it.
    /// </summary>
    private static void Pointer(
        in EditorShell shell, SfxEditorSession session, SfxEditorView view,
        in SfxEditorLayout layout, in EditorMouse mouse, double elapsedSeconds)
    {
        // Buttons first, then this screen's buttonless controls — the same order the press chain
        // below tests them in, so the label and the click always name the same thing.
        shell.Hover.Update(
            layout.TryButton(mouse.X, mouse.Y, out EditorButton hovered)
                ? HoverTarget.OfButton(hovered)
                : layout.RegionAt(mouse.X, mouse.Y) is SfxRegion region and not SfxRegion.None
                    ? HoverTarget.OfSfxRegion(region)
                    : null,
            elapsedSeconds);

        if (mouse.WheelDelta != 0 && layout.Slots.Contains(mouse.X, mouse.Y))
        {
            // The wheel over the selector walks the bank — the same gesture the sheet slider
            // gives the sprite screen, over the thing this screen has 64 of.
            view.StepSlot(-mouse.WheelDelta / 120 * WheelSlots);
        }

        if (mouse.LeftPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton pressed))
            {
                if (!EditorIcons.IsStub(pressed))
                {
                    HandleSfxButton(shell, session, view, pressed);
                }
                return;                         // a tab or the exit may have left this mode
            }
            if (layout.TrySlotCell(mouse.X, mouse.Y, out int slot))
            {
                view.SelectSlot(slot);
            }
            else if (layout.TryPitchCell(mouse.X, mouse.Y, out int step, out int semitone))
            {
                session.BeginStroke();          // a drag across the grid is ONE undo step
                view.BeginPitchDrag();
                view.WriteNoteAt(session, step, semitone);
            }
            else if (layout.TryVolumeCell(mouse.X, mouse.Y, out int volumeStep, out int level))
            {
                session.BeginStroke();
                view.BeginVolumeDrag();
                view.WriteVolumeAt(session, volumeStep, level);
            }
            else if (layout.TryLoopCell(mouse.X, mouse.Y, out int loopStep))
            {
                view.ToggleLoopStart(session, loopStep);
            }
            else if (layout.TryWaveCell(mouse.X, mouse.Y, out int wave))
            {
                view.SelectWave(wave);
                view.ApplyPenToCursor(session);
            }
            else if (layout.TryEffectCell(mouse.X, mouse.Y, out int effect))
            {
                view.SelectEffect(effect);
                view.ApplyPenToCursor(session);
            }
            else if (layout.TryFieldStepper(mouse.X, mouse.Y, out SfxField field, out int delta))
            {
                view.StepField(session, field, delta);
            }
        }
        else if (mouse.LeftDown && view.PitchDragActive)
        {
            // Clamped to the grid: a drag that leaves it keeps writing along its edge instead of
            // tearing, the way the map's strokes and the code screen's selections do.
            layout.ClampPitchCell(mouse.X, mouse.Y, out int dragStep, out int dragSemitone);
            view.WriteNoteAt(session, dragStep, dragSemitone);
        }
        else if (mouse.LeftDown && view.VolumeDragActive)
        {
            layout.ClampPitchCell(mouse.X, mouse.Y, out int dragStep, out _);
            if (layout.TryVolumeCell(mouse.X, mouse.Y, out int overStep, out int overLevel))
            {
                view.WriteVolumeAt(session, overStep, overLevel);
            }
            else
            {
                view.SetCursor(dragStep);       // off the grid vertically: follow the column, write nothing
            }
        }

        if (mouse.RightPressed && layout.TryLoopCell(mouse.X, mouse.Y, out int loopEndStep))
        {
            // The loop row's second button: the end marker. Right-click rather than a second
            // widget, because the two markers are two facts about the same 32 columns and a
            // second row of arrows would double the row for one bit of information.
            view.ToggleLoopEnd(session, loopEndStep);
        }

        if (mouse.LeftReleased)
        {
            view.EndDrags();
            session.EndStroke();                // the gesture commits as one undo step
        }
    }

    /// <summary>
    /// The sound screen's twin of its three siblings' own button handlers: tabs first (travel is
    /// the mode machine's verb), then <see cref="EditorIcons.ClickSfxButton"/>, the headless
    /// routing table the sound screen's button-contract test clicks every placed button through.
    /// </summary>
    private static void HandleSfxButton(
        in EditorShell shell, SfxEditorSession session, SfxEditorView view, EditorButton button)
    {
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            shell.Modes.SwitchEditorTab(tab);
            return;
        }
        if (EditorIcons.ClickSfxButton(session, view, button))
        {
            shell.Modes.HandleEscape();
        }
    }
}

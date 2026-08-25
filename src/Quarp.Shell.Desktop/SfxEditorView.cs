namespace Quarp.Shell.Desktop;

/// <summary>
/// What the sound editor looks like right now, as opposed to what the cartridge <em>is</em>: the
/// slot on screen, the step the cursor stands on, the octave the piano is playing, the note
/// properties the pen is holding, the two live pointer gestures, whether the slot is sounding,
/// and the footer's exit question. Headless like <see cref="MapEditorView"/> and
/// <see cref="CodeEditorView"/>, and for the same reason — every claim about it is a plain unit
/// test instead of a mouse at a window.
///
/// <para><b>One owner each, and they do not overlap.</b> The 4352 bytes belong to
/// <see cref="SfxEditorSession"/> and are never copied here; the geometry belongs to
/// <see cref="SfxEditorLayout"/> and arrives as a parameter; everything the author is
/// <em>looking at or holding</em> belongs here and nowhere else. That split is what lets the
/// session stay a pure document: it has no idea a screen exists, and the screen never has an
/// opinion about what the bank says.</para>
///
/// <para><b>The pen is one fact.</b> A note has four fields — semitone, waveform, volume,
/// effect — and only the semitone is chosen by the gesture that writes it (a piano key, a cell
/// in the pitch grid). The other three come from the pen, so the wave row, the effect row, the
/// volume grid and the octave field are all editing the same one thing, and both input channels
/// end at the session's single <see cref="SfxEditorSession.SetStep"/>. PICO-8 says the same
/// thing about its own editor — "using the currently selected instrument" — and it is what stops
/// "what will this key type" from having two answers.</para>
///
/// <para><b>Playback is asked for here and performed by the wiring.</b> This type owns
/// <see cref="PlayWanted"/> ("the author asked for the slot"), <see cref="PlayEpoch"/> ("and
/// this is a different asking from the last one") and <see cref="Playing"/> ("and the chip says
/// it is sounding"). It owns no synthesizer and no speaker, because it may own neither: the one
/// owner of synthesis is <c>Quarp.Core.Audio.Apu</c>, the one owner of the speaker is
/// <see cref="AudioOutput"/>, and both live above this layer. <c>QuarpGame</c> reads these three
/// members, drives the APU with the session's own payload and reports back through
/// <see cref="ReportPlaying"/> — the same shape the boot jingle already uses, and the reason
/// there is no second synthesizer anywhere in the shell.</para>
///
/// <para><b>The read-only bank is refused here, not at the session's door.</b> Every writing
/// verb below returns early when <see cref="SfxEditorSession.BankReadOnly"/> is true, which is
/// exactly where <c>MapEditorPaint</c> puts the same guard for <c>map.csv</c>: the session still
/// throws (that is its contract and its second lock), but a screen must not throw at an author
/// who pressed a key — it must do nothing and keep saying why on the prompt line.</para>
///
/// <para><b>Why the exit prompt lives here.</b> Same reading as its two siblings: the session is
/// the model and has no screen state, and the decision is not duplicated —
/// <see cref="RequestClose"/> asks the session the one question it owns
/// (<see cref="SfxEditorSession.IsDirty"/>) and applies the sprite editor's answer table, so
/// unsaved notes leave only through an explicit Z or X.</para>
/// </summary>
public sealed class SfxEditorView
{
    /// <summary>
    /// Semitones in the piano row's lower octave — <c>zsxdcvgbhnjm</c>, twelve keys, C to B.
    /// The upper row <c>q2w3er5t6y7ui</c> is thirteen: the same twelve an octave up plus the C
    /// that closes it, which is exactly how PICO-8 prints the pair and how TIC-80's
    /// <c>drawPianoOctave</c> lays its keys out. Reproduced here rather than reinterpreted,
    /// because REFERENCES-EDITORS §8 item 17 calls it a de-facto standard and forbids drifting
    /// from it.
    /// </summary>
    public const int PianoLowerKeys = 12;

    /// <summary>Keys in the upper piano row: the twelve of the octave above plus its closing C.</summary>
    public const int PianoUpperKeys = 13;

    /// <summary>Piano keys in both rows together — the range <see cref="SfxEditorInput"/> hands in.</summary>
    public const int PianoKeys = PianoLowerKeys + PianoUpperKeys;

    /// <summary>The highest octave the piano can stand in: 5, whose C is note 60 and whose top is the bank's D#7.</summary>
    public const int MaxOctave = SfxEditorSession.MaxNote / SfxEditorLayout.OctaveRows;

    /// <summary>
    /// Where the piano opens: octave 3, i.e. C-5. The middle of the bank's five and a third
    /// octaves, and the register a chiptune lead actually lives in — an editor that opened on
    /// C-2 would greet every author with a rumble.
    /// </summary>
    public const int DefaultOctave = 3;

    /// <summary>The volume the pen opens at: two thirds of the way up, the same idea as the sheet editor opening on a mid-palette colour.</summary>
    public const int DefaultVolume = 5;

    /// <summary>Which of the 64 slots is on screen. TIC-80's <c>sfx-&gt;index</c>; the selector and PgUp/PgDn write it.</summary>
    public int SelectedSlot { get; private set; }

    /// <summary>Which of the 32 steps the cursor stands on — where a piano key writes and where Del erases.</summary>
    public int CursorStep { get; private set; }

    /// <summary>The octave the piano rows and the pitch grid are showing, 0-<see cref="MaxOctave"/>.</summary>
    public int Octave { get; private set; } = DefaultOctave;

    /// <summary>The waveform the pen writes, 0-5.</summary>
    public int PenWave { get; private set; }

    /// <summary>The volume the pen writes, 1-7. Never 0: a rest is written by erasing, not by holding a silent pen.</summary>
    public int PenVolume { get; private set; } = DefaultVolume;

    /// <summary>The note effect the pen writes, 0-6.</summary>
    public int PenEffect { get; private set; }

    /// <summary>True while the dirty-exit question is on the footer line; the router then gives it the input.</summary>
    public bool ExitPromptShown { get; private set; }

    /// <summary>True between the press and the release of a drag across the pitch grid.</summary>
    public bool PitchDragActive { get; private set; }

    /// <summary>True between the press and the release of a drag across the volume grid.</summary>
    public bool VolumeDragActive { get; private set; }

    /// <summary>
    /// The author has asked for this slot to sound and has not asked it to stop. Written by
    /// <see cref="RequestPlay"/>, <see cref="RequestStop"/> and by the wiring's report that the
    /// chip has run out of steps.
    /// </summary>
    public bool PlayWanted { get; private set; }

    /// <summary>
    /// Bumped by every fresh <see cref="RequestPlay"/>. The wiring compares it with the epoch it
    /// last started, so pressing play twice restarts the slot from step 0 instead of being
    /// swallowed as "already playing" — which is the whole gesture when an author is auditioning
    /// one note change at a time.
    /// </summary>
    public int PlayEpoch { get; private set; }

    /// <summary>What the chip actually reports, as opposed to what was asked for — the play button's lit state.</summary>
    public bool Playing { get; private set; }

    // ---- selection and cursor ----

    /// <summary>The selector's click and PgUp/PgDn's key: which slot is on screen. Out of range throws — the two callers clamp.</summary>
    public void SelectSlot(int slot)
    {
        if (slot is < 0 or >= SfxEditorSession.SlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot), slot, $"the bank holds slots 0-{SfxEditorSession.SlotCount - 1}.");
        }
        if (slot == SelectedSlot)
        {
            return;
        }
        SelectedSlot = slot;
        CursorStep = 0;         // a new slot is read from its beginning
        RequestStop();          // and the sound of the old one has nothing to do with it
    }

    /// <summary>PgUp/PgDn: one slot along the bank, clamped at both ends. Wrapping would make "which slot am I on" a guess.</summary>
    public void StepSlot(int delta) =>
        SelectSlot(Math.Clamp(SelectedSlot + delta, 0, SfxEditorSession.SlotCount - 1));

    /// <summary>Puts the cursor on a step. Out of range throws: the grids clamp before calling in.</summary>
    public void SetCursor(int step)
    {
        if (step is < 0 or >= SfxEditorSession.StepCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step), step, $"a slot holds steps 0-{SfxEditorSession.StepCount - 1}.");
        }
        CursorStep = step;
    }

    /// <summary>
    /// The arrows, and what a piano key does after it writes: one step along, clamped. Clamped
    /// and not wrapped because a note entered at the end of a slot must not silently overwrite
    /// its beginning — the one place in this editor where a wrap would destroy work.
    /// </summary>
    public void StepCursor(int delta) =>
        CursorStep = Math.Clamp(CursorStep + delta, 0, SfxEditorSession.StepCount - 1);

    // ---- the pen ----

    /// <summary>The octave field and the [ ] keys, clamped to the bank's range.</summary>
    public void SetOctave(int octave) => Octave = Math.Clamp(octave, 0, MaxOctave);

    /// <summary>One octave up or down.</summary>
    public void StepOctave(int delta) => SetOctave(Octave + delta);

    /// <summary>The wave row's click. Out of range throws — the row has exactly six cells.</summary>
    public void SelectWave(int wave)
    {
        if (wave is < 0 or >= SfxEditorSession.WaveCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wave), wave, $"profile 8 defines waves 0-{SfxEditorSession.WaveCount - 1}.");
        }
        PenWave = wave;
    }

    /// <summary>The , and . keys: the next or previous waveform, wrapping — the sheet editor's own colour keys, one screen over.</summary>
    public void CycleWave(int delta) =>
        PenWave = ((PenWave + delta) % SfxEditorSession.WaveCount + SfxEditorSession.WaveCount)
            % SfxEditorSession.WaveCount;

    /// <summary>The effect row's click.</summary>
    public void SelectEffect(int effect)
    {
        if (effect is < 0 or >= SfxEditorSession.EffectCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effect), effect, $"profile 8 defines effects 0-{SfxEditorSession.EffectCount - 1}.");
        }
        PenEffect = effect;
    }

    /// <summary>F: the next effect, wrapping through "none".</summary>
    public void CycleEffect(int delta) =>
        PenEffect = ((PenEffect + delta) % SfxEditorSession.EffectCount + SfxEditorSession.EffectCount)
            % SfxEditorSession.EffectCount;

    /// <summary>The pen's volume, 1-7 — clamped rather than validated, because the arrows walk into the ends every day.</summary>
    public void SetPenVolume(int volume) => PenVolume = Math.Clamp(volume, 1, SfxEditorSession.MaxVolume);

    // ---- writing notes ----

    /// <summary>
    /// The semitone a piano key means, or -1 when that key is off the bank's top. Index 0-11 is
    /// the lower row (<c>zsxdcvgbhnjm</c>) in the current octave, 12-24 the upper row
    /// (<c>q2w3er5t6y7ui</c>) an octave above it — see <see cref="PianoLowerKeys"/> for why
    /// those two strings and no others. The top of the bank is D#7, so the highest octave has
    /// four notes and the rest of its keys honestly mean nothing rather than folding back to a
    /// note the author did not press.
    /// </summary>
    public int NoteOfPianoKey(int key)
    {
        if (key is < 0 or >= PianoKeys)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key), key, $"the two piano rows hold {PianoKeys} keys.");
        }
        int note = Octave * SfxEditorLayout.OctaveRows + key;
        return note > SfxEditorSession.MaxNote ? -1 : note;
    }

    /// <summary>
    /// A piano key struck: the note lands on the cursor's step with the pen's wave, volume and
    /// effect, and the cursor moves on — TIC-80's and PICO-8's tracker behaviour both, and the
    /// half of REFERENCES-EDITORS §8 item 17 that is about the gesture rather than the letters.
    /// A key past the top of the bank writes nothing and moves nothing.
    /// </summary>
    /// <returns>True when a note was written.</returns>
    public bool PlayPianoKey(SfxEditorSession session, int key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BankReadOnly)
        {
            return false;
        }
        int note = NoteOfPianoKey(key);
        if (note < 0)
        {
            return false;
        }
        WriteNote(session, CursorStep, note);
        StepCursor(1);
        return true;
    }

    /// <summary>
    /// A cell of the pitch grid: the same note-writing verb the piano key uses, at the step the
    /// pointer is over, and the cursor follows the pointer. One owner for "what a note write
    /// means", so the two channels cannot drift — the parity law of every editor screen here.
    /// </summary>
    public void WriteNoteAt(SfxEditorSession session, int step, int semitone)
    {
        ArgumentNullException.ThrowIfNull(session);
        SetCursor(step);
        if (session.BankReadOnly)
        {
            return;
        }
        int note = Octave * SfxEditorLayout.OctaveRows + semitone;
        if (note > SfxEditorSession.MaxNote)
        {
            return;
        }
        WriteNote(session, step, note);
    }

    /// <summary>
    /// A cell of the volume grid, and the up/down arrows' verb: level 0 erases the step (the
    /// canonical rest of AUDIO-FORMAT §3), anything else becomes the pen's volume <b>and</b> the
    /// step's. Writing the pen too is the point: PICO-8's editor changes the current instrument
    /// when you set one, and an author who lowers one note and types the next expects the next
    /// to be as quiet.
    ///
    /// <para>On a step that is a rest the note has to come from somewhere, and it comes from the
    /// pen's own octave — the C the piano is standing on. That composition lives here rather than
    /// in the session, because "what the pen is holding" is a screen fact.</para>
    /// </summary>
    public void WriteVolumeAt(SfxEditorSession session, int step, int level)
    {
        ArgumentNullException.ThrowIfNull(session);
        SetCursor(step);
        if (session.BankReadOnly)
        {
            return;
        }
        if (level <= 0)
        {
            session.ClearStep(SelectedSlot, step);
            return;
        }
        SetPenVolume(level);
        int note = session.StepIsRest(SelectedSlot, step)
            ? Math.Min(SfxEditorSession.MaxNote, Octave * SfxEditorLayout.OctaveRows)
            : session.StepNote(SelectedSlot, step);
        int wave = session.StepIsRest(SelectedSlot, step) ? PenWave : session.StepWave(SelectedSlot, step);
        int effect = session.StepIsRest(SelectedSlot, step) ? PenEffect : session.StepEffect(SelectedSlot, step);
        session.SetStep(SelectedSlot, step, note, wave, PenVolume, effect);
    }

    /// <summary>
    /// The up/down arrows: the pen's volume by one, applied to the step under the cursor when
    /// that step sounds. The keyboard twin of clicking one cell up or down the volume grid, and
    /// deliberately <em>not</em> able to reach level 0 — erasing is Del's job, and an arrow that
    /// deleted a note at the bottom of its travel would be a trap.
    /// </summary>
    public void StepVolume(SfxEditorSession session, int delta)
    {
        ArgumentNullException.ThrowIfNull(session);
        SetPenVolume(PenVolume + delta);
        if (!session.BankReadOnly && !session.StepIsRest(SelectedSlot, CursorStep))
        {
            session.SetStep(
                SelectedSlot, CursorStep, session.StepNote(SelectedSlot, CursorStep),
                session.StepWave(SelectedSlot, CursorStep), PenVolume,
                session.StepEffect(SelectedSlot, CursorStep));
        }
    }

    /// <summary>
    /// Choosing a waveform or an effect also applies it to the step under the cursor when that
    /// step sounds — PICO-8's "shift-click an instrument, effect, or volume to apply", made the
    /// plain behaviour because our cursor already names exactly one step. A rest is left alone:
    /// it has no fields that can be heard, and giving it any would break the format's one
    /// spelling of silence.
    /// </summary>
    public void ApplyPenToCursor(SfxEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BankReadOnly || session.StepIsRest(SelectedSlot, CursorStep))
        {
            return;
        }
        session.SetStep(
            SelectedSlot, CursorStep, session.StepNote(SelectedSlot, CursorStep),
            PenWave, session.StepVolume(SelectedSlot, CursorStep), PenEffect);
    }

    /// <summary>Del, and the volume grid's bottom row: the step under the cursor becomes a rest.</summary>
    public void EraseCursorStep(SfxEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BankReadOnly)
        {
            return;
        }
        session.ClearStep(SelectedSlot, CursorStep);
    }

    // ---- the loop ----

    /// <summary>
    /// The <c>`</c> key and a left click on the loop row: the loop starts at this step. Pressing
    /// it on the step that already carries the start turns the loop off, which is what makes two
    /// keys enough for all four states of REFERENCES-EDITORS §8 item 18 (no loop, start, end,
    /// both) without a third key nobody would remember.
    ///
    /// <para>A loop needs an end past its start, so setting a start where there is no loop yet
    /// takes the slot's whole tail — <c>loop start..length</c> — which is the loop an author
    /// means nine times out of ten and the one PICO-8's own defaults produce.</para>
    /// </summary>
    public void ToggleLoopStart(SfxEditorSession session, int step)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BankReadOnly)
        {
            return;
        }
        int length = session.SlotLength(SelectedSlot);
        int end = session.SlotLoopEnd(SelectedSlot);
        if (step >= length)
        {
            return;             // a marker outside the played steps would be a loop nobody reaches
        }
        if (end != 0 && session.SlotLoopStart(SelectedSlot) == step)
        {
            session.ClearLoop(SelectedSlot);
            return;
        }
        int wanted = end > step ? end : length;
        session.SetLoop(SelectedSlot, step, wanted);
    }

    /// <summary>
    /// Tab and a right click on the loop row: the loop ends <b>after</b> this step — the
    /// half-open end of AUDIO-FORMAT §2, so clicking step 5 gives <c>loopEnd = 6</c> and step 5
    /// is the last one repeated. Pressing it on the step that already ends the loop turns the
    /// loop off, the mirror of <see cref="ToggleLoopStart"/>.
    /// </summary>
    public void ToggleLoopEnd(SfxEditorSession session, int step)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BankReadOnly)
        {
            return;
        }
        int length = session.SlotLength(SelectedSlot);
        int end = step + 1;
        if (end > length)
        {
            return;
        }
        if (session.SlotLoopEnd(SelectedSlot) == end)
        {
            session.ClearLoop(SelectedSlot);
            return;
        }
        int start = session.SlotLoopEnd(SelectedSlot) != 0 && session.SlotLoopStart(SelectedSlot) < end
            ? session.SlotLoopStart(SelectedSlot)
            : 0;
        session.SetLoop(SelectedSlot, start, end);
    }

    // ---- the numeric fields ----

    /// <summary>
    /// One stepper of one field, from either channel: the panel's arrows and the keys that mirror
    /// them end here, so a field cannot move by a different amount depending on which hand moved
    /// it. Speed and length are the session's; the octave is this view's.
    /// </summary>
    public void StepField(SfxEditorSession session, SfxField field, int delta)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (field != SfxField.Octave && session.BankReadOnly)
        {
            // The octave is a screen fact and stays free; speed and length are bytes of the
            // bank, and while sfx.txt owns it the steppers are honestly inert.
            return;
        }
        switch (field)
        {
            case SfxField.Speed:
                session.SetSpeed(
                    SelectedSlot,
                    Math.Clamp(
                        session.EffectiveSpeed(SelectedSlot) + delta,
                        SfxEditorSession.MinSpeed,
                        SfxEditorSession.MaxSpeed));
                break;
            case SfxField.Length:
                session.SetLength(
                    SelectedSlot,
                    Math.Clamp(session.SlotLength(SelectedSlot) + delta, 0, SfxEditorSession.StepCount));
                break;
            default:
                StepOctave(delta);
                break;
        }
    }

    // ---- playback ----

    /// <summary>Space, and the play button: start the slot from its first step. A second ask restarts it (see <see cref="PlayEpoch"/>).</summary>
    public void RequestPlay()
    {
        PlayWanted = true;
        PlayEpoch++;
    }

    /// <summary>Stop asking for sound. Idempotent — a stop with nothing playing is not an error.</summary>
    public void RequestStop()
    {
        PlayWanted = false;
        Playing = false;
    }

    /// <summary>Space's whole body, and the play button's: play what is silent, silence what is playing.</summary>
    public void TogglePlay()
    {
        if (PlayWanted)
        {
            RequestStop();
        }
        else
        {
            RequestPlay();
        }
    }

    /// <summary>
    /// The wiring's report of what the chip is doing. A slot that has run out of steps stops
    /// being wanted too, so the button goes dark by itself at the end of a one-shot sound and
    /// the next Space starts it again rather than having to stop it first.
    /// </summary>
    public void ReportPlaying(bool sounding)
    {
        Playing = sounding;
        if (!sounding)
        {
            PlayWanted = false;
        }
    }

    // ---- gestures ----

    /// <summary>The pitch grid's press: a drag opens here and every later sample writes along it.</summary>
    public void BeginPitchDrag() => PitchDragActive = true;

    /// <summary>The volume grid's press.</summary>
    public void BeginVolumeDrag() => VolumeDragActive = true;

    /// <summary>The release. Safe without an open drag — releases arrive from off the grids.</summary>
    public void EndDrags()
    {
        PitchDragActive = false;
        VolumeDragActive = false;
    }

    // ---- the exit ----

    /// <summary>
    /// Escape, or the exit tab. The exact answer table
    /// <see cref="SpriteEditorSession.RequestClose"/>, <see cref="MapEditorView.RequestClose"/>
    /// and <see cref="CodeEditorView.RequestClose"/> use: a prompt already up comes down
    /// ("stay"), a dirty bank raises it, a clean one lets the shell leave. The gesture is closed
    /// first so an Esc mid-drag judges the bank as it stands rather than half-way through one.
    /// </summary>
    /// <returns>True when the caller may leave this screen.</returns>
    public bool RequestClose(SfxEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EndStroke();
        EndDrags();
        if (ExitPromptShown)
        {
            ExitPromptShown = false;
            return false;
        }
        if (session.IsDirty)
        {
            ExitPromptShown = true;
            return false;
        }
        return true;
    }

    /// <summary>Lowers the prompt after Z or X have been executed — the mode machine's half of the verb.</summary>
    public void CloseExitPrompt() => ExitPromptShown = false;

    /// <summary>
    /// The one place a note reaches the bank. Every writer above funnels through it — the piano
    /// key, the pitch grid, and whatever the next wave adds — so "write a note" has one meaning
    /// and one undo step.
    /// </summary>
    private void WriteNote(SfxEditorSession session, int step, int note) =>
        session.SetStep(SelectedSlot, step, note, PenWave, PenVolume, PenEffect);
}

using Microsoft.Xna.Framework;
using Quarp.CartKit;
using Quarp.Core;
using static Quarp.Shell.Desktop.ConsoleChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the sound editor <b>into the console's own framebuffer</b> (wave R5, ADR-029): the top
/// band with the exit button, the tooltip field and the five editor tabs; the panel column with
/// its button row, the 64-slot selector, the waveform preview and the three stepper fields; the
/// instrument column with the pitch grid, the loop row, the volume grid and the six waveform
/// cells; the seven effect cells across the foot of the screen; the status line and the one
/// message line.
///
/// <para><b>What this file used to be.</b> Until this wave it owned a <c>GraphicsDevice</c>, an
/// a host frame painter, a font atlas and an icon atlas, and painted at the window's
/// native resolution through a <c>SpriteBatch</c>. All of that is gone. Every pixel now goes
/// through <c>Cls</c>, <c>RectFill</c>, <c>Rect</c>, <c>Print</c> and <c>Pset</c> on a
/// <see cref="ShellScreen"/> — the same calls a cartridge makes — and the result is presented by
/// the same <see cref="ConsolePresenter"/> the cartridge's frame goes through. The class is
/// static for the same reason <see cref="MapEditorRenderer"/> is: with no device resource to own
/// there is nothing to construct and nothing to dispose.</para>
///
/// <para><b>Nothing was dropped, and here is the roll call</b> (the wave's law: if a control went
/// under a key, it gets named). Pitch grid, loop row, volume grid, 64-slot selector, waveform
/// preview, six waveform cells, seven effect cells, speed, length and octave with their steppers,
/// play/stop, save, undo, redo — <b>every one of them is on screen at once</b>, in the working
/// view, with no overlay and no mode. This screen needed neither, where the map screen needed
/// both: 256 tiles at 8x8 are more pixels than the console has, and a 32-step instrument is not.
/// The three things that did change shape are named in <see cref="SfxEditorLayout"/>'s type
/// comment — the effect labels are cut to five characters, save/undo/redo moved out of the status
/// band, and the status line stopped repeating the slot's speed, length and loop because all
/// three now have permanent homes of their own on this screen.</para>
///
/// <para><b>Every name on this screen comes from the format's own owner.</b> Note names
/// (<c>C-5</c>, <c>D#6</c>), wave names (<c>p12</c> … <c>noi</c>) and effect names
/// (<c>slide</c> … <c>arp</c>) are <see cref="AudioTextCompiler"/>'s, which is what
/// <c>sfx.txt</c> is written in and what <c>quarp audio build</c> reads. A second spelling here
/// would mean an author reading one name on screen and typing another into the text source — the
/// exact drift the one-owner rule exists to prevent. Where a name is too long for its cell it is
/// <em>cut</em> by <see cref="SfxEditorLayout.FitCellLabel"/> and never rewritten, so the
/// characters that survive are the format's own. The one exception is effect 0, whose format
/// name is the single character <c>-</c>: the row prints OFF, because a lone dash in a cell reads
/// as "nothing here" rather than as "no effect".</para>
///
/// <para><b>A wave is a colour here.</b> A lit pitch cell is drawn in slot <c>6 + wave</c> —
/// teal, green, yellow, orange, red, pink for the six waveforms of AUDIO-FORMAT §3 — which is
/// PICO-8's own convention ("using the currently selected instrument, indicated by colour"). Six
/// slots of the sixteen, and no <c>Pal</c> remap anywhere on this screen, so the colours the
/// author sees are the console's real ones.</para>
///
/// <para><b>What the waveform preview is, said plainly.</b> It is a <b>schematic of the six
/// names</b> AUDIO-FORMAT §3 gives — pulse at 12.5%, 25% and 50% duty, triangle, saw, and noise
/// from a 15-bit shift register — drawn at the duty fractions that sentence itself states. It is
/// deliberately <em>not</em> a sampling of <see cref="Quarp.Core.Audio.Apu"/>: that class renders
/// a whole tick per wave in six unrolled loops with no single-sample entry point, and inventing
/// one here would be a second implementation of the chip living in a renderer. Extracting one
/// sampler in the core and having both the tick loops and this box call it is the honest fix and
/// it is a core-side change, named in this wave's report rather than smuggled into draw code.</para>
///
/// <para><b>Not the cartridge's console.</b> The framebuffer written here belongs to the shell
/// (<see cref="ShellScreen"/>); the golden master the CI compares between architectures is
/// <see cref="CartSession"/>'s, and no call in this file can reach it.</para>
///
/// <para><b>Cost, measured rather than waved away.</b> The heaviest loop on this frame is the
/// instrument's ruler — twenty-four dotted columns of sixteen <c>Pset</c>s each plus eight whole
/// lines of forty-five pixels, about 750 pixels — followed by the preview's one bar per interior
/// column and the volume grid's worst case of 32 steps x 7 levels of a 2x2 block. Against the
/// 14400 pixels the <c>Cls</c> on the same frame writes, all of it together is a twentieth of one
/// clear. This is drawing, not simulation: it happens once per rendered frame, never inside a
/// tick, and no rewind replays it.</para>
/// </summary>
public static class SfxEditorRenderer
{
    /// <summary>What the tooltip field says when no control is hovered — TIC-80's <c>Names[mode]</c>.</summary>
    public const string ScreenName = "SOUND";

    /// <summary>Full deflection of a schematic sample, in the arbitrary units <see cref="Schematic"/> answers in.</summary>
    private const int PreviewScale = 1024;

    /// <summary>The noise schematic's shift register at rest — a constant, so the box does not shimmer between frames.</summary>
    private const uint NoiseSeed = 0x7F35u;

    /// <summary>The layout this screen is drawn with; the router asks for the same one, so picture and clicks cannot disagree.</summary>
    public static SfxEditorLayout LayoutFor(ShellScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return SfxEditorLayout.Compute(screen.Width, screen.Height);
    }

    /// <summary>
    /// One frame of the sound editor. Owns the whole surface: it resets the console's drawing
    /// state and clears, so nothing another screen left behind can bend these pixels.
    /// <paramref name="view"/> is the very state the router's hit tests read, so the picture and
    /// the clicks cannot disagree; <paramref name="hover"/> and <paramref name="tooltipVisible"/>
    /// come from the shell's <see cref="IconHoverTracker"/> — the hovered control's frame lights
    /// up immediately, the text label only after the tracker's three seconds, and the label lands
    /// in the top band rather than under the pointer
    /// (<see cref="ConsoleChrome.TooltipChars"/> explains why).
    /// </summary>
    /// <returns>The layout used, so a test can assert against exactly what was drawn.</returns>
    public static SfxEditorLayout Draw(
        ShellScreen screen, SfxEditorSession session, SfxEditorView view,
        HoverTarget? hover, bool tooltipVisible, IndexFormat indexes = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        SfxEditorLayout layout = LayoutFor(screen);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);

        DrawBands(console, layout.Chrome);
        DrawPanelFrames(console, layout);
        DrawStepCursor(console, layout, view);
        DrawStaffRules(console, layout);
        DrawInstrumentRuler(console, layout);
        DrawPitchGrid(console, layout, session, view);
        DrawLoopRow(console, layout, session, view);
        DrawVolumeGrid(console, layout, session, view);
        DrawSelector(console, layout, session, view);
        DrawPreview(console, layout, view);
        DrawCellRows(console, layout, view);
        DrawFields(console, layout, session, view);
        DrawButtons(console, layout, session, view, hover);

        DrawStatusText(console, layout.Chrome, Coordinates(session, view), Summary(view, indexes));
        DrawMessageLine(
            console, layout.Chrome, view.ExitPromptShown, session.SaveError, StandingNotice(session));
        DrawTooltipField(
            console, layout.Chrome,
            tooltipVisible && hover is HoverTarget target ? TooltipText(target) : null,
            ScreenName);
        return layout;
    }

    /// <summary>
    /// The status band's left field: which step the cursor is on and what is under it, in the
    /// names <c>sfx.txt</c> uses. TIC-80 spends the same row on <c>[x=%02i y=%02i]</c>; naming
    /// the note instead of its row index is the one improvement this screen can make for free,
    /// because the note has a name and the row does not.
    ///
    /// <para><b>Re-cut for forty columns.</b> The host line began with the slot number and then
    /// repeated the slot's speed, length and loop in a right-hand field. Both are gone from here:
    /// the slot number moved to <see cref="Summary"/>, and speed, length and the loop have
    /// permanent homes on this screen — two of the three stepper fields and the loop row's own
    /// markers. This field is therefore the one fact nothing else on screen can say: the full
    /// name of what the cursor stands on, including the effect names the row below has to cut.</para>
    /// </summary>
    public static string Coordinates(SfxEditorSession session, SfxEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        int slot = view.SelectedSlot;
        int step = view.CursorStep;
        string under = session.StepIsRest(slot, step)
            ? "---"
            : $"{AudioTextCompiler.NoteName(session.StepNote(slot, step))} "
                + $"{AudioTextCompiler.WaveName(session.StepWave(slot, step))} "
                + $"{session.StepVolume(slot, step)} "
                + $"{AudioTextCompiler.EffectName(session.StepEffect(slot, step))}";
        return $"STEP {step:00}  {under}".ToUpperInvariant();
    }

    /// <summary>
    /// The status band's right field: which of the 64 sounds is open. Right-aligned to the
    /// screen's edge by <see cref="ConsoleChromeRenderer.DrawStatusText"/>, so it stops jumping
    /// when it gains a digit — the same shape the sprite screen's <c>#003</c> and the map's have.
    /// </summary>
    public static string Summary(SfxEditorView view, IndexFormat indexes = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        // The base is the shell's, not this screen's (REFERENCES-EDITORS §8 item 20): one
        // Ctrl+H anywhere spells every bank index the same way. `default` is decimal — what this
        // method returned before the switch existed, and what every caller without a shell gets.
        return indexes.Slot("SFX", view.SelectedSlot);
    }

    /// <summary>
    /// The screen's standing line, re-cut for forty columns. The read-only notice, and nothing
    /// else: it is the one fact that changes what typing does, and it has to be said before the
    /// author types rather than at save time — that is the whole reason
    /// <see cref="SfxEditorSession.BankReadOnly"/> is a public property.
    ///
    /// <para>The host screen's text ran to 76 characters against the console line's 39. Cutting
    /// it here, at the one place that knows what it says, beats truncating it at the one place
    /// that knows how wide the line is — a truncated sentence ends mid-word. This is the same
    /// trade <see cref="SpriteEditorRenderer.StandingNotice"/> and
    /// <see cref="MapEditorRenderer.StandingNotice"/> made in the two waves before this one.</para>
    /// </summary>
    public static string? StandingNotice(SfxEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        // The clipboard's refusal wins, on all four screens, for the reason it is transient:
        // it answers a key the author has just pressed, while the read-only line answers a fact
        // about the folder that will still be true on the next frame.
        return session.ClipboardNotice
            ?? (session.BankReadOnly
                ? $"READ-ONLY: {SfxEditorSession.SfxSourceFileName.ToUpperInvariant()} OWNS THIS BANK"
                : null);
    }

    /// <summary>
    /// The hover label for whichever kind of target is under the pointer, and the screen's answer
    /// to REFERENCES-EDITORS §8 item 15: a button gets <see cref="EditorIcons.SfxTooltip"/>, and
    /// every control that is <em>not</em> a button — the selector, the three grids, the two cell
    /// rows, the three fields — gets <see cref="EditorIcons.SfxRegionTooltip"/>, which is where
    /// this screen's keys are announced. The cut to the field's width belongs to
    /// <see cref="ConsoleChrome.FitTooltip"/>, the only thing that knows how wide the field is.
    ///
    /// <para><b>A target this screen does not recognise means "no label", never an exception.</b>
    /// The tracker is cleared the moment the screen changes (<see cref="IconHoverTracker.Clear"/>
    /// carries the crash this rule answers), so a foreign target should not arrive here at all —
    /// this is the second lock on the same door. It is worth having because the caller is
    /// <c>Draw</c>: an exception thrown while painting a frame reaches nothing that can recover,
    /// and the console dies with the author's unsaved work still on screen. No tooltip is worth
    /// that. Returning null is what the field already does when nothing is hovered, so the
    /// degraded picture is one the eye has seen before.</para>
    /// </summary>
    public static string? TooltipText(in HoverTarget target)
    {
        if (target.Button is EditorButton button)
        {
            return EditorIcons.SfxTooltip(button);
        }
        return target.Sfx is SfxRegion.None ? null : EditorIcons.SfxRegionTooltip(target.Sfx);
    }

    /// <summary>
    /// The borders of the two panels that can be empty — the instrument (pitch grid, loop row and
    /// volume grid together) and the 64-slot selector. They are drawn <b>second</b>, right after
    /// the three band rules and before anything else, for the reason
    /// <c>SpriteEditorRenderer.DrawPanelFrames</c> gives: every pixel they touch is either free
    /// ground or a pixel some neighbour owns, and the neighbour must win. Here the only such
    /// neighbour is the step cursor's band, whose last step reaches the instrument's right rule.
    ///
    /// <para>They exist because of the defect the owner saw on 2026-08-25: on a slot with one
    /// note in it the right half of the screen — the largest panel there is — read as a hole,
    /// because silence draws nothing and nothing is colour 0. TIC-80's <c>drawCanvas</c> rings
    /// each of its led panels before filling it (<c>sfx.c</c>, REFERENCES-EDITORS §5.1); the two
    /// rectangles are <see cref="SfxEditorLayout.InstrumentFrame"/> and
    /// <see cref="SfxEditorLayout.SlotsFrame"/>, which carry the argument about which of their
    /// sides can be drawn at all.</para>
    /// </summary>
    private static void DrawPanelFrames(VirtualConsole console, in SfxEditorLayout layout)
    {
        Outline(console, layout.InstrumentFrame, Dim);
        Outline(console, layout.SlotsFrame, Dim);
    }

    /// <summary>The band of the step under the cursor, drawn under everything so the three grids read as one column.</summary>
    private static void DrawStepCursor(VirtualConsole console, in SfxEditorLayout layout, SfxEditorView view) =>
        Fill(console, layout.StepColumnRect(view.CursorStep), Dim);

    /// <summary>
    /// <b>The instrument's ruler</b> — what makes the three grids readable when they are empty
    /// and when they are full alike. It is TIC-80's led lattice
    /// (<c>drawCanvasLeds</c>, REFERENCES-EDITORS §5.1: every cell of a panel is drawn, lit ones
    /// bright and unlit ones in the panel's dark twin) at the density our palette allows —
    /// <see cref="SfxEditorLayout.StepGapX"/> carries that argument in full, and the short of it
    /// is that the lattice lives in the one column per step that no cell can paint, so it covers
    /// nothing at any data and needs no draw-order promise to keep that true.
    ///
    /// <para><b>What it says, in three registers.</b>
    /// <list type="bullet">
    /// <item><description><b>Steps.</b> Every step boundary carries a mark, and the last step of
    /// every beat (<see cref="SfxEditorLayout.BeatSteps"/> = 4, TIC-80's <c>NOTES_PER_BEAT</c>)
    /// carries a whole line the height of the instrument instead of a dotted one. Counting to
    /// step 20 is then counting five lines, not twenty columns.</description></item>
    /// <item><description><b>Pitches.</b> A dot on the row of each of the seven <em>natural</em>
    /// semitones of the octave and none on the five sharps, which draws the piano's own pattern
    /// down the grid — the 2-3 grouping of the white keys. That is TIC-80's <c>drawPianoOctave</c>
    /// (7 white and 5 black keys per octave, §5.1) read as rows rather than as keys, and it is
    /// what lets the eye say "that note is E" instead of "that note is on the fifth row". The C
    /// row's dot is <see cref="ConsoleChromeRenderer.Text"/> rather than
    /// <see cref="ConsoleChromeRenderer.Dim"/>: it is the octave's anchor, and the octave on
    /// screen is what the OCT field is changing.</description></item>
    /// <item><description><b>Volumes.</b> A dot at the top of every one of the eight levels, so a
    /// bar's height can be read off the scale beside it rather than guessed. Without this the
    /// volume row was, in the owner's words, one solid light block: thirty-two full bars and
    /// nothing to measure them against.</description></item>
    /// </list></para>
    /// </summary>
    private static void DrawInstrumentRuler(VirtualConsole console, in SfxEditorLayout layout)
    {
        int top = layout.Pitch.Y;
        int height = layout.Volume.Bottom - top;
        for (int step = 0; step < SfxEditorLayout.StepColumns; step++)
        {
            int x = layout.StepGapX(step);
            if (SfxEditorLayout.IsBeatEnd(step))
            {
                console.RectFill(x, top, 1, height, Dim);
                continue;
            }
            for (int semitone = 0; semitone < SfxEditorLayout.OctaveRows; semitone++)
            {
                if (!IsNaturalKey(semitone))
                {
                    continue;
                }
                console.Pset(x, layout.PitchCellRect(step, semitone).Y, semitone == 0 ? Text : Dim);
            }
            console.Pset(x, layout.Loop.Y + layout.Loop.Height / 2, Dim);
            for (int level = 0; level < SfxEditorLayout.VolumeLevels; level++)
            {
                console.Pset(x, layout.VolumeCellRect(step, level).Y, Dim);
            }
        }
    }

    /// <summary>
    /// True for the seven semitones a piano gives a white key — C D E F G A B, counted from C at
    /// 0. The set is written as a twelve-bit mask rather than as a switch because it is one fact
    /// about one octave and a reader can see the whole of it at once: bits 0, 2, 4, 5, 7, 9, 11.
    /// </summary>
    private static bool IsNaturalKey(int semitone) => (0b1010_1011_0101 >> semitone & 1) != 0;

    /// <summary>
    /// The two clear rows between the three grids, drawn as dim rules. They are the instrument's
    /// staff lines: the grids share one column per step, and without a line between them a note
    /// at the bottom of the pitch grid and a loop marker under it fuse into one mark.
    /// </summary>
    private static void DrawStaffRules(VirtualConsole console, in SfxEditorLayout layout)
    {
        console.RectFill(layout.Pitch.X, layout.Pitch.Bottom, layout.Pitch.Width, 1, Dim);
        console.RectFill(layout.Loop.X, layout.Loop.Bottom, layout.Loop.Width, 1, Dim);
    }

    /// <summary>
    /// The pitch grid: a lit cell where the open slot's step holds that semitone, in the colour
    /// of the step's own waveform, and a one-pixel marker on the top or bottom row for a note
    /// that is outside the octave on screen — an editor that simply hid such a note would look
    /// like it had lost it. Steps past the slot's length are drawn dim so the played region is
    /// visible without a second widget.
    /// </summary>
    private static void DrawPitchGrid(
        VirtualConsole console, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        int slot = view.SelectedSlot;
        int length = session.SlotLength(slot);
        int bottom = view.Octave * SfxEditorLayout.OctaveRows;
        for (int step = 0; step < SfxEditorLayout.StepColumns; step++)
        {
            if (session.StepIsRest(slot, step))
            {
                continue;
            }
            int note = session.StepNote(slot, step);
            byte ink = step < length ? (byte)(WaveInk + session.StepWave(slot, step)) : Dim;
            int row = note - bottom;
            if (row is >= 0 and < SfxEditorLayout.OctaveRows)
            {
                Fill(console, Column(layout.PitchCellRect(step, row)), ink);
                continue;
            }
            // Out of the octave on screen: a one-pixel mark on the edge it went past.
            Rectangle edge = Column(
                layout.PitchCellRect(step, row < 0 ? 0 : SfxEditorLayout.OctaveRows - 1));
            console.RectFill(
                edge.X, row < 0 ? edge.Bottom - 1 : edge.Y, edge.Width, 1, Dim);
        }
    }

    /// <summary>
    /// The loop row: the played steps as a dim rail, the looped ones lit, and the two markers on
    /// the columns that carry them. This is REFERENCES-EDITORS §8 item 18's "маркеры" — the loop
    /// saying where it is, rather than two hex numbers in a corner.
    /// </summary>
    private static void DrawLoopRow(
        VirtualConsole console, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        int slot = view.SelectedSlot;
        int length = session.SlotLength(slot);
        int start = session.SlotLoopStart(slot);
        int end = session.SlotLoopEnd(slot);
        for (int step = 0; step < length; step++)
        {
            bool looped = end != 0 && step >= start && step < end;
            Fill(console, Column(layout.LoopCellRect(step)), looped ? ActiveBg : Text);
        }
        if (end == 0)
        {
            return;
        }
        MarkLoopEdge(console, layout.LoopCellRect(start), left: true);
        MarkLoopEdge(console, layout.LoopCellRect(end - 1), left: false);
    }

    /// <summary>One loop bracket: a single bright column on the side of the cell the marker belongs to.</summary>
    private static void MarkLoopEdge(VirtualConsole console, Rectangle cell, bool left) =>
        console.RectFill(left ? cell.X : cell.Right - 2, cell.Y, 1, cell.Height, Bright);

    /// <summary>
    /// The volume grid: a bar per step, growing upward from the bottom row. The bottom row is
    /// volume 0 and is therefore the rest — it is drawn as an empty cell rather than a bar, which
    /// is how the eye tells "silent" from "very quiet".
    /// </summary>
    private static void DrawVolumeGrid(
        VirtualConsole console, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        int slot = view.SelectedSlot;
        int length = session.SlotLength(slot);
        for (int step = 0; step < SfxEditorLayout.StepColumns; step++)
        {
            int volume = session.StepVolume(slot, step);
            for (int level = 1; level <= volume; level++)
            {
                Fill(console, Column(layout.VolumeCellRect(step, level)), step < length ? Text : Dim);
            }
        }
    }

    /// <summary>
    /// The 64-slot selector — TIC-80's <c>drawSelector</c> at its own 3x3 square, including its
    /// one good idea: an empty slot is drawn darker than a used one, so the bank's shape is
    /// readable at a glance without clicking through it. The open slot carries the library's blue
    /// and a bright frame, because at 3x3 a colour alone is two pixels of signal.
    ///
    /// <para><b>And two ticks on the selector's own frame, which is what actually finds it.</b>
    /// The plate and the ring were not enough: they are one palette step from the used slots
    /// around them, and the plate is the palette's dark blue, so on a full bank the open slot
    /// read to the owner's eye as "a tiny blue dot" rather than as the thing being edited. The
    /// ticks stand outside the lattice, on ground that is colour 0, and name the open slot's
    /// column and row — <see cref="SfxEditorLayout.SlotColumnTickRect"/> carries the whole
    /// argument.</para>
    /// </summary>
    private static void DrawSelector(
        VirtualConsole console, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        for (int slot = 0; slot < SfxEditorSession.SlotCount; slot++)
        {
            byte ink = slot == view.SelectedSlot ? ActiveBg
                : session.SlotIsEmpty(slot) ? Dim
                : Text;
            Rectangle cell = layout.SlotCellRect(slot);
            console.RectFill(cell.X, cell.Y, cell.Width - 1, cell.Height - 1, ink);
        }
        Rectangle chosen = layout.SlotCellRect(view.SelectedSlot);
        console.Rect(chosen.X, chosen.Y, chosen.Width, chosen.Height, Bright);
        Fill(console, layout.SlotColumnTickRect(view.SelectedSlot), Bright);
        Fill(console, layout.SlotRowTickRect(view.SelectedSlot), Bright);
    }

    /// <summary>
    /// One cycle of the pen's waveform, one bar per interior column of the box, centred on its
    /// middle line — see the type note for exactly how much this picture claims. Noise has no
    /// cycle to draw, so it is drawn as what its name says: the output of a 15-bit shift
    /// register, which looks like noise because that is what a chip's noise is.
    /// </summary>
    private static void DrawPreview(VirtualConsole console, in SfxEditorLayout layout, SfxEditorView view)
    {
        Rectangle box = layout.Preview;
        console.Rect(box.X, box.Y, box.Width, box.Height, Dim);
        int samples = Math.Max(1, box.Width - 2);
        int middle = box.Y + box.Height / 2;
        int reach = Math.Max(1, box.Height / 2 - 2);
        uint noise = NoiseSeed;
        for (int i = 0; i < samples; i++)
        {
            int sample = Schematic(view.PenWave, i, samples, ref noise);
            int y = middle - sample * reach / PreviewScale;
            console.RectFill(
                box.X + 1 + i, Math.Min(y, middle), 1, Math.Max(1, Math.Abs(y - middle)), Bright);
        }
    }

    /// <summary>
    /// One sample of the schematic, in the range -<see cref="PreviewScale"/>..<see cref="PreviewScale"/>.
    /// <paramref name="index"/> runs over one cycle of <paramref name="cycle"/> samples — the box's
    /// own interior width, so the picture fills whatever room the panel has instead of being
    /// drawn at a fixed sample count and then clipped. The duty fractions are the ones
    /// AUDIO-FORMAT §3 names in words; the shift register is the classic 15-bit one, tapped at
    /// bits 0 and 1 — the shape "noise" means on every chip of this family.
    /// </summary>
    private static int Schematic(int wave, int index, int cycle, ref uint noise)
    {
        switch (wave)
        {
            case AudioFormat.WavePulse12:
                return index * 8 < cycle ? PreviewScale : -PreviewScale;
            case AudioFormat.WavePulse25:
                return index * 4 < cycle ? PreviewScale : -PreviewScale;
            case AudioFormat.WaveTriangle:
                {
                    int quarter = Math.Max(1, cycle / 4);
                    int t = index < 2 * quarter ? index - quarter : 3 * quarter - index;
                    return t * PreviewScale / quarter;
                }
            case AudioFormat.WaveSaw:
                return index * 2 * PreviewScale / cycle - PreviewScale;
            case AudioFormat.WaveNoise:
                {
                    uint bit = (noise ^ (noise >> 1)) & 1u;
                    noise = (noise >> 1) | (bit << 14);
                    return (noise & 1u) != 0 ? PreviewScale : -PreviewScale;
                }
            default:
                return index * 2 < cycle ? PreviewScale : -PreviewScale;
        }
    }

    /// <summary>
    /// The two rows of cells — six waves under the volume grid, seven effects across the foot of
    /// the screen — each labelled with the name the text format uses and the pen's own choice
    /// lit. The label is what makes them clickable knowledge rather than a row of coloured
    /// squares, which is the whole reason the effect row is full width
    /// (<see cref="SfxEditorLayout.CellLabelChars"/>).
    /// </summary>
    private static void DrawCellRows(VirtualConsole console, in SfxEditorLayout layout, SfxEditorView view)
    {
        for (int wave = 0; wave < SfxEditorSession.WaveCount; wave++)
        {
            DrawLabelledCell(
                console, layout.WaveCellRect(wave), AudioTextCompiler.WaveName(wave).ToUpperInvariant(),
                wave == view.PenWave);
        }
        for (int effect = 0; effect < SfxEditorSession.EffectCount; effect++)
        {
            // Effect 0's format name is the single character "-", which in a cell reads as
            // "nothing here" rather than as "no effect"; every other name is the format's own.
            string name = effect == 0 ? "OFF" : AudioTextCompiler.EffectName(effect);
            DrawLabelledCell(
                console, layout.EffectCellRect(effect), name.ToUpperInvariant(),
                effect == view.PenEffect);
        }
    }

    /// <summary>
    /// The three stepper fields: a left arrow, the number, a right arrow — the mouse twin of
    /// three key pairs (Shift+Left/Right, Shift+Up/Down, and <c>[</c> <c>]</c>).
    /// </summary>
    private static void DrawFields(
        VirtualConsole console, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        DrawField(console, layout, SfxField.Speed, $"SPD {session.EffectiveSpeed(view.SelectedSlot)}");
        DrawField(console, layout, SfxField.Length, $"LEN {session.SlotLength(view.SelectedSlot)}");
        DrawField(console, layout, SfxField.Octave, $"OCT {view.Octave}");
    }

    /// <summary>
    /// One field. The two steppers are marked by a dim divider each and an arrow glyph, not by a
    /// filled plate: a seven-pixel plate at either end of a sixty-two-pixel field would leave the
    /// readout looking like the small part of the control, when it is the part being read.
    /// </summary>
    private static void DrawField(
        VirtualConsole console, in SfxEditorLayout layout, SfxField field, string text)
    {
        Rectangle rect = layout.FieldRect(field);
        console.Rect(rect.X, rect.Y, rect.Width, rect.Height, Dim);
        Rectangle minus = layout.FieldDecreaseRect(field);
        Rectangle plus = layout.FieldIncreaseRect(field);
        console.RectFill(minus.Right - 1, rect.Y, 1, rect.Height, Dim);
        console.RectFill(plus.X, rect.Y, 1, rect.Height, Dim);
        console.Print("<", ConsoleChrome.ButtonTextX(minus, "<"), ConsoleChrome.ButtonTextY(minus), Bright);
        console.Print(">", ConsoleChrome.ButtonTextX(plus, ">"), ConsoleChrome.ButtonTextY(plus), Bright);
        Rectangle label = layout.FieldTextRect(field);
        string cut = SfxEditorLayout.FitCellLabel(text, label);
        console.Print(cut, ConsoleChrome.ButtonTextX(label, cut), ConsoleChrome.ButtonTextY(label), Text);
    }

    /// <summary>
    /// One labelled cell of the wave or effect row: the chosen one on the library's blue plate
    /// with a bright frame and a bright label, the rest on ink with a dim frame. Fill and
    /// brightness carry the signal, never hue alone — the same rule the sprite screen's flag
    /// toggles follow.
    /// </summary>
    private static void DrawLabelledCell(
        VirtualConsole console, Rectangle rect, string text, bool active)
    {
        if (active)
        {
            console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, ActiveBg);
        }
        console.Rect(rect.X, rect.Y, rect.Width, rect.Height, active ? Bright : Dim);
        string cut = SfxEditorLayout.FitCellLabel(text, rect);
        console.Print(
            cut, ConsoleChrome.ButtonTextX(rect, cut), ConsoleChrome.ButtonTextY(rect),
            active ? Bright : Text);
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="ConsoleChromeRenderer.DrawButton"/>
    /// owns. The only decisions this screen makes are its own tab's highlight and the play
    /// button's two faces — a triangle while the slot is silent, a square while it sounds, the
    /// same "one identity, two faces" rule Save has carried since wave 2e.
    /// </summary>
    private static void DrawButtons(
        VirtualConsole console, in SfxEditorLayout layout, SfxEditorSession session,
        SfxEditorView view, HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            var state = new EditorButtonState(
                Active: place.Id == EditorButton.SoundTab
                    || (place.Id == EditorButton.ToolPlay && view.Playing),
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: session.IsDirty,
                CanUndo: session.CanUndo,
                CanRedo: session.CanRedo);
            EditorIcon icon = place.Id == EditorButton.ToolPlay && view.Playing
                ? EditorIcon.Stop
                : EditorIcons.IconFor(place.Id);
            DrawButton(console, place, state, icon, text: null);
        }
    }

    /// <summary>Slot of the first waveform colour; the six waves take slots 6-11. See the type note.</summary>
    private const byte WaveInk = 6;

    /// <summary>
    /// A grid cell shrunk by its right-hand column, so neighbouring steps read as columns rather
    /// than as one solid block. Horizontal only: the volume grid's levels are meant to fuse
    /// vertically — that is what makes a stack of them a bar.
    /// </summary>
    private static Rectangle Column(Rectangle cell) =>
        new(cell.X, cell.Y, Math.Max(1, cell.Width - 1), cell.Height);

    /// <summary>One filled rectangle, a layout rectangle unpacked into the console's call.</summary>
    private static void Fill(VirtualConsole console, Rectangle rect, byte color) =>
        console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, color);

    /// <summary>One outline, skipped when the rectangle came back empty — the sprite screen's own helper.</summary>
    private static void Outline(VirtualConsole console, Rectangle rect, byte color)
    {
        if (rect.Width > 0 && rect.Height > 0)
        {
            console.Rect(rect.X, rect.Y, rect.Width, rect.Height, color);
        }
    }
}

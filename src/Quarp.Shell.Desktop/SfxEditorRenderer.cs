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
/// preview's one bar per interior column — sixty of them — and the volume grid's worst case of
/// 32 steps x 7 levels of a 2x2 block. Against the 14400 pixels the <c>Cls</c> on the same frame
/// writes, that is an order below what the sprite and map screens already pay. This is drawing,
/// not simulation: it happens once per rendered frame, never inside a tick, and no rewind
/// replays it.</para>
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
        HoverTarget? hover, bool tooltipVisible)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        SfxEditorLayout layout = LayoutFor(screen);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);

        DrawBands(console, layout.Chrome);
        DrawStepCursor(console, layout, view);
        DrawStaffRules(console, layout);
        DrawPitchGrid(console, layout, session, view);
        DrawLoopRow(console, layout, session, view);
        DrawVolumeGrid(console, layout, session, view);
        DrawSelector(console, layout, session, view);
        DrawPreview(console, layout, view);
        DrawCellRows(console, layout, view);
        DrawFields(console, layout, session, view);
        DrawButtons(console, layout, session, view, hover);

        DrawStatusText(console, layout.Chrome, Coordinates(session, view), Summary(view));
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
    public static string Summary(SfxEditorView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return $"SFX {view.SelectedSlot:00}";
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

    /// <summary>The band of the step under the cursor, drawn under everything so the three grids read as one column.</summary>
    private static void DrawStepCursor(VirtualConsole console, in SfxEditorLayout layout, SfxEditorView view) =>
        Fill(console, layout.StepColumnRect(view.CursorStep), Dim);

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
}

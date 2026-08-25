using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.CartKit;
using static Quarp.Shell.Desktop.EditorChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the sound editor screen in the shell standard, applied to a chip voice: the icon-only
/// tab strip and the status bar as tinted full-width bands, the play button in the left tool
/// column, the pitch grid with the notes of the open slot on it, the loop markers under them, the
/// volume grid under those, and a right-hand panel holding the 64-slot selector, the waveform
/// preview, the six waves, the seven effects and the three stepper fields. Host UI like its three
/// siblings — window-native resolution, <see cref="Quarp.Core.Palette.Master32"/> colours, the
/// system font and the icon strip — and just as unable to touch a framebuffer or a hash: no
/// cartridge runs while this draws.
///
/// <para>Everything the four editor screens paint the same way comes from
/// <see cref="EditorChromeRenderer"/>; this class owns the instrument. All geometry comes from
/// <see cref="SfxEditorLayout"/>, the same struct <see cref="SfxEditorInput"/> hit-tests the
/// mouse against, so a note cannot be drawn in one place and clicked in another.</para>
///
/// <para><b>Every name on this screen comes from the format's own owner.</b> Note names
/// (<c>C-5</c>, <c>D#6</c>), wave names (<c>p12</c> … <c>noi</c>) and effect names
/// (<c>slide</c> … <c>arp</c>) are <see cref="AudioTextCompiler"/>'s, which is what
/// <c>sfx.txt</c> is written in and what <c>quarp audio build</c> reads. A second spelling here
/// would mean an author reading one name on screen and typing another into the text source — the
/// exact drift the one-owner rule exists to prevent.</para>
///
/// <para><b>What the waveform preview is, said plainly.</b> It is a <b>schematic of the six
/// names</b> AUDIO-FORMAT §3 gives — pulse at 12.5%, 25% and 50% duty, triangle, saw, and noise
/// from a 15-bit shift register — drawn at the duty fractions that sentence itself states. It is
/// deliberately <em>not</em> a sampling of <see cref="Quarp.Core.Audio.Apu"/>: that class renders
/// a whole tick per wave in six unrolled loops with no single-sample entry point, and inventing
/// one here would be a second implementation of the chip living in a renderer. Extracting one
/// sampler in the core and having both the tick loops and this box call it is the honest fix and
/// it is a core-side change, named in this wave's report rather than smuggled into draw code.
/// Until then this box promises the shape of a name and nothing finer; the names themselves come
/// from <see cref="AudioTextCompiler"/>, so the picture and the label cannot disagree about which
/// wave is which.</para>
/// </summary>
public sealed class SfxEditorRenderer : IDisposable
{
    /// <summary>Horizontal samples across the preview box — one cycle, drawn as that many bars.</summary>
    private const int PreviewSamples = 64;

    /// <summary>Full deflection of a schematic sample, in the arbitrary units <see cref="Schematic"/> answers in.</summary>
    private const int PreviewScale = 1024;

    /// <summary>The noise schematic's shift register at rest — a constant, so the box does not shimmer between frames.</summary>
    private const uint NoiseSeed = 0x7F35u;

    private readonly GraphicsDevice _device;
    private readonly EditorChromeRenderer _chrome;

    public SfxEditorRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _chrome = new EditorChromeRenderer(device);
    }

    /// <summary>
    /// One frame of the sound editor. Owns the whole surface (clears, begins and ends the batch)
    /// like the other three host screens. <paramref name="view"/> is the very state the router's
    /// hit tests read, so the picture and the clicks cannot disagree; <paramref name="hover"/> and
    /// <paramref name="tooltipVisible"/> come from the shell's <see cref="IconHoverTracker"/> —
    /// frame highlight now, label after its three seconds.
    /// </summary>
    public void Draw(
        SpriteBatch batch, int width, int height, SfxEditorSession session, SfxEditorView view,
        HoverTarget? hover, bool tooltipVisible)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        var layout = SfxEditorLayout.Compute(width, height);

        _device.Clear(Ink);
        batch.Begin(samplerState: SamplerState.PointClamp);

        _chrome.DrawBands(batch, layout.Chrome);

        DrawStepCursor(batch, layout, view);
        DrawPitchGrid(batch, layout, session, view);
        DrawLoopRow(batch, layout, session, view);
        DrawVolumeGrid(batch, layout, session, view);
        DrawSelector(batch, layout, session, view);
        DrawPreview(batch, layout, view);
        DrawCellRows(batch, layout, view);
        DrawFields(batch, layout, session, view);
        DrawButtons(batch, layout, session, view, hover);

        _chrome.DrawStatusText(batch, layout.Chrome, Coordinates(session, view), Summary(session, view));
        _chrome.DrawPromptLine(
            batch, layout.Chrome, view.ExitPromptShown, session.SaveError, StandingNotice(session));
        DrawTooltip(batch, layout, width, height, hover, tooltipVisible);

        batch.End();
    }

    public void Dispose() => _chrome.Dispose();

    /// <summary>
    /// The status band's left field: where the cursor is and what is under it, in the names
    /// <c>sfx.txt</c> uses. TIC-80 spends the same row on <c>[x=%02i y=%02i]</c>; naming the note
    /// instead of its row index is the one improvement this screen can make for free, because the
    /// note has a name and the row does not.
    /// </summary>
    public static string Coordinates(SfxEditorSession session, SfxEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        int step = view.CursorStep;
        string under = session.StepIsRest(view.SelectedSlot, step)
            ? "---"
            : $"{AudioTextCompiler.NoteName(session.StepNote(view.SelectedSlot, step))} "
                + $"{AudioTextCompiler.WaveName(session.StepWave(view.SelectedSlot, step))} "
                + $"{session.StepVolume(view.SelectedSlot, step)} "
                + $"{AudioTextCompiler.EffectName(session.StepEffect(view.SelectedSlot, step))}";
        return $"SFX {view.SelectedSlot:00} STEP {step:00}  {under}";
    }

    /// <summary>
    /// The status band's right field: the slot's own three numbers, in the words the text format
    /// spells them with, so an author moving between the screen and <c>sfx.txt</c> reads one
    /// vocabulary. A slot that does not loop says so rather than printing 0..0.
    /// </summary>
    public static string Summary(SfxEditorSession session, SfxEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        int slot = view.SelectedSlot;
        string loop = session.SlotLoopEnd(slot) == 0
            ? "NO LOOP"
            : $"LOOP {session.SlotLoopStart(slot)}-{session.SlotLoopEnd(slot)}";
        return $"SPD {session.EffectiveSpeed(slot)}  LEN {session.SlotLength(slot)}  {loop}";
    }

    /// <summary>
    /// The screen's standing line under the prompt and the save error: the read-only notice, and
    /// nothing else. It is the map screen's own line one bank over, and it has to be said before
    /// the author types rather than at save time — that is the whole reason
    /// <see cref="SfxEditorSession.BankReadOnly"/> is a public property.
    /// </summary>
    public static string? StandingNotice(SfxEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.BankReadOnly
            ? $"READ-ONLY: {SfxEditorSession.SfxSourceFileName.ToUpperInvariant()} OWNS THIS BANK - "
                + "REMOVE IT TO EDIT SOUND INSIDE QUARP"
            : null;
    }

    /// <summary>The band of the step under the cursor, drawn under everything so the three grids read as one column.</summary>
    private void DrawStepCursor(SpriteBatch batch, in SfxEditorLayout layout, SfxEditorView view) =>
        batch.Draw(_chrome.White, layout.StepColumnRect(view.CursorStep), StripBg);

    /// <summary>
    /// The pitch grid: one octave of rows, a lit cell where the open slot's step holds that
    /// semitone, and a dim marker in the top or bottom row for a note that is outside the octave
    /// on screen — an editor that simply hid such a note would look like it had lost it.
    /// Steps past the slot's length are drawn dim so the played region is visible without a
    /// second widget.
    /// </summary>
    private void DrawPitchGrid(
        SpriteBatch batch, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        _chrome.DrawFrame(batch, layout.Pitch, Math.Max(1, layout.Ui / 2), Dim);
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
            Color ink = step < length ? PaletteColors.Opaque(6 + session.StepWave(slot, step)) : Dim;
            int row = note - bottom;
            if (row is >= 0 and < SfxEditorLayout.OctaveRows)
            {
                batch.Draw(_chrome.White, Inset(layout.PitchCellRect(step, row)), ink);
                continue;
            }
            // Out of the octave on screen: a half-height mark on the edge it went past.
            Rectangle edge = layout.PitchCellRect(step, row < 0 ? 0 : SfxEditorLayout.OctaveRows - 1);
            batch.Draw(
                _chrome.White,
                new Rectangle(edge.X, row < 0 ? edge.Bottom - edge.Height / 3 : edge.Y, edge.Width, edge.Height / 3),
                Dim);
        }
    }

    /// <summary>
    /// The loop row: the played steps as a dim rail, the looped ones lit, and the two markers on
    /// the columns that carry them. This is REFERENCES-EDITORS §8 item 18's "маркеры" — the loop
    /// said where the loop is, rather than as two hex numbers in a corner.
    /// </summary>
    private void DrawLoopRow(
        SpriteBatch batch, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        int slot = view.SelectedSlot;
        int length = session.SlotLength(slot);
        int start = session.SlotLoopStart(slot);
        int end = session.SlotLoopEnd(slot);
        for (int step = 0; step < SfxEditorLayout.StepColumns; step++)
        {
            if (step >= length)
            {
                continue;
            }
            bool looped = end != 0 && step >= start && step < end;
            batch.Draw(_chrome.White, Inset(layout.LoopCellRect(step)), looped ? ActiveBg : StripBg);
        }
        if (end == 0)
        {
            return;
        }
        batch.Draw(_chrome.White, MarkerRect(layout.LoopCellRect(start), left: true), Bright);
        batch.Draw(_chrome.White, MarkerRect(layout.LoopCellRect(end - 1), left: false), Bright);
    }

    /// <summary>
    /// The volume grid: a bar per step, growing upward from the bottom row. The bottom row is
    /// volume 0 and is therefore the rest — it is drawn as an empty cell rather than a bar, which
    /// is how the eye tells "silent" from "very quiet".
    /// </summary>
    private void DrawVolumeGrid(
        SpriteBatch batch, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        _chrome.DrawFrame(batch, layout.Volume, Math.Max(1, layout.Ui / 2), Dim);
        int slot = view.SelectedSlot;
        int length = session.SlotLength(slot);
        for (int step = 0; step < SfxEditorLayout.StepColumns; step++)
        {
            int volume = session.StepVolume(slot, step);
            for (int level = 1; level <= volume; level++)
            {
                batch.Draw(
                    _chrome.White, Inset(layout.VolumeCellRect(step, level)), step < length ? Text : Dim);
            }
        }
    }

    /// <summary>
    /// The 64-slot selector — TIC-80's <c>drawSelector</c>, including its one good idea: an empty
    /// slot is drawn darker than a used one, so the bank's shape is readable at a glance without
    /// clicking through it. The open slot carries the library's blue.
    /// </summary>
    private void DrawSelector(
        SpriteBatch batch, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        for (int slot = 0; slot < SfxEditorSession.SlotCount; slot++)
        {
            Color ink = slot == view.SelectedSlot ? ActiveBg
                : session.SlotIsEmpty(slot) ? StripBg
                : Text;
            batch.Draw(_chrome.White, Inset(layout.SlotCellRect(slot)), ink);
        }
        _chrome.DrawFrame(batch, layout.SlotCellRect(view.SelectedSlot), Math.Max(1, layout.Ui / 2), Bright);
    }

    /// <summary>
    /// One cycle of the pen's waveform as a bar per sample, centred on the box's middle line —
    /// see the type note for exactly how much this picture claims. Noise has no cycle to draw, so
    /// it is drawn as what its name says: the output of a 15-bit shift register, which looks like
    /// noise because that is what a chip's noise is.
    /// </summary>
    private void DrawPreview(SpriteBatch batch, in SfxEditorLayout layout, SfxEditorView view)
    {
        Rectangle box = layout.Preview;
        _chrome.DrawFrame(batch, box, Math.Max(1, layout.Ui / 2), Dim);
        int columnWidth = Math.Max(1, box.Width / PreviewSamples);
        int thickness = Math.Max(1, layout.Ui / 2);
        int middle = box.Y + box.Height / 2;
        int reach = Math.Max(thickness, box.Height / 2 - thickness);
        uint noise = NoiseSeed;
        for (int i = 0; i < PreviewSamples; i++)
        {
            int sample = Schematic(view.PenWave, i, ref noise);
            int y = middle - sample * reach / PreviewScale;
            batch.Draw(
                _chrome.White,
                new Rectangle(
                    box.X + i * columnWidth, Math.Min(y, middle), columnWidth,
                    Math.Max(thickness, Math.Abs(y - middle))),
                Bright);
        }
    }

    /// <summary>
    /// One sample of the schematic, in the range -<see cref="PreviewScale"/>..<see cref="PreviewScale"/>.
    /// <paramref name="index"/> runs over one cycle. The duty fractions are the ones
    /// AUDIO-FORMAT §3 names in words; the shift register is the classic 15-bit one, tapped at
    /// bits 0 and 1 — the shape "noise" means on every chip of this family.
    /// </summary>
    private static int Schematic(int wave, int index, ref uint noise)
    {
        switch (wave)
        {
            case AudioFormat.WavePulse12:
                return index * 8 < PreviewSamples ? PreviewScale : -PreviewScale;
            case AudioFormat.WavePulse25:
                return index * 4 < PreviewSamples ? PreviewScale : -PreviewScale;
            case AudioFormat.WaveTriangle:
                {
                    int quarter = PreviewSamples / 4;
                    int t = index < 2 * quarter ? index - quarter : 3 * quarter - index;
                    return t * PreviewScale / quarter;
                }
            case AudioFormat.WaveSaw:
                return index * 2 * PreviewScale / PreviewSamples - PreviewScale;
            case AudioFormat.WaveNoise:
                {
                    uint bit = (noise ^ (noise >> 1)) & 1u;
                    noise = (noise >> 1) | (bit << 14);
                    return (noise & 1u) != 0 ? PreviewScale : -PreviewScale;
                }
            default:
                return index * 2 < PreviewSamples ? PreviewScale : -PreviewScale;
        }
    }

    /// <summary>
    /// The two rows of cells — six waves, seven effects — each labelled with the name the text
    /// format uses and the pen's own choice lit. The label is what makes them clickable knowledge
    /// rather than a row of coloured squares.
    /// </summary>
    private void DrawCellRows(SpriteBatch batch, in SfxEditorLayout layout, SfxEditorView view)
    {
        for (int wave = 0; wave < SfxEditorSession.WaveCount; wave++)
        {
            DrawLabelledCell(
                batch, layout, layout.WaveCellRect(wave), AudioTextCompiler.WaveName(wave).ToUpperInvariant(),
                wave == view.PenWave);
        }
        for (int effect = 0; effect < SfxEditorSession.EffectCount; effect++)
        {
            string name = AudioTextCompiler.EffectName(effect);
            DrawLabelledCell(
                batch, layout, layout.EffectCellRect(effect),
                (effect == 0 ? "OFF" : name).ToUpperInvariant(), effect == view.PenEffect);
        }
    }

    /// <summary>The three stepper fields: a left arrow, the number, a right arrow — the mouse twin of three key pairs.</summary>
    private void DrawFields(
        SpriteBatch batch, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view)
    {
        DrawField(batch, layout, SfxField.Speed, $"SPD {session.EffectiveSpeed(view.SelectedSlot)}");
        DrawField(batch, layout, SfxField.Length, $"LEN {session.SlotLength(view.SelectedSlot)}");
        DrawField(batch, layout, SfxField.Octave, $"OCT {view.Octave}");
    }

    private void DrawField(SpriteBatch batch, in SfxEditorLayout layout, SfxField field, string text)
    {
        Rectangle rect = layout.FieldRect(field);
        _chrome.DrawFrame(batch, rect, 1, Dim);
        batch.Draw(_chrome.White, layout.FieldDecreaseRect(field), StripBg);
        batch.Draw(_chrome.White, layout.FieldIncreaseRect(field), StripBg);
        int scale = layout.Ui;
        Rectangle minus = layout.FieldDecreaseRect(field);
        Rectangle plus = layout.FieldIncreaseRect(field);
        _chrome.Font.Draw(batch, "<", minus.X + Math.Max(1, minus.Width / 3), TextY(minus, scale), scale, Bright);
        _chrome.Font.Draw(batch, ">", plus.X + Math.Max(1, plus.Width / 3), TextY(plus, scale), scale, Bright);
        Rectangle label = layout.FieldTextRect(field);
        _chrome.Font.Draw(
            batch, text,
            label.X + Math.Max(0, (label.Width - PixelFontAtlas.MeasureWidth(text, scale)) / 2),
            TextY(label, scale), scale, Text);
    }

    private void DrawLabelledCell(
        SpriteBatch batch, in SfxEditorLayout layout, Rectangle rect, string text, bool active)
    {
        batch.Draw(_chrome.White, rect, active ? ActiveBg : StripBg);
        _chrome.DrawFrame(batch, rect, 1, Dim);
        int scale = layout.Ui;
        _chrome.Font.Draw(
            batch, text,
            rect.X + Math.Max(0, (rect.Width - PixelFontAtlas.MeasureWidth(text, scale)) / 2),
            TextY(rect, scale), scale, active ? Bright : Text);
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="EditorChromeRenderer.DrawButton"/>
    /// owns. The only decisions this screen makes are its own tab's highlight and the play
    /// button's two faces — a triangle while the slot is silent, a square while it sounds, the
    /// same "one identity, two faces" rule Save has carried since wave 2e.
    /// </summary>
    private void DrawButtons(
        SpriteBatch batch, in SfxEditorLayout layout, SfxEditorSession session, SfxEditorView view,
        HoverTarget? hover)
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
            _chrome.DrawButton(batch, layout.Chrome, place, state, icon, text: null);
        }
    }

    /// <summary>
    /// The tooltip's sound half, and the screen's answer to REFERENCES-EDITORS §8 item 15: a
    /// button gets <see cref="EditorIcons.SfxTooltip"/>, and every control that is <em>not</em> a
    /// button — the selector, the three grids, the two cell rows, the three fields — gets
    /// <see cref="EditorIcons.SfxRegionTooltip"/>, which is where this screen's keys are
    /// announced. The box itself belongs to the shared painter.
    /// </summary>
    private void DrawTooltip(
        SpriteBatch batch, in SfxEditorLayout layout, int width, int height,
        HoverTarget? hover, bool tooltipVisible)
    {
        if (hover is not HoverTarget target || !tooltipVisible)
        {
            return;
        }
        if (target.Button is EditorButton button)
        {
            _chrome.DrawTooltip(
                batch, layout.Chrome, width, height,
                EditorIcons.SfxTooltip(button), layout.ButtonRect(button));
            return;
        }
        if (target.Sfx != SfxRegion.None)
        {
            _chrome.DrawTooltip(
                batch, layout.Chrome, width, height,
                EditorIcons.SfxRegionTooltip(target.Sfx), layout.RegionRect(target.Sfx));
        }
    }

    /// <summary>A cell shrunk by one pixel a side, so neighbours read as cells rather than as one solid block.</summary>
    private static Rectangle Inset(Rectangle rect) =>
        new(rect.X, rect.Y, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1));

    /// <summary>Half a cell wide, on the side of the column the marker belongs to — the loop's two brackets.</summary>
    private static Rectangle MarkerRect(Rectangle cell, bool left)
    {
        int width = Math.Max(1, cell.Width / 4);
        return new Rectangle(left ? cell.X : cell.Right - width, cell.Y, width, cell.Height);
    }

    private static int TextY(Rectangle rect, int scale) =>
        rect.Y + Math.Max(0, (rect.Height - PixelFontAtlas.LineHeight(scale)) / 2);
}

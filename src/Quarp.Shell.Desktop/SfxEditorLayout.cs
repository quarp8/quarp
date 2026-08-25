using Microsoft.Xna.Framework;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The three numeric fields the sound screen's panel carries, each with a pair of steppers and
/// a keyboard twin. An enum rather than three hit tests, so <see cref="SfxEditorLayout"/> can
/// answer "which field, and which way" in one call and <see cref="SfxEditorInput"/> can route it
/// in one switch — the same shape <see cref="EditorPromptVerb"/> has for the prompt line.
/// </summary>
public enum SfxField
{
    /// <summary>Console ticks per step, 1-255 (AUDIO-FORMAT §2). Keyboard: Shift+Left/Right.</summary>
    Speed,

    /// <summary>How many steps the slot plays, 0-32. Keyboard: Shift+Down/Up.</summary>
    Length,

    /// <summary>Which octave the piano keys and the pitch grid are showing. Keyboard: [ and ].</summary>
    Octave,
}

/// <summary>
/// Where everything on the <b>sound</b> editor screen sits, as a pure function of the window
/// size — the fourth member of the family <see cref="SpriteEditorLayout"/>,
/// <see cref="MapEditorLayout"/> and <see cref="CodeEditorLayout"/> started, and its <b>single
/// owner</b> of geometry: <see cref="SfxEditorRenderer"/> draws these rectangles and
/// <see cref="SfxEditorInput"/> hit-tests the mouse against the very same ones, so a note can
/// never be painted in one place and clicked in another.
///
/// <para>The shared frame — the tab band, the status band, the reserved prompt line, the margins
/// and the button side — is measured by <see cref="EditorChrome"/>, exactly as the other three
/// screens measure theirs. What this file adds is the sound screen's own furniture.</para>
///
/// <para><b>The shape, and where each piece comes from.</b> Left of everything, the shell's
/// standard tool column, holding the one button this screen has a verb for: play/stop. In the
/// middle, three grids that share one column per step and therefore read as one instrument
/// 32 steps wide:</para>
/// <list type="bullet">
///   <item><see cref="Pitch"/> — 32 columns by <see cref="OctaveRows"/> rows, one octave of
///     semitones with C at the bottom. LIKO-12's <c>pitchGrid</c> (REFERENCES-EDITORS §5.2),
///     narrowed from its 7 octaves to the one the piano keys are playing, because the piano row
///     and the grid must agree about what a click means: both enter a note in the current
///     octave, which is what makes them each other's parity twin.</item>
///   <item><see cref="Loop"/> — one row of 32 cells carrying the loop markers, which is
///     REFERENCES-EDITORS §8 item 18 ("loop start/size с показом маркеров") made into a place
///     the mouse can reach. TIC-80 spends two pairs of arrows and a hex readout on the same
///     fact; a marker sitting under the step it marks says it without arithmetic.</item>
///   <item><see cref="Volume"/> — 32 columns by <see cref="VolumeLevels"/> rows, loudest at the
///     top. LIKO-12's <c>volumeGrid</c>, and the bottom row is volume 0, i.e. the rest.</item>
/// </list>
///
/// <para>Right of them, the panel: the 64-slot selector (TIC-80's <c>drawSelector</c>, laid out
/// 16 by 4 rather than its 4 groups of 4x4 because this screen's spare room is wide and shallow),
/// the waveform preview (§8 item 18's other half — "предпросмотр волны"), the six waveform cells,
/// the seven effect cells, and the three stepper fields of <see cref="SfxField"/>.</para>
///
/// <para><b>One cell size for the three grids, chosen from the height and capped by the
/// width.</b> The grids are 21 cell-rows tall together, so the cell falls out of the content
/// height; the width then only has to be checked, and what the grids do not use becomes the
/// panel. Sizing the other way round — cell from the width — is what would leave the pocket of
/// air beside a 32-column grid in a 1280-pixel window that the owner's reviews threw out of the
/// sprite screen twice.</para>
///
/// <para><b>Every scale is a whole integer floored at 1</b> (ARCHITECTURE §5 applied to host
/// UI). In a window too small for one pixel per cell the screen is clipped, not crashed — the
/// same floor, and the same carded debt (tasks/open/debt-tiny-window-layout.md), the other three
/// screens already document.</para>
/// </summary>
public readonly struct SfxEditorLayout
{
    /// <summary>Steps in a slot — the column count of all three grids. Borrowed, never re-derived.</summary>
    public const int StepColumns = SfxEditorSession.StepCount;

    /// <summary>Rows of the pitch grid: the twelve semitones of one octave, C at the bottom.</summary>
    public const int OctaveRows = 12;

    /// <summary>Rows of the volume grid: 0-7, loudest at the top; the bottom row is the rest.</summary>
    public const int VolumeLevels = SfxEditorSession.MaxVolume + 1;

    /// <summary>Columns of the slot selector — 16 by 4 covers all 64 slots in a wide, shallow box.</summary>
    public const int SlotColumns = 16;

    /// <summary>Rows of the slot selector.</summary>
    public const int SlotRows = SfxEditorSession.SlotCount / SlotColumns;

    /// <summary>Cell-rows the three grids occupy together: 12 pitch + 1 loop + 8 volume.</summary>
    private const int GridRows = OctaveRows + 1 + VolumeLevels;

    /// <summary>
    /// The status band's row, outermost first: redo, undo, save. Slot 0 — the sprite screen's
    /// Clear — stays empty here as it does on the map and the code screens, so the three shared
    /// buttons keep the pixels the author's hand already knows on every editor screen.
    /// </summary>
    private static readonly EditorButton?[] _statusSlots =
    {
        null, EditorButton.Redo, EditorButton.Undo, EditorButton.Save,
    };

    /// <summary>
    /// The left tool column, top to bottom. One entry: this screen has exactly one verb that is
    /// a button rather than a grid — play/stop the slot. Everything else the screen does has a
    /// place of its own to be clicked (a grid cell, a wave cell, a stepper), and a button with
    /// nothing behind it is the defect class the button contract closed in wave 2g.
    /// </summary>
    private static readonly EditorButton[] _toolColumn = { EditorButton.ToolPlay };

    /// <summary>The frame this screen stands in — bands, margins, button size, prompt line.</summary>
    public EditorChrome Chrome { get; private init; }

    // Forwarded, not recomputed — EditorChrome is the only place these exist.
    public int Ui => Chrome.Ui;

    public int Margin => Chrome.Margin;

    public int ButtonSize => Chrome.ButtonSize;

    public Rectangle TabStrip => Chrome.TabStrip;

    public Rectangle StatusBar => Chrome.StatusBar;

    public int PromptY => Chrome.PromptY;

    /// <summary>The ten placed buttons — six tabs, a tool column of one, three status buttons.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>Window pixels per grid cell — the number every grid hit test divides by.</summary>
    public int Cell { get; private init; }

    /// <summary>The pitch grid: 32 steps by one octave, semitone 11 on the top row.</summary>
    public Rectangle Pitch { get; private init; }

    /// <summary>The loop marker row, one cell tall, under the pitch grid and sharing its columns.</summary>
    public Rectangle Loop { get; private init; }

    /// <summary>The volume grid: 32 steps by 8 levels, volume 7 on the top row and 0 on the bottom.</summary>
    public Rectangle Volume { get; private init; }

    /// <summary>The 64-slot selector, 16 by 4.</summary>
    public Rectangle Slots { get; private init; }

    /// <summary>Window pixels per side of one slot cell.</summary>
    public int SlotCell { get; private init; }

    /// <summary>The waveform preview box — one cycle of the pen's wave, drawn full width of the panel.</summary>
    public Rectangle Preview { get; private init; }

    /// <summary>The six waveform cells, in one row.</summary>
    public Rectangle Waves { get; private init; }

    /// <summary>The seven effect cells, in one row.</summary>
    public Rectangle Effects { get; private init; }

    /// <summary>The speed field: two steppers and a readout.</summary>
    public Rectangle SpeedField { get; private init; }

    /// <summary>The length field.</summary>
    public Rectangle LengthField { get; private init; }

    /// <summary>The octave field.</summary>
    public Rectangle OctaveField { get; private init; }

    public static SfxEditorLayout Compute(int width, int height)
    {
        var buttons = new EditorButtonPlace[10];
        int placed = 0;
        EditorChrome chrome = EditorChrome.Compute(width, height, buttons, ref placed, _statusSlots);

        int ui = chrome.Ui;
        int margin = chrome.Margin;
        int button = chrome.ButtonSize;
        int top = chrome.ContentTop;
        int contentHeight = Math.Max(1, chrome.ContentBottom - top);
        int gridX = margin + button + margin;

        // The panel's floor: enough room for "OCT 5" and its two steppers at ui scale. Below it
        // the panel would be a decoration rather than a control, so the grids give way first.
        int minPanel = 24 * ui;
        int roomWidth = Math.Max(1, width - gridX - margin);
        int cellFromHeight = (contentHeight - 4 * ui) / GridRows;
        int cellFromWidth = (roomWidth - margin - minPanel) / StepColumns;
        int cell = Math.Max(1, Math.Min(cellFromHeight, cellFromWidth));

        int gridWidth = StepColumns * cell;
        var pitch = new Rectangle(gridX, top, gridWidth, OctaveRows * cell);
        var loop = new Rectangle(gridX, pitch.Bottom + 2 * ui, gridWidth, cell);
        var volume = new Rectangle(gridX, loop.Bottom + 2 * ui, gridWidth, VolumeLevels * cell);

        int panelX = gridX + gridWidth + margin;
        int panelWidth = Math.Max(1, width - margin - panelX);

        // The panel's own vertical budget, spent top-down and always inside the content box:
        // the selector takes what it needs but never more than a third of the height, the
        // preview a sixth of what is left (capped so it cannot become a poster), and the five
        // rows share the remainder. Sizing every piece against what is actually left is what
        // keeps the panel inside the window at 640x360 as well as at 2560x1440.
        int slotCell = Math.Max(1, Math.Min(panelWidth / SlotColumns, contentHeight / (3 * SlotRows)));
        var slots = new Rectangle(panelX, top, SlotColumns * slotCell, SlotRows * slotCell);

        int afterSlots = Math.Max(1, contentHeight - slots.Height - 2 * ui);
        int previewHeight = Math.Clamp(afterSlots / 6, 1, 12 * ui);
        var preview = new Rectangle(panelX, slots.Bottom + 2 * ui, panelWidth, previewHeight);

        int afterPreview = Math.Max(1, afterSlots - previewHeight - 2 * ui);
        int rowHeight = Math.Clamp((afterPreview - 4 * ui) / 5, 1, 8 * ui);
        int rowStep = rowHeight + ui;
        int rowTop = preview.Bottom + 2 * ui;
        var waves = new Rectangle(panelX, rowTop, panelWidth, rowHeight);
        var effects = new Rectangle(panelX, rowTop + rowStep, panelWidth, rowHeight);
        var speed = new Rectangle(panelX, rowTop + 2 * rowStep, panelWidth, rowHeight);
        var length = new Rectangle(panelX, rowTop + 3 * rowStep, panelWidth, rowHeight);
        var octave = new Rectangle(panelX, rowTop + 4 * rowStep, panelWidth, rowHeight);

        // The tool column grows DOWNWARD only, like the map's and the code screen's: widening it
        // would move the grids' left edge and change how much of the instrument is on screen.
        for (int i = 0; i < _toolColumn.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _toolColumn[i],
                Rect = new Rectangle(margin, top + i * (button + chrome.Gap), button, button),
            };
        }

        return new SfxEditorLayout
        {
            Chrome = chrome,
            Buttons = buttons,
            Cell = cell,
            Pitch = pitch,
            Loop = loop,
            Volume = volume,
            Slots = slots,
            SlotCell = slotCell,
            Preview = preview,
            Waves = waves,
            Effects = effects,
            SpeedField = speed,
            LengthField = length,
            OctaveField = octave,
        };
    }

    /// <summary>The placed rectangle of one button — the tooltip anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id) => EditorChrome.ButtonRect(Buttons, id);

    /// <summary>The icon's destination inside a button: centred, at scale <see cref="Ui"/>.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => Chrome.ButtonIconRect(buttonRect);

    /// <summary>Window point → button under it, stubs included (hover needs the dead ones too).</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        EditorChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="EditorChrome"/> owns the prompt line for every screen.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Window point → prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    // ---- the three grids ----

    /// <summary>
    /// Window point → (step, semitone) on the pitch grid, or false off it. The semitone is
    /// counted from the <b>bottom</b>, so it reads the way a keyboard does and the way every
    /// tracker's pitch panel does: row 0 of the grid is semitone 11.
    /// </summary>
    public bool TryPitchCell(int x, int y, out int step, out int semitone)
    {
        step = 0;
        semitone = 0;
        if (!Pitch.Contains(x, y))
        {
            return false;
        }
        step = (x - Pitch.X) / Cell;
        semitone = OctaveRows - 1 - (y - Pitch.Y) / Cell;
        return step < StepColumns && semitone >= 0;
    }

    /// <summary>
    /// Window point → nearest <em>visible</em> pitch cell, for drags: a gesture whose pointer
    /// leaves the grid keeps writing along its edge instead of tearing, exactly as
    /// <see cref="MapEditorLayout.ClampMapCell"/> does for the map canvas. Floored rather than
    /// truncated so a pointer above or left of the box keeps counting the right way — C#
    /// division rounds toward zero.
    /// </summary>
    public void ClampPitchCell(int x, int y, out int step, out int semitone)
    {
        step = Math.Clamp(FloorDiv(x - Pitch.X, Cell), 0, StepColumns - 1);
        semitone = OctaveRows - 1 - Math.Clamp(FloorDiv(y - Pitch.Y, Cell), 0, OctaveRows - 1);
    }

    /// <summary>The window rectangle of one pitch cell — the one mapping the notes and the cursor frame share.</summary>
    public Rectangle PitchCellRect(int step, int semitone) =>
        new(Pitch.X + step * Cell, Pitch.Y + (OctaveRows - 1 - semitone) * Cell, Cell, Cell);

    /// <summary>Window point → (step, volume level) on the volume grid, or false off it. Level 0 is the bottom row and means a rest.</summary>
    public bool TryVolumeCell(int x, int y, out int step, out int level)
    {
        step = 0;
        level = 0;
        if (!Volume.Contains(x, y))
        {
            return false;
        }
        step = (x - Volume.X) / Cell;
        level = VolumeLevels - 1 - (y - Volume.Y) / Cell;
        return step < StepColumns && level >= 0;
    }

    /// <summary>The window rectangle of one volume cell.</summary>
    public Rectangle VolumeCellRect(int step, int level) =>
        new(Volume.X + step * Cell, Volume.Y + (VolumeLevels - 1 - level) * Cell, Cell, Cell);

    /// <summary>Window point → step on the loop marker row, or false off it.</summary>
    public bool TryLoopCell(int x, int y, out int step)
    {
        step = 0;
        if (!Loop.Contains(x, y))
        {
            return false;
        }
        step = (x - Loop.X) / Cell;
        return step < StepColumns;
    }

    /// <summary>The window rectangle of one loop marker cell.</summary>
    public Rectangle LoopCellRect(int step) => new(Loop.X + step * Cell, Loop.Y, Cell, Loop.Height);

    /// <summary>
    /// The full-height band of one step, pitch grid through volume grid — what the step cursor
    /// is drawn as, so the eye can follow one column across three panels that mean three
    /// different things about the same step.
    /// </summary>
    public Rectangle StepColumnRect(int step) =>
        new(Pitch.X + step * Cell, Pitch.Y, Cell, Volume.Bottom - Pitch.Y);

    // ---- the panel ----

    /// <summary>Window point → slot number in the selector, or false off it.</summary>
    public bool TrySlotCell(int x, int y, out int slot)
    {
        slot = 0;
        if (!Slots.Contains(x, y))
        {
            return false;
        }
        int column = (x - Slots.X) / SlotCell;
        int row = (y - Slots.Y) / SlotCell;
        if (column >= SlotColumns || row >= SlotRows)
        {
            return false;
        }
        slot = row * SlotColumns + column;
        return true;
    }

    /// <summary>The window rectangle of one slot cell.</summary>
    public Rectangle SlotCellRect(int slot) =>
        new(Slots.X + slot % SlotColumns * SlotCell, Slots.Y + slot / SlotColumns * SlotCell, SlotCell, SlotCell);

    /// <summary>The window rectangle of one waveform cell, 0-5.</summary>
    public Rectangle WaveCellRect(int wave) => RowCellRect(Waves, wave, SfxEditorSession.WaveCount);

    /// <summary>Window point → waveform index, or false off the wave row.</summary>
    public bool TryWaveCell(int x, int y, out int wave) =>
        TryRowCell(Waves, SfxEditorSession.WaveCount, x, y, out wave);

    /// <summary>The window rectangle of one effect cell, 0-6.</summary>
    public Rectangle EffectCellRect(int effect) => RowCellRect(Effects, effect, SfxEditorSession.EffectCount);

    /// <summary>Window point → effect index, or false off the effect row.</summary>
    public bool TryEffectCell(int x, int y, out int effect) =>
        TryRowCell(Effects, SfxEditorSession.EffectCount, x, y, out effect);

    /// <summary>The rectangle of one numeric field.</summary>
    public Rectangle FieldRect(SfxField field) => field switch
    {
        SfxField.Speed => SpeedField,
        SfxField.Length => LengthField,
        _ => OctaveField,
    };

    /// <summary>The field's decrement stepper: a square at its left edge. The renderer draws a marker in it and the mouse hits exactly this.</summary>
    public Rectangle FieldDecreaseRect(SfxField field)
    {
        Rectangle rect = FieldRect(field);
        return new Rectangle(rect.X, rect.Y, rect.Height, rect.Height);
    }

    /// <summary>The field's increment stepper: the mirrored square at its right edge.</summary>
    public Rectangle FieldIncreaseRect(SfxField field)
    {
        Rectangle rect = FieldRect(field);
        return new Rectangle(rect.Right - rect.Height, rect.Y, rect.Height, rect.Height);
    }

    /// <summary>Where a field's readout is written: between its two steppers.</summary>
    public Rectangle FieldTextRect(SfxField field)
    {
        Rectangle rect = FieldRect(field);
        return new Rectangle(
            rect.X + rect.Height, rect.Y, Math.Max(0, rect.Width - 2 * rect.Height), rect.Height);
    }

    /// <summary>
    /// Window point → which field's stepper was hit and which way it points, or false. One call
    /// rather than six rectangles at the call site, so the router cannot wire a field's arrows
    /// the wrong way round in one place and rightly in another.
    /// </summary>
    public bool TryFieldStepper(int x, int y, out SfxField field, out int delta)
    {
        foreach (SfxField candidate in new[] { SfxField.Speed, SfxField.Length, SfxField.Octave })
        {
            if (FieldDecreaseRect(candidate).Contains(x, y))
            {
                field = candidate;
                delta = -1;
                return true;
            }
            if (FieldIncreaseRect(candidate).Contains(x, y))
            {
                field = candidate;
                delta = 1;
                return true;
            }
        }
        field = default;
        delta = 0;
        return false;
    }

    /// <summary>
    /// Window point → which buttonless control of this screen is under it, or
    /// <see cref="SfxRegion.None"/>. One hit test for the hover clock, so the tooltip and the
    /// click cannot disagree about what the pointer is on: the click chain below tests exactly
    /// these rectangles, in this order.
    /// </summary>
    public SfxRegion RegionAt(int x, int y)
    {
        if (Slots.Contains(x, y))
        {
            return SfxRegion.Slots;
        }
        if (Pitch.Contains(x, y))
        {
            return SfxRegion.Pitch;
        }
        if (Loop.Contains(x, y))
        {
            return SfxRegion.Loop;
        }
        if (Volume.Contains(x, y))
        {
            return SfxRegion.Volume;
        }
        if (Waves.Contains(x, y))
        {
            return SfxRegion.Waves;
        }
        if (Effects.Contains(x, y))
        {
            return SfxRegion.Effects;
        }
        if (SpeedField.Contains(x, y))
        {
            return SfxRegion.Speed;
        }
        if (LengthField.Contains(x, y))
        {
            return SfxRegion.Length;
        }
        return OctaveField.Contains(x, y) ? SfxRegion.Octave : SfxRegion.None;
    }

    /// <summary>The rectangle a region's tooltip anchors to — the same box <see cref="RegionAt"/> answered from.</summary>
    public Rectangle RegionRect(SfxRegion region) => region switch
    {
        SfxRegion.Slots => Slots,
        SfxRegion.Pitch => Pitch,
        SfxRegion.Loop => Loop,
        SfxRegion.Volume => Volume,
        SfxRegion.Waves => Waves,
        SfxRegion.Effects => Effects,
        SfxRegion.Speed => SpeedField,
        SfxRegion.Length => LengthField,
        SfxRegion.Octave => OctaveField,
        _ => Rectangle.Empty,
    };

    private static Rectangle RowCellRect(Rectangle row, int index, int count)
    {
        int cellWidth = Math.Max(1, row.Width / count);
        return new Rectangle(row.X + index * cellWidth, row.Y, cellWidth, row.Height);
    }

    private static bool TryRowCell(Rectangle row, int count, int x, int y, out int index)
    {
        index = 0;
        if (!row.Contains(x, y))
        {
            return false;
        }
        index = Math.Min(count - 1, (x - row.X) / Math.Max(1, row.Width / count));
        return true;
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}

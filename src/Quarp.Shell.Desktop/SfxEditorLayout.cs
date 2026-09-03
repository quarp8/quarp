using Microsoft.Xna.Framework;
using Quarp.Core;

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
/// Where everything on the <b>sound</b> editor screen sits, in <b>console pixels</b> — 160x90 on
/// profile 8. Wave R5 moved this screen onto the console (ADR-029) exactly as R2 moved the
/// sprite screen and R3 the map, and this struct is the whole of the geometry that moved: every
/// coordinate that used to be derived from the window size and <c>PixelFontMetrics.UiScale</c>
/// is now a fixed number on the console's own grid, the way TIC-80 writes
/// <c>drawCanvas(sfx, 88, 12, …)</c> and means column 88 of a 240-pixel screen. It stays the
/// geometry's <b>single owner</b>: <see cref="SfxEditorRenderer"/> draws these rectangles and
/// <see cref="SfxEditorInput"/> hit-tests the pointer against the same ones, so a note can never
/// be painted in one place and clicked in another.
///
/// <para>The shared frame — the top band, the three rules, the exit button, the five editor
/// tabs, the tooltip field, the message line and its clickable verbs — is measured by
/// <see cref="ConsoleChrome"/> and only forwarded here. There is no second chrome on this
/// screen.</para>
///
/// <para><b>THE ARITHMETIC, in full, because on this screen it decided the whole design.</b>
/// <see cref="ConsoleChrome"/> leaves <b>64 rows by 160 columns</b> of content (10 top band + 1
/// rule + 64 content + 3 slider + 1 rule + 5 message + 1 rule + 5 status = 90). Into those 64x160
/// this screen has to fit eleven things at once: a 32x12 pitch grid, a 32-cell loop row, a 32x8
/// volume grid, a 64-slot selector, a waveform preview, six named waveform cells, seven named
/// effect cells, three stepper fields, and the play/save/undo/redo buttons.</para>
///
/// <para><b>The width, and why a step is three pixels and not four.</b> Thirty-two steps at 4 px
/// — TIC-80's own led width — is 128 px and fits across the console with 32 px to spare. Those
/// 32 px are <b>eight characters</b> of the 4x6 system font, and eight characters cannot hold
/// anything on this screen that has a name: a labelled waveform cell needs three characters plus
/// its one-pixel frame (14 px), the speed field's readout is "SPD 255" (28 px), and a 64-slot
/// selector at TIC-80's own 3x3 square is 48 px wide. At <b>3 px per step</b> the instrument is
/// 96 px and the column beside it is <b>62 px — fifteen characters</b>, which is what lets every
/// one of those controls keep a permanent home instead of going under a key. Ninety-six plus two
/// of gutter plus sixty-two is one hundred and sixty.</para>
///
/// <para><b>The height, twice, because the screen is two columns and a full-width row.</b>
/// <list type="bullet">
/// <item><description><b>The panel</b> (x 0..61): 10 rows of button row + 1 + 12 of selector
/// (4 rows of 3 px) + 1 + 8 of preview + 1 + 7 + 1 + 7 + 1 + 7 for the three stepper fields = 56,
/// which lands its last row on 66.</description></item>
/// <item><description><b>The instrument</b> (x 64..159): 24 rows of pitch (12 semitones at
/// <see cref="PitchRowHeight"/> = 2, TIC-80's own led height) + 1 + 3 of loop row + 1 + 16 of
/// volume (8 levels at 2) + 1 + 7 of waveform cells = 53, landing on 63.</description></item>
/// <item><description><b>The effect row</b> spans the whole width on the last seven rows
/// (68..74). It is full width and not in either column because it is the one control whose
/// labels the format spells out to seven characters ("fadeout"): at 160 px its seven cells are
/// 22 px each, which holds five characters — enough to keep FADEIN and FADEOUT apart, and one
/// character more than a 96-px row could give them.</description></item>
/// </list></para>
///
/// <para><b>What the move cost, named rather than hidden.</b> (1) A step is three console pixels
/// wide, so the pitch grid is 96 px rather than the host screen's several hundred. (2) An effect
/// label is cut to what its cell holds (<see cref="CellLabelChars"/>) — FADEIN and FADEOUT read
/// as FADEI and FADEO; the whole name is in the status line whenever the cursor stands on a step
/// that carries it. (3) Save, undo and redo left the status band for the panel's button row, for
/// the reason the sprite and map screens' did: the console's status line is five pixels tall and
/// an icon-button is ten. (4) The tooltip is printed in the top band instead of popping under the
/// pointer, and is cut to 22 characters — see <see cref="ConsoleChrome.TooltipChars"/>. (5) The
/// message band is one line, so a standing notice yields to the exit prompt — see
/// <see cref="ConsoleChromeRenderer.DrawMessageLine"/>. (6) The status line no longer repeats the
/// slot's speed, length and loop: they have permanent homes on this screen now (two stepper
/// fields and the loop row's own markers), and the line carries the cursor's step and the slot
/// number instead. <b>Nothing went under a key or into a mode</b>: every control the host screen
/// had is on screen at once, which is why this screen has no overlay and no Tab mode where the
/// map screen needed both.</para>
///
/// <para><b>The three rows the frame reserves for a slider stay empty here, on purpose.</b>
/// <see cref="ConsoleChrome.SliderY"/> is where the sprite screen's sheet slider and the map's
/// position bar live. This screen has nothing to scroll — all 32 steps and all 64 slots are on
/// screen — so it draws no slider rather than inventing a control to fill the band. The band is
/// the frame's, not this screen's, and a screen that reached into it would be measuring the
/// chrome's private arithmetic.</para>
///
/// <para><b>Every scale is one.</b> There is no fractional scale on this screen and no path that
/// can produce one (ARCHITECTURE §5); the window's only say is the whole-integer factor
/// <see cref="FramePlacement"/> presents the finished frame at.</para>
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

    /// <summary>Console pixels per step across all three grids — see the type comment for why three and not four.</summary>
    public const int StepWidth = 3;

    /// <summary>
    /// Steps in one beat. Four, which is TIC-80's own <c>NOTES_PER_BEAT</c> — the value its music
    /// editor scrolls by and highlights beats with (REFERENCES-EDITORS §6.1) — and half of the
    /// eight-note "SFX quarter" PICO-8 names for this very editor (§5.3). It is what the
    /// instrument's ruler counts in, so an author can find step 20 without counting to twenty.
    /// </summary>
    public const int BeatSteps = 4;

    /// <summary>Console pixels per semitone row. Two, which is exactly TIC-80's led cell height (REFERENCES-EDITORS §5.1).</summary>
    public const int PitchRowHeight = 2;

    /// <summary>Console pixels per volume level — the same two, so the two grids read as one instrument.</summary>
    public const int VolumeRowHeight = 2;

    /// <summary>Height of the loop marker row: three, one more than a grid row, so its brackets are visible.</summary>
    public const int LoopRowHeight = 3;

    /// <summary>Side of one slot cell in the selector — TIC-80's own 3x3 square (<c>drawSelector</c>).</summary>
    public const int SlotCellSize = 3;

    /// <summary>Height of the waveform preview box, frame included.</summary>
    public const int PreviewHeight = 8;

    /// <summary>Height of a row of labelled cells or of one stepper field: five pixels of glyph plus a frame pixel each side.</summary>
    public const int CellRowHeight = SystemFont.GlyphHeight + 2;

    /// <summary>Top-to-top distance between the three stepper fields.</summary>
    public const int FieldPitch = CellRowHeight + 1;

    /// <summary>Clear pixels between the panel column and the instrument — the only air on this screen.</summary>
    public const int Gutter = 2;

    /// <summary>
    /// The panel's button row, left to right: play/stop, then the three the host frame kept in
    /// its status bar. Those three moved for the reason the sprite screen's and the map's did:
    /// the console's status line is five pixels tall and an icon-button is ten, and a band that
    /// cannot hold a button cannot hold a button row.
    /// </summary>
    private static readonly EditorButton[] _toolRow =
    {
        EditorButton.ToolPlay, EditorButton.Save, EditorButton.Undo, EditorButton.Redo,
    };

    /// <summary>The frame this screen stands in. See <see cref="ConsoleChrome"/>.</summary>
    public ConsoleChrome Chrome { get; private init; }

    // Forwarded, not recomputed — ConsoleChrome is the only place these exist.

    /// <summary>Screen width in console pixels.</summary>
    public int ScreenWidth => Chrome.ScreenWidth;

    /// <summary>Screen height in console pixels.</summary>
    public int ScreenHeight => Chrome.ScreenHeight;

    /// <summary>Side of every icon-button — ten console pixels, an 8x8 mask plus its frame.</summary>
    public int ButtonSize => ConsoleChrome.ButtonSize;

    /// <summary>Screen-edge inset for text — one pixel, because forty columns is the whole line.</summary>
    public int Margin => ConsoleChrome.Margin;

    /// <summary>The top band that carries the exit button, the tooltip field and the five editor tabs.</summary>
    public Rectangle TabStrip => Chrome.TopBar;

    /// <summary>The status band: the cursor's step at the left, the slot number at the right.</summary>
    public Rectangle StatusBar => Chrome.StatusBar;

    /// <summary>Glyph top of the single message line — the exit prompt, the save error or the standing notice.</summary>
    public int PromptY => Chrome.MessageY;

    /// <summary>The ten placed buttons — the frame's six and the panel's four.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The pitch grid: 32 steps by one octave, semitone 11 on the top row.</summary>
    public Rectangle Pitch { get; private init; }

    /// <summary>
    /// The ring around the whole instrument — pitch grid, loop row and volume grid, which share
    /// one column per step and are read as one panel.
    ///
    /// <para><b>Why it exists at all.</b> TIC-80 rings every led panel of its sound editor before
    /// filling it: <c>drawCanvas</c> draws the border at <c>x - 1, y - 1, w + 2, h + 2</c> and
    /// only then calls <c>drawCanvasLeds</c> (<c>src/studio/editors/sfx.c</c>,
    /// REFERENCES-EDITORS §5.1). Ours had no border, and an empty slot draws nothing, and the
    /// screen's ground is colour 0 — so the largest panel on the screen read as a hole in the
    /// interface. That is the same defect the sprite screen's canvas had on 2026-08-25, and
    /// <see cref="SpriteEditorLayout.CanvasFrame"/> is the same rectangle answering it.</para>
    ///
    /// <para><b>Three sides stand outside the panel and the fourth stands inside it, on purpose.</b>
    /// Left, top and bottom are a pixel clear of the grids. The right side would want column 160,
    /// which does not exist — the instrument runs flush to the screen's edge — so it stands on
    /// <see cref="StepGapX"/> of the last step, the one column no cell of any of the three grids
    /// can paint. The alternative was moving the whole instrument a pixel left, which would shift
    /// every rectangle on the right half of the screen to buy a column that is already free. The
    /// sprite screen had to make the other choice for its sheet window
    /// (<see cref="SpriteEditorLayout.SheetFrame"/> leaves the fourth side off the screen)
    /// because there no such column existed.</para>
    /// </summary>
    public Rectangle InstrumentFrame =>
        new(Pitch.X - 1, Pitch.Y - 1, Pitch.Width + 1, Volume.Bottom - Pitch.Y + 2);

    /// <summary>The loop marker row, under the pitch grid and sharing its columns.</summary>
    public Rectangle Loop { get; private init; }

    /// <summary>The volume grid: 32 steps by 8 levels, volume 7 on the top row and 0 on the bottom.</summary>
    public Rectangle Volume { get; private init; }

    /// <summary>The 64-slot selector, 16 by 4.</summary>
    public Rectangle Slots { get; private init; }

    /// <summary>
    /// The ring around the 64-slot selector. TIC-80 draws its selector inside a panel of its own
    /// (<c>drawSelectorPanel</c>, §5.1) and ours stood on bare ground; the ring is also what the
    /// open slot's two bright ticks hang off, which is how this screen answers "which of the
    /// sixty-four squares am I editing" at a glance — see <see cref="SlotColumnTickRect"/>.
    ///
    /// <para>Its left side is the screen's own edge and is therefore not drawn: the selector
    /// starts at column 0. That is the same honest three-sided answer
    /// <see cref="SpriteEditorLayout.SheetFrame"/> gives, and the rectangle is written whole
    /// rather than clipped here because <c>VirtualConsole</c> clips it for free.</para>
    /// </summary>
    public Rectangle SlotsFrame =>
        new(Slots.X - 1, Slots.Y - 1, Slots.Width + 2, Slots.Height + 2);

    /// <summary>The waveform preview box — one cycle of the pen's wave, full width of the panel.</summary>
    public Rectangle Preview { get; private init; }

    /// <summary>The six waveform cells, in one row under the volume grid.</summary>
    public Rectangle Waves { get; private init; }

    /// <summary>The seven effect cells, in one row across the whole screen — see the type comment for why full width.</summary>
    public Rectangle Effects { get; private init; }

    /// <summary>The speed field: two steppers and a readout.</summary>
    public Rectangle SpeedField { get; private init; }

    /// <summary>The length field.</summary>
    public Rectangle LengthField { get; private init; }

    /// <summary>The octave field.</summary>
    public Rectangle OctaveField { get; private init; }

    /// <summary>
    /// The screen's geometry for a console of the given size. The two numbers are <b>console</b>
    /// pixels — 160x90 on profile 8 — and never a window size: since this wave the window's only
    /// say in this screen is the whole-integer scale <see cref="FramePlacement"/> presents it at.
    /// </summary>
    public static SfxEditorLayout Compute(int screenWidth, int screenHeight)
    {
        var buttons = new EditorButtonPlace[11];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(screenWidth, screenHeight, buttons, ref placed);

        int button = ConsoleChrome.ButtonSize;
        int top = chrome.ContentTop;

        // The instrument is sized first because its width is fixed by the format (32 steps) and
        // the panel takes what is left. Sizing the panel first and giving the rest to the grids
        // is what would put a fractional step width on screen.
        int gridWidth = StepColumns * StepWidth;
        int panelWidth = Math.Max(1, screenWidth - gridWidth - Gutter);
        int gridX = panelWidth + Gutter;

        // The panel's button row, hard against the left edge and growing rightward: it is one
        // button tall, so the selector under it starts as high as the instrument's second band.
        for (int i = 0; i < _toolRow.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _toolRow[i],
                Rect = new Rectangle(i * button, top, button, button),
            };
        }

        var slots = new Rectangle(
            0, top + button + 1, SlotColumns * SlotCellSize, SlotRows * SlotCellSize);
        var preview = new Rectangle(0, slots.Bottom + 1, panelWidth, PreviewHeight);
        int fieldTop = preview.Bottom + 1;
        var speed = new Rectangle(0, fieldTop, panelWidth, CellRowHeight);
        var length = new Rectangle(0, fieldTop + FieldPitch, panelWidth, CellRowHeight);
        var octave = new Rectangle(0, fieldTop + 2 * FieldPitch, panelWidth, CellRowHeight);

        var pitch = new Rectangle(gridX, top, gridWidth, OctaveRows * PitchRowHeight);
        var loop = new Rectangle(gridX, pitch.Bottom + 1, gridWidth, LoopRowHeight);
        var volume = new Rectangle(
            gridX, loop.Bottom + 1, gridWidth, VolumeLevels * VolumeRowHeight);
        var waves = new Rectangle(gridX, volume.Bottom + 1, gridWidth, CellRowHeight);

        // The effect row: the screen's last seven rows, whole cells, centred on what is left
        // over. Measured UP from the frame's content bottom rather than written down, so it
        // cannot mean the wrong row on a console that is not 90 pixels tall.
        int effectCell = Math.Max(1, screenWidth / SfxEditorSession.EffectCount);
        int effectWidth = effectCell * SfxEditorSession.EffectCount;
        var effects = new Rectangle(
            (screenWidth - effectWidth) / 2, chrome.ContentBottom - CellRowHeight,
            effectWidth, CellRowHeight);

        return new SfxEditorLayout
        {
            Chrome = chrome,
            Buttons = buttons,
            Pitch = pitch,
            Loop = loop,
            Volume = volume,
            Slots = slots,
            Preview = preview,
            Waves = waves,
            Effects = effects,
            SpeedField = speed,
            LengthField = length,
            OctaveField = octave,
        };
    }

    /// <summary>The placed rectangle of one button — the hover frame anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id) => ConsoleChrome.ButtonRect(Buttons, id);

    /// <summary>The 8x8 mask's destination inside a button.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => ConsoleChrome.ButtonIconRect(buttonRect);

    /// <summary>Console point to the button under it, stubs included (hover needs the dead ones too).</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        ConsoleChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="ConsoleChrome"/> owns the message line.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Console point to a prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    // ---- the three grids ----

    /// <summary>
    /// Console point to (step, semitone) on the pitch grid, or false off it. The semitone is
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
        step = (x - Pitch.X) / StepWidth;
        semitone = OctaveRows - 1 - (y - Pitch.Y) / PitchRowHeight;
        return step < StepColumns && semitone >= 0;
    }

    /// <summary>
    /// Console point to nearest <em>visible</em> pitch cell, for drags: a gesture whose pointer
    /// leaves the grid keeps writing along its edge instead of tearing, exactly as
    /// <see cref="MapEditorLayout.ClampMapCell"/> does for the map canvas. Floored rather than
    /// truncated so a pointer above or left of the box keeps counting the right way — C#
    /// division rounds toward zero.
    /// </summary>
    public void ClampPitchCell(int x, int y, out int step, out int semitone)
    {
        step = Math.Clamp(FloorDiv(x - Pitch.X, StepWidth), 0, StepColumns - 1);
        semitone = OctaveRows - 1
            - Math.Clamp(FloorDiv(y - Pitch.Y, PitchRowHeight), 0, OctaveRows - 1);
    }

    /// <summary>The console rectangle of one pitch cell — the one mapping the notes and the cursor share.</summary>
    public Rectangle PitchCellRect(int step, int semitone) =>
        new(Pitch.X + step * StepWidth,
            Pitch.Y + (OctaveRows - 1 - semitone) * PitchRowHeight,
            StepWidth,
            PitchRowHeight);

    /// <summary>Console point to (step, volume level) on the volume grid, or false off it. Level 0 is the bottom row and means a rest.</summary>
    public bool TryVolumeCell(int x, int y, out int step, out int level)
    {
        step = 0;
        level = 0;
        if (!Volume.Contains(x, y))
        {
            return false;
        }
        step = (x - Volume.X) / StepWidth;
        level = VolumeLevels - 1 - (y - Volume.Y) / VolumeRowHeight;
        return step < StepColumns && level >= 0;
    }

    /// <summary>The console rectangle of one volume cell.</summary>
    public Rectangle VolumeCellRect(int step, int level) =>
        new(Volume.X + step * StepWidth,
            Volume.Y + (VolumeLevels - 1 - level) * VolumeRowHeight,
            StepWidth,
            VolumeRowHeight);

    /// <summary>Console point to a step on the loop marker row, or false off it.</summary>
    public bool TryLoopCell(int x, int y, out int step)
    {
        step = 0;
        if (!Loop.Contains(x, y))
        {
            return false;
        }
        step = (x - Loop.X) / StepWidth;
        return step < StepColumns;
    }

    /// <summary>The console rectangle of one loop marker cell.</summary>
    public Rectangle LoopCellRect(int step) =>
        new(Loop.X + step * StepWidth, Loop.Y, StepWidth, Loop.Height);

    /// <summary>
    /// The full-height band of one step, pitch grid through volume grid — what the step cursor
    /// is drawn as, so the eye can follow one column across three panels that mean three
    /// different things about the same step.
    /// </summary>
    public Rectangle StepColumnRect(int step) =>
        new(Pitch.X + step * StepWidth, Pitch.Y, StepWidth, Volume.Bottom - Pitch.Y);

    /// <summary>
    /// The one console column of a step that no grid cell can ever paint, and the column the
    /// instrument's ruler lives in.
    ///
    /// <para>Every cell of all three grids is drawn a pixel narrower than <see cref="StepWidth"/>
    /// so that neighbouring steps read as columns rather than as one slab
    /// (<c>SfxEditorRenderer.Column</c>). That reserved pixel is this one, and it is reserved in
    /// the pitch grid, the loop row and the volume grid alike — which is exactly what a ruler
    /// needs: markup that cannot cover content, at any data, without depending on draw order.
    /// TIC-80 gets the same effect the other way round, by drawing <em>every</em> led of a panel
    /// and colouring the unlit ones in the panel's dark twin (<c>drawCanvasLeds</c>, §5.1). We
    /// cannot: the console's sixteen slots hold one grey (slot 1) and no dark twin per hue, so a
    /// full lattice of unlit cells would be a slab of grey with the notes barely standing above
    /// it. <b>The divergence is therefore density, not idea</b>: the same lattice, drawn one pixel
    /// per cell in the column that is free anyway.</para>
    /// </summary>
    public int StepGapX(int step) => Pitch.X + step * StepWidth + StepWidth - 1;

    /// <summary>True at the last step of a beat — where the ruler draws a whole line instead of a dotted one.</summary>
    public static bool IsBeatEnd(int step) => step % BeatSteps == BeatSteps - 1;

    // ---- the panel ----

    /// <summary>Console point to a slot number in the selector, or false off it.</summary>
    public bool TrySlotCell(int x, int y, out int slot)
    {
        slot = 0;
        if (!Slots.Contains(x, y))
        {
            return false;
        }
        int column = (x - Slots.X) / SlotCellSize;
        int row = (y - Slots.Y) / SlotCellSize;
        if (column >= SlotColumns || row >= SlotRows)
        {
            return false;
        }
        slot = row * SlotColumns + column;
        return true;
    }

    /// <summary>The console rectangle of one slot cell.</summary>
    public Rectangle SlotCellRect(int slot) =>
        new(Slots.X + slot % SlotColumns * SlotCellSize,
            Slots.Y + slot / SlotColumns * SlotCellSize,
            SlotCellSize,
            SlotCellSize);

    /// <summary>
    /// The open slot's tick on the selector's top rule — the column it stands in, said outside
    /// the lattice.
    ///
    /// <para><b>Why a tick and not a brighter square.</b> The cell itself already wears the
    /// library's blue under a bright ring, and that was not enough to find: at 3x3 a used slot is
    /// light grey (slot 2) and the ring is white (slot 3), two neighbours in the palette, while
    /// the blue plate (slot 4) is the palette's <em>dark</em> blue and so reads as a hole rather
    /// than as a highlight — the owner's eye reported exactly that, "a tiny blue dot". Nothing
    /// inside a sixteen-by-four field of 3x3 squares can be made to stand out by hue or by one
    /// step of brightness. A mark <b>outside</b> the field can: the frame's ground is colour 0,
    /// so a white tick on it is maximum contrast, and two of them — a column and a row — name one
    /// square of the sixty-four between them. TIC-80 hangs marks off a panel's frame for the same
    /// reason (the sheet's neighbouring-page strokes, §2.1).</para>
    /// </summary>
    public Rectangle SlotColumnTickRect(int slot) =>
        new(SlotCellRect(slot).X, SlotsFrame.Y, SlotCellSize, 1);

    /// <summary>The open slot's tick on the selector's right rule — the row it stands in. See <see cref="SlotColumnTickRect"/>.</summary>
    public Rectangle SlotRowTickRect(int slot) =>
        new(SlotsFrame.Right - 1, SlotCellRect(slot).Y, 1, SlotCellSize);

    /// <summary>The console rectangle of one waveform cell, 0-5.</summary>
    public Rectangle WaveCellRect(int wave) => RowCellRect(Waves, wave, SfxEditorSession.WaveCount);

    /// <summary>Console point to a waveform index, or false off the wave row.</summary>
    public bool TryWaveCell(int x, int y, out int wave) =>
        TryRowCell(Waves, SfxEditorSession.WaveCount, x, y, out wave);

    /// <summary>The console rectangle of one effect cell, 0-6.</summary>
    public Rectangle EffectCellRect(int effect) =>
        RowCellRect(Effects, effect, SfxEditorSession.EffectCount);

    /// <summary>Console point to an effect index, or false off the effect row.</summary>
    public bool TryEffectCell(int x, int y, out int effect) =>
        TryRowCell(Effects, SfxEditorSession.EffectCount, x, y, out effect);

    /// <summary>
    /// How many characters of label one cell of a labelled row holds, once its one-pixel frame
    /// has taken a column at each side. <b>The single owner of the cut</b>, in the one place that
    /// knows how wide a cell is — the same discipline <see cref="ConsoleChrome.FitTooltip"/>
    /// carries for the hover label. It is why the effect row is full width: at 22 px a cell holds
    /// five characters and FADEIN and FADEOUT stay distinguishable, where a 96-px row's 13 px
    /// would hold three and collapse them both to FAD.
    /// </summary>
    public static int CellLabelChars(Rectangle cell) =>
        Math.Max(0, (cell.Width - 2) / SystemFont.CellWidth);

    /// <summary>Cuts a cell label to what that cell holds.</summary>
    public static string FitCellLabel(string text, Rectangle cell)
    {
        ArgumentNullException.ThrowIfNull(text);
        int chars = CellLabelChars(cell);
        return text.Length <= chars ? text : text[..chars];
    }

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
    /// Console point to which field's stepper was hit and which way it points, or false. One call
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
    /// Console point to which buttonless control of this screen is under it, or
    /// <see cref="SfxRegion.None"/>. One hit test for the hover clock, so the tooltip and the
    /// click cannot disagree about what the pointer is on: the click chain in
    /// <see cref="SfxEditorInput"/> tests exactly these rectangles, in this order.
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

    /// <summary>The rectangle a region's tooltip names — the same box <see cref="RegionAt"/> answered from.</summary>
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

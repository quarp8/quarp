namespace Quarp.Shell.Desktop;

/// <summary>
/// The <b>sound</b> screen's controls that are not buttons, as one name each — the slot
/// selector, the three grids, the two rows of cells and the three stepper fields. It exists for
/// the reason <see cref="HoverTarget.Slider"/> and <see cref="HoverTarget.Flag"/> exist one
/// screen over: a control with no button has nowhere to hang its hotkey, and
/// REFERENCES-EDITORS §8 item 15 makes a dense 160x90 UI learnable through exactly these labels.
/// One field instead of nine, because "which region is under the pointer" is one fact.
/// </summary>
public enum SfxRegion
{
    /// <summary>The pointer is over none of them.</summary>
    None,

    /// <summary>The 64-slot selector.</summary>
    Slots,

    /// <summary>The pitch grid — where the piano rows write.</summary>
    Pitch,

    /// <summary>The loop marker row.</summary>
    Loop,

    /// <summary>The volume grid.</summary>
    Volume,

    /// <summary>The six waveform cells.</summary>
    Waves,

    /// <summary>The seven effect cells.</summary>
    Effects,

    /// <summary>The speed field and its two steppers.</summary>
    Speed,

    /// <summary>The length field.</summary>
    Length,

    /// <summary>The octave field.</summary>
    Octave,
}

/// <summary>
/// What the mouse is hovering in the sprite editor: an icon-button, a palette swatch, a
/// variant button inside an open group flyout (since wave 2e — the 3-second tooltip contract
/// extends to variants by the order), the sheet scroll slider (wave 2h — the slider has
/// no button, and its tooltip is where the wheel and the [ ] keys are announced), or one of
/// the eight flag toggles (wave 3b-2 — buttonless cells like the swatches, and their tooltip
/// is where Shift+1..8 is announced). A record
/// struct so two frames over the same target compare equal by value — the hover clock below
/// hangs on that comparison; each factory fills every field so targets of different kinds
/// can never collide by accident.
/// </summary>
/// <summary>
/// The buttonless controls of the <b>music</b> screen — its twin of <see cref="SfxRegion"/>, and
/// it exists for the same reason: those controls need a hover label of their own, and a screen's
/// keys are announced on the thing they act on. Four, where the sound screen has nine, because
/// this screen is one grid and its three attendants.
/// </summary>
public enum MusicRegion
{
    /// <summary>Not on any of this screen's buttonless controls.</summary>
    None,

    /// <summary>The tracker grid: 64 patterns x 4 channels, ten rows of it at a time.</summary>
    Song,

    /// <summary>The three section markers of a pattern — loop start, loop end, stop.</summary>
    Flags,

    /// <summary>The channel header, where mute and solo live.</summary>
    Channels,

    /// <summary>The whole-song overview down the right edge — every pattern at once, and the scroll control.</summary>
    Overview,
}

/// <summary>
/// The buttonless controls of the <b>map</b> screen — its twin of <see cref="SfxRegion"/> and
/// <see cref="MusicRegion"/>, and it exists for the same reason (REFERENCES-EDITORS §8 item 15):
/// a control with no button has nowhere to hang its hotkey, and on 160x90 the label on the
/// control IS the documentation. Four, because this screen is one viewport and its three
/// attendants — and every one of them carries a gesture that is announced nowhere else
/// (Shift for the palette, Tab for the whole map, Ctrl+Shift+arrows for the block, the middle
/// button's eyedropper, Space+drag, the grid key).
/// </summary>
public enum MapRegion
{
    /// <summary>Not on any of this screen's buttonless controls.</summary>
    None,

    /// <summary>The map viewport — the 17x8 window onto the 256x72 grid the pencil paints on.</summary>
    Canvas,

    /// <summary>The tile palette overlay: one <see cref="SheetStrip"/> lane of tiles, up while Shift is held or the button is latched.</summary>
    Tiles,

    /// <summary>The whole-map view: 256x72 cells at two to the pixel, up in the Tab mode.</summary>
    Minimap,

    /// <summary>The horizontal position bar under the viewport.</summary>
    Slider,
}

/// <summary>
/// The buttonless controls of the <b>code</b> screen — the fourth twin of
/// <see cref="SfxRegion"/>, <see cref="MusicRegion"/> and <see cref="MapRegion"/>, and it exists
/// for the same reason (REFERENCES-EDITORS §8 item 15): a control with no button has nowhere to
/// hang its hotkey, and on 160x90 the label on the control IS the documentation. Two, because
/// this screen is a page of text and the one rail that travels it — and between them they carry
/// every key this editor owns that no button of the tool column names (the arrows and their
/// three modifiers, Tab, Ctrl+A, the wheel, F11).
/// </summary>
public enum CodeRegion
{
    /// <summary>Not on any of this screen's buttonless controls.</summary>
    None,

    /// <summary>The text field — the 11x36 page the caret lives on (15x40 with the chrome off).</summary>
    Text,

    /// <summary>The vertical scrollbar at the right edge: the mouse's only long-distance road through a file.</summary>
    ScrollBar,
}

public readonly record struct HoverTarget
{
    /// <summary>The hovered icon-button, or null when the target is a swatch, a flyout variant or the slider.</summary>
    public EditorButton? Button { get; init; }

    /// <summary>The hovered palette swatch 0-15, or -1 otherwise.</summary>
    public int Swatch { get; init; }

    /// <summary>The open flyout's slot when the target is one of its variants, or null.</summary>
    public EditorButton? FlyoutSlot { get; init; }

    /// <summary>Variant index inside <see cref="FlyoutSlot"/>'s flyout, or -1 otherwise.</summary>
    public int FlyoutVariant { get; init; }

    /// <summary>True when the target is the sheet scroll slider's track.</summary>
    public bool Slider { get; init; }

    /// <summary>The hovered flag toggle 0-7, or -1 otherwise.</summary>
    public int Flag { get; init; }

    /// <summary>Which buttonless region of the SOUND screen is hovered, or <see cref="SfxRegion.None"/>.</summary>
    public SfxRegion Sfx { get; init; }

    /// <summary>Which buttonless control of the music screen is under the pointer; <see cref="MusicRegion.None"/> for everything else.</summary>
    public MusicRegion Music { get; init; }

    /// <summary>Which buttonless control of the map screen is under the pointer; <see cref="MapRegion.None"/> for everything else.</summary>
    public MapRegion Map { get; init; }

    /// <summary>Which buttonless control of the code screen is under the pointer; <see cref="CodeRegion.None"/> for everything else.</summary>
    public CodeRegion Code { get; init; }

    public static HoverTarget OfButton(EditorButton button) =>
        new() { Button = button, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = SfxRegion.None, Music = MusicRegion.None, Map = MapRegion.None, Code = CodeRegion.None };

    public static HoverTarget OfSwatch(int swatch) =>
        new() { Button = null, Swatch = swatch, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = SfxRegion.None, Music = MusicRegion.None, Map = MapRegion.None, Code = CodeRegion.None };

    public static HoverTarget OfFlyoutVariant(EditorButton slot, int variant) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = slot, FlyoutVariant = variant, Slider = false, Flag = -1, Sfx = SfxRegion.None, Music = MusicRegion.None, Map = MapRegion.None, Code = CodeRegion.None };

    public static HoverTarget OfSlider() =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = true, Flag = -1, Sfx = SfxRegion.None, Music = MusicRegion.None, Map = MapRegion.None, Code = CodeRegion.None };

    /// <summary>The sound screen's buttonless controls; every other field is filled so kinds cannot collide.</summary>
    public static HoverTarget OfSfxRegion(SfxRegion region) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = region, Music = MusicRegion.None, Map = MapRegion.None, Code = CodeRegion.None };

    /// <summary>A buttonless control of the music screen is under the pointer.</summary>
    public static HoverTarget OfMusicRegion(MusicRegion region) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = SfxRegion.None, Music = region, Map = MapRegion.None, Code = CodeRegion.None };

    /// <summary>A buttonless control of the map screen is under the pointer.</summary>
    public static HoverTarget OfMapRegion(MapRegion region) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = SfxRegion.None, Music = MusicRegion.None, Map = region, Code = CodeRegion.None };

    /// <summary>A buttonless control of the code screen is under the pointer.</summary>
    public static HoverTarget OfCodeRegion(CodeRegion region) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = SfxRegion.None, Music = MusicRegion.None, Map = MapRegion.None, Code = region };

    public static HoverTarget OfFlag(int bit) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = bit, Sfx = SfxRegion.None, Music = MusicRegion.None, Map = MapRegion.None, Code = CodeRegion.None };
}

/// <summary>
/// The tooltip clock (M9 stage 2.5, owner's verdict): hovering any icon shows its frame
/// highlight immediately — that is <see cref="Target"/>, live from the first frame — but the
/// text tooltip only after three steady seconds, so a pointer crossing the toolbar does not
/// strobe labels. Headless on purpose: the shell feeds it the hit-test result and the frame's
/// elapsed seconds, and the wave's negative control ("the tooltip must NOT be instant") is a
/// plain unit test instead of a stopwatch at a window.
/// </summary>
public sealed class IconHoverTracker
{
    /// <summary>The owner's number, verbatim from the verdict: hover → tooltip after 3 seconds.</summary>
    public const double TooltipDelaySeconds = 3.0;

    private double _hoverSeconds;

    /// <summary>What is currently hovered — set the same frame the pointer arrives (the instant frame highlight).</summary>
    public HoverTarget? Target { get; private set; }

    /// <summary>True once the same target has been hovered for the full delay.</summary>
    public bool TooltipVisible => Target is not null && _hoverSeconds >= TooltipDelaySeconds;

    /// <summary>
    /// Forgets whatever is under the pointer, clock and all. The shell calls this the moment the
    /// screen changes, and that is not a nicety — it is a crash fix with a date.
    ///
    /// <para><b>What went wrong (2026-08-25, found by driving the window, not by a test).</b> A
    /// hover target is measured against ONE screen's layout, but this tracker outlives the
    /// screen. A frame goes input-then-draw: the pointer sat on the sound screen's OCTAVE
    /// stepper, that frame's reader wrote <c>OfSfxRegion(Octave)</c> here, and the same frame's
    /// Alt+Right moved the shell to the music screen — which then drew, asked this tracker what
    /// is under the pointer, got a target with no button and no music region, and asked
    /// <see cref="EditorIcons.MusicRegionTooltip"/> for the label of
    /// <see cref="MusicRegion.None"/>. That throws, inside <c>Draw</c>, which is not catchable
    /// anywhere useful: the console died with
    /// <c>System.ArgumentOutOfRangeException</c> and the author lost the tab they were on.
    /// One frame was all it took, because a keyboard tab switch lands between this frame's input
    /// and this frame's draw.</para>
    ///
    /// <para>The rule that replaces it: <b>a pointer target belongs to the screen it was
    /// measured on and does not outlive it.</b> Kept here rather than in the five readers because
    /// this type owns the target; a reader that forgot the call would put the crash straight
    /// back. The renderers were made unshakeable too (a foreign target now means "no label", not
    /// an exception) — belt and braces on purpose, since a renderer that can throw can kill the
    /// console, and no tooltip is ever worth that.</para>
    /// </summary>
    public void Clear()
    {
        Target = null;
        _hoverSeconds = 0;
    }

    /// <summary>
    /// One frame of hover. A change of target (including to or from nothing) restarts the
    /// clock at zero and deliberately banks none of this frame's time: the delay is measured
    /// from arrival, and crediting the arrival frame would make the delay shrink with lag.
    /// </summary>
    public void Update(HoverTarget? target, double elapsedSeconds)
    {
        if (!Nullable.Equals(target, Target))
        {
            Target = target;
            _hoverSeconds = 0;
            return;
        }
        if (Target is not null)
        {
            _hoverSeconds += elapsedSeconds;
        }
    }
}

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

    public static HoverTarget OfButton(EditorButton button) =>
        new() { Button = button, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = SfxRegion.None };

    public static HoverTarget OfSwatch(int swatch) =>
        new() { Button = null, Swatch = swatch, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = SfxRegion.None };

    public static HoverTarget OfFlyoutVariant(EditorButton slot, int variant) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = slot, FlyoutVariant = variant, Slider = false, Flag = -1, Sfx = SfxRegion.None };

    public static HoverTarget OfSlider() =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = true, Flag = -1, Sfx = SfxRegion.None };

    /// <summary>The sound screen's buttonless controls; every other field is filled so kinds cannot collide.</summary>
    public static HoverTarget OfSfxRegion(SfxRegion region) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = -1, Sfx = region };

    public static HoverTarget OfFlag(int bit) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1, Slider = false, Flag = bit, Sfx = SfxRegion.None };
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

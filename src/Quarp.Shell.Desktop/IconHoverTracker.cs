namespace Quarp.Shell.Desktop;

/// <summary>
/// What the mouse is hovering in the sprite editor: an icon-button, a palette swatch, or —
/// since wave 2e — a variant button inside an open group flyout (the 3-second tooltip
/// contract extends to variants by the order). A record struct so two frames over the same
/// target compare equal by value — the hover clock below hangs on that comparison; each
/// factory fills every field so targets of different kinds can never collide by accident.
/// </summary>
public readonly record struct HoverTarget
{
    /// <summary>The hovered icon-button, or null when the target is a swatch or a flyout variant.</summary>
    public EditorButton? Button { get; init; }

    /// <summary>The hovered palette swatch 0-15, or -1 otherwise.</summary>
    public int Swatch { get; init; }

    /// <summary>The open flyout's slot when the target is one of its variants, or null.</summary>
    public EditorButton? FlyoutSlot { get; init; }

    /// <summary>Variant index inside <see cref="FlyoutSlot"/>'s flyout, or -1 otherwise.</summary>
    public int FlyoutVariant { get; init; }

    public static HoverTarget OfButton(EditorButton button) =>
        new() { Button = button, Swatch = -1, FlyoutSlot = null, FlyoutVariant = -1 };

    public static HoverTarget OfSwatch(int swatch) =>
        new() { Button = null, Swatch = swatch, FlyoutSlot = null, FlyoutVariant = -1 };

    public static HoverTarget OfFlyoutVariant(EditorButton slot, int variant) =>
        new() { Button = null, Swatch = -1, FlyoutSlot = slot, FlyoutVariant = variant };
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

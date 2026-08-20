namespace Quarp.Shell.Desktop;

/// <summary>
/// The group-slot flyout's state machine (M9 stage 2.5 wave 2e): a long press or a right
/// click on a group slot opens a row of variant buttons beside it; a short press stays the
/// slot's ordinary click. Headless like <see cref="IconHoverTracker"/> and for the same
/// reason — the shell feeds it hits and elapsed seconds, and the negative controls ("a short
/// press must NOT open the flyout", "a press must not both open and click") are plain unit
/// tests instead of a stopwatch at a window.
///
/// <para>Two mutually exclusive states beyond idle: <b>armed</b> (the left button went down
/// on a group slot and the clock is running — the press's meaning is not decided yet) and
/// <b>open</b> (the flyout is on screen). What a chosen variant or a completed click MEANS is
/// deliberately not here: the shell routes those into the session, keeping this class free of
/// any way to touch a sheet.</para>
/// </summary>
public sealed class ToolbarFlyout
{
    /// <summary>
    /// How long a press must hold before it stops being a click and opens the flyout.
    /// Photoshop's own feel is a few hundred milliseconds; half a second keeps deliberate
    /// clicks (which run to ~0.1-0.2 s) comfortably below the threshold.
    /// </summary>
    public const double LongPressSeconds = 0.5;

    private double _heldSeconds;

    /// <summary>The slot whose flyout is on screen, or null. The renderer draws from this; the shell routes from it.</summary>
    public EditorButton? OpenSlot { get; private set; }

    /// <summary>The group slot under an undecided left press, or null.</summary>
    public EditorButton? ArmedSlot { get; private set; }

    /// <summary>
    /// Left press landed on a group slot: start the clock instead of acting. Any open flyout
    /// closes first — pressing another group slot while a flyout is up means the author moved
    /// on, and two open flyouts could overlap.
    /// </summary>
    public void Arm(EditorButton slot)
    {
        OpenSlot = null;
        ArmedSlot = slot;
        _heldSeconds = 0;
    }

    /// <summary>
    /// One frame of an armed press still held. Returns true on the frame the hold crosses
    /// <see cref="LongPressSeconds"/> and becomes the open flyout. Like the tooltip clock,
    /// the arming frame banks nothing — <see cref="Arm"/> zeroes the counter and the first
    /// Hold adds its own frame's time, so a lag spike cannot shrink the threshold.
    /// </summary>
    public bool Hold(double elapsedSeconds)
    {
        if (ArmedSlot is not EditorButton slot)
        {
            return false;
        }
        _heldSeconds += elapsedSeconds;
        if (_heldSeconds < LongPressSeconds)
        {
            return false;
        }
        ArmedSlot = null;
        OpenSlot = slot;
        return true;
    }

    /// <summary>
    /// The left button came up before the hold matured: the press was a click after all.
    /// Returns the slot whose click action the shell must now perform.
    /// </summary>
    public bool CompleteClick(out EditorButton slot)
    {
        if (ArmedSlot is not EditorButton armed)
        {
            slot = default;
            return false;
        }
        ArmedSlot = null;
        slot = armed;
        return true;
    }

    /// <summary>Right click on a group slot: the flyout opens immediately — no clock, no armed state.</summary>
    public void Open(EditorButton slot)
    {
        ArmedSlot = null;
        OpenSlot = slot;
    }

    /// <summary>A variant was chosen, the author clicked away, or Esc — the flyout is done either way.</summary>
    public void Close()
    {
        ArmedSlot = null;
        OpenSlot = null;
    }
}

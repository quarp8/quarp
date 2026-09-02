namespace Quarp.Api;

/// <summary>
/// Buttons of the console pointer (SPEC-8 §5, ADR-030). Three, deliberately: that is what the
/// shell's own editors read today and what TIC-80 exposes (ADR-030 п.2). The numeric value is
/// the bit index of the button in the replay's pointer stream (REPLAY-FORMAT §3), so the enum
/// cannot drift from the file format without the format tests noticing.
/// </summary>
public enum MouseButton
{
    /// <summary>Left button — also what a touch press reports (ADR-030 п.3).</summary>
    Left = 0,

    /// <summary>Right button.</summary>
    Right = 1,

    /// <summary>Middle button (wheel click).</summary>
    Middle = 2,
}

namespace Quarp.Api;

/// <summary>
/// Which built-in font a <see cref="IConsoleApi.Print(string, int, int, byte, Font)"/> call
/// draws with. Both fonts ship in every console; the cartridge picks per call, and there is
/// deliberately no "current font" state to set — a hidden mode is the kind of thing one
/// <c>Draw</c> path forgets to restore, and the frame it forgets in is the frame a replay
/// disagrees about.
/// <para><b>The player never chooses.</b> Text goes into the framebuffer, the framebuffer is
/// hashed, and the hash is the determinism contract (SPEC-8 §7): a font switch in the shell
/// would move pixels under every recorded replay and re-wrap every layout that measured itself
/// in glyph cells. Font size is a design decision of the cartridge, the same way sprite size
/// is. (An in-console code editor is a different room — it draws in host UI, outside the
/// framebuffer, and may scale text freely.)</para>
/// </summary>
public enum Font
{
    /// <summary>
    /// The 3x5 glyph in a 4x6 cell — the original QUARP-8 font and the default of every
    /// <c>Print</c> overload. 40 columns x 15 rows on the 160x90 screen, both whole; the densest
    /// text the console can hold, and what HUDs, scores and labels are laid out in.
    /// </summary>
    Small = 0,

    /// <summary>
    /// The 4x6 glyph in a 5x7 cell: 32 columns x 12 rows on the 160x90 screen. It costs a fifth
    /// of the characters per line and buys real descenders and wider letter shapes, which is the
    /// trade prose wants and a HUD does not.
    /// </summary>
    Large = 1,
}

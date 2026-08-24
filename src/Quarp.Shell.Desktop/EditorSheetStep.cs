using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One keyboard step of the sheet selection — Shift+arrows, the keyboard twin of clicking a
/// cell in the strip (M9 wave 2k, the gap the owner found before the parity instrument did).
///
/// <para>It lives in its own type rather than inside <c>QuarpGame.UpdateEditor</c> for one
/// reason, learned the hard way in the audit of that wave: the shell's editor dispatch cannot
/// be constructed without a <c>GraphicsDevice</c>, so anything written inline there can only be
/// tested by a copy of itself — and a test that mirrors the code it checks stays green when the
/// original is deleted. The arithmetic now has one owner that both the shell and the parity
/// instrument call.</para>
///
/// <para>Movement is in <b>strip</b> space through <see cref="SheetStrip"/>, the one owner of
/// the presentation mapping, so this path and the mouse's hit test aim at the same cell by
/// construction rather than by two copies of the same formula.</para>
/// </summary>
public static class EditorSheetStep
{
    /// <summary>
    /// Moves the edited sprite by one strip cell and returns the strip column it landed on, so
    /// the caller can scroll it into view. A step that would leave the strip is clamped, not
    /// wrapped: the ends of the sheet are ends, the way they are for the mouse.
    /// </summary>
    public static int Apply(SpriteEditorSession editor, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(editor);

        SheetStrip.SpriteToStripCell(editor.SpriteIndex, out int column, out int row);
        column = Math.Clamp(column + dx, 0, SheetStrip.Columns - 1);
        row = Math.Clamp(row + dy, 0, SheetStrip.Rows - 1);
        if (SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY))
        {
            editor.SelectRegionCell(sheetX, sheetY);
        }
        return column;
    }
}

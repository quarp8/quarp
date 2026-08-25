using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// <see cref="EditorIcons"/>' 8x8 masks, plotted straight onto the console — born as the
/// console-side twin of a host icon atlas (wave R1) and, since that atlas left the tree with
/// the host font path in wave R6, the only way an icon reaches a tool screen at all.
///
/// <para><b>Why Pset and not the shell's sprite sheet.</b> Both were on the table. A sheet
/// would mean copying every mask into a 128x128 <c>_sheet</c> at boot and drawing with
/// <c>Spr</c> — one call per icon instead of up to 64 plots. It was rejected because it makes
/// a second copy of the icon pixels, in a second layout (sheet cells), with a second thing to
/// keep in step whenever <see cref="EditorIcons"/> gains a glyph: the sheet index becomes a
/// fact somebody has to own, and the mask table stops being the whole truth about what an icon
/// looks like. Plotting reads the mask directly, so <see cref="EditorIcons"/> stays the single
/// owner of icon pixels.</para>
///
/// <para>The cost is the argument against it, so here it is measured rather than waved away:
/// an icon is 64 <c>Pset</c> calls worst case, a tool screen shows on the order of twenty
/// icons, so a full screen is about 1200 plots — against the 14400 the <c>Cls</c> on the same
/// frame writes. This is drawing, not simulation: it happens once per rendered frame, never in
/// a tick, and no rewind ever replays it.</para>
///
/// <para>The colour is a normal palette slot, so an icon's state (live, stubbed, active,
/// warning) is an argument here rather than a property of the picture. Colour 0 is
/// not treated as transparent: mask bits that are clear simply are not plotted, which leaves
/// whatever the screen drew underneath — the same result <c>Palt</c> would give, without
/// touching a console-wide setting on the caller's behalf.</para>
/// </summary>
public static class ConsoleIcons
{
    /// <summary>Side of an icon in console pixels — <see cref="EditorIcons.IconPixels"/>, named here so callers laying out a strip need not reach for the other file's constant.</summary>
    public const int Size = EditorIcons.IconPixels;

    /// <summary>
    /// Draws one icon with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).
    /// Clipping, camera and palette remap are the console's, because every pixel goes through
    /// <c>Pset</c> — an icon half off the screen is half drawn, not skipped.
    /// </summary>
    public static void Draw(VirtualConsole console, EditorIcon icon, int x, int y, byte color)
    {
        ArgumentNullException.ThrowIfNull(console);
        for (int row = 0; row < Size; row++)
        {
            for (int col = 0; col < Size; col++)
            {
                if (EditorIcons.IsSet(icon, col, row))
                {
                    console.Pset(x + col, y + row, color);
                }
            }
        }
    }
}

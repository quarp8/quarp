using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// How much room the shell's host-UI text takes, as pure arithmetic over
/// <see cref="SystemFont"/>'s cell — width of a line, advance between lines, and the whole
/// integer scale a window of a given size deserves.
///
/// <para><b>Why it is not part of <see cref="PixelFontAtlas"/></b> (M9, the module-boundary
/// wave). The atlas owns a <see cref="Microsoft.Xna.Framework.Graphics.Texture2D"/>: it cannot
/// exist without a graphics device, and it belongs to the drawing layer. These three functions
/// need no device and no pixels — they are what <em>layout</em> asks before anything is drawn,
/// and <see cref="EditorChrome"/> is layout. Holding both facts in one file made the frame's
/// geometry a reader of the drawing layer, which is the one direction references must never
/// take. Nothing moved but the file boundary; the arithmetic is character-for-character the
/// atlas's, and the atlas still exposes it under its old names for callers that already had a
/// font in hand (see the forwarders there — one owner, one extra doorway).</para>
/// </summary>
public static class PixelFontMetrics
{
    /// <summary>
    /// Width of one line of text at the given scale, spacing included — what layout code adds
    /// to a cursor. The trailing 1 px of inter-character spacing is counted rather than
    /// trimmed (contrast <c>ShellOverlay.MeasureWidth</c>): host-UI centring is off by half a
    /// scaled pixel either way, and the simpler figure is the one that cannot be summed wrong.
    /// </summary>
    public static int MeasureWidth(string text, int scale) => text.Length * SystemFont.CellWidth * scale;

    /// <summary>Line advance at the given scale, for callers laying out multiple rows.</summary>
    public static int LineHeight(int scale) => SystemFont.CellHeight * scale;

    /// <summary>
    /// Whole-integer host-UI text scale from the window size — one formula for every host
    /// screen (library, sprite editor), living with the font's metrics because it is a fact
    /// about text density. Anchored at 320x180 rather than the console's 160x90 because host
    /// UI wants density, not console-sized letters: a 1280x720 window gets x4 (24 px line
    /// height, ~28 rows), and the floor of 2 keeps text legible in a window shrunk below the
    /// anchor.
    /// </summary>
    public static int UiScale(int width, int height) =>
        Math.Max(2, Math.Min(width / 320, height / 180));
}

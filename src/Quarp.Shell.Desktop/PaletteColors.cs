using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The one place the shell turns a <see cref="Palette.Master32"/> entry (0xRRGGBB) into a
/// MonoGame <see cref="Color"/>. The unpacking is three shifts, but it appeared in two files
/// the moment the library screen was written, and a byte-order slip in one copy would be a
/// palette bug that only shows on screen — so both the presenter and the host UI call this.
/// (<see cref="ShellOverlay"/> keeps its own alpha-premultiplying variant: that one bakes
/// translucency in, which is a different fact.)
/// </summary>
public static class PaletteColors
{
    /// <summary>Master palette entry as a fully opaque colour.</summary>
    public static Color Opaque(int index)
    {
        uint rgb = Palette.Master32[index];
        return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}

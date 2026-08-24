using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the library screen — the console's face when no cartridge is running (the sprite
/// editor has its own renderer, <see cref="SpriteEditorRenderer"/>, because it also owns a
/// sheet texture). Host UI, deliberately unlike the game presenter: it paints at the window's
/// native resolution (an organizer decision for M9 stage 1 — this is the host's screen, not
/// the virtual one), but on <see cref="Palette.Master32"/> colours and the system font, so it
/// still reads as the console and not as an OS dialog.
///
/// <para>Nothing here can touch a framebuffer or a hash: the golden master is the cartridge's
/// frame, and no cartridge is running while this draws. That is also why this file needs no
/// determinism care — it may measure the window every frame and lay out from it.</para>
/// </summary>
public sealed class LibraryRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly PixelFontAtlas _font;
    private readonly Texture2D _white;

    // The library's fixed cast of palette roles. Indices are Master32's documented visible
    // slots (Palette.cs): 0 ink, 1 gray, 2 light gray, 3 white, 4 blue, 10 red.
    private static readonly Color Ink = PaletteColors.Opaque(0);
    private static readonly Color Dim = PaletteColors.Opaque(1);
    private static readonly Color Text = PaletteColors.Opaque(2);
    private static readonly Color Bright = PaletteColors.Opaque(3);
    private static readonly Color SelectionBar = PaletteColors.Opaque(4);
    private static readonly Color Error = PaletteColors.Opaque(10);

    public LibraryRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _font = new PixelFontAtlas(device);
        _white = new Texture2D(device, 1, 1);
        _white.SetData(new[] { Color.White });
    }

    /// <summary>
    /// One frame of the library. Owns the whole surface (clears, begins and ends the batch):
    /// callers hand over the window, not a layer.
    /// </summary>
    /// <param name="message">The last failed launch's message, or null (ShellModeMachine.LibraryMessage).</param>
    public void DrawLibrary(SpriteBatch batch, int width, int height, CartLibrary library, string? message)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(library);
        _device.Clear(Ink);
        int scale = PixelFontAtlas.UiScale(width, height);
        int margin = 4 * scale;
        batch.Begin(samplerState: SamplerState.PointClamp);

        int y = margin;
        _font.Draw(batch, "QUARP", margin, y, scale * 2, Bright);
        _font.Draw(batch, "GAME LIBRARY", margin + PixelFontAtlas.MeasureWidth("QUARP ", scale * 2), y + PixelFontAtlas.LineHeight(scale), scale, Dim);
        y += PixelFontAtlas.LineHeight(scale * 2) + scale * 2;

        // The bottom strip: key hints always, the error line above them only when there is one.
        int footerY = height - margin - PixelFontAtlas.LineHeight(scale);
        _font.Draw(batch, "UP/DOWN SELECT   Z/ENTER PLAY   X EDITOR   ESC MENU", margin, footerY, scale, Dim);
        int listBottom = footerY - scale * 2;
        if (message is not null)
        {
            listBottom -= PixelFontAtlas.LineHeight(scale) + scale;
            _font.Draw(batch, message, margin, listBottom + scale, scale, Error);
        }

        if (library.Entries.Count == 0)
        {
            // The work order's empty-state promise, verbatim in spirit: tell the player where
            // carts come from instead of showing them a test pattern.
            _font.Draw(batch, "NO CARTRIDGES FOUND", margin, y, scale, Bright);
            y += PixelFontAtlas.LineHeight(scale) + scale;
            _font.Draw(batch, "PUT A CART FOLDER OR A .QUARP8 FILE IN CARTS/", margin, y, scale, Text);
            y += PixelFontAtlas.LineHeight(scale);
            _font.Draw(batch, "OR CREATE ONE:  QUARP NEW MYGAME", margin, y, scale, Text);
        }
        else
        {
            DrawEntries(batch, library, margin, y, listBottom, width, scale);
        }

        batch.End();
    }

    public void Dispose()
    {
        _font.Dispose();
        _white.Dispose();
    }

    /// <summary>
    /// The cart rows, windowed around the selection so a library longer than the screen
    /// scrolls instead of clipping. The selected row gets a bar, not just a colour: colour
    /// alone disappears on a bad projector, and this screen is the first thing a player sees.
    /// </summary>
    private void DrawEntries(SpriteBatch batch, CartLibrary library, int margin, int top, int bottom, int width, int scale)
    {
        int rowHeight = PixelFontAtlas.LineHeight(scale) + scale;
        int visible = Math.Max(1, (bottom - top) / rowHeight);
        int count = library.Entries.Count;
        int first = Math.Clamp(library.SelectedIndex - visible / 2, 0, Math.Max(0, count - visible));
        for (int i = first; i < Math.Min(count, first + visible); i++)
        {
            int rowY = top + (i - first) * rowHeight;
            bool selected = i == library.SelectedIndex;
            if (selected)
            {
                batch.Draw(_white, new Rectangle(margin - scale, rowY - scale / 2, width - 2 * margin + 2 * scale, rowHeight), SelectionBar);
            }
            _font.Draw(batch, library.Entries[i].Name, margin, rowY, scale, selected ? Bright : Text);
        }
    }

}

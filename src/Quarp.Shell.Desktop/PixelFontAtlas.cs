using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// <see cref="SystemFont"/> rasterized once into a texture strip, for text drawn at the
/// window's native resolution (the library and the editor stub — host UI, not console
/// pixels). One white glyph per cell, tinted at draw time, scaled by whole integers and
/// sampled with <see cref="SamplerState.PointClamp"/>: that is the "scaled console font over
/// SpriteBatch" the M9 stage 1 order names as the sanctioned mechanism, and it keeps the
/// shell's face in the same 3x5 type as everything a cartridge prints.
///
/// <para>The glyph data has exactly one owner — <see cref="SystemFont"/>; this type only
/// unpacks it into pixels. Unlike <see cref="ShellOverlay"/>, which re-plots glyphs every time
/// its text changes because its surface is console-sized, this pays the unpacking cost once
/// and then every character is a single textured quad, which is what makes a full screen of
/// library rows cheap.</para>
/// </summary>
public sealed class PixelFontAtlas : IDisposable
{
    private const int GlyphCount = SystemFont.LastChar - SystemFont.FirstChar + 1;

    /// <summary>Strip cell for characters outside ASCII 32-126: SystemFont's fallback box.</summary>
    private const int FallbackIndex = GlyphCount;

    private readonly Texture2D _texture;

    public PixelFontAtlas(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        int stripWidth = (GlyphCount + 1) * SystemFont.CellWidth;
        _texture = new Texture2D(device, stripWidth, SystemFont.GlyphHeight);
        var pixels = new Color[stripWidth * SystemFont.GlyphHeight];
        for (int i = 0; i <= GlyphCount; i++)
        {
            // One past LastChar lands outside SystemFont's table, which is exactly how the
            // fallback box is asked for — the font, not this atlas, owns what "unknown" looks like.
            uint glyph = SystemFont.GetGlyph((char)(SystemFont.FirstChar + i));
            for (int row = 0; row < SystemFont.GlyphHeight; row++)
            {
                for (int col = 0; col < SystemFont.GlyphWidth; col++)
                {
                    if (SystemFont.IsSet(glyph, col, row))
                    {
                        pixels[row * stripWidth + i * SystemFont.CellWidth + col] = Color.White;
                    }
                }
            }
        }
        _texture.SetData(pixels);
    }

    // The three text-metric questions have ONE owner, and since the module-boundary wave it is
    // PixelFontMetrics — a device-free type the layout layer may read without reaching up into
    // the drawing layer. What stays here are three forwarders, kept because a caller that
    // already holds a font naturally asks the font, and because deleting them would rewrite
    // call sites (a test among them) for no gain in behaviour. They compute nothing.

    /// <inheritdoc cref="PixelFontMetrics.MeasureWidth"/>
    public static int MeasureWidth(string text, int scale) => PixelFontMetrics.MeasureWidth(text, scale);

    /// <inheritdoc cref="PixelFontMetrics.LineHeight"/>
    public static int LineHeight(int scale) => PixelFontMetrics.LineHeight(scale);

    /// <inheritdoc cref="PixelFontMetrics.UiScale"/>
    public static int UiScale(int width, int height) => PixelFontMetrics.UiScale(width, height);

    /// <summary>
    /// Draws one line (no newline handling — the callers own their layout) inside an already
    /// begun <paramref name="batch"/>. Characters outside ASCII 32-126 show
    /// <see cref="SystemFont"/>'s fallback box, same as a cartridge's <c>Print</c> would.
    /// </summary>
    public void Draw(SpriteBatch batch, string text, int x, int y, int scale, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(text);
        int cursorX = x;
        foreach (char c in text)
        {
            int index = c is >= SystemFont.FirstChar and <= SystemFont.LastChar
                ? c - SystemFont.FirstChar
                : FallbackIndex;
            var source = new Rectangle(
                index * SystemFont.CellWidth, 0, SystemFont.GlyphWidth, SystemFont.GlyphHeight);
            var destination = new Rectangle(
                cursorX, y, SystemFont.GlyphWidth * scale, SystemFont.GlyphHeight * scale);
            batch.Draw(_texture, destination, source, color);
            cursorX += SystemFont.CellWidth * scale;
        }
    }

    public void Dispose() => _texture.Dispose();
}

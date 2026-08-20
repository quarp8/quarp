using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Quarp.Shell.Desktop;

/// <summary>
/// <see cref="EditorIcons"/>' masks rasterized once into a texture strip — the exact shape of
/// <see cref="PixelFontAtlas"/>, for the exact reason: white pixels tinted at draw time make
/// every icon state (live, stub, active, warning) a color argument instead of a texture, and
/// one strip upload makes a screen full of buttons cost one quad each. The pixel data has one
/// owner (<see cref="EditorIcons"/>); this type only unpacks it.
/// </summary>
public sealed class EditorIconAtlas : IDisposable
{
    private readonly Texture2D _texture;

    public EditorIconAtlas(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        int stripWidth = EditorIcons.IconCount * EditorIcons.IconPixels;
        _texture = new Texture2D(device, stripWidth, EditorIcons.IconPixels);
        var pixels = new Color[stripWidth * EditorIcons.IconPixels];
        for (int i = 0; i < EditorIcons.IconCount; i++)
        {
            for (int row = 0; row < EditorIcons.IconPixels; row++)
            {
                for (int col = 0; col < EditorIcons.IconPixels; col++)
                {
                    if (EditorIcons.IsSet((EditorIcon)i, col, row))
                    {
                        pixels[row * stripWidth + i * EditorIcons.IconPixels + col] = Color.White;
                    }
                }
            }
        }
        _texture.SetData(pixels);
    }

    /// <summary>
    /// One icon as one tinted quad inside an already begun batch. The destination should be a
    /// whole multiple of <see cref="EditorIcons.IconPixels"/> (the layout guarantees it) —
    /// under PointClamp anything else would resample pixel art into blur.
    /// </summary>
    public void Draw(SpriteBatch batch, EditorIcon icon, Rectangle destination, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var source = new Rectangle(
            (int)icon * EditorIcons.IconPixels, 0, EditorIcons.IconPixels, EditorIcons.IconPixels);
        batch.Draw(_texture, destination, source, color);
    }

    public void Dispose() => _texture.Dispose();
}

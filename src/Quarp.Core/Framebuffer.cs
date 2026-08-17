namespace Quarp.Core;

/// <summary>
/// Indexed framebuffer: one byte per pixel, an index into <see cref="Palette.Master32"/>.
/// The cartridge-facing surface will be 0-15 plus Pal-remapping (SPEC-8 §2); at M0 the
/// test pattern writes full 0-31 directly.
/// </summary>
public sealed class Framebuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public Framebuffer(ConsoleProfile profile)
    {
        Width = profile.Width;
        Height = profile.Height;
        Pixels = new byte[Width * Height];
    }

    public void Clear(byte color) => Array.Fill(Pixels, color);

    public void FillRect(int x, int y, int width, int height, byte color)
    {
        int x0 = Math.Max(x, 0);
        int y0 = Math.Max(y, 0);
        int x1 = Math.Min(x + width, Width);
        int y1 = Math.Min(y + height, Height);
        for (int py = y0; py < y1; py++)
        {
            int row = py * Width;
            for (int px = x0; px < x1; px++)
            {
                Pixels[row + px] = color;
            }
        }
    }
}

using Quarp.Core;

namespace Quarp.Cli;

/// <summary>Minimal 24-bit BMP writer — dev tool for dumping the framebuffer without image libraries.</summary>
internal static class BmpWriter
{
    public static void Write(string path, Framebuffer fb)
    {
        int width = fb.Width;
        int height = fb.Height;
        int rowStride = (width * 3 + 3) & ~3;
        int pixelBytes = rowStride * height;
        int fileSize = 54 + pixelBytes;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(54);          // pixel data offset
        writer.Write(40);          // BITMAPINFOHEADER size
        writer.Write(width);
        writer.Write(height);      // positive = bottom-up
        writer.Write((short)1);    // planes
        writer.Write((short)24);   // bpp
        writer.Write(0);           // no compression
        writer.Write(pixelBytes);
        writer.Write(2835); writer.Write(2835); // 72 DPI
        writer.Write(0); writer.Write(0);

        var row = new byte[rowStride];
        for (int y = height - 1; y >= 0; y--)
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                uint rgb = Palette.Master32[fb.Pixels[rowStart + x]];
                row[x * 3 + 0] = (byte)rgb;          // B
                row[x * 3 + 1] = (byte)(rgb >> 8);   // G
                row[x * 3 + 2] = (byte)(rgb >> 16);  // R
            }
            writer.Write(row);
        }
    }
}

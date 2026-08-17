using System.Buffers.Binary;
using System.IO.Compression;
using Quarp.Core;

namespace Quarp.CartKit;

/// <summary>
/// Minimal PNG decoder for cartridge art (Format spec v1, M1 work order): chunks
/// IHDR/PLTE/tRNS/IDAT/IEND, color types 3 (indexed), 2 (RGB), 6 (RGBA), bit depth 8,
/// scanline filters 0-4, no interlacing; inflate via <see cref="ZLibStream"/>.
/// Chunk CRCs are not verified — corruption fails loudly in inflate or palette matching.
/// Output is palette indices: every opaque pixel must exactly match one of the 16 visible
/// colors of <see cref="Palette.Master32"/> (no nearest-color — SPEC-8 §6); alpha 0 maps
/// to index 0; partial alpha is an error.
/// </summary>
public static class PngDecoder
{
    private const uint ChunkIhdr = 0x49484452; // "IHDR"
    private const uint ChunkPlte = 0x504c5445; // "PLTE"
    private const uint ChunkTrns = 0x74524e53; // "tRNS"
    private const uint ChunkIdat = 0x49444154; // "IDAT"
    private const uint ChunkIend = 0x49454e44; // "IEND"

    /// <summary>
    /// Decodes a PNG into row-major palette indices 0-15. The image must be exactly
    /// <paramref name="expectedWidth"/> x <paramref name="expectedHeight"/> pixels.
    /// <paramref name="sourceName"/> (e.g. "gfx.png") prefixes every error message.
    /// </summary>
    public static byte[] DecodeToPaletteIndices(byte[] png, int expectedWidth, int expectedHeight, string sourceName)
    {
        if (png.Length < 8
            || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4e || png[3] != 0x47
            || png[4] != 0x0d || png[5] != 0x0a || png[6] != 0x1a || png[7] != 0x0a)
        {
            throw new CartLoadException($"{sourceName}: not a PNG file (bad signature).");
        }

        int width = 0;
        int height = 0;
        int colorType = 0;
        bool sawIhdr = false;
        bool sawIend = false;
        byte[]? plte = null;
        byte[]? trns = null;
        using var idat = new MemoryStream();

        int offset = 8;
        while (!sawIend)
        {
            if (offset + 8 > png.Length)
            {
                throw new CartLoadException($"{sourceName}: truncated PNG (chunk stream ends without IEND).");
            }
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            uint type = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 4, 4));
            if (length < 0 || offset + 12 + length > png.Length)
            {
                throw new CartLoadException($"{sourceName}: truncated PNG chunk.");
            }
            ReadOnlySpan<byte> chunk = png.AsSpan(offset + 8, length);

            if (!sawIhdr && type != ChunkIhdr)
            {
                throw new CartLoadException($"{sourceName}: malformed PNG (first chunk is not IHDR).");
            }

            switch (type)
            {
                case ChunkIhdr:
                    if (length != 13)
                    {
                        throw new CartLoadException($"{sourceName}: malformed IHDR chunk.");
                    }
                    width = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(0, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(4, 4));
                    int bitDepth = chunk[8];
                    colorType = chunk[9];
                    int compression = chunk[10];
                    int filterMethod = chunk[11];
                    int interlace = chunk[12];
                    if (bitDepth != 8)
                    {
                        throw new CartLoadException($"{sourceName}: bit depth {bitDepth} is not supported (8-bit only).");
                    }
                    if (colorType != 2 && colorType != 3 && colorType != 6)
                    {
                        throw new CartLoadException(
                            $"{sourceName}: color type {colorType} is not supported (indexed, RGB or RGBA only).");
                    }
                    if (compression != 0 || filterMethod != 0)
                    {
                        throw new CartLoadException($"{sourceName}: malformed PNG (unknown compression or filter method).");
                    }
                    if (interlace != 0)
                    {
                        throw new CartLoadException(
                            $"{sourceName}: interlaced (Adam7) PNGs are not supported — re-export without interlacing.");
                    }
                    if (width != expectedWidth || height != expectedHeight)
                    {
                        throw new CartLoadException(
                            $"{sourceName}: image is {width}x{height}, must be exactly {expectedWidth}x{expectedHeight}.");
                    }
                    sawIhdr = true;
                    break;
                case ChunkPlte:
                    if (length == 0 || length % 3 != 0 || length > 768)
                    {
                        throw new CartLoadException($"{sourceName}: malformed PLTE chunk.");
                    }
                    plte = chunk.ToArray();
                    break;
                case ChunkTrns:
                    trns = chunk.ToArray();
                    break;
                case ChunkIdat:
                    idat.Write(chunk);
                    break;
                case ChunkIend:
                    sawIend = true;
                    break;
                default:
                    // Ancillary chunks (pHYs, tEXt, gAMA, ...) are irrelevant here.
                    break;
            }
            offset += 12 + length; // 8-byte header + data + 4-byte CRC (not verified).
        }

        if (colorType == 3 && plte is null)
        {
            throw new CartLoadException($"{sourceName}: indexed PNG has no PLTE chunk.");
        }
        if (idat.Length == 0)
        {
            throw new CartLoadException($"{sourceName}: PNG has no IDAT pixel data.");
        }

        int bpp = colorType switch { 3 => 1, 2 => 3, _ => 4 };
        int rowBytes = width * bpp;
        byte[] raw = new byte[(rowBytes + 1) * height];
        idat.Position = 0;
        try
        {
            using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
            inflate.ReadExactly(raw);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            throw new CartLoadException($"{sourceName}: corrupt or truncated PNG pixel data.", e);
        }

        byte[] recon = Unfilter(raw, width, height, bpp, sourceName);
        return MapToPalette(recon, width, height, colorType, bpp, plte, trns, sourceName);
    }

    private static byte[] Unfilter(byte[] raw, int width, int height, int bpp, string sourceName)
    {
        int rowBytes = width * bpp;
        byte[] recon = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
        {
            int rawRow = y * (rowBytes + 1);
            int filter = raw[rawRow];
            if (filter > 4)
            {
                throw new CartLoadException($"{sourceName}: unknown PNG scanline filter {filter} on row {y}.");
            }
            int outRow = y * rowBytes;
            for (int i = 0; i < rowBytes; i++)
            {
                int value = raw[rawRow + 1 + i];
                int left = i >= bpp ? recon[outRow + i - bpp] : 0;
                int up = y > 0 ? recon[outRow - rowBytes + i] : 0;
                int upLeft = i >= bpp && y > 0 ? recon[outRow - rowBytes + i - bpp] : 0;
                value += filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };
                recon[outRow + i] = (byte)value;
            }
        }
        return recon;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }
        return pb <= pc ? b : c;
    }

    private static byte[] MapToPalette(
        byte[] recon, int width, int height, int colorType, int bpp, byte[]? plte, byte[]? trns, string sourceName)
    {
        byte[] indices = new byte[width * height];
        ReadOnlySpan<uint> master = Palette.Master32;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * bpp;
            for (int x = 0; x < width; x++)
            {
                int r, g, b, alpha;
                if (colorType == 3)
                {
                    int paletteIndex = recon[row + x];
                    if (paletteIndex * 3 >= plte!.Length)
                    {
                        throw new CartLoadException(
                            $"{sourceName}: pixel ({x},{y}) uses palette index {paletteIndex}, beyond the PLTE table.");
                    }
                    r = plte[paletteIndex * 3];
                    g = plte[paletteIndex * 3 + 1];
                    b = plte[paletteIndex * 3 + 2];
                    alpha = trns is not null && paletteIndex < trns.Length ? trns[paletteIndex] : 255;
                }
                else if (colorType == 2)
                {
                    int p = row + x * 3;
                    r = recon[p];
                    g = recon[p + 1];
                    b = recon[p + 2];
                    // tRNS for RGB is a 16-bit-per-sample color key; at bit depth 8 the value sits in the low byte.
                    alpha = trns is { Length: >= 6 } && r == trns[1] && g == trns[3] && b == trns[5] ? 0 : 255;
                }
                else
                {
                    int p = row + x * 4;
                    r = recon[p];
                    g = recon[p + 1];
                    b = recon[p + 2];
                    alpha = recon[p + 3];
                }

                if (alpha == 0)
                {
                    indices[y * width + x] = 0;
                    continue;
                }
                uint rgb = (uint)((r << 16) | (g << 8) | b);
                if (alpha != 255)
                {
                    throw new CartLoadException(
                        $"{sourceName}: pixel ({x},{y}) color #{rgb:x6} has partial alpha {alpha}; "
                        + "pixels must be fully opaque or fully transparent.");
                }
                int match = -1;
                for (int i = 0; i < Palette.VisibleCount; i++)
                {
                    if (master[i] == rgb)
                    {
                        match = i;
                        break;
                    }
                }
                if (match < 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: pixel ({x},{y}) color #{rgb:x6} is not one of the 16 visible palette colors "
                        + "(exact match required, no nearest-color — SPEC-8 §6).");
                }
                indices[y * width + x] = (byte)match;
            }
        }
        return indices;
    }
}

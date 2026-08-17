using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Quarp.CartKit.Tests;

/// <summary>
/// Hand-crafts valid PNG byte streams for decoder tests: correct signature, chunk CRCs
/// (computed here with a self-contained CRC-32) and zlib-compressed scanlines, with
/// per-row filter types applied by the standard PNG filter formulas.
/// </summary>
public static class PngBuilder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    public static void WriteChunk(MemoryStream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        byte[] typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData.AsSpan(4));
        stream.Write(typeAndData);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        stream.Write(crc);
    }

    public static byte[] ZlibCompress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Applies the PNG scanline filter of the given type to raw pixel rows, producing the
    /// pre-compression byte stream (filter byte + filtered bytes per row).
    /// </summary>
    public static byte[] FilterScanlines(byte[] pixels, int width, int height, int bpp, int[] rowFilters)
    {
        int rowBytes = width * bpp;
        byte[] output = new byte[(rowBytes + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int filter = rowFilters[y];
            output[y * (rowBytes + 1)] = (byte)filter;
            for (int i = 0; i < rowBytes; i++)
            {
                int raw = pixels[y * rowBytes + i];
                int left = i >= bpp ? pixels[y * rowBytes + i - bpp] : 0;
                int up = y > 0 ? pixels[(y - 1) * rowBytes + i] : 0;
                int upLeft = i >= bpp && y > 0 ? pixels[(y - 1) * rowBytes + i - bpp] : 0;
                int predictor = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };
                output[y * (rowBytes + 1) + 1 + i] = (byte)(raw - predictor);
            }
        }
        return output;
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

    /// <summary>
    /// Builds a complete PNG: signature, IHDR, optional PLTE/tRNS, one IDAT, IEND.
    /// <paramref name="colorType"/>: 3 = indexed (bpp 1), 2 = RGB (bpp 3), 6 = RGBA (bpp 4).
    /// <paramref name="pixels"/> is raw row-major sample data without filter bytes.
    /// </summary>
    public static byte[] Build(
        int width,
        int height,
        int colorType,
        byte[] pixels,
        byte[]? plte = null,
        byte[]? trns = null,
        int[]? rowFilters = null)
    {
        int bpp = colorType switch { 3 => 1, 2 => 3, 6 => 4, _ => throw new ArgumentException(null, nameof(colorType)) };
        rowFilters ??= new int[height];

        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;                    // bit depth
        ihdr[9] = (byte)colorType;
        WriteChunk(stream, "IHDR", ihdr);

        if (plte is not null)
        {
            WriteChunk(stream, "PLTE", plte);
        }
        if (trns is not null)
        {
            WriteChunk(stream, "tRNS", trns);
        }

        byte[] filtered = FilterScanlines(pixels, width, height, bpp, rowFilters);
        WriteChunk(stream, "IDAT", ZlibCompress(filtered));
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    /// <summary>PLTE bytes for a set of 0xRRGGBB colors.</summary>
    public static byte[] PlteFromRgb(params uint[] colors)
    {
        byte[] plte = new byte[colors.Length * 3];
        for (int i = 0; i < colors.Length; i++)
        {
            plte[i * 3] = (byte)(colors[i] >> 16);
            plte[i * 3 + 1] = (byte)(colors[i] >> 8);
            plte[i * 3 + 2] = (byte)colors[i];
        }
        return plte;
    }
}

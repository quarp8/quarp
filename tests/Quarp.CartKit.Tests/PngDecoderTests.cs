using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// PngDecoder against hand-crafted byte fixtures (built with correct CRCs by
/// <see cref="PngBuilder"/>): indexed/RGB/RGBA decoding, all five scanline filters,
/// exact-palette enforcement with pixel coordinates in the error, and malformed streams.
/// </summary>
public class PngDecoderTests
{
    private static uint Master(int index) => Palette.Master32[index];

    [Fact]
    public void BuilderCrcMatchesKnownVector()
    {
        // CRC-32 of the ASCII bytes "IEND" is the classic published value; anchors the helper.
        Assert.Equal(0xAE426082u, PngBuilder.Crc32("IEND"u8));
    }

    [Fact]
    public void IndexedPngDecodesToMasterIndices()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(3), Master(10), Master(7), Master(0));
        byte[] pixels =
        {
            0, 1, 2, 3,
            3, 2, 1, 0,
            0, 0, 1, 1,
            2, 2, 3, 3,
        };
        byte[] png = PngBuilder.Build(4, 4, colorType: 3, pixels, plte);
        byte[] decoded = PngDecoder.DecodeToPaletteIndices(png, 4, 4, "gfx.png");
        byte[] expected =
        {
            3, 10, 7, 0,
            0, 7, 10, 3,
            3, 3, 10, 10,
            7, 7, 0, 0,
        };
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void IndexedTransparentEntryDecodesToZero()
    {
        // PLTE entry 0 is white (master 3), but tRNS declares it fully transparent.
        byte[] plte = PngBuilder.PlteFromRgb(Master(3), Master(10));
        byte[] trns = { 0 };
        byte[] pixels = { 0, 1, 1, 0 };
        byte[] png = PngBuilder.Build(2, 2, colorType: 3, pixels, plte, trns);
        byte[] decoded = PngDecoder.DecodeToPaletteIndices(png, 2, 2, "gfx.png");
        Assert.Equal(new byte[] { 0, 10, 10, 0 }, decoded);
    }

    [Fact]
    public void AllFiveScanlineFiltersReconstructExactly()
    {
        // 8x5 indexed image, one row per filter type 0-4, wavy data so predictors differ.
        byte[] plte = PngBuilder.PlteFromRgb(
            Master(0), Master(1), Master(2), Master(3), Master(4), Master(5), Master(6), Master(7),
            Master(8), Master(9), Master(10), Master(11), Master(12), Master(13), Master(14), Master(15));
        byte[] pixels = new byte[8 * 5];
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                pixels[y * 8 + x] = (byte)((x * 3 + y * 5 + (x & y)) & 0x0F);
            }
        }
        byte[] png = PngBuilder.Build(8, 5, colorType: 3, pixels, plte, rowFilters: new[] { 0, 1, 2, 3, 4 });
        byte[] decoded = PngDecoder.DecodeToPaletteIndices(png, 8, 5, "gfx.png");
        Assert.Equal(pixels, decoded); // PLTE entry i is master color i, so indices round-trip
    }

    [Fact]
    public void RgbPngDecodesAndColorKeyIsTransparent()
    {
        // tRNS for RGB is a 16-bit-per-sample color key; low bytes carry the 8-bit value.
        uint key = Master(5);
        byte[] trns =
        {
            0, (byte)(key >> 16), 0, (byte)(key >> 8), 0, (byte)key,
        };
        byte[] pixels = new byte[2 * 1 * 3];
        WriteRgb(pixels, 0, Master(9));
        WriteRgb(pixels, 1, key);
        byte[] png = PngBuilder.Build(2, 1, colorType: 2, pixels, trns: trns);
        byte[] decoded = PngDecoder.DecodeToPaletteIndices(png, 2, 1, "gfx.png");
        Assert.Equal(new byte[] { 9, 0 }, decoded);
    }

    [Fact]
    public void RgbaPngDecodesWithAlphaZeroAsIndexZero()
    {
        byte[] pixels = new byte[2 * 2 * 4];
        WriteRgba(pixels, 0, Master(12), 255);
        WriteRgba(pixels, 1, 0x123456, 0);      // fully transparent: color is irrelevant
        WriteRgba(pixels, 2, Master(15), 255);
        WriteRgba(pixels, 3, Master(0), 255);   // opaque ink is index 0 too
        byte[] png = PngBuilder.Build(2, 2, colorType: 6, pixels);
        byte[] decoded = PngDecoder.DecodeToPaletteIndices(png, 2, 2, "gfx.png");
        Assert.Equal(new byte[] { 12, 0, 15, 0 }, decoded);
    }

    [Fact]
    public void OffPaletteColorFailsWithPixelCoordinates()
    {
        byte[] pixels = new byte[4 * 3 * 3];
        for (int i = 0; i < 12; i++)
        {
            WriteRgb(pixels, i, Master(5));
        }
        WriteRgb(pixels, 1 * 4 + 2, 0x123456);  // pixel (2,1) is not a palette color
        byte[] png = PngBuilder.Build(4, 3, colorType: 2, pixels);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 4, 3, "gfx.png"));
        Assert.Contains("pixel (2,1)", e.Message);
        Assert.Contains("#123456", e.Message);
        Assert.Contains("gfx.png", e.Message);
    }

    [Fact]
    public void SecretPaletteColorsAreRejectedInGfx()
    {
        // Only the 16 visible colors are legal cart art; secret twins (16-31) are not.
        byte[] pixels = new byte[3];
        WriteRgb(pixels, 0, Master(23));
        byte[] png = PngBuilder.Build(1, 1, colorType: 2, pixels);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 1, 1, "gfx.png"));
        Assert.Contains("pixel (0,0)", e.Message);
    }

    [Fact]
    public void PartialAlphaFailsWithCoordinates()
    {
        byte[] pixels = new byte[4];
        WriteRgba(pixels, 0, Master(7), 128);
        byte[] png = PngBuilder.Build(1, 1, colorType: 6, pixels);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 1, 1, "gfx.png"));
        Assert.Contains("pixel (0,0)", e.Message);
        Assert.Contains("alpha 128", e.Message);
    }

    [Fact]
    public void PaletteIndexBeyondPlteFails()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(3), Master(10));
        byte[] pixels = { 0, 5 };               // index 5 with a 2-entry PLTE
        byte[] png = PngBuilder.Build(2, 1, colorType: 3, pixels, plte);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 2, 1, "gfx.png"));
        Assert.Contains("palette index 5", e.Message);
        Assert.Contains("(1,0)", e.Message);
    }

    [Fact]
    public void WrongDimensionsFail()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(0));
        byte[] png = PngBuilder.Build(3, 3, colorType: 3, new byte[9], plte);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 4, 4, "gfx.png"));
        Assert.Contains("3x3", e.Message);
        Assert.Contains("4x4", e.Message);
    }

    [Fact]
    public void BadSignatureFails()
    {
        var e = Assert.Throws<CartLoadException>(
            () => PngDecoder.DecodeToPaletteIndices(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 4, 4, "gfx.png"));
        Assert.Contains("not a PNG", e.Message);
    }

    [Fact]
    public void TruncatedStreamFails()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(0));
        byte[] png = PngBuilder.Build(2, 2, colorType: 3, new byte[4], plte);
        byte[] cut = png.AsSpan(0, png.Length - 16).ToArray(); // drop IEND and part of IDAT's CRC
        Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(cut, 2, 2, "gfx.png"));
    }

    [Fact]
    public void CorruptIdatFails()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(0));
        byte[] png = PngBuilder.Build(2, 2, colorType: 3, new byte[4], plte);
        // Find the IDAT chunk and flip bytes inside its zlib payload.
        int idat = FindChunk(png, "IDAT");
        png[idat + 8 + 2] ^= 0xFF;
        png[idat + 8 + 3] ^= 0xFF;
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 2, 2, "gfx.png"));
        Assert.Contains("corrupt or truncated", e.Message);
    }

    [Fact]
    public void UnknownScanlineFilterFails()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(0));
        byte[] png = PngBuilder.Build(2, 2, colorType: 3, new byte[4], plte, rowFilters: new[] { 0, 7 });
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 2, 2, "gfx.png"));
        Assert.Contains("filter 7", e.Message);
        Assert.Contains("row 1", e.Message);
    }

    [Fact]
    public void IndexedWithoutPlteFails()
    {
        byte[] png = PngBuilder.Build(2, 2, colorType: 3, new byte[4]);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 2, 2, "gfx.png"));
        Assert.Contains("no PLTE", e.Message);
    }

    [Fact]
    public void InterlacedPngFails()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(0));
        byte[] png = PngBuilder.Build(2, 2, colorType: 3, new byte[4], plte);
        int ihdr = FindChunk(png, "IHDR");
        png[ihdr + 8 + 12] = 1;                 // interlace method: Adam7
        FixChunkCrc(png, ihdr);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 2, 2, "gfx.png"));
        Assert.Contains("interlaced", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonEightBitDepthFails()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(0));
        byte[] png = PngBuilder.Build(2, 2, colorType: 3, new byte[4], plte);
        int ihdr = FindChunk(png, "IHDR");
        png[ihdr + 8 + 8] = 4;                  // bit depth 4
        FixChunkCrc(png, ihdr);
        var e = Assert.Throws<CartLoadException>(() => PngDecoder.DecodeToPaletteIndices(png, 2, 2, "gfx.png"));
        Assert.Contains("bit depth 4", e.Message);
    }

    [Fact]
    public void AncillaryChunksAreIgnored()
    {
        byte[] plte = PngBuilder.PlteFromRgb(Master(3));
        byte[] png = PngBuilder.Build(1, 1, colorType: 3, new byte[] { 0 }, plte);
        // Splice a tEXt chunk between IHDR and PLTE.
        int plteOffset = FindChunk(png, "PLTE");
        using var stream = new MemoryStream();
        stream.Write(png, 0, plteOffset);
        PngBuilder.WriteChunk(stream, "tEXt", "Comment\0quarp"u8);
        stream.Write(png, plteOffset, png.Length - plteOffset);
        byte[] decoded = PngDecoder.DecodeToPaletteIndices(stream.ToArray(), 1, 1, "gfx.png");
        Assert.Equal(new byte[] { 3 }, decoded);
    }

    // --- helpers ---

    private static void WriteRgb(byte[] pixels, int pixelIndex, uint rgb)
    {
        pixels[pixelIndex * 3] = (byte)(rgb >> 16);
        pixels[pixelIndex * 3 + 1] = (byte)(rgb >> 8);
        pixels[pixelIndex * 3 + 2] = (byte)rgb;
    }

    private static void WriteRgba(byte[] pixels, int pixelIndex, uint rgb, byte alpha)
    {
        pixels[pixelIndex * 4] = (byte)(rgb >> 16);
        pixels[pixelIndex * 4 + 1] = (byte)(rgb >> 8);
        pixels[pixelIndex * 4 + 2] = (byte)rgb;
        pixels[pixelIndex * 4 + 3] = alpha;
    }

    /// <summary>Offset of the chunk's length field within the PNG byte stream.</summary>
    private static int FindChunk(byte[] png, string type)
    {
        int offset = 8;
        while (offset + 8 <= png.Length)
        {
            int length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            string name = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (name == type)
            {
                return offset;
            }
            offset += 12 + length;
        }
        throw new InvalidOperationException($"chunk {type} not found");
    }

    private static void FixChunkCrc(byte[] png, int chunkOffset)
    {
        int length = (png[chunkOffset] << 24) | (png[chunkOffset + 1] << 16)
            | (png[chunkOffset + 2] << 8) | png[chunkOffset + 3];
        uint crc = PngBuilder.Crc32(png.AsSpan(chunkOffset + 4, 4 + length));
        png[chunkOffset + 8 + length] = (byte)(crc >> 24);
        png[chunkOffset + 8 + length + 1] = (byte)(crc >> 16);
        png[chunkOffset + 8 + length + 2] = (byte)(crc >> 8);
        png[chunkOffset + 8 + length + 3] = (byte)crc;
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// PngEncoder against its two contracts (M9 stage 2, sprite editor save path):
/// pixel-exact round trip through the real <see cref="PngDecoder"/>, and byte-exact
/// determinism — pinned by a golden SHA-256, cross-checked by an independent CRC
/// implementation (<see cref="PngBuilder"/>) and by the runtime's own zlib verifying
/// the Adler-32 trailer.
/// </summary>
public class PngEncoderTests
{
    // --- fixtures ---

    /// <summary>
    /// 128x128 sheet exercising all 16 visible colors: an 8x8-pixel checkers grid whose tile
    /// color cycles through the whole palette. This is the sheet behind the golden SHA-256.
    /// </summary>
    private static byte[] GoldenSheet()
    {
        byte[] sheet = new byte[CartData.GfxWidth * CartData.GfxHeight];
        for (int y = 0; y < CartData.GfxHeight; y++)
        {
            for (int x = 0; x < CartData.GfxWidth; x++)
            {
                sheet[y * CartData.GfxWidth + x] = (byte)((x / 8 + (y / 8) * 16) % 16);
            }
        }
        return sheet;
    }

    /// <summary>Deterministic LCG noise (Numerical Recipes constants) — worst case for any
    /// accidental content-dependence in the encoder, reproducible on every run.</summary>
    private static byte[] NoiseSheet(int width, int height, uint seed)
    {
        byte[] sheet = new byte[width * height];
        uint state = seed;
        for (int i = 0; i < sheet.Length; i++)
        {
            state = state * 1664525u + 1013904223u;
            sheet[i] = (byte)((state >> 24) % 16);
        }
        return sheet;
    }

    private static byte[] RoundTrip(byte[] sheet, int width, int height)
    {
        byte[] png = PngEncoder.EncodeFromPaletteIndices(sheet, width, height);
        return PngDecoder.DecodeToPaletteIndices(png, width, height, "gfx.png");
    }

    // --- pixel round trip ---

    [Fact]
    public void AllSixteenVisibleColorsRoundTrip()
    {
        byte[] sheet = GoldenSheet();
        Assert.Equal(sheet, RoundTrip(sheet, CartData.GfxWidth, CartData.GfxHeight));
    }

    [Fact]
    public void NoiseSheetRoundTrips()
    {
        byte[] sheet = NoiseSheet(CartData.GfxWidth, CartData.GfxHeight, seed: 0xC0FFEE);
        Assert.Equal(sheet, RoundTrip(sheet, CartData.GfxWidth, CartData.GfxHeight));
    }

    [Fact]
    public void SheetLargerThanOneStoredBlockRoundTrips()
    {
        // 521 bytes/scanline x 200 rows = 104200 raw bytes > 65535: the zlib stream must
        // split into two stored blocks, and the split must be invisible to the decoder.
        byte[] sheet = NoiseSheet(520, 200, seed: 7);
        Assert.Equal(sheet, RoundTrip(sheet, 520, 200));
    }

    /// <summary>
    /// The demo carts through the real loader: decode(encode(decode(f))) == decode(f) for the
    /// sheet of every cartridge in carts/. As of M9 stage 2 no demo ships a gfx.png at all —
    /// their art is code-generated (the M4 crutches this editor replaces) — so today this pins
    /// the absent-asset all-zero sheet through CartSource; the day a demo gains a real
    /// gfx.png, the same enumeration covers its actual pixels with no test edit.
    /// </summary>
    [Fact]
    public void EveryDemoCartSheetRoundTrips()
    {
        string carts = FindCartsRoot();
        int found = 0;
        foreach (string folder in Directory.GetDirectories(carts).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(folder, "manifest.json")))
            {
                continue;
            }
            found++;
            byte[] sheet = CartSource.Load(folder).Gfx;
            byte[] png = PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight);
            Assert.Equal(sheet, PngDecoder.DecodeToPaletteIndices(png, CartData.GfxWidth, CartData.GfxHeight, "gfx.png"));
            Assert.Equal(png, PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight));
        }
        // The proof is only as strong as the enumeration: six demos exist, an empty scan
        // (moved folder, wrong root) must fail loudly instead of vacuously passing.
        Assert.True(found >= 6, $"expected at least the 6 demo carts, found {found} in {carts}");
    }

    // --- byte determinism ---

    [Fact]
    public void EncodingTwiceProducesIdenticalBytes()
    {
        byte[] sheet = NoiseSheet(CartData.GfxWidth, CartData.GfxHeight, seed: 42);
        Assert.Equal(
            PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight),
            PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight));
    }

    /// <summary>
    /// The canary for "determinism by construction": the exact bytes of one known sheet,
    /// pinned. Round-trip tests cannot see byte drift that still decodes to the same pixels
    /// (a different-but-valid zlib header, a re-filtered scanline, an extra ancillary chunk) —
    /// this hash can. It may only move with a deliberate encoder change, never with a runtime
    /// or OS upgrade; if it fires on its own, the encoder has grown a dependency on somebody
    /// else's bytes and that is a stop-the-line bug, not a re-pin.
    /// </summary>
    [Fact]
    public void GoldenSheetBytesArePinned()
    {
        byte[] png = PngEncoder.EncodeFromPaletteIndices(GoldenSheet(), CartData.GfxWidth, CartData.GfxHeight);
        Assert.Equal(
            "AEC7DB9ED7DD9F2D121A2CCDEE95FA15709DE6D6EE9C2CFF40BAA4F34110E0B8",
            Convert.ToHexString(SHA256.HashData(png)));
    }

    // --- the file beyond our own decoder (it skips CRCs and the zlib trailer) ---

    [Fact]
    public void ChunkLayoutAndCrcsVerifyAgainstIndependentChecker()
    {
        // PngDecoder ignores chunk CRCs, so a CRC bug would survive every round-trip test and
        // get baked into the golden hash — yet Aseprite and image viewers would reject the
        // file. PngBuilder's CRC-32 is a separate implementation anchored to the published
        // "IEND" test vector, which makes it a real referee here, not a mirror.
        byte[] png = PngEncoder.EncodeFromPaletteIndices(GoldenSheet(), CartData.GfxWidth, CartData.GfxHeight);
        var chunkTypes = new List<string>();
        int offset = 8;
        while (offset < png.Length)
        {
            int length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            chunkTypes.Add(Encoding.ASCII.GetString(png, offset + 4, 4));
            uint stored = (uint)((png[offset + 8 + length] << 24) | (png[offset + 9 + length] << 16)
                | (png[offset + 10 + length] << 8) | png[offset + 11 + length]);
            Assert.Equal(PngBuilder.Crc32(png.AsSpan(offset + 4, 4 + length)), stored);
            offset += 12 + length;
        }
        Assert.Equal(new[] { "IHDR", "PLTE", "IDAT", "IEND" }, chunkTypes);
    }

    [Fact]
    public void IdatInflatesToFilterZeroScanlinesWithValidAdler()
    {
        // Decompress the IDAT stream with the runtime's zlib, reading to end-of-stream: that
        // forces inflate to verify the Adler-32 trailer with an implementation that is not
        // ours. (Today's PngDecoder happens to surface a bad trailer too — its buffered
        // ReadExactly reaches the trailer, proven by negative control — but that is runtime
        // buffering behavior, not a contract; CopyTo-to-EOF checks it by construction. This
        // test also diagnoses layout: the payload must be exactly filter-0 scanlines,
        // 0x00 + row pixels, so a pin failure can be localized without hex-diffing files.)
        byte[] sheet = NoiseSheet(CartData.GfxWidth, CartData.GfxHeight, seed: 99);
        byte[] png = PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight);

        using var inflated = new MemoryStream();
        using (var idat = new MemoryStream(ExtractChunk(png, "IDAT")))
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        byte[] expected = new byte[(CartData.GfxWidth + 1) * CartData.GfxHeight];
        for (int y = 0; y < CartData.GfxHeight; y++)
        {
            Array.Copy(sheet, y * CartData.GfxWidth, expected, y * (CartData.GfxWidth + 1) + 1, CartData.GfxWidth);
        }
        Assert.Equal(expected, inflated.ToArray());
    }

    // --- input validation ---

    [Fact]
    public void SecretIndexFailsWithCoordinatesAndReason()
    {
        // Master indices 16-31 are legal on screen (Pal) but not in gfx.png (SPEC-8 §6): the
        // decoder maps files onto the 16 visible colors only, so encoding a secret index
        // would produce a file our own loader rejects. Fail at save time, with the pixel.
        byte[] sheet = new byte[8 * 4];
        sheet[2 * 8 + 5] = 20;
        var e = Assert.Throws<ArgumentException>(() => PngEncoder.EncodeFromPaletteIndices(sheet, 8, 4));
        Assert.Contains("(5,2)", e.Message);
        Assert.Contains("20", e.Message);
        Assert.Contains("SPEC-8", e.Message);
    }

    [Fact]
    public void ValueBeyondMasterRangeFailsWithCoordinates()
    {
        byte[] sheet = { 0, 0, 200, 0, 0, 0 };
        var e = Assert.Throws<ArgumentException>(() => PngEncoder.EncodeFromPaletteIndices(sheet, 3, 2));
        Assert.Contains("(2,0)", e.Message);
        Assert.Contains("200", e.Message);
    }

    [Fact]
    public void LengthMismatchFails()
    {
        var e = Assert.Throws<ArgumentException>(() => PngEncoder.EncodeFromPaletteIndices(new byte[10], 4, 4));
        Assert.Contains("10", e.Message);
        Assert.Contains("4x4", e.Message);
    }

    // --- helpers ---

    private static string FindCartsRoot()
    {
        // Walk up from the test bin folder to the repo root, same as SnakeCartTests.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "carts");
            if (File.Exists(Path.Combine(candidate, "snake", "manifest.json")))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/ not found above the test directory");
    }

    private static byte[] ExtractChunk(byte[] png, string type)
    {
        int offset = 8;
        while (offset + 8 <= png.Length)
        {
            int length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == type)
            {
                return png.AsSpan(offset + 8, length).ToArray();
            }
            offset += 12 + length;
        }
        throw new InvalidOperationException($"chunk {type} not found");
    }
}

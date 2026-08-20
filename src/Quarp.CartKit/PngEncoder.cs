using System.Buffers.Binary;
using Quarp.Core;

namespace Quarp.CartKit;

/// <summary>
/// Deterministic PNG encoder for cartridge sprite sheets — the writing half of
/// <see cref="PngDecoder"/> (M9 sprite editor: a dirty session saves gfx.png through here).
///
/// <para><b>Determinism by construction is the point.</b> Same pixels must produce the same
/// bytes on every machine, architecture and runtime version, because gfx.png bytes feed the
/// cartridge identity hash and the repository diff. Every degree of freedom PNG/zlib gives an
/// encoder is therefore pinned to a constant: the scanline filter is always 0 (None — no
/// adaptive per-row heuristics); the zlib stream is hand-written as stored (uncompressed)
/// deflate blocks with a constant header and fixed segmentation — never
/// <c>System.IO.Compression</c>, whose compressed output is a property of the runtime's zlib
/// build (it already changed once when .NET swapped in zlib-ng), i.e. somebody else's bytes;
/// the chunk sequence is fixed at IHDR, PLTE, IDAT, IEND with no ancillary chunks and no tRNS;
/// and PLTE is always the 16 visible <see cref="Palette.Master32"/> colors in index order,
/// independent of which colors the sheet actually uses, so pixel bytes are the input indices
/// themselves. A 128x128 sheet stored uncompressed is ~16 KB; size is irrelevant here,
/// reproducibility is not.</para>
///
/// <para>Input is visible palette indices 0-15 only — exactly what
/// <see cref="PngDecoder.DecodeToPaletteIndices"/> accepts back: gfx.png admits only the 16
/// visible colors (SPEC-8 §6); the secret twins 16-31 exist at runtime via <c>Pal</c>, never
/// on disk. Index 0 is written as opaque ink rather than a tRNS entry, because transparency
/// is a runtime meaning of index 0 (<c>Palt</c>), not a file fact, and the decoder maps both
/// spellings to index 0 anyway.</para>
///
/// <para>CRC-32 and Adler-32 are implemented locally: the BCL ships neither (Crc32 lives in a
/// separate NuGet package) and both are pure functions of the payload, so owning them is what
/// keeps the output ours.</para>
/// </summary>
public static class PngEncoder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Encodes row-major visible palette indices (one byte per pixel, values 0-15) into a
    /// complete PNG file. Pixel-exact round trip with
    /// <see cref="PngDecoder.DecodeToPaletteIndices"/> and byte-exact reproducibility are the
    /// contract; both are pinned by tests (including a golden SHA-256 of a known sheet).
    /// </summary>
    public static byte[] EncodeFromPaletteIndices(byte[] indices, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (indices.Length != (long)width * height)
        {
            throw new ArgumentException(
                $"sheet is {indices.Length} bytes, must be exactly {width}x{height} = {(long)width * height} (one palette index per pixel).",
                nameof(indices));
        }
        for (int i = 0; i < indices.Length; i++)
        {
            byte value = indices[i];
            if (value >= Palette.VisibleCount)
            {
                int x = i % width;
                int y = i / width;
                throw new ArgumentException(
                    value < Palette.MasterCount
                        ? $"pixel ({x},{y}) is master index {value}: secret colors 16-31 live at runtime only (Pal); "
                          + "gfx.png admits just the 16 visible palette colors (SPEC-8 §6)."
                        : $"pixel ({x},{y}) is {value}, which is not a palette index (visible colors are 0-15).",
                    nameof(indices));
            }
        }

        // Raw scanline stream: one filter byte per row (always 0 = None) followed by the
        // row's indices verbatim. With filter None and 1 byte/pixel the pre-compression
        // bytes ARE the pixels, which keeps the whole file a pure function of the input.
        byte[] raw = new byte[(width + 1) * height];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(indices, y * width, raw, y * (width + 1) + 1, width);
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4), height);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 3;    // color type: indexed
        // Bytes 10-12 stay 0: compression method 0, filter method 0, no interlace.
        WriteChunk(png, "IHDR"u8, ihdr);

        // Identity palette: PLTE entry i is master color i for all 16 visible colors, whether
        // the sheet uses them or not. A usage-ordered palette would make PLTE bytes (and every
        // pixel byte) depend on scan order and color frequency — a determinism hazard for zero
        // gain at this file size.
        Span<byte> plte = stackalloc byte[Palette.VisibleCount * 3];
        for (int i = 0; i < Palette.VisibleCount; i++)
        {
            uint rgb = Palette.Master32[i];
            plte[i * 3] = (byte)(rgb >> 16);
            plte[i * 3 + 1] = (byte)(rgb >> 8);
            plte[i * 3 + 2] = (byte)rgb;
        }
        WriteChunk(png, "PLTE"u8, plte);

        WriteChunk(png, "IDAT"u8, StoredZlib(raw));
        WriteChunk(png, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    /// <summary>
    /// Writes one PNG chunk: big-endian length, 4-byte type, data, and the CRC-32 of
    /// type + data. <see cref="PngDecoder"/> skips CRCs, but external tools (Aseprite, image
    /// viewers) verify them, so a correct CRC is part of "the file opens everywhere".
    /// </summary>
    private static void WriteChunk(MemoryStream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        png.Write(word);
        png.Write(type);
        png.Write(data);
        uint crc = 0xFFFFFFFFu;
        crc = Crc32Update(crc, type);
        crc = Crc32Update(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc ^ 0xFFFFFFFFu);
        png.Write(word);
    }

    /// <summary>
    /// Wraps raw bytes in a zlib stream made of stored (BTYPE=00) deflate blocks. The header
    /// is the constant pair 0x78 0x01 (CM=8, CINFO=7, FLEVEL=fastest; 0x7801 = 31 x 991, so
    /// FCHECK holds); segmentation is a fixed min(remaining, 65535) per block; the trailer is
    /// Adler-32 of the payload, a pure function. No compressor is consulted, so no runtime
    /// upgrade can ever move these bytes.
    /// </summary>
    private static byte[] StoredZlib(ReadOnlySpan<byte> raw)
    {
        const int MaxStoredBlock = 65535;
        using var zlib = new MemoryStream();
        zlib.WriteByte(0x78);
        zlib.WriteByte(0x01);
        int offset = 0;
        do
        {
            int len = Math.Min(MaxStoredBlock, raw.Length - offset);
            bool final = offset + len == raw.Length;
            zlib.WriteByte(final ? (byte)1 : (byte)0);  // BFINAL in bit 0, BTYPE=00 (stored)
            zlib.WriteByte((byte)len);                   // LEN, little-endian per DEFLATE
            zlib.WriteByte((byte)(len >> 8));
            zlib.WriteByte((byte)~len);                  // NLEN = one's complement of LEN
            zlib.WriteByte((byte)(~len >> 8));
            zlib.Write(raw.Slice(offset, len));
            offset += len;
        } while (offset < raw.Length);
        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, Adler32(raw));
        zlib.Write(adler);
        return zlib.ToArray();
    }

    /// <summary>
    /// zlib Adler-32 (RFC 1950) — the checksum inflate verifies at end of stream. Per-byte
    /// modulo instead of the deferred-mod optimization: a sheet is ~16 KB and the naive form
    /// is the one whose correctness is visible at a glance.
    /// </summary>
    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint Mod = 65521; // largest prime below 2^16, per RFC 1950
        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % Mod;
            b = (b + a) % Mod;
        }
        return (b << 16) | a;
    }

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        // Standard PNG CRC-32 (reflected polynomial 0xEDB88320). The tests verify chunk CRCs
        // with an independent implementation anchored to the published "IEND" vector, so a
        // slip here cannot pin itself into the golden hash unnoticed.
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
}

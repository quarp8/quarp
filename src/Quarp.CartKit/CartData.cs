namespace Quarp.CartKit;

/// <summary>
/// Everything loaded from a cartridge folder or a .quarp8 package, validated against the
/// profile-8 limits. Missing optional assets come back as all-zero arrays (Format spec v1:
/// absent assets = zeros), so consumers never see null.
/// </summary>
public sealed class CartData
{
    public const int GfxWidth = 128;
    public const int GfxHeight = 128;
    public const int MapWidth = 256;
    public const int MapHeight = 72;
    public const int FlagCount = 256;

    public required CartManifest Manifest { get; init; }

    /// <summary>Sources sorted by relative path (ordinal) — a deterministic compilation input order.</summary>
    public required IReadOnlyList<CartSourceFile> Sources { get; init; }

    /// <summary>Sprite sheet: 128x128 palette indices 0-15, row-major.</summary>
    public required byte[] Gfx { get; init; }

    /// <summary>Map: 256x72 tile bytes, row-major.</summary>
    public required byte[] Map { get; init; }

    /// <summary>Sprite flags: 8 flag bits per sprite, 256 sprites.</summary>
    public required byte[] Flags { get; init; }

    /// <summary>
    /// The SFX bank as the console consumes it: <see cref="AudioFormat.SfxPayloadSize"/> bytes,
    /// 64 slot headers followed by 64 x 32 step words, with the file header of <c>sfx.bin</c>
    /// already stripped and every field already validated (docs/AUDIO-FORMAT.md §2).
    ///
    /// <para>Not <c>required</c>, unlike the graphics: the default is an all-zero bank, which is
    /// exactly "64 empty slots" — a cart without audio is silent, not broken, and every existing
    /// construction site keeps meaning what it meant.</para>
    /// </summary>
    public byte[] Sfx { get; init; } = AudioFormat.EmptySfxPayload();

    /// <summary>
    /// The music bank as the console consumes it: <see cref="AudioFormat.MusicPayloadSize"/> bytes,
    /// 64 x 4 channel bytes followed by 64 pattern flag bytes (docs/AUDIO-FORMAT.md §4). All-zero
    /// means every pattern is empty, i.e. no music.
    /// </summary>
    public byte[] Music { get; init; } = AudioFormat.EmptyMusicPayload();

    /// <summary>Highest data-bank number a cartridge may carry (ADR-035): <c>data/00.bin</c>
    /// through <c>data/63.bin</c>.</summary>
    public const int DataBankCount = 64;

    /// <summary>Largest single data bank, in bytes (ADR-035).</summary>
    public const int DataBankMaxBytes = 1024 * 1024;

    /// <summary>Largest sum of all data banks, in bytes (ADR-035).</summary>
    public const int DataBanksMaxTotalBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The data banks of ADR-035: <see cref="DataBankCount"/> blobs of arbitrary bytes the
    /// cartridge carries and reads through <c>DataGet</c>/<c>DataToGfx</c>/<c>DataToMap</c>.
    ///
    /// <para>Always exactly <see cref="DataBankCount"/> entries; an absent bank is a zero-length
    /// array, never null — the "missing asset = zeros" rule of the format spec, spelled for a
    /// blob whose natural zero is emptiness rather than a field of zero bytes.</para>
    /// </summary>
    public IReadOnlyList<byte[]> DataBanks { get; init; } = EmptyDataBanks();

    /// <summary>A full set of empty banks — what a cartridge without <c>data/**</c> gets.</summary>
    public static byte[][] EmptyDataBanks()
    {
        var banks = new byte[DataBankCount][];
        for (int i = 0; i < DataBankCount; i++)
        {
            banks[i] = Array.Empty<byte>();
        }
        return banks;
    }
}

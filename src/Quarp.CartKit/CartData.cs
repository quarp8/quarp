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
}

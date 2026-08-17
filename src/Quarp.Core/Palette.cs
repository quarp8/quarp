namespace Quarp.Core;

/// <summary>
/// Master palette: 16 visible colors (0-15) + their 16 "secret" counterparts (16-31).
/// Model is frozen (ADR-016); the hex values below are draft v1, ratified at M4 (SPEC-8 §2).
/// </summary>
public static class Palette
{
    public const int VisibleCount = 16;
    public const int MasterCount = 32;

    private static readonly uint[] _master32 =
    {
        // Visible 0-15: ink, gray, light gray, white, blue, sky, teal, green,
        // yellow, orange, red, pink, purple, brown, tan, light skin.
        0x1d1626, 0x6e7b8f, 0xb9c2cf, 0xf7f3e8,
        0x2c3e8c, 0x46a1e0, 0x2f9e8f, 0x55c04b,
        0xf7d94c, 0xef9433, 0xd8434e, 0xea6bb0,
        0x8348a8, 0x8a5535, 0xcf9367, 0xf3cfa7,
        // Secret 16-31: darker/lighter counterparts of the visible slot (index - 16).
        0x383052, 0x47536b, 0x8b96a5, 0xddd4c2,
        0x1a2554, 0x33719f, 0x1d6259, 0x2e7434,
        0xc7a02e, 0xb4611f, 0x7e2937, 0xa34a82,
        0x542d75, 0x55301d, 0xa5714a, 0xffe9d1,
    };

    /// <summary>All 32 colors as 0xRRGGBB.</summary>
    public static ReadOnlySpan<uint> Master32 => _master32;
}

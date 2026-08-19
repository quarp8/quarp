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
        // Secret 16-31: paired shades of the visible slot (index - 16). v2 hue-shift pass
        // (M4 palette grill, 2026-08-19): eleven twins keep their exact lightness (the dim
        // contrast every game relies on) but their hue now travels the way hand-shaded
        // pixel art travels -- cool colors' shadows lean toward blue-violet, warm colors'
        // shadows walk the wheel toward rust/wine/plum. Unchanged on purpose: 16 (twilight
        // lift of near-black -- there is no darker), 19 parchment / 29 umber / 30 clay (the
        // spec demands WARM darks and paper/skin shadows are classically warm), 31 peach
        // (the warm highlight of light skin, the palette's fourth skin tone).
        0x383052, 0x424b70, 0x8591ab, 0xddd4c2,
        0x191b55, 0x3262a0, 0x1d5e62, 0x2d753c,
        0xcb952a, 0xb4561f, 0x7f2842, 0xa6478e,
        0x4d2c76, 0x55301d, 0xa5714a, 0xffe9d1,
    };

    /// <summary>All 32 colors as 0xRRGGBB.</summary>
    public static ReadOnlySpan<uint> Master32 => _master32;
}

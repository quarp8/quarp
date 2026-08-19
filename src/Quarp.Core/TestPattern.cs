namespace Quarp.Core;

/// <summary>
/// The M0 boot image: all 32 master colors plus the ramps the palette was designed around
/// (neutral 8-step, blue 4-step, skin/wood 6-step). Doubles as a visual check that
/// integer scaling and the palette pipeline work — which only holds if the image covers the
/// screen it was handed, so it is laid out from the framebuffer's size and paints every pixel
/// of any profile (no <c>Clear</c> is needed, and TestPatternTests holds the two far edges).
/// </summary>
public static class TestPattern
{
    private static readonly byte[] NeutralRamp = { 0, 16, 17, 1, 18, 2, 19, 3 };
    private static readonly byte[] BlueRamp = { 20, 4, 21, 5 };
    private static readonly byte[] SkinWoodRamp = { 29, 13, 30, 14, 15, 31 };

    public static void Render(Framebuffer fb)
    {
        // Everything below is a fraction of the framebuffer's own size, never a literal 128 or
        // 72. This image is the first thing a new console shows, so it is also the first thing
        // that would lie about one: a layout pinned to the old screen would paint an L-shaped
        // black margin along the right and bottom edges of a bigger console and still look
        // deliberate. Blocks are cut as "start of block i" to "start of block i+1", the split
        // that tiles a width exactly whether or not it divides evenly.
        //
        // Four bands: the 32 master colors take the top two thirds (visible over secret, 3:2),
        // and the designed ramps share the bottom third. On 128x72 those fractions come out at
        // rows 0-27 / 28-47 / 48-59 / 60-71 — the M0 image, pixel for pixel.
        int colorsEnd = fb.Height * 2 / 3;
        int visibleEnd = colorsEnd * 3 / 5;
        int neutralEnd = colorsEnd + (fb.Height - colorsEnd) / 2;

        // The 16 visible colors, each with its secret counterpart directly underneath: same
        // column bounds for both rows, which is what makes the pair readable as a pair.
        for (int i = 0; i < 16; i++)
        {
            int x = i * fb.Width / 16;
            int next = (i + 1) * fb.Width / 16;
            fb.FillRect(x, 0, next - x, visibleEnd, (byte)i);
            fb.FillRect(x, visibleEnd, next - x, colorsEnd - visibleEnd, (byte)(i + 16));
        }

        // Neutral ramp: 8 blocks across the full width.
        FillRamp(fb, NeutralRamp, 0, fb.Width, colorsEnd, neutralEnd);

        // Bottom band: blue ramp on the left half, skin/wood ramp on the right half.
        int half = fb.Width / 2;
        FillRamp(fb, BlueRamp, 0, half, neutralEnd, fb.Height);
        FillRamp(fb, SkinWoodRamp, half, fb.Width, neutralEnd, fb.Height);
    }

    /// <summary>
    /// Paints one ramp as equal-ish blocks filling [<paramref name="left"/>,
    /// <paramref name="right"/>) x [<paramref name="top"/>, <paramref name="bottom"/>) with no
    /// gap and no overshoot: block edges are computed from the span, so the last block always
    /// ends exactly on <paramref name="right"/> even when the span is not divisible by the ramp
    /// length (160 px over 6 skin/wood steps, say).
    /// </summary>
    private static void FillRamp(Framebuffer fb, byte[] ramp, int left, int right, int top, int bottom)
    {
        int span = right - left;
        for (int i = 0; i < ramp.Length; i++)
        {
            int x = left + i * span / ramp.Length;
            int next = left + (i + 1) * span / ramp.Length;
            fb.FillRect(x, top, next - x, bottom - top, ramp[i]);
        }
    }
}

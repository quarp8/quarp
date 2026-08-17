namespace Quarp.Core;

/// <summary>
/// The M0 boot image: all 32 master colors plus the ramps the palette was designed around
/// (neutral 8-step, blue 4-step, skin/wood 6-step). Doubles as a visual check that
/// integer scaling and the palette pipeline work.
/// </summary>
public static class TestPattern
{
    private static readonly byte[] NeutralRamp = { 0, 16, 17, 1, 18, 2, 19, 3 };
    private static readonly byte[] BlueRamp = { 20, 4, 21, 5 };
    private static readonly byte[] SkinWoodRamp = { 29, 13, 30, 14, 15, 31 };

    public static void Render(Framebuffer fb)
    {
        fb.Clear(0);

        // Rows 0-27: the 16 visible colors, 8 px per column.
        for (int i = 0; i < 16; i++)
        {
            fb.FillRect(i * 8, 0, 8, 28, (byte)i);
        }

        // Rows 28-47: their secret counterparts, aligned under the parents.
        for (int i = 0; i < 16; i++)
        {
            fb.FillRect(i * 8, 28, 8, 20, (byte)(i + 16));
        }

        // Rows 48-59: neutral ramp, 8 blocks of 16 px.
        for (int i = 0; i < NeutralRamp.Length; i++)
        {
            fb.FillRect(i * 16, 48, 16, 12, NeutralRamp[i]);
        }

        // Rows 60-71: blue ramp (left half) and skin/wood ramp (right half).
        for (int i = 0; i < BlueRamp.Length; i++)
        {
            fb.FillRect(i * 16, 60, 16, 12, BlueRamp[i]);
        }
        for (int i = 0; i < SkinWoodRamp.Length; i++)
        {
            int x = 64 + i * 64 / SkinWoodRamp.Length;
            int next = 64 + (i + 1) * 64 / SkinWoodRamp.Length;
            fb.FillRect(x, 60, next - x, 12, SkinWoodRamp[i]);
        }
    }
}

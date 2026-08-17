namespace Quarp.Api;

/// <summary>
/// Deterministic cartridge math on <see cref="Fix"/>: table-driven Sin/Cos/Atan2 and an
/// integer Sqrt. Pure integer arithmetic — results are bit-identical on every platform
/// (SPEC-8 §7). Angles are in turns: 1.0 = full circle, 0.25 = quarter turn (ADR-014).
/// Orientation is mathematical: Sin(0.25) = 1 and angles grow counterclockwise with +y up;
/// on screen +y points down, so the same rotation looks clockwise.
/// Porting from PICO-8: negate its sin (PICO-8 flips the sine for screen space), cos matches.
/// </summary>
public static partial class SMath
{
    // One turn = 65536 raw units; one quadrant = 16384 raw = 1024 sine nodes of 16 raw each.
    private const int QuarterRaw = 1 << 14;

    /// <summary>
    /// Sine of an angle in turns; whole turns wrap away, only the fractional part matters.
    /// Quarter-wave table (1024 nodes + guard) with linear interpolation; the other quadrants
    /// are exact mirrors, so Sin(0) = 0, Sin(0.25) = 1, Sin(0.5) = 0, Sin(0.75) = -1 exactly.
    /// Measured max error ~2.2e-5 (ADR-014).
    /// </summary>
    public static Fix Sin(Fix turns)
    {
        int phase = turns.Raw & 0xFFFF;
        int pos = phase & (QuarterRaw - 1);
        return (phase >> 14) switch
        {
            0 => Fix.FromRaw(QuarterSin(pos)),
            1 => Fix.FromRaw(QuarterSin(QuarterRaw - pos)),
            2 => Fix.FromRaw(-QuarterSin(pos)),
            _ => Fix.FromRaw(-QuarterSin(QuarterRaw - pos)),
        };
    }

    /// <summary>Cosine of an angle in turns: Cos(t) = Sin(t + 0.25). Cos(0) = 1 exactly.</summary>
    public static Fix Cos(Fix turns) => Sin(Fix.FromRaw(unchecked(turns.Raw + QuarterRaw)));

    /// <summary>
    /// Square root, rounded down to the nearest representable value.
    /// Negative input returns 0 — soft and deterministic (draft until API-8.md is ratified).
    /// </summary>
    public static Fix Sqrt(Fix value)
    {
        if (value.Raw <= 0)
        {
            return Fix.Zero;
        }
        // sqrt(raw / 65536) * 65536 == isqrt(raw * 65536): one integer sqrt, no rescaling error.
        return Fix.FromRaw((int)IntegerSqrt((ulong)value.Raw << Fix.FracBits));
    }

    /// <summary>
    /// Angle of the vector (x, y) in turns, in [0, 1): 0 points along +x, 0.25 along +y.
    /// Octant reduction + 256-node atan table with interpolation; error well under 1e-3 turns.
    /// Atan2(0, 0) = 0 by definition (draft until API-8.md is ratified).
    /// </summary>
    public static Fix Atan2(Fix y, Fix x)
    {
        long dy = y.Raw;
        long dx = x.Raw;
        if (dy == 0 && dx == 0)
        {
            return Fix.Zero;
        }
        long ay = dy < 0 ? -dy : dy;
        long ax = dx < 0 ? -dx : dx;
        int octant = ay <= ax
            ? AtanUnit((int)((ay << 16) / ax))
            : QuarterRaw - AtanUnit((int)((ax << 16) / ay));
        int angle = dx >= 0
            ? (dy >= 0 ? octant : 4 * QuarterRaw - octant)
            : (dy >= 0 ? 2 * QuarterRaw - octant : 2 * QuarterRaw + octant);
        return Fix.FromRaw(angle & 0xFFFF);
    }

    /// <summary>Piecewise-linear quarter sine: pos in [0, 16384] raw turn units, result in Q16.16.</summary>
    private static int QuarterSin(int pos)
    {
        int index = pos >> 4;
        int frac = pos & 0xF;
        int value = _sinQuarter[index];
        if (frac != 0)
        {
            value += ((_sinQuarter[index + 1] - value) * frac) >> 4;
        }
        return value;
    }

    /// <summary>Piecewise-linear atan: ratio in [0, 65536] (Q16 of min/max), result in [0, 8192] raw turns.</summary>
    private static int AtanUnit(int ratio)
    {
        int index = ratio >> 8;
        int frac = ratio & 0xFF;
        int value = _atanUnit[index];
        if (frac != 0)
        {
            value += ((_atanUnit[index + 1] - value) * frac) >> 8;
        }
        return value;
    }

    /// <summary>Floor of the square root, digit-by-digit method — plain integer ops only.</summary>
    private static uint IntegerSqrt(ulong value)
    {
        ulong result = 0;
        ulong bit = 1UL << 62;
        while (bit > value)
        {
            bit >>= 2;
        }
        while (bit != 0)
        {
            if (value >= result + bit)
            {
                value -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }
        return (uint)result;
    }
}

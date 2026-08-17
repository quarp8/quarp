namespace Quarp.Api;

/// <summary>
/// Deterministic Q16.16 fixed-point number — the only fractional type allowed in cartridge code.
/// Range [-32768; +32768), step 1/65536. Pure integer arithmetic: bit-identical on every
/// platform and runtime (SPEC-8 §7).
/// Overflow wraps (unchecked), division by zero throws — both deterministic; final semantics
/// are ratified in API-8.md.
/// </summary>
public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
{
    /// <summary>Number of fractional bits (16): the value is stored as <c>value * 65536</c>.</summary>
    public const int FracBits = 16;

    /// <summary>Raw representation of 1.0 (65536) — the scale factor of the format.</summary>
    public const int RawOne = 1 << FracBits;

    /// <summary>The value 0.</summary>
    public static readonly Fix Zero = default;

    /// <summary>The value 1.</summary>
    public static readonly Fix One = FromRaw(RawOne);

    /// <summary>The value 0.5, exactly representable.</summary>
    public static readonly Fix Half = FromRaw(RawOne / 2);

    /// <summary>Smallest representable value, -32768.</summary>
    public static readonly Fix MinValue = FromRaw(int.MinValue);

    /// <summary>Largest representable value, one step below +32768.</summary>
    public static readonly Fix MaxValue = FromRaw(int.MaxValue);

    /// <summary>The underlying integer (value multiplied by 65536): the shape stored in saves and replays.</summary>
    public int Raw { get; }

    private Fix(int raw) => Raw = raw;

    /// <summary>Wraps a raw Q16.16 integer without scaling: <c>Fix.FromRaw(65536)</c> is 1.</summary>
    public static Fix FromRaw(int raw) => new(raw);

    /// <summary>
    /// Exact fraction, e.g. <c>Fix.Ratio(1, 2)</c> = 0.5. The blessed way to write fractional
    /// constants. Truncates toward zero when the fraction is not representable; a zero
    /// denominator throws <see cref="DivideByZeroException"/>.
    /// </summary>
    public static Fix Ratio(int numerator, int denominator) =>
        FromRaw((int)(((long)numerator << FracBits) / denominator));

    /// <summary>Whole numbers convert implicitly; magnitudes of 32768 and above wrap.</summary>
    public static implicit operator Fix(int value) => FromRaw(value << FracBits);

    /// <summary>Floors toward negative infinity (arithmetic shift): -0.5 becomes -1, not 0.</summary>
    public static explicit operator int(Fix value) => value.Raw >> FracBits;

    /// <summary>Sum; overflow wraps around the range instead of throwing (deterministic).</summary>
    public static Fix operator +(Fix a, Fix b) => FromRaw(unchecked(a.Raw + b.Raw));

    /// <summary>Difference; overflow wraps around the range instead of throwing (deterministic).</summary>
    public static Fix operator -(Fix a, Fix b) => FromRaw(unchecked(a.Raw - b.Raw));

    /// <summary>Negation; negating <see cref="MinValue"/> wraps back to itself.</summary>
    public static Fix operator -(Fix a) => FromRaw(unchecked(-a.Raw));

    /// <summary>
    /// Product through a 64-bit intermediate, so only the final result can overflow (and wraps).
    /// Extra fractional bits are dropped toward negative infinity.
    /// </summary>
    public static Fix operator *(Fix a, Fix b) => FromRaw((int)(((long)a.Raw * b.Raw) >> FracBits));

    /// <summary>
    /// Quotient through a 64-bit intermediate, truncated toward zero; overflow wraps.
    /// Division by zero throws <see cref="DivideByZeroException"/> — loud beats silently wrong.
    /// </summary>
    public static Fix operator /(Fix a, Fix b) => FromRaw((int)(((long)a.Raw << FracBits) / b.Raw));

    /// <summary>True when both values have the same raw representation.</summary>
    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;

    /// <summary>True when the raw representations differ.</summary>
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;

    /// <summary>Less than.</summary>
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;

    /// <summary>Greater than.</summary>
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

    /// <summary>Exact comparison of the raw representations — no epsilon, values are exact.</summary>
    public bool Equals(Fix other) => Raw == other.Raw;

    /// <summary>Equal only to another <see cref="Fix"/> with the same raw representation.</summary>
    public override bool Equals(object? obj) => obj is Fix other && Equals(other);

    /// <summary>Hash of the raw representation; stable across runs and platforms.</summary>
    public override int GetHashCode() => Raw;

    /// <summary>Orders by numeric value: negative, zero or positive as usual.</summary>
    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

    /// <summary>
    /// Decimal text with up to five fractional digits and trailing zeros trimmed
    /// ("0.5", "-1.5", "3"). Digits are produced by integer arithmetic, so the text is
    /// invariant by construction: no culture and no floating point anywhere (SPEC-8 §7).
    /// </summary>
    public override string ToString()
    {
        // The sign is peeled off first, in long, so |int.MinValue| still fits.
        long magnitude = Raw < 0 ? -(long)Raw : Raw;
        long integerPart = magnitude >> FracBits;
        // Five decimal digits of the fraction, rounded half away from zero (RawOne / 2 is half
        // of the last digit's weight). The largest possible fraction, 65535/65536, rounds to
        // 99998 — it can never carry into the whole part, so there is no carry to handle.
        long fraction = (((magnitude & (RawOne - 1)) * 100000) + (RawOne / 2)) >> FracBits;

        // Digits are written back to front; the longest result, "-32767.99998", is 12 chars.
        // Cleared right away — CODESTYLE forbids reading a partially initialized stack buffer.
        Span<char> buffer = stackalloc char[16];
        buffer.Clear();
        int start = buffer.Length;

        if (fraction != 0)
        {
            int digits = 5;
            while (fraction % 10 == 0)
            {
                fraction /= 10;
                digits--;
            }
            for (int i = 0; i < digits; i++)
            {
                buffer[--start] = (char)('0' + (int)(fraction % 10));
                fraction /= 10;
            }
            buffer[--start] = '.';
        }

        do
        {
            buffer[--start] = (char)('0' + (int)(integerPart % 10));
            integerPart /= 10;
        }
        while (integerPart != 0);

        if (Raw < 0)
        {
            buffer[--start] = '-';
        }
        return new string(buffer[start..]);
    }
}

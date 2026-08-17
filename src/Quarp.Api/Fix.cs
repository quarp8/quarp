using System.Globalization;

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
    public const int FracBits = 16;
    public const int RawOne = 1 << FracBits;

    public static readonly Fix Zero = default;
    public static readonly Fix One = FromRaw(RawOne);
    public static readonly Fix Half = FromRaw(RawOne / 2);
    public static readonly Fix MinValue = FromRaw(int.MinValue);
    public static readonly Fix MaxValue = FromRaw(int.MaxValue);

    public int Raw { get; }

    private Fix(int raw) => Raw = raw;

    public static Fix FromRaw(int raw) => new(raw);

    /// <summary>Exact fraction, e.g. <c>Fix.Ratio(1, 2)</c> = 0.5. The blessed way to write fractional constants.</summary>
    public static Fix Ratio(int numerator, int denominator) =>
        FromRaw((int)(((long)numerator << FracBits) / denominator));

    public static implicit operator Fix(int value) => FromRaw(value << FracBits);

    /// <summary>Floors toward negative infinity (arithmetic shift).</summary>
    public static explicit operator int(Fix value) => value.Raw >> FracBits;

    public static Fix operator +(Fix a, Fix b) => FromRaw(unchecked(a.Raw + b.Raw));
    public static Fix operator -(Fix a, Fix b) => FromRaw(unchecked(a.Raw - b.Raw));
    public static Fix operator -(Fix a) => FromRaw(unchecked(-a.Raw));
    public static Fix operator *(Fix a, Fix b) => FromRaw((int)(((long)a.Raw * b.Raw) >> FracBits));
    public static Fix operator /(Fix a, Fix b) => FromRaw((int)(((long)a.Raw << FracBits) / b.Raw));

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix other && Equals(other);
    public override int GetHashCode() => Raw;
    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

    public override string ToString() =>
        (Raw / 65536.0).ToString("0.#####", CultureInfo.InvariantCulture);
}

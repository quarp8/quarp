using System.Globalization;

namespace Quarp.Core;

/// <summary>
/// The one framebuffer fingerprint in Quarp: FNV-1a 64, printed as exactly 16 lowercase hex
/// digits. Everything that quotes a frame hash — <c>quarp sim</c>, <c>quarp replay record</c>,
/// <c>quarp replay play</c>, the golden-master tests, the CI determinism jobs — goes through
/// this type and nothing else.
///
/// <para><b>Why it lives in the core and why there is only one copy.</b> The M2 criterion is
/// literally "the frame hashes match between architectures" (ROADMAP M2, REPLAY-FORMAT §6).
/// A hash reimplemented once per project agrees with its siblings by luck, and the day one
/// copy drifts the failure surfaces as a cross-architecture determinism bug — the most
/// expensive possible disguise for a one-line typo. One implementation cannot disagree with
/// itself, so the comparison keeps measuring the simulation instead of the hashers.</para>
///
/// <para><b>The text form is a contract, not a preference.</b>
/// <c>.github/workflows/ci.yml</c> lifts the hash out of a command's stdout by shape —
/// <c>^[0-9a-f]{16}$</c> on a line of its own — and compares it between windows-x64 and
/// linux-arm64. Lowercase, always 16 digits, zero-padded, no <c>0x</c>, no separators.
/// Changing the shape breaks the milestone's proof and every golden constant in the test
/// suite at the same time.</para>
///
/// <para><b>Portable by construction.</b> The loop consumes one byte at a time, so no machine
/// word is ever reinterpreted and byte order cannot enter the result; the framebuffer is a
/// <c>byte[]</c> to begin with (SPEC-8 §2), which is why hashing it needs no marshalling.
/// Formatting pins <see cref="CultureInfo.InvariantCulture"/> explicitly rather than trusting
/// the ambient culture — hex formatting of an integer ignores culture today, and
/// <c>InvariantGlobalization</c> is on for the whole solution, but "the hash text is the same
/// on every machine" is exactly the kind of promise that should not rest on two defaults
/// staying put.</para>
///
/// <para>FNV-1a is not a cryptographic hash and is not used as one. It answers "are these two
/// frames the same bytes", which is the only question asked of it, and it is trivial enough to
/// reimplement from this file if a future tool outside the solution ever needs to.</para>
/// </summary>
public static class FrameHash
{
    /// <summary>FNV-1a 64-bit offset basis (0xcbf29ce484222325).</summary>
    private const ulong OffsetBasis = 14695981039346656037UL;

    /// <summary>FNV-1a 64-bit prime (0x100000001b3).</summary>
    private const ulong Prime = 1099511628211UL;

    /// <summary>Digits in the text form — the width the CI matches on.</summary>
    public const int HexLength = 16;

    /// <summary>The framebuffer's hash as the 16-hex-digit line the CI greps for.</summary>
    public static string Of(Framebuffer framebuffer) => Format(Compute(framebuffer));

    /// <summary>The bytes' hash as the 16-hex-digit line the CI greps for.</summary>
    public static string Of(ReadOnlySpan<byte> data) => Format(Compute(data));

    /// <summary>The framebuffer's raw 64-bit hash, for callers that compare rather than print.</summary>
    public static ulong Compute(Framebuffer framebuffer)
    {
        ArgumentNullException.ThrowIfNull(framebuffer);
        return Compute(framebuffer.Pixels);
    }

    /// <summary>The bytes' raw 64-bit hash. Allocates nothing.</summary>
    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        ulong hash = OffsetBasis;
        for (int i = 0; i < data.Length; i++)
        {
            hash = unchecked((hash ^ data[i]) * Prime);
        }
        return hash;
    }

    /// <summary>
    /// The canonical text form of an already-computed hash: 16 lowercase hex digits,
    /// zero-padded, invariant culture. The single place that decides what a frame hash
    /// looks like on screen, in a log and in a golden constant.
    /// </summary>
    public static string Format(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);
}

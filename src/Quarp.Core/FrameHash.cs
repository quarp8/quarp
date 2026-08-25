using System.Globalization;
using Quarp.Core.Audio;

namespace Quarp.Core;

/// <summary>
/// The one output fingerprint in Quarp: FNV-1a 64, printed as exactly 16 lowercase hex
/// digits. Everything that quotes a frame or audio hash — <c>quarp sim</c>,
/// <c>quarp replay record</c>, <c>quarp replay play</c>, the golden-master tests, the CI
/// determinism jobs — goes through this type and nothing else.
///
/// <para><b>M3 added the second half of a tick.</b> A tick produces a frame and an
/// <see cref="AudioBlock"/>, and both are hashed here, by the same digest and into the same
/// 16-digit text form. The text of a <em>frame</em> hash is byte-for-byte what it was in M2 —
/// same bytes in, same digest, same formatting — because the CI greps and every golden constant
/// in the suite depend on it. What is new is only that a second thing can be hashed. Where the
/// two hashes appear together, whoever prints them owes the reader a line shape that keeps the
/// bare <c>^[0-9a-f]{16}$</c> line meaning "the final frame hash", which is what M1 and M2
/// consumers already grep for.</para>
///
/// <para><b>The palette wave added a third subject, on the same terms.</b> A frame is now
/// described by a pair — the index buffer the cartridge drew, and the output state it is shown
/// through (<see cref="DisplayPalette"/>) — and both are hashed here, by the same digest and
/// into the same 16-digit text form. The frame hash itself is untouched: its input is still the
/// index buffer and nothing else, which is what keeps the eight anchors, the twelve demo hashes
/// and every editor golden constant where they are, because the default output state is the
/// identity map and changes no pixel. What is new is only that a third thing can be hashed, and
/// that a picture recoloured at output time is now a fact some quantity can see.</para>
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

    /// <summary>
    /// The digest of nothing: the seed a running digest starts from, and what
    /// <see cref="Compute(ReadOnlySpan{byte})"/> returns for an empty span.
    /// </summary>
    public const ulong Empty = OffsetBasis;

    /// <summary>The framebuffer's hash as the 16-hex-digit line the CI greps for.</summary>
    public static string Of(Framebuffer framebuffer) => Format(Compute(framebuffer));

    /// <summary>The tick's audio hash as a 16-hex-digit string, in the same form as a frame hash.</summary>
    public static string Of(AudioBlock block) => Format(Compute(block));

    /// <summary>
    /// The output state's hash — "how is this coloured" — as a 16-hex-digit string, in the same
    /// form as a frame hash. See <see cref="Compute(DisplayPalette)"/> for why it is a second
    /// quantity and not a change to the first.
    /// </summary>
    public static string Of(DisplayPalette display) => Format(Compute(display));

    /// <summary>The bytes' hash as the 16-hex-digit line the CI greps for.</summary>
    public static string Of(ReadOnlySpan<byte> data) => Format(Compute(data));

    /// <summary>The framebuffer's raw 64-bit hash, for callers that compare rather than print.</summary>
    public static ulong Compute(Framebuffer framebuffer)
    {
        ArgumentNullException.ThrowIfNull(framebuffer);
        return Compute(framebuffer.Pixels);
    }

    /// <summary>
    /// The output state's raw 64-bit hash: the same FNV-1a, over the fixed-size record
    /// <see cref="DisplayPalette.WriteHashBytes"/> lays out (223 bytes on QUARP-8 — a 5-byte
    /// shape header, 4 x 32 set bytes, 90 selector bytes).
    ///
    /// <para><b>Why a second hash and not a wider first one.</b> The frame hash answers "what did
    /// the cartridge draw" and is quoted by eight determinism anchors, twelve pinned demo hashes,
    /// the cross-architecture CI job and every editor golden test; it is computed over the index
    /// buffer and must not move by a byte. The display stage never touches the index buffer, so
    /// folding it into that digest would move every one of those numbers for a change that draws
    /// nothing. Splitting the question in two keeps both answers exact: same digest, same
    /// 16-digit text form, one hasher — the rule this file's header states — and two subjects.
    /// The proof that the split is necessary rather than tidy is a test: a frame flooded through
    /// the display stage into a different colour has the <em>same</em> frame hash as the untinted
    /// one and a <em>different</em> display hash.</para>
    /// </summary>
    public static ulong Compute(DisplayPalette display)
    {
        ArgumentNullException.ThrowIfNull(display);
        Span<byte> record = stackalloc byte[display.HashLength];
        record.Clear();
        display.WriteHashBytes(record);
        return Compute(record);
    }

    /// <summary>The audio block's raw 64-bit hash, for callers that compare rather than print.</summary>
    public static ulong Compute(AudioBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return Compute((ReadOnlySpan<short>)block.Samples);
    }

    /// <summary>
    /// Samples' raw 64-bit hash. Each sample is fed low byte first — the same little-endian
    /// order <see cref="AudioBlock.CopyBytesTo"/> writes — so the digest of a block equals the
    /// digest of the bytes a .wav of it would contain, on every architecture. Feeding the array
    /// through <c>MemoryMarshal.AsBytes</c> instead would produce one answer on x64 and another
    /// on a big-endian host, which is the precise failure the cross-architecture job exists to
    /// catch and the last place anyone would look for it.
    /// </summary>
    public static ulong Compute(ReadOnlySpan<short> samples) => Combine(Empty, samples);

    /// <summary>
    /// Continues a running digest over one more tick's samples, so a whole run's PCM folds
    /// into one number without ever holding more than a block of it.
    ///
    /// <para>This exists because sound has no equivalent of a frame you are allowed to skip.
    /// A checkpoint every twenty ticks samples one frame in twenty and that is a fair probe of
    /// a picture; doing the same to audio would leave nineteen blocks out of twenty compared
    /// against nothing. The audio column of a checkpoint is therefore cumulative — every
    /// sample the run has produced up to that tick — while the frame column stays
    /// instantaneous. Both still name the tick where two machines first disagree.</para>
    /// </summary>
    public static ulong Combine(ulong hash, ReadOnlySpan<short> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            int sample = samples[i];
            hash = unchecked((hash ^ (byte)(sample & 0xFF)) * Prime);
            hash = unchecked((hash ^ (byte)((sample >> 8) & 0xFF)) * Prime);
        }
        return hash;
    }

    /// <summary>Continues a running digest over one more <see cref="AudioBlock"/>.</summary>
    public static ulong Combine(ulong hash, AudioBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return Combine(hash, (ReadOnlySpan<short>)block.Samples);
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

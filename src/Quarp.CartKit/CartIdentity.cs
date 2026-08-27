using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Quarp.CartKit;

/// <summary>
/// The 32-byte cartridge identity stamped into every replay header (REPLAY-FORMAT §5):
/// a SHA-256 over everything the simulation can see, normalized so that the same cartridge
/// hashes to the same bytes on Windows and on Linux. The core never computes this and never
/// interprets it — it stores the digest, writes it into the <c>.qrpr</c> and compares it.
///
/// <para><b>What goes in</b> — sources and decoded assets, plus the profile:</para>
/// <list type="bullet">
///   <item>every <c>src/**/*.cs</c>, text <em>as written</em> — comments are <b>not</b>
///     stripped, unlike <see cref="CodeBudget"/>, whose job is to not punish readability;
///     here the job is to answer "is this exactly that cartridge", and a hash that ignores
///     part of a file lies about the file;</item>
///   <item>each file's cart-relative path alongside its text, files ordered by that path —
///     otherwise renaming <c>a.cs</c> to <c>b.cs</c> would be invisible, and directory
///     enumeration order (which differs between file systems) would leak into the digest;</item>
///   <item>the <b>decoded</b> assets — the 128x128 index sheet, the map and the flags — not
///     the bytes of <c>gfx.png</c>: a PNG re-saved by another optimizer with the same pixels
///     must give the same identity, because the simulation sees pixels, not chunks;</item>
///   <item>the <b>audio banks</b>, on the same terms and for the same reason as the frame:
///     since M3 the synthesizer's PCM is part of the golden master, so a cartridge whose SFX
///     changed is a different cartridge. What is hashed is the payload
///     (docs/AUDIO-FORMAT.md §2, §4), not the file: the 8-byte container header carries the
///     format version, not music, and an absent <c>sfx.bin</c> must hash exactly like a bank
///     whose 64 slots are all empty — those are the same cartridge to a listener;</item>
///   <item><c>profile</c> from the manifest: 8 versus 16 changes everything.</item>
/// </list>
///
/// <para><b>What stays out</b>: <c>name</c>, <c>author</c>, <c>cover.png</c> (cosmetics),
/// and <c>save.dat</c> (a separate simulation input — its snapshot lives in the replay
/// header). The Quarp core version is also absent, which is a known and deliberate v1 hole
/// documented in REPLAY-FORMAT §7.</para>
///
/// <para><b>The normalization, precisely</b> — this is the part that has to be identical on
/// two operating systems, so it is spelled out rather than left to a library:</para>
/// <list type="number">
///   <item><b>Line endings</b>: <c>\r\n</c> and a lone <c>\r</c> both become <c>\n</c>.
///     Without this a repository cloned with <c>core.autocrlf=true</c> hashes differently
///     from the same repository on Linux, and the milestone's cross-architecture proof dies
///     on a text-mode checkout rather than on anything real.</item>
///   <item><b>Encoding</b>: UTF-8, no BOM. Both loaders already strip a BOM
///     (<see cref="CartSource"/>), so this only restates the invariant.</item>
///   <item><b>Path separators</b>: <c>/</c>, never <c>\</c> — a folder cart on Windows and
///     its <c>.quarp8</c> must agree.</item>
///   <item><b>Order</b>: ordinal (byte-value) comparison of those <c>/</c>-separated paths.
///     Ordinal, not culture-aware: culture collation is a machine setting, which is exactly
///     the class of input a determinism hash must not have.</item>
///   <item><b>Framing</b>: every variable-length field is preceded by its length as a u32
///     little-endian, and the whole digest starts with a version tag. Concatenating without
///     lengths would let two different cartridges hash identically — a file named <c>ab</c>
///     holding <c>c</c> against a file named <c>a</c> holding <c>bc</c>.</item>
/// </list>
/// </summary>
public static class CartIdentity
{
    /// <summary>Length of an identity in bytes — SHA-256 is 32.</summary>
    public const int Size = 32;

    /// <summary>
    /// Tag mixed in first, so that a future change to what is hashed (or how it is framed)
    /// cannot silently collide with an earlier digest. Bumping this string invalidates every
    /// recorded identity on purpose.
    ///
    /// <para>Bumped to /2 in M3, when the audio banks joined the hash. Recorded identities from
    /// M2 stop matching, which is the honest answer: those replays were taken from a console
    /// that could not make a sound, and identity mismatch is a warning by design
    /// (REPLAY-FORMAT §5), so no replay stops playing.</para>
    /// </summary>
    private const string DomainTag = "quarp-cart-identity/2\n";

    private static readonly byte[] ZeroIdentity = new byte[Size];

    /// <summary>All-zero identity: "recorded without a cartridge hash", the same value the core calls unknown.</summary>
    public static ReadOnlySpan<byte> Unknown => ZeroIdentity;

    /// <summary>
    /// Hashes a loaded cartridge. Cold path — called once per load and once per successful
    /// hot reload — so it favours being obviously correct over being fast.
    /// </summary>
    public static byte[] Compute(CartData cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        // Sorted here rather than trusted from the caller: CartSource already sorts, but this
        // is public API and the whole point of the type is that the order is not negotiable.
        var sources = new CartSourceFile[cart.Sources.Count];
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = cart.Sources[i];
        }
        Array.Sort(sources, static (a, b) =>
            string.CompareOrdinal(NormalizePath(a.RelativePath), NormalizePath(b.RelativePath)));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(DomainTag));
        AppendInt32(hash, cart.Manifest.Profile);
        AppendInt32(hash, sources.Length);
        foreach (CartSourceFile source in sources)
        {
            AppendBlock(hash, Encoding.UTF8.GetBytes(NormalizePath(source.RelativePath)));
            AppendBlock(hash, Encoding.UTF8.GetBytes(NormalizeText(source.Text)));
        }
        AppendBlock(hash, cart.Gfx);
        AppendBlock(hash, cart.Map);
        AppendBlock(hash, cart.Flags);
        AppendBlock(hash, cart.Sfx);
        AppendBlock(hash, cart.Music);
        AppendDataBanks(hash, cart);

        byte[] digest = hash.GetHashAndReset();
        if (digest.Length != Size)
        {
            throw new InvalidOperationException($"SHA-256 produced {digest.Length} bytes, expected {Size}.");
        }
        return digest;
    }

    /// <summary>Lowercase hex, for messages and for <c>quarp replay</c> output.</summary>
    public static string ToHex(ReadOnlySpan<byte> identity) => Convert.ToHexStringLower(identity);

    /// <summary>Short prefix — eight hex digits, enough to tell two builds apart in a log line.</summary>
    public static string ToShortHex(ReadOnlySpan<byte> identity) =>
        identity.Length >= 4 ? Convert.ToHexStringLower(identity[..4]) : ToHex(identity);

    /// <summary>Cart-relative path with forward slashes; the key both cart shapes sort by.</summary>
    private static string NormalizePath(string path) => path.Replace('\\', '/');

    /// <summary>
    /// Line endings to <c>\n</c>. Handles a lone <c>\r</c> too: C# treats it as a line
    /// terminator, so a file saved with classic Mac endings is legal source, and leaving it
    /// alone would make the identity depend on which editor last touched the file.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (text.IndexOf('\r') < 0)
        {
            return text;    // The common case: nothing to do, no allocation.
        }
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;    // \r\n counts once.
                }
                builder.Append('\n');
                continue;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Folds the data banks of ADR-035 into the identity — but <b>only when at least one bank
    /// carries bytes</b>.
    ///
    /// <para>The exception is not a shortcut, it is the point. A cartridge without banks means
    /// exactly what it meant before banks existed, so its identity must not move: every replay
    /// already recorded stores the identity it was taken against (REPLAY-FORMAT §5), and a
    /// hash that shifted "because the console grew a feature this cart does not use" would
    /// detach every one of them at once. A cartridge that does carry banks is a different
    /// cartridge from one that does not, and gets a different identity, which is what the ADR
    /// asks for.</para>
    /// </summary>
    private static void AppendDataBanks(IncrementalHash hash, CartData cart)
    {
        IReadOnlyList<byte[]> banks = cart.DataBanks;
        bool any = false;
        for (int i = 0; i < banks.Count; i++)
        {
            if (banks[i].Length > 0)
            {
                any = true;
                break;
            }
        }
        if (!any)
        {
            return;
        }
        AppendInt32(hash, banks.Count);
        for (int i = 0; i < banks.Count; i++)
        {
            AppendBlock(hash, banks[i]);
        }
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    /// <summary>Length-prefixed block: the framing that keeps two different carts from colliding.</summary>
    private static void AppendBlock(IncrementalHash hash, byte[] data)
    {
        AppendInt32(hash, data.Length);
        hash.AppendData(data);
    }
}

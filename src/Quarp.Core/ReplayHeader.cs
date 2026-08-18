namespace Quarp.Core;

/// <summary>
/// The identity half of a replay: which cartridge it was recorded against, what the RNG was
/// seeded with, and the 64 persistent slots as they stood the moment recording started.
/// Together with the button log these are the complete inputs of the simulation (SPEC-8 §7):
/// drop any one of them and the replay stops reproducing.
/// Immutable and pure data. The core carries and compares the cartridge identity but never
/// computes it — the 32 bytes are a SHA-256 of the cartridge's normalized sources and assets,
/// produced by CartKit and handed in ready-made.
/// </summary>
public sealed class ReplayHeader
{
    /// <summary>The .qrpr format version this build writes. Readers reject anything else.</summary>
    public const ushort CurrentVersion = 1;

    /// <summary>Length of a cartridge identity in bytes — SHA-256 is 32.</summary>
    public const int IdentitySize = 32;

    /// <summary>Persistent slots a replay snapshots: the console's full save area (SPEC-8 §6).</summary>
    public const int PersistentSlots = VirtualConsole.PersistentSlots;

    private static readonly byte[] ZeroIdentity = new byte[IdentitySize];

    private readonly byte[] _identity;
    private readonly int[] _persistent;

    /// <summary>
    /// Builds a header for recording. <paramref name="cartIdentity"/> must be exactly
    /// <see cref="IdentitySize"/> bytes — pass <see cref="UnknownIdentity"/> when there is no
    /// hash to record. <paramref name="persistent"/> holds raw Fix values, at most
    /// <see cref="PersistentSlots"/> of them; a shorter snapshot leaves the tail zeroed,
    /// exactly like <see cref="VirtualConsole.LoadPersistent"/>.
    /// </summary>
    public ReplayHeader(ReadOnlySpan<byte> cartIdentity, int seed, ReadOnlySpan<int> persistent)
        : this(CurrentVersion, cartIdentity, seed, persistent)
    {
    }

    private ReplayHeader(ushort version, ReadOnlySpan<byte> cartIdentity, int seed, ReadOnlySpan<int> persistent)
    {
        if (cartIdentity.Length != IdentitySize)
        {
            throw new ArgumentException(
                $"Cartridge identity must be exactly {IdentitySize} bytes, got {cartIdentity.Length}.",
                nameof(cartIdentity));
        }
        if (persistent.Length > PersistentSlots)
        {
            throw new ArgumentException(
                $"A persistent snapshot holds at most {PersistentSlots} slots, got {persistent.Length}.",
                nameof(persistent));
        }
        Version = version;
        Seed = seed;
        _identity = cartIdentity.ToArray();
        _persistent = new int[PersistentSlots];
        persistent.CopyTo(_persistent);
    }

    /// <summary>Used by the reader to carry the version it actually found in the file.</summary>
    internal static ReplayHeader ForVersion(
        ushort version, ReadOnlySpan<byte> cartIdentity, int seed, ReadOnlySpan<int> persistent) =>
        new(version, cartIdentity, seed, persistent);

    /// <summary>All-zero identity: "recorded without a cartridge hash". Never matches a real SHA-256.</summary>
    public static ReadOnlySpan<byte> UnknownIdentity => ZeroIdentity;

    /// <summary>The .qrpr version this header came from; headers built for recording carry <see cref="CurrentVersion"/>.</summary>
    public ushort Version { get; }

    /// <summary>The RNG seed the console booted with — <see cref="VirtualConsole.AttachCart"/> takes it.</summary>
    public int Seed { get; }

    /// <summary>The 32-byte cartridge hash, or all zeros when none was recorded.</summary>
    public ReadOnlySpan<byte> CartIdentity => _identity;

    /// <summary>The 64 persistent slots as raw Fix values, as of the start of the recording.</summary>
    public ReadOnlySpan<int> Persistent => _persistent;

    /// <summary>False when the identity is all zeros, i.e. the replay cannot be matched to a cartridge.</summary>
    public bool HasIdentity => !_identity.AsSpan().SequenceEqual(ZeroIdentity);

    /// <summary>
    /// True when the given 32-byte hash is the one recorded. A mismatch means the cartridge
    /// changed since the recording; what to do about it — warn or refuse — is the shell's call,
    /// not the core's.
    /// </summary>
    public bool IdentityMatches(ReadOnlySpan<byte> cartIdentity) =>
        _identity.AsSpan().SequenceEqual(cartIdentity);

    /// <summary>Copies the persistent snapshot out; the destination must hold all 64 slots.</summary>
    public void CopyPersistentTo(Span<int> destination) => _persistent.CopyTo(destination);

    /// <summary>
    /// The same header pointed at a different cartridge build. Seed and persistent snapshot are
    /// untouched, so a session that hot-reloads new code keeps resimulating identically while
    /// the replay it eventually saves names the build that is actually running.
    /// </summary>
    public ReplayHeader WithIdentity(ReadOnlySpan<byte> cartIdentity) =>
        new(Version, cartIdentity, Seed, _persistent);
}

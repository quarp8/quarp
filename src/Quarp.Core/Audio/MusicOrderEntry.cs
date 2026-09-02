namespace Quarp.Core.Audio;

/// <summary>
/// One step of a song's <em>order</em>: which pattern plays, in which key, and what happens when
/// it ends (docs/AUDIO-FORMAT.md §4, ADR-040).
///
/// <para><b>The order is what separates the score from the arrangement.</b> Version 1 had none:
/// the sequencer walked patterns 0, 1, 2... in index order, so playing a chorus three times meant
/// three copies of it, and reordering a song meant retyping it. Here the same pattern index can
/// appear in as many entries as the song needs, each with its own <see cref="Transpose"/> — the
/// verse a fifth up costs four bytes instead of a second pattern.</para>
///
/// <para><b>The flag byte is version 1's flag byte, bit for bit.</b> <c>loop-start</c> is 1,
/// <c>loop-back</c> is 2, <c>stop</c> is 4 — the same values <c>music.bin</c> v1 stores per
/// pattern (docs/AUDIO-FORMAT.md §4), because that is what let one sequencer serve both versions
/// instead of two that have to agree. Bit 3, <see cref="MusicFlags.Jump"/>, is the new one: an
/// explicit target instead of "search backwards for a marker".</para>
/// </summary>
public readonly struct MusicOrderEntry : IEquatable<MusicOrderEntry>
{
    /// <summary>Entries the order table holds: 128.</summary>
    public const int Count = 128;

    /// <summary>Largest transpose an entry may carry, in semitones.</summary>
    public const int MaxTranspose = 63;

    /// <summary>Smallest transpose an entry may carry, in semitones.</summary>
    public const int MinTranspose = -64;

    private readonly byte _pattern;
    private readonly byte _flags;
    private readonly byte _target;
    private readonly sbyte _transpose;

    /// <summary>Builds an order entry, clamping every field.</summary>
    public MusicOrderEntry(int pattern, MusicFlags flags = MusicFlags.None, int target = 0, int transpose = 0)
    {
        _pattern = (byte)Math.Clamp(pattern, 0, AudioBank.PatternCount - 1);
        _flags = (byte)(flags & (MusicFlags.LoopStart | MusicFlags.LoopEnd | MusicFlags.Stop | MusicFlags.Jump));
        _target = (byte)Math.Clamp(target, 0, Count - 1);
        _transpose = (sbyte)Math.Clamp(transpose, MinTranspose, MaxTranspose);
    }

    /// <summary>The pattern this entry plays.</summary>
    public int Pattern => _pattern;

    /// <summary>What happens when the pattern ends; see <see cref="MusicFlags"/>.</summary>
    public MusicFlags Flags => (MusicFlags)_flags;

    /// <summary>Where <see cref="MusicFlags.Jump"/> goes; 0 when the entry does not jump.</summary>
    public int Target => _target;

    /// <summary>Semitones this instance of the pattern is transposed by, -64..+63.</summary>
    public int Transpose => _transpose;

    /// <summary>True when the entry is four zero bytes — pattern 0, no flags, no transpose.</summary>
    public bool IsZero => _pattern == 0 && _flags == 0 && _target == 0 && _transpose == 0;

    /// <inheritdoc/>
    public bool Equals(MusicOrderEntry other) =>
        _pattern == other._pattern && _flags == other._flags
        && _target == other._target && _transpose == other._transpose;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MusicOrderEntry other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _pattern | (_flags << 8) | (_target << 16) | ((_transpose & 0xFF) << 24);

    /// <summary>Field-by-field equality.</summary>
    public static bool operator ==(MusicOrderEntry a, MusicOrderEntry b) => a.Equals(b);

    /// <summary>Field-by-field inequality.</summary>
    public static bool operator !=(MusicOrderEntry a, MusicOrderEntry b) => !a.Equals(b);
}

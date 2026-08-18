namespace Quarp.Core.Audio;

/// <summary>
/// One of the 64 music patterns: which SFX slot each of the four channels plays, plus the
/// section flags that decide what happens when the pattern ends (SPEC-8 §4).
///
/// <para><b>Channel N of a pattern always plays on channel N of the chip.</b> Music does not
/// allocate channels, because a song that landed on different channels depending on what the
/// game happened to be doing would sound different every playthrough — and "sounds different
/// every playthrough" is the exact failure this milestone exists to rule out. Dynamic
/// allocation belongs to <see cref="Apu.PlaySfx"/> alone, and even there it is a fixed,
/// documented rule rather than a policy.</para>
///
/// <para>The pattern lasts as long as the <em>longest</em> of its slots, so nothing is cut off
/// mid-phrase; a shorter slot simply falls silent and waits. A pattern with no slots is a rest
/// of <see cref="Apu.MinPatternTicks"/> ticks, which also guarantees the sequencer can never
/// spin on a zero-length pattern.</para>
///
/// <para>A channel is stored as the byte <c>music.bin</c> stores — bit 6 set means the channel
/// plays, bits 0-5 are the slot — so that <c>default(MusicPattern)</c> is the empty pattern and
/// an all-zero music bank means "no music" with no special case anywhere.</para>
/// </summary>
public readonly struct MusicPattern : IEquatable<MusicPattern>
{
    /// <summary>Channels a pattern addresses: 4, one per chip channel (SPEC-8 §4).</summary>
    public const int ChannelCount = Apu.ChannelCount;

    /// <summary>The value a channel reads as when the pattern leaves that channel alone.</summary>
    public const int Empty = -1;

    /// <summary>Bit 6 of a channel byte: this channel plays in this pattern.</summary>
    public const byte ActiveBit = 0x40;

    /// <summary>Bits 0-5 of a channel byte: which SFX slot it plays.</summary>
    public const byte SlotMask = 0x3F;

    private readonly byte _slot0;
    private readonly byte _slot1;
    private readonly byte _slot2;
    private readonly byte _slot3;

    /// <summary>
    /// Builds a pattern. A slot outside 0-63 becomes <see cref="Empty"/>, so
    /// <c>new MusicPattern(-1, 3, -1, -1)</c> reads exactly as it looks.
    /// </summary>
    public MusicPattern(int channel0, int channel1, int channel2, int channel3, MusicFlags flags = MusicFlags.None)
    {
        _slot0 = Encode(channel0);
        _slot1 = Encode(channel1);
        _slot2 = Encode(channel2);
        _slot3 = Encode(channel3);
        Flags = flags;
    }

    /// <summary>What the sequencer does when this pattern ends.</summary>
    public MusicFlags Flags { get; }

    /// <summary>
    /// SFX slot playing on a channel, or <see cref="Empty"/>. A channel outside 0-3 reads
    /// <see cref="Empty"/>.
    /// </summary>
    public int this[int channel] => channel switch
    {
        0 => Decode(_slot0),
        1 => Decode(_slot1),
        2 => Decode(_slot2),
        3 => Decode(_slot3),
        _ => Empty,
    };

    /// <summary>True when the pattern plays nothing on any channel — a rest, not the end of the song.</summary>
    public bool IsEmpty => (_slot0 | _slot1 | _slot2 | _slot3) == 0;

    /// <summary>The same pattern with different section flags.</summary>
    public MusicPattern WithFlags(MusicFlags flags) => new(this[0], this[1], this[2], this[3], flags);

    /// <summary>
    /// Rebuilds a pattern from the four channel bytes and the flag byte of <c>music.bin</c>
    /// (docs/AUDIO-FORMAT.md §4). Unknown flag bits are dropped and a channel byte with bit 6
    /// clear is empty whatever its low bits say — the loader has already rejected both cases
    /// with a message, and dropping them here means a bad bank cannot reach the synthesizer.
    /// </summary>
    public static MusicPattern FromBytes(byte channel0, byte channel1, byte channel2, byte channel3, byte flags) =>
        new(Decode(channel0), Decode(channel1), Decode(channel2), Decode(channel3),
            (MusicFlags)(flags & (byte)(MusicFlags.LoopStart | MusicFlags.LoopEnd | MusicFlags.Stop)));

    /// <summary>The channel byte as <c>music.bin</c> stores it; a channel outside 0-3 gives 0.</summary>
    public byte ChannelByte(int channel) => channel switch
    {
        0 => _slot0,
        1 => _slot1,
        2 => _slot2,
        3 => _slot3,
        _ => 0,
    };

    private static byte Encode(int sfxId) => (uint)sfxId < AudioBank.SfxCount ? (byte)(ActiveBit | sfxId) : (byte)0;

    private static int Decode(byte value) => (value & ActiveBit) == 0 ? Empty : value & SlotMask;

    /// <inheritdoc/>
    public bool Equals(MusicPattern other) =>
        _slot0 == other._slot0 && _slot1 == other._slot1 &&
        _slot2 == other._slot2 && _slot3 == other._slot3 && Flags == other.Flags;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MusicPattern other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        (_slot0 | (_slot1 << 8) | (_slot2 << 16) | (_slot3 << 24)) ^ (int)Flags;

    /// <summary>Field-by-field equality.</summary>
    public static bool operator ==(MusicPattern a, MusicPattern b) => a.Equals(b);

    /// <summary>Field-by-field inequality.</summary>
    public static bool operator !=(MusicPattern a, MusicPattern b) => !a.Equals(b);
}

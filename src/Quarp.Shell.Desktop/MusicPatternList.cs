namespace Quarp.Shell.Desktop;

/// <summary>
/// The pattern list the MUSIC tab still navigates: 64 patterns x 4 channel bytes, then 64 flag
/// bytes. <b>No file has this layout any more.</b>
///
/// <para>Until ADR-041 this was the whole of <c>music.bin</c> and it lived in
/// <see cref="Quarp.CartKit.AudioFormat"/>, the owner of the cartridge's binary formats. ADR-041
/// left the console one music format — the tracker song of
/// <see cref="Quarp.CartKit.MusicFormat"/> — and took the pattern list out of
/// <c>Quarp.CartKit</c> with it. What survives here is not a format: it is the in-memory model
/// of <see cref="MusicEditorSession"/>, a pattern navigator that the next wave replaces with a
/// tracker (docs/REFERENCES-EDITORS.md §6.1). It is kept, unchanged, so that screen goes on
/// compiling, drawing and undoing while it waits — and so the wave that writes the tracker
/// deletes one file instead of reconstructing a screen.</para>
///
/// <para>A channel byte is bit 6 "this channel plays" plus bits 0-5 "which SFX slot", so a
/// silent channel is <c>0x00</c> and the all-zero list is 64 empty patterns. The offsets live
/// here and nowhere else: "the channel byte of pattern P is at <c>4P + C</c>" is one sentence in
/// the tree, which is what let the session be written without a single byte offset in it.</para>
/// </summary>
public static class MusicPatternList
{
    /// <summary>Patterns in the list: 64.</summary>
    public const int PatternCount = 64;

    /// <summary>Channels in a pattern: 4.</summary>
    public const int ChannelCount = 4;

    /// <summary>The channel table at the front: 64 x 4 = 256 bytes.</summary>
    public const int ChannelTableSize = PatternCount * ChannelCount;

    /// <summary>The flag table after it: one byte per pattern.</summary>
    public const int FlagTableSize = PatternCount;

    /// <summary>Bytes of a whole list: 320.</summary>
    public const int PayloadSize = ChannelTableSize + FlagTableSize;

    /// <summary>Bit 6 of a channel byte: this channel plays in this pattern.</summary>
    public const byte ChannelActiveBit = 0x40;

    /// <summary>Bits 0-5 of a channel byte: which SFX slot it plays.</summary>
    public const byte ChannelSlotMask = 0x3F;

    /// <summary>Playback returns here when it meets <see cref="FlagLoopEnd"/>.</summary>
    public const byte FlagLoopStart = 0x01;

    /// <summary>End of a section: jump back to the nearest <see cref="FlagLoopStart"/>.</summary>
    public const byte FlagLoopEnd = 0x02;

    /// <summary>The song ends after this pattern.</summary>
    public const byte FlagStop = 0x04;

    /// <summary>Every flag bit that has a meaning; the rest must be zero.</summary>
    public const byte FlagMask = FlagLoopStart | FlagLoopEnd | FlagStop;

    /// <summary>An all-zero list: 64 patterns with no active channel.</summary>
    public static byte[] Empty() => new byte[PayloadSize];

    /// <summary>The SFX slot a channel plays in this pattern, or -1 when the channel is silent.</summary>
    public static int PatternChannel(ReadOnlySpan<byte> list, int pattern, int channel)
    {
        byte value = list[(pattern * ChannelCount) + channel];
        return (value & ChannelActiveBit) == 0 ? -1 : value & ChannelSlotMask;
    }

    /// <summary>Section flags of a pattern: loop start, loop end, stop.</summary>
    public static byte PatternFlags(ReadOnlySpan<byte> list, int pattern) => list[ChannelTableSize + pattern];

    /// <summary>True when no channel plays in this pattern — a bar of rest, not the end of the song.</summary>
    public static bool PatternIsEmpty(ReadOnlySpan<byte> list, int pattern)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            if (PatternChannel(list, pattern, channel) >= 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Sets a channel; <paramref name="slot"/> below zero makes the channel silent.</summary>
    public static void WritePatternChannel(Span<byte> list, int pattern, int channel, int slot) =>
        list[(pattern * ChannelCount) + channel] =
            slot < 0 ? (byte)0 : (byte)(ChannelActiveBit | (slot & ChannelSlotMask));

    /// <summary>Sets a pattern's section flags.</summary>
    public static void WritePatternFlags(Span<byte> list, int pattern, byte flags) =>
        list[ChannelTableSize + pattern] = flags;
}

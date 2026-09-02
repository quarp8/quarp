using Quarp.Core.Audio;

namespace Quarp.Core.Tests;

/// <summary>
/// A pattern of the list the console read before ADR-041: which SFX slot each of the four
/// channels plays, plus the section flags. <b>Test scaffolding, not a format</b> — no file has
/// this layout any more.
///
/// <para><b>Why it survives here.</b> Forty-odd sequencer tests — channel stealing, the mask,
/// the fade, paging, the order walk — were written against it, and their pinned tick counts are
/// evidence about the <em>sequencer</em>, not about the file. <see cref="LegacyPatternBank"/>
/// turns one of these into the song that sounds the same, by exactly the rule
/// <c>quarp audio upgrade</c> used while it existed, so those tests go on asking the questions
/// they were written to ask and go on getting the same numbers.</para>
/// </summary>
public readonly struct MusicPattern
{
    /// <summary>Channels a pattern addresses: 4, one per chip channel.</summary>
    public const int ChannelCount = Apu.ChannelCount;

    /// <summary>The value a channel reads as when the pattern leaves that channel alone.</summary>
    public const int Empty = -1;

    private readonly int _slot0;
    private readonly int _slot1;
    private readonly int _slot2;
    private readonly int _slot3;

    /// <summary>Builds a pattern; a slot outside 0-63 becomes <see cref="Empty"/>.</summary>
    public MusicPattern(int channel0, int channel1, int channel2, int channel3, MusicFlags flags = MusicFlags.None)
    {
        _slot0 = Normalize(channel0);
        _slot1 = Normalize(channel1);
        _slot2 = Normalize(channel2);
        _slot3 = Normalize(channel3);
        Flags = flags;
    }

    /// <summary>The section flags: loop start, loop end, stop.</summary>
    public MusicFlags Flags { get; }

    /// <summary>The SFX slot a channel plays, or <see cref="Empty"/>.</summary>
    public int this[int channel] => channel switch
    {
        0 => _slot0,
        1 => _slot1,
        2 => _slot2,
        _ => _slot3,
    };

    private static int Normalize(int slot) => (uint)slot < AudioBank.SfxCount ? slot : Empty;
}

/// <summary>
/// Fills an <see cref="AudioBank"/>'s song from patterns of the old list, the way
/// <c>quarp audio upgrade</c> did before ADR-041 deleted it: one row per pattern, whose speed is
/// the number of ticks the old sequencer would have held that bar; every active channel gets a
/// note-on with an instrument rooted at its slot's own first note, so the slot transposes by
/// nothing and plays exactly as written; every silent channel gets a note off, because a
/// turnover of the old list stopped the voices the new bar did not fill.
///
/// <para>The order is "entry k plays pattern k", 64 entries long, which is the order the old
/// list had by construction — that is what keeps <c>CurrentPattern</c>, the loop flags and the
/// run-off-the-end stop meaning what the tests that pin them meant.</para>
/// </summary>
public static class LegacyPatternBank
{
    /// <summary>Writes one pattern of the old list into the bank's song. The SFX slots it names must already be loaded.</summary>
    public static void SetPattern(this AudioBank bank, int index, MusicPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(bank);
        MusicSong song = bank.Song;
        song.OrderLength = AudioBank.PatternCount;
        for (int entry = 0; entry < AudioBank.PatternCount; entry++)
        {
            // "Entry k plays pattern k" — the order the old list had by construction, and what a
            // song that runs off the end of its written patterns into empty ones depends on.
            if (song.Order(entry).IsZero)
            {
                song.SetOrder(entry, new MusicOrderEntry(entry));
            }
        }
        song.SetOrder(index, new MusicOrderEntry(index, pattern.Flags));

        int ticks = 0;
        for (int channel = 0; channel < MusicPattern.ChannelCount; channel++)
        {
            int slot = pattern[channel];
            if (slot < 0 || bank.GetSfx(slot).IsEmpty)
            {
                continue;
            }
            int length = bank.GetSfx(slot).LengthTicks;
            if (length > ticks)
            {
                ticks = length;
            }
        }
        (int rows, int speed) = Geometry(Math.Max(ticks, Apu.MinPatternTicks));
        song.SetPatternRows(index, rows);
        song.SetPatternSpeed(index, speed);

        for (int channel = 0; channel < MusicPattern.ChannelCount; channel++)
        {
            int slot = pattern[channel];
            if (slot < 0)
            {
                song.SetCell(index, 0, channel, new MusicCell(MusicNoteKind.Off, 0, -1, -1));
                continue;
            }
            int root = bank.GetSfx(slot).IsEmpty ? 0 : bank.GetSfx(slot)[0].Note;
            song.SetInstrument(slot, new MusicInstrument(slot, root));
            song.SetCell(index, 0, channel, new MusicCell(MusicNoteKind.On, root, slot, -1));
        }
    }

    /// <summary>The bank's song as the <see cref="AudioBank.SongPayloadSize"/> bytes of <c>music.bin</c>'s payload.</summary>
    public static byte[] SongPayload(AudioBank bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        MusicSong song = bank.Song;
        byte[] payload = new byte[AudioBank.SongPayloadSize];
        payload[0] = 0;                                   // layout word: version 0
        payload[1] = 0;
        payload[2] = MusicSong.RowCount;
        payload[3] = MusicInstrument.Count;
        payload[4] = (byte)(song.OrderLength & 0xFF);
        payload[5] = (byte)((song.OrderLength >> 8) & 0xFF);

        for (int i = 0; i < MusicInstrument.Count; i++)
        {
            MusicInstrument instrument = song.Instrument(i);
            int at = AudioBank.SongInstrumentTableOffset + (i * 4);
            payload[at] = (byte)instrument.Slot;
            payload[at + 1] = (byte)instrument.Root;
            payload[at + 2] = instrument.Flags;
            payload[at + 3] = (byte)instrument.Speed;
        }
        for (int i = 0; i < MusicOrderEntry.Count; i++)
        {
            MusicOrderEntry entry = song.Order(i);
            int at = AudioBank.SongOrderTableOffset + (i * 4);
            payload[at] = (byte)entry.Pattern;
            payload[at + 1] = (byte)entry.Flags;
            payload[at + 2] = (byte)entry.Target;
            payload[at + 3] = (byte)(sbyte)entry.Transpose;
        }
        for (int pattern = 0; pattern < AudioBank.PatternCount; pattern++)
        {
            int at = AudioBank.SongPatternTableOffset + (pattern * 4);
            int speed = song.PatternSpeed(pattern);
            payload[at] = (byte)(speed & 0xFF);
            payload[at + 1] = (byte)((speed >> 8) & 0xFF);
            payload[at + 2] = (byte)song.PatternRows(pattern);
            payload[at + 3] = 0;
        }
        for (int pattern = 0; pattern < AudioBank.PatternCount; pattern++)
        {
            for (int row = 0; row < MusicSong.RowCount; row++)
            {
                for (int channel = 0; channel < Apu.ChannelCount; channel++)
                {
                    uint word = song.Cell(pattern, row, channel).Word;
                    int cell = AudioBank.SongCellTableOffset
                        + ((((pattern * MusicSong.RowCount) + row) * Apu.ChannelCount + channel) * 4);
                    payload[cell] = (byte)(word & 0xFF);
                    payload[cell + 1] = (byte)((word >> 8) & 0xFF);
                    payload[cell + 2] = (byte)((word >> 16) & 0xFF);
                    payload[cell + 3] = (byte)((word >> 24) & 0xFF);
                }
            }
        }
        return payload;
    }

    /// <summary>
    /// Rows and row speed for a bar that has to last exactly <paramref name="ticks"/> ticks: one
    /// row whenever a row is long enough, otherwise as few rows as divide the bar evenly.
    /// </summary>
    private static (int Rows, int Speed) Geometry(int ticks)
    {
        int units = ticks * MusicSong.SpeedUnitsPerTick;
        for (int rows = 1; rows <= MusicSong.RowCount; rows++)
        {
            if (units % rows != 0)
            {
                continue;
            }
            int speed = units / rows;
            if (speed is >= MusicSong.MinRowSpeed and <= MusicSong.MaxRowSpeed)
            {
                return (rows, speed);
            }
        }
        throw new InvalidOperationException($"no whole number of rows lasts {ticks} ticks");
    }
}

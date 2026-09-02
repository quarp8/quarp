namespace Quarp.Core.Audio;

/// <summary>
/// A cartridge's music: 64 patterns of cells, a table of 64 instruments and an order that says
/// in which sequence and in which key the patterns play (docs/AUDIO-FORMAT.md §4, ADR-040 and
/// ADR-041). This is the whole of what a cartridge can sing — there is no second layout.
///
/// <para><b>This type is also the tracker's model.</b> The editor of the next wave reads and
/// writes cells through <see cref="Cell"/>/<see cref="SetCell"/>, pattern geometry through
/// <see cref="PatternRows"/>/<see cref="PatternSpeed"/>, and asks
/// <see cref="PatternTicks"/>/<see cref="RowTicks"/> how long things last; the chip plays a
/// single row or a single pattern through <see cref="Apu.PreviewRow"/> and
/// <see cref="Apu.PreviewPattern"/>. Nothing here knows about files: <c>Quarp.CartKit</c> owns
/// <c>music.bin</c>, and what crosses into the core is the header-stripped payload.</para>
///
/// <para><b>An empty song is silence and needs no special case.</b> Every array starts at its
/// zero value: no rows in any pattern, an order of length 0, instruments pointing at slot 0. A
/// cartridge whose <c>music.bin</c> is absent lands in exactly that state, and a song with an
/// empty order behaves the same way: <c>Music(0)</c> finds nothing to play and does nothing.</para>
/// </summary>
public sealed class MusicSong
{
    /// <summary>Rows a pattern can hold: 32, the number profile 8 counts everything else in.</summary>
    public const int RowCount = 32;

    /// <summary>Sub-divisions of a tick a row speed is measured in: 32 (see <see cref="PatternSpeed"/>).</summary>
    public const int SpeedUnitsPerTick = 32;

    /// <summary>Shortest row: one whole tick, because registers land on tick boundaries (SPEC-8 §7).</summary>
    public const int MinRowSpeed = SpeedUnitsPerTick;

    /// <summary>Longest row: 65535/32 = 2047.97 ticks.</summary>
    public const int MaxRowSpeed = 0xFFFF;

    /// <summary>Row speed of a pattern nobody configured: 8 ticks, the default an SFX slot gets.</summary>
    public const int DefaultRowSpeed = 8 * SpeedUnitsPerTick;

    private readonly MusicCell[] _cells =
        new MusicCell[AudioBank.PatternCount * RowCount * Apu.ChannelCount];

    private readonly byte[] _rows = new byte[AudioBank.PatternCount];
    private readonly ushort[] _speed = new ushort[AudioBank.PatternCount];
    private readonly MusicOrderEntry[] _order = new MusicOrderEntry[MusicOrderEntry.Count];
    private readonly MusicInstrument[] _instruments = new MusicInstrument[MusicInstrument.Count];
    private int _orderLength;

    /// <summary>How many order entries are the song, 0..128. Entries past it are never played.</summary>
    public int OrderLength
    {
        get => _orderLength;
        set => _orderLength = Math.Clamp(value, 0, MusicOrderEntry.Count);
    }

    /// <summary>True when the song has no order to walk — nothing to play, which is legal.</summary>
    public bool IsEmpty => _orderLength == 0;

    /// <summary>One cell. Any index outside the geometry reads as <see cref="MusicCell.Empty"/>.</summary>
    public MusicCell Cell(int pattern, int row, int channel) =>
        InRange(pattern, row, channel) ? _cells[CellIndex(pattern, row, channel)] : MusicCell.Empty;

    /// <summary>Writes one cell; an index outside the geometry is ignored, the way a slot ignores a step past its end.</summary>
    public void SetCell(int pattern, int row, int channel, MusicCell cell)
    {
        if (InRange(pattern, row, channel))
        {
            _cells[CellIndex(pattern, row, channel)] = cell;
        }
    }

    /// <summary>Rows a pattern plays, 0-32; 0 means the pattern is unused and playing it is a bar of rest.</summary>
    public int PatternRows(int pattern) =>
        (uint)pattern < AudioBank.PatternCount ? _rows[pattern] : 0;

    /// <summary>Sets a pattern's row count, clamped to 0..32.</summary>
    public void SetPatternRows(int pattern, int rows)
    {
        if ((uint)pattern < AudioBank.PatternCount)
        {
            _rows[pattern] = (byte)Math.Clamp(rows, 0, RowCount);
        }
    }

    /// <summary>
    /// How long one row of a pattern lasts, in 1/32 of a console tick. Thirty-seconds rather than
    /// whole ticks because that is what the ports asked for by name: a PICO-8 step is
    /// <c>speed x 15/32</c> ticks, so every PICO-8 tempo lands on this grid exactly (Celeste
    /// GAPS §1.7). The rows themselves still land on whole ticks — the fraction buys an exact
    /// average tempo, not sub-tick events.
    /// </summary>
    public int PatternSpeed(int pattern) =>
        (uint)pattern < AudioBank.PatternCount ? _speed[pattern] : 0;

    /// <summary>Sets a pattern's row speed in 1/32 ticks, clamped to 0 or <see cref="MinRowSpeed"/>..<see cref="MaxRowSpeed"/>.</summary>
    public void SetPatternSpeed(int pattern, int speed)
    {
        if ((uint)pattern >= AudioBank.PatternCount)
        {
            return;
        }
        _speed[pattern] = speed <= 0 ? (ushort)0 : (ushort)Math.Clamp(speed, MinRowSpeed, MaxRowSpeed);
    }

    /// <summary>
    /// Ticks a whole pattern lasts: <c>ceil(rows x speed / 32)</c>, or
    /// <see cref="Apu.MinPatternTicks"/> for an unused pattern, exactly as version 1 gives an
    /// empty pattern a bar of rest. The row clock restarts at every pattern, so this is a pure
    /// function of the pattern — which is what the tracker's ruler needs it to be.
    /// </summary>
    public int PatternTicks(int pattern)
    {
        int rows = PatternRows(pattern);
        if (rows == 0)
        {
            return Apu.MinPatternTicks;
        }
        int speed = PatternSpeed(pattern);
        if (speed < MinRowSpeed)
        {
            speed = MinRowSpeed;
        }
        return ((rows * speed) + SpeedUnitsPerTick - 1) / SpeedUnitsPerTick;
    }

    /// <summary>Ticks from the start of a pattern to the start of one of its rows: <c>ceil(row x speed / 32)</c>.</summary>
    public int RowTicks(int pattern, int row)
    {
        if (row <= 0)
        {
            return 0;
        }
        int speed = Math.Max(PatternSpeed(pattern), MinRowSpeed);
        return ((row * speed) + SpeedUnitsPerTick - 1) / SpeedUnitsPerTick;
    }

    /// <summary>One order entry; an index past the table reads as the zero entry.</summary>
    public MusicOrderEntry Order(int entry) =>
        (uint)entry < MusicOrderEntry.Count ? _order[entry] : default;

    /// <summary>Writes one order entry; an index past the table is ignored.</summary>
    public void SetOrder(int entry, MusicOrderEntry value)
    {
        if ((uint)entry < MusicOrderEntry.Count)
        {
            _order[entry] = value;
        }
    }

    /// <summary>One instrument; an index past the table reads as <see cref="MusicInstrument.Default"/>.</summary>
    public MusicInstrument Instrument(int instrument) =>
        (uint)instrument < MusicInstrument.Count ? _instruments[instrument] : MusicInstrument.Default;

    /// <summary>Writes one instrument; an index past the table is ignored.</summary>
    public void SetInstrument(int instrument, MusicInstrument value)
    {
        if ((uint)instrument < MusicInstrument.Count)
        {
            _instruments[instrument] = value;
        }
    }

    /// <summary>Back to an empty song: no rows, no order, default instruments.</summary>
    public void Clear()
    {
        Array.Clear(_cells);
        Array.Clear(_rows);
        Array.Clear(_speed);
        Array.Clear(_order);
        Array.Clear(_instruments);
        _orderLength = 0;
    }

    /// <summary>Copies another song into this one, keeping this instance — the defensive copy <see cref="AudioBank.CopyFrom"/> makes.</summary>
    public void CopyFrom(MusicSong other)
    {
        ArgumentNullException.ThrowIfNull(other);
        other._cells.CopyTo(_cells, 0);
        other._rows.CopyTo(_rows, 0);
        other._speed.CopyTo(_speed, 0);
        other._order.CopyTo(_order, 0);
        other._instruments.CopyTo(_instruments, 0);
        _orderLength = other._orderLength;
    }

    private static bool InRange(int pattern, int row, int channel) =>
        (uint)pattern < AudioBank.PatternCount
        && (uint)row < RowCount
        && (uint)channel < Apu.ChannelCount;

    private static int CellIndex(int pattern, int row, int channel) =>
        (pattern * RowCount * Apu.ChannelCount) + (row * Apu.ChannelCount) + channel;
}

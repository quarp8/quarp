using System.Globalization;

namespace Quarp.CartKit;

/// <summary>
/// Compiles the author-facing text of a song — the <c>music.txt</c> that opens with
/// <c>version 0</c> — into the payload of <see cref="MusicFormat"/> (docs/AUDIO-FORMAT.md §6,
/// ADR-040 and ADR-041). It is the only music grammar there is; the pattern-list text this
/// project used to compile is gone with its format.
///
/// <para>The three rules <c>sfx.txt</c> follows hold here too: a line starting with a decimal
/// digit is a data row whose first column is its own index, a line starting with a letter is a
/// directive, and <c>#</c> starts a comment only at the beginning of a token so that
/// <c>C#4</c> stays a note. There are three kinds of block — <c>inst</c>, <c>order</c> and
/// <c>pattern</c> — and a data row inside a pattern carries four channel columns of four fields
/// each, laid out the way a tracker lays them out:</para>
/// <code>
/// version 0
///
/// inst 00 sfx 02 root C-3
///
/// order
///   00 00 loop-start
///   01 01 loop-back
///
/// pattern 00 rows 16 speed 7.5
///   00  C-3 00 7 ---  |  .  |  .  |  .
///   04  ===           |  .  |  .  |  .
/// </code>
///
/// <para>Compilation is a pure function of the text — no clock, no culture, no dictionary
/// iteration — which is what lets CI rebuild the bank and compare bytes.</para>
/// </summary>
public static class MusicTextCompiler
{
    /// <summary>An empty cell: nothing happens on this row in this channel.</summary>
    public const string EmptyCellToken = ".";

    /// <summary>A cell that silences the channel; it takes no other column, so it is a whole cell on its own.</summary>
    public const string NoteOffToken = "===";

    /// <summary>The note column of a cell that carries no note.</summary>
    public const string NoNoteToken = "---";

    /// <summary>The instrument column of a cell that keeps the channel's instrument.</summary>
    public const string NoInstrumentToken = "--";

    /// <summary>The volume column of a cell that keeps the channel's level.</summary>
    public const string NoVolumeToken = "-";

    /// <summary>The effect column of a cell with no effect.</summary>
    public const string NoEffectToken = "---";

    /// <summary>The column separator the writer emits between channels; ignored wherever it appears.</summary>
    public const string ChannelSeparator = "|";

    /// <summary>Effect names, indexed by <see cref="MusicFormat.EffectNone"/> and friends.</summary>
    private static readonly string[] EffectNames = ["---", "arp", "slide", "cut"];

    /// <summary>Compiles a <c>music.txt</c> into a 33800-byte payload.</summary>
    public static byte[] Compile(string text, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] payload = MusicFormat.EmptyPayload();
        var state = new State(payload, sourceName);
        string[] lines = AudioTextCompiler.SplitLines(text);

        for (int i = 0; i < lines.Length; i++)
        {
            int line = i + 1;
            string[] tokens = AudioTextCompiler.Tokenize(lines[i]);
            if (tokens.Length == 0)
            {
                continue;
            }
            if (char.IsAsciiDigit(tokens[0][0]))
            {
                state.DataRow(tokens, line);
                continue;
            }
            state.Directive(tokens, line);
        }

        state.Finish();
        // The compiler's output has to be something the loader accepts: if this ever fires it is
        // a bug here, not in the author's file, and it is far better found now than at load time.
        MusicFormat.ValidatePayload(payload, sourceName);
        return payload;
    }

    /// <summary>The text name of a cell effect — used by diagnostics and by the writer's round trip.</summary>
    public static string EffectName(int effect) => EffectNames[effect];

    /// <summary>Effect index for a text name, or -1 when the name is not one.</summary>
    public static int ParseEffect(string name)
    {
        for (int i = 0; i < EffectNames.Length; i++)
        {
            if (string.Equals(EffectNames[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Reads a row speed written in ticks — <c>8</c>, <c>7.5</c>, <c>1.09375</c> — as a whole
    /// number of 1/32 ticks, entirely in integers so that two machines cannot disagree about a
    /// decimal. A value that is not a whole number of thirty-seconds is refused with the two
    /// nearest ones spelled out, because silently rounding a tempo is exactly the bug the
    /// fraction exists to end.
    /// </summary>
    public static bool TryParseSpeed(string token, out int units, out string? problem)
    {
        units = 0;
        problem = null;
        int dot = token.IndexOf('.');
        string wholePart = dot < 0 ? token : token[..dot];
        string fractionPart = dot < 0 ? string.Empty : token[(dot + 1)..];
        if (wholePart.Length == 0
            || !int.TryParse(wholePart, NumberStyles.None, CultureInfo.InvariantCulture, out int whole))
        {
            problem = $"'{token}' is not a number of ticks";
            return false;
        }
        if (dot >= 0 && (fractionPart.Length == 0 || fractionPart.Length > 9))
        {
            problem = $"'{token}' is not a number of ticks";
            return false;
        }
        long scaled = whole * (long)MusicFormat.SpeedUnitsPerTick;
        if (fractionPart.Length > 0)
        {
            if (!long.TryParse(fractionPart, NumberStyles.None, CultureInfo.InvariantCulture, out long fraction))
            {
                problem = $"'{token}' is not a number of ticks";
                return false;
            }
            long power = 1;
            for (int i = 0; i < fractionPart.Length; i++)
            {
                power *= 10;
            }
            long numerator = fraction * MusicFormat.SpeedUnitsPerTick;
            if (numerator % power != 0)
            {
                long low = numerator / power;
                problem = $"speed {token} is not a whole number of 1/{MusicFormat.SpeedUnitsPerTick} ticks; "
                    + $"the nearest are {SpeedText((int)(whole * (long)MusicFormat.SpeedUnitsPerTick + low))} and "
                    + $"{SpeedText((int)(whole * (long)MusicFormat.SpeedUnitsPerTick + low + 1))}";
                return false;
            }
            scaled += numerator / power;
        }
        if (scaled < MusicFormat.MinRowSpeed || scaled > MusicFormat.MaxRowSpeed)
        {
            problem = $"speed {token} is outside {SpeedText(MusicFormat.MinRowSpeed)}.."
                + $"{SpeedText(MusicFormat.MaxRowSpeed)} ticks per row";
            return false;
        }
        units = (int)scaled;
        return true;
    }

    /// <summary>
    /// A row speed in 1/32 ticks written back as the shortest exact decimal — 256 becomes "8",
    /// 240 becomes "7.5". Exact rather than rounded: every value of the field is a multiple of
    /// 1/32, and 1/32 is 0.03125, so five digits always suffice and never lie.
    /// </summary>
    public static string SpeedText(int units)
    {
        int whole = units / MusicFormat.SpeedUnitsPerTick;
        int rest = units % MusicFormat.SpeedUnitsPerTick;
        if (rest == 0)
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }
        // rest/32 as an exact decimal: multiply by 100000/32 = 3125 and trim the trailing zeros.
        string fraction = (rest * 3125).ToString("D5", CultureInfo.InvariantCulture).TrimEnd('0');
        return whole.ToString(CultureInfo.InvariantCulture) + "." + fraction;
    }

    /// <summary>The block a directive opened; data rows mean different things inside each.</summary>
    private enum Block
    {
        None,
        Order,
        Pattern,
    }

    /// <summary>
    /// Everything the walk over the lines carries. A class rather than a pile of locals so that
    /// the two halves — directives and data rows — read as the small state machine they are.
    /// </summary>
    private sealed class State(byte[] payload, string sourceName)
    {
        private readonly int[] _instrumentLine = new int[MusicFormat.InstrumentCount];
        private readonly int[] _patternLine = new int[AudioFormat.MusicPatternCount];
        private readonly int[] _orderLine = new int[MusicFormat.OrderCount];

        private bool _versionSeen;
        private Block _block = Block.None;
        private int _pattern = -1;
        private int _patternHeaderLine;
        private int _declaredRows;
        private int _rowsLine;
        private int _speedUnits = MusicFormat.DefaultRowSpeed;
        private int _lastRow = -1;
        private int _lastOrder = -1;
        private int _orderLength;

        public void Directive(string[] tokens, int line)
        {
            switch (tokens[0].ToLowerInvariant())
            {
                case "version":
                    RequireCount(tokens, 2, "version 0", line);
                    if (_versionSeen)
                    {
                        throw Error(line, "the version line comes once, at the top of the file.");
                    }
                    if (tokens[1] != "0")
                    {
                        throw Error(line, $"'{tokens[1]}': there is exactly one music format and it is version 0 "
                            + "(ADR-041); no other version is read.");
                    }
                    _versionSeen = true;
                    return;

                case "inst":
                    CloseBlock();
                    ParseInstrument(tokens, line);
                    return;

                case "order":
                    CloseBlock();
                    RequireCount(tokens, 1, "order", line);
                    _block = Block.Order;
                    return;

                case "pattern":
                    CloseBlock();
                    ParsePatternHeader(tokens, line);
                    return;

                default:
                    throw Error(line,
                        $"unknown directive '{tokens[0]}' (expected version, inst, order or pattern; a line that "
                        + "starts with a digit is a data row).");
            }
        }

        public void DataRow(string[] tokens, int line)
        {
            switch (_block)
            {
                case Block.Order:
                    ParseOrderRow(tokens, line);
                    return;
                case Block.Pattern:
                    ParseCellRow(tokens, line);
                    return;
                default:
                    throw Error(line,
                        "a data row needs an 'order' or 'pattern <id>' header above it — this row belongs to "
                        + "no block.");
            }
        }

        public void Finish()
        {
            CloseBlock();
            if (!_versionSeen)
            {
                throw Error(1, "a music.txt opens with the line 'version 0'.");
            }
            MusicFormat.WritePreamble(payload, _orderLength);
        }

        private void CloseBlock()
        {
            if (_block == Block.Pattern)
            {
                int rows = _declaredRows > 0 ? _declaredRows : _lastRow + 1;
                if (rows == 0)
                {
                    throw Error(_patternHeaderLine,
                        $"pattern {_pattern} has no rows; an unused pattern is written by leaving the block out "
                        + "entirely.");
                }
                if (rows < _lastRow + 1)
                {
                    throw Error(_rowsLine,
                        $"rows {rows} is shorter than the last row written ({_lastRow}) of pattern {_pattern}.");
                }
                MusicFormat.WritePattern(payload, _pattern, _speedUnits, rows);
            }
            _block = Block.None;
            _pattern = -1;
            _declaredRows = 0;
            _speedUnits = MusicFormat.DefaultRowSpeed;
            _lastRow = -1;
        }

        private void ParseInstrument(string[] tokens, int line)
        {
            const string Shape = "inst <id> sfx <slot> [root <note>] [once] [speed <ticks>]";
            int id = Index(tokens, 1, MusicFormat.InstrumentCount - 1, "instrument", line, Shape);
            if (_instrumentLine[id] != 0)
            {
                throw Error(line, $"instrument {id} is already defined on line {_instrumentLine[id]}.");
            }
            _instrumentLine[id] = line;

            int slot = -1;
            int root = 0;
            byte flags = 0;
            int speed = 0;
            for (int t = 2; t < tokens.Length; t++)
            {
                switch (tokens[t].ToLowerInvariant())
                {
                    case "sfx":
                        slot = Index(tokens, ++t, AudioFormat.SfxSlotCount - 1, "sfx slot", line, Shape);
                        break;
                    case "root":
                        root = Note(tokens, ++t, line, Shape);
                        break;
                    case "once":
                        if ((flags & MusicFormat.InstrumentOnce) != 0)
                        {
                            throw Error(line, "'once' is repeated.");
                        }
                        flags |= MusicFormat.InstrumentOnce;
                        break;
                    case "speed":
                        speed = Index(tokens, ++t, 255, "instrument speed", line, Shape);
                        if (speed == 0)
                        {
                            throw Error(line,
                                "speed 0 would freeze the instrument; leave 'speed' out to use the slot's own.");
                        }
                        break;
                    default:
                        throw Error(line, $"unknown instrument field '{tokens[t]}' ('{Shape}').");
                }
            }
            if (slot < 0)
            {
                throw Error(line, $"instrument {id} names no SFX slot ('{Shape}').");
            }
            MusicFormat.WriteInstrument(payload, id, slot, root, flags, speed);
        }

        private void ParsePatternHeader(string[] tokens, int line)
        {
            const string Shape = "pattern <id> [rows <n>] [speed <ticks>]";
            int id = Index(tokens, 1, AudioFormat.MusicPatternCount - 1, "pattern", line, Shape);
            if (_patternLine[id] != 0)
            {
                throw Error(line, $"pattern {id} is already defined on line {_patternLine[id]}.");
            }
            _patternLine[id] = line;
            _block = Block.Pattern;
            _pattern = id;
            _patternHeaderLine = line;
            _rowsLine = line;
            _declaredRows = 0;
            _speedUnits = MusicFormat.DefaultRowSpeed;
            _lastRow = -1;

            for (int t = 2; t < tokens.Length; t++)
            {
                switch (tokens[t].ToLowerInvariant())
                {
                    case "rows":
                        _declaredRows = Index(tokens, ++t, MusicFormat.RowCount, "rows", line, Shape);
                        if (_declaredRows == 0)
                        {
                            throw Error(line,
                                $"rows 0 means an unused pattern; delete the whole 'pattern {id}' block instead.");
                        }
                        _rowsLine = line;
                        break;
                    case "speed":
                        if (++t >= tokens.Length)
                        {
                            throw Error(line, $"missing speed: the line reads '{Shape}'.");
                        }
                        if (!TryParseSpeed(tokens[t], out _speedUnits, out string? problem))
                        {
                            throw Error(line, problem + ".");
                        }
                        break;
                    default:
                        throw Error(line, $"unknown pattern field '{tokens[t]}' ('{Shape}').");
                }
            }
        }

        private void ParseOrderRow(string[] tokens, int line)
        {
            const string Shape =
                "<index> <pattern> [loop-start] [loop-back] [stop] [jump <target>] [transpose <semitones>]";
            int index = Index(tokens, 0, MusicFormat.OrderCount - 1, "order index", line, Shape);
            if (_orderLine[index] != 0)
            {
                throw Error(line, $"order entry {index} is already written on line {_orderLine[index]}.");
            }
            if (index != _lastOrder + 1)
            {
                throw Error(line, index <= _lastOrder
                    ? $"order entry {index} comes after {_lastOrder}; entries go in ascending order."
                    : $"order entry {_lastOrder + 1} is missing; the song's entries run 0..{index} with no gaps.");
            }
            int pattern = Index(tokens, 1, AudioFormat.MusicPatternCount - 1, "pattern", line, Shape);

            byte flags = 0;
            int target = 0;
            int transpose = 0;
            for (int t = 2; t < tokens.Length; t++)
            {
                string token = tokens[t].ToLowerInvariant();
                byte flag = token switch
                {
                    "loop-start" => MusicFormat.OrderLoopStart,
                    "loop-back" => MusicFormat.OrderLoopBack,
                    "stop" => MusicFormat.OrderStop,
                    "jump" => MusicFormat.OrderJump,
                    _ => 0,
                };
                if (flag != 0)
                {
                    if ((flags & flag) != 0)
                    {
                        throw Error(line, $"flag '{tokens[t]}' is repeated.");
                    }
                    flags |= flag;
                    if (flag == MusicFormat.OrderJump)
                    {
                        target = Index(tokens, ++t, MusicFormat.OrderCount - 1, "jump target", line, Shape);
                    }
                    continue;
                }
                if (token == "transpose")
                {
                    transpose = Transpose(tokens, ++t, line, Shape);
                    continue;
                }
                throw Error(line,
                    $"unknown flag '{tokens[t]}' (expected loop-start, loop-back, stop, jump <target> or "
                    + "transpose <semitones>).");
            }

            MusicFormat.WriteOrder(payload, index, pattern, flags, target, transpose);
            _orderLine[index] = line;
            _lastOrder = index;
            _orderLength = index + 1;
        }

        private void ParseCellRow(string[] tokens, int line)
        {
            int row = Index(tokens, 0, MusicFormat.RowCount - 1, "row", line, "<row> <cell> | <cell> | ...");
            if (row <= _lastRow)
            {
                throw Error(line,
                    $"row {row} comes after row {_lastRow}; rows go in ascending order, and each row is written "
                    + "at most once.");
            }

            int at = 1;
            for (int channel = 0; channel < AudioFormat.MusicChannelCount; channel++)
            {
                while (at < tokens.Length && tokens[at] == ChannelSeparator)
                {
                    at++;
                }
                if (at >= tokens.Length)
                {
                    throw Error(line,
                        $"row {row} names {channel} channel(s); a row writes all {AudioFormat.MusicChannelCount}, "
                        + $"and '{EmptyCellToken}' is the whole of an empty one.");
                }
                uint cell = ParseCell(tokens, ref at, channel, line);
                MusicFormat.WriteCell(payload, _pattern, row, channel, cell);
            }
            while (at < tokens.Length && tokens[at] == ChannelSeparator)
            {
                at++;
            }
            if (at < tokens.Length)
            {
                throw Error(line,
                    $"'{tokens[at]}' is past the last channel; a row writes exactly "
                    + $"{AudioFormat.MusicChannelCount} cells.");
            }
            _lastRow = row;
        }

        private uint ParseCell(string[] tokens, ref int at, int channel, int line)
        {
            const string Shape = "<note> <instrument> <volume> <effect>";
            string first = tokens[at];
            if (first == EmptyCellToken)
            {
                at++;
                return 0;
            }
            if (first == NoteOffToken)
            {
                // A note off silences the voice, so a volume or an effect beside it would have
                // nothing to act on; the bank keeps one spelling for it and so does the text.
                at++;
                return MusicFormat.PackCell(0, MusicFormat.NoteOff, 0, false, 0, false, 0, 0);
            }
            if (at + 4 > tokens.Length)
            {
                throw Error(line,
                    $"channel {channel}: a cell is '{Shape}' (4 columns), '{EmptyCellToken}' for an empty one or "
                    + $"'{NoteOffToken}' for a note off.");
            }

            int note = 0;
            int kind = MusicFormat.NoteNone;
            string noteToken = tokens[at];
            if (noteToken != NoNoteToken)
            {
                note = AudioTextCompiler.ParseNote(noteToken, out bool outOfRange);
                if (note < 0)
                {
                    throw Error(line, outOfRange
                        ? $"channel {channel}: note '{noteToken}' is outside "
                            + $"{AudioTextCompiler.NoteName(0)}..{AudioTextCompiler.NoteName(AudioFormat.MaxNote)}."
                        : $"channel {channel}: '{noteToken}' is not a note; write a letter A-G, then '-' or '#', "
                            + $"then the octave digit, or '{NoNoteToken}' for no note and '{NoteOffToken}' for a "
                            + "note off.");
                }
                kind = MusicFormat.NoteOn;
            }

            string instrumentToken = tokens[at + 1];
            bool hasInstrument = instrumentToken != NoInstrumentToken;
            int instrument = hasInstrument
                ? Index(tokens, at + 1, MusicFormat.InstrumentCount - 1, $"channel {channel} instrument", line,
                    Shape)
                : 0;

            string volumeToken = tokens[at + 2];
            bool hasVolume = volumeToken != NoVolumeToken;
            int volume = hasVolume
                ? Index(tokens, at + 2, AudioFormat.MaxVolume, $"channel {channel} volume", line, Shape)
                : 0;

            (int effect, int param) = ParseEffectColumn(tokens[at + 3], channel, line);
            at += 4;

            if (effect == MusicFormat.EffectSlide && kind != MusicFormat.NoteOn)
            {
                throw Error(line,
                    $"channel {channel}: a slide glides to the cell's own note, so the cell has to carry one.");
            }
            return MusicFormat.PackCell(note, kind, instrument, hasInstrument, volume, hasVolume, effect, param);
        }

        private (int Effect, int Param) ParseEffectColumn(string token, int channel, int line)
        {
            if (token == NoEffectToken)
            {
                return (MusicFormat.EffectNone, 0);
            }
            int colon = token.IndexOf(':');
            if (colon < 0)
            {
                throw Error(line,
                    $"channel {channel}: effect '{token}' carries no parameter; write "
                    + $"'{NoEffectToken}', 'arp:<xy>', 'slide:<ticks>' or 'cut:<ticks>'.");
            }
            string name = token[..colon];
            string argument = token[(colon + 1)..];
            int effect = ParseEffect(name);
            if (effect <= 0)
            {
                throw Error(line,
                    $"channel {channel}: unknown effect '{name}' (expected arp, slide or cut; "
                    + $"'{NoEffectToken}' is no effect).");
            }
            int param;
            if (effect == MusicFormat.EffectArpeggio)
            {
                if (argument.Length != 2 || !TryHex(argument[0], out int high) || !TryHex(argument[1], out int low))
                {
                    throw Error(line,
                        $"channel {channel}: 'arp:{argument}' takes two hex digits — the two semitone offsets of "
                        + "the chord above the cell's note, as in 'arp:47' for a major triad (+4, +7) or "
                        + "'arp:37' for a minor one.");
                }
                param = (high << 4) | low;
            }
            else if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out param)
                || param > 255)
            {
                throw Error(line, $"channel {channel}: '{name}:{argument}' takes a number of ticks 1..255.");
            }
            if (param == 0)
            {
                throw Error(line,
                    $"channel {channel}: '{token}' does nothing; write '{NoEffectToken}' for no effect.");
            }
            return (effect, param);
        }

        private static bool TryHex(char c, out int value)
        {
            value = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            return value >= 0;
        }

        private int Index(string[] tokens, int position, int max, string what, int line, string shape)
        {
            if (position >= tokens.Length)
            {
                throw Error(line, $"missing {what}: the line reads '{shape}'.");
            }
            string token = tokens[position];
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                throw Error(line, $"{what} '{token}' is not a decimal number (0..{max}).");
            }
            if (value > max)
            {
                throw Error(line, $"{what} {value} is out of range 0..{max}.");
            }
            return value;
        }

        private int Note(string[] tokens, int position, int line, string shape)
        {
            if (position >= tokens.Length)
            {
                throw Error(line, $"missing note: the line reads '{shape}'.");
            }
            int note = AudioTextCompiler.ParseNote(tokens[position], out bool outOfRange);
            if (note < 0)
            {
                throw Error(line, outOfRange
                    ? $"note '{tokens[position]}' is outside {AudioTextCompiler.NoteName(0)}.."
                        + $"{AudioTextCompiler.NoteName(AudioFormat.MaxNote)}."
                    : $"'{tokens[position]}' is not a note: write a letter A-G, then '-' or '#', then the octave "
                        + "digit.");
            }
            return note;
        }

        private int Transpose(string[] tokens, int position, int line, string shape)
        {
            if (position >= tokens.Length)
            {
                throw Error(line, $"missing transpose: the line reads '{shape}'.");
            }
            string token = tokens[position];
            if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            {
                throw Error(line, $"transpose '{token}' is not a whole number of semitones.");
            }
            if (value < MusicFormat.MinTranspose || value > MusicFormat.MaxTranspose)
            {
                throw Error(line,
                    $"transpose {value} is outside {MusicFormat.MinTranspose}..+{MusicFormat.MaxTranspose} "
                    + "semitones.");
            }
            return value;
        }

        private void RequireCount(string[] tokens, int expected, string shape, int line)
        {
            if (tokens.Length != expected)
            {
                throw Error(line, $"'{shape}' takes {expected - 1} argument(s), got {tokens.Length - 1}.");
            }
        }

        private CartLoadException Error(int line, string message) => new($"{sourceName}:{line}: {message}");
    }
}

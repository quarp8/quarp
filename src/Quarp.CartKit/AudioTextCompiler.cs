using System.Globalization;

namespace Quarp.CartKit;

/// <summary>
/// Compiles the author-facing audio text — <c>sfx.txt</c> and <c>music.txt</c> in the cart
/// folder — into the payloads of <see cref="AudioFormat"/>. This is the whole authoring tool of
/// milestone M3: the tracker UI arrives in M9, and until then the text file is the instrument,
/// so it is designed to be written by hand, read in a code review and merged by git.
///
/// <para>Three rules make the grammar small enough to hold in one's head:</para>
/// <list type="number">
///   <item>a line that starts with a decimal digit is a <b>data row</b> — a step in
///     <c>sfx.txt</c>, a pattern in <c>music.txt</c> — and its first column is the row index,
///     spelled out rather than implied by position, so that inserting a line cannot silently
///     shift the rows below it and a diff of one changed note is one changed line;</item>
///   <item>a line that starts with a letter is a <b>directive</b> (<c>sfx</c>, <c>speed</c>,
///     <c>length</c>, <c>loop</c>);</item>
///   <item>everything from a <c>#</c> that <b>begins a token</b> to the end of the line is a
///     comment. Only at the beginning of a token: <c>C#4</c> is a note, not the start of a
///     comment, and a format for musicians that cannot spell C sharp would be a bad joke.</item>
/// </list>
///
/// <para>Rows may be sparse and must ascend; every diagnostic names the file and the line, so
/// the CLI can print it unchanged. Compilation is a pure function of the text — no clock, no
/// culture, no dictionary iteration — which is what lets CI rebuild the banks and compare bytes.</para>
/// </summary>
public static class AudioTextCompiler
{
    /// <summary>
    /// Console ticks per step when a block does not say. 8 ticks is about 133 ms, a comfortable
    /// tracker row, and it is deliberately the same number the core gives an unconfigured slot
    /// (<c>Quarp.Core.Audio.SfxSlot.DefaultSpeed</c>): one default, one tempo, nothing to
    /// explain when a sound written in text and a sound built in code play at different speeds.
    /// </summary>
    public const int DefaultSpeed = 8;

    /// <summary>Note index 0 is C-2; the text spells octaves in scientific pitch notation.</summary>
    public const int LowestOctave = 2;

    private static readonly string[] WaveNames = ["p12", "p25", "sqr", "tri", "saw", "noi"];

    private static readonly string[] EffectNames = ["-", "slide", "vib", "drop", "fadein", "fadeout", "arp"];

    /// <summary>Semitone offset inside an octave for the seven letters, indexed by letter - 'A'.</summary>
    private static readonly int[] LetterSemitones = [9, 11, 0, 2, 4, 5, 7];

    /// <summary>The one spelling of each pitch class; index is the semitone inside the octave.</summary>
    private static readonly string[] PitchClassNames =
        ["C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-"];

    /// <summary>
    /// Compiles <c>sfx.txt</c> into a 4352-byte SFX payload. <paramref name="sourceName"/> is the
    /// name that prefixes diagnostics — the file name as the author typed it, not a temp path.
    /// </summary>
    public static byte[] CompileSfx(string text, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] payload = AudioFormat.EmptySfxPayload();
        int[] definedAt = new int[AudioFormat.SfxSlotCount];
        var block = new SfxBlock();
        string[] lines = SplitLines(text);

        for (int i = 0; i < lines.Length; i++)
        {
            int line = i + 1;
            string[] tokens = Tokenize(lines[i]);
            if (tokens.Length == 0)
            {
                continue;
            }

            if (char.IsAsciiDigit(tokens[0][0]))
            {
                ParseStepRow(payload, block, tokens, sourceName, line);
                continue;
            }

            switch (tokens[0].ToLowerInvariant())
            {
                case "sfx":
                {
                    FinishBlock(payload, block, sourceName);
                    int slot = ParseIndex(tokens, 1, AudioFormat.SfxSlotCount - 1, "sfx slot", sourceName, line,
                        "sfx <id>");
                    RequireTokenCount(tokens, 2, "sfx <id>", sourceName, line);
                    if (definedAt[slot] != 0)
                    {
                        throw Error(sourceName, line, $"sfx {slot} is already defined on line {definedAt[slot]}.");
                    }
                    definedAt[slot] = line;
                    block.Reset(slot, line);
                    break;
                }
                case "speed":
                {
                    RequireBlock(block, sourceName, line, "speed");
                    RequireTokenCount(tokens, 2, "speed <ticks per step>", sourceName, line);
                    block.Speed = ParseIndex(tokens, 1, 255, "speed", sourceName, line, "speed <ticks per step>");
                    if (block.Speed == 0)
                    {
                        throw Error(sourceName, line, "speed 0 would freeze the sound; speed is 1..255 console ticks per step.");
                    }
                    break;
                }
                case "length":
                {
                    RequireBlock(block, sourceName, line, "length");
                    RequireTokenCount(tokens, 2, "length <steps>", sourceName, line);
                    block.Length = ParseIndex(tokens, 1, AudioFormat.SfxStepCount, "length", sourceName, line,
                        "length <steps>");
                    if (block.Length == 0)
                    {
                        throw Error(sourceName, line,
                            $"length 0 means an empty slot; delete the whole 'sfx {block.Slot}' block instead.");
                    }
                    block.LengthLine = line;
                    break;
                }
                case "loop":
                {
                    RequireBlock(block, sourceName, line, "loop");
                    RequireTokenCount(tokens, 3, "loop <first step> <one past last step>", sourceName, line);
                    block.LoopStart = ParseIndex(tokens, 1, AudioFormat.SfxStepCount - 1, "loop start", sourceName,
                        line, "loop <first step> <one past last step>");
                    block.LoopEnd = ParseIndex(tokens, 2, AudioFormat.SfxStepCount, "loop end", sourceName, line,
                        "loop <first step> <one past last step>");
                    block.LoopLine = line;
                    break;
                }
                default:
                    throw Error(sourceName, line,
                        $"unknown directive '{tokens[0]}' (expected sfx, speed, length or loop; a line that starts "
                        + "with a digit is a step row).");
            }
        }

        FinishBlock(payload, block, sourceName);
        // The compiler's output must be something the loader accepts — if this ever fires it is a
        // bug here, not in the author's file, and it is much better found now than at load time.
        AudioFormat.ValidateSfxPayload(payload, sourceName);
        return payload;
    }

    /// <summary>Compiles <c>music.txt</c> into a 320-byte music payload.</summary>
    public static byte[] CompileMusic(string text, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(text);
        const string RowShape = "<pattern> <ch0> <ch1> <ch2> <ch3> [loop-start] [loop-end] [stop]";
        byte[] payload = AudioFormat.EmptyMusicPayload();
        int[] definedAt = new int[AudioFormat.MusicPatternCount];
        int lastPattern = -1;
        string[] lines = SplitLines(text);

        for (int i = 0; i < lines.Length; i++)
        {
            int line = i + 1;
            string[] tokens = Tokenize(lines[i]);
            if (tokens.Length == 0)
            {
                continue;
            }
            if (!char.IsAsciiDigit(tokens[0][0]))
            {
                throw Error(sourceName, line,
                    $"'{tokens[0]}' is not a pattern number; music.txt has no directives, every line is "
                    + $"'{RowShape}'.");
            }

            int pattern = ParseIndex(tokens, 0, AudioFormat.MusicPatternCount - 1, "pattern", sourceName, line, RowShape);
            if (definedAt[pattern] != 0)
            {
                throw Error(sourceName, line, $"pattern {pattern} is already defined on line {definedAt[pattern]}.");
            }
            if (pattern < lastPattern)
            {
                throw Error(sourceName, line,
                    $"pattern {pattern} comes after pattern {lastPattern}; rows go in ascending order.");
            }
            if (tokens.Length < 1 + AudioFormat.MusicChannelCount)
            {
                throw Error(sourceName, line,
                    $"a pattern row needs {AudioFormat.MusicChannelCount} channel columns ('--' for a silent "
                    + $"channel): '{RowShape}'.");
            }

            for (int channel = 0; channel < AudioFormat.MusicChannelCount; channel++)
            {
                string token = tokens[1 + channel];
                int slot;
                if (token == "--")
                {
                    slot = -1;
                }
                else if (!char.IsAsciiDigit(token[0]))
                {
                    throw Error(sourceName, line,
                        $"channel {channel} '{token}': write an SFX slot number 0..{AudioFormat.SfxSlotCount - 1}, "
                        + "or '--' for a channel that stays silent in this pattern.");
                }
                else
                {
                    slot = ParseIndex(tokens, 1 + channel, AudioFormat.SfxSlotCount - 1, $"channel {channel}",
                        sourceName, line, RowShape);
                }
                AudioFormat.WritePatternChannel(payload, pattern, channel, slot);
            }

            byte flags = 0;
            for (int t = 1 + AudioFormat.MusicChannelCount; t < tokens.Length; t++)
            {
                byte flag = tokens[t].ToLowerInvariant() switch
                {
                    "loop-start" => AudioFormat.PatternFlagLoopStart,
                    "loop-end" => AudioFormat.PatternFlagLoopEnd,
                    "stop" => AudioFormat.PatternFlagStop,
                    _ => throw Error(sourceName, line,
                        $"unknown flag '{tokens[t]}' (expected loop-start, loop-end or stop)."),
                };
                if ((flags & flag) != 0)
                {
                    throw Error(sourceName, line, $"flag '{tokens[t]}' is repeated.");
                }
                flags |= flag;
            }
            AudioFormat.WritePatternFlags(payload, pattern, flags);

            definedAt[pattern] = line;
            lastPattern = pattern;
        }

        AudioFormat.ValidateMusicPayload(payload, sourceName);
        return payload;
    }

    /// <summary>Wave index for a text name, or -1.</summary>
    public static int ParseWave(string name) => IndexOfName(WaveNames, name);

    /// <summary>Effect index for a text name, or -1.</summary>
    public static int ParseEffect(string name) => IndexOfName(EffectNames, name);

    /// <summary>
    /// Note index for a name like <c>C-4</c> or <c>F#5</c>, or -1 when the text is not a note at
    /// all; <paramref name="outOfRange"/> tells the two apart, so the caller can say "that is not
    /// a note" and "that note is too low" differently.
    /// </summary>
    public static int ParseNote(string name, out bool outOfRange)
    {
        outOfRange = false;
        if (name.Length != 3)
        {
            return -1;
        }
        char letter = char.ToUpperInvariant(name[0]);
        if (letter < 'A' || letter > 'G')
        {
            return -1;
        }
        int accidental = name[1] switch { '-' => 0, '#' => 1, _ => -1 };
        if (accidental < 0 || !char.IsAsciiDigit(name[2]))
        {
            return -1;
        }
        int octave = name[2] - '0';
        int index = ((octave - LowestOctave) * 12) + LetterSemitones[letter - 'A'] + accidental;
        if (index < 0 || index > AudioFormat.MaxNote)
        {
            outOfRange = true;
            return -1;
        }
        return index;
    }

    /// <summary>The text name of a wave index — used by diagnostics and by the tests' round trip.</summary>
    public static string WaveName(int wave) => WaveNames[wave];

    /// <summary>The text name of an effect index.</summary>
    public static string EffectName(int effect) => EffectNames[effect];

    /// <summary>
    /// The text name of a note index, e.g. 48 -> "C-6". Sharps only: the format has one spelling
    /// per pitch, because two spellings would mean two texts compiling to the same bytes and a
    /// diff that argues about enharmonics.
    /// </summary>
    public static string NoteName(int note)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, AudioFormat.MaxNote);
        return PitchClassNames[note % 12] + (char)('0' + LowestOctave + (note / 12));
    }

    private static void ParseStepRow(byte[] payload, SfxBlock block, string[] tokens, string sourceName, int line)
    {
        const string RowShape = "<step> <note> <wave> <volume> <effect>";
        if (block.Slot < 0)
        {
            throw Error(sourceName, line,
                "a step row needs an 'sfx <id>' header above it — this row does not belong to any slot.");
        }
        int step = ParseIndex(tokens, 0, AudioFormat.SfxStepCount - 1, "step", sourceName, line, RowShape);
        if (step <= block.LastStep)
        {
            throw Error(sourceName, line,
                $"step {step} comes after step {block.LastStep}; rows go in ascending order, and each step is "
                + "written at most once.");
        }

        ushort word;
        if (tokens.Length == 2)
        {
            if (tokens[1] != "---")
            {
                throw Error(sourceName, line,
                    $"'{tokens[1]}' is not a rest; a rest row is '<step> ---', a note row is '{RowShape}'.");
            }
            word = 0;   // A rest: no note, no wave, no volume, no effect — the zero word.
        }
        else if (tokens.Length == 5)
        {
            int note = ParseNote(tokens[1], out bool outOfRange);
            if (note < 0)
            {
                throw Error(sourceName, line, outOfRange
                    ? $"note '{tokens[1]}' is outside the profile-8 range {NoteName(0)} .. "
                        + $"{NoteName(AudioFormat.MaxNote)}."
                    : $"'{tokens[1]}' is not a note: write a letter A-G, then '-' or '#', then the octave digit "
                        + $"({NoteName(0)} .. {NoteName(AudioFormat.MaxNote)}); a rest row is '<step> ---'.");
            }
            int wave = ParseWave(tokens[2]);
            if (wave < 0)
            {
                throw Error(sourceName, line,
                    $"unknown wave '{tokens[2]}' (expected {string.Join(", ", WaveNames)}).");
            }
            int volume = ParseIndex(tokens, 3, AudioFormat.MaxVolume, "volume", sourceName, line, RowShape);
            int effect = ParseEffect(tokens[4]);
            if (effect < 0)
            {
                throw Error(sourceName, line,
                    $"unknown effect '{tokens[4]}' (expected {string.Join(", ", EffectNames)}; '-' is no effect).");
            }
            if (volume == 0)
            {
                // Volume 0 silences the step whatever else is written, so the bank stores one
                // word for it and one only (docs/AUDIO-FORMAT.md §3). Compiling this row into
                // that word would quietly drop the note — and quietly change the sound, since a
                // rest's note is where the next slide starts. So it is refused instead, at the
                // line the author can see, exactly as a flat or an unknown wave is.
                throw Error(sourceName, line,
                    $"volume 0 is a rest, and a rest is written '{tokens[0]} ---': the bank keeps no note, wave or "
                    + "effect for a step nobody hears, so a silenced note has to lose them here too.");
            }
            word = AudioFormat.PackStep(note, wave, volume, effect);
        }
        else
        {
            throw Error(sourceName, line,
                $"a step row is '{RowShape}' (5 columns) or '<step> ---' for a rest, got {tokens.Length} columns.");
        }

        AudioFormat.WriteStep(payload, block.Slot, step, word);
        block.LastStep = step;
    }

    /// <summary>Writes the slot header of the block being closed and checks the directives against each other.</summary>
    private static void FinishBlock(byte[] payload, SfxBlock block, string sourceName)
    {
        if (block.Slot < 0)
        {
            return;
        }
        if (block.LastStep < 0)
        {
            throw Error(sourceName, block.HeaderLine,
                $"sfx {block.Slot} has no step rows; an empty slot is written by leaving the block out entirely.");
        }
        int length = block.Length > 0 ? block.Length : block.LastStep + 1;
        if (length < block.LastStep + 1)
        {
            throw Error(sourceName, block.LengthLine,
                $"length {length} is shorter than the last step row ({block.LastStep}) of sfx {block.Slot}.");
        }
        if (block.LoopEnd != 0 && (block.LoopStart >= block.LoopEnd || block.LoopEnd > length))
        {
            throw Error(sourceName, block.LoopLine,
                $"loop {block.LoopStart} {block.LoopEnd} must satisfy start < end <= length ({length}) in "
                + $"sfx {block.Slot}; the range is half-open, so 'loop 2 6' repeats steps 2..5.");
        }
        if (block.LoopEnd == 0 && block.LoopStart != 0)
        {
            throw Error(sourceName, block.LoopLine,
                $"loop {block.LoopStart} {block.LoopEnd}: a loop that ends at step 0 is not a loop.");
        }
        AudioFormat.WriteSlotHeader(payload, block.Slot, block.Speed, length, block.LoopStart, block.LoopEnd);
        block.Slot = -1;
    }

    private static void RequireBlock(SfxBlock block, string sourceName, int line, string directive)
    {
        if (block.Slot < 0)
        {
            throw Error(sourceName, line, $"'{directive}' applies to a slot, so it needs an 'sfx <id>' header above it.");
        }
    }

    private static void RequireTokenCount(string[] tokens, int expected, string shape, string sourceName, int line)
    {
        if (tokens.Length != expected)
        {
            throw Error(sourceName, line, $"'{shape}' takes {expected - 1} argument(s), got {tokens.Length - 1}.");
        }
    }

    private static int ParseIndex(
        string[] tokens, int position, int max, string what, string sourceName, int line, string shape)
    {
        if (position >= tokens.Length)
        {
            throw Error(sourceName, line, $"missing {what}: the line reads '{shape}'.");
        }
        string token = tokens[position];
        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw Error(sourceName, line, $"{what} '{token}' is not a decimal number (0..{max}).");
        }
        if (value > max)
        {
            throw Error(sourceName, line, $"{what} {value} is out of range 0..{max}.");
        }
        return value;
    }

    private static int IndexOfName(string[] names, string token)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], token, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Splits into lines the same way on every platform. <see cref="string.ReplaceLineEndings(string)"/>
    /// folds CRLF, CR and the exotic Unicode terminators into "\n" first, so a file saved on
    /// Windows and the same file saved on Linux compile to the same bytes and report the same
    /// line numbers.
    /// </summary>
    private static string[] SplitLines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// Strips the comment and splits on whitespace. A '#' only starts a comment where a token
    /// starts — at the beginning of the line or after whitespace — which is what keeps
    /// <c>C#4</c> a note.
    /// </summary>
    private static string[] Tokenize(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                line = line[..i];
                break;
            }
        }
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static CartLoadException Error(string sourceName, int line, string message) =>
        new($"{sourceName}:{line}: {message}");

    /// <summary>The directives seen since the current <c>sfx</c> header, resolved when the block closes.</summary>
    private sealed class SfxBlock
    {
        public int Slot = -1;
        public int HeaderLine;
        public int Speed = DefaultSpeed;
        public int Length;
        public int LengthLine;
        public int LoopStart;
        public int LoopEnd;
        public int LoopLine;
        public int LastStep = -1;

        public void Reset(int slot, int headerLine)
        {
            Slot = slot;
            HeaderLine = headerLine;
            Speed = DefaultSpeed;
            Length = 0;
            LengthLine = headerLine;
            LoopStart = 0;
            LoopEnd = 0;
            LoopLine = headerLine;
            LastStep = -1;
        }
    }
}

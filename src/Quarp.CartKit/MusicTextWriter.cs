using System.Globalization;
using System.Text;

namespace Quarp.CartKit;

/// <summary>
/// Writes a music payload back out as the <c>music.txt</c> a person edits — the direction
/// <c>sfx.txt</c> deliberately does not have (docs/AUDIO-FORMAT.md §1: "обратной операции нет и
/// не планируется").
///
/// <para><b>Why music gets one anyway.</b> Two jobs need it and neither is "decompile the
/// cartridge". The first is the test that keeps the two halves honest — text to bank to text to
/// bank, compared byte for byte, which is the only cheap way to prove that the grammar can spell
/// everything the layout can hold. The second is the tracker of the next wave: it edits the bank
/// and will want to write the source beside it.</para>
///
/// <para>The output is <b>canonical</b>: the same payload always produces the same characters,
/// with no clock, no culture and no dictionary iteration in the way, so a round trip is a
/// comparison and not a judgement call.</para>
/// </summary>
public static class MusicTextWriter
{
    /// <summary>Line endings the writer emits: "\n" on every platform, so a file written on Windows and one written on Linux are the same bytes.</summary>
    public const string NewLine = "\n";

    /// <summary>Renders a validated music payload as <c>music.txt</c>.</summary>
    public static string Write(ReadOnlySpan<byte> payload)
    {
        MusicFormat.ValidatePayload(payload, "music.bin");
        var text = new StringBuilder();
        text.Append("# music.txt — Quarp audio format version 0 (docs/AUDIO-FORMAT.md §6).").Append(NewLine);
        text.Append("version 0").Append(NewLine);

        WriteInstruments(text, payload);
        WriteOrder(text, payload);
        WritePatterns(text, payload);
        return text.ToString();
    }

    private static void WriteInstruments(StringBuilder text, ReadOnlySpan<byte> payload)
    {
        bool[] used = UsedInstruments(payload);
        bool any = false;
        for (int i = 0; i < MusicFormat.InstrumentCount; i++)
        {
            int slot = MusicFormat.InstrumentSlot(payload, i);
            int root = MusicFormat.InstrumentRoot(payload, i);
            byte flags = MusicFormat.InstrumentFlags(payload, i);
            int speed = MusicFormat.InstrumentSpeed(payload, i);
            // An all-zero record is a legal instrument (slot 0 rooted at C-2), so silence is not a
            // reason to skip a line — being unreferenced and unconfigured is. Skipping those keeps
            // the file the size of the song rather than the size of the table, and skipping them
            // costs nothing: an instrument nobody writes compiles back to the zeros it came from.
            if (!used[i] && slot == 0 && root == 0 && flags == 0 && speed == 0)
            {
                continue;
            }
            if (!any)
            {
                text.Append(NewLine);
                text.Append("# instruments — an SFX slot used as a timbre, plus the note it is written at")
                    .Append(NewLine);
                any = true;
            }
            text.Append("inst ").Append(Two(i))
                .Append(" sfx ").Append(Two(slot))
                .Append(" root ").Append(AudioTextCompiler.NoteName(root));
            if ((flags & MusicFormat.InstrumentOnce) != 0)
            {
                text.Append(" once");
            }
            if (speed != 0)
            {
                text.Append(" speed ").Append(speed.ToString(CultureInfo.InvariantCulture));
            }
            text.Append(NewLine);
        }
    }

    private static void WriteOrder(StringBuilder text, ReadOnlySpan<byte> payload)
    {
        int length = MusicFormat.OrderLength(payload);
        text.Append(NewLine);
        text.Append("# order — which pattern plays, in which key, and what happens when it ends").Append(NewLine);
        text.Append("order").Append(NewLine);
        text.Append("#  ##  pat  flags").Append(NewLine);
        for (int i = 0; i < length; i++)
        {
            text.Append("   ").Append(Two(i))
                .Append("  ").Append(Two(MusicFormat.OrderPattern(payload, i)));
            byte flags = MusicFormat.OrderFlags(payload, i);
            var tail = new StringBuilder();
            if ((flags & MusicFormat.OrderLoopStart) != 0)
            {
                tail.Append(" loop-start");
            }
            if ((flags & MusicFormat.OrderJump) != 0)
            {
                tail.Append(" jump ").Append(Two(MusicFormat.OrderTarget(payload, i)));
            }
            if ((flags & MusicFormat.OrderLoopBack) != 0)
            {
                tail.Append(" loop-back");
            }
            if ((flags & MusicFormat.OrderStop) != 0)
            {
                tail.Append(" stop");
            }
            int transpose = MusicFormat.OrderTranspose(payload, i);
            if (transpose != 0)
            {
                tail.Append(" transpose ")
                    .Append(transpose > 0 ? "+" : string.Empty)
                    .Append(transpose.ToString(CultureInfo.InvariantCulture));
            }
            if (tail.Length > 0)
            {
                text.Append("  ").Append(tail);
            }
            text.Append(NewLine);
        }
    }

    private static void WritePatterns(StringBuilder text, ReadOnlySpan<byte> payload)
    {
        for (int pattern = 0; pattern < AudioFormat.MusicPatternCount; pattern++)
        {
            int rows = MusicFormat.PatternRows(payload, pattern);
            if (rows == 0)
            {
                continue;
            }
            text.Append(NewLine);
            text.Append("pattern ").Append(Two(pattern))
                .Append(" rows ").Append(rows.ToString(CultureInfo.InvariantCulture))
                .Append(" speed ").Append(MusicTextCompiler.SpeedText(MusicFormat.PatternSpeed(payload, pattern)))
                .Append(NewLine);
            text.Append("#  row   ch0              ch1              ch2              ch3").Append(NewLine);
            for (int row = 0; row < rows; row++)
            {
                if (RowIsEmpty(payload, pattern, row))
                {
                    // Rows may be sparse, exactly as step rows may be in sfx.txt: a row nobody
                    // wrote is a row where nothing happens, and 'rows' above says how many there
                    // are. A pattern of two chords is two lines, not thirty-two.
                    continue;
                }
                text.Append("   ").Append(Two(row)).Append("   ");
                for (int channel = 0; channel < AudioFormat.MusicChannelCount; channel++)
                {
                    if (channel > 0)
                    {
                        text.Append("|  ");
                    }
                    string cell = CellText(MusicFormat.Cell(payload, pattern, row, channel));
                    text.Append(cell);
                    text.Append(' ', Math.Max(1, 16 - cell.Length));
                }
                TrimEnd(text);
                text.Append(NewLine);
            }
        }
    }

    /// <summary>One cell as the grammar spells it: "." for empty, "===" for a note off, four columns otherwise.</summary>
    public static string CellText(uint cell)
    {
        if (cell == 0)
        {
            return MusicTextCompiler.EmptyCellToken;
        }
        int kind = MusicFormat.CellNoteKind(cell);
        if (kind == MusicFormat.NoteOff)
        {
            return MusicTextCompiler.NoteOffToken;
        }
        string note = kind == MusicFormat.NoteOn
            ? AudioTextCompiler.NoteName(MusicFormat.CellNote(cell))
            : MusicTextCompiler.NoNoteToken;
        string instrument = MusicFormat.CellHasInstrument(cell)
            ? Two(MusicFormat.CellInstrument(cell))
            : MusicTextCompiler.NoInstrumentToken;
        string volume = MusicFormat.CellHasVolume(cell)
            ? MusicFormat.CellVolume(cell).ToString(CultureInfo.InvariantCulture)
            : MusicTextCompiler.NoVolumeToken;
        int effect = MusicFormat.CellEffect(cell);
        string effectText = effect == MusicFormat.EffectNone
            ? MusicTextCompiler.NoEffectToken
            : MusicTextCompiler.EffectName(effect) + ":" + ParamText(effect, MusicFormat.CellParam(cell));
        return $"{note} {instrument} {volume} {effectText}";
    }

    private static string ParamText(int effect, int param) =>
        effect == MusicFormat.EffectArpeggio
            ? param.ToString("x2", CultureInfo.InvariantCulture)
            : param.ToString(CultureInfo.InvariantCulture);

    private static bool RowIsEmpty(ReadOnlySpan<byte> payload, int pattern, int row)
    {
        for (int channel = 0; channel < AudioFormat.MusicChannelCount; channel++)
        {
            if (MusicFormat.Cell(payload, pattern, row, channel) != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static bool[] UsedInstruments(ReadOnlySpan<byte> payload)
    {
        bool[] used = new bool[MusicFormat.InstrumentCount];
        for (int pattern = 0; pattern < AudioFormat.MusicPatternCount; pattern++)
        {
            for (int row = 0; row < MusicFormat.RowCount; row++)
            {
                for (int channel = 0; channel < AudioFormat.MusicChannelCount; channel++)
                {
                    uint cell = MusicFormat.Cell(payload, pattern, row, channel);
                    if (MusicFormat.CellHasInstrument(cell))
                    {
                        used[MusicFormat.CellInstrument(cell)] = true;
                    }
                }
            }
        }
        return used;
    }

    private static void TrimEnd(StringBuilder text)
    {
        while (text.Length > 0 && text[^1] == ' ')
        {
            text.Length--;
        }
    }

    private static string Two(int value) => value.ToString("D2", CultureInfo.InvariantCulture);
}

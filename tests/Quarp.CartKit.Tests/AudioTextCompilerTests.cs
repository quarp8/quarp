using System.Text;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The author-facing audio text (docs/AUDIO-FORMAT.md §6): the example printed in the document
/// is compiled here, so "a complete example that actually compiles" is a fact and not a claim;
/// the musical content survives text -> binary -> text -> binary unchanged; and every malformed
/// input produces a diagnostic that names the line.
///
/// <para>The round trip carries its own negative control: the same test changes one note in the
/// source and asserts the compiled bytes move. A round-trip test over a compiler that dropped
/// its input on the floor would otherwise pass with flying colours.</para>
/// </summary>
public class AudioTextCompilerTests
{
    /// <summary>Verbatim from docs/AUDIO-FORMAT.md §6 — if the document changes, this test tells.</summary>
    private const string DocumentedSfxText = """
        # Банк эффектов змейки.

        sfx 0                     # яблоко: короткий писк вверх
          speed 3                 # 3 тика на шаг = 20 шагов в секунду
          00 C-6 sqr 6 -
          01 E-6 sqr 5 -
          02 G-6 sqr 4 fadeout

        sfx 1                     # смерть: падающий шум
          speed 6
          00 A-4 noi 7 drop
          01 ---
          02 D-3 noi 5 fadeout

        sfx 2                     # бас темы, крутится сам по себе
          speed 8
          loop 0 4
          00 C-3 tri 4 -
          01 ---
          02 G-3 tri 3 -
          03 ---

        sfx 3                     # лид темы
          speed 8
          00 E-5 p25 5 -
          01 G-5 p25 4 -
          02 C-6 p25 5 arp
          03 ---
        """;

    /// <summary>Verbatim from docs/AUDIO-FORMAT.md §6.</summary>
    private const string DocumentedMusicText = """
        # Порядок паттернов: бас один такт, потом бас с лидом, и обратно.
        #  P  ch0 ch1 ch2 ch3   флаги

           0   02  --  --  --   loop-start
           1   02  03  --  --
           2   02  03  --  --   loop-end
        """;

    private static byte[] CompileSfx(string text) => AudioTextCompiler.CompileSfx(text, "sfx.txt");

    private static byte[] CompileMusic(string text) => AudioTextCompiler.CompileMusic(text, "music.txt");

    private static CartLoadException SfxFails(string text) =>
        Assert.Throws<CartLoadException>(() => CompileSfx(text));

    private static CartLoadException MusicFails(string text) =>
        Assert.Throws<CartLoadException>(() => CompileMusic(text));

    // --- the documented example ---

    [Fact]
    public void TheDocumentedSfxExampleCompilesToWhatItSays()
    {
        byte[] payload = CompileSfx(DocumentedSfxText);

        // Slot 0: speed 3, three steps, no loop, and the very word the hexdump in §9 shows.
        Assert.Equal(3, AudioFormat.SlotSpeed(payload, 0));
        Assert.Equal(3, AudioFormat.SlotLength(payload, 0));
        Assert.Equal(0, AudioFormat.SlotLoopEnd(payload, 0));
        Assert.Equal(0x0cb0, AudioFormat.Step(payload, 0, 0));
        Assert.Equal(0x0ab4, AudioFormat.Step(payload, 0, 1));
        Assert.Equal(0x58b7, AudioFormat.Step(payload, 0, 2));

        // Slot 1: the rest in the middle is the zero word, and length still counts it.
        Assert.Equal(3, AudioFormat.SlotLength(payload, 1));
        Assert.Equal(0, AudioFormat.Step(payload, 1, 1));
        Assert.Equal(AudioFormat.WaveNoise, AudioFormat.Wave(AudioFormat.Step(payload, 1, 0)));
        Assert.Equal(AudioFormat.EffectDrop, AudioFormat.Effect(AudioFormat.Step(payload, 1, 0)));

        // Slot 2 loops over its whole four steps.
        Assert.Equal(4, AudioFormat.SlotLength(payload, 2));
        Assert.Equal(0, AudioFormat.SlotLoopStart(payload, 2));
        Assert.Equal(4, AudioFormat.SlotLoopEnd(payload, 2));

        // Slot 3 exists, slots 4..63 do not.
        Assert.Equal(4, AudioFormat.SlotLength(payload, 3));
        for (int slot = 4; slot < AudioFormat.SfxSlotCount; slot++)
        {
            Assert.Equal(0, AudioFormat.SlotLength(payload, slot));
        }
    }

    [Fact]
    public void TheDocumentedMusicExampleCompilesToWhatItSays()
    {
        byte[] payload = CompileMusic(DocumentedMusicText);

        Assert.Equal(2, AudioFormat.PatternChannel(payload, 0, 0));
        Assert.Equal(-1, AudioFormat.PatternChannel(payload, 0, 1));
        Assert.Equal(AudioFormat.PatternFlagLoopStart, AudioFormat.PatternFlags(payload, 0));
        Assert.Equal(3, AudioFormat.PatternChannel(payload, 1, 1));
        Assert.Equal(0, AudioFormat.PatternFlags(payload, 1));
        Assert.Equal(AudioFormat.PatternFlagLoopEnd, AudioFormat.PatternFlags(payload, 2));
        Assert.True(AudioFormat.PatternIsEmpty(payload, 3));
    }

    [Fact]
    public void TheDocumentedExampleIsExactlyWhatTheBinaryExampleShows()
    {
        // §9 shows the same three steps as a hexdump; compiling the text must produce those bytes.
        byte[] file = AudioFormat.WriteSfxFile(CompileSfx("""
            sfx 0
              speed 3
              00 C-6 sqr 6 -
              01 E-6 sqr 5 -
              02 G-6 sqr 4 fadeout
            """));
        Assert.Equal(new byte[] { 0x03, 0x03, 0x00, 0x00 }, file[8..12]);
        Assert.Equal(new byte[] { 0xb0, 0x0c, 0xb4, 0x0a, 0xb7, 0x58 }, file[0x108..0x10e]);
    }

    // --- round trip ---

    [Fact]
    public void SfxSurvivesTextToBinaryToTextToBinary()
    {
        byte[] first = CompileSfx(DocumentedSfxText);
        byte[] second = CompileSfx(WriteSfxText(first));
        Assert.Equal(first, second);

        // Negative control: the round trip must actually be carrying the notes. One changed
        // note in the source has to move the bytes, or the comparison above proves nothing.
        byte[] altered = CompileSfx(DocumentedSfxText.Replace("01 E-6 sqr 5 -", "01 F-6 sqr 5 -"));
        Assert.NotEqual(first, altered);
        Assert.Equal(altered, CompileSfx(WriteSfxText(altered)));
    }

    [Fact]
    public void MusicSurvivesTextToBinaryToTextToBinary()
    {
        byte[] first = CompileMusic(DocumentedMusicText);
        byte[] second = CompileMusic(WriteMusicText(first));
        Assert.Equal(first, second);

        byte[] altered = CompileMusic(DocumentedMusicText.Replace("1   02  03  --  --", "1   02  01  --  --"));
        Assert.NotEqual(first, altered);
        Assert.Equal(altered, CompileMusic(WriteMusicText(altered)));
    }

    [Fact]
    public void EveryNoteNameRoundTripsThroughItsIndex()
    {
        for (int note = 0; note <= AudioFormat.MaxNote; note++)
        {
            string name = AudioTextCompiler.NoteName(note);
            Assert.Equal(note, AudioTextCompiler.ParseNote(name, out bool outOfRange));
            Assert.False(outOfRange);
        }
        Assert.Equal("C-2", AudioTextCompiler.NoteName(0));
        Assert.Equal("D#7", AudioTextCompiler.NoteName(AudioFormat.MaxNote));
    }

    // --- grammar details that are easy to get wrong ---

    [Fact]
    public void ASharpIsANoteAndNotTheStartOfAComment()
    {
        byte[] payload = CompileSfx("""
            sfx 0
              00 C#4 tri 7 -   # a real comment, this one
            """);
        Assert.Equal(AudioTextCompiler.ParseNote("C#4", out _), AudioFormat.Note(AudioFormat.Step(payload, 0, 0)));
        Assert.Equal(7, AudioFormat.Volume(AudioFormat.Step(payload, 0, 0)));
    }

    [Fact]
    public void KeywordsAndNamesAreCaseInsensitive()
    {
        byte[] lower = CompileSfx("sfx 0\n  speed 5\n  00 c-4 tri 3 fadein\n");
        byte[] upper = CompileSfx("SFX 0\n  SPEED 5\n  00 C-4 TRI 3 FADEIN\n");
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void RowsMayBeSparseAndTheGapsAreRests()
    {
        byte[] payload = CompileSfx("sfx 0\n  00 C-4 tri 3 -\n  05 C-4 tri 3 -\n");
        Assert.Equal(6, AudioFormat.SlotLength(payload, 0));
        Assert.Equal(0, AudioFormat.Step(payload, 0, 3));
        Assert.NotEqual(0, AudioFormat.Step(payload, 0, 5));
    }

    [Fact]
    public void SpeedDefaultsToTheTrackerRowAndLengthCanBeExtended()
    {
        byte[] payload = CompileSfx("sfx 0\n  length 8\n  00 C-4 tri 3 -\n");
        Assert.Equal(AudioTextCompiler.DefaultSpeed, AudioFormat.SlotSpeed(payload, 0));
        Assert.Equal(8, AudioFormat.SlotLength(payload, 0));
    }

    [Fact]
    public void CommentsAndBlankLinesCompileToTheEmptyBank()
    {
        Assert.Equal(AudioFormat.EmptySfxPayload(), CompileSfx("# nothing here\n\n   \n"));
        Assert.Equal(AudioFormat.EmptyMusicPayload(), CompileMusic("# nothing here\n\n"));
    }

    [Fact]
    public void LineEndingsDoNotChangeTheOutput()
    {
        byte[] lf = CompileSfx("sfx 0\n  00 C-4 tri 3 -\n");
        byte[] crlf = CompileSfx("sfx 0\r\n  00 C-4 tri 3 -\r\n");
        Assert.Equal(lf, crlf);
    }

    // --- diagnostics: every one names the line ---

    [Fact]
    public void UnknownWaveNamesTheLine()
    {
        var e = SfxFails("sfx 0\n  00 C-4 sq2 3 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("sq2", e.Message);
        Assert.Contains("sqr", e.Message);
    }

    [Fact]
    public void UnknownEffectNamesTheLine()
    {
        var e = SfxFails("sfx 0\n  00 C-4 tri 3 wobble\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("wobble", e.Message);
    }

    [Fact]
    public void NoteOutOfRangeIsToldApartFromNonsense()
    {
        var tooHigh = SfxFails("sfx 0\n  00 E-7 tri 3 -\n");
        Assert.StartsWith("sfx.txt:2:", tooHigh.Message);
        Assert.Contains("outside the profile-8 range", tooHigh.Message);

        var nonsense = SfxFails("sfx 0\n  00 Hb9 tri 3 -\n");
        Assert.StartsWith("sfx.txt:2:", nonsense.Message);
        Assert.Contains("is not a note", nonsense.Message);

        // Control: the notes just inside the range are accepted.
        CompileSfx("sfx 0\n  00 C-2 tri 3 -\n  01 D#7 tri 3 -\n");
    }

    [Fact]
    public void FlatsAreRefusedRatherThanGuessed()
    {
        var e = SfxFails("sfx 0\n  00 Db4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
    }

    [Fact]
    public void VolumeOutOfRangeNamesTheLine()
    {
        var e = SfxFails("sfx 0\n  00 C-4 tri 9 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("volume 9", e.Message);
    }

    [Fact]
    public void VolumeZeroInANoteRowIsRefusedBecauseARestHasOneSpelling()
    {
        // The bank stores a rest as the zero word and nothing else (§3), so this row could only
        // compile by throwing the note away — and that would change the sound quietly, since a
        // rest's note is where the next slide starts. Refused at the line, like a flat.
        var e = SfxFails("sfx 0\n  00 C-4 tri 0 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("---", e.Message);

        // Control: the rest that row was trying to be, and the same note one volume step up.
        byte[] payload = CompileSfx("sfx 0\n  00 ---\n  01 C-4 tri 1 -\n");
        Assert.Equal(0, AudioFormat.Step(payload, 0, 0));
        Assert.Equal(1, AudioFormat.Volume(AudioFormat.Step(payload, 0, 1)));
        Assert.Equal(2, AudioFormat.SlotLength(payload, 0));
    }

    [Fact]
    public void OutOfOrderStepsAreRefused()
    {
        var e = SfxFails("sfx 0\n  00 C-4 tri 3 -\n  05 C-4 tri 3 -\n  03 C-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:4:", e.Message);
        Assert.Contains("ascending", e.Message);
    }

    [Fact]
    public void ARepeatedStepIsRefused()
    {
        var e = SfxFails("sfx 0\n  00 C-4 tri 3 -\n  00 D-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:3:", e.Message);
    }

    [Fact]
    public void ARepeatedSlotNamesBothLines()
    {
        var e = SfxFails("sfx 0\n  00 C-4 tri 3 -\nsfx 0\n  00 D-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:3:", e.Message);
        Assert.Contains("line 1", e.Message);
    }

    [Fact]
    public void AStepRowOutsideABlockIsRefused()
    {
        var e = SfxFails("  00 C-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:1:", e.Message);
        Assert.Contains("sfx <id>", e.Message);
    }

    [Fact]
    public void AnUnknownDirectiveIsRefused()
    {
        var e = SfxFails("sfx 0\n  tempo 120\n  00 C-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("tempo", e.Message);
    }

    [Fact]
    public void AWrongColumnCountIsRefused()
    {
        var e = SfxFails("sfx 0\n  00 C-4 tri 3\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("columns", e.Message);

        var rest = SfxFails("sfx 0\n  00 --\n");
        Assert.StartsWith("sfx.txt:2:", rest.Message);
    }

    [Fact]
    public void AnEmptyBlockIsRefused()
    {
        var e = SfxFails("sfx 0\n  speed 4\nsfx 1\n  00 C-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:1:", e.Message);
        Assert.Contains("no step rows", e.Message);
    }

    [Fact]
    public void ALoopOutsideTheBlockIsRefusedAtTheLoopLine()
    {
        var e = SfxFails("sfx 0\n  loop 0 8\n  00 C-4 tri 3 -\n  01 C-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("length (2)", e.Message);

        // Control: the same loop inside a long enough block is fine.
        byte[] payload = CompileSfx("sfx 0\n  loop 0 8\n  00 C-4 tri 3 -\n  07 C-4 tri 3 -\n");
        Assert.Equal(8, AudioFormat.SlotLoopEnd(payload, 0));
    }

    [Fact]
    public void ALengthShorterThanTheRowsIsRefused()
    {
        var e = SfxFails("sfx 0\n  length 2\n  00 C-4 tri 3 -\n  05 C-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
        Assert.Contains("shorter", e.Message);
    }

    [Fact]
    public void SpeedZeroIsRefused()
    {
        var e = SfxFails("sfx 0\n  speed 0\n  00 C-4 tri 3 -\n");
        Assert.StartsWith("sfx.txt:2:", e.Message);
    }

    [Fact]
    public void MusicDiagnosticsNameTheLine()
    {
        var flag = MusicFails("0 01 -- -- -- loopstart\n");
        Assert.StartsWith("music.txt:1:", flag.Message);
        Assert.Contains("loop-start", flag.Message);

        var missing = MusicFails("# header\n0 01 --\n");
        Assert.StartsWith("music.txt:2:", missing.Message);
        Assert.Contains("channel columns", missing.Message);

        var directive = MusicFails("pattern 0 01 -- -- --\n");
        Assert.StartsWith("music.txt:1:", directive.Message);
        Assert.Contains("no directives", directive.Message);

        var order = MusicFails("2 01 -- -- --\n1 01 -- -- --\n");
        Assert.StartsWith("music.txt:2:", order.Message);
        Assert.Contains("ascending", order.Message);

        var duplicate = MusicFails("1 01 -- -- --\n1 02 -- -- --\n");
        Assert.StartsWith("music.txt:2:", duplicate.Message);
        Assert.Contains("line 1", duplicate.Message);

        var channel = MusicFails("0 01 -x -- --\n");
        Assert.StartsWith("music.txt:1:", channel.Message);
        Assert.Contains("'--'", channel.Message);

        var slot = MusicFails("0 64 -- -- --\n");
        Assert.StartsWith("music.txt:1:", slot.Message);

        var repeated = MusicFails("0 01 -- -- -- stop stop\n");
        Assert.StartsWith("music.txt:1:", repeated.Message);

        // Control: the shape all of these deviate from does compile.
        CompileMusic("0 01 -- -- -- stop\n");
    }

    // --- helpers: the "back to text" half of the round trip ---

    private static string WriteSfxText(byte[] payload)
    {
        var text = new StringBuilder();
        for (int slot = 0; slot < AudioFormat.SfxSlotCount; slot++)
        {
            int length = AudioFormat.SlotLength(payload, slot);
            if (length == 0)
            {
                continue;
            }
            text.Append("sfx ").Append(slot).Append('\n');
            text.Append("  speed ").Append(AudioFormat.SlotSpeed(payload, slot)).Append('\n');
            text.Append("  length ").Append(length).Append('\n');
            int loopEnd = AudioFormat.SlotLoopEnd(payload, slot);
            if (loopEnd != 0)
            {
                text.Append("  loop ").Append(AudioFormat.SlotLoopStart(payload, slot))
                    .Append(' ').Append(loopEnd).Append('\n');
            }
            for (int step = 0; step < length; step++)
            {
                ushort word = AudioFormat.Step(payload, slot, step);
                text.Append("  ").Append(step).Append(' ');
                if (word == 0)
                {
                    text.Append("---\n");
                    continue;
                }
                text.Append(AudioTextCompiler.NoteName(AudioFormat.Note(word))).Append(' ')
                    .Append(AudioTextCompiler.WaveName(AudioFormat.Wave(word))).Append(' ')
                    .Append(AudioFormat.Volume(word)).Append(' ')
                    .Append(AudioTextCompiler.EffectName(AudioFormat.Effect(word))).Append('\n');
            }
        }
        return text.ToString();
    }

    private static string WriteMusicText(byte[] payload)
    {
        var text = new StringBuilder();
        for (int pattern = 0; pattern < AudioFormat.MusicPatternCount; pattern++)
        {
            byte flags = AudioFormat.PatternFlags(payload, pattern);
            if (AudioFormat.PatternIsEmpty(payload, pattern) && flags == 0)
            {
                continue;
            }
            text.Append(pattern);
            for (int channel = 0; channel < AudioFormat.MusicChannelCount; channel++)
            {
                int slot = AudioFormat.PatternChannel(payload, pattern, channel);
                text.Append(' ').Append(slot < 0 ? "--" : slot.ToString());
            }
            if ((flags & AudioFormat.PatternFlagLoopStart) != 0)
            {
                text.Append(" loop-start");
            }
            if ((flags & AudioFormat.PatternFlagLoopEnd) != 0)
            {
                text.Append(" loop-end");
            }
            if ((flags & AudioFormat.PatternFlagStop) != 0)
            {
                text.Append(" stop");
            }
            text.Append('\n');
        }
        return text.ToString();
    }
}

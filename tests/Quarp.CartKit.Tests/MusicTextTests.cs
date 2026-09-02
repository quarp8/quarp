using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The author-facing grammar of a song (docs/AUDIO-FORMAT.md §6, ADR-040 and ADR-041): the round
/// trip in both directions, the fractional row speed the ports asked for by name, and a refusal
/// with a line number for every way the text can be wrong.
///
/// <para>Two round trips are asserted, and they prove different things. <b>Text to bytes to
/// text</b> says the writer spells what the compiler reads. <b>Bytes to text to bytes</b> says
/// nothing is lost on the way through the page — which is the property <c>quarp audio upgrade</c>
/// and the tracker's "save" both depend on.</para>
/// </summary>
public class MusicTextTests
{
    /// <summary>
    /// A song that exercises every column of the grammar at once: two instruments (one with
    /// <c>once</c> and a speed override), an order that loops and transposes, and a pattern at a
    /// speed with a fraction in it.
    /// </summary>
    private const string Sample = """
        # a small song
        version 0

        inst 00 sfx 02 root C-4
        inst 01 sfx 03 root C-5 once speed 4

        order
           00  00   loop-start
           01  00   loop-back transpose +5

        pattern 00 rows 4 speed 7.5
        #  row   ch0              ch1
           00   C-4 00 7 ---    |  C-5 01 - arp:47  |  .  |  .
           02   D#4 -- - slide:6 | .                |  .  |  .
           03   .               |  ===              |  .  |  .
        """;

    private static byte[] Compile(string text) => MusicTextCompiler.Compile(text, "music.txt");

    private static string Rejected(string text) =>
        Assert.Throws<CartLoadException>(() => Compile(text)).Message;

    /// <summary>The sample compiles — the negative control every rejection test below leans on.</summary>
    [Fact]
    public void TheSampleCompiles()
    {
        byte[] payload = Compile(Sample);

        MusicFormat.ValidatePayload(payload, "music.txt");
        Assert.Equal(2, MusicFormat.OrderLength(payload));
        Assert.Equal(4, MusicFormat.PatternRows(payload, 0));
        Assert.Equal(240, MusicFormat.PatternSpeed(payload, 0));
        Assert.Equal(5, MusicFormat.OrderTranspose(payload, 1));
        Assert.Equal(MusicFormat.InstrumentOnce, MusicFormat.InstrumentFlags(payload, 1));
        Assert.Equal(4, MusicFormat.InstrumentSpeed(payload, 1));
    }

    /// <summary>
    /// Bytes to text to bytes is the identity. This is what makes the writer safe to point at a
    /// cartridge: whatever the tracker saves, re-reading the page gives the same bank back.
    /// </summary>
    [Fact]
    public void BytesSurviveARoundTripThroughThePage()
    {
        byte[] payload = Compile(Sample);

        Assert.Equal(payload, Compile(MusicTextWriter.Write(payload)));
    }

    /// <summary>Text to bytes to text is the identity too, once the text is in the writer's own spelling.</summary>
    [Fact]
    public void TheWritersOwnTextSurvivesARoundTrip()
    {
        string written = MusicTextWriter.Write(Compile(Sample));

        Assert.Equal(written, MusicTextWriter.Write(Compile(written)));
    }

    /// <summary>
    /// Compilation is a pure function of the text — that is what lets CI rebuild a bank and
    /// compare bytes, and what <c>quarp audio build --check</c> is.
    /// </summary>
    [Fact]
    public void CompilingTheSameTextTwiceGivesTheSameBytes() => Assert.Equal(Compile(Sample), Compile(Sample));

    /// <summary>
    /// The writer emits "\n" on every platform, so a file written on Windows and one written on
    /// Linux are the same bytes — the same rule the rest of the pipeline follows.
    /// </summary>
    [Fact]
    public void TheWriterEmitsOnlyLineFeeds()
    {
        Assert.DoesNotContain('\r', MusicTextWriter.Write(Compile(Sample)));
    }

    /// <summary>A file with CRLF line endings compiles to the same bytes as one with LF.</summary>
    [Fact]
    public void CarriageReturnsDoNotChangeTheBytes()
    {
        Assert.Equal(Compile(Sample), Compile(Sample.ReplaceLineEndings("\r\n")));
    }

    // --- the version line: one version, and a file has to say it ---

    /// <summary>
    /// <c>MusicTextVersion</c> reports what the file says and nothing else: a file with no version
    /// line answers <see cref="AudioTextCompiler.NoVersionLine"/>, so the compiler can refuse it
    /// by name instead of guessing a grammar for it (ADR-041).
    /// </summary>
    [Fact]
    public void AFileWithoutAVersionLineIsNoVersionAtAll()
    {
        Assert.Equal(AudioTextCompiler.NoVersionLine, AudioTextCompiler.MusicTextVersion("0 02 -- -- --\n"));
        Assert.Equal(0, AudioTextCompiler.MusicTextVersion(Sample));
        Assert.Equal(0, AudioTextCompiler.MusicTextVersion("# comment\n\nversion 0\n"));
        Assert.Equal(2, AudioTextCompiler.MusicTextVersion("version 2\n"));
    }

    [Fact]
    public void TheVersionLineComesOnceAndAtTheTop()
    {
        Assert.Contains("comes once", Rejected(Sample + "\nversion 0\n"));
        Assert.Contains("version 0", Rejected("order\n   00  00\n"));
    }

    [Fact]
    public void AVersionThisCompilerDoesNotReadIsRefusedByName()
    {
        Assert.Contains("version 0", Rejected("version 3\n"));
        Assert.Contains("version 0", Rejected("version 2\n"));
    }

    // --- the fractional row speed: the gap the ports measured (Celeste GAPS §1.7, §5.3) ---

    /// <summary>
    /// The whole reason the row speed is a fixed-point number: a PICO-8 step is
    /// <c>speed x 60/128 = speed x 15/32</c> ticks, so every PICO-8 tempo is a whole number of
    /// thirty-seconds of a tick and converts with no rounding at all. Celeste measured the cost of
    /// rounding it — +6.67 % on the music tempo, -14.7 % on one slot — and asked for exactly this.
    ///
    /// <para>Break recipe: change <see cref="MusicFormat.SpeedUnitsPerTick"/> to 16 — PICO-8
    /// speeds 1, 3, 5 and 7 stop being representable and this fails on the first of them. Run to
    /// red 2026-09-01.</para>
    /// </summary>
    [Theory]
    [InlineData(1, "0.46875", 15)]
    [InlineData(2, "0.9375", 30)]
    [InlineData(3, "1.40625", 45)]
    [InlineData(5, "2.34375", 75)]
    [InlineData(8, "3.75", 120)]
    [InlineData(16, "7.5", 240)]
    public void EveryPico8TempoIsAWholeNumberOfThirtySecondsOfATick(int pico8Speed, string ticks, int units)
    {
        // speed x 15/32 ticks, exactly, with no remainder anywhere.
        Assert.Equal(units, pico8Speed * 15);
        Assert.Equal(ticks, MusicTextCompiler.SpeedText(units));

        // Speeds under a whole tick are not writable as a row speed — a row is at least one tick —
        // but the arithmetic that gets there is exact, which is what the format promises.
        if (units >= MusicFormat.MinRowSpeed)
        {
            Assert.True(MusicTextCompiler.TryParseSpeed(ticks, out int parsed, out _));
            Assert.Equal(units, parsed);
        }
    }

    /// <summary>A speed written back is the shortest exact decimal, never a rounded one.</summary>
    [Theory]
    [InlineData(32, "1")]
    [InlineData(33, "1.03125")]
    [InlineData(240, "7.5")]
    [InlineData(256, "8")]
    [InlineData(65535, "2047.96875")]
    public void ASpeedIsWrittenBackAsTheShortestExactDecimal(int units, string text)
    {
        Assert.Equal(text, MusicTextCompiler.SpeedText(units));
        Assert.True(MusicTextCompiler.TryParseSpeed(text, out int parsed, out _));
        Assert.Equal(units, parsed);
    }

    /// <summary>
    /// A tempo that is not a whole number of thirty-seconds is refused with the two nearest ones
    /// spelled out. Silently rounding a tempo is precisely the bug the fraction exists to end, so
    /// the compiler will not do it quietly on the author's behalf.
    /// </summary>
    [Fact]
    public void ATempoTheFieldCannotHoldIsRefusedWithTheTwoNearestOnes()
    {
        Assert.False(MusicTextCompiler.TryParseSpeed("7.51", out _, out string? problem));
        Assert.Contains("1/32", problem);
        Assert.Contains("7.5", problem);
        Assert.Contains("7.53125", problem);
    }

    [Fact]
    public void ARowShorterThanATickOrLongerThanTheFieldIsRefused()
    {
        Assert.False(MusicTextCompiler.TryParseSpeed("0.5", out _, out string? tooShort));
        Assert.Contains("outside", tooShort);
        Assert.False(MusicTextCompiler.TryParseSpeed("2048", out _, out string? tooLong));
        Assert.Contains("outside", tooLong);
    }

    [Fact]
    public void ASpeedThatIsNotANumberIsRefused()
    {
        Assert.False(MusicTextCompiler.TryParseSpeed("fast", out _, out _));
        Assert.False(MusicTextCompiler.TryParseSpeed("7.", out _, out _));
        Assert.False(MusicTextCompiler.TryParseSpeed("-8", out _, out _));
    }

    // --- comments, tokens and the three rules v1 already had ---

    /// <summary>
    /// <c>#</c> starts a comment only at the beginning of a token, so <c>C#4</c> stays a note. A
    /// format for musicians that could not write a sharp would be a bad joke — v1 §6 rule 3,
    /// unchanged.
    /// </summary>
    [Fact]
    public void ASharpIsANoteAndNotAComment()
    {
        byte[] payload = Compile("""
            version 0
            inst 00 sfx 00 root C#4
            order
               00  00
            pattern 00 rows 1
               00   C#5 00 7 ---  |  .  |  .  |  .   # a sharp, then a comment
            """);

        Assert.Equal(25, MusicFormat.InstrumentRoot(payload, 0));
        Assert.Equal(37, MusicFormat.CellNote(MusicFormat.Cell(payload, 0, 0, 0)));
    }

    /// <summary>Keywords and note names are case-insensitive, as in v1.</summary>
    [Fact]
    public void CaseDoesNotMatter()
    {
        Assert.Equal(
            Compile("version 0\ninst 00 sfx 02 root c-4\norder\n   00  00\npattern 00 rows 1\n   00  c-4 00 7 --- | . | . | .\n"),
            Compile("version 0\nINST 00 SFX 02 ROOT C-4\nORDER\n   00  00\nPATTERN 00 ROWS 1\n   00  C-4 00 7 --- | . | . | .\n"));
    }

    // --- rejections, each naming a line ---

    [Fact]
    public void EveryDiagnosticNamesTheFileAndTheLine()
    {
        Assert.StartsWith("music.txt:3:", Rejected("version 0\norder\n   00  99\n"));
        Assert.StartsWith("music.txt:2:", Rejected("version 0\nwibble 3\n"));
    }

    [Fact]
    public void AnUnknownDirectiveIsRefusedWithTheListOfRealOnes()
    {
        Assert.Contains("version, inst, order or pattern", Rejected("version 0\nwibble 3\n"));
    }

    [Fact]
    public void ADataRowOutsideAnyBlockIsRefused()
    {
        Assert.Contains("belongs to no block", Rejected("version 0\n   00  00\n"));
    }

    /// <summary>
    /// The order is dense and ascending: it is the song's timeline, and a gap in it would be an
    /// entry that plays pattern 0 because nobody said otherwise. A repeat is caught by name and
    /// with the line the entry was first written on, which is the more useful of the two answers.
    /// </summary>
    [Fact]
    public void AnOrderWithAGapOrARepeatIsRefused()
    {
        Assert.Contains("is missing", Rejected("version 0\norder\n   00  00\n   02  00\n"));
        Assert.Contains("already written on line 3", Rejected("version 0\norder\n   00  00\n   01  00\n   00  00\n"));
    }

    [Fact]
    public void ARowWrittenTwiceOrOutOfOrderIsRefused()
    {
        Assert.Contains(
            "ascending order",
            Rejected("version 0\norder\n   00  00\npattern 00 rows 4\n   02  . | . | . | .\n   01  . | . | . | .\n"));
    }

    [Fact]
    public void APatternOrInstrumentDefinedTwiceIsRefused()
    {
        Assert.Contains("already defined", Rejected(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00 . | . | . | .\npattern 00 rows 1\n   00 . | . | . | .\n"));
        Assert.Contains("already defined", Rejected("version 0\ninst 00 sfx 01\ninst 00 sfx 02\n"));
    }

    /// <summary>
    /// A row writes all four channels. Letting it write fewer would make the column a cell lands
    /// in depend on counting, which is the one thing a tracker layout must never do.
    /// </summary>
    [Fact]
    public void ARowWritesEveryChannel()
    {
        Assert.Contains("channel(s)", Rejected("version 0\norder\n   00  00\npattern 00 rows 1\n   00  . | .\n"));
        Assert.Contains(
            "past the last channel",
            Rejected("version 0\norder\n   00  00\npattern 00 rows 1\n   00  . | . | . | . | .\n"));
    }

    [Fact]
    public void ARowsCountShorterThanTheRowsWrittenIsRefused()
    {
        Assert.Contains(
            "shorter than the last row",
            Rejected("version 0\norder\n   00  00\npattern 00 rows 2\n   05  . | . | . | .\n"));
    }

    [Fact]
    public void AnEmptyPatternBlockIsRefusedRatherThanWrittenAsUnused()
    {
        Assert.Contains("delete the whole", Rejected("version 0\npattern 00 rows 0\n"));
        Assert.Contains("has no rows", Rejected("version 0\npattern 00\n"));
    }

    [Fact]
    public void AnInstrumentWithoutASlotIsRefused()
    {
        Assert.Contains("names no SFX slot", Rejected("version 0\ninst 00 root C-4\n"));
    }

    [Fact]
    public void AnInstrumentSpeedOfZeroIsRefusedRatherThanFreezingTheVoice()
    {
        Assert.Contains("freeze", Rejected("version 0\ninst 00 sfx 01 speed 0\n"));
    }

    [Fact]
    public void AnUnknownNoteOrOneOutsideTheRangeIsRefused()
    {
        Assert.Contains("is not a note", Rejected("version 0\ninst 00 sfx 01 root H-4\n"));
        Assert.Contains("outside", Rejected("version 0\ninst 00 sfx 01 root C-9\n"));
    }

    [Fact]
    public void AnUnknownEffectOrOneWithoutAParameterIsRefused()
    {
        Assert.Contains("unknown effect", Rejected(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 00 7 wobble:3 | . | . | .\n"));
        Assert.Contains("carries no parameter", Rejected(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 00 7 cut | . | . | .\n"));
    }

    /// <summary>An arpeggio's parameter is two hex digits — the two semitone offsets of the chord.</summary>
    [Fact]
    public void AnArpeggioTakesTwoHexDigits()
    {
        byte[] payload = Compile(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 00 7 arp:47 | . | . | .\n");
        Assert.Equal(0x47, MusicFormat.CellParam(MusicFormat.Cell(payload, 0, 0, 0)));

        Assert.Contains("two hex digits", Rejected(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 00 7 arp:4 | . | . | .\n"));
    }

    /// <summary>An effect with parameter 0 does nothing, and the text refuses it for the same reason the bank does.</summary>
    [Fact]
    public void AnEffectWithAZeroParameterIsRefusedInTheTextToo()
    {
        Assert.Contains("does nothing", Rejected(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 00 7 cut:0 | . | . | .\n"));
    }

    /// <summary>A slide has to have a note to glide to — refused in the grammar, not only in the bank.</summary>
    [Fact]
    public void ASlideWithoutANoteIsRefusedInTheTextToo()
    {
        Assert.Contains("has to carry one", Rejected(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  --- 00 7 slide:4 | . | . | .\n"));
    }

    [Fact]
    public void AnUnknownOrderFlagIsRefusedWithTheListOfRealOnes()
    {
        Assert.Contains("loop-start, loop-back, stop, jump", Rejected("version 0\norder\n   00  00  loopstart\n"));
    }

    [Fact]
    public void ATransposeOutsideTheSignedByteIsRefused()
    {
        Assert.Contains("outside", Rejected("version 0\norder\n   00  00  transpose 64\n"));
        Assert.Contains("outside", Rejected("version 0\norder\n   00  00  transpose -65\n"));
    }

    /// <summary>
    /// A note off is a whole cell on its own: it silences the voice, so a volume or an effect
    /// beside it would have nothing to act on, and the bank keeps one spelling for it.
    /// </summary>
    [Fact]
    public void ANoteOffIsAWholeCellOnItsOwn()
    {
        byte[] payload = Compile(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  === | === | === | ===\n");
        uint cell = MusicFormat.Cell(payload, 0, 0, 0);

        Assert.Equal(MusicFormat.NoteOff, MusicFormat.CellNoteKind(cell));
        Assert.Equal(0, MusicFormat.CellNote(cell));
        Assert.False(MusicFormat.CellHasVolume(cell));
        Assert.Equal(MusicFormat.EffectNone, MusicFormat.CellEffect(cell));
    }

    /// <summary>
    /// The columns a cell may leave out — instrument and volume — mean "keep what the channel
    /// has", and they compile to a cell that says so rather than to one carrying a stale number.
    /// </summary>
    [Fact]
    public void TheDashColumnsMeanKeepWhatTheChannelHas()
    {
        byte[] payload = Compile(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 -- - --- | . | . | .\n");
        uint cell = MusicFormat.Cell(payload, 0, 0, 0);

        Assert.Equal(MusicFormat.NoteOn, MusicFormat.CellNoteKind(cell));
        Assert.False(MusicFormat.CellHasInstrument(cell));
        Assert.False(MusicFormat.CellHasVolume(cell));
        Assert.Equal(0, MusicFormat.CellInstrument(cell));
        Assert.Equal(0, MusicFormat.CellVolume(cell));
    }

    /// <summary>
    /// The bar between channels is decoration: it may be there, be missing, or be doubled, and the
    /// bytes do not change. A tracker layout that only parsed when the pipes lined up would punish
    /// the author for editing by hand — which is the whole point of the text format.
    /// </summary>
    [Fact]
    public void TheColumnSeparatorIsDecorationAndNotSyntax()
    {
        byte[] withBars = Compile(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 00 7 --- | . | . | .\n");
        byte[] withoutBars = Compile(
            "version 0\norder\n   00  00\npattern 00 rows 1\n   00  C-4 00 7 --- . . .\n");

        Assert.Equal(withBars, withoutBars);
    }

    /// <summary>
    /// Rows may be sparse: an unwritten row is an empty row, exactly as an unwritten step is a
    /// pause in v1. That is what makes a diff of one changed note one changed line.
    /// </summary>
    [Fact]
    public void UnwrittenRowsAreEmptyRows()
    {
        byte[] payload = Compile(
            "version 0\norder\n   00  00\npattern 00 rows 8\n   00  C-4 00 7 --- | . | . | .\n");

        Assert.Equal(8, MusicFormat.PatternRows(payload, 0));
        Assert.Equal(0u, MusicFormat.Cell(payload, 0, 3, 0));
        MusicFormat.ValidatePayload(payload, "music.txt");
    }

    /// <summary>
    /// A pattern nobody wrote a <c>speed</c> for gets 8 ticks a row — the same default a v1 slot
    /// gets, so an author moving between the two formats does not have to relearn the tempo.
    /// </summary>
    [Fact]
    public void APatternWithoutASpeedGetsTheSameDefaultAnSfxSlotGets()
    {
        byte[] payload = Compile("version 0\norder\n   00  00\npattern 00 rows 1\n   00 . | . | . | .\n");

        Assert.Equal(MusicFormat.DefaultRowSpeed, MusicFormat.PatternSpeed(payload, 0));
        Assert.Equal(8 * 32, MusicFormat.PatternSpeed(payload, 0));
    }
}

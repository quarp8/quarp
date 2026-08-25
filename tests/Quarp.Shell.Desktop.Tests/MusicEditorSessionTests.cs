using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The music editor's model contract, proven headless: the one payload, the absent file that is
/// silence, the dirty rule, the save contract, the read-only song of a cart that still has
/// <c>music.txt</c>, the tracker grid this format actually has (64 patterns x 4 channels of SFX
/// slot references — there is no note in <c>music.bin</c>, see AUDIO-FORMAT §4), and one undo
/// stack over all of it. Driven through <see cref="MusicEditorSession"/> alone, the way
/// <see cref="MapEditorSessionTests"/> and <c>SfxEditorTests</c>' document half drive theirs.
///
/// <para>The named negative-control targets: (a) a clean session writes nothing — proven by an
/// empty directory listing and by an untouched write timestamp; (b) the writer is the payload and
/// nothing else — a dirty save of the real snake song differs from the original in exactly the
/// one byte that was edited, and putting it back reproduces the file byte for byte; (c) the
/// length check refuses a truncated file and puts both numbers in the sentence.</para>
///
/// <para><c>carts/</c> holds pinned goldens and nothing here may write into it — the demo cart is
/// only ever <em>read</em>, to get real bytes to copy into a temp folder.</para>
/// </summary>
public class MusicEditorSessionTests : IDisposable
{
    private readonly string _root;

    public MusicEditorSessionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-musiced-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_root, recursive: true);
    }

    // ---- helpers ----

    /// <summary>An empty cart folder, optionally seeded with a music bank and/or a music.txt.</summary>
    private string CartFolder(byte[]? music = null, bool musicSource = false)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (music is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, MusicEditorSession.MusicFileName), music);
        }
        if (musicSource)
        {
            File.WriteAllText(Path.Combine(folder, MusicEditorSession.MusicSourceFileName), "# a hand-authored song\n");
        }
        return folder;
    }

    /// <summary>Walks up from the test bin folder to the repo root, same as the other session tests.</summary>
    private static string CartsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "carts");
            if (File.Exists(Path.Combine(candidate, "snake", "manifest.json")))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/ not found above the test directory");
    }

    /// <summary>The real bytes of a demo cart's song. Read, never written.</summary>
    private static byte[] DemoMusic(string cart) =>
        File.ReadAllBytes(Path.Combine(CartsRoot(), cart, MusicEditorSession.MusicFileName));

    /// <summary>The raw channel byte behind a cell, so a test can pin the layout and not merely the accessor.</summary>
    private static int ChannelByte(MusicEditorSession session, int pattern, int channel) =>
        session.Payload[pattern * MusicEditorSession.ChannelCount + channel];

    /// <summary>The raw flag byte of a pattern — after the 256-byte channel table.</summary>
    private static int FlagByte(MusicEditorSession session, int pattern) =>
        session.Payload[AudioFormat.MusicChannelTableSize + pattern];

    // ==================================================================================
    // 1. Absent file is silence, and a clean session writes nothing.
    // ==================================================================================

    [Fact]
    public void AbsentBankOpensEmptyAndClean()
    {
        string folder = CartFolder();
        var session = new MusicEditorSession(folder);

        Assert.Equal(MusicEditorSession.PayloadSize, session.Payload.Length);
        Assert.All(session.Payload.ToArray(), b => Assert.Equal(0, (int)b));
        for (int pattern = 0; pattern < MusicEditorSession.PatternCount; pattern++)
        {
            Assert.True(session.PatternIsEmpty(pattern));
            Assert.Equal(0, (int)session.PatternFlags(pattern));
        }
        Assert.False(session.IsDirty);
        Assert.False(session.BankReadOnly);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void CleanSessionCreatesNoFile()
    {
        string folder = CartFolder();
        var session = new MusicEditorSession(folder);

        Assert.True(session.Save());
        Assert.True(session.Save());
        Assert.Null(session.SaveError);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void CleanSessionOverAnExistingBankDoesNotTouchTheFile()
    {
        string folder = CartFolder(DemoMusic("snake"));
        string path = Path.Combine(folder, MusicEditorSession.MusicFileName);
        var stamp = File.GetLastWriteTimeUtc(path);

        var session = new MusicEditorSession(folder);
        Assert.True(session.Save());

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    // ==================================================================================
    // 2. Round trip on the real snake song.
    // ==================================================================================

    [Fact]
    public void SnakeSongOpensUnchangedAndSavesByteIdentical()
    {
        byte[] original = DemoMusic("snake");
        string folder = CartFolder(original);
        string path = Path.Combine(folder, MusicEditorSession.MusicFileName);

        var session = new MusicEditorSession(folder);
        Assert.False(session.IsDirty);
        Assert.True(session.Save());
        Assert.Equal(original, File.ReadAllBytes(path));

        // And the song really is snake's: four patterns on the two upper channels, loop-start on
        // the first and loop-end on the last. These numbers were READ OUT of carts/snake/music.bin
        // rather than assumed from music.txt — the first cut of this test guessed channel 0 and
        // three patterns and was wrong on both, which is exactly what a round-trip test is for.
        Assert.Equal(2, session.ChannelSlot(0, 2));
        Assert.True(session.ChannelIsSilent(0, 0));
        Assert.True(session.ChannelIsSilent(0, 1));
        Assert.True(session.ChannelIsSilent(0, 3));
        Assert.Equal(2, session.ChannelSlot(1, 2));
        Assert.Equal(4, session.ChannelSlot(1, 3));
        Assert.Equal(3, session.ChannelSlot(2, 2));
        Assert.Equal(5, session.ChannelSlot(2, 3));
        Assert.True(session.HasFlag(0, MusicEditorSession.FlagLoopStart));
        Assert.True(session.HasFlag(3, MusicEditorSession.FlagLoopEnd));
        Assert.True(session.PatternIsEmpty(4));
    }

    [Fact]
    public void DirtySaveDiffersInExactlyTheEditedByteAndPuttingItBackReproducesTheFile()
    {
        byte[] original = DemoMusic("snake");
        string folder = CartFolder(original);
        string path = Path.Combine(folder, MusicEditorSession.MusicFileName);

        var session = new MusicEditorSession(folder);
        session.SetChannelSlot(1, 2, 5);
        Assert.True(session.IsDirty);
        Assert.True(session.Save());

        byte[] written = File.ReadAllBytes(path);
        Assert.Equal(original.Length, written.Length);
        var differing = new List<int>();
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != written[i])
            {
                differing.Add(i);
            }
        }
        Assert.Single(differing);
        Assert.Equal(AudioFormat.HeaderSize + 1 * MusicEditorSession.ChannelCount + 2, differing[0]);

        // Putting it back means writing the slot that was there (2), not clearing the cell:
        // pattern 1 channel 2 sounds in snake's song, and clearing it would leave the file one
        // byte away from the original forever.
        session.SetChannelSlot(1, 2, 2);
        Assert.True(session.Save());
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void MusicTextMakesTheSongReadOnly()
    {
        string folder = CartFolder(DemoMusic("snake"), musicSource: true);
        var session = new MusicEditorSession(folder);

        Assert.True(session.BankReadOnly);
        var thrown = Assert.Throws<InvalidOperationException>(() => session.SetChannelSlot(0, 0, 1));
        Assert.Contains(MusicEditorSession.MusicSourceFileName, thrown.Message);
        Assert.False(session.IsDirty);
    }

    // ==================================================================================
    // 3. Typing a slot at the cursor: the right byte, and the cursor moves.
    // ==================================================================================

    [Fact]
    public void EnterSlotWritesTheActiveBitAndTheSlotAndStepsTheCursor()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetCursor(4, 2);

        session.EnterSlot(9);

        // Bits 0-5 are the slot, bit 6 is "this channel plays", bit 7 is reserved (AUDIO-FORMAT §4).
        Assert.Equal(0x49, ChannelByte(session, 4, 2));
        Assert.Equal(9, session.ChannelSlot(4, 2));
        Assert.Equal(5, session.CursorPattern);
        Assert.Equal(2, session.CursorChannel);
        Assert.True(session.IsDirty);

        // A rest is the zero byte and nothing else — a channel that is off may not remember a slot.
        session.SetCursor(4, 2);
        session.EnterRest();
        Assert.Equal(0x00, ChannelByte(session, 4, 2));
        Assert.True(session.ChannelIsSilent(4, 2));
        Assert.Equal(5, session.CursorPattern);
    }

    [Fact]
    public void TheCursorClampsAtTheGridEdgesInsteadOfWrapping()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetCursor(MusicEditorSession.PatternCount - 1, MusicEditorSession.ChannelCount - 1);
        session.MoveCursor(1, 1);
        Assert.Equal(MusicEditorSession.PatternCount - 1, session.CursorPattern);
        Assert.Equal(MusicEditorSession.ChannelCount - 1, session.CursorChannel);

        session.MoveCursor(-100, -100);
        Assert.Equal(0, session.CursorPattern);
        Assert.Equal(0, session.CursorChannel);
    }

    [Fact]
    public void PatternFlagsRefuseTheReservedBits()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetPatternFlags(7, MusicEditorSession.FlagStop);
        Assert.Equal(MusicEditorSession.FlagStop, (byte)FlagByte(session, 7));

        session.TogglePatternFlag(7, MusicEditorSession.FlagLoopStart);
        Assert.True(session.HasFlag(7, MusicEditorSession.FlagStop));
        Assert.True(session.HasFlag(7, MusicEditorSession.FlagLoopStart));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetPatternFlags(7, 0x08));
    }

    // ==================================================================================
    // 4. Insert and delete: what shifts, and what stands still.
    // ==================================================================================

    [Fact]
    public void InsertingIntoOneChannelShiftsThatColumnOnly()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetChannelSlot(0, 0, 1);
        session.SetChannelSlot(1, 0, 2);
        session.SetChannelSlot(0, 1, 40);       // the neighbour that must not move
        session.SetChannelSlot(1, 1, 41);
        session.SetPatternFlags(1, MusicEditorSession.FlagLoopEnd);
        session.SetChannelSlot(MusicEditorSession.PatternCount - 1, 0, 63);

        session.InsertChannelCell(0, 0);

        Assert.True(session.ChannelIsSilent(0, 0));
        Assert.Equal(1, session.ChannelSlot(1, 0));
        Assert.Equal(2, session.ChannelSlot(2, 0));
        // Neighbours and flags stood still.
        Assert.Equal(40, session.ChannelSlot(0, 1));
        Assert.Equal(41, session.ChannelSlot(1, 1));
        Assert.Equal(MusicEditorSession.FlagLoopEnd, session.PatternFlags(1));
        // The bank is 64 rows and cannot grow, so the last cell of that column fell off.
        Assert.True(session.ChannelIsSilent(MusicEditorSession.PatternCount - 1, 0));
    }

    [Fact]
    public void DeletingFromOneChannelPullsThatColumnUpOnly()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetChannelSlot(0, 0, 1);
        session.SetChannelSlot(1, 0, 2);
        session.SetChannelSlot(2, 0, 3);
        session.SetChannelSlot(1, 3, 17);
        session.SetPatternFlags(2, MusicEditorSession.FlagStop);

        session.DeleteChannelCell(1, 0);

        Assert.Equal(1, session.ChannelSlot(0, 0));      // above the cut, untouched
        Assert.Equal(3, session.ChannelSlot(1, 0));      // row 1 took what row 2 held
        Assert.True(session.ChannelIsSilent(2, 0));
        Assert.Equal(17, session.ChannelSlot(1, 3));     // the neighbouring channel did not move
        Assert.Equal(MusicEditorSession.FlagStop, session.PatternFlags(2));
        Assert.True(session.ChannelIsSilent(MusicEditorSession.PatternCount - 1, 0));
    }

    [Fact]
    public void InsertingAWholeRowMovesEveryChannelAndItsFlags()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetChannelSlot(0, 0, 2);
        session.SetChannelSlot(0, 1, 3);
        session.SetPatternFlags(0, MusicEditorSession.FlagLoopStart);

        session.InsertPatternRow(0);

        Assert.True(session.PatternIsEmpty(0));
        Assert.Equal(0, (int)session.PatternFlags(0));
        Assert.Equal(2, session.ChannelSlot(1, 0));
        Assert.Equal(3, session.ChannelSlot(1, 1));
        Assert.Equal(MusicEditorSession.FlagLoopStart, session.PatternFlags(1));

        session.DeletePatternRow(0);

        Assert.Equal(2, session.ChannelSlot(0, 0));
        Assert.Equal(MusicEditorSession.FlagLoopStart, session.PatternFlags(0));
        Assert.True(session.PatternIsEmpty(MusicEditorSession.PatternCount - 1));
    }

    // ==================================================================================
    // 5. Shifting the slots of a marked block.
    // ==================================================================================

    [Fact]
    public void ShiftingMovesSoundingCellsOnlyAndClampsIntoTheSlotRange()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetChannelSlot(0, 0, 10);
        session.SetChannelSlot(1, 0, MusicEditorSession.MaxSlot);   // already at the ceiling
        session.SetChannelSlot(2, 0, 30);
        // (1,1) and the rest are silent and must stay the zero byte.
        session.SetChannelSlot(3, 0, 40);       // outside the marking

        session.SelectRange(0, 0, 2, 1);
        session.ShiftSelectionSlots(5);

        Assert.Equal(15, session.ChannelSlot(0, 0));
        Assert.Equal(MusicEditorSession.MaxSlot, session.ChannelSlot(1, 0));
        Assert.Equal(35, session.ChannelSlot(2, 0));
        Assert.Equal(0x00, ChannelByte(session, 0, 1));     // a rest stayed a rest
        Assert.Equal(0x00, ChannelByte(session, 2, 1));
        Assert.Equal(40, session.ChannelSlot(3, 0));        // outside the marking, untouched

        session.ShiftSelectionSlots(-100);
        Assert.Equal(0, session.ChannelSlot(0, 0));
        Assert.Equal(0, session.ChannelSlot(2, 0));
        Assert.Equal(0x00, ChannelByte(session, 0, 1));
    }

    // ==================================================================================
    // 6. Copy and paste a block.
    // ==================================================================================

    [Fact]
    public void CopyingAndPastingMovesTheCellsAndNotTheFlags()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetChannelSlot(0, 0, 2);
        session.SetChannelSlot(0, 1, 3);
        session.SetChannelSlot(1, 1, 4);
        session.SetPatternFlags(0, MusicEditorSession.FlagLoopStart);

        session.SelectRange(0, 0, 1, 1);
        Assert.True(session.CopySelection());
        Assert.Equal(2, session.Clipboard.Patterns);
        Assert.Equal(2, session.Clipboard.Channels);

        Assert.True(session.PasteAt(10, 2));

        Assert.Equal(2, session.ChannelSlot(10, 2));
        Assert.Equal(3, session.ChannelSlot(10, 3));
        Assert.True(session.ChannelIsSilent(11, 2));       // the copied rest came across as a rest
        Assert.Equal(4, session.ChannelSlot(11, 3));
        Assert.Equal(0, (int)session.PatternFlags(10));     // flags belong to a bar, not to a block

        // Off the right edge: the cells that fall outside are dropped, the rest land.
        Assert.True(session.PasteAt(20, 3));
        Assert.Equal(2, session.ChannelSlot(20, 3));
        Assert.True(session.ChannelIsSilent(21, 3));
    }

    [Fact]
    public void CuttingLeavesTheBlockOnTheClipboardAndSilenceBehind()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetChannelSlot(5, 0, 8);
        session.SetChannelSlot(5, 1, 9);
        session.SelectRange(5, 0, 5, 1);

        session.CutSelection();

        Assert.Equal(0x00, ChannelByte(session, 5, 0));
        Assert.Equal(0x00, ChannelByte(session, 5, 1));
        Assert.True(session.Clipboard.HasBlock);

        Assert.True(session.PasteAt(6, 0));
        Assert.Equal(8, session.ChannelSlot(6, 0));
        Assert.Equal(9, session.ChannelSlot(6, 1));
    }

    // ==================================================================================
    // 7. Undo: one step per action, whichever hand performed it.
    // ==================================================================================

    [Fact]
    public void EveryVerbCostsExactlyOneUndo()
    {
        var session = new MusicEditorSession(CartFolder());
        byte[] empty = session.Payload.ToArray();

        session.SetChannelSlot(0, 0, 1);
        session.SetChannelSlot(1, 0, 2);
        session.SetPatternFlags(1, MusicEditorSession.FlagStop);
        byte[] afterThree = session.Payload.ToArray();

        session.SelectRange(0, 0, 1, 0);
        session.ShiftSelectionSlots(4);
        session.InsertPatternRow(0);

        session.Undo();     // the row insert
        session.Undo();     // the shift
        Assert.Equal(afterThree, session.Payload.ToArray());

        session.Undo();
        session.Undo();
        session.Undo();
        Assert.Equal(empty, session.Payload.ToArray());
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);

        session.Redo();
        Assert.Equal(1, session.ChannelSlot(0, 0));
    }

    [Fact]
    public void OneGestureIsOneUndoStepAndAnIdleClickIsNone()
    {
        var session = new MusicEditorSession(CartFolder());

        session.BeginStroke();
        session.SetChannelSlot(0, 0, 1);
        session.SetChannelSlot(1, 0, 1);
        session.SetChannelSlot(2, 0, 1);
        session.EndStroke();
        Assert.True(session.CanUndo);

        session.Undo();
        Assert.True(session.PatternIsEmpty(0));
        Assert.True(session.PatternIsEmpty(2));
        Assert.False(session.CanUndo);

        // Writing the value that is already there is not a change and pushes nothing.
        session.BeginStroke();
        session.ClearChannel(0, 0);
        session.EndStroke();
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
    }

    // ==================================================================================
    // 8. A broken file is refused, by name and with the numbers in it.
    // ==================================================================================

    [Fact]
    public void ATruncatedBankIsRefusedWithBothLengths()
    {
        byte[] truncated = DemoMusic("snake")[..100];
        string folder = CartFolder(truncated);

        var thrown = Assert.Throws<CartLoadException>(() => new MusicEditorSession(folder));
        Assert.Contains(MusicEditorSession.MusicFileName, thrown.Message);
        Assert.Contains("100", thrown.Message);
        Assert.Contains(AudioFormat.MusicFileSize.ToString(), thrown.Message);
    }

    [Fact]
    public void ABankThatIsNotABankIsRefusedByItsMagic()
    {
        string folder = CartFolder(new byte[AudioFormat.MusicFileSize]);
        var thrown = Assert.Throws<CartLoadException>(() => new MusicEditorSession(folder));
        Assert.Contains("QMUS", thrown.Message);
    }

    [Fact]
    public void ASilentChannelThatStillNamesASlotIsRefused()
    {
        byte[] payload = AudioFormat.EmptyMusicPayload();
        payload[0] = 0x03;      // channel off, bit 6 clear, but slot 3 remembered — not canonical
        byte[] file = new byte[AudioFormat.MusicFileSize];
        AudioFormat.MusicMagic.CopyTo(file);
        file[4] = 1;
        file[6] = AudioFormat.MusicPatternCount;
        file[7] = AudioFormat.MusicChannelCount;
        payload.CopyTo(file, AudioFormat.HeaderSize);

        string folder = CartFolder(file);
        var thrown = Assert.Throws<CartLoadException>(() => new MusicEditorSession(folder));
        Assert.Contains("0x00", thrown.Message);
    }

    [Fact]
    public void OutOfRangeArgumentsThrowInsteadOfClamping()
    {
        var session = new MusicEditorSession(CartFolder());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetChannelSlot(0, 0, MusicEditorSession.SlotCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetChannelSlot(0, 0, -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetChannelSlot(MusicEditorSession.PatternCount, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetChannelSlot(0, MusicEditorSession.ChannelCount, 1));
    }
}

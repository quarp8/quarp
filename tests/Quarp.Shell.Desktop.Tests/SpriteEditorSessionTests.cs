using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The sprite editor's policy, proven headless (M9 stage 2, wave 2b) — every claim of the
/// work order's save/undo/palette contracts, driven through <see cref="SpriteEditorSession"/>
/// alone, the way <see cref="ModeTransitionTests"/> drives <see cref="ShellModeMachine"/>.
///
/// <para>Three of these are the order's named negative-control targets: a clean session
/// never touches the disk (proven with a read-only file — a write <em>attempt</em> would
/// fail loudly, so "no error" means "no write"), undo back to the loaded sheet clears the
/// dirty flag, and one stroke is exactly one undo step however many pixels it painted.</para>
/// </summary>
public class SpriteEditorSessionTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorSessionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Some tests deliberately leave gfx.png read-only (the no-write proof); Delete would
        // throw on it, so attributes are normalized first.
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>A cart folder, optionally with a gfx.png encoded from the given sheet and/or a flags.bin.</summary>
    private string CartFolder(byte[]? sheet = null, byte[]? flags = null)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (sheet is not null)
        {
            File.WriteAllBytes(
                Path.Combine(folder, "gfx.png"),
                PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight));
        }
        if (flags is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, SpriteEditorSession.FlagsFileName), flags);
        }
        return folder;
    }

    /// <summary>Every pixel a distinct-ish visible index — corruption anywhere shows up somewhere.</summary>
    private static byte[] PatternSheet()
    {
        var sheet = new byte[CartData.GfxWidth * CartData.GfxHeight];
        for (int i = 0; i < sheet.Length; i++)
        {
            sheet[i] = (byte)(i % Palette.VisibleCount);
        }
        return sheet;
    }

    /// <summary>Every flag byte distinct-ish — corruption anywhere shows up somewhere.</summary>
    private static byte[] PatternFlags()
    {
        var flags = new byte[SpriteEditorSession.FlagsPayloadSize];
        for (int i = 0; i < flags.Length; i++)
        {
            flags[i] = (byte)(i ^ 0x5A);
        }
        return flags;
    }

    /// <summary>Moves the region anchor so <see cref="SpriteEditorSession.SpriteIndex"/> equals the given sprite — the flag panel's implicit target.</summary>
    private static void SelectSprite(SpriteEditorSession session, int sprite) =>
        session.SelectRegionCell(sprite % SpriteEditorSession.GridCells, sprite / SpriteEditorSession.GridCells);

    private static byte PixelAt(SpriteEditorSession session, int sheetX, int sheetY) =>
        session.Pixels[sheetY * CartData.GfxWidth + sheetX];

    /// <summary>One complete pencil gesture: press, samples, release.</summary>
    private static void Stroke(SpriteEditorSession session, params (int X, int Y)[] points)
    {
        session.BeginStroke();
        foreach ((int x, int y) in points)
        {
            session.Paint(x, y);
        }
        session.EndStroke();
    }

    // ---- opening ----

    [Fact]
    public void ACartWithoutGfxPngOpensAsAnEmptyCleanSheet()
    {
        var session = new SpriteEditorSession(CartFolder());

        Assert.Equal(CartData.GfxWidth * CartData.GfxHeight, session.Pixels.Length);
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);      // all zeros — snake's normal case
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void AnExistingGfxPngLoadsPixelForPixel()
    {
        byte[] sheet = PatternSheet();
        var session = new SpriteEditorSession(CartFolder(sheet));

        Assert.True(session.Pixels.SequenceEqual(sheet));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void ACorruptGfxPngThrowsTheSameExceptionALoadWould()
    {
        string folder = CartFolder();
        File.WriteAllText(Path.Combine(folder, "gfx.png"), "not a png at all");

        Assert.Throws<CartLoadException>(() => new SpriteEditorSession(folder));
    }

    // ---- pencil, eyedropper, palette guard ----

    [Fact]
    public void ThePencilPaintsTheCurrentColorIntoTheSelectedCell()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectRegionCell(2, 3);
        session.SelectColor(7);

        Stroke(session, (1, 1));

        Assert.Equal(7, PixelAt(session, 2 * 8 + 1, 3 * 8 + 1));    // region-local → sheet coordinates
        Assert.True(session.IsDirty);
    }

    /// <summary>The eraser is the pencil with color 0 — no separate tool, per the niche survey in the work order.</summary>
    [Fact]
    public void PaintingWithColorZeroErasesAndErasingEverythingBackIsCleanAgain()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(7);
        Stroke(session, (0, 0));
        Assert.True(session.IsDirty);

        session.SelectColor(0);
        Stroke(session, (0, 0));

        Assert.Equal(0, PixelAt(session, 0, 0));
        // Dirty is content, not history: the sheet again equals the disk, so saving would
        // change nothing and the session is honestly clean.
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void TheEyedropperPicksTheColorUnderTheCursor()
    {
        byte[] sheet = PatternSheet();
        var session = new SpriteEditorSession(CartFolder(sheet));
        session.SelectRegionCell(1, 0);     // sheet x 8..15, y 0..7

        session.PickColor(3, 0);            // sheet (11, 0) → pattern value 11 % 16

        Assert.Equal(11, session.CurrentColor);
    }

    /// <summary>
    /// The order's hard boundary: no path can put a value above 15 into the sheet. The pencil
    /// writes CurrentColor, and this is CurrentColor's only external door — it must slam, not
    /// mask. (The other doors: the decoder emits only visible-palette matches, the eyedropper
    /// copies sheet values, undo/redo swap former sheets.)
    /// </summary>
    [Fact]
    public void ColorsOutsideTheVisiblePaletteAreRejectedAtTheDoor()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(15);            // the last legal one

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectColor(Palette.VisibleCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectColor(31));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectColor(-1));
        Assert.Equal(15, session.CurrentColor);     // a rejected value must not half-apply
    }

    [Fact]
    public void AStrokeInterpolatesBetweenSamples()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(5);

        // Two samples, eight pixels: the mouse reports per frame, the line fills the gap.
        Stroke(session, (0, 0), (7, 7));

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(5, PixelAt(session, i, i));
        }
    }

    // ---- undo / redo ----

    [Fact]
    public void OneStrokeIsOneUndoStepHoweverManyPixelsItPainted()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(9);

        Stroke(session, (0, 0), (7, 0), (7, 7), (0, 7));    // many pixels, one gesture

        session.Undo();
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);     // fully back in ONE step
        Assert.False(session.CanUndo);                                  // and there is no second step
    }

    [Fact]
    public void UndoRestoresAndRedoReapplies()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(3);
        Stroke(session, (2, 2));

        session.Undo();
        Assert.Equal(0, PixelAt(session, 2, 2));
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.Equal(3, PixelAt(session, 2, 2));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void UndoBackToTheLoadedSheetClearsTheDirtyFlag()
    {
        byte[] sheet = PatternSheet();
        var session = new SpriteEditorSession(CartFolder(sheet));
        session.SelectColor(0);
        Stroke(session, (4, 4));
        Assert.True(session.IsDirty);

        session.Undo();

        Assert.False(session.IsDirty);      // the exit prompt must not cry wolf after this
    }

    [Fact]
    public void ANewStrokeClearsTheRedoFuture()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(2);
        Stroke(session, (0, 0));
        session.Undo();
        Assert.True(session.CanRedo);

        Stroke(session, (1, 1));            // history has branched; the old future is gone

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void AStrokeThatChangesNothingIsInvisibleToUndoAndDirt()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(0);             // painting 0 over 0

        Stroke(session, (0, 0), (5, 5));

        Assert.False(session.CanUndo);      // an idle click must not make Ctrl+Z appear dead
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void UndoMidStrokeCommitsTheGestureAndRollsItBackWhole()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(6);
        session.BeginStroke();
        session.Paint(0, 0);
        session.Paint(3, 3);

        session.Undo();                     // no EndStroke — Ctrl+Z arrived mid-drag

        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.StrokeActive);
        Assert.True(session.CanRedo);       // the committed gesture is redoable as one piece
    }

    // ---- save contract ----

    [Fact]
    public void SaveWritesARoundTrippablePngAndCleansTheSession()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);
        session.SelectColor(12);
        Stroke(session, (0, 0), (7, 7));

        Assert.True(session.Save());

        byte[] decoded = PngDecoder.DecodeToPaletteIndices(
            File.ReadAllBytes(Path.Combine(folder, "gfx.png")), CartData.GfxWidth, CartData.GfxHeight, "gfx.png");
        Assert.True(session.Pixels.SequenceEqual(decoded));
        Assert.False(session.IsDirty);
        Assert.Null(session.SaveError);
    }

    /// <summary>The clean-session guarantee, absent-file half: open-and-save on an untouched cart creates nothing.</summary>
    [Fact]
    public void ACleanSessionNeverCreatesAGfxPng()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);

        Assert.True(session.Save());

        Assert.False(File.Exists(Path.Combine(folder, "gfx.png")));
    }

    /// <summary>
    /// The clean-session guarantee, existing-file half — proven with a read-only file: if the
    /// guard were gone, the write attempt would fail against the attribute and surface in
    /// <see cref="SpriteEditorSession.SaveError"/>, so "no error" here means "no write happened
    /// at all", not "the write was byte-identical".
    /// </summary>
    [Fact]
    public void ACleanSessionNeverTouchesAnExistingGfxPng()
    {
        string folder = CartFolder(PatternSheet());
        string gfx = Path.Combine(folder, "gfx.png");
        File.SetAttributes(gfx, FileAttributes.ReadOnly);
        var session = new SpriteEditorSession(folder);

        Assert.True(session.Save());

        Assert.Null(session.SaveError);
    }

    /// <summary>Same proof for the second Ctrl+S: once saved, saving again without edits attempts nothing.</summary>
    [Fact]
    public void ARepeatedSaveWithoutNewEditsIsANoOp()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);
        session.SelectColor(4);
        Stroke(session, (2, 5));
        Assert.True(session.Save());
        File.SetAttributes(Path.Combine(folder, "gfx.png"), FileAttributes.ReadOnly);

        Assert.True(session.Save());

        Assert.Null(session.SaveError);
    }

    [Fact]
    public void AFailedSaveReportsAndKeepsThePixels()
    {
        string folder = CartFolder(PatternSheet());
        string gfx = Path.Combine(folder, "gfx.png");
        File.SetAttributes(gfx, FileAttributes.ReadOnly);
        var session = new SpriteEditorSession(folder);
        session.SelectColor(5);             // pattern pixel (0,0) is 0 — this is a real change
        Stroke(session, (0, 0));

        Assert.False(session.Save());

        Assert.NotNull(session.SaveError);
        Assert.True(session.IsDirty);       // the author's work is still here, still saveable

        File.SetAttributes(gfx, FileAttributes.Normal);
        Assert.True(session.Save());        // and a retry after fixing the disk succeeds
        Assert.Null(session.SaveError);
    }

    // ---- region and coordinate contracts ----

    [Fact]
    public void RegionSelectionClampsToTheGrid()
    {
        var session = new SpriteEditorSession(CartFolder());

        session.SelectRegionCell(99, -3);

        Assert.Equal(SpriteEditorSession.GridCells - session.RegionCells, session.RegionCellX);
        Assert.Equal(0, session.RegionCellY);
        Assert.Equal(15, session.SpriteIndex);      // row 0, column 15 — Spr(n) numbering
    }

    [Fact]
    public void PaintOutsideTheRegionThrows()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.BeginStroke();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Paint(session.RegionPixels, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Paint(0, -1));
    }

    [Fact]
    public void PaintWithoutAStrokeThrows()
    {
        var session = new SpriteEditorSession(CartFolder());

        Assert.Throws<InvalidOperationException>(() => session.Paint(0, 0));
    }

    // ---- exit prompt ----

    [Fact]
    public void ACleanSessionClosesImmediately()
    {
        var session = new SpriteEditorSession(CartFolder());

        Assert.True(session.RequestClose());
        Assert.False(session.ExitPromptShown);
    }

    [Fact]
    public void ADirtySessionRaisesThePromptInsteadOfClosing()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(1);
        Stroke(session, (0, 0));

        Assert.False(session.RequestClose());
        Assert.True(session.ExitPromptShown);
    }

    [Fact]
    public void EscapeOnThePromptMeansStay()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(1);
        Stroke(session, (0, 0));
        Assert.False(session.RequestClose());   // first Esc: the prompt rises

        Assert.False(session.RequestClose());   // second Esc: stay — prompt lowers, nothing closes
        Assert.False(session.ExitPromptShown);

        Assert.False(session.RequestClose());   // still dirty: a third Esc raises it again
        Assert.True(session.ExitPromptShown);
    }

    // ---- sprite flags (moved from MapEditorSession, wave 3b-1) ----
    //
    // These are the flag bank's rules moved with it, not rewritten: absent file = zeros and
    // clean; dirty is content against the disk, per file; Save writes only the dirty bank; a
    // flag write mid-stroke commits the stroke first; a flag write shares this session's one
    // undo stack with sheet edits (the wave's whole point, replacing MapEditorSession's old
    // "one stack over two banks" now that there is only one bank left there); the length is
    // checked on the way in and the way out by one helper.

    [Fact]
    public void ACartWithoutFlagsBinOpensAsZerosAndIsClean()
    {
        var session = new SpriteEditorSession(CartFolder());

        // Every sprite reads zero, not just the one the region anchor starts on.
        for (int sprite = 0; sprite < SpriteEditorSession.GridCells * SpriteEditorSession.GridCells; sprite += 37)
        {
            SelectSprite(session, sprite);
            Assert.Equal((byte)0, session.Flags);
        }
        Assert.False(session.IsFlagsDirty);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    /// <summary>The clean-session guarantee, existing-file half, flags side: a read-only flags.bin proves a clean session attempts no write at all.</summary>
    [Fact]
    public void ACleanSessionNeverTouchesAnExistingFlagsBin()
    {
        string folder = CartFolder(flags: PatternFlags());
        string flagsPath = Path.Combine(folder, SpriteEditorSession.FlagsFileName);
        DateTime before = File.GetLastWriteTimeUtc(flagsPath);
        File.SetAttributes(flagsPath, FileAttributes.ReadOnly);
        var session = new SpriteEditorSession(folder);

        Assert.True(session.Save());

        Assert.Null(session.SaveError);
        Assert.Equal(before, File.GetLastWriteTimeUtc(flagsPath));
    }

    /// <summary>
    /// Per-file dirty and per-file save: a flags-only edit must write flags.bin and NOTHING
    /// else — no gfx.png, no gfx-layers.png — the proof that the two banks stayed independent
    /// after sharing a class.
    /// </summary>
    [Fact]
    public void ADirtyFlagsBankWritesOnlyFlagsBinAndExactlyItsLength()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);
        SelectSprite(session, 200);

        session.ToggleFlag(7);
        Assert.True(session.IsFlagsDirty);
        Assert.False(session.IsLayersDirty);
        Assert.True(session.Save());

        string[] written = Directory.GetFiles(folder);
        Assert.Single(written);
        Assert.Equal(SpriteEditorSession.FlagsFileName, Path.GetFileName(written[0]));
        Assert.Equal(SpriteEditorSession.FlagsPayloadSize, new FileInfo(written[0]).Length);
        Assert.Equal(0b1000_0000, session.Flags);
        Assert.False(session.IsDirty);
    }

    /// <summary>Dirty is content against the disk, not a history of edits — same rule as the sheet, one bank over.</summary>
    [Fact]
    public void AFlagToggledBackIsCleanAgain()
    {
        var session = new SpriteEditorSession(CartFolder(flags: PatternFlags()));
        SelectSprite(session, 12);

        session.ToggleFlag(3);
        Assert.True(session.IsFlagsDirty);
        session.ToggleFlag(3);

        Assert.False(session.IsFlagsDirty);
        Assert.False(session.IsDirty);
    }

    /// <summary>
    /// One stack over both banks: a pencil stroke and a flag write undo in reverse order, and
    /// each step carries a snapshot of BOTH — the sheet stays painted while the flag step alone
    /// is rolled back, which is what "one shared undo stack" has to mean in practice. This is
    /// the wave's replacement for the old two-banks-in-one-class test now that the banks live
    /// in two different classes with two different "other" things (layers vs. a map).
    /// </summary>
    [Fact]
    public void UndoAndRedoWalkTheSheetAndTheFlagsOneOperationAtATime()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(3);
        Stroke(session, (0, 0));                 // step 1: the sheet
        SelectSprite(session, 5);
        session.ToggleFlag(2);                   // step 2: the flags

        session.Undo();
        Assert.False(session.IsFlagSet(2));
        Assert.Equal(3, session.Pixels[0]);       // the sheet step is untouched by the flag step's undo

        session.Undo();
        Assert.Equal(0, session.Pixels[0]);
        Assert.False(session.CanUndo);

        session.Redo();
        Assert.Equal(3, session.Pixels[0]);
        Assert.False(session.IsFlagSet(2));

        session.Redo();
        Assert.True(session.IsFlagSet(2));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void AFlagWriteAfterAnUndoneStrokeClearsTheRedoFuture()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(2);
        Stroke(session, (0, 0));
        session.Undo();
        Assert.True(session.CanRedo);

        SelectSprite(session, 1);
        session.ToggleFlag(0);                   // history branched; the old future is gone

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void AFlagWriteThatChangesNothingIsInvisibleToUndoAndDirt()
    {
        var session = new SpriteEditorSession(CartFolder(flags: PatternFlags()));
        SelectSprite(session, 40);
        byte current = session.Flags;

        session.SetFlags(current);

        Assert.False(session.CanUndo);
        Assert.False(session.IsFlagsDirty);
    }

    /// <summary>
    /// A flag write while the pencil is still down closes the gesture first. Otherwise the flag
    /// step would be pushed under the pre-stroke snapshot and undo would replay the two
    /// operations in the wrong order — the first Ctrl+Z would erase the sheet instead of the
    /// flag. The exact scenario <c>MapEditorSession.SetFlags</c> proved first, one class over.
    /// </summary>
    [Fact]
    public void AFlagWriteMidStrokeCommitsTheGestureFirst()
    {
        var session = new SpriteEditorSession(CartFolder());
        SelectSprite(session, 5);      // the flag panel and the pencil agree on which sprite
        session.SelectColor(5);
        session.BeginStroke();
        session.Paint(1, 1);

        session.ToggleFlag(0);         // still sprite 5 — the region anchor never moved

        Assert.False(session.StrokeActive);
        session.Undo();
        Assert.False(session.IsFlagSet(0));
        Assert.Equal(5, PixelAt(session, 5 * 8 + 1, 1));   // the sheet gesture is still a separate, later-undone step
        session.Undo();
        Assert.Equal(0, PixelAt(session, 5 * 8 + 1, 1));
    }

    [Fact]
    public void AFlagsBinOfTheWrongLengthIsRefused()
    {
        string folder = CartFolder(flags: new byte[SpriteEditorSession.FlagsPayloadSize + 1]);

        var e = Assert.Throws<CartLoadException>(() => new SpriteEditorSession(folder));

        Assert.Contains(SpriteEditorSession.FlagsFileName, e.Message, StringComparison.Ordinal);
        Assert.Contains("256", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlagBitsOutsideTheByteAreRejectedAtTheDoor()
    {
        var session = new SpriteEditorSession(CartFolder());

        Assert.Throws<ArgumentOutOfRangeException>(() => session.IsFlagSet(SpriteEditorSession.FlagBits));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.IsFlagSet(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ToggleFlag(SpriteEditorSession.FlagBits));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ToggleFlag(-1));
        Assert.False(session.IsFlagsDirty);     // a rejected bit must not half-apply
    }

    /// <summary>The flag panel's target follows the region anchor, the same sprite the pixels and the eyedropper answer for.</summary>
    [Fact]
    public void TheFlagByteFollowsTheSelectedSpriteNotAnyOther()
    {
        var flags = new byte[SpriteEditorSession.FlagsPayloadSize];
        flags[5] = 0b0000_0001;
        flags[6] = 0b0000_0010;
        var session = new SpriteEditorSession(CartFolder(flags: flags));

        SelectSprite(session, 5);
        Assert.Equal(0b0000_0001, session.Flags);

        SelectSprite(session, 6);
        Assert.Equal(0b0000_0010, session.Flags);
    }
}

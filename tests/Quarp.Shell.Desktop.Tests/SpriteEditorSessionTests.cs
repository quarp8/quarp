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

    /// <summary>A cart folder, optionally with a gfx.png encoded from the given sheet.</summary>
    private string CartFolder(byte[]? sheet = null)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (sheet is not null)
        {
            File.WriteAllBytes(
                Path.Combine(folder, "gfx.png"),
                PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight));
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
}

using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Wave 2f's stamp tool, proven headless through <see cref="SpriteEditorSession"/>. The
/// wave's named negative-control target lives here: a print that is not exactly one undo
/// step turns <see cref="AStampIsOneUndoStep"/> red — its assertions demand that a single
/// Ctrl+Z removes the whole print and nothing else.
/// </summary>
public class SpriteEditorStampTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorStampTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-stmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SpriteEditorSession Session()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return new SpriteEditorSession(folder);
    }

    private static byte PixelAt(SpriteEditorSession session, int sheetX, int sheetY) =>
        session.Pixels[sheetY * CartData.GfxWidth + sheetX];

    private static void PaintPixel(SpriteEditorSession session, int color, int x, int y)
    {
        session.SelectColor(color);
        session.BeginStroke();
        session.Paint(x, y);
        session.EndStroke();
    }

    /// <summary>A complete rectangle-select gesture — the stamp's way of loading its source.</summary>
    private static void SelectBox(SpriteEditorSession session, int x0, int y0, int x1, int y1)
    {
        session.SelectSelectionVariant(SelectionVariant.Rectangle);
        session.BeginSelect(x0, y0);
        session.UpdateSelect(x1, y1);
        session.CommitSelect();
    }

    [Fact]
    public void AStampPrintsTheCapturedPixelsCenteredAtTheCursor()
    {
        var session = Session();
        PaintPixel(session, 7, 2, 2);
        PaintPixel(session, 9, 3, 2);
        SelectBox(session, 2, 2, 3, 2);             // source: [7, 9], 2x1

        session.SelectTool(SpriteEditorTool.Stamp);
        session.StampAt(5, 5);                      // center for width 2 puts the origin at x=4

        Assert.Equal(7, PixelAt(session, 4, 5));
        Assert.Equal(9, PixelAt(session, 5, 5));
        Assert.True(session.IsDirty);
    }

    /// <summary>Color 0 in the source prints nothing (the niche's transparency pattern) — what lies under it survives.</summary>
    [Fact]
    public void ColorZeroInTheSourceIsTransparent()
    {
        var session = Session();
        PaintPixel(session, 7, 2, 2);
        SelectBox(session, 1, 2, 3, 2);             // source: [0, 7, 0] — a hole each side
        PaintPixel(session, 9, 4, 5);               // the pixel a leaky hole would erase

        session.StampAt(5, 5);                      // origin x=4: the left hole lands exactly on the 9

        Assert.Equal(9, PixelAt(session, 4, 5));    // survived under the transparent cell
        Assert.Equal(7, PixelAt(session, 5, 5));    // the ink printed
        Assert.Equal(0, PixelAt(session, 6, 5));    // the right hole printed nothing
    }

    /// <summary>The named negative control: one print, one Ctrl+Z, gone whole — a split or doubled snapshot reddens this.</summary>
    [Fact]
    public void AStampIsOneUndoStep()
    {
        var session = Session();
        PaintPixel(session, 7, 2, 2);
        PaintPixel(session, 9, 3, 3);
        SelectBox(session, 2, 2, 3, 3);
        byte[] beforeStamp = session.Pixels.ToArray();

        session.StampAt(6, 6);
        Assert.False(session.Pixels.SequenceEqual(beforeStamp));    // the print landed

        session.Undo();
        Assert.True(session.Pixels.SequenceEqual(beforeStamp));     // one step peeled it whole
        session.Undo();                                             // the next steps are the two paints
        session.Undo();
        Assert.False(session.CanUndo);
    }

    /// <summary>No selection was ever made: the stamp is empty and the click honestly does nothing (the tooltip explains why).</summary>
    [Fact]
    public void AnInklessStampHonestlyDoesNothing()
    {
        var session = Session();
        session.SelectTool(SpriteEditorTool.Stamp);
        int version = session.Version;

        session.StampAt(4, 4);

        Assert.False(session.HasStampSource);
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(version, session.Version);
    }

    /// <summary>
    /// "Кламп регионом": the print is clipped at the region border — the center stays at the
    /// cursor and the part that would cross the border is dropped, never written into a
    /// neighbouring sprite.
    /// </summary>
    [Fact]
    public void TheStampClipsAtTheRegionBorder()
    {
        var session = Session();
        session.SelectColor(7);
        session.BeginStroke();
        session.Paint(2, 2);
        session.Paint(4, 2);                        // a 3-wide bar
        session.EndStroke();
        SelectBox(session, 2, 2, 4, 2);

        session.StampAt(0, 0);                      // origin x=-1: the leftmost source pixel has nowhere to land

        Assert.Equal(7, PixelAt(session, 0, 0));
        Assert.Equal(7, PixelAt(session, 1, 0));
        Assert.Equal(5, CountColored(session, 7));  // 3 painted + 2 printed: the third was clipped, not wrapped
        Assert.Equal(0, PixelAt(session, 8, 0));    // the neighbouring sprite never sees a stamp
    }

    /// <summary>The source is a memory taken at selection commit, not a live view of the sheet.</summary>
    [Fact]
    public void TheSourceIsCapturedAtSelectionNotLive()
    {
        var session = Session();
        PaintPixel(session, 7, 2, 2);
        SelectBox(session, 2, 2, 2, 2);             // captured: a single 7
        PaintPixel(session, 9, 2, 2);               // repaint the very pixel afterwards

        session.StampAt(5, 5);

        Assert.Equal(7, PixelAt(session, 5, 5));    // the stamp prints what was captured
    }

    /// <summary>
    /// The memory survives what the selection itself does not: Esc and region moves. Select in
    /// one sprite cell, stamp in another — the copy-between-sprites workflow the tool exists for.
    /// </summary>
    [Fact]
    public void TheSourceSurvivesEscAndRegionMoves()
    {
        var session = Session();
        PaintPixel(session, 7, 3, 3);
        SelectBox(session, 3, 3, 3, 3);

        session.ClearSelection();                   // Esc
        session.SelectRegionCell(1, 0);             // a different sprite cell drops any mask…
        Assert.False(session.HasSelection);
        Assert.True(session.HasStampSource);        // …but never the memory

        session.StampAt(4, 4);
        Assert.Equal(7, PixelAt(session, 12, 4));   // printed in the new cell (sheet x = 8 + 4)
    }

    private static int CountColored(SpriteEditorSession session, byte color)
    {
        int count = 0;
        foreach (byte pixel in session.Pixels)
        {
            if (pixel == color)
            {
                count++;
            }
        }
        return count;
    }
}

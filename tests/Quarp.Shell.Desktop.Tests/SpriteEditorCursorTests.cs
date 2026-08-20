using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The canvas cursor (M9 stage 2.5, keyboard drawing) — the session-side half of input
/// parity. The wave's named negative-control target lives here: a cursor that escapes the
/// region ("draws outside the canvas") turns <see cref="MovesClampAtEveryEdge"/> and
/// <see cref="PaintingAtTheCursorAfterAWildMoveIsAlwaysLegal"/> red, because the clamp in
/// <see cref="SpriteEditorSession.SetCursor"/> is the only thing standing between an arrow
/// key and Paint's out-of-range throw.
/// </summary>
public class SpriteEditorCursorTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorCursorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-cur-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SpriteEditorSession Session()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return new SpriteEditorSession(folder);
    }

    [Fact]
    public void TheCursorStartsAtTheRegionOrigin()
    {
        var session = Session();

        Assert.Equal((0, 0), (session.CursorX, session.CursorY));
    }

    [Fact]
    public void SetCursorLandsInsideTheRegion()
    {
        var session = Session();

        session.SetCursor(3, 5);

        Assert.Equal((3, 5), (session.CursorX, session.CursorY));
    }

    [Fact]
    public void MovesClampAtEveryEdge()
    {
        var session = Session();                    // 8-px region: legal cursor is 0-7

        session.MoveCursor(-5, -5);
        Assert.Equal((0, 0), (session.CursorX, session.CursorY));

        session.MoveCursor(1000, 1000);
        Assert.Equal((7, 7), (session.CursorX, session.CursorY));
    }

    /// <summary>
    /// The parity contract the shell relies on: after ANY sequence of moves, painting at the
    /// cursor is in-range — no second clamp exists in the shell, on purpose.
    /// </summary>
    [Fact]
    public void PaintingAtTheCursorAfterAWildMoveIsAlwaysLegal()
    {
        var session = Session();
        session.SelectColor(7);
        session.MoveCursor(999, 999);

        session.BeginStroke();
        session.Paint(session.CursorX, session.CursorY);    // would throw if the clamp lied
        session.EndStroke();

        Assert.Equal(7, session.Pixels[7 * Quarp.CartKit.CartData.GfxWidth + 7]);
        Assert.True(session.IsDirty);
    }

    /// <summary>Shrinking the region (32 → 8 px) must pull a far cursor back inside — the re-clamp in CycleRegionSize.</summary>
    [Fact]
    public void ShrinkingTheRegionPullsTheCursorBackInside()
    {
        var session = Session();
        session.CycleRegionSize();                  // 16 px
        session.CycleRegionSize();                  // 32 px
        session.SetCursor(31, 31);

        session.CycleRegionSize();                  // back to 8 px

        Assert.Equal(8, session.RegionPixels);
        Assert.True(session.CursorX < session.RegionPixels);
        Assert.True(session.CursorY < session.RegionPixels);

        session.BeginStroke();
        session.Paint(session.CursorX, session.CursorY);    // and painting there is still legal
        session.EndStroke();
    }

    [Fact]
    public void GrowingTheRegionKeepsTheCursorWhereItWas()
    {
        var session = Session();
        session.SetCursor(6, 4);

        session.CycleRegionSize();                  // 8 → 16 px: (6,4) is still inside

        Assert.Equal((6, 4), (session.CursorX, session.CursorY));
    }
}

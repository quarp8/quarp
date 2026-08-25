using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Tile 0 is the map's empty cell — the console skips it when it draws a map (MAP-FORMAT §2,
/// the PICO-8 and LIKO-12 rule) — and until 2026-08-25 both editors kept that fact to
/// themselves. The map's picker went further and painted an opaque plate over the cell, so an
/// author who drew on sprite 0 saw his own art replaced by a blank box and read the editor as
/// broken. That is the exact path the first outside author walked.
///
/// <para>This is the fix's pin. It does not test pixels — the renderers need a GraphicsDevice
/// and cannot be constructed here — but the two standing lines ARE the fix's promise in words,
/// they are pure functions of a session, and a silent editor is what the defect was. The
/// picker's dim frame is checked by eye against the same rule.</para>
/// </summary>
public class EmptyTileNoticeTests : IDisposable
{
    private readonly string _root;

    public EmptyTileNoticeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-zero-" + Guid.NewGuid().ToString("N"));
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

    private string CartFolder()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void TheMapSaysOutLoudThatTileZeroErases()
    {
        var map = new MapEditorSession(CartFolder());

        map.SelectSprite(0);
        Assert.Equal("TILE 000 IS THE EMPTY CELL - PAINTING WITH IT ERASES", MapEditorRenderer.StandingNotice(map));

        map.SelectSprite(1);
        Assert.Null(MapEditorRenderer.StandingNotice(map));
    }

    [Fact]
    public void TheSpriteEditorWarnsWhileTheAuthorIsStillDrawingOnSpriteZero()
    {
        var editor = new SpriteEditorSession(CartFolder());

        Assert.Equal(0, editor.SpriteIndex);
        Assert.Equal(
            "SPRITE 000 IS THE MAP'S EMPTY TILE - A MAP WILL NOT DRAW IT",
            SpriteEditorRenderer.StandingNotice(editor));

        editor.SelectRegionCell(1, 0);
        Assert.Equal(1, editor.SpriteIndex);
        Assert.Null(SpriteEditorRenderer.StandingNotice(editor));
    }
}

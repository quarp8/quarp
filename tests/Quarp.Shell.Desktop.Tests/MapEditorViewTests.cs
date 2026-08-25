using Microsoft.Xna.Framework;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The map screen's travel, cursor and exit prompt (M9 stage 3) — <see cref="MapEditorView"/>,
/// <see cref="MapEditorTileStep"/> and <see cref="MapEditorPaint"/>, the three owners that
/// exist so the shell's windowed dispatch has nothing of its own to get wrong. Every method
/// checked here is <b>called</b> by <c>QuarpGame.UpdateMapEditor</c>, never mirrored: delete
/// the call there and the shell breaks, delete the logic here and this file goes red — which
/// is the whole point of the wave-2k lesson these types were built from.
/// </summary>
public class MapEditorViewTests : IDisposable
{
    private readonly string _root;

    public MapEditorViewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-mapview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// The working geometry of the map screen, in console pixels since wave R3 (ADR-029):
    /// 17 columns by 8 rows of a 256x72 map, measured on the console's own 160x90 grid. It used
    /// to be measured on a 1280x720 window, which is a number this project no longer has.
    /// </summary>
    private static MapEditorLayout Layout() => MapEditorLayout.Compute(160, 90);

    /// <summary>The same screen with the tile palette raised — the only state in which a tile is clickable.</summary>
    private static MapEditorLayout PaletteLayout(int selectedSprite) =>
        MapEditorLayout.Compute(160, 90, MapEditorOverlay.Tiles, selectedSprite);

    /// <summary>How many pages of travel cover the whole map — derived, so a re-laid screen re-counts them.</summary>
    private static int PagesAcross(in MapEditorLayout layout) =>
        (MapEditorLayout.MapColumns + layout.VisibleColumns - 1) / layout.VisibleColumns;

    private static int PagesDown(in MapEditorLayout layout) =>
        (MapEditorLayout.MapRows + layout.VisibleRows - 1) / layout.VisibleRows;

    /// <summary>
    /// The direct question of this wave, keyboard half: paging reaches the far corner in a
    /// handful of presses, lands exactly on (255, 71), and the cursor is still on screen when
    /// it gets there. Break recipe: delete the <c>FollowCursor</c> call at the end of
    /// <see cref="MapEditorView.SetCursor"/> — the cursor reaches the corner and the camera
    /// stays at the origin, so the last assertion (the cursor is inside the viewport) goes red
    /// while the coordinates still look right. That is exactly the invisible-pencil bug.
    /// </summary>
    [Fact]
    public void PagingReachesTheFarCornerAndKeepsTheCursorOnScreen()
    {
        var layout = Layout();
        var view = new MapEditorView();

        for (int i = 0; i < PagesAcross(layout); i++)
        {
            view.PageCursor(layout, 1, 0);      // 16 pages of 17 columns covers 256
        }
        for (int i = 0; i < PagesDown(layout); i++)
        {
            view.PageCursor(layout, 0, 1);      // 9 pages of 8 rows covers 72
        }

        Assert.Equal((MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1), (view.CursorX, view.CursorY));
        Assert.Equal((layout.MaxCameraX, layout.MaxCameraY), (view.CameraX, view.CameraY));
        Assert.InRange(view.CursorX, view.CameraX, view.CameraX + layout.VisibleColumns - 1);
        Assert.InRange(view.CursorY, view.CameraY, view.CameraY + layout.VisibleRows - 1);
    }

    /// <summary>
    /// The camera is the map's border, in both directions. Break recipe: remove the outer
    /// <c>Math.Clamp(..., 0, layout.MaxCameraX)</c> in <c>FollowCursor</c> and the far side
    /// scrolls past the last column.
    /// </summary>
    [Fact]
    public void NoAmountOfTravelScrollsPastTheMapsBorders()
    {
        var layout = Layout();
        var view = new MapEditorView();

        for (int i = 0; i < 50; i++)
        {
            view.PageCursor(layout, 1, 1);
        }
        Assert.Equal((layout.MaxCameraX, layout.MaxCameraY), (view.CameraX, view.CameraY));

        for (int i = 0; i < 50; i++)
        {
            view.PageCursor(layout, -1, -1);
        }
        Assert.Equal((0, 0), (view.CameraX, view.CameraY));
        Assert.Equal((0, 0), (view.CursorX, view.CursorY));
    }

    /// <summary>
    /// One arrow at the viewport's edge scrolls by exactly one cell, not by a screen and not
    /// at all. Break recipe: replace <c>FollowCursor</c>'s minimal clamp with a centring jump
    /// and this goes red on the first step past the edge.
    /// </summary>
    [Fact]
    public void ArrowsScrollTheViewByOneCellOnlyWhenTheCursorLeavesIt()
    {
        var layout = Layout();
        var view = new MapEditorView();

        for (int i = 0; i < layout.VisibleColumns - 1; i++)
        {
            view.MoveCursor(layout, 1, 0);
        }
        Assert.Equal(0, view.CameraX);                          // still inside: nothing scrolled

        view.MoveCursor(layout, 1, 0);
        Assert.Equal(1, view.CameraX);                          // one past the edge: exactly one cell
        Assert.Equal(layout.VisibleColumns, view.CursorX);
    }

    /// <summary>
    /// The direct question, mouse half: the minimap's jump centres the viewport on the clicked
    /// cell and clamps at the borders, so a click in the far corner lands the cursor on
    /// (255, 71) — the same cell the keyboard's paging reaches. Break recipe: drop the
    /// <c>- layout.VisibleColumns / 2</c> centring and the middle-of-the-map case goes red
    /// while the corners still pass, naming the defect precisely.
    /// </summary>
    [Fact]
    public void TheMinimapJumpCentresTheViewportAndClampsAtTheBorders()
    {
        var layout = Layout();
        var view = new MapEditorView();

        view.JumpTo(layout, 128, 36);
        Assert.Equal((128, 36), (view.CursorX, view.CursorY));
        Assert.Equal(128 - layout.VisibleColumns / 2, view.CameraX);
        Assert.Equal(36 - layout.VisibleRows / 2, view.CameraY);

        view.JumpTo(layout, MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1);
        Assert.Equal((MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1), (view.CursorX, view.CursorY));
        Assert.Equal((layout.MaxCameraX, layout.MaxCameraY), (view.CameraX, view.CameraY));

        view.JumpTo(layout, 0, 0);
        Assert.Equal((0, 0), (view.CameraX, view.CameraY));
    }

    /// <summary>
    /// The wheel moves the camera and drags the cursor along, so the pencil is never left off
    /// screen. Break recipe: delete <c>PullCursorIntoView</c> from
    /// <see cref="MapEditorView.ScrollRows"/>.
    /// </summary>
    [Fact]
    public void TheWheelMovesTheCameraAndTheCursorRidesWithIt()
    {
        var layout = Layout();
        var view = new MapEditorView();

        view.ScrollRows(layout, 20);

        Assert.Equal(20, view.CameraY);
        Assert.InRange(view.CursorY, view.CameraY, view.CameraY + layout.VisibleRows - 1);
    }

    /// <summary>
    /// <b>Re-pinned in wave R3, and this paragraph is why.</b> This test used to widen a
    /// <em>window</em> — a 320x180 layout parked the camera past a 1280x720 layout's ceiling,
    /// and <see cref="MapEditorView.Clamp"/> had to pull it back. Since ADR-029 the map screen
    /// is laid out on the console and the console has exactly one size, so a window resize can
    /// no longer move this ceiling at all and the old scenario cannot be built. What survives is
    /// the property the old test was really about, and the router still calls
    /// <see cref="MapEditorView.Clamp"/> every frame for it: <b>a camera that has come from
    /// somewhere else must be pulled inside the drawn viewport, and the cursor with it.</b> The
    /// "somewhere else" is now a stale position rather than a stale window — a session restored
    /// on the far corner, or any future path that writes the camera without asking the layout.
    ///
    /// <para>Break recipe unchanged: make <see cref="MapEditorView.Clamp"/> a no-op and both
    /// range assertions go red.</para>
    /// </summary>
    [Fact]
    public void ClampPullsAStaleCameraBackInsideTheDrawnViewport()
    {
        var layout = Layout();
        var view = new MapEditorView();
        view.JumpTo(layout, MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1);
        Assert.Equal((layout.MaxCameraX, layout.MaxCameraY), (view.CameraX, view.CameraY));

        view.Clamp(layout);

        Assert.InRange(view.CameraX, 0, layout.MaxCameraX);
        Assert.InRange(view.CameraY, 0, layout.MaxCameraY);
        Assert.InRange(view.CursorX, view.CameraX, view.CameraX + layout.VisibleColumns - 1);
        Assert.InRange(view.CursorY, view.CameraY, view.CameraY + layout.VisibleRows - 1);
    }

    /// <summary>
    /// The exit prompt's answer table, the sprite editor's verbatim: clean leaves, dirty asks,
    /// asking twice means "stay". Break recipe: remove the <c>ExitPromptShown</c> early-return
    /// in <see cref="MapEditorView.RequestClose"/> and the third assertion goes red — Esc would
    /// then leave a dirty map instead of lowering the question.
    /// </summary>
    [Fact]
    public void TheExitPromptFollowsTheSpriteEditorsAnswerTable()
    {
        var session = new MapEditorSession(Folder("exit-prompt"));
        var view = new MapEditorView();

        Assert.True(view.RequestClose(session));            // clean: leave
        Assert.False(view.ExitPromptShown);

        // A non-zero tile on purpose: the session is dirty by CONTENT, so painting the default
        // sprite 0 onto an empty map would change nothing and leave it honestly clean — which
        // is exactly what this test caught the first time it ran.
        session.SelectSprite(5);
        session.BeginStroke();
        session.PaintTile(3, 4);
        session.EndStroke();
        Assert.True(session.IsDirty);

        Assert.False(view.RequestClose(session));           // dirty: ask
        Assert.True(view.ExitPromptShown);
        Assert.False(view.RequestClose(session));           // ask again: stay, question down
        Assert.False(view.ExitPromptShown);
    }

    /// <summary>
    /// The keyboard's tile picker lands where a click on the same strip cell lands, and its
    /// ends are ends. Both halves go through <see cref="SheetStrip"/>, so this pins the glue,
    /// not the mapping. Break recipe: change <see cref="MapEditorTileStep"/>'s
    /// <c>sheetY * SheetStrip.LaneColumns + sheetX</c> to <c>sheetX * ... + sheetY</c> — the
    /// key path and the click path stop agreeing and the equality goes red.
    /// </summary>
    [Fact]
    public void TheKeyboardTileStepLandsWhereTheClickLandsAndClampsAtTheStripEnds()
    {
        var session = new MapEditorSession(Folder("tile-step"));

        for (int i = 0; i < 3; i++)
        {
            MapEditorTileStep.Apply(session, 1, 0);
        }
        MapEditorTileStep.Apply(session, 0, 1);

        MapEditorLayout palette = PaletteLayout(session.SelectedSprite);
        Rectangle cell = palette.TileCellRect(session.SelectedSprite);
        Assert.True(palette.TryTileCell(cell.X + cell.Width / 2, cell.Y + cell.Height / 2, out int clicked));
        Assert.Equal(session.SelectedSprite, clicked);
        Assert.NotEqual(0, session.SelectedSprite);

        for (int i = 0; i < 200; i++)
        {
            MapEditorTileStep.Apply(session, 1, 1);         // walk off the far end of the strip
        }
        Assert.Equal(LastStripCellSprite(), session.SelectedSprite);

        for (int i = 0; i < 200; i++)
        {
            MapEditorTileStep.Apply(session, -1, -1);
        }
        Assert.Equal(0, session.SelectedSprite);
    }

    /// <summary>
    /// The sprite in the strip's bottom-right cell, derived rather than typed: the last column
    /// of the last lane, bottom row. A hand-written 255 would agree today and lie the moment
    /// <see cref="SheetStrip.Rows"/> changes again.
    /// </summary>
    private static int LastStripCellSprite()
    {
        SheetStrip.TryStripCellToSheetCell(
            SheetStrip.Columns - 1, SheetStrip.Rows - 1, out int sheetX, out int sheetY);
        return sheetY * SheetStrip.LaneColumns + sheetX;
    }

    /// <summary>
    /// The map of a cart that still has map.csv is read-only (MAP-FORMAT §4), and the guard
    /// lives in the one owner both input channels call, not at every call site. Break recipe:
    /// delete the <c>MapReadOnly</c> early-return in <see cref="MapEditorPaint.Begin"/> — the
    /// session throws instead of refusing, and this test turns red with an exception rather
    /// than a clean assertion, which is exactly the crash an author would have seen.
    /// </summary>
    [Fact]
    public void PaintIsRefusedOnAReadOnlyMapWithoutThrowing()
    {
        string folder = Folder("read-only");
        File.WriteAllText(Path.Combine(folder, MapEditorSession.MapSourceFileName), "0,0\n");
        var session = new MapEditorSession(folder);
        Assert.True(session.MapReadOnly);

        MapEditorPaint.Begin(session, 5, 5);
        MapEditorPaint.Continue(session, 6, 5);
        MapEditorPaint.End(session);

        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.TileAt(5, 5));
        Assert.True(session.Save());                        // clean: nothing written, nothing thrown
        Assert.False(File.Exists(Path.Combine(folder, MapEditorSession.MapFileName)));
    }

    /// <summary>
    /// The other half of the same owner: on a writable map one gesture is one undo step,
    /// however many cells it crossed. Break recipe: move <c>BeginStroke</c> out of
    /// <see cref="MapEditorPaint.Begin"/> into the shell — this file keeps passing but
    /// <c>PaintTile</c> starts throwing "outside a stroke" in the window, which is why the
    /// begin/continue/end trio lives in one owner both channels call.
    /// </summary>
    [Fact]
    public void OneGestureThroughTheSharedOwnerIsOneUndoStep()
    {
        var session = new MapEditorSession(Folder("one-step"));
        session.SelectSprite(9);

        MapEditorPaint.Begin(session, 1, 1);
        MapEditorPaint.Continue(session, 2, 1);
        MapEditorPaint.Continue(session, 3, 1);
        MapEditorPaint.End(session);

        Assert.Equal(9, session.TileAt(3, 1));
        session.Undo();
        Assert.Equal(0, session.TileAt(1, 1));
        Assert.Equal(0, session.TileAt(3, 1));
        Assert.False(session.CanUndo);
    }
}

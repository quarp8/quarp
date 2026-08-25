using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The map editor screen's geometry contract (M9 stage 3): whole-integer scales, the shell
/// standard's strip order, the seventh review's two mirrored tool columns, no overlapping
/// panels at the shell's real window sizes, and — the part that actually bites — hit tests
/// that agree with the rectangles, because <see cref="MapEditorLayout"/> is the single owner
/// both <see cref="MapEditorRenderer"/> draws from and the mouse routing asks. A drift between
/// "where the cell is" and "what a click on it means" is the bug class this file exists to
/// make impossible.
/// </summary>
public class MapEditorLayoutTests
{
    /// <summary>The shell's default window (8x the console) — where the editor will actually be used.</summary>
    private static MapEditorLayout Default() => MapEditorLayout.Compute(1280, 720);

    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));

    /// <summary>
    /// Break recipe: make any scale fractional — e.g. drop the <c>/ mapCell * mapCell</c> trim
    /// on the canvas by using <c>roomWidth</c> directly — and the whole-multiple assertions go
    /// red.
    /// </summary>
    [Theory]
    [InlineData(320, 180)]      // the UiScale anchor, below the shell's working size (carded debt)
    [InlineData(640, 360)]
    [InlineData(1280, 720)]     // the default
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void ScalesAreWholeAndAtLeastOne(int width, int height)
    {
        var layout = MapEditorLayout.Compute(width, height);

        Assert.True(layout.MapScale >= 1);
        Assert.True(layout.SheetScale >= 1);
        Assert.True(layout.MinimapScale >= 1);
        // Whole scaling is checked through the rectangles being exact multiples — a fractional
        // scale could not produce these sizes.
        Assert.Equal(0, layout.Canvas.Width % layout.MapCell);
        Assert.Equal(0, layout.Canvas.Height % layout.MapCell);
        Assert.Equal(SheetStrip.PixelWidth * layout.SheetScale, layout.Sheet.Width);
        Assert.Equal(SheetStrip.PixelHeight * layout.SheetScale, layout.Sheet.Height);
        Assert.Equal(MapEditorLayout.MapColumns * layout.MinimapScale, layout.Minimap.Width);
        Assert.Equal(MapEditorLayout.MapRows * layout.MinimapScale, layout.Minimap.Height);
        Assert.Equal((EditorIcons.IconPixels + 4) * layout.Ui, layout.ButtonSize);
        // The viewport never claims more of the map than the map has.
        Assert.InRange(layout.VisibleColumns, 1, MapEditorLayout.MapColumns);
        Assert.InRange(layout.VisibleRows, 1, MapEditorLayout.MapRows);
    }

    /// <summary>
    /// Break recipe: delete the <c>- 2 * ui</c> that lifts the canvas off the picker in
    /// <c>Compute</c> (or the <c>- button - margin</c> that reserves the right tool column) and
    /// the disjointness assertions go red.
    /// </summary>
    [Fact]
    public void AtTheDefaultWindowNothingOverlapsAndEverythingFits()
    {
        var layout = Default();
        var window = new Rectangle(0, 0, 1280, 720);

        Assert.True(window.Contains(layout.Canvas));
        Assert.True(window.Contains(layout.Sheet));
        Assert.True(window.Contains(layout.Minimap));
        Assert.True(window.Contains(layout.StatusBar));
        Assert.False(layout.Canvas.Intersects(layout.Sheet));
        Assert.False(layout.Canvas.Intersects(layout.Minimap));
        Assert.False(layout.Sheet.Intersects(layout.Minimap));
        // Everything stops above the reserved prompt line — the exit question must never hide
        // under the picker.
        Assert.True(layout.Canvas.Bottom <= layout.PromptY);
        Assert.True(layout.Sheet.Bottom <= layout.PromptY);
        Assert.True(layout.Minimap.Bottom <= layout.PromptY);
    }

    /// <summary>
    /// Break recipe: place a button the layout forgot (delete one of the two
    /// <c>buttons[placed++]</c> tool lines) — the count assertion names it; or add a button to
    /// <see cref="EditorIcons.BelongsToMapEditor"/> without placing it, same red.
    /// </summary>
    [Fact]
    public void EveryButtonOfThisScreenIsPlacedInsideTheWindowWithoutOverlaps()
    {
        var layout = Default();
        var window = new Rectangle(0, 0, 1280, 720);

        Assert.Equal(AllButtons.Count(EditorIcons.BelongsToMapEditor), layout.Buttons.Count);
        Assert.All(layout.Buttons, place => Assert.True(EditorIcons.BelongsToMapEditor(place.Id)));
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            Assert.True(window.Contains(layout.Buttons[i].Rect), $"{layout.Buttons[i].Id} is off screen");
            Assert.False(layout.Buttons[i].Rect.Intersects(layout.Canvas));
            Assert.False(layout.Buttons[i].Rect.Intersects(layout.Sheet));
            for (int j = i + 1; j < layout.Buttons.Count; j++)
            {
                Assert.False(layout.Buttons[i].Rect.Intersects(layout.Buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// The shell standard's tab strip, literally — the same order the sprite editor's test
    /// pins, so the two screens cannot drift apart. Break recipe: swap two entries of
    /// <c>rightTabs</c> in <c>Compute</c>.
    /// </summary>
    [Fact]
    public void TheTabStripFollowsTheShellStandardsOrder()
    {
        var layout = Default();
        Rectangle exit = layout.ButtonRect(EditorButton.ExitTab);
        Rectangle code = layout.ButtonRect(EditorButton.CodeTab);
        Rectangle sprites = layout.ButtonRect(EditorButton.SpritesTab);
        Rectangle tilemap = layout.ButtonRect(EditorButton.TilemapTab);
        Rectangle sound = layout.ButtonRect(EditorButton.SoundTab);
        Rectangle music = layout.ButtonRect(EditorButton.MusicTab);

        Assert.Equal(layout.Margin, exit.X);
        Assert.True(code.X < sprites.X);
        Assert.True(sprites.X < tilemap.X);
        Assert.True(tilemap.X < sound.X);
        Assert.True(sound.X < music.X);
        Assert.Equal(1280 - layout.Margin, music.Right);
        Assert.All(
            new[] { exit, code, sprites, tilemap, sound, music },
            tab => Assert.True(layout.TabStrip.Contains(tab)));
        Assert.Equal(0, layout.TabStrip.X);
        Assert.Equal(1280, layout.TabStrip.Width);
    }

    /// <summary>
    /// The seventh review, applied to this screen: the drawing surface stands between two
    /// mirrored strips of buttons, the same gap on each side. Break recipe: change the right
    /// column's <c>canvas.Right + margin</c> to <c>+ gap</c> — the symmetry assertion goes red
    /// while everything else stays green, which is exactly the drift the review was about.
    /// </summary>
    [Fact]
    public void TheCanvasStandsBetweenTwoMirroredToolColumns()
    {
        var layout = Default();
        Rectangle left = layout.ButtonRect(EditorButton.ToolPencil);
        Rectangle right = layout.ButtonRect(EditorButton.ToolEraser);

        Assert.Equal(layout.Canvas.X - left.Right, right.X - layout.Canvas.Right);
        Assert.Equal(layout.Margin, layout.Canvas.X - left.Right);
        Assert.Equal(layout.Canvas.Y, left.Y);
        Assert.Equal(layout.Canvas.Y, right.Y);
        Assert.Equal(layout.ButtonSize, left.Width);
        Assert.Equal(layout.ButtonSize, right.Width);
        Assert.True(right.Right <= 1280 - layout.Margin);
    }

    /// <summary>
    /// The number the owner will judge by eye and the order asked for by name: how much map is
    /// on screen. 35 x 11 = 385 cells of 18 432 — about 48 screens of content — and it is the
    /// SAME patch at every window the shell is used at, because the picker's cell is capped at
    /// half a map cell (see <c>Compute</c>). Without that cap the strip's scale grows with the
    /// window and the map is squeezed to <see cref="MapEditorLayout.MinMapRows"/> at 2560x1440;
    /// break the cap and this theory goes red at the two big sizes only, naming the defect.
    /// </summary>
    [Theory]
    [InlineData(640, 360)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void TheViewportShowsTheSamePatchAtEveryWorkingWindowSize(int width, int height)
    {
        var layout = MapEditorLayout.Compute(width, height);

        Assert.Equal(35, layout.VisibleColumns);
        Assert.Equal(11, layout.VisibleRows);
        Assert.True(layout.VisibleRows >= MapEditorLayout.MinMapRows);
        // The camera ceilings follow from the same numbers, and are what every travel path clamps to.
        Assert.Equal(MapEditorLayout.MapColumns - 35, layout.MaxCameraX);
        Assert.Equal(MapEditorLayout.MapRows - 11, layout.MaxCameraY);
    }

    /// <summary>
    /// The canvas hit test round-trips against the rectangle the renderer draws, at a camera
    /// that is not the origin — the case a formula that forgot to add the camera would pass at
    /// (0,0) and fail everywhere else. Break recipe: drop <c>cameraX +</c> from
    /// <see cref="MapEditorLayout.TryMapCell"/>.
    /// </summary>
    [Fact]
    public void MapCellHitTestsRoundTripThroughTheirRectanglesAtAnyCamera()
    {
        var layout = Default();
        const int cameraX = 100;
        const int cameraY = 30;

        for (int row = 0; row < layout.VisibleRows; row++)
        {
            for (int column = 0; column < layout.VisibleColumns; column++)
            {
                int cellX = cameraX + column;
                int cellY = cameraY + row;
                Rectangle rect = layout.MapCellRect(cellX, cellY, cameraX, cameraY);
                Assert.True(layout.Canvas.Contains(rect));
                Assert.True(
                    layout.TryMapCell(
                        rect.X + rect.Width / 2, rect.Y + rect.Height / 2, cameraX, cameraY,
                        out int hitX, out int hitY));
                Assert.Equal((cellX, cellY), (hitX, hitY));
            }
        }
    }

    /// <summary>
    /// A drag that wanders off the viewport keeps painting along its edge instead of tearing —
    /// and never names a cell outside the map, which is what lets
    /// <see cref="MapEditorSession.PaintTile"/> keep throwing on out-of-range input. Break
    /// recipe: remove the clamp's upper bound in <see cref="MapEditorLayout.ClampMapCell"/> and
    /// the far-side cases go red.
    /// </summary>
    [Fact]
    public void TheDragClampPullsOutsidePointsToTheNearestVisibleCell()
    {
        var layout = Default();
        const int cameraX = 221;        // the far edge: the camera at its ceiling
        const int cameraY = 61;

        layout.ClampMapCell(-500, -500, cameraX, cameraY, out int leftX, out int topY);
        Assert.Equal((cameraX, cameraY), (leftX, topY));

        layout.ClampMapCell(99999, 99999, cameraX, cameraY, out int rightX, out int bottomY);
        Assert.Equal((MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1), (rightX, bottomY));
    }

    /// <summary>
    /// Every one of the 256 tiles is pickable, and the picker holds them all with no scroll —
    /// the claim that let this screen drop <see cref="SheetScroll"/> entirely. Break recipe:
    /// change <c>SheetStrip.Rows</c> to a band that does not divide the sheet, or break
    /// <see cref="MapEditorLayout.TryTileCell"/>'s sprite arithmetic — every sprite whose lane
    /// moved goes red by number.
    /// </summary>
    [Fact]
    public void ThePickerHoldsEverySpriteAndRoundTripsEachOne()
    {
        var layout = Default();

        for (int sprite = 0; sprite < VirtualConsole.SpriteCount; sprite++)
        {
            Rectangle cell = layout.TileCellRect(sprite);
            Assert.True(layout.Sheet.Contains(cell), $"sprite {sprite} is not inside the picker window");
            Assert.True(
                layout.TryTileCell(cell.X + cell.Width / 2, cell.Y + cell.Height / 2, out int hit));
            Assert.Equal(sprite, hit);
        }
    }

    /// <summary>
    /// The minimap answers "take me there" for every corner, and the viewport outline it draws
    /// is derived from the same camera the canvas is. Break recipe: drop the
    /// <c>MinimapScale</c> division in <see cref="MapEditorLayout.TryMinimapCell"/> — the far
    /// corner then lands in the middle of the map and this goes red.
    /// </summary>
    [Fact]
    public void TheMinimapReachesEveryCornerAndItsViewportFollowsTheCamera()
    {
        var layout = Default();

        Assert.True(layout.TryMinimapCell(layout.Minimap.X, layout.Minimap.Y, out int x0, out int y0));
        Assert.Equal((0, 0), (x0, y0));
        Assert.True(
            layout.TryMinimapCell(layout.Minimap.Right - 1, layout.Minimap.Bottom - 1, out int x1, out int y1));
        Assert.Equal((MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1), (x1, y1));
        Assert.False(layout.TryMinimapCell(layout.Minimap.X - 1, layout.Minimap.Y, out _, out _));

        Rectangle viewport = layout.MinimapViewport(layout.MaxCameraX, layout.MaxCameraY);
        Assert.True(layout.Minimap.Contains(viewport));
        Assert.Equal(layout.Minimap.Right, viewport.Right);
        Assert.Equal(layout.Minimap.Bottom, viewport.Bottom);
        Assert.Equal(layout.VisibleColumns * layout.MinimapScale, viewport.Width);
    }

    /// <summary>
    /// The footer prompt is one fact with one owner: this screen does not re-derive the verbs'
    /// rectangles, it asks <see cref="SpriteEditorLayout"/> for them, so the two editors can
    /// never disagree about where "Z SAVE+EXIT" is clickable. Break recipe: replace the
    /// delegation in <see cref="MapEditorLayout.PromptVerbRect"/> with a local copy of the
    /// formula and then change one constant in <see cref="SpriteEditorLayout"/> — this goes red
    /// while both screens still "look right" in isolation.
    /// </summary>
    [Fact]
    public void ThePromptVerbsAreTheSpriteEditorsOwnRectangles()
    {
        var layout = Default();
        var sprite = SpriteEditorLayout.Compute(1280, 720, regionCells: 1);

        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Assert.Equal(sprite.PromptVerbRect(verb), layout.PromptVerbRect(verb));
            Rectangle rect = layout.PromptVerbRect(verb);
            Assert.True(
                layout.TryPromptVerb(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, out EditorPromptVerb hit));
            Assert.Equal(verb, hit);
            Assert.True(rect.Bottom <= layout.StatusBar.Y);
        }
        Assert.Equal(sprite.PromptY, layout.PromptY);
        Assert.Equal(sprite.StatusBar, layout.StatusBar);
    }

    /// <summary>
    /// The grid's lines fall exactly on cell boundaries and stay inside the canvas (wave 3d).
    /// Geometry, not pixels: the renderer only chooses whether to draw them and in what colour.
    /// Break recipe: drop the <c>Canvas.X +</c> from
    /// <see cref="MapEditorLayout.GridColumnLine"/> and the containment assertion goes red;
    /// start the loop at column 0 in the renderer instead and the line would land on the
    /// canvas frame, which is what the "interior only" range below documents.
    /// </summary>
    [Fact]
    public void TheGridLinesFallOnCellBoundariesInsideTheCanvas()
    {
        var layout = Default();

        for (int column = 1; column < layout.VisibleColumns; column++)
        {
            Rectangle line = layout.GridColumnLine(column, layout.Ui);
            Assert.Equal(layout.Canvas.X + column * layout.MapCell, line.X);
            Assert.Equal(layout.Canvas.Y, line.Y);
            Assert.Equal(layout.Canvas.Height, line.Height);
            Assert.True(layout.Canvas.Contains(new Rectangle(line.X, line.Y, 1, line.Height)));
        }
        for (int row = 1; row < layout.VisibleRows; row++)
        {
            Rectangle line = layout.GridRowLine(row, layout.Ui);
            Assert.Equal(layout.Canvas.Y + row * layout.MapCell, line.Y);
            Assert.Equal(layout.Canvas.X, line.X);
            Assert.Equal(layout.Canvas.Width, line.Width);
            Assert.True(layout.Canvas.Contains(new Rectangle(line.X, line.Y, line.Width, 1)));
        }
    }

    /// <summary>
    /// The pan gesture's arithmetic: a window x inside the canvas answers its column offset,
    /// and one to the LEFT of the canvas answers a negative one rather than sticking at zero.
    /// C# division truncates toward zero, so the naive form reports 0 for the whole first cell
    /// off the edge and a drag would stall there.
    ///
    /// <para>Break recipe: replace <c>FloorDiv</c> with plain <c>/</c> in
    /// <see cref="MapEditorLayout.CanvasColumnOffset"/> — the two negative cases go red and
    /// every positive one still passes, which is precisely the shape of that bug.</para>
    /// </summary>
    [Fact]
    public void ThePanOffsetsFloorInsteadOfTruncating()
    {
        var layout = Default();
        int cell = layout.MapCell;

        Assert.Equal(0, layout.CanvasColumnOffset(layout.Canvas.X));
        Assert.Equal(0, layout.CanvasColumnOffset(layout.Canvas.X + cell - 1));
        Assert.Equal(3, layout.CanvasColumnOffset(layout.Canvas.X + 3 * cell + cell / 2));
        Assert.Equal(-1, layout.CanvasColumnOffset(layout.Canvas.X - 1));
        Assert.Equal(-1, layout.CanvasColumnOffset(layout.Canvas.X - cell));
        Assert.Equal(-2, layout.CanvasColumnOffset(layout.Canvas.X - cell - 1));

        Assert.Equal(0, layout.CanvasRowOffset(layout.Canvas.Y));
        Assert.Equal(2, layout.CanvasRowOffset(layout.Canvas.Y + 2 * cell));
        Assert.Equal(-1, layout.CanvasRowOffset(layout.Canvas.Y - 1));
    }

    /// <summary>A point in no panel hits nothing — the negative control for four hit tests at once.</summary>
    [Fact]
    public void APointOutsideEveryPanelHitsNothing()
    {
        var layout = Default();
        int x = layout.Canvas.X + layout.Canvas.Width / 2;
        int y = layout.Canvas.Bottom + 1;    // the gap between the canvas and the picker

        Assert.False(layout.TryButton(x, y, out _));
        Assert.False(layout.TryMapCell(x, y, 0, 0, out _, out _));
        Assert.False(layout.TryTileCell(x, y, out _));
        Assert.False(layout.TryMinimapCell(x, y, out _, out _));
    }
}

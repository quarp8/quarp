using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The map editor screen's geometry contract, <b>re-pinned in wave R3 for the console</b>.
///
/// <para><b>Why every number in this file moved, in one paragraph.</b> Until this wave the map
/// screen was host UI: <c>MapEditorLayout.Compute</c> took a window size, the theory pinned here
/// was "35 x 11 cells at every working window size", and the picker and the minimap were
/// permanent panels beside the map. ADR-029 ended that — a tool screen is drawn into the
/// console's own 160x90 framebuffer with the same calls a cartridge uses — so the window is no
/// longer an input to this geometry at all and the old theories could not be adjusted, only
/// replaced. What replaced them is arithmetic with no freedom left in it: 64 rows of content
/// over 160 columns, a map cell that is 8x8 and never magnified, and eleven buttons of 10x10
/// that have to stand somewhere. That yields <b>17 x 8</b> visible cells and nothing else does.
/// The two panels that no longer fit are named in <see cref="MapEditorLayout"/>'s type comment
/// with the pixel counts that ruled them out, and both are pinned below as overlays: the palette
/// as one page of 128 tiles, the minimap as a mode at two cells to the pixel.</para>
///
/// <para>What did <b>not</b> change is what this file is really for: hit tests that agree with
/// the rectangles, because <see cref="MapEditorLayout"/> is the single owner both
/// <see cref="MapEditorRenderer"/> draws from and <see cref="MapEditorInput"/> asks. A drift
/// between "where the cell is" and "what a click on it means" is the bug class this file exists
/// to make impossible.</para>
/// </summary>
public class MapEditorLayoutTests
{
    /// <summary>The console — the only surface this screen has since wave R3.</summary>
    private static MapEditorLayout Working() => MapEditorLayout.Compute(160, 90);

    private static MapEditorLayout Palette(int selectedSprite = 0) =>
        MapEditorLayout.Compute(160, 90, MapEditorOverlay.Tiles, selectedSprite);

    private static MapEditorLayout World() =>
        MapEditorLayout.Compute(160, 90, MapEditorOverlay.World, 0);

    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));

    /// <summary>
    /// The wave's headline number, and the one the owner will judge by eye: how much map is on
    /// screen. Seventeen by eight of a 256x72 map, and every term of that is forced —
    /// <see cref="MapEditorLayout"/>'s type comment derives it. Break recipe: widen the tool
    /// block to three columns, or give the viewport a margin, and the column count drops here by
    /// name.
    /// </summary>
    [Fact]
    public void TheViewportIsSeventeenByEightWholeCells()
    {
        var layout = Working();

        Assert.Equal(new Rectangle(24, 11, 136, 64), layout.Canvas);
        Assert.Equal(17, layout.VisibleColumns);
        Assert.Equal(8, layout.VisibleRows);
        Assert.Equal(1, layout.MapScale);
        Assert.Equal(VirtualConsole.SpriteSize, layout.MapCell);
        Assert.Equal(0, layout.Canvas.Width % layout.MapCell);
        Assert.Equal(0, layout.Canvas.Height % layout.MapCell);
        // The camera ceilings follow from the same numbers, and are what every travel path
        // clamps to.
        Assert.Equal(MapEditorLayout.MapColumns - 17, layout.MaxCameraX);
        Assert.Equal(MapEditorLayout.MapRows - 8, layout.MaxCameraY);
        // The viewport reaches the screen's right edge exactly: nothing is left over to hide a
        // sliced cell in.
        Assert.Equal(160, layout.Canvas.Right);
    }

    /// <summary>
    /// Nothing overlaps anything, in all three states of the screen, and everything stands above
    /// the reserved message line. This is the assertion the host version could only make about
    /// one window size at a time; here there is one size and three states, and all three are
    /// swept. Break recipe: centre the whole-map view on the SCREEN instead of on the viewport —
    /// four of its columns land under the tool block and the button/panel assertion goes red,
    /// which is precisely the defect that placement was changed to avoid.
    /// </summary>
    [Theory]
    [InlineData(MapEditorOverlay.None)]
    [InlineData(MapEditorOverlay.Tiles)]
    [InlineData(MapEditorOverlay.World)]
    public void NothingOverlapsAnythingInAnyStateOfTheScreen(MapEditorOverlay overlay)
    {
        var layout = MapEditorLayout.Compute(160, 90, overlay, 0);
        var screen = new Rectangle(0, 0, 160, 90);

        foreach (Rectangle panel in new[] { layout.Canvas, layout.Sheet, layout.Minimap, layout.Slider })
        {
            if (panel.IsEmpty)
            {
                continue;
            }
            Assert.True(screen.Contains(panel), $"{panel} runs off the console");
            Assert.True(panel.Bottom <= layout.PromptY, $"{panel} reaches the message line");
            foreach (EditorButtonPlace place in layout.Buttons)
            {
                Assert.False(
                    place.Rect.Intersects(panel), $"{place.Id} sits under {panel}");
            }
        }
        // The two overlays are alternatives, never neighbours — that is what the enum buys.
        Assert.False(layout.Sheet.Intersects(layout.Minimap));
    }

    /// <summary>
    /// Break recipe: place a button the layout forgot (delete one of the <c>_toolSlots</c>
    /// entries) — the count assertion names it; or add a button to
    /// <see cref="EditorIcons.BelongsToMapEditor"/> without placing it, same red.
    /// </summary>
    [Fact]
    public void EveryButtonOfThisScreenIsPlacedOnTheConsoleWithoutOverlaps()
    {
        var layout = Working();
        var screen = new Rectangle(0, 0, 160, 90);

        Assert.Equal(AllButtons.Count(EditorIcons.BelongsToMapEditor), layout.Buttons.Count);
        Assert.All(layout.Buttons, place => Assert.True(EditorIcons.BelongsToMapEditor(place.Id)));
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            Assert.True(screen.Contains(layout.Buttons[i].Rect), $"{layout.Buttons[i].Id} is off screen");
            Assert.Equal(ConsoleChrome.ButtonSize, layout.Buttons[i].Rect.Width);
            for (int j = i + 1; j < layout.Buttons.Count; j++)
            {
                Assert.False(layout.Buttons[i].Rect.Intersects(layout.Buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// The shell standard's tab strip, literally — the same order the sprite editor's test pins,
    /// and from the same single owner (<see cref="EditorChrome.RightTabs"/>), so the two screens
    /// cannot drift apart. Break recipe: swap two entries of that list.
    /// </summary>
    [Fact]
    public void TheTabStripFollowsTheShellStandardsOrder()
    {
        var layout = Working();
        Rectangle exit = layout.ButtonRect(EditorButton.ExitTab);
        Rectangle code = layout.ButtonRect(EditorButton.CodeTab);
        Rectangle sprites = layout.ButtonRect(EditorButton.SpritesTab);
        Rectangle tilemap = layout.ButtonRect(EditorButton.TilemapTab);
        Rectangle sound = layout.ButtonRect(EditorButton.SoundTab);
        Rectangle music = layout.ButtonRect(EditorButton.MusicTab);

        Assert.Equal(0, exit.X);
        Assert.True(code.X < sprites.X);
        Assert.True(sprites.X < tilemap.X);
        Assert.True(tilemap.X < sound.X);
        Assert.True(sound.X < music.X);
        Assert.Equal(160, music.Right);
        Assert.All(
            new[] { exit, code, sprites, tilemap, sound, music },
            tab => Assert.True(layout.TabStrip.Contains(tab)));
        Assert.Equal(0, layout.TabStrip.X);
        Assert.Equal(160, layout.TabStrip.Width);
    }

    /// <summary>
    /// The tool block reads the way the digit keys run: pencil, hand, select, fill in the first
    /// two rows, left to right and down (REFERENCES-EDITORS §3.1). The seventh review's mirrored
    /// columns are gone with the host frame — there is no room on 160 px for a column on each
    /// side of a viewport worth the name — and this is what replaced it. Break recipe: reorder
    /// <c>_toolSlots</c> and the reading order goes red.
    /// </summary>
    [Fact]
    public void TheToolBlockReadsInTheDigitKeysOrder()
    {
        var layout = Working();
        Rectangle pencil = layout.ButtonRect(EditorButton.ToolPencil);
        Rectangle hand = layout.ButtonRect(EditorButton.ToolHand);
        Rectangle select = layout.ButtonRect(EditorButton.ToolSelect);
        Rectangle fill = layout.ButtonRect(EditorButton.ToolFill);

        Assert.Equal(new Rectangle(0, 11, 10, 10), pencil);
        Assert.Equal(pencil.Y, hand.Y);
        Assert.True(pencil.X < hand.X);
        Assert.Equal(select.Y, fill.Y);
        Assert.True(select.Y > pencil.Y);
        Assert.Equal(pencil.X, select.X);
        Assert.Equal(hand.X, fill.X);
        // The block ends above the position bar; the four spare rows are the slack the
        // arithmetic left, not a panel nobody placed.
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            Assert.True(place.Rect.Bottom <= layout.Slider.Y);
        }
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
        var layout = Working();
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
    /// <b>The overlay's whole point, as a hit test.</b> A point in the middle of the viewport is
    /// a map cell while nothing stands over the map and is <em>nothing at all</em> while
    /// something does — because a click that painted a tile through a palette the author is
    /// reading would be the exact bug the overlay's rectangle exists to prevent. Break recipe:
    /// drop the <c>CanvasLive</c> test from <see cref="MapEditorLayout.TryMapCell"/> and the two
    /// overlay cases go red while the working case still passes.
    /// </summary>
    [Fact]
    public void TheCanvasIsDeafWhileAnythingStandsOverIt()
    {
        var working = Working();
        int x = working.Canvas.X + working.Canvas.Width / 2;
        int y = working.Canvas.Y + working.Canvas.Height / 2;

        Assert.True(working.CanvasLive);
        Assert.True(working.TryMapCell(x, y, 0, 0, out _, out _));

        Assert.False(Palette().CanvasLive);
        Assert.False(Palette().TryMapCell(x, y, 0, 0, out _, out _));
        Assert.False(World().CanvasLive);
        Assert.False(World().TryMapCell(x, y, 0, 0, out _, out _));
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
        var layout = Working();
        int cameraX = layout.MaxCameraX;
        int cameraY = layout.MaxCameraY;

        layout.ClampMapCell(-500, -500, cameraX, cameraY, out int leftX, out int topY);
        Assert.Equal((cameraX, cameraY), (leftX, topY));

        layout.ClampMapCell(99999, 99999, cameraX, cameraY, out int rightX, out int bottomY);
        Assert.Equal((MapEditorLayout.MapColumns - 1, MapEditorLayout.MapRows - 1), (rightX, bottomY));
    }

    /// <summary>
    /// <b>Every one of the 256 tiles is still pickable</b> — the claim the wave owes, now that
    /// the palette shows 128 of them at a time. The sweep asks for each sprite's own page, finds
    /// its cell there, and clicks it back; and it asserts the other half of the rule, that a
    /// sprite of the OTHER page has no rectangle at all rather than a plausible one belonging to
    /// its page-mate.
    ///
    /// <para>Break recipe: drop the lane offset from <see cref="MapEditorLayout.TryTileStripCell"/>
    /// and every sprite of page 1 comes back as its page-0 twin — 128 named failures; return the
    /// page-mate's rectangle instead of <see cref="Rectangle.Empty"/> from
    /// <see cref="MapEditorLayout.TileBlockRect"/> and the absent-tile assertion goes red while
    /// the round trip still passes, which is the shape of a frame drawn around the wrong art.</para>
    /// </summary>
    [Fact]
    public void EverySpriteIsPickableOnItsOwnPageAndOnNoOther()
    {
        int perPage = SheetStrip.LaneColumns * SheetStrip.Rows;

        for (int sprite = 0; sprite < VirtualConsole.SpriteCount; sprite++)
        {
            MapEditorLayout page = Palette(sprite);
            Rectangle cell = page.TileCellRect(sprite);
            Assert.True(page.Sheet.Contains(cell), $"sprite {sprite} is not inside its own page");
            Assert.True(
                page.TryTileCell(cell.X + cell.Width / 2, cell.Y + cell.Height / 2, out int hit));
            Assert.Equal(sprite, hit);

            // The same sprite, asked of the other page: no rectangle, and the same point there
            // answers that page's own tile instead.
            MapEditorLayout other = Palette((sprite + perPage) % VirtualConsole.SpriteCount);
            Assert.True(other.TileCellRect(sprite).IsEmpty);
            Assert.True(
                other.TryTileCell(cell.X + cell.Width / 2, cell.Y + cell.Height / 2, out int neighbour));
            Assert.Equal((sprite + perPage) % VirtualConsole.SpriteCount, neighbour);
        }
        Assert.Equal(2, SheetStrip.Lanes);   // two pages hold the sheet; a third would need a third road
    }

    /// <summary>
    /// The palette is deaf while it is down — the negative control the whole overlay design
    /// rests on. Break recipe: give <see cref="MapEditorLayout.Sheet"/> its rectangle
    /// unconditionally and every one of these goes red.
    /// </summary>
    [Fact]
    public void TheLoweredPaletteHasNoRectangleAndAnswersNothing()
    {
        var working = Working();
        MapEditorLayout page = Palette();

        Assert.True(working.Sheet.IsEmpty);
        Assert.False(
            working.TryTileCell(page.Sheet.X + 4, page.Sheet.Y + 4, out _));
        Assert.False(
            working.TryTileStripCell(page.Sheet.X + 4, page.Sheet.Y + 4, out _, out _));
        Assert.True(working.TileCellRect(0).IsEmpty);
    }

    /// <summary>
    /// The block drag is confined to the page on show: a pointer dragged off either side sizes
    /// the block along that page's edge and never onto the other page's tiles, which the author
    /// cannot see. Break recipe: clamp to <c>SheetStrip.Columns</c> instead of to the lane in
    /// <see cref="MapEditorLayout.ClampTileStripCell"/> — the page-1 case starts answering
    /// column 31 for a drag off the left edge of page 0.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void ThePickerDragIsClampedToThePageOnShow(int selectedSprite)
    {
        MapEditorLayout page = Palette(selectedSprite);
        int first = page.PaletteLane * SheetStrip.LaneColumns;

        page.ClampTileStripCell(-500, -500, out int leftColumn, out int topRow);
        Assert.Equal((first, 0), (leftColumn, topRow));

        page.ClampTileStripCell(99999, 99999, out int rightColumn, out int bottomRow);
        Assert.Equal(
            (first + SheetStrip.LaneColumns - 1, SheetStrip.Rows - 1), (rightColumn, bottomRow));
    }

    /// <summary>
    /// The whole-map view reaches every corner of a 256x72 map, and the viewport outline it
    /// draws comes from the same camera the canvas is drawn from.
    ///
    /// <para><b>Why the far corner answers (254, 70) and not (255, 71)</b>, which is the one
    /// number a reader of the old test will trip over: a minimap pixel now stands for a 2x2
    /// block of cells (<see cref="MapEditorLayout.MinimapCellsPerPixel"/> — 256 cells cannot be
    /// shown one-to-one on a 160 px console, and that is arithmetic, not a choice), and it names
    /// the first cell of its block. Travel is not lost by it: the verb behind a minimap click
    /// centres the viewport on the answer and the camera clamps at the border, so the last
    /// column and the last row are on screen either way — which the camera assertion below
    /// states outright.</para>
    /// </summary>
    [Fact]
    public void TheWholeMapViewReachesEveryCornerAndItsViewportFollowsTheCamera()
    {
        var world = World();

        Assert.Equal(
            new Rectangle(28, 25, MapEditorLayout.MapColumns / 2, MapEditorLayout.MapRows / 2),
            world.Minimap);

        Assert.True(world.TryMinimapCell(world.Minimap.X, world.Minimap.Y, out int x0, out int y0));
        Assert.Equal((0, 0), (x0, y0));
        Assert.True(
            world.TryMinimapCell(world.Minimap.Right - 1, world.Minimap.Bottom - 1, out int x1, out int y1));
        Assert.Equal((MapEditorLayout.MapColumns - 2, MapEditorLayout.MapRows - 2), (x1, y1));
        Assert.False(world.TryMinimapCell(world.Minimap.X - 1, world.Minimap.Y, out _, out _));

        // A jump to that cell still parks the camera at its ceiling, so the true far corner is
        // drawn — the property the coarser pixel could have cost and does not.
        var view = new MapEditorView();
        view.JumpTo(world, x1, y1);
        Assert.Equal((world.MaxCameraX, world.MaxCameraY), (view.CameraX, view.CameraY));

        Rectangle outline = world.MinimapViewport(world.MaxCameraX, world.MaxCameraY);
        Assert.True(world.Minimap.Contains(outline));
        Assert.Equal(world.VisibleColumns / 2, outline.Width);
        Assert.Equal(world.VisibleRows / 2, outline.Height);

        // And it is deaf while it is down.
        Assert.True(Working().Minimap.IsEmpty);
        Assert.False(Working().TryMinimapCell(world.Minimap.X + 4, world.Minimap.Y + 4, out _, out _));
    }

    /// <summary>
    /// The position bar: the thumb's place is the inverse of the press's answer, so the bar
    /// cannot show the viewport in one place and travel to another. It spans the viewport's own
    /// width, which is what makes "where the thumb is" read as "where the map is".
    ///
    /// <para>Break recipe: change either side's <c>* MapColumns / Slider.Width</c> and the
    /// round trip drifts — worst at the far end, which is why the sweep walks the whole
    /// track.</para>
    /// </summary>
    [Fact]
    public void ThePositionBarsThumbAndItsPressAgreeAcrossTheWholeTrack()
    {
        var layout = Working();

        Assert.Equal(new Rectangle(24, 75, 136, 3), layout.Slider);
        Assert.Equal(layout.Canvas.X, layout.Slider.X);
        Assert.Equal(layout.Canvas.Width, layout.Slider.Width);

        for (int x = layout.Slider.X; x < layout.Slider.Right; x++)
        {
            Assert.True(layout.TrySliderColumn(x, layout.Slider.Y + 1, out int column));
            Assert.InRange(column, 0, MapEditorLayout.MapColumns - 1);
            Rectangle thumb = layout.SliderThumb(Math.Min(column, layout.MaxCameraX));
            Assert.True(layout.Slider.Contains(thumb), $"the thumb for column {column} leaves the track");
            Assert.True(thumb.Width >= 2);
        }
        Assert.False(layout.TrySliderColumn(layout.Slider.X - 1, layout.Slider.Y + 1, out _));
        Assert.Equal(layout.Slider.X, layout.SliderThumb(0).X);
    }

    /// <summary>
    /// The message line is one fact with one owner: this screen does not re-derive the verbs'
    /// rectangles, it asks <see cref="ConsoleChrome"/> for them — the same frame the sprite
    /// screen stands in, so the two console screens cannot disagree about where "ESC STAY" is
    /// clickable. Break recipe: replace the delegation in
    /// <see cref="MapEditorLayout.PromptVerbRect"/> with a local copy of the formula and then
    /// change one constant in <see cref="ConsoleChrome"/> — this goes red while both screens
    /// still "look right" in isolation.
    ///
    /// <para><b>Re-pinned in wave R3.</b> The reference used to be the CODE screen's rectangles,
    /// because that was the nearest sibling still in the host frame after wave R2 took the
    /// sprite screen out of it. The map screen has now left that frame too, so the reference is
    /// the sprite screen again — the frame it actually shares.</para>
    /// </summary>
    [Fact]
    public void ThePromptVerbsAreTheSharedConsoleChromesOwnRectangles()
    {
        var layout = Working();
        var sprites = SpriteEditorLayout.Compute(160, 90, regionCells: 1);

        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Assert.Equal(sprites.PromptVerbRect(verb), layout.PromptVerbRect(verb));
            Rectangle rect = layout.PromptVerbRect(verb);
            Assert.True(
                layout.TryPromptVerb(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, out EditorPromptVerb hit));
            Assert.Equal(verb, hit);
            Assert.True(rect.Bottom <= layout.StatusBar.Y);
        }
        Assert.Equal(sprites.PromptY, layout.PromptY);
        Assert.Equal(sprites.StatusBar, layout.StatusBar);
    }

    /// <summary>
    /// The grid's lines fall exactly on cell boundaries and stay inside the canvas. Geometry,
    /// not pixels: the renderer only chooses whether to draw them and in what colour. Break
    /// recipe: drop the <c>Canvas.X +</c> from <see cref="MapEditorLayout.GridColumnLine"/> and
    /// the containment assertion goes red.
    /// </summary>
    [Fact]
    public void TheGridLinesFallOnCellBoundariesInsideTheCanvas()
    {
        var layout = Working();

        for (int column = 1; column < layout.VisibleColumns; column++)
        {
            Rectangle line = layout.GridColumnLine(column, 1);
            Assert.Equal(layout.Canvas.X + column * layout.MapCell, line.X);
            Assert.Equal(layout.Canvas.Y, line.Y);
            Assert.Equal(layout.Canvas.Height, line.Height);
            Assert.True(layout.Canvas.Contains(line));
        }
        for (int row = 1; row < layout.VisibleRows; row++)
        {
            Rectangle line = layout.GridRowLine(row, 1);
            Assert.Equal(layout.Canvas.Y + row * layout.MapCell, line.Y);
            Assert.Equal(layout.Canvas.X, line.X);
            Assert.Equal(layout.Canvas.Width, line.Width);
            Assert.True(layout.Canvas.Contains(line));
        }
    }

    /// <summary>
    /// The pan gesture's arithmetic: a console x inside the canvas answers its column offset,
    /// and one to the LEFT of the canvas answers a negative one rather than sticking at zero.
    /// C# division truncates toward zero, so the naive form reports 0 for the whole first cell
    /// off the edge and a drag would stall there.
    ///
    /// <para>Break recipe: replace <c>FloorDiv</c> with plain <c>/</c> in
    /// <see cref="MapEditorLayout.CanvasColumnOffset"/> — the negative cases go red and every
    /// positive one still passes, which is precisely the shape of that bug.</para>
    /// </summary>
    [Fact]
    public void ThePanOffsetsFloorInsteadOfTruncating()
    {
        var layout = Working();
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

    /// <summary>A point in no panel hits nothing — the negative control for five hit tests at once.</summary>
    [Fact]
    public void APointOutsideEveryPanelHitsNothing()
    {
        var layout = Working();
        int x = layout.Canvas.X - 1;        // the gutter between the tool block and the viewport
        int y = layout.Canvas.Y + 4;

        Assert.False(layout.TryButton(x, y, out _));
        Assert.False(layout.TryMapCell(x, y, 0, 0, out _, out _));
        Assert.False(layout.TryTileCell(x, y, out _));
        Assert.False(layout.TryMinimapCell(x, y, out _, out _));
        Assert.False(layout.TrySliderColumn(x, y, out _));
    }
}

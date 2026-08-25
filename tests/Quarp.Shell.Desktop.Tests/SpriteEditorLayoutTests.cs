using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The sprite screen's geometry contract on the console (wave R2, ADR-029): whole-integer
/// scales, the dictated tab order, the two-wide tool column, the middle column of palette /
/// flags / layer tabs, the sheet window at the right edge with its slider, the two bands — and,
/// the part that actually bites, hit tests that agree with the rectangles, because
/// <see cref="SpriteEditorLayout"/> is the single owner both the renderer draws from and the
/// mouse routing asks. A drift between "where the button is" and "what a click on it means" is
/// exactly the bug class this file exists to make impossible.
///
/// <para><b>Re-pinned in wave R2, and this paragraph is the explanation the re-pin owes.</b>
/// Every number here used to be a window pixel at 1280x720 and several of the tests swept five
/// window sizes. There is no window any more: ADR-029 put this screen in the console's
/// framebuffer, so the surface is 160x90 and there is exactly one of it. Three consequences,
/// all deliberate. (1) The sweeps over window sizes are gone, because the axis they swept no
/// longer varies — what still varies is the region size, and that sweep stayed. (2) "Does it
/// fit" finally has a single answer instead of one per window, which makes the containment
/// assertions stronger than they were. (3) Some furniture genuinely moved, and where it did the
/// test says so in its own words rather than being quietly deleted: save / undo / redo / clear
/// left the status band for the tool column, because the console's status band is five pixels
/// tall and an icon-button is ten.</para>
///
/// <para>What this file no longer pins, on purpose: the pixels. Those have a golden master now
/// (<see cref="SpriteEditorScreenGoldenTests"/>), which is the thing rectangles could never
/// check — a renderer that drew the palette inside the sheet's rectangle passed every assertion
/// in this file and always would have.</para>
/// </summary>
public class SpriteEditorLayoutTests
{
    /// <summary>The console — the screen's one surface.</summary>
    private const int ScreenWidth = 160;

    private const int ScreenHeight = 90;

    private static SpriteEditorLayout Default() =>
        SpriteEditorLayout.Compute(ScreenWidth, ScreenHeight, regionCells: 1);

    private static Rectangle Screen => new(0, 0, ScreenWidth, ScreenHeight);

    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ScalesAreWholeAndAtLeastOne(int regionCells)
    {
        var layout = SpriteEditorLayout.Compute(ScreenWidth, ScreenHeight, regionCells);

        Assert.True(layout.CanvasScale >= 1);
        Assert.True(layout.SheetScale >= 1);
        // Whole-integer scaling is checked through the rectangles being exact multiples — a
        // fractional scale could not produce these sizes.
        Assert.Equal(layout.RegionPixels * layout.CanvasScale, layout.Canvas.Width);
        Assert.Equal(layout.Canvas.Width, layout.Canvas.Height);        // the region is square, so is its view
        Assert.Equal(64, layout.Canvas.Width);                          // an 8x8 sprite at zoom 8, at every region size
        // The sheet window shows the strip's whole height and a whole number of its columns: a
        // fractional scale or an untrimmed width would leave a sliced cell at an edge.
        Assert.Equal(SheetStrip.PixelHeight * layout.SheetScale, layout.Sheet.Height);
        Assert.Equal(0, layout.Sheet.Width % (VirtualConsole.SpriteSize * layout.SheetScale));
        Assert.True(layout.SheetVisiblePixels >= 1 && layout.SheetVisiblePixels < SheetStrip.PixelWidth);
        // An icon-button is the 8x8 mask plus a one-pixel frame each side.
        Assert.Equal(EditorIcons.IconPixels + 2, layout.ButtonSize);
    }

    /// <summary>
    /// The whole screen adds up: one hundred and sixty columns spent on four things and no
    /// spare, ninety rows spent on seven and no spare. This is the arithmetic the wave was asked
    /// to solve honestly, asserted rather than described.
    /// </summary>
    [Fact]
    public void TheColumnsAndRowsAddUpToTheConsoleExactly()
    {
        var layout = Default();

        // Across: tool column, canvas, middle column, sheet window.
        Assert.Equal(0, layout.ButtonRect(EditorButton.ToolSelect).X);
        Assert.Equal(2 * layout.ButtonSize, layout.Canvas.X);
        // The middle column starts where the canvas ends and is two buttons wide; its LEFT edge
        // is the layer tabs', because the palette and the flag block are nineteen pixels in a
        // twenty-pixel column and their spare one is spent on the canvas's border instead of
        // being parked at the right of the column (SpriteEditorLayout.CanvasFrame).
        Rectangle middleColumn = layout.ButtonRect(EditorButton.LayerTab1);
        Assert.Equal(layout.Canvas.Right, middleColumn.X);
        Assert.Equal(middleColumn.X + 2 * layout.ButtonSize, layout.Sheet.X);
        Assert.Equal(ScreenWidth, layout.Sheet.Right);
        // ...and the border pixel is that column's, not the canvas's: the drawing surface is
        // still the full sixty-four, which is the 8x8 sprite at zoom 8 the order protects.
        Assert.Equal(64, layout.Canvas.Width);
        Assert.Equal(layout.Canvas.X - 1, layout.CanvasFrame.X);
        Assert.Equal(layout.Canvas.Right, layout.CanvasFrame.Right - 1);
        Assert.Equal(middleColumn.X + 1, layout.Swatches.X);

        // Down: top band, rule, content, slider, rule, message, rule, status.
        Assert.Equal(0, layout.TabStrip.Y);
        Assert.Equal(layout.ButtonSize, layout.TabStrip.Height);
        Assert.Equal(layout.TabStrip.Bottom, layout.Chrome.HeaderRuleY);
        Assert.Equal(layout.Chrome.HeaderRuleY + 1, layout.Canvas.Y);
        Assert.Equal(64, layout.Chrome.ContentHeight);
        Assert.Equal(layout.Canvas.Bottom, layout.SheetSlider.Y);
        Assert.Equal(layout.SheetSlider.Bottom, layout.Chrome.FooterRuleY);
        Assert.Equal(layout.Chrome.FooterRuleY + 1, layout.PromptY);
        Assert.Equal(layout.PromptY + SystemFont.GlyphHeight, layout.StatusBar.Y);
        Assert.Equal(ScreenHeight, layout.StatusBar.Bottom);
    }

    [Fact]
    public void NothingOverlapsAndEverythingIsOnTheScreen()
    {
        var layout = Default();

        Assert.True(Screen.Contains(layout.Canvas));
        Assert.True(Screen.Contains(layout.Sheet));
        Assert.True(Screen.Contains(layout.Swatches));
        Assert.True(Screen.Contains(layout.FlagPanel));
        Assert.True(Screen.Contains(layout.SheetSlider));
        Assert.True(Screen.Contains(layout.StatusBar));
        Assert.False(layout.Canvas.Intersects(layout.Sheet));
        Assert.False(layout.Canvas.Intersects(layout.Swatches));
        Assert.False(layout.Canvas.Intersects(layout.FlagPanel));
        Assert.False(layout.Sheet.Intersects(layout.Swatches));
        Assert.False(layout.Sheet.Intersects(layout.FlagPanel));
        Assert.False(layout.SheetSlider.Intersects(layout.Sheet));
        // Panels stop above the reserved message line — the prompt must never hide under the sheet.
        Assert.True(layout.Canvas.Bottom <= layout.PromptY);
        Assert.True(layout.SheetSlider.Bottom <= layout.PromptY);
    }

    [Fact]
    public void EveryButtonIsPlacedInsideTheScreenWithoutOverlaps()
    {
        var layout = Default();

        // Every button that belongs to THIS screen is placed, none forgotten.
        // EditorIcons.BelongsToSpriteEditor is the one owner of which button lives where, so a
        // button added and not placed is still red here, and another screen's own is not a false
        // alarm.
        Assert.Equal(AllButtons.Count(EditorIcons.BelongsToSpriteEditor), layout.Buttons.Count);
        Assert.All(layout.Buttons, place => Assert.True(EditorIcons.BelongsToSpriteEditor(place.Id)));
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            Assert.True(Screen.Contains(layout.Buttons[i].Rect));
            Assert.False(layout.Buttons[i].Rect.Intersects(layout.Canvas));
            Assert.False(layout.Buttons[i].Rect.Intersects(layout.Sheet));
            for (int j = i + 1; j < layout.Buttons.Count; j++)
            {
                Assert.False(layout.Buttons[i].Rect.Intersects(layout.Buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// The tab strip, literally: exit alone at the left corner; from the right corner leftwards
    /// music, sounds, tilemaps, sprites, code. The order has one owner —
    /// <see cref="ConsoleChrome.RightTabs"/> — so no two screens can present the tabs
    /// differently.
    ///
    /// <para>That owner moved house in wave R6 and this assertion moved with it. The list used
    /// to live in the host frame, which published it in R2 because a second frame existed then;
    /// R6 deleted the host frame and the list went to its reader, which is this one. The array
    /// was moved, not copied — the assertion below still names exactly one list.</para>
    /// </summary>
    [Fact]
    public void TheTabStripFollowsTheOwnersOrder()
    {
        var layout = Default();
        Rectangle exit = layout.ButtonRect(EditorButton.ExitTab);
        Rectangle music = layout.ButtonRect(EditorButton.MusicTab);
        Rectangle sound = layout.ButtonRect(EditorButton.SoundTab);
        Rectangle tilemap = layout.ButtonRect(EditorButton.TilemapTab);
        Rectangle sprites = layout.ButtonRect(EditorButton.SpritesTab);
        Rectangle code = layout.ButtonRect(EditorButton.CodeTab);

        Assert.Equal((0, 0), (exit.X, exit.Y));
        Assert.Equal(ScreenWidth, music.Right);                         // music hugs the right corner
        Assert.True(code.X < sprites.X);                                // left-to-right at the right edge:
        Assert.True(sprites.X < tilemap.X);                             // code, sprites, tilemaps, sounds, music
        Assert.True(tilemap.X < sound.X);
        Assert.True(sound.X < music.X);
        Assert.All(new[] { exit, music, sound, tilemap, sprites, code }, tab => Assert.Equal(0, tab.Y));
        Assert.Equal(
            new[] { EditorButton.MusicTab, EditorButton.SoundTab, EditorButton.TilemapTab,
                    EditorButton.SpritesTab, EditorButton.CodeTab },
            ConsoleChrome.RightTabs);
    }

    /// <summary>
    /// The tool column after the move: TWO buttons wide and six deep, tools in the first three
    /// rows in the owner's order, then the size toggle, then the four buttons that used to sit
    /// in the status band.
    ///
    /// <para><b>Why they moved, since a reader will ask.</b> The console's status band is five
    /// pixels of text and an icon-button is ten; a band that cannot hold a button cannot hold a
    /// button row. TIC-80 answers the same problem the same way — its cut/copy/paste/undo/redo
    /// live in the toolbar, not in <c>drawStatus</c>. What the status line keeps is what the
    /// order asks of it: coordinates at the left, the sprite number at the right.</para>
    ///
    /// <para>Break recipe: insert the brush toggle anywhere but the last entry of
    /// <c>_toolSlots</c> — every position assertion below it shifts by a row or a column and
    /// goes red, which is the guard that keeps a new control from silently rearranging a column
    /// an author's hand has learned.</para>
    /// </summary>
    [Fact]
    public void TheToolColumnIsTwoWideAndCarriesTheStatusButtonsToo()
    {
        var layout = Default();
        Rectangle select = layout.ButtonRect(EditorButton.ToolSelect);
        Rectangle pencil = layout.ButtonRect(EditorButton.ToolPencil);
        Rectangle fill = layout.ButtonRect(EditorButton.ToolFill);
        Rectangle stamp = layout.ButtonRect(EditorButton.ToolStamp);
        Rectangle shape = layout.ButtonRect(EditorButton.ToolShape);
        Rectangle transform = layout.ButtonRect(EditorButton.ToolTransform);
        Rectangle size = layout.ButtonRect(EditorButton.SizeToggle);
        Rectangle clear = layout.ButtonRect(EditorButton.Clear);
        Rectangle save = layout.ButtonRect(EditorButton.Save);
        Rectangle undo = layout.ButtonRect(EditorButton.Undo);
        Rectangle redo = layout.ButtonRect(EditorButton.Redo);
        Rectangle brush = layout.ButtonRect(EditorButton.BrushToggle);
        var column = new[]
        {
            select, pencil, fill, stamp, shape, transform, size, clear, save, undo, redo, brush,
        };

        // Reading order, two per row: the six tools first, then the size toggle and clear, then
        // save, undo and redo.
        Assert.Equal((0, layout.Chrome.ContentTop), (select.X, select.Y));
        Assert.Equal((layout.ButtonSize, layout.Chrome.ContentTop), (pencil.X, pencil.Y));
        Assert.Equal(select.Y + layout.ButtonSize, fill.Y);
        Assert.Equal(fill.Y, stamp.Y);
        Assert.Equal(fill.Y + layout.ButtonSize, shape.Y);
        Assert.Equal(shape.Y, transform.Y);
        Assert.Equal(shape.Y + layout.ButtonSize, size.Y);
        Assert.Equal(size.Y, clear.Y);
        Assert.Equal(size.Y + layout.ButtonSize, save.Y);
        Assert.Equal(save.Y, undo.Y);
        Assert.Equal(save.Y + layout.ButtonSize, redo.Y);
        // The brush toggle took the column's one empty slot — beside redo, on the last row, and
        // it pushed nothing: every assertion above is the same number it was before it landed.
        Assert.Equal(redo.Y, brush.Y);
        Assert.Equal(redo.X + layout.ButtonSize, brush.X);
        // The whole column stands left of the canvas and inside the content band.
        Assert.All(column, button => Assert.True(button.Right <= layout.Canvas.Left));
        Assert.All(column, button => Assert.True(button.X == 0 || button.X == layout.ButtonSize));
        Assert.All(column, button => Assert.True(button.Y >= layout.TabStrip.Bottom));
        Assert.All(column, button => Assert.True(button.Bottom <= layout.Chrome.ContentBottom));
        // And nothing of theirs is in the status band any more — that band is text only.
        Assert.All(column, button => Assert.False(button.Intersects(layout.StatusBar)));
    }

    /// <summary>
    /// The two bands: the top one spans the whole width (it is the background the tabs and the
    /// tooltip field stand on) and the status band spans it at the bottom. Neither swallows a
    /// working panel — that is what "the chrome is separated from the canvas" means in geometry.
    /// </summary>
    [Fact]
    public void TheTabAndStatusBandsSpanTheScreenAndHoldTheirButtons()
    {
        var layout = Default();

        Assert.Equal((0, 0, ScreenWidth), (layout.TabStrip.X, layout.TabStrip.Y, layout.TabStrip.Width));
        Assert.Equal((0, ScreenWidth, ScreenHeight), (layout.StatusBar.X, layout.StatusBar.Width, layout.StatusBar.Bottom));
        foreach (EditorButton tab in new[]
        {
            EditorButton.ExitTab, EditorButton.CodeTab, EditorButton.SpritesTab,
            EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
        })
        {
            Assert.True(layout.TabStrip.Contains(layout.ButtonRect(tab)));
        }
        Assert.False(layout.TabStrip.Intersects(layout.Canvas));
        Assert.False(layout.StatusBar.Intersects(layout.Canvas));
        Assert.False(layout.TabStrip.Intersects(layout.Swatches));
        Assert.False(layout.StatusBar.Intersects(layout.Sheet));
        // The tooltip field is the strip left between the exit button and the leftmost tab, and
        // it is where a hover label goes (TIC-80's drawToolbar). It must not be zero.
        Assert.True(layout.Chrome.TooltipChars > 0);
        Assert.Equal(layout.ButtonRect(EditorButton.ExitTab).Right, layout.Chrome.TooltipField.X);
        Assert.Equal(layout.ButtonRect(EditorButton.CodeTab).X, layout.Chrome.TooltipField.Right);
    }

    /// <summary>
    /// The middle column: palette on top, the eight flag toggles under it, the five layer tabs
    /// under those — one edge for all three, one clear pixel between them, and the whole column
    /// two buttons wide so the tabs fit two abreast.
    ///
    /// <para><b>The shared edge is the right one since 2026-08-25.</b> The palette and the flag
    /// block are nineteen pixels wide (four cells of four, three gaps of one) inside a column
    /// that is twenty because the layer tabs need twenty; that twentieth pixel used to sit
    /// unused at the right of the column and now sits at its left, where it is the canvas's
    /// border — the only free pixel the 20 + 64 + 20 + 56 split had, and the reason the canvas
    /// has a visible edge at all. See <see cref="SpriteEditorLayout.CanvasFrame"/>.</para>
    ///
    /// <para>Break recipe: left-align the two blocks on <c>middleX</c> again in
    /// <see cref="SpriteEditorLayout.Compute"/> — these assertions go red, and so does
    /// <c>SpriteEditorPanelEdgeTests.TheCanvasEdgeIsVisibleEvenWhenTheSpriteIsEmpty</c>, because
    /// the palette then repaints the very column the border is drawn on.</para>
    /// </summary>
    [Fact]
    public void TheMiddleColumnStacksPaletteFlagsAndLayerTabsOnOneEdge()
    {
        var layout = Default();
        Rectangle firstTab = layout.ButtonRect(EditorButton.LayerTab1);
        Rectangle lastTab = layout.ButtonRect(EditorButton.LayerTab5);

        Assert.Equal(layout.Swatches.X, layout.FlagPanel.X);
        // One edge for all three, and since 2026-08-25 it is the RIGHT one: the palette and the
        // flag block are nineteen pixels wide in a twenty-pixel column, and their spare pixel
        // was moved from the right of the column (where it did nothing) to the left of it,
        // where it is the canvas's border. See SpriteEditorLayout.CanvasFrame.
        Assert.Equal(layout.Swatches.Right, layout.FlagPanel.Right);
        Assert.Equal(firstTab.X + 2 * layout.ButtonSize, layout.Swatches.Right);
        Assert.Equal(firstTab.X, lastTab.X);                     // five tabs, two abreast: 1 and 5 share a column
        Assert.Equal(firstTab.X + 1, layout.Swatches.X);
        Assert.Equal(firstTab.X + 1, layout.FlagPanel.X);
        Assert.Equal(layout.Chrome.ContentTop, layout.Swatches.Y);
        Assert.Equal(layout.Swatches.Bottom + 1, layout.FlagPanel.Y);
        Assert.Equal(layout.FlagPanel.Bottom + 1, firstTab.Y);
        Assert.True(firstTab.Y < lastTab.Y);                     // the five tabs wrap two abreast
        Assert.True(lastTab.Bottom <= layout.Chrome.ContentBottom);
        // The sheet window owns everything right of this column, full content height.
        Assert.Equal(layout.Chrome.ContentTop, layout.Sheet.Y);
        Assert.Equal(layout.Chrome.ContentBottom, layout.Sheet.Bottom);
        Assert.Equal(layout.Sheet.Width, layout.SheetSlider.Width);
        Assert.Equal(layout.Sheet.X, layout.SheetSlider.X);
    }

    /// <summary>
    /// The law the eye only notices when it breaks: pressing Tab must not move the furniture.
    /// The canvas box is a whole multiple of the largest region, so 8, 16 and 32 px sprites all
    /// fill exactly the same 64x64 square, and everything measured from it stays put.
    ///
    /// <para>Negative control: drop the rounding from <c>canvasBox</c> and an 8-px region would
    /// take the whole 64 rows while a 32-px one takes 64 as well — that particular pair survives,
    /// but change the content band to a height that is not a multiple of 32 and the panels start
    /// twitching, which is why the box is rounded rather than fitted.</para>
    /// </summary>
    [Fact]
    public void TabbingTheRegionSizeMovesNoPanel()
    {
        var eight = SpriteEditorLayout.Compute(ScreenWidth, ScreenHeight, regionCells: 1);
        var sixteen = SpriteEditorLayout.Compute(ScreenWidth, ScreenHeight, regionCells: 2);
        var thirtyTwo = SpriteEditorLayout.Compute(ScreenWidth, ScreenHeight, regionCells: 4);

        foreach (SpriteEditorLayout other in new[] { sixteen, thirtyTwo })
        {
            Assert.Equal(eight.Canvas, other.Canvas);
            Assert.Equal(eight.Sheet, other.Sheet);
            Assert.Equal(eight.SheetSlider, other.SheetSlider);
            Assert.Equal(eight.Swatches, other.Swatches);
            Assert.Equal(eight.FlagPanel, other.FlagPanel);
            Assert.Equal(
                eight.ButtonRect(EditorButton.SizeToggle), other.ButtonRect(EditorButton.SizeToggle));
        }
        // The premise: the three do differ where they must — one region pixel is a different
        // number of console pixels each time, which is the only thing Tab is allowed to change.
        Assert.True(eight.CanvasScale > sixteen.CanvasScale && sixteen.CanvasScale > thirtyTwo.CanvasScale);
        Assert.Equal((8, 4, 2), (eight.CanvasScale, sixteen.CanvasScale, thirtyTwo.CanvasScale));
    }

    /// <summary>
    /// Pins the stub list, which is now <b>empty</b>: the music-editor wave took the last name
    /// off it, so there is no button in this shell that is drawn but dead. The test stays and the
    /// mechanism stays with it — the day a screen is sketched before it is wired, one name goes
    /// back into <see cref="EditorIcons.IsStub"/> and this array is where it is declared.
    /// </summary>
    [Fact]
    public void ExactlyTheVerdictsButtonsAreStubs()
    {
        var stubs = Array.Empty<EditorButton>();
        foreach (EditorButton button in AllButtons)
        {
            Assert.Equal(stubs.Contains(button), EditorIcons.IsStub(button));
        }
    }

    // ---- group flyouts ----

    /// <summary>
    /// Flyout variant buttons sit in a row right of their slot, are disjoint, and their centres
    /// hit themselves — the same roundtrip discipline as every clickable rectangle. They float
    /// over the canvas on purpose: on a 160 px screen there is no space to reserve for a row
    /// that is closed on 99 % of frames.
    /// </summary>
    [Theory]
    [InlineData(EditorButton.ToolSelect, 3)]
    [InlineData(EditorButton.ToolShape, 2)]
    [InlineData(EditorButton.ToolTransform, 3)]
    [InlineData(EditorButton.SizeToggle, 3)]
    // The brush list is the longest flyout on this screen (four steps of TIC-80's BRUSH_SIZES)
    // AND it hangs off the column's bottom row, so it is the one that would run off the screen
    // first if the row ever grew — which is exactly why it is pinned here by name.
    [InlineData(EditorButton.BrushToggle, 4)]
    public void FlyoutVariantsRoundTripThroughTheirRectangles(EditorButton slot, int count)
    {
        var layout = Default();
        Rectangle anchor = layout.ButtonRect(slot);

        for (int i = 0; i < count; i++)
        {
            Rectangle rect = layout.FlyoutVariantRect(slot, i);
            Assert.True(Screen.Contains(rect));
            Assert.True(rect.X > anchor.Right);                 // rightward of the slot, never over it
            Assert.Equal(anchor.Y, rect.Y);                     // one row, photoshop-style
            Assert.True(layout.TryFlyoutVariant(rect.Center.X, rect.Center.Y, slot, out int hit));
            Assert.Equal(i, hit);
            for (int j = i + 1; j < count; j++)
            {
                Assert.False(rect.Intersects(layout.FlyoutVariantRect(slot, j)));
            }
        }
        // One past the last variant is nothing — the hit test is bounded by GroupVariantCount.
        Rectangle beyond = layout.FlyoutVariantRect(slot, count);
        Assert.False(layout.TryFlyoutVariant(beyond.Center.X, beyond.Center.Y, slot, out _));
    }

    [Fact]
    public void ButtonHitTestsRoundTripThroughTheirRectangles()
    {
        var layout = Default();
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            Assert.True(layout.TryButton(place.Rect.Center.X, place.Rect.Center.Y, out EditorButton id));
            Assert.Equal(place.Id, id);
        }
    }

    [Fact]
    public void SwatchHitTestsRoundTripThroughTheirRectangles()
    {
        var layout = Default();
        for (int i = 0; i < Palette.VisibleCount; i++)
        {
            Rectangle rect = layout.SwatchRect(i);

            Assert.True(layout.TrySwatch(rect.Center.X, rect.Center.Y, out int color));
            Assert.Equal(i, color);
            Assert.True(layout.Swatches.Contains(rect));
        }
    }

    [Fact]
    public void CanvasCornersMapToTheFirstAndLastPixel()
    {
        var layout = Default();

        Assert.True(layout.TryCanvasPixel(layout.Canvas.X, layout.Canvas.Y, out int x0, out int y0));
        Assert.Equal((0, 0), (x0, y0));

        Assert.True(layout.TryCanvasPixel(layout.Canvas.Right - 1, layout.Canvas.Bottom - 1, out int x1, out int y1));
        Assert.Equal((layout.RegionPixels - 1, layout.RegionPixels - 1), (x1, y1));
    }

    [Fact]
    public void SheetHitTestFindsTheSpriteMappedIntoTheStrip()
    {
        var layout = Default();
        int cell = layout.SheetScale * VirtualConsole.SpriteSize;
        const int sprite = 200;
        SheetStrip.SpriteToStripCell(sprite, out int stripColumn, out int stripRow);
        int scroll = Math.Min(stripColumn * VirtualConsole.SpriteSize, layout.SheetMaxScroll);

        Assert.True(layout.TrySheetCell(
            layout.Sheet.X + (stripColumn * VirtualConsole.SpriteSize - scroll) * layout.SheetScale + cell / 2,
            layout.Sheet.Y + stripRow * cell + cell / 2,
            scroll,
            out int cellX,
            out int cellY));

        Assert.Equal((8, 12), (cellX, cellY));
        Assert.Equal(sprite, cellY * 16 + cellX);
    }

    /// <summary>
    /// A region straddling a lane boundary is still highlighted as the two pieces it looks like
    /// on the strip. The boundary is between sheet rows 7 and 8 (<see cref="SheetStrip.Rows"/>
    /// is untouched by wave R2), so this pins sprite 126: its top row sits at the bottom of lane
    /// 0 and its bottom row reappears at the top of lane 1, sixteen columns right.
    /// </summary>
    [Fact]
    public void SixteenPixelRegionAtSprite126SplitsAcrossTwoStripLanes()
    {
        var layout = Default();
        int cell = layout.SheetScale * VirtualConsole.SpriteSize;
        int lane = SheetStrip.LaneColumns;

        IReadOnlyList<Rectangle> pieces = layout.SheetRegionHighlights(
            sheetCellX: 14, sheetCellY: SheetStrip.Rows - 1, regionCells: 2, scroll: 0);

        Assert.Equal(2, pieces.Count);
        Assert.Equal(
            new Rectangle(
                layout.Sheet.X + 14 * cell, layout.Sheet.Y + (SheetStrip.Rows - 1) * cell, 2 * cell, cell),
            pieces[0]);
        Assert.Equal(new Rectangle(layout.Sheet.X + (lane + 14) * cell, layout.Sheet.Y, 2 * cell, cell), pieces[1]);
    }

    [Fact]
    public void RegionInsideOneLaneStaysOneRectangle()
    {
        var layout = Default();
        int cell = layout.SheetScale * VirtualConsole.SpriteSize;

        IReadOnlyList<Rectangle> pieces = layout.SheetRegionHighlights(
            sheetCellX: 5, sheetCellY: 1, regionCells: 2, scroll: 0);

        Rectangle only = Assert.Single(pieces);
        Assert.Equal(new Rectangle(layout.Sheet.X + 5 * cell, layout.Sheet.Y + cell, 2 * cell, 2 * cell), only);
    }

    /// <summary>
    /// The scrolled hit test agrees with the scrolled picture: the same point names a cell
    /// shifted by exactly the scroll offset, and the last pixel inside the window is a real cell
    /// rather than a gap — at rest AND scrolled to the end, where it must be the strip's very
    /// last column.
    /// </summary>
    [Fact]
    public void SheetHitTestFollowsTheScrollAndTheWindowIsWholeCellsToItsEdge()
    {
        var layout = Default();
        int cell = layout.SheetScale * VirtualConsole.SpriteSize;
        int visibleColumns = layout.SheetVisiblePixels / VirtualConsole.SpriteSize;

        Assert.True(layout.TrySheetCell(
            layout.Sheet.X + cell / 2, layout.Sheet.Y + cell / 2, 2 * VirtualConsole.SpriteSize,
            out int cellX, out _));
        Assert.Equal(2, cellX);     // two sprite columns scrolled off → the first visible cell is column 2

        // The last pixel inside the window belongs to the last visible column, not to a gap.
        Assert.True(layout.TrySheetCell(
            layout.Sheet.Right - 1, layout.Sheet.Bottom - 1, 0, out int lastX, out int lastY));
        Assert.True(SheetStrip.TryStripCellToSheetCell(
            visibleColumns - 1, SheetStrip.Rows - 1, out int expectedX, out int expectedY));
        Assert.Equal((expectedX, expectedY), (lastX, lastY));

        // Scrolled to the end, that same pixel is the strip's very last cell — sprite 255.
        Assert.True(layout.TrySheetCell(
            layout.Sheet.Right - 1, layout.Sheet.Bottom - 1, layout.SheetMaxScroll,
            out int endX, out int endY));
        Assert.Equal(VirtualConsole.SpriteCount - 1, endY * SpriteEditorSession.GridCells + endX);

        // One pixel further right is off the screen and belongs to nobody.
        Assert.False(layout.TrySheetCell(layout.Sheet.Right, layout.Sheet.Y + cell / 2, 0, out _, out _));
    }

    /// <summary>
    /// A point owned by nobody. On the console this is a harder thing to find than it was in a
    /// window — the screen's corners are the exit button and the music tab — so the point picked
    /// is the strip of rows under the canvas and left of the slider, which is exactly the slack
    /// the vertical budget left over.
    /// </summary>
    [Fact]
    public void APointOutsideEveryPanelHitsNothing()
    {
        var layout = Default();
        int x = layout.Canvas.X;
        int y = layout.SheetSlider.Y + 1;
        Assert.True(y < layout.PromptY && x < layout.SheetSlider.X);     // the premise of the point

        Assert.False(layout.TryCanvasPixel(x, y, out _, out _));
        Assert.False(layout.TrySheetCell(x, y, 0, out _, out _));
        Assert.False(layout.TrySwatch(x, y, out _));
        Assert.False(layout.TryFlag(x, y, out _));
        Assert.False(layout.TryButton(x, y, out _));
        Assert.False(layout.TryPromptVerb(x, y, out _));
    }

    /// <summary>The drag clamp: a stroke wandering off the canvas keeps painting along its edge.</summary>
    [Fact]
    public void TheDragClampPullsOutsidePointsToTheNearestEdgePixel()
    {
        var layout = Default();

        layout.ClampCanvasPixel(0, layout.Canvas.Center.Y, out int leftX, out _);
        Assert.Equal(0, leftX);

        layout.ClampCanvasPixel(layout.Canvas.Right + 500, layout.Canvas.Bottom + 500, out int farX, out int farY);
        Assert.Equal((layout.RegionPixels - 1, layout.RegionPixels - 1), (farX, farY));
    }

    /// <summary>
    /// The prompt's three clickable verbs (mouse parity for Z/X/Esc): disjoint, on the message
    /// line, right-aligned to the screen's edge, and each one's centre hits itself.
    ///
    /// <para>Right-aligned and NOT measured from the heading, which is the one thing about this
    /// line that changed with the move: the console's heading grows from "UNSAVED." to
    /// "SAVE FAILED." when a save fails, and a verb that slid sideways under the pointer while
    /// the author was deciding would be the worst possible moment to move a button.</para>
    /// </summary>
    [Fact]
    public void PromptVerbsAreDisjointHitTestableAndAboveTheStatusBar()
    {
        var layout = Default();
        var verbs = new[] { EditorPromptVerb.SaveAndExit, EditorPromptVerb.Discard, EditorPromptVerb.Stay };

        Assert.Equal(ScreenWidth - ConsoleChrome.Margin, layout.PromptVerbRect(EditorPromptVerb.Stay).Right);
        for (int i = 0; i < verbs.Length; i++)
        {
            Rectangle rect = layout.PromptVerbRect(verbs[i]);
            Assert.Equal(layout.PromptY, rect.Y);
            Assert.True(rect.Bottom <= layout.StatusBar.Y);
            Assert.True(layout.TryPromptVerb(rect.Center.X, rect.Center.Y, out EditorPromptVerb hit));
            Assert.Equal(verbs[i], hit);
            for (int j = i + 1; j < verbs.Length; j++)
            {
                Assert.False(rect.Intersects(layout.PromptVerbRect(verbs[j])));
            }
        }
        // The heading has room to its left even in its longer, failed-save form.
        Assert.True(ConsoleChrome.PromptFailedHeading.Length <= layout.Chrome.PromptHeadingChars);
    }
}

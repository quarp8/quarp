using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The editor screen's geometry contract under the owner's verdict layout (M9 stage 2.5,
/// sixth review applied): whole-integer scales, the dictated strip order (exit left;
/// music-sounds-tilemaps-sprites-code from the right corner leftwards; the six-slot toolbar
/// column with no action row; the right column at the window's edge — palette on top, then
/// ONE narrow row holding the size toggle and the five layer tabs, then the sheet window
/// owning every remaining pixel of the column down to its slider; the full-width tab and
/// status bands), no overlapping panels at the shell's real window sizes, and — the part
/// that actually bites — hit tests that agree with the rectangles, because
/// <see cref="SpriteEditorLayout"/> is the single owner both the renderer draws from and the
/// mouse routing asks. A drift between "where the button is" and "what a click on it means"
/// is exactly the bug class this file exists to make impossible.
/// </summary>
public class SpriteEditorLayoutTests
{
    /// <summary>The shell's default window (8x the console) — where the editor will actually be used.</summary>
    private static SpriteEditorLayout Default() => SpriteEditorLayout.Compute(1280, 720, regionCells: 1);

    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));

    [Theory]
    [InlineData(320, 180)]      // the UiScale anchor, the smallest sensible window
    [InlineData(640, 360)]
    [InlineData(1280, 720)]     // the default
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void ScalesAreWholeAndAtLeastOne(int width, int height)
    {
        var layout = SpriteEditorLayout.Compute(width, height, regionCells: 1);

        Assert.True(layout.CanvasScale >= 1);
        Assert.True(layout.SheetScale >= 1);
        // Whole-integer scaling is checked through the rectangles being exact multiples —
        // a fractional scale could not produce these sizes.
        Assert.Equal(layout.RegionPixels * layout.CanvasScale, layout.Canvas.Width);
        Assert.Equal(layout.Canvas.Width, layout.Canvas.Height);        // the region is square, so is its view
        // The sheet window shows the strip's whole height and a whole number of its columns:
        // a fractional scale or an untrimmed width would leave a sliced cell at an edge.
        Assert.Equal(SheetStrip.PixelHeight * layout.SheetScale, layout.Sheet.Height);
        Assert.Equal(0, layout.Sheet.Width % (VirtualConsole.SpriteSize * layout.SheetScale));
        Assert.True(layout.SheetVisiblePixels >= 1 && layout.SheetVisiblePixels < SheetStrip.PixelWidth);
        // Icon buttons are 8-px masks at scale Ui plus symmetric padding — whole by construction.
        Assert.Equal((EditorIcons.IconPixels + 4) * layout.Ui, layout.ButtonSize);
    }

    [Fact]
    public void AtTheDefaultWindowNothingOverlapsAndEverythingFits()
    {
        var layout = Default();
        var window = new Rectangle(0, 0, 1280, 720);

        Assert.True(window.Contains(layout.Canvas));
        Assert.True(window.Contains(layout.Sheet));
        Assert.True(window.Contains(layout.Swatches));
        Assert.True(window.Contains(layout.SheetSlider));
        Assert.True(window.Contains(layout.StatusBar));
        Assert.False(layout.Canvas.Intersects(layout.Sheet));
        Assert.False(layout.Canvas.Intersects(layout.Swatches));
        Assert.False(layout.Sheet.Intersects(layout.Swatches));
        Assert.False(layout.SheetSlider.Intersects(layout.Sheet));
        Assert.False(layout.SheetSlider.Intersects(layout.Swatches));
        // Panels stop above the reserved prompt line — the prompt must never hide under the sheet.
        Assert.True(layout.Canvas.Bottom <= layout.PromptY);
        Assert.True(layout.SheetSlider.Bottom <= layout.PromptY);
    }

    [Fact]
    public void EveryButtonIsPlacedInsideTheWindowWithoutOverlaps()
    {
        var layout = Default();
        var window = new Rectangle(0, 0, 1280, 720);

        Assert.Equal(AllButtons.Length, layout.Buttons.Count);          // all 22, none forgotten
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            Assert.True(window.Contains(layout.Buttons[i].Rect));
            Assert.False(layout.Buttons[i].Rect.Intersects(layout.Canvas));
            for (int j = i + 1; j < layout.Buttons.Count; j++)
            {
                Assert.False(layout.Buttons[i].Rect.Intersects(layout.Buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// The verdict's tab strip, literally: exit alone at the left margin; from the right
    /// corner leftwards music, sounds, tilemaps, sprites, code — all icon-sized, all on the
    /// top row, no text headers anywhere in the geometry.
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

        Assert.Equal((layout.Margin, layout.Margin), (exit.X, exit.Y));
        Assert.Equal(1280 - layout.Margin, music.Right);                // music hugs the right corner
        Assert.True(code.X < sprites.X);                                // left-to-right at the right edge:
        Assert.True(sprites.X < tilemap.X);                             // code, sprites, tilemaps, sounds, music
        Assert.True(tilemap.X < sound.X);
        Assert.True(sound.X < music.X);
        Assert.All(
            new[] { exit, music, sound, tilemap, sprites, code },
            tab => Assert.Equal(layout.Margin, tab.Y));
    }

    /// <summary>
    /// The toolbar after the owner's second review: ONE column of six slots left of the
    /// canvas, top-to-bottom select / pencil / fill / stamp / shape / transform — the action
    /// row is gone (its verbs live in the transform group slot and the status bar's clear).
    /// An action row reappearing under the column would land buttons right of the margin
    /// column and turn the All-assert red.
    /// </summary>
    [Fact]
    public void TheToolbarIsOneColumnOfSixLeftOfTheCanvas()
    {
        var layout = Default();
        Rectangle select = layout.ButtonRect(EditorButton.ToolSelect);
        Rectangle pencil = layout.ButtonRect(EditorButton.ToolPencil);
        Rectangle fill = layout.ButtonRect(EditorButton.ToolFill);
        Rectangle stamp = layout.ButtonRect(EditorButton.ToolStamp);
        Rectangle shape = layout.ButtonRect(EditorButton.ToolShape);
        Rectangle transform = layout.ButtonRect(EditorButton.ToolTransform);
        var column = new[] { select, pencil, fill, stamp, shape, transform };

        Assert.True(select.Y < pencil.Y && pencil.Y < fill.Y && fill.Y < stamp.Y
            && stamp.Y < shape.Y && shape.Y < transform.Y);
        Assert.All(column, tool => Assert.Equal(layout.Margin, tool.X));
        Assert.All(column, tool => Assert.True(tool.Right <= layout.Canvas.Left));
        // The column starts below the tab band and stays above the prompt line.
        Assert.True(select.Y >= layout.TabStrip.Bottom);
        Assert.True(transform.Bottom <= layout.PromptY);
    }

    /// <summary>
    /// The strips of the second review: both bands span the whole window width (they are the
    /// background that separates chrome from canvas, so a gap would break the reading), hold
    /// their buttons, and never touch the panels between them.
    /// </summary>
    [Fact]
    public void TheTabAndStatusBandsSpanTheWindowAndHoldTheirButtons()
    {
        var layout = Default();

        Assert.Equal((0, 0, 1280), (layout.TabStrip.X, layout.TabStrip.Y, layout.TabStrip.Width));
        Assert.Equal((0, 1280, 720), (layout.StatusBar.X, layout.StatusBar.Width, layout.StatusBar.Bottom));
        foreach (EditorButton tab in new[]
        {
            EditorButton.ExitTab, EditorButton.CodeTab, EditorButton.SpritesTab,
            EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
        })
        {
            Assert.True(layout.TabStrip.Contains(layout.ButtonRect(tab)));
        }
        // The bands must not swallow the working panels — that is what "отделены" means in geometry.
        Assert.False(layout.TabStrip.Intersects(layout.Canvas));
        Assert.False(layout.StatusBar.Intersects(layout.Canvas));
        Assert.False(layout.TabStrip.Intersects(layout.Swatches));
        Assert.False(layout.StatusBar.Intersects(layout.Sheet));
    }

    /// <summary>
    /// The right column after the SIXTH review, which is this wave's whole subject. Four
    /// facts, each one of the owner's four points: the palette still owns the top-right
    /// corner; the size toggle and all five layer tabs share ONE row under it, in that order,
    /// all at the same Y (before this wave the toggle sat in a band beside the palette and
    /// the tabs were a row of their own); the sheet window is strictly wider than the palette
    /// it used to copy and reaches from that row down to its slider; and the slider ends the
    /// column, with no reserved emptiness left under it — the space below the slider is now
    /// less than one button tall, where it used to be about a third of the column.
    /// </summary>
    [Fact]
    public void TheRightColumnFollowsTheSixthReview()
    {
        var layout = Default();
        Rectangle firstTab = layout.ButtonRect(EditorButton.LayerTab1);
        Rectangle lastTab = layout.ButtonRect(EditorButton.LayerTab5);
        Rectangle toggle = layout.ButtonRect(EditorButton.SizeToggle);

        Assert.Equal(1280 - layout.Margin, layout.Swatches.Right);      // the palette hugs the edge
        Assert.True(layout.Swatches.X >= layout.Canvas.Right);
        // Point 2: ONE row — toggle first, then tabs 1..5, every one of them on the same line.
        Assert.All(
            new[] { firstTab, lastTab, layout.ButtonRect(EditorButton.LayerTab3) },
            tab => Assert.Equal(toggle.Y, tab.Y));
        Assert.True(toggle.Right <= firstTab.X && firstTab.X < lastTab.X);
        Assert.True(layout.Swatches.Bottom <= toggle.Y);                // the row is under the palette
        Assert.True(toggle.Bottom <= layout.Sheet.Y);                   // and above the sheet window
        Assert.Equal(layout.Sheet.X, toggle.X);                         // row and window share a left edge
        // Point 3: the window is wider than the palette that used to dictate its width, and
        // taller than the pre-2k four-row window — the strip's full height at a whole scale.
        Assert.True(layout.Sheet.Width > layout.Swatches.Width);
        Assert.Equal(SheetStrip.PixelHeight * layout.SheetScale, layout.Sheet.Height);
        Assert.True(layout.Sheet.Height > 4 * VirtualConsole.SpriteSize * layout.Ui);
        // Point 4: no air right of the canvas below the palette — the window starts within a
        // cell of the canvas's own margin and ends flush with the palette at the window edge.
        Assert.Equal(1280 - layout.Margin, layout.Sheet.Right);
        Assert.True(layout.Sheet.X >= layout.Canvas.Right + layout.Margin);
        Assert.True(
            layout.Sheet.X - layout.Canvas.Right
                < layout.Margin + VirtualConsole.SpriteSize * layout.SheetScale);
        // The slider directly under the sheet window, same width, and it ends the column.
        Assert.Equal(layout.Sheet.X, layout.SheetSlider.X);
        Assert.Equal(layout.Sheet.Width, layout.SheetSlider.Width);
        Assert.True(layout.Sheet.Bottom <= layout.SheetSlider.Y);
        Assert.True(layout.SheetSlider.Bottom < layout.PromptY);
        Assert.True(layout.PromptY - layout.SheetSlider.Bottom < layout.ButtonSize);
    }

    /// <summary>
    /// The sixth review's other geometric law, which the eye only notices when it breaks:
    /// pressing Tab must not move the furniture. The canvas box is a whole multiple of the
    /// largest region, so 8, 16 and 32 px all fill exactly the same square, and everything
    /// measured from it — the narrow row, the sheet window, the slider — stays put.
    ///
    /// <para>The window sizes are a Theory on purpose: at 1280x720 the free height happens to
    /// be a multiple of 32 and the pre-2k formula (largest square per region, no rounding)
    /// agreed by luck. At 320x180 it does not — an 8-px region would take 72 px where a 32-px
    /// one takes 64 — so dropping the rounding from <c>canvasBox</c> turns exactly that case
    /// red, which is this test's negative control.</para>
    /// </summary>
    [Theory]
    [InlineData(320, 180)]
    [InlineData(640, 360)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void TabbingTheRegionSizeMovesNoPanel(int width, int height)
    {
        var eight = SpriteEditorLayout.Compute(width, height, regionCells: 1);
        var sixteen = SpriteEditorLayout.Compute(width, height, regionCells: 2);
        var thirtyTwo = SpriteEditorLayout.Compute(width, height, regionCells: 4);

        foreach (SpriteEditorLayout other in new[] { sixteen, thirtyTwo })
        {
            Assert.Equal(eight.Canvas, other.Canvas);
            Assert.Equal(eight.Sheet, other.Sheet);
            Assert.Equal(eight.SheetSlider, other.SheetSlider);
            Assert.Equal(eight.Swatches, other.Swatches);
            Assert.Equal(
                eight.ButtonRect(EditorButton.SizeToggle), other.ButtonRect(EditorButton.SizeToggle));
        }
        // The premise: the three do differ where they must — one region pixel is a different
        // number of window pixels each time, which is the only thing Tab is allowed to change.
        Assert.True(eight.CanvasScale > sixteen.CanvasScale && sixteen.CanvasScale > thirtyTwo.CanvasScale);
    }

    /// <summary>
    /// The status bar holds its four buttons in the second review's order: clear outermost
    /// right ("справа от redo" — the owner's words), then redo, undo, and save innermost.
    /// </summary>
    [Fact]
    public void TheStatusButtonsLiveInsideTheStatusBar()
    {
        var layout = Default();
        Rectangle save = layout.ButtonRect(EditorButton.Save);
        Rectangle undo = layout.ButtonRect(EditorButton.Undo);
        Rectangle redo = layout.ButtonRect(EditorButton.Redo);
        Rectangle clear = layout.ButtonRect(EditorButton.Clear);

        Assert.True(layout.StatusBar.Contains(save));
        Assert.True(layout.StatusBar.Contains(undo));
        Assert.True(layout.StatusBar.Contains(redo));
        Assert.True(layout.StatusBar.Contains(clear));
        Assert.Equal(1280 - layout.Margin, clear.Right);        // clear hugs the right edge, a margin in
        Assert.True(save.X < undo.X && undo.X < redo.X && redo.X < clear.X);
    }

    /// <summary>
    /// Pins the stub list as of wave 2f: only the four future-editor tabs stay dead — the
    /// verdict's whole toolbar is live now (select and stamp woke last). A tab waking early,
    /// or a tool going dark again, makes this red before any UI is even drawn.
    /// </summary>
    [Fact]
    public void ExactlyTheVerdictsButtonsAreStubs()
    {
        var stubs = new[]
        {
            EditorButton.CodeTab, EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
        };
        foreach (EditorButton button in AllButtons)
        {
            Assert.Equal(stubs.Contains(button), EditorIcons.IsStub(button));
        }
    }

    // ---- group flyouts (wave 2e) ----

    /// <summary>
    /// Flyout variant buttons sit in a row right of their slot, are disjoint, and their
    /// centres hit themselves — the same roundtrip discipline as every clickable rectangle.
    /// </summary>
    [Theory]
    [InlineData(EditorButton.ToolSelect, 3)]    // 2 → 3 in wave 2g: the owner's wand is the select group's third variant
    [InlineData(EditorButton.ToolShape, 2)]
    [InlineData(EditorButton.ToolTransform, 3)]
    [InlineData(EditorButton.SizeToggle, 3)]    // 8/16/32 — the size list rides the same flyout machinery (wave 2h)
    public void FlyoutVariantsRoundTripThroughTheirRectangles(EditorButton slot, int count)
    {
        var layout = Default();
        Rectangle anchor = layout.ButtonRect(slot);
        var window = new Rectangle(0, 0, 1280, 720);

        for (int i = 0; i < count; i++)
        {
            Rectangle rect = layout.FlyoutVariantRect(slot, i);
            Assert.True(window.Contains(rect));
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
        int scroll = stripColumn * VirtualConsole.SpriteSize;

        Assert.True(layout.TrySheetCell(
            layout.Sheet.X + cell / 2, layout.Sheet.Y + stripRow * cell + cell / 2, scroll,
            out int cellX, out int cellY));

        Assert.Equal((8, 12), (cellX, cellY));
        Assert.Equal(sprite, cellY * 16 + cellX);
    }

    /// <summary>
    /// A region straddling a lane boundary is still highlighted as the two pieces it looks
    /// like on the strip. The boundary moved with the strip's shape (it is between sheet rows
    /// 7 and 8 now, not 3 and 4), so this pins sprite 126: its top row sits at the bottom of
    /// lane 0 and its bottom row reappears at the top of lane 1, sixteen columns right.
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
    /// The scrolled hit test agrees with the scrolled picture: the same window point names a
    /// cell shifted by exactly the scroll offset. The second half replaces the old
    /// "refuses the slack" check, which had become a dead branch: the sixth review's window
    /// is trimmed to whole sprite columns, so there IS no slack any more, and the live way to
    /// say that is the opposite claim — the last pixel inside the window is a real cell, at
    /// rest AND scrolled to the end, where it must be the strip's very last column.
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

        // One pixel further right is outside the window and belongs to nobody.
        Assert.False(layout.TrySheetCell(layout.Sheet.Right, layout.Sheet.Y + cell / 2, 0, out _, out _));
    }

    [Fact]
    public void APointOutsideEveryPanelHitsNothing()
    {
        var layout = Default();

        // The window's corner: margin space, owned by no panel and no button.
        Assert.False(layout.TryCanvasPixel(0, 0, out _, out _));
        Assert.False(layout.TrySheetCell(0, 0, 0, out _, out _));
        Assert.False(layout.TrySwatch(0, 0, out _));
        Assert.False(layout.TryButton(0, 0, out _));
        Assert.False(layout.TryPromptVerb(0, 0, out _));
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
    /// The prompt's three clickable verbs (mouse parity for Z/X/Esc): disjoint, above the
    /// status bar, and each one's centre hits itself — the same roundtrip discipline every
    /// clickable rectangle here lives under.
    /// </summary>
    [Fact]
    public void PromptVerbsAreDisjointHitTestableAndAboveTheStatusBar()
    {
        var layout = Default();
        var verbs = new[] { EditorPromptVerb.SaveAndExit, EditorPromptVerb.Discard, EditorPromptVerb.Stay };

        for (int i = 0; i < verbs.Length; i++)
        {
            Rectangle rect = layout.PromptVerbRect(verbs[i]);
            Assert.True(rect.Bottom <= layout.StatusBar.Y);
            Assert.True(layout.TryPromptVerb(rect.Center.X, rect.Center.Y, out EditorPromptVerb hit));
            Assert.Equal(verbs[i], hit);
            for (int j = i + 1; j < verbs.Length; j++)
            {
                Assert.False(rect.Intersects(layout.PromptVerbRect(verbs[j])));
            }
        }
    }
}

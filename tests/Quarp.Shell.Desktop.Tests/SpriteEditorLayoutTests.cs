using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The editor screen's geometry contract under the owner's verdict layout (M9 stage 2.5, the
/// second review applied): whole-integer scales, the dictated strip order (exit left;
/// music-sounds-tilemaps-sprites-code from the right corner leftwards; the six-slot toolbar
/// column with no action row; palette over the layers stub over the sheet; the full-width
/// tab and status bands), no overlapping panels at the shell's real window sizes, and — the
/// part that actually bites — hit tests that agree with the rectangles, because
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
        Assert.Equal(VirtualConsole.SheetWidth * layout.SheetScale, layout.Sheet.Width);
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
        Assert.True(window.Contains(layout.LayersStub));
        Assert.True(window.Contains(layout.StatusBar));
        Assert.False(layout.Canvas.Intersects(layout.Sheet));
        Assert.False(layout.Canvas.Intersects(layout.Swatches));
        Assert.False(layout.Sheet.Intersects(layout.Swatches));
        Assert.False(layout.LayersStub.Intersects(layout.Swatches));
        Assert.False(layout.LayersStub.Intersects(layout.Sheet));
        // Panels stop above the reserved prompt line — the prompt must never hide under the sheet.
        Assert.True(layout.Canvas.Bottom <= layout.PromptY);
        Assert.True(layout.Sheet.Bottom <= layout.PromptY);
    }

    [Fact]
    public void EveryButtonIsPlacedInsideTheWindowWithoutOverlaps()
    {
        var layout = Default();
        var window = new Rectangle(0, 0, 1280, 720);

        Assert.Equal(AllButtons.Length, layout.Buttons.Count);          // all 16, none forgotten
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

    /// <summary>The right column stacks in the verdict's order: palette, then the layers stub, then the sheet.</summary>
    [Fact]
    public void TheRightColumnStacksPaletteLayersSheet()
    {
        var layout = Default();

        Assert.True(layout.Swatches.X >= layout.Canvas.Right);
        Assert.True(layout.Swatches.Bottom <= layout.LayersStub.Y);
        Assert.True(layout.LayersStub.Bottom <= layout.Sheet.Y);
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
    public void SheetHitTestFindsTheClickedCell()
    {
        var layout = Default();
        int cell = layout.SheetScale * VirtualConsole.SpriteSize;

        Assert.True(layout.TrySheetCell(
            layout.Sheet.X + 5 * cell + cell / 2, layout.Sheet.Y + 9 * cell + cell / 2, out int cellX, out int cellY));

        Assert.Equal((5, 9), (cellX, cellY));
    }

    [Fact]
    public void APointOutsideEveryPanelHitsNothing()
    {
        var layout = Default();

        // The window's corner: margin space, owned by no panel and no button.
        Assert.False(layout.TryCanvasPixel(0, 0, out _, out _));
        Assert.False(layout.TrySheetCell(0, 0, out _, out _));
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

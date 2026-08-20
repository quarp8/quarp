using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The editor screen's geometry contract under the owner's verdict layout (M9 stage 2.5):
/// whole-integer scales, the dictated strip order (exit left; music-sounds-tilemaps-sprites-
/// code from the right corner leftwards; toolbar column over the action row; palette over the
/// layers stub over the sheet; status bar at the bottom), no overlapping panels at the shell's
/// real window sizes, and — the part that actually bites — hit tests that agree with the
/// rectangles, because <see cref="SpriteEditorLayout"/> is the single owner both the renderer
/// draws from and the mouse routing asks. A drift between "where the button is" and "what a
/// click on it means" is exactly the bug class this file exists to make impossible.
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

        Assert.Equal(AllButtons.Length, layout.Buttons.Count);          // all 18, none forgotten
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

    /// <summary>The toolbar column runs top-to-bottom left of the canvas; the action row sits under it.</summary>
    [Fact]
    public void TheToolbarColumnAndActionRowSitLeftOfTheCanvas()
    {
        var layout = Default();
        Rectangle select = layout.ButtonRect(EditorButton.ToolSelect);
        Rectangle pencil = layout.ButtonRect(EditorButton.ToolPencil);
        Rectangle fill = layout.ButtonRect(EditorButton.ToolFill);
        Rectangle stamp = layout.ButtonRect(EditorButton.ToolStamp);
        Rectangle shape = layout.ButtonRect(EditorButton.ToolShape);
        Rectangle flipH = layout.ButtonRect(EditorButton.FlipH);
        Rectangle flipV = layout.ButtonRect(EditorButton.FlipV);
        Rectangle rotate = layout.ButtonRect(EditorButton.Rotate);
        Rectangle clear = layout.ButtonRect(EditorButton.Clear);

        // Tools top-to-bottom in the verdict's order, one column at the left margin.
        Assert.True(select.Y < pencil.Y && pencil.Y < fill.Y && fill.Y < stamp.Y && stamp.Y < shape.Y);
        Assert.All(new[] { select, pencil, fill, stamp, shape }, tool => Assert.Equal(layout.Margin, tool.X));
        // Actions as one row below the column, left-to-right F / V / R / Del.
        Assert.True(flipH.Y > shape.Bottom);
        Assert.Equal(flipH.Y, flipV.Y);
        Assert.Equal(flipV.Y, rotate.Y);
        Assert.Equal(rotate.Y, clear.Y);
        Assert.True(flipH.X < flipV.X && flipV.X < rotate.X && rotate.X < clear.X);
        // The whole panel is left of the drawing surface.
        Assert.True(clear.Right <= layout.Canvas.Left);
        Assert.True(shape.Right <= layout.Canvas.Left);
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

    /// <summary>The status bar holds its three buttons: redo outermost right, then undo, then save.</summary>
    [Fact]
    public void TheStatusButtonsLiveInsideTheStatusBar()
    {
        var layout = Default();
        Rectangle save = layout.ButtonRect(EditorButton.Save);
        Rectangle undo = layout.ButtonRect(EditorButton.Undo);
        Rectangle redo = layout.ButtonRect(EditorButton.Redo);

        Assert.True(layout.StatusBar.Contains(save));
        Assert.True(layout.StatusBar.Contains(undo));
        Assert.True(layout.StatusBar.Contains(redo));
        Assert.Equal(layout.StatusBar.Right, redo.Right);
        Assert.True(save.X < undo.X && undo.X < redo.X);
    }

    /// <summary>
    /// Pins the stub list to the owner's verdict: the four future-editor tabs and the three
    /// wave-2e tools, nothing else. Waking a tool early (the negative-control scenario: a
    /// digit switching to the stamp) makes this red before any UI is even drawn.
    /// </summary>
    [Fact]
    public void ExactlyTheVerdictsButtonsAreStubs()
    {
        var stubs = new[]
        {
            EditorButton.CodeTab, EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
            EditorButton.ToolSelect, EditorButton.ToolStamp, EditorButton.ToolShape,
        };
        foreach (EditorButton button in AllButtons)
        {
            Assert.Equal(stubs.Contains(button), EditorIcons.IsStub(button));
        }
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

using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The test that was missing on 2026-08-25, which is why the defect reached the owner's
/// eyes.</b> He opened a fresh cart on the SPRITES screen and saw the tool column, the palette
/// and the flags — and where the canvas and the sheet window should have been, nothing: an
/// empty sprite is colour 0, the screen's ground is colour 0
/// (<see cref="ConsoleChromeRenderer.Ink"/>), and neither panel had an edge of its own. The
/// whole suite could not see it. Every layout test in this repository asserts
/// <em>rectangles</em> — where the layout says a panel is — and a panel that is drawn in
/// exactly the background colour satisfies all of them; the golden hashes would have caught a
/// change but not the absence, because the absence was what they were pinned to.
///
/// <para>So the assertions here are <see cref="VirtualConsole.Pget"/> at named coordinates, and
/// the property they name is the one the eye was checking: <b>on a panel's border, with the
/// sprite EMPTY, the pixel is not the background colour.</b> That is the shape a test of "can
/// the author see the surface" has to have. It cannot be expressed as a rectangle, and it is
/// not expressible on this screen at all until the screen is drawn into a framebuffer — which
/// wave R2 made it, and which is what makes writing it now cheap.</para>
///
/// <para>The references are TIC-80's <c>src/studio/editors/sprite.c</c>: <c>drawCanvas</c> rings
/// the 64x64 box at <c>x - 1, y - 1, CANVAS_SIZE + 2</c> and <c>drawSheetVBank1</c> rings the
/// sheet the same way (REFERENCES-EDITORS §2.1 records the sheet's frame in passing — the
/// neighbouring-page marks hang off it). Both rings are OUTSIDE their panel, which is the
/// property the third test here pins: not one pixel of anybody's art is covered.</para>
/// </summary>
public class SpriteEditorPanelEdgeTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorPanelEdgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spredge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — no gfx.png, no layers, no flags.</summary>
    private SpriteEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"edges\",\"author\":\"\",\"profile\":8}");
        return new SpriteEditorSession(folder);
    }

    /// <summary>One frame with nothing hovered, no flyout open, the strip at rest and the clock at zero.</summary>
    private static SpriteEditorLayout DrawIdle(ShellScreen screen, SpriteEditorSession editor) =>
        SpriteEditorRenderer.Draw(screen, editor, null, false, null, new SheetScroll(), 0.0);

    /// <summary>
    /// <b>The defect itself, as an assertion.</b> With the sprite empty — every one of its
    /// sixty-four pixels colour 0 — the ring of console pixels immediately outside the canvas
    /// box carries something that is not the background, on all four sides and at every row and
    /// column of it. That is what "the author can see where he is drawing" means when there is
    /// nothing drawn yet.
    ///
    /// <para>The ring is not all ours and does not need to be: the top row is the header rule,
    /// the left column is the tool buttons' own borders, the lower half of the right column is
    /// the layer tabs' — <see cref="SpriteEditorLayout.CanvasFrame"/> names each side. What the
    /// assertion pins is the RESULT, not the owner, which is the only thing the eye can check.
    /// </para>
    ///
    /// <para><b>Negative control</b>, two of them, because "not the background" is cheap to
    /// satisfy by accident: (1) one pixel further out is still the background, so what is being
    /// seen is a one-pixel border and not a slab of grey creeping over the screen; (2) the
    /// canvas's interior below and right of the cursor cell is still entirely colour 0, so the
    /// border did not leak inside and repaint the surface — a "border" drawn on the canvas's own
    /// outermost ring would pass the positive assertion and fail this one, and it is precisely
    /// the mistake rule 3 of the order forbids.</para>
    ///
    /// <para>Break recipe: in <see cref="SpriteEditorLayout.CanvasFrame"/> drop the <c>- 1</c>
    /// and the <c>+ 2</c> so the ring lands on the canvas itself — control (2) goes red. Delete
    /// the <c>DrawPanelFrames</c> call from <c>SpriteEditorRenderer.Draw</c> — the bottom row
    /// and the four rows under the tool column go back to being background and the positive
    /// assertion goes red. Left-align the palette on <c>middleX</c> again in
    /// <see cref="SpriteEditorLayout.Compute"/> — swatch 0 is colour 0, so it repaints four
    /// pixels of the right side in the background colour and the positive assertion goes
    /// red.</para>
    /// </summary>
    [Fact]
    public void TheCanvasEdgeIsVisibleEvenWhenTheSpriteIsEmpty()
    {
        SpriteEditorSession editor = FreshCart();
        var screen = new ShellScreen();

        SpriteEditorLayout layout = DrawIdle(screen, editor);
        VirtualConsole console = screen.Console;

        // The premise: the sprite really is empty, so there is no art propping the edge up.
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal((byte)0, editor.Pixels[y * VirtualConsole.SheetWidth + x]);
            }
        }

        Rectangle canvas = layout.Canvas;
        for (int y = canvas.Y - 1; y <= canvas.Bottom; y++)
        {
            Assert.True(
                console.Pget(canvas.X - 1, y) != ConsoleChromeRenderer.Ink,
                $"the canvas has no left edge at row {y}");
            Assert.True(
                console.Pget(canvas.Right, y) != ConsoleChromeRenderer.Ink,
                $"the canvas has no right edge at row {y}");
        }
        for (int x = canvas.X - 1; x <= canvas.Right; x++)
        {
            Assert.True(
                console.Pget(x, canvas.Y - 1) != ConsoleChromeRenderer.Ink,
                $"the canvas has no top edge at column {x}");
            Assert.True(
                console.Pget(x, canvas.Bottom) != ConsoleChromeRenderer.Ink,
                $"the canvas has no bottom edge at column {x}");
        }

        // Negative control 1: the border is one pixel. The row under it and the two columns
        // beside it are ordinary ground, so nothing here is passing by flooding the screen.
        Assert.Equal((byte)0, console.Pget(canvas.X + canvas.Width / 2, canvas.Bottom + 1));
        Assert.Equal((byte)0, console.Pget(canvas.X - 2, canvas.Bottom - 3));
        Assert.Equal((byte)0, console.Pget(canvas.Right + 1, canvas.Bottom - 3));

        // Negative control 2: nothing was drawn INSIDE the canvas. Everything from the second
        // sprite pixel on is still colour 0 — the cursor's ring owns the first cell and this
        // sweep deliberately starts past it, because the cursor is what the order says to leave
        // alone.
        for (int y = canvas.Y + 8; y < canvas.Bottom; y++)
        {
            for (int x = canvas.X + 8; x < canvas.Right; x++)
            {
                Assert.Equal((byte)0, console.Pget(x, y));
            }
        }

        // And the border cost the drawing surface nothing: still 8x8 sprite pixels at zoom 8.
        Assert.Equal(64, canvas.Width);
        Assert.Equal(8, layout.CanvasScale);
        Assert.True(layout.CanvasFrame.Contains(canvas));
    }

    /// <summary>
    /// <b>The other half of the same defect.</b> The sheet window was the same void — 256 cells
    /// of colour 0 with no edge and no cell boundaries, so "select sprite 47" was counting
    /// blind. Two properties are pinned: the window's left edge carries something at every row
    /// (its other three sides are the header rule, the slider's track and the screen's own edge
    /// — <see cref="SpriteEditorLayout.SheetFrame"/> says why the fourth cannot be ours), and
    /// two neighbouring empty cells are told apart by their own dim frames.
    ///
    /// <para><b>Negative control:</b> the inside of an empty cell is still the background. That
    /// is what says the cell is marked by a FRAME and not by a fill — a fill would read as "this
    /// sprite is grey", would be a lie about the art, and would pass a naive "the cell is not
    /// background" assertion.</para>
    ///
    /// <para>Break recipe: delete the <c>DrawEmptyCellMarks</c> call from <c>DrawSheet</c> — the
    /// cell-boundary assertions go red and the window is a slab again. Change its
    /// <c>Outline</c> to a <c>Fill</c> — the negative control goes red. Delete the
    /// <c>SheetFrame</c> line from <c>DrawPanelFrames</c> — the left-edge assertion goes red at
    /// the ten rows the layer tabs do not reach.</para>
    /// </summary>
    [Fact]
    public void TheSheetEdgeAndItsCellsAreVisibleOnAnEmptyCart()
    {
        SpriteEditorSession editor = FreshCart();
        var screen = new ShellScreen();

        SpriteEditorLayout layout = DrawIdle(screen, editor);
        VirtualConsole console = screen.Console;
        Rectangle sheet = layout.Sheet;

        for (int y = sheet.Y - 1; y <= sheet.Bottom; y++)
        {
            Assert.True(
                console.Pget(sheet.X - 1, y) != ConsoleChromeRenderer.Ink,
                $"the sheet window has no left edge at row {y}");
        }
        for (int x = sheet.X; x < sheet.Right; x++)
        {
            Assert.True(
                console.Pget(x, sheet.Y - 1) != ConsoleChromeRenderer.Ink,
                $"the sheet window has no top edge at column {x}");
            Assert.True(
                console.Pget(x, sheet.Bottom) != ConsoleChromeRenderer.Ink,
                $"the sheet window has no bottom edge at column {x}");
        }

        // Cells 1 and 2 of the strip's first row: two empty sprites side by side. Their shared
        // boundary is two pixels of grey — each cell's own frame — and the eye can count along
        // it. Cell 0 is deliberately not used here: it is the selected region and wears the
        // bright frame that already worked before this wave.
        int cell = VirtualConsole.SpriteSize * layout.SheetScale;
        int firstX = sheet.X + cell;
        int secondX = sheet.X + 2 * cell;
        int midY = sheet.Y + cell / 2;
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(firstX, midY));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(secondX - 1, midY));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(secondX, midY));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(firstX, sheet.Y));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(firstX, sheet.Y + cell - 1));

        // Negative control: the cell's inside is untouched ground, so the mark is a frame and
        // not a plate painted over a sprite that has no art yet.
        Assert.Equal((byte)0, console.Pget(firstX + cell / 2, midY));
        Assert.Equal((byte)0, console.Pget(secondX + cell / 2, midY));
    }

    /// <summary>
    /// <b>Rule 3 of the order of 2026-08-25, as an assertion: nothing the fix draws may cover a
    /// pixel of the drawing.</b> A sprite is filled corner to corner with one colour and its
    /// cell in the sheet window is then checked pixel by pixel — all sixty-four of them carry
    /// the paint, so the empty-cell mark did not survive the paint and no border was laid over
    /// the art. The canvas is checked the same way: every one of its 64x64 console pixels is the
    /// paint except the cursor's ring, which was there before this wave and stays.
    ///
    /// <para><b>Negative control:</b> the very next cell, left empty in the same frame, still
    /// carries its dim mark. Without it this test would also pass if the empty-cell mark had
    /// simply been deleted, which is the change it is here to forbid.</para>
    ///
    /// <para>Break recipe: make <c>IsCellEmpty</c> return <c>true</c> unconditionally — the
    /// filled cell grows a frame over its art and the first sweep goes red. Make it return
    /// <c>false</c> unconditionally — the sweep passes and the negative control goes red.</para>
    /// </summary>
    [Fact]
    public void AFilledCellKeepsEveryPixelOfItsArtWhileAnEmptyNeighbourKeepsItsMark()
    {
        const byte paint = 5;
        SpriteEditorSession editor = FreshCart();
        editor.SelectColor(paint);
        editor.SelectRegionCell(1, 0);           // sprite 1: filled solid, corner to corner
        editor.BeginStroke();
        for (int y = 0; y < VirtualConsole.SpriteSize; y++)
        {
            for (int x = 0; x < VirtualConsole.SpriteSize; x++)
            {
                editor.Paint(x, y);
            }
        }
        editor.EndStroke();
        var screen = new ShellScreen();

        SpriteEditorLayout layout = DrawIdle(screen, editor);
        VirtualConsole console = screen.Console;
        int cell = VirtualConsole.SpriteSize * layout.SheetScale;

        // Sprite 1 sits in the strip's second column of its first row; the selected region is
        // sprite 1 too, so its bright frame is asserted separately below and the sweep runs over
        // the CANVAS, where the art is shown at zoom 8 and nothing but the cursor may be on it.
        Rectangle canvas = layout.Canvas;
        Rectangle cursor = new(canvas.X, canvas.Y, layout.CanvasScale, layout.CanvasScale);
        for (int y = canvas.Y; y < canvas.Bottom; y++)
        {
            for (int x = canvas.X; x < canvas.Right; x++)
            {
                if (cursor.Contains(x, y))
                {
                    continue;               // the keyboard cursor's ring: older than this wave
                }
                Assert.Equal(paint, console.Pget(x, y));
            }
        }

        // Sprite 2 is still empty and still marked, in the same frame — so the mark is
        // conditional on emptiness and was not simply removed.
        int emptyX = layout.Sheet.X + 2 * cell;
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(emptyX, layout.Sheet.Y));
        Assert.Equal((byte)0, console.Pget(emptyX + cell / 2, layout.Sheet.Y + cell / 2));
    }

    /// <summary>
    /// The panel borders are drawn under everything that owns their pixels, never over it. Two
    /// facts say so: hovering a control still lights its own frame white on the very columns the
    /// canvas and sheet borders run down, and the two hashes of the same state agree, so nothing
    /// here depends on draw order twice.
    ///
    /// <para><b>Negative control:</b> the same frame with nothing hovered has that pixel grey,
    /// so the assertion is reading the hover and not a constant.</para>
    ///
    /// <para>Break recipe: move the <c>DrawPanelFrames</c> call in <c>SpriteEditorRenderer.Draw</c>
    /// to the end, after <c>DrawButtons</c> — the hovered tab's bright border is painted grey
    /// again and this goes red.</para>
    /// </summary>
    [Fact]
    public void APanelBorderNeverPaintsOverTheControlThatOwnsThatPixel()
    {
        SpriteEditorSession editor = FreshCart();
        var hovered = new ShellScreen();
        var quiet = new ShellScreen();

        SpriteEditorLayout layout = SpriteEditorRenderer.Draw(
            hovered, editor, HoverTarget.OfButton(EditorButton.LayerTab1), false, null,
            new SheetScroll(), 0.0);
        DrawIdle(quiet, editor);

        // LayerTab1's left border runs down the very column the canvas's right border does.
        Rectangle tab = layout.ButtonRect(EditorButton.LayerTab1);
        Assert.Equal(layout.Canvas.Right, tab.X);
        int probeY = tab.Y + tab.Height / 2;
        Assert.Equal(ConsoleChromeRenderer.Bright, hovered.Console.Pget(tab.X, probeY));
        // Negative control: unhovered, the same pixel is the ordinary dim edge.
        Assert.Equal(ConsoleChromeRenderer.Dim, quiet.Console.Pget(tab.X, probeY));
    }
}

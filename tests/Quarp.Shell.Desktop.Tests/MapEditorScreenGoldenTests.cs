using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The golden master of the map editor screen</b> — what wave R3 was worth doing for, and the
/// same instrument <c>LibraryScreenGoldenTests</c> (R1) and <c>SpriteEditorScreenGoldenTests</c>
/// (R2) put on the two screens that moved before this one.
///
/// <para>Until this wave the map editor was painted at the window's native resolution through a
/// <c>SpriteBatch</c>, and there was no artefact of it a test could look at: no buffer, no
/// pixels, only draw calls into a graphics device no headless runner has. Every layout assertion
/// in the suite was therefore about <em>rectangles</em> — where the layout said a panel was — and
/// none about pixels, so a renderer that drew the tile palette in the minimap's rectangle would
/// have passed all of them. Now the screen is drawn into a <see cref="Framebuffer"/> by the same
/// core calls a cartridge uses, so it can be hashed by exactly the owner that hashes a
/// cartridge's frame: <see cref="FrameHash"/>. Same digest, same 16-hex text form, same
/// discipline. There is no second hasher in this repository and this file does not introduce
/// one.</para>
///
/// <para><b>These constants are not determinism anchors.</b> The eight anchors and the twelve
/// demo hashes are facts about the <em>cartridge's</em> console and must never move without a
/// verdict. These are facts about a tool screen's layout, they belong to no cross-architecture
/// promise, and they are expected to move whenever the screen is deliberately redesigned. The
/// discipline that does carry over is PLAYBOOK §4's: never re-pin silently. If one of these
/// changes, the commit message says which pixel moved and why.</para>
///
/// <para><b>Where these five constants came from — read this before re-pinning one.</b> Wave R3,
/// like R1 and R2 before it, was carried out in an environment with no .NET SDK and no package
/// feed, so nothing in the repository could be built or run. The hashes below were therefore
/// <em>derived</em>, not observed: by transliterating <c>VirtualConsole</c>'s <c>Cls</c>,
/// <c>RectFill</c>, <c>Rect</c>, <c>Print</c> and <c>Plot</c> together with
/// <see cref="SystemFont"/>'s glyph table, <c>EditorIcons</c>' mask table and this screen's draw
/// order, and running <see cref="FrameHash"/>'s FNV-1a over the result. That model was first
/// checked against <b>all six hashes already pinned in this suite</b> — the three in
/// <c>LibraryScreenGoldenTests</c> and the three in <c>SpriteEditorScreenGoldenTests</c> — and
/// reproduced every one of them exactly, which is the evidence that the rasterizer, the font and
/// the icon masks are modelled right; what remains unproven by that check is only this file's own
/// transcription of <em>this</em> screen's draw order. So: <b>if one of these five fails on the
/// first real build while the <c>Pget</c> probes above it all pass, the overwhelmingly likely
/// explanation is a slip in that transcription and not a defect in the screen</b> — check the
/// probes, look at the frame, and re-pin with a note saying so. If a probe fails too, the screen
/// genuinely changed and the ordinary rule applies: say which pixel moved and why.</para>
///
/// <para><b>Why the probes are here at all.</b> A bare hash mismatch says "something moved" and
/// nothing else. The <c>Pget</c> assertions above each hash name the structural facts the picture
/// is supposed to have — the viewport starts at column 24, the grid falls on cell boundaries, a
/// tile's art comes off the sibling session's sheet, the palette page lies over the map, the
/// whole-map view folds two cells into a pixel — so a failure tells whoever reads it whether the
/// screen is broken or merely redrawn.</para>
/// </summary>
public class MapEditorScreenGoldenTests : IDisposable
{
    private readonly string _root;

    public MapEditorScreenGoldenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-mapscreen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — no gfx.png, no map.bin.</summary>
    private (MapEditorSession Map, SpriteEditorSession Sheet) FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"golden\",\"author\":\"\",\"profile\":8}");
        return (new MapEditorSession(folder), new SpriteEditorSession(folder));
    }

    /// <summary>
    /// A cart with something to look at: sprite 1 is a yellow diagonal, and three map cells hold
    /// it — (0,0), (1,1), (2,2), so a viewport drawn at the wrong pitch or a minimap folded at
    /// the wrong ratio cannot pass by accident.
    /// </summary>
    private (MapEditorSession Map, SpriteEditorSession Sheet) DrawnCart()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = FreshCart();
        sheet.SelectRegionCell(1, 0);           // sprite 1, the tile the map will carry
        sheet.SelectColor(8);
        sheet.BeginStroke();
        for (int i = 0; i < 8; i++)
        {
            sheet.Paint(i, i);
        }
        sheet.EndStroke();

        map.SelectSprite(1);
        map.BeginStroke();
        map.PaintTile(0, 0);
        map.PaintTile(1, 1);
        map.PaintTile(2, 2);
        map.EndStroke();
        return (map, sheet);
    }

    /// <summary>One frame with nothing hovered and no tooltip due.</summary>
    private static MapEditorLayout DrawIdle(
        ShellScreen screen, MapEditorSession map, SpriteEditorSession sheet, MapEditorView view) =>
        MapEditorRenderer.Draw(screen, map, sheet, view, null, false);

    /// <summary>
    /// The screen an author meets on a brand-new cart: an empty 17x8 viewport with the grid on,
    /// the pencil in hand, tile 0 selected, nothing saved and nothing to undo — and the standing
    /// notice that tile 000 is the map's empty cell.
    /// </summary>
    [Fact]
    public void AFreshCartOpensOnAnEmptyMapWithTheGridOn()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = FreshCart();
        var view = new MapEditorView();
        var screen = new ShellScreen();

        MapEditorLayout layout = DrawIdle(screen, map, sheet, view);

        // The screen is the console's screen, not a window's — the whole of ADR-029 in four
        // numbers, and the reason every constant below is a fixed console pixel.
        Assert.Equal(160, screen.Framebuffer.Width);
        Assert.Equal(90, screen.Framebuffer.Height);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(24, 11, 136, 64), layout.Canvas);
        Assert.Equal(MapEditorOverlay.None, layout.Overlay);

        VirtualConsole console = screen.Console;
        // The three rules that cut the screen into bands: under the top bar, above the message
        // line, above the status line.
        Assert.Equal((byte)1, console.Pget(0, 10));
        Assert.Equal((byte)1, console.Pget(159, 10));
        Assert.Equal((byte)1, console.Pget(0, 78));
        Assert.Equal((byte)1, console.Pget(0, 84));
        // The gutter between the tool block and the viewport is ink; the viewport's first cell
        // wears the cursor frame, eight pixels wide and eight tall.
        Assert.Equal((byte)0, console.Pget(23, 11));
        Assert.Equal((byte)3, console.Pget(24, 11));
        Assert.Equal((byte)3, console.Pget(31, 11));
        Assert.Equal((byte)3, console.Pget(24, 18));
        // The grid falls on cell boundaries: the first interior line is at x = 24 + 8, the first
        // interior row at y = 11 + 8. A viewport drawn at any other pitch misses both.
        Assert.Equal((byte)1, console.Pget(32, 11));
        Assert.Equal((byte)1, console.Pget(24, 19));
        // The position bar: the thumb rests at the track's left end, and the track's interior
        // beyond it is ink — "there is a great deal more map than viewport".
        Assert.Equal((byte)2, console.Pget(24, 76));
        Assert.Equal((byte)0, console.Pget(60, 76));
        Assert.Equal((byte)1, console.Pget(159, 75));
        // The tilemap tab is the active one: its plate is the library's blue, showing through
        // the gaps of the grid glyph.
        Assert.Equal((byte)3, console.Pget(133, 1));
        // The tool block: the pencil is in hand (blue plate) and the palette switch is not
        // latched (no plate, and the Tiles glyph's first column is blank).
        Assert.Equal((byte)4, console.Pget(5, 12));
        Assert.Equal((byte)0, console.Pget(1, 42));
        Assert.Equal((byte)2, console.Pget(11, 42));
        // The status line's cell readout, the standing notice above it, and the screen's name in
        // the top band's free strip.
        Assert.Equal((byte)2, console.Pget(2, 85));
        Assert.Equal((byte)8, console.Pget(2, 79));
        Assert.Equal((byte)1, console.Pget(11, 2));
        // Sixteen slots and no more: nothing on this screen reaches a master colour above 15.
        foreach (byte pixel in screen.Framebuffer.Pixels)
        {
            Assert.InRange(pixel, (byte)0, (byte)15);
        }

        Assert.Equal("946b249650ae1f1e", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The same screen with work on it: three cells of tile 1, whose art is a yellow diagonal on
    /// the sibling sprite session's sheet. Four things move at once and all four are in the hash
    /// — the tiles, the tile number in the status line, the chrome's report that there is unsaved
    /// work and something to undo, and the standing notice going away because the tile in hand is
    /// no longer the empty one.
    /// </summary>
    [Fact]
    public void PaintedCellsShowTheSiblingSessionsArtAtOneToOne()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = DrawnCart();
        var view = new MapEditorView();
        var screen = new ShellScreen();

        Assert.True(map.IsDirty);
        Assert.True(map.CanUndo);
        Assert.False(map.CanRedo);

        DrawIdle(screen, map, sheet, view);

        VirtualConsole console = screen.Console;
        // The diagonal's second pixel in each of the three painted cells: (0,0) at the viewport's
        // origin, (1,1) one cell down and right, (2,2) two. A map drawn at the wrong pitch fails
        // the second and third; a map that read the sheet at the wrong cell fails all three.
        Assert.Equal((byte)8, console.Pget(25, 12));
        Assert.Equal((byte)8, console.Pget(33, 20));
        Assert.Equal((byte)8, console.Pget(41, 28));
        // Unsaved work: the save button's face is the modified floppy in warn yellow.
        Assert.Equal((byte)8, console.Pget(1, 52));
        // The tile number at the right edge of the status line, and the notice gone with it.
        Assert.Equal((byte)3, console.Pget(145, 85));
        Assert.Equal((byte)0, console.Pget(2, 79));

        Assert.Equal("2bcfb8a6a5ab4a97", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The tile palette, slid over the map (TIC-80's <c>drawSheetButton</c> — held by Shift or
    /// latched by its button). This is the frame the wave's whole layout argument produced: 256
    /// tiles at 8x8 are 16 384 pixels and the console is 14 400, so the palette cannot stand
    /// beside the map and cannot even be shown whole — it is one page of 128 lying over the
    /// viewport, and which page is on show is derived from the tile in hand.
    ///
    /// <para>The probes name that argument's visible consequences: the page starts at column 28
    /// and not at the viewport's own edge, tile 0 wears a dim frame wherever it appears, the tile
    /// in hand wears a bright one, and the strip of viewport the page does not cover still shows
    /// the map underneath — which is what makes it read as an overlay rather than a screen.</para>
    /// </summary>
    [Fact]
    public void ThePaletteSlidesOverTheMapAndFramesTheTileInHand()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = DrawnCart();
        var view = new MapEditorView();
        view.ToggleTiles();
        var screen = new ShellScreen();

        MapEditorLayout layout = DrawIdle(screen, map, sheet, view);

        Assert.Equal(MapEditorOverlay.Tiles, layout.Overlay);
        Assert.Equal(0, layout.PaletteLane);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(28, 11, 128, 64), layout.Sheet);

        VirtualConsole console = screen.Console;
        // Tile 0's dim frame at the page's first cell, and the bright frame of the tile in hand
        // one cell right of it.
        Assert.Equal((byte)1, console.Pget(28, 11));
        Assert.Equal((byte)1, console.Pget(28, 18));
        Assert.Equal((byte)3, console.Pget(36, 11));
        Assert.Equal((byte)3, console.Pget(43, 11));
        // Sprite 1's art inside that frame — the same pixels the map is showing, at 1:1.
        Assert.Equal((byte)8, console.Pget(37, 12));
        // The palette switch reads as on.
        Assert.Equal((byte)4, console.Pget(1, 42));
        // And the four columns of viewport the page does not cover still carry the map's cursor:
        // an overlay, not a screen.
        Assert.Equal((byte)3, console.Pget(24, 11));

        Assert.Equal("1bd6e547533896ab", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The whole-map view (TIC-80's <c>world.c</c>, reached by Tab or its button): the entire
    /// 256x72 map at two cells to the pixel, with the viewport's outline on it. One pixel per
    /// cell would be 256 pixels wide on a 160-pixel console, which is the arithmetic that made
    /// this a mode and made its pixel coarse; both facts are probed here.
    /// </summary>
    [Fact]
    public void TheWholeMapViewFoldsTwoCellsIntoEachPixel()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = DrawnCart();
        var view = new MapEditorView();
        view.ToggleWorld();
        var screen = new ShellScreen();

        MapEditorLayout layout = DrawIdle(screen, map, sheet, view);

        Assert.Equal(MapEditorOverlay.World, layout.Overlay);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(28, 25, 128, 36), layout.Minimap);

        VirtualConsole console = screen.Console;
        // Cells (0,0) and (1,1) share one minimap pixel, so the three painted cells light two
        // pixels and not three: (0,0) — under the viewport outline — and (1,1). A minimap drawn
        // one-to-one would light three, and would run off the console besides.
        Assert.Equal((byte)2, console.Pget(29, 26));
        Assert.Equal((byte)0, console.Pget(30, 27));
        // The viewport outline: 17x8 cells become 8x4 pixels at the map's origin.
        Assert.Equal((byte)3, console.Pget(28, 25));
        Assert.Equal((byte)3, console.Pget(35, 25));
        Assert.Equal((byte)3, console.Pget(28, 28));
        // The map viewport itself is not drawn at all while this is up — the mode owns the band.
        Assert.Equal((byte)0, console.Pget(24, 11));
        Assert.Equal((byte)0, console.Pget(32, 19));
        // The whole-map switch reads as on.
        Assert.Equal((byte)3, console.Pget(11, 42));

        Assert.Equal("adc60f3b2d93dced", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Esc on a dirty map: the question. The message line stops carrying the standing notice and
    /// carries the prompt instead — the heading at the left margin in warn yellow, the three
    /// verbs right-aligned to the screen's edge, each on the very rectangle
    /// <see cref="MapEditorLayout.PromptVerbRect"/> makes clickable, and all of it measured by
    /// <see cref="ConsoleChrome"/> so the map screen and the sprite screen cannot disagree about
    /// where "ESC STAY" is.
    /// </summary>
    [Fact]
    public void TheExitPromptTakesTheMessageLineAndItsVerbsAreWhereTheHitTestSaysTheyAre()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = DrawnCart();
        var view = new MapEditorView();
        Assert.False(view.RequestClose(map));        // dirty: the prompt goes up instead of closing
        Assert.True(view.ExitPromptShown);
        var screen = new ShellScreen();

        MapEditorLayout layout = DrawIdle(screen, map, sheet, view);

        VirtualConsole console = screen.Console;
        // "UNSAVED." at the margin in warn yellow: 'U' fills its top-left pixel.
        Assert.Equal((byte)8, console.Pget(1, 79));
        // The first verb is drawn one pixel inside the rectangle a click is tested against, so
        // the label and its hit target cannot drift apart.
        Microsoft.Xna.Framework.Rectangle save = layout.PromptVerbRect(EditorPromptVerb.SaveAndExit);
        Assert.Equal((byte)3, console.Pget(save.X + 1, save.Y));
        Assert.True(layout.TryPromptVerb(save.X + save.Width / 2, save.Y + 2, out EditorPromptVerb hit));
        Assert.Equal(EditorPromptVerb.SaveAndExit, hit);
        // The map is untouched by the question: the work the author is deciding about is still on
        // screen, which is the whole reason the prompt lives on one reserved line.
        Assert.Equal((byte)8, console.Pget(25, 12));

        Assert.Equal("b01e09aa69735bbd", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Two consoles, two framebuffers. The shell's screen and a cartridge's are separate
    /// instances by construction (<see cref="ShellScreen"/>), and this says so out loud: drawing
    /// the whole editor leaves a console built the same way untouched. It is the property that
    /// keeps anything the shell draws out of the buffer the CI hashes.
    /// </summary>
    [Fact]
    public void DrawingTheEditorTouchesNoOtherConsole()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = FreshCart();
        var view = new MapEditorView();
        var shell = new ShellScreen();
        var other = new ShellScreen();
        string before = FrameHash.Of(other.Framebuffer);

        DrawIdle(shell, map, sheet, view);

        Assert.Equal(before, FrameHash.Of(other.Framebuffer));
        Assert.NotEqual(before, FrameHash.Of(shell.Framebuffer));
        Assert.NotSame(shell.Framebuffer, other.Framebuffer);
    }

    /// <summary>
    /// Redrawing the same state gives the same bytes: the screen depends on the two sessions and
    /// the view and on nothing else — no window size, no clock, no leftover console state. That
    /// is what makes a pinned hash meaningful rather than lucky, and it is why
    /// <see cref="ShellScreen.Begin"/> resets camera, clip, palette and transparency before every
    /// draw.
    /// </summary>
    [Fact]
    public void TheScreenIsAPureFunctionOfTheSessionsAndTheViewState()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = DrawnCart();
        var view = new MapEditorView();
        var screen = new ShellScreen();

        DrawIdle(screen, map, sheet, view);
        string first = FrameHash.Of(screen.Framebuffer);

        // A different picture in between, so a stale-state bug has something to leak: the
        // whole-map view, then the palette, then a hovered button with its tooltip up.
        view.ToggleWorld();
        DrawIdle(screen, map, sheet, view);
        view.ToggleWorld();
        view.ToggleTiles();
        MapEditorRenderer.Draw(
            screen, map, sheet, view, HoverTarget.OfButton(EditorButton.ToolFill), true);
        view.ToggleTiles();
        DrawIdle(screen, map, sheet, view);

        Assert.Equal(first, FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The tooltip is TIC-80's, not a popup: hovering a control prints its label into the top
    /// band's free strip instead of covering the map with a box, and the label is cut to what the
    /// strip holds. Same mechanism the sprite screen uses, same single owner of the cut
    /// (<see cref="ConsoleChrome.FitTooltip"/>), so the two console screens cannot grow two
    /// tooltip styles.
    /// </summary>
    [Fact]
    public void AHoveredControlPrintsItsLabelIntoTheTopBandAndNowhereElse()
    {
        (MapEditorSession map, SpriteEditorSession sheet) = DrawnCart();
        var view = new MapEditorView();
        var screen = new ShellScreen();

        MapEditorLayout layout = MapEditorRenderer.Draw(
            screen, map, sheet, view, HoverTarget.OfButton(EditorButton.ToolPencil), true);

        Assert.Equal(25, layout.Chrome.TooltipChars);
        Assert.Equal(
            EditorIcons.MapTooltip(EditorButton.ToolPencil)[..25],
            layout.Chrome.FitTooltip(EditorIcons.MapTooltip(EditorButton.ToolPencil)));
        bool inkInField = false;
        for (int x = layout.Chrome.TooltipField.X; x < layout.Chrome.TooltipField.Right; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                inkInField |= screen.Console.Pget(x, y) != 0;
            }
        }
        Assert.True(inkInField);
        // ...and the map is exactly what it was with nothing hovered: no box over the tiles.
        var quiet = new ShellScreen();
        DrawIdle(quiet, map, sheet, new MapEditorView());
        for (int y = layout.Canvas.Y; y < layout.Canvas.Bottom; y++)
        {
            for (int x = layout.Canvas.X; x < layout.Canvas.Right; x++)
            {
                Assert.Equal(quiet.Console.Pget(x, y), screen.Console.Pget(x, y));
            }
        }
    }
}

using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>A grid on the sprite canvas, with a switch</b> — REFERENCES-EDITORS §8 item 11, PICO-8's
/// <c>CTRL-G</c> ("toggle black grid lines when zoomed in"). The map screen has had one since
/// wave R3; the canvas had none at all.
///
/// <para><b>The two claims this file exists for.</b> (1) The switch is a fact of the VIEW, on the
/// key the map already answers — one gesture for two panels. (2) The lines go where they cannot
/// eat the picture, which on this screen is a statement about the zoom and is therefore
/// testable: at zoom 8 and 4 they lie on art-pixel boundaries, at zoom 2 the pixel lines are
/// refused and only the sprite boundaries remain, and at every zoom the canvas's own outer
/// column and row stay art.</para>
///
/// <para>The renderer is driven headless — a <see cref="ShellScreen"/> is 160x90 of indexed
/// bytes and needs no graphics device — and the key is driven through the production router with
/// the production <see cref="ShellCommandReader"/>.</para>
/// </summary>
public class SpriteCanvasGridTests : IDisposable
{
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int Off = -1000;

    /// <summary>
    /// The probe row, chosen so that it is <b>not</b> a horizontal grid line at any zoom: 43 is
    /// odd, so it is a multiple of neither 8, 4 nor 2. Every assertion below is about the
    /// VERTICAL lines, and a row that happened to carry a horizontal one would answer Dim
    /// everywhere and prove nothing.
    /// </summary>
    private const int ProbeRow = 43;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private readonly string _root;

    public SpriteCanvasGridTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-canvasgrid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — every sprite is colour 0, so any non-zero pixel on the canvas is something this renderer drew on purpose.</summary>
    private SpriteEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"grid\",\"author\":\"\",\"profile\":8}");
        return new SpriteEditorSession(folder);
    }

    private static SpriteEditorLayout Draw(
        ShellScreen screen, SpriteEditorSession editor, SpriteEditorView? view) =>
        SpriteEditorRenderer.Draw(screen, editor, null, false, null, new SheetScroll(), 0.0, view);

    private static SpriteEditorView GridOn()
    {
        var view = new SpriteEditorView();
        view.ToggleGrid();
        return view;
    }

    // ==================================================================================
    // 1. The switch.
    // ==================================================================================

    /// <summary>
    /// The grid starts <b>off</b> on this screen, where the map's starts on — each reference is
    /// right about its own panel (TIC-80 ships <c>.canvas = {.grid = true}</c> for the map,
    /// PICO-8 ships its canvas grid off behind CTRL-G), and the state is the view's, not the
    /// session's: flipping it dirties nothing.
    ///
    /// <para>Break recipe: give <see cref="SpriteEditorView.GridShown"/> an initializer of
    /// <c>true</c> and the first assertion goes red — together with every golden master of this
    /// screen, which is the point of shipping it off.</para>
    /// </summary>
    [Fact]
    public void TheCanvasGridStartsOffAndIsNotADocumentFact()
    {
        SpriteEditorSession editor = FreshCart();
        var view = new SpriteEditorView();

        Assert.False(view.GridShown);

        view.ToggleGrid();
        Assert.True(view.GridShown);
        Assert.False(editor.IsDirty);       // a way of looking is not an edit

        view.ToggleGrid();
        Assert.False(view.GridShown);
    }

    /// <summary>
    /// <b>One gesture, two panels.</b> The backtick — TIC-80's own key for its own
    /// <c>drawGridButton</c>, and the key this shell's map screen has answered since wave R3 —
    /// now also flips the canvas grid, and each panel keeps its own answer. PICO-8's own key for
    /// this switch (<c>CTRL-G</c>) could not be taken: <see cref="ShellCommands.CodeFindNext"/>
    /// is Ctrl+G on the code screen, and one chord may not mean two things.
    ///
    /// <para>There is no button beside the key, and that is a named loss rather than an
    /// oversight: the sprite screen's tool column is two buttons wide by six rows and all twelve
    /// slots are occupied (<c>SpriteEditorLayout</c>'s <c>_toolSlots</c>), so the switch is
    /// keyboard-only until a slot frees up.</para>
    ///
    /// <para>Break recipe: delete the <c>EditorGridToggle</c> block from
    /// <see cref="SpriteEditorInput"/> and the sprite half goes red while the map half — tested
    /// in <c>MapEditorToolsTests</c> — stays green, which is exactly the one-screen-only defect
    /// this assertion is shaped to catch.</para>
    /// </summary>
    [Fact]
    public void TheBacktickFlipsTheGridOnBothPanelsAndEachKeepsItsOwn()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"grid\",\"author\":\"\",\"profile\":8}");
        var modes = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        modes.Menu.SkipIntro();
        modes.OpenLibrary();
        modes.OpenEditor();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();

        void Frame(Keys[] down)
        {
            ShellCommands commands = keys.Read(new KeyboardState(down));
            EditorMouse mouse = pointer.Read(new MouseState(
                Off, Off, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
            var shell = new EditorShell(
                modes, new ToolbarFlyout(), new IconHoverTracker(), new SheetScroll(),
                ConsoleWidth, ConsoleHeight);
            if (modes.Mode == ShellMode.Editor)
            {
                SpriteEditorInput.Update(shell, commands, mouse, FrameSeconds);
            }
            else
            {
                MapEditorInput.Update(shell, commands, mouse, FrameSeconds);
            }
        }

        void Tap(Keys key)
        {
            Frame(new[] { key });
            Frame(NoKeys);
        }

        Assert.False(modes.SpriteView.GridShown);
        Tap(Keys.OemTilde);
        Assert.True(modes.SpriteView.GridShown);

        modes.SwitchEditorTab(ShellMode.MapEditor);
        Assert.True(modes.MapView!.GridShown);      // the map's own default, untouched by the press above
        Tap(Keys.OemTilde);
        Assert.False(modes.MapView.GridShown);
        Assert.True(modes.SpriteView.GridShown);    // two panels, two answers, one key
    }

    // ==================================================================================
    // 2. Where the lines go.
    // ==================================================================================

    /// <summary>
    /// At zoom 8 — one 8x8 sprite in a 64x64 box, the size an author works at — a line lands on
    /// every art-pixel boundary <em>inside</em> the canvas, in <see cref="ConsoleChromeRenderer.Dim"/>.
    /// The two edge probes are the "it must not eat the picture" claim made concrete: the
    /// canvas's own first and last columns carry art, never grid, because the ring around the box
    /// is already <c>DrawPanelFrames</c>' job and a line there would be one pixel inside the
    /// drawing.
    ///
    /// <para>Break recipe: start the loop in <c>DrawCanvasGrid</c> at 0 instead of
    /// <c>step</c> — the left-edge probe goes red at once. Let it run to
    /// <c>RegionPixels</c> inclusive and the right-edge probe follows.</para>
    /// </summary>
    [Fact]
    public void AtZoomEightTheLinesLieOnEveryInteriorPixelBoundary()
    {
        SpriteEditorSession editor = FreshCart();
        var screen = new ShellScreen();

        SpriteEditorLayout layout = Draw(screen, editor, GridOn());
        int y = layout.Canvas.Y + ProbeRow;

        Assert.Equal(8, layout.CanvasScale);
        for (int i = 1; i < 8; i++)
        {
            Assert.Equal(
                ConsoleChromeRenderer.Dim, screen.Console.Pget(layout.Canvas.X + i * 8, y));
        }
        // ...and the pixel next to a line is still the author's, so a line costs one console
        // pixel of an eight-wide square and not the square.
        Assert.Equal((byte)0, screen.Console.Pget(layout.Canvas.X + 8 + 1, y));
        Assert.Equal((byte)0, screen.Console.Pget(layout.Canvas.X, y));
        Assert.Equal((byte)0, screen.Console.Pget(layout.Canvas.Right - 1, y));
    }

    /// <summary>
    /// <b>The negative control, and the whole of the design decision in one assertion.</b> At
    /// zoom 2 (a 32-px region in the same 64x64 box) a one-console-pixel line would be HALF of
    /// every art pixel it touched, so the pixel grid is refused there and only the sprite
    /// boundaries — one line every eight art pixels — are drawn. At zoom 4 the pixel grid is
    /// back, because a quarter of a pixel is scaffolding and half of it is the picture.
    ///
    /// <para>Break recipe: delete the <c>CanvasScale &gt;= PixelGridMinScale</c> test in
    /// <c>DrawCanvasGrid</c> (make <c>step</c> always 1) and the zoom-2 refusal goes red;
    /// invert it and the zoom-4 half goes red instead. Lower <c>PixelGridMinScale</c> to 2 and
    /// the refusal goes red on its own, which is the number's only reason to exist.</para>
    /// </summary>
    [Fact]
    public void AtZoomTwoThePixelLinesAreRefusedAndOnlyTheSpriteLinesRemain()
    {
        SpriteEditorSession coarse = FreshCart();
        coarse.SelectRegionSize(4);                 // 32 px of sheet in the 64 px box → zoom 2
        var screen = new ShellScreen();

        SpriteEditorLayout layout = Draw(screen, coarse, GridOn());
        int y = layout.Canvas.Y + ProbeRow;

        Assert.Equal(2, layout.CanvasScale);
        // An art-pixel boundary at this zoom: two console pixels apart, and refused.
        Assert.Equal((byte)0, screen.Console.Pget(layout.Canvas.X + 2, y));
        Assert.Equal((byte)0, screen.Console.Pget(layout.Canvas.X + 6, y));
        // A sprite boundary: eight art pixels = sixteen console pixels, and drawn — so the switch
        // still visibly does something at the coarsest zoom.
        Assert.Equal(
            ConsoleChromeRenderer.Dim, screen.Console.Pget(layout.Canvas.X + 16, y));
        Assert.Equal(
            ConsoleChromeRenderer.Dim, screen.Console.Pget(layout.Canvas.X + 32, y));

        SpriteEditorSession middle = FreshCart();
        middle.SelectRegionSize(2);                 // 16 px in the 64 px box → zoom 4
        SpriteEditorLayout fine = Draw(screen, middle, GridOn());

        Assert.Equal(4, fine.CanvasScale);
        Assert.Equal(
            ConsoleChromeRenderer.Dim, screen.Console.Pget(fine.Canvas.X + 4, fine.Canvas.Y + ProbeRow));
    }

    /// <summary>
    /// The second negative control: with the switch off the screen is <b>byte-identical</b> to
    /// the screen drawn with no view at all. That is what makes every golden master of this
    /// screen still true after the feature landed, and it is what would fail first if the grid
    /// ever leaked into a frame nobody asked for.
    ///
    /// <para>Break recipe: drop the <c>GridShown</c> test at the top of <c>DrawCanvasGrid</c>
    /// and the hashes part company immediately.</para>
    /// </summary>
    [Fact]
    public void WithTheSwitchOffTheFrameIsTheOneThatWasDrawnBeforeTheFeature()
    {
        SpriteEditorSession editor = FreshCart();
        var screen = new ShellScreen();

        Draw(screen, editor, null);
        string withoutView = FrameHash.Of(screen.Framebuffer);

        Draw(screen, editor, new SpriteEditorView());
        Assert.Equal(withoutView, FrameHash.Of(screen.Framebuffer));

        Draw(screen, editor, GridOn());
        Assert.NotEqual(withoutView, FrameHash.Of(screen.Framebuffer));
    }
}

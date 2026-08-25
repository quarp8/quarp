using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The <b>dispatch</b> half of the three gaps REFERENCES-EDITORS §8 named against the sprite
/// editor: which physical button and which key reach which session verb. The policy half — what
/// the verbs then do — is <see cref="SpriteEditorBrushAndInkTests"/>'s, one file over; splitting
/// them is deliberate, because a router bug and a policy bug are different repairs and a test
/// that could be either names neither.
///
/// <para>These are whole frames through the production <see cref="SpriteEditorInput"/>, in the
/// shape <see cref="EditorInputRouterTests"/> established and for the reason its type comment
/// gives: before the router moved out of the window class, every one of these facts could only
/// be tested by a mirror, and a mirror stays green when the thing it mirrors is deleted. The one
/// thing this harness adds to that one is the <b>middle</b> button, which no test needed until
/// the eyedropper moved onto it (TIC-80's <c>drawCanvasVBank1</c>).</para>
/// </summary>
public class SpriteEditorSecondButtonRouterTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorSecondButtonRouterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-2button-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The console's own screen — the surface this editor is laid out on since wave R2.</summary>
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside any rectangle the layout places — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    /// <summary>
    /// <c>QuarpGame</c> minus the graphics device: the four shell objects it owns, the two
    /// production readers it polls, and the console's size as two constants. Only the sprite
    /// screen is driven here, so there is no mode switch to mirror.
    /// </summary>
    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        internal SpriteEditorSession Editor => Modes.Editor!;

        internal EditorShell Context =>
            new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight);

        internal SpriteEditorLayout Layout =>
            SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, Editor.RegionCells);

        /// <summary>One whole frame through the production router — keys and all three buttons.</summary>
        internal void Frame(
            Keys[] down, int x, int y, ButtonState left, ButtonState middle, ButtonState right)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                x, y, 0, left, middle, right, ButtonState.Released, ButtonState.Released));
            SpriteEditorInput.Update(Context, commands, mouse, FrameSeconds);
        }

        internal void Idle() =>
            Frame(NoKeys, Off, Off, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
        internal void Tap(params Keys[] down)
        {
            Frame(down, Off, Off, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Idle();
        }

        internal void Click(int x, int y) => Click(NoKeys, x, y);

        internal void Click(Keys[] held, int x, int y)
        {
            Frame(held, x, y, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
            Frame(held, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }

        internal void RightClick(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Pressed);
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }

        internal void MiddleClick(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Pressed, ButtonState.Released);
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }
    }

    /// <summary>A mode machine standing in the sprite editor over a one-cart library of its own.</summary>
    private Harness OpenSpriteEditor()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"twobutton\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();           // the real road since ADR-028: menu → library → editor
        machine.OpenLibrary();
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        return new Harness(machine);
    }

    private static (int X, int Y) Centre(Rectangle rect) =>
        (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>Region-local pixel → the console point that lands on its middle.</summary>
    private static (int X, int Y) CanvasPoint(in SpriteEditorLayout layout, int localX, int localY) =>
        (layout.Canvas.X + (localX * layout.CanvasScale) + (layout.CanvasScale / 2),
         layout.Canvas.Y + (localY * layout.CanvasScale) + (layout.CanvasScale / 2));

    private static byte RegionPixelAt(SpriteEditorSession editor, int localX, int localY) =>
        editor.Pixels[
            (editor.RegionCellY * VirtualConsole.SpriteSize + localY) * CartData.GfxWidth
            + editor.RegionCellX * VirtualConsole.SpriteSize + localX];

    // ==================================================================================
    // §8 item 7 — the two buttons.
    // ==================================================================================

    /// <summary>
    /// The two swatch doors: a left click loads the first ink, a right click the second, and
    /// neither disturbs the other (TIC-80's <c>drawPalette</c>). This is the whole of what a
    /// two-ink palette means at the pointer.
    ///
    /// <para>Break recipe: drop the swatch branch from <see cref="SpriteEditorInput"/>'s
    /// right-button block and the right click falls through to nothing — the second ink stays 0
    /// and its assertion goes red while the left one stays green, naming exactly the missing
    /// branch. Pass <c>SpriteEditorInk.Primary</c> there instead and the FIRST ink's assertion
    /// goes red, which is the other way this can be wired wrong.</para>
    /// </summary>
    [Fact]
    public void TheTwoSwatchDoorsLoadTheTwoInksSeparately()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;

        (int leftX, int leftY) = Centre(harness.Layout.SwatchRect(7));
        harness.Click(leftX, leftY);
        (int rightX, int rightY) = Centre(harness.Layout.SwatchRect(3));
        harness.RightClick(rightX, rightY);

        Assert.Equal(7, editor.CurrentColor);
        Assert.Equal(3, editor.SecondaryColor);
    }

    /// <summary>
    /// The right button on the canvas draws — with the second ink — and the left with the
    /// first, which is REFERENCES-EDITORS §8 item 7 in one gesture each. The right button used
    /// to be the eyedropper here; that it now paints is the behaviour change this test pins.
    ///
    /// <para>Break recipe: delete the canvas branch of the right-button block in
    /// <see cref="SpriteEditorInput"/> — the right-drawn pixel stays 0 and its assertion goes
    /// red. Delete the <c>RightReleased</c> line instead and nothing here goes red, which is
    /// worth knowing: what that line guards is the undo step, and
    /// <see cref="ASecondInkStrokeCommitsAsOneUndoStep"/> below is the test that catches it.</para>
    /// </summary>
    [Fact]
    public void TheRightButtonDrawsWithTheSecondInkAndTheLeftWithTheFirst()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        harness.Click(Centre(harness.Layout.SwatchRect(7)).X, Centre(harness.Layout.SwatchRect(7)).Y);
        harness.RightClick(
            Centre(harness.Layout.SwatchRect(3)).X, Centre(harness.Layout.SwatchRect(3)).Y);

        SpriteEditorLayout layout = harness.Layout;
        (int rx, int ry) = CanvasPoint(layout, 2, 2);
        harness.RightClick(rx, ry);
        (int lx, int ly) = CanvasPoint(layout, 4, 4);
        harness.Click(lx, ly);

        Assert.Equal(3, RegionPixelAt(editor, 2, 2));
        Assert.Equal(7, RegionPixelAt(editor, 4, 4));
    }

    /// <summary>
    /// A right-button stroke is one undo step, exactly like a left-button one: the release
    /// closes it. Without the router's <c>RightReleased</c> line the stroke would stay open
    /// past the gesture and the next one would fold into it — two marks, one step, and a
    /// Ctrl+Z that eats work the author did not ask it to.
    ///
    /// <para>Break recipe: delete the <c>if (mouse.RightReleased)</c> block from
    /// <see cref="SpriteEditorInput"/> and the second undo assertion goes red — after undoing
    /// once the first pixel is gone too, because both marks landed inside one never-closed
    /// stroke.</para>
    /// </summary>
    [Fact]
    public void ASecondInkStrokeCommitsAsOneUndoStep()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        harness.RightClick(
            Centre(harness.Layout.SwatchRect(5)).X, Centre(harness.Layout.SwatchRect(5)).Y);

        SpriteEditorLayout layout = harness.Layout;
        (int firstX, int firstY) = CanvasPoint(layout, 1, 1);
        harness.RightClick(firstX, firstY);
        (int secondX, int secondY) = CanvasPoint(layout, 6, 6);
        harness.RightClick(secondX, secondY);

        Assert.Equal(5, RegionPixelAt(editor, 1, 1));
        Assert.Equal(5, RegionPixelAt(editor, 6, 6));

        editor.Undo();
        Assert.Equal(0, RegionPixelAt(editor, 6, 6));
        Assert.Equal(5, RegionPixelAt(editor, 1, 1));   // the earlier gesture is its own step
    }

    /// <summary>
    /// The middle button is the eyedropper, and it fills the <b>first</b> ink — TIC-80's
    /// <c>drawCanvasVBank1</c> reads <c>tic_mouse_middle</c> and writes <c>color</c>. It is the
    /// button that took over the job the right one used to do, so if it is not wired the
    /// eyedropper has no pointer path at all.
    ///
    /// <para>Break recipe: delete the <c>MiddlePressed</c> branch from
    /// <see cref="SpriteEditorInput"/> and the first ink stays 12 — its assertion goes red and
    /// names the branch. Change its argument to <c>SpriteEditorInk.Secondary</c> and BOTH
    /// assertions go red at once, which is the other way round it can be wrong.</para>
    /// </summary>
    [Fact]
    public void TheMiddleButtonIsTheEyedropperIntoTheFirstInk()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        SpriteEditorLayout layout = harness.Layout;

        harness.Click(Centre(layout.SwatchRect(5)).X, Centre(layout.SwatchRect(5)).Y);
        (int px, int py) = CanvasPoint(layout, 1, 1);
        harness.Click(px, py);                                  // paint colour 5 there
        harness.Click(Centre(layout.SwatchRect(12)).X, Centre(layout.SwatchRect(12)).Y);
        Assert.Equal(12, editor.CurrentColor);

        harness.MiddleClick(px, py);

        Assert.Equal(5, editor.CurrentColor);
        Assert.Equal(0, editor.SecondaryColor);                 // untouched: this button is the first ink's
    }

    /// <summary>
    /// The keyboard's half of the two inks, which the parity law demands and LIKO-12 already
    /// wrote: Shift held is the second colour, for the pencil (Shift+Z) and for the eyedropper
    /// (Shift+X) alike. Without it the second ink would be a mouse-only feature — reachable by
    /// pointer, invisible to a keyboard hand — which is the exact gap this shell's parity
    /// instrument exists to forbid.
    ///
    /// <para>Break recipe: drop <c>EditorSecondaryInk</c> from
    /// <see cref="SpriteEditorInput"/>'s <c>keyInk</c> (hard-code <c>Primary</c>) and both
    /// halves go red with the FIRST ink's colour in them — the pencil paints 7 instead of 3 and
    /// the eyedropper overwrites the first ink instead of the second, which are the two ways
    /// this can be miswired and they fail separately.</para>
    /// </summary>
    [Fact]
    public void ShiftIsTheKeyboardsSecondInkForThePencilAndTheEyedropperAlike()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        SpriteEditorLayout layout = harness.Layout;

        harness.Click(Centre(layout.SwatchRect(7)).X, Centre(layout.SwatchRect(7)).Y);
        harness.RightClick(Centre(layout.SwatchRect(3)).X, Centre(layout.SwatchRect(3)).Y);

        harness.Tap(Keys.LeftShift, Keys.Z);            // the pencil, second ink, at the cursor
        Assert.Equal(3, RegionPixelAt(editor, editor.CursorX, editor.CursorY));
        Assert.Equal(7, editor.CurrentColor);           // the first ink never moved

        harness.Click(Centre(layout.SwatchRect(12)).X, Centre(layout.SwatchRect(12)).Y);
        harness.RightClick(Centre(layout.SwatchRect(9)).X, Centre(layout.SwatchRect(9)).Y);
        harness.Tap(Keys.LeftShift, Keys.X);            // the eyedropper, into the second ink

        Assert.Equal(3, editor.SecondaryColor);         // what the pixel under the cursor holds
        Assert.Equal(12, editor.CurrentColor);          // and the first ink is untouched by it
    }

    // ==================================================================================
    // §8 item 6 — Ctrl over the bucket.
    // ==================================================================================

    /// <summary>
    /// Ctrl over the bucket is "replace this colour everywhere", TIC-80's
    /// <c>processFillCanvasMouse</c> branching to <c>replaceColor</c>. The test paints two
    /// islands of one colour, clicks one of them, and watches the OTHER change — the plain fill
    /// could never do that, which is what makes the assertion a real distinction and not a
    /// restatement.
    ///
    /// <para>Break recipe: drop the <c>replace</c> argument in
    /// <see cref="SpriteEditorInput"/>'s canvas press (pass <c>false</c>, or stop reading
    /// <c>commands.EditorShapeFill</c>) and the far island stays colour 6 — its assertion goes
    /// red while the clicked island's stays green, which is precisely the difference between the
    /// two halves of the tool.</para>
    /// </summary>
    [Fact]
    public void CtrlOverTheBucketReplacesTheColourAcrossTheWholeRegion()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        SpriteEditorLayout layout = harness.Layout;

        harness.Click(Centre(layout.SwatchRect(6)).X, Centre(layout.SwatchRect(6)).Y);
        (int islandAx, int islandAy) = CanvasPoint(layout, 0, 0);
        harness.Click(islandAx, islandAy);
        (int islandBx, int islandBy) = CanvasPoint(layout, 7, 7);
        harness.Click(islandBx, islandBy);
        harness.Click(Centre(layout.SwatchRect(9)).X, Centre(layout.SwatchRect(9)).Y);
        harness.Click(Centre(layout.ButtonRect(EditorButton.ToolFill)).X,
                      Centre(layout.ButtonRect(EditorButton.ToolFill)).Y);
        Assert.Equal(SpriteEditorTool.Fill, editor.Tool);

        harness.Click(new[] { Keys.LeftControl }, islandBx, islandBy);

        Assert.Equal(9, RegionPixelAt(editor, 7, 7));
        Assert.Equal(9, RegionPixelAt(editor, 0, 0));   // the island the click never touched
    }

    // ==================================================================================
    // §8 item 12 — the brush's two input paths.
    // ==================================================================================

    /// <summary>
    /// TIC-80's <c>-</c> and <c>=</c> reach the ladder through the router, and they wrap — the
    /// keyboard half of the brush control, without which the panel button would be a mouse-only
    /// feature and this screen would break its own parity law.
    ///
    /// <para>Break recipe: delete either <c>CycleBrushSize</c> call from
    /// <see cref="SpriteEditorInput"/>, or drop <c>EditorBrushSmaller</c>/<c>EditorBrushBigger</c>
    /// from <c>ShellCommandReader</c>'s key table — the brush stays at 1 and the first assertion
    /// names which key went missing.</para>
    /// </summary>
    [Fact]
    public void TheMinusAndEqualsKeysWalkTheBrushLadder()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        Assert.Equal(1, editor.BrushSize);

        harness.Tap(Keys.OemPlus);
        Assert.Equal(2, editor.BrushSize);
        harness.Tap(Keys.OemMinus);
        Assert.Equal(1, editor.BrushSize);
        harness.Tap(Keys.OemMinus);
        Assert.Equal(4, editor.BrushSize);      // the wrap, reached by the real key
    }

    /// <summary>
    /// The pointer half: the brush toggle's list opens like the sprite-size list (right-click,
    /// or the long press the flyout owns) and picking from it sets the brush. It rides the very
    /// machinery the size toggle rides — <see cref="EditorIcons.ClickOpensFlyout"/> and
    /// <see cref="EditorIcons.ChooseVariant"/> — which is why the button needed no new mechanism
    /// of its own.
    ///
    /// <para>Break recipe: remove <see cref="EditorButton.BrushToggle"/> from
    /// <c>ClickOpensFlyout</c> and the list never opens (the first assertion goes red); remove
    /// its arm from <c>ChooseVariant</c> and the list opens but the pick does nothing (the
    /// second goes red). The two failures are separate on purpose — they are separate
    /// bugs.</para>
    /// </summary>
    [Fact]
    public void TheBrushListOpensFromItsButtonAndSetsTheBrush()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        SpriteEditorLayout layout = harness.Layout;

        (int slotX, int slotY) = Centre(layout.ButtonRect(EditorButton.BrushToggle));
        harness.RightClick(slotX, slotY);
        Assert.Equal(EditorButton.BrushToggle, harness.Flyout.OpenSlot);

        // Variant 2 of TIC-80's four-step ladder is a three-pixel brush.
        (int variantX, int variantY) = Centre(layout.FlyoutVariantRect(EditorButton.BrushToggle, 2));
        harness.Click(variantX, variantY);

        Assert.Equal(3, editor.BrushSize);
        Assert.Null(harness.Flyout.OpenSlot);       // the pick closes the list, like every other
    }

    /// <summary>
    /// The brush reaches the canvas through the router, not only through the session: a click
    /// with a wide brush lays a square. Pinned here as well as in
    /// <see cref="SpriteEditorBrushAndInkTests"/> because the two can fail apart — a router that
    /// stopped calling <see cref="SpriteEditorSession.Paint"/> and started calling something
    /// narrower would leave every session test green.
    ///
    /// <para>Break recipe: make the router's canvas press write one pixel of its own instead of
    /// opening a stroke, and the eight neighbours of the click go back to 0.</para>
    /// </summary>
    [Fact]
    public void AClickWithAWideBrushLaysASquareThroughTheRouter()
    {
        Harness harness = OpenSpriteEditor();
        SpriteEditorSession editor = harness.Editor;
        SpriteEditorLayout layout = harness.Layout;
        harness.Click(Centre(layout.SwatchRect(8)).X, Centre(layout.SwatchRect(8)).Y);
        harness.Tap(Keys.OemPlus);
        harness.Tap(Keys.OemPlus);
        Assert.Equal(3, editor.BrushSize);

        (int x, int y) = CanvasPoint(layout, 4, 4);
        harness.Click(x, y);

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Assert.Equal(8, RegionPixelAt(editor, 4 + dx, 4 + dy));
            }
        }
        Assert.Equal(0, RegionPixelAt(editor, 2, 4));
    }
}

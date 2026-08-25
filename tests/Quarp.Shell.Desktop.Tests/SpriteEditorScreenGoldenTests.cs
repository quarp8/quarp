using System.Linq;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The golden master of the sprite editor screen</b> — what wave R2 was worth doing for, and
/// the same instrument <c>LibraryScreenGoldenTests</c> put on the library one wave earlier.
///
/// <para>Until this wave the editor was painted at the window's native resolution through a
/// <c>SpriteBatch</c>, and there was no artefact of it a test could look at: no buffer, no
/// pixels, only draw calls into a graphics device no headless runner has. Every layout
/// assertion in the suite was therefore about <em>rectangles</em> — where the layout said a
/// panel was — and none about pixels, so a renderer that drew the palette in the sheet's
/// rectangle would have passed all of them. Now the screen is drawn into a
/// <see cref="Framebuffer"/> by the same core calls a cartridge uses, so it can be hashed by
/// exactly the owner that hashes a cartridge's frame: <see cref="FrameHash"/>. Same digest,
/// same 16-hex text form, same discipline. There is no second hasher in this repository and
/// this file does not introduce one.</para>
///
/// <para><b>These constants are not determinism anchors.</b> The eight anchors and the twelve
/// demo hashes are facts about the <em>cartridge's</em> console and must never move without a
/// verdict. These are facts about a tool screen's layout, they belong to no cross-architecture
/// promise, and they are expected to move whenever the screen is deliberately redesigned. The
/// discipline that does carry over is PLAYBOOK §4's: never re-pin silently. If one of these
/// changes, the commit message says which pixel moved and why.</para>
///
/// <para><b>Where these three constants came from — read this before re-pinning one.</b> Wave
/// R2, like wave R1 before it, was carried out in an environment with no .NET SDK and no
/// package feed, so nothing in the repository could be built or run. The hashes below were
/// therefore <em>derived</em>, not observed: by transliterating <c>VirtualConsole</c>'s
/// <c>Cls</c>, <c>RectFill</c>, <c>Rect</c>, <c>Print</c> and <c>Plot</c> together with
/// <see cref="SystemFont"/>'s glyph table, <c>EditorIcons</c>' mask table and this screen's
/// draw order, and running <see cref="FrameHash"/>'s FNV-1a over the result. The same model was
/// first checked against all three of <c>LibraryScreenGoldenTests</c>' already-pinned hashes
/// and reproduced them exactly, which is the evidence that the rasterizer and the font are
/// modelled right; what remains unproven by that check is only this file's own transcription of
/// the draw order. So: <b>if one of these three fails on the first real build while the
/// <c>Pget</c> probes above it all pass, the overwhelmingly likely explanation is a slip in
/// that transcription and not a defect in the screen</b> — check the probes, look at the frame,
/// and re-pin with a note saying so. If a probe fails too, the screen genuinely changed and the
/// ordinary rule applies: say which pixel moved and why.</para>
///
/// <para><b>Why the probes are here at all.</b> A bare hash mismatch says "something moved" and
/// nothing else. The <c>Pget</c> assertions above each hash name the structural facts the
/// picture is supposed to have — the canvas holds the sprite's pixels at zoom 8, the current
/// colour wears a ring, the selected sheet cell wears a bright frame, the three rules are where
/// the arithmetic says — so a failure tells whoever reads it whether the screen is broken or
/// merely redrawn.</para>
///
/// <para><b>Re-pinned 2026-08-25, wave S1 (brush sizes and two inks).</b> All three hashes
/// moved, and both causes were named BEFORE the numbers were read, then confirmed by looking at
/// the running window: (1) the twelfth slot of the tool column was empty and now carries the
/// brush-size button (console x 10-19, y 61-70) — REFERENCES-EDITORS §8 item 12; (2) the palette
/// gained a second selected colour, drawn as a pixel INSIDE the swatch's body while the first
/// stays a ring around it — §8 item 7, TIC-80's <c>color</c>/<c>color2</c> — and on a fresh
/// session both inks are 0, so swatch 0 gains one pixel. Every <c>Pget</c> probe in this file
/// passed unchanged through the re-pin, which is what says the screen was redrawn and not
/// broken; the three numbers were then read off the real build on the owner's machine, not
/// derived from a model, and the screen was photographed and looked at before they were written
/// down. Was / became: <c>2afc6f21dc819db1</c> / <c>722b2140ce71474e</c>,
/// <c>62d9aec6f33cd512</c> / <c>9bbcedf3a923b791</c>, <c>2688306247f13a38</c> /
/// <c>e977417de90e0f07</c>.</para>
///
/// <para><b>Re-pinned again the same day, wave S2 (the panels got an edge).</b> Cause, again
/// named before the numbers were read and then confirmed on the running window: on a fresh cart
/// the canvas and the sheet were <em>invisible</em> — an empty sprite is colour 0, the chrome's
/// ground is colour 0, and neither panel had a border, so the author saw a void with a single
/// white cursor ring floating in it. That is the defect the owner reported and the reason this
/// file now also carries <c>SpriteEditorPanelEdgeTests</c>, which asks the question a hash
/// cannot: "is the edge of the canvas a different colour from the ground when the sprite is
/// empty?" TIC-80 answers it with a one-pixel <c>rectb</c> outside the box
/// (<c>sprite.c</c>, <c>drawCanvas</c>) and so do we; the sheet's empty cells wear a dim ring
/// each, which is our own addition and is argued where it is drawn. The palette and the flag
/// block moved one column right, because the middle column's twentieth pixel — idle until now —
/// paid for the canvas frame's right side. Every <c>Pget</c> probe here passed through this
/// change too, after the four palette probes were moved the same one column.
/// Was / became: <c>722b2140ce71474e</c> / <c>d7586acedcbd9269</c>,
/// <c>9bbcedf3a923b791</c> / <c>a440fd38a44e731a</c>, <c>e977417de90e0f07</c> /
/// <c>a110ff917226f080</c>.</para>
///
/// <para><b>Re-pinned 2026-08-25, wave X6 (the flag byte joined the status band).</b> Cause
/// named before the numbers were read: REFERENCES-EDITORS §8 item 8 asks for TIC-80's number
/// beside the eight flag circles (<c>sprite.c</c> prints the byte next to them), because eight
/// lit-or-dark rings tell you the state one bit at a time and a byte tells you all eight at
/// once. It went into the status band rather than the middle column: under the layer tabs there
/// are four content rows and a glyph needs five, so the only ways in were the rows the chrome
/// reserves for a slider or moving the tabs — neither worth a readout. The band already carried
/// two facts about this same sprite; this is the third, and the worst case is 22 of its 39
/// characters. It is NOT governed by the shell-wide hex/dec switch, and that is a property of
/// the value rather than a preference: a flag byte is eight independent bits, and only base
/// sixteen shows a human which lamps are lit — 165 is the right number and a useless one.
/// Every probe in this file passed the change; the band's left field still starts where it did
/// and the right field is still flush right. Was / became:
/// <c>d7586acedcbd9269</c> / <c>9b0e89f5fc5c7705</c>,
/// <c>a440fd38a44e731a</c> / <c>961f347605d04ad2</c>,
/// <c>a110ff917226f080</c> / <c>af32c85410a518c8</c>.</para>
/// </summary>
public class SpriteEditorScreenGoldenTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorScreenGoldenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-sprscreen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — no gfx.png, no layers, no flags.</summary>
    private SpriteEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"golden\",\"author\":\"\",\"profile\":8}");
        return new SpriteEditorSession(folder);
    }

    /// <summary>One frame with nothing hovered, no flyout open, the strip at rest and the clock at zero.</summary>
    private static SpriteEditorLayout DrawIdle(ShellScreen screen, SpriteEditorSession editor) =>
        SpriteEditorRenderer.Draw(screen, editor, null, false, null, new SheetScroll(), 0.0);

    /// <summary>
    /// The screen an author meets on a brand-new cart: an empty 8x8 sprite at zoom 8, the pencil
    /// in hand, colour 0 selected, layer 1 active, nothing saved and nothing to undo — and the
    /// standing notice that sprite 000 is the map's empty tile.
    /// </summary>
    [Fact]
    public void AFreshCartOpensOnAnEmptySpriteAtZoomEight()
    {
        SpriteEditorSession editor = FreshCart();
        var screen = new ShellScreen();

        SpriteEditorLayout layout = DrawIdle(screen, editor);

        // The screen is the console's screen, not a window's — the whole of ADR-029 in four
        // numbers, and the reason every constant below is a fixed console pixel.
        Assert.Equal(160, screen.Framebuffer.Width);
        Assert.Equal(90, screen.Framebuffer.Height);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(20, 11, 64, 64), layout.Canvas);
        Assert.Equal(8, layout.CanvasScale);

        VirtualConsole console = screen.Console;
        // The three rules that cut the screen into bands: under the top bar, above the message
        // line, above the status line. They span the whole width, which is what makes them read
        // as bands and not as decoration.
        Assert.Equal((byte)1, console.Pget(0, 10));
        Assert.Equal((byte)1, console.Pget(159, 10));
        Assert.Equal((byte)1, console.Pget(0, 78));
        Assert.Equal((byte)1, console.Pget(0, 84));
        // The canvas: an empty sprite is ink, and the keyboard cursor rings its first pixel.
        Assert.Equal((byte)0, console.Pget(21, 12));
        Assert.Equal((byte)3, console.Pget(20, 11));
        // The palette's fifth swatch is the console's blue, in its own body; colour 0 is the one
        // in hand, so its cell — and only its cell — wears a bright ring.
        // The three probes moved one column right with the palette itself: the middle column's
        // twentieth pixel, which stood idle to the RIGHT of these blocks, was spent on the
        // canvas frame's right side (x=84), so palette and flags are now flush right at 85..103.
        // Column 84 is therefore frame, not swatch, and asking it about a ring would be asking
        // the wrong pixel — see SpriteEditorLayout.CanvasFrame for where the pixel came from.
        Assert.Equal((byte)4, console.Pget(86, 17));
        Assert.Equal((byte)3, console.Pget(85, 11));
        Assert.Equal((byte)8, console.Pget(85, 21));
        // The sheet window: sprite 0 is the selected region, so its cell wears a bright frame,
        // and the sprite itself is empty inside it.
        Assert.Equal((byte)3, console.Pget(104, 11));
        Assert.Equal((byte)0, console.Pget(105, 12));
        // The sprites tab is the active one: its plate is the library's blue, showing through
        // the gaps of the checkerboard glyph.
        Assert.Equal((byte)4, console.Pget(123, 1));
        // The scroll thumb rests at the left end of its track; the track's interior beyond it is
        // ink, which is what "there is more strip than window" looks like.
        Assert.Equal((byte)2, console.Pget(104, 76));
        Assert.Equal((byte)0, console.Pget(130, 76));
        // The status line's coordinates, and the standing notice above it. 'S' has a blank
        // top-left pixel in this font, so the notice's first ink is one column in.
        Assert.Equal((byte)2, console.Pget(2, 85));
        Assert.Equal((byte)8, console.Pget(2, 79));
        // Sixteen slots and no more: nothing on this screen reaches a master colour above 15,
        // because reaching one would mean remapping a slot the palette must show truthfully.
        foreach (byte pixel in screen.Framebuffer.Pixels)
        {
            Assert.InRange(pixel, (byte)0, (byte)15);
        }

        Assert.Equal("9b0e89f5fc5c7705", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The same screen with work on it: a yellow diagonal drawn across sprite 0 in one stroke.
    /// Four things move at once and all four are in the hash — the canvas, the sheet window's
    /// first cell (the same pixels at 1:1), the colour in hand, and the chrome's report that
    /// there is unsaved work and something to undo.
    /// </summary>
    [Fact]
    public void ADrawnSpriteShowsOnTheCanvasAndInTheSheetAtOnce()
    {
        SpriteEditorSession editor = FreshCart();
        editor.SelectColor(8);
        editor.BeginStroke();
        for (int i = 0; i < 8; i++)
        {
            editor.Paint(i, i);
        }
        editor.EndStroke();
        var screen = new ShellScreen();

        Assert.True(editor.IsDirty);
        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);

        DrawIdle(screen, editor);

        VirtualConsole console = screen.Console;
        // The canvas at zoom 8: region pixel (0,0) is an 8x8 block of colour 8, and so is (1,1)
        // one block down and right. A canvas drawn at the wrong scale fails the second one.
        Assert.Equal((byte)8, console.Pget(21, 12));
        Assert.Equal((byte)8, console.Pget(30, 21));
        Assert.Equal((byte)0, console.Pget(29, 12));
        // The same art in the sheet window at 1:1 — the diagonal's second pixel.
        Assert.Equal((byte)8, console.Pget(105, 12));
        // The ring followed the colour: off swatch 0, onto swatch 8. One column right of where
        // it used to be asked, for the reason written above the twin probes in the first test.
        Assert.Equal((byte)0, console.Pget(85, 11));
        Assert.Equal((byte)3, console.Pget(85, 21));
        // The notice is unchanged — sprite 000 is still the map's empty tile, drawn on or not.
        Assert.Equal((byte)8, console.Pget(2, 79));

        Assert.Equal("961f347605d04ad2", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Esc on a dirty session: the question. The message line stops carrying the standing notice
    /// and carries the prompt instead — the heading at the left margin in warn yellow, the three
    /// verbs right-aligned to the screen's edge, each on the very rectangle
    /// <see cref="SpriteEditorLayout.PromptVerbRect"/> makes clickable.
    ///
    /// <para>That trade is the console's one named loss and it is asserted here on purpose: at
    /// 160x90 the message band is one line, so the notice yields while the question stands and
    /// comes back when it is lowered. The host frame stacked all three lines; there is no room
    /// to, and pretending otherwise would have cost the canvas six of its sixty-four rows.</para>
    /// </summary>
    [Fact]
    public void TheExitPromptTakesTheMessageLineAndItsVerbsAreWhereTheHitTestSaysTheyAre()
    {
        SpriteEditorSession editor = FreshCart();
        editor.SelectColor(8);
        editor.BeginStroke();
        for (int i = 0; i < 8; i++)
        {
            editor.Paint(i, i);
        }
        editor.EndStroke();
        Assert.False(editor.RequestClose());        // dirty: the prompt goes up instead of closing
        Assert.True(editor.ExitPromptShown);
        var screen = new ShellScreen();

        SpriteEditorLayout layout = DrawIdle(screen, editor);

        VirtualConsole console = screen.Console;
        // "UNSAVED." at the margin in warn yellow: 'U' fills its top-left pixel.
        Assert.Equal((byte)8, console.Pget(1, 79));
        // The notice's first ink is gone from the line the prompt has taken.
        Assert.Equal((byte)0, console.Pget(2, 79));
        // The last verb is drawn one pixel inside the rectangle a click is tested against, so
        // the label and its hit target cannot drift apart.
        Microsoft.Xna.Framework.Rectangle stay = layout.PromptVerbRect(EditorPromptVerb.Stay);
        Assert.Equal((byte)3, console.Pget(stay.X + 1, stay.Y));
        Assert.True(layout.TryPromptVerb(stay.X + stay.Width / 2, stay.Y + 2, out EditorPromptVerb hit));
        Assert.Equal(EditorPromptVerb.Stay, hit);
        // The canvas is untouched by the question: the art the author is deciding about is still
        // on screen, which is the whole reason the prompt lives on one reserved line.
        Assert.Equal((byte)8, console.Pget(21, 12));

        Assert.Equal("af32c85410a518c8", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Two consoles, two framebuffers. The shell's screen and a cartridge's are separate
    /// instances by construction (<see cref="ShellScreen"/>), and this says so out loud: drawing
    /// the whole editor leaves a console built the same way untouched. It is the property that
    /// lets a tool screen be opened over a paused game without eating the frame that game left
    /// behind — and, more importantly, the property that keeps anything the shell draws out of
    /// the buffer the CI hashes.
    /// </summary>
    [Fact]
    public void DrawingTheEditorTouchesNoOtherConsole()
    {
        SpriteEditorSession editor = FreshCart();
        var shell = new ShellScreen();
        var other = new ShellScreen();
        string before = FrameHash.Of(other.Framebuffer);

        DrawIdle(shell, editor);

        Assert.Equal(before, FrameHash.Of(other.Framebuffer));
        Assert.NotEqual(before, FrameHash.Of(shell.Framebuffer));
        Assert.NotSame(shell.Framebuffer, other.Framebuffer);
    }

    /// <summary>
    /// Redrawing the same state gives the same bytes: the screen depends on the session, the
    /// hover, the flyout and the scroll and on nothing else — no window size, no leftover
    /// console state. That is what makes a pinned hash meaningful rather than lucky, and it is
    /// why <see cref="ShellScreen.Begin"/> resets camera, clip, palette and transparency before
    /// every draw.
    /// </summary>
    [Fact]
    public void TheScreenIsAPureFunctionOfTheSessionAndTheViewState()
    {
        SpriteEditorSession editor = FreshCart();
        var screen = new ShellScreen();

        DrawIdle(screen, editor);
        string first = FrameHash.Of(screen.Framebuffer);

        // A different picture in between, so a stale-state bug has something to leak: a scrolled
        // strip, an open flyout and a hovered button all at once.
        var scrolled = new SheetScroll();
        SpriteEditorLayout layout = SpriteEditorRenderer.LayoutFor(screen, editor);
        scrolled.ScrollBy(layout, 40);
        SpriteEditorRenderer.Draw(
            screen, editor, HoverTarget.OfButton(EditorButton.ToolFill), true,
            EditorButton.ToolShape, scrolled, 0.25);
        DrawIdle(screen, editor);

        Assert.Equal(first, FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The tooltip is TIC-80's, not a popup: hovering a control prints its label into the top
    /// band's free strip instead of covering the canvas with a box, and the label is cut to what
    /// the strip holds. This is a deliberate departure from what the host-resolution screen did,
    /// named here so that a future reader finds the decision and not a mystery.
    /// </summary>
    [Fact]
    public void AHoveredControlPrintsItsLabelIntoTheTopBandAndNowhereElse()
    {
        SpriteEditorSession editor = FreshCart();
        var screen = new ShellScreen();

        SpriteEditorLayout layout = SpriteEditorRenderer.Draw(
            screen, editor, HoverTarget.OfSlider(), true, null, new SheetScroll(), 0.0);

        // The slider's label is 53 characters and the field holds 25 — the cut is the field's,
        // not the label's, so no second owner of what a control is called ever appears.
        Assert.Equal(25, layout.Chrome.TooltipChars);
        Assert.Equal(
            EditorIcons.SliderTooltip[..25], layout.Chrome.FitTooltip(EditorIcons.SliderTooltip));
        // Ink appears in the top band's free strip...
        bool inkInField = false;
        for (int x = layout.Chrome.TooltipField.X; x < layout.Chrome.TooltipField.Right; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                inkInField |= screen.Console.Pget(x, y) != 0;
            }
        }
        Assert.True(inkInField);
        // ...and the canvas is exactly what it was with nothing hovered: no box over the art.
        var quiet = new ShellScreen();
        DrawIdle(quiet, editor);
        for (int y = layout.Canvas.Y; y < layout.Canvas.Bottom; y++)
        {
            for (int x = layout.Canvas.X; x < layout.Canvas.Right; x++)
            {
                Assert.Equal(quiet.Console.Pget(x, y), screen.Console.Pget(x, y));
            }
        }
    }

    /// <summary>
    /// <b>The fourteen hashes on this screen and its three siblings were re-pinned on
    /// 2026-08-25, and the cause is written here so nobody has to guess later.</b> The music
    /// editor's wave emptied <c>EditorIcons.IsStub</c> — the MUSIC tab stopped being a dead
    /// button, and with it the SOUND tab's neighbour in the strip stopped being painted dim.
    /// Every screen carries that strip, so every screen's frame changed by the same handful of
    /// pixels, and not one of them changed anywhere else: every <c>Pget</c> probe in these files
    /// passed through the re-pin untouched, and the music screen's own goldens — computed
    /// against the new stub list from the start — passed on their first run.
    ///
    /// <para>The rule this obeys is the project's oldest: a hash may be re-pinned, never
    /// silently. What follows is the pin of the cause itself, so that the next time a tab's ink
    /// changes it is a named test that says so and not fourteen anonymous hashes.</para>
    /// </summary>
    [Fact]
    public void NoTabInTheStripIsPaintedAsADeadButtonAnyMore()
    {
        Assert.Empty(Enum.GetValues<EditorButton>().Where(EditorIcons.IsStub));
    }
}

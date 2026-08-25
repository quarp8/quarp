using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The golden master of the code editor screen</b> — what wave R4 was worth doing for, and
/// the same instrument <c>LibraryScreenGoldenTests</c> (R1), <c>SpriteEditorScreenGoldenTests</c>
/// (R2) and <c>MapEditorScreenGoldenTests</c> (R3) put on the three screens that moved before
/// this one.
///
/// <para>Until this wave the code editor was painted at the window's native resolution through a
/// <c>SpriteBatch</c>, and there was no artefact of it a test could look at: no buffer, no
/// pixels, only draw calls into a graphics device no headless runner has. Every layout assertion
/// in <c>CodeEditorScreenTests</c> was therefore about <em>rectangles</em> — where the layout
/// said a box was — and none about pixels, so a renderer that drew the selection band a line
/// below the selection would have passed all of them. Now the screen is drawn into a
/// <see cref="Framebuffer"/> by the same core calls a cartridge uses, so it can be hashed by
/// exactly the owner that hashes a cartridge's frame: <see cref="FrameHash"/>. Same digest, same
/// 16-hex text form, same discipline. There is no second hasher in this repository and this file
/// does not introduce one.</para>
///
/// <para><b>These constants are not determinism anchors.</b> The eight anchors and the twelve
/// demo hashes are facts about the <em>cartridge's</em> console and must never move without a
/// verdict. These are facts about a tool screen's layout, they belong to no cross-architecture
/// promise, and they are expected to move whenever the screen is deliberately redesigned. The
/// discipline that does carry over is PLAYBOOK §4's: never re-pin silently. If one of these
/// changes, the commit message says which pixel moved and why.</para>
///
/// <para><b>Where these three constants came from — read this before re-pinning one.</b> Wave
/// R4, like R1-R3 before it, was carried out in an environment with no .NET SDK and no package
/// feed, so nothing in the repository could be built or run. The hashes below were therefore
/// <em>derived</em>, not observed: by transliterating <see cref="VirtualConsole"/>'s <c>Cls</c>,
/// <c>RectFill</c>, <c>Rect</c>, <c>Print</c> and <c>Plot</c> together with
/// <see cref="SystemFont"/>'s glyph table, <c>EditorIcons</c>' mask table and this screen's draw
/// order, and running <see cref="FrameHash"/>'s FNV-1a over the result. That model was first
/// checked against <b>all eleven hashes already pinned in this suite</b> — the three in
/// <c>LibraryScreenGoldenTests</c>, the three in <c>SpriteEditorScreenGoldenTests</c> and the
/// five in <c>MapEditorScreenGoldenTests</c> — and reproduced every one of them exactly, which is
/// the evidence that the rasterizer, the font, the icon masks and the shared console chrome are
/// modelled right; what remains unproven by that check is only this file's own transcription of
/// <em>this</em> screen's draw order. So: <b>if one of these three fails on the first real build
/// while the <c>Pget</c> probes above it all pass, the overwhelmingly likely explanation is a
/// slip in that transcription and not a defect in the screen</b> — check the probes, look at the
/// frame, and re-pin with a note saying so. If a probe fails too, the screen genuinely changed
/// and the ordinary rule applies: say which pixel moved and why.</para>
///
/// <para><b>Why the probes are here at all.</b> A bare hash mismatch says "something moved" and
/// nothing else. The <c>Pget</c> assertions above each hash name the structural facts the picture
/// is supposed to have — the page starts at column 12 and is eleven lines by thirty-six columns,
/// the caret is one pixel wide and a whole cell tall, the selection band stops at the field's
/// last pixel and a long line is cut at the same place, the scrollbar's thumb shrinks when the
/// file outgrows the page — so a failure tells whoever reads it whether the screen is broken or
/// merely redrawn.</para>
/// </summary>
public class CodeEditorScreenGoldenTests : IDisposable
{
    private readonly string _root;

    public CodeEditorScreenGoldenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-codescreen-golden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// A sample with the three shapes the page has to survive: a line exactly as wide as the
    /// field (line 2 is 36 characters), a line half again wider than it (line 8 is 51, and it is
    /// inside the eleven lines on show, so the horizontal cut is in the picture), and more lines
    /// than fit, so the scrollbar's thumb is a thumb and not a full track.
    /// </summary>
    private static readonly string[] SampleLines =
    {
        "using Quarp.Api;",
        "",
        "public sealed class Demo : Cartridge",
        "{",
        "    int _x;",
        "",
        "    public override void Tick()",
        "    {",
        "        _x = (_x + 1) % VirtualConsole.ScreenWidth;",
        "    }",
        "",
        "    public override void Draw()",
        "    {",
        "        Q.Cls(0);",
        "        Q.RectFill(_x, 40, 8, 8, 8);   // the box the demo pushes across the screen",
        "    }",
        "}",
    };

    private static readonly string Sample = string.Join('\n', SampleLines);

    /// <summary>A cart folder with nothing in it but its manifest — no <c>src</c>, no main.cs.</summary>
    private CodeEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"golden\",\"author\":\"\",\"profile\":8}");
        return new CodeEditorSession(folder);
    }

    /// <summary>The same cart with <see cref="Sample"/> already on disk, so the buffer opens clean.</summary>
    private CodeEditorSession WrittenCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"golden\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(
            Path.Combine(
                folder, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName),
            Sample);
        return new CodeEditorSession(folder);
    }

    /// <summary>One frame with nothing hovered, no tooltip due and the draw clock at zero (a lit caret).</summary>
    private static CodeEditorLayout DrawIdle(
        ShellScreen screen, CodeEditorSession session, CodeEditorView view) =>
        CodeEditorRenderer.Draw(screen, session, view, null, false, 0.0);

    /// <summary>The view, synced against the very layout the renderer will draw with.</summary>
    private static CodeEditorView SyncedView(CodeEditorSession session)
    {
        var view = new CodeEditorView();
        view.Sync(CodeEditorLayout.Compute(160, 90), session);
        return view;
    }

    /// <summary>
    /// The screen an author meets on a cart that has never had a line of code in it: an empty
    /// buffer, the caret on its first cell, nothing saved and nothing to undo — and the whole of
    /// wave R4's arithmetic visible in four numbers, because the page is a rectangle now and not
    /// a function of somebody's window.
    /// </summary>
    [Fact]
    public void AFreshCartOpensOnAnEmptyPageOfElevenLinesByThirtySixColumns()
    {
        CodeEditorSession session = FreshCart();
        CodeEditorView view = SyncedView(session);
        var screen = new ShellScreen();

        CodeEditorLayout layout = DrawIdle(screen, session, view);

        // The screen is the console's screen, not a window's — the whole of ADR-029 in four
        // numbers, and the reason every constant below is a fixed console pixel.
        Assert.Equal(160, screen.Framebuffer.Width);
        Assert.Equal(90, screen.Framebuffer.Height);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(12, 11, 144, 66), layout.Text);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(157, 11, 3, 66), layout.ScrollBar);
        // Eleven by thirty-six. CodeEditorLayout's type note derives both; this is the assertion
        // that stops the derivation from drifting away from the rectangle.
        Assert.Equal(11, layout.VisibleLines);
        Assert.Equal(36, layout.VisibleColumns);
        Assert.Equal(1, session.LineCount);

        VirtualConsole console = screen.Console;
        // The three rules that cut the screen into bands: under the top bar, above the message
        // line, above the status line.
        Assert.Equal((byte)1, console.Pget(0, 10));
        Assert.Equal((byte)1, console.Pget(159, 10));
        Assert.Equal((byte)1, console.Pget(0, 78));
        Assert.Equal((byte)1, console.Pget(0, 84));
        // The tool column is one button wide and hard against the left edge; the two pixels
        // between it and the text are the only air on this screen.
        Assert.Equal((byte)1, console.Pget(0, 11));
        Assert.Equal((byte)1, console.Pget(9, 11));
        Assert.Equal((byte)1, console.Pget(0, 20));
        Assert.Equal((byte)0, console.Pget(10, 11));
        Assert.Equal((byte)0, console.Pget(11, 11));
        // The caret rests on the first cell: one pixel wide, a whole 6-row cell tall.
        Assert.Equal((byte)3, console.Pget(12, 11));
        Assert.Equal((byte)3, console.Pget(12, 16));
        Assert.Equal((byte)0, console.Pget(13, 13));
        // The scrollbar's gap column is ink, and the thumb fills the whole track because the
        // whole (empty) file is on screen — "there is nothing below this" drawn as a full rail.
        Assert.Equal((byte)0, console.Pget(156, 11));
        Assert.Equal((byte)0, console.Pget(156, 40));
        Assert.Equal((byte)2, console.Pget(157, 11));
        Assert.Equal((byte)2, console.Pget(158, 40));
        Assert.Equal((byte)2, console.Pget(159, 76));
        Assert.Equal((byte)0, console.Pget(157, 77));
        // The status line: "LINE 1/1 COL 1" at the left margin, the byte budget right-aligned to
        // the screen's edge (eight characters, so its first digit lands on column 127).
        Assert.Equal("LINE 1/1 COL 1", CodeEditorRenderer.Coordinates(session));
        Assert.Equal("0/262144", CodeEditorRenderer.Budget(session));
        Assert.Equal((byte)2, console.Pget(1, 85));
        Assert.Equal((byte)3, console.Pget(127, 85));
        // Nothing to say on the message line, and the screen's own name in the top band's strip.
        Assert.Null(CodeEditorRenderer.StandingNotice(session, view));
        Assert.Equal((byte)0, console.Pget(1, 79));
        Assert.Equal((byte)1, console.Pget(12, 2));
        // The code tab is the active one: its plate is the library's blue.
        Assert.Equal((byte)4, console.Pget(113, 1));
        // Sixteen slots and no more: nothing on this screen reaches a master colour above 15.
        foreach (byte pixel in screen.Framebuffer.Pixels)
        {
            Assert.InRange(pixel, (byte)0, (byte)15);
        }

        Assert.Equal("e2f93f9e52505e38", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// A real source file with a selection across three lines and the caret at its far end. This
    /// is the frame the wave's whole layout argument produced, and the probes name the argument's
    /// visible consequences: the page holds eleven of the file's seventeen lines and thirty-six
    /// of line 8's fifty-one characters, the selection band stops at the field's last pixel
    /// rather than running under the scrollbar, a line whose break is inside the selection gets
    /// one extra cell of band, and the thumb has shrunk to the page's share of the file.
    /// </summary>
    [Fact]
    public void ASelectionAndACaretOnRealSourceAreDrawnWhereTheHitTestSaysTheyAre()
    {
        CodeEditorSession session = WrittenCart();
        Assert.Equal(17, session.LineCount);
        Assert.Equal(36, SampleLines[2].Length);        // exactly the field's width
        Assert.Equal(51, SampleLines[8].Length);        // half again wider, and on screen
        session.SetCursor(2, 7);
        session.SetCursor(4, 11, extend: true);
        CodeEditorView view = SyncedView(session);
        var screen = new ShellScreen();

        CodeEditorLayout layout = DrawIdle(screen, session, view);

        Assert.True(session.HasSelection);
        Assert.Equal(new CodePosition(2, 7), session.SelectionStart);
        Assert.Equal(new CodePosition(4, 11), session.SelectionEnd);
        Assert.Equal(0, view.FirstLine);
        Assert.Equal(0, view.FirstColumn);
        Assert.False(session.IsDirty);                  // a caret move is not an edit

        VirtualConsole console = screen.Console;
        // Line 2's band starts at column 7 and runs to the last pixel of the field — and stops
        // there. Column 36 does not exist on this screen, and neither does a band under it.
        Assert.Equal((byte)0, console.Pget(39, 23));
        Assert.Equal((byte)4, console.Pget(40, 23));
        Assert.Equal((byte)4, console.Pget(155, 23));
        Assert.Equal((byte)0, console.Pget(156, 23));
        // Line 3 is a lone "{": one cell of brace plus one of swallowed newline, and no more.
        Assert.Equal((byte)4, console.Pget(12, 29));
        Assert.Equal((byte)4, console.Pget(19, 29));
        Assert.Equal((byte)0, console.Pget(20, 29));
        // Line 4 carries the band up to the caret's column and not past it.
        Assert.Equal((byte)4, console.Pget(12, 35));
        Assert.Equal((byte)4, console.Pget(55, 35));
        // The caret at the selection's far end: one pixel wide, a whole cell tall, and it stops.
        Assert.Equal((byte)3, console.Pget(56, 35));
        Assert.Equal((byte)3, console.Pget(56, 40));
        Assert.Equal((byte)0, console.Pget(56, 41));
        // Line 8 is 51 characters and the page is 36: the thirty-sixth is drawn, the rest is not.
        Assert.Equal((byte)2, console.Pget(153, 60));
        Assert.Equal((byte)0, console.Pget(156, 60));
        // The thumb is the page's share of the file now (11 of 17), so the track shows below it.
        Assert.Equal(
            new Microsoft.Xna.Framework.Rectangle(157, 11, 3, 42),
            layout.ScrollThumbRect(view.FirstLine, session.LineCount));
        Assert.Equal((byte)2, console.Pget(158, 20));
        Assert.Equal((byte)0, console.Pget(158, 60));
        Assert.Equal((byte)1, console.Pget(157, 60));
        // The status line reads the caret and the budget, both in the numbers the author sees.
        Assert.Equal("LINE 5/17 COL 12", CodeEditorRenderer.Coordinates(session));
        Assert.Equal("314/262144", CodeEditorRenderer.Budget(session));
        // ...and the message line is empty, because nothing has anything to say yet.
        Assert.Equal((byte)0, console.Pget(1, 79));

        Assert.Equal("5554d7948b66f654", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The find line, up and with a term in it (TIC-80's <c>TEXT_FIND_MODE</c>, Ctrl+F or the
    /// button). The console has one message line and no room for a box, so the field IS that
    /// line while it lives — and its button lights, which is what tells the author that Esc has
    /// something to close.
    /// </summary>
    [Fact]
    public void TheFindLineTakesTheMessageLineAndLightsItsButton()
    {
        CodeEditorSession session = WrittenCart();
        CodeEditorView view = SyncedView(session);
        view.OpenFind();
        foreach (char c in "Rect")
        {
            view.TypeIntoField(c);
        }
        var screen = new ShellScreen();

        DrawIdle(screen, session, view);

        Assert.Equal("FIND: Rect", CodeEditorRenderer.StandingNotice(session, view));

        VirtualConsole console = screen.Console;
        // "FIND: Rect" at the left margin in warn yellow: 'F' fills its whole top row.
        Assert.Equal((byte)8, console.Pget(1, 79));
        Assert.Equal((byte)8, console.Pget(2, 79));
        // The find button is latched — blue plate, bright face; the go-to button beside it is not.
        Assert.Equal((byte)4, console.Pget(1, 12));
        Assert.Equal((byte)3, console.Pget(5, 12));
        Assert.Equal((byte)2, console.Pget(1, 22));
        // The buffer is untouched and its caret still blinks where it was: the field owns the
        // keystrokes, not the document.
        Assert.Equal(Sample, session.Text);
        Assert.Equal((byte)3, console.Pget(12, 11));
        Assert.Equal((byte)3, console.Pget(12, 16));

        Assert.Equal("e645bdb3084cf646", FrameHash.Of(screen.Framebuffer));
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
        CodeEditorSession session = WrittenCart();
        CodeEditorView view = SyncedView(session);
        var shell = new ShellScreen();
        var other = new ShellScreen();
        string before = FrameHash.Of(other.Framebuffer);

        DrawIdle(shell, session, view);

        Assert.Equal(before, FrameHash.Of(other.Framebuffer));
        Assert.NotEqual(before, FrameHash.Of(shell.Framebuffer));
        Assert.NotSame(shell.Framebuffer, other.Framebuffer);
    }

    /// <summary>
    /// Redrawing the same state gives the same bytes: the screen depends on the session, the
    /// view and the draw clock, and on nothing else — no window size, no leftover console state.
    /// That is what makes a pinned hash meaningful rather than lucky, and it is why
    /// <see cref="ShellScreen.Begin"/> resets camera, clip, palette and transparency before every
    /// draw.
    ///
    /// <para>The clock is the one honest input, and it is held at zero here for the same reason
    /// the sprite screen holds it there for its marching ants: a blink is host chrome, it reaches
    /// no simulation and no hash of a cartridge's frame, and a golden constant may not depend on
    /// when the test ran.</para>
    /// </summary>
    [Fact]
    public void TheScreenIsAPureFunctionOfTheSessionTheViewAndTheClock()
    {
        CodeEditorSession session = WrittenCart();
        CodeEditorView view = SyncedView(session);
        var screen = new ShellScreen();

        DrawIdle(screen, session, view);
        string first = FrameHash.Of(screen.Framebuffer);

        // A different picture in between, so a stale-state bug has something to leak: the find
        // line, then a hovered button with its tooltip up, then half a blink.
        view.OpenFind();
        DrawIdle(screen, session, view);
        view.CloseFind();
        CodeEditorRenderer.Draw(
            screen, session, view, HoverTarget.OfButton(EditorButton.ToolGoTo), true, 0.60);
        DrawIdle(screen, session, view);

        Assert.Equal(first, FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The caret is the one thing on this screen that moves by itself, and it moves on the draw
    /// clock alone: half a blink later the same session and the same view give a different frame,
    /// and half a blink after that the first one back. Break recipe: drop the
    /// <c>timeSeconds</c> term from <c>DrawCaret</c> and the middle assertion goes red.
    /// </summary>
    [Fact]
    public void TheCaretBlinksOnTheDrawClockAndOnNothingElse()
    {
        CodeEditorSession session = WrittenCart();
        CodeEditorView view = SyncedView(session);
        var screen = new ShellScreen();

        // Two blinks a second, so the caret is lit through the first half of every second and
        // dark through the second half.
        CodeEditorRenderer.Draw(screen, session, view, null, false, 0.0);
        string lit = FrameHash.Of(screen.Framebuffer);
        CodeEditorRenderer.Draw(screen, session, view, null, false, 0.60);
        string dark = FrameHash.Of(screen.Framebuffer);
        CodeEditorRenderer.Draw(screen, session, view, null, false, 1.0);
        string litAgain = FrameHash.Of(screen.Framebuffer);

        Assert.NotEqual(lit, dark);
        Assert.Equal(lit, litAgain);

        // ...and it stops blinking while the exit prompt is up: a caret winking under a question
        // the author is answering reads as an invitation to type.
        Assert.True(view.RequestClose(session));        // clean buffer: nothing to ask, it leaves
        session.Insert("x");
        Assert.False(view.RequestClose(session));       // dirty: the prompt goes up instead
        Assert.True(view.ExitPromptShown);
        CodeEditorRenderer.Draw(screen, session, view, null, false, 0.0);
        string promptLit = FrameHash.Of(screen.Framebuffer);
        CodeEditorRenderer.Draw(screen, session, view, null, false, 0.60);
        Assert.Equal(promptLit, FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The tooltip is TIC-80's, not a popup: hovering a control prints its label into the top
    /// band's free strip instead of covering the code with a box, and the label is cut to what
    /// the strip holds. Same mechanism the other two console screens use, same single owner of
    /// the cut (<see cref="ConsoleChrome.FitTooltip"/>), so the three cannot grow three tooltip
    /// styles.
    /// </summary>
    [Fact]
    public void AHoveredControlPrintsItsLabelIntoTheTopBandAndNowhereElse()
    {
        CodeEditorSession session = WrittenCart();
        CodeEditorView view = SyncedView(session);
        var screen = new ShellScreen();

        CodeEditorLayout layout = CodeEditorRenderer.Draw(
            screen, session, view, HoverTarget.OfButton(EditorButton.ToolFind), true, 0.0);

        Assert.Equal(25, layout.Chrome.TooltipChars);
        Assert.Equal(
            EditorIcons.CodeTooltip(EditorButton.ToolFind)[..25],
            layout.Chrome.FitTooltip(EditorIcons.CodeTooltip(EditorButton.ToolFind)));
        bool inkInField = false;
        for (int x = layout.Chrome.TooltipField.X; x < layout.Chrome.TooltipField.Right; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                inkInField |= screen.Console.Pget(x, y) != 0;
            }
        }
        Assert.True(inkInField);
        // ...and the page is exactly what it was with nothing hovered: no box over the code.
        var quiet = new ShellScreen();
        DrawIdle(quiet, session, SyncedView(session));
        for (int y = layout.Text.Y; y < layout.Text.Bottom; y++)
        {
            for (int x = layout.Text.X; x < layout.Text.Right; x++)
            {
                Assert.Equal(quiet.Console.Pget(x, y), screen.Console.Pget(x, y));
            }
        }
    }
}

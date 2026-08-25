using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The CODE tab woken up: the screen's geometry, the rule that the view always catches the
/// caret, the mouse landing on the exact character it was aimed at, the character stream
/// reaching the buffer, the clipboard, the find line, travel between the three tabs, the
/// dirty-exit contract, and the button-contract sweep this project made law in wave 2g —
/// <b>every button the layout places and the stub list does not kill must, clicked through the
/// real router pieces, change something observable</b>.
///
/// <para><b>Whole frames of the production router, with no window anywhere.</b> The harness is
/// <c>EditorInputRouterTests</c>'s, with one addition: <see cref="CodeEditorInput"/> also takes
/// the frame's <em>characters</em>, because a text editor needs the keyboard layout applied and
/// a <c>KeyboardState</c> has no such thing in it. In the window those come from
/// <c>Window.TextInput</c>; here they are a string the test writes. Everything else is the
/// production article: the real <see cref="ShellCommandReader"/>, the real
/// <see cref="EditorMouseReader"/>, the real <see cref="CodeEditorLayout"/>, the real
/// <see cref="ShellModeMachine"/>.</para>
///
/// <para>The one thing mirrored is <see cref="Harness.Frame"/>'s two-line mode switch, exactly
/// as its neighbour mirrors it and for the same reason: the mode can change <em>inside</em> a
/// frame (a click on the exit tab, a Discard on the prompt) and the next frame must land on the
/// other screen. It consults the same single owner of "which mode is on screen" the shell does.</para>
///
/// <para><b>Re-pinned in wave R4, whole file, and here is the one sentence that explains every
/// changed number below.</b> ADR-029 moved this screen off the window and onto the console, so
/// the surface it is laid out on is 160x90 instead of 1280x720 and <b>every pixel in this file
/// is a console pixel</b>. Nothing about what the screen <em>does</em> changed — the same
/// router, the same session, the same view, the same button contract — but three kinds of
/// assertion had to move with the surface: the harness now hands the router the console's two
/// numbers (as <c>QuarpGame.ConsoleEditorContext</c> does), the layout is computed at 160x90,
/// and a click aimed at a column that no longer exists on a 36-column page had to be re-aimed.
/// Each of those carries its own note at the assertion. What this file deliberately does NOT do
/// any more is pin pixels: the picture itself is a framebuffer now, and
/// <c>CodeEditorScreenGoldenTests</c> hashes it.</para>
/// </summary>
public class CodeEditorScreenTests : IDisposable
{
    private readonly string _root;

    public CodeEditorScreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-codescreen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    /// <summary>
    /// The console's own screen — the surface the sprite (R2), map (R3) and code (R4) editors
    /// are laid out on. Every mouse point in this file is a console pixel because of it.
    /// </summary>
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    /// <summary>One frame at 60 Hz — the router spends it on the tooltip clock only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside any rectangle the layout places — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));

    /// <summary>A sample with lines of several lengths, so a click past the end of a short one has somewhere to land.</summary>
    private const string Sample = "using System;\npublic class Demo\n{\n    public void Run() { }\n}\n";

    // ==================================================================================
    // The window, minus the window.
    // ==================================================================================

    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        // MonoGame reports the wheel as a running total, not a per-frame delta, and the
        // production reader subtracts the previous frame's. Keeping the total here rather than
        // passing a delta is what makes a wheel gesture followed by an ordinary frame behave the
        // way it does in the window — the second frame reports no movement at all.
        private int _wheelTotal;

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        /// <summary>
        /// Rebuilt per frame, like the window's. Since wave R2 the two numbers are <b>the size
        /// of the surface the screen on show is laid out on</b>, and since wave R4 the code
        /// editor's surface is the console itself (ADR-029) exactly as the sprite and map
        /// screens' are: 160x90, not the back buffer. <c>QuarpGame</c> makes exactly this
        /// switch — see <c>ConsoleEditorContext</c> — so a frame here means what a frame there
        /// means. The consequence for whoever writes a test against this screen: <b>its mouse
        /// points are console pixels</b>, taken straight off the layout's own rectangles.
        /// Production reaches the same numbers by putting the window's point through
        /// <see cref="EditorMouse.ToConsole"/>, whose own arithmetic is pinned in
        /// <c>EditorMouseReaderTests</c> rather than re-run here.
        ///
        /// <para>The sound screen is the one mode left that is still measured against the back
        /// buffer, so it is the exception written here rather than the rule; this harness never
        /// enters it, and the branch exists so that the day it does, the frame it gets is the
        /// one the window would give it.</para>
        /// </summary>
        internal EditorShell Context =>
            Modes.Mode == ShellMode.SfxEditor
                ? new(Modes, Flyout, Hover, SheetScroll, WindowWidth, WindowHeight)
                : new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight);

        internal CodeEditorLayout Layout => CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);

        internal CodeEditorSession Session => Modes.CodeEditor!;

        internal CodeEditorView View => Modes.CodeView!;

        /// <summary>One whole frame through the production router for whichever editor is on screen.</summary>
        internal void Frame(Keys[] down, string typed, int mouseX, int mouseY, ButtonState left)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, _wheelTotal, left, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
            switch (Modes.Mode)
            {
                case ShellMode.Editor:
                    SpriteEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
                case ShellMode.MapEditor:
                    MapEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
                case ShellMode.CodeEditor:
                    CodeEditorInput.Update(Context, commands, mouse, typed.ToCharArray(), FrameSeconds);
                    break;
            }
        }

        internal void Idle() => Frame(NoKeys, string.Empty, Off, Off, ButtonState.Released);

        /// <summary>One frame with the pointer parked on a console pixel and nothing pressed — a hover, and only that.</summary>
        internal void Move(int x, int y) => Frame(NoKeys, string.Empty, x, y, ButtonState.Released);

        /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
        internal void Tap(params Keys[] down)
        {
            Frame(down, string.Empty, Off, Off, ButtonState.Released);
            Idle();
        }

        /// <summary>
        /// Text typed the way the OS delivers it: one character per frame, no key edges at all.
        /// That is exactly what <c>Window.TextInput</c> produces — the key frame and the
        /// character stream are two channels, and this one carries no keys.
        /// </summary>
        internal void Type(string text)
        {
            foreach (char c in text)
            {
                Frame(NoKeys, c.ToString(), Off, Off, ButtonState.Released);
                Idle();
            }
        }

        internal void LeftDown(int x, int y) => Frame(NoKeys, string.Empty, x, y, ButtonState.Pressed);

        internal void LeftDrag(int x, int y) => Frame(NoKeys, string.Empty, x, y, ButtonState.Pressed);

        internal void LeftUp(int x, int y) => Frame(NoKeys, string.Empty, x, y, ButtonState.Released);

        /// <summary>
        /// One wheel gesture over a point. MonoGame reports the wheel as a running total and
        /// the production reader turns it into this frame's delta, so the test hands over the
        /// new total exactly as the device would: one notch is 120.
        /// </summary>
        internal void Wheel(int notches, int x, int y)
        {
            _wheelTotal += notches * 120;       // one notch is 120 detents, the device's own unit
            Frame(NoKeys, string.Empty, x, y, ButtonState.Released);
        }

        internal void Click(int x, int y)
        {
            LeftDown(x, y);
            LeftUp(x, y);
        }

        /// <summary>
        /// The rectangle comes from the layout of the screen ON SHOW, not from this screen's
        /// layout. Since wave R4 all three screens this harness can stand on are measured on the
        /// console's own 160x90 frame and their six tabs land on identical pixels — but a tool
        /// button does not (each screen places its own tool block), so asking the screen you are
        /// actually standing on stays the only correct question. It is also how this helper
        /// failed when the sprite editor moved in R2, which is why it asks at all.
        /// </summary>
        internal void ClickButton(EditorButton button)
        {
            Rectangle rect = Modes.Mode switch
            {
                ShellMode.Editor => SpriteEditorLayout
                    .Compute(ConsoleWidth, ConsoleHeight, Modes.Editor!.RegionCells)
                    .ButtonRect(button),
                ShellMode.MapEditor => MapEditorLayout
                    .Compute(ConsoleWidth, ConsoleHeight)
                    .ButtonRect(button),
                _ => Layout.ButtonRect(button),
            };
            Click(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
    }

    // ==================================================================================
    // Fixtures — the road the shell really takes, menu → library → editor → CODE tab.
    // ==================================================================================

    private ShellModeMachine MachineWithCode(out string cartFolder, string source)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        cartFolder = Path.Combine(root, "cart");
        Directory.CreateDirectory(Path.Combine(cartFolder, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(cartFolder, "manifest.json"), "{\"name\":\"code\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(SourcePath(cartFolder), source);
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();           // the real road since ADR-028: menu → library → editor
        machine.OpenLibrary();
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        machine.SwitchEditorTab(ShellMode.CodeEditor);
        Assert.Equal(ShellMode.CodeEditor, machine.Mode);
        return machine;
    }

    private Harness OpenCodeEditor(out string cartFolder, string source = Sample) =>
        new(MachineWithCode(out cartFolder, source));

    private static string SourcePath(string cartFolder) => Path.Combine(
        cartFolder, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName);

    /// <summary>A bare session and view, for the claims that need no shell at all.</summary>
    private (CodeEditorSession Session, CodeEditorView View) Document(string source)
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(SourcePath(folder), source);
        return (new CodeEditorSession(folder), new CodeEditorView());
    }

    // ==================================================================================
    // 1. Geometry.
    // ==================================================================================

    /// <summary>
    /// Everything the screen draws is inside the <b>console</b>, nothing overlaps anything else,
    /// and the text box holds a whole number of characters by a whole number of lines.
    ///
    /// <para><b>Re-pinned in wave R4, and this is what changed.</b> This used to be a
    /// <c>[Theory]</c> over two window sizes, because the screen was measured against a window;
    /// ADR-029 moved it onto the console, so there is exactly one size to check and its numbers
    /// are constants rather than functions. The gutter assertions are gone with the gutter —
    /// <see cref="CodeEditorLayout"/>'s type note carries the arithmetic that spent its six
    /// columns on code, as all three reference consoles do — and the text-scale assertion is
    /// gone with the second text scale: on the console the system font has one size.</para>
    ///
    /// <para>Break recipe, and each of these was checked to actually fire rather than assumed:
    /// take the text field back to <c>chrome.ContentBottom</c> instead of
    /// <see cref="ConsoleChrome.SliderBottom"/> and the eleven becomes ten; widen
    /// <c>ToolColumns</c> to two, or the scrollbar past three pixels, and the thirty-six drops;
    /// drop either <c>/ CellWidth * CellWidth</c> trim and the whole-multiple assertions go.
    /// Deleting the one-pixel <c>ScrollBarGap</c> is deliberately NOT on that list: at 160 the
    /// cell trim already leaves that pixel free (144 of 145 either way), so the constant is
    /// insurance for a console of another width and nothing here can see it — which is worth
    /// knowing before someone writes a test that claims otherwise.</para>
    /// </summary>
    [Fact]
    public void EverythingSitsInsideTheConsoleAndNothingOverlaps()
    {
        var layout = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        var console = new Rectangle(0, 0, ConsoleWidth, ConsoleHeight);

        Assert.True(console.Contains(layout.Text), "the text field is off screen");
        Assert.True(console.Contains(layout.ScrollBar), "the scrollbar is off screen");
        Assert.True(console.Contains(layout.StatusBar));
        Assert.False(layout.Text.Intersects(layout.ScrollBar));

        // Everything stops above the reserved message line — the exit question must never hide
        // under the page.
        Assert.True(layout.Text.Bottom <= layout.PromptY);
        Assert.True(layout.ScrollBar.Bottom <= layout.PromptY);

        // Whole cells, and the exact page wave R4 argued for.
        Assert.Equal(0, layout.Text.Width % layout.CharWidth);
        Assert.Equal(0, layout.Text.Height % layout.LineHeight);
        Assert.Equal(SystemFont.CellWidth, layout.CharWidth);
        Assert.Equal(SystemFont.CellHeight, layout.LineHeight);
        Assert.Equal(11, layout.VisibleLines);
        Assert.Equal(36, layout.VisibleColumns);

        // Every button this screen owns is placed, inside the console, and clear of everything.
        Assert.Equal(AllButtons.Count(EditorIcons.BelongsToCodeEditor), layout.Buttons.Count);
        Assert.All(layout.Buttons, place => Assert.True(EditorIcons.BelongsToCodeEditor(place.Id)));
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            Rectangle rect = layout.Buttons[i].Rect;
            Assert.True(console.Contains(rect), $"{layout.Buttons[i].Id} is off screen");
            Assert.False(rect.Intersects(layout.Text), $"{layout.Buttons[i].Id} sits on the text");
            Assert.False(rect.Intersects(layout.ScrollBar), $"{layout.Buttons[i].Id} sits on the scrollbar");
            for (int j = i + 1; j < layout.Buttons.Count; j++)
            {
                Assert.False(rect.Intersects(layout.Buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// This screen stands in the SAME frame as the two screens that moved before it: identical
    /// button size, margin, bands, prompt verbs and — the part the mouse depends on — identical
    /// rectangles for the exit button and the five editor tabs. That is what lets an author's
    /// hand aim at a tab without first asking which editor is on screen.
    ///
    /// <para><b>Re-pinned twice, and both times because a screen moved.</b> Until wave R2 this
    /// test compared the code screen with the sprite and map screens in the HOST frame; R2 and
    /// R3 took those two onto the console, so the reference became the sound screen, the last
    /// other tenant of the host frame. Wave R4 has now taken this screen onto the console too,
    /// which flips the comparison the whole way round: the siblings are the sprite and map
    /// screens again, in <see cref="ConsoleChrome"/>, and the sound screen is the one this
    /// screen no longer shares a coordinate system with. Asking whether its exit tab equals
    /// theirs would be asking whether 1152 equals 10 — a question with a wrong answer and no
    /// meaning — so the last line asserts the inequality instead, which is what stops a later
    /// hand from "fixing" it by dragging one screen back.</para>
    ///
    /// <para><b>Save, Undo and Redo are deliberately NOT compared any more.</b> In the host
    /// frame they were chrome — a right-aligned row in the status band, on the same pixels for
    /// every screen. The console's status band is five pixels tall and an icon-button is ten, so
    /// on the console each screen carries those three in its own tool block: the sprite and map
    /// screens at the foot of a two-wide column, this one at the foot of a one-wide column.
    /// Their pixels differ by construction and asserting them equal would be asserting a
    /// coincidence.</para>
    ///
    /// <para>Break recipe: give <see cref="CodeEditorLayout"/> its own copy of the chrome
    /// arithmetic instead of calling <see cref="ConsoleChrome.Compute"/> — every assertion here
    /// goes red, which is exactly what one owner of the frame makes impossible.</para>
    /// </summary>
    [Fact]
    public void TheCodeScreenStandsInTheSameChromeAsItsSiblings()
    {
        var code = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        var sprite = SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, regionCells: 1);
        var map = MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight);

        Assert.Equal(ConsoleChrome.ButtonSize, code.ButtonSize);
        Assert.Equal(sprite.ButtonSize, code.ButtonSize);
        Assert.Equal(sprite.Margin, code.Margin);
        Assert.Equal(sprite.TabStrip, code.TabStrip);
        Assert.Equal(sprite.StatusBar, code.StatusBar);
        Assert.Equal(sprite.PromptY, code.PromptY);
        Assert.Equal(map.TabStrip, code.TabStrip);
        Assert.Equal(map.StatusBar, code.StatusBar);
        Assert.Equal(map.PromptY, code.PromptY);
        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Assert.Equal(sprite.PromptVerbRect(verb), code.PromptVerbRect(verb));
            Assert.Equal(map.PromptVerbRect(verb), code.PromptVerbRect(verb));
        }
        EditorButton[] shared =
        {
            EditorButton.ExitTab, EditorButton.CodeTab, EditorButton.SpritesTab,
            EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
        };
        foreach (EditorButton button in shared)
        {
            Assert.Equal(sprite.ButtonRect(button), code.ButtonRect(button));
            Assert.Equal(map.ButtonRect(button), code.ButtonRect(button));
        }
        // The sound screen was the last host-frame tenant and it moved in wave R5, so it belongs
        // in the agreement now rather than opposite it. That the console frame is a different
        // frame from the host one is pinned once, where it belongs: ConsoleChromeTests.
        var sound = SfxEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        Assert.Equal(sound.ButtonRect(EditorButton.ExitTab), code.ButtonRect(EditorButton.ExitTab));
    }

    // ==================================================================================
    // 2. The view always catches the caret.
    // ==================================================================================

    /// <summary>
    /// The screen's one standing promise: after any caret movement the scroll has moved by
    /// exactly as much as it took to show the caret again — not more, not less — in both
    /// directions and on both axes.
    ///
    /// <para>Break recipe: delete the inner <c>Math.Clamp</c> in
    /// <see cref="CodeEditorView.FollowCaret"/> and every assertion after the first goes red;
    /// make <see cref="CodeEditorView.Sync"/> follow unconditionally and the wheel test below
    /// goes red instead.</para>
    /// </summary>
    [Fact]
    public void TheViewScrollsExactlyEnoughToShowTheCaret()
    {
        var layout = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        string source = string.Join(
            "\n", Enumerable.Range(0, 200).Select(i => $"line {i} " + new string('x', 200)));
        (CodeEditorSession session, CodeEditorView view) = Document(source);

        view.Sync(layout, session);
        Assert.Equal(0, view.FirstLine);
        Assert.Equal(0, view.FirstColumn);

        int lines = layout.VisibleLines;

        // One line past the bottom edge: the window moves down by exactly one.
        session.SetCursor(lines, 0);
        view.Sync(layout, session);
        Assert.Equal(1, view.FirstLine);

        // Twenty further: exactly twenty more.
        session.SetCursor(lines + 20, 0);
        view.Sync(layout, session);
        Assert.Equal(21, view.FirstLine);

        // Back up above the top edge: the caret's own line becomes the first one, and no more.
        session.SetCursor(5, 0);
        view.Sync(layout, session);
        Assert.Equal(5, view.FirstLine);

        // Horizontally: the same rule, one column at a time.
        session.SetCursor(5, layout.VisibleColumns + 3);
        view.Sync(layout, session);
        Assert.Equal(4, view.FirstColumn);
        session.SetCursor(5, 0);
        view.Sync(layout, session);
        Assert.Equal(0, view.FirstColumn);
    }

    /// <summary>
    /// The other half of the same rule, and the reason <see cref="CodeEditorView.Sync"/> watches
    /// the caret rather than following it every frame: the wheel moves the window and leaves the
    /// caret alone, and the next keystroke brings the window back to it.
    /// </summary>
    [Fact]
    public void TheWheelScrollsWithoutDraggingTheCaretAndTheNextKeyBringsItBack()
    {
        Harness harness = OpenCodeEditor(
            out _, string.Join("\n", Enumerable.Range(0, 200).Select(i => $"line {i}")));
        CodeEditorLayout layout = harness.Layout;
        int x = layout.Text.X + layout.Text.Width / 2;
        int y = layout.Text.Y + layout.Text.Height / 2;

        harness.Wheel(-10, x, y);

        Assert.Equal(30, harness.View.FirstLine);        // ten notches of three lines each
        Assert.Equal(0, harness.Session.CursorLine);     // the caret stayed where it was

        harness.Tap(Keys.Down);

        // The key moved the caret, so the view came back to it — to the least scroll that shows
        // it, which coming from below means the caret's own line at the top.
        Assert.Equal(1, harness.Session.CursorLine);
        Assert.Equal(1, harness.View.FirstLine);
    }

    // ==================================================================================
    // 3-4. The mouse.
    // ==================================================================================

    /// <summary>
    /// A click lands on the character it was aimed at — and past the end of a short line it
    /// lands at that line's end, which is the session's clamp doing its job rather than the
    /// layout guessing.
    ///
    /// <para>Break recipe: add or drop a <c>+ 1</c> in
    /// <see cref="CodeEditorLayout.TryTextCell"/>'s division and the exact-cell assertion goes
    /// red; make <see cref="CodeEditorSession.SetCursor"/> throw instead of clamp and the
    /// past-the-end one does.</para>
    /// </summary>
    [Fact]
    public void AClickPutsTheCaretOnTheCharacterItWasAimedAt()
    {
        Harness harness = OpenCodeEditor(out _);
        CodeEditorLayout layout = harness.Layout;

        Rectangle cell = layout.CellRect(3, 7, 0, 0);
        harness.Click(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);

        Assert.Equal(3, harness.Session.CursorLine);
        Assert.Equal(7, harness.Session.CursorColumn);

        // Line 1 is "public class Demo" — seventeen characters; column 30 is nothing but air.
        // (It used to be column 40. The console's page is 36 columns wide — CodeEditorLayout's
        // type note has the arithmetic — so column 40 is no longer a place on this screen at
        // all, and a click aimed there would land on the scrollbar instead of on the line.)
        Rectangle air = layout.CellRect(1, 30, 0, 0);
        harness.Click(air.X + 2, air.Y + 2);

        Assert.Equal(1, harness.Session.CursorLine);
        Assert.Equal("public class Demo".Length, harness.Session.CursorColumn);
    }

    /// <summary>
    /// A drag selects from where it went down to where it came up, across lines.
    ///
    /// <para>Break recipe: pass <c>extend: false</c> in the drag branch of
    /// <see cref="CodeEditorInput"/> and the selection collapses to nothing; forget
    /// <c>BeginTextDrag</c> and the drag frames stop being seen at all.</para>
    /// </summary>
    [Fact]
    public void ADragSelectsFromWhereItStartedToWhereItEnded()
    {
        Harness harness = OpenCodeEditor(out _);
        CodeEditorLayout layout = harness.Layout;
        Rectangle from = layout.CellRect(0, 2, 0, 0);
        Rectangle to = layout.CellRect(3, 9, 0, 0);

        harness.LeftDown(from.X + 1, from.Y + 1);
        harness.LeftDrag(to.X + 1, to.Y + 1);
        harness.LeftUp(to.X + 1, to.Y + 1);

        Assert.True(harness.Session.HasSelection);
        Assert.Equal(new CodePosition(0, 2), harness.Session.SelectionStart);
        Assert.Equal(new CodePosition(3, 9), harness.Session.SelectionEnd);
        Assert.Equal("ing System;\npublic class Demo\n{\n    publi", harness.Session.SelectedText);
    }

    // ==================================================================================
    // 5-6. Typing and the clipboard.
    // ==================================================================================

    /// <summary>
    /// The wave's central seam: a character from the window's text-input stream reaches the
    /// document and changes it, and it arrives at the caret the mouse left.
    ///
    /// <para>Break recipe: drop the <paramref name="typed"/> loop from
    /// <see cref="CodeEditorInput"/> and this is the only test that notices — which is precisely
    /// why it exists; every key-frame test would stay green with typing deleted.</para>
    /// </summary>
    [Fact]
    public void ATypedCharacterReachesTheBufferAtTheCaret()
    {
        Harness harness = OpenCodeEditor(out _, "abc");
        CodeEditorLayout layout = harness.Layout;
        Rectangle cell = layout.CellRect(0, 1, 0, 0);
        harness.Click(cell.X + 2, cell.Y + 2);

        harness.Type("XY");

        Assert.Equal("aXYbc", harness.Session.Text);
        Assert.True(harness.Session.IsDirty);
        Assert.Equal(3, harness.Session.CursorColumn);
    }

    /// <summary>
    /// Ctrl+A, Ctrl+C and Ctrl+V through the production reader and router, over the view's own
    /// clipboard. The buffer is internal this wave (see <see cref="ITextClipboard"/>); what this
    /// pins is the three verbs' effect on the text, which is what a later system-clipboard
    /// implementation must keep true.
    ///
    /// <para>Break recipe: drop the <c>ctrl &amp;&amp;</c> guard from <c>CodeSelectAll</c> in
    /// <see cref="ShellCommandReader"/> and typing "a" starts selecting the file; swap
    /// <see cref="CodeEditorView.Copy"/>'s and <see cref="CodeEditorView.Cut"/>'s bodies and the
    /// paste assertion goes red.</para>
    /// </summary>
    [Fact]
    public void SelectAllCopyAndPasteGiveTheExpectedText()
    {
        Harness harness = OpenCodeEditor(out _, "abc\ndef");

        harness.Tap(Keys.LeftControl, Keys.A);
        Assert.True(harness.Session.HasSelection);
        Assert.Equal("abc\ndef", harness.Session.SelectedText);

        harness.Tap(Keys.LeftControl, Keys.C);
        Assert.Equal("abc\ndef", harness.View.ClipboardText);

        harness.Tap(Keys.LeftControl, Keys.End);        // Ctrl+End: past everything, selection dropped
        Assert.False(harness.Session.HasSelection);

        harness.Tap(Keys.LeftControl, Keys.V);
        Assert.Equal("abc\ndefabc\ndef", harness.Session.Text);

        // And the cut half: select all again, cut, and the buffer is empty while the clipboard
        // holds what was there.
        harness.Tap(Keys.LeftControl, Keys.A);
        harness.Tap(Keys.LeftControl, Keys.X);
        Assert.Equal(string.Empty, harness.Session.Text);
        Assert.Equal("abc\ndefabc\ndef", harness.View.ClipboardText);
    }

    // ==================================================================================
    // 7. Find.
    // ==================================================================================

    /// <summary>
    /// Ctrl+F raises the find line, the characters typed after it land in the line and not in
    /// the buffer, and Enter walks the caret onto the occurrence.
    ///
    /// <para>Break recipe: let <see cref="CodeEditorInput"/> fall through to the edit keys while
    /// <see cref="CodeEditorView.FieldShown"/> is true and the buffer assertion goes red — the
    /// search term would be typed into the file.</para>
    /// </summary>
    [Fact]
    public void FindOpensTakesTextAndJumpsToTheOccurrence()
    {
        const string Source = "alpha\nbeta\ngamma\nbeta\n";
        Harness harness = OpenCodeEditor(out _, Source);

        harness.Tap(Keys.LeftControl, Keys.F);
        Assert.True(harness.View.FindShown);

        harness.Type("gamma");
        Assert.Equal("gamma", harness.View.FindText);
        Assert.Equal(Source, harness.Session.Text);         // the buffer never saw a keystroke
        Assert.False(harness.Session.IsDirty);

        harness.Tap(Keys.Enter);

        Assert.Equal(2, harness.Session.CursorLine);
        Assert.True(harness.Session.HasSelection);
        Assert.Equal("gamma", harness.Session.SelectedText);

        // Esc closes the line and leaves the editor standing — it is not an exit.
        harness.Tap(Keys.Escape);
        Assert.False(harness.View.FindShown);
        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);
    }

    /// <summary>
    /// Go-to-line is the other footer tenant and takes the other key: Ctrl+L, PICO-8's own.
    /// The number the author types is 1-based, which is what the status line prints.
    /// </summary>
    [Fact]
    public void GoToLineJumpsToTheNumberTheAuthorTyped()
    {
        Harness harness = OpenCodeEditor(
            out _, string.Join("\n", Enumerable.Range(0, 50).Select(i => $"line {i}")));

        harness.Tap(Keys.LeftControl, Keys.L);
        Assert.True(harness.View.GoToShown);
        harness.Type("31");
        harness.Tap(Keys.Enter);

        Assert.False(harness.View.GoToShown);
        Assert.Equal(30, harness.Session.CursorLine);       // 1-based in, 0-based inside
        Assert.Equal("line 30", harness.Session.Lines[harness.Session.CursorLine]);
    }

    // ==================================================================================
    // 8. Travel between the three tabs.
    // ==================================================================================

    /// <summary>
    /// From the code tab to the sprites tab and back — by click and by key — with the unsaved
    /// text intact, and the same session on the way back rather than a reload.
    ///
    /// <para>Break recipe: null <c>CodeEditor</c> out in
    /// <see cref="ShellModeMachine.SwitchEditorTab"/>, or rebuild the session on every visit,
    /// and the identity or the text assertion goes red — which is the shape of the data loss.</para>
    /// </summary>
    [Fact]
    public void TheTabsTravelBothWaysWithoutLosingUnsavedText()
    {
        Harness harness = OpenCodeEditor(out _, "abc");
        harness.Type("Z");
        CodeEditorSession code = harness.Session;
        Assert.Equal("Zabc", code.Text);
        Assert.True(code.IsDirty);

        // The mouse path: the sprites tab, then the code tab again. Both screens place the six
        // tabs on the same pixels (ConsoleChromeTests pins that), so one rectangle serves both.
        harness.ClickButton(EditorButton.SpritesTab);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        harness.ClickButton(EditorButton.CodeTab);
        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);
        Assert.Same(code, harness.Modes.CodeEditor);
        Assert.Equal("Zabc", code.Text);

        // The keyboard path: Alt+Right walks one tab right (code → sprites), Alt+Left back.
        harness.Tap(Keys.LeftAlt, Keys.Right);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        harness.Tap(Keys.LeftAlt, Keys.Left);
        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);
        Assert.Same(code, harness.Modes.CodeEditor);
        Assert.Equal("Zabc", code.Text);
        Assert.True(code.IsDirty);
    }

    /// <summary>
    /// A cart that never visits the CODE tab gets no code session and therefore cannot get a
    /// <c>src</c> folder — the session's "absent file is a valid empty buffer" rule protected at
    /// the one place it could be broken by accident. Break recipe: create the session eagerly in
    /// <see cref="ShellModeMachine.OpenEditor"/>.
    /// </summary>
    [Fact]
    public void TheCodeSessionIsNotBornUntilTheTabIsVisited()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"nocode\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();

        Assert.Null(machine.CodeEditor);
        Assert.Null(machine.CodeView);
        Assert.False(Directory.Exists(Path.Combine(folder, CodeEditorSession.SourceDirectoryName)));
    }

    // ==================================================================================
    // 9. The exit.
    // ==================================================================================

    /// <summary>
    /// Esc on a dirty buffer raises the footer question instead of leaving, and Z saves and
    /// leaves — the same contract the other two screens carry, over a third payload.
    ///
    /// <para>Break recipe: return true unconditionally from
    /// <see cref="CodeEditorView.RequestClose"/> and unsaved text starts leaving silently, which
    /// is the whole class of loss the prompt exists for.</para>
    /// </summary>
    [Fact]
    public void EscapeOnADirtyBufferAsksAndZSavesAndLeaves()
    {
        Harness harness = OpenCodeEditor(out string folder, "abc");
        harness.Type("q");

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);
        Assert.True(harness.Modes.CodeView!.ExitPromptShown);

        harness.Tap(Keys.Z);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Null(harness.Modes.CodeEditor);
        Assert.Equal("qabc", File.ReadAllText(SourcePath(folder)));
    }

    /// <summary>A clean buffer leaves in one Esc — the chain must not add a step to the common case.</summary>
    [Fact]
    public void EscapeOnACleanBufferLeavesWithoutAsking()
    {
        Harness harness = OpenCodeEditor(out _, "abc");

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Null(harness.Modes.CodeEditor);
        Assert.Null(harness.Modes.Editor);
    }

    /// <summary>
    /// X leaves the disk byte-for-byte untouched. Break recipe: make
    /// <see cref="ShellModeMachine.DiscardCodeAndClose"/> call <c>Save</c> first.
    /// </summary>
    [Fact]
    public void DiscardingTheCodeWritesNothingAtAll()
    {
        Harness harness = OpenCodeEditor(out string folder, "abc");
        harness.Type("q");
        harness.Tap(Keys.Escape);
        Assert.True(harness.Modes.CodeView!.ExitPromptShown);

        harness.Tap(Keys.X);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Equal("abc", File.ReadAllText(SourcePath(folder)));
    }

    /// <summary>
    /// The trap the shared exit exists to avoid, now with three banks: leaving from the CODE tab
    /// while the sprite sheet on another tab is dirty must not drop the sheet. The editor stays
    /// open, that tab comes to the front and asks.
    ///
    /// <para>Break recipe: make <c>CloseUnlessAnotherBankIsDirty</c> call <c>CloseEditor</c>
    /// straight away and every assertion here goes red at once.</para>
    /// </summary>
    [Fact]
    public void LeavingFromTheCodeTabDoesNotDropADirtySheet()
    {
        Harness harness = OpenCodeEditor(out _, "abc");
        SpriteEditorSession sheet = harness.Modes.Editor!;
        sheet.SelectColor(7);
        sheet.BeginStroke();
        sheet.Paint(2, 3);
        sheet.EndStroke();
        Assert.True(sheet.IsDirty);
        Assert.False(harness.Session.IsDirty);      // the code itself is clean

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        Assert.True(sheet.ExitPromptShown);
        Assert.NotNull(harness.Modes.CodeEditor);
    }

    // ==================================================================================
    // 10. The button contract — the CODE tab is not a stub any more.
    // ==================================================================================

    /// <summary>
    /// The one-line version of this whole wave: the CODE tab is no longer drawn-but-dead. Its
    /// stub flag is gone, it routes to a real mode, its tooltip promises a key instead of a
    /// later portion, and clicking it from the sprite screen arrives at the code editor.
    ///
    /// <para>Break recipe: put <see cref="EditorButton.CodeTab"/> back into
    /// <see cref="EditorIcons.IsStub"/> — every assertion here goes red, and so does the
    /// contract sweep below, because the router refuses stubs before any verb.</para>
    /// </summary>
    [Fact]
    public void TheCodeTabIsNoLongerADeadButton()
    {
        Assert.False(EditorIcons.IsStub(EditorButton.CodeTab));
        Assert.Equal(ShellMode.CodeEditor, EditorIcons.TabTarget(EditorButton.CodeTab));
        Assert.DoesNotContain(
            "LATER PORTION", EditorIcons.Tooltip(EditorButton.CodeTab), StringComparison.Ordinal);
        Assert.Contains("ALT+", EditorIcons.Tooltip(EditorButton.CodeTab), StringComparison.Ordinal);

        // ...and the click really arrives, from the screen the author is most likely on.
        ShellModeMachine machine = MachineWithCode(out _, "abc");
        machine.SwitchEditorTab(ShellMode.Editor);
        var harness = new Harness(machine);
        harness.Frame(NoKeys, string.Empty, Off, Off, ButtonState.Released);
        // The author is standing on the SPRITE screen, which since wave R2 is the console's own
        // 160x90 frame — so the tab's rectangle comes from that screen's layout, not this one's.
        Rectangle tab = SpriteEditorLayout
            .Compute(ConsoleWidth, ConsoleHeight, machine.Editor!.RegionCells)
            .ButtonRect(EditorButton.CodeTab);
        harness.Click(tab.X + tab.Width / 2, tab.Y + tab.Height / 2);

        Assert.Equal(ShellMode.CodeEditor, machine.Mode);
    }

    /// <summary>
    /// Everything a code button click may legally touch, in one comparable value. A button whose
    /// only effect is invisible to this record would read as unwired, which is the contract
    /// working.
    /// </summary>
    private sealed record Snapshot(
        ShellMode Mode, int Version, bool Dirty, bool CanUndo, bool CanRedo, bool PromptShown,
        bool FindShown, bool GoToShown);

    private static Snapshot Observe(ShellModeMachine machine)
    {
        CodeEditorSession code = machine.CodeEditor!;
        CodeEditorView view = machine.CodeView!;
        return new Snapshot(
            machine.Mode, code.Version, code.IsDirty, code.CanUndo, code.CanRedo,
            view.ExitPromptShown, view.FindShown, view.GoToShown);
    }

    /// <summary>The shell's press dispatch over the real router pieces — the same two-line mirror the map's sweep uses.</summary>
    private static void RouteClick(ShellModeMachine machine, EditorButton button)
    {
        if (EditorIcons.IsStub(button))
        {
            return;                                     // the router refuses stubs before any verb
        }
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            machine.SwitchEditorTab(tab);               // travel is the mode machine's verb
            return;
        }
        if (EditorIcons.ClickCodeButton(machine.CodeEditor!, machine.CodeView!, button))
        {
            machine.HandleEscape();                     // the exit tab's verb belongs to the machine
        }
    }

    /// <summary>A session where every live button has work to do: dirt, an undo step and a redo step.</summary>
    private static void Prepare(CodeEditorSession session)
    {
        session.Insert("hello");
        session.InsertNewLine();
        session.Insert("world");
        session.Undo();
    }

    /// <summary>
    /// The sweep. Live buttons must change the snapshot; stubs and the code tab (it names the
    /// screen already on show) must change exactly nothing.
    ///
    /// <para>Break recipe: delete any <c>case</c> from
    /// <see cref="EditorIcons.ClickCodeButton"/> — that one button's assertion goes red by name.
    /// Add a button to <see cref="CodeEditorLayout"/> without wiring it and the same line names
    /// the new one.</para>
    /// </summary>
    [Fact]
    public void EveryPlacedLiveCodeButtonChangesSomethingObservable()
    {
        foreach (EditorButtonPlace place in CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Buttons)
        {
            ShellModeMachine machine = MachineWithCode(out _, "abc");
            Prepare(machine.CodeEditor!);
            Snapshot before = Observe(machine);

            RouteClick(machine, place.Id);

            Snapshot after = Observe(machine);
            bool contractedNoOp = EditorIcons.IsStub(place.Id) || place.Id == EditorButton.CodeTab;
            if (contractedNoOp)
            {
                Assert.True(before == after, $"{place.Id} is a no-op by contract but changed state");
            }
            else
            {
                Assert.True(
                    before != after,
                    $"{place.Id} is placed and live but its click changed nothing — unwired?");
            }
        }
    }

    /// <summary>
    /// The two code-only buttons, pinned by name: find opens the find line, go-to opens the
    /// number field, and each is the mouse twin of a chord the tooltip names.
    /// </summary>
    [Fact]
    public void TheFindAndGoToButtonsOpenTheirFooterFields()
    {
        ShellModeMachine machine = MachineWithCode(out _, "abc");

        RouteClick(machine, EditorButton.ToolFind);
        Assert.True(machine.CodeView!.FindShown);
        Assert.Contains("CTRL+F", EditorIcons.Tooltip(EditorButton.ToolFind), StringComparison.Ordinal);

        RouteClick(machine, EditorButton.ToolGoTo);
        Assert.True(machine.CodeView!.GoToShown);
        Assert.False(machine.CodeView!.FindShown);      // one footer line, one tenant
        Assert.Contains("CTRL+L", EditorIcons.Tooltip(EditorButton.ToolGoTo), StringComparison.Ordinal);
    }

    /// <summary>
    /// The status line's two fields, named: TIC-80's <c>line X/Y col Z</c> on the left and its
    /// <c>size N/MAX</c> on the right. Break recipe: make either 0-based and the text changes
    /// under an author who is comparing it with a compiler error.
    ///
    /// <para><b>Wave R4 cut one word out of the right-hand field and this is where it is
    /// recorded.</b> The console's status line holds 39 characters; the caret pair can want 22
    /// of them and <c>SIZE 262144/262144</c> wants 18, which is one too many. The word went and
    /// the ratio stayed — see <see cref="CodeEditorRenderer.Budget"/> for the whole argument,
    /// including the red-on-overflow that went with it and where its meaning reappeared. Both
    /// fields at their widest now fit the line with four characters to spare, which is the
    /// assertion below.</para>
    ///
    /// <para>The standing line was re-cut for the same 39 columns: a field that is still empty
    /// spends the line teaching its keys, and from the first character typed the line is the
    /// term. Both shapes are asserted, because the host screen printed one string that was
    /// neither.</para>
    /// </summary>
    [Fact]
    public void TheStatusLineReadsTheCaretAndTheBudget()
    {
        (CodeEditorSession session, CodeEditorView view) = Document("abc\ndef\n");
        session.SetCursor(1, 2);

        Assert.Equal("LINE 2/3 COL 3", CodeEditorRenderer.Coordinates(session));
        Assert.Equal("8/262144", CodeEditorRenderer.Budget(session));
        Assert.Null(CodeEditorRenderer.StandingNotice(session, view));

        // The two fields at their worst still fit the console's 39-character line, which is the
        // whole reason the word SIZE is not in the second one.
        const string WidestCoordinates = "LINE 9999/9999 COL 999";
        const string WidestBudget = "262144/262144";
        Assert.Equal(22, WidestCoordinates.Length);
        Assert.Equal(13, WidestBudget.Length);
        Assert.True(
            WidestCoordinates.Length + WidestBudget.Length
                <= CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Chrome.LineChars);

        view.OpenFind();
        Assert.StartsWith("FIND:", CodeEditorRenderer.StandingNotice(session, view), StringComparison.Ordinal);
        Assert.Contains("ESC CLOSES", CodeEditorRenderer.StandingNotice(session, view), StringComparison.Ordinal);
        view.TypeIntoField('a');
        Assert.Equal("FIND: a", CodeEditorRenderer.StandingNotice(session, view));

        view.OpenGoTo();
        Assert.StartsWith("GO TO LINE:", CodeEditorRenderer.StandingNotice(session, view), StringComparison.Ordinal);
        view.TypeIntoField('7');
        Assert.Equal("GO TO LINE: 7", CodeEditorRenderer.StandingNotice(session, view));

        // Every one of those lines fits what the message band can print, which is what stops a
        // sentence from ending mid-word at the screen's edge.
        ConsoleChrome chrome = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Chrome;
        Assert.True(
            CodeEditorRenderer.StandingNotice(session, view)!.Length <= chrome.LineChars);
    }

    // ==================================================================================
    // 8. Every buttonless control of the code screen names its keys (§8 item 15).
    // ==================================================================================

    /// <summary>
    /// <b>The sweep</b>, the code screen's twin of
    /// <c>SfxEditorTests.EveryKeylessControlAnnouncesItsKeys</c>,
    /// <c>MusicEditorScreenTests.EveryButtonlessControlNamesItsKeys</c> and
    /// <c>MapEditorTransformsTests.EveryButtonlessControlNamesItsKeys</c>: every value of
    /// <see cref="CodeRegion"/> has a rectangle on the screen that shows it, that rectangle's
    /// centre hit-tests back to that same region, and the region has a printable label. Driven
    /// off <c>Enum.GetValues</c>, so a region added without a rectangle or without a label turns
    /// this red on arrival — that is what makes it a sweep and not two assertions.
    ///
    /// <para>The named keys below are the point of the whole item: on a 160x90 console the label
    /// on the control IS the documentation, and these gestures are announced on no button of this
    /// screen — the tool column has find, go-to, save, undo and redo and nothing else, so
    /// Shift-selects, Ctrl+A, Tab, the wheel and F11 have no button to be written on.</para>
    ///
    /// <para><b>Negative control:</b> <see cref="CodeRegion.None"/> is not a control — it has no
    /// rectangle and no label, and asking for one throws by name rather than answering with some
    /// other region's text. A point off every rectangle answers None, which is what makes the
    /// positive half above mean something.</para>
    ///
    /// <para>Break recipe: delete the <c>CodeRegion.ScrollBar</c> arm from
    /// <c>CodeEditorLayout.RegionRect</c> — that region's rectangle comes back empty and the
    /// sweep names it. Delete an arm from <c>EditorIcons.CodeRegionTooltip</c> and the label
    /// assertion throws for that region by name. Drop "F11" out of the page's text and the key
    /// assertion goes red on the one line that names it.</para>
    /// </summary>
    [Fact]
    public void EveryButtonlessControlNamesItsKeys()
    {
        CodeEditorLayout layout = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        foreach (CodeRegion region in Enum.GetValues<CodeRegion>())
        {
            if (region == CodeRegion.None)
            {
                // None is not a control: it has no rectangle and, like its three siblings, no label.
                Assert.Equal(Rectangle.Empty, layout.RegionRect(region));
                Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.CodeRegionTooltip(region));
                continue;
            }
            Rectangle rect = layout.RegionRect(region);
            Assert.NotEqual(Rectangle.Empty, rect);
            Assert.Equal(region, layout.RegionAt(rect.Center.X, rect.Center.Y));
            string label = EditorIcons.CodeRegionTooltip(region);
            Assert.False(string.IsNullOrWhiteSpace(label));
            // ASCII only: the system font has no other alphabet.
            Assert.All(label, c => Assert.InRange(c, ' ', '~'));
        }

        Assert.Equal(CodeRegion.None, layout.RegionAt(Off, Off));

        // The gestures that live on no button of this screen, each named on the control it acts
        // on. This list IS the answer to "where is that documented".
        Assert.Contains("SHIFT+MOVE", EditorIcons.CodeTextTooltip, StringComparison.Ordinal);
        Assert.Contains("CTRL+A", EditorIcons.CodeTextTooltip, StringComparison.Ordinal);
        Assert.Contains("TAB", EditorIcons.CodeTextTooltip, StringComparison.Ordinal);
        Assert.Contains("WHEEL", EditorIcons.CodeTextTooltip, StringComparison.Ordinal);
        Assert.Contains("F11", EditorIcons.CodeTextTooltip, StringComparison.Ordinal);
        Assert.Contains("PGUP/PGDN", EditorIcons.CodeScrollBarTooltip, StringComparison.Ordinal);
        Assert.Contains("DRAG", EditorIcons.CodeScrollBarTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    /// The router builds those targets — the half a table of texts cannot prove. The pointer is
    /// parked on each control through the production <see cref="CodeEditorInput.Update"/> and the
    /// tracker is asked what it saw, so "the scrollbar is mute" would show up here even with
    /// every label written.
    ///
    /// <para><b>Negative control:</b> a pointer on a BUTTON still produces a button target and
    /// not a region, which is what keeps the two kinds from colliding — and a pointer off every
    /// rectangle produces no target at all.</para>
    ///
    /// <para>Break recipe: put the <c>RegionAt</c> arm before <c>TryButton</c> in
    /// <c>CodeEditorInput.Pointer</c>'s hover call and the button assertion goes red; delete the
    /// arm entirely and both region assertions go red at once.</para>
    /// </summary>
    [Fact]
    public void ThePointerOnEachButtonlessControlProducesItsHoverTarget()
    {
        Harness harness = OpenCodeEditor(out _);
        CodeEditorLayout layout = harness.Layout;

        Rectangle text = layout.Text;
        harness.Move(text.Center.X, text.Center.Y);
        AssertRegion(harness, CodeRegion.Text);

        Rectangle bar = layout.ScrollBar;
        harness.Move(bar.Center.X, bar.Center.Y);
        AssertRegion(harness, CodeRegion.ScrollBar);

        // Negative controls: a button is still a button, and nothing is still nothing.
        Rectangle save = layout.ButtonRect(EditorButton.Save);
        harness.Move(save.Center.X, save.Center.Y);
        Assert.NotNull(harness.Hover.Target);
        Assert.Equal(EditorButton.Save, harness.Hover.Target!.Value.Button!.Value);
        Assert.Equal(CodeRegion.None, harness.Hover.Target!.Value.Code);

        harness.Move(Off, Off);
        Assert.Null(harness.Hover.Target);
    }

    /// <summary>What the tracker saw this frame: this region and no button — the two halves of "the label is the control's".</summary>
    private static void AssertRegion(Harness harness, CodeRegion expected)
    {
        Assert.NotNull(harness.Hover.Target);
        Assert.Equal(expected, harness.Hover.Target!.Value.Code);
        Assert.Null(harness.Hover.Target!.Value.Button);
    }

    /// <summary>
    /// The crash lock of 2026-08-25, applied to this screen: a hover target measured on another
    /// screen — no button, no code region — means "no label", never an exception. A frame is
    /// input-then-draw and a tab switch lands between the two halves, so this shape reaches
    /// <c>Draw</c> for real (see <see cref="IconHoverTracker.Clear"/>).
    ///
    /// <para><b>Negative control:</b> a target this screen DOES own still gets its label, both
    /// kinds, so the nulls above are the None arm speaking and not a method that answers null
    /// always. And the mirror: this screen's own region seen by the four screens that already
    /// carry the rule is null there too.</para>
    ///
    /// <para>Break recipe: change <c>CodeEditorRenderer.TooltipText</c>'s last line to
    /// <c>EditorIcons.CodeRegionTooltip(target.Code)</c> with no None arm — the first assertion
    /// goes red with the very exception that killed the console.</para>
    /// </summary>
    [Fact]
    public void AHoverTargetFromAnotherScreenAsksTheCodeScreenForNoLabelInsteadOfKillingTheFrame()
    {
        HoverTarget foreign = HoverTarget.OfSfxRegion(SfxRegion.Octave);
        Assert.Null(foreign.Button);
        Assert.Equal(CodeRegion.None, foreign.Code);
        Assert.Null(CodeEditorRenderer.TooltipText(foreign));

        Assert.Null(CodeEditorRenderer.TooltipText(HoverTarget.OfMapRegion(MapRegion.Canvas)));
        Assert.Null(CodeEditorRenderer.TooltipText(HoverTarget.OfMusicRegion(MusicRegion.Song)));
        Assert.Null(CodeEditorRenderer.TooltipText(HoverTarget.OfSlider()));

        // ...and the code screen's own targets are labelled, both kinds.
        Assert.Equal(
            EditorIcons.CodeTextTooltip,
            CodeEditorRenderer.TooltipText(HoverTarget.OfCodeRegion(CodeRegion.Text)));
        Assert.Equal(
            EditorIcons.CodeTooltip(EditorButton.Save),
            CodeEditorRenderer.TooltipText(HoverTarget.OfButton(EditorButton.Save)));

        // And the mirror: the code screen's target seen by the four screens that carry the rule.
        HoverTarget codeTarget = HoverTarget.OfCodeRegion(CodeRegion.Text);
        Assert.Null(MapEditorRenderer.TooltipText(codeTarget));
        Assert.Null(SfxEditorRenderer.TooltipText(codeTarget));
        Assert.Null(MusicEditorRenderer.TooltipText(codeTarget));
    }
}

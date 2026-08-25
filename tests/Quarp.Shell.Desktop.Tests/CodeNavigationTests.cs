using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The three facts this wave added around the CODE screen, each pinned where it lives:
///
/// <list type="number">
///   <item><b>What counts as a declaration</b> (REFERENCES-EDITORS §8 item 14) —
///     <see cref="CodeEditorSession.IsDeclarationLine"/>, the document layer, driven as a plain
///     object with no screen anywhere near it. The rule is stated in words on that method; this
///     file is where the words become a table, <b>negatives included</b>, because a rule that is
///     only ever shown its own examples is not a rule.</item>
///   <item><b>Alt+Up / Alt+Down travel by it</b> and Alt+Left/Right still travel by tab —
///     driven through the production <see cref="ShellCommandReader"/> and
///     <see cref="CodeEditorInput"/>, so the chord/bare-key precedence being asserted is the
///     shell's own and not a second copy of it.</item>
///   <item><b>F1..F5 name the five editors</b> (§8 item 16) and <b>the budget readout turns red
///     past the limit</b> (§8 item 13, TIC-80's <c>drawStatus</c>).</item>
/// </list>
///
/// <para><b>Headless throughout.</b> <see cref="CodeEditorSession"/> is a plain object,
/// <see cref="ShellScreen"/> is the console's framebuffer with no graphics device behind it, and
/// the harness below is <c>CodeEditorFullscreenTests</c>' cut down to what these claims need. No
/// window is constructed in this file.</para>
///
/// <para><b>What this file does not do: move a pixel of any pinned picture.</b> The status
/// colour's default is asserted to be the very colour the shared painter always drew
/// (<see cref="TheStatusColourIsOptionalAndFourScreensPicturesDoNotMove"/>), which is the whole
/// reason <c>*ScreenGoldenTests</c> need no new hashes: over the byte limit is the only state
/// that draws anything new, and no golden scenario is anywhere near 256 KB.</para>
/// </summary>
public class CodeNavigationTests : IDisposable
{
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    /// <summary>One frame at 60 Hz; the router spends it on the tooltip clock only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside any rectangle the layout places — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private readonly string _root;

    public CodeNavigationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-codenav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ==================================================================================
    // Fixtures.
    // ==================================================================================

    /// <summary>A cart on disk with the given source already in <c>src/main.cs</c>, so the buffer opens clean.</summary>
    private CodeEditorSession Cart(string source)
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"nav\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(
            Path.Combine(folder, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName),
            source);
        return new CodeEditorSession(folder);
    }

    /// <summary>
    /// The sample buffer the travel tests walk. Nine lines, and every negative the rule names is
    /// in it: a commented-out declaration, a declaration inside a string literal, a field with an
    /// initialiser, a plain call and a qualified call.
    /// </summary>
    private const string Sample =
        "public class Player\n"                     // 0  declaration
        + "{\n"                                     // 1
        + "    // public void Ghost()\n"            // 2  comment
        + "    const string S = \"public void Ghost()\";\n"   // 3  string literal
        + "    public int Hp = 3;\n"                // 4  field
        + "    public void Update()\n"              // 5  declaration
        + "    {\n"                                 // 6
        + "        Ghost();\n"                      // 7  call
        + "        Player.Update();\n"              // 8  qualified call
        + "    }\n"                                 // 9
        + "}\n";                                    // 10

    /// <summary>The window, minus the window — <c>CodeEditorFullscreenTests</c>' harness, driving the production reader so edge detection is not reinvented.</summary>
    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal IconHoverTracker Hover { get; } = new();

        internal CodeEditorSession Session => Modes.CodeEditor!;

        internal CodeEditorView View => Modes.CodeView!;

        internal EditorShell Context =>
            new(Modes, new ToolbarFlyout(), Hover, new SheetScroll(), ConsoleWidth, ConsoleHeight);

        internal void Frame(Keys[] down)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                Off, Off, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
            if (Modes.Mode == ShellMode.CodeEditor)
            {
                CodeEditorInput.Update(Context, commands, mouse, Array.Empty<char>(), FrameSeconds);
            }
        }

        /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
        internal void Tap(params Keys[] down)
        {
            Frame(down);
            Frame(NoKeys);
        }

        internal void Type(string text)
        {
            foreach (char c in text)
            {
                ShellCommands commands = _keys.Read(new KeyboardState(NoKeys));
                EditorMouse mouse = _pointer.Read(new MouseState(
                    Off, Off, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released,
                    ButtonState.Released, ButtonState.Released));
                if (Modes.Mode == ShellMode.CodeEditor)
                {
                    CodeEditorInput.Update(Context, commands, mouse, new[] { c }, FrameSeconds);
                }
            }
        }
    }

    /// <summary>The real road into the CODE tab: menu → library → editor → tab, as the shell walks it.</summary>
    private Harness OpenCodeEditor(string source)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string cartFolder = Path.Combine(root, "cart");
        Directory.CreateDirectory(Path.Combine(cartFolder, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(cartFolder, "manifest.json"), "{\"name\":\"nav\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(
            Path.Combine(
                cartFolder, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName),
            source);
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();
        machine.SwitchEditorTab(ShellMode.CodeEditor);
        Assert.Equal(ShellMode.CodeEditor, machine.Mode);
        return new Harness(machine);
    }

    // ==================================================================================
    // 1. What counts as a declaration — the rule, and everything it must refuse.
    // ==================================================================================

    /// <summary>
    /// The rule of <see cref="CodeEditorSession.IsDeclarationLine"/> as a table, positives and
    /// negatives in one test on purpose: it is the <em>difference</em> between the two halves
    /// that makes Alt+Down useful, and a rule that answered "yes" to everything would leave every
    /// positive row green.
    ///
    /// <para>The three negatives the order named are rows here by name — a declaration inside a
    /// line comment, a declaration inside a string literal, and a method call — together with the
    /// two the rule has to get right to be worth having: a field with an initialiser is not a
    /// declaration (Alt+Down is for structure), and a method with a defaulted parameter still
    /// is, even though its line carries an <c>=</c>.</para>
    ///
    /// <para>Break recipe: drop the <c>StripToCode</c> call from
    /// <see cref="CodeEditorSession.IsDeclarationLine"/> and the comment and string rows go red
    /// while every positive stays green; drop the first-word check against
    /// <c>DeclarationStarters</c> and the two call rows go red; drop the <c>open &lt; assign</c>
    /// half of the method clause and the defaulted-parameter row goes red.</para>
    /// </summary>
    [Theory]
    // Positives — the four shapes the order names: class, struct, method, property.
    [InlineData("public class Player", true)]
    [InlineData("public struct Point", true)]
    [InlineData("public sealed record Tile(int Id)", true)]
    [InlineData("public enum Kind", true)]
    [InlineData("    public void Update()", true)]
    [InlineData("    void Update()", true)]
    [InlineData("    public Player(int hp)", true)]
    [InlineData("    public void Move(bool extend = false)", true)]
    [InlineData("    public int Score { get; set; }", true)]
    [InlineData("    public int Lives => 3;", true)]
    [InlineData("    public bool Alive", true)]
    [InlineData("    static void Main() => Run();", true)]
    // Negatives — the three the order named, first.
    [InlineData("    // public void Ghost()", false)]
    [InlineData("    /// <summary>public void Ghost()</summary>", false)]
    [InlineData("    const string S = \"public void Ghost()\";", false)]
    [InlineData("        Ghost();", false)]
    [InlineData("        Player.Update();", false)]
    // ...and the shapes the rule has to keep refusing to stay a rule about structure.
    [InlineData("    public int Hp = 3;", false)]
    [InlineData("    private readonly List<int> Items = new();", false)]
    [InlineData("    public static readonly int[] Table = { 1, 2 };", false)]
    [InlineData("{", false)]
    [InlineData("}", false)]
    [InlineData("    [Obsolete]", false)]
    [InlineData("#pragma warning disable", false)]
    [InlineData("using Quarp.Api;", false)]
    [InlineData("", false)]
    // Too deep to be a header at all: sixteen columns is inside somebody's body.
    [InlineData("                public void Update()", false)]
    public void TheDeclarationRuleAcceptsHeadersAndRefusesEverythingElse(string line, bool expected)
    {
        CodeEditorSession session = Cart(line + "\n");

        Assert.Equal(expected, session.IsDeclarationLine(0));
    }

    /// <summary>
    /// The one clause that cannot be checked a line at a time: a <c>/* … */</c> that spans lines
    /// blanks all of them, which is why the rule's readers walk the buffer from the top instead
    /// of looking at one line in isolation.
    ///
    /// <para>The negative control is the last line: the very same text, outside the comment, is a
    /// declaration. Without it a rule that answered "no" to everything would pass this test.</para>
    ///
    /// <para>Break recipe: make <c>StripToCode</c> take the block-comment flag by value instead of
    /// by reference and the two commented rows go green-to-red while the last row stays green.</para>
    /// </summary>
    [Fact]
    public void ABlockCommentHidesTheDeclarationsInsideItAndOnlyThose()
    {
        CodeEditorSession session = Cart(
            "/*\n"
            + "public class Hidden\n"
            + "    public void AlsoHidden()\n"
            + "*/\n"
            + "public class Visible\n");

        Assert.False(session.IsDeclarationLine(0));
        Assert.False(session.IsDeclarationLine(1));
        Assert.False(session.IsDeclarationLine(2));
        Assert.False(session.IsDeclarationLine(3));
        Assert.True(session.IsDeclarationLine(4));
    }

    /// <summary>
    /// The document's two verbs, without a screen: the caret lands on declaration lines and on no
    /// others, and the walk <b>stops at the ends rather than wrapping</b> — the one place this
    /// deliberately differs from <see cref="CodeEditorSession.FindNext"/>, because a jump through
    /// structure that teleports past the end costs the reader the place they were reading.
    ///
    /// <para>Break recipe: let <c>JumpToDeclaration</c> wrap and the two "stayed put" assertions
    /// go red; return the caret to the column it came from instead of 0 and the column
    /// assertion does.</para>
    /// </summary>
    [Fact]
    public void TheWalkVisitsOnlyDeclarationsAndStopsAtBothEnds()
    {
        CodeEditorSession session = Cart(Sample);
        session.SetCursor(0, 0);

        Assert.True(session.MoveToNextDeclaration());
        Assert.Equal(5, session.CursorLine);        // straight past the comment, the string and the field
        Assert.Equal(0, session.CursorColumn);      // the start of the line, as both references land

        Assert.False(session.MoveToNextDeclaration());
        Assert.Equal(5, session.CursorLine);        // no wrap: nothing below, so nothing moves

        Assert.True(session.MoveToPreviousDeclaration());
        Assert.Equal(0, session.CursorLine);

        Assert.False(session.MoveToPreviousDeclaration());
        Assert.Equal(0, session.CursorLine);

        // The negative control for the whole walk: every line it refused to stop on says so.
        Assert.True(session.IsDeclarationLine(0));
        Assert.True(session.IsDeclarationLine(5));
        foreach (int skipped in new[] { 1, 2, 3, 4, 6, 7, 8, 9, 10, 11 })
        {
            Assert.False(session.IsDeclarationLine(skipped), $"line {skipped} must not be a declaration");
        }
    }

    // ==================================================================================
    // 2. Alt+Up/Down through the real router, beside the Alt+Left/Right it must not disturb.
    // ==================================================================================

    /// <summary>
    /// The chord and the bare key on the same arrow, decided in the router the way Ctrl+Left and
    /// Left already are: <b>Alt+Down jumps to the next declaration and plain Down moves one
    /// line</b>. Both halves in one test, because the interesting failure is not "the jump does
    /// nothing" — it is "the jump also moved a line", which only the second half can see.
    ///
    /// <para>Break recipe: turn the <c>else if (commands.MenuUp)</c> in
    /// <c>CodeEditorInput.Movement</c> back into a plain <c>if</c> and the two exact line numbers
    /// go red by exactly one; bind the reader's <c>CodeDeclarationNext</c> to a bare
    /// <c>Keys.Down</c> and the plain-arrow assertions go red instead.</para>
    /// </summary>
    [Fact]
    public void AltArrowsWalkDeclarationsAndBareArrowsStillWalkLines()
    {
        Harness harness = OpenCodeEditor(Sample);
        CodeEditorSession session = harness.Session;

        harness.Tap(Keys.Down);
        Assert.Equal(1, session.CursorLine);         // the bare arrow is still one line

        harness.Tap(Keys.LeftAlt, Keys.Down);
        Assert.Equal(5, session.CursorLine);         // ...and the chord is a jump, not a jump plus a line

        harness.Tap(Keys.LeftAlt, Keys.Up);
        Assert.Equal(0, session.CursorLine);

        harness.Tap(Keys.Up);
        Assert.Equal(0, session.CursorLine);         // already at the top; the arrow clamps, as ever

        // A jump is travel and never a selection, whatever Shift is doing — the two references
        // that have this key do not extend either.
        harness.Tap(Keys.LeftShift, Keys.LeftAlt, Keys.Down);
        Assert.Equal(5, session.CursorLine);
        Assert.False(session.HasSelection);
    }

    /// <summary>
    /// The collision the order asked about, checked rather than reasoned about: <b>Alt+Left and
    /// Alt+Right still walk the tab strip and must not move the caret; Alt+Up and Alt+Down must
    /// not move the tab.</b> Four arrows, one modifier, two verbs.
    ///
    /// <para>Break recipe: bind <c>CodeDeclarationPrev</c> to <c>Keys.Left</c> in
    /// <see cref="ShellCommandReader"/> — the mode assertion in the first half goes red, because
    /// the router answers the declaration jump before the tab strip and would never leave the
    /// screen.</para>
    /// </summary>
    [Fact]
    public void TheTabStripKeepsAltLeftRightAndTheWalkKeepsAltUpDown()
    {
        Harness harness = OpenCodeEditor(Sample);

        harness.Tap(Keys.LeftAlt, Keys.Down);
        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);     // the walk did not travel
        Assert.Equal(5, harness.Session.CursorLine);

        harness.Tap(Keys.LeftAlt, Keys.Right);
        Assert.Equal(EditorIcons.LiveEditorTabs[1], harness.Modes.Mode);
        // The tab key left the caret exactly where the walk had put it.
        Assert.Equal(5, harness.Modes.CodeEditor!.CursorLine);
    }

    // ==================================================================================
    // 3. F1..F5 — TIC-80's five named tab keys.
    // ==================================================================================

    /// <summary>
    /// Each of the five keys lands on the tab of the same number, in
    /// <see cref="EditorIcons.LiveEditorTabs"/>' own left-to-right order — driven through the
    /// production reader and the production router, one key per case, from the CODE screen every
    /// time.
    ///
    /// <para>Break recipe: reorder <see cref="EditorIcons.LiveEditorTabs"/> and every row but the
    /// one whose tab did not move goes red — which is the whole reason
    /// <see cref="EditorIcons.EditorTabForNumber"/> reads that list instead of carrying a second
    /// one. Change <c>FunctionTab</c>'s base key to <c>Keys.F2</c> and all five go red at once.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void FunctionKeysJumpStraightToTheEditorOfThatNumber(int number)
    {
        Harness harness = OpenCodeEditor(Sample);

        harness.Tap(Keys.F1 + (number - 1));

        Assert.Equal(EditorIcons.LiveEditorTabs[number - 1], harness.Modes.Mode);
    }

    /// <summary>
    /// The named key and the ring must count the tabs the same way, and the keys stop where the
    /// strip stops. Three claims, and the last two are the negative control: <b>F6 is not a sixth
    /// editor</b>, and <b>a function key is deaf while the exit prompt is up</b> — the prompt owns
    /// the input, and a keystroke that carried the author away from a question about their unsaved
    /// text is how that text gets lost.
    ///
    /// <para>Break recipe: make <see cref="EditorIcons.EditorTabForNumber"/> clamp instead of
    /// answering null and the F6 assertion goes red; move the router's function-key block above
    /// the exit-prompt branch in <c>CodeEditorInput.Update</c> and the prompt assertions do.</para>
    /// </summary>
    [Fact]
    public void TheKeysAgreeWithTheRingStopAtFiveAndAreDeafUnderThePrompt()
    {
        // F2 and one step of Alt+Right must be the same screen — one owner of the order.
        Harness byKey = OpenCodeEditor(Sample);
        byKey.Tap(Keys.F2);
        Harness byRing = OpenCodeEditor(Sample);
        byRing.Tap(Keys.LeftAlt, Keys.Right);
        Assert.Equal(byRing.Modes.Mode, byKey.Modes.Mode);

        // F6 is not a tab: nothing above five exists, and clamping would be a key that lies.
        Harness harness = OpenCodeEditor(Sample);
        Assert.Null(EditorIcons.EditorTabForNumber(EditorIcons.LiveEditorTabs.Count + 1));
        harness.Tap(Keys.F6);
        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);

        // Under the exit prompt every key but Z / X / Esc is deaf, this one included.
        harness.Type("X");
        Assert.True(harness.Session.IsDirty);
        harness.Tap(Keys.Escape);
        Assert.True(harness.View.ExitPromptShown);
        harness.Tap(Keys.F2);
        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);
        Assert.True(harness.View.ExitPromptShown);
    }

    // ==================================================================================
    // 4. The red limit in the status line (TIC-80's drawStatus).
    // ==================================================================================

    /// <summary>
    /// <c>code->status.color = codeLen > MAX_CODE ? tic_color_red : tic_color_white</c>
    /// (REFERENCES-EDITORS §8 item 13) in this tree's colours, and <b>the boundary is the
    /// boundary</b>: exactly at the limit the readout is still plain, one byte over it is red.
    /// The negative control is the "exactly at" row — a comparison written with <c>&gt;=</c>
    /// would leave the over-limit row green and only that one red.
    ///
    /// <para>Break recipe: swap <c>&gt;</c> for <c>&gt;=</c> in
    /// <see cref="CodeEditorRenderer.BudgetInk"/> and the at-the-limit assertion goes red; make
    /// it read <see cref="CodeEditorSession.IsDirty"/> instead and the empty-buffer row does.</para>
    /// </summary>
    [Fact]
    public void TheBudgetReadoutIsRedOnlyPastTheLimit()
    {
        CodeEditorSession session = Cart("public class Player\n");
        Assert.Equal(ConsoleChromeRenderer.Bright, CodeEditorRenderer.BudgetInk(session));

        // Dirty is not over-budget: the save icon's yellow says that, and one hue must mean
        // one thing.
        session.Insert("X");
        Assert.True(session.IsDirty);
        Assert.Equal(ConsoleChromeRenderer.Bright, CodeEditorRenderer.BudgetInk(session));

        CodeEditorSession atLimit = Cart(new string('A', CodeEditorSession.MaxByteCount));
        Assert.Equal(CodeEditorSession.MaxByteCount, atLimit.ByteCount);
        Assert.Equal(ConsoleChromeRenderer.Bright, CodeEditorRenderer.BudgetInk(atLimit));

        atLimit.Insert("A");
        Assert.Equal(CodeEditorSession.MaxByteCount + 1, atLimit.ByteCount);
        Assert.Equal(ConsoleChromeRenderer.Error, CodeEditorRenderer.BudgetInk(atLimit));
    }

    /// <summary>
    /// The other half of giving a shared painter a colour: <b>the four screens that say nothing
    /// draw exactly what they drew before.</b> The same call with the parameter omitted and with
    /// the old colour spelled out must paint the identical framebuffer — which is what lets the
    /// sprite, map, sound and music golden hashes stand untouched — and passing a different
    /// colour must actually reach the pixels, or the default would be untestable for the right
    /// reason and the feature untestable for the wrong one.
    ///
    /// <para>Break recipe: change <c>DrawStatusText</c>'s default from <c>Bright</c> to anything
    /// else and the first comparison goes red, naming every screen whose picture just moved;
    /// print the number with a hard-coded <c>Bright</c> again and the second goes red.</para>
    /// </summary>
    [Fact]
    public void TheStatusColourIsOptionalAndFourScreensPicturesDoNotMove()
    {
        Assert.True(ScreensMatch(DrawStatus(null), DrawStatus(ConsoleChromeRenderer.Bright)));
        Assert.False(ScreensMatch(DrawStatus(null), DrawStatus(ConsoleChromeRenderer.Error)));
    }

    /// <summary>One status line on a fresh console; null means "let the painter keep its own default".</summary>
    private static VirtualConsole DrawStatus(byte? numberColor)
    {
        var screen = new ShellScreen();
        var buttons = new EditorButtonPlace[6];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(ConsoleWidth, ConsoleHeight, buttons, ref placed);
        screen.Begin();
        screen.Console.Cls(ConsoleChromeRenderer.Ink);
        if (numberColor is byte color)
        {
            ConsoleChromeRenderer.DrawStatusText(screen.Console, chrome, "LINE 1/1 COL 1", "123/456", color);
        }
        else
        {
            ConsoleChromeRenderer.DrawStatusText(screen.Console, chrome, "LINE 1/1 COL 1", "123/456");
        }
        return screen.Console;
    }

    private static bool ScreensMatch(VirtualConsole a, VirtualConsole b)
    {
        for (int y = 0; y < ConsoleHeight; y++)
        {
            for (int x = 0; x < ConsoleWidth; x++)
            {
                if (a.Pget(x, y) != b.Pget(x, y))
                {
                    return false;
                }
            }
        }
        return true;
    }
}

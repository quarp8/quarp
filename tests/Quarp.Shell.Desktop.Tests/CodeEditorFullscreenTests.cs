using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The fullscreen code page and the clipboard seam under it — the two facts this wave added to
/// the code editor, each pinned where it lives.
///
/// <para><b>The claim, in one sentence.</b> ADR-029 accepted the tightest code screen in the
/// niche (eleven lines by thirty-six columns) and named the mitigation in the same paragraph:
/// "полноэкранный режим без хрома возвращает все 15 строк". This file is what turns that
/// sentence into a number the tree can check — fifteen by forty, six hundred characters, with
/// nothing on the surface but text — and what stops the number from being reached by drawing a
/// page nobody can click on correctly.</para>
///
/// <para><b>Every test here is headless.</b> <see cref="CodeEditorLayout"/> is a pure function of
/// three inputs, <see cref="CodeEditorView"/> is a plain object, <see cref="ShellScreen"/> is the
/// console's framebuffer with no graphics device behind it, and <see cref="CodeEditorInput"/> is
/// driven through the production <see cref="ShellCommandReader"/> and
/// <see cref="EditorMouseReader"/> exactly as <c>CodeEditorScreenTests</c> drives it. No window
/// is constructed anywhere in this file, which is the same proof of layering the rest of the
/// suite runs on: a fact that needs a window is a fact in the wrong layer.</para>
///
/// <para><b>What this file deliberately does not do: pin pixels of the windowed screen.</b> That
/// picture is hashed by <c>CodeEditorScreenGoldenTests</c> and this wave does not move a pixel of
/// it — fullscreen defaults to off and nothing new is drawn until F11 is pressed. The probes
/// below that do read the framebuffer read the <em>fullscreen</em> one, which has no pinned hash
/// yet, and they read the structural facts (is the chrome's rule there, is the fifteenth line
/// painted) rather than a digest.</para>
/// </summary>
public class CodeEditorFullscreenTests : IDisposable
{
    /// <summary>The console — the only surface any editor is laid out on since ADR-029.</summary>
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    /// <summary>One frame at 60 Hz; the router spends it on the tooltip clock only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside any rectangle the layout places — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private readonly string _root;

    public CodeEditorFullscreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-codefull-" + Guid.NewGuid().ToString("N"));
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
            Path.Combine(folder, "manifest.json"), "{\"name\":\"full\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(
            Path.Combine(folder, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName),
            source);
        return new CodeEditorSession(folder);
    }

    /// <summary>Twenty numbered lines — more than either page can hold, so "how many fit" is answerable by looking.</summary>
    private static string TwentyLines()
    {
        var lines = new string[20];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = $"LINE{i:00}";
        }
        return string.Join('\n', lines);
    }

    /// <summary>
    /// The window, minus the window — the same shape <c>CodeEditorScreenTests.Harness</c> uses,
    /// cut down to the one mode this file stands in. It drives the <b>production</b> reader, so
    /// the edge detection that turns "F11 is down" into "F11 was pressed once" is the shell's
    /// own and not a second copy of it.
    /// </summary>
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

        internal void Frame(Keys[] down, string typed, int mouseX, int mouseY, ButtonState left)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, 0, left, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
            if (Modes.Mode == ShellMode.CodeEditor)
            {
                CodeEditorInput.Update(Context, commands, mouse, typed.ToCharArray(), FrameSeconds);
            }
        }

        internal void Idle() => Frame(NoKeys, string.Empty, Off, Off, ButtonState.Released);

        /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
        internal void Tap(params Keys[] down)
        {
            Frame(down, string.Empty, Off, Off, ButtonState.Released);
            Idle();
        }

        internal void Type(string text)
        {
            foreach (char c in text)
            {
                Frame(NoKeys, c.ToString(), Off, Off, ButtonState.Released);
                Idle();
            }
        }

        internal void Click(int x, int y)
        {
            Frame(NoKeys, string.Empty, x, y, ButtonState.Pressed);
            Frame(NoKeys, string.Empty, x, y, ButtonState.Released);
        }
    }

    /// <summary>The real road into the CODE tab: menu → library → editor → tab, as the shell walks it.</summary>
    private Harness OpenCodeEditor(string source)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string cartFolder = Path.Combine(root, "cart");
        Directory.CreateDirectory(Path.Combine(cartFolder, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(cartFolder, "manifest.json"), "{\"name\":\"full\",\"author\":\"\",\"profile\":8}");
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

    /// <summary>True when any pixel in the half-open row band carries the given master index.</summary>
    private static bool BandHas(VirtualConsole console, int fromRow, int toRow, byte color)
    {
        for (int y = fromRow; y < toRow; y++)
        {
            for (int x = 0; x < ConsoleWidth; x++)
            {
                if (console.Pget(x, y) == color)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // ==================================================================================
    // 1. The arithmetic ADR-029 promised.
    // ==================================================================================

    /// <summary>
    /// <b>The headline number, with its own negative control standing beside it.</b> The same
    /// <see cref="CodeEditorLayout.Compute"/>, on the same 160x90 console, gives eleven lines by
    /// thirty-six columns with the chrome up and fifteen by forty with it down — 396 characters
    /// against 600. Both halves are asserted in one test on purpose: a fullscreen page that
    /// silently equalled the windowed one, or a windowed page that had quietly grown, would each
    /// leave the other assertion green, and it is the <em>difference</em> that ADR-029 promised.
    ///
    /// <para>The divisions are checked as divisions, not as remembered constants: 90/6 and 160/4
    /// are exact, which is the reason the mode is worth having at all and the reason the
    /// scrollbar had to go (156/4 is 39, and losing the fortieth column would have thrown away
    /// the one measurement on which we beat PICO-8).</para>
    ///
    /// <para>Break recipe: leave the scrollbar in <c>CodeEditorLayout.Full</c> — take
    /// <c>screenWidth</c> down to <c>screenWidth - ScrollBarWidth - ScrollBarGap</c> — and the
    /// column count drops to 39 while every other assertion here stays green, naming the defect
    /// exactly.</para>
    /// </summary>
    [Fact]
    public void FullscreenIsFifteenLinesByFortyColumnsAndTheChromedPageIsElevenByThirtySix()
    {
        var windowed = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        var full = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight, fullscreen: true);

        Assert.False(windowed.Fullscreen);
        Assert.Equal(11, windowed.VisibleLines);
        Assert.Equal(36, windowed.VisibleColumns);

        Assert.True(full.Fullscreen);
        Assert.Equal(15, full.VisibleLines);
        Assert.Equal(40, full.VisibleColumns);
        Assert.Equal(600, full.VisibleLines * full.VisibleColumns);

        // Exact on both axes — the whole reason the mode returns round numbers.
        Assert.Equal(ConsoleHeight / SystemFont.CellHeight, full.VisibleLines);
        Assert.Equal(ConsoleWidth / SystemFont.CellWidth, full.VisibleColumns);
        Assert.Equal(new Rectangle(0, 0, 160, 90), full.Text);
        Assert.Equal(Rectangle.Empty, full.ScrollBar);
    }

    /// <summary>
    /// The summoned status row costs exactly one line and lands on the rows the chrome already
    /// uses for the same readout, so the numbers do not jump when the author peeks at them.
    ///
    /// <para>Break recipe: measure the band from <c>ScreenHeight - SystemFont.CellHeight</c>
    /// instead of from <see cref="ConsoleChrome.StatusRuleY"/> — both are six rows off the
    /// bottom on a 90-row console, so the line count stays fourteen and only the
    /// <c>StatusTextY</c> assertion goes red, which is the assertion that says the readout did
    /// not move.</para>
    /// </summary>
    [Fact]
    public void TheSummonedStatusRowCostsOneLineAndSitsWhereTheChromePutsIt()
    {
        var bare = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight, fullscreen: true);
        var withBand = CodeEditorLayout.Compute(
            ConsoleWidth, ConsoleHeight, fullscreen: true, statusBand: true);
        var windowed = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);

        Assert.Equal(15, bare.VisibleLines);
        Assert.Equal(14, withBand.VisibleLines);
        Assert.Equal(40, withBand.VisibleColumns);
        Assert.Equal(new Rectangle(0, 0, 160, 84), withBand.Text);
        // Same row as the chrome's own status text: 85 on a 90-row console.
        Assert.Equal(windowed.StatusTextY, withBand.StatusTextY);
        Assert.Equal(85, withBand.StatusTextY);
    }

    // ==================================================================================
    // 2. The band is summoned, never standing.
    // ==================================================================================

    /// <summary>
    /// <b>The rule the order asked for, and its negative control.</b> In fullscreen the bottom
    /// row exists only while something has called for it — the peek, a find line, a go-to line,
    /// or a buffer over the byte budget — and it is <em>absent</em> in the plain case, which is
    /// the assertion that keeps the page fifteen lines. Outside fullscreen it is always false,
    /// because there the chrome owns a status band that is always there and this flag would be a
    /// second owner of the same fact.
    ///
    /// <para>The behaviour is LIKO-12's: its code editor's status strip is replaced by
    /// <c>ISRCH: &lt;текст&gt;</c> while incremental search is on (REFERENCES-EDITORS §4.2) —
    /// one strip, whoever called for it.</para>
    ///
    /// <para>Break recipe: drop the <c>StatusPeek ||</c> from
    /// <see cref="CodeEditorView.StatusBandShown"/> and the peek case goes red on its own; make
    /// the method return <c>Fullscreen</c> alone and the very first assertion — the one that says
    /// the default page keeps its fifteenth line — goes red instead. Each half names a different
    /// mistake.</para>
    /// </summary>
    [Fact]
    public void TheFullscreenStatusRowIsAbsentUntilSomethingCallsForIt()
    {
        CodeEditorSession session = Cart("PRINT();\n");
        var view = new CodeEditorView();

        // Windowed: never, because the chrome always carries one.
        Assert.False(view.StatusBandShown(session));
        view.OpenFind();
        Assert.False(view.StatusBandShown(session));
        view.CloseFind();

        view.ToggleFullscreen();
        Assert.True(view.Fullscreen);
        Assert.False(view.StatusBandShown(session));     // the default page is fifteen lines

        view.ToggleStatusPeek();
        Assert.True(view.StatusBandShown(session));
        view.ToggleStatusPeek();
        Assert.False(view.StatusBandShown(session));

        // A field is its own summons — it IS the band's tenant while it lives.
        view.OpenFind();
        Assert.True(view.StatusBandShown(session));
        view.CloseFind();
        Assert.False(view.StatusBandShown(session));

        view.OpenGoTo();
        Assert.True(view.StatusBandShown(session));
        view.CloseGoTo();
        Assert.False(view.StatusBandShown(session));
    }

    /// <summary>
    /// The peek never survives the mode: leaving fullscreen puts it out, so a page re-entered
    /// later opens on all fifteen lines rather than on whatever the author once asked to see.
    /// That is the whole of "показывается только пока её вызвали, а не постоянно".
    ///
    /// <para>Break recipe: delete the <c>StatusPeek = false</c> from
    /// <see cref="CodeEditorView.ToggleFullscreen"/>'s leaving branch — the first two assertions
    /// stay green and the last one goes red, which is precisely the "it quietly became
    /// permanent" defect.</para>
    /// </summary>
    [Fact]
    public void ThePeekDoesNotSurviveLeavingFullscreen()
    {
        CodeEditorSession session = Cart("PRINT();\n");
        var view = new CodeEditorView();

        view.ToggleFullscreen();
        view.ToggleStatusPeek();
        Assert.True(view.StatusBandShown(session));

        view.ToggleFullscreen();                        // back to the chrome
        Assert.False(view.Fullscreen);
        Assert.False(view.StatusPeek);

        view.ToggleFullscreen();                        // and in again
        Assert.False(view.StatusBandShown(session));
    }

    // ==================================================================================
    // 3. Nothing to click, and nothing that pretends to be clickable.
    // ==================================================================================

    /// <summary>
    /// Fullscreen places no button and no scrollbar, and the hit tests agree with the picture
    /// everywhere on the surface — swept, not spot-checked, because a rectangle left behind by a
    /// mode that no longer draws it is exactly the kind of ghost a spot check misses.
    ///
    /// <para>Break recipe: hand <c>Full</c> the same populated <c>buttons</c> array the windowed
    /// branch builds instead of <see cref="Array.Empty{T}"/> — the sweep goes red at the first
    /// pixel of the exit tab, at (0,0), while every arithmetic assertion above stays green.</para>
    /// </summary>
    [Fact]
    public void FullscreenPlacesNothingClickableAndSaysSoAtEveryPixel()
    {
        var full = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight, fullscreen: true);
        var windowed = CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight);

        Assert.Empty(full.Buttons);
        Assert.NotEmpty(windowed.Buttons);              // the negative control: the sweep can fail

        for (int y = 0; y < ConsoleHeight; y++)
        {
            for (int x = 0; x < ConsoleWidth; x++)
            {
                Assert.False(full.TryButton(x, y, out _));
                Assert.False(full.TryScrollBarLine(x, y, 500, out _));
                // Every pixel is text, and it maps to a cell inside the page.
                Assert.True(full.TryTextCell(x, y, 0, 0, out int line, out int column));
                Assert.InRange(line, 0, full.VisibleLines - 1);
                Assert.InRange(column, 0, full.VisibleColumns - 1);
            }
        }
    }

    // ==================================================================================
    // 4. The key, through the production reader.
    // ==================================================================================

    /// <summary>
    /// <b>F11 through the real router.</b> The key is TIC-80's own for this verb
    /// (REFERENCES-EDITORS §1, <c>processShortcuts</c>: "F11 фуллскрин"); PICO-8's TAB, which
    /// ADR-029 cites, could not be taken because on the code screen TAB is the indent key in
    /// every reference including PICO-8 itself (§4.3) and in this shell
    /// (<c>ShellCommands.EditorRegionCycle</c> → <c>InsertTab</c>).
    ///
    /// <para>The last two assertions are the negative control and they are the point of driving
    /// the production reader rather than calling the view: <b>Tab must still indent</b> and must
    /// not toggle anything. A wave that had wired the mode to TAB would pass every geometric test
    /// in this file and go red here, on the buffer's own text.</para>
    ///
    /// <para>Break recipe: change the reader's <c>CodeFullscreen</c> to <c>Keys.Tab</c> — the
    /// first three assertions still pass and the indent assertion goes red with a line that never
    /// got its four spaces.</para>
    /// </summary>
    [Fact]
    public void F11TogglesTheModeAndTabStillIndents()
    {
        Harness harness = OpenCodeEditor("AB\n");
        Assert.False(harness.View.Fullscreen);

        harness.Tap(Keys.F11);
        Assert.True(harness.View.Fullscreen);

        harness.Tap(Keys.F11);
        Assert.False(harness.View.Fullscreen);

        // Tab is the indent key here and nothing else: the caret sits at the start of line 1.
        harness.Tap(Keys.Tab);
        Assert.False(harness.View.Fullscreen);
        Assert.Equal(new string(' ', CodeEditorSession.TabWidth) + "AB", harness.Session.Lines[0]);
    }

    /// <summary>
    /// Shift+F11 summons the row and only inside fullscreen — PICO-8's own two-step
    /// (<c>TAB</c> / <c>SHIFT-TAB</c>, REFERENCES-EDITORS §2.3) read so that the bare key gives
    /// the bar-less page, because the fifteenth line is what the mode exists for.
    ///
    /// <para>Break recipe: drop the <c>Fullscreen</c> guard inside
    /// <see cref="CodeEditorView.ToggleStatusPeek"/> and the first assertion — that the chord
    /// does nothing while the chrome is up — goes red.</para>
    /// </summary>
    [Fact]
    public void ShiftF11SummonsTheRowAndOnlyInsideFullscreen()
    {
        Harness harness = OpenCodeEditor("AB\n");

        harness.Tap(Keys.LeftShift, Keys.F11);
        Assert.False(harness.View.Fullscreen);
        Assert.False(harness.View.StatusPeek);

        harness.Tap(Keys.F11);
        harness.Tap(Keys.LeftShift, Keys.F11);
        Assert.True(harness.View.Fullscreen);
        Assert.True(harness.View.StatusPeek);
        Assert.True(harness.View.StatusBandShown(harness.Session));

        harness.Tap(Keys.LeftShift, Keys.F11);
        Assert.False(harness.View.StatusPeek);
    }

    // ==================================================================================
    // 5. The exit question can never be asked where it cannot be seen.
    // ==================================================================================

    /// <summary>
    /// <b>The rung this mode had to add to Esc.</b> The exit prompt is drawn on the chrome's
    /// message line and answered with three clickable verbs, none of which is on a fullscreen
    /// surface — so Esc spends itself on the chrome first, and only the <em>next</em> Esc asks
    /// about unsaved text. Without the rung a dirty buffer in fullscreen would raise an
    /// unanswerable question.
    ///
    /// <para>Break recipe: delete the <c>view.LeaveFullscreen()</c> rung from
    /// <c>CodeEditorInput.EditKeys</c> — the prompt goes up on the first Esc while the screen is
    /// still fullscreen, so the middle assertion (no prompt yet) goes red and names it.</para>
    /// </summary>
    [Fact]
    public void EscapeGivesTheChromeBackBeforeItEverAsksAboutUnsavedText()
    {
        Harness harness = OpenCodeEditor("AB\n");
        harness.Tap(Keys.F11);
        harness.Type("X");                              // now dirty, and fullscreen
        Assert.True(harness.Session.IsDirty);
        Assert.True(harness.View.Fullscreen);

        harness.Tap(Keys.Escape);
        Assert.Equal(ShellMode.CodeEditor, harness.Modes.Mode);
        Assert.False(harness.View.Fullscreen);
        Assert.False(harness.View.ExitPromptShown);     // the chrome came back, the question did not

        harness.Tap(Keys.Escape);
        Assert.True(harness.View.ExitPromptShown);      // and now it is asked, where it can be seen
    }

    /// <summary>
    /// The other road to the same prompt: the mode machine raises it from OUTSIDE this screen
    /// when the author tries to leave the editor with unsaved code on a tab they are not standing
    /// on. Esc's rung cannot help there, so the guard lives in
    /// <see cref="CodeEditorView.RequestClose"/> itself — the one place that raises the prompt.
    ///
    /// <para>Break recipe: remove the <c>Fullscreen = false</c> from that method and this goes
    /// red with a prompt up on a surface that draws no message line.</para>
    /// </summary>
    [Fact]
    public void RaisingTheExitPromptPutsTheChromeBackWhoeverRaisedIt()
    {
        CodeEditorSession session = Cart("AB\n");
        var view = new CodeEditorView();
        view.ToggleFullscreen();
        view.ToggleStatusPeek();
        session.Insert("X");
        Assert.True(session.IsDirty);

        Assert.False(view.RequestClose(session));
        Assert.True(view.ExitPromptShown);
        Assert.False(view.Fullscreen);
        Assert.False(view.StatusPeek);
    }

    // ==================================================================================
    // 6. The picture.
    // ==================================================================================

    /// <summary>
    /// The fullscreen frame, read off the console's own framebuffer: none of the chrome's three
    /// rules is on it, the caret is at the very first pixel, and the fifteenth line is painted —
    /// which is the pixel-level version of "fifteen lines fit".
    ///
    /// <para>The probes use column 159 on purpose. Character cells are four pixels wide and the
    /// glyphs are three, so column 39's fourth pixel is the inter-character gap and can never
    /// carry ink from text — while the chrome's rules span the whole width. That makes
    /// <c>Pget(159, y)</c> a clean question about the chrome alone.</para>
    ///
    /// <para>Break recipe: call <c>DrawBands</c> before the fullscreen early return in
    /// <c>CodeEditorRenderer.Draw</c> — the three rule assertions go red at once while the
    /// fifteenth-line assertion stays green.</para>
    /// </summary>
    [Fact]
    public void TheFullscreenFrameHasNoChromeAndPaintsTheFifteenthLine()
    {
        CodeEditorSession session = Cart(TwentyLines());
        var view = new CodeEditorView();
        view.ToggleFullscreen();
        var screen = new ShellScreen();

        CodeEditorLayout layout = CodeEditorRenderer.Draw(screen, session, view, null, false, 0.0);
        VirtualConsole console = screen.Console;

        Assert.True(layout.Fullscreen);
        Assert.Equal(15, layout.VisibleLines);
        // The chrome's header rule (10), footer rule (78) and status rule (84) are all gone.
        Assert.Equal((byte)0, console.Pget(159, 10));
        Assert.Equal((byte)0, console.Pget(159, 78));
        Assert.Equal((byte)0, console.Pget(159, 84));
        // The caret is on the page's very first pixel — the page starts at the screen's corner.
        Assert.Equal(new Rectangle(0, 0, 4, 6), layout.CellRect(0, 0, 0, 0));
        Assert.Equal((byte)3, console.Pget(0, 0));
        // Row fourteen — the fifteenth line — carries glyphs.
        Assert.True(BandHas(console, 84, 90, ConsoleChromeRenderer.Text));
    }

    /// <summary>
    /// The same frame with the row summoned: the rule is back on row 84, the readout is on row
    /// 85, and the page has given back exactly one line.
    ///
    /// <para>Break recipe: draw the band without its rule and the first assertion goes red —
    /// which is the case where the last line of code and the readout touch and neither can be
    /// read.</para>
    /// </summary>
    [Fact]
    public void TheSummonedRowDrawsItsRuleAndItsReadout()
    {
        CodeEditorSession session = Cart(TwentyLines());
        var view = new CodeEditorView();
        view.ToggleFullscreen();
        view.ToggleStatusPeek();
        var screen = new ShellScreen();

        CodeEditorLayout layout = CodeEditorRenderer.Draw(screen, session, view, null, false, 0.0);
        VirtualConsole console = screen.Console;

        Assert.Equal(14, layout.VisibleLines);
        Assert.Equal((byte)1, console.Pget(159, 84));   // the rule spans the whole width
        Assert.Equal((byte)0, console.Pget(159, 10));   // and the chrome's other two are still gone
        Assert.Equal((byte)0, console.Pget(159, 78));
        // "LINE 1/20 COL 1" starts at the one-pixel margin of row 85.
        Assert.Equal("LINE 1/20 COL 1", CodeEditorRenderer.Coordinates(session));
        Assert.True(BandHas(console, 85, 90, ConsoleChromeRenderer.Text));
        Assert.True(BandHas(console, 85, 90, ConsoleChromeRenderer.Bright));
    }

    /// <summary>
    /// A find line opened in fullscreen reaches the surface: it summons the row and it is the
    /// row's tenant, so the search term the author is typing is visible without the chrome. This
    /// is §8 item 14's "ввод с экрана" holding in the mode that has no message line.
    ///
    /// <para>Break recipe: drop the <c>FieldShown</c> term from
    /// <see cref="CodeEditorView.StatusBandShown"/> — the layout stays fifteen lines, the band is
    /// never drawn, and the author types a search term into a screen that shows nothing; the
    /// first assertion goes red.</para>
    /// </summary>
    [Fact]
    public void AFindLineOpenedInFullscreenIsVisibleOnTheSummonedRow()
    {
        Harness harness = OpenCodeEditor(TwentyLines());
        harness.Tap(Keys.F11);
        harness.Tap(Keys.LeftControl, Keys.F);
        harness.Type("LINE07");

        Assert.True(harness.View.FindShown);
        Assert.True(harness.View.StatusBandShown(harness.Session));
        Assert.Equal("FIND: LINE07", CodeEditorRenderer.StandingNotice(harness.Session, harness.View));

        var screen = new ShellScreen();
        CodeEditorLayout layout =
            CodeEditorRenderer.Draw(screen, harness.Session, harness.View, null, false, 0.0);
        Assert.True(layout.Fullscreen);
        Assert.Equal(14, layout.VisibleLines);
        // The notice is printed in the warning ink, which nothing else on this surface uses.
        Assert.True(BandHas(screen.Console, 85, 90, ConsoleChromeRenderer.Warn));

        // And Enter still walks to the occurrence, fullscreen or not.
        harness.Tap(Keys.Enter);
        Assert.Equal(7, harness.Session.CursorLine);
    }

    /// <summary>
    /// The go-to field is the same tenant of the same row, and Enter jumps — §8 item 14's other
    /// half, on a surface with no message line. The field's keys are PICO-8's
    /// (<c>CTRL-L</c> "to jump to a line number", REFERENCES-EDITORS §4.3), which is also why
    /// find-next is Ctrl+G rather than TIC-80's Ctrl+G-means-goto.
    ///
    /// <para>Break recipe: make <see cref="CodeEditorView.OpenGoTo"/> leave
    /// <c>FindShown</c> alone instead of closing it — both fields then claim the one row, and the
    /// notice assertion goes red because the find text wins the precedence in
    /// <see cref="CodeEditorRenderer.StandingNotice"/>.</para>
    /// </summary>
    [Fact]
    public void AGoToFieldIsTheSameTenantOfTheSameRowAndEnterJumps()
    {
        Harness harness = OpenCodeEditor(TwentyLines());
        harness.Tap(Keys.F11);
        harness.Tap(Keys.LeftControl, Keys.F);          // the other field first, to prove it yields
        harness.Tap(Keys.LeftControl, Keys.L);
        harness.Type("12");

        Assert.False(harness.View.FindShown);
        Assert.True(harness.View.GoToShown);
        Assert.True(harness.View.StatusBandShown(harness.Session));
        Assert.Equal(
            "GO TO LINE: 12", CodeEditorRenderer.StandingNotice(harness.Session, harness.View));

        harness.Tap(Keys.Enter);
        Assert.False(harness.View.GoToShown);
        Assert.Equal(11, harness.Session.CursorLine);   // GoToLine is 1-based; line 12 is index 11
        // The field is gone, so the row goes with it — the whole page is back.
        Assert.False(harness.View.StatusBandShown(harness.Session));
    }

    // ==================================================================================
    // 7. The clipboard seam.
    // ==================================================================================

    /// <summary>Records what the editor handed the host, so the seam can be watched from outside.</summary>
    private sealed class SpyClipboard : ITextClipboard
    {
        internal string Held { get; set; } = string.Empty;

        internal int Writes { get; private set; }

        internal int Reads { get; private set; }

        public string Read()
        {
            Reads++;
            return Held;
        }

        public void Write(string text)
        {
            Writes++;
            Held = text ?? string.Empty;
        }
    }

    /// <summary>
    /// <b>The seam, proved from both ends.</b> Ctrl+X puts the cut text into whatever
    /// <see cref="ITextClipboard"/> the view was constructed with — not into a private field of
    /// its own — and Ctrl+V takes back whatever that object holds, <em>including text this editor
    /// never cut</em>. The second half is what makes the interface a system clipboard seam rather
    /// than an internal buffer with an interface painted on it: the paste below is of a string
    /// planted from outside, exactly as the machine's clipboard plants one when the author copies
    /// in a browser.
    ///
    /// <para>Break recipe: make <see cref="CodeEditorView"/> ignore its constructor argument and
    /// always build an <see cref="InMemoryTextClipboard"/> — the cut assertions still pass
    /// (the text goes somewhere) and the outside-planted paste goes red, which is precisely the
    /// difference between the two kinds of clipboard.</para>
    /// </summary>
    [Fact]
    public void TheEditorCutsIntoAndPastesOutOfTheClipboardItWasGiven()
    {
        CodeEditorSession session = Cart("ALPHA\nBETA\n");
        var spy = new SpyClipboard();
        var view = new CodeEditorView(spy);

        session.SelectAll();
        view.Cut(session);
        Assert.Equal(1, spy.Writes);
        Assert.Equal("ALPHA\nBETA\n", spy.Held);
        Assert.Equal(string.Empty, session.Text);

        // Planted from outside, as the system clipboard does when another program copies.
        spy.Held = "FROM ANOTHER PROGRAM";
        view.Paste(session);
        Assert.True(spy.Reads > 0);
        Assert.Equal("FROM ANOTHER PROGRAM", session.Text);
    }

    /// <summary>
    /// Ctrl+X, Ctrl+C and Ctrl+V through the production reader and router, so the chords
    /// themselves are pinned and not only the view's three methods. PICO-8's own trio ("CTRL-X,
    /// C, V to cut copy or paste selected", REFERENCES-EDITORS §4.3), which TIC-80 and LIKO-12
    /// spell identically.
    ///
    /// <para>Break recipe: drop the <c>ctrl &amp;&amp;</c> guard from <c>CodeCut</c> in
    /// <see cref="ShellCommandReader"/> — a bare X would then cut, and the last assertion (the
    /// typed letter reached the buffer) goes red while the chord assertions stay green.</para>
    /// </summary>
    [Fact]
    public void TheThreeChordsReachTheClipboardAndABareLetterStillTypes()
    {
        Harness harness = OpenCodeEditor("ALPHA\n");
        harness.Tap(Keys.LeftControl, Keys.A);          // select all
        harness.Tap(Keys.LeftControl, Keys.C);
        Assert.Equal("ALPHA\n", harness.View.ClipboardText);

        harness.Tap(Keys.LeftControl, Keys.A);
        harness.Tap(Keys.LeftControl, Keys.X);
        Assert.Equal(string.Empty, harness.Session.Text);

        harness.Tap(Keys.LeftControl, Keys.V);
        Assert.Equal("ALPHA\n", harness.Session.Text);

        // The negative control: X and V are letters again the moment Ctrl is up. The caret sits
        // where the paste left it — at the end — so the letter lands there and nowhere else.
        harness.Type("X");
        Assert.Equal("ALPHA\nX", harness.Session.Text);
    }

    /// <summary>
    /// <b>The host clipboard, and the honest half of it.</b>
    /// <see cref="SystemTextClipboard"/> binds SDL2 by hand rather than by <c>DllImport</c>
    /// precisely so that a host without it — this test process, first of all — gets a working
    /// editor instead of a <c>DllNotFoundException</c> at the first Ctrl+C. Two claims are pinned
    /// here and they hold on every host: the constructor never throws and reports its own state
    /// truthfully (<see cref="SystemTextClipboard.Degraded"/> is exactly "not available" before
    /// anything has been written), and a write followed by a read returns the same text whichever
    /// road it took — through SDL when a window is up, through the in-process buffer when SDL is
    /// absent or refuses (it refuses when the video subsystem is not initialised, which is the
    /// case in every test process).
    ///
    /// <para>Break recipe: delete the <c>_fallback.Write(value)</c> from the degraded branch of
    /// <see cref="SystemTextClipboard.Write"/> and the round trip goes red on any host without an
    /// initialised SDL video subsystem — which is every host this suite runs on.</para>
    /// </summary>
    [Fact]
    public void TheSystemClipboardNeverThrowsAndAlwaysRoundTripsItsOwnText()
    {
        var clipboard = new SystemTextClipboard();
        Assert.Equal(!clipboard.Available, clipboard.Degraded);

        clipboard.Write("QUARP CODE");
        Assert.Equal("QUARP CODE", clipboard.Read());

        clipboard.Write(string.Empty);
        Assert.Equal(string.Empty, clipboard.Read());
    }
}

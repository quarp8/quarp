using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>M9 stage 5, the whole of it: play, pause, edit, continue on the same tick.</b>
///
/// <para>The project has been able to do this since M2 — <c>TimeMachine.Rebuild</c> applies a
/// recorded input log to freshly compiled code and lands on the tick the player was standing on
/// (ADR-006, ADR-007), and <see cref="ContinuationReloadTests"/> has proved it since. What did
/// not exist was a way to <em>reach</em> it: the only key out of a running game called
/// <c>Session.Dispose()</c>, and the editors could be opened only from the library, which is to
/// say only after that Dispose had thrown the input log and the time machine away. The console's
/// headline feature had no keypress. This file is the keypress.</para>
///
/// <para><b>Everything below is the production path.</b> A cartridge folder on disk, a Roslyn
/// compile, a collectible load context, the real <see cref="CartWatcher"/> and its 150 ms
/// debounce, the real <see cref="ShellModeMachine"/>, the real routers behind the real
/// <see cref="ShellCommandReader"/>. The only thing missing compared to <c>quarp.exe</c> is the
/// window — <see cref="Frames"/> is this suite's usual mirror of the one dispatch
/// <c>QuarpGame.Update</c> owns and cannot expose, and every verb it calls is public production
/// code.</para>
///
/// <para><b>These tests compile cartridges and wait on a file-system watcher, so they cost
/// seconds rather than milliseconds</b> — the same trade <see cref="ContinuationReloadTests"/>
/// makes, and for the same reason: this is the one claim the stage is named after, and a fake
/// trigger would prove nothing about the road an author actually walks.</para>
/// </summary>
public class PauseAndContinueTests : IDisposable
{
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside every rectangle the layouts place — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    /// <summary>Ticks a shell frame spends at normal speed, the way <see cref="TickAccumulator"/> hands them out.</summary>
    private const int TicksPerFrame = 1;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    /// <summary>
    /// A cartridge whose picture at tick N exists only if every tick before it ran: <c>_x</c>
    /// walks and <c>_step</c> counts, and <c>Init</c> reseeds both — a rewind reuses the same
    /// instance and nothing zeroes a field it does not assign. <c>{COLOR}</c> stands in for the
    /// one thing an author edits mid-game in this story.
    /// </summary>
    private const string CartSource = """
        using Quarp.Api;

        public sealed class PauseCart : Cartridge
        {
            private int _x;
            private int _step;

            public override void Init()
            {
                _x = 4;
                _step = 0;
            }

            public override void Update()
            {
                _step++;
                _x = (_x + 1) % 110;
            }

            public override void Draw()
            {
                Cls(0);
                RectFill(_x, 20, 8, 8, {COLOR});
                Pset(_step % 128, 60, 3);
            }
        }
        """;

    /// <summary>
    /// The <b>second</b> cartridge on the shelf, and the reason it exists: every claim of the
    /// shape "the editor is open on the cartridge that is running" is vacuous on a shelf with one
    /// cart on it. That is exactly how this shell came to open cart A's code over cart B's game
    /// with a green suite standing behind it — the fixture could not tell the two apart because
    /// there was only ever one. Its class name is the marker the CODE screen is read for.
    /// </summary>
    private const string SecondCartSource = """
        using Quarp.Api;

        public sealed class SecondCart : Cartridge
        {
            public override void Draw()
            {
                Cls(1);
                RectFill(10, 10, 4, 4, 9);
            }
        }
        """;

    private readonly string _root;
    private readonly string _cart;
    private readonly string _second;

    public PauseAndContinueTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-pause-" + Guid.NewGuid().ToString("N"));
        _cart = Path.Combine(_root, "cart");
        Directory.CreateDirectory(Path.Combine(_cart, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(_cart, "manifest.json"), "{\"name\":\"pause\",\"author\":\"\",\"profile\":8}");
        WriteCart(7);

        // "second" sorts after "pause" by name, which is the library's own order, so the shelf's
        // first cart — the one every other test here launches — does not move by this addition.
        _second = Path.Combine(_root, "second");
        Directory.CreateDirectory(Path.Combine(_second, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(_second, "manifest.json"), "{\"name\":\"second\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(SourcePathOf(_second), SecondCartSource);
    }

    /// <summary>Where a cart folder keeps the one file the CODE screen edits.</summary>
    private static string SourcePathOf(string cart) =>
        Path.Combine(cart, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteCart(byte color) => File.WriteAllText(SourcePathOf(_cart), Source(color));

    private static string Source(byte color) =>
        CartSource.Replace("{COLOR}", color.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The frame a straight run of that exact source reaches at that tick — the answer every claim here is measured against.</summary>
    private static byte[] ReferenceFrame(byte color, int ticks)
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", Source(color)) }, "reference");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        using CartHost host = CartHost.Load(result.AssemblyBytes);

        var machine = new TimeMachine(
            ConsoleProfile.Profile8,
            host.Cartridge,
            new ReplayHeader(CartIdentity.Unknown, seed: 0, ReadOnlySpan<int>.Empty),
            new ReplayLog());
        machine.Boot();
        machine.Advance(ticks, default);
        return machine.Framebuffer.Pixels.ToArray();
    }

    /// <summary>A machine standing on a running game, reached the road an author walks: menu → library → launch.</summary>
    private ShellModeMachine Playing()
    {
        var machine = new ShellModeMachine(
            new CartLibrary(_root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        Assert.NotNull(machine.LaunchSelected());
        Assert.Equal(ShellMode.Game, machine.Mode);
        return machine;
    }

    /// <summary>A machine standing in the CODE editor with no game behind it — the library's X, then F2.</summary>
    private ShellModeMachine EditingWithNoGame()
    {
        var machine = new ShellModeMachine(
            new CartLibrary(_root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();
        machine.SwitchEditorTab(ShellMode.CodeEditor);
        Assert.Equal(ShellMode.CodeEditor, machine.Mode);
        Assert.Null(machine.Session);
        return machine;
    }

    /// <summary>
    /// One frame of the shell, minus the window: the reload poll the machine owns, then the
    /// router of whichever screen is on show. It mirrors <c>QuarpGame.Update</c>'s switch — the
    /// one dispatch in this shell that cannot be driven headless, because that class needs a
    /// graphics device to construct — and every verb it calls is a router's public entry point.
    /// The tick budget is the same shape the window's is: nothing while the session is paused,
    /// <see cref="TicksPerFrame"/> otherwise, which is what makes a missing pause show up here as
    /// a tick that ran away.
    /// </summary>
    private static void Frame(
        ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer,
        Keys[] down, int mouseX = Off, int mouseY = Off, ButtonState left = ButtonState.Released)
    {
        modes.PollSessionReload();
        ShellCommands commands = keys.Read(new KeyboardState(down));
        EditorMouse mouse = pointer.Read(new MouseState(
            mouseX, mouseY, 0, left, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released));
        var shell = new EditorShell(
            modes, new ToolbarFlyout(), new IconHoverTracker(), new SheetScroll(),
            ConsoleWidth, ConsoleHeight);
        switch (modes.Mode)
        {
            case ShellMode.Game:
                GameScreenInput.Update(shell, commands, mouse, FrameSeconds);
                if (modes.Mode == ShellMode.Game && modes.Session is CartSession session)
                {
                    session.Update(session.IsPaused ? 0 : TicksPerFrame, default, rewinding: false);
                }
                break;
            case ShellMode.Editor:
                SpriteEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
            case ShellMode.MapEditor:
                MapEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
            case ShellMode.CodeEditor:
                CodeEditorInput.Update(shell, commands, mouse, Array.Empty<char>(), FrameSeconds);
                break;
            case ShellMode.SfxEditor:
                SfxEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
            case ShellMode.MusicEditor:
                MusicEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
        }
    }

    /// <summary>N idle frames of the shell.</summary>
    private static void Frames(ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Frame(modes, keys, pointer, NoKeys);
        }
    }

    /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
    private static void Tap(ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, params Keys[] down)
    {
        Frame(modes, keys, pointer, down);
        Frame(modes, keys, pointer, NoKeys);
    }

    /// <summary>
    /// Idles the shell until the debounced watcher has fired and the rebuild it asked for has
    /// finished. Wall clock rather than a signal, because the thing under test is the real
    /// FileSystemWatcher road a save actually travels; three seconds is roughly twenty debounce
    /// windows plus a warm Roslyn compile, and a rebuild that has not landed by then is a failure
    /// worth reporting as one — which the assertions after the call do, by name.
    /// </summary>
    private static void PumpUntilRebuilt(
        ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, byte[] expected)
    {
        for (int frame = 0; frame < 300; frame++)
        {
            Frame(modes, keys, pointer, NoKeys);
            if (modes.Session!.Framebuffer.Pixels.SequenceEqual(expected))
            {
                return;
            }
            Thread.Sleep(10);
        }
    }

    /// <summary>Puts the pause menu's cursor on a named row, the way Up/Down would.</summary>
    private static void SelectPauseMenuItem(ShellModeMachine modes, PauseMenuItem item)
    {
        int index = modes.PauseMenu.Items.ToList().IndexOf(item);
        Assert.True(index >= 0, $"{item} is not on the menu this state offers");
        modes.PauseMenu.Select(index);
    }

    /// <summary>Puts the pause menu's cursor on a named row, the way Up/Down would, and chooses it.</summary>
    private static void ChoosePauseMenuItem(ShellModeMachine modes, PauseMenuItem item)
    {
        SelectPauseMenuItem(modes, item);
        modes.ActivatePauseMenuItem();
    }

    /// <summary>
    /// Taps a scrub arrow <paramref name="count"/> times, standing on the STEP row. A tap is one
    /// tick by <see cref="TickScrubber"/>'s own press-edge rule — the distance <c>,</c> and
    /// <c>.</c> move — so this travels an <b>exact</b> number of ticks through the production
    /// router, which is what the acceptance check below needs and what no held arrow can promise.
    /// </summary>
    private static void ScrubTaps(
        ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, Keys arrow, int count)
    {
        SelectPauseMenuItem(modes, PauseMenuItem.Scrub);
        for (int i = 0; i < count; i++)
        {
            Tap(modes, keys, pointer, arrow);
        }
    }

    // ==================================================================================
    // 1. The stage, in one run.
    // ==================================================================================

    /// <summary>
    /// <b>The sentence M9 stage 5 exists to make true</b>, walked end to end at two different
    /// ticks: play, Esc, F2, change a line, Ctrl+S, and continue on the very same tick with the
    /// new code — then step 60 ticks back and 60 forward and land on the same frame twice.
    ///
    /// <para>Two ticks and not one, because the work order asks for it (acceptance 2, "не только
    /// на маленьком") and because the failure modes differ: a restart looks like a small tick and
    /// a resimulation that silently truncates looks like a large one. 120 is inside the first
    /// second of play, 900 is fifteen seconds of recorded input that the new code has to survive
    /// before the author sees anything.</para>
    ///
    /// <para><b>What each assertion would catch.</b> The tick equality is the continuation
    /// itself — a rebuild that restarted would land on 0. The frame equality against a straight
    /// run of the NEW source is what says the new code is the code that is running <em>and</em>
    /// that the recorded past was replayed through it; comparing against the new code at tick 0
    /// is the negative control for "landed on the right frame for the wrong reason". The rewind
    /// pair is the stage's own acceptance item 3.</para>
    ///
    /// <para><b>Break recipe (the work order's first named negative control).</b> Make
    /// <c>TimeMachine.Rebuild</c> resimulate from somewhere other than zero — in
    /// <c>RebuildCore</c>, replace the <c>Resimulate(landing)</c> after the boot with
    /// <c>Resimulate(landing / 2)</c> — and both rows go red on the frame comparison while the
    /// tick comparison still passes, which is exactly the shape of the bug that rule exists to
    /// catch. Delete <c>ShellModeMachine.PollSessionReload</c>'s body and both rows go red on
    /// every assertion after the save: the edit never reaches the running cartridge at all.</para>
    /// </summary>
    [Theory]
    [InlineData(120)]
    [InlineData(900)]
    public void PlayEscEditSaveContinuesOnTheSameTickWithTheNewCode(int ticks)
    {
        byte[] oldCodeThere = ReferenceFrame(7, ticks);
        byte[] newCodeThere = ReferenceFrame(11, ticks);
        byte[] newCodeAtStart = ReferenceFrame(11, 0);
        const int back = 60;        // one console second, the distance acceptance 3 names
        byte[] newCodeEarlier = ReferenceFrame(11, ticks - back);

        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        // Play. The frame loop spends its ticks because nothing is holding the session still.
        Frames(modes, keys, pointer, ticks);
        Assert.Equal(ticks, session.Tick);
        Assert.Equal(oldCodeThere, session.Framebuffer.Pixels);

        // Esc: the pause menu, over the frame — not the door out it used to be.
        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);
        Assert.Equal(ShellMode.Game, modes.Mode);
        Assert.Same(session, modes.Session);

        // F2: the code of the cart that is running. The session is still alive and now paused.
        Tap(modes, keys, pointer, Keys.F2);
        Assert.Equal(ShellMode.CodeEditor, modes.Mode);
        Assert.Same(session, modes.Session);
        Assert.True(session.IsPaused);

        // The author changes the colour and saves with Ctrl+S, through the real router.
        modes.CodeEditor!.SelectAll();
        modes.CodeEditor.Insert(Source(11));
        Assert.True(modes.CodeEditor.IsDirty);
        Tap(modes, keys, pointer, Keys.LeftControl, Keys.S);
        Assert.False(modes.CodeEditor.IsDirty);

        PumpUntilRebuilt(modes, keys, pointer, newCodeThere);

        // The stage's sentence: same tick, new code, provably not a restart.
        Assert.Equal(ticks, session.Tick);
        Assert.Equal(newCodeThere, session.Framebuffer.Pixels);
        Assert.NotEqual(newCodeAtStart, session.Framebuffer.Pixels);

        // Back to the game tab: the frame is where it was left, with the menu over it.
        Tap(modes, keys, pointer, Keys.F1);
        Assert.Equal(ShellMode.Game, modes.Mode);
        Assert.True(modes.PauseMenu.Shown);
        Assert.Equal(ticks, session.Tick);

        // Acceptance 3: sixty back, sixty forward, the same frame both times. Sixty taps of the
        // scrub row's arrows since stage 5a took the REWIND 60 / AHEAD 60 rows away — the verb is
        // the same one, and the distance is exact because a tap is one tick.
        ScrubTaps(modes, keys, pointer, Keys.Left, back);
        Assert.Equal(ticks - back, session.Tick);
        Assert.Equal(newCodeEarlier, session.Framebuffer.Pixels);

        ScrubTaps(modes, keys, pointer, Keys.Right, back);
        Assert.Equal(ticks, session.Tick);
        Assert.Equal(newCodeThere, session.Framebuffer.Pixels);
    }

    // ==================================================================================
    // 2. Р1 — walking off the game tab stops the simulation.
    // ==================================================================================

    /// <summary>
    /// The work order's Р1: an author must not be editing a moving target. Leaving the game tab
    /// for any of the five editors stops the cartridge, and coming back does not start it — the
    /// menu is there with RESUME on it, and the decision is the author's.
    ///
    /// <para><b>Break recipe (the work order's second named negative control), measured rather
    /// than guessed.</b> Delete <c>Session?.PauseForEditing()</c> from
    /// <c>ShellModeMachine.SwitchEditorTab</c> and all five rows go red — on the pause assertion
    /// first, and if that assertion were not here, on the tick assertion after the return, because
    /// the sixty frames spent looking at the returned game screen would spend sixty ticks.
    /// Deleting the same call from <c>EnterGameTab</c> instead turns <b>nothing</b> red, and that
    /// was checked, not assumed: with the leaving half in place the session is already stopped by
    /// the time the game tab comes back, so that call is a belt beside a brace and its own comment
    /// says so.</para>
    ///
    /// <para>Said plainly, because it is worth knowing when reading a failure: while the shell
    /// stands on an editor tab nothing calls <c>CartSession.Update</c> at all, so the pause is
    /// not what stops the clock there — <see cref="CartSession.IsPaused"/> is what the returning
    /// frame loop reads, and that is the assertion doing the work.</para>
    /// </summary>
    [Theory]
    [InlineData(ShellMode.CodeEditor)]
    [InlineData(ShellMode.Editor)]
    [InlineData(ShellMode.MapEditor)]
    [InlineData(ShellMode.SfxEditor)]
    [InlineData(ShellMode.MusicEditor)]
    public void LeavingTheGameTabStopsTheSimulationAndComingBackDoesNotStartIt(ShellMode tab)
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 90);
        int left = session.Tick;
        Assert.Equal(90, left);
        Assert.False(session.IsPaused);

        modes.SwitchEditorTab(tab);
        Assert.Equal(tab, modes.Mode);
        Assert.True(session.IsPaused);
        Assert.Same(session, modes.Session);        // Р4: the session survives the door

        Frames(modes, keys, pointer, 60);           // an author reading their code for a second
        Assert.Equal(left, session.Tick);

        modes.SwitchEditorTab(ShellMode.Game);
        Frames(modes, keys, pointer, 60);           // and a second of looking at the paused frame
        Assert.Equal(left, session.Tick);
        Assert.True(modes.PauseMenu.Shown);

        // RESUME, and only then does time start again.
        ChoosePauseMenuItem(modes, PauseMenuItem.Resume);
        Assert.False(modes.PauseMenu.Shown);
        Frames(modes, keys, pointer, 10);
        Assert.Equal(left + 10, session.Tick);
    }

    // ==================================================================================
    // 3. Р8 — a replay recorded before the edit is refused, by identity, with a message.
    // ==================================================================================

    /// <summary>
    /// The work order's Р8. Editing a cart mid-session changes its identity — that is
    /// REPLAY-FORMAT §5's own hash, over the source text among other things — so the newest
    /// <c>.qrpr</c> beside the cart is now a recording of a cartridge that no longer exists.
    /// F8 must say so and refuse, not play something that merely looks close: in a window there
    /// is no terminal for a warning to land in, and a replay that diverges silently reads as a
    /// bug in the game.
    ///
    /// <para><b>This is a deliberate divergence from REPLAY-FORMAT §5</b>, which says a mismatch
    /// is a warning and not a refusal. The two clauses that sentence exists to protect are
    /// untouched: <c>TimeMachine.Rebuild</c> still applies the live log to new code without a
    /// word (the test above IS that), and the CLI's <c>quarp replay play</c> still warns and
    /// plays. What changed is F8 in the shell. The doc needs the distinction written into it.</para>
    ///
    /// <para><b>Break recipe (the work order's third named negative control).</b> Turn the
    /// identity branch in <c>CartSession.TogglePlayback</c> back into a warning — delete the
    /// <c>Flash</c> and the <c>return</c> — and the two assertions after the edit go red: the
    /// stale replay starts playing and the live tick is replaced by the recording's.</para>
    /// </summary>
    [Fact]
    public void AReplayRecordedBeforeTheEditIsRefusedByIdentity()
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 200);
        Assert.Equal(200, session.Tick);

        // F5 writes a replay of the cart as it is now — and F8 plays it, because it matches.
        session.ApplyCommands(new ShellCommands { SaveReplay = true });
        Assert.Single(Directory.GetFiles(Path.Combine(_cart, "replays"), "*.qrpr"));
        session.ApplyCommands(new ShellCommands { PlayReplay = true });
        Assert.True(session.IsPlayingReplay);
        session.ApplyCommands(new ShellCommands { PlayReplay = true });   // back to the live session
        Assert.False(session.IsPlayingReplay);
        Assert.Equal(200, session.Tick);

        // Now the author edits the cart. The identity moves with the source text.
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.F2);
        modes.CodeEditor!.SelectAll();
        modes.CodeEditor.Insert(Source(11));
        Tap(modes, keys, pointer, Keys.LeftControl, Keys.S);
        PumpUntilRebuilt(modes, keys, pointer, ReferenceFrame(11, 200));
        Assert.Equal(200, session.Tick);

        // The same F8, the same file, and it is refused with a message the window can show.
        Tap(modes, keys, pointer, Keys.F1);
        session.ApplyCommands(new ShellCommands { PlayReplay = true });

        Assert.False(session.IsPlayingReplay);
        Assert.Equal(200, session.Tick);
        Assert.Contains("STALE REPLAY", session.Status ?? "", StringComparison.Ordinal);
    }

    // ==================================================================================
    // 4. Р7 — F1 with nothing running, and Р5 — the direct launch.
    // ==================================================================================

    /// <summary>
    /// The work order's Р7: an author can reach the editors from the library without launching
    /// anything, and F1 must still mean something there. It shows the same menu with START on its
    /// first row, and START launches the very folder being edited — so what runs is the cart with
    /// the changes already saved to it.
    ///
    /// <para>Break recipe: return early from <c>ShellModeMachine.StartGameFromEditor</c> and the
    /// session assertion goes red; take the <c>GameRunning</c> argument out of
    /// <c>PauseMenu.Open</c> and the item-list assertion does.</para>
    /// </summary>
    [Fact]
    public void F1WithNothingRunningOffersStartAndStartLaunchesTheEditedCart()
    {
        ShellModeMachine modes = EditingWithNoGame();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();

        Tap(modes, keys, pointer, Keys.F1);

        Assert.Equal(ShellMode.Game, modes.Mode);
        Assert.True(modes.PauseMenu.Shown);
        Assert.False(modes.PauseMenu.GameRunning);
        Assert.Equal(new[] { PauseMenuItem.Resume, PauseMenuItem.Exit }, modes.PauseMenu.Items);
        Assert.Contains("START", modes.PauseMenu.Text(null), StringComparison.Ordinal);
        // Two rows and no third: the header row that used to read "NO GAME RUNNING" went with
        // every other header in stage 5a, and with nothing running there is no tick to scrub.
        Assert.Equal(2, modes.PauseMenu.Text(null).Split('\n').Length);
        Assert.DoesNotContain("<", modes.PauseMenu.Text(null), StringComparison.Ordinal);

        ChoosePauseMenuItem(modes, PauseMenuItem.Resume);

        Assert.NotNull(modes.Session);
        Assert.False(modes.PauseMenu.Shown);
        Frames(modes, keys, pointer, 30);
        Assert.Equal(30, modes.Session!.Tick);
    }

    /// <summary>
    /// The work order's Р5: <c>quarp run &lt;cart&gt;</c> stopped being a trap. Esc used to close
    /// the window, so an author who launched by path could not reach one editor of the five this
    /// milestone built. Now Esc raises the same menu everyone else gets, the tabs work from it,
    /// and EXIT is what ends the process.
    ///
    /// <para>Break recipe: restore the <c>Mode == ShellMode.Game when !_directLaunch</c> guard on
    /// <c>HandleEscape</c>'s game case and the first two assertions go red — Esc quits again and
    /// there is no menu to travel from.</para>
    /// </summary>
    [Fact]
    public void ADirectLaunchGetsThePauseMenuTheTabsAndAnExitThatQuits()
    {
        using CartSession session = CartSession.Start(_cart);
        var modes = new ShellModeMachine(
            new CartLibrary(_root),
            static _ => throw new InvalidOperationException("a direct launch never starts sessions"),
            static () => { },
            session);
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();

        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);
        Assert.False(modes.ExitRequested);

        Tap(modes, keys, pointer, Keys.F2);
        Assert.Equal(ShellMode.CodeEditor, modes.Mode);      // the door that did not exist before
        Assert.Same(session, modes.Session);

        Tap(modes, keys, pointer, Keys.F1);
        Assert.Equal(ShellMode.Game, modes.Mode);
        ChoosePauseMenuItem(modes, PauseMenuItem.Exit);

        Assert.True(modes.ExitRequested);
        Assert.Same(session, modes.Session);        // the window's own teardown saves and unloads it
    }

    // ==================================================================================
    // 5. Р3 — new code that cannot survive the past does not take the session with it.
    // ==================================================================================

    /// <summary>
    /// The work order's Р3, which is M2's behaviour (ADR-018 §5) made reachable: a save whose
    /// code crashes while replaying the recorded past must leave a usable session behind, not a
    /// dead console. It falls back to restart mode on the new build — the tick moves, and that is
    /// the honest answer — and the shell keeps running.
    ///
    /// <para>Break recipe: let <c>RebuildCore</c>'s resimulation exception escape instead of
    /// being reported through <c>RebuildResult</c> and this test throws out of
    /// <c>PollSessionReload</c> instead of asserting.</para>
    /// </summary>
    [Fact]
    public void ASaveWhoseCodeCannotReplayThePastKeepsTheSessionAlive()
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 300);
        Assert.Equal(300, session.Tick);

        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.F2);
        modes.CodeEditor!.SelectAll();
        modes.CodeEditor.Insert("""
            using Quarp.Api;

            public sealed class PauseCart : Cartridge
            {
                public override void Update()
                {
                    if (Ticks == 150)
                    {
                        throw new System.InvalidOperationException("cannot replay tick 150");
                    }
                }

                public override void Draw() => Cls(0);
            }
            """);
        Tap(modes, keys, pointer, Keys.LeftControl, Keys.S);

        for (int frame = 0; frame < 300 && session.Tick >= 300; frame++)
        {
            Frame(modes, keys, pointer, NoKeys);
            Thread.Sleep(10);
        }

        Assert.True(session.Tick < 300);                    // restart mode, as ADR-018 §5 promises
        Assert.Same(session, modes.Session);                // and the session is still the session
        Tap(modes, keys, pointer, Keys.F1);
        ChoosePauseMenuItem(modes, PauseMenuItem.Resume);
        Frames(modes, keys, pointer, 10);
        Assert.True(session.Tick <= 160);
    }

    // ==================================================================================
    // 6. The menu itself: two channels, one verb.
    // ==================================================================================

    /// <summary>
    /// Input parity for the pause menu, the law of every screen in this shell (M9 stage 2.5):
    /// clicking a row and walking to it with the arrows before pressing Enter must do the same
    /// thing, and the row the pointer lands on must be the row the picture drew — which is one
    /// arithmetic, <see cref="PauseMenu.ItemRect"/>, used by the hit test and by
    /// <see cref="ShellOverlay"/> alike.
    ///
    /// <para><b>Break recipe, corrected twice.</b> The one written here originally was false, and
    /// saying so is the point: it claimed that making <see cref="PauseMenu.ItemRect"/> count rows
    /// from <c>index</c> instead of <c>index + 1</c> would turn this red. It could not — both
    /// channels went through that same function. Since stage 5a the two channels are further
    /// apart than they were (a held key on a selected row against a held pointer on a printed
    /// arrow) and the recipe that fails is: drop the <c>ScrubDirection</c> pointer branch in
    /// <c>GameScreenInput</c>, or return early from <c>PauseMenu.Select</c>, and the two roads
    /// disagree at once. What ties a rectangle to a printed glyph is
    /// <see cref="AClickWhereTheArrowIsPrintedScrubsThatWay"/>, which measures the click point
    /// from the ink instead.</para>
    /// </summary>
    [Fact]
    public void TheMenuAnswersTheArrowsAndThePointerWithTheSameVerb()
    {
        byte[] pointerFrame = LeaveThroughTheMenu(byPointer: true);
        byte[] keyboardFrame = LeaveThroughTheMenu(byPointer: false);
        Assert.Equal(keyboardFrame, pointerFrame);
    }

    /// <summary>
    /// One run of "play a while, open the menu, scrub one tick back" — once with the mouse alone
    /// and once with the keyboard alone. No <see cref="Keys"/> value is touched on the pointer
    /// road past the Escape that opens the menu, and no coordinate on the keyboard road at all.
    /// </summary>
    private byte[] LeaveThroughTheMenu(bool byPointer)
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 100);
        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);

        if (byPointer)
        {
            Rectangle arrow = modes.PauseMenu.ScrubArrowRect(-1, ConsoleWidth, ConsoleHeight);
            Frame(modes, keys, pointer, NoKeys, arrow.Center.X, arrow.Center.Y, ButtonState.Pressed);
            Frame(modes, keys, pointer, NoKeys, arrow.Center.X, arrow.Center.Y);
        }
        else
        {
            Tap(modes, keys, pointer, Keys.Down);
            Assert.Equal(PauseMenuItem.Scrub, modes.PauseMenu.Current);
            Tap(modes, keys, pointer, Keys.Left);
        }

        Assert.Equal(99, session.Tick);
        return session.Framebuffer.Pixels.ToArray();
    }

    /// <summary>
    /// The menu's shape as a model: the box is on the screen, the rows are inside it and do not
    /// overlap, every row's rectangle answers the hit test with its own index, and the cursor
    /// marks exactly one row of the printed block.
    ///
    /// <para>Break recipe: give <see cref="PauseMenu.LineCount"/> back the <c>+ 1</c> the header
    /// row needed — the box grows a row nothing prints on, every row shifts, and the count of
    /// printed lines stops matching the count of items.</para>
    /// </summary>
    [Fact]
    public void TheMenusRowsAreInsideItsBoxAndHitTestBackToThemselves()
    {
        var menu = new PauseMenu();
        menu.Open(gameRunning: true);
        Rectangle box = menu.Box(ConsoleWidth, ConsoleHeight);

        Assert.True(box.X >= 0 && box.Y >= 0);
        Assert.True(box.Right <= ConsoleWidth && box.Bottom <= ConsoleHeight);

        var rows = new List<Rectangle>();
        for (int i = 0; i < menu.Items.Count; i++)
        {
            Rectangle row = menu.ItemRect(i, ConsoleWidth, ConsoleHeight);
            Assert.True(box.Contains(row), $"row {i} is outside the menu's own box");
            Assert.All(rows, other => Assert.False(other.Intersects(row)));
            rows.Add(row);

            Assert.True(menu.TryItem(row.Center.X, row.Center.Y, ConsoleWidth, ConsoleHeight, out int hit));
            Assert.Equal(i, hit);
        }
        Assert.False(menu.TryItem(0, 0, ConsoleWidth, ConsoleHeight, out _));

        // The two scrub arrows: inside the box, on the scrub row, not on each other, and each
        // hit-testing back to its own direction.
        Rectangle backArrow = menu.ScrubArrowRect(-1, ConsoleWidth, ConsoleHeight);
        Rectangle forwardArrow = menu.ScrubArrowRect(+1, ConsoleWidth, ConsoleHeight);
        int scrubRow = menu.Items.ToList().IndexOf(PauseMenuItem.Scrub);
        Assert.True(box.Contains(backArrow) && box.Contains(forwardArrow));
        Assert.False(backArrow.Intersects(forwardArrow));
        Assert.True(rows[scrubRow].Contains(backArrow) && rows[scrubRow].Contains(forwardArrow));
        Assert.True(menu.TryScrubArrow(
            backArrow.Center.X, backArrow.Center.Y, ConsoleWidth, ConsoleHeight, out int backWay));
        Assert.Equal(-1, backWay);
        Assert.True(menu.TryScrubArrow(
            forwardArrow.Center.X, forwardArrow.Center.Y, ConsoleWidth, ConsoleHeight, out int forwardWay));
        Assert.Equal(+1, forwardWay);
        Assert.False(menu.TryScrubArrow(0, 0, ConsoleWidth, ConsoleHeight, out _));

        // The printed block: one row per item and no header at all, with the cursor on exactly one.
        string[] lines = menu.Text(300).Split('\n');
        Assert.Equal(menu.Items.Count, lines.Length);
        Assert.Contains("300", lines[scrubRow], StringComparison.Ordinal);
        Assert.Equal(1, lines.Count(line => line.StartsWith(">", StringComparison.Ordinal)));
        menu.Move(+1);
        Assert.Equal(
            1, menu.Text(300).Split('\n').Count(line => line.StartsWith(">", StringComparison.Ordinal)));
    }

    // ==================================================================================
    // 7. The tab is on the strip, and it is visible.
    // ==================================================================================

    /// <summary>
    /// The strip's sixth stop, as geometry: every one of the five editor screens places the GAME
    /// tab, it is inside the top band, it does not sit on any other button, and the glyph is
    /// actually drawn — ink inside its own ten-by-ten cell.
    ///
    /// <para>This is the evidence behind the eighteen golden hashes re-pinned in this wave. Every
    /// structural probe in the five <c>*ScreenGoldenTests</c> passed the change untouched, which
    /// says the screens were redrawn and not broken; what those probes could not say is that
    /// something new appeared and where. This does. One rendered screen suffices for the glyph
    /// because all five paint their buttons through one owner —
    /// <see cref="ConsoleChromeRenderer.DrawButton"/> asking
    /// <see cref="EditorIcons.IconFor"/> — and the placement half is asserted on all five.</para>
    ///
    /// <para>Break recipe: drop <see cref="EditorButton.GameTab"/> from
    /// <see cref="ConsoleChrome.RightTabs"/> and the placement rows throw where they look the
    /// rectangle up; give the tab an all-zero mask in <c>EditorIcons</c> and the ink assertion
    /// goes red on its own.</para>
    /// </summary>
    [Fact]
    public void EveryEditorScreenPlacesTheGameTabAndTheSpriteScreenDrawsIt()
    {
        var strips = new (string Screen, ConsoleChrome Chrome, IReadOnlyList<EditorButtonPlace> Buttons)[]
        {
            ("sprites", SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, 1).Chrome,
                SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, 1).Buttons),
            ("map", MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight, MapEditorOverlay.None, 0).Chrome,
                MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight, MapEditorOverlay.None, 0).Buttons),
            ("code", CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Chrome,
                CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Buttons),
            ("sound", SfxEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Chrome,
                SfxEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Buttons),
            ("music", MusicEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Chrome,
                MusicEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Buttons),
        };

        foreach ((string screen, ConsoleChrome chrome, IReadOnlyList<EditorButtonPlace> buttons) in strips)
        {
            Rectangle game = ConsoleChrome.ButtonRect(buttons, EditorButton.GameTab);
            Assert.True(chrome.TopBar.Contains(game), $"the GAME tab left the top band on the {screen} screen");
            Assert.Equal(chrome.TooltipField.Right, game.X);      // it is the leftmost of the right group
            foreach (EditorButtonPlace place in buttons)
            {
                if (place.Id != EditorButton.GameTab)
                {
                    Assert.False(place.Rect.Intersects(game), $"{place.Id} overlaps the GAME tab on the {screen} screen");
                }
            }
        }

        // And the glyph really lands: ink inside the cell on a screen that draws it for real.
        var editor = new SpriteEditorSession(_cart);
        var display = new ShellScreen();
        SpriteEditorLayout drawn = SpriteEditorRenderer.Draw(
            display, editor, null, false, null, new SheetScroll(), 0.0);
        Rectangle cell = ConsoleChrome.ButtonIconRect(drawn.ButtonRect(EditorButton.GameTab));
        bool ink = false;
        for (int y = cell.Y; y < cell.Bottom; y++)
        {
            for (int x = cell.X; x < cell.Right; x++)
            {
                ink |= display.Console.Pget(x, y) != 0;
            }
        }
        Assert.True(ink, "the GAME tab is placed but its icon draws nothing");
    }

    /// <summary>
    /// The two keys the game screen withholds from the session while the menu is up, and the
    /// reason each is withheld (see <see cref="GameScreenInput"/>): Space must not start the
    /// simulation under an open menu, and F5 is the sound tab there rather than
    /// <see cref="ShellCommands.SaveReplay"/> — one physical edge cannot write a file and travel
    /// at once.
    ///
    /// <para>Break recipe: hand <c>commands</c> through unfiltered in
    /// <c>GameScreenInput.MenuFrame</c> and both halves go red — Space resumes behind the menu,
    /// and F5 leaves a <c>.qrpr</c> on disk on its way to the sound screen.</para>
    /// </summary>
    [Fact]
    public void TheOpenMenuWithholdsSpaceAndTheReplayKeysFromTheSession()
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 40);
        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(session.IsPaused);

        Tap(modes, keys, pointer, Keys.Space);
        Assert.True(session.IsPaused);          // the menu is still up; the game did not restart
        Assert.True(modes.PauseMenu.Shown);

        Tap(modes, keys, pointer, Keys.F5);
        Assert.Equal(ShellMode.SfxEditor, modes.Mode);
        Assert.False(Directory.Exists(Path.Combine(_cart, "replays")));

        // While the game is playing the same key still writes a replay, which is the other half
        // of the gate: the filter is about the menu, not about the key.
        modes.SwitchEditorTab(ShellMode.Game);
        ChoosePauseMenuItem(modes, PauseMenuItem.Resume);
        Tap(modes, keys, pointer, Keys.F5);
        Assert.Single(Directory.GetFiles(Path.Combine(_cart, "replays"), "*.qrpr"));
    }

    // ==================================================================================
    // 8. One cartridge at a time: the editor does not follow the author to the next cart.
    // ==================================================================================

    /// <summary>
    /// <b>Leaving a cartridge lets go of all of it.</b> The road below is the one an author walks
    /// without thinking: play cart A, Esc, F2 (the editor is born on A), F1, EXIT — and then launch
    /// cart B from the library and press F2 again. Before this fix the second F2 showed <b>cart A's
    /// code</b>, because the exit disposed the session and left the editor and its folder standing:
    /// two fields that had to be cleared together, cleared in one place out of two. The author was
    /// then editing a file belonging to a cartridge that was not running, and Ctrl+S wrote it there
    /// — silently, with the right cart's name nowhere on screen.
    ///
    /// <para>The last third of the test is the part that matters most: after the save, cart B's
    /// source has changed and cart A's file is <b>byte-for-byte</b> what it was. A test that only
    /// looked at which text was on screen would pass on a shell that showed B and saved to A.</para>
    ///
    /// <para><b>Break recipe.</b> In <c>ShellModeMachine.ReleaseCartridge</c>, drop the
    /// <c>_open = null;</c> line — the exit goes back to releasing the session alone, which is
    /// exactly the shipped defect — and the run goes red on the first assertion after the second
    /// F2: the CODE screen holds <c>PauseCart</c>, the cart that was left, and the save lands in
    /// cart A's file.</para>
    /// </summary>
    [Fact]
    public void TheEditorDoesNotFollowTheAuthorOutOfOneCartridgeIntoTheNext()
    {
        byte[] untouchedA = File.ReadAllBytes(SourcePathOf(_cart));
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();

        // Cart A: play a little, then walk into its code the way the stage's own test does.
        Frames(modes, keys, pointer, 30);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.F2);
        Assert.Equal(ShellMode.CodeEditor, modes.Mode);
        Assert.Contains("PauseCart", modes.CodeEditor!.Text, StringComparison.Ordinal);
        Assert.Equal("cart", modes.Editor!.CartName);       // the sheet names its folder

        // Out through the one door: F1, then EXIT.
        Tap(modes, keys, pointer, Keys.F1);
        ChoosePauseMenuItem(modes, PauseMenuItem.Exit);
        Assert.Equal(ShellMode.Library, modes.Mode);
        Assert.Null(modes.Session);
        Assert.Null(modes.Editor);          // the cartridge was let go as a whole
        Assert.Null(modes.CodeEditor);

        // Cart B, launched from the library the ordinary way.
        modes.Library.SelectPath(_second);
        Assert.Equal("second", modes.Library.Selected!.Value.Name);
        Assert.NotNull(modes.LaunchSelected());
        Frames(modes, keys, pointer, 10);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.F2);

        // The screen shows the code of the cart that is RUNNING.
        Assert.Equal(ShellMode.CodeEditor, modes.Mode);
        Assert.Equal("second", modes.Editor!.CartName);
        Assert.Contains("SecondCart", modes.CodeEditor!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("PauseCart", modes.CodeEditor.Text, StringComparison.Ordinal);

        // And Ctrl+S writes to that cart's file, leaving the other one exactly as it was.
        modes.CodeEditor.SelectAll();
        modes.CodeEditor.Insert(SecondCartSource.Replace("Cls(1);", "Cls(2);", StringComparison.Ordinal));
        Tap(modes, keys, pointer, Keys.LeftControl, Keys.S);
        Assert.False(modes.CodeEditor.IsDirty);

        Assert.Contains("Cls(2);", File.ReadAllText(SourcePathOf(_second)), StringComparison.Ordinal);
        Assert.Equal(untouchedA, File.ReadAllBytes(SourcePathOf(_cart)));
    }

    // ==================================================================================
    // 9. The printed row and the clickable row are the same row.
    // ==================================================================================

    /// <summary>
    /// <b>A click where the letters are must do what the letters say.</b> Every row is checked by
    /// its own verb, and the point clicked is measured <em>from the printed menu</em>, never from
    /// <see cref="PauseMenu.ItemRect"/> — see <see cref="WhereTheMenuPrints"/>, which finds the ink
    /// of a row by printing the block through the console's own text layout and blanking that one
    /// line. So the two things that must agree, the picture and the hit test, are compared instead
    /// of being asked of the same function twice.
    ///
    /// <para><b>Why it had to be written this way.</b> The parity test above takes its click point
    /// from <c>ItemRect</c> — the very arithmetic <c>GameScreenInput</c> hit-tests with — so nothing
    /// in it tied a rectangle to a printed line: dropping the header's row from <c>ItemRect</c>
    /// (<c>index</c> instead of <c>index + 1</c>) left all fifteen tests of this file green while
    /// every click in the window landed one row off.</para>
    ///
    /// <para><b>Break recipe (measured, not guessed).</b> That same edit — <c>ItemRect</c> counting
    /// rows from <c>index</c> — turns every row of this theory red: rows 0..4 select and activate
    /// the row <em>below</em> the letters clicked, and EXIT (the last row) hit-tests to nothing at
    /// all, so the game is still standing where the assertion expects the library.</para>
    /// </summary>
    [Theory]
    [InlineData(PauseMenuItem.Resume)]
    [InlineData(PauseMenuItem.Scrub)]
    [InlineData(PauseMenuItem.Exit)]
    public void AClickWhereARowIsPrintedActivatesThatRow(PauseMenuItem item)
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 100);
        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);
        Assert.Equal(100, session.Tick);

        Point spot = WhereTheMenuPrints(modes.PauseMenu, session.Tick, LabelOf(item));

        // Hover first: the row under the pointer must be the row the letters belong to.
        Frame(modes, keys, pointer, NoKeys, spot.X, spot.Y);
        Assert.Equal(item, modes.PauseMenu.Current);

        // Then the click, and the verb of that row and no other.
        Frame(modes, keys, pointer, NoKeys, spot.X, spot.Y, ButtonState.Pressed);

        switch (item)
        {
            case PauseMenuItem.Resume:
                Assert.False(modes.PauseMenu.Shown);
                Assert.False(session.IsPaused);
                break;
            case PauseMenuItem.Scrub:
                // The one row with no verb of its own: clicking its letters puts the cursor
                // there and moves nothing. Its two verbs are the arrows, and they are checked
                // the same way, from their own ink, by the theory below.
                Assert.True(modes.PauseMenu.Shown);
                Assert.Equal(100, session.Tick);
                break;
            default:
                Assert.Equal(ShellMode.Library, modes.Mode);
                Assert.Null(modes.Session);
                break;
        }
    }

    /// <summary>
    /// <b>A click where an arrow is printed must scrub the way the arrow points.</b> The sibling
    /// of the theory above, for the two glyphs that replaced four rows in stage 5a, and written
    /// the same way for the same reason: the point clicked is measured from the <em>ink</em>
    /// (<see cref="WhereTheMenuPrints"/> finds the one character by blanking it), never from
    /// <see cref="PauseMenu.ScrubArrowRect"/> — which is the arithmetic the hit test itself uses,
    /// so taking the click point from it would be asking one function two questions and calling
    /// the agreement a test.
    ///
    /// <para><b>Break recipe (measured, and the obvious one does not work).</b> Moving
    /// <c>PauseMenu.LeftArrowColumn</c> changes <b>nothing</b> here, and that is the single owner
    /// doing its job: the printed glyph and the hit rectangle are placed by the same constant, so
    /// they move together. What this test catches is the two disagreeing, so the recipe has to
    /// break one side only — add one to the column inside <c>PauseMenu.ScrubArrowRect</c> and the
    /// backward row goes red (the click lands on the space beside the rectangle and nothing
    /// scrubs) while the forward row stays green, together with the four rows of
    /// <see cref="TheScrubRowNeverCutsTheTickNumberAndNeverMovesItsArrows"/>.</para>
    /// </summary>
    [Theory]
    [InlineData("<", -1)]
    [InlineData(">", +1)]
    public void AClickWhereTheArrowIsPrintedScrubsThatWay(string arrow, int direction)
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 100);
        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);
        Assert.Equal(100, session.Tick);

        Point spot = WhereTheMenuPrints(modes.PauseMenu, session.Tick, arrow);

        // Press: one frame of the hold, which is one tick by the scrubber's press-edge rule.
        Frame(modes, keys, pointer, NoKeys, spot.X, spot.Y, ButtonState.Pressed);
        Frame(modes, keys, pointer, NoKeys, spot.X, spot.Y);

        Assert.Equal(100 + direction, session.Tick);
        Assert.True(modes.PauseMenu.Shown);         // scrubbing never leaves the menu
    }

    /// <summary>
    /// What each row reads on a running game — the test's own expectation of the printed word,
    /// rather than a copy of the menu's private table.
    /// </summary>
    private static string LabelOf(PauseMenuItem item) => item switch
    {
        PauseMenuItem.Resume => "RESUME",
        PauseMenuItem.Scrub => "STEP",
        _ => "EXIT",
    };

    /// <summary>
    /// The centre of the <b>ink</b> of a label where the menu actually prints it. The block is
    /// printed twice into a console — once whole and once with those characters replaced by
    /// spaces — and the pixels that differ are, by construction, exactly that label's glyphs;
    /// their bounding box is where the eye sees it. Nothing here measures a font or counts a line
    /// height: the layout comes from <c>VirtualConsole.Print</c>, which is the same cursor rule
    /// <c>ShellOverlay.Print</c> mirrors when it paints this menu over the frame ("the same 4x6
    /// layout VirtualConsole.Print uses", by that method's own comment).
    ///
    /// <para>Only the label is blanked, not the whole row, because since stage 5a a row can carry
    /// three separate controls (the word, and the two arrows on either side of the tick) and the
    /// centre of a whole row would be none of them. Column 0 is skipped when searching: it is the
    /// selection cursor's column, and the cursor is a <c>&gt;</c>.</para>
    /// </summary>
    private static Point WhereTheMenuPrints(PauseMenu menu, int? tick, string label)
    {
        string[] lines = menu.Text(tick).Split('\n');
        int row = -1;
        int column = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            int at = lines[i].IndexOf(label, 1, StringComparison.Ordinal);
            if (at >= 0)
            {
                Assert.True(row < 0, $"'{label}' is printed on two rows of the menu");
                row = i;
                column = at;
            }
        }
        Assert.True(row >= 0, $"'{label}' is not printed on the menu at all");

        string[] blanked = (string[])lines.Clone();
        blanked[row] = string.Concat(
            lines[row].AsSpan(0, column),
            new string(' ', label.Length),
            lines[row].AsSpan(column + label.Length));
        Point origin = menu.TextOrigin(ConsoleWidth, ConsoleHeight);
        VirtualConsole whole = Printed(string.Join('\n', lines), origin);
        VirtualConsole without = Printed(string.Join('\n', blanked), origin);

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < ConsoleHeight; y++)
        {
            for (int x = 0; x < ConsoleWidth; x++)
            {
                if (whole.Pget(x, y) == without.Pget(x, y))
                {
                    continue;
                }
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        Assert.True(maxX >= 0, $"'{label}' printed no ink at all");
        return new Point((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>One block of menu text on a console of its own, drawn where the overlay draws it.</summary>
    private static VirtualConsole Printed(string text, Point origin)
    {
        var screen = new ShellScreen();
        screen.Begin();
        screen.Console.Print(text, origin.X, origin.Y, 7);
        return screen.Console;
    }

    /// <summary>
    /// The scrub row prints the whole tick number, whatever the number is, and neither arrow
    /// moves when it grows. The first half is the rule the deleted header used to carry (a
    /// session past 999999 ticks — four and a half hours at 60 Hz, and any session a long rewind
    /// has walked twice — once printed "PAUSED  T 100000" for tick 1000000: not a shortened
    /// number but a different one). The second half is new and is the reason the number is
    /// centred in a fixed field: a clickable glyph that moves while the author is reading is the
    /// worst kind of button, which is the rule <c>ConsoleChrome.PromptVerbRect</c> already states
    /// for the exit prompt.
    ///
    /// <para><b>Break recipe (measured).</b> Make the tick field grow with the number — replace
    /// the centring in <c>PauseMenu.ScrubRowText</c> with a left-aligned copy at
    /// <c>TickColumn</c> and let <c>RightArrowColumn</c> follow the digits — and the arrow rows
    /// go red the moment the tick has a different number of digits from the first case. Cut the
    /// digits to the field with <c>digits[..TickColumns]</c> instead and the int.MaxValue row
    /// goes red on the number itself.</para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(1_000_000)]
    [InlineData(int.MaxValue)]
    public void TheScrubRowNeverCutsTheTickNumberAndNeverMovesItsArrows(int tick)
    {
        var menu = new PauseMenu();
        menu.Open(gameRunning: true);
        int scrubRow = menu.Items.ToList().IndexOf(PauseMenuItem.Scrub);

        string row = menu.Text(tick).Split('\n')[scrubRow];

        Assert.Contains(
            tick.ToString(System.Globalization.CultureInfo.InvariantCulture), row, StringComparison.Ordinal);
        Assert.True(
            row.Length * SystemFont.CellWidth <= menu.Box(ConsoleWidth, ConsoleHeight).Width,
            "the scrub row outgrew the box it is printed in");
        Assert.All(menu.Text(tick).Split('\n'), line => Assert.Equal(row.Length, line.Length));

        // Both arrows are printed exactly where the hit test says they are, at every width of
        // number: the printed column is taken from the string, the rectangle from the model.
        Assert.Equal('<', row[ColumnOf(menu, -1)]);
        Assert.Equal('>', row[ColumnOf(menu, +1)]);
    }

    /// <summary>Which character cell of the printed row an arrow's rectangle covers.</summary>
    private static int ColumnOf(PauseMenu menu, int direction)
    {
        Rectangle arrow = menu.ScrubArrowRect(direction, ConsoleWidth, ConsoleHeight);
        Point origin = menu.TextOrigin(ConsoleWidth, ConsoleHeight);
        return (arrow.X - origin.X) / SystemFont.CellWidth;
    }

    // ==================================================================================
    // 10. The pause belongs to the shell: nothing else may start the game.
    // ==================================================================================

    /// <summary>
    /// <b>An edit that arrives while the pause menu is up must not start the game under it.</b> The
    /// author is looking at a menu; a cartridge that begins running behind it is the moving target
    /// Р1 exists to forbid, and it happens on the most ordinary road there is — the file is saved
    /// by an editor outside the window (VS Code, which this project's own scaffolding sets up), so
    /// the reload arrives with nobody having touched a key in the console.
    ///
    /// <para>Two roads reach the same defect, and both are here: new code that <em>cannot</em>
    /// replay the recorded past falls back to restart mode (<c>CartSession.TryReload</c>'s second
    /// half), and an edit that arrives while a replay is on screen leaves playback
    /// (<c>StopPlayback</c>). Both used to clear the session's pause on their way through.</para>
    ///
    /// <para><b>Break recipe.</b> Put <c>_paused = false;</c> back after <c>_machine.Restart()</c>
    /// in <c>CartSession.TryReload</c> and the first row goes red; drop the <c>_paused = paused;</c>
    /// line after that method's <c>StopPlayback</c> call and the second one does. The tick
    /// assertions at the end are what catch it: the sixty frames after the reload spend sixty ticks
    /// a paused session must not spend.</para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEditArrivingUnderThePauseMenuDoesNotStartTheGame(bool throughAReplay)
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 200);
        Assert.Equal(200, session.Tick);

        if (throughAReplay)
        {
            // Record and play, through the same two commands F5 and F8 hand the session (the
            // open menu withholds those two keys, which is why they are given here rather than
            // typed after the Escape below). No frame is spent on them, so the live session is
            // still standing on tick 200 while the recording plays over it.
            session.ApplyCommands(new ShellCommands { SaveReplay = true });
            session.ApplyCommands(new ShellCommands { PlayReplay = true });
            Assert.True(session.IsPlayingReplay);
        }

        Tap(modes, keys, pointer, Keys.Escape);
        Assert.True(modes.PauseMenu.Shown);
        Assert.True(session.IsPaused);

        // The edit lands on disk from outside the console: no editor screen, no Ctrl+S.
        File.WriteAllText(SourcePathOf(_cart), throughAReplay ? Source(11) : CrashingSource);

        for (int frame = 0; frame < 300 && !ReloadLanded(session, throughAReplay); frame++)
        {
            Frame(modes, keys, pointer, NoKeys);
            Thread.Sleep(10);
        }

        Assert.True(ReloadLanded(session, throughAReplay), "the edit never reached the running cartridge");
        Assert.True(session.IsPaused, "the reload started the game under an open pause menu");
        Assert.True(modes.PauseMenu.Shown);

        int standing = session.Tick;
        Frames(modes, keys, pointer, 60);
        Assert.Equal(standing, session.Tick);
    }

    /// <summary>
    /// Whether the reload the test is waiting for has happened, told apart from "not yet" by the
    /// thing each road changes: the crashing source cannot replay tick 150, so the restart puts the
    /// session below the tick it was standing on; the compiling one lands the new colour on the
    /// frame the recorded past reaches.
    /// </summary>
    private static bool ReloadLanded(CartSession session, bool throughAReplay) =>
        throughAReplay
            ? !session.IsPlayingReplay && session.Framebuffer.Pixels.SequenceEqual(ReferenceFrame(11, 200))
            : session.Tick < 200;

    /// <summary>Code that compiles and then crashes partway through the recorded past — the restart road of the reload.</summary>
    private const string CrashingSource = """
        using Quarp.Api;

        public sealed class PauseCart : Cartridge
        {
            public override void Update()
            {
                if (Ticks == 150)
                {
                    throw new System.InvalidOperationException("cannot replay tick 150");
                }
            }

            public override void Draw() => Cls(0);
        }
        """;

    // ==================================================================================
    // 11. The doors: EXIT asks about unsaved work, Esc travels, and the START menu stays up.
    // ==================================================================================

    /// <summary>
    /// <b>EXIT asks about unsaved work, with the editors' own question.</b> The tab strip means an
    /// author can be standing on the game screen holding an unsaved sprite sheet; EXIT walked
    /// straight past it and let the cartridge go, so the pixels died with no question asked — while
    /// <c>Esc</c> on the sprite screen, one key away, refuses to lose those very pixels. There is
    /// one owner of the question now (<c>ShellModeMachine.RaiseDirtyBankPrompt</c>), so the two
    /// roads out of a cartridge cannot answer it differently.
    ///
    /// <para>The prompt comes up <em>on the screen that owns the work</em>, which is the rule the
    /// five editors already share, and answering it with Z (save) finishes the leaving that was
    /// interrupted: one EXIT, one question, then the library.</para>
    ///
    /// <para><b>Break recipe:</b> take the <c>RaiseDirtyBankPrompt</c> call out of
    /// <c>ShellModeMachine.LeaveGame</c> and the first three assertions go red — the shell is in the
    /// library with the session gone and the unsaved pixel nowhere.</para>
    /// </summary>
    [Fact]
    public void ExitAsksAboutUnsavedWorkBeforeLeavingTheCartridge()
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 40);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.F3);                 // the sprite sheet
        Assert.Equal(ShellMode.Editor, modes.Mode);

        SpriteEditorSession sheet = modes.Editor!;
        sheet.SelectColor(7);
        sheet.BeginStroke();
        sheet.Paint(0, 0);
        sheet.EndStroke();
        Assert.True(sheet.IsDirty);

        Tap(modes, keys, pointer, Keys.F1);
        ChoosePauseMenuItem(modes, PauseMenuItem.Exit);

        // Not gone: the question is up, on the screen that holds the work.
        Assert.Equal(ShellMode.Editor, modes.Mode);
        Assert.True(sheet.ExitPromptShown);
        Assert.Same(session, modes.Session);
        Assert.False(modes.PauseMenu.Shown);

        // Z answers it, and the leaving the author asked for finishes.
        modes.SaveEditorAndClose();
        Assert.Equal(ShellMode.Library, modes.Mode);
        Assert.Null(modes.Session);
        Assert.Null(modes.Editor);
        Assert.True(File.Exists(Path.Combine(_cart, "gfx.png")));
    }

    /// <summary>
    /// <b>Esc in an editor with a cartridge running is travel, not a door.</b> It goes back to the
    /// game tab and touches nothing: the session, its input log and its time machine are exactly
    /// what they were, and so is the unsaved line of code left on the CODE screen.
    ///
    /// <para>Before this fix the same keypress ran the close chain, which lets the cartridge go —
    /// so <c>Esc</c> on an editor screen destroyed a <b>live</b> session, the one thing this whole
    /// stage exists to keep. The way out of a cartridge is EXIT on the pause menu, and only that.
    /// The comment in <c>ShellModeMachine</c> and ADR-042 §4 named a method
    /// (<c>ReplaceSession</c>) that does not exist in this repository; the real second caller was
    /// this road, and the owner's ruling was that the code was wrong, not the documentation.</para>
    ///
    /// <para><b>Break recipe (run, not guessed):</b> make
    /// <c>ShellModeMachine.EscapeReturnsToTheGame</c> answer false always and the first assertion
    /// goes red with "Expected: Game, Actual: CodeEditor" — the close chain runs instead of the
    /// travel, and it stops on this screen to ask about the unsaved text. Clean the text first and
    /// the same break ends where it used to: the library, with the live session disposed.</para>
    /// </summary>
    [Fact]
    public void EscapeInAnEditorWithACartridgeRunningGoesBackToTheGame()
    {
        ShellModeMachine modes = Playing();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();
        CartSession session = modes.Session!;

        Frames(modes, keys, pointer, 70);
        Tap(modes, keys, pointer, Keys.Escape);
        Tap(modes, keys, pointer, Keys.F2);
        modes.CodeEditor!.SelectAll();
        modes.CodeEditor.Insert(Source(11));            // unsaved work, deliberately
        Assert.True(modes.CodeEditor.IsDirty);

        Tap(modes, keys, pointer, Keys.Escape);

        Assert.Equal(ShellMode.Game, modes.Mode);
        Assert.Same(session, modes.Session);
        Assert.Equal(70, session.Tick);
        Assert.True(modes.PauseMenu.Shown);
        Assert.True(modes.CodeEditor!.IsDirty);         // the work is still there, unasked about
        Assert.False(modes.CodeView!.ExitPromptShown);  // and nothing was asked, because nothing was lost

        // EXIT is the road that leaves, and it asks about that same unsaved text.
        ChoosePauseMenuItem(modes, PauseMenuItem.Exit);
        Assert.Equal(ShellMode.CodeEditor, modes.Mode);
        modes.DiscardCodeAndClose();
        Assert.Equal(ShellMode.Library, modes.Mode);
        Assert.Null(modes.Session);
    }

    /// <summary>
    /// <b>Esc on the START menu keeps it up.</b> With no cartridge behind it this menu is not an
    /// overlay over anything — it IS the game screen (Р7) — so lowering it left a black screen on
    /// which the tab keys do not work either, because the router only offers them while the menu is
    /// up: a console that looks hung, one keypress away from the library. Nothing to close, so Esc
    /// closes nothing; EXIT is the row that leaves.
    ///
    /// <para><b>Break recipe:</b> take the <c>Session is null</c> guard out of
    /// <c>ShellModeMachine.TogglePauseMenu</c> and the first assertion goes red — and the two after
    /// it are what the author was left with: F2 does nothing, and neither does anything else.</para>
    /// </summary>
    [Fact]
    public void EscapeOnTheStartMenuKeepsItUpInsteadOfLeavingABlankScreen()
    {
        ShellModeMachine modes = EditingWithNoGame();
        var keys = new ShellCommandReader();
        var pointer = new EditorMouseReader();

        Tap(modes, keys, pointer, Keys.F1);
        Assert.True(modes.PauseMenu.Shown);
        Assert.Null(modes.Session);

        Tap(modes, keys, pointer, Keys.Escape);

        Assert.True(modes.PauseMenu.Shown);
        Assert.Equal(ShellMode.Game, modes.Mode);

        // Still a working console: the tabs answer, and so does EXIT.
        Tap(modes, keys, pointer, Keys.F2);
        Assert.Equal(ShellMode.CodeEditor, modes.Mode);
        Tap(modes, keys, pointer, Keys.F1);
        ChoosePauseMenuItem(modes, PauseMenuItem.Exit);
        Assert.Equal(ShellMode.Library, modes.Mode);
    }
}

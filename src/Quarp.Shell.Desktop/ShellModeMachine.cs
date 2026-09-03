using Quarp.CartKit;

namespace Quarp.Shell.Desktop;

// The four faces themselves are vocabulary, not wiring, and since the module-boundary wave
// they live in ShellMode.cs — see the note there for why the split had to happen.

/// <summary>
/// Owns the transitions between the shell's modes and the <see cref="CartSession"/> lifetime
/// that goes with them (M9 stage 1). This is genuinely new work, not a refactor: until this
/// milestone a session was born in the shell's constructor and died with the process, so
/// nothing ever had to leave a game <em>and keep the window alive</em>.
///
/// <para><b>Why it is a class of its own and not part of <c>QuarpGame</c>.</b> The game class
/// cannot be constructed without a graphics device, and the one thing this milestone must
/// prove — that leaving a game writes save.dat, silences the speaker and lets the cart's
/// collectible AssemblyLoadContext die — is exactly the thing that must be provable in a
/// headless test. The machine therefore holds the policy and calls out through two seams the
/// shell provides: a session factory and an audio drain.</para>
///
/// <para><b>Escape means different things on purpose.</b> Esc in the library returns to the boot
/// menu (ADR-028; it used to quit, before the menu existed to return to); Esc in the menu quits —
/// except that mid-intro it skips, and in the name field it cancels the field. Esc in the editor
/// <b>with a cartridge running</b> goes back to the game tab and touches nothing else
/// (<see cref="EscapeReturnsToTheGame"/>); with nothing running it returns to the library when the
/// session is clean, and raises the session's footer prompt when it is not — unsaved pixels leave
/// only through an explicit Z (save) or X (discard), never silently. Esc <b>in a game</b> raises
/// <see cref="PauseMenu"/> — see below.</para>
///
/// <para><b>M9 stage 5: the game became a tab, and the session stopped dying at the door.</b>
/// Until this stage leaving a game meant <c>Session.Dispose()</c>, so the input log and the
/// <c>TimeMachine</c> — everything ADR-006 and ADR-007 are about — were destroyed by the only
/// keypress that led out of a running cart, and the editors could only be entered from the
/// library, which is to say only after that destruction. "Pause, edit, continue at the same
/// tick" was therefore a property of the core with no key attached to it. Now:
///
/// <list type="bullet">
///   <item>Esc raises the pause menu over the frame; <see cref="PauseMenuItem.Exit"/> is the
///     only door that still disposes anything;</item>
///   <item><see cref="SwitchEditorTab"/> travels between the game and the five editors in both
///     directions and <b>keeps the session</b> — walking off the game tab only pauses it (Р1:
///     an author must not be editing a moving target);</item>
///   <item>a save in any editor reaches the running cartridge through
///     <see cref="PollSessionReload"/>, which is <see cref="TimeMachine.Rebuild"/> and a return
///     to the very same tick (Р2);</item>
///   <item>a direct launch (<c>quarp run &lt;cart&gt;</c>) gets the same menu, and its Exit
///     leaves the process (Р5) — the author is no longer locked out of the editors.</item>
/// </list>
///
/// <para><b>One owner of "the cartridge this shell has open".</b> A cartridge is open as a
/// <em>whole</em>: the running <see cref="Session"/> and the folder's five editor banks are faces
/// of one thing, and they are let go together by <see cref="ReleaseCartridge"/> — the only method
/// that calls <see cref="CartSession.Dispose"/> and the only one that forgets a bank. It has
/// exactly the two callers Р4 allows: leaving the cartridge (the pause menu's EXIT, or closing the
/// editors when nothing is running — both through <see cref="ReturnToLibrary"/>) and a cartridge
/// change (<see cref="LaunchSelected"/>, <see cref="LoadCartFromPath"/>).
///
/// <para>This is a fix, not decoration. Until it, leaving a game for the library disposed the
/// session and left the <em>editor</em> standing on the cart just left; the next cart launched from
/// the library then found a non-null folder, F2 opened the previous cartridge's code, and Ctrl+S
/// wrote it there. Two fields that had to be cleared together, in two methods that each knew about
/// one of them. Now the banks live in one object (<see cref="OpenCartridge"/>) whose whole lifetime
/// is one assignment, so "forgot to clear the other flag" is not a state this class can reach —
/// pinned by the two-cartridge fixture in <c>PauseAndContinueTests</c>, which is what caught it.</para></para>
/// </summary>
public sealed class ShellModeMachine
{
    private readonly Func<string, CartSession> _startSession;
    private readonly Action _drainAudio;
    private readonly bool _directLaunch;

    /// <summary>Where CREATE GAME scaffolds — the cwd-relative carts root by default, injectable for tests.</summary>
    private readonly string _createRoot;


    /// <summary>
    /// Library entry when <paramref name="directSession"/> is null; game entry around an
    /// already-running session otherwise. The direct session is created by the caller (the CLI
    /// wants load errors as process exit codes, not as library messages), so the machine only
    /// adopts it.
    /// </summary>
    /// <param name="startSession">
    /// Turns a library entry's path into a running session. The shell's implementation also
    /// wires the session to the window and the speaker; the tests' implementation is
    /// <see cref="CartSession.Start(string, Quarp.Core.ConsoleProfile?)"/> bare.
    /// </param>
    /// <param name="drainAudio">
    /// Empties whatever the sound device is still holding. Called on the game → library
    /// transition and nowhere else: on process exit the window's own teardown stops the device.
    /// </param>
    public ShellModeMachine(
        CartLibrary library,
        Func<string, CartSession> startSession,
        Action drainAudio,
        CartSession? directSession = null,
        string? createRoot = null,
        ITextClipboard? textClipboard = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(startSession);
        ArgumentNullException.ThrowIfNull(drainAudio);
        Library = library;
        _startSession = startSession;
        _drainAudio = drainAudio;
        // The same cwd-relative root the library's default scan reads: a cart born in the
        // menu must appear in the library the moment the author comes back to it.
        _createRoot = createRoot ?? Path.Combine(Environment.CurrentDirectory, CartLibrary.FolderName);
        TextClipboard = textClipboard ?? new InMemoryTextClipboard();
        if (directSession is not null)
        {
            Session = directSession;
            Mode = ShellMode.Game;
            _directLaunch = true;
        }
        else
        {
            // The boot menu, not the library (ADR-028): the library is the menu's first door,
            // and its scan now runs on entry through that door rather than here.
            Mode = ShellMode.Menu;
        }
    }

    /// <summary>
    /// The one clipboard of this process, behind <see cref="ITextClipboard"/>. The window hands
    /// down a <see cref="SystemTextClipboard"/> (the machine's own, through SDL); a headless
    /// test passes nothing and gets an <see cref="InMemoryTextClipboard"/>, which is why every
    /// claim about Ctrl+C and Ctrl+V below is a plain unit test with no operating system in it.
    ///
    /// <para><b>Why one, and why it is public now.</b> Until this wave only the code editor had
    /// a clipboard and the machine merely carried the instance into
    /// <see cref="CodeEditorView"/>'s constructor. REFERENCES-EDITORS §8 item 2 asks for the
    /// other four screens as well, and it asks for them to share: "всё межредакторное
    /// копирование идёт через системный буфер" (§1) — one buffer, or copying a piece of a level
    /// out of one cartridge and into another does not work. The five input routers are layer 4
    /// like this machine, so they read it from here; the sessions and views below never see it
    /// and go on taking and returning plain strings.</para>
    ///
    /// <para>One instance per process, constructed by <c>QuarpGame</c> and nowhere else: it is a
    /// host device, and a second one would mean a second answer to "what is on the clipboard".</para>
    /// </summary>
    public ITextClipboard TextClipboard { get; }

    /// <summary>The list the library screen shows; scanned on every entry into the library.</summary>
    public CartLibrary Library { get; }

    /// <summary>The boot screen's model — intro clock, selection, the name field. Idle on a direct launch.</summary>
    public MainMenuSession Menu { get; } = new();

    /// <summary>
    /// The menu that stands over a paused game (M9 stage 5). It is up exactly while the
    /// simulation is being held still by the shell rather than by the player's Space, which is
    /// why the machine and not the window owns it: raising it and pausing the session are one
    /// act, and two owners of that act would eventually disagree.
    /// </summary>
    public PauseMenu PauseMenu { get; } = new();

    public ShellMode Mode { get; private set; }

    /// <summary>
    /// The running cartridge, or null when none is. It is <b>not</b> a mirror of
    /// <see cref="Mode"/>: since M9 stage 5 a session goes on standing (paused) while the author
    /// works on any of the five editor tabs, which is the whole point of the stage — the sentence
    /// that used to be here, "non-null exactly while Mode is Game", stopped being true the day the
    /// game became a tab. What is still exact is the other direction: the game screen with no
    /// session behind it is the START menu of Р7, and nothing else.
    /// </summary>
    public CartSession? Session { get; private set; }

    /// <summary>
    /// The open sprite sheet; non-null while any editor tab is open, because the two graphics tabs
    /// are two faces of one open cartridge and the sheet stays alive (unsaved pixels and all) while
    /// the author is elsewhere. That is the stage-3 promise "there and back without losing unsaved
    /// work".
    /// </summary>
    public SpriteEditorSession? Editor => _open?.Sheet;

    /// <summary>
    /// The open map of the same cart, created lazily by the first visit to the tilemap tab and
    /// then kept until the whole editor closes — so flipping tabs never costs an unsaved cell.
    /// Null until that first visit: a cart whose map is never opened must not get a session
    /// (and therefore cannot get a file) it never asked for.
    /// </summary>
    public MapEditorSession? MapEditor => _open?.Map;

    /// <summary>The map screen's camera, cursor and exit prompt; non-null exactly while <see cref="MapEditor"/> is.</summary>
    public MapEditorView? MapView => _open?.MapView;

    /// <summary>
    /// The sprite screen's view — the canvas grid switch and the sheet-block drag. Unlike its
    /// four siblings it is <b>never null and never replaced</b>: it holds no reference to a
    /// session and nothing in it is a fact about a particular cartridge, so there is nothing for
    /// a close to invalidate. Making it non-null buys the routers and the renderer one fewer
    /// null check on a path that runs sixty times a second, and it lets the grid switch survive
    /// a trip to the library the way a preference should.
    /// </summary>
    public SpriteEditorView SpriteView { get; } = new();

    /// <summary>
    /// How every screen of this shell spells a bank index — the one live copy of
    /// <see cref="IndexFormat"/>, flipped by Ctrl+H from any editor screen
    /// (REFERENCES-EDITORS §8 item 20). It lives here, on the one object that outlives every
    /// session and that all five routers already hold, because the whole point of the feature is
    /// that the author sets it once: five per-screen copies would be five answers to one
    /// question. Nothing about it is ever written to a cartridge, which is why it is not on a
    /// session; and it survives <see cref="ReleaseCartridge"/> on purpose, because a way of reading
    /// is not a property of the cart being read.
    /// </summary>
    public IndexFormat Indexes { get; private set; }

    /// <summary>Ctrl+H, from any of the five editor screens — the only writer of <see cref="Indexes"/>.</summary>
    public void ToggleIndexFormat() => Indexes = Indexes.Toggled();

    /// <summary>
    /// The open text of the same cart's <c>src/main.cs</c>, created lazily by the first visit to
    /// the CODE tab and then kept until the whole editor closes — so flipping tabs never costs
    /// an unsaved character. Null until that first visit: a cart whose code is never opened must
    /// not get a session (and therefore cannot get a <c>src</c> folder) it never asked for.
    /// </summary>
    public CodeEditorSession? CodeEditor => _open?.Code;

    /// <summary>The code screen's scroll, footer fields and exit prompt; non-null exactly while <see cref="CodeEditor"/> is.</summary>
    public CodeEditorView? CodeView => _open?.CodeView;

    /// <summary>
    /// The open effects bank of the same cart's <c>sfx.bin</c>, created lazily by the first
    /// visit to the SOUND tab and then kept until the whole editor closes — so flipping tabs
    /// never costs an unsaved note. Null until that first visit: a cart whose sound is never
    /// opened must not get a session (and therefore cannot get an <c>sfx.bin</c>) it never
    /// asked for.
    /// </summary>
    public SfxEditorSession? SfxEditor => _open?.Sfx;

    /// <summary>The sound screen's slot, cursor, pen, playback request and exit prompt; non-null exactly while <see cref="SfxEditor"/> is.</summary>
    public SfxEditorView? SfxView => _open?.SfxView;

    /// <summary>
    /// The open song of the same cart's <c>music.bin</c>, created lazily by the first visit to
    /// the MUSIC tab and then kept until the whole editor closes — so flipping tabs never costs an
    /// unsaved pattern. Null until that first visit: a cart whose song is never opened must not
    /// get a session (and therefore cannot get a <c>music.bin</c>) it never asked for.
    /// </summary>
    public MusicEditorSession? MusicEditor => _open?.Music;

    /// <summary>The music screen's window, mute table, playback request and exit prompt; non-null exactly while <see cref="MusicEditor"/> is.</summary>
    public MusicEditorView? MusicView => _open?.MusicView;

    /// <summary>
    /// The cartridge whose banks are open in the editors, or null when none is. The one field the
    /// six properties above read: opening is one assignment and closing is one assignment, so a
    /// bank cannot outlive the cartridge it belongs to — see the type comment for the defect that
    /// bought this shape.
    /// </summary>
    private OpenCartridge? _open;

    /// <summary>The folder those banks belong to — remembered because an editor session does not carry its own path.</summary>
    private string? EditorFolder => _open?.Folder;

    /// <summary>
    /// True once Escape meant "leave the process". The shell polls this and calls
    /// <c>Game.Exit()</c>; the machine cannot end the process itself and must not try —
    /// the window's own exit path (save, audio report, dispose) has to run.
    /// </summary>
    public bool ExitRequested { get; private set; }

    /// <summary>
    /// What the library screen should say about the last failed launch, or null. A broken cart
    /// stays in the list and reports here instead of crashing the shell: the author who is
    /// mid-edit on that cart needs the message, not a dead window.
    /// </summary>
    public string? LibraryMessage { get; private set; }

    /// <summary>One Escape press, resolved per mode — see the type comment for why they differ.</summary>
    public void HandleEscape()
    {
        switch (Mode)
        {
            case ShellMode.Game:
                // M9 stage 5: Esc no longer leaves a game — it raises the pause menu over the
                // frame, and pressing it again is the menu's own RESUME. The two roads out of a
                // cartridge are now that menu's Exit and the tab strip, and neither of them is a
                // key the player's thumb finds by accident. The direct launch gets the identical
                // treatment (Р5): it used to quit the process here, which is exactly what locked
                // `quarp run <cart>` out of every editor this milestone built.
                TogglePauseMenu();
                break;
            case ShellMode.Menu when Menu.Phase == MenuPhase.Intro:
                // Esc is "any key" here like every other key: it cuts the intro, it does not
                // quit — nobody presses Esc during a boot animation to leave the console.
                Menu.SkipIntro();
                break;
            case ShellMode.Menu when Menu.Phase == MenuPhase.NameEntry:
                Menu.CancelNameEntry();
                break;
            case ShellMode.Library:
                // Back out the way the author came in (ADR-028); the menu is the root now.
                LibraryMessage = null;
                Menu.Message = null;
                Mode = ShellMode.Menu;
                break;
            case ShellMode.Editor:
                // With a cartridge running this is the way back to it and nothing else (see
                // EscapeReturnsToTheGame). With nothing running the session judges (clean closes,
                // dirty raises or lowers its prompt); the machine only executes the verdict — and
                // then asks the OTHER open bank the same question, because leaving the editor must
                // not drop a dirty map that happens to be on the tab the author is not looking at.
                if (EscapeReturnsToTheGame(Editor!.ExitPromptShown))
                {
                    break;
                }
                if (Editor!.RequestClose())
                {
                    CloseAfterSheetResolved();
                }
                break;
            case ShellMode.MapEditor:
                if (EscapeReturnsToTheGame(MapView!.ExitPromptShown))
                {
                    break;
                }
                if (MapView!.RequestClose(MapEditor!))
                {
                    CloseAfterMapResolved();
                }
                break;
            case ShellMode.CodeEditor:
                if (EscapeReturnsToTheGame(CodeView!.ExitPromptShown))
                {
                    break;
                }
                if (CodeView!.RequestClose(CodeEditor!))
                {
                    CloseAfterCodeResolved();
                }
                break;
            case ShellMode.SfxEditor:
                if (EscapeReturnsToTheGame(SfxView!.ExitPromptShown))
                {
                    break;
                }
                if (SfxView!.RequestClose(SfxEditor!))
                {
                    CloseAfterSfxResolved();
                }
                break;
            case ShellMode.MusicEditor:
                if (EscapeReturnsToTheGame(MusicView!.ExitPromptShown))
                {
                    break;
                }
                if (MusicView!.RequestClose(MusicEditor!))
                {
                    CloseAfterMusicResolved();
                }
                break;
            default:
                // The menu at rest: leave the process. A session, if one somehow still stands,
                // is deliberately left alone — QuarpGame's OnExiting/Dispose path saves and
                // unloads it, same as it always has.
                ExitRequested = true;
                break;
        }
    }

    /// <summary>
    /// The half of <c>Esc</c> that is the same on all five editor screens: <b>with a cartridge
    /// running it is the way back to the game tab, and it destroys nothing.</b>
    ///
    /// <para>This is a fix of stage 5's own making. Esc in an editor still ran the road it ran
    /// when a session could not survive a trip to the editors — the close chain, which ends by
    /// letting the cartridge go — so the key that used to mean "back to the library" quietly killed
    /// a <em>live</em> session, input log, time machine and all, from a screen the author had
    /// reached with the game paused behind it. The way out of a cartridge is the pause menu's EXIT
    /// (which asks about unsaved work first); Esc here is travel, not a door.</para>
    ///
    /// <para>A raised exit prompt outranks it, on all five screens: there Esc already means "stay",
    /// and answering a question by leaving the screen it is asked on would lose it. With nothing
    /// running the old road is the whole road — the editors were opened from the library, and the
    /// library is where they close to.</para>
    /// </summary>
    /// <param name="promptShown">Whether this screen is already asking about its unsaved work.</param>
    /// <returns>True when Escape has been dealt with by travelling; false to let the screen judge.</returns>
    private bool EscapeReturnsToTheGame(bool promptShown)
    {
        if (promptShown || Session is null)
        {
            return false;
        }
        SwitchEditorTab(ShellMode.Game);
        return true;
    }

    // --- the pause menu (M9 stage 5) ---

    /// <summary>
    /// Esc on the game screen, both ways: raise the menu and stop the simulation, or lower it
    /// and let the simulation go. Stopping and raising are one act on purpose — a menu that was
    /// up over a running game would be exactly the moving target Р1 forbids, and a pause with no
    /// menu on it would be the Space key, which already exists.
    /// </summary>
    public void TogglePauseMenu()
    {
        if (Mode != ShellMode.Game)
        {
            return;
        }
        if (PauseMenu.Shown)
        {
            if (Session is null)
            {
                // Nothing to lower it onto. With no cartridge behind it this menu is not an
                // overlay, it IS the game screen (Р7: START and EXIT), so "close" would leave a
                // black screen whose only keys — the tabs — this router only offers while the menu
                // is up. Esc therefore does nothing here; EXIT is the row that leaves.
                return;
            }
            ResumeFromPauseMenu();
            return;
        }
        Session?.PauseForEditing();
        PauseMenu.Open(Session is not null);
    }

    /// <summary>
    /// Enter / Z on the menu, and a click on a row. Returns the session it just started, or
    /// null — the same shape <see cref="LaunchSelected"/> has, and for the same reason: the
    /// window has wiring to do (speaker, title, tick accumulator) that this class must not know
    /// about. Every other verb answers null because it started nothing.
    /// </summary>
    public CartSession? ActivatePauseMenuItem()
    {
        if (Mode != ShellMode.Game || !PauseMenu.Shown)
        {
            return null;
        }
        switch (PauseMenu.Current)
        {
            case PauseMenuItem.Resume:
                return Session is null ? StartGameFromEditor() : ResumeFromPauseMenu();
            case PauseMenuItem.StepBack:
                Session?.ApplyCommands(new ShellCommands { StepBack = true });
                return null;
            case PauseMenuItem.StepForward:
                Session?.ApplyCommands(new ShellCommands { StepForward = true });
                return null;
            case PauseMenuItem.Rewind:
                Session?.JumpTicks(-PauseMenu.JumpTicks);
                return null;
            case PauseMenuItem.Ahead:
                Session?.JumpTicks(PauseMenu.JumpTicks);
                return null;
            default:
                LeaveGame();
                return null;
        }
    }

    /// <summary>Lowers the menu and lets the cartridge run again — RESUME, and Esc on an open menu.</summary>
    private CartSession? ResumeFromPauseMenu()
    {
        PauseMenu.Close();
        Session?.Resume();
        return null;
    }

    /// <summary>
    /// START: the author walked into the editors from the library, pressed F1, and there is no
    /// cartridge behind the menu (Р7). Launches the very folder the editor banks belong to, so
    /// the game that appears is the one being edited — including every change already saved to
    /// disk. Failures land on <see cref="LibraryMessage"/> and leave the menu up, exactly as a
    /// failed library launch leaves the library up.
    /// </summary>
    private CartSession? StartGameFromEditor()
    {
        if (EditorFolder is not string folder)
        {
            return null;
        }
        try
        {
            CartSession session = _startSession(folder);
            Session = session;
            PauseMenu.Close();
            LibraryMessage = null;
            return session;
        }
        catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
        {
            LibraryMessage = $"{Path.GetFileName(folder)}: {FirstLine(e.Message)}";
            return null;
        }
    }

    /// <summary>
    /// EXIT on the pause menu — the one door out of a cartridge, and (with a cartridge change)
    /// one of the only two places in this shell that disposes a session.
    ///
    /// <para><b>It asks about unsaved work first, with the editors' own question</b> (the one
    /// <c>Esc</c> in an editor asks): the tab strip means the author can be holding an unsaved
    /// sprite sheet or an unsaved line of code while standing on the game screen, and a door that
    /// dropped those silently would be the same door for a whole cartridge that the editors refuse
    /// to be for a single bank. One owner of "may I leave with this unsaved" —
    /// <see cref="RaiseDirtyBankPrompt"/> — so the answer cannot differ between the two roads that
    /// ask it. The leaving resumes at <see cref="FinishLeavingCartridge"/> once every open bank has
    /// been answered for.</para>
    /// </summary>
    public void LeaveGame()
    {
        if (Mode != ShellMode.Game)
        {
            return;
        }
        if (RaiseDirtyBankPrompt(resolved: null))
        {
            // The question is now up on the bank's own screen, and the game screen is no longer
            // the one on show — so the menu comes down here for the same reason the tab strip
            // lowers it. The session stays paused: only RESUME ever starts it again.
            PauseMenu.Close();
            return;
        }
        FinishLeavingCartridge();
    }

    /// <summary>
    /// Every open bank has answered for itself; now the cartridge is actually left. A direct launch
    /// leaves the <b>process</b>, because that is what <c>quarp run &lt;cart&gt;</c> means and the
    /// author's F5 loop must not end in a library it never asked for — and there is no library to
    /// go back to in a direct launch, which is why this is the end of both roads there. Anything
    /// else goes back to the library it came from.
    /// </summary>
    private void FinishLeavingCartridge()
    {
        PauseMenu.Close();
        if (_directLaunch)
        {
            // The session stays standing: QuarpGame's OnExiting/Dispose path saves and unloads
            // it, exactly as it did when Esc meant this.
            ExitRequested = true;
            return;
        }
        ReturnToLibrary();
    }

    /// <summary>
    /// One frame's worth of "did the cartridge on disk change" for a session that is <b>not</b>
    /// being ticked — which, since the game became a tab, is every frame the author spends in an
    /// editor. This is the whole of the stage's save rule (Р2) at this layer: <c>Ctrl+S</c>
    /// anywhere writes a file, the file is what <see cref="CartWatcher"/> watches, and the
    /// rebuild that follows is <see cref="TimeMachine.Rebuild"/> — same input log, new code and
    /// banks, the same tick.
    ///
    /// <para>Called unconditionally by the window once a frame; the guard is here rather than
    /// there so a headless test can drive the rule. On the game screen it does nothing, because
    /// <see cref="CartSession.Update(int, InputState, bool)"/> polls the watcher itself and
    /// polling twice would rebuild twice.</para>
    /// </summary>
    public void PollSessionReload()
    {
        if (Mode != ShellMode.Game)
        {
            Session?.PollReload();
        }
    }

    /// <summary>
    /// Starts the cart under the library's selection bar. Returns the new session (so the
    /// shell can wire it up) or null when there was nothing to launch or the launch failed —
    /// failure lands in <see cref="LibraryMessage"/>, never in an exception, because the
    /// library must survive every cart it lists.
    /// </summary>
    public CartSession? LaunchSelected()
    {
        if (Mode != ShellMode.Library || Library.Selected is not CartLibraryEntry entry)
        {
            return null;
        }
        try
        {
            CartSession session = _startSession(entry.Path);
            // The cartridge change of Р4, stated rather than assumed: whatever was open goes
            // before the new one arrives. Standing on the library screen there is nothing left to
            // release (that is what got the author here), so today this releases nothing — and it
            // is the line that keeps that true, because "launch a second cart over the first" is
            // the shape the two-cartridge defect took.
            ReleaseCartridge();
            Session = session;
            Mode = ShellMode.Game;
            PauseMenu.Close();      // a cart launched from the library RUNS; the menu is Esc's
            LibraryMessage = null;
            return session;
        }
        catch (CartLoadException e)
        {
            LibraryMessage = $"{entry.Name}: {FirstLine(e.Message)}";
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LibraryMessage = $"{entry.Name}: {FirstLine(e.Message)}";
            return null;
        }
    }

    /// <summary>
    /// X in the library: opens the sprite editor on the selected cart's own sheet. Folder
    /// carts only — a .quarp8 is a sealed package, and the honest answer is a library line
    /// <em>before</em> any editing, not a surprise at save time (work order: unpacking is not
    /// this milestone). A cart with no gfx.png opens as an empty sheet — that is snake, and
    /// it is the normal path, not an error. Load failures (corrupt PNG, unreadable file)
    /// report exactly like a failed launch: the library survives every cart it lists.
    /// </summary>
    public void OpenEditor()
    {
        if (Mode != ShellMode.Library || Library.Selected is not CartLibraryEntry entry)
        {
            return;
        }
        if (!Directory.Exists(entry.Path))
        {
            LibraryMessage = "read-only: unpack to a folder to edit";
            return;
        }
        try
        {
            _open = new OpenCartridge(entry.Path, new SpriteEditorSession(entry.Path));
            Mode = ShellMode.Editor;
            LibraryMessage = null;
        }
        catch (CartLoadException e)
        {
            LibraryMessage = $"{entry.Name}: {FirstLine(e.Message)}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LibraryMessage = $"{entry.Name}: {FirstLine(e.Message)}";
        }
    }

    /// <summary>Door 1 of the menu: the library, scanned fresh on the way in — same promise as every entry.</summary>
    public void OpenLibrary()
    {
        if (Mode != ShellMode.Menu || Menu.Phase != MenuPhase.Menu)
        {
            return;
        }
        Menu.Message = null;
        LibraryMessage = null;
        Mode = ShellMode.Library;
        Library.Rescan();
    }

    /// <summary>Door 3 of the menu: raises the name field; creation happens on <see cref="ConfirmCreateGame"/>.</summary>
    public void BeginCreateGame()
    {
        if (Mode == ShellMode.Menu)
        {
            Menu.BeginNameEntry();
        }
    }

    /// <summary>
    /// Enter in the name field: scaffold <c>carts/&lt;name&gt;</c> from the very template
    /// <c>quarp new</c> writes (<see cref="CartScaffold"/> — one owner both entrances call),
    /// then open the sprite editor on the newborn cart. Straight into the editor by the
    /// owner's decision on the boot-menu order: create-and-draw without leaving the console
    /// is the full-cycle promise of M9 stage 4. Refusals (bad name, name taken, disk trouble)
    /// land on the menu's message line and keep the field up — the author fixes the name
    /// instead of retyping it.
    /// </summary>
    public void ConfirmCreateGame()
    {
        if (Mode != ShellMode.Menu || Menu.Phase != MenuPhase.NameEntry)
        {
            return;
        }
        string name = Menu.NameText;
        if (!CartScaffold.IsValidName(name))
        {
            Menu.Message = "NAME: a-z 0-9 - _";
            return;
        }
        string root = Path.Combine(_createRoot, name);
        try
        {
            if (CartScaffold.CartridgeExists(root))
            {
                Menu.Message = $"{name}: ALREADY EXISTS";
                return;
            }
            CartScaffold.Create(root);
            // Best effort, like the CLI: a cartridge that exists outweighs IDE integration,
            // and the window has no terminal to show these on — stderr still tells the
            // author who launched from one.
            if (CartScaffold.TryWriteDevProject(root, out string? devWarning) is false && devWarning is not null)
            {
                Console.Error.WriteLine(devWarning);
            }
            if (CartScaffold.TryWriteVsCodeFiles(root, out string? vsCodeWarning) is false && vsCodeWarning is not null)
            {
                Console.Error.WriteLine(vsCodeWarning);
            }
            _open = new OpenCartridge(root, new SpriteEditorSession(root));
            Mode = ShellMode.Editor;
            Menu.CancelNameEntry();     // the menu the author eventually returns to is at rest
        }
        catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
        {
            Menu.Message = FirstLine(e.Message);
        }
    }

    /// <summary>
    /// A cartridge arriving by path — the menu's LOAD CART dialog or a file dropped into the
    /// window (both roads by the owner's decision, ADR-028; the drop is the niche's own way,
    /// PICO-8 manual). Works from the menu and from the library, the two screens where no
    /// session owns the window; a failure reports on the screen the author is looking at,
    /// and a mid-entry drop simply puts the name field away first. Returns the new session
    /// (so the shell wires sound and title, like <see cref="LaunchSelected"/>) or null.
    /// </summary>
    public CartSession? LoadCartFromPath(string path)
    {
        if (Mode is not (ShellMode.Menu or ShellMode.Library) || (Mode == ShellMode.Menu && Menu.Phase == MenuPhase.Intro))
        {
            return null;
        }
        Menu.CancelNameEntry();
        string name = Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        try
        {
            CartSession session = _startSession(path);
            ReleaseCartridge();     // the same cartridge change LaunchSelected makes, same reason
            Session = session;
            Mode = ShellMode.Game;
            PauseMenu.Close();
            Menu.Message = null;
            LibraryMessage = null;
            return session;
        }
        catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
        {
            string report = $"{name}: {FirstLine(e.Message)}";
            if (Mode == ShellMode.Library)
            {
                LibraryMessage = report;
            }
            else
            {
                Menu.Message = report;
            }
            return null;
        }
    }

    /// <summary>
    /// The six live tabs — <b>game</b>, code, sprites, tilemap, sound and music — clicked or
    /// keyed: the one door between the faces of one open cartridge, running one included
    /// (<see cref="EditorIcons.TabTarget"/> owns which button means which). Asking for the tab
    /// already on screen is the honest no-op the tab strip promises. Every session but the
    /// sheet's is born here, on first arrival, and a bank that will not load — a map.bin of the
    /// wrong length, an unreadable src/main.cs, an sfx.bin that breaks a rule of AUDIO-FORMAT
    /// §5 — reports the way a failed launch does instead of throwing the shell away.
    ///
    /// <para><b>Walking off the game tab pauses the cartridge</b> (M9 stage 5, Р1). The pause
    /// happens here, once, and only when the travel actually succeeded: a tab that refused to
    /// open (a corrupt bank, a sealed .quarp8) leaves the author on the game screen, and a game
    /// left paused by a journey that never happened would be a pause nobody asked for. What that
    /// refusal cannot yet do is <em>say</em> so on the game screen — see the note at the guard
    /// itself.</para>
    /// </summary>
    public void SwitchEditorTab(ShellMode target)
    {
        bool leavingGame = Mode == ShellMode.Game;
        SwitchEditorTabCore(target);
        if (leavingGame && Mode != ShellMode.Game)
        {
            Session?.PauseForEditing();
            PauseMenu.Close();
        }
    }

    private void SwitchEditorTabCore(ShellMode target)
    {
        if (Mode is not (ShellMode.Game or ShellMode.Editor or ShellMode.MapEditor
            or ShellMode.CodeEditor or ShellMode.SfxEditor or ShellMode.MusicEditor))
        {
            return;
        }
        if (target == ShellMode.Game)
        {
            EnterGameTab();
            return;
        }
        if (!EnsureSheetOpen())
        {
            // No cartridge to edit, or one that cannot be edited. The reason lands in
            // LibraryMessage, which the library screen prints — and that is a NAMED GAP when the
            // refusal happens on the GAME screen, which has no message line of its own: an author
            // who ran `quarp run game.quarp8` and pressed F2 sees the key do nothing. Sealed
            // packages are not editable in this milestone by decision (unpacking is not M9), so
            // the gap is a silent no-op rather than a wrong action; closing it wants a notice line
            // on the pause menu, which is a screen this stage did not build.
            return;
        }
        if (target == ShellMode.Editor)
        {
            Mode = ShellMode.Editor;
            return;
        }
        if (target == ShellMode.MapEditor)
        {
            if (MapEditor is null)
            {
                try
                {
                    _open!.Map = new MapEditorSession(_open.Folder);
                    _open.MapView = new MapEditorView();
                }
                catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
                {
                    LibraryMessage = $"{Editor!.CartName}: {FirstLine(e.Message)}";
                    return;     // stay put: a broken map must not take the open sprites with it
                }
            }
            Mode = ShellMode.MapEditor;
            return;
        }
        if (target == ShellMode.CodeEditor)
        {
            if (CodeEditor is null)
            {
                try
                {
                    // The same lazy birth and the same failure rule as the map's: an unreadable
                    // src/main.cs reports the way a failed launch does and leaves the tab the
                    // author is standing on exactly where it was.
                    _open!.Code = new CodeEditorSession(_open.Folder);
                    _open.CodeView = new CodeEditorView(TextClipboard);
                }
                catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
                {
                    LibraryMessage = $"{Editor!.CartName}: {FirstLine(e.Message)}";
                    return;
                }
            }
            Mode = ShellMode.CodeEditor;
            return;
        }
        if (target == ShellMode.SfxEditor)
        {
            if (EnsureSfxBank() is not null)
            {
                Mode = ShellMode.SfxEditor;
            }
            return;
        }
        if (target != ShellMode.MusicEditor)
        {
            return;
        }
        if (MusicEditor is null)
        {
            try
            {
                // The fifth and last lazy birth, same rule as the other four: a corrupt music.bin
                // — wrong magic, wrong length, a reserved bit somebody set — reports the way a
                // failed launch does and leaves the tab the author is standing on exactly where it
                // was. A cart with no music.bin at all opens silently, because an absent bank is
                // 64 empty patterns and not an error (AUDIO-FORMAT §1).
                _open!.Music = new MusicEditorSession(_open.Folder);
                _open.MusicView = new MusicEditorView();
            }
            catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
            {
                LibraryMessage = $"{Editor!.CartName}: {FirstLine(e.Message)}";
                return;
            }
        }
        Mode = ShellMode.MusicEditor;
    }

    /// <summary>
    /// Arriving on the game tab. The screen shows whatever frame the cartridge last drew, with
    /// the pause menu over it — because the simulation has been standing still since the author
    /// walked off (Р1), and a game that started running again the instant its tab came back
    /// would take the decision away from them. With no cartridge behind it the same menu appears
    /// with START on its first row (Р7).
    /// </summary>
    private void EnterGameTab()
    {
        if (Mode == ShellMode.Game)
        {
            return;         // the honest no-op every tab's own button is
        }
        Mode = ShellMode.Game;
        // Belt beside the brace — but only since the reload stopped clearing the pause. Until
        // that fix this call had a road with a real effect on it: an edit that arrived while the
        // author stood on an editor tab and whose new code could not survive the recorded past
        // fell back to restart mode, and the restart cleared CartSession's pause, so the game was
        // running by the time F1 brought its tab back and only this line stopped it. The pause is
        // the shell's to hold now (CartSession.TryReload puts it back), so today this call changes
        // nothing and removing it turns no test red — said out loud rather than left as a mystery.
        // It stays because "the game screen is entered by the strip with the simulation standing
        // still" is the rule, and the day a second road reaches this method is the day the rule
        // would otherwise be broken by an edit that looked unrelated.
        Session?.PauseForEditing();
        PauseMenu.Open(Session is not null);
    }

    /// <summary>
    /// The open sprite sheet, which every editor screen needs whether it draws sprites or not:
    /// the map screen paints its tiles from it, and the four sibling screens report their load
    /// failures through its cart name. It is born by <see cref="OpenEditor"/> when the author
    /// comes from the library — and, since M9 stage 5, here, when they come from a running game
    /// instead and no editor has been opened yet.
    ///
    /// <para>A sealed <c>.quarp8</c> answers false with the same message the library gives:
    /// unpacking is not this milestone, and the honest answer belongs <em>before</em> any
    /// editing rather than as a surprise at save time.</para>
    ///
    /// <para><b>An already-open cartridge is trusted, and that trust is what
    /// <see cref="ReleaseCartridge"/> pays for.</b> Nothing here compares the open folder with the
    /// running session's, because the two cannot disagree: a cartridge is opened as a whole and
    /// let go as a whole, so there is no state in which the banks belong to one cart and the
    /// session to another. That is precisely the invariant this shell did not have — the editor
    /// used to survive the trip to the library and open the previous cart's code over the next
    /// cart's game.</para>
    /// </summary>
    private bool EnsureSheetOpen()
    {
        if (_open is not null)
        {
            return true;
        }
        if (Session is not CartSession session)
        {
            return false;
        }
        if (!Directory.Exists(session.CartPath))
        {
            LibraryMessage = "read-only: unpack to a folder to edit";
            return false;
        }
        try
        {
            _open = new OpenCartridge(session.CartPath, new SpriteEditorSession(session.CartPath));
            return true;
        }
        catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
        {
            LibraryMessage = $"{session.Name}: {FirstLine(e.Message)}";
            return false;
        }
    }

    /// <summary>
    /// The open <c>sfx.bin</c>, born on demand — the fourth lazy birth, lifted out of
    /// <see cref="SwitchEditorTab"/> because it has a <b>second</b> caller now. A corrupt bank
    /// (wrong magic, wrong length, a step past the slot's end that is not the zero word) reports
    /// the way a failed launch does and answers null; a cart with no <c>sfx.bin</c> at all opens
    /// silently, because an absent bank is 64 empty slots and not an error (AUDIO-FORMAT §1).
    ///
    /// <para><b>Why the music screen's preview calls this.</b> A song is 64 patterns of
    /// <em>references</em> to SFX slots — there is not one note in <c>music.bin</c> — so a preview
    /// that loaded only the song would run the sequencer in silence. The two banks together are
    /// what makes a sound, and the one owner of the cart's <c>sfx.bin</c> while the editor is open
    /// is <see cref="SfxEditorSession"/>. So the audition asks for that owner rather than opening
    /// the file a second time: what the author hears is the sounds <em>as currently edited</em>,
    /// unsaved changes and all, which is the same promise the sound screen's own audition makes.
    /// Being born here costs nothing on disk — a clean session writes no file (that is
    /// <see cref="SfxEditorSession.Save"/>'s contract) and a clean bank raises no exit
    /// question.</para>
    /// </summary>
    /// <returns>The open effects bank, or null when it could not be read.</returns>
    public SfxEditorSession? EnsureSfxBank()
    {
        if (SfxEditor is not null)
        {
            return SfxEditor;
        }
        if (_open is null)
        {
            return null;        // no cartridge is open: nothing to read a bank out of
        }
        try
        {
            _open.Sfx = new SfxEditorSession(_open.Folder);
            _open.SfxView = new SfxEditorView();
        }
        catch (Exception e) when (e is CartLoadException or IOException or UnauthorizedAccessException)
        {
            LibraryMessage = $"{Editor!.CartName}: {FirstLine(e.Message)}";
            return null;
        }
        return SfxEditor;
    }

    /// <summary>The keyboard half of the tab strip: Home flips between the two GRAPHICS faces. The code, sound and music screens are reached by Alt+Left/Right, which is the ring that holds all five stops.</summary>
    public void ToggleEditorTab() =>
        SwitchEditorTab(Mode == ShellMode.MapEditor ? ShellMode.Editor : ShellMode.MapEditor);

    /// <summary>
    /// Alt+Left / Alt+Right: one step along the live tab strip, wrapping. The list is
    /// <see cref="EditorIcons.LiveEditorTabs"/> — the view layer's own order, left to right, so
    /// the key walks the tabs in the order the eye reads them. Wrapping rather than stopping,
    /// because with five stops a strip that stops at the ends costs eight presses to cross and a
    /// wrapping one costs one; the ends of a <em>ring</em> are not ends.
    /// </summary>
    public void CycleEditorTab(int direction)
    {
        if (Mode is not (ShellMode.Game or ShellMode.Editor or ShellMode.MapEditor
            or ShellMode.CodeEditor or ShellMode.SfxEditor or ShellMode.MusicEditor))
        {
            return;
        }
        IReadOnlyList<ShellMode> tabs = EditorIcons.LiveEditorTabs;
        int at = 0;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i] == Mode)
            {
                at = i;
                break;
            }
        }
        int step = Math.Sign(direction);
        SwitchEditorTab(tabs[((at + step) % tabs.Count + tabs.Count) % tabs.Count]);
    }

    /// <summary>
    /// Z on the sheet's exit prompt: save, then leave — but only if the save really landed;
    /// a failed write keeps the editor (and the author's pixels) alive with the error in the
    /// footer. Guarded to the prompt because a bare Z has no exit meaning in the editor.
    /// </summary>
    public void SaveEditorAndClose()
    {
        if (Mode != ShellMode.Editor || Editor is not { ExitPromptShown: true } editor)
        {
            return;
        }
        if (editor.Save())
        {
            CloseAfterSheetResolved();
        }
    }

    /// <summary>X on the sheet's exit prompt: leave without saving — the disk stays byte-for-byte untouched.</summary>
    public void DiscardEditorAndClose()
    {
        if (Mode != ShellMode.Editor || Editor is not { ExitPromptShown: true })
        {
            return;
        }
        CloseAfterSheetResolved();
    }

    /// <summary>Z on the map's exit prompt — the map half of <see cref="SaveEditorAndClose"/>, same failure rule.</summary>
    public void SaveMapAndClose()
    {
        if (Mode != ShellMode.MapEditor || MapView is not { ExitPromptShown: true })
        {
            return;
        }
        if (MapEditor!.Save())
        {
            MapView.CloseExitPrompt();
            CloseAfterMapResolved();
        }
    }

    /// <summary>X on the map's exit prompt: leave the cells unsaved — map.bin stays byte-for-byte untouched.</summary>
    public void DiscardMapAndClose()
    {
        if (Mode != ShellMode.MapEditor || MapView is not { ExitPromptShown: true })
        {
            return;
        }
        MapView.CloseExitPrompt();
        CloseAfterMapResolved();
    }

    /// <summary>Z on the code's exit prompt — the text half of <see cref="SaveEditorAndClose"/>, same failure rule: a write that did not land keeps the editor and the author's lines alive with the error in the footer.</summary>
    public void SaveCodeAndClose()
    {
        if (Mode != ShellMode.CodeEditor || CodeView is not { ExitPromptShown: true })
        {
            return;
        }
        if (CodeEditor!.Save())
        {
            CodeView.CloseExitPrompt();
            CloseAfterCodeResolved();
        }
    }

    /// <summary>X on the code's exit prompt: leave the text unsaved — <c>src/main.cs</c> stays byte-for-byte untouched, and a cart that never had one still does not.</summary>
    public void DiscardCodeAndClose()
    {
        if (Mode != ShellMode.CodeEditor || CodeView is not { ExitPromptShown: true })
        {
            return;
        }
        CodeView.CloseExitPrompt();
        CloseAfterCodeResolved();
    }

    /// <summary>Z on the sound screen's exit prompt — the bank half of <see cref="SaveEditorAndClose"/>, same failure rule: a write that did not land keeps the editor and the author's notes alive with the error in the footer.</summary>
    public void SaveSfxAndClose()
    {
        if (Mode != ShellMode.SfxEditor || SfxView is not { ExitPromptShown: true })
        {
            return;
        }
        if (SfxEditor!.Save())
        {
            SfxView.CloseExitPrompt();
            CloseAfterSfxResolved();
        }
    }

    /// <summary>X on the sound screen's exit prompt: leave the notes unsaved — <c>sfx.bin</c> stays byte-for-byte untouched, and a cart that never had one still does not.</summary>
    public void DiscardSfxAndClose()
    {
        if (Mode != ShellMode.SfxEditor || SfxView is not { ExitPromptShown: true })
        {
            return;
        }
        SfxView.CloseExitPrompt();
        CloseAfterSfxResolved();
    }

    /// <summary>Z on the music screen's exit prompt — the song half of <see cref="SaveEditorAndClose"/>, same failure rule: a write that did not land keeps the editor and the author's patterns alive with the error in the footer.</summary>
    public void SaveMusicAndClose()
    {
        if (Mode != ShellMode.MusicEditor || MusicView is not { ExitPromptShown: true })
        {
            return;
        }
        if (MusicEditor!.Save())
        {
            MusicView.CloseExitPrompt();
            CloseAfterMusicResolved();
        }
    }

    /// <summary>X on the music screen's exit prompt: leave the patterns unsaved — <c>music.bin</c> stays byte-for-byte untouched, and a cart that never had one still does not.</summary>
    public void DiscardMusicAndClose()
    {
        if (Mode != ShellMode.MusicEditor || MusicView is not { ExitPromptShown: true })
        {
            return;
        }
        MusicView.CloseExitPrompt();
        CloseAfterMusicResolved();
    }

    /// <summary>The sheet's half of the exit is settled — now every other open bank's.</summary>
    private void CloseAfterSheetResolved() => CloseUnlessAnotherBankIsDirty(ShellMode.Editor);

    /// <summary>The map's half is settled.</summary>
    private void CloseAfterMapResolved() => CloseUnlessAnotherBankIsDirty(ShellMode.MapEditor);

    /// <summary>The code's half is settled.</summary>
    private void CloseAfterCodeResolved() => CloseUnlessAnotherBankIsDirty(ShellMode.CodeEditor);

    /// <summary>The sound bank's half is settled.</summary>
    private void CloseAfterSfxResolved() => CloseUnlessAnotherBankIsDirty(ShellMode.SfxEditor);

    /// <summary>The song's half is settled.</summary>
    private void CloseAfterMusicResolved() => CloseUnlessAnotherBankIsDirty(ShellMode.MusicEditor);

    /// <summary>
    /// One bank of the open cartridge has just been answered for; if any <em>other</em> open
    /// bank is still dirty, come to the front of that one and ask there, and only when every
    /// bank is settled does the editor close. The author is shown what is unsaved <b>where</b>,
    /// rather than being asked about pixels or lines they cannot see — which is the whole reason
    /// the three tabs share one exit.
    ///
    /// <para>One method rather than three, because the rule is one rule: with two banks it was
    /// already stated twice, and a third would have made "ask the others" a thing that exists in
    /// three places and can differ in three ways. The order the others are asked in is the tab
    /// strip's own, so the questions arrive left to right.</para>
    /// </summary>
    private void CloseUnlessAnotherBankIsDirty(ShellMode resolved)
    {
        if (RaiseDirtyBankPrompt(resolved))
        {
            return;
        }
        FinishLeavingCartridge();
    }

    /// <summary>
    /// Comes to the front of the first open bank that is still unsaved and raises its exit
    /// question there, or answers false when every bank is settled. <b>The one owner of "may this
    /// cartridge be left with unsaved work in it"</b>, asked by both roads that leave one: the last
    /// answered editor prompt (<see cref="CloseUnlessAnotherBankIsDirty"/>) and the pause menu's
    /// EXIT (<see cref="LeaveGame"/>), which before this fix walked straight past every open bank
    /// and dropped it. <paramref name="resolved"/> is the bank that has just answered — skipped, so
    /// the same question is not asked of it twice — or null when the asking has just begun.
    ///
    /// <para>The order is the tab strip's own, so the questions arrive left to right, and a prompt
    /// that is already up is left alone rather than raised again: <c>RequestClose</c> on a raised
    /// prompt means "stay", which would lower the very question being asked.</para>
    /// </summary>
    private bool RaiseDirtyBankPrompt(ShellMode? resolved)
    {
        if (resolved != ShellMode.Editor && Editor is { IsDirty: true } sheet)
        {
            Mode = ShellMode.Editor;
            if (!sheet.ExitPromptShown)
            {
                sheet.RequestClose();           // dirty and down ⇒ this raises it, exactly once
            }
            return true;
        }
        if (resolved != ShellMode.MapEditor && MapEditor is { IsDirty: true } map)
        {
            Mode = ShellMode.MapEditor;
            if (!MapView!.ExitPromptShown)
            {
                MapView.RequestClose(map);
            }
            return true;
        }
        if (resolved != ShellMode.CodeEditor && CodeEditor is { IsDirty: true } code)
        {
            Mode = ShellMode.CodeEditor;
            if (!CodeView!.ExitPromptShown)
            {
                CodeView.RequestClose(code);
            }
            return true;
        }
        if (resolved != ShellMode.SfxEditor && SfxEditor is { IsDirty: true } sfx)
        {
            Mode = ShellMode.SfxEditor;
            if (!SfxView!.ExitPromptShown)
            {
                SfxView.RequestClose(sfx);
            }
            return true;
        }
        if (resolved != ShellMode.MusicEditor && MusicEditor is { IsDirty: true } music)
        {
            Mode = ShellMode.MusicEditor;
            if (!MusicView!.ExitPromptShown)
            {
                MusicView.RequestClose(music);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// The transition this milestone stage exists for — the cartridge is left and the library
    /// comes back — in an order the lifecycle tests pin:
    ///
    /// <list type="number">
    ///   <item>drain the audio device — the queued tail of the game dies on the same frame as
    ///     the keypress, before anything slower runs;</item>
    ///   <item><see cref="CartSession.Dispose"/> — which flushes the unsaved tail of save.dat
    ///     (<c>SaveIfDirty(force: true)</c>), stops the file watcher and unloads the cart's
    ///     collectible AssemblyLoadContext;</item>
    ///   <item>drop the references — the session, its TimeMachine, the cartridge instance and
    ///     every open editor bank become unreachable together, which is what actually lets the
    ///     load context be collected: <c>Unload()</c> alone only asks;</item>
    ///   <item>rescan the library, so the list reflects the disk as it is now, and put the bar on
    ///     the cart just left whatever moved around it — including a cart born in the menu's
    ///     CREATE GAME, which the rescan's own keep-by-path cannot know about because a newborn
    ///     cart was never the previous selection.</item>
    /// </list>
    ///
    /// <para>Both roads out of a cartridge end here: the pause menu's EXIT and the last answered
    /// exit prompt of an editor opened with nothing running.</para>
    /// </summary>
    private void ReturnToLibrary()
    {
        string? left = ReleaseCartridge();
        // The message is NOT cleared here, and that is the whole point of the line's absence.
        // A broken bank is reported by SwitchEditorTabCore and EnsureSfxBank into LibraryMessage,
        // and the library screen is the only screen that prints it (LibraryRenderer): clearing it
        // on the way there would mean the author never sees why the tab refused to open. Every
        // path that starts something new clears it already — launching a cart, opening the
        // editor, entering the library from the menu — so a stale message cannot survive an
        // action; only the report about the action that just failed does.
        Mode = ShellMode.Library;
        Library.Rescan();
        if (left is not null)
        {
            Library.SelectPath(left);
        }
    }

    /// <summary>
    /// <b>Letting a cartridge go — the one owner of that act.</b> It is one act and not two: the
    /// running session and the five editor banks are faces of one open cartridge, and the defect
    /// this method exists to make unreachable was exactly a road that released one of them and
    /// forgot the other (game → EXIT dropped the session and left the editor standing on that
    /// cart, so the next cart launched from the library opened the previous one's code under F2
    /// and Ctrl+S wrote it there).
    ///
    /// <para>A no-op when nothing is open. The audio drain is a game's tail, so it happens only
    /// when a game is what is being let go; the library's own silence comes from the drain that
    /// already ran.</para>
    /// </summary>
    /// <returns>The folder that was open — the session's or the editors' — or null when neither was.</returns>
    private string? ReleaseCartridge()
    {
        string? folder = EditorFolder ?? Session?.CartPath;
        if (Session is CartSession session)
        {
            _drainAudio();
            session.Dispose();
            Session = null;
        }
        // The sprite view outlives the cartridge (see SpriteView), so the ONE thing in it that is
        // about a gesture rather than a preference is closed by hand: a drag left open by an
        // editor that went away must not still be open when the next cart's sheet appears.
        SpriteView.EndTileBlock();
        _open = null;
        PauseMenu.Close();
        return folder;
    }

    /// <summary>
    /// The cartridge the editors have open, as one object. Its point is its lifetime: every bank
    /// below is born lazily on the tab that needs it and dies with the whole, so the shell cannot
    /// end up holding one cartridge's sprites while another one runs. The sheet is not nullable
    /// because it is what "open" means — the four sibling screens report their load failures
    /// through its cart name, and the map screen paints its tiles out of it.
    /// </summary>
    private sealed class OpenCartridge
    {
        public OpenCartridge(string folder, SpriteEditorSession sheet)
        {
            Folder = folder;
            Sheet = sheet;
        }

        /// <summary>The cartridge folder every bank here was read from and will be written back to.</summary>
        public string Folder { get; }

        public SpriteEditorSession Sheet { get; }

        public MapEditorSession? Map { get; set; }

        public MapEditorView? MapView { get; set; }

        public CodeEditorSession? Code { get; set; }

        public CodeEditorView? CodeView { get; set; }

        public SfxEditorSession? Sfx { get; set; }

        public SfxEditorView? SfxView { get; set; }

        public MusicEditorSession? Music { get; set; }

        public MusicEditorView? MusicView { get; set; }
    }

    /// <summary>
    /// Compile failures arrive as multi-line diagnostics; the library has one text row for
    /// them. The first line names the file and position, which is enough to send the author
    /// to a terminal (`quarp build`) for the rest.
    /// </summary>
    private static string FirstLine(string message)
    {
        int cut = message.IndexOfAny(new[] { '\r', '\n' });
        return cut < 0 ? message : message[..cut];
    }
}

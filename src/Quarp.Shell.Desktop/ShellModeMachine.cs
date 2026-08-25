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
/// <para><b>Escape means different things on purpose</b> (work order, stage 1): a cart started
/// as <c>quarp run &lt;cart&gt;</c> is the author's F5 loop, and Esc quits the process like it
/// always has — the library must not wedge itself into that loop. A cart started from the
/// library returns to the library. Esc in the library returns to the boot menu (ADR-028; it
/// used to quit, before the menu existed to return to); Esc in the menu quits — except that
/// mid-intro it skips, and in the name field it cancels the field. Esc in the editor returns
/// to the library when the session is clean, and raises the session's footer prompt when it
/// is not — unsaved pixels leave only through an explicit Z (save) or X (discard), never
/// silently.</para>
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

    public ShellMode Mode { get; private set; }

    /// <summary>The running cartridge; non-null exactly while <see cref="Mode"/> is <see cref="ShellMode.Game"/>.</summary>
    public CartSession? Session { get; private set; }

    /// <summary>
    /// The open sprite sheet; non-null while <see cref="Mode"/> is <see cref="ShellMode.Editor"/>
    /// <b>or</b> <see cref="ShellMode.MapEditor"/> — the two tabs are two faces of one open
    /// cartridge, and the sheet stays alive (unsaved pixels and all) while the author is on the
    /// map tab. That is the stage-3 promise "there and back without losing unsaved work".
    /// </summary>
    public SpriteEditorSession? Editor { get; private set; }

    /// <summary>
    /// The open map of the same cart, created lazily by the first visit to the tilemap tab and
    /// then kept until the whole editor closes — so flipping tabs never costs an unsaved cell.
    /// Null until that first visit: a cart whose map is never opened must not get a session
    /// (and therefore cannot get a file) it never asked for.
    /// </summary>
    public MapEditorSession? MapEditor { get; private set; }

    /// <summary>The map screen's camera, cursor and exit prompt; non-null exactly while <see cref="MapEditor"/> is.</summary>
    public MapEditorView? MapView { get; private set; }

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
    /// session; and it survives <see cref="CloseEditor"/> on purpose, because a way of reading
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
    public CodeEditorSession? CodeEditor { get; private set; }

    /// <summary>The code screen's scroll, footer fields and exit prompt; non-null exactly while <see cref="CodeEditor"/> is.</summary>
    public CodeEditorView? CodeView { get; private set; }

    /// <summary>
    /// The open effects bank of the same cart's <c>sfx.bin</c>, created lazily by the first
    /// visit to the SOUND tab and then kept until the whole editor closes — so flipping tabs
    /// never costs an unsaved note. Null until that first visit: a cart whose sound is never
    /// opened must not get a session (and therefore cannot get an <c>sfx.bin</c>) it never
    /// asked for.
    /// </summary>
    public SfxEditorSession? SfxEditor { get; private set; }

    /// <summary>The sound screen's slot, cursor, pen, playback request and exit prompt; non-null exactly while <see cref="SfxEditor"/> is.</summary>
    public SfxEditorView? SfxView { get; private set; }

    /// <summary>
    /// The open song of the same cart's <c>music.bin</c>, created lazily by the first visit to
    /// the MUSIC tab and then kept until the whole editor closes — so flipping tabs never costs an
    /// unsaved pattern. Null until that first visit: a cart whose song is never opened must not
    /// get a session (and therefore cannot get a <c>music.bin</c>) it never asked for.
    /// </summary>
    public MusicEditorSession? MusicEditor { get; private set; }

    /// <summary>The music screen's window, mute table, playback request and exit prompt; non-null exactly while <see cref="MusicEditor"/> is.</summary>
    public MusicEditorView? MusicView { get; private set; }

    /// <summary>The folder both editor sessions belong to — remembered because a session does not carry its own path.</summary>
    private string? _editorFolder;

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
            case ShellMode.Game when !_directLaunch:
                LeaveGameForLibrary();
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
                // The session judges (clean closes, dirty raises or lowers its prompt);
                // the machine only executes the verdict — and then asks the OTHER open bank
                // the same question, because leaving the editor must not drop a dirty map that
                // happens to be on the tab the author is not looking at.
                if (Editor!.RequestClose())
                {
                    CloseAfterSheetResolved();
                }
                break;
            case ShellMode.MapEditor:
                if (MapView!.RequestClose(MapEditor!))
                {
                    CloseAfterMapResolved();
                }
                break;
            case ShellMode.CodeEditor:
                if (CodeView!.RequestClose(CodeEditor!))
                {
                    CloseAfterCodeResolved();
                }
                break;
            case ShellMode.SfxEditor:
                if (SfxView!.RequestClose(SfxEditor!))
                {
                    CloseAfterSfxResolved();
                }
                break;
            case ShellMode.MusicEditor:
                if (MusicView!.RequestClose(MusicEditor!))
                {
                    CloseAfterMusicResolved();
                }
                break;
            default:
                // A direct-launch game, or the menu at rest: leave the process. The session,
                // if any, is deliberately left standing — QuarpGame's OnExiting/Dispose path
                // saves and unloads it, same as it always has.
                ExitRequested = true;
                break;
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
            Session = session;
            Mode = ShellMode.Game;
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
            Editor = new SpriteEditorSession(entry.Path);
            _editorFolder = entry.Path;
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
            Editor = new SpriteEditorSession(root);
            _editorFolder = root;
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
            Session = session;
            Mode = ShellMode.Game;
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
    /// The four live tabs — sprites, tilemap, code and sound — clicked or keyed: the one door
    /// between the faces of one open cartridge (<see cref="EditorIcons.TabTarget"/> owns which
    /// button means which). Asking for the tab already on screen is the honest no-op the tab
    /// strip promises. Every session but the sheet's is born here, on first arrival, and a bank
    /// that will not load — a map.bin of the wrong length, an unreadable src/main.cs, an sfx.bin
    /// that breaks a rule of AUDIO-FORMAT §5 — reports the way a failed launch does instead of
    /// throwing the shell away.
    /// </summary>
    public void SwitchEditorTab(ShellMode target)
    {
        if (Mode is not (ShellMode.Editor or ShellMode.MapEditor or ShellMode.CodeEditor
            or ShellMode.SfxEditor or ShellMode.MusicEditor))
        {
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
                    MapEditor = new MapEditorSession(_editorFolder!);
                    MapView = new MapEditorView();
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
                    CodeEditor = new CodeEditorSession(_editorFolder!);
                    CodeView = new CodeEditorView(TextClipboard);
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
                MusicEditor = new MusicEditorSession(_editorFolder!);
                MusicView = new MusicEditorView();
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
        if (_editorFolder is null)
        {
            return null;        // no cartridge is open: nothing to read a bank out of
        }
        try
        {
            SfxEditor = new SfxEditorSession(_editorFolder);
            SfxView = new SfxEditorView();
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
        if (Mode is not (ShellMode.Editor or ShellMode.MapEditor or ShellMode.CodeEditor
            or ShellMode.SfxEditor or ShellMode.MusicEditor))
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
        if (resolved != ShellMode.Editor && Editor is { IsDirty: true } sheet)
        {
            Mode = ShellMode.Editor;
            if (!sheet.ExitPromptShown)
            {
                sheet.RequestClose();           // dirty and down ⇒ this raises it, exactly once
            }
            return;
        }
        if (resolved != ShellMode.MapEditor && MapEditor is { IsDirty: true } map)
        {
            Mode = ShellMode.MapEditor;
            if (!MapView!.ExitPromptShown)
            {
                MapView.RequestClose(map);
            }
            return;
        }
        if (resolved != ShellMode.CodeEditor && CodeEditor is { IsDirty: true } code)
        {
            Mode = ShellMode.CodeEditor;
            if (!CodeView!.ExitPromptShown)
            {
                CodeView.RequestClose(code);
            }
            return;
        }
        if (resolved != ShellMode.SfxEditor && SfxEditor is { IsDirty: true } sfx)
        {
            Mode = ShellMode.SfxEditor;
            if (!SfxView!.ExitPromptShown)
            {
                SfxView.RequestClose(sfx);
            }
            return;
        }
        if (resolved != ShellMode.MusicEditor && MusicEditor is { IsDirty: true } music)
        {
            Mode = ShellMode.MusicEditor;
            if (!MusicView!.ExitPromptShown)
            {
                MusicView.RequestClose(music);
            }
            return;
        }
        CloseEditor();
    }

    /// <summary>
    /// Editor → library. The rescan mirrors <see cref="LeaveGameForLibrary"/>: carts appear
    /// and disappear while one is being edited, and the bar must land on the cart just edited
    /// whatever moved around it.
    /// </summary>
    private void CloseEditor()
    {
        string? edited = _editorFolder;
        Editor = null;
        // The sprite view outlives the session (see SpriteView), so the ONE thing in it that is
        // about a gesture rather than a preference is closed by hand: a drag left open by an
        // editor that went away must not still be open when the next cart's sheet appears.
        SpriteView.EndTileBlock();
        MapEditor = null;
        MapView = null;
        CodeEditor = null;
        CodeView = null;
        SfxEditor = null;
        SfxView = null;
        MusicEditor = null;
        MusicView = null;
        _editorFolder = null;
        Mode = ShellMode.Library;
        Library.Rescan();
        if (edited is not null)
        {
            // The bar lands on the cart just edited even when the editor was opened from the
            // menu's CREATE GAME — the rescan's own keep-by-path only knows the previous
            // selection, and a newborn cart never was one.
            Library.SelectPath(edited);
        }
    }

    /// <summary>
    /// The transition this milestone stage exists for, in an order the lifecycle tests pin:
    ///
    /// <list type="number">
    ///   <item>drain the audio device — the queued tail of the game dies on the same frame as
    ///     the keypress, before anything slower runs;</item>
    ///   <item><see cref="CartSession.Dispose"/> — which flushes the unsaved tail of save.dat
    ///     (<c>SaveIfDirty(force: true)</c>), stops the file watcher and unloads the cart's
    ///     collectible AssemblyLoadContext;</item>
    ///   <item>drop the reference — the session, its TimeMachine and the cartridge instance
    ///     become unreachable together, which is what actually lets the load context be
    ///     collected: <c>Unload()</c> alone only asks;</item>
    ///   <item>rescan the library, so the list reflects the disk as it is now and the bar
    ///     stays on the cart just played.</item>
    /// </list>
    /// </summary>
    private void LeaveGameForLibrary()
    {
        _drainAudio();
        Session!.Dispose();
        Session = null;
        LibraryMessage = null;
        Mode = ShellMode.Library;
        Library.Rescan();
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

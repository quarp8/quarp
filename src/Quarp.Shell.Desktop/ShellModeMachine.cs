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
/// library returns to the library. Esc in the library quits; Esc in the editor returns to the
/// library when the session is clean, and raises the session's footer prompt when it is not —
/// unsaved pixels leave only through an explicit Z (save) or X (discard), never silently.</para>
/// </summary>
public sealed class ShellModeMachine
{
    private readonly Func<string, CartSession> _startSession;
    private readonly Action _drainAudio;
    private readonly bool _directLaunch;

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
        CartSession? directSession = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(startSession);
        ArgumentNullException.ThrowIfNull(drainAudio);
        Library = library;
        _startSession = startSession;
        _drainAudio = drainAudio;
        if (directSession is not null)
        {
            Session = directSession;
            Mode = ShellMode.Game;
            _directLaunch = true;
        }
        else
        {
            Mode = ShellMode.Library;
            library.Rescan();
        }
    }

    /// <summary>The list the library screen shows; scanned on every entry into the library.</summary>
    public CartLibrary Library { get; }

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
            default:
                // A direct-launch game or the library itself: leave the process. The session,
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

    /// <summary>
    /// The tilemap and sprites tabs, clicked or keyed — the one door between the editor's two
    /// faces (<see cref="EditorIcons.TabTarget"/> owns which button means which). Asking for
    /// the tab already on screen is the honest no-op the tab strip promises. The map session is
    /// born here, on first arrival, and a cart whose map.bin is the wrong length reports the way
    /// a failed launch does instead of throwing the shell away.
    /// </summary>
    public void SwitchEditorTab(ShellMode target)
    {
        if (Mode is not (ShellMode.Editor or ShellMode.MapEditor))
        {
            return;
        }
        if (target == ShellMode.Editor)
        {
            Mode = ShellMode.Editor;
            return;
        }
        if (target != ShellMode.MapEditor)
        {
            return;
        }
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
                return;     // stay on the sheet: a broken map must not take the open sprites with it
            }
        }
        Mode = ShellMode.MapEditor;
    }

    /// <summary>The keyboard half of the tab strip: one key flips between the two open faces.</summary>
    public void ToggleEditorTab() =>
        SwitchEditorTab(Mode == ShellMode.MapEditor ? ShellMode.Editor : ShellMode.MapEditor);

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

    /// <summary>
    /// The sheet's half of the exit is settled — now the map's. A dirty map raises its own
    /// prompt on its own tab (the author is shown what is unsaved where, rather than being
    /// asked about pixels they cannot see), and only when both banks are settled does the
    /// editor close. This is the whole reason the two tabs share one exit.
    /// </summary>
    private void CloseAfterSheetResolved()
    {
        if (MapEditor is null || !MapEditor.IsDirty)
        {
            CloseEditor();
            return;
        }
        Mode = ShellMode.MapEditor;
        if (!MapView!.ExitPromptShown)
        {
            MapView.RequestClose(MapEditor);    // dirty and down ⇒ this raises it, exactly once
        }
    }

    /// <summary>The mirror: the map is settled, ask the sheet.</summary>
    private void CloseAfterMapResolved()
    {
        if (!Editor!.IsDirty)
        {
            CloseEditor();
            return;
        }
        Mode = ShellMode.Editor;
        if (!Editor.ExitPromptShown)
        {
            Editor.RequestClose();
        }
    }

    /// <summary>
    /// Editor → library. The rescan mirrors <see cref="LeaveGameForLibrary"/>: carts appear
    /// and disappear while one is being edited, and the bar must land on the cart just edited
    /// whatever moved around it.
    /// </summary>
    private void CloseEditor()
    {
        Editor = null;
        MapEditor = null;
        MapView = null;
        _editorFolder = null;
        Mode = ShellMode.Library;
        Library.Rescan();
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

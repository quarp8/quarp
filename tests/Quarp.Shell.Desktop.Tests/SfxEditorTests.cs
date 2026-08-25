using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The SOUND tab woken up: the bank's document contract (absent file is silence, a clean session
/// writes nothing, a round trip is byte-exact, a truncated file is refused by name), the piano
/// rows every reference console shares, the slot's three numbers and its loop, undo at one step
/// per action, the screen's geometry, travel between the four tabs, and the button-contract sweep
/// this project made law in wave 2g — <b>every button the layout places and the stub list does
/// not kill must, clicked through the real router pieces, change something observable</b>.
///
/// <para><b>Whole frames of the production router, with no window anywhere.</b> The harness is
/// <c>CodeEditorScreenTests</c>'s, minus the character stream this screen does not read: the real
/// <see cref="ShellCommandReader"/>, the real <see cref="EditorMouseReader"/>, the real
/// <see cref="SfxEditorLayout"/>, the real <see cref="ShellModeMachine"/>, entered the way the
/// shell really enters — <c>Menu.SkipIntro()</c>, <c>OpenLibrary()</c>, <c>OpenEditor()</c>, then
/// the SOUND tab.</para>
///
/// <para><b>carts/ is read, never written.</b> The pinned demo banks (carts/demo-goldens.tsv) are
/// copied into a temp folder before a session is allowed near them, exactly as
/// <c>MapEditorSessionTests</c> and <c>CodeEditorSessionTests</c> do.</para>
///
/// <para><b>What this file cannot test, and says so.</b> The audition's sound. Making a slot
/// audible needs <c>Quarp.Core.Audio.Apu</c> and <see cref="AudioOutput"/>, and the second of
/// those owns a device; the shell's arrangement therefore splits at
/// <see cref="SfxEditorView.PlayWanted"/> — asked for here, performed by <c>QuarpGame</c>. What
/// is pinned below is the half that can be: the request, its epoch and the button's two faces.
/// The other half is the boot jingle's already-shipped path with a different bank in it.</para>
/// </summary>
public class SfxEditorTests : IDisposable
{
    private readonly string _root;

    public SfxEditorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-sfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    /// <summary>The console's own screen — the surface the sprite editor is laid out on since wave R2.</summary>
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    /// <summary>One frame at 60 Hz — the router spends it on the tooltip clock only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    /// <summary>Far outside any rectangle the layout places — an idle pointer must hit nothing.</summary>
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));

    // ==================================================================================
    // The window, minus the window.
    // ==================================================================================

    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();
        private int _wheelTotal;

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        /// <summary>
        /// Rebuilt per frame, like the window's. Since wave R2 the two numbers are <b>the size
        /// of the surface the screen on show is laid out on</b> (ADR-029): 160x90, not the back
        /// buffer. <c>QuarpGame</c> makes exactly this switch — see <c>ConsoleEditorContext</c> —
        /// so a frame here means what a frame there means. The consequence for whoever writes a
        /// test against a console screen: <b>its mouse points are console pixels</b>, taken
        /// straight off the layout's own rectangles. Production reaches the same numbers by
        /// putting the window's point through <see cref="EditorMouse.ToConsole"/>, whose own
        /// arithmetic is pinned in <c>EditorMouseReaderTests</c> rather than re-run here.
        ///
        /// <para><b>Wave R4 turned the test round.</b> It used to name the one screen that had
        /// moved; four have now, and the SOUND screen — this file's own — is the one that has
        /// not. So the exception is written here instead of the rule. It matters beyond
        /// tidiness: this harness walks the tab strip, and the frames it spends on the sprite,
        /// map and code screens on the way through must hand each of those routers the surface
        /// it is actually laid out on.</para>
        /// </summary>
        internal EditorShell Context =>
            Modes.Mode == ShellMode.SfxEditor
                ? new(Modes, Flyout, Hover, SheetScroll, WindowWidth, WindowHeight)
                : new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight);

        internal SfxEditorLayout Layout => SfxEditorLayout.Compute(WindowWidth, WindowHeight);

        internal SfxEditorSession Session => Modes.SfxEditor!;

        internal SfxEditorView View => Modes.SfxView!;

        /// <summary>One whole frame through the production router for whichever editor is on screen.</summary>
        internal void Frame(Keys[] down, int mouseX, int mouseY, ButtonState left, ButtonState right)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, _wheelTotal, left, ButtonState.Released, right,
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
                    CodeEditorInput.Update(Context, commands, mouse, Array.Empty<char>(), FrameSeconds);
                    break;
                case ShellMode.SfxEditor:
                    SfxEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
            }
        }

        internal void Idle() =>
            Frame(NoKeys, Off, Off, ButtonState.Released, ButtonState.Released);

        /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
        internal void Tap(params Keys[] down)
        {
            Frame(down, Off, Off, ButtonState.Released, ButtonState.Released);
            Idle();
        }

        internal void LeftDown(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Pressed, ButtonState.Released);

        internal void LeftUp(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released);

        internal void Click(int x, int y)
        {
            LeftDown(x, y);
            LeftUp(x, y);
        }

        internal void RightClick(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Pressed);
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released);
        }

        internal void ClickRect(Rectangle rect) => Click(rect.Center.X, rect.Center.Y);

        /// <summary>
        /// Since wave R2 the sprite editor lives on the console's own 160x90 frame (ADR-029)
        /// while this screen is still on the host frame, so the two no longer place their tabs
        /// on the same pixels. The rectangle must come from the layout of the screen ON SHOW.
        /// </summary>
        /// <summary>
        /// The rectangle comes from the layout of the screen ON SHOW. The sound screen is the
        /// only one still measured against the window; every other screen this harness can be
        /// standing on is on the console, and its tabs are ten console pixels wide.
        /// </summary>
        internal void ClickButton(EditorButton button) => ClickRect(
            Modes.Mode switch
            {
                ShellMode.Editor =>
                    SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, Modes.Editor!.RegionCells)
                        .ButtonRect(button),
                ShellMode.MapEditor =>
                    MapEditorLayout.Compute(ConsoleWidth, ConsoleHeight).ButtonRect(button),
                ShellMode.CodeEditor =>
                    CodeEditorLayout.Compute(ConsoleWidth, ConsoleHeight).ButtonRect(button),
                _ => Layout.ButtonRect(button),
            });
    }

    // ==================================================================================
    // Fixtures — the road the shell really takes, menu → library → editor → SOUND tab.
    // ==================================================================================

    /// <summary>Walks up from the test bin folder to the repo root, same as its three sibling suites.</summary>
    private static string CartsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "carts");
            if (File.Exists(Path.Combine(candidate, "snake", "manifest.json")))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/ not found above the test directory");
    }

    private string NewCartFolder(byte[]? sfxFile = null, bool sfxSource = false)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"sound\",\"author\":\"\",\"profile\":8}");
        if (sfxFile is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, SfxEditorSession.SfxFileName), sfxFile);
        }
        if (sfxSource)
        {
            File.WriteAllText(Path.Combine(folder, SfxEditorSession.SfxSourceFileName), "# hand-written\n");
        }
        return folder;
    }

    /// <summary>
    /// A copy of one demo cart's <c>sfx.bin</c> in the temp tree — <b>only</b> that file, so the
    /// session is writable and a real round trip can be exercised. <c>carts/snake</c> itself is
    /// opened read-only and never written; copying the whole folder would drag its
    /// <c>sfx.txt</c> along, which by this editor's own rule would make the bank read-only (that
    /// rule has a test of its own below).
    /// </summary>
    private string CopyDemoBank(string cart)
    {
        string folder = NewCartFolder();
        File.Copy(
            Path.Combine(CartsRoot(), cart, SfxEditorSession.SfxFileName),
            Path.Combine(folder, SfxEditorSession.SfxFileName));
        File.SetAttributes(
            Path.Combine(folder, SfxEditorSession.SfxFileName), FileAttributes.Normal);
        return folder;
    }

    private static string SfxPath(string folder) =>
        Path.Combine(folder, SfxEditorSession.SfxFileName);

    /// <summary>The folder's file names, sorted — the whole of what "wrote nothing" has to mean.</summary>
    private static string[] FileNames(string folder) =>
        Directory.GetFiles(folder).Select(path => Path.GetFileName(path) ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private static ShellModeMachine MachineOnTheSoundTab(string cartFolder)
    {
        var machine = new ShellModeMachine(
            new CartLibrary(Path.GetDirectoryName(cartFolder)!),
            static path => CartSession.Start(path),
            static () => { });
        machine.Menu.SkipIntro();           // the real road since ADR-028: menu → library → editor
        machine.OpenLibrary();
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        machine.SwitchEditorTab(ShellMode.SfxEditor);
        Assert.Equal(ShellMode.SfxEditor, machine.Mode);
        return machine;
    }

    private Harness OpenSoundEditor(out string cartFolder, byte[]? sfxFile = null, bool sfxSource = false)
    {
        cartFolder = NewCartFolder(sfxFile, sfxSource);
        return new Harness(MachineOnTheSoundTab(cartFolder));
    }

    // ==================================================================================
    // 1. Absent sfx.bin is silence, and a clean session writes nothing.
    // ==================================================================================

    /// <summary>
    /// AUDIO-FORMAT §1's headline rule, in the editor: a cart with no <c>sfx.bin</c> opens as 64
    /// empty slots — not an error, not a message — and closing it again leaves the folder exactly
    /// as it was found. The file is created only by a dirty save, so visiting the SOUND tab
    /// cannot leave one behind.
    ///
    /// <para>Break recipe: make <c>SfxEditorSession.ReadPayload</c> throw when the file is
    /// missing and the first half goes red; drop the <c>if (!IsDirty) return true</c> guard from
    /// <see cref="SfxEditorSession.Save"/> and the second half does — an <c>sfx.bin</c> appears
    /// in a cart that never had sound.</para>
    /// </summary>
    [Fact]
    public void AnAbsentBankIsSixtyFourEmptySlotsAndACleanSessionWritesNothing()
    {
        Harness harness = OpenSoundEditor(out string folder);
        SfxEditorSession session = harness.Session;

        Assert.Equal(SfxEditorSession.SlotCount, 64);
        for (int slot = 0; slot < SfxEditorSession.SlotCount; slot++)
        {
            Assert.True(session.SlotIsEmpty(slot), $"slot {slot} is not empty");
            Assert.Equal(0, session.SlotSpeed(slot));
            Assert.Equal(0, session.SlotLoopStart(slot));
            Assert.Equal(0, session.SlotLoopEnd(slot));
            for (int step = 0; step < SfxEditorSession.StepCount; step++)
            {
                Assert.Equal(0, session.Step(slot, step));
            }
        }
        Assert.False(session.IsDirty);
        Assert.Null(session.SaveError);

        string[] before = FileNames(folder);

        Assert.True(session.Save());            // Ctrl+S on a clean bank
        harness.Tap(Keys.LeftControl, Keys.S);  // and the same through the real router
        harness.Tap(Keys.Escape);               // a clean exit walks straight back to the library

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Null(harness.Modes.SfxEditor);
        Assert.False(File.Exists(SfxPath(folder)));
        Assert.Equal(before, FileNames(folder));
    }

    // ==================================================================================
    // 2. Round trip against a real demo bank.
    // ==================================================================================

    /// <summary>
    /// The wave's headline guarantee on a real file: open <c>carts/snake/sfx.bin</c> (copied out
    /// of <c>carts/</c>, which is only ever read), touch nothing, save — and the bytes are the
    /// bytes. Then the writer proper: change one field, save, change it back, save, and the file
    /// is byte-identical to the one <c>quarp audio build</c> produced, which is what proves the
    /// editor is not quietly re-encoding a bank it merely understands.
    ///
    /// <para>Break recipe: have <see cref="SfxEditorSession.Save"/> build the file by hand
    /// instead of calling <see cref="AudioFormat.WriteSfxFile"/>, or drop a byte of the header,
    /// and the last comparison goes red; make the clean guard fall through and the first one
    /// still passes but the file's timestamp moves — which is why the timestamp is checked too.</para>
    /// </summary>
    [Fact]
    public void ARealBankSurvivesAnOpenAndSaveByteForByte()
    {
        string folder = CopyDemoBank("snake");
        byte[] original = File.ReadAllBytes(SfxPath(folder));
        Assert.Equal(AudioFormat.SfxFileSize, original.Length);
        DateTime stamp = File.GetLastWriteTimeUtc(SfxPath(folder));

        var session = new SfxEditorSession(folder);
        Assert.False(session.BankReadOnly);     // only sfx.bin was copied, so the bank is ours
        Assert.False(session.IsDirty);
        Assert.True(session.Save());

        Assert.Equal(original, File.ReadAllBytes(SfxPath(folder)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(SfxPath(folder)));

        // ...and the writer proper: out and back again through real edits.
        int speed = session.SlotSpeed(0);
        session.SetSpeed(0, speed == 9 ? 10 : 9);
        Assert.True(session.IsDirty);
        Assert.True(session.Save());
        Assert.NotEqual(original, File.ReadAllBytes(SfxPath(folder)));

        session.SetSpeed(0, speed);
        Assert.True(session.Save());
        Assert.Equal(original, File.ReadAllBytes(SfxPath(folder)));
        Assert.False(session.IsDirty);
    }

    /// <summary>
    /// The map's verdict applied to audio (M9 work order, "Решения организатора"): while
    /// <c>sfx.txt</c> lies in the folder the text source owns the bank, the editor says so out
    /// loud, and no key can write a byte. snake ships exactly that pair, which is why its demo
    /// bank has a second lock on it.
    ///
    /// <para>Break recipe: delete the <c>BankReadOnly</c> guard from
    /// <see cref="SfxEditorView.PlayPianoKey"/> and the dirt assertion goes red; delete the
    /// property's read of the file and the notice assertion does.</para>
    /// </summary>
    [Fact]
    public void ABankWithATextSourceIsReadOnlyAndSaysSo()
    {
        Harness harness = OpenSoundEditor(out _, sfxSource: true);

        Assert.True(harness.Session.BankReadOnly);
        Assert.NotNull(SfxEditorRenderer.StandingNotice(harness.Session));
        Assert.Contains(
            SfxEditorSession.SfxSourceFileName.ToUpperInvariant(),
            SfxEditorRenderer.StandingNotice(harness.Session)!,
            StringComparison.Ordinal);

        harness.Tap(Keys.Z);                    // the piano's C, on a bank nobody may write

        Assert.False(harness.Session.IsDirty);
        Assert.True(harness.Session.SlotIsEmpty(0));
        Assert.False(harness.Session.CanUndo);
        // The session's own door still slams — the guard above is politeness, this is the lock.
        Assert.Throws<InvalidOperationException>(() => harness.Session.SetSpeed(0, 4));
    }

    // ==================================================================================
    // 3. The piano rows.
    // ==================================================================================

    /// <summary>
    /// REFERENCES-EDITORS §8 item 17, in bits. The two rows are TIC-80's and PICO-8's letter for
    /// letter, a key enters a note at the cursor with the pen's other three fields, and the
    /// cursor moves on. The word is checked <b>bit by bit</b> against AUDIO-FORMAT §3's own
    /// layout rather than against <see cref="AudioFormat.PackStep"/>, so a packer that started
    /// shifting volume by 10 would be caught here and not merely agreed with.
    ///
    /// <para>Break recipe: reorder <c>ShellCommandReader.PianoRows</c> (put <c>x</c> before
    /// <c>s</c>) and the semitone assertions go red; drop the <c>StepCursor(1)</c> from
    /// <see cref="SfxEditorView.PlayPianoKey"/> and the cursor assertion does; change the pen's
    /// volume shift and the raw-word assertion does.</para>
    /// </summary>
    [Fact]
    public void APianoKeyWritesTheRightStepWordAndMovesTheCursor()
    {
        Harness harness = OpenSoundEditor(out _);
        SfxEditorSession session = harness.Session;
        SfxEditorView view = harness.View;

        int octave = view.Octave;
        int wave = view.PenWave;
        int volume = view.PenVolume;
        int effect = view.PenEffect;
        Assert.Equal(0, view.CursorStep);

        harness.Tap(Keys.Z);                    // the lower row's first key: C of the current octave

        int expectedNote = octave * 12;
        ushort expected = (ushort)(expectedNote | (wave << 6) | (volume << 9) | (effect << 12));
        Assert.Equal(expected, session.Step(0, 0));
        Assert.Equal(expectedNote, session.StepNote(0, 0));
        Assert.Equal(wave, session.StepWave(0, 0));
        Assert.Equal(volume, session.StepVolume(0, 0));
        Assert.Equal(effect, session.StepEffect(0, 0));
        Assert.Equal(0, session.Step(0, 0) & 0x8000);   // bit 15 is reserved and must stay 0
        Assert.Equal(1, view.CursorStep);               // and the cursor walked on
        // The slot came alive: one step long, at the text format's own default tempo.
        Assert.Equal(1, session.SlotLength(0));
        Assert.Equal(AudioTextCompiler.DefaultSpeed, session.SlotSpeed(0));

        // The row is a scale: s is C#, x is D — one semitone each, in the order the letters run.
        harness.Tap(Keys.S);
        harness.Tap(Keys.X);
        Assert.Equal(expectedNote + 1, session.StepNote(0, 1));
        Assert.Equal(expectedNote + 2, session.StepNote(0, 2));
        Assert.Equal(3, view.CursorStep);

        // The upper row is the same twelve an octave up: q is the C above z's.
        harness.Tap(Keys.Q);
        Assert.Equal(expectedNote + 12, session.StepNote(0, 3));
    }

    /// <summary>
    /// The piano's edges, where a de-facto standard stops being a rule of thumb: Ctrl+Z is an
    /// undo and never the note C, Shift+Left is the speed stepper and never a note, and a key
    /// past the bank's top (D#7) writes nothing at all rather than folding back to some note the
    /// author did not press.
    /// </summary>
    [Fact]
    public void ChordsAreNotNotesAndTheTopOfTheBankIsHonest()
    {
        Harness harness = OpenSoundEditor(out _);

        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.True(harness.Session.SlotIsEmpty(0));    // Ctrl+Z undid nothing; it did not type a C
        Assert.Equal(0, harness.View.CursorStep);

        // The highest octave holds four notes (60..63); its fifth key is off the top.
        while (harness.View.Octave < SfxEditorView.MaxOctave)
        {
            harness.Tap(Keys.OemCloseBrackets);
        }
        Assert.Equal(SfxEditorView.MaxOctave, harness.View.Octave);
        Assert.Equal(SfxEditorSession.MaxNote, harness.View.NoteOfPianoKey(3));
        Assert.Equal(-1, harness.View.NoteOfPianoKey(4));

        harness.Tap(Keys.C);                            // index 4 of the lower row — past D#7
        Assert.True(harness.Session.SlotIsEmpty(0));
        Assert.Equal(0, harness.View.CursorStep);
    }

    // ==================================================================================
    // 4. Speed, length and the loop — changed, and surviving a save.
    // ==================================================================================

    /// <summary>
    /// The slot's three numbers through both channels, and the format rule that hangs off them:
    /// steps past <c>length</c> are zero words (AUDIO-FORMAT §2), enforced when the length
    /// shrinks rather than swept up before writing — because the same bytes are handed to the
    /// APU for the audition. Everything then survives a save and a reopen.
    ///
    /// <para>Break recipe: drop the zeroing loop from <see cref="SfxEditorSession.SetLength"/>
    /// and the file stops parsing on reopen — <see cref="AudioFormat.WriteSfxFile"/> refuses it
    /// first, so the save itself throws; drop <c>ClampLoop</c> and a loop hanging past the new
    /// length does the same.</para>
    /// </summary>
    [Fact]
    public void SpeedLengthAndLoopChangeAndSurviveASave()
    {
        Harness harness = OpenSoundEditor(out string folder);
        SfxEditorSession session = harness.Session;

        // Eight notes, typed the way an author types them.
        for (int i = 0; i < 8; i++)
        {
            harness.Tap(Keys.Z);
        }
        Assert.Equal(8, session.SlotLength(0));

        session.SetSpeed(0, 3);
        session.SetLoop(0, 2, 6);
        Assert.Equal(3, session.SlotSpeed(0));
        Assert.Equal(2, session.SlotLoopStart(0));
        Assert.Equal(6, session.SlotLoopEnd(0));

        // Shrink: the dropped steps become zero words and the loop is pulled inside.
        session.SetLength(0, 4);
        Assert.Equal(4, session.SlotLength(0));
        Assert.Equal(4, session.SlotLoopEnd(0));
        for (int step = 4; step < SfxEditorSession.StepCount; step++)
        {
            Assert.Equal(0, session.Step(0, step));
        }

        Assert.True(session.Save());
        Assert.True(File.Exists(SfxPath(folder)));

        var reopened = new SfxEditorSession(folder);
        Assert.Equal(3, reopened.SlotSpeed(0));
        Assert.Equal(4, reopened.SlotLength(0));
        Assert.Equal(2, reopened.SlotLoopStart(0));
        Assert.Equal(4, reopened.SlotLoopEnd(0));
        for (int step = 4; step < SfxEditorSession.StepCount; step++)
        {
            Assert.Equal(0, reopened.Step(0, step));
        }

        // Length 0 is how a slot is emptied, and it empties the whole record — the one spelling
        // AUDIO-FORMAT §5 allows an unused slot.
        reopened.SetLength(0, 0);
        Assert.True(reopened.SlotIsEmpty(0));
        Assert.Equal(0, reopened.SlotSpeed(0));
        Assert.Equal(0, reopened.SlotLoopStart(0));
        Assert.Equal(0, reopened.SlotLoopEnd(0));
    }

    /// <summary>
    /// The loop through the keys and the mouse, and the markers those two write: <c>`</c> (or a
    /// left click on the loop row) sets the start, Tab (or a right click) sets the end
    /// <b>after</b> the step — the half-open interval of AUDIO-FORMAT §2 — and pressing either on
    /// the marker it already carries turns the loop off. That is REFERENCES-EDITORS §8 item 18
    /// with both channels wired.
    /// </summary>
    [Fact]
    public void TheLoopMarkersAreSetAndClearedFromEitherChannel()
    {
        Harness harness = OpenSoundEditor(out _);
        SfxEditorLayout layout = harness.Layout;
        for (int i = 0; i < 8; i++)
        {
            harness.Tap(Keys.Z);
        }

        // Mouse: start at step 2, end after step 5.
        harness.ClickRect(layout.LoopCellRect(2));
        Assert.Equal(2, harness.Session.SlotLoopStart(0));
        Assert.Equal(8, harness.Session.SlotLoopEnd(0));     // a fresh start takes the slot's tail

        harness.RightClick(layout.LoopCellRect(5).Center.X, layout.LoopCellRect(5).Center.Y);
        Assert.Equal(2, harness.Session.SlotLoopStart(0));
        Assert.Equal(6, harness.Session.SlotLoopEnd(0));     // half-open: step 5 is the last repeated

        // Clicking the start marker again clears the loop.
        harness.ClickRect(layout.LoopCellRect(2));
        Assert.Equal(0, harness.Session.SlotLoopEnd(0));
        Assert.Equal(0, harness.Session.SlotLoopStart(0));

        // Keyboard: the same two verbs at the cursor, which is what ` and Tab mean here.
        while (harness.View.CursorStep > 3)
        {
            harness.Tap(Keys.Left);
        }
        Assert.Equal(3, harness.View.CursorStep);
        harness.Tap(Keys.OemTilde);
        Assert.Equal(3, harness.Session.SlotLoopStart(0));
        harness.Tap(Keys.Right);
        harness.Tap(Keys.Tab);
        Assert.Equal(5, harness.Session.SlotLoopEnd(0));     // cursor 4, end after it
    }

    // ==================================================================================
    // 5. Undo: one step per action.
    // ==================================================================================

    /// <summary>
    /// One Ctrl+Z per action, whichever hand performed it — and a pointer <em>gesture</em> is one
    /// action however many columns it crossed, exactly as a pencil stroke is on the map.
    ///
    /// <para>Break recipe: push the snapshot inside <see cref="SfxEditorSession.SetStep"/>'s
    /// write instead of at the gesture's end and the drag becomes many steps; drop the
    /// <c>_strokeChanged</c> check from <see cref="SfxEditorSession.EndStroke"/> and an idle
    /// click starts pushing no-op steps, which the last assertion catches.</para>
    /// </summary>
    [Fact]
    public void UndoTakesOneStepPerAction()
    {
        Harness harness = OpenSoundEditor(out _);
        SfxEditorSession session = harness.Session;

        harness.Tap(Keys.Z);                    // action 1: a note
        harness.Tap(Keys.S);                    // action 2: another
        session.SetSpeed(0, 5);                 // action 3: a header byte
        Assert.Equal(2, session.SlotLength(0));
        Assert.Equal(5, session.SlotSpeed(0));

        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.Equal(AudioTextCompiler.DefaultSpeed, session.SlotSpeed(0));
        Assert.Equal(2, session.SlotLength(0));  // only the speed came back

        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.Equal(1, session.SlotLength(0));
        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.True(session.SlotIsEmpty(0));
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);           // back to the disk's own bytes: the absent file

        // Redo walks back up the same three steps.
        harness.Tap(Keys.LeftControl, Keys.Y);
        Assert.Equal(1, session.SlotLength(0));

        // A drag across the pitch grid is ONE step, whatever it crossed.
        SfxEditorLayout layout = harness.Layout;
        while (session.CanUndo)
        {
            session.Undo();
        }
        Rectangle from = layout.PitchCellRect(0, 0);
        harness.LeftDown(from.Center.X, from.Center.Y);
        for (int step = 1; step < 6; step++)
        {
            Rectangle cell = layout.PitchCellRect(step, step);
            harness.Frame(NoKeys, cell.Center.X, cell.Center.Y, ButtonState.Pressed, ButtonState.Released);
        }
        Rectangle last = layout.PitchCellRect(5, 5);
        harness.LeftUp(last.Center.X, last.Center.Y);

        Assert.Equal(6, session.SlotLength(0));
        harness.Tap(Keys.LeftControl, Keys.Z);
        Assert.True(session.SlotIsEmpty(0));     // the whole drag, in one press

        // An idle click on the grid's own cell writes nothing new and must not push a step.
        harness.Tap(Keys.LeftControl, Keys.Y);
        bool couldUndo = session.CanUndo;
        harness.ClickRect(layout.PitchCellRect(0, 0));
        harness.ClickRect(layout.PitchCellRect(0, 0));
        Assert.Equal(couldUndo, session.CanUndo);
    }

    // ==================================================================================
    // 6. The payload's length is checked, in both directions.
    // ==================================================================================

    /// <summary>
    /// A truncated bank is refused by name and by number, not read as far as it goes. The check
    /// belongs to <see cref="AudioFormat"/> — the format's one owner — and this session calls it
    /// on the way in and again on the way out, so the editor cannot write a bank it could not
    /// read back.
    ///
    /// <para>Break recipe: have the session read the file with <c>File.ReadAllBytes</c> and slice
    /// off eight bytes itself instead of calling <see cref="AudioFormat.ParseSfxFile"/> — the
    /// truncated file loads as garbage and every assertion here goes red at once.</para>
    /// </summary>
    [Fact]
    public void ATruncatedBankIsRefusedWithTheNumbersInTheMessage()
    {
        byte[] whole = File.ReadAllBytes(Path.Combine(CartsRoot(), "snake", SfxEditorSession.SfxFileName));
        string folder = NewCartFolder(whole[..4000]);

        var error = Assert.Throws<CartLoadException>(() => new SfxEditorSession(folder));

        Assert.Contains(SfxEditorSession.SfxFileName, error.Message, StringComparison.Ordinal);
        Assert.Contains("4000", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            AudioFormat.SfxFileSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error.Message, StringComparison.Ordinal);

        // And the shell survives it the way it survives a failed launch: the message reaches the
        // library line and the tab the author was standing on does not move.
        var machine = new ShellModeMachine(
            new CartLibrary(Path.GetDirectoryName(folder)!),
            static path => CartSession.Start(path),
            static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();
        machine.SwitchEditorTab(ShellMode.SfxEditor);

        Assert.Equal(ShellMode.Editor, machine.Mode);
        Assert.Null(machine.SfxEditor);
        Assert.NotNull(machine.LibraryMessage);
    }

    /// <summary>The other direction: a payload of the wrong length cannot leave this editor, because the format's writer re-validates it.</summary>
    [Fact]
    public void TheWriterChecksTheLengthToo()
    {
        var error = Assert.Throws<CartLoadException>(
            () => AudioFormat.WriteSfxFile(new byte[AudioFormat.SfxPayloadSize - 1]));
        Assert.Contains(
            AudioFormat.SfxPayloadSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error.Message, StringComparison.Ordinal);
    }

    // ==================================================================================
    // 7. Geometry.
    // ==================================================================================

    /// <summary>
    /// Everything the screen draws is inside the window, above the reserved prompt line, and
    /// clear of everything else — at the shell's default window and at a quarter of it. The three
    /// grids share one column per step, so their columns are checked to line up: that is the
    /// whole reason the eye can read a step across three panels.
    ///
    /// <para>Break recipe: size the grid cell from the width instead of the height in
    /// <see cref="SfxEditorLayout.Compute"/> and the panel is pushed off the right edge at
    /// 1280x720; drop the <c>minPanel</c> term and the same thing happens at 640x360; give the
    /// loop row a different cell width and the column-alignment assertions go red.</para>
    /// </summary>
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(640, 360)]
    public void EverythingSitsInsideTheWindowAndNothingOverlaps(int width, int height)
    {
        var layout = SfxEditorLayout.Compute(width, height);
        var window = new Rectangle(0, 0, width, height);

        var boxes = new (string Name, Rectangle Rect)[]
        {
            ("pitch", layout.Pitch), ("loop", layout.Loop), ("volume", layout.Volume),
            ("slots", layout.Slots), ("preview", layout.Preview), ("waves", layout.Waves),
            ("effects", layout.Effects), ("speed", layout.SpeedField),
            ("length", layout.LengthField), ("octave", layout.OctaveField),
        };

        foreach ((string name, Rectangle rect) in boxes)
        {
            Assert.True(window.Contains(rect), $"{name} is off screen");
            Assert.True(rect.Bottom <= layout.PromptY, $"{name} runs under the prompt line");
            Assert.True(rect.Top >= layout.TabStrip.Bottom, $"{name} runs under the tab strip");
            Assert.False(rect.Intersects(layout.StatusBar), $"{name} sits on the status bar");
        }
        for (int i = 0; i < boxes.Length; i++)
        {
            for (int j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(
                    boxes[i].Rect.Intersects(boxes[j].Rect),
                    $"{boxes[i].Name} overlaps {boxes[j].Name}");
            }
        }

        // The three grids are one instrument: same left edge, same column width, whole cells.
        Assert.Equal(layout.Pitch.X, layout.Loop.X);
        Assert.Equal(layout.Pitch.X, layout.Volume.X);
        Assert.Equal(layout.Pitch.Width, layout.Volume.Width);
        Assert.Equal(0, layout.Pitch.Width % SfxEditorLayout.StepColumns);
        Assert.Equal(layout.Cell * SfxEditorLayout.OctaveRows, layout.Pitch.Height);
        Assert.Equal(layout.Cell * SfxEditorLayout.VolumeLevels, layout.Volume.Height);
        Assert.True(layout.Cell >= 1);
        Assert.True(layout.SlotCell >= 1);

        // Every button this screen owns is placed, inside the window, and clear of everything.
        Assert.Equal(AllButtons.Count(EditorIcons.BelongsToSfxEditor), layout.Buttons.Count);
        Assert.All(layout.Buttons, place => Assert.True(EditorIcons.BelongsToSfxEditor(place.Id)));
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            Rectangle rect = layout.Buttons[i].Rect;
            Assert.True(window.Contains(rect), $"{layout.Buttons[i].Id} is off screen");
            foreach ((string name, Rectangle box) in boxes)
            {
                Assert.False(rect.Intersects(box), $"{layout.Buttons[i].Id} sits on {name}");
            }
            for (int j = i + 1; j < layout.Buttons.Count; j++)
            {
                Assert.False(rect.Intersects(layout.Buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// This screen is the <b>last tenant of the host frame</b>, and it still stands in the frame
    /// itself rather than in a copy of it: the same scale, margin, button size, bands, prompt
    /// line and prompt verbs <see cref="EditorChrome.Compute"/> produces from nothing but a
    /// window size, and the same rectangles for the exit button and the five editor tabs.
    ///
    /// <para><b>Re-pinned in wave R4, and this is the whole of why.</b> This test used to
    /// compare the sound screen with a sibling: three of them at first, then the code screen
    /// alone after R2 and R3 took the sprite and map screens onto the console. Wave R4 took the
    /// code screen too (ADR-029), so there is no sibling left to compare with — its exit tab is
    /// ten console pixels wide where this one's is hundreds of window pixels across, and
    /// asserting those equal would assert a falsehood. The comparison therefore turns inward:
    /// the screen is measured against <see cref="EditorChrome"/> itself, which is the stronger
    /// question anyway (two screens can agree with each other and both be wrong about the frame)
    /// and the only one that survives having exactly one tenant. When this screen moves in its
    /// own wave, this test and <see cref="EditorChrome"/> die together — and the second half
    /// below is what will notice if it moves and this file is forgotten.</para>
    ///
    /// <para><b>Save, Undo and Redo dropped out of the shared list, and not because they moved
    /// on this screen.</b> In the host frame they are placed by
    /// <see cref="EditorChrome.Compute"/> from a status-slot array each screen passes in — the
    /// slot list is the screen's, not the frame's — so a bare chrome computed here cannot
    /// produce their rectangles, and re-deriving them in a test would be exactly the second
    /// owner this file exists to forbid. Their placement on this screen stays covered by
    /// <see cref="EverythingSitsInsideTheWindowAndNothingOverlaps"/>.</para>
    /// </summary>
    [Fact]
    public void TheSoundScreenIsTheLastTenantOfTheHostFrameAndStandsInItself()
    {
        var sfx = SfxEditorLayout.Compute(WindowWidth, WindowHeight);
        // The frame with nothing in it: six buttons is exactly the exit tab plus the five editor
        // tabs, and an empty status-slot list places nothing else.
        var buttons = new EditorButtonPlace[6];
        int placed = 0;
        EditorChrome chrome = EditorChrome.Compute(
            WindowWidth, WindowHeight, buttons, ref placed, new EditorButton?[0]);
        Assert.Equal(6, placed);

        Assert.Equal(chrome.Ui, sfx.Ui);
        Assert.Equal(chrome.Margin, sfx.Margin);
        Assert.Equal(chrome.ButtonSize, sfx.ButtonSize);
        Assert.Equal(chrome.TabStrip, sfx.TabStrip);
        Assert.Equal(chrome.StatusBar, sfx.StatusBar);
        Assert.Equal(chrome.PromptY, sfx.PromptY);
        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Assert.Equal(chrome.PromptVerbRect(verb), sfx.PromptVerbRect(verb));
        }
        EditorButton[] shared =
        {
            EditorButton.ExitTab, EditorButton.CodeTab, EditorButton.SpritesTab,
            EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
        };
        foreach (EditorButton button in shared)
        {
            Assert.Equal(EditorChrome.ButtonRect(buttons, button), sfx.ButtonRect(button));
        }

        // The other four screens are on the console and this one is not — the fact that makes
        // the paragraph above true, asserted so that the day it stops being true, it is this
        // test that says so.
        Assert.NotEqual(
            ConsoleChrome.ButtonSize, sfx.ButtonSize);
        Assert.Equal(
            ConsoleChrome.ButtonSize,
            CodeEditorLayout.Compute(160, 90).ButtonSize);
        Assert.NotEqual(
            CodeEditorLayout.Compute(160, 90).ButtonRect(EditorButton.ExitTab),
            sfx.ButtonRect(EditorButton.ExitTab));
    }

    /// <summary>
    /// Every clickable rectangle answers its own centre — the roundtrip discipline every screen
    /// here carries, applied to the pieces that are not buttons. A cell drawn in one place and
    /// hit-tested in another is the defect this closes.
    /// </summary>
    [Fact]
    public void EveryGridCellRoundTripsThroughItsRectangle()
    {
        var layout = SfxEditorLayout.Compute(WindowWidth, WindowHeight);

        for (int step = 0; step < SfxEditorLayout.StepColumns; step++)
        {
            for (int semitone = 0; semitone < SfxEditorLayout.OctaveRows; semitone++)
            {
                Point at = layout.PitchCellRect(step, semitone).Center;
                Assert.True(layout.TryPitchCell(at.X, at.Y, out int hitStep, out int hitSemitone));
                Assert.Equal(step, hitStep);
                Assert.Equal(semitone, hitSemitone);
            }
            for (int level = 0; level < SfxEditorLayout.VolumeLevels; level++)
            {
                Point at = layout.VolumeCellRect(step, level).Center;
                Assert.True(layout.TryVolumeCell(at.X, at.Y, out int hitStep, out int hitLevel));
                Assert.Equal(step, hitStep);
                Assert.Equal(level, hitLevel);
            }
            Point loop = layout.LoopCellRect(step).Center;
            Assert.True(layout.TryLoopCell(loop.X, loop.Y, out int hitLoopStep));
            Assert.Equal(step, hitLoopStep);
        }
        for (int slot = 0; slot < SfxEditorSession.SlotCount; slot++)
        {
            Point at = layout.SlotCellRect(slot).Center;
            Assert.True(layout.TrySlotCell(at.X, at.Y, out int hitSlot));
            Assert.Equal(slot, hitSlot);
        }
        for (int wave = 0; wave < SfxEditorSession.WaveCount; wave++)
        {
            Point at = layout.WaveCellRect(wave).Center;
            Assert.True(layout.TryWaveCell(at.X, at.Y, out int hitWave));
            Assert.Equal(wave, hitWave);
        }
        for (int effect = 0; effect < SfxEditorSession.EffectCount; effect++)
        {
            Point at = layout.EffectCellRect(effect).Center;
            Assert.True(layout.TryEffectCell(at.X, at.Y, out int hitEffect));
            Assert.Equal(effect, hitEffect);
        }
        foreach (SfxField field in Enum.GetValues<SfxField>())
        {
            Point minus = layout.FieldDecreaseRect(field).Center;
            Assert.True(layout.TryFieldStepper(minus.X, minus.Y, out SfxField hitField, out int down));
            Assert.Equal(field, hitField);
            Assert.Equal(-1, down);
            Point plus = layout.FieldIncreaseRect(field).Center;
            Assert.True(layout.TryFieldStepper(plus.X, plus.Y, out hitField, out int up));
            Assert.Equal(field, hitField);
            Assert.Equal(1, up);
        }
    }

    // ==================================================================================
    // 8. The SOUND tab is alive, and travel keeps unsaved work.
    // ==================================================================================

    /// <summary>
    /// The one-line version of this whole wave: the SOUND tab is no longer drawn-but-dead. Its
    /// stub flag is gone, it routes to a real mode, its tooltip promises a key instead of a later
    /// portion, it joins the ring Alt+Left/Right walks, and clicking it from another editor
    /// screen arrives at the sound editor.
    ///
    /// <para>Break recipe: put <see cref="EditorButton.SoundTab"/> back into
    /// <see cref="EditorIcons.IsStub"/> — every assertion here goes red, and so does the contract
    /// sweep below, because the router refuses stubs before any verb.</para>
    /// </summary>
    [Fact]
    public void TheSoundTabIsNoLongerADeadButton()
    {
        Assert.False(EditorIcons.IsStub(EditorButton.SoundTab));
        Assert.Equal(ShellMode.SfxEditor, EditorIcons.TabTarget(EditorButton.SoundTab));
        Assert.DoesNotContain(
            "LATER PORTION", EditorIcons.Tooltip(EditorButton.SoundTab), StringComparison.Ordinal);
        Assert.Contains("ALT+", EditorIcons.Tooltip(EditorButton.SoundTab), StringComparison.Ordinal);
        Assert.Contains(ShellMode.SfxEditor, EditorIcons.LiveEditorTabs);
        // Music is the last one left, and it must still look and act dead.
        Assert.True(EditorIcons.IsStub(EditorButton.MusicTab));

        // ...and the click really arrives, from the screen the author is most likely on.
        Harness harness = OpenSoundEditor(out _);
        harness.Modes.SwitchEditorTab(ShellMode.Editor);
        harness.Idle();
        // Console pixels: the sprite screen is on the console now, and so is the point a click
        // on its sound tab must land on (see Harness.Context).
        Rectangle tab = SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, regionCells: 1)
            .ButtonRect(EditorButton.SoundTab);
        harness.Click(tab.X + tab.Width / 2, tab.Y + tab.Height / 2);

        Assert.Equal(ShellMode.SfxEditor, harness.Modes.Mode);
    }

    /// <summary>
    /// From the sound tab to the sprites tab and back — by click and by key — with the unsaved
    /// bank intact, and the same session on the way back rather than a reload.
    ///
    /// <para>Break recipe: null <c>SfxEditor</c> out in
    /// <see cref="ShellModeMachine.SwitchEditorTab"/>, or rebuild the session on every visit, and
    /// the identity or the note assertion goes red — which is the shape of the data loss.</para>
    /// </summary>
    [Fact]
    public void TheTabsTravelBothWaysWithoutLosingUnsavedNotes()
    {
        Harness harness = OpenSoundEditor(out _);
        harness.Tap(Keys.Z);
        SfxEditorSession sfx = harness.Session;
        ushort note = sfx.Step(0, 0);
        Assert.NotEqual(0, note);
        Assert.True(sfx.IsDirty);

        harness.ClickButton(EditorButton.SpritesTab);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        harness.ClickButton(EditorButton.SoundTab);
        Assert.Equal(ShellMode.SfxEditor, harness.Modes.Mode);
        Assert.Same(sfx, harness.Modes.SfxEditor);
        Assert.Equal(note, sfx.Step(0, 0));

        // The keyboard path: Alt+Left walks one tab left (sound → tilemap), Alt+Right back.
        harness.Tap(Keys.LeftAlt, Keys.Left);
        Assert.Equal(ShellMode.MapEditor, harness.Modes.Mode);
        harness.Tap(Keys.LeftAlt, Keys.Right);
        Assert.Equal(ShellMode.SfxEditor, harness.Modes.Mode);
        Assert.Same(sfx, harness.Modes.SfxEditor);
        Assert.Equal(note, sfx.Step(0, 0));
        Assert.True(sfx.IsDirty);
    }

    /// <summary>
    /// A cart that never visits the SOUND tab gets no sound session and therefore cannot get an
    /// <c>sfx.bin</c> — the "absent bank is silence" rule protected at the one place it could be
    /// broken by accident. Break recipe: create the session eagerly in
    /// <see cref="ShellModeMachine.OpenEditor"/>.
    /// </summary>
    [Fact]
    public void TheSoundSessionIsNotBornUntilTheTabIsVisited()
    {
        string folder = NewCartFolder();
        var machine = new ShellModeMachine(
            new CartLibrary(Path.GetDirectoryName(folder)!),
            static path => CartSession.Start(path),
            static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();

        Assert.Null(machine.SfxEditor);
        Assert.Null(machine.SfxView);
        Assert.False(File.Exists(SfxPath(folder)));
    }

    /// <summary>
    /// The trap the shared exit exists to avoid, now with four banks: leaving from the SOUND tab
    /// while the sprite sheet on another tab is dirty must not drop the sheet — and the mirror,
    /// leaving from the sprite tab while the bank is dirty.
    /// </summary>
    [Fact]
    public void LeavingFromEitherTabDoesNotDropTheOtherBank()
    {
        Harness harness = OpenSoundEditor(out _);
        SpriteEditorSession sheet = harness.Modes.Editor!;
        sheet.SelectColor(7);
        sheet.BeginStroke();
        sheet.Paint(2, 3);
        sheet.EndStroke();
        Assert.True(sheet.IsDirty);
        Assert.False(harness.Session.IsDirty);

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        Assert.True(sheet.ExitPromptShown);
        Assert.NotNull(harness.Modes.SfxEditor);

        // Answer for the sheet, and — every bank now settled — the editor closes for good.
        harness.Modes.DiscardEditorAndClose();
        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Null(harness.Modes.SfxEditor);
        Assert.Null(harness.Modes.Editor);
    }

    /// <summary>
    /// Esc on a dirty bank asks instead of leaving, Z saves and leaves, and the file lands with
    /// the note in it. The same contract the other three screens carry, over a fourth payload.
    /// </summary>
    [Fact]
    public void EscapeOnADirtyBankAsksAndZSavesAndLeaves()
    {
        Harness harness = OpenSoundEditor(out string folder);
        harness.Tap(Keys.Z);
        ushort written = harness.Session.Step(0, 0);

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.SfxEditor, harness.Modes.Mode);
        Assert.True(harness.Modes.SfxView!.ExitPromptShown);

        harness.Tap(Keys.Z);                    // on the prompt, Z is "save and exit" — not a note

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.Null(harness.Modes.SfxEditor);
        Assert.True(File.Exists(SfxPath(folder)));
        byte[] payload = AudioFormat.ParseSfxFile(
            File.ReadAllBytes(SfxPath(folder)), SfxEditorSession.SfxFileName);
        Assert.Equal(written, AudioFormat.Step(payload, 0, 0));
    }

    /// <summary>X on the prompt leaves the disk untouched — a cart that never had an sfx.bin still does not.</summary>
    [Fact]
    public void DiscardingTheBankWritesNothingAtAll()
    {
        Harness harness = OpenSoundEditor(out string folder);
        harness.Tap(Keys.Z);
        harness.Tap(Keys.Escape);
        Assert.True(harness.Modes.SfxView!.ExitPromptShown);

        harness.Tap(Keys.X);

        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.False(File.Exists(SfxPath(folder)));
    }

    // ==================================================================================
    // 9. The button contract, and two-way input parity.
    // ==================================================================================

    /// <summary>Everything a sound button click may legally touch, in one comparable value.</summary>
    private sealed record Snapshot(
        ShellMode Mode, int Version, bool Dirty, bool CanUndo, bool CanRedo, bool PromptShown,
        bool PlayWanted, int Slot, int Cursor);

    private static Snapshot Observe(ShellModeMachine machine)
    {
        SfxEditorSession sfx = machine.SfxEditor!;
        SfxEditorView view = machine.SfxView!;
        return new Snapshot(
            machine.Mode, sfx.Version, sfx.IsDirty, sfx.CanUndo, sfx.CanRedo,
            view.ExitPromptShown, view.PlayWanted, view.SelectedSlot, view.CursorStep);
    }

    /// <summary>The shell's press dispatch over the real router pieces — the same two-line mirror its siblings use.</summary>
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
        if (EditorIcons.ClickSfxButton(machine.SfxEditor!, machine.SfxView!, button))
        {
            machine.HandleEscape();                     // the exit tab's verb belongs to the machine
        }
    }

    /// <summary>A session where every live button has work to do: dirt, an undo step and a redo step.</summary>
    private static void Prepare(SfxEditorSession session)
    {
        session.SetStep(0, 0, 24, 1, 5, 0);
        session.SetStep(0, 1, 28, 1, 4, 0);
        session.Undo();
    }

    /// <summary>
    /// The sweep. Live buttons must change the snapshot; stubs and the sound tab (it names the
    /// screen already on show) must change exactly nothing.
    ///
    /// <para>Break recipe: delete any <c>case</c> from
    /// <see cref="EditorIcons.ClickSfxButton"/> — that one button's assertion goes red by name.
    /// Add a button to <see cref="SfxEditorLayout"/> without wiring it and the same line names
    /// the new one.</para>
    /// </summary>
    [Fact]
    public void EveryPlacedLiveSoundButtonChangesSomethingObservable()
    {
        foreach (EditorButtonPlace place in SfxEditorLayout.Compute(WindowWidth, WindowHeight).Buttons)
        {
            string folder = NewCartFolder();
            ShellModeMachine machine = MachineOnTheSoundTab(folder);
            Prepare(machine.SfxEditor!);
            Snapshot before = Observe(machine);

            RouteClick(machine, place.Id);

            Snapshot after = Observe(machine);
            bool contractedNoOp = EditorIcons.IsStub(place.Id) || place.Id == EditorButton.SoundTab;
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
    /// Input parity, both ways, on the actions this screen invented: a keyboard-only run and a
    /// mouse-only run must leave <b>byte-identical banks</b>. Neither run contains a single call
    /// the other one makes — no <see cref="Keys"/> value below the keyboard block reaches the
    /// mouse block, and no coordinate reaches the keyboard block.
    ///
    /// <para>Break recipe: change what the wave row's click does without changing what , and .
    /// do (or the reverse) and the byte comparison goes red — which is exactly the drift the
    /// parity law exists to catch.</para>
    /// </summary>
    [Fact]
    public void TheKeyboardAndTheMouseWriteTheSameBank()
    {
        // --- channel A: keyboard only ---
        Harness keyboard = OpenSoundEditor(out string keyboardFolder);
        keyboard.Tap(Keys.OemPeriod);            // wave: one along
        keyboard.Tap(Keys.OemPeriod);            // and another
        keyboard.Tap(Keys.F);                    // effect: one along
        keyboard.Tap(Keys.OemCloseBrackets);     // octave: one up
        keyboard.Tap(Keys.Z);                    // a note
        keyboard.Tap(Keys.X);                    // and another, two semitones up (z s x)
        keyboard.Tap(Keys.PageDown);             // next slot
        keyboard.Tap(Keys.Z);                    // a note there too
        keyboard.Session.Save();
        byte[] keyboardBank = File.ReadAllBytes(SfxPath(keyboardFolder));

        // --- channel B: mouse only ---
        Harness mouse = OpenSoundEditor(out string mouseFolder);
        SfxEditorLayout layout = mouse.Layout;
        mouse.ClickRect(layout.WaveCellRect(2));
        mouse.ClickRect(layout.EffectCellRect(1));
        mouse.ClickRect(layout.FieldIncreaseRect(SfxField.Octave));
        int octave = mouse.View.Octave;
        mouse.ClickRect(layout.PitchCellRect(0, 0));
        mouse.ClickRect(layout.PitchCellRect(1, 2));
        mouse.ClickRect(layout.SlotCellRect(1));
        mouse.ClickRect(layout.PitchCellRect(0, 0));
        mouse.Session.Save();
        byte[] mouseBank = File.ReadAllBytes(SfxPath(mouseFolder));

        Assert.Equal(SfxEditorView.DefaultOctave + 1, octave);
        Assert.Equal(keyboardBank, mouseBank);
    }

    /// <summary>
    /// The play button, and the one honest thing this suite can say about sound: the request is
    /// a fact of the view, both channels write it, a second ask restarts rather than being
    /// swallowed, and the button's face follows what the chip reports rather than what was asked.
    /// The chip itself lives in <c>QuarpGame.UpdateSfxPreview</c> and is
    /// <c>Quarp.Core.Audio.Apu</c> — the cartridge's own synthesizer, not a second one.
    /// </summary>
    [Fact]
    public void ThePlayButtonAndSpaceAskForTheSameThing()
    {
        Harness harness = OpenSoundEditor(out _);
        SfxEditorView view = harness.View;

        Assert.False(view.PlayWanted);
        Assert.False(view.Playing);

        harness.Tap(Keys.Space);
        Assert.True(view.PlayWanted);
        int epoch = view.PlayEpoch;

        harness.Tap(Keys.Space);                 // Space toggles
        Assert.False(view.PlayWanted);

        harness.ClickButton(EditorButton.ToolPlay);
        Assert.True(view.PlayWanted);
        Assert.True(view.PlayEpoch > epoch);     // a fresh ask, so the slot restarts from step 0

        // The face is what the chip says, not what was asked: nothing has reported yet.
        Assert.False(view.Playing);
        view.ReportPlaying(true);
        Assert.True(view.Playing);
        view.ReportPlaying(false);               // the slot ran out of steps
        Assert.False(view.Playing);
        Assert.False(view.PlayWanted);           // and the button goes dark by itself

        Assert.Contains("SPACE", EditorIcons.Tooltip(EditorButton.ToolPlay), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every control this screen has that is not a button still announces its keys somewhere a
    /// hovering hand can find them — the discoverability half of the parity law, which for the
    /// other screens is checked by their tooltip sweeps. This screen has nine such controls, so
    /// each got a name (<see cref="SfxRegion"/>) rather than a sentence squeezed onto the tab's
    /// own label, and the sweep walks all nine: every region the layout can report must have a
    /// label, and every region must answer its own rectangle.
    ///
    /// <para>Break recipe: add a rectangle to <see cref="SfxEditorLayout.RegionAt"/> without a
    /// case in <see cref="EditorIcons.SfxRegionTooltip"/> and the sweep throws by name; drop a
    /// region from <c>RegionAt</c> and the roundtrip assertion goes red.</para>
    /// </summary>
    [Fact]
    public void EveryKeylessControlAnnouncesItsKeys()
    {
        var layout = SfxEditorLayout.Compute(WindowWidth, WindowHeight);
        foreach (SfxRegion region in Enum.GetValues<SfxRegion>())
        {
            if (region == SfxRegion.None)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.SfxRegionTooltip(region));
                continue;
            }
            Rectangle rect = layout.RegionRect(region);
            Assert.NotEqual(Rectangle.Empty, rect);
            Assert.Equal(region, layout.RegionAt(rect.Center.X, rect.Center.Y));
            string label = EditorIcons.SfxRegionTooltip(region);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.All(label, c => Assert.InRange(c, ' ', '~'));
        }
        Assert.Equal(SfxRegion.None, layout.RegionAt(Off, Off));

        Assert.Contains("PGUP", EditorIcons.SfxSlotTooltip, StringComparison.Ordinal);
        Assert.Contains("ZSXDCVGBHNJM", EditorIcons.SfxPitchTooltip, StringComparison.Ordinal);
        Assert.Contains("Q2W3ER5T6Y7UI", EditorIcons.SfxPitchTooltip, StringComparison.Ordinal);
        Assert.Contains("TAB", EditorIcons.SfxLoopTooltip, StringComparison.Ordinal);
        Assert.Contains("`", EditorIcons.SfxLoopTooltip, StringComparison.Ordinal);
        Assert.Contains("UP/DOWN", EditorIcons.SfxVolumeTooltip, StringComparison.Ordinal);
        Assert.Contains(",", EditorIcons.SfxWaveTooltip, StringComparison.Ordinal);
        Assert.Contains("F", EditorIcons.SfxEffectTooltip, StringComparison.Ordinal);
        Assert.Contains("SHIFT+LEFT/RIGHT", EditorIcons.SfxFieldTooltip(SfxField.Speed), StringComparison.Ordinal);
        Assert.Contains("SHIFT+UP/DOWN", EditorIcons.SfxFieldTooltip(SfxField.Length), StringComparison.Ordinal);
        Assert.Contains("[", EditorIcons.SfxFieldTooltip(SfxField.Octave), StringComparison.Ordinal);

        // ASCII only: the system font has no other alphabet.
        string[] all =
        {
            EditorIcons.SfxSlotTooltip, EditorIcons.SfxPitchTooltip, EditorIcons.SfxLoopTooltip,
            EditorIcons.SfxVolumeTooltip, EditorIcons.SfxWaveTooltip, EditorIcons.SfxEffectTooltip,
            EditorIcons.SfxFieldTooltip(SfxField.Speed), EditorIcons.SfxFieldTooltip(SfxField.Length),
            EditorIcons.SfxFieldTooltip(SfxField.Octave), EditorIcons.SfxTooltip(EditorButton.SoundTab),
        };
        Assert.All(all, text => Assert.All(text, c => Assert.InRange(c, ' ', '~')));
    }

    /// <summary>
    /// The status band's two fields, named: where the cursor is and what is under it on the left,
    /// the slot's three numbers on the right — in the vocabulary <c>sfx.txt</c> uses, so an author
    /// moving between the screen and the text source reads one set of words.
    /// </summary>
    [Fact]
    public void TheStatusLineReadsTheCursorAndTheSlot()
    {
        Harness harness = OpenSoundEditor(out _);

        Assert.Contains("---", SfxEditorRenderer.Coordinates(harness.Session, harness.View), StringComparison.Ordinal);
        Assert.Contains("NO LOOP", SfxEditorRenderer.Summary(harness.Session, harness.View), StringComparison.Ordinal);
        Assert.Null(SfxEditorRenderer.StandingNotice(harness.Session));

        harness.Tap(Keys.Z);
        harness.Tap(Keys.Left);

        string coordinates = SfxEditorRenderer.Coordinates(harness.Session, harness.View);
        Assert.Contains(
            AudioTextCompiler.NoteName(harness.Session.StepNote(0, 0)), coordinates, StringComparison.Ordinal);
        Assert.Contains("STEP 00", coordinates, StringComparison.Ordinal);
        Assert.Contains("LEN 1", SfxEditorRenderer.Summary(harness.Session, harness.View), StringComparison.Ordinal);
    }
}

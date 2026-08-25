using Microsoft.Xna.Framework.Input;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The shell's time-control keys for one frame, already edge-detected (API-8 §8):
///
/// <list type="table">
///   <item><term>Space</term><description>pause / resume</description></item>
///   <item><term>.</term><description>one tick forward</description></item>
///   <item><term>,</term><description>one tick back</description></item>
///   <item><term>[ / ]</term><description>slower / faster along the ladder</description></item>
///   <item><term>Backspace (held)</term><description>rewind in real time</description></item>
///   <item><term>Home</term><description>back to tick 0</description></item>
///   <item><term>F5 / F8</term><description>save replay / play replay</description></item>
///   <item><term>Esc</term><description>quit</description></item>
/// </list>
///
/// These never reach the cartridge: <see cref="InputMapper"/> maps a disjoint set of keys,
/// so a cart cannot bind space to jump and fight the shell over it. Everything but the
/// rewind is a press, not a hold — repeat-on-hold would make a single tap of <c>,</c>
/// unpredictable, and holding is what Backspace is for.
///
/// <para><b>The menu block (M9).</b> The library and the editor stub read the
/// <c>Menu*</c> fields; the game mode ignores them, because in a game those same keys
/// belong to the cartridge via <see cref="InputMapper"/>. They live in this one struct,
/// filled by the one reader with the one previous-frame state, so a key held across a mode
/// switch cannot fire twice: Esc that left the game is already "down" when the library's
/// first frame reads the keyboard, and edge detection sees no edge.</para>
/// </summary>
public readonly struct ShellCommands
{
    public bool Quit { get; init; }
    public bool TogglePause { get; init; }
    public bool StepForward { get; init; }
    public bool StepBack { get; init; }

    /// <summary>Game: [ — slower along the time ladder. Editor: scroll the sheet window one sprite column left (wave 2h; the slider's tooltip names the keys).</summary>
    public bool Slower { get; init; }

    /// <summary>Game: ] — faster. Editor: scroll the sheet window one sprite column right.</summary>
    public bool Faster { get; init; }
    public bool Rewinding { get; init; }
    public bool ToStart { get; init; }
    public bool SaveReplay { get; init; }
    public bool PlayReplay { get; init; }

    /// <summary>Library: move the selection bar up. Editor: canvas cursor one pixel up.</summary>
    public bool MenuUp { get; init; }

    /// <summary>Library: move the selection bar down. Editor: canvas cursor one pixel down.</summary>
    public bool MenuDown { get; init; }

    /// <summary>Editor: canvas cursor one pixel left (M9 stage 2.5 keyboard drawing). The library's list has no columns.</summary>
    public bool MenuLeft { get; init; }

    /// <summary>Editor: canvas cursor one pixel right.</summary>
    public bool MenuRight { get; init; }

    /// <summary>
    /// Library: launch the selected cart — Z or Enter, the confirm keys the pad maps to
    /// O/Start. In the editor's exit prompt the same Z means "save and exit". Never fires
    /// with Ctrl held: Ctrl+Z is <see cref="EditorUndo"/>, and a chord must not double as
    /// its bare key.
    /// </summary>
    public bool MenuConfirm { get; init; }

    /// <summary>
    /// Library: open the sprite editor for the selected cart — X (M9 stage 2). In the
    /// editor's exit prompt the same X means "exit without saving"; during normal editing it
    /// is the keyboard eyedropper at the canvas cursor (stage 2.5 parity — the key mirrors
    /// the right mouse button). Ctrl-chorded X is ignored for the same reason as
    /// <see cref="MenuConfirm"/>.
    /// </summary>
    public bool MenuEditor { get; init; }

    /// <summary>Editor: Ctrl+Z — undo one pencil stroke.</summary>
    public bool EditorUndo { get; init; }

    /// <summary>Editor: Ctrl+Y — redo.</summary>
    public bool EditorRedo { get; init; }

    /// <summary>Editor: Ctrl+S — save the sheet (a no-op on a clean session, by the save contract).</summary>
    public bool EditorSave { get; init; }

    /// <summary>
    /// Map editor: Ctrl+C — copy the marked rectangle of the map (TIC-80's
    /// <c>copySelectionToClipboard</c>, REFERENCES-EDITORS §3.1). A Ctrl chord like undo, redo
    /// and save, read here by the same <c>ctrl &amp;&amp; Pressed(...)</c> shape, so the guard
    /// that keeps a chord from doubling as its bare key is the one guard this file already has.
    /// No bare C is bound anywhere in the shell; the chord is stated as a chord anyway, because
    /// the day one is bound must not be the day this quietly starts firing on it.
    /// </summary>
    public bool EditorCopy { get; init; }

    /// <summary>
    /// Map editor: Ctrl+X — cut (copy + empty), one undo step. The bare X is the keyboard
    /// eyedropper (<see cref="MenuEditor"/>), which is already <c>!ctrl</c>-guarded, so the two
    /// meanings of the key cannot collide.
    /// </summary>
    public bool EditorCut { get; init; }

    /// <summary>
    /// Map editor: Ctrl+V — start a floating paste, placed by the next paint press. The bare V
    /// is the sprite editor's vertical flip (<see cref="EditorFlipV"/>), already
    /// <c>!ctrl</c>-guarded for exactly this reason.
    /// </summary>
    public bool EditorPaste { get; init; }

    /// <summary>Editor: B — pencil ↔ bucket. Bare key, so Ctrl-guarded like every editor letter (wave 2c).</summary>
    public bool EditorToolToggle { get; init; }

    /// <summary>Editor: Tab — cycle the region size 8/16/32 px.</summary>
    public bool EditorRegionCycle { get; init; }

    /// <summary>Editor: F — flip the region horizontally (PICO-8's key, per the niche survey).</summary>
    public bool EditorFlipH { get; init; }

    /// <summary>Editor: V — flip the region vertically.</summary>
    public bool EditorFlipV { get; init; }

    /// <summary>Editor: R — rotate the region 90° clockwise.</summary>
    public bool EditorRotate { get; init; }

    /// <summary>Editor: Delete — clear the region to color 0. On the map screen: empty the marked rectangle, or (with nothing marked) select tile 0.</summary>
    public bool EditorClear { get; init; }

    /// <summary>
    /// Map editor: <c>`</c> — show / hide the tile grid, TIC-80's own key for its own
    /// <c>drawGridButton</c> (REFERENCES-EDITORS §3.1). A bare key like the editor letters,
    /// and unclaimed by anything else in the shell.
    /// </summary>
    public bool EditorGridToggle { get; init; }

    /// <summary>
    /// Map editor: Space is held — the temporary-pan modifier (TIC-80 <c>map.c</c>:
    /// <c>bool space = tic_api_key(tic, tic_key_space)</c>, and a left drag under it pans the
    /// viewport). A level, not an edge: the gesture it modifies lasts as long as the button
    /// is down.
    ///
    /// <para><b>What it costs, said out loud.</b> Space is also half of
    /// <see cref="EditorPaintDown"/>. On the MAP screen the modifier wins — the router there
    /// refuses to open a paint gesture while this is true, and the keyboard pencil is bare Z —
    /// which is the same "a chord must not double as its bare key" rule Ctrl already carries
    /// over Z. The sprite screen never reads this field and keeps Z/Space unchanged.</para>
    /// </summary>
    public bool EditorPanModifier { get; init; }

    /// <summary>
    /// Editor: the keyboard pencil is held — bare Z (never the Ctrl+Z chord) or Space, either
    /// one (M9 stage 2.5: draw a stroke by holding this and steering with the arrows). Space
    /// doubles as the game mode's pause; the modes never read each other's fields.
    /// </summary>
    public bool EditorPaintDown { get; init; }

    /// <summary>Editor: the keyboard pencil went down this frame — begins a stroke (or fills) at the canvas cursor.</summary>
    public bool EditorPaintPressed { get; init; }

    /// <summary>
    /// Editor: the keyboard pencil came up this frame — commits the stroke as one undo step,
    /// the exact mirror of the mouse's <see cref="EditorMouse.LeftReleased"/>. Pressing Ctrl
    /// while Z is held counts as a release: the chord takes the key, and the open gesture must
    /// close rather than smear into whatever Ctrl+Z is about to undo.
    /// </summary>
    public bool EditorPaintReleased { get; init; }

    /// <summary>
    /// Editor: which toolbar digit (1-6, the toolbar's top-to-bottom order) was pressed this
    /// frame, 0 for none. The digit policy — stubs stay dead, group slots cycle their variant
    /// on a repeat — is <see cref="EditorIcons.PressToolDigit"/>'s, not the reader's: this
    /// only reports the key. <b>Bare digits only</b> since wave 3b-2: Shift+digit belongs to
    /// <see cref="EditorFlagDigit"/>, and a chord must not double as its bare key — the same
    /// rule <see cref="MenuConfirm"/> and the editor letters already carry for Ctrl.
    /// </summary>
    public int EditorToolDigit { get; init; }

    /// <summary>
    /// Editor: which flag toggle Shift+1..8 asked for this frame as a 1-based digit, 0 for
    /// none — the keyboard half of clicking a cell in the flag row (wave 3b-2). Bit
    /// <c>EditorFlagDigit - 1</c>, because the flags are numbered 0-7 (<c>Fget</c>'s index,
    /// PICO-8's "indexed from 0 starting from the left") while the keys they sit on are not.
    /// Eight, not six: the tool digits stop at 6 and 7/8 were free either way, but the whole
    /// row is on Shift so the eight keys read as one block.
    /// </summary>
    public int EditorFlagDigit { get; init; }

    /// <summary>
    /// Editor: Ctrl is held — the shape tool's "filled" modifier (PICO-8's pattern), a level
    /// and not an edge because the preview must flip between outline and filled the moment the
    /// modifier changes. Note the Z-pencil interplay: Ctrl arriving while Z is held releases
    /// the paint key (the chord rule), so the keyboard's filled-shape gesture is Space+Ctrl —
    /// Space stays down through the chord on purpose.
    /// </summary>
    public bool EditorShapeFill { get; init; }

    /// <summary>Editor: , — previous palette color (the keyboard's swatch hand; shown in the swatch tooltips).</summary>
    public bool EditorColorPrev { get; init; }

    /// <summary>Editor: . — next palette color, wrapping 15 → 0.</summary>
    public bool EditorColorNext { get; init; }

    /// <summary>Editor: PageUp — one layer up the stack (toward the covering layers), clamped at 5. The layer tabs' keyboard half (wave 2h).</summary>
    public bool EditorLayerUp { get; init; }

    /// <summary>Editor: PageDown — one layer down, clamped at the base.</summary>
    public bool EditorLayerDown { get; init; }

    /// <summary>
    /// Editor: Shift+Left/Right — step the edited sprite along the sheet strip, -1, 0 or +1.
    /// The keyboard half of clicking a cell in the strip; without it the most-used action of
    /// the editor had no key at all, which the input-parity law (M9 stage 2.5) forbids.
    /// Movement is in <b>strip</b> cells, not canonical sheet cells, because the strip is what
    /// the author sees: right runs across <see cref="SheetStrip.Columns"/>, the way the eye
    /// reads it. <see cref="SheetStrip"/> owns that shape — the wave-2k re-cut moved these
    /// two fields' range without touching a line here.
    /// </summary>
    public int EditorSheetDx { get; init; }

    /// <summary>Editor: Shift+Up/Down — the same step across the strip's <see cref="SheetStrip.Rows"/> rows.</summary>
    public int EditorSheetDy { get; init; }

    /// <summary>
    /// Map editor: Ctrl+Shift+Left/Right — grow or shrink the picker's block by one strip
    /// column, -1, 0 or +1 (wave 3e). The keyboard half of dragging a rectangle across the tile
    /// picker; without it the wave's headline feature would be mouse-only, which the
    /// input-parity law forbids. Ctrl is what separates it from <see cref="EditorSheetDx"/>,
    /// which moves the tile itself — and <see cref="EditorSheetDx"/> gained its own
    /// <c>!ctrl</c> guard in the same breath, by this file's standing rule that a chord must
    /// not double as its bare key.
    /// </summary>
    public int EditorBlockDx { get; init; }

    /// <summary>Map editor: Ctrl+Shift+Up/Down — the same step in strip rows.</summary>
    public int EditorBlockDy { get; init; }
    // ---- the tab strip's keyboard half ----

    /// <summary>
    /// Any editor screen: Alt+Left — one tab left along the strip. LIKO-12's and PICO-8's own
    /// key for exactly this (REFERENCES-EDITORS §8 item 16), and the only one that can serve all
    /// three screens: Home already means "start of the line" in the code editor, so the two-tab
    /// <c>Home</c> toggle could not grow a third stop.
    ///
    /// <para>The bare arrow still fires on the same frame — this is a modifier, not a
    /// replacement — and each router decides which of the two it obeys, exactly as the code
    /// screen decides between Ctrl+Left and Left.</para>
    /// </summary>
    public bool EditorTabPrev { get; init; }

    /// <summary>Any editor screen: Alt+Right — one tab right along the strip.</summary>
    public bool EditorTabNext { get; init; }

    // ---- the code editor's own keys ----
    //
    // Every field below is a key edge nothing in this shell read before, named for the one
    // screen that reads it — the same way EditorGridToggle and EditorPanModifier were added for
    // the map. Keys the code editor shares with its siblings are NOT duplicated here: Home is
    // ToStart, PageUp/PageDown are EditorLayerUp/Down, Tab is EditorRegionCycle, Delete is
    // EditorClear, the arrows are MenuUp..MenuRight and Ctrl+Z/Y/S are the editor's undo, redo
    // and save. One field per physical key edge, with per-mode meanings in its own comment, is
    // this struct's rule (see Slower/Faster for the oldest example of it).

    /// <summary>
    /// Code editor: Enter — break the line. Its own field rather than <see cref="MenuConfirm"/>,
    /// which also fires on a bare Z: in a text editor Z is the letter z, and a key that both
    /// types and breaks lines is the one bug this separation exists to make impossible.
    /// Ctrl-guarded like every other editor key, so a future Ctrl+Enter cannot double as it.
    /// </summary>
    public bool CodeNewLine { get; init; }

    /// <summary>
    /// Code editor: Backspace, as an <b>edge</b>. <see cref="Rewinding"/> is the same physical
    /// key read as a level, because the game's rewind is a hold; a hold here would delete a
    /// line a frame, which is why the two readings are two fields and each mode reads one.
    /// </summary>
    public bool CodeBackspace { get; init; }

    /// <summary>Code editor: End — to the end of the line. Ctrl+End is <see cref="CodeDocumentEnd"/> instead.</summary>
    public bool CodeLineEnd { get; init; }

    /// <summary>Code editor: Ctrl+Home — to the start of the file (TIC-80's own chord).</summary>
    public bool CodeDocumentStart { get; init; }

    /// <summary>Code editor: Ctrl+End — to the end of the file.</summary>
    public bool CodeDocumentEnd { get; init; }

    /// <summary>Code editor: Ctrl+Left — one word left (PICO-8: "CTRL-LEFT, RIGHT to jump by word").</summary>
    public bool CodeWordLeft { get; init; }

    /// <summary>Code editor: Ctrl+Right — one word right.</summary>
    public bool CodeWordRight { get; init; }

    /// <summary>
    /// Code editor: Shift is held — the extend-the-selection modifier for every movement key,
    /// which is what "Shift+любое движение — выделение" means in one bit. A level, not an edge:
    /// it modifies whatever movement arrives on the same frame.
    /// </summary>
    public bool CodeExtend { get; init; }

    /// <summary>Code editor: Ctrl+A — select the whole buffer.</summary>
    public bool CodeSelectAll { get; init; }

    /// <summary>Code editor: Ctrl+C — copy the selection into the editor's clipboard.</summary>
    public bool CodeCopy { get; init; }

    /// <summary>Code editor: Ctrl+X — cut it.</summary>
    public bool CodeCut { get; init; }

    /// <summary>Code editor: Ctrl+V — paste. Never fires <see cref="EditorFlipV"/>, which is Ctrl-guarded.</summary>
    public bool CodePaste { get; init; }

    /// <summary>Code editor: Ctrl+F — open the find line (all three references' key). Never fires <see cref="EditorFlipH"/>.</summary>
    public bool CodeFind { get; init; }

    /// <summary>Code editor: Ctrl+G — the next occurrence (PICO-8: "CTRL-G to repeat the last search again").</summary>
    public bool CodeFindNext { get; init; }

    /// <summary>
    /// Code editor: Ctrl+L — jump to a line number (PICO-8: "CTRL-L to jump to a line number").
    /// TIC-80 spends Ctrl+G on this and LIKO-12 spends Ctrl+I on incremental search; taking
    /// PICO-8's pair keeps find-next and go-to-line on two keys that mean the same thing in the
    /// only reference that has both.
    /// </summary>
    public bool CodeGoToLine { get; init; }
}

/// <summary>
/// Turns raw keyboard states into <see cref="ShellCommands"/>, remembering the previous
/// frame so a held key produces exactly one command. Lives in the shell, one instance per
/// window; polling the keyboard is the caller's job, because the same
/// <see cref="KeyboardState"/> also drives the cartridge's own input.
/// </summary>
public sealed class ShellCommandReader
{
    private KeyboardState _previous;

    public ShellCommands Read(KeyboardState keyboard)
    {
        bool ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        // Alt joins the modifier row for the tab strip (REFERENCES-EDITORS §8 item 16). It
        // modifies the arrows and nothing else, so no existing field needs a guard: the routers
        // that care check the chord before the bare key, the way the code screen already has to
        // for Ctrl+Left.
        bool alt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        // The keyboard pencil's held state is computed against each frame's own Ctrl: pressing
        // Ctrl mid-hold turns "down" into "released" (the chord takes the key), and both edges
        // below fall out of comparing the two frames' truths rather than raw key states.
        bool prevCtrl = _previous.IsKeyDown(Keys.LeftControl) || _previous.IsKeyDown(Keys.RightControl);
        bool paintDown = (!ctrl && keyboard.IsKeyDown(Keys.Z)) || keyboard.IsKeyDown(Keys.Space);
        bool paintWasDown = (!prevCtrl && _previous.IsKeyDown(Keys.Z)) || _previous.IsKeyDown(Keys.Space);
        var commands = new ShellCommands
        {
            Quit = Pressed(keyboard, Keys.Escape),
            TogglePause = Pressed(keyboard, Keys.Space),
            StepForward = Pressed(keyboard, Keys.OemPeriod),
            StepBack = Pressed(keyboard, Keys.OemComma),
            Slower = Pressed(keyboard, Keys.OemOpenBrackets),
            Faster = Pressed(keyboard, Keys.OemCloseBrackets),
            Rewinding = keyboard.IsKeyDown(Keys.Back),
            ToStart = Pressed(keyboard, Keys.Home),
            SaveReplay = Pressed(keyboard, Keys.F5),
            PlayReplay = Pressed(keyboard, Keys.F8),
            // The arrows stay the arrows for everyone. An earlier cut of this gated MenuUp..
            // MenuRight on !shift so the editor could give Shift+arrows to the sheet strip —
            // and silently took Shift+Down away from the LIBRARY, which reads the same four
            // fields (QuarpGame's library navigation). The gate belongs where the meaning
            // differs, not in the reader every mode shares: the editor ignores cursor movement
            // on the frames where the sheet step fires, and nothing else changes.
            MenuUp = Pressed(keyboard, Keys.Up),
            MenuDown = Pressed(keyboard, Keys.Down),
            MenuLeft = Pressed(keyboard, Keys.Left),
            MenuRight = Pressed(keyboard, Keys.Right),
            // Shift+arrows step the tile, Ctrl+Shift+arrows size the picker's block (wave 3e).
            // The !ctrl here is the same rule the editor letters carry: a chord must not double
            // as its bare key, and before this wave Ctrl+Shift+Right quietly stepped the tile.
            EditorSheetDx = shift && !ctrl
                ? (Pressed(keyboard, Keys.Right) ? 1 : 0) - (Pressed(keyboard, Keys.Left) ? 1 : 0)
                : 0,
            EditorSheetDy = shift && !ctrl
                ? (Pressed(keyboard, Keys.Down) ? 1 : 0) - (Pressed(keyboard, Keys.Up) ? 1 : 0)
                : 0,
            EditorBlockDx = shift && ctrl
                ? (Pressed(keyboard, Keys.Right) ? 1 : 0) - (Pressed(keyboard, Keys.Left) ? 1 : 0)
                : 0,
            EditorBlockDy = shift && ctrl
                ? (Pressed(keyboard, Keys.Down) ? 1 : 0) - (Pressed(keyboard, Keys.Up) ? 1 : 0)
                : 0,
            MenuConfirm = (!ctrl && Pressed(keyboard, Keys.Z)) || Pressed(keyboard, Keys.Enter),
            MenuEditor = !ctrl && Pressed(keyboard, Keys.X),
            EditorUndo = ctrl && Pressed(keyboard, Keys.Z),
            EditorRedo = ctrl && Pressed(keyboard, Keys.Y),
            EditorSave = ctrl && Pressed(keyboard, Keys.S),
            // The map's clipboard chords (wave 3e), read exactly like undo/redo/save above —
            // the three keys TIC-80 uses for the same three verbs (REFERENCES-EDITORS §3.1).
            EditorCopy = ctrl && Pressed(keyboard, Keys.C),
            EditorCut = ctrl && Pressed(keyboard, Keys.X),
            EditorPaste = ctrl && Pressed(keyboard, Keys.V),
            // The editor letters carry the !ctrl guard for the same reason MenuConfirm does:
            // a chord must not double as its bare key, today (Ctrl+S over a future S-binding)
            // or when a chord lands on these letters later.
            EditorToolToggle = !ctrl && Pressed(keyboard, Keys.B),
            EditorRegionCycle = Pressed(keyboard, Keys.Tab),
            EditorFlipH = !ctrl && Pressed(keyboard, Keys.F),
            EditorFlipV = !ctrl && Pressed(keyboard, Keys.V),
            EditorRotate = !ctrl && Pressed(keyboard, Keys.R),
            EditorClear = Pressed(keyboard, Keys.Delete),
            // The grid key is the backtick, TIC-80's own. Bare, and Ctrl-guarded like every
            // other editor letter — no chord lands on it today, and the guard is what keeps
            // that true when one does.
            EditorGridToggle = !ctrl && Pressed(keyboard, Keys.OemTilde),
            EditorPanModifier = keyboard.IsKeyDown(Keys.Space),
            EditorPaintDown = paintDown,
            EditorPaintPressed = paintDown && !paintWasDown,
            EditorPaintReleased = !paintDown && paintWasDown,
            // Shift splits the digit row in two (wave 3b-2): bare 1-6 are the toolbar's, and
            // Shift+1..8 are the flag panel's. Before this wave Shift+1..6 quietly selected a
            // tool — an accident of ToolDigit not looking at the modifier, named here rather
            // than left to be rediscovered.
            EditorToolDigit = shift ? 0 : ToolDigit(keyboard),
            EditorFlagDigit = shift ? FlagDigit(keyboard) : 0,
            EditorShapeFill = ctrl,
            EditorColorPrev = Pressed(keyboard, Keys.OemComma),
            EditorColorNext = Pressed(keyboard, Keys.OemPeriod),
            EditorLayerUp = Pressed(keyboard, Keys.PageUp),
            EditorLayerDown = Pressed(keyboard, Keys.PageDown),
            EditorTabPrev = alt && Pressed(keyboard, Keys.Left),
            EditorTabNext = alt && Pressed(keyboard, Keys.Right),
            // The code editor's block. Each of these is a key edge nothing else in the shell
            // reads; the keys it shares with its siblings are read through the shared fields
            // above (see the block comment in ShellCommands).
            CodeNewLine = !ctrl && Pressed(keyboard, Keys.Enter),
            CodeBackspace = Pressed(keyboard, Keys.Back),
            CodeLineEnd = !ctrl && Pressed(keyboard, Keys.End),
            CodeDocumentStart = ctrl && Pressed(keyboard, Keys.Home),
            CodeDocumentEnd = ctrl && Pressed(keyboard, Keys.End),
            CodeWordLeft = ctrl && Pressed(keyboard, Keys.Left),
            CodeWordRight = ctrl && Pressed(keyboard, Keys.Right),
            CodeExtend = shift,
            CodeSelectAll = ctrl && Pressed(keyboard, Keys.A),
            CodeCopy = ctrl && Pressed(keyboard, Keys.C),
            CodeCut = ctrl && Pressed(keyboard, Keys.X),
            CodePaste = ctrl && Pressed(keyboard, Keys.V),
            CodeFind = ctrl && Pressed(keyboard, Keys.F),
            CodeFindNext = ctrl && Pressed(keyboard, Keys.G),
            CodeGoToLine = ctrl && Pressed(keyboard, Keys.L),
        };
        _previous = keyboard;
        return commands;
    }

    /// <summary>First freshly pressed toolbar digit, 0 for none — two digits in one frame is not a gesture worth defining.</summary>
    private int ToolDigit(KeyboardState keyboard)
    {
        for (int i = 0; i < 6; i++)
        {
            if (Pressed(keyboard, Keys.D1 + i))
            {
                return i + 1;
            }
        }
        return 0;
    }

    /// <summary>First freshly pressed flag digit 1-8, 0 for none — the same one-key-per-frame rule as the toolbar's.</summary>
    private int FlagDigit(KeyboardState keyboard)
    {
        for (int i = 0; i < SpriteEditorSession.FlagBits; i++)
        {
            if (Pressed(keyboard, Keys.D1 + i))
            {
                return i + 1;
            }
        }
        return 0;
    }

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_previous.IsKeyDown(key);
}

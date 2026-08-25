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
    /// Any editor screen: <c>Ctrl+H</c> — print bank indexes in hexadecimal or in decimal
    /// (REFERENCES-EDITORS §8 item 20). PICO-8's own key for its own switch, in its own words:
    /// "CTRL-H to toggle hex view (shows sprite index in hexadecimal)" (§2.3), and it is offered
    /// there in <em>both</em> graphics editors, which is why this field is not named for one
    /// screen — every router reads it and <see cref="IndexFormat"/> is the one thing it moves.
    ///
    /// <para><b>The key was checked by name before it was taken.</b> <c>Keys.H</c> occurs once
    /// in this file, in <c>PianoRows</c>, and that whole row is read only when neither Ctrl nor
    /// Shift is down — so the chord was free. PICO-8's <em>other</em> key from the same
    /// paragraph, <c>CTRL-G</c> for the grid, was <b>not</b> free: <see cref="CodeFindNext"/> is
    /// Ctrl+G on the code screen. That is why the canvas grid one item up took the map's
    /// <c>`</c> (<see cref="EditorGridToggle"/>) instead — one gesture for two palettes, and no
    /// chord taken twice.</para>
    /// </summary>
    public bool EditorHexToggle { get; init; }

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
    /// Map editor: Shift is held — the tile palette slides over the map while it is
    /// (REFERENCES-EDITORS §3.1: TIC-80's <c>drawSheetButton</c> is labelled "SHOW TILES
    /// [shift]"). A level and not an edge, because the palette is meant to be <em>peeked</em>:
    /// hold, look, pick with Shift+arrows or the mouse, release. The button next to it latches
    /// the same overlay open for a pointer-only hand.
    ///
    /// <para><b>What it shares and why that is safe.</b> Shift also selects the sheet-step
    /// fields (<see cref="EditorSheetDx"/>) and the flag digits. That overlap is the feature and
    /// not a clash: the very chord that steps the tile is the one that shows the tiles, so the
    /// author sees what he is stepping through. Nothing else in the shell reads a bare Shift as
    /// a verb.</para>
    /// </summary>
    public bool EditorTilesModifier { get; init; }

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

    /// <summary>
    /// Map editor: Ctrl is held — the bucket's <b>replace</b> modifier. TIC-80 hangs
    /// <c>replaceTile</c> on exactly this key over exactly this tool (REFERENCES-EDITORS §3.1,
    /// §8 item 6), and the sprite screen one bank over already reads the same physical key for
    /// the same verb, so the author learns one rule for both.
    ///
    /// <para><b>Why a second field carrying the same bit as <see cref="EditorShapeFill"/>.</b>
    /// This struct already does that twice — <see cref="EditorTilesModifier"/> and
    /// <see cref="EditorSecondaryInk"/> are both Shift — and for the reason that applies here: a
    /// field is named for what a screen means by it, not for which key it came off, so a later
    /// edit can move one screen's modifier without silently moving another's. The shape tool's
    /// "filled" flag and the map bucket's "replace" flag are two meanings, on two screens,
    /// sharing one key today.</para>
    ///
    /// <para>A level and not an edge, like its twin: the modifier is read at the press it
    /// modifies, and the map router also reads it to decide that Ctrl+Space is the keyboard's
    /// bucket press rather than a pan (<see cref="EditorPanModifier"/>).</para>
    /// </summary>
    public bool EditorReplaceModifier { get; init; }

    /// <summary>Editor: , — previous palette color (the keyboard's swatch hand; shown in the swatch tooltips).</summary>
    public bool EditorColorPrev { get; init; }

    /// <summary>Editor: . — next palette color, wrapping 15 → 0.</summary>
    public bool EditorColorNext { get; init; }

    /// <summary>
    /// Sprite editor: <c>-</c> — one step down the brush ladder, wrapping to the widest. TIC-80's
    /// own key for its own <c>updateBrushSize(sprite, -1)</c> (REFERENCES-EDITORS §2.1). Not
    /// Ctrl-guarded the way the editor letters are and for the same reason the brackets are not:
    /// no chord lands on the minus key in this shell, and the letters' guard exists because a
    /// letter is where a chord goes.
    /// </summary>
    public bool EditorBrushSmaller { get; init; }

    /// <summary>Sprite editor: <c>=</c> — one step up the brush ladder, wrapping to the narrowest (TIC-80's <c>=</c>).</summary>
    public bool EditorBrushBigger { get; init; }

    /// <summary>
    /// Sprite editor: Shift is held — the keyboard's <b>second ink</b>, which is LIKO-12's own
    /// arrangement verbatim (<c>src/OS/DiskOS/Editors/sprite.lua</c>:
    /// <c>if isKDown("lshift","rshift") or isMDown(2) then b = 2 end</c>, REFERENCES-EDITORS
    /// §2.2). A level and not an edge, like <see cref="EditorShapeFill"/>: what matters is which
    /// ink was in the hand at the press, and the press is an edge of another field.
    ///
    /// <para>It shares the physical key with <see cref="EditorSheetDx"/> and
    /// <see cref="EditorFlagDigit"/>, which is the struct's rule (one field per key edge, per-mode
    /// meanings in the comments) rather than a collision: those two are Shift+<em>arrow</em> and
    /// Shift+<em>digit</em>, and this one only ever colours Shift+Z/Space and Shift+X, keys
    /// neither of them reads.</para>
    /// </summary>
    public bool EditorSecondaryInk { get; init; }

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

    /// <summary>
    /// Any editor screen: which editor <b>F1..F5</b> asked for this frame as a <b>1-based</b>
    /// number, 0 for none — the shape <see cref="EditorToolDigit"/>, <see cref="SfxPianoKey"/>
    /// and <see cref="MusicSlotDigit"/> already use, so a default-constructed frame means
    /// "nothing was asked for" rather than "the first editor". TIC-80's own keys for exactly this
    /// (REFERENCES-EDITORS §8 item 16: "<c>F1..F5</c>, <c>Alt+1..5</c>, <c>Ctrl+PgUp/PgDn</c>"),
    /// and the thing <see cref="EditorTabPrev"/>/<see cref="EditorTabNext"/> cannot do: a ring of
    /// five costs up to two presses to cross and a named key costs one.
    ///
    /// <para><b>What the number means has exactly one owner</b> and it is not this struct:
    /// <see cref="EditorIcons.EditorTabForNumber"/> turns it into a screen, off the same
    /// <see cref="EditorIcons.LiveEditorTabs"/> list Alt+Left/Right walks, so the key and the
    /// strip cannot come to disagree about which editor is the third one.</para>
    ///
    /// <para><b>F5 is shared with <see cref="SaveReplay"/>, and that is this struct's ordinary
    /// arrangement rather than a clash</b> — the same shape <see cref="Slower"/> has carried
    /// since the beginning. Replay saving is a GAME-mode verb and the editor tabs are an
    /// EDITOR-mode verb; the modes never read each other's fields, and the gate belongs where
    /// the meaning differs. Unmodified, like TIC-80's: no chord in this shell lands on F1..F5,
    /// and <see cref="ShellModeMachine.SwitchEditorTab"/> is a no-op outside the five editor
    /// screens anyway.</para>
    /// </summary>
    public int EditorTabJump { get; init; }

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

    /// <summary>
    /// Code editor: <b>F11</b> — drop the chrome and give the whole console to the text
    /// (15 lines x 40 columns instead of 11 x 36). The mode ADR-029 named as the mitigation for
    /// the tightest code page in the niche: "полноэкранный режим без хрома возвращает все 15
    /// строк".
    ///
    /// <para><b>Why F11 and not the TAB that ADR-029 cites.</b> The ADR quotes PICO-8's
    /// "TAB | Toggle fullscreen view" as the precedent, and it is the right precedent for the
    /// verb — but PICO-8 spends TAB on that in the <em>sprite and map</em> editors only. In its
    /// code editor TAB is the indent key ("TAB to indent a selection (shift to un-indent)",
    /// REFERENCES-EDITORS §4.3), exactly as it is here (<see cref="EditorRegionCycle"/> →
    /// <c>InsertTab</c>) and in LIKO-12 ("Tab вставляет один пробел", §4.2). So the one screen
    /// that needs the mode is the one screen whose key is already taken, and the other
    /// reference's key for the same verb is taken instead: TIC-80's <c>processShortcuts</c>
    /// list ends "F6 CRT-шейдер, F8 скриншот, F9 видео, <b>F11 фуллскрин</b>"
    /// (REFERENCES-EDITORS §1). F11 is free on every screen in this shell and reads as
    /// "fullscreen" to anyone who has used a browser.</para>
    ///
    /// <para>Guarded on Shift so <see cref="CodeFullscreenStatus"/> can have the other half of
    /// the key, the way PICO-8 itself splits <c>TAB</c> from <c>SHIFT-TAB</c>.</para>
    /// </summary>
    public bool CodeFullscreen { get; init; }

    /// <summary>
    /// Code editor: <b>Shift+F11</b> — summon or dismiss the single status row while the screen
    /// is fullscreen. PICO-8's own two-step, one key apart: <c>TAB</c> is "Toggle fullscreen
    /// view" and <c>SHIFT-TAB</c> is "full-fullscreen mode (with no red menu bars)"
    /// (REFERENCES-EDITORS §2.3), i.e. the reference itself distinguishes "fullscreen that still
    /// carries a bar" from "fullscreen carrying nothing". Ours reads the pair the other way round
    /// — bare F11 gives the bar-less page, Shift+F11 puts the one bar back — because the whole
    /// point of the mode here is the fifteenth line, and a mode whose default costs a line would
    /// be the wrong default.
    /// </summary>
    public bool CodeFullscreenStatus { get; init; }

    /// <summary>
    /// Code editor: <b>Alt+Up</b> — the caret to the previous declaration in the buffer. Two of
    /// the three references spell this key letter for letter (REFERENCES-EDITORS §8 item 14:
    /// LIKO-12's <c>Alt+Up/Down</c> → <c>searchPreviousFunction</c>/<c>searchNextFunction</c>,
    /// PICO-8's "ALT-UP, DOWN to navigate to the previous, next function"; TIC-80 spends
    /// <c>Ctrl+O</c> on an outline list instead), so there was nothing left to choose.
    ///
    /// <para><b>It cannot collide with the tab strip.</b> <see cref="EditorTabPrev"/> and
    /// <see cref="EditorTabNext"/> are Alt+<em>Left/Right</em>; this is Alt+<em>Up/Down</em>, and
    /// the four arrows are four keys. The bare arrow still fires on the same frame — Alt is a
    /// modifier and not a replacement — so the code screen's router checks the chord before the
    /// bare key, exactly as it already has to for Ctrl+Left.</para>
    ///
    /// <para><b>What counts as a declaration is not decided here.</b> That is a fact about the
    /// text, so it belongs to the document: <see cref="CodeEditorSession.IsDeclarationLine"/>
    /// carries the whole rule in words, and this field is only the key edge.</para>
    /// </summary>
    public bool CodeDeclarationPrev { get; init; }

    /// <summary>Code editor: <b>Alt+Down</b> — the caret to the next declaration; the exact mirror of <see cref="CodeDeclarationPrev"/>.</summary>
    public bool CodeDeclarationNext { get; init; }

    // ---- the sound editor's own key ----

    /// <summary>
    /// Sound editor: which <b>piano</b> key was struck this frame, as a <b>1-based</b> index
    /// into the two standard rows, 0 for none — the shape <see cref="EditorToolDigit"/> and
    /// <see cref="EditorFlagDigit"/> already use, so a default-constructed frame means "nothing
    /// was struck" rather than "the lowest C". Keys 1-12 are the lower row <c>zsxdcvgbhnjm</c>
    /// in the current octave; 13-25 are the upper row <c>q2w3er5t6y7ui</c> an octave above it,
    /// thirteen keys ending on the C that closes the octave. Those two strings are TIC-80's and PICO-8's
    /// letter for letter (REFERENCES-EDITORS §8 item 17 calls them a de-facto standard and
    /// forbids drifting), and <see cref="SfxEditorView.NoteOfPianoKey"/> turns the index into a
    /// semitone.
    ///
    /// <para><b>Why one field for twenty-five keys, and why the digits appear twice.</b> "One
    /// field per physical press" is a rule about <em>facts</em>, not about key codes: which
    /// piano key was struck is one fact, and fifteen new letter fields would be fifteen owners
    /// of it. Five of the twenty-five keys — 2, 3, 5, 6, 7 — also reach
    /// <see cref="EditorToolDigit"/>, and Z, X, B, V and R also reach <see cref="MenuConfirm"/>,
    /// <see cref="MenuEditor"/>, <see cref="EditorToolToggle"/>, <see cref="EditorFlipV"/> and
    /// <see cref="EditorRotate"/>. That is the same shape <see cref="EditorSheetDx"/> and
    /// <see cref="MenuLeft"/> have carried since wave 2h, and it is resolved the same way and in
    /// the same place: <b>the gate belongs where the meaning differs</b>. The sound screen's
    /// router reads this field and ignores those six; every other screen reads those six and
    /// never looks here. Putting the gate in this reader instead would take Shift+Down away from
    /// the library all over again.</para>
    ///
    /// <para>Ctrl- and Shift-guarded, so Ctrl+Z stays undo, Ctrl+V stays paste and Shift+arrows
    /// stay the field steppers — the standing rule that a chord must not double as its bare
    /// key.</para>
    /// </summary>
    public int SfxPianoKey { get; init; }

    // ---- the music editor's own key ----

    /// <summary>
    /// Music editor: which <b>decimal digit</b> was struck this frame, as a <b>1-based</b> value
    /// (1 means '0', 10 means '9'), 0 for none — the shape <see cref="EditorToolDigit"/>,
    /// <see cref="EditorFlagDigit"/> and <see cref="SfxPianoKey"/> already use, so a
    /// default-constructed frame means "nothing was typed" rather than "the digit zero".
    ///
    /// <para><b>Why a decimal digit and not a hex one.</b> A channel of <c>music.bin</c> names an
    /// SFX slot 0-63, and every other screen in this shell prints a slot in decimal —
    /// <c>SFX 07</c> on the sound screen, <c>#003</c> on the sprite screen. TIC-80's tracker takes
    /// its SFX numbers as two hex digits; copying that here would make the same slot read
    /// <c>3F</c> on one tab and <c>63</c> on the next, which is a second numbering of one fact.
    /// So two digits it is, and <see cref="MusicEditorView.TypeDigit"/> owns what a pair
    /// means.</para>
    ///
    /// <para><b>Why one field for ten keys, and why the digits appear twice.</b> "One field per
    /// physical press" is a rule about <em>facts</em>, not about key codes: which digit was typed
    /// is one fact. Six of the ten — 1 to 6 — also reach <see cref="EditorToolDigit"/>, and five
    /// of them also reach <see cref="SfxPianoKey"/>. That is the same shape those two fields
    /// already carry, resolved the same way and in the same place: <b>the gate belongs where the
    /// meaning differs</b>. The music screen's router reads this field and ignores the toolbar
    /// digits; every other screen reads those and never looks here.</para>
    ///
    /// <para>Ctrl- and Shift-guarded, so Ctrl+Z stays undo and Shift+1..8 stay the mute and solo
    /// rows — the standing rule that a chord must not double as its bare key.</para>
    /// </summary>
    public int MusicSlotDigit { get; init; }
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
            // PICO-8's hex-view chord (REFERENCES-EDITORS §8 item 20, §2.3). Ctrl+H was
            // unclaimed: the only other reader of this physical key is the piano row below,
            // which is gated on `ctrl || shift ? 0` and therefore cannot see a chord at all.
            EditorHexToggle = ctrl && Pressed(keyboard, Keys.H),
            EditorPanModifier = keyboard.IsKeyDown(Keys.Space),
            EditorTilesModifier = shift,
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
            EditorReplaceModifier = ctrl,
            EditorColorPrev = Pressed(keyboard, Keys.OemComma),
            EditorColorNext = Pressed(keyboard, Keys.OemPeriod),
            // TIC-80's brush keys, on the physical keys TIC-80 names (REFERENCES-EDITORS §2.1).
            EditorBrushSmaller = Pressed(keyboard, Keys.OemMinus),
            EditorBrushBigger = Pressed(keyboard, Keys.OemPlus),
            EditorSecondaryInk = shift,
            EditorLayerUp = Pressed(keyboard, Keys.PageUp),
            EditorLayerDown = Pressed(keyboard, Keys.PageDown),
            EditorTabPrev = alt && Pressed(keyboard, Keys.Left),
            EditorTabNext = alt && Pressed(keyboard, Keys.Right),
            // TIC-80's five named tab keys (REFERENCES-EDITORS §8 item 16). Unmodified, the way
            // the reference has them and the way F11 above is: no chord in this shell lands on
            // F1..F5. F5 also fills SaveReplay two lines up — one key edge read by two fields
            // whose modes never meet; see the field's own comment for the whole argument.
            EditorTabJump = FunctionTab(keyboard),
            // Alt+Up/Down: the code screen's declaration walk. Alt is already read above for the
            // tab strip's Left/Right, and these are the OTHER two arrows, so nothing is shared.
            CodeDeclarationPrev = alt && Pressed(keyboard, Keys.Up),
            CodeDeclarationNext = alt && Pressed(keyboard, Keys.Down),
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
            // TIC-80's own fullscreen key (REFERENCES-EDITORS §1, processShortcuts), split on
            // Shift into PICO-8's own two steps. Nothing else in this shell reads F11, and no
            // Ctrl guard is needed: Ctrl+F11 means nothing here either way.
            CodeFullscreen = !shift && Pressed(keyboard, Keys.F11),
            CodeFullscreenStatus = shift && Pressed(keyboard, Keys.F11),
            // The sound editor's piano. Guarded on both modifiers so the chords above keep
            // their keys; see the field's own comment for why the five digits and the five
            // letters it shares with other fields are gated in the router and not here.
            SfxPianoKey = ctrl || shift ? 0 : PianoKey(keyboard),
            // The music editor's digits, guarded on both modifiers for the same reason the piano
            // is: Ctrl+Z stays undo and Shift+1..8 stay that screen's mute and solo rows.
            MusicSlotDigit = ctrl || shift ? 0 : SlotDigit(keyboard),
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

    /// <summary>
    /// First freshly pressed function key F1..F5 as a 1-based number, 0 for none — the same
    /// one-key-per-frame rule the toolbar digits, the flag digits and the piano follow. Five and
    /// not twelve: <see cref="EditorIcons.LiveEditorTabs"/> holds five stops, F8 is
    /// <see cref="PlayReplay"/> and F11 is <see cref="CodeFullscreen"/>, so the range stops
    /// exactly where the tab strip does.
    /// </summary>
    private int FunctionTab(KeyboardState keyboard)
    {
        for (int i = 0; i < EditorIcons.LiveEditorTabs.Count; i++)
        {
            if (Pressed(keyboard, Keys.F1 + i))
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

    /// <summary>
    /// The two piano rows, in semitone order, exactly as TIC-80 and PICO-8 spell them:
    /// <c>z s x d c v g b h n j m</c> is C to B of the current octave, and
    /// <c>q 2 w 3 e r 5 t 6 y 7 u i</c> is C to C an octave above. The array IS the layout —
    /// one owner, so the letters cannot be right in the reader and wrong in a tooltip.
    /// </summary>
    private static readonly Keys[] PianoRows =
    {
        Keys.Z, Keys.S, Keys.X, Keys.D, Keys.C, Keys.V, Keys.G, Keys.B, Keys.H, Keys.N, Keys.J, Keys.M,
        Keys.Q, Keys.D2, Keys.W, Keys.D3, Keys.E, Keys.R, Keys.D5, Keys.T, Keys.D6, Keys.Y, Keys.D7,
        Keys.U, Keys.I,
    };

    /// <summary>First freshly struck piano key as a 1-based index, 0 for none — the same one-key-per-frame rule the toolbar and flag digits follow.</summary>
    private int PianoKey(KeyboardState keyboard)
    {
        for (int i = 0; i < PianoRows.Length; i++)
        {
            if (Pressed(keyboard, PianoRows[i]))
            {
                return i + 1;
            }
        }
        return 0;
    }

    /// <summary>
    /// First freshly struck decimal digit as a 1-based value, 0 for none — the same
    /// one-key-per-frame rule the toolbar digits, the flag digits and the piano follow. The top
    /// row only: <c>Keys.D0</c>..<c>Keys.D9</c>, because the numeric keypad is not on every
    /// keyboard this console runs on and a second source for one fact is what this file exists to
    /// prevent.
    /// </summary>
    private int SlotDigit(KeyboardState keyboard)
    {
        for (int i = 0; i < 10; i++)
        {
            if (Pressed(keyboard, Keys.D0 + i))
            {
                return i + 1;
            }
        }
        return 0;
    }

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_previous.IsKeyDown(key);
}

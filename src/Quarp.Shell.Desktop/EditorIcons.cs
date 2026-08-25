namespace Quarp.Shell.Desktop;

/// <summary>
/// Every clickable icon-button on the sprite editor screen (M9 stage 2.5, the owner's
/// verdict layout). One enum for all three strips — tabs, toolbar, status — because they share
/// one mechanism: <see cref="SpriteEditorLayout"/> places them, <see cref="SpriteEditorRenderer"/>
/// draws them and <c>QuarpGame</c> routes clicks through the same rectangles, exactly the
/// swatch discipline extended to buttons.
/// </summary>
public enum EditorButton
{
    /// <summary>Top-left: leave the editor — the mouse's Esc.</summary>
    ExitTab,

    // The right-edge tab group, in the owner's dictated order: from the right corner leftwards
    // music, sounds, tilemaps, sprites, code. Only sprites is alive in this portion (ADR-026).
    CodeTab,
    SpritesTab,
    TilemapTab,
    SoundTab,
    MusicTab,

    // The left toolbar, top to bottom — all six live since wave 2f. Select, shape and
    // transform are photoshop-style GROUP slots (owner's second review): one button, several
    // variants, a corner marker, a flyout.
    ToolSelect,
    ToolPencil,
    ToolFill,
    ToolStamp,
    ToolShape,
    ToolTransform,

    // The status bar's buttons. Save doubles as the modified/saved indicator; clear moved
    // here from the dead action row by the owner's second review — right of redo, Del hotkey
    // unchanged.
    Save,
    Undo,
    Redo,
    Clear,

    /// <summary>
    /// The sprite-size toggle in the band between the canvas and the right column (wave 2h):
    /// its face is the current size as text ("8"/"16"/"32"), a click opens the 8/16/32 list
    /// through the same flyout machinery the tool groups use, Tab keeps cycling.
    /// </summary>
    SizeToggle,

    // The five layer tabs above the sheet window (wave 2h, ADR-027): text-faced digits 1-5,
    // the active one highlighted; PgUp/PgDn are their keyboard half.
    LayerTab1,
    LayerTab2,
    LayerTab3,
    LayerTab4,
    LayerTab5,

    /// <summary>
    /// The map editor's second tool column (M9 stage 3): "the empty tile". Clicking it selects
    /// tile 0, which MAP-FORMAT §2 defines as emptiness rather than sprite 0, so this button IS
    /// the map's eraser; Del is its keyboard twin. It is the one member of this enum the sprite
    /// editor never places — see <see cref="EditorIcons.BelongsToSpriteEditor"/>, the owner of
    /// which screen a button belongs to now that two screens share this list.
    /// </summary>
    ToolEraser,

    /// <summary>
    /// The map editor's fourth tool (wave 3d): TIC-80's <c>MAP_DRAG_MODE</c> — a left-drag on
    /// the canvas pans the viewport instead of painting. Map-only, like
    /// <see cref="ToolEraser"/>: the sprite canvas is one region at one magnification and has
    /// nothing to pan.
    /// </summary>
    ToolHand,

    /// <summary>
    /// The map editor's grid switch (wave 3d): TIC-80's <c>drawGridButton</c> ("SHOW/HIDE GRID",
    /// key <c>`</c>), on by default. Map-only for the same reason the hand is — the sprite
    /// canvas draws its own pixel grid unconditionally and has no such choice to offer.
    /// </summary>
    GridToggle,

    /// <summary>
    /// The code editor's first tool button: open the find line (TIC-80's <c>FIND [ctrl+f]</c>,
    /// REFERENCES-EDITORS §4.1). Code-only — the other two screens search nothing.
    /// </summary>
    ToolFind,

    /// <summary>
    /// The code editor's second tool button: open the go-to-line field (TIC-80's <c>GOTO</c>
    /// button; the key is Ctrl+L, PICO-8's). Code-only for the same reason.
    /// </summary>
    ToolGoTo,

    /// <summary>
    /// The sound editor's only tool button: play or stop the slot on screen (TIC-80's
    /// <c>Space</c>, LIKO-12's <c>playRect</c>, PICO-8's "SPACE to play/stop" — all three
    /// consoles put this in the same place). Sound-only: nothing on the other three screens has
    /// a voice to start.
    /// </summary>
    ToolPlay,

    /// <summary>
    /// The map editor's tile palette switch (wave R3): TIC-80's <c>drawSheetButton</c>
    /// ("SHOW TILES [shift]"). On a 160x90 console the palette cannot stand beside the map —
    /// see <see cref="MapEditorLayout"/> for the arithmetic — so it slides over it, held open
    /// by Shift or latched by this button. Map-only: the sprite editor's sheet window is always
    /// on screen and has nothing to reveal.
    /// </summary>
    TilesToggle,

    /// <summary>
    /// The map editor's whole-map view switch (wave R3): TIC-80's <c>drawWorldButton</c>
    /// ("WORLD MAP [tab]", <c>src/studio/editors/world.c</c>). The minimap of a 256x72 map
    /// needs 128x36 pixels even at two cells to the pixel, which is more than half the console's
    /// content band, so it is a mode rather than a panel. Map-only for the obvious reason.
    /// </summary>
    WorldToggle,
}

/// <summary>
/// Which 8x8 glyph a button (or a state of one) shows — separate from <see cref="EditorButton"/>
/// only because the save button has two faces (saved / modified) and one identity.
/// </summary>
public enum EditorIcon
{
    Exit,
    Code,
    Sprites,
    Tilemap,
    Sound,
    Music,
    SelectRect,
    Pencil,
    Fill,
    Stamp,
    ShapeOval,
    ShapeRect,
    FlipH,
    FlipV,
    Rotate,
    Clear,
    Undo,
    Redo,
    Saved,
    Modified,
    // Appended past the wave-2e set: the mask array below is indexed by this enum's values,
    // so new glyphs only ever join at the end.
    SelectBrush,
    Wand,
    Eraser,
    Hand,
    Grid,
    Find,
    GoTo,
    Play,
    Stop,
    // Appended in wave R3, when the map screen moved onto the console and its tile palette and
    // its whole-map view became controls of their own (ADR-029, REFERENCES-EDITORS §3.1:
    // TIC-80's drawSheetButton and drawWorldButton).
    Tiles,
    World,
}

/// <summary>
/// The editor's icon set and button metadata, all in one owner: pixel masks (byte rows, MSB =
/// leftmost, the same packing discipline as <see cref="Quarp.Core.SystemFont"/>'s glyphs — drawn
/// in code so the icons carry the project's own license and no asset pipeline), tooltip texts
/// (name + hotkey, the order's contract), the stub list (which buttons are visible-but-dead in
/// this wave), the group-slot metadata (which slots carry variants, their icons and tooltips)
/// and the whole toolbar-digit policy. Keeping stub-ness, group-ness and the digit policy in
/// the same file is deliberate: enabling a tool in wave 2f is a one-place edit, and a digit
/// can never wake a tool this file still calls a stub.
///
/// <para>The masks are first honest drafts — the work order names icon looks as the owner's
/// call, so these exist to be legible, not final.</para>
/// </summary>
public static class EditorIcons
{
    /// <summary>Icon side in mask pixels; on screen it is scaled by a whole integer like all pixel art here.</summary>
    public const int IconPixels = 8;

    /// <summary>One byte per row, bit 7 = leftmost pixel — indexed by <see cref="EditorIcon"/>.</summary>
    private static readonly byte[][] _masks =
    {
        new byte[] // Exit: an arrow leaving through a right-side door frame
        {
            0b00000111,
            0b00000001,
            0b01000001,
            0b11111101,
            0b01000001,
            0b00000001,
            0b00000001,
            0b00000111,
        },
        new byte[] // Code: angle brackets
        {
            0b00000000,
            0b00100100,
            0b01000010,
            0b10000001,
            0b10000001,
            0b01000010,
            0b00100100,
            0b00000000,
        },
        new byte[] // Sprites: the transparency checkerboard
        {
            0b11001100,
            0b11001100,
            0b00110011,
            0b00110011,
            0b11001100,
            0b11001100,
            0b00110011,
            0b00110011,
        },
        new byte[] // Tilemap: a 3x3 cell grid
        {
            0b11111110,
            0b10010010,
            0b10010010,
            0b11111110,
            0b10010010,
            0b10010010,
            0b11111110,
            0b00000000,
        },
        new byte[] // Sound: a speaker cone with a wave arc
        {
            0b00010000,
            0b00110000,
            0b11110010,
            0b11110001,
            0b11110001,
            0b11110010,
            0b00110000,
            0b00010000,
        },
        new byte[] // Music: a beamed pair of eighth notes
        {
            0b00111110,
            0b00100010,
            0b00100010,
            0b00100010,
            0b00100010,
            0b11101110,
            0b11101110,
            0b00000000,
        },
        new byte[] // SelectRect: a dashed rectangle (marching ants, frozen) — the select slot's rectangle face
        {
            0b11011011,
            0b00000000,
            0b10000001,
            0b10000001,
            0b00000000,
            0b10000001,
            0b10000001,
            0b11011011,
        },
        new byte[] // Pencil: a diagonal body tapering to a tip at lower-left
        {
            0b00000111,
            0b00001110,
            0b00011100,
            0b00111000,
            0b01110000,
            0b11100000,
            0b11000000,
            0b10000000,
        },
        new byte[] // Fill: a paint bucket, handle arc on top
        {
            0b00111100,
            0b01000010,
            0b11111111,
            0b01111110,
            0b01111110,
            0b00111100,
            0b00111100,
            0b00000000,
        },
        new byte[] // Stamp: a handle over a flared base
        {
            0b00011000,
            0b00011000,
            0b00111100,
            0b00111100,
            0b01111110,
            0b11111111,
            0b11111111,
            0b00000000,
        },
        new byte[] // ShapeOval: an ellipse outline — the shape slot's face while the oval variant is current
        {
            0b00000000,
            0b00111100,
            0b01000010,
            0b10000001,
            0b10000001,
            0b01000010,
            0b00111100,
            0b00000000,
        },
        new byte[] // ShapeRect: a rectangle outline — the rectangle variant's face (the old paired glyph, split per the order)
        {
            0b00000000,
            0b11111111,
            0b10000001,
            0b10000001,
            0b10000001,
            0b10000001,
            0b11111111,
            0b00000000,
        },
        new byte[] // FlipH: a horizontal double arrow across a dashed vertical axis
        {
            0b00010000,
            0b00000000,
            0b01000010,
            0b11111111,
            0b01000010,
            0b00000000,
            0b00010000,
            0b00000000,
        },
        new byte[] // FlipV: a vertical double arrow across a dashed horizontal axis
        {
            0b00010000,
            0b00111000,
            0b00010000,
            0b10010010,
            0b00010000,
            0b00010000,
            0b00111000,
            0b00010000,
        },
        new byte[] // Rotate: an open ring with a clockwise arrowhead at the gap
        {
            0b00000111,
            0b00111010,
            0b01000010,
            0b01000010,
            0b01000010,
            0b01000010,
            0b00111100,
            0b00000000,
        },
        new byte[] // Clear: a cross
        {
            0b10000001,
            0b01000010,
            0b00100100,
            0b00011000,
            0b00011000,
            0b00100100,
            0b01000010,
            0b10000001,
        },
        new byte[] // Undo: an arrow pointing left, tail curling under
        {
            0b00100000,
            0b01000000,
            0b11111100,
            0b01000010,
            0b00100001,
            0b00000001,
            0b00000110,
            0b00000000,
        },
        new byte[] // Redo: the undo arrow mirrored
        {
            0b00000100,
            0b00000010,
            0b00111111,
            0b01000010,
            0b10000100,
            0b10000000,
            0b01100000,
            0b00000000,
        },
        new byte[] // Saved: a floppy with its label block
        {
            0b11111110,
            0b10011001,
            0b10011001,
            0b10000001,
            0b10111101,
            0b10111101,
            0b10111101,
            0b11111111,
        },
        new byte[] // Modified: the floppy with a dot instead of the label — unsaved work inside
        {
            0b11111110,
            0b10011001,
            0b10011001,
            0b10000001,
            0b10011001,
            0b10011001,
            0b10000001,
            0b11111111,
        },
        new byte[] // SelectBrush: a brush — handle from the top-right down to a flared bristle head
        {
            0b00000011,
            0b00000111,
            0b00001110,
            0b00011100,
            0b01111000,
            0b11110000,
            0b11110000,
            0b01100000,
        },
        new byte[] // Wand: a four-point sparkle at the tip of a thin diagonal handle — the select slot's wand face
        {
            0b00000100,
            0b00001010,
            0b00000100,
            0b00001000,
            0b00010000,
            0b00100000,
            0b01000000,
            0b10000000,
        },
        new byte[] // Eraser: a tilted block rubber over the line it is wiping — the map's "tile 0"
        {
            0b00000000,
            0b00011100,
            0b00111110,
            0b01111110,
            0b01111100,
            0b00111000,
            0b00000000,
            0b11111111,
        },
        new byte[] // Hand: an open palm with four fingers and a thumb — TIC-80's tic_icon_hand
        {
            0b00010100,
            0b00010101,
            0b00110101,
            0b00110101,
            0b01110111,
            0b01111110,
            0b00111110,
            0b00011100,
        },
        new byte[] // Grid: four cells of tile boundary — the lines the switch shows and hides
        {
            0b10010010,
            0b00000000,
            0b00000000,
            0b10010010,
            0b00000000,
            0b00000000,
            0b10010010,
            0b00000000,
        },
        new byte[] // Find: a magnifier — round lens over a handle running to the lower right
        {
            0b00111100,
            0b01000010,
            0b10000001,
            0b10000001,
            0b01000010,
            0b00111100,
            0b00001100,
            0b00000110,
        },
        new byte[] // GoTo: an arrow aimed right, between two lines of text — "jump to line"
        {
            0b11111000,
            0b00000100,
            0b00000010,
            0b11111111,
            0b00000010,
            0b00000100,
            0b11111000,
            0b00000000,
        },
        new byte[] // Play: the transport triangle every console puts on this button
        {
            0b01000000,
            0b01100000,
            0b01111000,
            0b01111110,
            0b01111110,
            0b01111000,
            0b01100000,
            0b01000000,
        },
        new byte[] // Stop: the filled square — the play button's other face while the slot sounds
        {
            0b00000000,
            0b01111110,
            0b01111110,
            0b01111110,
            0b01111110,
            0b01111110,
            0b01111110,
            0b00000000,
        },
        new byte[] // Tiles: four solid tiles with the seams between them — the sheet, not the grid
        {
            0b01110111,
            0b01110111,
            0b01110111,
            0b00000000,
            0b01110111,
            0b01110111,
            0b01110111,
            0b00000000,
        },
        new byte[] // World: the whole map as a frame with the viewport riding inside it
        {
            0b11111111,
            0b10000001,
            0b10110001,
            0b10110001,
            0b10000001,
            0b10000001,
            0b10000001,
            0b11111111,
        },
    };

    /// <summary>How many glyphs exist — the atlas sizes its strip from this.</summary>
    public static int IconCount => _masks.Length;

    /// <summary>True if the icon has an ink pixel at (col, row), both 0-7 — same shape as SystemFont.IsSet.</summary>
    public static bool IsSet(EditorIcon icon, int col, int row) =>
        ((_masks[(int)icon][row] >> (IconPixels - 1 - col)) & 1) != 0;

    /// <summary>
    /// The buttons that are drawn but deliberately dead: the future-editor tabs whose portions
    /// of ADR-026 have not landed. This list is the <b>one owner</b> of "is it a stub": the
    /// layout paints from it, the click routing refuses from it, and
    /// <see cref="PressToolDigit"/> consults it — so waking a button is one edit here, never a
    /// drift between what looks dead and what is dead.
    ///
    /// <para><b>The list is now empty, and that is the whole of what the music-editor wave
    /// means.</b> The CODE tab left it in the code-editor screen wave, the SOUND tab in the
    /// sound-editor screen wave, and the MUSIC tab in this one — the fifth and last editor
    /// landed, so there is no button left in this shell that is drawn but dead. The method stays
    /// where it is and keeps its callers: it is still the one owner of the answer, the two frame
    /// painters still ask it which face to dim, the four routers still refuse a stub before any
    /// verb, and <see cref="PressToolDigit"/> still consults it. An empty list is a fact about
    /// today, not a reason to delete the mechanism — the day a screen is sketched before it is
    /// wired, one name goes back in here and the dimming, the refusal and the honest "when"
    /// tooltip all come back with it.</para>
    /// </summary>
    public static bool IsStub(EditorButton button) => false;

    /// <summary>
    /// The live editor tab a click asks the shell to open, or null for every button that is
    /// not one. Consulted <b>before</b> <see cref="ClickButton"/> and <see cref="ClickMapButton"/>
    /// by the shell and by the button-contract test alike, because a tab's verb belongs to
    /// <see cref="ShellModeMachine"/> and not to either session — the same split
    /// <see cref="ClickButton"/> already makes for the exit tab, made explicit now that there
    /// are two live tabs to travel between (M9 stage 3). Clicking the tab of the screen you
    /// are already on is the honest no-op the machine turns it into.
    /// </summary>
    public static ShellMode? TabTarget(EditorButton button) => button switch
    {
        EditorButton.SpritesTab => ShellMode.Editor,
        EditorButton.TilemapTab => ShellMode.MapEditor,
        EditorButton.CodeTab => ShellMode.CodeEditor,
        EditorButton.SoundTab => ShellMode.SfxEditor,
        EditorButton.MusicTab => ShellMode.MusicEditor,
        _ => null,
    };

    /// <summary>
    /// The live editor tabs in strip order, left to right — the one list
    /// <see cref="ShellModeMachine.CycleEditorTab"/> walks for Alt+Left/Right. It is derived
    /// from nothing: a tab joins here the same day it stops being a stub, and the parity sweeps
    /// notice if it does not.
    /// </summary>
    public static IReadOnlyList<ShellMode> LiveEditorTabs { get; } = new[]
    {
        ShellMode.CodeEditor, ShellMode.Editor, ShellMode.MapEditor, ShellMode.SfxEditor,
        ShellMode.MusicEditor,
    };

    /// <summary>
    /// Which of the two editor screens places a button. One owner, because a single enum now
    /// serves two layouts and "forgot to place it" must stay a red test rather than a missing
    /// button: <see cref="SpriteEditorLayout"/> places everything this answers true for, and
    /// <see cref="MapEditorLayout"/> everything <see cref="BelongsToMapEditor"/> does.
    /// </summary>
    public static bool BelongsToSpriteEditor(EditorButton button) => button is not (
        EditorButton.ToolEraser or EditorButton.ToolHand or EditorButton.GridToggle
        or EditorButton.ToolFind or EditorButton.ToolGoTo or EditorButton.ToolPlay
        or EditorButton.TilesToggle or EditorButton.WorldToggle);

    /// <summary>
    /// The map editor's own button list: the shared chrome (tabs, exit, save, undo, redo), the
    /// four tools of TIC-80's <c>map->mode</c> (pencil, hand, select, fill — REFERENCES-EDITORS
    /// §3.1), the grid switch and the eraser. Everything the map still has no model verb for —
    /// stamp, shapes, transforms, clear, the sprite-size toggle and the layer tabs — stays off
    /// this screen, because a placed button with nothing behind it is the defect class the
    /// button contract test closed in wave 2g.
    ///
    /// <para><see cref="EditorButton.ToolSelect"/> and <see cref="EditorButton.ToolFill"/> are
    /// borrowed from the sprite editor's list rather than duplicated under map-only names: one
    /// enum member per <em>meaning</em>, and "rectangle select" means the same thing on both
    /// canvases. What differs is the verb behind the click, and that already has two owners —
    /// <see cref="ClickButton"/> and <see cref="ClickMapButton"/>.</para>
    /// </summary>
    public static bool BelongsToMapEditor(EditorButton button) => button is
        EditorButton.ExitTab or EditorButton.CodeTab or EditorButton.SpritesTab
        or EditorButton.TilemapTab or EditorButton.SoundTab or EditorButton.MusicTab
        or EditorButton.ToolPencil or EditorButton.ToolHand or EditorButton.ToolSelect
        or EditorButton.ToolFill or EditorButton.ToolEraser or EditorButton.GridToggle
        or EditorButton.TilesToggle or EditorButton.WorldToggle
        or EditorButton.Save or EditorButton.Undo or EditorButton.Redo;

    /// <summary>
    /// The code editor's own button list: the shared chrome (six tabs, exit, save, undo, redo)
    /// and the two tools this screen has model verbs for — find and go-to-line. Everything the
    /// code screen has nothing to do with (every drawing tool, the grid, the eraser, the layer
    /// tabs, the size toggle, clear) stays off it, because a placed button with nothing behind
    /// it is the defect class the button contract test closed in wave 2g.
    /// </summary>
    public static bool BelongsToCodeEditor(EditorButton button) => button is
        EditorButton.ExitTab or EditorButton.CodeTab or EditorButton.SpritesTab
        or EditorButton.TilemapTab or EditorButton.SoundTab or EditorButton.MusicTab
        or EditorButton.ToolFind or EditorButton.ToolGoTo
        or EditorButton.Save or EditorButton.Undo or EditorButton.Redo;

    /// <summary>
    /// The <b>sound</b> editor's own button list: the shared chrome (six tabs, exit, save, undo,
    /// redo) and the one tool this screen has a model verb for — play/stop. Everything the sound
    /// screen has nothing to do with (every drawing tool, the grid, the eraser, the layer tabs,
    /// the size toggle, clear, find, go-to) stays off it, because a placed button with nothing
    /// behind it is the defect class the button contract test closed in wave 2g. The screen's
    /// other controls — the slot selector, the three grids, the wave and effect rows, the three
    /// stepper fields — are rectangles of <see cref="SfxEditorLayout"/> rather than members of
    /// this enum, exactly as the palette swatches and the sheet slider are on the sprite screen.
    /// </summary>
    public static bool BelongsToSfxEditor(EditorButton button) => button is
        EditorButton.ExitTab or EditorButton.CodeTab or EditorButton.SpritesTab
        or EditorButton.TilemapTab or EditorButton.SoundTab or EditorButton.MusicTab
        or EditorButton.ToolPlay
        or EditorButton.Save or EditorButton.Undo or EditorButton.Redo;

    /// <summary>
    /// The <b>music</b> editor's own button list: the shared chrome (six tabs, exit, save, undo,
    /// redo) and the one tool this screen has a model verb for — play/stop. Everything the music
    /// screen has nothing to do with (every drawing tool, the grid, the eraser, the layer tabs,
    /// the size toggle, clear, find, go-to) stays off it, because a placed button with nothing
    /// behind it is the defect class the button contract test closed in wave 2g. The screen's
    /// other controls — the pattern grid, the three section markers of a row, the four mute and
    /// four solo toggles, the whole-song overview — are rectangles of
    /// <see cref="MusicEditorLayout"/> rather than members of this enum, exactly as the palette
    /// swatches are on the sprite screen and the three grids on the sound screen.
    /// </summary>
    public static bool BelongsToMusicEditor(EditorButton button) => button is
        EditorButton.ExitTab or EditorButton.CodeTab or EditorButton.SpritesTab
        or EditorButton.TilemapTab or EditorButton.SoundTab or EditorButton.MusicTab
        or EditorButton.ToolPlay
        or EditorButton.Save or EditorButton.Undo or EditorButton.Redo;

    /// <summary>
    /// The photoshop-style group slots (owner's second review): a corner marker on the button,
    /// a flyout of variants on long-press/right-click, a repeat-digit cycle on the keyboard.
    /// One owner for "is it a group", like the stub list: the renderer marks from it, the
    /// shell arms the long-press from it, and the layout sizes flyouts from
    /// <see cref="GroupVariantCount"/> next door. The size toggle joined in wave 2h — it is a
    /// group in every mechanical sense (variants, flyout, marker), differing only in that its
    /// short click opens the list (<see cref="ClickOpensFlyout"/>) and its faces are text.
    /// </summary>
    public static bool IsGroupSlot(EditorButton button) => button is
        EditorButton.ToolSelect or EditorButton.ToolShape or EditorButton.ToolTransform
        or EditorButton.SizeToggle;

    /// <summary>
    /// The one group slot whose short click opens its flyout instead of acting (wave 2h):
    /// the size toggle's only verb IS choosing from the list, so "click = list" (the owner's
    /// card) and there is no separate click action to perform. The shell and the button
    /// contract test both consult this, so the dispatch cannot drift between them.
    /// </summary>
    public static bool ClickOpensFlyout(EditorButton button) => button == EditorButton.SizeToggle;

    /// <summary>How many variants a group slot's flyout shows; 0 for everything that is not a group.</summary>
    public static int GroupVariantCount(EditorButton button) => button switch
    {
        EditorButton.ToolSelect => 3,       // SelectionVariant: rectangle, brush, wand (2g)
        EditorButton.ToolShape => 2,        // ShapeVariant: oval, rectangle
        EditorButton.ToolTransform => 3,    // TransformVariant: flip H, flip V, rotate
        EditorButton.SizeToggle => 3,       // region sides: 1, 2, 4 cells — 8/16/32 px (2h)
        _ => 0,
    };

    /// <summary>
    /// The size list's variant → region side in cells (1, 2, 4) — with <see cref="SizeVariantOf"/>
    /// the one owner of the list↔session mapping, so the flyout highlight and the chosen size
    /// cannot disagree. Index i is 2^i cells: the same 8/16/32 ladder Tab walks.
    /// </summary>
    public static int SizeVariantCells(int variant) => 1 << variant;

    /// <summary>Region side in cells → the size list's variant index. Inverse of <see cref="SizeVariantCells"/>.</summary>
    public static int SizeVariantOf(int cells) => cells switch { 1 => 0, 2 => 1, _ => 2 };

    /// <summary>The size a region side shows as text — "8"/"16"/"32", the toggle's face and the list's labels alike.</summary>
    public static string SizeLabel(int cells) =>
        (cells * Quarp.Core.VirtualConsole.SpriteSize).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The text a text-faced button shows instead of an 8x8 glyph, or null for icon buttons
    /// (wave 2h): the size toggle wears its current size, the layer tabs their 1-based
    /// number — the owner's sketch shows numbers, and digits at UI scale are more legible
    /// than any 8-px numeral glyph. One owner, so the renderer never guesses which face a
    /// button has.
    /// </summary>
    public static string? ButtonText(EditorButton button, SpriteEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return button switch
        {
            EditorButton.SizeToggle => SizeLabel(session.RegionCells),
            EditorButton.LayerTab1 or EditorButton.LayerTab2 or EditorButton.LayerTab3
                or EditorButton.LayerTab4 or EditorButton.LayerTab5 =>
                (button - EditorButton.LayerTab1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>
    /// The session's remembered variant of a group slot, as the flyout index
    /// <see cref="VariantIcon"/> expects. Lived as a private copy in the sprite renderer
    /// until the boot-wave's crash repair: the face test must ask the very truth the
    /// renderer draws, and a private method of a device-owning class is exactly what a
    /// headless test can never reach.
    /// </summary>
    public static int CurrentVariant(SpriteEditorSession session, EditorButton slot)
    {
        ArgumentNullException.ThrowIfNull(session);
        return slot switch
        {
            EditorButton.ToolSelect => (int)session.CurrentSelection,
            EditorButton.ToolShape => (int)session.CurrentShape,
            EditorButton.SizeToggle => SizeVariantOf(session.RegionCells),
            _ => (int)session.CurrentTransform,
        };
    }

    /// <summary>
    /// The one answer to "what does this button wear": its text, or its icon — exactly one,
    /// never neither. Text first: that is the contract <see cref="IconFor"/> always carried
    /// in prose ("the renderer branches on ButtonText before ever asking here"), and the
    /// module-split wave broke it exactly the way prose breaks — the sprite renderer kept
    /// computing an icon for every button, and the first text-faced one (the size toggle)
    /// took the whole window down with ArgumentOutOfRange on the editor's first frame, while
    /// 1264 headless tests stayed green, because only code holding a GraphicsDevice ever made
    /// the choice. This method is that choice with no device attached, and
    /// <c>EditorButtonFaceTests</c> walks every button of every layout through it.
    /// </summary>
    public static (string? Text, EditorIcon? Icon) Face(EditorButton button, SpriteEditorSession session)
    {
        string? text = ButtonText(button, session);
        if (text is not null)
        {
            return (text, null);
        }
        return (null, IsGroupSlot(button)
            ? VariantIcon(button, CurrentVariant(session, button))
            : IconFor(button));
    }

    /// <summary>
    /// The glyph of one flyout variant — also the slot's own face for that variant (the wave's
    /// card: the group button shows the CURRENT variant). Indices are the session enums' values
    /// (<see cref="ShapeVariant"/>, <see cref="TransformVariant"/>), so this is the cast the
    /// enum comments promise, not a second table that could reorder.
    /// </summary>
    public static EditorIcon VariantIcon(EditorButton slot, int variant) => (slot, variant) switch
    {
        (EditorButton.ToolSelect, (int)SelectionVariant.Rectangle) => EditorIcon.SelectRect,
        (EditorButton.ToolSelect, (int)SelectionVariant.Brush) => EditorIcon.SelectBrush,
        (EditorButton.ToolSelect, (int)SelectionVariant.Wand) => EditorIcon.Wand,
        (EditorButton.ToolShape, (int)ShapeVariant.Oval) => EditorIcon.ShapeOval,
        (EditorButton.ToolShape, (int)ShapeVariant.Rectangle) => EditorIcon.ShapeRect,
        (EditorButton.ToolTransform, (int)TransformVariant.FlipH) => EditorIcon.FlipH,
        (EditorButton.ToolTransform, (int)TransformVariant.FlipV) => EditorIcon.FlipV,
        (EditorButton.ToolTransform, (int)TransformVariant.Rotate) => EditorIcon.Rotate,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), (slot, variant), "not a group slot variant."),
    };

    /// <summary>Flyout variant tooltips — the 3-second hover contract extends to variants, and each names its key path.</summary>
    public static string VariantTooltip(EditorButton slot, int variant) => (slot, variant) switch
    {
        (EditorButton.ToolSelect, (int)SelectionVariant.Rectangle) => "RECTANGLE SELECT  1 CYCLES",
        (EditorButton.ToolSelect, (int)SelectionVariant.Brush) => "BRUSH SELECT  1 CYCLES",
        (EditorButton.ToolSelect, (int)SelectionVariant.Wand) => "WAND SELECT  1 CYCLES   CLICK PICKS ONE COLOR AREA",
        (EditorButton.ToolShape, (int)ShapeVariant.Oval) => "OVAL  5 CYCLES",
        (EditorButton.ToolShape, (int)ShapeVariant.Rectangle) => "RECTANGLE  5 CYCLES",
        (EditorButton.ToolTransform, (int)TransformVariant.FlipH) => "FLIP H  F",
        (EditorButton.ToolTransform, (int)TransformVariant.FlipV) => "FLIP V  V",
        (EditorButton.ToolTransform, (int)TransformVariant.Rotate) => "ROTATE 90  R",
        (EditorButton.SizeToggle, 0 or 1 or 2) =>
            $"{SizeLabel(SizeVariantCells(variant))} PX SPRITE  TAB CYCLES",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), (slot, variant), "not a group slot variant."),
    };

    /// <summary>The glyph a button shows. Save's two faces are the renderer's pick — this returns the clean one.</summary>
    public static EditorIcon IconFor(EditorButton button) => button switch
    {
        EditorButton.ExitTab => EditorIcon.Exit,
        EditorButton.CodeTab => EditorIcon.Code,
        EditorButton.SpritesTab => EditorIcon.Sprites,
        EditorButton.TilemapTab => EditorIcon.Tilemap,
        EditorButton.SoundTab => EditorIcon.Sound,
        EditorButton.MusicTab => EditorIcon.Music,
        EditorButton.ToolPencil => EditorIcon.Pencil,
        EditorButton.ToolFill => EditorIcon.Fill,
        EditorButton.ToolStamp => EditorIcon.Stamp,
        // The group slots' clean defaults (variant 0); the renderer asks VariantIcon with the
        // session's current variant instead, the same way it picks Save's two faces.
        EditorButton.ToolSelect => EditorIcon.SelectRect,
        EditorButton.ToolShape => EditorIcon.ShapeOval,
        EditorButton.ToolTransform => EditorIcon.FlipH,
        EditorButton.Clear => EditorIcon.Clear,
        EditorButton.Save => EditorIcon.Saved,
        EditorButton.Undo => EditorIcon.Undo,
        EditorButton.Redo => EditorIcon.Redo,
        EditorButton.ToolEraser => EditorIcon.Eraser,
        EditorButton.ToolHand => EditorIcon.Hand,
        EditorButton.GridToggle => EditorIcon.Grid,
        EditorButton.TilesToggle => EditorIcon.Tiles,
        EditorButton.WorldToggle => EditorIcon.World,
        EditorButton.ToolFind => EditorIcon.Find,
        EditorButton.ToolGoTo => EditorIcon.GoTo,
        // The clean face; the sound renderer swaps in EditorIcon.Stop while the slot sounds,
        // exactly as it swaps Save's two faces — one identity, two faces, one owner of the pick.
        EditorButton.ToolPlay => EditorIcon.Play,
        // The text-faced buttons (size toggle, layer tabs) have no glyph on purpose — the
        // renderer branches on ButtonText before ever asking here, so reaching this is a bug.
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "a text-faced button has no icon (ButtonText owns its face)."),
    };

    /// <summary>
    /// The hover tooltip: name + hotkey for live buttons (the order's input-parity contract
    /// made visible — every key path is discoverable from the mouse path), and an honest
    /// "when" for stubs. ASCII only: the system font has no other alphabet.
    /// </summary>
    public static string Tooltip(EditorButton button) => button switch
    {
        EditorButton.ExitTab => "EXIT  ESC",
        EditorButton.CodeTab => "CODE  ALT+LEFT/RIGHT WALK THE TABS",
        EditorButton.SpritesTab => "SPRITES  HOME SWITCHES   ALT+LEFT/RIGHT WALK THE TABS",
        EditorButton.TilemapTab => "MAPS  HOME SWITCHES   ALT+LEFT/RIGHT WALK THE TABS",
        EditorButton.SoundTab => "SOUNDS  ALT+LEFT/RIGHT WALK THE TABS",
        EditorButton.MusicTab => "MUSIC  ALT+LEFT/RIGHT WALK THE TABS",
        EditorButton.ToolSelect => "SELECT  1 CYCLES   DRAG MARKS, GRAB INSIDE MOVES, ESC DROPS",
        EditorButton.ToolPencil => "PENCIL  2   ARROWS MOVE, Z/SPACE DRAW, X PICK, SHIFT+ARROWS SPRITE",
        EditorButton.ToolFill => "FILL  3   Z/SPACE FILLS AT THE CURSOR",
        EditorButton.ToolStamp => "STAMP  4   CLICK/Z PRINTS THE LAST SELECTION",
        EditorButton.ToolShape => "SHAPES  5 CYCLES   DRAG DRAWS, CTRL FILLS, HOLD/RCLICK VARIANTS",
        EditorButton.ToolTransform => "TRANSFORM  6 CYCLES, F/V/R APPLY   CLICK APPLIES, HOLD/RCLICK VARIANTS",
        EditorButton.Clear => "CLEAR  DEL",
        // The three map-only buttons live in THIS table, not in MapTooltip: their meaning does
        // not differ between screens (the sprite editor simply never places them), and a text
        // that exists only in the override would leave this method — the fallthrough every
        // other caller reaches — answering "REDO CTRL+Y" for them.
        EditorButton.ToolEraser => "ERASE  DEL   SELECTS TILE 000",
        EditorButton.ToolHand => "DRAG MAP  2   SPACE+DRAG PANS ANYWHERE   ARROWS AND [ ] PGUP/PGDN TRAVEL",
        EditorButton.GridToggle => "SHOW/HIDE GRID  `",
        // The two switches wave R3 added, in THIS table for the reason the other map-only
        // buttons are: their meaning does not differ between screens, only one screen places
        // them. Both name the key the reference names (REFERENCES-EDITORS §3.1).
        EditorButton.TilesToggle =>
            "SHOW TILES  HOLD SHIFT, OR CLICK TO LATCH   WHEEL OVER IT FLIPS THE PAGE",
        EditorButton.WorldToggle => "WHOLE MAP  TAB   CLICK IT TO TRAVEL",
        // The two code-only buttons live in THIS table for the same reason the map's three do:
        // their meaning does not differ between screens, only one screen places them.
        EditorButton.ToolFind => "FIND  CTRL+F   ENTER OR CTRL+G WALKS, ESC CLOSES",
        EditorButton.ToolGoTo => "GO TO LINE  CTRL+L   TYPE A NUMBER, ENTER JUMPS",
        // The sound-only button lives in THIS table for the reason the map's three and the
        // code's two do: its meaning does not differ between screens, only one screen places it.
        EditorButton.ToolPlay => "PLAY / STOP SLOT  SPACE",
        EditorButton.Save => "SAVE  CTRL+S",
        EditorButton.Undo => "UNDO  CTRL+Z",
        EditorButton.SizeToggle => "SPRITE SIZE  TAB CYCLES, CLICK LISTS 8/16/32",
        EditorButton.LayerTab1 => "LAYER 1  PGUP/PGDN",
        EditorButton.LayerTab2 => "LAYER 2  PGUP/PGDN",
        EditorButton.LayerTab3 => "LAYER 3  PGUP/PGDN",
        EditorButton.LayerTab4 => "LAYER 4  PGUP/PGDN",
        EditorButton.LayerTab5 => "LAYER 5  PGUP/PGDN",
        _ => "REDO  CTRL+Y",
    };

    /// <summary>
    /// The tooltip with its one state-dependent case: an inkless stamp explains what to do
    /// (the order's SELECT FIRST) instead of naming a click that would do nothing. Every other
    /// button falls through to the static text — the renderer calls this overload for all of
    /// them so the special case lives here, with the texts, and not in draw code.
    /// </summary>
    public static string Tooltip(EditorButton button, SpriteEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return button == EditorButton.ToolStamp && !session.HasStampSource
            ? "STAMP  4   EMPTY - SELECT FIRST"
            : Tooltip(button);
    }

    /// <summary>
    /// The map editor's tooltip for a button whose meaning differs on that screen, falling
    /// through to <see cref="Tooltip(EditorButton)"/> for everything shared. Two screens, one
    /// tooltip file: keeping the fallback here rather than a second table there is what stops
    /// "SAVE  CTRL+S" from existing twice and drifting once. Note what is <b>not</b> here: the
    /// map-only buttons (the eraser, the hand, the grid switch) are in the base table, because
    /// their meaning does not differ between screens — only one screen places them.
    ///
    /// <para>The two buttonless controls of that screen — the tile picker and the minimap —
    /// have no <see cref="HoverTarget"/> kind of their own (that type is shared chrome and does
    /// not fork for one editor), so their key paths are announced on the buttons next to them:
    /// the picker's on the pencil, the view's travel keys on the tilemap tab. Every key on the
    /// map screen is therefore reachable from some tooltip, which is what the parity sweep
    /// checks.</para>
    /// </summary>
    public static string MapTooltip(EditorButton button) => button switch
    {
        EditorButton.ToolPencil =>
            "PENCIL  1   ARROWS MOVE, Z DRAWS, X PICKS   SHIFT+ARROWS PICK A TILE"
            + "   DRAG THE PICKER OR CTRL+SHIFT+ARROWS FOR A BLOCK",
        EditorButton.ToolSelect =>
            "SELECT  3   DRAG MARKS A RECTANGLE, DEL EMPTIES IT, ESC DROPS IT"
            + "   CTRL+C/X/V COPY, CUT, PASTE",
        EditorButton.ToolFill =>
            "FILL  4   Z FILLS THE AREA AT THE CURSOR   RCLICK FILLS WITH TILE 000",
        EditorButton.TilemapTab =>
            "MAPS - ACTIVE   [ ] PAGE ACROSS, PGUP/PGDN PAGE DOWN, HOME SWITCHES TAB",
        _ => Tooltip(button),
    };

    /// <summary>
    /// The <b>code</b> editor's tooltip for a button whose meaning differs on that screen,
    /// falling through to <see cref="Tooltip(EditorButton)"/> for everything shared — the exact
    /// shape of <see cref="MapTooltip"/>, and for the same reason: three screens, one tooltip
    /// file, so "SAVE  CTRL+S" exists once and cannot drift.
    ///
    /// <para>The code screen's one control without a button is the text field itself, and its
    /// key paths are announced on the tab it lives under — every key on this screen is therefore
    /// reachable from some tooltip, which is what the parity sweep checks.</para>
    /// </summary>
    public static string CodeTooltip(EditorButton button) => button switch
    {
        EditorButton.CodeTab =>
            "CODE - ACTIVE   ARROWS/HOME/END/PGUP/PGDN MOVE, SHIFT SELECTS, CTRL+ARROWS BY WORD",
        _ => Tooltip(button),
    };

    /// <summary>
    /// The <b>sound</b> editor's tooltip for a button whose meaning differs on that screen,
    /// falling through to <see cref="Tooltip(EditorButton)"/> for everything shared — the exact
    /// shape of <see cref="MapTooltip"/> and <see cref="CodeTooltip"/>, and for the same reason:
    /// four screens, one tooltip file, so "SAVE  CTRL+S" exists once and cannot drift.
    ///
    /// <para>This screen has more controls without buttons than any other — the slot selector,
    /// three grids, two rows of cells and three stepper fields — so its own tab carries the keys
    /// that belong to no button, which is where the parity sweep looks for them. Their labels
    /// live below as <see cref="SfxSlotTooltip"/>, <see cref="SfxPitchTooltip"/> and their
    /// neighbours, the way <see cref="SliderTooltip"/> does for the sheet slider.</para>
    /// </summary>
    public static string SfxTooltip(EditorButton button) => button switch
    {
        EditorButton.SoundTab =>
            "SOUNDS - ACTIVE   ARROWS WALK STEPS, PGUP/PGDN WALK SLOTS, DEL ERASES A STEP",
        _ => Tooltip(button),
    };

    /// <summary>
    /// The <b>music</b> editor's tooltip for a button whose meaning differs on that screen,
    /// falling through to <see cref="Tooltip(EditorButton)"/> for everything shared — the exact
    /// shape of <see cref="MapTooltip"/>, <see cref="CodeTooltip"/> and <see cref="SfxTooltip"/>,
    /// and for the same reason: five screens, one tooltip file, so "SAVE  CTRL+S" exists once and
    /// cannot drift.
    ///
    /// <para>Two buttons say something different here. The MUSIC tab names the screen it is
    /// already on and carries the keys that belong to no button of this screen; the play button
    /// says <em>where</em> playback starts, which is the one thing that differs from the sound
    /// screen's transport — there it plays the open slot, here it plays the song from the
    /// cursor's pattern.</para>
    /// </summary>
    public static string MusicTooltip(EditorButton button) => button switch
    {
        EditorButton.MusicTab =>
            "MUSIC - ACTIVE   ARROWS MOVE, 0-9 SET A SLOT, DEL RESTS, PGUP/PGDN PAGE",
        EditorButton.ToolPlay => "PLAY / STOP SONG FROM THE CURSOR  SPACE",
        _ => Tooltip(button),
    };

    /// <summary>The grid's label — the keys that write a cell, which is what this screen is for.</summary>
    public const string MusicSongTooltip =
        "PATTERNS   0-9 SET A SLOT, DEL RESTS, WHEEL STEPS ONE, SHIFT+ARROWS MARK";

    /// <summary>The section markers' label — the three flags and the three keys that reach them.</summary>
    public const string MusicFlagsTooltip =
        "SECTION   ` LOOP START, TAB LOOP END, X STOP   OR CLICK A MARKER";

    /// <summary>
    /// The channel header's label. It says outright that neither toggle touches the cartridge,
    /// because a control that looks like an edit and is not has to say so where it is.
    /// </summary>
    public const string MusicChannelsTooltip =
        "MUTE/SOLO   SHIFT+1-4 MUTE, SHIFT+5-8 SOLO   LISTENING ONLY, SAVES NOTHING";

    /// <summary>The whole-song overview's label — every pattern at once, and the scroll control.</summary>
    public const string MusicOverviewTooltip =
        "WHOLE SONG   CLICK TO JUMP, WHEEL SCROLLS, PGUP/PGDN PAGE";

    /// <summary>
    /// The label of one buttonless control of the music screen — the single lookup its renderer
    /// hangs a tooltip on, so every key this screen owns is discoverable from the pointer and the
    /// input-parity sweep has one place to check. <see cref="MusicRegion.None"/> never reaches
    /// here: the hover tracker is fed null instead.
    /// </summary>
    public static string MusicRegionTooltip(MusicRegion region) => region switch
    {
        MusicRegion.Song => MusicSongTooltip,
        MusicRegion.Flags => MusicFlagsTooltip,
        MusicRegion.Channels => MusicChannelsTooltip,
        MusicRegion.Overview => MusicOverviewTooltip,
        _ => throw new ArgumentOutOfRangeException(
            nameof(region), region, "MusicRegion.None is not a control and has no label."),
    };

    /// <summary>The slot selector's label — the one control that chooses which of the 64 sounds is on screen.</summary>
    public const string SfxSlotTooltip = "SLOT   PGUP/PGDN WALK THE BANK";

    /// <summary>
    /// The pitch grid's label. It names the piano rows outright, because a de-facto standard
    /// nobody is told about is not a standard (REFERENCES-EDITORS §8 item 17).
    /// </summary>
    public const string SfxPitchTooltip =
        "NOTES   ZSXDCVGBHNJM AND Q2W3ER5T6Y7UI PLAY, [ ] CHANGE OCTAVE";

    /// <summary>The loop row's label — the four states and the two keys that reach them.</summary>
    public const string SfxLoopTooltip =
        "LOOP   ` OR CLICK SETS START, TAB OR RCLICK SETS END, AGAIN CLEARS";

    /// <summary>The volume grid's label; the bottom row is the rest, which is also what Del writes.</summary>
    public const string SfxVolumeTooltip = "VOLUME   UP/DOWN   BOTTOM ROW IS A REST, LIKE DEL";

    /// <summary>The wave row's label.</summary>
    public const string SfxWaveTooltip = "WAVE   , AND . CYCLE";

    /// <summary>The effect row's label.</summary>
    public const string SfxEffectTooltip = "EFFECT   F CYCLES";

    /// <summary>The three stepper fields' labels, so each one names its keyboard twin where it is shown.</summary>
    public static string SfxFieldTooltip(SfxField field) => field switch
    {
        SfxField.Speed => "SPEED - TICKS PER STEP   SHIFT+LEFT/RIGHT",
        SfxField.Length => "LENGTH - STEPS PLAYED   SHIFT+UP/DOWN",
        _ => "OCTAVE   [ AND ]",
    };

    /// <summary>
    /// The label of one buttonless control of the sound screen — the single lookup its renderer
    /// hangs a tooltip on, so every key this screen owns is discoverable from the pointer and
    /// the input-parity sweep has one place to check. <see cref="SfxRegion.None"/> never reaches
    /// here: the hover tracker is fed null instead.
    /// </summary>
    public static string SfxRegionTooltip(SfxRegion region) => region switch
    {
        SfxRegion.Slots => SfxSlotTooltip,
        SfxRegion.Pitch => SfxPitchTooltip,
        SfxRegion.Loop => SfxLoopTooltip,
        SfxRegion.Volume => SfxVolumeTooltip,
        SfxRegion.Waves => SfxWaveTooltip,
        SfxRegion.Effects => SfxEffectTooltip,
        SfxRegion.Speed => SfxFieldTooltip(SfxField.Speed),
        SfxRegion.Length => SfxFieldTooltip(SfxField.Length),
        SfxRegion.Octave => SfxFieldTooltip(SfxField.Octave),
        _ => throw new ArgumentOutOfRangeException(
            nameof(region), region, "SfxRegion.None is not a control and has no label."),
    };

    /// <summary>
    /// A click on a live, non-tab icon-button of the <b>sound</b> editor — the fourth twin of
    /// <see cref="ClickButton"/>, <see cref="ClickMapButton"/> and <see cref="ClickCodeButton"/>,
    /// headless for the same reason: the routing table has to exist where no graphics device is
    /// required, so a contract test can click every placed button and catch the "placed but
    /// never wired" defect on arrival. Returns true when the click means "leave the editor" (the
    /// exit tab), which is <see cref="ShellModeMachine"/>'s verb and not the session's. Tab
    /// clicks never come here: <see cref="TabTarget"/> answers them first.
    ///
    /// <para><b>Two owners of state, one router.</b> The slot on screen, the cursor, the pen and
    /// the request to play are what the author is <em>looking at</em>, not what <c>sfx.bin</c>
    /// holds, so they live in <see cref="SfxEditorView"/>; the bank's own three verbs live in the
    /// session. This table takes both halves, and nothing else decides what a sound button
    /// means.</para>
    /// </summary>
    public static bool ClickSfxButton(SfxEditorSession session, SfxEditorView view, EditorButton button)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        switch (button)
        {
            case EditorButton.ExitTab:
                return true;                        // clean → back; dirty → the prompt — SfxEditorView judges
            case EditorButton.ToolPlay:
                view.TogglePlay();                  // Space is its key; the wiring drives the APU
                return false;
            case EditorButton.Save:
                session.Save();                     // the modified/saved icon IS this button — click = Ctrl+S
                return false;
            case EditorButton.Undo:
                session.Undo();
                return false;
            case EditorButton.Redo:
                session.Redo();
                return false;
            default:
                return false;                       // the music stub: nothing to do here
        }
    }

    /// <summary>
    /// A click on a live, non-tab icon-button of the <b>music</b> editor — the fifth twin of
    /// <see cref="ClickButton"/>, <see cref="ClickMapButton"/>, <see cref="ClickCodeButton"/> and
    /// <see cref="ClickSfxButton"/>, headless for the same reason: the routing table has to exist
    /// where no graphics device is required, so a contract test can click every placed button and
    /// catch the "placed but never wired" defect on arrival. Returns true when the click means
    /// "leave the editor" (the exit tab), which is <see cref="ShellModeMachine"/>'s verb and not
    /// the session's. Tab clicks never come here: <see cref="TabTarget"/> answers them first.
    ///
    /// <para><b>Two owners of state, one router.</b> The window, the mute and solo tables and the
    /// request to play are what the author is <em>looking at and listening to</em>, not what
    /// <c>music.bin</c> holds, so they live in <see cref="MusicEditorView"/>; the bank's own three
    /// verbs live in the session. This table takes both halves, and nothing else decides what a
    /// music button means.</para>
    /// </summary>
    public static bool ClickMusicButton(
        MusicEditorSession session, MusicEditorView view, EditorButton button)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        switch (button)
        {
            case EditorButton.ExitTab:
                return true;                        // clean → back; dirty → the prompt — MusicEditorView judges
            case EditorButton.ToolPlay:
                view.TogglePlay(session);           // Space is its key; the wiring drives the APU
                return false;
            case EditorButton.Save:
                session.Save();                     // the modified/saved icon IS this button — click = Ctrl+S
                return false;
            case EditorButton.Undo:
                session.Undo();
                return false;
            case EditorButton.Redo:
                session.Redo();
                return false;
            default:
                return false;                       // the music tab: already the mode on screen
        }
    }

    /// <summary>Swatch tooltip: the keyboard color mechanism, discoverable where the colors are.</summary>
    public static string SwatchTooltip(int color) => $"COLOR {color}   , PREV   . NEXT";

    /// <summary>
    /// Flag toggle tooltip (wave 3b-2). The bit is 0-based (PICO-8: "indexed from 0 starting
    /// from the left", and it is <c>Fget</c>'s own index); its key is the 1-based digit, so the
    /// pair has to be spelled out or the row would be a guessing game. The third clause is the
    /// group rule made discoverable — at region 16 or 32 px a click moves four or sixteen
    /// sprites at once, and a panel that did that silently would be a trap.
    /// </summary>
    public static string FlagTooltip(int bit) =>
        $"FLAG {bit}  SHIFT+{bit + 1}   SETS THE WHOLE REGION";

    /// <summary>
    /// The sheet slider's tooltip (wave 2h) — it is the one control without a button, so this
    /// is where its wheel and [ ] key paths get announced (the input-parity law's
    /// discoverability half).
    /// </summary>
    public const string SliderTooltip = "SHEET   SHIFT+ARROWS PICK   DRAG, WHEEL OR [ ] SCROLL";

    /// <summary>Toolbar digit → its slot, top-to-bottom (1 select … 6 transform); null off the toolbar.</summary>
    public static EditorButton? ButtonForDigit(int digit) => digit switch
    {
        1 => EditorButton.ToolSelect,
        2 => EditorButton.ToolPencil,
        3 => EditorButton.ToolFill,
        4 => EditorButton.ToolStamp,
        5 => EditorButton.ToolShape,
        6 => EditorButton.ToolTransform,
        _ => null,
    };

    /// <summary>
    /// The <b>map</b> screen's digit table, top-to-bottom down its tool column and — the same
    /// thing — TIC-80's own numbering (REFERENCES-EDITORS §3.1: <c>DRAW [1]</c>,
    /// <c>DRAG MAP [2]</c>, <c>SELECT [3]</c>, <c>FILL [4]</c>). Separate from
    /// <see cref="ButtonForDigit"/> because the two screens hold different tools in different
    /// order, and one table pretending to serve both would put the map's fill on the sprite
    /// editor's stamp key. Digits 5 and 6 are the sprite editor's alone and answer null here.
    /// </summary>
    public static EditorButton? MapButtonForDigit(int digit) => digit switch
    {
        1 => EditorButton.ToolPencil,
        2 => EditorButton.ToolHand,
        3 => EditorButton.ToolSelect,
        4 => EditorButton.ToolFill,
        _ => null,
    };

    /// <summary>
    /// Which map tool a button IS, or null for every button that is not one of the four. The
    /// single owner of the button↔tool mapping: <see cref="ClickMapButton"/> routes clicks
    /// through it, <see cref="PressMapToolDigit"/> routes keys through it, and
    /// <c>MapEditorRenderer</c> asks it which button to draw active — so the highlight cannot
    /// disagree with the tool in hand.
    /// </summary>
    public static MapEditorTool? MapToolOf(EditorButton button) => button switch
    {
        EditorButton.ToolPencil => MapEditorTool.Pencil,
        EditorButton.ToolHand => MapEditorTool.Hand,
        EditorButton.ToolSelect => MapEditorTool.Select,
        EditorButton.ToolFill => MapEditorTool.Fill,
        _ => null,
    };

    /// <summary>
    /// The map screen's whole tool-digit policy, the twin of <see cref="PressToolDigit"/>: a
    /// digit that names one of the four tools selects it, and every other digit does nothing.
    /// No repeat-cycles here — the map has no group slots, so a second press of the same digit
    /// is the honest no-op of re-choosing the tool already in hand.
    /// </summary>
    public static void PressMapToolDigit(MapEditorView view, int digit)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (MapButtonForDigit(digit) is EditorButton slot && MapToolOf(slot) is MapEditorTool tool)
        {
            view.SelectTool(tool);
        }
    }

    /// <summary>
    /// The keyboard's whole toolbar-digit policy in one testable place (the shell calls this
    /// verbatim). Plain tools select; the select and shape groups select on the first press
    /// and cycle their variant on a repeat (the wave's "повторное нажатие цифры циклит
    /// варианты"); the transform slot has no mode to enter, so every press is a repeat and
    /// cycles — applying stays on F/V/R and on the slot's click, never on a digit, or cycling
    /// to a variant would wreck the sheet on the way. Digits on stub slots do nothing: a dead
    /// button must be exactly as dead from the keyboard as from the mouse (the named negative
    /// control).
    /// </summary>
    public static void PressToolDigit(SpriteEditorSession session, int digit)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ButtonForDigit(digit) is not EditorButton slot || IsStub(slot))
        {
            return;
        }
        switch (slot)
        {
            case EditorButton.ToolSelect:
                if (session.Tool == SpriteEditorTool.Select)
                {
                    session.CycleSelectionVariant();
                }
                else
                {
                    session.SelectTool(SpriteEditorTool.Select);
                }
                break;
            case EditorButton.ToolPencil:
                session.SelectTool(SpriteEditorTool.Pencil);
                break;
            case EditorButton.ToolFill:
                session.SelectTool(SpriteEditorTool.Fill);
                break;
            case EditorButton.ToolStamp:
                session.SelectTool(SpriteEditorTool.Stamp);
                break;
            case EditorButton.ToolShape:
                if (session.Tool == SpriteEditorTool.Shape)
                {
                    session.CycleShape();
                }
                else
                {
                    session.SelectTool(SpriteEditorTool.Shape);
                }
                break;
            case EditorButton.ToolTransform:
                session.CycleTransform();
                break;
        }
    }

    /// <summary>
    /// A click on a live, non-group icon-button, routed to the same session calls the keys use
    /// (the parity law). Pulled out of the shell in wave 2g so the routing table itself is
    /// headless-testable: the stamp shipped in 2f placed by the layout but absent from this
    /// switch (the third review's bug 1), and the only mechanism that closes the whole defect
    /// class is a contract test that clicks every placed button — which needs the table to
    /// exist where no graphics device is required. Returns true when the click means "leave
    /// the editor" (the exit tab): leaving is the mode machine's verb, not the session's, so
    /// the shell executes it. The sprites tab is the one honest no-op — it names the mode
    /// already on screen. Group slots never come here: their press has two meanings and goes
    /// through <see cref="ToolbarFlyout"/>'s arm/click path.
    /// </summary>
    public static bool ClickButton(SpriteEditorSession session, EditorButton button)
    {
        ArgumentNullException.ThrowIfNull(session);
        switch (button)
        {
            case EditorButton.ExitTab:
                return true;                        // clean → library; dirty → the prompt — the mode machine judges
            case EditorButton.ToolPencil:
                session.SelectTool(SpriteEditorTool.Pencil);
                return false;
            case EditorButton.ToolFill:
                session.SelectTool(SpriteEditorTool.Fill);
                return false;
            case EditorButton.ToolStamp:
                session.SelectTool(SpriteEditorTool.Stamp);
                return false;
            case EditorButton.Clear:
                session.ClearRegion();              // in the status bar since the owner's second review; Del unchanged
                return false;
            case EditorButton.Save:
                session.Save();                     // the modified/saved icon IS this button — click = Ctrl+S
                return false;
            case EditorButton.Undo:
                session.Undo();
                return false;
            case EditorButton.Redo:
                session.Redo();
                return false;
            case EditorButton.LayerTab1:
            case EditorButton.LayerTab2:
            case EditorButton.LayerTab3:
            case EditorButton.LayerTab4:
            case EditorButton.LayerTab5:
                // The tabs' click half; PgUp/PgDn walk the same setter (wave 2h parity).
                session.SelectLayer(button - EditorButton.LayerTab1);
                return false;
            default:
                return false;                       // SpritesTab: already the mode on screen
        }
    }

    /// <summary>
    /// A click on a live, non-tab icon-button of the <b>map</b> editor, routed to the same
    /// session calls the keys use — <see cref="ClickButton"/>'s twin, and headless for the same
    /// reason: the routing table has to exist where no graphics device is required, so a
    /// contract test can click every placed button and catch the "placed but never wired"
    /// defect on arrival. Returns true when the click means "leave the editor" (the exit tab),
    /// which is <see cref="ShellModeMachine"/>'s verb and not the session's. Tab clicks never
    /// come here: <see cref="TabTarget"/> answers them first.
    ///
    /// <para><b>Two owners of state, one router (wave 3d).</b> The tools, the marked rectangle
    /// and the grid switch are what the author is <em>looking at</em>, not what
    /// <c>map.bin</c> holds, so they live in <see cref="MapEditorView"/> — and this table
    /// therefore takes both halves. It is still one router: nothing else decides what a map
    /// button means, and the contract sweep clicks every placed button through exactly this.</para>
    /// </summary>
    public static bool ClickMapButton(MapEditorSession session, MapEditorView view, EditorButton button)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        if (MapToolOf(button) is MapEditorTool tool)
        {
            view.SelectTool(tool);
            return false;
        }
        switch (button)
        {
            case EditorButton.ExitTab:
                return true;                        // clean → back; dirty → the prompt — MapEditorView judges
            case EditorButton.ToolEraser:
                // MAP-FORMAT §2 and REFERENCES-EDITORS §7.3: not one of the three references
                // has an eraser TOOL — LIKO-12 erases by forcing the selected tile to 0 under
                // the right button. So this button is not a mode: it SELECTS tile 0, and the
                // pencil then erases with it. Legal on a read-only map — choosing writes nothing.
                session.SelectSprite(MapEditorSession.EmptyTile);
                return false;
            case EditorButton.GridToggle:
                view.ToggleGrid();                  // TIC-80's drawGridButton; ` is its key
                return false;
            case EditorButton.TilesToggle:
                // The mouse's half of "hold Shift": a click latches the palette open and a
                // second one lowers it, so a pointer-only author never has to hold a key
                // (TIC-80's drawSheetButton is exactly this, next to exactly that key).
                view.ToggleTiles();
                return false;
            case EditorButton.WorldToggle:
                view.ToggleWorld();                 // TIC-80's drawWorldButton; Tab is its key
                return false;
            case EditorButton.Save:
                session.Save();                     // the modified/saved icon IS this button — click = Ctrl+S
                return false;
            case EditorButton.Undo:
                session.Undo();
                return false;
            case EditorButton.Redo:
                session.Redo();
                return false;
            default:
                return false;                       // SpritesTab and the stubs: nothing to do here
        }
    }

    /// <summary>
    /// A click on a live, non-tab icon-button of the <b>code</b> editor —
    /// <see cref="ClickButton"/>'s and <see cref="ClickMapButton"/>'s third twin, headless for
    /// the same reason: the routing table has to exist where no graphics device is required, so
    /// a contract test can click every placed button and catch the "placed but never wired"
    /// defect on arrival. Returns true when the click means "leave the editor" (the exit tab),
    /// which is <see cref="ShellModeMachine"/>'s verb and not the session's. Tab clicks never
    /// come here: <see cref="TabTarget"/> answers them first.
    ///
    /// <para><b>Two owners of state, one router.</b> The find and go-to lines are what the
    /// author is <em>looking at</em>, not what <c>src/main.cs</c> holds, so they live in
    /// <see cref="CodeEditorView"/>; the buffer's own three verbs live in the session. This
    /// table takes both halves, and nothing else decides what a code button means.</para>
    /// </summary>
    public static bool ClickCodeButton(CodeEditorSession session, CodeEditorView view, EditorButton button)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        switch (button)
        {
            case EditorButton.ExitTab:
                return true;                        // clean → back; dirty → the prompt — CodeEditorView judges
            case EditorButton.ToolFind:
                view.OpenFind();                    // TIC-80's FIND button; Ctrl+F is its key
                return false;
            case EditorButton.ToolGoTo:
                view.OpenGoTo();                    // TIC-80's GOTO button; Ctrl+L is its key
                return false;
            case EditorButton.Save:
                session.Save();                     // the modified/saved icon IS this button — click = Ctrl+S
                return false;
            case EditorButton.Undo:
                session.Undo();
                return false;
            case EditorButton.Redo:
                session.Redo();
                return false;
            default:
                return false;                       // the stubs: nothing to do here
        }
    }

    /// <summary>
    /// A completed short click on a group slot (the mouse's verb, decided by
    /// <see cref="ToolbarFlyout"/>): the transform slot APPLIES its current variant to the
    /// region — the click is the mouse's F/V/R — while the select and shape slots just become
    /// the active tool, because both need a canvas gesture to mean anything.
    /// </summary>
    public static void ClickGroupSlot(SpriteEditorSession session, EditorButton slot)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (slot == EditorButton.ToolTransform)
        {
            session.ApplyTransform();
        }
        else if (slot == EditorButton.ToolShape)
        {
            session.SelectTool(SpriteEditorTool.Shape);
        }
        else if (slot == EditorButton.ToolSelect)
        {
            session.SelectTool(SpriteEditorTool.Select);
        }
    }

    /// <summary>
    /// A variant picked from an open flyout: remembered, never applied — applying is the
    /// slot-click's job (the wave's "выбор запоминается"). Picking a select or shape variant
    /// also activates its tool, photoshop-style: the author asked for that marker or shape,
    /// not for a note about it.
    /// </summary>
    public static void ChooseVariant(SpriteEditorSession session, EditorButton slot, int variant)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (slot == EditorButton.ToolTransform)
        {
            session.SelectTransform((TransformVariant)variant);
        }
        else if (slot == EditorButton.ToolShape)
        {
            session.SelectShape((ShapeVariant)variant);
            session.SelectTool(SpriteEditorTool.Shape);
        }
        else if (slot == EditorButton.ToolSelect)
        {
            session.SelectSelectionVariant((SelectionVariant)variant);
            session.SelectTool(SpriteEditorTool.Select);
        }
        else if (slot == EditorButton.SizeToggle)
        {
            // The one flyout whose pick APPLIES: choosing a size IS the action (there is no
            // "size tool" to arm), unlike the tool groups where the pick is remembered only.
            session.SelectRegionSize(SizeVariantCells(variant));
        }
    }
}

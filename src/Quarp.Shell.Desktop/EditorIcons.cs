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
    };

    /// <summary>How many glyphs exist — the atlas sizes its strip from this.</summary>
    public static int IconCount => _masks.Length;

    /// <summary>True if the icon has an ink pixel at (col, row), both 0-7 — same shape as SystemFont.IsSet.</summary>
    public static bool IsSet(EditorIcon icon, int col, int row) =>
        ((_masks[(int)icon][row] >> (IconPixels - 1 - col)) & 1) != 0;

    /// <summary>
    /// The buttons that are drawn but deliberately dead: the four future-editor tabs (their
    /// portions of ADR-026 have not landed — the whole toolbar woke by wave 2f). This list is
    /// the <b>one owner</b> of "is it a stub": the layout paints from it, the click routing
    /// refuses from it, and <see cref="PressToolDigit"/> consults it — so waking a button is
    /// one edit here, never a drift between what looks dead and what is dead.
    /// </summary>
    public static bool IsStub(EditorButton button) => button is
        EditorButton.CodeTab or EditorButton.SoundTab or EditorButton.MusicTab;

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
        _ => null,
    };

    /// <summary>
    /// Which of the two editor screens places a button. One owner, because a single enum now
    /// serves two layouts and "forgot to place it" must stay a red test rather than a missing
    /// button: <see cref="SpriteEditorLayout"/> places everything this answers true for, and
    /// <see cref="MapEditorLayout"/> everything <see cref="BelongsToMapEditor"/> does.
    /// </summary>
    public static bool BelongsToSpriteEditor(EditorButton button) => button is not EditorButton.ToolEraser;

    /// <summary>
    /// The map editor's own button list: the shared chrome (tabs, exit, save, undo, redo), the
    /// pencil and the eraser. Everything the map has no model verb for — fill, stamp, shapes,
    /// transforms, select, clear, the sprite-size toggle and the layer tabs — stays off this
    /// screen, because a placed button with nothing behind it is the defect class the button
    /// contract test closed in wave 2g.
    /// </summary>
    public static bool BelongsToMapEditor(EditorButton button) => button is
        EditorButton.ExitTab or EditorButton.CodeTab or EditorButton.SpritesTab
        or EditorButton.TilemapTab or EditorButton.SoundTab or EditorButton.MusicTab
        or EditorButton.ToolPencil or EditorButton.ToolEraser
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
        EditorButton.CodeTab => "CODE - IN A LATER PORTION",
        EditorButton.SpritesTab => "SPRITES  HOME SWITCHES",
        EditorButton.TilemapTab => "MAPS  HOME SWITCHES",
        EditorButton.SoundTab => "SOUNDS - IN A LATER PORTION",
        EditorButton.MusicTab => "MUSIC - IN A LATER PORTION",
        EditorButton.ToolSelect => "SELECT  1 CYCLES   DRAG MARKS, GRAB INSIDE MOVES, ESC DROPS",
        EditorButton.ToolPencil => "PENCIL  2   ARROWS MOVE, Z/SPACE DRAW, X PICK, SHIFT+ARROWS SPRITE",
        EditorButton.ToolFill => "FILL  3   Z/SPACE FILLS AT THE CURSOR",
        EditorButton.ToolStamp => "STAMP  4   CLICK/Z PRINTS THE LAST SELECTION",
        EditorButton.ToolShape => "SHAPES  5 CYCLES   DRAG DRAWS, CTRL FILLS, HOLD/RCLICK VARIANTS",
        EditorButton.ToolTransform => "TRANSFORM  6 CYCLES, F/V/R APPLY   CLICK APPLIES, HOLD/RCLICK VARIANTS",
        EditorButton.Clear => "CLEAR  DEL",
        EditorButton.ToolEraser => "EMPTY TILE 0  DEL",
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
    /// "SAVE  CTRL+S" from existing twice and drifting once.
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
            "PENCIL  ARROWS MOVE, Z/SPACE DRAW, X PICKS   SHIFT+ARROWS PICK A TILE",
        EditorButton.TilemapTab =>
            "MAPS - ACTIVE   [ ] PAGE ACROSS, PGUP/PGDN PAGE DOWN, HOME SWITCHES TAB",
        _ => Tooltip(button),
    };

    /// <summary>Swatch tooltip: the keyboard color mechanism, discoverable where the colors are.</summary>
    public static string SwatchTooltip(int color) => $"COLOR {color}   , PREV   . NEXT";

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
    /// <para>The pencil is the one honest no-op, exactly as the sprites tab is on the other
    /// screen: it names the tool already in hand. The map model of stage 3 has a single
    /// drawing verb (<see cref="MapEditorSession.PaintTile"/>) and a single reader
    /// (<see cref="MapEditorSession.PickTile"/>), so there is nothing for a click to switch
    /// to — inventing a tool the model does not have would be worse than a button that says
    /// what is true.</para>
    /// </summary>
    public static bool ClickMapButton(MapEditorSession session, EditorButton button)
    {
        ArgumentNullException.ThrowIfNull(session);
        switch (button)
        {
            case EditorButton.ExitTab:
                return true;                        // clean → back; dirty → the prompt — MapEditorView judges
            case EditorButton.ToolEraser:
                // MAP-FORMAT §2: tile 0 is emptiness rather than sprite 0, so "select tile 0"
                // IS the eraser. Legal on a read-only map — choosing a tile writes nothing.
                session.SelectSprite(0);
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
                return false;                       // ToolPencil: the tool already in hand
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

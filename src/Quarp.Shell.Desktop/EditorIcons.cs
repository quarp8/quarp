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

    // The left toolbar, top to bottom. Select, stamp and shape are wave 2e's — visible,
    // disabled, honest about it in their tooltips.
    ToolSelect,
    ToolPencil,
    ToolFill,
    ToolStamp,
    ToolShape,

    // The action row under the toolbar — mouse parity for F/V/R/Del.
    FlipH,
    FlipV,
    Rotate,
    Clear,

    // The status bar's buttons. Save doubles as the modified/saved indicator.
    Save,
    Undo,
    Redo,
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
    Select,
    Pencil,
    Fill,
    Stamp,
    Shape,
    FlipH,
    FlipV,
    Rotate,
    Clear,
    Undo,
    Redo,
    Saved,
    Modified,
}

/// <summary>
/// The editor's icon set and button metadata, all in one owner: pixel masks (byte rows, MSB =
/// leftmost, the same packing discipline as <see cref="Quarp.Core.SystemFont"/>'s glyphs — drawn
/// in code so the icons carry the project's own license and no asset pipeline), tooltip texts
/// (name + hotkey, the order's contract), the stub list (which buttons are visible-but-dead in
/// this wave) and the digit→tool map. Keeping stub-ness and the digit map in the same file is
/// deliberate: enabling a tool in wave 2e is a one-place edit, and a digit can never wake a
/// tool this file still calls a stub.
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
        new byte[] // Select: a dashed rectangle (marching ants, frozen)
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
        new byte[] // Shape: a rectangle outline overlapping an oval outline
        {
            0b11111000,
            0b10001000,
            0b10001000,
            0b10001110,
            0b11111001,
            0b00010001,
            0b00010001,
            0b00001110,
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
    };

    /// <summary>How many glyphs exist — the atlas sizes its strip from this.</summary>
    public static int IconCount => _masks.Length;

    /// <summary>True if the icon has an ink pixel at (col, row), both 0-7 — same shape as SystemFont.IsSet.</summary>
    public static bool IsSet(EditorIcon icon, int col, int row) =>
        ((_masks[(int)icon][row] >> (IconPixels - 1 - col)) & 1) != 0;

    /// <summary>
    /// The buttons that are drawn but deliberately dead this wave: the four future-editor tabs
    /// (their portions of ADR-026 have not landed) and the three wave-2e tools. This list is
    /// the <b>one owner</b> of "is it a stub": the layout paints from it, the click routing
    /// refuses from it, and <see cref="ToolForDigit"/> consults it — so waking a tool is one
    /// edit here, never a drift between what looks dead and what is dead.
    /// </summary>
    public static bool IsStub(EditorButton button) => button is
        EditorButton.CodeTab or EditorButton.TilemapTab or EditorButton.SoundTab or EditorButton.MusicTab
        or EditorButton.ToolSelect or EditorButton.ToolStamp or EditorButton.ToolShape;

    /// <summary>The glyph a button shows. Save's two faces are the renderer's pick — this returns the clean one.</summary>
    public static EditorIcon IconFor(EditorButton button) => button switch
    {
        EditorButton.ExitTab => EditorIcon.Exit,
        EditorButton.CodeTab => EditorIcon.Code,
        EditorButton.SpritesTab => EditorIcon.Sprites,
        EditorButton.TilemapTab => EditorIcon.Tilemap,
        EditorButton.SoundTab => EditorIcon.Sound,
        EditorButton.MusicTab => EditorIcon.Music,
        EditorButton.ToolSelect => EditorIcon.Select,
        EditorButton.ToolPencil => EditorIcon.Pencil,
        EditorButton.ToolFill => EditorIcon.Fill,
        EditorButton.ToolStamp => EditorIcon.Stamp,
        EditorButton.ToolShape => EditorIcon.Shape,
        EditorButton.FlipH => EditorIcon.FlipH,
        EditorButton.FlipV => EditorIcon.FlipV,
        EditorButton.Rotate => EditorIcon.Rotate,
        EditorButton.Clear => EditorIcon.Clear,
        EditorButton.Save => EditorIcon.Saved,
        EditorButton.Undo => EditorIcon.Undo,
        _ => EditorIcon.Redo,
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
        EditorButton.SpritesTab => "SPRITES - ACTIVE",
        EditorButton.TilemapTab => "MAPS - IN A LATER PORTION",
        EditorButton.SoundTab => "SOUNDS - IN A LATER PORTION",
        EditorButton.MusicTab => "MUSIC - IN A LATER PORTION",
        EditorButton.ToolSelect => "SELECT - WAVE 2E",
        EditorButton.ToolPencil => "PENCIL  2   ARROWS MOVE, Z/SPACE DRAW, X PICK",
        EditorButton.ToolFill => "FILL  3   Z/SPACE FILLS AT THE CURSOR",
        EditorButton.ToolStamp => "STAMP - WAVE 2E",
        EditorButton.ToolShape => "SHAPES - WAVE 2E",
        EditorButton.FlipH => "FLIP H  F",
        EditorButton.FlipV => "FLIP V  V",
        EditorButton.Rotate => "ROTATE 90  R",
        EditorButton.Clear => "CLEAR  DEL",
        EditorButton.Save => "SAVE  CTRL+S",
        EditorButton.Undo => "UNDO  CTRL+Z",
        _ => "REDO  CTRL+Y",
    };

    /// <summary>Swatch tooltip: the keyboard color mechanism, discoverable where the colors are.</summary>
    public static string SwatchTooltip(int color) => $"COLOR {color}   , PREV   . NEXT";

    /// <summary>
    /// The keyboard's tool switch: digits 1-5 in the toolbar's top-to-bottom order. Digits on
    /// stub tools return null — a dead button must be exactly as dead from the keyboard as
    /// from the mouse, which is the negative-control target the wave order names.
    /// </summary>
    public static SpriteEditorTool? ToolForDigit(int digit)
    {
        EditorButton? button = digit switch
        {
            1 => EditorButton.ToolSelect,
            2 => EditorButton.ToolPencil,
            3 => EditorButton.ToolFill,
            4 => EditorButton.ToolStamp,
            5 => EditorButton.ToolShape,
            _ => null,
        };
        if (button is not EditorButton b || IsStub(b))
        {
            return null;
        }
        return b switch
        {
            EditorButton.ToolPencil => SpriteEditorTool.Pencil,
            EditorButton.ToolFill => SpriteEditorTool.Fill,
            _ => null,
        };
    }
}

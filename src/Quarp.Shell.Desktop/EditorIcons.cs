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
        EditorButton.CodeTab or EditorButton.TilemapTab or EditorButton.SoundTab or EditorButton.MusicTab;

    /// <summary>
    /// The photoshop-style group slots (owner's second review): a corner marker on the button,
    /// a flyout of variants on long-press/right-click, a repeat-digit cycle on the keyboard.
    /// One owner for "is it a group", like the stub list: the renderer marks from it, the
    /// shell arms the long-press from it, and the layout sizes flyouts from
    /// <see cref="GroupVariantCount"/> next door.
    /// </summary>
    public static bool IsGroupSlot(EditorButton button) =>
        button is EditorButton.ToolSelect or EditorButton.ToolShape or EditorButton.ToolTransform;

    /// <summary>How many variants a group slot's flyout shows; 0 for everything that is not a group.</summary>
    public static int GroupVariantCount(EditorButton button) => button switch
    {
        EditorButton.ToolSelect => 2,       // SelectionVariant: rectangle, brush
        EditorButton.ToolShape => 2,        // ShapeVariant: oval, rectangle
        EditorButton.ToolTransform => 3,    // TransformVariant: flip H, flip V, rotate
        _ => 0,
    };

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
        (EditorButton.ToolShape, (int)ShapeVariant.Oval) => "OVAL  5 CYCLES",
        (EditorButton.ToolShape, (int)ShapeVariant.Rectangle) => "RECTANGLE  5 CYCLES",
        (EditorButton.ToolTransform, (int)TransformVariant.FlipH) => "FLIP H  F",
        (EditorButton.ToolTransform, (int)TransformVariant.FlipV) => "FLIP V  V",
        (EditorButton.ToolTransform, (int)TransformVariant.Rotate) => "ROTATE 90  R",
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
        EditorButton.ToolSelect => "SELECT  1 CYCLES   DRAG MARKS, GRAB INSIDE MOVES, ESC DROPS",
        EditorButton.ToolPencil => "PENCIL  2   ARROWS MOVE, Z/SPACE DRAW, X PICK",
        EditorButton.ToolFill => "FILL  3   Z/SPACE FILLS AT THE CURSOR",
        EditorButton.ToolStamp => "STAMP  4   CLICK/Z PRINTS THE LAST SELECTION",
        EditorButton.ToolShape => "SHAPES  5 CYCLES   DRAG DRAWS, CTRL FILLS, HOLD/RCLICK VARIANTS",
        EditorButton.ToolTransform => "TRANSFORM  6 CYCLES, F/V/R APPLY   CLICK APPLIES, HOLD/RCLICK VARIANTS",
        EditorButton.Clear => "CLEAR  DEL",
        EditorButton.Save => "SAVE  CTRL+S",
        EditorButton.Undo => "UNDO  CTRL+Z",
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

    /// <summary>Swatch tooltip: the keyboard color mechanism, discoverable where the colors are.</summary>
    public static string SwatchTooltip(int color) => $"COLOR {color}   , PREV   . NEXT";

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
    }
}

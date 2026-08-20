using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>One placed icon-button: identity plus rectangle. Enabled-ness is not stored — <see cref="EditorIcons.IsStub"/> owns it.</summary>
public readonly struct EditorButtonPlace
{
    public EditorButton Id { get; init; }

    public Rectangle Rect { get; init; }
}

/// <summary>The three clickable verbs of the dirty-exit prompt line — mouse parity for Z / X / Esc.</summary>
public enum EditorPromptVerb
{
    SaveAndExit,
    Discard,
    Stay,
}

/// <summary>
/// Where everything on the sprite editor screen sits, as a pure function of the window size
/// and the region size — computed fresh each frame, like every host-UI measurement (see
/// <see cref="LibraryRenderer"/>'s type comment on why that is safe here and nowhere near a
/// hash). This is the geometry's <b>single owner</b>: <see cref="SpriteEditorRenderer"/> draws
/// these rectangles and <c>QuarpGame</c> hit-tests the mouse against the same ones, so a
/// button can never be painted in one place and clicked in another.
///
/// <para><b>The shape is the owner's verdict (M9 stage 2.5), verbatim.</b> Top: a tab strip of
/// icons only — exit at the left; from the right corner leftwards music, sounds, tilemaps,
/// sprites, code; no text headers of any kind. Bottom: a status bar — cursor coordinates,
/// sprite number, the clickable saved/modified icon, undo/redo. Left: the tool column (select,
/// pencil, fill, stamp, shape) over the action row (flip H, flip V, rotate, clear). The canvas
/// sits right of the toolbar; right of it the palette, the layers stub and the sheet stack up.
/// The dirty-exit prompt keeps a reserved text line just above the status bar, so its
/// appearance never moves the canvas.</para>
///
/// <para><b>Every scale is a whole integer</b>, floored at 1: the canvas is the region's
/// pixels multiplied up, the sheet view is the 128x128 sheet multiplied up, icons are 8-px
/// masks multiplied up, and fractional scales would resample pixel art into blur
/// (ARCHITECTURE §5's rule, applied to host UI). In a pathologically small window the parts
/// keep scale 1 and may overflow — clipped, not crashed; the shell's default window is 8x the
/// console and the floor exists for resizes, not for use.</para>
/// </summary>
public readonly struct SpriteEditorLayout
{
    private const int SwatchColumns = 8;
    private const int SwatchRows = Palette.VisibleCount / SwatchColumns;

    // The prompt's three verbs, owned here because the renderer draws them and the hit test
    // measures them — two copies of these strings would be two opinions about where a click lands.
    public const string PromptHeading = "UNSAVED CHANGES:";
    public const string PromptSaveVerb = "Z SAVE+EXIT";
    public const string PromptDiscardVerb = "X DISCARD";
    public const string PromptStayVerb = "ESC STAY";

    private static readonly string[] _promptVerbs = { PromptSaveVerb, PromptDiscardVerb, PromptStayVerb };

    /// <summary>Host-UI text scale, same anchor the library uses (<see cref="PixelFontAtlas.UiScale"/>).</summary>
    public int Ui { get; private init; }

    /// <summary>Screen-edge inset, in window pixels — the library's 4 * ui, kept identical so the modes read as one shell.</summary>
    public int Margin { get; private init; }

    /// <summary>Side of every icon-button: an 8-px icon at scale <see cref="Ui"/> plus 2 * ui padding a side.</summary>
    public int ButtonSize { get; private init; }

    /// <summary>All 18 placed buttons — tabs, tools, actions, status. The renderer walks it; the hit test walks it.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The zoomed region view — the surface the pencil paints on.</summary>
    public Rectangle Canvas { get; private init; }

    /// <summary>Window pixels per region pixel on the canvas.</summary>
    public int CanvasScale { get; private init; }

    /// <summary>The whole-sheet view with the region cursor.</summary>
    public Rectangle Sheet { get; private init; }

    /// <summary>Window pixels per sheet pixel in the sheet view.</summary>
    public int SheetScale { get; private init; }

    /// <summary>Side of one palette swatch, in window pixels.</summary>
    public int SwatchSize { get; private init; }

    /// <summary>Bounding box of all 16 swatches — the renderer frames it, the hit test pre-filters with it.</summary>
    public Rectangle Swatches { get; private init; }

    /// <summary>The one-row layers placeholder between the palette and the sheet (real layers are a later wave, owner's call).</summary>
    public Rectangle LayersStub { get; private init; }

    /// <summary>The bottom strip that holds the coordinates, the sprite number and the save/undo/redo buttons.</summary>
    public Rectangle StatusBar { get; private init; }

    /// <summary>Baseline for the status bar's text, vertically centred against its buttons.</summary>
    public int StatusTextY { get; private init; }

    /// <summary>Baseline of the reserved prompt/save-error line just above the status bar.</summary>
    public int PromptY { get; private init; }

    /// <summary>Region side in pixels, denormalized from the session so hit tests need only the layout.</summary>
    public int RegionPixels { get; private init; }

    public static SpriteEditorLayout Compute(int width, int height, int regionCells)
    {
        int ui = PixelFontAtlas.UiScale(width, height);
        int margin = 4 * ui;
        int gap = ui;
        int button = (EditorIcons.IconPixels + 4) * ui;
        int regionPixels = regionCells * VirtualConsole.SpriteSize;

        var buttons = new EditorButtonPlace[18];
        int placed = 0;

        // Tab strip. Exit alone at the left; the five editor tabs hang off the right edge in
        // the verdict's order — the rightmost is music, and walking leftwards: sounds,
        // tilemaps, sprites, code.
        buttons[placed++] = new EditorButtonPlace
        {
            Id = EditorButton.ExitTab, Rect = new Rectangle(margin, margin, button, button),
        };
        EditorButton[] rightTabs =
        {
            EditorButton.MusicTab, EditorButton.SoundTab, EditorButton.TilemapTab,
            EditorButton.SpritesTab, EditorButton.CodeTab,
        };
        for (int i = 0; i < rightTabs.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = rightTabs[i],
                Rect = new Rectangle(width - margin - button - i * (button + gap), margin, button, button),
            };
        }

        // Status bar with its buttons off the right edge: redo outermost, then undo, then the
        // saved/modified icon — so save, the most-used, is the innermost and closest to the canvas.
        var statusBar = new Rectangle(margin, height - margin - button, width - 2 * margin, button);
        EditorButton[] statusButtons = { EditorButton.Redo, EditorButton.Undo, EditorButton.Save };
        for (int i = 0; i < statusButtons.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = statusButtons[i],
                Rect = new Rectangle(statusBar.Right - button - i * (button + gap), statusBar.Y, button, button),
            };
        }

        // The prompt line is reserved whether or not a prompt is up: a canvas that jumps the
        // frame the author gets asked about unsaved work would move the very pixels they are
        // deciding over.
        int promptY = statusBar.Y - 2 * ui - PixelFontAtlas.LineHeight(ui);
        int top = margin + button + 2 * ui;
        int bottom = promptY - 2 * ui;

        // Left toolbar: the tool column, then the action row under it. The row is what fixes
        // the panel's width — four buttons across.
        EditorButton[] tools =
        {
            EditorButton.ToolSelect, EditorButton.ToolPencil, EditorButton.ToolFill,
            EditorButton.ToolStamp, EditorButton.ToolShape,
        };
        for (int i = 0; i < tools.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = tools[i], Rect = new Rectangle(margin, top + i * (button + gap), button, button),
            };
        }
        EditorButton[] actions =
        {
            EditorButton.FlipH, EditorButton.FlipV, EditorButton.Rotate, EditorButton.Clear,
        };
        int actionsY = top + tools.Length * (button + gap) + 2 * ui;
        for (int i = 0; i < actions.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = actions[i], Rect = new Rectangle(margin + i * (button + gap), actionsY, button, button),
            };
        }
        int panelWidth = actions.Length * button + (actions.Length - 1) * gap;

        // The canvas gets the largest whole-integer square the window allows after reserving
        // the toolbar's panel and a right column wide enough for the sheet at x2 — pixel-art
        // editing lives or dies by target size, so the drawing surface wins the leftovers.
        int canvasX = margin + panelWidth + margin;
        int canvasScale = Math.Max(1, Math.Min(
            (width - canvasX - 2 * margin - 2 * VirtualConsole.SheetWidth) / regionPixels,
            (bottom - top) / regionPixels));
        var canvas = new Rectangle(canvasX, top, regionPixels * canvasScale, regionPixels * canvasScale);

        int rightX = canvas.Right + margin;
        int rightWidth = Math.Max(0, width - rightX - margin);

        // Swatches want to be finger-big but must never push the sheet off screen, so their
        // size follows the column width down; the gap of one ui pixel keeps neighbouring
        // colors from fusing into a gradient.
        int swatchSize = Math.Max(4, Math.Min(12 * ui, (rightWidth - (SwatchColumns - 1) * gap) / SwatchColumns));
        var swatches = new Rectangle(
            rightX, top,
            SwatchColumns * swatchSize + (SwatchColumns - 1) * gap,
            SwatchRows * swatchSize + (SwatchRows - 1) * gap);

        var layersStub = new Rectangle(
            rightX, swatches.Bottom + 2 * ui, swatches.Width, PixelFontAtlas.LineHeight(ui) + 2 * ui);

        int sheetTop = layersStub.Bottom + 2 * ui;
        int sheetScale = Math.Max(
            1, Math.Min(rightWidth / VirtualConsole.SheetWidth, (bottom - sheetTop) / VirtualConsole.SheetHeight));
        var sheet = new Rectangle(
            rightX, sheetTop, VirtualConsole.SheetWidth * sheetScale, VirtualConsole.SheetHeight * sheetScale);

        return new SpriteEditorLayout
        {
            Ui = ui,
            Margin = margin,
            ButtonSize = button,
            Buttons = buttons,
            Canvas = canvas,
            CanvasScale = canvasScale,
            Sheet = sheet,
            SheetScale = sheetScale,
            SwatchSize = swatchSize,
            Swatches = swatches,
            LayersStub = layersStub,
            StatusBar = statusBar,
            StatusTextY = statusBar.Y + (button - PixelFontAtlas.LineHeight(ui)) / 2,
            PromptY = promptY,
            RegionPixels = regionPixels,
        };
    }

    /// <summary>The placed rectangle of one button — the tooltip anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id)
    {
        foreach (EditorButtonPlace place in Buttons)
        {
            if (place.Id == id)
            {
                return place.Rect;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(id), id, "every EditorButton is placed by Compute.");
    }

    /// <summary>The icon's destination inside a button: centred, at scale <see cref="Ui"/> — a whole multiple of the 8-px mask.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect)
    {
        int side = EditorIcons.IconPixels * Ui;
        int pad = (ButtonSize - side) / 2;
        return new Rectangle(buttonRect.X + pad, buttonRect.Y + pad, side, side);
    }

    /// <summary>
    /// Window point → button under it, stubs included — hover needs the dead buttons too
    /// (their tooltips say when they wake up); the click routing filters by
    /// <see cref="EditorIcons.IsStub"/> itself.
    /// </summary>
    public bool TryButton(int x, int y, out EditorButton id)
    {
        foreach (EditorButtonPlace place in Buttons)
        {
            if (place.Rect.Contains(x, y))
            {
                id = place.Id;
                return true;
            }
        }
        id = default;
        return false;
    }

    /// <summary>Swatch rectangle for a visible palette index — the one place swatch geometry exists.</summary>
    public Rectangle SwatchRect(int color)
    {
        int gap = Ui;
        return new Rectangle(
            Swatches.X + color % SwatchColumns * (SwatchSize + gap),
            Swatches.Y + color / SwatchColumns * (SwatchSize + gap),
            SwatchSize,
            SwatchSize);
    }

    /// <summary>Window point → region-local pixel, or false when the point is off the canvas (a press, not a drag).</summary>
    public bool TryCanvasPixel(int x, int y, out int localX, out int localY)
    {
        if (!Canvas.Contains(x, y))
        {
            localX = 0;
            localY = 0;
            return false;
        }
        localX = (x - Canvas.X) / CanvasScale;
        localY = (y - Canvas.Y) / CanvasScale;
        return true;
    }

    /// <summary>
    /// Window point → nearest region-local pixel, for drags: a stroke whose cursor leaves the
    /// canvas keeps painting along the edge instead of tearing (the clamp is why
    /// <see cref="SpriteEditorSession.Paint"/> can afford to throw on out-of-range input).
    /// Division truncates toward zero, which for points left/above the canvas lands on 0-ish
    /// values the clamp fixes anyway.
    /// </summary>
    public void ClampCanvasPixel(int x, int y, out int localX, out int localY)
    {
        localX = Math.Clamp((x - Canvas.X) / CanvasScale, 0, RegionPixels - 1);
        localY = Math.Clamp((y - Canvas.Y) / CanvasScale, 0, RegionPixels - 1);
    }

    /// <summary>Window point → sheet grid cell (0-15 each way), or false when the point is off the sheet view.</summary>
    public bool TrySheetCell(int x, int y, out int cellX, out int cellY)
    {
        if (!Sheet.Contains(x, y))
        {
            cellX = 0;
            cellY = 0;
            return false;
        }
        int cellPixels = SheetScale * VirtualConsole.SpriteSize;
        cellX = (x - Sheet.X) / cellPixels;
        cellY = (y - Sheet.Y) / cellPixels;
        return true;
    }

    /// <summary>Window point → visible palette index, or false. Sixteen rectangle checks, on a click, not per frame.</summary>
    public bool TrySwatch(int x, int y, out int color)
    {
        if (Swatches.Contains(x, y))
        {
            for (int i = 0; i < Palette.VisibleCount; i++)
            {
                if (SwatchRect(i).Contains(x, y))
                {
                    color = i;
                    return true;
                }
            }
        }
        color = 0;
        return false;
    }

    /// <summary>
    /// Clickable area of one prompt verb, ui-padded around its text on the prompt line. Only
    /// meaningful while the session's <see cref="SpriteEditorSession.ExitPromptShown"/> is up —
    /// the shell gates the hit test on that, the same way it gates the keys.
    /// </summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb)
    {
        int x = Margin + PixelFontAtlas.MeasureWidth(PromptHeading, Ui) + 4 * Ui;
        for (int i = 0; i < (int)verb; i++)
        {
            x += PixelFontAtlas.MeasureWidth(_promptVerbs[i], Ui) + 6 * Ui;
        }
        return new Rectangle(
            x - Ui, PromptY - Ui,
            PixelFontAtlas.MeasureWidth(_promptVerbs[(int)verb], Ui) + 2 * Ui,
            PixelFontAtlas.LineHeight(Ui) + 2 * Ui);
    }

    /// <summary>Window point → prompt verb, or false. Three rectangles, checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb)
    {
        for (int i = 0; i < _promptVerbs.Length; i++)
        {
            if (PromptVerbRect((EditorPromptVerb)i).Contains(x, y))
            {
                verb = (EditorPromptVerb)i;
                return true;
            }
        }
        verb = default;
        return false;
    }
}

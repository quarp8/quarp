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
/// <para><b>The shape is the owner's verdict (M9 stage 2.5) through his sixth review.</b>
/// Top: a tab strip of icons only — exit at the left; from the right corner leftwards music,
/// sounds, tilemaps, sprites, code; no text headers of any kind. Bottom: a status bar —
/// cursor coordinates, sprite number, the clickable saved/modified icon, undo/redo and (the
/// second review's move) the clear button right of redo. Both strips are full-window-width
/// bands with their own background tone, so they read as chrome and not as floating icons.
/// Left: the tool column alone — select, pencil, fill, stamp, shape, transform. Then the
/// canvas. Everything right of the canvas is <b>one column that the sheet window owns</b>
/// (sixth review, 2026-08-24): the palette keeps the top-right corner it took in the fourth
/// review, under it ONE narrow row carries the size toggle and the five layer tabs — both
/// moved left out of the places they used to hold — and under that row the sheet window
/// takes every remaining pixel down to its horizontal slider. Nothing is reserved for
/// future blocks any more; that reserve was exactly the emptiness the sixth review
/// rejected. The dirty-exit prompt keeps a reserved text line just above the status bar, so
/// its appearance never moves the canvas.</para>
///
/// <para><b>Every scale is a whole integer</b>, floored at 1: the canvas is the region's
/// pixels multiplied up, the sheet view is the <see cref="SheetStrip"/> presentation strip
/// multiplied up, icons are 8-px masks multiplied up, and fractional scales would resample
/// pixel art into blur (ARCHITECTURE §5's rule, applied to host UI). Two whole-number
/// choices carry the sixth review: the canvas gets the largest square that is a whole
/// multiple of the <b>largest</b> region, so all three region sizes draw the same rectangle
/// and neither the column nor the sheet twitches when Tab resizes the sprite; and the sheet
/// window is the tallest whole scale of the strip that fits under the row, then trimmed to a
/// whole number of sprite columns, so a half-drawn cell can never sit at its edge. The strip
/// still overflows that window at every window size the shell is used at, which is what
/// keeps the slider meaningful. In a pathologically small window the parts keep scale 1 and
/// may overflow — clipped, not crashed; the shell's default window is 8x the console and the
/// floor exists for resizes, not for use.</para>
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

    /// <summary>All 22 placed buttons — tabs, tools, status, the size toggle and the layer tabs. The renderer walks it; the hit test walks it.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>
    /// The full-width top band behind the tab icons (owner's second review: the strips get
    /// their own background so they stop melting into the window). The renderer fills it;
    /// panels start below it.
    /// </summary>
    public Rectangle TabStrip { get; private init; }

    /// <summary>The zoomed region view — the surface the pencil paints on.</summary>
    public Rectangle Canvas { get; private init; }

    /// <summary>Window pixels per region pixel on the canvas.</summary>
    public int CanvasScale { get; private init; }

    /// <summary>
    /// The sheet <b>window</b> (sixth review): everything the column has left under the
    /// narrow row — always a whole number of sprite columns wide and the strip's full
    /// <see cref="SheetStrip.Rows"/> tall. The presentation strip draws inside it at
    /// <see cref="SheetScale"/>, shifted by the scroll offset; what does not fit
    /// horizontally is what <see cref="SheetSlider"/> reaches.
    /// </summary>
    public Rectangle Sheet { get; private init; }

    /// <summary>
    /// Window pixels per strip pixel — the tallest whole scale the space under the row can
    /// hold, so the window is as big as the freed space allows without ever cutting a cell.
    /// </summary>
    public int SheetScale { get; private init; }

    /// <summary>The horizontal scroll slider's track, directly under the sheet window.</summary>
    public Rectangle SheetSlider { get; private init; }

    /// <summary>Side of one palette swatch, in window pixels.</summary>
    public int SwatchSize { get; private init; }

    /// <summary>Bounding box of all 16 swatches — the renderer frames it, the hit test pre-filters with it.</summary>
    public Rectangle Swatches { get; private init; }

    /// <summary>
    /// The bottom band that holds the coordinates, the sprite number and the
    /// save/undo/redo/clear buttons — full window width like <see cref="TabStrip"/>, filled
    /// with the same strip tone.
    /// </summary>
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

        var buttons = new EditorButtonPlace[22];
        int placed = 0;

        // Tab strip: a full-width band (owner's second review — its own background makes the
        // top row read as chrome). Exit alone at the left; the five editor tabs hang off the
        // right edge in the verdict's order — the rightmost is music, and walking leftwards:
        // sounds, tilemaps, sprites, code. Buttons sit a margin in from every band edge.
        var tabStrip = new Rectangle(0, 0, width, button + 2 * margin);
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

        // Status bar: the mirror band at the bottom. Buttons off the right edge, outermost
        // first: clear (the review's move — right of redo, Del hotkey unchanged), then redo,
        // undo, and the saved/modified icon — so save, the most-used, stays innermost and
        // closest to the canvas.
        var statusBar = new Rectangle(0, height - button - 2 * margin, width, button + 2 * margin);
        int statusButtonY = statusBar.Y + margin;
        EditorButton[] statusButtons =
        {
            EditorButton.Clear, EditorButton.Redo, EditorButton.Undo, EditorButton.Save,
        };
        for (int i = 0; i < statusButtons.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = statusButtons[i],
                Rect = new Rectangle(width - margin - button - i * (button + gap), statusButtonY, button, button),
            };
        }

        // The prompt line is reserved whether or not a prompt is up: a canvas that jumps the
        // frame the author gets asked about unsaved work would move the very pixels they are
        // deciding over.
        int promptY = statusBar.Y - 2 * ui - PixelFontAtlas.LineHeight(ui);
        int top = tabStrip.Bottom + 2 * ui;
        int bottom = promptY - 2 * ui;

        // Left toolbar: one column of tool slots, top to bottom — the action row died in the
        // owner's second review (its verbs live in the transform group slot and in the status
        // bar's clear), so the panel is exactly one button wide.
        EditorButton[] tools =
        {
            EditorButton.ToolSelect, EditorButton.ToolPencil, EditorButton.ToolFill,
            EditorButton.ToolStamp, EditorButton.ToolShape, EditorButton.ToolTransform,
        };
        for (int i = 0; i < tools.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = tools[i], Rect = new Rectangle(margin, top + i * (button + gap), button, button),
            };
        }
        int panelWidth = button;

        // The palette keeps the top-right corner (fourth review: it hugs the window's edge).
        // Swatches want to be finger-big; only a window too narrow to give a third of itself
        // to the column shrinks them, and the gap of one ui pixel keeps neighbouring colors
        // from fusing into a gradient. The sixth review moved everything BELOW it, so the
        // palette no longer dictates the column's width — the sheet does.
        int swatchSize = Math.Max(
            4, Math.Min(12 * ui, (width / 3 - (SwatchColumns - 1) * gap) / SwatchColumns));
        int paletteWidth = SwatchColumns * swatchSize + (SwatchColumns - 1) * gap;
        int paletteX = width - margin - paletteWidth;
        var swatches = new Rectangle(
            paletteX, top, paletteWidth, SwatchRows * swatchSize + (SwatchRows - 1) * gap);

        // The canvas takes the largest square that is a whole multiple of the LARGEST region
        // the size list offers (EditorIcons owns that list — asking it means a future 64-px
        // region cannot silently break this). Sizing the box instead of the sprite is what
        // makes 8, 16 and 32 px draw the identical rectangle: the column to its right is
        // measured from the box, so pressing Tab moves no panel but the pixels inside.
        int canvasX = margin + panelWidth + margin;
        int largestRegion = EditorIcons.SizeVariantCells(
            EditorIcons.GroupVariantCount(EditorButton.SizeToggle) - 1) * VirtualConsole.SpriteSize;
        int canvasRoom = Math.Min(bottom - top, paletteX - margin - canvasX);
        int canvasBox = Math.Max(largestRegion, canvasRoom / largestRegion * largestRegion);
        int canvasScale = Math.Max(1, canvasBox / regionPixels);
        var canvas = new Rectangle(canvasX, top, regionPixels * canvasScale, regionPixels * canvasScale);

        // The narrow row of the sixth review, and under it the sheet window that owns the
        // rest of the column. The sheet is sized first because the row is aligned to it: one
        // left edge for row, window and slider is what makes the three read as one block.
        // Height comes before width — the strip's whole scale is what the space below the row
        // can hold — and the width is then trimmed to whole sprite columns, so the window
        // never shows a sliced cell and the slider's thumb reports an honest fraction.
        int rowY = swatches.Bottom + 2 * ui;
        int sheetTop = rowY + button + 2 * ui;
        int sliderHeight = 4 * ui;
        int sheetScale = Math.Max(1, (bottom - sheetTop - sliderHeight - gap) / SheetStrip.PixelHeight);
        int sheetCell = VirtualConsole.SpriteSize * sheetScale;
        int columnRoom = width - margin - (canvasX + canvasBox + margin);
        int sheetWidth = Math.Clamp(columnRoom / sheetCell, 1, SheetStrip.Columns) * sheetCell;
        int sheetX = width - margin - sheetWidth;
        var sheet = new Rectangle(sheetX, sheetTop, sheetWidth, SheetStrip.PixelHeight * sheetScale);
        var slider = new Rectangle(sheetX, sheet.Bottom + gap, sheetWidth, sliderHeight);

        // The row itself: the size toggle first, then the five layer tabs (ADR-027's "вкладки
        // над окном листа" survives — they are still directly above the sheet, just sharing
        // the row now). Both left-aligned with the sheet window they steer.
        buttons[placed++] = new EditorButtonPlace
        {
            Id = EditorButton.SizeToggle, Rect = new Rectangle(sheetX, rowY, button, button),
        };
        for (int i = 0; i < 5; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = EditorButton.LayerTab1 + i,
                Rect = new Rectangle(sheetX + (i + 1) * (button + gap), rowY, button, button),
            };
        }

        return new SpriteEditorLayout
        {
            Ui = ui,
            Margin = margin,
            ButtonSize = button,
            Buttons = buttons,
            TabStrip = tabStrip,
            Canvas = canvas,
            CanvasScale = canvasScale,
            Sheet = sheet,
            SheetScale = sheetScale,
            SheetSlider = slider,
            SwatchSize = swatchSize,
            Swatches = swatches,
            StatusBar = statusBar,
            StatusTextY = statusButtonY + (button - PixelFontAtlas.LineHeight(ui)) / 2,
            PromptY = promptY,
            RegionPixels = regionPixels,
        };
    }

    /// <summary>
    /// How many virtual strip pixels the window shows across — a whole number of sprite
    /// columns by construction, because <see cref="Compute"/> trims the window to cells.
    /// </summary>
    public int SheetVisiblePixels => Math.Min(SheetStrip.PixelWidth, Sheet.Width / SheetScale);

    /// <summary>The scroll offset's ceiling, in strip pixels — the slider's and wheel's shared clamp.</summary>
    public int SheetMaxScroll => SheetStrip.PixelWidth - SheetVisiblePixels;

    /// <summary>
    /// The slider's thumb for a given scroll offset: proportional width (the visible share of
    /// the sheet), full track when nothing overflows — an honest "everything is on screen".
    /// The renderer draws this rectangle and <see cref="SheetScrollForSliderX"/> inverts it,
    /// so the thumb can never sit where a drag would not put it.
    /// </summary>
    public Rectangle SheetThumb(int scroll)
    {
        int thumbWidth = Math.Max(2 * Ui, SheetSlider.Width * SheetVisiblePixels / SheetStrip.PixelWidth);
        int range = SheetSlider.Width - thumbWidth;
        int max = SheetMaxScroll;
        int x = max == 0 ? SheetSlider.X : SheetSlider.X + scroll * range / max;
        return new Rectangle(x, SheetSlider.Y, thumbWidth, SheetSlider.Height);
    }

    /// <summary>
    /// A drag position on the slider → the scroll offset that centres the thumb there,
    /// clamped to the sheet's border (the wave's named negative control: a drag past the
    /// track's end parks at the last column, never beyond the sheet). Zero when nothing
    /// overflows, so a drag on a resting slider is a visible no-op.
    /// </summary>
    public int SheetScrollForSliderX(int x)
    {
        int max = SheetMaxScroll;
        if (max == 0)
        {
            return 0;
        }
        int thumbWidth = Math.Max(2 * Ui, SheetSlider.Width * SheetVisiblePixels / SheetStrip.PixelWidth);
        int range = SheetSlider.Width - thumbWidth;
        if (range <= 0)
        {
            return 0;   // a track the thumb fills exactly has nowhere to drag
        }
        return Math.Clamp((x - thumbWidth / 2 - SheetSlider.X) * max / range, 0, max);
    }

    /// <summary>
    /// One variant button of a group slot's flyout: a row growing rightward from the slot,
    /// same size as every icon-button. It deliberately floats over the canvas — the flyout is
    /// transient and drawn last, so reserving space for it would waste canvas on the 99 % of
    /// frames it is closed.
    /// </summary>
    public Rectangle FlyoutVariantRect(EditorButton slot, int variant)
    {
        Rectangle anchor = ButtonRect(slot);
        return new Rectangle(
            anchor.Right + Ui + variant * (ButtonSize + Ui), anchor.Y, ButtonSize, ButtonSize);
    }

    /// <summary>Window point → variant index inside <paramref name="slot"/>'s flyout, or false. Checked only while that flyout is open.</summary>
    public bool TryFlyoutVariant(int x, int y, EditorButton slot, out int variant)
    {
        for (int i = 0; i < EditorIcons.GroupVariantCount(slot); i++)
        {
            if (FlyoutVariantRect(slot, i).Contains(x, y))
            {
                variant = i;
                return true;
            }
        }
        variant = 0;
        return false;
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

    /// <summary>
    /// Window point → canonical sheet cell (0-15 each way), or false when the point is off
    /// the strip. The inverse delegates to <see cref="SheetStrip"/>, because the
    /// renderer and mouse router must not own competing versions of the lane formula.
    /// </summary>
    public bool TrySheetCell(int x, int y, int scroll, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        if (!Sheet.Contains(x, y))
        {
            return false;
        }
        int stripX = (x - Sheet.X) / SheetScale + scroll;
        int stripY = (y - Sheet.Y) / SheetScale;
        return SheetStrip.TryStripCellToSheetCell(
            stripX / VirtualConsole.SpriteSize,
            stripY / VirtualConsole.SpriteSize,
            out cellX,
            out cellY);
    }

    /// <summary>
    /// Highlight rectangles for a canonical square region. A region crossing a lane boundary
    /// (<see cref="SheetStrip.Rows"/> sheet rows) intentionally becomes multiple rectangles:
    /// it is still one model region, but its rows live in separate pieces of the strip.
    /// </summary>
    public IReadOnlyList<Rectangle> SheetRegionHighlights(
        int sheetCellX, int sheetCellY, int regionCells, int scroll)
    {
        var highlights = new List<Rectangle>(2);
        int sheetEndY = sheetCellY + regionCells;
        int cell = SheetScale * VirtualConsole.SpriteSize;
        for (int y = sheetCellY; y < sheetEndY;)
        {
            int laneEndY = Math.Min(sheetEndY, (y / SheetStrip.Rows + 1) * SheetStrip.Rows);
            SheetStrip.SpriteToStripCell(
                y * SheetStrip.LaneColumns + sheetCellX, out int stripColumn, out int stripRow);
            var piece = new Rectangle(
                Sheet.X + (stripColumn * VirtualConsole.SpriteSize - scroll) * SheetScale,
                Sheet.Y + stripRow * cell,
                regionCells * cell,
                (laneEndY - y) * cell);
            highlights.Add(piece);
            y = laneEndY;
        }
        return highlights;
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

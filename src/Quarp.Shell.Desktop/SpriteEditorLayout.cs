using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where everything on the sprite editor screen sits, as a pure function of the window size
/// and the region size — computed fresh each frame, like every host-UI measurement (see
/// <see cref="LibraryRenderer"/>'s type comment on why that is safe here and nowhere near a
/// hash). This is the geometry's <b>single owner</b>: <see cref="SpriteEditorRenderer"/> draws
/// these rectangles and <c>QuarpGame</c> hit-tests the mouse against the same ones, so a
/// button can never be painted in one place and clicked in another.
///
/// <para>The shared frame — bands, button row, prompt line, margins — is measured by
/// <see cref="EditorChrome"/> and only forwarded here; this struct owns what stands inside it.</para>
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
/// rejected.</para>
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

    // The status band's row, outermost first: clear (the second review's move — right of redo,
    // Del hotkey unchanged), then redo, undo, and the saved/modified icon — so save, the
    // most-used, stays innermost and closest to the canvas.
    // Slot 0 is the rightmost place in the status bar. Clear owns it here (the owner's second
    // review: "кнопка очистки — вниз, в статус-бар, справа от redo"); the map screen leaves the
    // same slot empty so Save, Undo and Redo do not move between editors.
    private static readonly EditorButton?[] _statusSlots =
    {
        EditorButton.Clear, EditorButton.Redo, EditorButton.Undo, EditorButton.Save,
    };

    /// <summary>The frame this screen stands in — bands, margins, button size, prompt line. See <see cref="EditorChrome"/>.</summary>
    public EditorChrome Chrome { get; private init; }

    // Forwarded, not recomputed — EditorChrome is the only place these exist.
    public int Ui => Chrome.Ui;

    public int Margin => Chrome.Margin;

    public int ButtonSize => Chrome.ButtonSize;

    public Rectangle TabStrip => Chrome.TabStrip;

    public Rectangle StatusBar => Chrome.StatusBar;

    public int PromptY => Chrome.PromptY;

    /// <summary>All 22 placed buttons — tabs, tools, status, the size toggle and the layer tabs. The renderer walks it; the hit test walks it.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

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

    /// <summary>Region side in pixels, denormalized from the session so hit tests need only the layout.</summary>
    public int RegionPixels { get; private init; }

    public static SpriteEditorLayout Compute(int width, int height, int regionCells)
    {
        var buttons = new EditorButtonPlace[22];
        int placed = 0;
        EditorChrome chrome = EditorChrome.Compute(width, height, buttons, ref placed, _statusSlots);

        int ui = chrome.Ui;
        int margin = chrome.Margin;
        int gap = chrome.Gap;
        int button = chrome.ButtonSize;
        int top = chrome.ContentTop;
        int bottom = chrome.ContentBottom;
        int regionPixels = regionCells * VirtualConsole.SpriteSize;

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

        // Swatch size first: the palette's width is what the canvas has to leave room for, and
        // finger-big swatches are the point (only a window too narrow to give a third of itself
        // to the column shrinks them). The gap of one ui pixel keeps neighbouring colors from
        // fusing into a gradient.
        int swatchSize = Math.Max(
            4, Math.Min(12 * ui, (width / 3 - (SwatchColumns - 1) * gap) / SwatchColumns));
        int paletteWidth = SwatchColumns * swatchSize + (SwatchColumns - 1) * gap;

        // The canvas takes the largest square that is a whole multiple of the LARGEST region
        // the size list offers (EditorIcons owns that list — asking it means a future 64-px
        // region cannot silently break this). Sizing the box instead of the sprite is what
        // makes 8, 16 and 32 px draw the identical rectangle: everything to its right is
        // measured from the box, so pressing Tab moves no panel but the pixels inside.
        //
        // Room for the right side is now two things, not one (seventh review, 2026-08-24): a
        // tool column one button wide that hugs the canvas the way the toolbar hugs it on the
        // left, and behind it the content column that must at least hold the palette.
        int canvasX = margin + panelWidth + margin;
        int largestRegion = EditorIcons.SizeVariantCells(
            EditorIcons.GroupVariantCount(EditorButton.SizeToggle) - 1) * VirtualConsole.SpriteSize;
        int rightSideRoom = button + margin + paletteWidth + margin;
        int canvasRoom = Math.Min(bottom - top, width - canvasX - margin - rightSideRoom);
        int canvasBox = Math.Max(largestRegion, canvasRoom / largestRegion * largestRegion);
        int canvasScale = Math.Max(1, canvasBox / regionPixels);
        var canvas = new Rectangle(canvasX, top, regionPixels * canvasScale, regionPixels * canvasScale);

        // The seventh review's two columns. The right tool column starts level with the canvas
        // and with the left toolbar — same gap from the canvas on both sides, so the drawing
        // surface sits between two mirrored strips of buttons. The content column starts one
        // more gap to the right and owns everything else: palette on top, the layer tabs under
        // it, the sheet under them, all sharing one left edge.
        int rightToolsX = canvasX + canvasBox + margin;
        int contentX = rightToolsX + button + margin;
        var swatches = new Rectangle(
            contentX, top, paletteWidth, SwatchRows * swatchSize + (SwatchRows - 1) * gap);
        buttons[placed++] = new EditorButtonPlace
        {
            Id = EditorButton.SizeToggle, Rect = new Rectangle(rightToolsX, top, button, button),
        };

        // Under the palette: the layer tabs, and under them the sheet window that owns the rest
        // of the column. The sheet is sized first because the tabs are aligned to it — one left
        // edge for tabs, window and slider is what makes the three read as one block. Height
        // comes before width (the strip's whole scale is what the space under the tabs can
        // hold), and the width is then trimmed to whole sprite columns, so the window never
        // shows a sliced cell and the slider's thumb reports an honest fraction.
        int rowY = swatches.Bottom + 2 * ui;
        int sheetTop = rowY + button + 2 * ui;
        int sliderHeight = 4 * ui;
        // Math.Max(1, ...) is a floor, not a fit: in a window below the shell's working size
        // (320x180, the console's own resolution) even one-to-one does not fit under the row,
        // and the sheet plus its slider are drawn over the prompt line. Known, measured and
        // carded (tasks/open/debt-tiny-window-layout.md) rather than papered over — the honest
        // repairs are a minimum window size or a vertically clipped strip, and neither belongs
        // in a wave about the right column.
        int sheetScale = Math.Max(1, (bottom - sheetTop - sliderHeight - gap) / SheetStrip.PixelHeight);
        int sheetCell = VirtualConsole.SpriteSize * sheetScale;
        int columnRoom = width - margin - contentX;
        int sheetWidth = Math.Clamp(columnRoom / sheetCell, 1, SheetStrip.Columns) * sheetCell;
        var sheet = new Rectangle(contentX, sheetTop, sheetWidth, SheetStrip.PixelHeight * sheetScale);
        var slider = new Rectangle(contentX, sheet.Bottom + gap, sheetWidth, sliderHeight);

        // The five layer tabs, left-aligned with the window they steer (ADR-027's "вкладки над
        // окном листа" survives every review — they are still directly above the sheet). The
        // size toggle used to open this row; the seventh review moved it into the tool column,
        // where it stands next to the canvas it resizes.
        for (int i = 0; i < 5; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = EditorButton.LayerTab1 + i,
                Rect = new Rectangle(contentX + i * (button + gap), rowY, button, button),
            };
        }

        return new SpriteEditorLayout
        {
            Chrome = chrome,
            Buttons = buttons,
            Canvas = canvas,
            CanvasScale = canvasScale,
            Sheet = sheet,
            SheetScale = sheetScale,
            SheetSlider = slider,
            SwatchSize = swatchSize,
            Swatches = swatches,
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
    public Rectangle ButtonRect(EditorButton id) => EditorChrome.ButtonRect(Buttons, id);

    /// <summary>The icon's destination inside a button: centred, at scale <see cref="Ui"/>.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => Chrome.ButtonIconRect(buttonRect);

    /// <summary>Window point → button under it, stubs included — hover needs the dead buttons too.</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        EditorChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="EditorChrome"/> owns the prompt line.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Window point → prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

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
}

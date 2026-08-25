using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where everything on the sprite editor screen sits, in <b>console pixels</b> — 160x90 on
/// profile 8. Wave R2 moved this screen onto the console (ADR-029), and this struct is the
/// whole of the geometry that moved: every coordinate that used to be derived from the window
/// size and <c>PixelFontMetrics.UiScale</c> is now a fixed number on the console's own grid,
/// the way TIC-80 writes <c>PaletteX = 24, PaletteY = 112</c> and means columns and rows of a
/// 240x136 screen. It stays the geometry's <b>single owner</b>:
/// <see cref="SpriteEditorRenderer"/> draws these rectangles and
/// <see cref="SpriteEditorInput"/> hit-tests the pointer against the same ones, so a button
/// can never be painted in one place and clicked in another.
///
/// <para>The shared frame — the two bands, the three rules, the exit button, the five editor
/// tabs, the tooltip field, the message line and its clickable verbs — is measured by
/// <see cref="ConsoleChrome"/> and only forwarded here; this struct owns what stands inside
/// it.</para>
///
/// <para><b>THE ARITHMETIC, in full, because on this screen it is the design.</b> Ninety rows
/// minus the ten-row top band, the three rules, three rows of scroll slider, five rows of
/// message and five of status leaves <b>sixty-four</b> rows of content
/// (<see cref="ConsoleChrome"/> carries that sum). Sixty-four is exactly an 8x8 sprite at zoom
/// 8, which is what the order asks the canvas to be and what PICO-8 and TIC-80 both give it,
/// and it is also exactly <see cref="SheetStrip.PixelHeight"/>, so the sheet window can show
/// the strip's full height at scale 1 with no vertical scrolling to invent. Across the 160
/// columns: twenty for a two-wide tool column, sixty-four for the canvas, twenty for a two-wide
/// column of palette / flags / layer tabs, and the remaining fifty-six — seven whole sprite
/// cells — for the sheet window. Twenty plus sixty-four plus twenty plus fifty-six is one
/// hundred and sixty, with nothing spare, which is why no panel here has a margin around it:
/// the tool column's own button frames are what separate it from the canvas.</para>
///
/// <para><b>What the move cost, named rather than hidden.</b> (1) The sheet window shows 7x8 =
/// <b>56</b> of the 256 sprites at once and scrolls horizontally for the rest; the host screen
/// showed sixteen columns and TIC-80 shows all 256 at once on a screen 80 px wider than ours.
/// (2) The palette is a 4x4 grid, not two rows of eight — eight columns of swatch-plus-gap
/// need 39 px and this column is 20. PICO-8's sprite editor palette is 4x4 for the same
/// reason. (3) The flag row is 4x2 for that same reason, where all three references use one row
/// of eight. (4) The tooltip is printed in the top band instead of popping under the pointer,
/// and is cut to 25 characters — see <see cref="ConsoleChrome.TooltipChars"/>. (5) The message
/// band is one line, so a standing notice yields to the exit prompt — see
/// <see cref="ConsoleChromeRenderer.DrawMessageLine"/>. Nothing was dropped: all
/// twenty-two buttons of the host layout are placed, and every hit test it had is here.</para>
///
/// <para><b>Every scale is a whole integer.</b> The canvas box is a whole multiple of the
/// <em>largest</em> region the size list offers, so 8, 16 and 32 px sprites all draw the same
/// 64x64 rectangle and pressing Tab moves no furniture; the sheet window is a whole number of
/// sprite cells wide, so a half-drawn cell can never sit at its edge. A fractional scale would
/// resample pixel art into blur (ARCHITECTURE §5) and there is no path here that can produce
/// one.</para>
/// </summary>
public readonly struct SpriteEditorLayout
{
    /// <summary>Palette shape: sixteen colours as four columns of four — PICO-8's own grid, and all this column can hold.</summary>
    private const int SwatchColumns = 4;

    private const int SwatchRows = Palette.VisibleCount / SwatchColumns;

    /// <summary>Flag shape: eight toggles as four columns of two, for the width the column has.</summary>
    private const int FlagColumns = 4;

    private const int FlagRows = SpriteEditorSession.FlagBits / FlagColumns;

    /// <summary>Side of a swatch and of a flag cell; the one gap between them keeps neighbouring colours from fusing.</summary>
    private const int CellSize = 4;

    private const int CellPitch = CellSize + 1;

    /// <summary>The tool column is two buttons wide; its twelve slots hold eleven buttons and one gap.</summary>
    private const int ToolColumns = 2;

    /// <summary>The middle column is two buttons wide; the layer tabs use three of its rows.</summary>
    private const int MiddleColumns = 2;

    // The tool column in reading order, left to right and down. The six tools first (the
    // owner's second review's column, unchanged in content), then the size toggle, then the
    // four buttons the host frame kept in its status bar — save, undo, redo, clear. They moved
    // because the console's status line is five pixels tall and an icon-button is ten: a band
    // that cannot hold a button cannot hold a button row. A null is an empty slot, not a
    // shifted neighbour, exactly as EditorChrome's status slots work.
    private static readonly EditorButton?[] _toolSlots =
    {
        EditorButton.ToolSelect, EditorButton.ToolPencil,
        EditorButton.ToolFill, EditorButton.ToolStamp,
        EditorButton.ToolShape, EditorButton.ToolTransform,
        EditorButton.SizeToggle, EditorButton.Clear,
        EditorButton.Save, EditorButton.Undo,
        EditorButton.Redo, null,
    };

    /// <summary>The frame this screen stands in. See <see cref="ConsoleChrome"/>.</summary>
    public ConsoleChrome Chrome { get; private init; }

    // Forwarded, not recomputed — ConsoleChrome is the only place these exist.

    /// <summary>Screen width in console pixels.</summary>
    public int ScreenWidth => Chrome.ScreenWidth;

    /// <summary>Screen height in console pixels.</summary>
    public int ScreenHeight => Chrome.ScreenHeight;

    /// <summary>Side of every icon-button — ten console pixels, an 8x8 mask plus its frame.</summary>
    public int ButtonSize => ConsoleChrome.ButtonSize;

    /// <summary>Screen-edge inset for text — one pixel, because forty columns is the whole line.</summary>
    public int Margin => ConsoleChrome.Margin;

    /// <summary>The top band that carries the exit button, the tooltip field and the five editor tabs.</summary>
    public Rectangle TabStrip => Chrome.TopBar;

    /// <summary>The status band: coordinates at the left, the sprite number at the right.</summary>
    public Rectangle StatusBar => Chrome.StatusBar;

    /// <summary>Glyph top of the single message line — the exit prompt, the save error or the standing notice.</summary>
    public int PromptY => Chrome.MessageY;

    /// <summary>All 22 placed buttons — the frame's six, the tool column's eleven and the five layer tabs.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The zoomed region view — the surface the pencil paints on. Always 64x64.</summary>
    public Rectangle Canvas { get; private init; }

    /// <summary>Console pixels per region pixel on the canvas: 8, 4 or 2 for an 8, 16 or 32 px sprite.</summary>
    public int CanvasScale { get; private init; }

    /// <summary>
    /// The sheet window: seven whole sprite columns wide and the strip's full
    /// <see cref="SheetStrip.Rows"/> tall. The presentation strip draws inside it at
    /// <see cref="SheetScale"/>, shifted by the scroll offset; what does not fit horizontally is
    /// what <see cref="SheetSlider"/> reaches.
    /// </summary>
    public Rectangle Sheet { get; private init; }

    /// <summary>Console pixels per strip pixel — one, and one is the only value 64 rows allow.</summary>
    public int SheetScale { get; private init; }

    /// <summary>The horizontal scroll slider's track, directly under the sheet window.</summary>
    public Rectangle SheetSlider { get; private init; }

    /// <summary>Side of one palette swatch, in console pixels.</summary>
    public int SwatchSize { get; private init; }

    /// <summary>Bounding box of all 16 swatches — the renderer walks it, the hit test pre-filters with it.</summary>
    public Rectangle Swatches { get; private init; }

    /// <summary>Side of one flag toggle's cell — a swatch, so the two blocks share one grid and one left edge.</summary>
    public int FlagSize { get; private init; }

    /// <summary>Bounding box of the eight flag toggles, under the palette in the middle column.</summary>
    public Rectangle FlagPanel { get; private init; }

    /// <summary>Region side in pixels, denormalized from the session so hit tests need only the layout.</summary>
    public int RegionPixels { get; private init; }

    /// <summary>
    /// The screen's geometry for a console of the given size. The two numbers are <b>console</b>
    /// pixels — 160x90 on profile 8 — and never a window size: since wave R2 the window's only
    /// say in this screen is the whole-integer scale <see cref="FramePlacement"/> presents it at.
    /// </summary>
    public static SpriteEditorLayout Compute(int screenWidth, int screenHeight, int regionCells)
    {
        var buttons = new EditorButtonPlace[22];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(screenWidth, screenHeight, buttons, ref placed);

        int button = ConsoleChrome.ButtonSize;
        int top = chrome.ContentTop;
        int regionPixels = regionCells * VirtualConsole.SpriteSize;

        // The tool column: two buttons wide, hard against the left edge. Its own button frames
        // are the rule that separates it from the canvas — there are no spare columns for a
        // margin, and a frame is already a line.
        for (int slot = 0; slot < _toolSlots.Length; slot++)
        {
            if (_toolSlots[slot] is not EditorButton id)
            {
                continue;
            }
            buttons[placed++] = new EditorButtonPlace
            {
                Id = id,
                Rect = new Rectangle(
                    slot % ToolColumns * button, top + slot / ToolColumns * button, button, button),
            };
        }
        int toolWidth = ToolColumns * button;

        // The canvas takes the largest square that is a whole multiple of the LARGEST region the
        // size list offers (EditorIcons owns that list, so a future 64-px region cannot silently
        // break this). Sizing the BOX and not the sprite is what makes 8, 16 and 32 px draw the
        // identical rectangle: everything right of it is measured from the box, so pressing Tab
        // moves no panel, only the pixels inside.
        int largestRegion = EditorIcons.SizeVariantCells(
            EditorIcons.GroupVariantCount(EditorButton.SizeToggle) - 1) * VirtualConsole.SpriteSize;
        int canvasBox = Math.Max(largestRegion, chrome.ContentHeight / largestRegion * largestRegion);
        int canvasScale = Math.Max(1, canvasBox / regionPixels);
        var canvas = new Rectangle(toolWidth, top, regionPixels * canvasScale, regionPixels * canvasScale);

        // The middle column: palette on top, the eight flag toggles under it, the five layer
        // tabs under those — one left edge for all three, one clear row between them.
        int middleX = toolWidth + canvasBox;
        int swatchSize = CellSize;
        var swatches = new Rectangle(
            middleX, top,
            SwatchColumns * CellPitch - 1, SwatchRows * CellPitch - 1);
        var flagPanel = new Rectangle(
            middleX, swatches.Bottom + 1,
            FlagColumns * CellPitch - 1, FlagRows * CellPitch - 1);
        int layerTabsY = flagPanel.Bottom + 1;
        for (int i = 0; i < SpriteEditorSession.LayerCount; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = EditorButton.LayerTab1 + i,
                Rect = new Rectangle(
                    middleX + i % MiddleColumns * button,
                    layerTabsY + i / MiddleColumns * button,
                    button,
                    button),
            };
        }

        // The sheet window takes every column the other three panels left, trimmed to whole
        // sprite cells so its edge can never show a sliced sprite. Its height is the strip's own,
        // at the only scale sixty-four rows allow.
        int sheetX = middleX + MiddleColumns * button;
        int sheetScale = Math.Max(1, chrome.ContentHeight / SheetStrip.PixelHeight);
        int sheetCell = VirtualConsole.SpriteSize * sheetScale;
        int sheetWidth =
            Math.Clamp((screenWidth - sheetX) / sheetCell, 1, SheetStrip.Columns) * sheetCell;
        var sheet = new Rectangle(sheetX, top, sheetWidth, SheetStrip.PixelHeight * sheetScale);
        var slider = new Rectangle(sheetX, chrome.SliderY, sheetWidth, ConsoleChrome.SliderHeight);

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
            FlagSize = CellSize,
            FlagPanel = flagPanel,
            RegionPixels = regionPixels,
        };
    }

    /// <summary>How many strip pixels the window shows across — a whole number of sprite columns by construction.</summary>
    public int SheetVisiblePixels => Math.Min(SheetStrip.PixelWidth, Sheet.Width / SheetScale);

    /// <summary>The scroll offset's ceiling, in strip pixels — the slider's and the wheel's shared clamp.</summary>
    public int SheetMaxScroll => SheetStrip.PixelWidth - SheetVisiblePixels;

    /// <summary>
    /// The slider's thumb for a given scroll offset: proportional width (the visible share of
    /// the strip), full track when nothing overflows. The renderer draws this rectangle and
    /// <see cref="SheetScrollForSliderX"/> inverts it, so the thumb can never sit where a drag
    /// would not put it. The two-pixel floor is what keeps a thumb grabbable at the console's
    /// scale — two console pixels are sixteen window pixels at the shell's default window.
    /// </summary>
    public Rectangle SheetThumb(int scroll)
    {
        int thumbWidth = Math.Max(2, SheetSlider.Width * SheetVisiblePixels / SheetStrip.PixelWidth);
        int range = SheetSlider.Width - thumbWidth;
        int max = SheetMaxScroll;
        int x = max == 0 ? SheetSlider.X : SheetSlider.X + scroll * range / max;
        return new Rectangle(x, SheetSlider.Y, thumbWidth, SheetSlider.Height);
    }

    /// <summary>
    /// A drag position on the slider to the scroll offset that centres the thumb there, clamped
    /// to the strip's border: a drag past the track's end parks at the last column, never
    /// beyond. Zero when nothing overflows, so a drag on a resting slider is a visible no-op.
    /// </summary>
    public int SheetScrollForSliderX(int x)
    {
        int max = SheetMaxScroll;
        if (max == 0)
        {
            return 0;
        }
        int thumbWidth = Math.Max(2, SheetSlider.Width * SheetVisiblePixels / SheetStrip.PixelWidth);
        int range = SheetSlider.Width - thumbWidth;
        if (range <= 0)
        {
            return 0;   // a track the thumb fills exactly has nowhere to drag
        }
        return Math.Clamp((x - thumbWidth / 2 - SheetSlider.X) * max / range, 0, max);
    }

    /// <summary>
    /// One variant button of a group slot's flyout: a row growing rightward from the slot, same
    /// size as every icon-button. It deliberately floats over the canvas — the flyout is
    /// transient and drawn last, so reserving space for it would waste canvas on the 99 % of
    /// frames it is closed, and on a 160 px screen there is no space to reserve.
    /// </summary>
    public Rectangle FlyoutVariantRect(EditorButton slot, int variant)
    {
        Rectangle anchor = ButtonRect(slot);
        return new Rectangle(
            anchor.Right + 1 + variant * (ButtonSize + 1), anchor.Y, ButtonSize, ButtonSize);
    }

    /// <summary>Console point to a variant index inside <paramref name="slot"/>'s flyout, or false. Checked only while that flyout is open.</summary>
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

    /// <summary>The placed rectangle of one button.</summary>
    public Rectangle ButtonRect(EditorButton id) => ConsoleChrome.ButtonRect(Buttons, id);

    /// <summary>The 8x8 mask's destination inside a button.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => ConsoleChrome.ButtonIconRect(buttonRect);

    /// <summary>Console point to the button under it, stubs included — hover needs the dead buttons too.</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        ConsoleChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="ConsoleChrome"/> owns the message line.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Console point to a prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    /// <summary>Swatch rectangle for a visible palette index — the one place swatch geometry exists.</summary>
    public Rectangle SwatchRect(int color) =>
        new(Swatches.X + color % SwatchColumns * CellPitch,
            Swatches.Y + color / SwatchColumns * CellPitch,
            SwatchSize,
            SwatchSize);

    /// <summary>
    /// One flag toggle's cell, bit 0 at the top left and reading order from there — the one
    /// place flag geometry exists, so the renderer's square and the router's click can never sit
    /// on different pixels. The number a toggle carries is read off its POSITION (PICO-8's
    /// "indexed from 0 starting from the left"), the way the sixteen swatches carry their
    /// indices; the hover label names the bit and its key for whoever wants it spelled out.
    ///
    /// <para>Four columns of two rather than the references' single row of eight is this
    /// column's width speaking: eight cells of swatch-plus-gap need 39 px and the column is 20.
    /// The cell is the whole hit target here — the host layout painted a smaller mark inside a
    /// bigger cell, which at four console pixels would leave a mark of two.</para>
    /// </summary>
    public Rectangle FlagRect(int bit) =>
        new(FlagPanel.X + bit % FlagColumns * CellPitch,
            FlagPanel.Y + bit / FlagColumns * CellPitch,
            FlagSize,
            FlagSize);

    /// <summary>Console point to a flag bit 0-7, or false — the same shape as <see cref="TrySwatch"/>, gaps included and therefore rejected.</summary>
    public bool TryFlag(int x, int y, out int bit)
    {
        if (FlagPanel.Contains(x, y))
        {
            for (int i = 0; i < SpriteEditorSession.FlagBits; i++)
            {
                if (FlagRect(i).Contains(x, y))
                {
                    bit = i;
                    return true;
                }
            }
        }
        bit = 0;
        return false;
    }

    /// <summary>Console point to a region-local pixel, or false when the point is off the canvas (a press, not a drag).</summary>
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
    /// Console point to the nearest region-local pixel, for drags: a stroke whose cursor leaves
    /// the canvas keeps painting along the edge instead of tearing (the clamp is why
    /// <see cref="SpriteEditorSession.Paint"/> can afford to throw on out-of-range input).
    /// </summary>
    public void ClampCanvasPixel(int x, int y, out int localX, out int localY)
    {
        localX = Math.Clamp((x - Canvas.X) / CanvasScale, 0, RegionPixels - 1);
        localY = Math.Clamp((y - Canvas.Y) / CanvasScale, 0, RegionPixels - 1);
    }

    /// <summary>
    /// Console point to a canonical sheet cell (0-15 each way), or false when the point is off
    /// the window. The inverse delegates to <see cref="SheetStrip"/>, because the renderer and
    /// the mouse router must not own competing versions of the lane formula.
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
    /// (<see cref="SheetStrip.Rows"/> sheet rows) intentionally becomes several rectangles: it
    /// is still one model region, but its rows live in separate pieces of the strip.
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

    /// <summary>Console point to a visible palette index, or false. Sixteen rectangle checks, on a click, not per frame.</summary>
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

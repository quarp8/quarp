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
/// the tool column's own button frames are what separate it from the canvas.
///
/// <para><b>One pixel of that sum was, however, being wasted, and since 2026-08-25 it is not.</b>
/// The middle column is twenty pixels because five layer tabs must fit two abreast, but the
/// palette and the flag block are nineteen (four cells of four, three gaps of one). Their spare
/// pixel used to sit on the right of the column doing nothing; the blocks are now flush against
/// the column's right edge and the pixel sits on its left, where it is the canvas's border —
/// see <see cref="CanvasFrame"/>, which is why the canvas had no visible edge at all until that
/// day and has one now.</para>
///
/// <para><b>What the move cost, named rather than hidden.</b> (1) The sheet window shows 7x8 =
/// <b>56</b> of the 256 sprites at once and scrolls horizontally for the rest; the host screen
/// showed sixteen columns and TIC-80 shows all 256 at once on a screen 80 px wider than ours.
/// (2) The palette is a 4x4 grid, not two rows of eight — eight columns of swatch-plus-gap
/// need 39 px and this column is 20. PICO-8's sprite editor palette is 4x4 for the same
/// reason. (3) The flag row is 4x2 for that same reason, where all three references use one row
/// of eight. (4) The tooltip is printed in the top band instead of popping under the pointer,
/// and is cut to 22 characters — see <see cref="ConsoleChrome.TooltipChars"/>. (5) The message
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

    /// <summary>The tool column is two buttons wide; its twelve slots are now all twelve buttons — the last one was the brush toggle's.</summary>
    private const int ToolColumns = 2;

    /// <summary>The middle column is two buttons wide; the layer tabs use three of its rows.</summary>
    private const int MiddleColumns = 2;

    // The tool column in reading order, left to right and down. The six tools first (the
    // owner's second review's column, unchanged in content), then the size toggle, then the
    // four buttons the host frame kept in its status bar — save, undo, redo, clear. They moved
    // because the console's status line is five pixels tall and an icon-button is ten: a band
    // that cannot hold a button cannot hold a button row. A null is an empty slot, not a
    // shifted neighbour, exactly as the dead host frame's status slots worked.
    //
    // The twelfth slot was that empty one until the brush wave, and the brush toggle took it
    // rather than pushing anything: TIC-80 stands its brush slider beside the canvas
    // (drawBrushSlider) and this column is the console's answer to that side of the screen, so
    // the control lands where the hand already goes for the size toggle. Nothing else moved by
    // a pixel — which is the whole reason the gap was left holding a name-shaped hole.
    private static readonly EditorButton?[] _toolSlots =
    {
        EditorButton.ToolSelect, EditorButton.ToolPencil,
        EditorButton.ToolFill, EditorButton.ToolStamp,
        EditorButton.ToolShape, EditorButton.ToolTransform,
        EditorButton.SizeToggle, EditorButton.Clear,
        EditorButton.Save, EditorButton.Undo,
        EditorButton.Redo, EditorButton.BrushToggle,
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

    /// <summary>All 23 placed buttons — the frame's six, the tool column's twelve and the five layer tabs.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The zoomed region view — the surface the pencil paints on. Always 64x64.</summary>
    public Rectangle Canvas { get; private init; }

    /// <summary>Console pixels per region pixel on the canvas: 8, 4 or 2 for an 8, 16 or 32 px sprite.</summary>
    public int CanvasScale { get; private init; }

    /// <summary>
    /// The one-pixel ring immediately <b>outside</b> the canvas box — the surface's own border.
    /// It exists because of a defect the owner found with his eyes on 2026-08-25: an empty
    /// sprite is colour 0, the screen's ground is colour 0
    /// (<see cref="ConsoleChromeRenderer.Ink"/>), so the canvas of a fresh cart was a black
    /// square on black and an author could not see where the surface he draws on begins or ends.
    ///
    /// <para><b>The reference does exactly this, and it is worth being precise about how.</b>
    /// TIC-80 (<c>src/studio/editors/sprite.c</c>) separates canvas from background twice over:
    /// its editors clear the screen to a grey a black sprite pixel can never be mistaken for,
    /// and <c>drawCanvas</c> then lays a one-pixel <c>rectb</c> around the 64x64 box — at
    /// <c>x - 1, y - 1, CANVAS_SIZE + 2, CANVAS_SIZE + 2</c>, i.e. <em>around</em> it, never on
    /// the sprite's own pixels. We take the second half exactly and cannot take the first: this
    /// console has sixteen slots, the palette on this very screen must show all sixteen
    /// truthfully, and repainting the ground would spend one of them (the argument is
    /// <see cref="ConsoleChromeRenderer"/>'s). The ring is drawn in
    /// <see cref="ConsoleChromeRenderer.Dim"/> and not in TIC-80's white, which is a named
    /// divergence: on this chrome grey is the ink every structural rule is drawn in (the three
    /// bands, the map screen's <c>DrawGrid</c>) and white already means "the cell the cursor is
    /// on" — a white ring here would compete with the canvas cursor, and the order of
    /// 2026-08-25 says the cursor is not to be touched. LIKO-12 and PICO-8 agree with TIC-80 on
    /// the principle (a canvas that is a distinct plate, not the background) but neither's
    /// mechanism is transferable at 160x90: LIKO-12 stands its canvas on a filled editor
    /// background, PICO-8 offers grid lines <em>over</em> the zoomed pixels (<c>CTRL-G</c>,
    /// REFERENCES-EDITORS §2.3) and lines over the art are exactly what this fix may not do.
    /// Where they diverge, TIC-80 wins, per the order.</para>
    ///
    /// <para><b>Where the pixels come from, named because the order asked for it.</b> The
    /// across-the-screen split is 20 + 64 + 20 + 56 = 160 with nothing spare, so the ring lands
    /// on the ring of pixels just outside the box and each of its four sides is accounted for.
    /// <b>Top:</b> the header rule, already drawn on that row — the frame coincides with it and
    /// costs nothing. <b>Left:</b> the tool column's own button borders for the sixty rows the
    /// twelve buttons cover, and free ground for the four rows below them. <b>Right:</b> the one
    /// genuinely free pixel on this screen — the middle column is allotted twenty pixels and its
    /// palette and flag blocks need nineteen, so <see cref="Compute"/> now right-aligns those
    /// blocks and the spare pixel moves from the useless right side of the column to the useful
    /// left side of it; below them the layer tabs' own left borders continue the line.
    /// <b>Bottom:</b> free, because the three slider rows under the content are spanned only by
    /// the sheet's columns. <b>Nothing was taken from the drawing surface</b>: the canvas is
    /// still 64x64 at zoom 8, which is the 8x8 sprite the order says not to shrink.</para>
    /// </summary>
    public Rectangle CanvasFrame =>
        new(Canvas.X - 1, Canvas.Y - 1, Canvas.Width + 2, Canvas.Height + 2);

    /// <summary>
    /// The sheet window: seven whole sprite columns wide and the strip's full
    /// <see cref="SheetStrip.Rows"/> tall. The presentation strip draws inside it at
    /// <see cref="SheetScale"/>, shifted by the scroll offset; what does not fit horizontally is
    /// what <see cref="SheetSlider"/> reaches.
    /// </summary>
    public Rectangle Sheet { get; private init; }

    /// <summary>Console pixels per strip pixel — one, and one is the only value 64 rows allow.</summary>
    public int SheetScale { get; private init; }

    /// <summary>
    /// The same ring around the sheet window. TIC-80 frames its 128x128 sheet the same way
    /// (<c>drawSheetVBank1</c>) and even hangs the neighbouring-page marks off that frame, which
    /// is the one detail REFERENCES-EDITORS §2.1 records about it in so many words: "наличие
    /// соседних страниц показано штрихами по бокам <b>рамки листа</b>". So the sheet has a
    /// border in the reference and ours had none.
    ///
    /// <para><b>Three of the four sides are already paid for.</b> Above it is the header rule;
    /// below it is the slider's own track, which spans exactly these columns; on the left is the
    /// middle column's last pixel, which the palette and flag blocks now sit flush against. The
    /// fourth side is off the screen and stays off it: TIC-80 buys its sheet's right border by
    /// standing the sheet one pixel in from the screen edge (<c>SheetX = TIC80_WIDTH -
    /// TIC_SPRITESHEET_SIZE - 1</c>) and we cannot follow it there — fifty-six columns is exactly
    /// seven whole sprite cells, and a pixel taken from them costs a whole column of sprites,
    /// which is the "useful area" the order forbids spending. The rectangle therefore names a
    /// side at x = 160 and <c>VirtualConsole.Rect</c> clips it away, which is the honest outcome:
    /// a border drawn one column in would sit on sprite art, and rule 3 of the order says
    /// nothing may.</para>
    /// </summary>
    public Rectangle SheetFrame =>
        new(Sheet.X - 1, Sheet.Y - 1, Sheet.Width + 2, Sheet.Height + 2);

    /// <summary>The horizontal scroll slider's track, directly under the sheet window.</summary>
    public Rectangle SheetSlider { get; private init; }

    /// <summary>Side of one palette swatch, in console pixels.</summary>
    public int SwatchSize { get; private init; }

    /// <summary>Bounding box of all 16 swatches — the renderer walks it, the hit test pre-filters with it.</summary>
    public Rectangle Swatches { get; private init; }

    /// <summary>Side of one flag toggle's cell — a swatch, so the two blocks share one grid and one edge (the column's right one; see <see cref="CanvasFrame"/>).</summary>
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
        var buttons = new EditorButtonPlace[24];
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
        // tabs under those — one clear row between them, and all three flush against the
        // column's RIGHT edge rather than its left one.
        //
        // The right edge, not the left, and this is the whole of the answer to "where did the
        // border's pixel come from" (see CanvasFrame). The column is two buttons — twenty
        // pixels — because the five layer tabs must fit two abreast, and twenty is what the
        // 20 + 64 + 20 + 56 split leaves it. But the palette is four cells of four plus three
        // gaps of one = NINETEEN, and so is the flag block; their twentieth pixel existed and
        // was spent on nothing. Left-aligning the blocks parked that pixel on the right of the
        // column, where it did nothing; right-aligning them parks it on the LEFT, where it is
        // the one column of ground between the canvas and this panel — the canvas's own border.
        // Nothing was taken from any control to get it: the palette cells, the flag cells, the
        // layer tabs, the canvas and the sheet window are all exactly the size they were.
        int middleWidth = MiddleColumns * button;
        int middleX = toolWidth + canvasBox;
        int swatchSize = CellSize;
        int swatchesWidth = SwatchColumns * CellPitch - 1;
        int flagsWidth = FlagColumns * CellPitch - 1;
        var swatches = new Rectangle(
            middleX + middleWidth - swatchesWidth, top,
            swatchesWidth, SwatchRows * CellPitch - 1);
        var flagPanel = new Rectangle(
            middleX + middleWidth - flagsWidth, swatches.Bottom + 1,
            flagsWidth, FlagRows * CellPitch - 1);
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
        int sheetX = middleX + middleWidth;
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
    ///
    /// <para>Kept as the square door onto <see cref="SheetBlockHighlights"/> because it is what
    /// a caller with one number in hand wants — the dim frame around sprite 0 is drawn through
    /// it, and so is every existing test. Both shapes come out of one body, so the block frame
    /// and the region frame cannot be measured differently.</para>
    /// </summary>
    public IReadOnlyList<Rectangle> SheetRegionHighlights(
        int sheetCellX, int sheetCellY, int regionCells, int scroll) =>
        SheetBlockHighlights(sheetCellX, sheetCellY, regionCells, regionCells, scroll);

    /// <summary>
    /// Highlight rectangles for a canonical N×M block of the sheet — the free rectangle a drag
    /// across the sheet window marks (REFERENCES-EDITORS §8 item 3). Same lane-splitting rule as
    /// the square case above, which it generalizes: the block is one rectangle in the model and
    /// may be several in the strip's presentation of it.
    /// </summary>
    public IReadOnlyList<Rectangle> SheetBlockHighlights(
        int sheetCellX, int sheetCellY, int width, int height, int scroll)
    {
        var highlights = new List<Rectangle>(2);
        int sheetEndY = sheetCellY + height;
        int cell = SheetScale * VirtualConsole.SpriteSize;
        for (int y = sheetCellY; y < sheetEndY;)
        {
            int laneEndY = Math.Min(sheetEndY, (y / SheetStrip.Rows + 1) * SheetStrip.Rows);
            SheetStrip.SpriteToStripCell(
                y * SheetStrip.LaneColumns + sheetCellX, out int stripColumn, out int stripRow);
            var piece = new Rectangle(
                Sheet.X + (stripColumn * VirtualConsole.SpriteSize - scroll) * SheetScale,
                Sheet.Y + stripRow * cell,
                width * cell,
                (laneEndY - y) * cell);
            highlights.Add(piece);
            y = laneEndY;
        }
        return highlights;
    }

    /// <summary>
    /// Console point to the nearest strip cell of the sheet window — the block drag's twin of
    /// <see cref="ClampCanvasPixel"/>, and there for the same reason: a drag whose pointer
    /// leaves the window must keep sizing the block along its edge instead of freezing or
    /// tearing. Floored, not truncated, so a pointer left of the window counts as its first
    /// visible column rather than sticking a whole cell wide of it.
    ///
    /// <para>Resolved against <paramref name="scroll"/> — the very offset the strip is drawn at
    /// — because this window scrolls horizontally where the map screen's palette does not. A
    /// clamp that ignored it would mark the sprites the author is not looking at.</para>
    /// </summary>
    public void ClampSheetStripCell(int x, int y, int scroll, out int column, out int row)
    {
        int cell = VirtualConsole.SpriteSize * SheetScale;
        column = Math.Clamp(
            FloorDiv(x - Sheet.X + scroll * SheetScale, cell), 0, SheetStrip.Columns - 1);
        row = Math.Clamp(FloorDiv(y - Sheet.Y, cell), 0, SheetStrip.Rows - 1);
    }

    /// <summary>Division that floors toward minus infinity — C# truncates toward zero, and a pointer one pixel left of the window must be its first cell, not its zeroth twice.</summary>
    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

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

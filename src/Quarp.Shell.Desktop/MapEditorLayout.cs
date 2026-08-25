using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where everything on the <b>map</b> editor screen sits, in <b>console pixels</b> — 160x90 on
/// profile 8. Wave R3 moved this screen onto the console (ADR-029) exactly as wave R2 moved the
/// sprite screen, and this struct is the whole of the geometry that moved: every coordinate
/// that used to be derived from the window size and <c>PixelFontMetrics.UiScale</c> is now a
/// fixed number on the console's own grid, the way TIC-80 writes <c>MAP_Y = TOOLBAR_SIZE</c>
/// and means row 7 of a 136-row screen. It stays the geometry's <b>single owner</b>:
/// <see cref="MapEditorRenderer"/> draws these rectangles and <see cref="MapEditorInput"/>
/// hit-tests the pointer against the same ones.
///
/// <para>The shared frame — the top band, the three rules, the exit button, the five editor
/// tabs, the tooltip field, the message line and its clickable verbs — is measured by
/// <see cref="ConsoleChrome"/> and only forwarded here, the same frame the sprite screen stands
/// in. There is no third chrome.</para>
///
/// <para><b>THE ARITHMETIC, in full, because on this screen it decided the whole design.</b>
/// <see cref="ConsoleChrome"/> leaves <b>64 rows by 160 columns</b> of content (10 top band + 1
/// rule + 64 content + 3 slider + 1 rule + 5 message + 1 rule + 5 status = 90). A map cell is
/// 8x8 and never magnified, so those 64 rows are <b>eight map rows</b> and nothing anyone
/// arranges can make them nine. Across the 160 columns: eleven buttons at 10x10 need two
/// columns and six rows (20 px wide, 60 tall — it fits the 64 with four rows to spare, where
/// one column would have needed 110 and a horizontal row would have cost two whole map rows),
/// four columns of gutter, and the remaining <b>136 — seventeen whole map cells</b> — for the
/// viewport. Twenty plus four plus one hundred and thirty-six is one hundred and sixty.</para>
///
/// <para><b>What could NOT be kept on screen, with the numbers that ruled it out.</b>
/// <list type="number">
/// <item><description><b>The tile palette.</b> 256 tiles at 8x8 are 16 384 pixels; the whole
/// console is 14 400. It is not a layout problem and no arrangement solves it — the palette
/// cannot be on screen with anything else, and cannot even be on screen whole. So it became an
/// overlay of <b>one page</b>: a <see cref="SheetStrip"/> lane is 16 cells by 8 = 128x64, which
/// is exactly the content band's height and fits inside the viewport's width. Two pages hold
/// all 256, and which page is shown is not state — it is <em>derived</em> from the selected
/// tile (<see cref="PaletteLane"/>), so the palette always contains the tile in hand and
/// nothing can leave the two out of step. TIC-80 answers the same wall the same way
/// (<c>drawSheetButton</c>, "SHOW TILES [shift]"), with the sheet whole because its screen is
/// 80 px wider.</description></item>
/// <item><description><b>The minimap.</b> One pixel per cell is 256x72 — wider than the whole
/// console. At <see cref="MinimapCellsPerPixel"/> = 2 it is 128x36, which fits the content band
/// with room, and that is what the whole-map mode shows: TIC-80's <c>world.c</c>, reached by
/// Tab or its button. It could not stay a panel — 36 of 64 rows is more than half the content
/// band, and what was left would not have been a map viewport.</description></item>
/// </list>
/// Nothing was dropped: every tool, the eraser, the grid, the palette, the block stamp, the
/// clipboard and the minimap are all still reachable, two of them through a key and a button
/// instead of a permanent panel. <see cref="MapEditorRenderer"/>'s type comment lists them.</para>
///
/// <para><b>Every scale is one.</b> The map draws its 8-px tiles at 1:1, the palette its at
/// 1:1, and the minimap folds two cells into a pixel. There is no fractional scale on this
/// screen and no path that can produce one (ARCHITECTURE §5); the window's only say is the
/// whole-integer factor <see cref="FramePlacement"/> presents the finished frame at.</para>
/// </summary>
public readonly struct MapEditorLayout
{
    /// <summary>Map columns — borrowed from the session, which borrows it from <c>CartData</c>. No second owner of the map's size.</summary>
    public const int MapColumns = MapEditorSession.MapColumns;

    /// <summary>Map rows, same chain of ownership.</summary>
    public const int MapRows = MapEditorSession.MapRows;

    /// <summary>
    /// How many map cells share one minimap pixel. Two, and the number is forced: the map is
    /// 256 cells wide and the console is 160 pixels wide, so one-to-one cannot show the whole
    /// map however the panel is placed. A minimap pixel is therefore lit when <em>any</em> cell
    /// of its 2x2 block is occupied — the thumbnail answers "where is there anything, and where
    /// am I", which is the question <c>world.c</c> answers too, and it answers it about the
    /// whole map rather than about 62 % of it.
    /// </summary>
    public const int MinimapCellsPerPixel = 2;

    /// <summary>Columns of icon-buttons left of the viewport.</summary>
    private const int ToolColumns = 2;

    /// <summary>Clear pixels between the tool block and the viewport — the only air on this screen, and it is what separates them.</summary>
    private const int Gutter = 4;

    // The tool block in reading order, left to right and down. The four map modes first, in the
    // order their digit keys run (REFERENCES-EDITORS §3.1: 1 draw, 2 drag, 3 select, 4 fill), so
    // the block and the keyboard read the same way; then the eraser and the grid switch; then
    // wave R3's two overlay switches; then the three the host frame kept in its status bar.
    // Those three moved for the reason the sprite screen's did: the console's status line is
    // five pixels tall and an icon-button is ten, and a band that cannot hold a button cannot
    // hold a button row. A null is an empty slot, not a shifted neighbour.
    private static readonly EditorButton?[] _toolSlots =
    {
        EditorButton.ToolPencil, EditorButton.ToolHand,
        EditorButton.ToolSelect, EditorButton.ToolFill,
        EditorButton.ToolEraser, EditorButton.GridToggle,
        EditorButton.TilesToggle, EditorButton.WorldToggle,
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

    /// <summary>The status band: the cursor's cell at the left, the tile number at the right.</summary>
    public Rectangle StatusBar => Chrome.StatusBar;

    /// <summary>Glyph top of the single message line — the exit prompt, the save error or the standing notice.</summary>
    public int PromptY => Chrome.MessageY;

    /// <summary>What stands over the viewport in the frame this layout was measured for.</summary>
    public MapEditorOverlay Overlay { get; private init; }

    /// <summary>
    /// Which page of the tile palette is on show: the <see cref="SheetStrip"/> lane holding the
    /// selected tile. Derived, never stored — see the type comment for why the palette has no
    /// page state of its own.
    /// </summary>
    public int PaletteLane { get; private init; }

    /// <summary>The seventeen placed buttons — the frame's six and the tool block's eleven.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The map viewport — 17x8 whole cells, the surface the pencil paints on.</summary>
    public Rectangle Canvas { get; private init; }

    /// <summary>Console pixels per <b>map pixel</b>. One, always, on a console this size.</summary>
    public int MapScale => 1;

    /// <summary>Console pixels per map cell — the number every canvas hit test divides by.</summary>
    public int MapCell => VirtualConsole.SpriteSize * MapScale;

    /// <summary>How many map columns the viewport shows. Whole by construction: the canvas is trimmed to cells.</summary>
    public int VisibleColumns => Canvas.Width / MapCell;

    /// <summary>How many map rows the viewport shows.</summary>
    public int VisibleRows => Canvas.Height / MapCell;

    /// <summary>The camera's ceiling, in map cells — the shared clamp of every writer of the view position.</summary>
    public int MaxCameraX => Math.Max(0, MapColumns - VisibleColumns);

    /// <summary>The camera's vertical ceiling.</summary>
    public int MaxCameraY => Math.Max(0, MapRows - VisibleRows);

    /// <summary>
    /// The tile palette's page, or <see cref="Rectangle.Empty"/> when the palette is not up.
    /// Empty rather than "the rectangle it would have": every hit test on this screen starts
    /// with <c>Contains</c>, and an empty rectangle contains nothing, so a lowered palette is
    /// deaf without a single extra branch in the router.
    /// </summary>
    public Rectangle Sheet { get; private init; }

    /// <summary>Console pixels per strip pixel in the palette — one, the only value 64 rows allow.</summary>
    public int SheetScale => 1;

    /// <summary>
    /// The whole map at <see cref="MinimapCellsPerPixel"/> cells to the pixel, or
    /// <see cref="Rectangle.Empty"/> outside the whole-map mode. Same discipline as
    /// <see cref="Sheet"/>.
    /// </summary>
    public Rectangle Minimap { get; private init; }

    /// <summary>
    /// The horizontal position bar under the viewport, in the three rows
    /// <see cref="ConsoleChrome.SliderY"/> reserves. It is the mouse's short road across a map
    /// eighty screens wide without leaving the working view for the whole-map mode: the thumb
    /// says where the viewport is, and a press anywhere on the track travels there through the
    /// same <see cref="MapEditorView.JumpTo"/> the minimap uses — one verb, two roads.
    /// </summary>
    public Rectangle Slider { get; private init; }

    /// <summary>
    /// The screen's geometry for a console of the given size, the given overlay and the given
    /// selected tile. The two sizes are <b>console</b> pixels — 160x90 on profile 8 — and never
    /// a window size.
    /// </summary>
    public static MapEditorLayout Compute(
        int screenWidth, int screenHeight, MapEditorOverlay overlay, int selectedSprite)
    {
        var buttons = new EditorButtonPlace[17];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(screenWidth, screenHeight, buttons, ref placed);

        int button = ConsoleChrome.ButtonSize;
        int top = chrome.ContentTop;

        // The tool block: two buttons wide, hard against the left edge, growing downward only.
        // Its own button frames are the rule that separates it from the gutter.
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

        // The viewport takes every column the tool block and the gutter left, trimmed to whole
        // cells so its edge can never show a sliced tile, and every row of the content band.
        int canvasX = ToolColumns * button + Gutter;
        int cell = VirtualConsole.SpriteSize;
        int columns = Math.Clamp((screenWidth - canvasX) / cell, 1, MapColumns);
        int rows = Math.Clamp(chrome.ContentHeight / cell, 1, MapRows);
        var canvas = new Rectangle(canvasX, top, columns * cell, rows * cell);

        // The palette page: one lane of the strip, centred over the viewport. Centred rather
        // than flush so the eye reads it as something lying ON the map, which is what it is.
        int lane = Math.Clamp(selectedSprite, 0, VirtualConsole.SpriteCount - 1)
            / (SheetStrip.LaneColumns * SheetStrip.Rows);
        int pageWidth = SheetStrip.LaneColumns * cell;
        var sheet = overlay == MapEditorOverlay.Tiles
            ? new Rectangle(
                canvas.X + (canvas.Width - pageWidth) / 2, top, pageWidth, SheetStrip.PixelHeight)
            : Rectangle.Empty;

        // The whole-map view: centred over the viewport, both axes. Over the VIEWPORT and not
        // over the whole screen, and that is not taste — the tool block stands at the left edge
        // and its buttons are tested before any panel is, so a thumbnail centred on the screen
        // would put four of its columns under buttons and make those cells unclickable. Centring
        // it where the map was keeps every pixel of it live and puts it where the eye already is.
        int minimapWidth = MapColumns / MinimapCellsPerPixel;
        int minimapHeight = MapRows / MinimapCellsPerPixel;
        var minimap = overlay == MapEditorOverlay.World
            ? new Rectangle(
                canvas.X + (canvas.Width - minimapWidth) / 2,
                top + (chrome.ContentHeight - minimapHeight) / 2,
                minimapWidth,
                minimapHeight)
            : Rectangle.Empty;

        return new MapEditorLayout
        {
            Chrome = chrome,
            Overlay = overlay,
            PaletteLane = lane,
            Buttons = buttons,
            Canvas = canvas,
            Sheet = sheet,
            Minimap = minimap,
            Slider = new Rectangle(
                canvas.X, chrome.SliderY, canvas.Width, ConsoleChrome.SliderHeight),
        };
    }

    /// <summary>The working state's geometry — nothing over the map, tile 0 in hand.</summary>
    public static MapEditorLayout Compute(int screenWidth, int screenHeight) =>
        Compute(screenWidth, screenHeight, MapEditorOverlay.None, 0);

    /// <summary>
    /// True when a point on the viewport means a map cell. False while anything stands over the
    /// map: the palette covers most of the viewport and the whole-map view covers all of it, and
    /// a click that painted a tile through an overlay would be the exact bug the overlay's own
    /// rectangle is supposed to prevent.
    /// </summary>
    public bool CanvasLive => Overlay == MapEditorOverlay.None;

    /// <summary>The placed rectangle of one button — the hover frame anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id) => ConsoleChrome.ButtonRect(Buttons, id);

    /// <summary>The 8x8 mask's destination inside a button.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => ConsoleChrome.ButtonIconRect(buttonRect);

    /// <summary>Console point to the button under it, stubs included (hover needs the dead ones too).</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        ConsoleChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="ConsoleChrome"/> owns the message line.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Console point to a prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    /// <summary>
    /// Console point to map cell, given where the camera stands, or false when the point is off
    /// the viewport or an overlay owns the screen. The camera is added AFTER the division, so
    /// the answer is a cell of the whole 256x72 map and never of the window — which is what lets
    /// the session keep throwing on out-of-map coordinates instead of masking them.
    /// </summary>
    public bool TryMapCell(int x, int y, int cameraX, int cameraY, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        if (!CanvasLive || !Canvas.Contains(x, y))
        {
            return false;
        }
        cellX = cameraX + (x - Canvas.X) / MapCell;
        cellY = cameraY + (y - Canvas.Y) / MapCell;
        return cellX < MapColumns && cellY < MapRows;
    }

    /// <summary>
    /// Console point to nearest visible map cell, for drags: a stroke whose pointer leaves the
    /// viewport keeps painting along its edge instead of tearing, exactly as
    /// <see cref="SpriteEditorLayout.ClampCanvasPixel"/> does for the sprite canvas.
    /// </summary>
    public void ClampMapCell(int x, int y, int cameraX, int cameraY, out int cellX, out int cellY)
    {
        cellX = Math.Clamp(
            cameraX + (x - Canvas.X) / MapCell, cameraX, Math.Min(MapColumns, cameraX + VisibleColumns) - 1);
        cellY = Math.Clamp(
            cameraY + (y - Canvas.Y) / MapCell, cameraY, Math.Min(MapRows, cameraY + VisibleRows) - 1);
    }

    /// <summary>The console rectangle of one map cell — the single mapping the cursor frame and the tiles share.</summary>
    public Rectangle MapCellRect(int cellX, int cellY, int cameraX, int cameraY) =>
        MapAreaRect(cellX, cellY, 1, 1, cameraX, cameraY);

    /// <summary>
    /// The console rectangle of a block of cells — <see cref="MapCellRect"/> generalized, so the
    /// selection outline and one cell are drawn from one formula. Deliberately not clipped to
    /// the canvas: the caller draws a frame, and a rectangle whose corners were pulled inside
    /// the viewport would claim the selection ends where the screen does.
    /// </summary>
    public Rectangle MapAreaRect(int cellX, int cellY, int width, int height, int cameraX, int cameraY) =>
        new(Canvas.X + (cellX - cameraX) * MapCell,
            Canvas.Y + (cellY - cameraY) * MapCell,
            width * MapCell,
            height * MapCell);

    /// <summary>
    /// How many cells right of the viewport's left edge a console x lies — the pan gesture's
    /// whole arithmetic. Floored rather than truncated, so a pointer dragged left of the canvas
    /// keeps counting downwards instead of sticking at 0 for a whole cell: C# division rounds
    /// toward zero, and a pan that stalls at the edge is exactly what that would look like.
    /// </summary>
    public int CanvasColumnOffset(int x) => FloorDiv(x - Canvas.X, MapCell);

    /// <summary>The same for a console y and the viewport's top edge.</summary>
    public int CanvasRowOffset(int y) => FloorDiv(y - Canvas.Y, MapCell);

    /// <summary>
    /// The vertical grid line before visible column <paramref name="column"/> (0 is the
    /// canvas's own left edge), one console pixel wide. Geometry lives here, with every other
    /// rectangle the renderer draws and the mouse is tested against.
    /// </summary>
    public Rectangle GridColumnLine(int column, int thickness) =>
        new(Canvas.X + column * MapCell, Canvas.Y, thickness, Canvas.Height);

    /// <summary>The horizontal grid line above visible row <paramref name="row"/>.</summary>
    public Rectangle GridRowLine(int row, int thickness) =>
        new(Canvas.X, Canvas.Y + row * MapCell, Canvas.Width, thickness);

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    /// <summary>
    /// Console point to a sprite number in the palette, or false — off the page, or the palette
    /// is not up. The strip's lane arithmetic is delegated to <see cref="SheetStrip"/>: the
    /// renderer and the mouse must not own competing versions of it, and the page's own offset
    /// is added <em>in strip columns</em> so the two never meet in pixel space at all.
    /// </summary>
    public bool TryTileCell(int x, int y, out int sprite)
    {
        sprite = 0;
        if (!TryTileStripCell(x, y, out int column, out int row)
            || !SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY))
        {
            return false;
        }
        sprite = sheetY * SheetStrip.LaneColumns + sheetX;
        return true;
    }

    /// <summary>
    /// The palette cell of one sprite, or <see cref="Rectangle.Empty"/> when that sprite is on
    /// the other page (or the palette is down). Empty rather than a plausible-looking rectangle
    /// on purpose: a page shows 128 of the 256 tiles, and a rectangle for an absent tile would
    /// be a frame drawn around some other tile's art.
    /// </summary>
    public Rectangle TileCellRect(int sprite) => TileBlockRect(sprite, 1, 1);

    /// <summary>
    /// The palette rectangle of a block anchored at one sprite — <see cref="TileCellRect"/>
    /// generalized, so the block frame and the single-tile frame come out of one formula (at
    /// 1x1 this <em>is</em> the cell frame). Not clipped to the page: the caller draws a frame,
    /// and a rectangle pulled inside the box would claim the block ends where the page does.
    /// </summary>
    public Rectangle TileBlockRect(int sprite, int width, int height)
    {
        if (Sheet.IsEmpty)
        {
            return Rectangle.Empty;
        }
        SheetStrip.SpriteToStripCell(sprite, out int column, out int row);
        int lane = column / SheetStrip.LaneColumns;
        if (lane != PaletteLane)
        {
            return Rectangle.Empty;
        }
        int cell = VirtualConsole.SpriteSize * SheetScale;
        return new Rectangle(
            Sheet.X + (column - PaletteLane * SheetStrip.LaneColumns) * cell,
            Sheet.Y + row * cell,
            width * cell,
            height * cell);
    }

    /// <summary>
    /// Console point to a <b>strip</b> cell of the palette, or false off the page. The block
    /// drag is measured in strip cells and not in sprite numbers because the rectangle the
    /// author drags is a rectangle <em>on screen</em>: the strip lays the sheet's pages side by
    /// side (<see cref="SheetStrip"/>), so two cells that touch in the palette need not be
    /// consecutive sprites, and only the strip's own coordinates can say what "the next column"
    /// means. <see cref="TryTileCell"/> stays the single-cell answer for everything that wants
    /// a sprite number.
    /// </summary>
    public bool TryTileStripCell(int x, int y, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (!Sheet.Contains(x, y))
        {
            return false;
        }
        int cell = VirtualConsole.SpriteSize * SheetScale;
        column = PaletteLane * SheetStrip.LaneColumns + (x - Sheet.X) / cell;
        row = (y - Sheet.Y) / cell;
        return column < SheetStrip.Columns && row < SheetStrip.Rows;
    }

    /// <summary>
    /// Console point to the nearest strip cell <b>of the page on show</b> — the block drag's
    /// twin of <see cref="ClampMapCell"/>, and there for the same reason: a drag whose pointer
    /// leaves the palette must keep sizing the block along its edge instead of freezing or
    /// tearing. Clamped to the page rather than to the whole strip, because a block that ran off
    /// the page would have cells the author cannot see. Floored, not truncated, so a pointer
    /// left of the page counts as its first column rather than sticking a whole cell wide of it.
    /// </summary>
    public void ClampTileStripCell(int x, int y, out int column, out int row)
    {
        int cell = VirtualConsole.SpriteSize * SheetScale;
        int first = PaletteLane * SheetStrip.LaneColumns;
        column = Math.Clamp(
            first + FloorDiv(x - Sheet.X, cell), first, first + SheetStrip.LaneColumns - 1);
        row = Math.Clamp(FloorDiv(y - Sheet.Y, cell), 0, SheetStrip.Rows - 1);
    }

    /// <summary>
    /// Console point to a map cell on the minimap, or false — off it, or the whole-map view is
    /// not up. A minimap pixel names the <b>first</b> of the <see cref="MinimapCellsPerPixel"/>
    /// cells it stands for, which is why the bottom-right pixel answers cell (254, 70) and not
    /// (255, 71): the two cells share the pixel and the travel verb centres the viewport on the
    /// answer anyway, so the far corner is on screen either way.
    /// </summary>
    public bool TryMinimapCell(int x, int y, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        if (!Minimap.Contains(x, y))
        {
            return false;
        }
        cellX = Math.Clamp((x - Minimap.X) * MinimapCellsPerPixel, 0, MapColumns - 1);
        cellY = Math.Clamp((y - Minimap.Y) * MinimapCellsPerPixel, 0, MapRows - 1);
        return true;
    }

    /// <summary>The viewport's outline on the minimap — drawn from the same camera the canvas is drawn from.</summary>
    public Rectangle MinimapViewport(int cameraX, int cameraY) =>
        new(Minimap.X + cameraX / MinimapCellsPerPixel,
            Minimap.Y + cameraY / MinimapCellsPerPixel,
            Math.Max(1, Math.Min(VisibleColumns, MapColumns) / MinimapCellsPerPixel),
            Math.Max(1, Math.Min(VisibleRows, MapRows) / MinimapCellsPerPixel));

    /// <summary>
    /// The position bar's thumb for a camera column: the visible share of the map's width, in
    /// the track's own pixels. The renderer draws this and <see cref="TrySliderColumn"/> reads
    /// the same linear mapping back, so the thumb can never sit where a press would not put it.
    /// The two-pixel floor keeps it visible when seventeen of 256 columns is under one pixel.
    /// </summary>
    public Rectangle SliderThumb(int cameraX) =>
        new(Slider.X + cameraX * Slider.Width / MapColumns,
            Slider.Y,
            Math.Max(2, VisibleColumns * Slider.Width / MapColumns),
            Slider.Height);

    /// <summary>
    /// Console point to the map column it names on the position bar, or false off the track.
    /// The column is handed to <see cref="MapEditorView.JumpTo"/> — the same verb the minimap's
    /// click uses — so the bar is a second road to one place and not a second travel rule.
    /// </summary>
    public bool TrySliderColumn(int x, int y, out int column)
    {
        column = 0;
        if (!Slider.Contains(x, y))
        {
            return false;
        }
        column = Math.Clamp((x - Slider.X) * MapColumns / Slider.Width, 0, MapColumns - 1);
        return true;
    }
}

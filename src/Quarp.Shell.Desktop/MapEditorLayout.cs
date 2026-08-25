using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where everything on the <b>map</b> editor screen sits, as a pure function of the window
/// size — the map's own <see cref="SpriteEditorLayout"/>, a separate type rather than a second
/// tenant in that struct, because the two screens share a chrome shape and not one rectangle:
/// the map canvas is a 256x72 grid seen through a window, the sprite canvas is a whole region
/// magnified. Same law as next door: this is the geometry's <b>single owner</b> —
/// <see cref="MapEditorRenderer"/> draws these rectangles and <c>QuarpGame</c> hit-tests the
/// mouse against the same ones, so nothing can be painted in one place and clicked in another.
///
/// <para>The shared frame — bands, button row, prompt line, margins — is measured by
/// <see cref="EditorChrome"/>. This file used to carry its own copy of that arithmetic and had
/// to borrow the sprite editor's <c>Compute</c> for the prompt verbs.</para>
///
/// <para><b>The shape is the shell standard (M9 stage 2.5, through the seventh review),
/// applied to a map.</b> Left of the canvas, one margin away: the tool column (the pencil).
/// Right of the canvas, at <b>the same margin</b> (seventh review — the drawing surface stands
/// between two mirrored strips of buttons): the second tool column (the eraser, which is
/// "tile 0", MAP-FORMAT §2). Under the canvas: the tile picker — the sprite sheet through
/// <see cref="SheetStrip"/>, the one owner of the strip presentation — and, filling the room
/// beside it, the minimap.</para>
///
/// <para><b>Why the picker is under the canvas and not in a right-hand column.</b> The strip
/// is 32 cells wide by 8 tall; a 4:1 band cannot be poured into a narrow column without
/// shrinking cells until fewer sprites fit — the very trade the owner rejected in his sixth
/// review of the sprite editor. The map canvas wants width for the same reason (the map is
/// 3.6:1), so both wide things get their own full-width row instead of fighting over one
/// column.</para>
///
/// <para><b>The picker never scrolls.</b> <see cref="SheetScale"/> is chosen so all 32 strip
/// columns fit the band, capped so the strip can never eat the map's height below
/// <see cref="MinMapRows"/>. At the shell's window sizes that shows every one of the 256 tiles
/// at once, which is why this screen has no sheet slider and no scroll offset — and therefore
/// no second copy of <see cref="SheetScroll"/>'s state living here. In a window too small for
/// scale 1 the strip is clipped, not crashed: the same floor, and the same carded debt
/// (tasks/open/debt-tiny-window-layout.md), the sprite editor already documents.</para>
///
/// <para><b>Every scale is a whole integer</b>, floored at 1 (ARCHITECTURE §5 applied to host
/// UI): the map draws its 8-px tiles at <see cref="MapScale"/>, the picker its at
/// <see cref="SheetScale"/>, the minimap one map cell per <see cref="MinimapScale"/> window
/// pixels, and icons are 8-px masks at <see cref="Ui"/>.</para>
/// </summary>
public readonly struct MapEditorLayout
{
    /// <summary>
    /// The map rows the canvas is never allowed to fall below when the picker takes its share
    /// of the height. The map is the subject of this screen and the picker is the accessory;
    /// without a floor stated here the strip's scale would grow with the window until the
    /// canvas was a letterbox.
    /// </summary>
    public const int MinMapRows = 8;

    /// <summary>Map columns — borrowed from the session, which borrows it from <c>CartData</c>. No second owner of the map's size.</summary>
    public const int MapColumns = MapEditorSession.MapColumns;

    /// <summary>Map rows, same chain of ownership.</summary>
    public const int MapRows = MapEditorSession.MapRows;

    // The status band's row, outermost first: redo, undo, save. There is no Clear here because
    // the map model has no "clear the region" verb to wire it to, and a button with nothing
    // behind it is the defect class the button contract closed.
    // The map has nothing to clear, so slot 0 — the sprite screen's Clear — stays empty and the
    // shared three keep the pixels the author's hand already knows.
    private static readonly EditorButton?[] _statusSlots =
    {
        null, EditorButton.Redo, EditorButton.Undo, EditorButton.Save,
    };

    /// <summary>The left tool column, top to bottom — the four map modes in their digit-key order (wave 3d).</summary>
    private static readonly EditorButton[] _toolColumn =
    {
        EditorButton.ToolPencil, EditorButton.ToolHand, EditorButton.ToolSelect, EditorButton.ToolFill,
    };

    /// <summary>The right column, top to bottom: the empty-tile button (level with the pencil) and the grid switch.</summary>
    private static readonly EditorButton[] _rightColumn =
    {
        EditorButton.ToolEraser, EditorButton.GridToggle,
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

    /// <summary>The fifteen placed buttons — six tabs, a tool column of four, a right column of two, three status buttons.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The map viewport — a whole number of map cells, the surface the pencil paints on.</summary>
    public Rectangle Canvas { get; private init; }

    /// <summary>Window pixels per <b>map pixel</b>. A map cell is <see cref="VirtualConsole.SpriteSize"/> of them.</summary>
    public int MapScale { get; private init; }

    /// <summary>Window pixels per map cell — the number every canvas hit test divides by.</summary>
    public int MapCell => VirtualConsole.SpriteSize * MapScale;

    /// <summary>How many map columns the viewport shows. Whole by construction: the canvas is trimmed to cells.</summary>
    public int VisibleColumns => Canvas.Width / MapCell;

    /// <summary>How many map rows the viewport shows.</summary>
    public int VisibleRows => Canvas.Height / MapCell;

    /// <summary>The camera's ceiling, in map cells — the shared clamp of every writer of the view position.</summary>
    public int MaxCameraX => Math.Max(0, MapColumns - VisibleColumns);

    /// <summary>The camera's vertical ceiling.</summary>
    public int MaxCameraY => Math.Max(0, MapRows - VisibleRows);

    /// <summary>The tile picker window: the whole <see cref="SheetStrip"/>, drawn at <see cref="SheetScale"/>.</summary>
    public Rectangle Sheet { get; private init; }

    /// <summary>Window pixels per strip pixel in the picker.</summary>
    public int SheetScale { get; private init; }

    /// <summary>The whole map at <see cref="MinimapScale"/> pixels per cell — the mouse's way to the far corner.</summary>
    public Rectangle Minimap { get; private init; }

    /// <summary>Window pixels per map cell on the minimap.</summary>
    public int MinimapScale { get; private init; }

    public static MapEditorLayout Compute(int width, int height)
    {
        var buttons = new EditorButtonPlace[15];
        int placed = 0;
        EditorChrome chrome = EditorChrome.Compute(width, height, buttons, ref placed, _statusSlots);

        int ui = chrome.Ui;
        int margin = chrome.Margin;
        int button = chrome.ButtonSize;
        int top = chrome.ContentTop;
        int bottom = chrome.ContentBottom;
        int canvasX = margin + button + margin;

        // The picker's scale, decided before the canvas because the canvas takes what is left.
        // Two ceilings, both whole: the whole strip must fit the band's width, and the band
        // must not push the map below MinMapRows.
        int mapScale = ui;
        int mapCell = VirtualConsole.SpriteSize * mapScale;
        int stripRoom = width - canvasX - margin;
        int bandRoom = bottom - top - MinMapRows * mapCell - 2 * ui;
        // The cap is the interesting one: a picker cell is <b>half</b> a map cell. On the map a
        // tile has to be comfortable to place; in the picker it only has to be recognisable,
        // and halving it hands the map back half a screen of height. Without the cap the strip
        // grows with the window until the picker is taller than the map it serves — measured,
        // not feared: at 2560x1440 the uncapped scale is 7 and the map is left with the eight
        // rows of MinMapRows. With it, every window from 640x360 up shows the same 35x11 cells.
        int sheetScale = Math.Clamp(
            Math.Max(1, Math.Min(stripRoom / SheetStrip.PixelWidth, bandRoom / SheetStrip.PixelHeight)),
            1,
            Math.Max(1, mapScale / 2));
        int sheetHeight = SheetStrip.PixelHeight * sheetScale;
        var sheet = new Rectangle(
            canvasX, bottom - sheetHeight, SheetStrip.PixelWidth * sheetScale, sheetHeight);

        // The canvas: whole cells, flanked by two button columns one margin away on each side.
        int roomWidth = width - canvasX - margin - button - margin;
        int roomHeight = sheet.Y - 2 * ui - top;
        int columns = Math.Clamp(roomWidth / mapCell, 1, MapColumns);
        int rows = Math.Clamp(roomHeight / mapCell, 1, MapRows);
        var canvas = new Rectangle(canvasX, top, columns * mapCell, rows * mapCell);

        // The two mirrored tool columns. The left one is the toolbar of the shell standard —
        // since wave 3d it holds all four of TIC-80's map modes, top to bottom in the order
        // their digit keys run (REFERENCES-EDITORS §3.1: 1 draw, 2 drag, 3 select, 4 fill), so
        // the column and the keyboard read the same way down. The right one is the seventh
        // review's second column, at the identical gap: the empty-tile button first — it must
        // stay level with the pencil, which is the symmetry the layout test pins — and the
        // grid switch under it.
        //
        // Both columns grow DOWNWARD only. Widening either would move the canvas edges and
        // change how much map is on screen, which is a number the owner judges by eye and a
        // theory pins at 35x11 for every working window size.
        for (int i = 0; i < _toolColumn.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _toolColumn[i],
                Rect = new Rectangle(margin, top + i * (button + chrome.Gap), button, button),
            };
        }
        for (int i = 0; i < _rightColumn.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _rightColumn[i],
                Rect = new Rectangle(
                    canvas.Right + margin, top + i * (button + chrome.Gap), button, button),
            };
        }

        // The minimap fills the room beside the picker — the pocket of air the sixth and
        // seventh reviews both refused to leave anywhere on an editor screen — and earns it:
        // it is the mouse's path to any cell of a map eighty screens wide (a click jumps
        // there), and the one place the author can see where the viewport is.
        int minimapRoomX = sheet.Right + margin;
        int minimapRoomWidth = width - margin - minimapRoomX;
        int minimapScale = Math.Max(
            1, Math.Min(minimapRoomWidth / MapColumns, sheet.Height / MapRows));
        int minimapWidth = MapColumns * minimapScale;
        int minimapHeight = MapRows * minimapScale;
        // Left-aligned against the picker, not centred in what is left (2026-08-25, the
        // organizer's own eye pass on a live window). Centring split the leftover width into
        // two pockets and put one of them BETWEEN the two boxes, which reads as a hole in the
        // middle of the band — the very thing the sixth and seventh reviews threw out of the
        // sprite screen. Left-aligned, the whole remainder sits at the window's right edge,
        // where the eye reads it as a margin.
        var minimap = new Rectangle(
            minimapRoomX,
            sheet.Y + (sheet.Height - minimapHeight) / 2,
            minimapWidth,
            minimapHeight);

        return new MapEditorLayout
        {
            Chrome = chrome,
            Buttons = buttons,
            Canvas = canvas,
            MapScale = mapScale,
            Sheet = sheet,
            SheetScale = sheetScale,
            Minimap = minimap,
            MinimapScale = minimapScale,
        };
    }

    /// <summary>The placed rectangle of one button — the tooltip anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id) => EditorChrome.ButtonRect(Buttons, id);

    /// <summary>The icon's destination inside a button: centred, at scale <see cref="Ui"/>.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => Chrome.ButtonIconRect(buttonRect);

    /// <summary>Window point → button under it, stubs included (hover needs the dead ones too).</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        EditorChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="EditorChrome"/> owns the prompt line, for both screens.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Window point → prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    /// <summary>
    /// Window point → map cell, given where the camera stands, or false when the point is off
    /// the viewport. The camera is added AFTER the division, so the answer is a cell of the
    /// whole 256x72 map and never of the window — which is what lets the session keep throwing
    /// on out-of-map coordinates instead of masking them.
    /// </summary>
    public bool TryMapCell(int x, int y, int cameraX, int cameraY, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        if (!Canvas.Contains(x, y))
        {
            return false;
        }
        cellX = cameraX + (x - Canvas.X) / MapCell;
        cellY = cameraY + (y - Canvas.Y) / MapCell;
        return cellX < MapColumns && cellY < MapRows;
    }

    /// <summary>
    /// Window point → nearest visible map cell, for drags: a stroke whose pointer leaves the
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

    /// <summary>The window rectangle of one map cell — the single mapping the cursor frame and the tiles share.</summary>
    public Rectangle MapCellRect(int cellX, int cellY, int cameraX, int cameraY) =>
        MapAreaRect(cellX, cellY, 1, 1, cameraX, cameraY);

    /// <summary>
    /// The window rectangle of a block of cells — <see cref="MapCellRect"/> generalized, so the
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
    /// How many cells right of the viewport's left edge a window x lies — the pan gesture's
    /// whole arithmetic. Floored rather than truncated, so a pointer dragged left of the canvas
    /// keeps counting downwards instead of sticking at 0 for a whole cell: C# division rounds
    /// toward zero, and a pan that stalls at the edge is exactly what that would look like.
    /// </summary>
    public int CanvasColumnOffset(int x) => FloorDiv(x - Canvas.X, MapCell);

    /// <summary>The same for a window y and the viewport's top edge.</summary>
    public int CanvasRowOffset(int y) => FloorDiv(y - Canvas.Y, MapCell);

    /// <summary>
    /// The vertical grid line before visible column <paramref name="column"/> (0 is the
    /// canvas's own left edge), one window pixel per <see cref="Ui"/> wide. Geometry lives
    /// here, with every other rectangle the renderer draws and the mouse is tested against.
    /// </summary>
    public Rectangle GridColumnLine(int column, int thickness) =>
        new(Canvas.X + column * MapCell, Canvas.Y, thickness, Canvas.Height);

    /// <summary>The horizontal grid line above visible row <paramref name="row"/>.</summary>
    public Rectangle GridRowLine(int row, int thickness) =>
        new(Canvas.X, Canvas.Y + row * MapCell, Canvas.Width, thickness);

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    /// <summary>
    /// Window point → sprite number in the picker, or false. The inverse of the strip mapping
    /// is delegated to <see cref="SheetStrip"/> — the renderer and the mouse must not own
    /// competing versions of the lane formula, and the sprite editor's picker does the same.
    /// </summary>
    public bool TryTileCell(int x, int y, out int sprite)
    {
        sprite = 0;
        if (!Sheet.Contains(x, y))
        {
            return false;
        }
        int cell = VirtualConsole.SpriteSize * SheetScale;
        if (!SheetStrip.TryStripCellToSheetCell(
                (x - Sheet.X) / cell, (y - Sheet.Y) / cell, out int sheetX, out int sheetY))
        {
            return false;
        }
        sprite = sheetY * SheetStrip.LaneColumns + sheetX;
        return true;
    }

    /// <summary>The picker cell of one sprite — where the renderer draws it and where the selection frame goes.</summary>
    public Rectangle TileCellRect(int sprite)
    {
        SheetStrip.SpriteToStripCell(sprite, out int column, out int row);
        int cell = VirtualConsole.SpriteSize * SheetScale;
        return new Rectangle(Sheet.X + column * cell, Sheet.Y + row * cell, cell, cell);
    }

    /// <summary>
    /// Window point → <b>strip</b> cell of the picker, or false off the picker (wave 3e). The
    /// block drag is measured in strip cells and not in sprite numbers because the rectangle
    /// the author drags is a rectangle <em>on screen</em>: the strip lays the sheet's pages
    /// side by side (<see cref="SheetStrip"/>), so two cells that touch in the picker need not
    /// be consecutive sprites, and only the strip's own coordinates can say what "the next
    /// column" means. <see cref="TryTileCell"/> stays the single-cell answer for everything
    /// that wants a sprite number.
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
        column = (x - Sheet.X) / cell;
        row = (y - Sheet.Y) / cell;
        return column < SheetStrip.Columns && row < SheetStrip.Rows;
    }

    /// <summary>
    /// Window point → nearest strip cell, for the block drag — <see cref="ClampMapCell"/>'s
    /// twin, and there for the same reason: a drag whose pointer leaves the picker must keep
    /// sizing the block along the strip's edge instead of freezing or tearing. Floored, not
    /// truncated, so a pointer left of the picker counts as column 0 rather than sticking a
    /// whole cell wide of it (C# division rounds toward zero).
    /// </summary>
    public void ClampTileStripCell(int x, int y, out int column, out int row)
    {
        int cell = VirtualConsole.SpriteSize * SheetScale;
        column = Math.Clamp(FloorDiv(x - Sheet.X, cell), 0, SheetStrip.Columns - 1);
        row = Math.Clamp(FloorDiv(y - Sheet.Y, cell), 0, SheetStrip.Rows - 1);
    }

    /// <summary>
    /// The picker rectangle of a block anchored at one sprite — <see cref="TileCellRect"/>
    /// generalized, so the block frame and the single-tile frame come out of one formula (at
    /// 1x1 this <em>is</em> <see cref="TileCellRect"/>). Not clipped to the picker: the caller
    /// draws a frame, and a rectangle pulled inside the band would claim the block ends where
    /// the box does.
    /// </summary>
    public Rectangle TileBlockRect(int sprite, int width, int height)
    {
        Rectangle first = TileCellRect(sprite);
        int cell = VirtualConsole.SpriteSize * SheetScale;
        return new Rectangle(first.X, first.Y, width * cell, height * cell);
    }

    /// <summary>Window point → map cell on the minimap, or false. A click there is "take me to this cell".</summary>
    public bool TryMinimapCell(int x, int y, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        if (!Minimap.Contains(x, y))
        {
            return false;
        }
        cellX = Math.Clamp((x - Minimap.X) / MinimapScale, 0, MapColumns - 1);
        cellY = Math.Clamp((y - Minimap.Y) / MinimapScale, 0, MapRows - 1);
        return true;
    }

    /// <summary>The viewport's outline on the minimap — drawn from the same camera the canvas is drawn from.</summary>
    public Rectangle MinimapViewport(int cameraX, int cameraY) =>
        new(Minimap.X + cameraX * MinimapScale,
            Minimap.Y + cameraY * MinimapScale,
            Math.Min(VisibleColumns, MapColumns) * MinimapScale,
            Math.Min(VisibleRows, MapRows) * MinimapScale);
}

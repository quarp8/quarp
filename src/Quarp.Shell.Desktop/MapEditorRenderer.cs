using Microsoft.Xna.Framework;
using Quarp.Core;
using static Quarp.Shell.Desktop.ConsoleChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the map editor <b>into the console's own framebuffer</b> (wave R3, ADR-029): the top
/// band with the exit button, the tooltip field and the five editor tabs; the two-wide tool
/// block; the 17x8 map viewport with its grid, marked rectangle, floating paste and cursor; the
/// position bar under it; the tile palette or the whole-map view when either is up; the status
/// line and the one message line.
///
/// <para><b>What this file used to be.</b> Until this wave it owned a <c>GraphicsDevice</c>, a
/// 128x128 <c>Texture2D</c> of the sprite sheet, a second one of the minimap, a font atlas and
/// an icon atlas, and painted at the window's native resolution through a <c>SpriteBatch</c>.
/// All of that is gone. Every pixel now goes through <c>Cls</c>, <c>RectFill</c>, <c>Rect</c>,
/// <c>Print</c> and <c>Pset</c> on a <see cref="ShellScreen"/> — the same calls a cartridge
/// makes — and the result is presented by the same <see cref="ConsolePresenter"/> the
/// cartridge's frame goes through. The class is static for the same reason
/// <see cref="SpriteEditorRenderer"/> is: with no device resource to own there is nothing to
/// construct and nothing to dispose, and therefore also nothing to keep in step with a
/// session's <c>Version</c> — the two texture caches this file used to carry went with the
/// textures.</para>
///
/// <para><b>Nothing was dropped, and here is the roll call</b> (the wave's law: if a control
/// went under a key, it gets named). Pencil, hand, select and fill: buttons in the tool block,
/// digits 1-4. Eraser (tile 0): its button, <c>Del</c>, and the right mouse button. Grid: its
/// button and <c>`</c>. Tile palette: <b>an overlay now</b> — hold Shift, or latch it with its
/// button; the wheel over it flips between its two pages. Whole-map view: <b>a mode now</b> —
/// <c>Tab</c> or its button; a click on it travels. Block stamp: drag across the palette, or
/// Ctrl+Shift+arrows. Clipboard: Ctrl+C / Ctrl+X / Ctrl+V. Save, undo, redo: buttons and their
/// usual chords. Long-distance travel: arrows, <c>[</c> <c>]</c>, PgUp/PgDn, the wheel over the
/// map, the position bar and the whole-map view. Flip, flip and rotate the marked rectangle:
/// <c>F</c>, <c>V</c>, <c>R</c> — keys only, deliberately (the tool block is two columns by six
/// rows with one free slot, and three transform buttons do not fit without a flyout this screen
/// does not have), which is exactly why the same wave gave the four buttonless controls their
/// own hover labels: <see cref="TooltipText"/> is where those three keys are announced.</para>
///
/// <para><b>Where the picture of a tile comes from.</b> A map cell is a sprite number
/// (MAP-FORMAT §2), and the sprite sheet has exactly one owner —
/// <see cref="SpriteEditorSession"/>, the other tab of the same open cart. This renderer borrows
/// that session's flattened <see cref="SpriteEditorSession.Pixels"/> and plots them, so a sprite
/// edited on the sprites tab is visible on the map the moment the author flips back. It borrows
/// the <em>picture</em> and never the bank: no session reads another session's bytes off disk,
/// which is the stage-3 contract.</para>
///
/// <para><b>Tile 0 is drawn as emptiness on the map and as its art in the palette.</b>
/// <c>Map()</c> skips a zero cell entirely (MAP-FORMAT §2), so the viewport draws nothing there;
/// the palette shows sprite 0's real pixels with a dim frame around them, which is the correction
/// the owner's report of 2026-08-25 made to an earlier opaque plate — hiding the art hid the
/// author's own work. The standing line under the map says what the tile does when it is the one
/// in hand.</para>
///
/// <para><b>Cost, measured rather than waved away.</b> A full viewport is 17x8 cells of 64
/// pixels = 8704 plots worst case, and far fewer on a real map because a zero cell plots
/// nothing; the palette page is 128x64 = 8192. Against the 14400 the <c>Cls</c> on the same
/// frame writes, that is the same order the sprite screen already pays. This is drawing, not
/// simulation: it happens once per rendered frame, never inside a tick, and no rewind replays
/// it.</para>
///
/// <para><b>Not the cartridge's console.</b> The framebuffer written here belongs to the shell
/// (<see cref="ShellScreen"/>); the golden master the CI compares between architectures is
/// <see cref="CartSession"/>'s, and no call in this file can reach it.</para>
/// </summary>
public static class MapEditorRenderer
{
    /// <summary>What the tooltip field says when no control is hovered — TIC-80's <c>Names[mode]</c>.</summary>
    public const string ScreenName = "TILEMAP";

    /// <summary>The layout this screen is drawn with; the router asks for the same one, so picture and clicks cannot disagree.</summary>
    public static MapEditorLayout LayoutFor(
        ShellScreen screen, MapEditorSession map, MapEditorView view)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(view);
        return MapEditorLayout.Compute(screen.Width, screen.Height, view.Overlay, map.SelectedSprite);
    }

    /// <summary>
    /// One frame of the map editor. Owns the whole surface: it resets the console's drawing
    /// state and clears, so nothing another screen left behind can bend these pixels.
    /// <paramref name="sheet"/> is the sibling tab's session, the sole source of what a tile
    /// looks like; <paramref name="view"/> is the camera, the cursor and the overlay the
    /// router's hit tests read, so the picture and the clicks cannot disagree;
    /// <paramref name="hover"/> and <paramref name="tooltipVisible"/> come from the shell's
    /// <see cref="IconHoverTracker"/> — the hovered control's frame lights up immediately, the
    /// text label only after the tracker's three seconds, and the label lands in the top band
    /// rather than under the pointer (<see cref="ConsoleChrome.TooltipChars"/> explains why).
    /// </summary>
    /// <returns>The layout used, so a test can assert against exactly what was drawn.</returns>
    public static MapEditorLayout Draw(
        ShellScreen screen, MapEditorSession map, SpriteEditorSession sheet, MapEditorView view,
        HoverTarget? hover, bool tooltipVisible, IndexFormat indexes = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        MapEditorLayout layout = LayoutFor(screen, map, view);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);

        DrawBands(console, layout.Chrome);
        if (layout.Overlay != MapEditorOverlay.World)
        {
            DrawCanvas(console, layout, map, sheet, view);
        }
        DrawPalette(console, layout, map, sheet);
        DrawMinimap(console, layout, map, view);
        DrawSlider(console, layout, view);
        DrawButtons(console, layout, map, view, hover);
        // The readouts: the cursor's cell — the pair an author would hand Mget — and the tile
        // number in hand, which is the block's anchor when a block is in hand.
        // Both fields are spelled by the shell's one IndexFormat (Ctrl+H, REFERENCES-EDITORS §8
        // item 20) — the same value the sprite screen prints its own two with, so the author who
        // flipped the switch on one graphics tab finds it flipped on the other. `default` is
        // decimal, which is what this line printed before the switch existed.
        DrawStatusText(
            console, layout.Chrome,
            indexes.Pair(view.CursorX, view.CursorY), indexes.Sprite(map.SelectedSprite));
        DrawMessageLine(
            console, layout.Chrome, view.ExitPromptShown, map.SaveError, StandingNotice(map));
        DrawTooltipField(
            console, layout.Chrome,
            tooltipVisible && hover is HoverTarget target ? TooltipText(target) : null,
            ScreenName);
        return layout;
    }

    /// <summary>
    /// The hover label for whichever kind of target is under the pointer: a button gets
    /// <see cref="EditorIcons.MapTooltip"/>, and every control that is <em>not</em> a button —
    /// the viewport, the tile palette, the whole-map view, the position bar — gets
    /// <see cref="EditorIcons.MapRegionTooltip"/>, which is where this screen's least public
    /// gestures are announced (REFERENCES-EDITORS §8 item 15). The cut to the field's width
    /// belongs to <see cref="ConsoleChrome.FitTooltip"/>, the only thing that knows how wide the
    /// field is.
    ///
    /// <para><b>A target this screen does not recognise means "no label", never an exception</b> —
    /// the rule the sound and music renderers were given after the crash of 2026-08-25, and this
    /// screen is built with it rather than repaired into it. A hover target is measured against
    /// ONE screen's layout but the tracker outlives the screen (see
    /// <see cref="IconHoverTracker.Clear"/>): a keyboard tab switch lands between a frame's input
    /// and that frame's draw, so this method can be handed a sound-screen region with no button
    /// and no map region. Returning null there costs a tooltip for one frame; throwing costs the
    /// console, inside <c>Draw</c>, where nothing useful can catch it.</para>
    /// </summary>
    public static string? TooltipText(in HoverTarget target)
    {
        if (target.Button is EditorButton button)
        {
            return EditorIcons.MapTooltip(button);
        }
        return target.Map is MapRegion.None ? null : EditorIcons.MapRegionTooltip(target.Map);
    }

    /// <summary>
    /// The screen's standing line, re-cut for forty columns. Read-only wins when both apply: it
    /// is the one that changes what saving does — the session refuses map edits while map.csv is
    /// present (MAP-FORMAT §4), and an author who is not told discovers it by drawing into a
    /// wall.
    ///
    /// <para>The tile-0 line is the palette's dim frame spelled out: the console skips tile 0
    /// when it draws a map (MAP-FORMAT §2, the PICO-8 and LIKO-12 rule), so painting with it
    /// clears cells, and an author who selected it deserves to read that before he wonders why
    /// his grass will not stick.</para>
    ///
    /// <para>Both texts are shorter than the host screen's, which ran to 57 and 52 characters
    /// against the console line's 39. Cutting them here, at the one place that knows what they
    /// say, beats truncating them at the one place that knows how wide the line is — a truncated
    /// sentence ends mid-word. This is the same trade <see cref="SpriteEditorRenderer.StandingNotice"/>
    /// made one wave earlier.</para>
    /// </summary>
    public static string? StandingNotice(MapEditorSession map)
    {
        ArgumentNullException.ThrowIfNull(map);
        // The clipboard's refusal wins — see SfxEditorRenderer.StandingNotice for why the
        // transient answer to a keystroke outranks the two standing facts about the folder.
        return map.ClipboardNotice
            ?? (map.MapReadOnly
                ? $"READ-ONLY: {MapEditorSession.MapSourceFileName.ToUpperInvariant()} OWNS THIS MAP"
                : map.SelectedSprite == MapEditorSession.EmptyTile
                    ? "TILE 000 IS EMPTY - IT ERASES CELLS"
                    : null);
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="ConsoleChromeRenderer.DrawButton"/>
    /// owns. The only decision this screen makes is which buttons read as active: the tab of the
    /// screen you are on, the tool in hand, the grid switch while the grid is on, and wave R3's
    /// two overlay switches while their overlay is up. Which button is which tool is not decided
    /// here — <see cref="EditorIcons.MapToolOf"/> owns that mapping and the click router reads
    /// the same one, so the highlight cannot point at a button whose click selects something
    /// else.
    /// </summary>
    private static void DrawButtons(
        VirtualConsole console, in MapEditorLayout layout, MapEditorSession map, MapEditorView view,
        HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            var state = new EditorButtonState(
                Active: place.Id == EditorButton.TilemapTab
                    || EditorIcons.MapToolOf(place.Id) == view.Tool
                    || (place.Id == EditorButton.GridToggle && view.GridShown)
                    || (place.Id == EditorButton.TilesToggle
                        && layout.Overlay == MapEditorOverlay.Tiles)
                    || (place.Id == EditorButton.WorldToggle && view.WorldShown),
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: map.IsDirty,
                CanUndo: map.CanUndo,
                CanRedo: map.CanRedo);
            DrawButton(console, place, state, EditorIcons.IconFor(place.Id), text: null);
        }
    }

    /// <summary>
    /// The map viewport: every visible non-zero cell plotted out of the sibling session's sheet,
    /// then the grid, then the marked rectangle, the floating paste and the cursor. Zero cells
    /// draw nothing at all, which is exactly what <c>VirtualConsole.Map</c> does with them — the
    /// editor shows the console's own truth, not a prettier one.
    /// </summary>
    private static void DrawCanvas(
        VirtualConsole console, in MapEditorLayout layout, MapEditorSession map,
        SpriteEditorSession sheet, MapEditorView view)
    {
        ReadOnlySpan<byte> pixels = sheet.Pixels;
        int size = VirtualConsole.SpriteSize;
        for (int row = 0; row < layout.VisibleRows; row++)
        {
            int cellY = view.CameraY + row;
            for (int column = 0; column < layout.VisibleColumns; column++)
            {
                int cellX = view.CameraX + column;
                byte tile = map.TileAt(cellX, cellY);
                if (tile == MapEditorSession.EmptyTile)
                {
                    continue;
                }
                BlitSprite(
                    console, pixels, tile,
                    layout.Canvas.X + column * size, layout.Canvas.Y + row * size);
            }
        }

        DrawGrid(console, layout, view);

        // The marked rectangle (wave 3d), under the cursor frame so the cursor stays readable
        // inside it. A frame, never a tint: the author is choosing cells by their art.
        if (view.HasSelection)
        {
            Outline(
                console,
                Rectangle.Intersect(
                    layout.MapAreaRect(
                        view.SelectionX, view.SelectionY, view.SelectionWidth, view.SelectionHeight,
                        view.CameraX, view.CameraY),
                    layout.Canvas),
                Warn);
        }

        DrawFloatingPaste(console, layout, sheet, view);

        // The cursor — where the keyboard pencil is and what the status line reads. A frame
        // around the cell, not over it: the tile being placed must stay visible under it.
        Rectangle cursor = layout.MapCellRect(view.CursorX, view.CursorY, view.CameraX, view.CameraY);
        Outline(console, Rectangle.Intersect(cursor, layout.Canvas), Bright);
    }

    /// <summary>
    /// The tile grid: one dim line on every cell boundary inside the viewport, over the tiles
    /// and under the selection and cursor frames. TIC-80 has it on by default and on a key
    /// (<c>drawGridButton</c>, <c>`</c>) and so do we — <see cref="MapEditorView.GridShown"/> is
    /// the switch.
    ///
    /// <para><b>The host screen refused to draw this below map scale 2</b>, on the argument that
    /// a one-pixel line in an eight-pixel cell reads as graph paper. Wave R3 reverses that, and
    /// the reversal is the point of ADR-029: eight pixels to the cell with a one-pixel grid is
    /// not a degraded case of some larger scale, it is <em>the reference's own density</em>
    /// (TIC-80 draws exactly this, in vbank1, over a map at exactly this size). The old rule
    /// would have left the grid button with nothing to draw on every frame of this screen, which
    /// is a switch that cannot be seen to work.</para>
    /// </summary>
    private static void DrawGrid(VirtualConsole console, in MapEditorLayout layout, MapEditorView view)
    {
        if (!view.GridShown)
        {
            return;
        }
        for (int column = 1; column < layout.VisibleColumns; column++)
        {
            Fill(console, layout.GridColumnLine(column, 1), Dim);
        }
        for (int row = 1; row < layout.VisibleRows; row++)
        {
            Fill(console, layout.GridRowLine(row, 1), Dim);
        }
    }

    /// <summary>
    /// The floating paste (wave 3e; TIC-80 <c>drawPasteData</c>): the copied block drawn over
    /// the map at the cursor, so the author sees where it will land <em>before</em> the click
    /// that lands it. Nothing here touches the map — the block is the view's clipboard and the
    /// position is the view's cursor, and both stay exactly as they were until
    /// <see cref="MapEditorPaint.PasteAt"/> writes.
    ///
    /// <para>Cells outside the viewport are skipped rather than drawn: the canvas rectangle is
    /// the screen's own border, and a ghost tile painted past it would sit on the chrome. The
    /// paste itself is clipped at the map's edge by the session, which is why a block hanging
    /// off the corner shows only the part that will actually be written.</para>
    /// </summary>
    private static void DrawFloatingPaste(
        VirtualConsole console, in MapEditorLayout layout, SpriteEditorSession sheet, MapEditorView view)
    {
        if (!view.PasteFloating || !view.Clipboard.HasBlock)
        {
            return;
        }
        ReadOnlySpan<byte> pixels = sheet.Pixels;
        int width = view.Clipboard.Width;
        int height = view.Clipboard.Height;
        ReadOnlySpan<byte> tiles = view.Clipboard.Tiles;
        for (int row = 0; row < height; row++)
        {
            int cellY = view.CursorY + row;
            if (cellY < view.CameraY || cellY >= view.CameraY + layout.VisibleRows
                || cellY >= MapEditorLayout.MapRows)
            {
                continue;
            }
            for (int column = 0; column < width; column++)
            {
                int cellX = view.CursorX + column;
                if (cellX < view.CameraX || cellX >= view.CameraX + layout.VisibleColumns
                    || cellX >= MapEditorLayout.MapColumns)
                {
                    continue;
                }
                byte tile = tiles[row * width + column];
                Rectangle destination = layout.MapCellRect(cellX, cellY, view.CameraX, view.CameraY);
                if (tile == MapEditorSession.EmptyTile)
                {
                    // Tile 0 is emptiness, and pasting it erases — so the ghost shows a plate
                    // rather than nothing, or an author would read "this cell is not part of the
                    // block" where the truth is "this cell will be cleared".
                    Fill(console, destination, Dim);
                    continue;
                }
                BlitSprite(console, pixels, tile, destination.X, destination.Y);
            }
        }
        Outline(
            console,
            Rectangle.Intersect(
                layout.MapAreaRect(view.CursorX, view.CursorY, width, height, view.CameraX, view.CameraY),
                layout.Canvas),
            Warn);
    }

    /// <summary>
    /// The tile palette, one page of it, lying over the map (TIC-80 <c>drawSheetReg</c>). The
    /// page is the <see cref="SheetStrip"/> lane holding the selected tile, which is why it
    /// always contains the block's frame and why the palette needs no page state of its own —
    /// <see cref="MapEditorLayout.PaletteLane"/> carries the whole argument.
    ///
    /// <para>Tile 0 wears a dim frame wherever the page shows it: it is the map's empty cell,
    /// and the author is told so here — where he picks — and not only after painting with it and
    /// watching his grass disappear. The chosen block wears a bright one, clipped at the page's
    /// edge so a block sized past it by Ctrl+Shift+arrows cannot draw a frame across the map.</para>
    /// </summary>
    private static void DrawPalette(
        VirtualConsole console, in MapEditorLayout layout, MapEditorSession map, SpriteEditorSession sheet)
    {
        if (layout.Sheet.IsEmpty)
        {
            return;
        }
        ReadOnlySpan<byte> pixels = sheet.Pixels;
        int size = VirtualConsole.SpriteSize;
        for (int row = 0; row < SheetStrip.Rows; row++)
        {
            for (int column = 0; column < SheetStrip.LaneColumns; column++)
            {
                if (!SheetStrip.TryStripCellToSheetCell(
                    layout.PaletteLane * SheetStrip.LaneColumns + column, row,
                    out int sheetX, out int sheetY))
                {
                    continue;
                }
                BlitSheetCell(
                    console, pixels, sheetX, sheetY,
                    layout.Sheet.X + column * size, layout.Sheet.Y + row * size);
            }
        }
        Outline(
            console, Rectangle.Intersect(layout.TileCellRect(0), layout.Sheet), Dim);
        Outline(
            console,
            Rectangle.Intersect(
                layout.TileBlockRect(map.SelectedSprite, map.BlockWidth, map.BlockHeight),
                layout.Sheet),
            Bright);
    }

    /// <summary>
    /// The whole-map view (TIC-80's <c>world.c</c>, reached by Tab or its button): the entire
    /// 256x72 map folded two cells to the pixel, with the viewport's outline riding on it. A
    /// pixel is lit when any cell of its block is occupied — at this size a tile's own colours
    /// would be noise, and the question this view answers is "where is there anything, and where
    /// am I": the shape of the level, not its art.
    /// </summary>
    private static void DrawMinimap(
        VirtualConsole console, in MapEditorLayout layout, MapEditorSession map, MapEditorView view)
    {
        if (layout.Minimap.IsEmpty)
        {
            return;
        }
        int step = MapEditorLayout.MinimapCellsPerPixel;
        for (int row = 0; row < layout.Minimap.Height; row++)
        {
            for (int column = 0; column < layout.Minimap.Width; column++)
            {
                if (BlockOccupied(map, column * step, row * step, step))
                {
                    console.Pset(layout.Minimap.X + column, layout.Minimap.Y + row, Text);
                }
            }
        }
        Outline(console, layout.MinimapViewport(view.CameraX, view.CameraY), Bright);
    }

    /// <summary>True when any cell of one minimap pixel's block carries a tile.</summary>
    private static bool BlockOccupied(MapEditorSession map, int cellX, int cellY, int step)
    {
        for (int y = 0; y < step; y++)
        {
            for (int x = 0; x < step; x++)
            {
                if (map.TileAt(cellX + x, cellY + y) != MapEditorSession.EmptyTile)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The horizontal position bar: the track as a dim outline, the thumb where the camera
    /// stands. It is drawn in every state of this screen, including under an overlay, because it
    /// says where the map underneath is — the one piece of the working view that stays true
    /// while something else is on top of it.
    /// </summary>
    private static void DrawSlider(VirtualConsole console, in MapEditorLayout layout, MapEditorView view)
    {
        Outline(console, layout.Slider, Dim);
        Fill(console, layout.SliderThumb(view.CameraX), Text);
    }

    /// <summary>One tile's 8x8 art, by sprite number, plotted straight out of the sibling session's sheet.</summary>
    private static void BlitSprite(
        VirtualConsole console, ReadOnlySpan<byte> pixels, int sprite, int x, int y) =>
        BlitSheetCell(
            console, pixels,
            sprite % VirtualConsole.SheetColumns, sprite / VirtualConsole.SheetColumns, x, y);

    /// <summary>
    /// One 8x8 cell of the canonical sheet, plotted at (<paramref name="x"/>,
    /// <paramref name="y"/>). The one place sheet bytes become console pixels on this screen, so
    /// the map, the palette and the paste ghost cannot acquire three different ideas of where a
    /// sprite's pixels live.
    /// </summary>
    private static void BlitSheetCell(
        VirtualConsole console, ReadOnlySpan<byte> pixels, int cellX, int cellY, int x, int y)
    {
        int size = VirtualConsole.SpriteSize;
        for (int row = 0; row < size; row++)
        {
            int source = (cellY * size + row) * VirtualConsole.SheetWidth + cellX * size;
            for (int column = 0; column < size; column++)
            {
                console.Pset(x + column, y + row, pixels[source + column]);
            }
        }
    }

    /// <summary>One filled rectangle, a layout rectangle unpacked into the console's call.</summary>
    private static void Fill(VirtualConsole console, Rectangle rect, byte color) =>
        console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, color);

    /// <summary>One outline, skipped when the clipped rectangle came back empty.</summary>
    private static void Outline(VirtualConsole console, Rectangle rect, byte color)
    {
        if (rect.Width > 0 && rect.Height > 0)
        {
            console.Rect(rect.X, rect.Y, rect.Width, rect.Height, color);
        }
    }
}

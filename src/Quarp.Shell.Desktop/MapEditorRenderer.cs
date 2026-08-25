using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;
using static Quarp.Shell.Desktop.EditorChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the map editor screen in the shell standard (M9 stage 2.5's caravan of reviews,
/// applied to stage 3's map): the icon-only tab strip and the status bar as tinted full-width
/// bands, the pencil column left of the canvas and the empty-tile column right of it at the
/// identical margin (seventh review), the map viewport with its cursor, the tile picker under
/// it and the minimap beside that, the reserved prompt line and the hover tooltips. Host UI
/// like <see cref="LibraryRenderer"/> and <see cref="SpriteEditorRenderer"/> — window-native
/// resolution, <see cref="Palette.Master32"/> colors, the system font and the icon strip — and
/// just as unable to touch a framebuffer or a hash: no cartridge runs while this draws.
///
/// <para>Everything both editor screens paint the same way comes from
/// <see cref="EditorChromeRenderer"/>; this class owns the viewport, the picker and the minimap.</para>
///
/// <para>All geometry comes from <see cref="MapEditorLayout"/>, the same struct the shell
/// hit-tests the mouse against; this class owns only pixels-on-screen.</para>
///
/// <para><b>Where the picture of a tile comes from.</b> A map cell is a sprite number
/// (MAP-FORMAT §2), and the sprite sheet has exactly one owner —
/// <see cref="SpriteEditorSession"/>, the other tab of the same open cart. This renderer
/// borrows that session's flattened <see cref="SpriteEditorSession.Pixels"/> to build its
/// texture and re-uploads when its <see cref="SpriteEditorSession.Version"/> moves, so a
/// sprite edited on the sprites tab is visible on the map the moment the author flips back.
/// It borrows the <em>picture</em> and never the bank: no session reads another session's
/// bytes off disk, which is the stage-3 contract.</para>
///
/// <para><b>Tile 0 is drawn as emptiness, not as sprite 0.</b> <c>Map()</c> skips a zero cell
/// entirely (MAP-FORMAT §2), so the canvas draws nothing there, and the picker's cell 0 is
/// overpainted with an explicit empty plate whatever art sprite 0 happens to hold. Showing
/// sprite 0's pixels in a tile picker would promise a tile the console will never draw.</para>
/// </summary>
public sealed class MapEditorRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly EditorChromeRenderer _chrome;
    private readonly Texture2D _sheetTexture;
    private readonly Color[] _sheetPixels;
    private readonly Texture2D _minimapTexture;
    private readonly Color[] _minimapPixels;
    private readonly Color[] _palette;

    // Which session and which of its versions each texture currently shows. The reference
    // matters as much as the number: a fresh session starts at Version 0, and matching
    // versions across different sessions would leave the previous cart's picture on screen.
    private SpriteEditorSession? _shownSheetSession;
    private int _shownSheetVersion;
    private MapEditorSession? _shownMapSession;
    private int _shownMapVersion;

    public MapEditorRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _chrome = new EditorChromeRenderer(device);
        _sheetTexture = new Texture2D(device, VirtualConsole.SheetWidth, VirtualConsole.SheetHeight);
        _sheetPixels = new Color[VirtualConsole.SheetWidth * VirtualConsole.SheetHeight];
        _minimapTexture = new Texture2D(device, MapEditorLayout.MapColumns, MapEditorLayout.MapRows);
        _minimapPixels = new Color[MapEditorLayout.MapColumns * MapEditorLayout.MapRows];
        _palette = new Color[Palette.VisibleCount];
        for (int i = 0; i < Palette.VisibleCount; i++)
        {
            _palette[i] = PaletteColors.Opaque(i);
        }
    }

    /// <summary>
    /// One frame of the map editor. Owns the whole surface (clears, begins and ends the batch)
    /// like the other two host screens. <paramref name="sheet"/> is the sibling tab's session,
    /// the sole source of what a tile looks like; <paramref name="view"/> is the camera and
    /// cursor the shell's hit tests read, so the picture and the clicks cannot disagree;
    /// <paramref name="hover"/> and <paramref name="tooltipVisible"/> come from the shell's
    /// <see cref="IconHoverTracker"/> — frame highlight now, label after its three seconds.
    /// </summary>
    public void Draw(
        SpriteBatch batch, int width, int height, MapEditorSession map, SpriteEditorSession sheet,
        MapEditorView view, HoverTarget? hover, bool tooltipVisible)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(view);
        var layout = MapEditorLayout.Compute(width, height);
        UploadSheetIfChanged(sheet);
        UploadMinimapIfChanged(map);

        _device.Clear(Ink);
        batch.Begin(samplerState: SamplerState.PointClamp);

        _chrome.DrawBands(batch, layout.Chrome);

        DrawCanvas(batch, layout, map, view);
        DrawButtons(batch, layout, map, view, hover);
        DrawPicker(batch, layout, map);
        DrawMinimap(batch, layout, view);
        _chrome.DrawStatusText(batch, layout.Chrome, MapCoordinates(view), $"#{map.SelectedSprite:D3}");
        _chrome.DrawPromptLine(
            batch, layout.Chrome, view.ExitPromptShown, map.SaveError, StandingNotice(map));
        DrawTooltip(batch, layout, width, height, hover, tooltipVisible);

        batch.End();
    }

    public void Dispose()
    {
        _chrome.Dispose();
        _sheetTexture.Dispose();
        _minimapTexture.Dispose();
    }

    /// <summary>The status band's readout: the cursor's cell — the pair an author would hand <c>Mget</c>.</summary>
    private static string MapCoordinates(MapEditorView view) => $"{view.CursorX:D3},{view.CursorY:D3}";

    /// <summary>
    /// The screen's standing line under the prompt and the save error. Not optional chrome: the
    /// session refuses map edits while map.csv is present (MAP-FORMAT §4), and an author who is
    /// not told discovers it by drawing into a wall.
    /// </summary>
    /// <summary>
    /// The screen's standing line. Read-only wins when both apply: it is the one that changes
    /// what saving does. The tile-0 line is the picker's marker spelled out — the console skips
    /// tile 0 when it draws a map (SPEC/MAP-FORMAT §2, the PICO-8 and LIKO-12 rule), so
    /// painting with it clears cells, and an author who selected it deserves to read that
    /// before he wonders why his grass will not stick.
    /// </summary>
    public static string? StandingNotice(MapEditorSession map) =>
        map.MapReadOnly
            ? $"READ-ONLY: {MapEditorSession.MapSourceFileName.ToUpperInvariant()} OWNS THIS MAP - REMOVE IT TO EDIT HERE"
            : map.SelectedSprite == 0
                ? "TILE 000 IS THE EMPTY CELL - PAINTING WITH IT ERASES"
                : null;

    private void UploadSheetIfChanged(SpriteEditorSession sheet)
    {
        if (ReferenceEquals(sheet, _shownSheetSession) && sheet.Version == _shownSheetVersion)
        {
            return;
        }
        ReadOnlySpan<byte> pixels = sheet.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            _sheetPixels[i] = _palette[pixels[i]];
        }
        _sheetTexture.SetData(_sheetPixels);
        _shownSheetSession = sheet;
        _shownSheetVersion = sheet.Version;
    }

    /// <summary>
    /// The minimap is one texel per map cell, so the whole 256x72 map is one quad however far
    /// the author has scrolled. Occupied or empty, nothing finer: at one pixel per cell a tile's
    /// own colours would be noise, and the question this thumbnail answers is "where is there
    /// anything, and where am I" — the shape of the level, not its art. It is rebuilt only when
    /// <see cref="MapEditorSession.Version"/> moves, so an idle screen costs no upload.
    /// </summary>
    private void UploadMinimapIfChanged(MapEditorSession map)
    {
        if (ReferenceEquals(map, _shownMapSession) && map.Version == _shownMapVersion)
        {
            return;
        }
        ReadOnlySpan<byte> tiles = map.Map;
        for (int i = 0; i < tiles.Length; i++)
        {
            _minimapPixels[i] = tiles[i] == 0 ? Ink : Text;
        }
        _minimapTexture.SetData(_minimapPixels);
        _shownMapSession = map;
        _shownMapVersion = map.Version;
    }

    /// <summary>
    /// The map viewport: every visible non-zero cell as one quad out of the sheet texture,
    /// plus the cursor frame. Zero cells draw nothing at all, which is exactly what
    /// <c>VirtualConsole.Map</c> does with them — the editor shows the console's own truth,
    /// not a prettier one.
    /// </summary>
    private void DrawCanvas(SpriteBatch batch, in MapEditorLayout layout, MapEditorSession map, MapEditorView view)
    {
        _chrome.DrawFrame(batch, layout.Canvas, layout.Ui, Dim);
        int size = VirtualConsole.SpriteSize;
        for (int row = 0; row < layout.VisibleRows; row++)
        {
            int cellY = view.CameraY + row;
            for (int column = 0; column < layout.VisibleColumns; column++)
            {
                int cellX = view.CameraX + column;
                byte tile = map.TileAt(cellX, cellY);
                if (tile == 0)
                {
                    continue;
                }
                var source = new Rectangle(
                    tile % VirtualConsole.SheetColumns * size,
                    tile / VirtualConsole.SheetColumns * size,
                    size,
                    size);
                batch.Draw(
                    _sheetTexture, layout.MapCellRect(cellX, cellY, view.CameraX, view.CameraY),
                    source, Color.White);
            }
        }

        DrawGrid(batch, layout, view);

        // The marked rectangle (wave 3d), under the cursor frame so the cursor stays readable
        // inside it. A frame, never a tint: the author is choosing cells by their art.
        if (view.HasSelection)
        {
            _chrome.DrawFrame(
                batch,
                layout.MapAreaRect(
                    view.SelectionX, view.SelectionY, view.SelectionWidth, view.SelectionHeight,
                    view.CameraX, view.CameraY),
                Math.Max(1, layout.Ui / 2),
                Warn);
        }

        // The cursor — where the keyboard pencil is and what the status bar reads. A frame
        // around the cell, not over it: the tile being placed must stay visible under it.
        _chrome.DrawFrame(
            batch,
            layout.MapCellRect(view.CursorX, view.CursorY, view.CameraX, view.CameraY),
            Math.Max(1, layout.Ui / 2),
            Bright);
    }

    /// <summary>
    /// The tile grid: one dim line on every cell boundary inside the viewport, drawn over the
    /// tiles and under the selection and cursor frames. TIC-80 has it on by default and on a
    /// key (<c>drawGridButton</c>, <c>`</c>) and so do we — <see cref="MapEditorView.GridShown"/>
    /// is the switch.
    ///
    /// <para><b>Never at <see cref="MapEditorLayout.MapScale"/> 1.</b> A map cell is then eight
    /// window pixels across and a one-pixel line is an eighth of it: the grid would take a
    /// visible share of every tile and the screen would read as graph paper with sprites on it
    /// rather than a map. The switch is still honoured — it just has nothing to draw — which is
    /// why the button's tooltip says so out loud.</para>
    /// </summary>
    private void DrawGrid(SpriteBatch batch, in MapEditorLayout layout, MapEditorView view)
    {
        if (!view.GridShown || layout.MapScale <= 1)
        {
            return;
        }
        int thickness = Math.Max(1, layout.Ui / 4);
        for (int column = 1; column < layout.VisibleColumns; column++)
        {
            batch.Draw(_chrome.White, layout.GridColumnLine(column, thickness), Dim);
        }
        for (int row = 1; row < layout.VisibleRows; row++)
        {
            batch.Draw(_chrome.White, layout.GridRowLine(row, thickness), Dim);
        }
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="EditorChromeRenderer.DrawButton"/>
    /// owns. The only decision this screen makes is which buttons read as active: the tab of the
    /// screen you are on, the tool in hand, and the grid switch while the grid is on. Which
    /// button is which tool is not decided here — <see cref="EditorIcons.MapToolOf"/> owns that
    /// mapping and the click router reads the same one, so the highlight cannot point at a
    /// button whose click selects something else.
    /// </summary>
    private void DrawButtons(
        SpriteBatch batch, in MapEditorLayout layout, MapEditorSession map, MapEditorView view,
        HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            var state = new EditorButtonState(
                Active: place.Id == EditorButton.TilemapTab
                    || EditorIcons.MapToolOf(place.Id) == view.Tool
                    || (place.Id == EditorButton.GridToggle && view.GridShown),
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: map.IsDirty,
                CanUndo: map.CanUndo,
                CanRedo: map.CanRedo);
            _chrome.DrawButton(
                batch, layout.Chrome, place, state, EditorIcons.IconFor(place.Id), text: null);
        }
    }

    /// <summary>
    /// The tile picker: the whole <see cref="SheetStrip"/>, one quad per lane (the same
    /// lane-block trick the sprite editor's sheet uses, so the presentation transform is not
    /// duplicated here), then tile 0's marker, then the selected tile's frame.
    /// There is no slider because <see cref="MapEditorLayout"/> sizes the window to hold every
    /// column — all 256 tiles at once at the shell's window sizes.
    /// </summary>
    private void DrawPicker(SpriteBatch batch, in MapEditorLayout layout, MapEditorSession map)
    {
        _chrome.DrawFrame(batch, layout.Sheet, layout.Ui, Dim);
        for (int lane = 0; lane < SheetStrip.Lanes; lane++)
        {
            int firstSprite = lane * SheetStrip.Rows * SheetStrip.LaneColumns;
            SheetStrip.SpriteToStripCell(firstSprite, out int stripColumn, out _);
            var source = new Rectangle(
                0,
                lane * SheetStrip.Rows * VirtualConsole.SpriteSize,
                VirtualConsole.SheetWidth,
                SheetStrip.PixelHeight);
            var drawn = new Rectangle(
                layout.Sheet.X + stripColumn * VirtualConsole.SpriteSize * layout.SheetScale,
                layout.Sheet.Y,
                source.Width * layout.SheetScale,
                source.Height * layout.SheetScale);
            batch.Draw(_sheetTexture, drawn, source, Color.White);
        }

        // Tile 0 wears a marker, not a lid (2026-08-25, owner's report). It used to be painted
        // over with an opaque plate so the picker would not "promise a tile the console will
        // never draw" — and that plate is what a first author hit: he drew grass on sprite 0,
        // opened the map, found a blank broken-looking cell, and concluded the editor was
        // broken. Both references that show a picker at all show sprite 0's real pixels there:
        // TIC-80 draws the whole sheet from index 0 with no transparent colour
        // (studio/editors/map.c, drawSheetReg), and PICO-8's navigator is the sheet itself.
        // Hiding the art hid the author's own work; the honest thing is to show the art and
        // say out loud what the tile does — the dim frame here, and the standing line under
        // the prompt when this tile is the selected one.
        _chrome.DrawFrame(batch, layout.TileCellRect(0), Math.Max(1, layout.Ui / 2), Dim);

        _chrome.DrawFrame(
            batch, layout.TileCellRect(map.SelectedSprite), Math.Max(1, layout.Ui / 2),
            Bright);
    }

    /// <summary>
    /// The whole map as one quad, with the viewport's outline riding on it — the mouse's road
    /// to the far corner (a click there is a jump) and the only place the author can see where
    /// the window is inside eighty screens of content.
    /// </summary>
    private void DrawMinimap(SpriteBatch batch, in MapEditorLayout layout, MapEditorView view)
    {
        _chrome.DrawFrame(batch, layout.Minimap, layout.Ui, Dim);
        batch.Draw(_minimapTexture, layout.Minimap, Color.White);
        _chrome.DrawFrame(
            batch, layout.MinimapViewport(view.CameraX, view.CameraY), Math.Max(1, layout.Ui / 2),
            Bright);
    }

    /// <summary>
    /// The tooltip's map half: which text and which anchor. On this screen the only hover
    /// target with a tooltip is a button, so the resolution is one lookup in
    /// <see cref="EditorIcons.MapTooltip"/>; the box itself belongs to the shared painter.
    /// </summary>
    private void DrawTooltip(
        SpriteBatch batch, in MapEditorLayout layout, int width, int height,
        HoverTarget? hover, bool tooltipVisible)
    {
        if (hover is not HoverTarget target || target.Button is not EditorButton button || !tooltipVisible)
        {
            return;
        }
        _chrome.DrawTooltip(
            batch, layout.Chrome, width, height,
            EditorIcons.MapTooltip(button), layout.ButtonRect(button));
    }
}

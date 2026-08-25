using System;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The sprite screen's <b>view</b> state — the facts about how the screen is being looked at
/// that no cartridge file can hold. Until this file the sprite editor was the one screen of the
/// five with no view of its own: its camera-shaped state lived in <see cref="SheetScroll"/>, its
/// transient chrome in <see cref="ToolbarFlyout"/>, and everything else had to be a document
/// fact or nothing at all. Two of this wave's three features are neither, so the twin of
/// <see cref="MapEditorView"/> exists now, holding exactly the two of them and not one thing
/// more.
///
/// <para><b>It is deliberately a near-copy of the map's, names included.</b> The block gesture
/// below is <see cref="MapEditorView.BeginTileBlock"/>, <c>UpdateTileBlock</c>,
/// <c>EndTileBlock</c> and <c>StepTileBlock</c> re-stated over the other palette, on the same
/// mouse drag and the same Ctrl+Shift+arrows, because the author has two sprite palettes on two
/// screens and must not be made to learn two rules for them. Where the two differ is only where
/// the screens differ, and each difference is named at its method.</para>
///
/// <para><b>Headless.</b> Nothing here names a MonoGame type; the layout arrives as an
/// <c>in</c> parameter and the session as a reference. That is the layer-2 test
/// <c>scripts/check-modules.sh</c> states — the type is constructed in a test with no
/// <c>GraphicsDevice</c> anywhere near it.</para>
/// </summary>
public sealed class SpriteEditorView
{
    /// <summary>
    /// Whether the pixel grid is drawn over the canvas (REFERENCES-EDITORS §8 item 11; PICO-8's
    /// <c>CTRL-G</c>, "toggle black grid lines when zoomed in"). <b>Off by default</b>, which is
    /// the opposite of <see cref="MapEditorView.GridShown"/> and deliberately so: TIC-80 ships
    /// its MAP grid on (<c>.canvas = {.grid = true}</c>) and PICO-8 ships its SPRITE grid off,
    /// and each reference is right about its own screen. A map cell is eight pixels of somebody
    /// else's picture and the grid is how you count cells; a canvas pixel is the thing being
    /// drawn, and a first-time author who has never pressed the key should see his art and
    /// nothing over it.
    ///
    /// <para>It is a fact of the VIEW and not of the session for the reason every fact here is:
    /// no byte of <c>gfx.png</c>, <c>gfx-layers.png</c> or <c>flags.bin</c> changes when it
    /// flips, so a session that carried it would be reporting itself dirty over a way of
    /// looking.</para>
    /// </summary>
    public bool GridShown { get; private set; }

    /// <summary>True between the press and the release of a drag across the sheet window — the twin of <see cref="MapEditorView.TileBlockGestureActive"/>.</summary>
    public bool TileBlockGestureActive { get; private set; }

    // The strip cell the sheet drag started on. The block is normalized out of this and the
    // current strip cell on every sample, exactly as the map picker's rectangle is out of its
    // anchor, so dragging up and left marks the same block as dragging down and right.
    private int _tileAnchorColumn;
    private int _tileAnchorRow;

    /// <summary>The grid switch, for the <c>`</c> key — TIC-80's own key for its own grid button, and the key the map screen already answers.</summary>
    public void ToggleGrid() => GridShown = !GridShown;

    /// <summary>
    /// The sheet drag's press: the block starts as the single strip cell under the pointer,
    /// which is exactly what a click that never moves did before this wave (TIC-80's own start
    /// state, <c>.sheet.rect = {0, 0, 1, 1}</c>). Every sample writes the block through
    /// <paramref name="session"/>, the one owner of "how much of the sheet is in hand".
    /// </summary>
    public void BeginTileBlock(
        SpriteEditorSession session, in SpriteEditorLayout layout, int x, int y, int scroll)
    {
        ArgumentNullException.ThrowIfNull(session);
        layout.ClampSheetStripCell(x, y, scroll, out _tileAnchorColumn, out _tileAnchorRow);
        TileBlockGestureActive = true;
        UpdateTileBlock(session, layout, x, y, scroll);
    }

    /// <summary>
    /// One more sample of an open sheet drag; a no-op without one. The rectangle is normalized
    /// out of the anchor and the current cell, the rule the map picker already carries.
    ///
    /// <para><b>The one difference from the map's, and why.</b> That screen's palette is a
    /// single page and clamps to it; this window scrolls horizontally, so the pointer's cell is
    /// resolved against <paramref name="scroll"/> — the very offset the renderer draws the strip
    /// at. Without it a drag on a scrolled sheet would mark the sprites the author is not looking
    /// at.</para>
    /// </summary>
    public void UpdateTileBlock(
        SpriteEditorSession session, in SpriteEditorLayout layout, int x, int y, int scroll)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!TileBlockGestureActive)
        {
            return;
        }
        layout.ClampSheetStripCell(x, y, scroll, out int column, out int row);
        SetTileBlock(
            session,
            Math.Min(_tileAnchorColumn, column),
            Math.Min(_tileAnchorRow, row),
            Math.Abs(column - _tileAnchorColumn) + 1,
            Math.Abs(row - _tileAnchorRow) + 1);
    }

    /// <summary>The sheet drag's release: the block stands until a single cell replaces it.</summary>
    public void EndTileBlock() => TileBlockGestureActive = false;

    /// <summary>
    /// The keyboard's half of the sheet drag (the parity law): grow or shrink the block by one
    /// strip cell, keeping its top-left sprite where it is — Ctrl+Shift+arrows, the map's own
    /// chord for the map's own version of this, chosen there because Shift+arrows already steps
    /// the selected sprite and a chord must not double as its bare key. Both statements are true
    /// on this screen too (<see cref="EditorSheetStep"/> is what Shift+arrows drives here), so
    /// the gesture transfers without a single change of meaning.
    /// </summary>
    public void StepTileBlock(SpriteEditorSession session, int deltaWidth, int deltaHeight)
    {
        ArgumentNullException.ThrowIfNull(session);
        SheetStrip.SpriteToStripCell(session.SpriteIndex, out int column, out int row);
        SetTileBlock(
            session,
            column,
            row,
            session.BlockWidth + deltaWidth,
            session.BlockHeight + deltaHeight);
    }

    /// <summary>
    /// The one place a block reaches the session: the strip cell of its top-left corner becomes
    /// the region anchor and the size travels with it. Clamped here, once, because a block
    /// hanging off the sheet would have cells with no sprite behind them — the same clamp
    /// <c>MapEditorView.SetTileBlock</c> does, in the same place and for the same reason.
    ///
    /// <para><b>One clamp the map does not have: the block stays inside one strip LANE.</b> The
    /// strip lays the sheet's sixteen rows out as two lanes of eight standing side by side
    /// (<see cref="SheetStrip"/>), so strip columns 0-15 are sheet rows 0-7 and columns 16-31
    /// are rows 8-15. A rectangle straddling that seam is a contiguous rectangle on the SCREEN
    /// and two disjoint pieces in the sheet — the map screen can live with that because it
    /// resolves every cell of its block through the strip on the way to the map
    /// (<c>MapEditorPaint.BlockTiles</c>), while a sprite block is a rectangle of the sheet
    /// itself: the flag row folds over it and the canvas anchor sits in its corner, and neither
    /// can be told in canonical sheet coordinates if the block is not one rectangle there. So a
    /// drag that crosses the seam stops at it. Named rather than hidden, because it is a real
    /// difference between the two palettes and the author will meet it.</para>
    /// </summary>
    private static void SetTileBlock(
        SpriteEditorSession session, int column, int row, int width, int height)
    {
        column = Math.Clamp(column, 0, SheetStrip.Columns - 1);
        row = Math.Clamp(row, 0, SheetStrip.Rows - 1);
        int laneFirst = column / SheetStrip.LaneColumns * SheetStrip.LaneColumns;
        width = Math.Clamp(width, 1, laneFirst + SheetStrip.LaneColumns - column);
        height = Math.Clamp(height, 1, SheetStrip.Rows - row);
        if (SheetStrip.TryStripCellToSheetCell(column, row, out int sheetX, out int sheetY))
        {
            session.SelectRegionBlock(sheetX, sheetY, width, height);
        }
    }
}

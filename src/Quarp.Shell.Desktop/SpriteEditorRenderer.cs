using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;
using static Quarp.Shell.Desktop.EditorChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the sprite editor screen in the owner's verdict shape (M9 stage 2.5, sixth review
/// applied): the icon-only tab strip and the status bar as tinted full-width bands, the left
/// toolbar column with its group slots (corner-marked, flyout on demand), the zoomed canvas
/// with the keyboard cursor, the shape preview, the selection as marching ants (plus the
/// holes and floating fragment during a move) and the stamp ghost — all session-state
/// overlays, never sheet pixels — the right column (palette at the window's edge, the sixth
/// review's one narrow row of size toggle plus layer tabs, the eight flag toggles under that
/// row in their three states, and under them the sheet window
/// owning the rest of the column, with its scroll slider), the size toggle's number face, the
/// status buttons (save/undo/redo/clear), the reserved prompt line and the hover tooltips.
/// Host UI like <see cref="LibraryRenderer"/> — window-native resolution,
/// <see cref="Palette.Master32"/> colors, the system font and the icon strip — and just as
/// unable to touch a framebuffer or a hash: no cartridge runs while this draws.
///
/// <para>All geometry comes from <see cref="SpriteEditorLayout"/>, the same struct the shell
/// hit-tests the mouse against; this class owns only pixels-on-screen. The picture lives in
/// one 128x128 texture holding the session's <b>composite</b> (ADR-027: what the author sees
/// is the flattened stack, on the canvas and in the sheet window alike), drawn twice —
/// scaled up for the canvas (source rectangle = the region) and by the sheet window's own
/// scale for the sheet view — re-uploaded only when
/// <see cref="SpriteEditorSession.Version"/> moves, so an idle editor costs a handful of
/// quads per frame. A floating move's overlay reads the same composite; where another layer
/// covers the moved pixels the ride shows the covering color, which is also exactly what the
/// drop will leave on screen.</para>
/// </summary>
public sealed class SpriteEditorRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly EditorChromeRenderer _chrome;
    private readonly Texture2D _sheetTexture;
    private readonly Color[] _sheetPixels;

    /// <summary>Palette lookup, unpacked once — 16 K pixels per sheet upload should not re-shift RGB each time.</summary>
    private readonly Color[] _palette;

    // Which session and which of its versions the sheet texture currently shows. The session
    // reference matters: a fresh session starts at Version 0, and matching versions across
    // different sessions would leave the previous cart's sheet on screen.
    private SpriteEditorSession? _shownSession;
    private int _shownVersion;

    // The marching ants' dash buffer, reused every frame: the outline is rebuilt each Draw
    // (it marches), but the list is not reallocated sixty times a second.
    private readonly List<AntDash> _ants = new();

    public SpriteEditorRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _chrome = new EditorChromeRenderer(device);
        _sheetTexture = new Texture2D(device, VirtualConsole.SheetWidth, VirtualConsole.SheetHeight);
        _sheetPixels = new Color[VirtualConsole.SheetWidth * VirtualConsole.SheetHeight];
        _palette = new Color[Palette.VisibleCount];
        for (int i = 0; i < Palette.VisibleCount; i++)
        {
            _palette[i] = PaletteColors.Opaque(i);
        }
    }

    /// <summary>
    /// One frame of the editor. Owns the whole surface (clears, begins and ends the batch),
    /// like the library does. <paramref name="hover"/> and <paramref name="tooltipVisible"/>
    /// come from the shell's <see cref="IconHoverTracker"/>: the hovered frame lights up
    /// immediately, the text label only after the tracker's three seconds.
    /// <paramref name="flyoutSlot"/> is the shell's <see cref="ToolbarFlyout.OpenSlot"/> —
    /// the flyout draws late so it floats over the canvas, and the tooltip still wins over it.
    /// <paramref name="scroll"/> is the shell's <see cref="SheetScroll"/>: the sheet window's
    /// slice and the slider's thumb are both drawn from its offset — the very number the hit
    /// tests use, so the picture and the clicks cannot disagree.
    /// <paramref name="timeSeconds"/> is the shell's draw clock, consumed only by the marching
    /// ants' phase — host chrome time, invisible to any simulation or hash.
    /// </summary>
    public void Draw(
        SpriteBatch batch, int width, int height, SpriteEditorSession editor,
        HoverTarget? hover, bool tooltipVisible, EditorButton? flyoutSlot, SheetScroll scroll,
        double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(scroll);
        var layout = SpriteEditorLayout.Compute(width, height, editor.RegionCells);
        UploadSheetIfChanged(editor);

        _device.Clear(Ink);
        batch.Begin(samplerState: SamplerState.PointClamp);

        // The bands go first: everything in the strips sits ON them.
        _chrome.DrawBands(batch, layout.Chrome);

        DrawCanvas(batch, layout, editor, timeSeconds);
        DrawButtons(batch, layout, editor, hover);
        DrawSwatches(batch, layout, editor);
        DrawFlags(batch, layout, editor, hover);
        DrawSheet(batch, layout, editor, scroll.Offset);
        DrawSlider(batch, layout, scroll, hover);
        // The readouts: the cursor in SHEET pixels — the coordinate an author would type into
        // code — and the sprite number, which is Spr(n)'s n for the region's anchor cell.
        _chrome.DrawStatusText(
            batch, layout.Chrome, SheetCoordinates(editor), $"#{editor.SpriteIndex:D3}");
        _chrome.DrawPromptLine(
            batch, layout.Chrome, editor.ExitPromptShown, editor.SaveError, StandingNotice(editor));
        DrawFlyout(batch, layout, editor, flyoutSlot, hover);
        DrawTooltip(batch, layout, width, height, editor, hover, tooltipVisible);

        batch.End();
    }

    public void Dispose()
    {
        _chrome.Dispose();
        _sheetTexture.Dispose();
    }

    /// <summary>
    /// The status band's readout, in <b>sheet</b> pixels — the coordinate an author would type
    /// into code, not a window position.
    /// </summary>
    private static string SheetCoordinates(SpriteEditorSession editor)
    {
        int size = VirtualConsole.SpriteSize;
        return $"{editor.RegionCellX * size + editor.CursorX:D3},{editor.RegionCellY * size + editor.CursorY:D3}";
    }

    /// <summary>
    /// The screen's standing line under the prompt and the save error. Two things can stand
    /// here, and the out-of-sync warning (ADR-027) wins because it changes what saving does:
    /// gfx.png was edited outside while gfx-layers.png stood — the stack wins and the next save
    /// will overwrite it, which must be announced, not sprung.
    ///
    /// <para>The second is the trap this editor could otherwise spring in total silence
    /// (2026-08-25, owner's report): a map cell holding 0 means "empty" and the console skips
    /// it, so art drawn on sprite 0 can never appear on a map. The author who hits this has
    /// already spent his time — he drew grass on the cell the editor opens on. Sprite 0 is a
    /// perfectly good sprite for <c>Spr</c>, so this is a notice and not a block; the map
    /// editor's picker carries the matching marker on the same tile.</para>
    /// </summary>
    public static string? StandingNotice(SpriteEditorSession editor) =>
        editor.GfxOutOfSyncOnDisk
            ? "GFX.PNG EDITED OUTSIDE - LAYERS WIN, SAVING OVERWRITES IT"
            : editor.SpriteIndex == 0
                ? "SPRITE 000 IS THE MAP'S EMPTY TILE - A MAP WILL NOT DRAW IT"
                : null;

    private void UploadSheetIfChanged(SpriteEditorSession editor)
    {
        if (ReferenceEquals(editor, _shownSession) && editor.Version == _shownVersion)
        {
            return;
        }
        ReadOnlySpan<byte> pixels = editor.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            // Values are 0-15 by the session's invariant; index 0 shows as opaque ink, the
            // same honest reading the encoder writes to disk (transparency is Palt's runtime
            // meaning, not a sheet fact).
            _sheetPixels[i] = _palette[pixels[i]];
        }
        _sheetTexture.SetData(_sheetPixels);
        _shownSession = editor;
        _shownVersion = editor.Version;
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="EditorChromeRenderer.DrawButton"/>
    /// owns. What only this screen can answer is decided here: which buttons read as active
    /// (the sprites tab, the tool in hand, the layer tab you are on), which face a group slot
    /// wears — its CURRENT variant's glyph (the wave's card), with the corner marker drawn over
    /// it in the same ink as the photoshop cue that more hides underneath — and which buttons
    /// are text-faced at all (<see cref="EditorIcons.ButtonText"/> owns that list).
    /// </summary>
    private void DrawButtons(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            bool active = place.Id == EditorButton.SpritesTab
                || (place.Id == EditorButton.ToolSelect && editor.Tool == SpriteEditorTool.Select)
                || (place.Id == EditorButton.ToolPencil && editor.Tool == SpriteEditorTool.Pencil)
                || (place.Id == EditorButton.ToolFill && editor.Tool == SpriteEditorTool.Fill)
                || (place.Id == EditorButton.ToolStamp && editor.Tool == SpriteEditorTool.Stamp)
                || (place.Id == EditorButton.ToolShape && editor.Tool == SpriteEditorTool.Shape)
                || (place.Id >= EditorButton.LayerTab1 && place.Id <= EditorButton.LayerTab5
                    && editor.ActiveLayerIndex == place.Id - EditorButton.LayerTab1);
            var state = new EditorButtonState(
                Active: active,
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: editor.IsDirty,
                CanUndo: editor.CanUndo,
                CanRedo: editor.CanRedo);
            // The face — text or icon, exactly one — has one owner since the crash repair:
            // EditorIcons.Face, the same call EditorButtonFaceTests drives headless. The old
            // inline pick asked VariantIcon/IconFor for every button, and the first
            // text-faced one (the size toggle) threw on the editor's first windowed frame.
            (string? text, EditorIcon? icon) = EditorIcons.Face(place.Id, editor);
            Color color = _chrome.DrawButton(batch, layout.Chrome, place, state, icon, text);
            if (EditorIcons.IsGroupSlot(place.Id))
            {
                DrawGroupMarker(batch, layout, place.Rect, color);
            }
        }
    }

    /// <summary>
    /// The corner marker of a group slot: a small stepped triangle in the bottom-right corner,
    /// built from three ui-sized quads (no glyph — it must overlay any variant icon). Same ink
    /// as the icon, so a dim state dims the cue with it.
    /// </summary>
    private void DrawGroupMarker(SpriteBatch batch, in SpriteEditorLayout layout, Rectangle slot, Color color)
    {
        int u = layout.Ui;
        for (int step = 0; step < 3; step++)
        {
            batch.Draw(
                _chrome.White,
                new Rectangle(slot.Right - u * (step + 1), slot.Bottom - u * (3 - step), u, u * (3 - step)),
                color);
        }
    }

    /// <summary>
    /// The open group flyout: the slot's variants as ordinary buttons floating right of it,
    /// the remembered variant on the active-blue fill. Drawn on Ink plates because the row
    /// overlaps the canvas — a variant icon over sheet pixels would be unreadable. The size
    /// toggle's variants are text (8/16/32), same faces as the slot itself.
    /// </summary>
    private void DrawFlyout(
        SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor,
        EditorButton? flyoutSlot, HoverTarget? hover)
    {
        if (flyoutSlot is not EditorButton slot)
        {
            return;
        }
        int current = EditorIcons.CurrentVariant(editor, slot);
        for (int i = 0; i < EditorIcons.GroupVariantCount(slot); i++)
        {
            Rectangle rect = layout.FlyoutVariantRect(slot, i);
            bool hovered = hover is HoverTarget target && target.FlyoutSlot == slot && target.FlyoutVariant == i;
            batch.Draw(_chrome.White, rect, i == current ? ActiveBg : Ink);
            _chrome.DrawFrame(batch, rect, 1, hovered ? Bright : Dim);
            Color ink = i == current ? Bright : Text;
            if (slot == EditorButton.SizeToggle)
            {
                string label = EditorIcons.SizeLabel(EditorIcons.SizeVariantCells(i));
                _chrome.Font.Draw(
                    batch, label,
                    rect.X + (rect.Width - PixelFontAtlas.MeasureWidth(label, layout.Ui)) / 2,
                    rect.Y + (rect.Height - PixelFontAtlas.LineHeight(layout.Ui)) / 2,
                    layout.Ui, ink);
            }
            else
            {
                _chrome.Icons.Draw(batch, EditorIcons.VariantIcon(slot, i), layout.ButtonIconRect(rect), ink);
            }
        }
    }

    private void DrawCanvas(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, double timeSeconds)
    {
        // The frame is what separates sheet-ink pixels from the ink-cleared window behind them.
        _chrome.DrawFrame(batch, layout.Canvas, layout.Ui, Dim);
        int size = VirtualConsole.SpriteSize;
        var region = new Rectangle(
            editor.RegionCellX * size, editor.RegionCellY * size, layout.RegionPixels, layout.RegionPixels);
        // One quad: the canvas is the sheet texture's region rectangle scaled by a whole
        // integer under PointClamp — the zoom IS the draw, there is no second pixel store.
        batch.Draw(_sheetTexture, layout.Canvas, region, Color.White);

        // The shape preview, straight from the session's point list — the very pixels the
        // commit will plot, tinted with the color they will get. Drawn as canvas-scale quads
        // over the sheet quad: the preview never enters the sheet (or its texture), so
        // cancelling a gesture costs nothing and dirt/undo stay untouched until the release.
        if (editor.ShapeActive)
        {
            foreach ((int px, int py) in editor.ShapePreview)
            {
                batch.Draw(_chrome.White, PixelRect(layout, px, py), _palette[editor.CurrentColor]);
            }
        }

        DrawSelection(batch, layout, editor, timeSeconds);
        DrawStampGhost(batch, layout, editor);

        // The canvas cursor — where the keyboard pencil is and what the status bar's
        // coordinates read. A frame around the pixel, not over it: the color being placed
        // must stay visible under the cursor.
        _chrome.DrawFrame(batch, PixelRect(layout, editor.CursorX, editor.CursorY), Math.Max(1, layout.Ui / 2), Bright);
    }

    /// <summary>One region pixel's quad on the canvas — the single mapping the shape preview, the selection, the ghost and the cursor all share.</summary>
    private static Rectangle PixelRect(in SpriteEditorLayout layout, int x, int y) =>
        new(layout.Canvas.X + x * layout.CanvasScale,
            layout.Canvas.Y + y * layout.CanvasScale,
            layout.CanvasScale, layout.CanvasScale);

    /// <summary>
    /// The selection made visible — marching ants on the mask's true boundary (the owner's
    /// third review: the old blue wash and white bounding box are gone), rebuilt from the
    /// session's mask every frame with the phase <see cref="SelectionOutline"/> derives from
    /// the draw clock. Drawn only under the select tool, the same gate the stamp ghost keeps:
    /// the session already guarantees no mask survives a tool switch, and the gate states the
    /// same law at the one place the overlay could appear. While a move floats, the picture
    /// still splits honestly into what the drop would produce — the holes show color 0, the
    /// lifted pixels ride at the offset, and the ants ride with them. All of it is overlay
    /// quads; none of it is in the sheet or its texture, which is why cancelling a move costs
    /// nothing and why no outline can ever reach a saved PNG.
    /// </summary>
    private void DrawSelection(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, double timeSeconds)
    {
        if (editor.Tool != SpriteEditorTool.Select || !editor.HasSelection)
        {
            return;
        }
        int n = layout.RegionPixels;
        int dx = editor.MoveActive ? editor.MoveOffsetX : 0;
        int dy = editor.MoveActive ? editor.MoveOffsetY : 0;
        // Two passes while moving: every hole first, then every lifted pixel — a fragment
        // pixel may land on another's hole, and landings must win, exactly as the drop writes.
        if (editor.MoveActive)
        {
            int sheetX0 = editor.RegionCellX * VirtualConsole.SpriteSize;
            int sheetY0 = editor.RegionCellY * VirtualConsole.SpriteSize;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (editor.IsSelected(x, y))
                    {
                        batch.Draw(_chrome.White, PixelRect(layout, x, y), _palette[0]);
                    }
                }
            }
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (editor.IsSelected(x, y))
                    {
                        byte color = editor.Pixels[(sheetY0 + y) * VirtualConsole.SheetWidth + sheetX0 + x];
                        batch.Draw(_chrome.White, PixelRect(layout, x + dx, y + dy), _palette[color]);
                    }
                }
            }
        }
        // The ants outline the mask as shown — shifted while floating, which the predicate's
        // one subtraction handles (out-of-range asks answer false, so borders stay honest).
        // Dash length and thickness scale with the ui unit like every piece of chrome.
        int dashLength = Math.Max(2, 3 * layout.Ui);
        SelectionOutline.Collect(
            (x, y) => editor.IsSelected(x - dx, y - dy), n, layout.CanvasScale, dashLength,
            Math.Max(1, layout.Ui / 2), SelectionOutline.Phase(timeSeconds, dashLength), _ants);
        foreach (AntDash dash in _ants)
        {
            batch.Draw(
                _chrome.White,
                new Rectangle(layout.Canvas.X + dash.X, layout.Canvas.Y + dash.Y, dash.Width, dash.Height),
                dash.Bright ? Bright : Ink);
        }
    }

    /// <summary>
    /// The stamp's ghost: the source at half strength under the cursor, placed by the very
    /// <see cref="SpriteEditorSession.StampOrigin"/> the print uses and clipped at the region
    /// the way the print will be — the author sees exactly what the click commits. Session
    /// state only; the sheet texture is never touched, and an inkless stamp shows nothing
    /// (its tooltip explains why).
    /// </summary>
    private void DrawStampGhost(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        if (editor.Tool != SpriteEditorTool.Stamp || !editor.HasStampSource)
        {
            return;
        }
        (int destX, int destY) = editor.StampOrigin(editor.CursorX, editor.CursorY);
        for (int sy = 0; sy < editor.StampHeight; sy++)
        {
            for (int sx = 0; sx < editor.StampWidth; sx++)
            {
                byte color = editor.StampPixelAt(sx, sy);
                int x = destX + sx;
                int y = destY + sy;
                if (color == 0 || x < 0 || x >= layout.RegionPixels || y < 0 || y >= layout.RegionPixels)
                {
                    continue;   // transparent source, or the part the border will clip off the print
                }
                batch.Draw(_chrome.White, PixelRect(layout, x, y), _palette[color] * 0.5f);
            }
        }
    }

    private void DrawSwatches(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        for (int i = 0; i < Palette.VisibleCount; i++)
        {
            Rectangle rect = layout.SwatchRect(i);
            // Every swatch gets a dim 1 px frame so color 0 (ink on the ink-cleared window)
            // has a visible body; the current color's frame is bright and exactly gap-thick,
            // filling the space between swatches without covering a neighbour — visible even
            // when the current color is white, because thickness carries the signal, not hue.
            bool current = i == editor.CurrentColor;
            _chrome.DrawFrame(batch, rect, current ? layout.Ui : 1, current ? Bright : Dim);
            batch.Draw(_chrome.White, rect, _palette[i]);
        }
    }

    /// <summary>
    /// The eight flag toggles (wave 3b-2), bit 0 leftmost. Three states, straight from the
    /// reference row (REFERENCES-EDITORS §2.1, TIC-80's <c>drawFlags</c>): raised on every
    /// sprite of the region — a filled cell; raised on some of them — an empty cell with a
    /// centre dot; raised on none — an empty cell. At region 8 px "every" and "some" are the
    /// same sprite, so the dot simply never appears; at 16 or 32 it is the only honest thing
    /// to draw, because one square cannot say "half of these four".
    ///
    /// <para>Fill and emptiness carry the signal, not hue — the same discipline the swatch
    /// frames use, and the reason the row stays readable for an author who cannot separate
    /// blue from grey. The painted square is <see cref="SpriteEditorLayout.FlagMarkRect"/>, two
    /// thirds of the cell the pointer may land on: the mark is the reference row's small square,
    /// the cell around it is what makes it easy to hit, and only the layout knows either.</para>
    /// </summary>
    private void DrawFlags(
        SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, HoverTarget? hover)
    {
        for (int bit = 0; bit < SpriteEditorSession.FlagBits; bit++)
        {
            Rectangle mark = layout.FlagMarkRect(bit);
            bool hovered = hover is HoverTarget target && target.Flag == bit;
            batch.Draw(_chrome.White, mark, Ink);
            _chrome.DrawFrame(batch, mark, 1, hovered ? Bright : Text);
            if (editor.IsFlagSetInAll(bit))
            {
                batch.Draw(_chrome.White, mark, Bright);
            }
            else if (editor.IsFlagSetInAny(bit))
            {
                int dot = Math.Max(1, mark.Width / 3);
                batch.Draw(
                    _chrome.White,
                    new Rectangle(
                        mark.X + (mark.Width - dot) / 2, mark.Y + (mark.Height - dot) / 2, dot, dot),
                    Text);
            }
        }
    }

    private void DrawSheet(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, int scroll)
    {
        _chrome.DrawFrame(batch, layout.Sheet, layout.Ui, Dim);
        int visibleRight = scroll + layout.SheetVisiblePixels;
        for (int lane = 0; lane < SheetStrip.Lanes; lane++)
        {
            // Mapping the lane's first sprite through the shared owner gives both the strip
            // destination and canonical texture row. Drawing one lane-block instead of every
            // individual cell keeps the presentation transform cheap without duplicating its
            // page arithmetic here — and it is why the sixth review's taller strip needed no
            // new drawing code, only SheetStrip's own Rows.
            int firstSprite = lane * SheetStrip.Rows * SheetStrip.LaneColumns;
            SheetStrip.SpriteToStripCell(firstSprite, out int stripColumn, out _);
            int laneStart = stripColumn * VirtualConsole.SpriteSize;
            int laneEnd = laneStart + VirtualConsole.SheetWidth;
            int clippedStart = Math.Max(scroll, laneStart);
            int clippedEnd = Math.Min(visibleRight, laneEnd);
            if (clippedStart >= clippedEnd)
            {
                continue;
            }

            var source = new Rectangle(
                clippedStart - laneStart,
                lane * SheetStrip.Rows * VirtualConsole.SpriteSize,
                clippedEnd - clippedStart,
                SheetStrip.PixelHeight);
            var drawn = new Rectangle(
                layout.Sheet.X + (clippedStart - scroll) * layout.SheetScale,
                layout.Sheet.Y,
                source.Width * layout.SheetScale,
                source.Height * layout.SheetScale);
            batch.Draw(_sheetTexture, drawn, source, Color.White);
        }

        // Highlight geometry is cell-derived because one canonical region may straddle two
        // strip pages. A single bounding box would falsely select the empty gap between them.
        // Sprite 0 wears a dim frame wherever the strip shows it: it is the map's empty tile,
        // and the author is told so here — where he draws — and not only after he has carried
        // the art to the map screen and found it missing.
        foreach (Rectangle zero in layout.SheetRegionHighlights(0, 0, 1, scroll))
        {
            Rectangle visibleZero = Rectangle.Intersect(zero, layout.Sheet);
            if (visibleZero.Width > 0 && visibleZero.Height > 0)
            {
                _chrome.DrawFrame(batch, visibleZero, Math.Max(1, layout.Ui / 2), Dim);
            }
        }

        foreach (Rectangle highlight in layout.SheetRegionHighlights(
            editor.RegionCellX, editor.RegionCellY, editor.RegionCells, scroll))
        {
            Rectangle visible = Rectangle.Intersect(highlight, layout.Sheet);
            if (visible.Width > 0 && visible.Height > 0)
            {
                _chrome.DrawFrame(batch, visible, Math.Max(1, layout.Ui / 2), Bright);
            }
        }
    }

    /// <summary>
    /// The sheet window's horizontal scroll slider (wave 2i): the track in the strip tone,
    /// the thumb from the very <see cref="SpriteEditorLayout.SheetThumb"/> the drag inverts.
    /// Until wave 2k the strip (64 columns) always overflowed the window and the thumb was
    /// always live. It is 32 columns now, so a wide-enough window — 2560x720 and up after the
    /// seventh review's tool column, measured and not guessed — shows the whole strip and the
    /// thumb honestly fills the track, with drags
    /// as no-ops; <c>SheetScrollTests</c> pins that case. It brightens under the pointer and
    /// while dragging, like every hovered control.
    /// </summary>
    private void DrawSlider(SpriteBatch batch, in SpriteEditorLayout layout, SheetScroll scroll, HoverTarget? hover)
    {
        _chrome.DrawFrame(batch, layout.SheetSlider, 1, Dim);
        batch.Draw(_chrome.White, layout.SheetSlider, StripBg);
        bool hot = scroll.Dragging || (hover is HoverTarget target && target.Slider);
        batch.Draw(_chrome.White, layout.SheetThumb(scroll.Offset), hot ? Bright : Text);
    }

    /// <summary>
    /// The tooltip's sprite-editor half: which text and which anchor. This screen has five
    /// kinds of hover target (button, flyout variant, slider, flag toggle, swatch) and each names its own
    /// tooltip and its own rectangle; the box itself belongs to the shared painter, which is
    /// where the flip-and-clamp rules live for both editors.
    /// </summary>
    private void DrawTooltip(
        SpriteBatch batch, in SpriteEditorLayout layout, int width, int height,
        SpriteEditorSession editor, HoverTarget? hover, bool tooltipVisible)
    {
        if (hover is not HoverTarget target || !tooltipVisible)
        {
            return;
        }
        string text =
            target.Button is EditorButton button ? EditorIcons.Tooltip(button, editor)
            : target.FlyoutSlot is EditorButton slot ? EditorIcons.VariantTooltip(slot, target.FlyoutVariant)
            : target.Slider ? EditorIcons.SliderTooltip
            : target.Flag >= 0 ? EditorIcons.FlagTooltip(target.Flag)
            : EditorIcons.SwatchTooltip(target.Swatch);
        Rectangle anchor =
            target.Button is EditorButton anchorButton ? layout.ButtonRect(anchorButton)
            : target.FlyoutSlot is EditorButton anchorSlot ? layout.FlyoutVariantRect(anchorSlot, target.FlyoutVariant)
            : target.Slider ? layout.SheetSlider
            : target.Flag >= 0 ? layout.FlagRect(target.Flag)
            : layout.SwatchRect(target.Swatch);
        _chrome.DrawTooltip(batch, layout.Chrome, width, height, text, anchor);
    }
}

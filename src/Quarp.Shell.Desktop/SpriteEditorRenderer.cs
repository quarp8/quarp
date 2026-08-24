using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the sprite editor screen in the owner's verdict shape (M9 stage 2.5, sixth review
/// applied): the icon-only tab strip and the status bar as tinted full-width bands, the left
/// toolbar column with its group slots (corner-marked, flyout on demand), the zoomed canvas
/// with the keyboard cursor, the shape preview, the selection as marching ants (plus the
/// holes and floating fragment during a move) and the stamp ghost — all session-state
/// overlays, never sheet pixels — the right column (palette at the window's edge, the sixth
/// review's one narrow row of size toggle plus layer tabs, and under it the sheet window
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
    private readonly PixelFontAtlas _font;
    private readonly EditorIconAtlas _icons;
    private readonly Texture2D _white;
    private readonly Texture2D _sheetTexture;
    private readonly Color[] _sheetPixels;

    /// <summary>Palette lookup, unpacked once — 16 K pixels per sheet upload should not re-shift RGB each time.</summary>
    private readonly Color[] _palette;

    // Which session and which of its versions the sheet texture currently shows. The session
    // reference matters: a fresh session starts at Version 0, and matching versions across
    // different sessions would leave the previous cart's sheet on screen.
    private SpriteEditorSession? _shownSession;
    private int _shownVersion;

    // Palette roles, same cast as the library's (Palette.cs documented visible slots).
    private static readonly Color Ink = PaletteColors.Opaque(0);
    private static readonly Color Dim = PaletteColors.Opaque(1);
    private static readonly Color Text = PaletteColors.Opaque(2);
    private static readonly Color Bright = PaletteColors.Opaque(3);
    private static readonly Color ActiveBg = PaletteColors.Opaque(4);   // blue: the library's selection bar, reused as "this is on"
    private static readonly Color Warn = PaletteColors.Opaque(8);       // yellow: the exit prompt and the modified icon — a decision, not a failure
    private static readonly Color Error = PaletteColors.Opaque(10);     // red: a save that did not happen

    // The strips' background (owner's second review: the tab and status bands must separate
    // from the window instead of melting into it). Master32[16] is the ink's own secret twin —
    // the "twilight lift of near-black" (Palette.cs) — so the bands read as raised chrome one
    // honest step lighter than the Ink-cleared window while Text and Dim keep their contrast
    // on top of it.
    private static readonly Color StripBg = PaletteColors.Opaque(16);

    // The marching ants' dash buffer, reused every frame: the outline is rebuilt each Draw
    // (it marches), but the list is not reallocated sixty times a second.
    private readonly List<AntDash> _ants = new();

    public SpriteEditorRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _font = new PixelFontAtlas(device);
        _icons = new EditorIconAtlas(device);
        _white = new Texture2D(device, 1, 1);
        _white.SetData(new[] { Color.White });
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
        batch.Draw(_white, layout.TabStrip, StripBg);
        batch.Draw(_white, layout.StatusBar, StripBg);

        DrawCanvas(batch, layout, editor, timeSeconds);
        DrawButtons(batch, layout, editor, hover);
        DrawSwatches(batch, layout, editor);
        DrawSheet(batch, layout, editor, scroll.Offset);
        DrawSlider(batch, layout, scroll, hover);
        DrawStatusText(batch, layout, editor);
        DrawPromptLine(batch, layout, editor);
        DrawFlyout(batch, layout, editor, flyoutSlot, hover);
        DrawTooltip(batch, layout, width, height, editor, hover, tooltipVisible);

        batch.End();
    }

    public void Dispose()
    {
        _font.Dispose();
        _icons.Dispose();
        _white.Dispose();
        _sheetTexture.Dispose();
    }

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
    /// Every icon-button through the one mechanism: state decides the ink, hover decides the
    /// frame. Stubs are dim (visible, honest, dead); the active tool, the sprites tab and the
    /// active layer tab get the library's blue bar as a background — thickness and fill carry
    /// the signal, not hue alone. The save button is also the dirty indicator: the modified
    /// glyph in warn yellow while unsaved work exists, the plain floppy otherwise. Group
    /// slots show their CURRENT variant's glyph (the wave's card) plus a corner marker — the
    /// photoshop cue that more hides underneath. Text-faced buttons (the size toggle's
    /// number, the layer tabs' digits — <see cref="EditorIcons.ButtonText"/> owns which) draw
    /// the font instead of a glyph, centred the way the icons are.
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
            bool hovered = hover is HoverTarget target && target.Button == place.Id;
            Color color =
                place.Id == EditorButton.Save && editor.IsDirty ? Warn
                : place.Id == EditorButton.Undo && !editor.CanUndo ? Dim
                : place.Id == EditorButton.Redo && !editor.CanRedo ? Dim
                : EditorIcons.IsStub(place.Id) ? Dim
                : active ? Bright
                : Text;
            if (active)
            {
                batch.Draw(_white, place.Rect, ActiveBg);
            }
            DrawFrame(batch, place.Rect, 1, hovered ? Bright : Dim);
            if (EditorIcons.ButtonText(place.Id, editor) is string text)
            {
                _font.Draw(
                    batch, text,
                    place.Rect.X + (place.Rect.Width - PixelFontAtlas.MeasureWidth(text, layout.Ui)) / 2,
                    place.Rect.Y + (place.Rect.Height - PixelFontAtlas.LineHeight(layout.Ui)) / 2,
                    layout.Ui, color);
            }
            else
            {
                EditorIcon icon = place.Id == EditorButton.Save
                    ? editor.IsDirty ? EditorIcon.Modified : EditorIcon.Saved
                    : EditorIcons.IsGroupSlot(place.Id)
                        ? EditorIcons.VariantIcon(place.Id, CurrentVariant(editor, place.Id))
                        : EditorIcons.IconFor(place.Id);
                _icons.Draw(batch, icon, layout.ButtonIconRect(place.Rect), color);
            }
            if (EditorIcons.IsGroupSlot(place.Id))
            {
                DrawGroupMarker(batch, layout, place.Rect, color);
            }
        }
    }

    /// <summary>The session's remembered variant of a group slot, as the flyout index <see cref="EditorIcons.VariantIcon"/> expects.</summary>
    private static int CurrentVariant(SpriteEditorSession editor, EditorButton slot) => slot switch
    {
        EditorButton.ToolSelect => (int)editor.CurrentSelection,
        EditorButton.ToolShape => (int)editor.CurrentShape,
        EditorButton.SizeToggle => EditorIcons.SizeVariantOf(editor.RegionCells),
        _ => (int)editor.CurrentTransform,
    };

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
                _white,
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
        int current = CurrentVariant(editor, slot);
        for (int i = 0; i < EditorIcons.GroupVariantCount(slot); i++)
        {
            Rectangle rect = layout.FlyoutVariantRect(slot, i);
            bool hovered = hover is HoverTarget target && target.FlyoutSlot == slot && target.FlyoutVariant == i;
            batch.Draw(_white, rect, i == current ? ActiveBg : Ink);
            DrawFrame(batch, rect, 1, hovered ? Bright : Dim);
            Color ink = i == current ? Bright : Text;
            if (slot == EditorButton.SizeToggle)
            {
                string label = EditorIcons.SizeLabel(EditorIcons.SizeVariantCells(i));
                _font.Draw(
                    batch, label,
                    rect.X + (rect.Width - PixelFontAtlas.MeasureWidth(label, layout.Ui)) / 2,
                    rect.Y + (rect.Height - PixelFontAtlas.LineHeight(layout.Ui)) / 2,
                    layout.Ui, ink);
            }
            else
            {
                _icons.Draw(batch, EditorIcons.VariantIcon(slot, i), layout.ButtonIconRect(rect), ink);
            }
        }
    }

    private void DrawCanvas(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, double timeSeconds)
    {
        // The frame is what separates sheet-ink pixels from the ink-cleared window behind them.
        DrawFrame(batch, layout.Canvas, layout.Ui, Dim);
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
                batch.Draw(_white, PixelRect(layout, px, py), _palette[editor.CurrentColor]);
            }
        }

        DrawSelection(batch, layout, editor, timeSeconds);
        DrawStampGhost(batch, layout, editor);

        // The canvas cursor — where the keyboard pencil is and what the status bar's
        // coordinates read. A frame around the pixel, not over it: the color being placed
        // must stay visible under the cursor.
        DrawFrame(batch, PixelRect(layout, editor.CursorX, editor.CursorY), Math.Max(1, layout.Ui / 2), Bright);
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
                        batch.Draw(_white, PixelRect(layout, x, y), _palette[0]);
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
                        batch.Draw(_white, PixelRect(layout, x + dx, y + dy), _palette[color]);
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
                _white,
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
                batch.Draw(_white, PixelRect(layout, x, y), _palette[color] * 0.5f);
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
            DrawFrame(batch, rect, current ? layout.Ui : 1, current ? Bright : Dim);
            batch.Draw(_white, rect, _palette[i]);
        }
    }

    private void DrawSheet(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, int scroll)
    {
        DrawFrame(batch, layout.Sheet, layout.Ui, Dim);
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
        foreach (Rectangle highlight in layout.SheetRegionHighlights(
            editor.RegionCellX, editor.RegionCellY, editor.RegionCells, scroll))
        {
            Rectangle visible = Rectangle.Intersect(highlight, layout.Sheet);
            if (visible.Width > 0 && visible.Height > 0)
            {
                DrawFrame(batch, visible, Math.Max(1, layout.Ui / 2), Bright);
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
        DrawFrame(batch, layout.SheetSlider, 1, Dim);
        batch.Draw(_white, layout.SheetSlider, StripBg);
        bool hot = scroll.Dragging || (hover is HoverTarget target && target.Slider);
        batch.Draw(_white, layout.SheetThumb(scroll.Offset), hot ? Bright : Text);
    }

    /// <summary>
    /// The status bar's text half (its buttons are drawn with all the others): the cursor's
    /// position in <b>sheet</b> pixels — the coordinate an author would type into code — and
    /// the sprite number, which is Spr(n)'s n for the region's anchor cell.
    /// </summary>
    private void DrawStatusText(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        int size = VirtualConsole.SpriteSize;
        string coords =
            $"{editor.RegionCellX * size + editor.CursorX:D3},{editor.RegionCellY * size + editor.CursorY:D3}";
        // The band spans the whole window, so the text takes the screen margin, not the band's X.
        _font.Draw(batch, coords, layout.Margin, layout.StatusTextY, layout.Ui, Text);
        _font.Draw(
            batch, $"#{editor.SpriteIndex:D3}",
            layout.Margin + PixelFontAtlas.MeasureWidth(coords + "   ", layout.Ui),
            layout.StatusTextY, layout.Ui, Bright);
    }

    /// <summary>
    /// The reserved line above the status bar: the dirty-exit prompt when it is up (its three
    /// verbs are the clickable rectangles <see cref="SpriteEditorLayout.PromptVerbRect"/> owns —
    /// mouse parity for Z/X/Esc), otherwise the last save error if there is one, then the
    /// out-of-sync notice (ADR-027: gfx.png was edited outside while gfx-layers.png stood —
    /// the stack wins and the next save will overwrite, which must be announced, not sprung).
    /// When several exist each moves one line up rather than being traded away: a failed
    /// save is why the prompt is still up, and hiding any of them would lie.
    /// </summary>
    private void DrawPromptLine(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        int lineY = layout.PromptY;
        int lineStep = PixelFontAtlas.LineHeight(layout.Ui) + layout.Ui;
        if (editor.ExitPromptShown)
        {
            _font.Draw(batch, SpriteEditorLayout.PromptHeading, layout.Margin, layout.PromptY, layout.Ui, Warn);
            DrawPromptVerb(batch, layout, EditorPromptVerb.SaveAndExit, SpriteEditorLayout.PromptSaveVerb);
            DrawPromptVerb(batch, layout, EditorPromptVerb.Discard, SpriteEditorLayout.PromptDiscardVerb);
            DrawPromptVerb(batch, layout, EditorPromptVerb.Stay, SpriteEditorLayout.PromptStayVerb);
            lineY -= lineStep;
        }
        if (editor.SaveError is string error)
        {
            _font.Draw(batch, $"SAVE FAILED: {error}".ToUpperInvariant(), layout.Margin, lineY, layout.Ui, Error);
            lineY -= lineStep;
        }
        if (editor.GfxOutOfSyncOnDisk)
        {
            _font.Draw(
                batch, "GFX.PNG EDITED OUTSIDE - LAYERS WIN, SAVING OVERWRITES IT",
                layout.Margin, lineY, layout.Ui, Warn);
        }
    }

    private void DrawPromptVerb(SpriteBatch batch, in SpriteEditorLayout layout, EditorPromptVerb verb, string text)
    {
        Rectangle rect = layout.PromptVerbRect(verb);
        DrawFrame(batch, rect, 1, Warn);
        _font.Draw(batch, text, rect.X + layout.Ui, rect.Y + layout.Ui, layout.Ui, Bright);
    }

    /// <summary>
    /// The tooltip, last so it sits over everything: name + hotkey from <see cref="EditorIcons"/>,
    /// anchored under the hovered rectangle, flipped above it when the bottom of the window is
    /// too close, and clamped into the horizontal margins — a label that runs off screen
    /// answers nothing.
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
            : EditorIcons.SwatchTooltip(target.Swatch);
        Rectangle anchor =
            target.Button is EditorButton anchorButton ? layout.ButtonRect(anchorButton)
            : target.FlyoutSlot is EditorButton anchorSlot ? layout.FlyoutVariantRect(anchorSlot, target.FlyoutVariant)
            : target.Slider ? layout.SheetSlider
            : layout.SwatchRect(target.Swatch);
        int boxWidth = PixelFontAtlas.MeasureWidth(text, layout.Ui) + 2 * layout.Ui;
        int boxHeight = PixelFontAtlas.LineHeight(layout.Ui) + 2 * layout.Ui;
        int x = Math.Clamp(anchor.X, layout.Margin, Math.Max(layout.Margin, width - layout.Margin - boxWidth));
        int y = anchor.Bottom + 2 * layout.Ui;
        if (y + boxHeight > height - layout.Margin)
        {
            y = anchor.Y - 2 * layout.Ui - boxHeight;
        }
        var box = new Rectangle(x, y, boxWidth, boxHeight);
        batch.Draw(_white, box, Ink);
        DrawFrame(batch, box, 1, Bright);
        _font.Draw(batch, text, box.X + layout.Ui, box.Y + layout.Ui, layout.Ui, Text);
    }

    /// <summary>A rectangle outline of the given thickness drawn <b>outside</b> <paramref name="rect"/>, so content is never covered.</summary>
    private void DrawFrame(SpriteBatch batch, Rectangle rect, int thickness, Color color)
    {
        int t = thickness;
        batch.Draw(_white, new Rectangle(rect.X - t, rect.Y - t, rect.Width + 2 * t, t), color);
        batch.Draw(_white, new Rectangle(rect.X - t, rect.Bottom, rect.Width + 2 * t, t), color);
        batch.Draw(_white, new Rectangle(rect.X - t, rect.Y, t, rect.Height), color);
        batch.Draw(_white, new Rectangle(rect.Right, rect.Y, t, rect.Height), color);
    }
}

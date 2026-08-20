using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the sprite editor screen in the owner's verdict shape (M9 stage 2.5): the icon-only
/// tab strip, the left toolbar with its action row, the zoomed canvas with the keyboard
/// cursor, the right column (palette, layers stub, sheet), the status bar (coordinates,
/// sprite number, save/undo/redo), the reserved prompt line and the hover tooltips. Host UI
/// like <see cref="LibraryRenderer"/> — window-native resolution, <see cref="Palette.Master32"/>
/// colors, the system font and the icon strip — and just as unable to touch a framebuffer or
/// a hash: no cartridge runs while this draws.
///
/// <para>All geometry comes from <see cref="SpriteEditorLayout"/>, the same struct the shell
/// hit-tests the mouse against; this class owns only pixels-on-screen. The sheet lives in one
/// 128x128 texture drawn twice — scaled up for the canvas (source rectangle = the region) and
/// scaled down-ish for the sheet view — re-uploaded only when
/// <see cref="SpriteEditorSession.Version"/> moves, so an idle editor costs a handful of
/// quads per frame.</para>
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
    /// </summary>
    public void Draw(
        SpriteBatch batch, int width, int height, SpriteEditorSession editor,
        HoverTarget? hover, bool tooltipVisible)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(editor);
        var layout = SpriteEditorLayout.Compute(width, height, editor.RegionCells);
        UploadSheetIfChanged(editor);

        _device.Clear(Ink);
        batch.Begin(samplerState: SamplerState.PointClamp);

        DrawCanvas(batch, layout, editor);
        DrawButtons(batch, layout, editor, hover);
        DrawSwatches(batch, layout, editor);
        DrawLayersStub(batch, layout);
        DrawSheet(batch, layout, editor);
        DrawStatusText(batch, layout, editor);
        DrawPromptLine(batch, layout, editor);
        DrawTooltip(batch, layout, width, height, hover, tooltipVisible);

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
    /// frame. Stubs are dim (visible, honest, dead); the active tool and the sprites tab get
    /// the library's blue bar as a background — thickness and fill carry the signal, not hue
    /// alone. The save button is also the dirty indicator: the modified glyph in warn yellow
    /// while unsaved work exists, the plain floppy otherwise.
    /// </summary>
    private void DrawButtons(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            bool active = place.Id == EditorButton.SpritesTab
                || (place.Id == EditorButton.ToolPencil && editor.Tool == SpriteEditorTool.Pencil)
                || (place.Id == EditorButton.ToolFill && editor.Tool == SpriteEditorTool.Fill);
            bool hovered = hover is HoverTarget target && target.Button == place.Id;
            EditorIcon icon = place.Id == EditorButton.Save
                ? editor.IsDirty ? EditorIcon.Modified : EditorIcon.Saved
                : EditorIcons.IconFor(place.Id);
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
            _icons.Draw(batch, icon, layout.ButtonIconRect(place.Rect), color);
        }
    }

    private void DrawCanvas(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        // The frame is what separates sheet-ink pixels from the ink-cleared window behind them.
        DrawFrame(batch, layout.Canvas, layout.Ui, Dim);
        int size = VirtualConsole.SpriteSize;
        var region = new Rectangle(
            editor.RegionCellX * size, editor.RegionCellY * size, layout.RegionPixels, layout.RegionPixels);
        // One quad: the canvas is the sheet texture's region rectangle scaled by a whole
        // integer under PointClamp — the zoom IS the draw, there is no second pixel store.
        batch.Draw(_sheetTexture, layout.Canvas, region, Color.White);

        // The canvas cursor — where the keyboard pencil is and what the status bar's
        // coordinates read. A frame around the pixel, not over it: the color being placed
        // must stay visible under the cursor.
        var cursor = new Rectangle(
            layout.Canvas.X + editor.CursorX * layout.CanvasScale,
            layout.Canvas.Y + editor.CursorY * layout.CanvasScale,
            layout.CanvasScale, layout.CanvasScale);
        DrawFrame(batch, cursor, Math.Max(1, layout.Ui / 2), Bright);
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

    /// <summary>
    /// The layers placeholder the owner ordered for this stage: one dim row naming the single
    /// implicit layer. Real layers (an authoring file beside gfx.png, flattened on save) are
    /// a separate wave by the owner's decision — this block reserves their place on screen so
    /// the layout does not reshuffle when they land.
    /// </summary>
    private void DrawLayersStub(SpriteBatch batch, in SpriteEditorLayout layout)
    {
        DrawFrame(batch, layout.LayersStub, 1, Dim);
        _font.Draw(
            batch, "BASE LAYER",
            layout.LayersStub.X + 2 * layout.Ui, layout.LayersStub.Y + layout.Ui, layout.Ui, Dim);
    }

    private void DrawSheet(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        DrawFrame(batch, layout.Sheet, layout.Ui, Dim);
        batch.Draw(_sheetTexture, layout.Sheet, null, Color.White);
        // The region cursor: a bright frame around the selected cells, drawn outside them so
        // it never covers the pixels being edited.
        int cell = layout.SheetScale * VirtualConsole.SpriteSize;
        var selected = new Rectangle(
            layout.Sheet.X + editor.RegionCellX * cell,
            layout.Sheet.Y + editor.RegionCellY * cell,
            editor.RegionCells * cell,
            editor.RegionCells * cell);
        DrawFrame(batch, selected, Math.Max(1, layout.Ui / 2), Bright);
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
        _font.Draw(batch, coords, layout.StatusBar.X + layout.Ui, layout.StatusTextY, layout.Ui, Text);
        _font.Draw(
            batch, $"#{editor.SpriteIndex:D3}",
            layout.StatusBar.X + layout.Ui + PixelFontAtlas.MeasureWidth(coords + "   ", layout.Ui),
            layout.StatusTextY, layout.Ui, Bright);
    }

    /// <summary>
    /// The reserved line above the status bar: the dirty-exit prompt when it is up (its three
    /// verbs are the clickable rectangles <see cref="SpriteEditorLayout.PromptVerbRect"/> owns —
    /// mouse parity for Z/X/Esc), otherwise the last save error if there is one. When both
    /// exist the error moves one line up rather than being traded away: a failed save is why
    /// the prompt is still up, and hiding either would lie.
    /// </summary>
    private void DrawPromptLine(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        int errorY = layout.PromptY;
        if (editor.ExitPromptShown)
        {
            errorY = layout.PromptY - PixelFontAtlas.LineHeight(layout.Ui) - layout.Ui;
            _font.Draw(batch, SpriteEditorLayout.PromptHeading, layout.Margin, layout.PromptY, layout.Ui, Warn);
            DrawPromptVerb(batch, layout, EditorPromptVerb.SaveAndExit, SpriteEditorLayout.PromptSaveVerb);
            DrawPromptVerb(batch, layout, EditorPromptVerb.Discard, SpriteEditorLayout.PromptDiscardVerb);
            DrawPromptVerb(batch, layout, EditorPromptVerb.Stay, SpriteEditorLayout.PromptStayVerb);
        }
        if (editor.SaveError is string error)
        {
            _font.Draw(batch, $"SAVE FAILED: {error}".ToUpperInvariant(), layout.Margin, errorY, layout.Ui, Error);
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
        HoverTarget? hover, bool tooltipVisible)
    {
        if (hover is not HoverTarget target || !tooltipVisible)
        {
            return;
        }
        string text = target.Button is EditorButton button
            ? EditorIcons.Tooltip(button)
            : EditorIcons.SwatchTooltip(target.Swatch);
        Rectangle anchor = target.Button is EditorButton anchorButton
            ? layout.ButtonRect(anchorButton)
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

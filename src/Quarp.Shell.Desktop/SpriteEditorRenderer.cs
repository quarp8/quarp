using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the sprite editor screen: header, the zoomed region canvas, the 16 palette swatches,
/// the whole-sheet grid with the region cursor, and the footer (hints, the unsaved-changes
/// prompt, save errors). Host UI like <see cref="LibraryRenderer"/> — window-native
/// resolution, <see cref="Palette.Master32"/> colors, the system font — and just as unable to
/// touch a framebuffer or a hash: no cartridge runs while this draws.
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
    private static readonly Color Warn = PaletteColors.Opaque(8);      // yellow: the exit prompt, a decision, not a failure
    private static readonly Color Error = PaletteColors.Opaque(10);    // red: a save that did not happen

    public SpriteEditorRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _font = new PixelFontAtlas(device);
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

    /// <summary>One frame of the editor. Owns the whole surface (clears, begins and ends the batch), like the library does.</summary>
    public void Draw(SpriteBatch batch, int width, int height, SpriteEditorSession editor)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(editor);
        var layout = SpriteEditorLayout.Compute(width, height, editor.RegionCells);
        UploadSheetIfChanged(editor);

        _device.Clear(Ink);
        batch.Begin(samplerState: SamplerState.PointClamp);

        DrawHeader(batch, layout, editor);
        DrawCanvas(batch, layout, editor);
        DrawSwatches(batch, layout, editor);
        DrawSheet(batch, layout, editor);
        DrawFooter(batch, layout, editor);

        batch.End();
    }

    public void Dispose()
    {
        _font.Dispose();
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

    private void DrawHeader(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        _font.Draw(batch, "SPRITE EDITOR", layout.Margin, layout.HeaderY, layout.Ui * 2, Bright);
        // The star is the dirty flag the author can see; the sprite number is Spr(n)'s n for
        // the selected cell, so what they read here is what they type in code.
        string detail = $"{editor.CartName}{(editor.IsDirty ? "*" : "")}  #{editor.SpriteIndex:D3}";
        _font.Draw(
            batch, detail,
            layout.Margin + PixelFontAtlas.MeasureWidth("SPRITE EDITOR ", layout.Ui * 2),
            layout.HeaderY + PixelFontAtlas.LineHeight(layout.Ui),
            layout.Ui, Dim);
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

    private void DrawFooter(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        if (editor.SaveError is string error)
        {
            _font.Draw(
                batch, $"SAVE FAILED: {error}".ToUpperInvariant(),
                layout.Margin, layout.FooterY - PixelFontAtlas.LineHeight(layout.Ui) - layout.Ui,
                layout.Ui, Error);
        }
        if (editor.ExitPromptShown)
        {
            // The work order's exact contract, as a footer line and not a modal: Z saves and
            // leaves, X leaves without saving, Esc stays.
            _font.Draw(
                batch, "UNSAVED CHANGES:  Z SAVE AND EXIT   X EXIT WITHOUT SAVING   ESC STAY",
                layout.Margin, layout.FooterY, layout.Ui, Warn);
            return;
        }
        _font.Draw(
            batch, "LMB DRAW   RMB PICK   CTRL+Z UNDO   CTRL+Y REDO   CTRL+S SAVE   ESC BACK",
            layout.Margin, layout.FooterY, layout.Ui, Dim);
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

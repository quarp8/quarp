using Microsoft.Xna.Framework;
using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where everything on the sprite editor screen sits, as a pure function of the window size
/// and the region size — computed fresh each frame, like every host-UI measurement (see
/// <see cref="LibraryRenderer"/>'s type comment on why that is safe here and nowhere near a
/// hash). This is the geometry's <b>single owner</b>: <see cref="SpriteEditorRenderer"/> draws
/// these rectangles and <c>QuarpGame</c> hit-tests the mouse against the same ones, so a
/// swatch can never be painted in one place and clicked in another.
///
/// <para><b>Every scale is a whole integer</b>, floored at 1: the canvas is the region's
/// pixels multiplied up (the niche's "zoom is a bigger pencil target, not a lens"), the sheet
/// view is the 128x128 sheet multiplied up, and fractional scales would resample pixel art
/// into blur (ARCHITECTURE §5's rule, applied to host UI).</para>
///
/// <para>The shape: header strip on top, footer strip at the bottom, the canvas fills the
/// left half between them, and the right column stacks the 16 swatches (two rows of eight)
/// over the sheet grid. In a pathologically small window the parts keep scale 1 and may
/// overflow to the right or bottom — clipped, not crashed; the shell's default window is 8x
/// the console and the floor exists for resizes, not for use.</para>
/// </summary>
public readonly struct SpriteEditorLayout
{
    private const int SwatchColumns = 8;
    private const int SwatchRows = Palette.VisibleCount / SwatchColumns;

    /// <summary>Host-UI text scale, same anchor the library uses (<see cref="PixelFontAtlas.UiScale"/>).</summary>
    public int Ui { get; private init; }

    /// <summary>Screen-edge inset, in window pixels — the library's 4 * ui, kept identical so the modes read as one shell.</summary>
    public int Margin { get; private init; }

    /// <summary>Baseline of the header text.</summary>
    public int HeaderY { get; private init; }

    /// <summary>Baseline of the footer key-hint / prompt line (the lower of the two footer rows).</summary>
    public int FooterY { get; private init; }

    /// <summary>
    /// Baseline of the status row above the hints — active tool, region size, mouse meanings
    /// (wave 2c). Owned by the layout rather than improvised in the renderer so the canvas
    /// bottom is computed above it: an always-on second footer row that overlapped the canvas
    /// would hide the very pixels being edited.
    /// </summary>
    public int StatusY { get; private init; }

    /// <summary>The zoomed region view — the surface the pencil paints on.</summary>
    public Rectangle Canvas { get; private init; }

    /// <summary>Window pixels per region pixel on the canvas.</summary>
    public int CanvasScale { get; private init; }

    /// <summary>The whole-sheet view with the region cursor.</summary>
    public Rectangle Sheet { get; private init; }

    /// <summary>Window pixels per sheet pixel in the sheet view.</summary>
    public int SheetScale { get; private init; }

    /// <summary>Side of one palette swatch, in window pixels.</summary>
    public int SwatchSize { get; private init; }

    /// <summary>Bounding box of all 16 swatches — the renderer frames it, the hit test pre-filters with it.</summary>
    public Rectangle Swatches { get; private init; }

    /// <summary>Region side in pixels, denormalized from the session so hit tests need only the layout.</summary>
    public int RegionPixels { get; private init; }

    public static SpriteEditorLayout Compute(int width, int height, int regionCells)
    {
        int ui = PixelFontAtlas.UiScale(width, height);
        int margin = 4 * ui;
        int headerY = margin;
        int footerY = height - margin - PixelFontAtlas.LineHeight(ui);
        int statusY = footerY - PixelFontAtlas.LineHeight(ui) - ui;
        int top = headerY + PixelFontAtlas.LineHeight(2 * ui) + 2 * ui;
        int bottom = statusY - 2 * ui;
        int regionPixels = regionCells * VirtualConsole.SpriteSize;

        // The canvas takes the left half: pixel-art editing lives or dies by target size, so
        // the drawing surface gets the single largest block the window can give it.
        int canvasScale = Math.Max(
            1, Math.Min((width / 2 - 2 * margin) / regionPixels, (bottom - top) / regionPixels));
        var canvas = new Rectangle(margin, top, regionPixels * canvasScale, regionPixels * canvasScale);

        int rightX = canvas.Right + margin;
        int rightWidth = Math.Max(0, width - rightX - margin);

        // Swatches want to be finger-big but must never push the sheet off screen, so their
        // size follows the column width down; the gap of one ui pixel keeps neighbouring
        // colors from fusing into a gradient.
        int gap = ui;
        int swatchSize = Math.Max(4, Math.Min(12 * ui, (rightWidth - (SwatchColumns - 1) * gap) / SwatchColumns));
        var swatches = new Rectangle(
            rightX, top,
            SwatchColumns * swatchSize + (SwatchColumns - 1) * gap,
            SwatchRows * swatchSize + (SwatchRows - 1) * gap);

        int sheetTop = swatches.Bottom + 2 * ui;
        int sheetScale = Math.Max(
            1, Math.Min(rightWidth / VirtualConsole.SheetWidth, (bottom - sheetTop) / VirtualConsole.SheetHeight));
        var sheet = new Rectangle(
            rightX, sheetTop, VirtualConsole.SheetWidth * sheetScale, VirtualConsole.SheetHeight * sheetScale);

        return new SpriteEditorLayout
        {
            Ui = ui,
            Margin = margin,
            HeaderY = headerY,
            FooterY = footerY,
            StatusY = statusY,
            Canvas = canvas,
            CanvasScale = canvasScale,
            Sheet = sheet,
            SheetScale = sheetScale,
            SwatchSize = swatchSize,
            Swatches = swatches,
            RegionPixels = regionPixels,
        };
    }

    /// <summary>Swatch rectangle for a visible palette index — the one place swatch geometry exists.</summary>
    public Rectangle SwatchRect(int color)
    {
        int gap = Ui;
        return new Rectangle(
            Swatches.X + color % SwatchColumns * (SwatchSize + gap),
            Swatches.Y + color / SwatchColumns * (SwatchSize + gap),
            SwatchSize,
            SwatchSize);
    }

    /// <summary>Window point → region-local pixel, or false when the point is off the canvas (a press, not a drag).</summary>
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
    /// Window point → nearest region-local pixel, for drags: a stroke whose cursor leaves the
    /// canvas keeps painting along the edge instead of tearing (the clamp is why
    /// <see cref="SpriteEditorSession.Paint"/> can afford to throw on out-of-range input).
    /// Division truncates toward zero, which for points left/above the canvas lands on 0-ish
    /// values the clamp fixes anyway.
    /// </summary>
    public void ClampCanvasPixel(int x, int y, out int localX, out int localY)
    {
        localX = Math.Clamp((x - Canvas.X) / CanvasScale, 0, RegionPixels - 1);
        localY = Math.Clamp((y - Canvas.Y) / CanvasScale, 0, RegionPixels - 1);
    }

    /// <summary>Window point → sheet grid cell (0-15 each way), or false when the point is off the sheet view.</summary>
    public bool TrySheetCell(int x, int y, out int cellX, out int cellY)
    {
        if (!Sheet.Contains(x, y))
        {
            cellX = 0;
            cellY = 0;
            return false;
        }
        int cellPixels = SheetScale * VirtualConsole.SpriteSize;
        cellX = (x - Sheet.X) / cellPixels;
        cellY = (y - Sheet.Y) / cellPixels;
        return true;
    }

    /// <summary>Window point → visible palette index, or false. Sixteen rectangle checks, on a click, not per frame.</summary>
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

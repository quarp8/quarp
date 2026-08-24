using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The one owner of the PICO-8 sheet-strip mapping. The session keeps the canonical 16x16
/// sheet; only this view lays its pages side by side into a wide, short strip. Keeping both
/// directions here prevents rendering and hit-testing from acquiring subtly different page
/// arithmetic.
///
/// <para><b>The strip's shape is one number: <see cref="Rows"/>.</b> The fifth review asked
/// for four rows (64x4, PICO-8's four pages end to end); the sixth review (2026-08-24) gave
/// the window the freed half of the right column and rejected that shape by name — "лист
/// показывает четыре ряда ... где мог бы показать больше". A 16:1 strip in a 2:1 window can
/// only fill it by blowing cells up until FEWER sprites fit, the opposite of the order, so
/// the same 256 sprites are re-cut as two 16x8 lanes (pages 0-1 stacked, then 2-3): 128
/// sprites on screen instead of 48, and the strip still overflows, so the slider the owner
/// kept stays real. Everything downstream reads these constants — nothing hard-codes 4 or
/// 64 — so a seventh review's band is one edit here, for any <see cref="Rows"/> that divides
/// the sheet's 16 rows (1, 2, 4, 8, 16). A band that does not divide them would leave the last
/// lane ragged; <c>SheetScrollTests.StripMappingRoundTripsEverySprite</c> is what turns that
/// red rather than letting sprite 255 slide into a column that is not there.</para>
/// </summary>
public static class SheetStrip
{
    /// <summary>Sprite rows the strip is tall — the sixth review's band, two PICO-8 pages stacked.</summary>
    public const int Rows = 8;

    /// <summary>Sprite cells across one lane: the canonical sheet's own width, never re-cut.</summary>
    public const int LaneColumns = VirtualConsole.SheetColumns;

    /// <summary>
    /// How many lanes stand side by side to hold all <see cref="VirtualConsole.SpriteCount"/>
    /// sprites: the sheet's own rows cut into bands of <see cref="Rows"/>. The audit of
    /// 2026-08-24 caught this dividing the sheet's WIDTH instead — right only because the sheet
    /// is square, and quietly wrong for any band that does not divide 16.
    /// </summary>
    public const int Lanes = SheetRows / Rows;

    /// <summary>Sprite rows the canonical sheet is tall — the other half of the shape being re-cut.</summary>
    public const int SheetRows = VirtualConsole.SpriteCount / VirtualConsole.SheetColumns;

    public const int Columns = LaneColumns * Lanes;
    public const int PixelWidth = Columns * VirtualConsole.SpriteSize;
    public const int PixelHeight = Rows * VirtualConsole.SpriteSize;

    /// <summary>Canonical sprite number to its cell in the presentation strip.</summary>
    public static void SpriteToStripCell(int sprite, out int column, out int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sprite);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sprite, VirtualConsole.SpriteCount);

        int sheetRow = sprite / LaneColumns;
        int lane = sheetRow / Rows;
        column = LaneColumns * lane + sprite % LaneColumns;
        row = sheetRow % Rows;
    }

    /// <summary>Strip cell back to the canonical sheet cell consumed by the session.</summary>
    public static bool TryStripCellToSheetCell(int column, int row, out int sheetX, out int sheetY)
    {
        sheetX = 0;
        sheetY = 0;
        if ((uint)column >= Columns || (uint)row >= Rows)
        {
            return false;
        }

        int lane = column / LaneColumns;
        sheetX = column % LaneColumns;
        sheetY = Rows * lane + row;
        return true;
    }
}

/// <summary>
/// The sheet strip's horizontal scroll state (M9 stage 2.5 wave 2i) — the one piece of the
/// slider that must survive between frames, headless like <see cref="ToolbarFlyout"/> and for
/// the same reason: the shell feeds it drags, wheel ticks and key presses, and the negative
/// control ("a drag past the track's end must not scroll past the sheet") is a plain unit
/// test instead of a mouse at a window.
///
/// <para>The offset is in <b>sheet pixels</b>, not window pixels, so the drawn slice always
/// starts on a whole texture column and stays crisp at any scale. All the geometry —
/// where the thumb is, what offset a drag position means, what the ceiling is — belongs to
/// <see cref="SpriteEditorLayout"/>, the single owner of editor geometry; this class only
/// remembers the answer and the fact that a drag is in progress. Every writer funnels
/// through the layout's clamp or <see cref="Clamp"/>, so no path can scroll past the
/// sheet's border, including a window resize that shrinks the ceiling mid-session.</para>
/// </summary>
public sealed class SheetScroll
{
    /// <summary>Current offset, strip pixels 0..<see cref="SpriteEditorLayout.SheetMaxScroll"/>.</summary>
    public int Offset { get; private set; }

    /// <summary>True while a slider drag owns the mouse — the shell must not read the same frames as canvas strokes.</summary>
    public bool Dragging { get; private set; }

    /// <summary>Press on the slider track: the thumb jumps under the pointer and the drag begins.</summary>
    public void BeginDrag(in SpriteEditorLayout layout, int mouseX)
    {
        Dragging = true;
        Offset = layout.SheetScrollForSliderX(mouseX);
    }

    /// <summary>One frame of an open drag. Safe without one — releases arrive when the press landed elsewhere.</summary>
    public void DragTo(in SpriteEditorLayout layout, int mouseX)
    {
        if (Dragging)
        {
            Offset = layout.SheetScrollForSliderX(mouseX);
        }
    }

    /// <summary>The button came up — wherever it was, the drag is over.</summary>
    public void EndDrag() => Dragging = false;

    /// <summary>The wheel's and the [ ] keys' step: sheet pixels, clamped at both borders.</summary>
    public void ScrollBy(in SpriteEditorLayout layout, int deltaSheetPixels) =>
        Offset = Math.Clamp(Offset + deltaSheetPixels, 0, layout.SheetMaxScroll);

    /// <summary>
    /// Re-clamp against the current layout — called once per frame, because a window resize
    /// or a scale change can shrink the ceiling under a standing offset, and a stale offset
    /// would hit-test pixels that are no longer drawn.
    /// </summary>
    public void Clamp(in SpriteEditorLayout layout) =>
        Offset = Math.Clamp(Offset, 0, layout.SheetMaxScroll);
}

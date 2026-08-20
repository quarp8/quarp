namespace Quarp.Shell.Desktop;

/// <summary>
/// The sheet window's horizontal scroll state (M9 stage 2.5 wave 2h) — the one piece of the
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
    /// <summary>Current offset, sheet pixels 0..<see cref="SpriteEditorLayout.SheetMaxScroll"/>. 0 whenever the whole sheet fits.</summary>
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

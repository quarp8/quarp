using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Quarp.Shell.Desktop.EditorChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the code editor screen in the shell standard, applied to text: the icon-only tab strip
/// and the status bar as tinted full-width bands, the find/go-to tool column left of the page,
/// the line-number gutter, the text itself in the console's own 3x5 type, the selection as a
/// band under the glyphs, a blinking caret, the vertical scrollbar, the reserved prompt line and
/// the hover tooltips. Host UI like its two siblings — window-native resolution,
/// <see cref="Quarp.Core.Palette.Master32"/> colours, the system font and the icon strip — and
/// just as unable to touch a framebuffer or a hash: no cartridge runs while this draws.
///
/// <para>Everything the three editor screens paint the same way comes from
/// <see cref="EditorChromeRenderer"/>; this class owns the page. All geometry comes from
/// <see cref="CodeEditorLayout"/>, the same struct <see cref="CodeEditorInput"/> hit-tests the
/// mouse against, so a glyph cannot be drawn in one place and clicked in another.</para>
///
/// <para><b>No syntax highlighting in this wave, and it is not an oversight.</b> LIKO-12 has it
/// (<c>Libraries.SyntaxHighlighter</c> with a nine-colour theme) and TIC-80 has it per language;
/// both colour a language they own the lexer for. Ours is C#, our palette has sixteen entries of
/// which four are already spoken for by chrome, and the honest implementations are a real
/// tokenizer (Roslyn is already a dependency of <c>Quarp.CartKit</c> — but it lives on the other
/// side of a module boundary and lexing per frame is a cost nobody has measured) or a regex
/// guess that will mis-colour verbatim strings and interpolation the first day. That is a
/// decision with a shape of its own, named in the report as this wave's chief candidate, not a
/// line to be smuggled into a renderer.</para>
///
/// <para><b>The caret blinks on the draw clock</b>, like the sprite editor's marching ants:
/// <paramref name="elapsedSeconds"/> is host time, it reaches no simulation and no hash, and
/// the blink stops (the caret stays lit) while the exit prompt is up, because a blinking caret
/// under a question the author is answering reads as an invitation to type.</para>
/// </summary>
public sealed class CodeEditorRenderer : IDisposable
{
    /// <summary>Caret blinks per second — TIC-80's own rate, and the one every terminal uses.</summary>
    private const double BlinkHz = 2.0;

    private readonly GraphicsDevice _device;
    private readonly EditorChromeRenderer _chrome;

    public CodeEditorRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _chrome = new EditorChromeRenderer(device);
    }

    /// <summary>
    /// One frame of the code editor. Owns the whole surface (clears, begins and ends the batch)
    /// like the other two host screens. <paramref name="view"/> is the very scroll the router's
    /// hit tests read, so the picture and the clicks cannot disagree; <paramref name="hover"/>
    /// and <paramref name="tooltipVisible"/> come from the shell's <see cref="IconHoverTracker"/>
    /// — frame highlight now, label after its three seconds; <paramref name="elapsedSeconds"/> is
    /// the window's total draw time, and only the caret's blink reads it.
    /// </summary>
    public void Draw(
        SpriteBatch batch, int width, int height, CodeEditorSession session, CodeEditorView view,
        HoverTarget? hover, bool tooltipVisible, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        var layout = CodeEditorLayout.Compute(width, height);

        _device.Clear(Ink);
        batch.Begin(samplerState: SamplerState.PointClamp);

        _chrome.DrawBands(batch, layout.Chrome);
        _chrome.DrawFrame(batch, layout.Text, layout.Ui, Dim);

        DrawSelection(batch, layout, session, view);
        DrawGutterAndText(batch, layout, session, view);
        DrawCaret(batch, layout, session, view, elapsedSeconds);
        DrawScrollBar(batch, layout, session, view);
        DrawButtons(batch, layout, session, view, hover);

        int bytes = session.ByteCount;
        _chrome.DrawStatusText(
            batch, layout.Chrome, Coordinates(session), $"SIZE {bytes}/{CodeEditorSession.MaxByteCount}",
            bytes > CodeEditorSession.MaxByteCount ? Error : (Color?)null);
        _chrome.DrawPromptLine(
            batch, layout.Chrome, view.ExitPromptShown, session.SaveError, StandingNotice(session, view));
        DrawTooltip(batch, layout, width, height, hover, tooltipVisible);

        batch.End();
    }

    public void Dispose() => _chrome.Dispose();

    /// <summary>
    /// The status band's left field: where the caret is, in the numbers the author sees.
    /// TIC-80's <c>line %i/%i col %i</c> and LIKO-12's <c>LINE x/y CHAR a/b</c> agree on the
    /// shape and disagree on the second pair; we take TIC-80's, because "which column am I in"
    /// is what a compiler error names and "how long is this line" is not.
    /// </summary>
    public static string Coordinates(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"LINE {session.CursorLine + 1}/{session.LineCount} COL {session.CursorColumn + 1}";
    }

    /// <summary>
    /// The screen's standing line under the prompt and the save error, in precedence order: the
    /// footer field that is up (it IS this line while it lives — TIC-80 puts find and goto on
    /// the status row the same way, and LIKO-12 replaces its line counter with <c>ISRCH:</c>),
    /// otherwise the over-budget warning. The limit is reported and never enforced
    /// (<see cref="CodeEditorSession"/>'s own contract: an editor that stops accepting text is an
    /// editor that loses it), so this line and the red size field are the whole of the screen's
    /// opinion about it.
    /// </summary>
    public static string? StandingNotice(CodeEditorSession session, CodeEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        if (view.FindShown)
        {
            return $"FIND: {view.FindText}   ENTER NEXT   ESC CLOSES";
        }
        if (view.GoToShown)
        {
            return $"GO TO LINE: {view.GoToText}   ENTER JUMPS   ESC CLOSES";
        }
        int over = session.ByteCount - CodeEditorSession.MaxByteCount;
        return over > 0 ? $"OVER THE CODE LIMIT BY {over} BYTES - THIS CART WILL NOT LOAD" : null;
    }

    /// <summary>
    /// The selection as a band per line, drawn <em>under</em> the glyphs so the text stays
    /// readable inside it. A line whose break is swallowed by the selection gets one extra
    /// column of band, which is how every editor shows "the newline is selected too" — without
    /// it a selected empty line would be invisible.
    /// </summary>
    private void DrawSelection(
        SpriteBatch batch, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view)
    {
        if (!session.HasSelection)
        {
            return;
        }
        CodePosition start = session.SelectionStart;
        CodePosition end = session.SelectionEnd;
        int lastVisible = view.FirstLine + layout.VisibleLines - 1;
        int rightEdge = view.FirstColumn + layout.VisibleColumns;
        for (int line = Math.Max(start.Line, view.FirstLine); line <= Math.Min(end.Line, lastVisible); line++)
        {
            int from = line == start.Line ? start.Column : 0;
            int to = line == end.Line ? end.Column : session.Lines[line].Length + 1;
            from = Math.Clamp(from, view.FirstColumn, rightEdge);
            to = Math.Clamp(to, view.FirstColumn, rightEdge);
            if (to <= from)
            {
                continue;
            }
            batch.Draw(
                _chrome.White, layout.RowSpanRect(line, from, to, view.FirstLine, view.FirstColumn),
                ActiveBg);
        }
    }

    /// <summary>
    /// The gutter and the page, one visible row at a time. Numbers are dim except the caret's
    /// own, which is the cheapest possible "you are here" and the one LIKO-12 spends a whole
    /// status line on; the text is clipped to the window by substring rather than by a scissor
    /// rectangle, because the font draws one quad per character and a character off the right
    /// edge is a quad nobody should pay for.
    /// </summary>
    private void DrawGutterAndText(
        SpriteBatch batch, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view)
    {
        int scale = layout.TextScale;
        for (int row = 0; row < layout.VisibleLines; row++)
        {
            int index = view.FirstLine + row;
            if (index >= session.LineCount)
            {
                break;
            }
            int y = layout.Text.Y + row * layout.LineHeight;
            string number = $"{index + 1}";
            _chrome.Font.Draw(
                batch, number,
                layout.Gutter.Right - layout.Ui - PixelFontAtlas.MeasureWidth(number, scale), y, scale,
                index == session.CursorLine ? Bright : Dim);

            string line = session.Lines[index];
            if (view.FirstColumn >= line.Length)
            {
                continue;
            }
            int count = Math.Min(line.Length - view.FirstColumn, layout.VisibleColumns);
            _chrome.Font.Draw(
                batch, line.Substring(view.FirstColumn, count), layout.Text.X, y, scale, Text);
        }
    }

    /// <summary>
    /// The caret: a bar on the left edge of the cell it sits in, one text pixel wide, blinking
    /// at <see cref="BlinkHz"/> off the draw clock. Drawn only when it is actually inside the
    /// window — the view's follow rule normally guarantees that, but the wheel is allowed to
    /// scroll away from it, and a caret painted on the frame's edge would be a lie.
    /// </summary>
    private void DrawCaret(
        SpriteBatch batch, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view,
        double elapsedSeconds)
    {
        bool lit = view.ExitPromptShown || (int)(elapsedSeconds * BlinkHz) % 2 == 0;
        if (!lit)
        {
            return;
        }
        int line = session.CursorLine;
        int column = session.CursorColumn;
        if (line < view.FirstLine || line >= view.FirstLine + layout.VisibleLines
            || column < view.FirstColumn || column > view.FirstColumn + layout.VisibleColumns)
        {
            return;
        }
        Rectangle cell = layout.CellRect(line, column, view.FirstLine, view.FirstColumn);
        batch.Draw(
            _chrome.White, new Rectangle(cell.X, cell.Y, Math.Max(1, layout.TextScale), cell.Height),
            Bright);
    }

    /// <summary>
    /// The scrollbar: a dim track the height of the page with a bright thumb on it. It is the
    /// mouse's only long-distance road through a file (the wheel walks, the thumb jumps), and
    /// the one place the author can see how much of the buffer is off screen — the same job the
    /// map's minimap does for a map eighty screens wide.
    /// </summary>
    private void DrawScrollBar(
        SpriteBatch batch, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view)
    {
        batch.Draw(_chrome.White, layout.ScrollBar, StripBg);
        _chrome.DrawFrame(batch, layout.ScrollBar, Math.Max(1, layout.Ui / 2), Dim);
        batch.Draw(
            _chrome.White, layout.ScrollThumbRect(view.FirstLine, session.LineCount),
            view.ScrollDragActive ? Bright : Text);
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="EditorChromeRenderer.DrawButton"/>
    /// owns. The only decision this screen makes is which buttons read as active: its own tab,
    /// and whichever footer field is up — so the find button lights while the find line lives,
    /// which is what tells the author that Esc has something to close.
    /// </summary>
    private void DrawButtons(
        SpriteBatch batch, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view,
        HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            var state = new EditorButtonState(
                Active: place.Id == EditorButton.CodeTab
                    || (place.Id == EditorButton.ToolFind && view.FindShown)
                    || (place.Id == EditorButton.ToolGoTo && view.GoToShown),
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: session.IsDirty,
                CanUndo: session.CanUndo,
                CanRedo: session.CanRedo);
            _chrome.DrawButton(
                batch, layout.Chrome, place, state, EditorIcons.IconFor(place.Id), text: null);
        }
    }

    /// <summary>The tooltip's code half: one lookup in <see cref="EditorIcons.CodeTooltip"/>; the box belongs to the shared painter.</summary>
    private void DrawTooltip(
        SpriteBatch batch, in CodeEditorLayout layout, int width, int height,
        HoverTarget? hover, bool tooltipVisible)
    {
        if (hover is not HoverTarget target || target.Button is not EditorButton button || !tooltipVisible)
        {
            return;
        }
        _chrome.DrawTooltip(
            batch, layout.Chrome, width, height,
            EditorIcons.CodeTooltip(button), layout.ButtonRect(button));
    }
}

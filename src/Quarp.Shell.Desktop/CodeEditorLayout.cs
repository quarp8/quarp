using Microsoft.Xna.Framework;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where everything on the <b>code</b> editor screen sits, as a pure function of the window
/// size — the third member of the family <see cref="SpriteEditorLayout"/> and
/// <see cref="MapEditorLayout"/> started, and its <b>single owner</b> of geometry:
/// <see cref="CodeEditorRenderer"/> draws these rectangles and <see cref="CodeEditorInput"/>
/// hit-tests the mouse against the very same ones, so a character can never be painted in one
/// place and clicked in another.
///
/// <para>The shared frame — the tab band, the status band, the reserved prompt line, the
/// margins and the button side — is measured by <see cref="EditorChrome"/>, exactly as the
/// other two screens measure theirs. What this file adds is the three boxes a text editor is
/// made of: the <see cref="Text"/> field, the <see cref="Gutter"/> of line numbers left of it,
/// and the vertical <see cref="ScrollBar"/> at the window's right edge.</para>
///
/// <para><b>Pure function of the window size, and nothing else.</b> Notably <em>not</em> of the
/// document: the gutter is <see cref="GutterDigits"/> characters wide whatever the file holds.
/// A gutter that grew when the buffer crossed line 100 would shift every character of every
/// line sideways in the middle of typing — the one thing a text editor must never do. Numbers
/// wider than the gutter are drawn right-aligned against the text field and grow leftwards
/// into the gutter's own padding, so they stay readable instead of pushing the code.</para>
///
/// <para><b>Why the code has its own text scale.</b> <see cref="EditorChrome"/>'s scale is
/// chosen for chrome legibility (<see cref="PixelFontMetrics.UiScale"/>); code wants
/// <em>rows</em>. <see cref="TextScale"/> is therefore one rung down that same ladder, floored
/// at 2 like the ladder itself — at 1280x720 that is 28 lines of 90 columns instead of 21 of
/// 67. It is a choice about density, not a second owner of text metrics: every width and every
/// line advance below still comes from <see cref="PixelFontMetrics"/>.</para>
///
/// <para><b>The text box holds whole characters and whole lines.</b> Its width is trimmed to a
/// multiple of <see cref="CharWidth"/> and its height to a multiple of <see cref="LineHeight"/>,
/// the same trim the map canvas does to whole cells — which is what makes
/// <see cref="VisibleLines"/> and <see cref="VisibleColumns"/> exact rather than "about", and
/// what lets a click divide instead of round.</para>
/// </summary>
public readonly struct CodeEditorLayout
{
    /// <summary>
    /// How many digits the line-number gutter reserves. Five covers 99999 lines; the 256 KB
    /// budget (<see cref="CodeEditorSession.MaxByteCount"/>) could in principle hold more empty
    /// ones, and those numbers simply reach left into the gutter's padding rather than moving
    /// the code — see the type note on why this number is fixed at all.
    /// </summary>
    public const int GutterDigits = 5;

    /// <summary>
    /// The status band's row, outermost first: redo, undo, save. Slot 0 — the sprite screen's
    /// Clear — stays empty here as it does on the map, so the three shared buttons keep the
    /// pixels the author's hand already knows on every editor screen.
    /// </summary>
    private static readonly EditorButton?[] _statusSlots =
    {
        null, EditorButton.Redo, EditorButton.Undo, EditorButton.Save,
    };

    /// <summary>
    /// The left tool column, top to bottom. TIC-80's code toolbar in the two entries we have
    /// verbs for (REFERENCES-EDITORS §4.1: <c>FIND [ctrl+f]</c>, <c>GOTO</c>); bookmarks and
    /// the outline have no model behind them, and a button with nothing behind it is the defect
    /// class the button contract closed.
    /// </summary>
    private static readonly EditorButton[] _toolColumn =
    {
        EditorButton.ToolFind, EditorButton.ToolGoTo,
    };

    /// <summary>The frame this screen stands in — bands, margins, button size, prompt line.</summary>
    public EditorChrome Chrome { get; private init; }

    // Forwarded, not recomputed — EditorChrome is the only place these exist.
    public int Ui => Chrome.Ui;

    public int Margin => Chrome.Margin;

    public int ButtonSize => Chrome.ButtonSize;

    public Rectangle TabStrip => Chrome.TabStrip;

    public Rectangle StatusBar => Chrome.StatusBar;

    public int PromptY => Chrome.PromptY;

    /// <summary>The eleven placed buttons — six tabs, a tool column of two, three status buttons.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>Whole-integer glyph scale of the code itself — see the type note on why it differs from <see cref="Ui"/>.</summary>
    public int TextScale { get; private init; }

    /// <summary>The line-number strip, left of the text and the same height.</summary>
    public Rectangle Gutter { get; private init; }

    /// <summary>The text field: a whole number of characters by a whole number of lines.</summary>
    public Rectangle Text { get; private init; }

    /// <summary>The vertical scrollbar's track, at the window's right margin and the text's height.</summary>
    public Rectangle ScrollBar { get; private init; }

    /// <summary>Window pixels per character cell — the number every horizontal hit test divides by.</summary>
    public int CharWidth => PixelFontMetrics.MeasureWidth(" ", TextScale);

    /// <summary>Window pixels per text row.</summary>
    public int LineHeight => PixelFontMetrics.LineHeight(TextScale);

    /// <summary>How many lines the text field shows. Whole by construction: the box is trimmed to rows.</summary>
    public int VisibleLines => Text.Height / LineHeight;

    /// <summary>How many columns the text field shows. Whole for the same reason.</summary>
    public int VisibleColumns => Text.Width / CharWidth;

    public static CodeEditorLayout Compute(int width, int height)
    {
        var buttons = new EditorButtonPlace[11];
        int placed = 0;
        EditorChrome chrome = EditorChrome.Compute(width, height, buttons, ref placed, _statusSlots);

        int ui = chrome.Ui;
        int margin = chrome.Margin;
        int button = chrome.ButtonSize;
        int top = chrome.ContentTop;
        int bottom = chrome.ContentBottom;

        // One rung down the chrome's ladder, floored where the ladder itself floors.
        int textScale = Math.Max(2, ui - 1);
        int charWidth = PixelFontMetrics.MeasureWidth(" ", textScale);
        int lineHeight = PixelFontMetrics.LineHeight(textScale);

        // Left to right: the tool column, one margin, the gutter, one ui, the text, one ui, the
        // scrollbar, one margin, the window's edge.
        int gutterX = margin + button + margin;
        int gutterWidth = GutterDigits * charWidth + 2 * ui;
        int barWidth = 2 * ui;
        int barX = width - margin - barWidth;
        int textX = gutterX + gutterWidth + ui;

        // Floors of one cell each. A window too small for a single character is clipped, not
        // crashed — the same floor, and the same carded debt
        // (tasks/open/debt-tiny-window-layout.md), the other two screens already document.
        int roomWidth = Math.Max(charWidth, barX - ui - textX);
        int roomHeight = Math.Max(lineHeight, bottom - top);
        var text = new Rectangle(
            textX, top, roomWidth / charWidth * charWidth, roomHeight / lineHeight * lineHeight);
        var gutter = new Rectangle(gutterX, text.Y, gutterWidth, text.Height);
        var scrollBar = new Rectangle(barX, text.Y, barWidth, text.Height);

        // The tool column grows DOWNWARD only, like the map's: widening it would move the text
        // field's left edge and change how much code is on screen.
        for (int i = 0; i < _toolColumn.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _toolColumn[i],
                Rect = new Rectangle(margin, top + i * (button + chrome.Gap), button, button),
            };
        }

        return new CodeEditorLayout
        {
            Chrome = chrome,
            Buttons = buttons,
            TextScale = textScale,
            Gutter = gutter,
            Text = text,
            ScrollBar = scrollBar,
        };
    }

    /// <summary>The placed rectangle of one button — the tooltip anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id) => EditorChrome.ButtonRect(Buttons, id);

    /// <summary>The icon's destination inside a button: centred, at scale <see cref="Ui"/>.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => Chrome.ButtonIconRect(buttonRect);

    /// <summary>Window point → button under it, stubs included (hover needs the dead ones too).</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        EditorChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="EditorChrome"/> owns the prompt line for every screen.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Window point → prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    /// <summary>
    /// Window point → the character cell under it, given where the view is scrolled, or false
    /// off the text field. The scroll is added AFTER the division, so the answer is a place in
    /// the whole document and never in the window — and it is deliberately <em>not</em> clamped
    /// to the line's length: <see cref="CodeEditorSession.SetCursor"/> owns that clamp, which is
    /// what makes a click past the end of a short line land at its end.
    /// </summary>
    public bool TryTextCell(int x, int y, int firstLine, int firstColumn, out int line, out int column)
    {
        line = 0;
        column = 0;
        if (!Text.Contains(x, y))
        {
            return false;
        }
        line = firstLine + (y - Text.Y) / LineHeight;
        column = firstColumn + (x - Text.X) / CharWidth;
        return true;
    }

    /// <summary>
    /// Window point → nearest <em>visible</em> character cell, for drags: a selection whose
    /// pointer leaves the text field keeps extending along its edge instead of tearing, exactly
    /// as <see cref="MapEditorLayout.ClampMapCell"/> does for the map canvas. Floored rather
    /// than truncated so a pointer above or left of the box keeps counting the right way — C#
    /// division rounds toward zero, and a drag that sticks for a whole cell at the edge is what
    /// that looks like.
    /// </summary>
    public void ClampTextCell(int x, int y, int firstLine, int firstColumn, out int line, out int column)
    {
        line = Math.Clamp(
            firstLine + FloorDiv(y - Text.Y, LineHeight), firstLine, firstLine + VisibleLines - 1);
        column = Math.Clamp(
            firstColumn + FloorDiv(x - Text.X, CharWidth), firstColumn, firstColumn + VisibleColumns);
    }

    /// <summary>Window row rectangle of one visible line — the selection's and the current line's band.</summary>
    public Rectangle LineRect(int line, int firstLine) =>
        new(Text.X, Text.Y + (line - firstLine) * LineHeight, Text.Width, LineHeight);

    /// <summary>The gutter's slot for visible row <paramref name="row"/> (0 is the first line on screen).</summary>
    public Rectangle GutterRowRect(int row) =>
        new(Gutter.X, Text.Y + row * LineHeight, Gutter.Width, LineHeight);

    /// <summary>
    /// The window rectangle of one character cell — the single mapping the caret and the
    /// selection are drawn from, so neither can sit half a character off the glyph it marks.
    /// </summary>
    public Rectangle CellRect(int line, int column, int firstLine, int firstColumn) =>
        new(Text.X + (column - firstColumn) * CharWidth,
            Text.Y + (line - firstLine) * LineHeight,
            CharWidth,
            LineHeight);

    /// <summary>
    /// The window rectangle of a run of characters on one line — <see cref="CellRect"/>
    /// generalized for the selection's bands. Deliberately not clipped: the caller clamps the
    /// columns it asks for to what is on screen, and a rectangle silently pulled inside the box
    /// would claim the selection ends where the window does.
    /// </summary>
    public Rectangle RowSpanRect(int line, int fromColumn, int toColumn, int firstLine, int firstColumn) =>
        new(Text.X + (fromColumn - firstColumn) * CharWidth,
            Text.Y + (line - firstLine) * LineHeight,
            (toColumn - fromColumn) * CharWidth,
            LineHeight);

    /// <summary>
    /// The scrollbar's thumb: as tall a share of the track as the window is of the document,
    /// floored at one line so it never vanishes in a long file, and positioned by how far down
    /// the first visible line is.
    /// </summary>
    public Rectangle ScrollThumbRect(int firstLine, int lineCount)
    {
        int total = Math.Max(lineCount, VisibleLines);
        int thumb = Math.Max(LineHeight, ScrollBar.Height * VisibleLines / total);
        int travel = Math.Max(0, ScrollBar.Height - thumb);
        int span = Math.Max(1, total - VisibleLines);
        int y = ScrollBar.Y + travel * Math.Clamp(firstLine, 0, span) / span;
        return new Rectangle(ScrollBar.X, y, ScrollBar.Width, thumb);
    }

    /// <summary>
    /// Window point → the first visible line a click or drag on the track asks for, or false off
    /// the bar. The point is taken as the <em>middle</em> of the window, so the thumb lands under
    /// the pointer instead of starting at it — which is what makes dragging feel like carrying
    /// the thumb rather than pushing it.
    /// </summary>
    public bool TryScrollBarLine(int x, int y, int lineCount, out int firstLine)
    {
        firstLine = 0;
        if (!ScrollBar.Contains(x, y))
        {
            return false;
        }
        int total = Math.Max(lineCount, VisibleLines);
        int centre = (y - ScrollBar.Y) * total / Math.Max(1, ScrollBar.Height);
        firstLine = Math.Clamp(centre - VisibleLines / 2, 0, Math.Max(0, lineCount - VisibleLines));
        return true;
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}

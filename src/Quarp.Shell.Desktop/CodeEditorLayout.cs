using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where everything on the <b>code</b> editor screen sits, in <b>console pixels</b> — 160x90 on
/// profile 8. Wave R4 moved this screen onto the console (ADR-029) exactly as R2 moved the
/// sprite screen and R3 the map, and this struct is the whole of the geometry that moved: every
/// coordinate that used to be derived from the window size and <c>PixelFontMetrics.UiScale</c>
/// is now a fixed number on the console's own grid. It stays the geometry's <b>single owner</b>:
/// <see cref="CodeEditorRenderer"/> draws these rectangles and <see cref="CodeEditorInput"/>
/// hit-tests the pointer against the same ones, so a character can never be painted in one
/// place and clicked in another.
///
/// <para>The shared frame — the top band, the three rules, the exit button, the five editor
/// tabs, the tooltip field, the message line and its clickable verbs — is measured by
/// <see cref="ConsoleChrome"/> and only forwarded here, the same frame the sprite and map
/// screens stand in. There is no second chrome and no third.</para>
///
/// <para><b>THE ARITHMETIC, in full, because on this screen it decided everything.</b>
///
/// <list type="number">
/// <item><description><b>Which font.</b> Two exist (ADR-022): <c>Font.Small</c> is a 3x5 glyph
/// in a 4x6 cell, <c>Font.Large</c> a 4x6 glyph in a 5x7 one (API-8 §3). Large is the wrong way
/// on both axes here — 67 rows hold nine of its lines against eleven of the small font's, and
/// 160 columns hold 32 of its characters against 40 — so a page of large type is 9x32 = 288
/// characters where small type gives 11x36 = 396. It is also the font the owner bounced on
/// playtest (<c>tasks/open/later-large-font-glyphs.md</c>: "сильно больше буквы плохо
/// читаются") and no cartridge uses. The code is set in <c>Font.Small</c>, and the choice is a
/// count, not a taste.</description></item>
///
/// <item><description><b>Height: eleven lines.</b> <see cref="ConsoleChrome"/> leaves 64 rows
/// between <see cref="ConsoleChrome.ContentTop"/> and <see cref="ConsoleChrome.ContentBottom"/>,
/// and three more below that which the frame reserves for a screen's horizontal position bar.
/// This screen has no horizontal bar — its long road through a file is the vertical
/// <see cref="ScrollBar"/> at the right edge — so it runs to
/// <see cref="ConsoleChrome.SliderBottom"/> and gets 67. At six rows to the line that is
/// <b>eleven</b> lines (66 rows used, one spare); the 64 it would have had hold ten. Nothing
/// arranges eleven out of 64: ten cells plus the last line's five ink rows is 65.</description></item>
///
/// <item><description><b>Width: thirty-six columns.</b> 160 pixels are 40 cells of the 4-px
/// font. One is spent on the tool column (<see cref="ToolColumns"/> x 10 px = two and a half
/// cells) because find, go-to, save, undo and redo need mouse paths; two pixels of air separate
/// it from the text; three pixels at the right edge are the scrollbar and one more is the gap
/// before it. 12 + 144 + 1 + 3 = 160, and 144 is <b>36 whole characters</b>.</description></item>
///
/// <item><description><b>No line-number gutter, and that is where the width came from.</b> The
/// host screen reserved <c>5</c> digits plus padding — 22 of the 160 pixels here, six of the 36
/// columns, a sixth of every line of code. All three references spend it on text instead:
/// TIC-80's code editor has no gutter at all (REFERENCES-EDITORS §4.1 — its whole toolbar is
/// eight buttons and its status row reads <c>line %i/%i col %i</c>), LIKO-12 prints
/// <c>LINE y/n CHAR x/len</c> on a status strip, PICO-8 shows neither. So do we: the caret's
/// line and column are in the status band every frame
/// (<see cref="CodeEditorRenderer.Coordinates"/>), and reaching a line by number is Ctrl+L and
/// its button. What is lost with the numbers is named rather than hidden: the mouse can no
/// longer put the caret at a line's start by clicking its number — Home does that, and the
/// click that used to is now a click into the text, which lands where it was
/// aimed.</description></item>
/// </list>
///
/// Eleven lines by thirty-six columns is 396 characters of code on screen. PICO-8 shows 21x32 =
/// 672 on a 128x128 screen and TIC-80 about 17x30 = 510 on 240x136, so ours is the smallest page
/// in the niche — ADR-029 named that as the price of a 90-row console and named the mitigation
/// with it ("полноэкранный режим без хрома возвращает все 15 строк").</para>
///
/// <para><b>THE FULLSCREEN ARITHMETIC, and it comes out exact.</b> With the chrome gone the page
/// is the console: 90 rows / 6 = <b>15 lines</b>, 160 px / 4 = <b>40 columns</b>, 600 characters
/// — one and a half times the windowed page, and every figure a whole division with no remainder
/// on either axis. Both numbers depend on there being nothing else on the surface, so fullscreen
/// takes the scrollbar off too: keeping its three pixels and their gap would have cost the
/// fortieth column (156 / 4 = 39), which is the one number ADR-029 uses to say we do not lose on
/// width to anybody. The wheel and PageUp/PageDown are the roads that survive, and they were
/// always the keyboard's roads anyway.</para>
///
/// <para>With the status row summoned (see <see cref="CodeEditorView.StatusBandShown"/>) the
/// page gives back the six rows below <see cref="ConsoleChrome.StatusRuleY"/> — 84 rows / 6 =
/// <b>14 lines</b> by the same 40 columns, 560 characters. The band lands on exactly the rows
/// the chrome's own status band uses, so the readout does not move between the two modes.</para>
///
/// <para>ADR-029's own table predicted "12-13" lines for the windowed code screen and 15 for the
/// fullscreen one. The 15 is exact. The 12-13 was measured against a hypothetical chrome of
/// "верхняя панель + строка статуса" only; the frame this shell actually grew
/// (<see cref="ConsoleChrome"/>: top band, three rules, a message line and a status line) costs
/// more than that, and the windowed page is eleven. The report names the discrepancy rather than
/// bending either number to meet the other.</para>
///
/// <para><b>Every scale is one.</b> The text is drawn at the system font's own size and the
/// scrollbar is three pixels wide. There is no fractional scale on this screen and no path that
/// can produce one (ARCHITECTURE §5); the window's only say is the whole-integer factor
/// <see cref="FramePlacement"/> presents the finished frame at.</para>
/// </summary>
public readonly struct CodeEditorLayout
{
    /// <summary>Columns of icon-buttons left of the text — one, and the whole width argument is in the type note.</summary>
    private const int ToolColumns = 1;

    /// <summary>Clear pixels between the tool column and the text. Two: the buttons' own frames are the rest of the separation.</summary>
    private const int Gutter = 2;

    /// <summary>Width of the vertical scrollbar's track, in console pixels.</summary>
    public const int ScrollBarWidth = 3;

    /// <summary>
    /// Clear pixels between the last character cell and the scrollbar, so a glyph never touches
    /// the rail. It bites only when the room left over is an exact multiple of the cell width —
    /// which on a 160-pixel console it is not (144 of 145 either way), so here it costs nothing
    /// and buys the guarantee on a console where it would.
    /// </summary>
    private const int ScrollBarGap = 1;

    // The tool column, top to bottom. TIC-80's code toolbar in the two entries we have model
    // verbs for (REFERENCES-EDITORS §4.1: FIND [ctrl+f], GOTO), then the three the host frame
    // kept in its status bar. Those three moved for the reason the sprite and map screens' did:
    // the console's status line is five pixels tall and an icon-button is ten, and a band that
    // cannot hold a button cannot hold a button row. Bookmarks and the outline stay off the
    // screen entirely — they have no model behind them, and a button with nothing behind it is
    // the defect class the button contract closed.
    private static readonly EditorButton[] _toolColumn =
    {
        EditorButton.ToolFind, EditorButton.ToolGoTo,
        EditorButton.Save, EditorButton.Undo, EditorButton.Redo,
    };

    /// <summary>The frame this screen stands in. See <see cref="ConsoleChrome"/>.</summary>
    public ConsoleChrome Chrome { get; private init; }

    /// <summary>
    /// True when this layout was measured with the chrome off. Kept on the layout rather than
    /// read off the view by every consumer for the reason the whole struct exists: the renderer
    /// draws these rectangles and the router hit-tests the same ones, and a screen that was
    /// <em>drawn</em> fullscreen must never be <em>clicked</em> as if it were not.
    /// </summary>
    public bool Fullscreen { get; private init; }

    /// <summary>
    /// True when fullscreen is carrying its one summoned status row. Always false outside
    /// fullscreen, where <see cref="ConsoleChrome"/> owns a status band that is always there.
    /// </summary>
    public bool StatusBand { get; private init; }

    // Forwarded, not recomputed — ConsoleChrome is the only place these exist.

    /// <summary>Screen width in console pixels.</summary>
    public int ScreenWidth => Chrome.ScreenWidth;

    /// <summary>Screen height in console pixels.</summary>
    public int ScreenHeight => Chrome.ScreenHeight;

    /// <summary>Side of every icon-button — ten console pixels, an 8x8 mask plus its frame.</summary>
    public int ButtonSize => ConsoleChrome.ButtonSize;

    /// <summary>Screen-edge inset for text — one pixel, because forty columns is the whole line.</summary>
    public int Margin => ConsoleChrome.Margin;

    /// <summary>The top band that carries the exit button, the tooltip field and the five editor tabs.</summary>
    public Rectangle TabStrip => Chrome.TopBar;

    /// <summary>The status band: the caret's place at the left, the byte budget at the right.</summary>
    public Rectangle StatusBar => Chrome.StatusBar;

    /// <summary>Glyph top of the single message line — the exit prompt, the save error or the standing notice.</summary>
    public int PromptY => Chrome.MessageY;

    /// <summary>The eleven placed buttons — the frame's six and the tool column's five.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The text field: a whole number of characters by a whole number of lines.</summary>
    public Rectangle Text { get; private init; }

    /// <summary>The vertical scrollbar's track, at the screen's right edge and the text's height.</summary>
    public Rectangle ScrollBar { get; private init; }

    /// <summary>Console pixels per character cell — the number every horizontal hit test divides by.</summary>
    public int CharWidth => SystemFont.CellWidth;

    /// <summary>Console pixels per text row — the font's own cell, with no leading added: 67 rows are too few to spend on air.</summary>
    public int LineHeight => SystemFont.CellHeight;

    /// <summary>How many lines the text field shows — eleven. Whole by construction: the box is trimmed to rows.</summary>
    public int VisibleLines => Text.Height / LineHeight;

    /// <summary>How many columns the text field shows — thirty-six. Whole for the same reason.</summary>
    public int VisibleColumns => Text.Width / CharWidth;

    /// <summary>Glyph top of the status readout in either mode — the chrome's own row, so the numbers do not jump between them.</summary>
    public int StatusTextY => Chrome.StatusTextY;

    /// <summary>
    /// The screen's geometry for a console of the given size. The two numbers are <b>console</b>
    /// pixels — 160x90 on profile 8 — and never a window size.
    /// </summary>
    /// <param name="fullscreen">
    /// Chrome off: no tab strip, no tool column, no scrollbar, no message line — 15x40 instead
    /// of 11x36 (the type note carries the arithmetic). The flag is a fact of
    /// <see cref="CodeEditorView"/>; this method only measures what it asks for.
    /// </param>
    /// <param name="statusBand">
    /// Only read when <paramref name="fullscreen"/> is set: keep the bottom six rows for the one
    /// summoned status row, leaving fourteen lines instead of fifteen.
    /// </param>
    public static CodeEditorLayout Compute(
        int screenWidth, int screenHeight, bool fullscreen = false, bool statusBand = false)
    {
        var buttons = new EditorButtonPlace[12];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(screenWidth, screenHeight, buttons, ref placed);

        if (fullscreen)
        {
            return Full(chrome, screenWidth, screenHeight, statusBand);
        }

        int button = ConsoleChrome.ButtonSize;
        int top = chrome.ContentTop;

        // The tool column grows DOWNWARD only, like the map's and the sprite screen's: widening
        // it would move the text field's left edge and cost the page a character of every line.
        for (int i = 0; i < _toolColumn.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _toolColumn[i],
                Rect = new Rectangle(i % ToolColumns * button, top + i / ToolColumns * button, button, button),
            };
        }

        // Left to right: the tool column, two pixels of air, the text, one pixel of air, the
        // scrollbar, the screen's edge. Down: the whole content band plus the three rows the
        // frame reserves for a horizontal bar this screen does not have (ConsoleChrome.SliderBottom).
        int textX = ToolColumns * button + Gutter;
        int barX = screenWidth - ScrollBarWidth;

        // Floors of one cell each. A console too small for a single character is clipped, not
        // crashed — the same floor the other two screens document.
        int roomWidth = Math.Max(SystemFont.CellWidth, barX - ScrollBarGap - textX);
        int roomHeight = Math.Max(SystemFont.CellHeight, chrome.SliderBottom - top);
        var text = new Rectangle(
            textX,
            top,
            roomWidth / SystemFont.CellWidth * SystemFont.CellWidth,
            roomHeight / SystemFont.CellHeight * SystemFont.CellHeight);

        return new CodeEditorLayout
        {
            Chrome = chrome,
            Buttons = buttons,
            Text = text,
            ScrollBar = new Rectangle(barX, text.Y, ScrollBarWidth, text.Height),
        };
    }

    /// <summary>
    /// The fullscreen measurement: the page is the console, and the only thing that can take a
    /// row off it is the summoned status band. No button is placed — deliberately, and it is the
    /// one thing this mode gives up: with no chrome there is nothing to click, so every control
    /// in fullscreen is a key and the mode's own key (F11) is how the buttons come back. The
    /// empty list is what makes <see cref="TryButton"/> answer false instead of hit-testing
    /// rectangles that are not on the screen.
    ///
    /// <para>The floors are the windowed measurement's, for the same reason: a console too small
    /// for one character is clipped rather than crashed.</para>
    /// </summary>
    private static CodeEditorLayout Full(
        in ConsoleChrome chrome, int screenWidth, int screenHeight, bool statusBand)
    {
        // The band sits on the chrome's own status rows (the rule at StatusRuleY and the five
        // glyph rows under it), so the readout does not move between windowed and fullscreen.
        int bottom = statusBand ? chrome.StatusRuleY : screenHeight;
        int roomWidth = Math.Max(SystemFont.CellWidth, screenWidth);
        int roomHeight = Math.Max(SystemFont.CellHeight, bottom);
        return new CodeEditorLayout
        {
            Chrome = chrome,
            Fullscreen = true,
            StatusBand = statusBand,
            Buttons = Array.Empty<EditorButtonPlace>(),
            Text = new Rectangle(
                0,
                0,
                roomWidth / SystemFont.CellWidth * SystemFont.CellWidth,
                roomHeight / SystemFont.CellHeight * SystemFont.CellHeight),
            ScrollBar = Rectangle.Empty,
        };
    }

    /// <summary>The placed rectangle of one button — the hover frame anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id) => ConsoleChrome.ButtonRect(Buttons, id);

    /// <summary>The 8x8 mask's destination inside a button.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => ConsoleChrome.ButtonIconRect(buttonRect);

    /// <summary>Console point to the button under it, stubs included (hover needs the dead ones too).</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        ConsoleChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="ConsoleChrome"/> owns the message line.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Console point to a prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    /// <summary>
    /// Console point to the character cell under it, given where the view is scrolled, or false
    /// off the text field. The scroll is added AFTER the division, so the answer is a place in
    /// the whole document and never on the screen — and it is deliberately <em>not</em> clamped
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
    /// Console point to nearest <em>visible</em> character cell, for drags: a selection whose
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

    /// <summary>
    /// The console rectangle of one character cell — the single mapping the caret and the
    /// selection are drawn from, so neither can sit half a character off the glyph it marks.
    /// </summary>
    public Rectangle CellRect(int line, int column, int firstLine, int firstColumn) =>
        new(Text.X + (column - firstColumn) * CharWidth,
            Text.Y + (line - firstLine) * LineHeight,
            CharWidth,
            LineHeight);

    /// <summary>
    /// The console rectangle of a run of characters on one line — <see cref="CellRect"/>
    /// generalized for the selection's bands. Deliberately not clipped: the caller clamps the
    /// columns it asks for to what is on screen, and a rectangle silently pulled inside the box
    /// would claim the selection ends where the screen does.
    /// </summary>
    public Rectangle RowSpanRect(int line, int fromColumn, int toColumn, int firstLine, int firstColumn) =>
        new(Text.X + (fromColumn - firstColumn) * CharWidth,
            Text.Y + (line - firstLine) * LineHeight,
            (toColumn - fromColumn) * CharWidth,
            LineHeight);

    /// <summary>
    /// The scrollbar's thumb: as tall a share of the track as the page is of the document,
    /// floored at one line so it never vanishes in a long file, and positioned by how far down
    /// the first visible line is. The renderer draws this and <see cref="TryScrollBarLine"/>
    /// reads the same mapping back, so the thumb can never sit where a press would not put it.
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
    /// Console point to the first visible line a click or drag on the track asks for, or false
    /// off the bar. The point is taken as the <em>middle</em> of the page, so the thumb lands
    /// under the pointer instead of starting at it — which is what makes dragging feel like
    /// carrying the thumb rather than pushing it.
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

    /// <summary>
    /// Console point to which <b>buttonless</b> control of this screen is under it, or
    /// <see cref="CodeRegion.None"/> — the code screen's twin of
    /// <see cref="SfxEditorLayout.RegionAt"/>, <see cref="MusicEditorLayout.RegionAt"/> and
    /// <see cref="MapEditorLayout.RegionAt"/>, and it exists for the same reason
    /// (REFERENCES-EDITORS §8 item 15): the scrollbar and the page of text itself have no button
    /// to hang a hotkey on, and between them they carry every key this editor owns that no button
    /// of the tool column names.
    ///
    /// <para><b>The order is the press chain's order</b>, control for control
    /// (<see cref="CodeEditorInput"/> tests the bar, then the text cell), so the label under the
    /// pointer and the click under the pointer can never name two different things. Buttons are
    /// not here at all: the router tests <see cref="TryButton"/> first and hands
    /// <see cref="HoverTarget.OfButton"/> instead.</para>
    ///
    /// <para>In fullscreen the bar is <see cref="Rectangle.Empty"/> (see <see cref="Full"/>), so
    /// it answers None with no extra branch and the page — which is then the whole console —
    /// answers for every point on the surface. That costs nothing: the mode draws no tooltip
    /// field to print the label in.</para>
    /// </summary>
    public CodeRegion RegionAt(int x, int y)
    {
        if (ScrollBar.Contains(x, y))
        {
            return CodeRegion.ScrollBar;
        }
        return Text.Contains(x, y) ? CodeRegion.Text : CodeRegion.None;
    }

    /// <summary>
    /// The rectangle a region's label names — the same box <see cref="RegionAt"/> answered from,
    /// and <see cref="Rectangle.Empty"/> for a control this layout is not showing (the bar, in
    /// fullscreen). Empty rather than "the rectangle it would have", by the rule
    /// <see cref="MapEditorLayout.RegionRect"/> already states: a control that is not on screen
    /// has no place on it.
    /// </summary>
    public Rectangle RegionRect(CodeRegion region) => region switch
    {
        CodeRegion.Text => Text,
        CodeRegion.ScrollBar => ScrollBar,
        _ => Rectangle.Empty,
    };

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}

using Microsoft.Xna.Framework;
using Quarp.Core;
using static Quarp.Shell.Desktop.ConsoleChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the code editor <b>into the console's own framebuffer</b> (wave R4, ADR-029): the top
/// band with the exit button, the tooltip field and the five editor tabs; the one-wide tool
/// column; the 11x36 page of text with its selection bands and blinking caret; the vertical
/// scrollbar at the right edge; the status line and the one message line.
///
/// <para><b>What this file used to be.</b> Until this wave it owned a <c>GraphicsDevice</c>, a
/// font atlas and an icon atlas, and painted at the window's native resolution through a
/// <c>SpriteBatch</c> — 28 lines of 90 columns at 1280x720. All of that is gone. Every pixel now
/// goes through <c>Cls</c>, <c>RectFill</c>, <c>Rect</c>, <c>Print</c> and <c>Pset</c> on a
/// <see cref="ShellScreen"/> — the same calls a cartridge makes — and the result is presented by
/// the same <see cref="ConsolePresenter"/> the cartridge's frame goes through. The class is
/// static for the same reason <see cref="MapEditorRenderer"/> is: with no device resource to own
/// there is nothing to construct and nothing to dispose.</para>
///
/// <para><b>What the move cost, in the one number that matters.</b> The page is
/// <b>eleven lines by thirty-six columns</b> — 396 characters, against the 2520 the host screen
/// showed at 1280x720. <see cref="CodeEditorLayout"/> carries the whole arithmetic and the four
/// decisions behind it (which font, where the eleventh line came from, why there is no
/// line-number gutter, what the tool column costs). ADR-029 accepted this as the price of a
/// 90-row console before any of it was written — and named the mitigation in the same breath.
/// That mitigation is here now: <b>F11</b> takes the chrome off and the page becomes the console,
/// fifteen lines by forty columns, 600 characters. <see cref="DrawFullscreen"/> is the whole of
/// what that costs this file.</para>
///
/// <para><b>Nothing was dropped, and here is the roll call</b> (the wave's law: if a control
/// went under a key, it gets named). Find: its button, <c>Ctrl+F</c>, <c>Ctrl+G</c> or
/// <c>Enter</c> to walk, <c>Shift+Enter</c> back. Go to line: its button and <c>Ctrl+L</c>.
/// Save, undo, redo: buttons in the tool column and their usual chords. Selection: the mouse
/// drag, <c>Shift</c> with any movement key, <c>Ctrl+A</c>. Clipboard: <c>Ctrl+C</c> /
/// <c>Ctrl+X</c> / <c>Ctrl+V</c>. Travel: arrows, <c>Ctrl+arrows</c> by word, Home/End,
/// <c>Ctrl+Home/End</c>, PgUp/PgDn, <c>Alt+Up/Down</c> by declaration, the wheel over the text
/// and the scrollbar. Tabs: their five buttons, <c>Alt+Left/Right</c> and <c>F1..F5</c>. The one control
/// that <em>left</em> is the line-number gutter, and with it the click that put the caret at a
/// line's start — that click is Home, and the pixels became six more columns of code. Fullscreen:
/// <c>F11</c>, and <c>Shift+F11</c> for its one status row.</para>
///
/// <para><b>No on-screen outline list, and that is a measurement rather than an opinion.</b>
/// REFERENCES-EDITORS §8 item 14 asks for three things — find, go-to-line and a list of
/// functions. The first two are here; the third is not, and here is the arithmetic. TIC-80's
/// outline (<c>TEXT_OUTLINE_MODE</c>, <c>Ctrl+O</c>) is a <em>mode</em>: it takes the whole code
/// area, owns the keyboard while it is up, scrolls, is clicked, and has its own way out. On this
/// screen the code area is eleven lines by thirty-six columns and the one message line already
/// has three tenants in a precedence order (<see cref="StandingNotice"/>); a list that covered
/// the page would need its own rectangle, its own scroll, its own hit test, its own Esc rung and
/// its own button — which is a second window on a 160x90 console, exactly what the order
/// excluded. So the wave takes the other half of the same item, the half two of the three
/// references make the primary tool: <b>Alt+Up / Alt+Down</b> walk the declarations directly
/// (<see cref="CodeEditorSession.IsDeclarationLine"/> owns what one is). The list is not
/// cancelled, it is unfunded: when it comes it comes as a mode of its own, with a layout, a
/// router and a golden picture, and not as three lines smuggled into a renderer.</para>
///
/// <para><b>No syntax highlighting in this wave, and it is not an oversight.</b> LIKO-12 has it
/// (<c>Libraries.SyntaxHighlighter</c>, a nine-colour theme) and TIC-80 has it per language;
/// both colour a language they own the lexer for. Ours is C#, our palette has sixteen slots of
/// which five are already spoken for by chrome, and the honest implementations are a real
/// tokenizer (Roslyn is a dependency of <c>Quarp.CartKit</c> — but it lives on the other side of
/// a module boundary and lexing per frame is a cost nobody has measured) or a regex guess that
/// will mis-colour verbatim strings and interpolation on the first day. That is a decision with
/// a shape of its own, not a line to be smuggled into a renderer.</para>
///
/// <para><b>The caret blinks on the draw clock</b>, like the sprite editor's marching ants:
/// <c>timeSeconds</c> is host time, it reaches no simulation and no hash, and the blink stops
/// (the caret stays lit) while the exit prompt is up, because a blinking caret under a question
/// the author is answering reads as an invitation to type. A golden test passes 0, which is a
/// lit caret.</para>
///
/// <para><b>Not the cartridge's console.</b> The framebuffer written here belongs to the shell
/// (<see cref="ShellScreen"/>); the golden master the CI compares between architectures is
/// <see cref="CartSession"/>'s, and no call in this file can reach it.</para>
/// </summary>
public static class CodeEditorRenderer
{
    /// <summary>Caret blinks per second — TIC-80's own rate, and the one every terminal uses.</summary>
    private const double BlinkHz = 2.0;

    /// <summary>What the tooltip field says when no control is hovered — TIC-80's <c>Names[mode]</c>.</summary>
    public const string ScreenName = "CODE";

    /// <summary>
    /// The layout this screen is drawn with; the router asks for the same one, so picture and
    /// clicks cannot disagree. It takes the view and the session because since the fullscreen
    /// wave the geometry has two more inputs than the screen's size —
    /// <see cref="CodeEditorView.Fullscreen"/> and <see cref="CodeEditorView.StatusBandShown"/>
    /// — and the whole point of one owner is that both channels read them from the same place.
    /// </summary>
    public static CodeEditorLayout LayoutFor(
        ShellScreen screen, CodeEditorSession session, CodeEditorView view)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        return CodeEditorLayout.Compute(
            screen.Width, screen.Height, view.Fullscreen, view.StatusBandShown(session));
    }

    /// <summary>
    /// One frame of the code editor. Owns the whole surface: it resets the console's drawing
    /// state and clears, so nothing another screen left behind can bend these pixels.
    /// <paramref name="view"/> is the very scroll the router's hit tests read, so the picture
    /// and the clicks cannot disagree; <paramref name="hover"/> and
    /// <paramref name="tooltipVisible"/> come from the shell's <see cref="IconHoverTracker"/> —
    /// the hovered control's frame lights up immediately, the text label only after the
    /// tracker's three seconds, and the label lands in the top band rather than under the
    /// pointer (<see cref="ConsoleChrome.TooltipChars"/> explains why);
    /// <paramref name="timeSeconds"/> is the window's draw clock, and only the caret's blink
    /// reads it.
    /// </summary>
    /// <returns>The layout used, so a test can assert against exactly what was drawn.</returns>
    public static CodeEditorLayout Draw(
        ShellScreen screen, CodeEditorSession session, CodeEditorView view,
        HoverTarget? hover, bool tooltipVisible, double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        CodeEditorLayout layout = LayoutFor(screen, session, view);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);

        if (layout.Fullscreen)
        {
            return DrawFullscreen(console, layout, session, view, timeSeconds);
        }

        DrawBands(console, layout.Chrome);
        DrawSelection(console, layout, session, view);
        DrawText(console, layout, session, view);
        DrawCaret(console, layout, session, view, timeSeconds);
        DrawScrollBar(console, layout, session, view);
        DrawButtons(console, layout, session, view, hover);
        // The readouts: where the caret is, in the numbers a compiler error names, and how much
        // of the code budget is spent.
        DrawStatusText(console, layout.Chrome, Coordinates(session), Budget(session), BudgetInk(session));
        DrawMessageLine(
            console, layout.Chrome, view.ExitPromptShown, session.SaveError, StandingNotice(session, view));
        DrawTooltipField(
            console, layout.Chrome,
            tooltipVisible && hover is HoverTarget target && target.Button is EditorButton button
                ? EditorIcons.CodeTooltip(button)
                : null,
            ScreenName);
        return layout;
    }

    /// <summary>
    /// One frame with the chrome off (F11): the console <em>is</em> the page — fifteen lines of
    /// forty columns, and fourteen while the status row is summoned. Three of the windowed
    /// screen's six painters are simply not called, and that is the whole implementation: no
    /// bands, no buttons, no tooltip field, no scrollbar. Nothing is drawn in a second place or
    /// by a second rule — the selection, the glyphs and the caret come off the same three
    /// methods and the same <see cref="CodeEditorLayout"/> rectangles the windowed screen uses,
    /// which is why a fullscreen page can never disagree with a windowed one about where a
    /// character sits inside its cell.
    ///
    /// <para><b>The band and the windowed status line now say the same thing in the same hue.</b>
    /// TIC-80's <c>drawStatus</c> turns the size readout red past the limit
    /// (<c>code->status.color = codeLen > MAX_CODE ? tic_color_red : tic_color_white</c>,
    /// REFERENCES-EDITORS §8 item 13). This band, which belongs to the code screen alone, took
    /// that red first; the windowed line went without it for as long as
    /// <see cref="ConsoleChromeRenderer.DrawStatusText"/> took no colour. It takes one now — an
    /// optional one, defaulting to what it always drew — so both surfaces read the same
    /// expression, <see cref="BudgetInk"/>, and neither can drift from the other.</para>
    /// </summary>
    private static CodeEditorLayout DrawFullscreen(
        VirtualConsole console, in CodeEditorLayout layout, CodeEditorSession session,
        CodeEditorView view, double timeSeconds)
    {
        DrawSelection(console, layout, session, view);
        DrawText(console, layout, session, view);
        DrawCaret(console, layout, session, view, timeSeconds);
        if (!layout.StatusBand)
        {
            return layout;
        }

        // The band's own rule, on the row the chrome puts one on: without it the bottom line of
        // code and the readout would touch, and the reader could not tell which was which.
        ConsoleChrome chrome = layout.Chrome;
        console.RectFill(0, chrome.StatusRuleY, chrome.ScreenWidth, 1, Dim);

        // One row, one tenant, in the same precedence the message line uses windowed: whatever
        // called the band up gets to speak on it, and the coordinates are the fallback tenant.
        // LIKO-12's ISRCH: replacing its line counter is this behaviour at the reference
        // (REFERENCES-EDITORS §4.2); see CodeEditorView.StatusBandShown for the whole argument.
        if (StandingNotice(session, view) is string notice)
        {
            console.Print(chrome.FitLine(notice), ConsoleChrome.Margin, layout.StatusTextY, Warn);
            return layout;
        }
        string budget = Budget(session);
        console.Print(Coordinates(session), ConsoleChrome.Margin, layout.StatusTextY, Text);
        console.Print(
            budget,
            chrome.ScreenWidth - ConsoleChrome.Margin - budget.Length * SystemFont.CellWidth,
            layout.StatusTextY,
            BudgetInk(session));
        return layout;
    }

    /// <summary>
    /// What colour the budget readout is drawn in, and the whole of TIC-80's
    /// <c>code->status.color = codeLen > MAX_CODE ? tic_color_red : tic_color_white</c>
    /// (REFERENCES-EDITORS §8 item 13) in one expression. <b>Red means exactly what it means
    /// there and nothing more: the buffer is past the limit.</b> Not "unsaved" (that is the save
    /// icon's yellow), not "a save failed" (that is the message line's own red sentence) — a
    /// status hue that meant three things would mean none of them.
    ///
    /// <para>One owner for both surfaces: the windowed status line hands it to
    /// <see cref="ConsoleChromeRenderer.DrawStatusText"/>'s optional colour and the fullscreen
    /// band prints with it directly, so the two cannot disagree about when the number is
    /// alarming. <see cref="CodeEditorSession.ByteCount"/> and not <see cref="CodeEditorSession.MeasureBudgetBytes"/>
    /// because this runs every frame and the readout beside it is the same number — the hue and
    /// the digits must agree with each other before either agrees with the loader.</para>
    /// </summary>
    public static byte BudgetInk(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.ByteCount > CodeEditorSession.MaxByteCount ? Error : Bright;
    }

    /// <summary>
    /// The status band's left field: where the caret is, in the numbers the author sees.
    /// TIC-80's <c>line %i/%i col %i</c> and LIKO-12's <c>LINE x/y CHAR a/b</c> agree on the
    /// shape and disagree on the second pair; we take TIC-80's, because "which column am I in"
    /// is what a compiler error names and "how long is this line" is not. Unchanged by the move
    /// to the console: at its widest — a 9999-line file with the caret past column 99 — it is 22
    /// characters, and the budget field beside it is at most 13, which leaves the 39-character
    /// line four to spare.
    /// </summary>
    public static string Coordinates(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"LINE {session.CursorLine + 1}/{session.LineCount} COL {session.CursorColumn + 1}";
    }

    /// <summary>
    /// The status band's right field: TIC-80's <c>size %i/%i</c> with the word <c>SIZE</c> cut
    /// off it. The word costs five of the line's 39 characters and the pair beside it can want
    /// 22 — 22 + 18 is 40, one more than there is — so the ratio stands alone, right-aligned to
    /// the screen's edge where a number that gains a digit does not shove its neighbour.
    ///
    /// <para><b>The other half of TIC-80's field is here too now.</b> Its <c>drawStatus</c> turns
    /// the size red past the limit, and so does this one — <see cref="BudgetInk"/> owns the
    /// choice and hands it to the shared painter's optional colour, which four screens leave
    /// alone. The sentence has not been replaced by the hue: <see cref="StandingNotice"/> still
    /// says how many bytes over and what will happen, because a colour cannot say a number and
    /// somebody reading this screen may not see red at all.</para>
    /// </summary>
    public static string Budget(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"{session.ByteCount}/{CodeEditorSession.MaxByteCount}";
    }

    /// <summary>
    /// The screen's standing line under the prompt and the save error, in precedence order: the
    /// footer field that is up (it IS this line while it lives — TIC-80 puts find and goto on
    /// the status row the same way, and LIKO-12 replaces its line counter with <c>ISRCH:</c>),
    /// otherwise the over-budget warning.
    ///
    /// <para><b>Re-cut for forty columns, and the cut carries the keys.</b> A field that is
    /// still empty spends the line teaching its two keys ("FIND: TYPE, ENTER WALKS, ESC
    /// CLOSES", 35 characters); the moment there is something to search for, the line is the
    /// search term, because that is what the author needs to see and a term long enough to fill
    /// 39 columns would have pushed the hints off anyway. The host screen printed both at once
    /// on a line that was 90 characters wide.</para>
    ///
    /// <para>The limit is reported and never enforced (<see cref="CodeEditorSession"/>'s own
    /// contract: an editor that stops accepting text is an editor that loses it), so this line
    /// and the size field are the whole of the screen's opinion about it. Its text keeps both
    /// halves of the host screen's — how far over, and that the cart will not load — inside 39
    /// characters, which the old one missed by twenty.</para>
    /// </summary>
    public static string? StandingNotice(CodeEditorSession session, CodeEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        if (view.FindShown)
        {
            return view.FindText.Length == 0
                ? "FIND: TYPE, ENTER WALKS, ESC CLOSES"
                : $"FIND: {view.FindText}";
        }
        if (view.GoToShown)
        {
            return view.GoToText.Length == 0
                ? "GO TO LINE: A NUMBER, ENTER JUMPS"
                : $"GO TO LINE: {view.GoToText}";
        }
        int over = session.ByteCount - CodeEditorSession.MaxByteCount;
        return over > 0 ? $"{over} BYTES OVER LIMIT - WILL NOT LOAD" : null;
    }

    /// <summary>
    /// The selection as a band per line, drawn <em>under</em> the glyphs so the text stays
    /// readable inside it. A line whose break is swallowed by the selection gets one extra
    /// column of band, which is how every editor shows "the newline is selected too" — without
    /// it a selected empty line would be invisible.
    /// </summary>
    private static void DrawSelection(
        VirtualConsole console, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view)
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
            Fill(console, layout.RowSpanRect(line, from, to, view.FirstLine, view.FirstColumn), ActiveBg);
        }
    }

    /// <summary>
    /// The page, one visible row at a time. The text is clipped to the field by substring rather
    /// than by a clip rectangle, because <c>Print</c> plots one glyph per character and a
    /// character off the right edge is a glyph nobody should pay for — and because a clip
    /// rectangle is console-wide state this screen would then have to put back.
    /// </summary>
    private static void DrawText(
        VirtualConsole console, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view)
    {
        for (int row = 0; row < layout.VisibleLines; row++)
        {
            int index = view.FirstLine + row;
            if (index >= session.LineCount)
            {
                break;
            }
            string line = session.Lines[index];
            if (view.FirstColumn >= line.Length)
            {
                continue;
            }
            int count = Math.Min(line.Length - view.FirstColumn, layout.VisibleColumns);
            console.Print(
                line.Substring(view.FirstColumn, count),
                layout.Text.X,
                layout.Text.Y + row * layout.LineHeight,
                Text);
        }
    }

    /// <summary>
    /// The caret: a one-pixel bar on the left edge of the cell it sits in, the cell's full
    /// height, blinking at <see cref="BlinkHz"/> off the draw clock. Drawn only when it is
    /// actually inside the page — the view's follow rule normally guarantees that, but the wheel
    /// is allowed to scroll away from it, and a caret painted on the frame's edge would be a lie.
    /// </summary>
    private static void DrawCaret(
        VirtualConsole console, in CodeEditorLayout layout, CodeEditorSession session,
        CodeEditorView view, double timeSeconds)
    {
        bool lit = view.ExitPromptShown || (int)(timeSeconds * BlinkHz) % 2 == 0;
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
        console.RectFill(cell.X, cell.Y, 1, cell.Height, Bright);
    }

    /// <summary>
    /// The scrollbar: a dim track the height of the page with a brighter thumb on it, drawn from
    /// the very <see cref="CodeEditorLayout.ScrollThumbRect"/> the drag inverts. It is the
    /// mouse's only long-distance road through a file (the wheel walks, the thumb jumps), and
    /// the one place the author can see how much of the buffer is off screen — the same job the
    /// map's position bar does for a map eighty screens wide. It brightens while it is being
    /// carried.
    /// </summary>
    private static void DrawScrollBar(
        VirtualConsole console, in CodeEditorLayout layout, CodeEditorSession session, CodeEditorView view)
    {
        Outline(console, layout.ScrollBar, Dim);
        Fill(
            console, layout.ScrollThumbRect(view.FirstLine, session.LineCount),
            view.ScrollDragActive ? Bright : Text);
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="ConsoleChromeRenderer.DrawButton"/>
    /// owns. The only decision this screen makes is which buttons read as active: its own tab,
    /// and whichever footer field is up — so the find button lights while the find line lives,
    /// which is what tells the author that Esc has something to close.
    /// </summary>
    private static void DrawButtons(
        VirtualConsole console, in CodeEditorLayout layout, CodeEditorSession session,
        CodeEditorView view, HoverTarget? hover)
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
            DrawButton(console, place, state, EditorIcons.IconFor(place.Id), text: null);
        }
    }

    /// <summary>One filled rectangle, a layout rectangle unpacked into the console's call.</summary>
    private static void Fill(VirtualConsole console, Rectangle rect, byte color) =>
        console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, color);

    /// <summary>One outline, skipped when the rectangle came back empty.</summary>
    private static void Outline(VirtualConsole console, Rectangle rect, byte color)
    {
        if (rect.Width > 0 && rect.Height > 0)
        {
            console.Rect(rect.X, rect.Y, rect.Width, rect.Height, color);
        }
    }
}

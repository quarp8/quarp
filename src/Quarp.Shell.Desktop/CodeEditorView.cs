using System.Globalization;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Who holds cut text between a Ctrl+X and a Ctrl+V. An interface with exactly two verbs, and
/// the seam that keeps the whole clipboard story out of layers 1 and 2: the session takes a
/// string and gives a string back, and <em>who took it from the operating system</em> is not its
/// business (REFERENCES-EDITORS §8 item 2 — "системный буфер обмена" — is a fact about the host,
/// and the host lives in the wiring layer).
///
/// <para><b>Two implementations, and the seam is exactly why that costs nothing.</b>
/// <see cref="InMemoryTextClipboard"/> is a string in this process — the default, and what every
/// headless test gets. <see cref="SystemTextClipboard"/> is the machine's own clipboard through
/// SDL2, constructed by the window and handed down through
/// <see cref="ShellModeMachine"/> into this view's constructor; not one line of the router, the
/// session or the renderer knows which of the two it is talking to. That is what lets the same
/// Ctrl+C be a real system copy in the shell and a deterministic string in a test.</para>
/// </summary>
public interface ITextClipboard
{
    /// <summary>What was last written, or the empty string. Never null — a paste of nothing is a no-op, not a crash.</summary>
    string Read();

    /// <summary>Replace the held text.</summary>
    void Write(string text);
}

/// <summary>The wave's clipboard: a string in this process. Replaceable by construction — see <see cref="ITextClipboard"/>.</summary>
public sealed class InMemoryTextClipboard : ITextClipboard
{
    private string _text = string.Empty;

    public string Read() => _text;

    public void Write(string text) => _text = text ?? string.Empty;
}

/// <summary>
/// What the code editor looks like right now, as opposed to what it <em>is</em>: the scroll
/// position, the find and go-to lines, the two live mouse gestures and the footer's exit
/// question. Headless like <see cref="MapEditorView"/> and <see cref="SheetScroll"/>, and for
/// the same reason — every claim about it ("the view always catches the caret", "a click on the
/// track lands the thumb under the pointer") is a plain unit test instead of a mouse at a window.
///
/// <para><b>One owner each, and they do not overlap.</b> The text and the caret belong to
/// <see cref="CodeEditorSession"/> and are never copied here; the geometry belongs to
/// <see cref="CodeEditorLayout"/> and arrives as a parameter; the scroll —
/// <see cref="FirstLine"/> and <see cref="FirstColumn"/> — belongs here and nowhere else. That
/// split is what lets the session stay a pure document: it has no idea a screen exists, and the
/// screen never has an opinion about what the file says.</para>
///
/// <para><b>The caret is always visible, and the rule lives in one place.</b> Rather than asking
/// every caller to remember to follow the caret after a move, <see cref="Sync"/> — called once
/// per frame before anything else — compares the session's caret with the one it last followed
/// and scrolls the least amount that brings it back on screen. Any caret move by any road (a
/// key, a click, a find, an undo restoring a position) is therefore followed exactly once, and
/// scrolling with the wheel or the bar does <em>not</em> drag the caret along: the reader may
/// look away from the caret, which is what a wheel is for, and the next keystroke brings the
/// view back. That is TIC-80's and LIKO-12's behaviour both.</para>
///
/// <para><b>Why the exit prompt lives here.</b> Same reading as <see cref="MapEditorView"/>:
/// the session is the model and has no screen state, and the decision is not duplicated —
/// <see cref="RequestClose"/> asks the session the one question it owns
/// (<see cref="CodeEditorSession.IsDirty"/>) and applies the sprite editor's answer table, so
/// unsaved text leaves only through an explicit Z or X.</para>
/// </summary>
public sealed class CodeEditorView
{
    private readonly ITextClipboard _clipboard;

    // The caret the view has already chased. Kept so Sync can tell "the caret moved" from
    // "the view moved", which are the two cases that must be handled in opposite directions.
    private CodePosition _followed;
    private bool _everFollowed;

    /// <param name="clipboard">
    /// The buffer Ctrl+C/X/V use. Null takes this view's own in-memory one; the system
    /// clipboard's future implementation arrives through exactly this parameter.
    /// </param>
    public CodeEditorView(ITextClipboard? clipboard = null) =>
        _clipboard = clipboard ?? new InMemoryTextClipboard();

    /// <summary>Topmost document line the text field shows.</summary>
    public int FirstLine { get; private set; }

    /// <summary>Leftmost document column the text field shows. Columns are characters — the session has no tabs in it.</summary>
    public int FirstColumn { get; private set; }

    /// <summary>True while the find line is up; it then owns typed characters, Enter and Backspace.</summary>
    public bool FindShown { get; private set; }

    /// <summary>What has been typed into the find line so far.</summary>
    public string FindText { get; private set; } = string.Empty;

    /// <summary>True while the go-to-line field is up. Mutually exclusive with <see cref="FindShown"/> — one footer line, one tenant.</summary>
    public bool GoToShown { get; private set; }

    /// <summary>What has been typed into the go-to-line field so far.</summary>
    public string GoToText { get; private set; } = string.Empty;

    /// <summary>True while either footer field is up — the one question the router asks before letting a character reach the buffer.</summary>
    public bool FieldShown => FindShown || GoToShown;

    /// <summary>
    /// True while the chrome is off and the whole console is the page — ADR-029's own
    /// mitigation for the tightest code screen in the niche ("полноэкранный режим без хрома
    /// возвращает все 15 строк"), reached with F11 (<see cref="ShellCommands.CodeFullscreen"/>
    /// carries the key argument).
    ///
    /// <para><b>Why the fact lives here and not in <see cref="CodeEditorSession"/>.</b> It is a
    /// fact of the <em>view</em>, in the tree's own sense of the word: the buffer is the same
    /// text whether the chrome is up or down, nothing on disk changes, and a headless test can
    /// flip it without a screen existing. Putting it in the session would have made "how the
    /// author likes to look at this file" part of the document — the same mistake
    /// <see cref="FirstLine"/> would be if it lived there.</para>
    ///
    /// <para><b>Not the window's fullscreen.</b> Nothing here touches the host window; the
    /// console is still 160x90 and still presented by <see cref="FramePlacement"/> at its whole
    /// integer scale. What goes away is <see cref="ConsoleChrome"/> — the tab strip, the tool
    /// column, the scrollbar, the message line and the status band — which is what buys the
    /// four extra lines and the four extra columns.</para>
    ///
    /// <para><b>What the mode gives up, named rather than hidden.</b> This shell's law is that
    /// every live action has a key path and a click path. Fullscreen suspends the mouse half by
    /// construction: with no chrome there is no button to click, so inside the mode every
    /// control is a key and the mode's own key is how the buttons come back. The references
    /// answer it the same way — PICO-8's fullscreen view is a bare <c>TAB</c> with no on-screen
    /// control at all — and the alternative, a button floated over the text, would spend the
    /// pixels the mode exists to win back.</para>
    /// </summary>
    public bool Fullscreen { get; private set; }

    /// <summary>
    /// True while the author has asked, with Shift+F11, to see the status row inside fullscreen.
    /// Reset every time fullscreen is left, so it can never quietly become the permanent state
    /// the mode exists to get rid of.
    /// </summary>
    public bool StatusPeek { get; private set; }

    /// <summary>
    /// F11: chrome off, chrome on. Leaving takes the peek with it (see
    /// <see cref="StatusPeek"/>); entering does <b>not</b> clear an open find or go-to field,
    /// because the field survives as the fullscreen band's tenant and losing a half-typed search
    /// term to a keystroke that was about screen space would be a plain data loss.
    /// </summary>
    public void ToggleFullscreen()
    {
        Fullscreen = !Fullscreen;
        if (!Fullscreen)
        {
            StatusPeek = false;
        }
    }

    /// <summary>
    /// Esc's fullscreen rung. Returns true when it consumed the key, so the router can spend Esc
    /// on the chrome before it spends it on leaving the screen: with no tab strip and no message
    /// line on the surface, the exit prompt would have nowhere to be drawn and no verb to be
    /// clicked, and an editor that asks an invisible question about unsaved text is the one
    /// failure this rung exists to make impossible.
    /// </summary>
    public bool LeaveFullscreen()
    {
        if (!Fullscreen)
        {
            return false;
        }
        ToggleFullscreen();
        return true;
    }

    /// <summary>Shift+F11: summon or dismiss the status row. A no-op outside fullscreen, where the chrome always carries one.</summary>
    public void ToggleStatusPeek()
    {
        if (Fullscreen)
        {
            StatusPeek = !StatusPeek;
        }
    }

    /// <summary>
    /// Whether fullscreen is currently carrying its one bottom row, and <b>the whole rule for
    /// when it is</b>: the band is a summoned tenant, never a standing fixture. It appears while
    /// the author has peeked at it (Shift+F11), while a find or go-to field is up, and while the
    /// buffer is over the byte budget — and at no other time, which is what keeps the default
    /// page fifteen lines instead of fourteen. The exit question is not on this list because it
    /// cannot happen here: <see cref="RequestClose"/> puts the chrome back before it raises one.
    ///
    /// <para><b>Whose behaviour this is: LIKO-12's.</b> Its code editor draws a permanent status
    /// strip (<c>ce:drawLineNum</c>, <c>LINE y/n CHAR x/len</c>) and <em>replaces</em> it with
    /// <c>ISRCH: &lt;текст&gt;</c> the moment incremental search is on (REFERENCES-EDITORS §4.2)
    /// — one strip, whoever has called for it. We keep the tenancy and drop the default tenant,
    /// because on a 90-row console the default tenant costs a line of code and LIKO-12's screen
    /// is 128 rows tall. TIC-80's <c>drawStatus</c> is the other reading and it is permanent; it
    /// is what our <em>windowed</em> chrome already does, so both readings are in the tree and
    /// each is where it pays.</para>
    /// </summary>
    public bool StatusBandShown(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Fullscreen
            && (StatusPeek || FieldShown || session.ByteCount > CodeEditorSession.MaxByteCount);
    }

    /// <summary>True while the dirty-exit question is on the footer line; the shell then gives it the input.</summary>
    public bool ExitPromptShown { get; private set; }

    /// <summary>True between the press and the release of a text-selection drag.</summary>
    public bool TextDragActive { get; private set; }

    /// <summary>True while the scrollbar's thumb is being carried.</summary>
    public bool ScrollDragActive { get; private set; }

    /// <summary>
    /// Once per frame, before anything else reads the view: re-clamp against the current layout
    /// (a resize changes how many lines fit) and, if the caret has moved since last time, scroll
    /// the least amount that shows it again. See the type note for why this is one method and
    /// not a duty spread over every caller.
    /// </summary>
    public void Sync(in CodeEditorLayout layout, CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ClampScroll(layout, session);
        if (!_everFollowed || session.Cursor != _followed)
        {
            FollowCaret(layout, session);
        }
    }

    /// <summary>
    /// The least scroll that puts the caret inside the text field — both edges, both axes. The
    /// inner clamp pulls the window onto the caret, the outer one keeps it inside the document;
    /// the second can never undo the first, because the last line's own window is exactly the
    /// document's last window.
    /// </summary>
    public void FollowCaret(in CodeEditorLayout layout, CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        int lines = Math.Max(1, layout.VisibleLines);
        int columns = Math.Max(1, layout.VisibleColumns);
        FirstLine = Math.Clamp(
            Math.Clamp(FirstLine, session.CursorLine - lines + 1, session.CursorLine),
            0,
            MaxFirstLine(layout, session));
        FirstColumn = Math.Max(
            0, Math.Clamp(FirstColumn, session.CursorColumn - columns + 1, session.CursorColumn));
        _followed = session.Cursor;
        _everFollowed = true;
    }

    /// <summary>The wheel and PageUp/PageDown's view half: the window by whole lines, the caret left where it is.</summary>
    public void ScrollLines(in CodeEditorLayout layout, CodeEditorSession session, int lines)
    {
        ArgumentNullException.ThrowIfNull(session);
        FirstLine = Math.Clamp(FirstLine + lines, 0, MaxFirstLine(layout, session));
    }

    /// <summary>The scrollbar's verb: show this line first. Clamped like every other writer of the scroll.</summary>
    public void ScrollTo(in CodeEditorLayout layout, CodeEditorSession session, int firstLine)
    {
        ArgumentNullException.ThrowIfNull(session);
        FirstLine = Math.Clamp(firstLine, 0, MaxFirstLine(layout, session));
    }

    /// <summary>The last line that may sit at the top of the window — the shared ceiling of every scroll writer.</summary>
    public static int MaxFirstLine(in CodeEditorLayout layout, CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Math.Max(0, session.LineCount - Math.Max(1, layout.VisibleLines));
    }

    // ---- the two footer fields ----

    /// <summary>Ctrl+F, or the find button. Opening it puts the go-to field away — one footer line, one tenant.</summary>
    public void OpenFind()
    {
        GoToShown = false;
        FindShown = true;
    }

    public void CloseFind() => FindShown = false;

    /// <summary>Ctrl+L, or the go-to button. The field opens empty rather than pre-filled: a typo is fixed by typing, not by clearing.</summary>
    public void OpenGoTo()
    {
        FindShown = false;
        GoToShown = true;
        GoToText = string.Empty;
    }

    public void CloseGoTo() => GoToShown = false;

    /// <summary>
    /// One character into whichever field is up. Control characters never arrive here — the
    /// router filters the character stream once, at its door, so neither field needs a copy of
    /// that rule.
    /// </summary>
    public void TypeIntoField(char c)
    {
        if (char.IsControl(c))
        {
            return;
        }
        if (FindShown)
        {
            FindText += c;
        }
        else if (GoToShown)
        {
            GoToText += c;
        }
    }

    /// <summary>Backspace inside a field. A no-op on an empty one — Esc is how a field is left.</summary>
    public void BackspaceField()
    {
        if (FindShown && FindText.Length > 0)
        {
            FindText = FindText[..^1];
        }
        else if (GoToShown && GoToText.Length > 0)
        {
            GoToText = GoToText[..^1];
        }
    }

    /// <summary>
    /// Enter in the go-to field: jump and put the field away. A number that will not parse
    /// leaves the caret alone and the field up — the author mistyped, and throwing the field
    /// away would make them start over.
    /// </summary>
    /// <returns>True when the caret was moved.</returns>
    public bool CommitGoTo(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!GoToShown)
        {
            return false;
        }
        if (!int.TryParse(GoToText, NumberStyles.None, CultureInfo.InvariantCulture, out int line))
        {
            return false;
        }
        session.GoToLine(line);     // the session clamps out-of-range itself
        GoToShown = false;
        return true;
    }

    /// <summary>
    /// Enter in the find line, and Ctrl+G's whole body: the session's own search from the caret,
    /// wrapping. The line stays up so Enter walks occurrence to occurrence — TIC-80's find mode
    /// and PICO-8's "CTRL-G to repeat the last search" are the same gesture from two ends.
    /// </summary>
    public bool FindNext(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return FindText.Length > 0 && session.FindNext(FindText);
    }

    /// <summary>Shift+Enter in the find line: the same walk backwards.</summary>
    public bool FindPrevious(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return FindText.Length > 0 && session.FindPrevious(FindText);
    }

    // ---- the clipboard's three verbs ----

    /// <summary>Ctrl+C. Nothing selected copies nothing — it must not silently empty what was copied before.</summary>
    public void Copy(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.HasSelection)
        {
            _clipboard.Write(session.SelectedText);
        }
    }

    /// <summary>Ctrl+X — a copy and a delete, in that order, so a failed delete cannot lose the text.</summary>
    public void Cut(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.HasSelection)
        {
            return;
        }
        _clipboard.Write(session.SelectedText);
        session.DeleteSelection();
    }

    /// <summary>Ctrl+V. The session replaces the selection and makes the whole paste one undo step, however many lines it spans.</summary>
    public void Paste(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string text = _clipboard.Read();
        if (text.Length > 0)
        {
            session.Insert(text);
        }
    }

    /// <summary>What the buffer holds — for the status of a test, and for whoever swaps the implementation.</summary>
    public string ClipboardText => _clipboard.Read();

    // ---- gestures ----

    /// <summary>The text field's press: a selection drag opens here and every later sample extends it.</summary>
    public void BeginTextDrag() => TextDragActive = true;

    /// <summary>The release. Safe without an open drag — releases arrive from off the text field.</summary>
    public void EndTextDrag() => TextDragActive = false;

    /// <summary>The scrollbar's press.</summary>
    public void BeginScrollDrag() => ScrollDragActive = true;

    public void EndScrollDrag() => ScrollDragActive = false;

    // ---- the exit ----

    /// <summary>
    /// Escape, or the exit tab. The exact answer table
    /// <see cref="SpriteEditorSession.RequestClose"/> and <see cref="MapEditorView.RequestClose"/>
    /// use: a prompt already up comes down ("stay"), a dirty buffer raises it, a clean one lets
    /// the shell leave.
    /// </summary>
    /// <returns>True when the caller may leave this screen.</returns>
    public bool RequestClose(CodeEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ExitPromptShown)
        {
            ExitPromptShown = false;
            return false;
        }
        if (session.IsDirty)
        {
            // The prompt is drawn on the chrome's message line and answered with three
            // clickable verbs, neither of which exists while the chrome is off — so raising it
            // brings the chrome back, unconditionally and in the one place that raises it. This
            // path matters even though Esc leaves fullscreen first
            // (<see cref="LeaveFullscreen"/>): the mode machine also raises this prompt from
            // OUTSIDE the code screen, when the author tries to leave the editor with unsaved
            // text on a tab they are not standing on.
            Fullscreen = false;
            StatusPeek = false;
            ExitPromptShown = true;
            return false;
        }
        return true;
    }

    /// <summary>Lowers the prompt after Z or X have been executed — the mode machine's half of the verb.</summary>
    public void CloseExitPrompt() => ExitPromptShown = false;

    private void ClampScroll(in CodeEditorLayout layout, CodeEditorSession session)
    {
        FirstLine = Math.Clamp(FirstLine, 0, MaxFirstLine(layout, session));
        FirstColumn = Math.Max(0, FirstColumn);
    }
}

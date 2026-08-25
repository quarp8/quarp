using System.Globalization;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Who holds cut text between a Ctrl+X and a Ctrl+V. An interface with exactly two verbs
/// because this wave deliberately ships the <b>internal</b> buffer only: touching the system
/// clipboard means SDL calls, a permission surface on some desktops, and a fact shared with
/// every other program on the machine — all of which deserve their own wave (REFERENCES-EDITORS
/// §8, item 2: "системный буфер обмена" is on the missing list for every editor at once, not
/// for the code editor alone).
///
/// <para>The seam is the point: when that wave lands, a <c>SystemTextClipboard</c> implementing
/// these two methods is handed to <see cref="CodeEditorView"/>'s constructor and not one line of
/// the router, the session or the renderer changes. Until then <see cref="InMemoryTextClipboard"/>
/// is the one owner of "what was copied".</para>
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

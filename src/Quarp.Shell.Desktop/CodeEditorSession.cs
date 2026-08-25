using System.Text;
using Quarp.CartKit;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One cursor move, named. The page moves are deliberately absent: how many lines a page is
/// belongs to whoever draws the text, so it arrives as a parameter of <see cref="CodeEditorSession.MovePage"/>
/// rather than as a value hidden in this list.
/// </summary>
public enum CodeMove
{
    Left,
    Right,
    Up,
    Down,
    WordLeft,
    WordRight,
    LineStart,
    LineEnd,
    DocumentStart,
    DocumentEnd,
}

/// <summary>
/// A place in the buffer: zero-based line, zero-based column. The column counts <b>characters</b>,
/// never bytes — the byte count of a line is a fact about the file, not about where the caret is,
/// and mixing the two is how editors corrupt non-ASCII text.
/// </summary>
public readonly record struct CodePosition(int Line, int Column);

/// <summary>
/// The code editing session of one cartridge <b>folder</b> — the headless model behind the CODE
/// tab (M9, editor-parity wave: document layer only). It owns the text of <c>src/main.cs</c>,
/// the caret, the selection, one undo stack and the save contract, and it owns nothing about the
/// screen: no layout, no glyphs, no scroll, no key bindings. Whatever the view needs from the
/// document it asks for by parameter (see <see cref="MovePage"/>), which is what keeps every
/// claim below provable without a GraphicsDevice — the split
/// <see cref="MapEditorSession"/>/<see cref="SpriteEditorSession"/> already live by.
///
/// <para><b>Lines, not a char buffer.</b> The text is a list of lines with no line terminators
/// in them; <c>\r\n</c> and a lone <c>\r</c> fold to <c>\n</c> on the way in and only <c>\n</c>
/// is ever written (LIKO-12's <c>code.lua</c> holds a table of lines the same way; TIC-80 keeps
/// one flat buffer and pays for it in every line operation). This is not a second owner of
/// "line endings are \n": <c>CartIdentity</c> already folds them before hashing, so a cart whose
/// file was checked out with CRLF (<c>core.autocrlf=true</c> in this tree) has the same identity
/// either way and the loader reads it with the same <see cref="File.ReadAllText(string)"/> this
/// class does. A file that ends with a newline therefore opens with a final <b>empty line</b>,
/// exactly as it is on disk — that is what makes the round trip byte-exact instead of
/// "byte-exact unless the last line is special".</para>
///
/// <para><b>Absent file = empty buffer, and that is clean.</b> A brand-new cart with no
/// <c>src/main.cs</c> opens as one empty line and writes nothing, the same reading of "missing
/// optional payload" the other two editors give an absent <c>map.bin</c> or <c>sfx.bin</c>.
/// The <c>src</c> directory is created by the first dirty save and not before, so opening the
/// CODE tab on a cart cannot leave a folder behind.</para>
///
/// <para><b>Undo granularity: a run of one kind of edit that never left the caret.</b> The order
/// offered TIC-80's rule (a step per elementary operation, <c>history()</c> after each edit) and
/// this one, and this one wins for typing: TIC-80's Ctrl+Z walks back one character at a time,
/// which is the single most complained-about thing about typing in it. So consecutive
/// <see cref="Insert"/>s (or <see cref="Backspace"/>s, or <see cref="Delete"/>s) coalesce into
/// one step while — and only while — each one continues where the previous one left the caret.
/// Three things close a run: any cursor move (<see cref="Move"/>, <see cref="SetCursor"/>,
/// <see cref="GoToLine"/>, a find), a change of edit kind, and a line break, because a step that
/// spans a newline reads as two edits to the person pressing Ctrl+Z. Everything else — a
/// multi-line paste, a selection delete, <see cref="InsertNewLine"/> — is one whole step by
/// itself, which is LIKO-12's <c>beginUndoable</c>/<c>endUndoable</c> answer to the same
/// question. A step is a whole snapshot of the lines plus the caret and the selection
/// (both references pack the cursor into history too): at 256 KB worst case the copy is a
/// string-reference array, not the text.</para>
///
/// <para><b>Tab inserts spaces to the next stop.</b> <c>.editorconfig</c> and
/// <c>docs/CODESTYLE.md</c> both say four-space indentation for every <c>.cs</c> in this tree —
/// cartridge sources included — so <see cref="TabWidth"/> is that number and no tab character
/// ever enters the buffer. Spaces to the next multiple of four rather than a fixed four, so the
/// key means "align" mid-line as well as "indent" at the start; because there are no tab
/// characters, a column and a screen column are the same number and the view needs no tab
/// arithmetic of its own. (LIKO-12's Tab inserts a single space and PICO-8's indents a
/// selection; neither behaviour survives contact with a four-space house style.)</para>
///
/// <para><b>The 256 KB limit is not enforced here.</b> <see cref="MaxByteCount"/> is borrowed
/// from <see cref="CodeBudget"/> — the one owner since ADR-024 — and the session only ever
/// <em>reports</em>: <see cref="ByteCount"/> is what the file will weigh, and
/// <see cref="MeasureBudgetBytes"/> asks the budget's own metric (comments stripped) what the
/// loader will judge. Typing is never refused at the limit: refusing keystrokes is cartridge
/// acceptance's job, and an editor that stops accepting text is an editor that loses it. The
/// status line turning red is the whole of this class's opinion, and all three reference
/// consoles do exactly that (TIC-80's <c>drawStatus</c>: <c>size %i/%i</c>, red over
/// <c>MAX_CODE</c>).</para>
/// </summary>
public sealed class CodeEditorSession
{
    /// <summary>The source folder inside a cartridge. One name owner for this editor: the constructor reads it, <see cref="Save"/> creates it, tests point at it.</summary>
    public const string SourceDirectoryName = "src";

    /// <summary>The one file this editor edits. Carts may hold more sources (the loader globs <c>src/**/*.cs</c>), but tabs are a later wave — see the report's "Расхождения".</summary>
    public const string SourceFileName = "main.cs";

    /// <summary>Spaces per indent step — the tree's own four (<c>.editorconfig</c>, CODESTYLE §Базовое).</summary>
    public const int TabWidth = 4;

    /// <summary>The code limit in UTF-8 bytes, borrowed from its owner (ADR-024). Reported, never enforced.</summary>
    public const int MaxByteCount = CodeBudget.MaxBytes;

    /// <summary>UTF-8 without BOM (SPEC-8: cartridge sources are plain UTF-8) — explicit, because a BOM would change the identity of every cart this editor touches.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>What an edit is doing right now — the key a run coalesces on. <c>None</c> is "this edit is a step by itself".</summary>
    private enum EditRun
    {
        None,
        Insert,
        Backspace,
        Delete,
    }

    /// <summary>One undo entry: the lines plus where the caret and the selection were before the step. The array holds string references, so a snapshot costs a pointer per line, not a copy of the text.</summary>
    private readonly record struct CodeSnapshot(string[] Lines, CodePosition Cursor, CodePosition? Anchor);

    private readonly string _sourceDirectory;
    private readonly string _sourcePath;

    // The live text. Never empty: an empty document is one empty line, so every line index the
    // caret can hold is real and no verb below needs a "what if there are no lines" branch.
    private readonly List<string> _lines = new();

    /// <summary>What the disk holds — the dirty comparison's baseline, replaced on save.</summary>
    private string[] _savedLines;

    private readonly List<CodeSnapshot> _undo = new();
    private readonly List<CodeSnapshot> _redo = new();

    // The open run and the caret position it ended at. A run continues only from that exact
    // place: a paste that lands elsewhere, or a caret the shell moved behind this class's back,
    // starts a new step instead of silently swallowing the previous one.
    private EditRun _run = EditRun.None;
    private CodePosition _runEnd;

    /// <summary>Where the selection was started; null when there is none. Selection is linear (anchor..caret), not a rectangle — text is not a sprite sheet.</summary>
    private CodePosition? _anchor;

    /// <summary>
    /// Opens the code of a cartridge folder. The file is optional (absent = empty, clean), and
    /// nothing is created here — see the class note on why the <c>src</c> folder waits for the
    /// first dirty save.
    /// </summary>
    public CodeEditorSession(string cartFolder)
    {
        ArgumentNullException.ThrowIfNull(cartFolder);
        CartName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cartFolder));
        _sourceDirectory = Path.Combine(cartFolder, SourceDirectoryName);
        _sourcePath = Path.Combine(_sourceDirectory, SourceFileName);
        _lines.AddRange(SplitLines(File.Exists(_sourcePath) ? File.ReadAllText(_sourcePath) : string.Empty));
        _savedLines = _lines.ToArray();
    }

    /// <summary>Folder name, for the header — the manifest is deliberately not read, same call as the other two sessions.</summary>
    public string CartName { get; }

    /// <summary>The live text, line by line, without terminators.</summary>
    public IReadOnlyList<string> Lines => _lines;

    public int LineCount => _lines.Count;

    /// <summary>The buffer as one string with <c>\n</c> joins — exactly the bytes <see cref="Save"/> writes.</summary>
    public string Text => string.Join('\n', _lines);

    public int CursorLine { get; private set; }

    public int CursorColumn { get; private set; }

    /// <summary>
    /// The column vertical movement aims at. Without it a caret that crosses a short line loses
    /// the column it started from and never gets it back — the "cursor drift" every editor
    /// solves this same way. Every move except up/down/page rewrites it from the caret.
    /// </summary>
    public int DesiredColumn { get; private set; }

    public CodePosition Cursor => new(CursorLine, CursorColumn);

    /// <summary>True when the anchor is set <em>and</em> sits somewhere else — an anchor on the caret selects nothing.</summary>
    public bool HasSelection => _anchor is CodePosition anchor && anchor != Cursor;

    /// <summary>The earlier end of the selection, or the caret when there is none.</summary>
    public CodePosition SelectionStart =>
        _anchor is CodePosition anchor && IsBefore(anchor, Cursor) ? anchor : Cursor;

    /// <summary>The later end of the selection, or the caret when there is none.</summary>
    public CodePosition SelectionEnd =>
        _anchor is CodePosition anchor && IsBefore(Cursor, anchor) ? anchor : Cursor;

    /// <summary>The selected text with <c>\n</c> joins — what a copy would put on the clipboard (the clipboard itself is the view's business).</summary>
    public string SelectedText => TextBetween(SelectionStart, SelectionEnd);

    /// <summary>
    /// What the file will weigh: UTF-8 bytes of the text, one byte per line break. Counted from
    /// the lines rather than from <see cref="Text"/> so the status line can ask every frame
    /// without building a 256 KB string to weigh it.
    /// </summary>
    public int ByteCount
    {
        get
        {
            int total = _lines.Count - 1;   // one '\n' between neighbours; a lone line has none
            for (int i = 0; i < _lines.Count; i++)
            {
                total += Utf8NoBom.GetByteCount(_lines[i]);
            }
            return total;
        }
    }

    /// <summary>
    /// What the <em>budget</em> weighs — the same Roslyn pass the loader runs (comments are free,
    /// SPEC-8 §6), so the number beside the limit is the number that will actually refuse the
    /// cart. A method and not a property because it parses the whole file: the status line is
    /// expected to ask on edits, not on frames, and <see cref="ByteCount"/> is the cheap one.
    /// </summary>
    public int MeasureBudgetBytes() =>
        CodeBudget.Measure(new[] { new CartSourceFile($"{SourceDirectoryName}/{SourceFileName}", Text) });

    /// <summary>True when the buffer differs from what the disk holds. Content, not history: undoing back to the loaded text is clean again.</summary>
    public bool IsDirty
    {
        get
        {
            if (_lines.Count != _savedLines.Length)
            {
                return true;
            }
            for (int i = 0; i < _savedLines.Length; i++)
            {
                if (!string.Equals(_lines[i], _savedLines[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Bumped on every change to the text (edit, undo, redo) so a renderer can re-lay-out only when something moved. Caret moves do not bump it — the view watches the caret directly.</summary>
    public int Version { get; private set; }

    /// <summary>Why the last save failed, or null. A save the author believes happened but did not is data loss, so it has to be sayable.</summary>
    public string? SaveError { get; private set; }

    // ---- caret and selection ----

    /// <summary>One named move. <paramref name="extend"/> is the Shift key: it drops an anchor where the caret was, if there is not one already.</summary>
    public void Move(CodeMove move, bool extend = false)
    {
        switch (move)
        {
            case CodeMove.Left:
                if (CursorColumn > 0)
                {
                    PlaceCaret(CursorLine, CursorColumn - 1, extend, keepDesired: false);
                }
                else if (CursorLine > 0)
                {
                    PlaceCaret(CursorLine - 1, _lines[CursorLine - 1].Length, extend, keepDesired: false);
                }
                else
                {
                    PlaceCaret(0, 0, extend, keepDesired: false);    // start of file: the move still settles the selection
                }
                break;
            case CodeMove.Right:
                if (CursorColumn < _lines[CursorLine].Length)
                {
                    PlaceCaret(CursorLine, CursorColumn + 1, extend, keepDesired: false);
                }
                else if (CursorLine < _lines.Count - 1)
                {
                    PlaceCaret(CursorLine + 1, 0, extend, keepDesired: false);
                }
                else
                {
                    PlaceCaret(CursorLine, CursorColumn, extend, keepDesired: false);
                }
                break;
            // Up/Down aim at DesiredColumn and let PlaceCaret's clamp do the shortening: that is
            // the whole mechanism — the short line clips the caret, the wish survives it.
            case CodeMove.Up:
                PlaceCaret(CursorLine - 1, DesiredColumn, extend, keepDesired: true);
                break;
            case CodeMove.Down:
                PlaceCaret(CursorLine + 1, DesiredColumn, extend, keepDesired: true);
                break;
            case CodeMove.WordLeft:
                MoveWordLeft(extend);
                break;
            case CodeMove.WordRight:
                MoveWordRight(extend);
                break;
            case CodeMove.LineStart:
                PlaceCaret(CursorLine, 0, extend, keepDesired: false);
                break;
            case CodeMove.LineEnd:
                PlaceCaret(CursorLine, _lines[CursorLine].Length, extend, keepDesired: false);
                break;
            case CodeMove.DocumentStart:
                PlaceCaret(0, 0, extend, keepDesired: false);
                break;
            case CodeMove.DocumentEnd:
                PlaceCaret(_lines.Count - 1, _lines[^1].Length, extend, keepDesired: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(move), move, "unknown caret move.");
        }
    }

    /// <summary>
    /// Page up/down. <paramref name="lines"/> is signed — negative up, positive down — and it is
    /// how many lines the <em>view</em> shows, which is the one thing about a page the document
    /// cannot know. Keeps <see cref="DesiredColumn"/>, like the single-line moves it repeats.
    /// </summary>
    public void MovePage(int lines, bool extend = false) =>
        PlaceCaret(CursorLine + lines, DesiredColumn, extend, keepDesired: true);

    /// <summary>Put the caret somewhere outright (a mouse click, once there is a view). Clamped, never thrown: a click lands where the pixels are, and the nearest legal place is the honest answer.</summary>
    public void SetCursor(int line, int column, bool extend = false) =>
        PlaceCaret(line, column, extend, keepDesired: false);

    /// <summary>Ctrl+A.</summary>
    public void SelectAll()
    {
        InterruptEdit();
        _anchor = new CodePosition(0, 0);
        CursorLine = _lines.Count - 1;
        CursorColumn = _lines[^1].Length;
        DesiredColumn = CursorColumn;
    }

    public void ClearSelection()
    {
        InterruptEdit();
        _anchor = null;
    }

    /// <summary>
    /// The goto dialog's verb, in the numbers the author sees: lines are 1-based here and
    /// nowhere else in this class, because that is what the status line prints and what they
    /// type. Out of range clamps to the first or last line — a typo must not throw at a person.
    /// </summary>
    public void GoToLine(int line) =>
        PlaceCaret(Math.Clamp(line, 1, _lines.Count) - 1, 0, extend: false, keepDesired: false);

    // ---- edits ----

    /// <summary>
    /// Insert text at the caret, replacing the selection. Multi-line text is the paste path, and
    /// it is one undo step whatever it contains; single-line text coalesces into the typing run.
    /// </summary>
    public void Insert(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }
        bool spansLines = text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0;
        BeginStep(spansLines ? EditRun.None : EditRun.Insert);
        DeleteSelectedRange();
        InsertAt(Cursor, text);
        EndStep(closeRun: spansLines);
    }

    /// <summary>
    /// Enter. The new line starts with the current line's own indentation — TIC-80's behaviour,
    /// and the reason nobody notices it until it is missing. Only the indentation that is
    /// actually to the <em>left</em> of the caret is copied: pressing Enter inside the leading
    /// spaces must not manufacture indentation the line never had.
    /// </summary>
    public void InsertNewLine()
    {
        BeginStep(EditRun.None);
        DeleteSelectedRange();
        string indent = LeadingIndent(_lines[CursorLine], CursorColumn);
        InsertAt(Cursor, "\n" + indent);
        EndStep(closeRun: true);
    }

    /// <summary>Tab: spaces to the next <see cref="TabWidth"/> stop. Measured from where the text will land, which is the selection's start when there is one.</summary>
    public void InsertTab()
    {
        int column = HasSelection ? SelectionStart.Column : CursorColumn;
        Insert(new string(' ', TabWidth - (column % TabWidth)));
    }

    /// <summary>Backspace. With a selection it deletes that instead — the one behaviour every editor shares. At the very start of the file it is a no-op, not a throw.</summary>
    public void Backspace()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }
        if (CursorLine == 0 && CursorColumn == 0)
        {
            return;
        }
        CodePosition to = Cursor;
        CodePosition from = CursorColumn > 0
            ? new CodePosition(CursorLine, CursorColumn - 1)
            : new CodePosition(CursorLine - 1, _lines[CursorLine - 1].Length);
        BeginStep(EditRun.Backspace);
        DeleteRange(from, to);
        EndStep(closeRun: from.Line != to.Line);
    }

    /// <summary>Delete. At the end of a line it pulls the next line up; at the end of the file it is a no-op.</summary>
    public void Delete()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }
        bool atLineEnd = CursorColumn == _lines[CursorLine].Length;
        if (atLineEnd && CursorLine == _lines.Count - 1)
        {
            return;
        }
        CodePosition to = atLineEnd
            ? new CodePosition(CursorLine + 1, 0)
            : new CodePosition(CursorLine, CursorColumn + 1);
        BeginStep(EditRun.Delete);
        DeleteRange(Cursor, to);
        EndStep(closeRun: atLineEnd);
    }

    /// <summary>Drop the selected text. One whole step however many lines it spanned.</summary>
    public void DeleteSelection()
    {
        if (!HasSelection)
        {
            return;
        }
        BeginStep(EditRun.None);
        DeleteRange(SelectionStart, SelectionEnd);
        EndStep(closeRun: true);
    }

    // ---- history ----

    /// <summary>Ctrl+Z. Restores the text, the caret and the selection together: landing back at the text without the caret is how an undo loses the reader's place.</summary>
    public void Undo()
    {
        InterruptEdit();
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(TakeSnapshot());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    /// <summary>Ctrl+Y — the exact mirror of <see cref="Undo"/>.</summary>
    public void Redo()
    {
        InterruptEdit();
        if (_redo.Count == 0)
        {
            return;
        }
        _undo.Add(TakeSnapshot());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
    }

    // ---- search ----

    /// <summary>
    /// Find the next occurrence from the caret, wrapping at the end. A found match becomes the
    /// selection with the caret at its far end, so pressing again walks on instead of finding
    /// the same place forever — the whole reason the search starts one character past the
    /// current match's start rather than at the caret.
    /// </summary>
    /// <returns>True when something was found and the caret moved to it.</returns>
    public bool FindNext(string needle, bool matchCase = false)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return false;
        }
        string text = Text;
        StringComparison how = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int from = FlatIndex(HasSelection ? SelectionStart : Cursor) + (HasSelection ? 1 : 0);
        int hit = from <= text.Length ? text.IndexOf(needle, from, how) : -1;
        if (hit < 0)
        {
            hit = text.IndexOf(needle, 0, how);     // round the circle
        }
        if (hit < 0)
        {
            return false;
        }
        SelectMatch(hit, needle.Length);
        return true;
    }

    /// <summary>Find the previous occurrence, wrapping at the start — <see cref="FindNext"/> backwards, and the mirror is exact: it looks strictly before the current match's start.</summary>
    public bool FindPrevious(string needle, bool matchCase = false)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return false;
        }
        string text = Text;
        if (text.Length == 0)
        {
            return false;
        }
        StringComparison how = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int before = FlatIndex(HasSelection ? SelectionStart : Cursor) - 1;
        int hit = before >= 0 ? text.LastIndexOf(needle, Math.Min(before, text.Length - 1), how) : -1;
        if (hit < 0)
        {
            hit = text.LastIndexOf(needle, text.Length - 1, how);
        }
        if (hit < 0)
        {
            return false;
        }
        SelectMatch(hit, needle.Length);
        return true;
    }

    // ---- disk ----

    /// <summary>
    /// Ctrl+S. The clean guard is the save contract's heart: a session whose text equals the
    /// disk writes <b>nothing</b> — open-and-close leaves the file untouched and, for a cart
    /// that never had one, uncreated, which is what keeps the pinned demo carts byte-identical
    /// after this editor has opened them. Note what that also buys on a CRLF checkout: an
    /// untouched file is not silently rewritten with <c>\n</c>, because it is not written at all.
    ///
    /// <para>Disk failures land in <see cref="SaveError"/> instead of throwing — a full disk must
    /// leave the author their work and a message.</para>
    /// </summary>
    /// <returns>True when the disk now matches the buffer (including "already did"), false when a write failed.</returns>
    public bool Save()
    {
        InterruptEdit();
        if (!IsDirty)
        {
            SaveError = null;
            return true;
        }
        try
        {
            Directory.CreateDirectory(_sourceDirectory);
            File.WriteAllText(_sourcePath, Text, Utf8NoBom);
            _savedLines = _lines.ToArray();
            SaveError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveError = e.Message;
            return false;
        }
    }

    // ---- the caret's one door ----

    /// <summary>
    /// Every caret movement in this class ends here, which is what makes the three rules
    /// unbreakable in one place: the position is clamped into the buffer, the anchor is dropped
    /// or cleared by <paramref name="extend"/>, and the open undo run is closed because the
    /// caret left. <paramref name="keepDesired"/> is set only by vertical moves — they are the
    /// ones whose wish must outlive a short line.
    /// </summary>
    private void PlaceCaret(int line, int column, bool extend, bool keepDesired)
    {
        InterruptEdit();
        line = Math.Clamp(line, 0, _lines.Count - 1);
        column = Math.Clamp(column, 0, _lines[line].Length);
        if (extend)
        {
            _anchor ??= Cursor;     // read before the caret moves: the anchor is where it WAS
        }
        else
        {
            _anchor = null;
        }
        CursorLine = line;
        CursorColumn = column;
        if (!keepDesired)
        {
            DesiredColumn = column;
        }
    }

    // ---- the two writers ----

    /// <summary>
    /// One of the two hands that change the text. Removes everything between two ordered
    /// positions and joins what is left of the first and last lines; the caret lands at the seam
    /// and the selection is gone, because after a delete there is nothing left to be selected.
    /// </summary>
    private void DeleteRange(CodePosition from, CodePosition to)
    {
        string head = _lines[from.Line][..from.Column];
        string tail = _lines[to.Line][to.Column..];
        _lines.RemoveRange(from.Line, to.Line - from.Line + 1);
        _lines.Insert(from.Line, head + tail);
        CursorLine = from.Line;
        CursorColumn = from.Column;
        DesiredColumn = from.Column;
        _anchor = null;
        Version++;
    }

    /// <summary>The other one. Splits the inserted text on line ends the same way the loader does, so a pasted CRLF block cannot smuggle a <c>\r</c> into the buffer.</summary>
    private void InsertAt(CodePosition at, string text)
    {
        List<string> parts = SplitLines(text);
        string current = _lines[at.Line];
        string head = current[..at.Column];
        string tail = current[at.Column..];
        if (parts.Count == 1)
        {
            _lines[at.Line] = head + parts[0] + tail;
            CursorLine = at.Line;
            CursorColumn = at.Column + parts[0].Length;
        }
        else
        {
            _lines[at.Line] = head + parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                _lines.Insert(at.Line + i, parts[i]);
            }
            int last = at.Line + parts.Count - 1;
            CursorLine = last;
            CursorColumn = parts[^1].Length;
            _lines[last] = parts[^1] + tail;
        }
        DesiredColumn = CursorColumn;
        _anchor = null;
        Version++;
    }

    /// <summary>Drop the selection if there is one, without opening a step of its own — the callers have already opened theirs.</summary>
    private void DeleteSelectedRange()
    {
        if (HasSelection)
        {
            DeleteRange(SelectionStart, SelectionEnd);
        }
    }

    // ---- steps ----

    /// <summary>
    /// Opens the undo step this edit belongs to. The run continues — no new snapshot — only when
    /// the kind matches, the caret is exactly where the previous edit left it, and nothing is
    /// selected; anything else is a new step. <see cref="EditRun.None"/> never continues
    /// anything, which is how "this edit is a step by itself" is spelled.
    /// </summary>
    private void BeginStep(EditRun kind)
    {
        if (kind != EditRun.None && _run == kind && _runEnd == Cursor && !HasSelection)
        {
            return;
        }
        _undo.Add(TakeSnapshot());
        _redo.Clear();      // the redone future described a text that no longer exists
        _run = kind;
    }

    private void EndStep(bool closeRun)
    {
        _runEnd = Cursor;
        if (closeRun)
        {
            _run = EditRun.None;
        }
    }

    /// <summary>What everything that cuts across an open run calls — the caret moved, a find jumped, a save happened — so the next edit starts a step of its own.</summary>
    private void InterruptEdit() => _run = EditRun.None;

    private CodeSnapshot TakeSnapshot() => new(_lines.ToArray(), Cursor, _anchor);

    private void Restore(CodeSnapshot snapshot)
    {
        _lines.Clear();
        _lines.AddRange(snapshot.Lines);
        CursorLine = snapshot.Cursor.Line;
        CursorColumn = snapshot.Cursor.Column;
        DesiredColumn = CursorColumn;
        _anchor = snapshot.Anchor;
        Version++;
    }

    // ---- words, lines, positions ----

    /// <summary>C#'s own idea of a word, so Ctrl+Left in the editor stops where it stops in the IDE the author came from.</summary>
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void MoveWordRight(bool extend)
    {
        string line = _lines[CursorLine];
        if (CursorColumn >= line.Length)
        {
            // At the end of the last line there is nowhere to go: without this branch the clamp
            // in PlaceCaret would turn "next line, column 0" into "this line, column 0" and the
            // caret would walk backwards.
            PlaceCaret(
                CursorLine < _lines.Count - 1 ? CursorLine + 1 : CursorLine,
                CursorLine < _lines.Count - 1 ? 0 : CursorColumn,
                extend,
                keepDesired: false);
            return;
        }
        int i = CursorColumn;
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }
        // One run of one kind: letters stop at punctuation and punctuation stops at letters,
        // which is what makes Ctrl+Right walk `foo.Bar(` in the four steps a reader expects.
        if (i < line.Length && IsWordChar(line[i]))
        {
            while (i < line.Length && IsWordChar(line[i]))
            {
                i++;
            }
        }
        else
        {
            while (i < line.Length && !IsWordChar(line[i]) && !char.IsWhiteSpace(line[i]))
            {
                i++;
            }
        }
        PlaceCaret(CursorLine, i, extend, keepDesired: false);
    }

    private void MoveWordLeft(bool extend)
    {
        if (CursorColumn == 0)
        {
            PlaceCaret(CursorLine - 1, CursorLine > 0 ? _lines[CursorLine - 1].Length : 0, extend, keepDesired: false);
            return;
        }
        string line = _lines[CursorLine];
        int i = CursorColumn;
        while (i > 0 && char.IsWhiteSpace(line[i - 1]))
        {
            i--;
        }
        if (i > 0 && IsWordChar(line[i - 1]))
        {
            while (i > 0 && IsWordChar(line[i - 1]))
            {
                i--;
            }
        }
        else
        {
            while (i > 0 && !IsWordChar(line[i - 1]) && !char.IsWhiteSpace(line[i - 1]))
            {
                i--;
            }
        }
        PlaceCaret(CursorLine, i, extend, keepDesired: false);
    }

    /// <summary>The leading whitespace of a line, cut at <paramref name="upTo"/> — see <see cref="InsertNewLine"/> for why the cut matters.</summary>
    private static string LeadingIndent(string line, int upTo)
    {
        int n = 0;
        while (n < line.Length && n < upTo && (line[n] == ' ' || line[n] == '\t'))
        {
            n++;
        }
        return line[..n];
    }

    private static bool IsBefore(CodePosition a, CodePosition b) =>
        a.Line < b.Line || (a.Line == b.Line && a.Column < b.Column);

    private string TextBetween(CodePosition from, CodePosition to)
    {
        if (from == to)
        {
            return string.Empty;
        }
        if (from.Line == to.Line)
        {
            return _lines[from.Line][from.Column..to.Column];
        }
        var builder = new StringBuilder();
        builder.Append(_lines[from.Line][from.Column..]);
        for (int i = from.Line + 1; i < to.Line; i++)
        {
            builder.Append('\n').Append(_lines[i]);
        }
        builder.Append('\n').Append(_lines[to.Line][..to.Column]);
        return builder.ToString();
    }

    /// <summary>Index into <see cref="Text"/> of a position — search works on the joined text, because a match may straddle a line end.</summary>
    private int FlatIndex(CodePosition at)
    {
        int index = 0;
        for (int i = 0; i < at.Line; i++)
        {
            index += _lines[i].Length + 1;
        }
        return index + at.Column;
    }

    private CodePosition PositionOf(int flat)
    {
        int line = 0;
        while (line < _lines.Count - 1 && flat > _lines[line].Length)
        {
            flat -= _lines[line].Length + 1;
            line++;
        }
        return new CodePosition(line, flat);
    }

    private void SelectMatch(int flat, int length)
    {
        InterruptEdit();
        _anchor = PositionOf(flat);
        CodePosition end = PositionOf(flat + length);
        CursorLine = end.Line;
        CursorColumn = end.Column;
        DesiredColumn = end.Column;
    }

    /// <summary>
    /// Splits text into lines, folding <c>\r\n</c> and a lone <c>\r</c> into a break. Never
    /// returns an empty list: <c>""</c> is one empty line, and a trailing break makes a trailing
    /// empty line, which is what keeps join-then-split an identity.
    /// </summary>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;    // \r\n is one break, not two
                }
                lines.Add(line.ToString());
                line.Clear();
                continue;
            }
            if (c == '\n')
            {
                lines.Add(line.ToString());
                line.Clear();
                continue;
            }
            line.Append(c);
        }
        lines.Add(line.ToString());
        return lines;
    }
}

using System.Text;
using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The code editor's model contract, proven headless (M9, editor-parity wave: document layer
/// only): the buffer, the caret, the selection, the edits, one undo stack, the search, and the
/// save contract — driven through <see cref="CodeEditorSession"/> alone, the way
/// <see cref="MapEditorSessionTests"/> drives the map editor.
///
/// <para>The named negative-control targets: (a) a clean session writes nothing — proven by an
/// empty directory listing, so even the <c>src</c> folder must not appear; (b) the writer is the
/// buffer and nothing else — a real demo source opens and saves without a byte moving, and a
/// CRLF file never grows a <c>\r</c> back; (c) the undo run's boundaries are exactly where the
/// class says they are — a caret move closes a step and a run of typing does not.</para>
///
/// <para>Every test works on a copy in a temp folder. <c>carts/</c> holds pinned goldens
/// (carts/demo-goldens.tsv) and nothing here may write into it — <c>carts/snake</c> is only ever
/// <em>read</em>, to get a real source file to copy.</para>
/// </summary>
public class CodeEditorSessionTests : IDisposable
{
    private readonly string _root;

    public CodeEditorSessionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-codeed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ---- helpers ----

    /// <summary>An empty cart folder, optionally seeded with a source file written verbatim (bytes, so a test can plant CRLF on any OS).</summary>
    private string CartFolder(byte[]? source = null)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (source is not null)
        {
            Directory.CreateDirectory(Path.Combine(folder, CodeEditorSession.SourceDirectoryName));
            File.WriteAllBytes(SourcePath(folder), source);
        }
        return folder;
    }

    private string CartFolder(string source) => CartFolder(new UTF8Encoding(false).GetBytes(source));

    private static string SourcePath(string folder) =>
        Path.Combine(folder, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName);

    /// <summary>Walks up from the test bin folder to the repo root, same as MapEditorSessionTests.</summary>
    private static string CartsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "carts");
            if (File.Exists(Path.Combine(candidate, "snake", "manifest.json")))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/ not found above the test directory");
    }

    /// <summary>
    /// What the buffer must equal for any file: the bytes with line endings folded. Computed in
    /// the test rather than borrowed from the session, so the session's own folding is checked
    /// against something and not against itself — this tree is checked out with
    /// <c>core.autocrlf=true</c>, so on Windows the demo source really does arrive as CRLF.
    /// </summary>
    private static string Folded(byte[] utf8) =>
        new UTF8Encoding(false).GetString(utf8).Replace("\r\n", "\n").Replace("\r", "\n");

    // ---- absent file, clean session ----

    [Fact]
    public void ACartWithoutASourceOpensAsAnEmptyBufferAndIsClean()
    {
        var session = new CodeEditorSession(CartFolder());

        Assert.Equal(1, session.LineCount);         // an empty document is one empty line, never zero lines
        Assert.Equal(string.Empty, session.Text);
        Assert.Equal(0, session.ByteCount);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.CursorLine);
        Assert.Equal(0, session.CursorColumn);
    }

    /// <summary>The clean-session guarantee: opening and saving an untouched cart creates nothing at all — not the file, and not the <c>src</c> folder either.</summary>
    [Fact]
    public void ACleanSessionCreatesNoFiles()
    {
        string folder = CartFolder();
        var session = new CodeEditorSession(folder);

        Assert.True(session.Save());

        Assert.Null(session.SaveError);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    /// <summary>The other half: the first dirty save creates the folder it needs.</summary>
    [Fact]
    public void ADirtySaveCreatesTheSourceFolder()
    {
        string folder = CartFolder();
        var session = new CodeEditorSession(folder);
        session.Insert("// hello");

        Assert.True(session.IsDirty);
        Assert.True(session.Save());

        Assert.Equal("// hello", File.ReadAllText(SourcePath(folder)));
        Assert.False(session.IsDirty);
    }

    // ---- the demo source: opened, never written ----

    /// <summary>
    /// The wave's headline guarantee on a real file: open <c>carts/snake/src/main.cs</c>, change
    /// nothing, save — and the bytes on disk are the bytes that were there, because a clean
    /// session does not write. The buffer is checked separately against the folded text, so
    /// "nothing was written" cannot hide "nothing was read either".
    /// </summary>
    [Fact]
    public void TheSnakeSourceSurvivesAnOpenAndSave()
    {
        byte[] original = File.ReadAllBytes(
            Path.Combine(CartsRoot(), "snake", CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName));
        string folder = CartFolder(original);
        DateTime before = File.GetLastWriteTimeUtc(SourcePath(folder));

        var session = new CodeEditorSession(folder);

        Assert.Equal(Folded(original), session.Text);
        Assert.False(session.IsDirty);
        Assert.True(session.Save());
        Assert.Null(session.SaveError);
        Assert.True(File.ReadAllBytes(SourcePath(folder)).AsSpan().SequenceEqual(original));
        Assert.Equal(before, File.GetLastWriteTimeUtc(SourcePath(folder)));
    }

    /// <summary>And a dirty save of the same file writes the buffer and nothing else: the folded original plus exactly the one character that was typed.</summary>
    [Fact]
    public void ADirtySaveOfTheSnakeSourceWritesTheBufferVerbatim()
    {
        byte[] original = File.ReadAllBytes(
            Path.Combine(CartsRoot(), "snake", CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName));
        string folder = CartFolder(original);
        var session = new CodeEditorSession(folder);
        session.Move(CodeMove.DocumentEnd);
        session.Insert("X");

        Assert.True(session.Save());

        Assert.Equal(Folded(original) + "X", File.ReadAllText(SourcePath(folder)));
    }

    // ---- line endings ----

    [Fact]
    public void CrlfCollapsesOnLoadAndNeverComesBack()
    {
        string folder = CartFolder(new UTF8Encoding(false).GetBytes("a\r\nb\r\n"));
        var session = new CodeEditorSession(folder);

        Assert.Equal(new[] { "a", "b", "" }, session.Lines);    // a trailing newline IS a trailing empty line
        Assert.Equal("a\nb\n", session.Text);
        Assert.False(session.IsDirty);                          // folding on load is not an edit

        session.SetCursor(0, 1);
        session.Insert("!");
        Assert.True(session.Save());

        byte[] written = File.ReadAllBytes(SourcePath(folder));
        Assert.DoesNotContain((byte)'\r', written);
        Assert.Equal("a!\nb\n", new UTF8Encoding(false).GetString(written));
    }

    /// <summary>A lone <c>\r</c> — an old Mac file, or a hand-built string — is a line break too, and it must not survive into the buffer.</summary>
    [Fact]
    public void ALoneCarriageReturnIsALineBreak()
    {
        var session = new CodeEditorSession(CartFolder(new UTF8Encoding(false).GetBytes("a\rb")));

        Assert.Equal(new[] { "a", "b" }, session.Lines);
    }

    // ---- edits ----

    [Fact]
    public void InsertPutsTextAtTheCaretAndMovesIt()
    {
        var session = new CodeEditorSession(CartFolder("ac\nz"));
        session.SetCursor(0, 1);

        session.Insert("b");

        Assert.Equal("abc\nz", session.Text);
        Assert.Equal(0, session.CursorLine);
        Assert.Equal(2, session.CursorColumn);
    }

    /// <summary>The paste path: multi-line text splits the line it lands in and leaves the caret at the end of what arrived.</summary>
    [Fact]
    public void AMultiLineInsertSplitsTheLine()
    {
        var session = new CodeEditorSession(CartFolder("ad"));
        session.SetCursor(0, 1);

        session.Insert("b\nc");

        Assert.Equal(new[] { "ab", "cd" }, session.Lines);
        Assert.Equal(1, session.CursorLine);
        Assert.Equal(1, session.CursorColumn);
    }

    [Fact]
    public void InsertAtTheStartOfTheFileIsTheStartOfTheFile()
    {
        var session = new CodeEditorSession(CartFolder("bc"));
        session.Move(CodeMove.DocumentStart);

        session.Insert("a");

        Assert.Equal("abc", session.Text);
    }

    [Fact]
    public void BackspaceRemovesTheCharacterBeforeTheCaret()
    {
        var session = new CodeEditorSession(CartFolder("abc"));
        session.SetCursor(0, 2);

        session.Backspace();

        Assert.Equal("ac", session.Text);
        Assert.Equal(1, session.CursorColumn);
    }

    /// <summary>The seam: backspace at column 0 pulls the line onto the end of the previous one, and the caret lands where the join happened.</summary>
    [Fact]
    public void BackspaceAtColumnZeroJoinsTheLines()
    {
        var session = new CodeEditorSession(CartFolder("ab\ncd"));
        session.SetCursor(1, 0);

        session.Backspace();

        Assert.Equal(new[] { "abcd" }, session.Lines);
        Assert.Equal(0, session.CursorLine);
        Assert.Equal(2, session.CursorColumn);
    }

    /// <summary>The start of the file has nothing before it: a no-op, not a throw and not a step.</summary>
    [Fact]
    public void BackspaceAtTheStartOfTheFileDoesNothing()
    {
        var session = new CodeEditorSession(CartFolder("ab"));
        session.Move(CodeMove.DocumentStart);

        session.Backspace();

        Assert.Equal("ab", session.Text);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void DeleteRemovesTheCharacterUnderTheCaretAndJoinsAtTheLineEnd()
    {
        var session = new CodeEditorSession(CartFolder("ab\ncd"));
        session.SetCursor(0, 1);

        session.Delete();
        Assert.Equal(new[] { "a", "cd" }, session.Lines);

        session.Delete();                       // now at the end of line 0: the next line comes up
        Assert.Equal(new[] { "acd" }, session.Lines);
        Assert.Equal(0, session.CursorLine);
        Assert.Equal(1, session.CursorColumn);
    }

    [Fact]
    public void DeleteAtTheEndOfTheFileDoesNothing()
    {
        var session = new CodeEditorSession(CartFolder("ab"));
        session.Move(CodeMove.DocumentEnd);

        session.Delete();

        Assert.Equal("ab", session.Text);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void DeletingASelectionRemovesExactlyTheSelectedSpan()
    {
        var session = new CodeEditorSession(CartFolder("abc\ndef\nghi"));
        session.SetCursor(0, 2);
        session.SetCursor(2, 1, extend: true);

        Assert.True(session.HasSelection);
        Assert.Equal("c\ndef\ng", session.SelectedText);

        session.DeleteSelection();

        Assert.Equal(new[] { "abhi" }, session.Lines);
        Assert.False(session.HasSelection);
        Assert.Equal(0, session.CursorLine);
        Assert.Equal(2, session.CursorColumn);
    }

    /// <summary>A selection is normalized by position, not by the order it was dragged in, and typing over it replaces it.</summary>
    [Fact]
    public void TypingOverABackwardsSelectionReplacesIt()
    {
        var session = new CodeEditorSession(CartFolder("abcd"));
        session.SetCursor(0, 3);
        session.SetCursor(0, 1, extend: true);      // dragged right-to-left

        Assert.Equal("bc", session.SelectedText);

        session.Insert("X");

        Assert.Equal("aXd", session.Text);
        Assert.False(session.HasSelection);
    }

    // ---- newline and tab ----

    [Fact]
    public void NewLineInheritsTheIndentOfTheCurrentLine()
    {
        var session = new CodeEditorSession(CartFolder("    foo();"));
        session.Move(CodeMove.LineEnd);

        session.InsertNewLine();

        Assert.Equal(new[] { "    foo();", "    " }, session.Lines);
        Assert.Equal(1, session.CursorLine);
        Assert.Equal(4, session.CursorColumn);      // after the inherited indent, ready to type
    }

    /// <summary>Only the indentation left of the caret is inherited: pressing Enter inside the leading spaces must not manufacture indentation the line never had.</summary>
    [Fact]
    public void NewLineInsideTheIndentInheritsOnlyWhatIsBehindTheCaret()
    {
        var session = new CodeEditorSession(CartFolder("    foo();"));
        session.SetCursor(0, 2);

        session.InsertNewLine();

        Assert.Equal(new[] { "  ", "    foo();" }, session.Lines);
        Assert.Equal(2, session.CursorColumn);
    }

    /// <summary>Tab is spaces to the next stop — never a tab character, so a column and a screen column stay the same number.</summary>
    [Fact]
    public void TabInsertsSpacesToTheNextStop()
    {
        var session = new CodeEditorSession(CartFolder());

        session.InsertTab();
        Assert.Equal(new string(' ', CodeEditorSession.TabWidth), session.Text);

        session.Insert("ab");
        session.InsertTab();                        // from column 6: two spaces reach column 8
        Assert.Equal(new string(' ', CodeEditorSession.TabWidth) + "ab  ", session.Text);
        Assert.DoesNotContain('\t', session.Text);
    }

    // ---- caret ----

    [Fact]
    public void VerticalMovementRemembersTheDesiredColumnAcrossAShortLine()
    {
        var session = new CodeEditorSession(CartFolder("abcdef\nxy\nabcdef"));
        session.SetCursor(0, 5);

        session.Move(CodeMove.Down);
        Assert.Equal(1, session.CursorLine);
        Assert.Equal(2, session.CursorColumn);      // clipped by the short line...
        Assert.Equal(5, session.DesiredColumn);     // ...but the wish survived it

        session.Move(CodeMove.Down);
        Assert.Equal(2, session.CursorLine);
        Assert.Equal(5, session.CursorColumn);      // ...and is honoured on the long line again
    }

    /// <summary>Any horizontal move rewrites the wish — otherwise a caret the author placed by hand would jump somewhere else on the next Down.</summary>
    [Fact]
    public void AHorizontalMoveRewritesTheDesiredColumn()
    {
        var session = new CodeEditorSession(CartFolder("abcdef\nxy\nabcdef"));
        session.SetCursor(0, 5);
        session.Move(CodeMove.Down);
        session.Move(CodeMove.Left);                // now at (1, 1), and the wish is 1

        session.Move(CodeMove.Down);

        Assert.Equal(2, session.CursorLine);
        Assert.Equal(1, session.CursorColumn);
    }

    /// <summary>How many lines a page is belongs to the view, so it arrives as a parameter — and the page move is a vertical move, wish and all.</summary>
    [Fact]
    public void APageIsWhateverTheViewSaysItIs()
    {
        var session = new CodeEditorSession(CartFolder("l0\nl1\nl2\nl3\nl4\nl5\nl6"));
        session.SetCursor(6, 2);

        session.MovePage(-4);
        Assert.Equal(2, session.CursorLine);

        session.MovePage(-99);                      // past the top clamps, it does not throw
        Assert.Equal(0, session.CursorLine);

        session.MovePage(99);
        Assert.Equal(6, session.CursorLine);
    }

    [Fact]
    public void WordMovementStopsAtWordBoundaries()
    {
        var session = new CodeEditorSession(CartFolder("foo.Bar(x)"));
        session.Move(CodeMove.DocumentStart);

        session.Move(CodeMove.WordRight);
        Assert.Equal(3, session.CursorColumn);      // end of "foo"
        session.Move(CodeMove.WordRight);
        Assert.Equal(4, session.CursorColumn);      // past the "." — punctuation is a run of its own
        session.Move(CodeMove.WordRight);
        Assert.Equal(7, session.CursorColumn);      // end of "Bar"
        session.Move(CodeMove.WordLeft);
        Assert.Equal(4, session.CursorColumn);      // back to the start of "Bar"
    }

    /// <summary>The last line's end is where WordRight stops: the clamp must not turn "the next line" into "column 0 of this one".</summary>
    [Fact]
    public void WordRightAtTheEndOfTheFileStaysPut()
    {
        var session = new CodeEditorSession(CartFolder("foo"));
        session.Move(CodeMove.DocumentEnd);

        session.Move(CodeMove.WordRight);

        Assert.Equal(0, session.CursorLine);
        Assert.Equal(3, session.CursorColumn);
    }

    // ---- undo ----

    /// <summary>
    /// The granularity this class chose: a run of typed characters is <b>one</b> step, and a
    /// caret move closes it. Both halves are asserted here, because either one alone would pass
    /// with a broken rule — "always one step" and "always one step per character" each satisfy
    /// half of it.
    /// </summary>
    [Fact]
    public void ARunOfTypingIsOneUndoStepAndACaretMoveClosesIt()
    {
        var session = new CodeEditorSession(CartFolder());
        session.Insert("a");
        session.Insert("b");

        session.Move(CodeMove.Left);
        session.Move(CodeMove.Right);               // back where it was — but the run is closed
        session.Insert("c");
        Assert.Equal("abc", session.Text);

        session.Undo();
        Assert.Equal("ab", session.Text);           // the "c" step alone

        session.Undo();
        Assert.Equal(string.Empty, session.Text);   // "ab" was one step, not two
        Assert.False(session.CanUndo);

        session.Redo();
        Assert.Equal("ab", session.Text);
        session.Redo();
        Assert.Equal("abc", session.Text);
        Assert.False(session.CanRedo);
    }

    /// <summary>A run of backspaces coalesces the same way, and undo restores the caret with the text.</summary>
    [Fact]
    public void ARunOfBackspacesIsOneUndoStep()
    {
        var session = new CodeEditorSession(CartFolder("abcdef"));
        session.SetCursor(0, 4);
        session.Backspace();
        session.Backspace();
        Assert.Equal("abef", session.Text);

        session.Undo();

        Assert.Equal("abcdef", session.Text);
        Assert.Equal(4, session.CursorColumn);      // the caret came back with its text
        Assert.False(session.CanUndo);
    }

    /// <summary>A step never spans a line break: Enter is its own step and does not swallow the typing that follows it.</summary>
    [Fact]
    public void ALineBreakClosesTheStep()
    {
        var session = new CodeEditorSession(CartFolder());
        session.Insert("ab");
        session.InsertNewLine();
        session.Insert("cd");

        session.Undo();
        Assert.Equal("ab\n", session.Text);

        session.Undo();
        Assert.Equal("ab", session.Text);
    }

    /// <summary>A new edit after an undo throws the redone future away — it described a text that no longer exists.</summary>
    [Fact]
    public void AnEditAfterAnUndoClearsRedo()
    {
        var session = new CodeEditorSession(CartFolder());
        session.Insert("ab");
        session.Undo();
        Assert.True(session.CanRedo);

        session.Insert("z");

        Assert.False(session.CanRedo);
        Assert.Equal("z", session.Text);
    }

    /// <summary>Dirt is content, not history: undoing back to the loaded text makes the session clean again.</summary>
    [Fact]
    public void UndoingBackToTheLoadedTextIsClean()
    {
        var session = new CodeEditorSession(CartFolder("hello"));
        session.Move(CodeMove.DocumentEnd);
        session.Insert("!");
        Assert.True(session.IsDirty);

        session.Undo();

        Assert.False(session.IsDirty);
    }

    // ---- search ----

    [Fact]
    public void FindNextWalksForwardAndRoundsTheCircle()
    {
        var session = new CodeEditorSession(CartFolder("cat\ndog cat\nCAT"));
        session.Move(CodeMove.DocumentStart);

        Assert.True(session.FindNext("cat", matchCase: true));
        Assert.Equal(new CodePosition(0, 0), session.SelectionStart);
        Assert.Equal(new CodePosition(0, 3), session.SelectionEnd);

        Assert.True(session.FindNext("cat", matchCase: true));
        Assert.Equal(new CodePosition(1, 4), session.SelectionStart);    // it moved on, not in place

        Assert.True(session.FindNext("cat", matchCase: true));
        Assert.Equal(new CodePosition(0, 0), session.SelectionStart);    // and rounded the circle
    }

    [Fact]
    public void FindRespectsTheCaseFlag()
    {
        var session = new CodeEditorSession(CartFolder("cat\ndog cat\nCAT"));
        session.SetCursor(1, 4);
        session.ClearSelection();

        Assert.True(session.FindNext("cat", matchCase: false));
        Assert.Equal(new CodePosition(1, 4), session.SelectionStart);    // the one under the caret
        Assert.True(session.FindNext("cat", matchCase: false));
        Assert.Equal(new CodePosition(2, 0), session.SelectionStart);    // CAT matches when case is ignored

        Assert.False(session.FindNext("CaT", matchCase: true));          // ...and nothing does when it is not
    }

    [Fact]
    public void FindPreviousWalksBackwardAndRoundsTheCircle()
    {
        var session = new CodeEditorSession(CartFolder("cat\ndog cat\nCAT"));
        session.SetCursor(1, 4);
        session.ClearSelection();

        Assert.True(session.FindPrevious("cat", matchCase: true));
        Assert.Equal(new CodePosition(0, 0), session.SelectionStart);

        Assert.True(session.FindPrevious("cat", matchCase: true));
        Assert.Equal(new CodePosition(1, 4), session.SelectionStart);    // wrapped to the last one
    }

    /// <summary>A match that straddles a line end is still a match: search works on the joined text, not line by line.</summary>
    [Fact]
    public void FindCrossesLineEnds()
    {
        var session = new CodeEditorSession(CartFolder("ab\ncd"));
        session.Move(CodeMove.DocumentStart);

        Assert.True(session.FindNext("b\nc", matchCase: true));

        Assert.Equal(new CodePosition(0, 1), session.SelectionStart);
        Assert.Equal(new CodePosition(1, 1), session.SelectionEnd);
    }

    [Fact]
    public void AnEmptyNeedleFindsNothing()
    {
        var session = new CodeEditorSession(CartFolder("abc"));

        Assert.False(session.FindNext(string.Empty));
        Assert.False(session.FindPrevious(string.Empty));
        Assert.False(session.HasSelection);
    }

    // ---- goto ----

    [Fact]
    public void GoToLineIsOneBasedAndClamps()
    {
        var session = new CodeEditorSession(CartFolder("l0\nl1\nl2"));

        session.GoToLine(2);
        Assert.Equal(1, session.CursorLine);
        Assert.Equal(0, session.CursorColumn);

        session.GoToLine(0);
        Assert.Equal(0, session.CursorLine);

        session.GoToLine(-99);
        Assert.Equal(0, session.CursorLine);

        session.GoToLine(99);
        Assert.Equal(2, session.CursorLine);
    }

    // ---- size ----

    /// <summary>
    /// The status line's left-hand number: UTF-8 bytes, so a two-byte character weighs two, and
    /// the count is exactly the length of the file the very next save produces. Counting UTF-16
    /// units instead would pass on ASCII and lie about everything else, which is why the fixture
    /// is not ASCII.
    /// </summary>
    [Fact]
    public void ByteCountIsUtf8AndMatchesTheSavedFile()
    {
        string folder = CartFolder();
        var session = new CodeEditorSession(folder);
        session.Insert("// счёт");                  // Cyrillic: 2 bytes a letter
        session.InsertNewLine();
        session.Insert("x");

        int counted = session.ByteCount;
        Assert.True(session.Save());

        Assert.Equal(counted, (int)new FileInfo(SourcePath(folder)).Length);
        Assert.Equal(counted, new UTF8Encoding(false).GetByteCount(session.Text));
        Assert.True(counted > session.Text.Length); // ...and it is genuinely bytes, not characters
    }

    /// <summary>
    /// The status line's right-hand number has one owner (ADR-024) and this class only borrows
    /// it — and the budget's own metric is not the file size, because comments are free
    /// (SPEC-8 §6). Both numbers are sayable, and the difference is real.
    /// </summary>
    [Fact]
    public void TheLimitIsBorrowedAndTheBudgetIgnoresComments()
    {
        Assert.Equal(CodeBudget.MaxBytes, CodeEditorSession.MaxByteCount);

        var session = new CodeEditorSession(CartFolder("class C { }\n"));
        int bare = session.MeasureBudgetBytes();
        session.Move(CodeMove.DocumentEnd);
        session.Insert("// a long comment that costs nothing at all");

        Assert.Equal(bare, session.MeasureBudgetBytes());        // comments are free...
        Assert.True(session.ByteCount > bare);                   // ...but they are still bytes on disk
    }

    /// <summary>Typing past the limit is never refused here — that is cartridge acceptance's job, and an editor that stops accepting text is an editor that loses it.</summary>
    [Fact]
    public void TheEditorDoesNotRefuseTextAtTheLimit()
    {
        var session = new CodeEditorSession(CartFolder());
        session.Insert(new string('x', CodeEditorSession.MaxByteCount + 10));

        Assert.Equal(CodeEditorSession.MaxByteCount + 10, session.ByteCount);
        Assert.True(session.IsDirty);
    }
}

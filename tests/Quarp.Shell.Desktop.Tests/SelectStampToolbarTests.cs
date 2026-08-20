using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The chrome policy of the two slots wave 2f woke: select (a group of two variants on the
/// shape slot's exact mechanism) and stamp (a plain tool). Mirrors <see cref="ToolbarGroupTests"/>
/// for the new slots; the wave's card "иконки оживают" is pinned here as behavior — digits,
/// clicks and flyout picks reaching a real session — plus the stamp's honest state-dependent
/// tooltip (the order's SELECT FIRST).
/// </summary>
public class SelectStampToolbarTests : IDisposable
{
    private readonly string _root;

    public SelectStampToolbarTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-sst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SpriteEditorSession Session()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return new SpriteEditorSession(folder);
    }

    // ---- the digits ----

    /// <summary>The select group's digit mechanism, same as the shape's: the first press selects, a repeat cycles the variant.</summary>
    [Fact]
    public void TheSelectDigitSelectsFirstThenCycles()
    {
        var session = Session();

        EditorIcons.PressToolDigit(session, 1);
        Assert.Equal(SpriteEditorTool.Select, session.Tool);
        Assert.Equal(SelectionVariant.Rectangle, session.CurrentSelection);     // the first press only selects

        EditorIcons.PressToolDigit(session, 1);
        Assert.Equal(SelectionVariant.Brush, session.CurrentSelection);         // the repeat cycles

        EditorIcons.PressToolDigit(session, 1);
        Assert.Equal(SelectionVariant.Rectangle, session.CurrentSelection);
    }

    [Fact]
    public void TheStampDigitSelectsTheStampTool()
    {
        var session = Session();

        EditorIcons.PressToolDigit(session, 4);

        Assert.Equal(SpriteEditorTool.Stamp, session.Tool);
        Assert.True(session.Pixels.IndexOfAnyExcept((byte)0) < 0);  // selecting a tool prints nothing
    }

    // ---- the slot's click and the flyout ----

    [Fact]
    public void ClickingTheSelectSlotSelectsTheTool()
    {
        var session = Session();

        EditorIcons.ClickGroupSlot(session, EditorButton.ToolSelect);

        Assert.Equal(SpriteEditorTool.Select, session.Tool);
    }

    [Fact]
    public void ChoosingASelectVariantActivatesTheToolAndRemembers()
    {
        var session = Session();

        EditorIcons.ChooseVariant(session, EditorButton.ToolSelect, (int)SelectionVariant.Brush);

        Assert.Equal(SelectionVariant.Brush, session.CurrentSelection);
        Assert.Equal(SpriteEditorTool.Select, session.Tool);        // the author asked for that marker, not a note about it
    }

    /// <summary>The slot's face is the current variant — the same VariantIcon cast the shape slot pins.</summary>
    [Fact]
    public void SelectVariantIconsAndTooltipsFollowTheEnum()
    {
        Assert.Equal(
            EditorIcon.SelectRect,
            EditorIcons.VariantIcon(EditorButton.ToolSelect, (int)SelectionVariant.Rectangle));
        Assert.Equal(
            EditorIcon.SelectBrush,
            EditorIcons.VariantIcon(EditorButton.ToolSelect, (int)SelectionVariant.Brush));
        Assert.Contains(
            "1", EditorIcons.VariantTooltip(EditorButton.ToolSelect, (int)SelectionVariant.Brush),
            StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.VariantIcon(EditorButton.ToolSelect, 2));
    }

    // ---- tooltips: alive, with hotkeys, honest about an empty stamp ----

    [Fact]
    public void TheWokenTooltipsNameTheirHotkeys()
    {
        Assert.Contains("1", EditorIcons.Tooltip(EditorButton.ToolSelect), StringComparison.Ordinal);
        Assert.Contains("ESC", EditorIcons.Tooltip(EditorButton.ToolSelect), StringComparison.Ordinal);
        Assert.Contains("4", EditorIcons.Tooltip(EditorButton.ToolStamp), StringComparison.Ordinal);
    }

    /// <summary>The order's words made visible: with nothing ever selected the stamp's tooltip says SELECT FIRST, not a hotkey lie.</summary>
    [Fact]
    public void TheStampTooltipExplainsSelectFirstUntilASelectionExists()
    {
        var session = Session();
        Assert.Contains(
            "SELECT FIRST", EditorIcons.Tooltip(EditorButton.ToolStamp, session), StringComparison.Ordinal);

        session.BeginSelect(2, 2);                  // one committed selection loads the stamp…
        session.CommitSelect();

        string loaded = EditorIcons.Tooltip(EditorButton.ToolStamp, session);
        Assert.DoesNotContain("SELECT FIRST", loaded, StringComparison.Ordinal);
        Assert.Equal(EditorIcons.Tooltip(EditorButton.ToolStamp), loaded);      // …and the static text returns

        // Every other button answers the static text through the same overload — the stamp is the one special case.
        Assert.Equal(
            EditorIcons.Tooltip(EditorButton.ToolPencil),
            EditorIcons.Tooltip(EditorButton.ToolPencil, session));
    }

    // ---- tool switching discipline carries over ----

    /// <summary>B keeps its 2c meaning next to the new tools: from Select and from Stamp it lands on the pencil, the opening tool.</summary>
    [Theory]
    [InlineData(SpriteEditorTool.Select)]
    [InlineData(SpriteEditorTool.Stamp)]
    public void ToggleToolFromTheNewToolsLandsOnThePencil(SpriteEditorTool tool)
    {
        var session = Session();
        session.SelectTool(tool);

        session.ToggleTool();

        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);
    }
}

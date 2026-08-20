using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The owner's third-review law (wave 2g): <b>the selection lives only under the select
/// tool</b>. Any path away from it — a toolbar click's <see cref="SpriteEditorSession.SelectTool"/>,
/// a digit through <see cref="EditorIcons.PressToolDigit"/>, the B key's
/// <see cref="SpriteEditorSession.ToggleTool"/> — parks an open float as a committed drop and
/// then drops the mask. This closes the review's bug 2 ("очистка не работает"): the mask used
/// to survive a tool switch invisibly (no overlay under the pencil) and quietly narrow Clear
/// to itself. The named negative control (б) — a SelectTool that stops clearing the mask —
/// reddens <see cref="NoOverlayGeometryExistsUnderAnotherTool"/> and
/// <see cref="ClearAfterAToolSwitchClearsTheWholeRegion"/> together: the invisible-selection
/// state becomes impossible, not just unlikely.
/// </summary>
public class SelectionToolSwitchTests : IDisposable
{
    private readonly string _root;

    public SelectionToolSwitchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-tsw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SpriteEditorSession Session()
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return new SpriteEditorSession(folder);
    }

    private static byte PixelAt(SpriteEditorSession session, int sheetX, int sheetY) =>
        session.Pixels[sheetY * CartData.GfxWidth + sheetX];

    private static void PaintPixel(SpriteEditorSession session, int color, int x, int y)
    {
        session.SelectColor(color);
        session.BeginStroke();
        session.Paint(x, y);
        session.EndStroke();
    }

    /// <summary>A committed rectangle selection over the inclusive box, with the select tool active — the real shell's precondition.</summary>
    private static void SelectBox(SpriteEditorSession session, int x0, int y0, int x1, int y1)
    {
        session.SelectTool(SpriteEditorTool.Select);
        session.SelectSelectionVariant(SelectionVariant.Rectangle);
        session.BeginSelect(x0, y0);
        session.UpdateSelect(x1, y1);
        session.CommitSelect();
        Assert.True(session.HasSelection);
    }

    // ---- every path away from the select tool drops the mask ----

    /// <summary>The toolbar-click path: SelectTool is what every button routes through.</summary>
    [Fact]
    public void SelectToolDropsTheSelection()
    {
        var session = Session();
        SelectBox(session, 1, 1, 2, 2);

        session.SelectTool(SpriteEditorTool.Pencil);

        Assert.False(session.HasSelection);
        Assert.False(session.SelectionGestureActive);
        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);
    }

    /// <summary>The digit path — "любым путём" is the owner's wording, so the keyboard route is pinned separately from the session verb.</summary>
    [Fact]
    public void TheDigitPathDropsTheSelectionToo()
    {
        var session = Session();
        SelectBox(session, 1, 1, 2, 2);

        EditorIcons.PressToolDigit(session, 2);     // pencil

        Assert.False(session.HasSelection);
        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);
    }

    /// <summary>The B-key path: ToggleTool routes through SelectTool, so it cannot mean less than a click.</summary>
    [Fact]
    public void TheToggleKeyPathDropsTheSelectionToo()
    {
        var session = Session();
        SelectBox(session, 1, 1, 2, 2);

        session.ToggleTool();                       // Select → pencil, the opening tool

        Assert.False(session.HasSelection);
        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);
    }

    /// <summary>Re-selecting the select tool itself is the no-op it always was: the mask survives, nothing is eaten for nothing.</summary>
    [Fact]
    public void ReselectingTheSelectToolKeepsTheMask()
    {
        var session = Session();
        SelectBox(session, 1, 1, 2, 2);

        session.SelectTool(SpriteEditorTool.Select);

        Assert.True(session.HasSelection);
    }

    // ---- the float parks, the memory survives ----

    /// <summary>
    /// A tool switch mid-float commits the drop where the author left it — one undo step,
    /// zeros beneath — instead of silently snapping the fragment home. Esc cancels; a switch
    /// is a change of subject, not a cancellation.
    /// </summary>
    [Fact]
    public void SwitchingToolsParksAnOpenFloatAsOneUndoStep()
    {
        var session = Session();
        PaintPixel(session, 7, 1, 1);
        SelectBox(session, 1, 1, 1, 1);
        session.BeginSelect(1, 1);                  // the grab
        session.UpdateSelect(4, 1);                 // floating at +3
        Assert.True(session.MoveActive);

        session.SelectTool(SpriteEditorTool.Pencil);

        Assert.Equal(7, PixelAt(session, 4, 1));    // the drop landed
        Assert.Equal(0, PixelAt(session, 1, 1));
        Assert.False(session.HasSelection);
        Assert.False(session.MoveActive);
        session.Undo();                             // ONE step: the parked move
        Assert.Equal(7, PixelAt(session, 1, 1));
        Assert.Equal(0, PixelAt(session, 4, 1));
        session.Undo();                             // the setup paint — and nothing between
        Assert.False(session.CanUndo);
    }

    /// <summary>The stamp source is the memory of the LAST commit, not the selection — it lives through the switch exactly as it lives through Esc.</summary>
    [Fact]
    public void TheStampSourceOutlivesTheSwitch()
    {
        var session = Session();
        PaintPixel(session, 7, 2, 2);
        SelectBox(session, 2, 2, 2, 2);

        session.SelectTool(SpriteEditorTool.Stamp);

        Assert.False(session.HasSelection);
        Assert.True(session.HasStampSource);
        session.StampAt(5, 5);
        Assert.Equal(7, PixelAt(session, 5, 5));
    }

    // ---- the review's bug 2, pinned shut ----

    /// <summary>
    /// The owner's trap verbatim: select something, switch to the pencil, press Delete —
    /// Clear must wipe the WHOLE region, because no selection exists to narrow it. Before
    /// this wave the mask survived the switch invisibly and Clear "did not work".
    /// </summary>
    [Fact]
    public void ClearAfterAToolSwitchClearsTheWholeRegion()
    {
        var session = Session();
        PaintPixel(session, 5, 1, 1);
        PaintPixel(session, 5, 6, 6);               // outside the selection — the pixel that used to survive
        SelectBox(session, 0, 0, 2, 2);

        EditorIcons.PressToolDigit(session, 2);     // the owner's exact step: digit 2, the pencil
        session.ClearRegion();                      // Del

        Assert.Equal(0, PixelAt(session, 1, 1));
        Assert.Equal(0, PixelAt(session, 6, 6));    // the whole region, not the dead mask's shadow
        session.Undo();                             // and it was one step
        Assert.Equal(5, PixelAt(session, 6, 6));
    }

    /// <summary>
    /// The overlay's headless proof: under any non-select tool there is no mask, so the
    /// renderer's data source — <see cref="SpriteEditorSession.IsSelected"/> through
    /// <see cref="SelectionOutline.Collect"/> — yields zero dashes. The renderer additionally
    /// gates on the tool (the stamp ghost's pattern), but this test makes the state it would
    /// gate against unreachable in the first place.
    /// </summary>
    [Theory]
    [InlineData(SpriteEditorTool.Pencil)]
    [InlineData(SpriteEditorTool.Fill)]
    [InlineData(SpriteEditorTool.Shape)]
    [InlineData(SpriteEditorTool.Stamp)]
    public void NoOverlayGeometryExistsUnderAnotherTool(SpriteEditorTool tool)
    {
        var session = Session();
        PaintPixel(session, 7, 2, 2);
        SelectBox(session, 1, 1, 3, 3);

        session.SelectTool(tool);

        Assert.False(session.HasSelection);
        var dashes = new List<AntDash>();
        SelectionOutline.Collect(
            session.IsSelected, session.RegionPixels, scale: 10, dashLength: 4, thickness: 2, phase: 0, dashes);
        Assert.Empty(dashes);                       // nothing for the renderer to draw, gate or no gate
    }
}

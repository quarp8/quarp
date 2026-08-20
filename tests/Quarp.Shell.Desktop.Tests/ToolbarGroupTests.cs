using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The group-slot policy of wave 2e — variants, their memory, and the three verbs the shell
/// routes verbatim (<see cref="EditorIcons.PressToolDigit"/>, <see cref="EditorIcons.ClickGroupSlot"/>,
/// <see cref="EditorIcons.ChooseVariant"/>) — proven against real sessions. The wave's named
/// negative-control target lives here: a digit that stops cycling the group's variant turns
/// <see cref="TheTransformDigitCyclesTheVariantWithoutApplying"/> and
/// <see cref="TheShapeDigitSelectsFirstThenCycles"/> red.
/// </summary>
public class ToolbarGroupTests : IDisposable
{
    private readonly string _root;

    public ToolbarGroupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-grp-" + Guid.NewGuid().ToString("N"));
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

    private static void Stroke(SpriteEditorSession session, int x, int y)
    {
        session.BeginStroke();
        session.Paint(x, y);
        session.EndStroke();
    }

    // ---- variant memory and the direct hotkeys ----

    /// <summary>F/V/R keep applying directly AND move the slot's highlight — the icon can never contradict the last hotkey.</summary>
    [Fact]
    public void DirectHotkeysHighlightTheirVariant()
    {
        var session = Session();
        Assert.Equal(TransformVariant.FlipH, session.CurrentTransform);     // the slot's opening face

        session.FlipVertical();
        Assert.Equal(TransformVariant.FlipV, session.CurrentTransform);

        session.RotateClockwise();
        Assert.Equal(TransformVariant.Rotate, session.CurrentTransform);

        session.FlipHorizontal();
        Assert.Equal(TransformVariant.FlipH, session.CurrentTransform);
    }

    [Fact]
    public void ApplyTransformAppliesTheRememberedVariant()
    {
        var session = Session();
        session.SelectColor(7);
        Stroke(session, 0, 3);
        session.SelectTransform(TransformVariant.FlipH);

        session.ApplyTransform();                           // the slot's click

        Assert.Equal(7, PixelAt(session, 7, 3));            // mirrored, exactly like the F key
        Assert.Equal(0, PixelAt(session, 0, 3));
    }

    /// <summary>"Выбор запоминается": picking from the flyout remembers, and deliberately applies NOTHING.</summary>
    [Fact]
    public void ChoosingATransformVariantRemembersWithoutApplying()
    {
        var session = Session();
        session.SelectColor(7);
        Stroke(session, 0, 3);
        int version = session.Version;

        EditorIcons.ChooseVariant(session, EditorButton.ToolTransform, (int)TransformVariant.FlipV);

        Assert.Equal(TransformVariant.FlipV, session.CurrentTransform);
        Assert.Equal(7, PixelAt(session, 0, 3));            // the sheet did not move
        Assert.Equal(version, session.Version);
    }

    [Fact]
    public void ChoosingAShapeVariantActivatesTheShapeTool()
    {
        var session = Session();

        EditorIcons.ChooseVariant(session, EditorButton.ToolShape, (int)ShapeVariant.Rectangle);

        Assert.Equal(ShapeVariant.Rectangle, session.CurrentShape);
        Assert.Equal(SpriteEditorTool.Shape, session.Tool); // the author asked for that shape, not a note about it
    }

    // ---- the slot's click ----

    [Fact]
    public void ClickingTheTransformSlotAppliesTheCurrentVariant()
    {
        var session = Session();
        session.SelectColor(9);
        Stroke(session, 2, 0);
        session.SelectTransform(TransformVariant.Rotate);

        EditorIcons.ClickGroupSlot(session, EditorButton.ToolTransform);

        Assert.Equal(9, PixelAt(session, 7, 2));            // top row → right column, same as the R key
        Assert.Equal(0, PixelAt(session, 2, 0));
    }

    [Fact]
    public void ClickingTheShapeSlotSelectsTheShapeTool()
    {
        var session = Session();

        EditorIcons.ClickGroupSlot(session, EditorButton.ToolShape);

        Assert.Equal(SpriteEditorTool.Shape, session.Tool);
    }

    // ---- the digits ----

    /// <summary>The wave's mechanism: the shape digit selects the tool on the first press and cycles the variant on a repeat.</summary>
    [Fact]
    public void TheShapeDigitSelectsFirstThenCycles()
    {
        var session = Session();

        EditorIcons.PressToolDigit(session, 5);
        Assert.Equal(SpriteEditorTool.Shape, session.Tool);
        Assert.Equal(ShapeVariant.Oval, session.CurrentShape);      // the first press only selects

        EditorIcons.PressToolDigit(session, 5);
        Assert.Equal(ShapeVariant.Rectangle, session.CurrentShape); // the repeat cycles

        EditorIcons.PressToolDigit(session, 5);
        Assert.Equal(ShapeVariant.Oval, session.CurrentShape);
    }

    /// <summary>
    /// The named negative control: the transform digit must cycle the highlight — and must
    /// never apply, or walking to a variant would wreck the sheet on the way.
    /// </summary>
    [Fact]
    public void TheTransformDigitCyclesTheVariantWithoutApplying()
    {
        var session = Session();
        session.SelectColor(7);
        Stroke(session, 0, 3);                              // an asymmetric pixel: any applied flip would move it

        EditorIcons.PressToolDigit(session, 6);
        Assert.Equal(TransformVariant.FlipV, session.CurrentTransform);
        EditorIcons.PressToolDigit(session, 6);
        Assert.Equal(TransformVariant.Rotate, session.CurrentTransform);
        EditorIcons.PressToolDigit(session, 6);
        Assert.Equal(TransformVariant.FlipH, session.CurrentTransform);

        Assert.Equal(7, PixelAt(session, 0, 3));            // three cycles, zero applications
        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);    // and no tool change either
    }

    [Fact]
    public void PlainDigitsStillSelectPencilAndFill()
    {
        var session = Session();

        EditorIcons.PressToolDigit(session, 3);
        Assert.Equal(SpriteEditorTool.Fill, session.Tool);

        EditorIcons.PressToolDigit(session, 2);
        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);
    }

    /// <summary>Stub digits stay exactly as dead as their buttons — the 2d law carried into 2e (select and stamp are wave 2f's).</summary>
    [Fact]
    public void StubDigitsSwitchNothing()
    {
        var session = Session();
        EditorIcons.PressToolDigit(session, 5);             // shape, so a stub press would visibly change something
        session.CycleShape();                               // rectangle, off the default

        EditorIcons.PressToolDigit(session, 1);             // select — wave 2f
        EditorIcons.PressToolDigit(session, 4);             // stamp — wave 2f
        EditorIcons.PressToolDigit(session, 0);
        EditorIcons.PressToolDigit(session, 9);

        Assert.Equal(SpriteEditorTool.Shape, session.Tool);
        Assert.Equal(ShapeVariant.Rectangle, session.CurrentShape);
        Assert.Equal(TransformVariant.FlipH, session.CurrentTransform);
    }

    // ---- tool selection discipline ----

    [Fact]
    public void SelectToolCommitsAnOpenStrokeFirst()
    {
        var session = Session();
        session.SelectColor(7);
        session.BeginStroke();
        session.Paint(0, 0);

        session.SelectTool(SpriteEditorTool.Fill);

        Assert.False(session.StrokeActive);                 // the gesture closed as its own undo step
        Assert.True(session.CanUndo);
        Assert.Equal(SpriteEditorTool.Fill, session.Tool);
    }

    /// <summary>Re-selecting the active tool is a no-op — a toolbar click must not eat an open gesture for nothing.</summary>
    [Fact]
    public void SelectingTheActiveToolKeepsTheGestureOpen()
    {
        var session = Session();
        session.SelectColor(7);
        session.BeginStroke();
        session.Paint(0, 0);

        session.SelectTool(SpriteEditorTool.Pencil);

        Assert.True(session.StrokeActive);
    }

    /// <summary>B keeps its 2c meaning next to the third tool: from Shape it lands on the pencil, the opening tool.</summary>
    [Fact]
    public void ToggleToolFromShapeLandsOnThePencil()
    {
        var session = Session();
        session.SelectTool(SpriteEditorTool.Shape);

        session.ToggleTool();

        Assert.Equal(SpriteEditorTool.Pencil, session.Tool);
    }
}

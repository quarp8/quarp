using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The wave-2g contract that closes the stamp's defect class (the third review's bug 1: the
/// stamp button was placed by the layout in 2f and never wired in the click router — visible,
/// hoverable, dead). The law: <b>every button the layout places and the stub list does not
/// kill must, clicked through the real router pieces, change something observable</b> — the
/// tool, the sheet version, the dirt, the undo stacks, the exit prompt or the shell mode.
/// The button list comes from <see cref="SpriteEditorLayout.Compute"/> itself, never by hand,
/// so a future button placed without wiring turns the sweep red on arrival; and every removed
/// router branch turns exactly its button's pass red (the wave's negative control (а)).
///
/// <para>The router pieces are the real ones: <see cref="EditorIcons.ClickButton"/> — the
/// table <c>QuarpGame.HandleEditorButton</c> routes plain clicks through, extracted this wave
/// precisely so this file can exist; <see cref="ToolbarFlyout"/> arm/complete plus
/// <see cref="EditorIcons.ClickGroupSlot"/> for group slots; and
/// <see cref="ShellModeMachine.HandleEscape"/> for the exit verb. Only the three-way press
/// dispatch (stub / group / plain) is mirrored in <see cref="RouteClick"/>, because the
/// windowed class that hosts it cannot be constructed without a graphics device — the mirror
/// consults the same single owners (<see cref="EditorIcons.IsStub"/>,
/// <see cref="EditorIcons.IsGroupSlot"/>) the shell does, so it cannot drift about WHO is a
/// stub or a group, only about the dispatch shape itself, which review owns.</para>
/// </summary>
public class EditorButtonContractTests : IDisposable
{
    private readonly string _root;

    public EditorButtonContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-btn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// A mode machine standing in the editor over a one-cart library of its own — its own so
    /// a Save clicked in one case can never leak a gfx.png into another case's baseline.
    /// </summary>
    private ShellModeMachine MachineWithOpenEditor()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"contract\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        return machine;
    }

    private static void Stroke(SpriteEditorSession editor, int x, int y)
    {
        editor.BeginStroke();
        editor.Paint(x, y);
        editor.EndStroke();
    }

    /// <summary>
    /// A session where every live button has work to do: ink at an asymmetric spot (clear and
    /// the transform's flip both move pixels), one stroke undone (undo AND redo both have a
    /// step), dirt (save has a write, the exit tab has a prompt to raise), and a tool that is
    /// not the one the button selects (so every tool click is a visible change — the pencil's
    /// own case starts from the bucket).
    /// </summary>
    private static void Prepare(SpriteEditorSession editor, EditorButton button)
    {
        editor.SelectColor(7);
        Stroke(editor, 0, 3);
        Stroke(editor, 1, 2);
        editor.Undo();
        if (button == EditorButton.ToolPencil)
        {
            editor.SelectTool(SpriteEditorTool.Fill);
        }
    }

    /// <summary>Everything a button click may legally touch, in one comparable value.</summary>
    private sealed record Snapshot(
        ShellMode Mode, SpriteEditorTool Tool, int Version, bool Dirty, bool CanUndo,
        bool CanRedo, bool PromptShown, SelectionVariant Selection, ShapeVariant Shape,
        TransformVariant Transform);

    private static Snapshot Observe(ShellModeMachine machine)
    {
        SpriteEditorSession editor = machine.Editor!;
        return new Snapshot(
            machine.Mode, editor.Tool, editor.Version, editor.IsDirty, editor.CanUndo,
            editor.CanRedo, editor.ExitPromptShown, editor.CurrentSelection,
            editor.CurrentShape, editor.CurrentTransform);
    }

    /// <summary>
    /// The shell's press dispatch over the real router pieces (see the type comment for why
    /// this one mirror exists). A group press whose button releases before the long-press
    /// clock matures is a click — exactly what Arm + CompleteClick model.
    /// </summary>
    private static void RouteClick(ShellModeMachine machine, ToolbarFlyout flyout, EditorButton button)
    {
        SpriteEditorSession editor = machine.Editor!;
        if (EditorIcons.IsStub(button))
        {
            return;                                 // the router refuses stubs before any verb
        }
        if (EditorIcons.IsGroupSlot(button))
        {
            flyout.Arm(button);
            if (flyout.CompleteClick(out EditorButton clicked))
            {
                EditorIcons.ClickGroupSlot(editor, clicked);
            }
            return;
        }
        if (EditorIcons.ClickButton(editor, button))
        {
            machine.HandleEscape();                 // the exit tab's verb belongs to the mode machine
        }
    }

    /// <summary>
    /// The sweep itself. Live buttons must change the snapshot; stubs and the sprites tab
    /// (which names the mode already on screen — its correct meaning IS "nothing changes")
    /// must change exactly nothing, because a stub that acts is as much a wiring bug as a
    /// live button that does not.
    /// </summary>
    [Fact]
    public void EveryPlacedLiveButtonChangesSomethingObservable()
    {
        foreach (EditorButtonPlace place in SpriteEditorLayout.Compute(1280, 720, regionCells: 1).Buttons)
        {
            ShellModeMachine machine = MachineWithOpenEditor();
            var flyout = new ToolbarFlyout();
            Prepare(machine.Editor!, place.Id);
            Snapshot before = Observe(machine);

            RouteClick(machine, flyout, place.Id);

            Snapshot after = Observe(machine);
            if (EditorIcons.IsStub(place.Id) || place.Id == EditorButton.SpritesTab)
            {
                Assert.True(before == after, $"{place.Id} is a no-op by contract but changed state");
            }
            else
            {
                Assert.True(before != after, $"{place.Id} is placed and live but its click changed nothing — unwired?");
            }
        }
    }

    /// <summary>The third review's bug 1, pinned by name: the stamp button's click selects the stamp tool.</summary>
    [Fact]
    public void TheStampButtonClickSelectsTheStampTool()
    {
        ShellModeMachine machine = MachineWithOpenEditor();

        RouteClick(machine, new ToolbarFlyout(), EditorButton.ToolStamp);

        Assert.Equal(SpriteEditorTool.Stamp, machine.Editor!.Tool);
    }

    /// <summary>The exit tab on a dirty sheet raises the footer prompt — the same judgement Esc gets, because both run through HandleEscape.</summary>
    [Fact]
    public void TheExitTabRaisesThePromptOnADirtySheet()
    {
        ShellModeMachine machine = MachineWithOpenEditor();
        machine.Editor!.SelectColor(7);
        Stroke(machine.Editor, 0, 0);

        RouteClick(machine, new ToolbarFlyout(), EditorButton.ExitTab);

        Assert.Equal(ShellMode.Editor, machine.Mode);       // still here — the prompt owns the decision
        Assert.True(machine.Editor!.ExitPromptShown);
    }

    /// <summary>The exit tab on a clean sheet leaves for the library — the mouse's Esc, whole.</summary>
    [Fact]
    public void TheExitTabLeavesACleanEditorToTheLibrary()
    {
        ShellModeMachine machine = MachineWithOpenEditor();

        RouteClick(machine, new ToolbarFlyout(), EditorButton.ExitTab);

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
    }
}

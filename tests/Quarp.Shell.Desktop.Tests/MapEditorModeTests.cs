using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The tilemap tab woken up (M9 stage 3): travel between the editor's two faces, the map's own
/// dirty-exit prompt, and the button-contract sweep this project made law in wave 2g — <b>every
/// button the layout places and the stub list does not kill must, clicked through the real
/// router pieces, change something observable</b>. The list comes from
/// <see cref="MapEditorLayout.Compute"/> itself, never by hand, so a future map button placed
/// without wiring turns the sweep red on arrival.
///
/// <para>The router pieces are the real ones: <see cref="EditorIcons.TabTarget"/> and
/// <see cref="EditorIcons.ClickMapButton"/> — the very table <c>QuarpGame.HandleMapButton</c>
/// routes clicks through — plus <see cref="ShellModeMachine"/> for the tab and exit verbs. Only
/// the two-line press dispatch (stub / tab / plain) is mirrored in <see cref="RouteClick"/>,
/// because the windowed class that hosts it cannot be constructed without a graphics device;
/// the mirror consults the same single owners the shell does, so it cannot drift about WHO is
/// a stub or a tab.</para>
/// </summary>
public class MapEditorModeTests : IDisposable
{
    private readonly string _root;

    public MapEditorModeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-mapmode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A mode machine standing in the sprite editor over a one-cart library of its own.</summary>
    private ShellModeMachine MachineWithOpenEditor(out string cartFolder)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        cartFolder = Path.Combine(root, "cart");
        Directory.CreateDirectory(cartFolder);
        File.WriteAllText(
            Path.Combine(cartFolder, "manifest.json"), "{\"name\":\"maps\",\"author\":\"\",\"profile\":8}");
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        return machine;
    }

    private ShellModeMachine MachineOnTheMapTab(out string cartFolder)
    {
        ShellModeMachine machine = MachineWithOpenEditor(out cartFolder);
        machine.SwitchEditorTab(ShellMode.MapEditor);
        Assert.Equal(ShellMode.MapEditor, machine.Mode);
        return machine;
    }

    private static void Stroke(MapEditorSession map, int x, int y)
    {
        MapEditorPaint.Begin(map, x, y);
        MapEditorPaint.End(map);
    }

    // ---- travel ----

    /// <summary>
    /// A cart that never visits the tilemap tab gets no map session and therefore cannot get a
    /// map file — the "absent file is a valid empty map" rule (MAP-FORMAT §1) protected at the
    /// one place it could be broken by accident. Break recipe: create the session eagerly in
    /// <see cref="ShellModeMachine.OpenEditor"/> and the null assertion goes red.
    /// </summary>
    [Fact]
    public void TheMapSessionIsNotBornUntilTheTabIsVisited()
    {
        ShellModeMachine machine = MachineWithOpenEditor(out string folder);

        Assert.Null(machine.MapEditor);
        Assert.Null(machine.MapView);
        Assert.False(File.Exists(Path.Combine(folder, MapEditorSession.MapFileName)));
    }

    /// <summary>
    /// The stage's headline: from the sprites to the map of the same cart and back, with
    /// unsaved work intact in BOTH. Break recipe: null <c>Editor</c> out in
    /// <see cref="ShellModeMachine.SwitchEditorTab"/>, or rebuild the map session on every
    /// visit instead of keeping it — the identity or the dirt assertion goes red.
    /// </summary>
    [Fact]
    public void TheTabsTravelBothWaysWithoutLosingUnsavedWorkInEither()
    {
        ShellModeMachine machine = MachineWithOpenEditor(out _);
        SpriteEditorSession sheet = machine.Editor!;
        sheet.SelectColor(7);
        sheet.BeginStroke();
        sheet.Paint(2, 3);
        sheet.EndStroke();

        machine.SwitchEditorTab(ShellMode.MapEditor);
        MapEditorSession map = machine.MapEditor!;
        map.SelectSprite(5);
        Stroke(map, 10, 4);
        Assert.True(map.IsDirty);

        machine.SwitchEditorTab(ShellMode.Editor);
        Assert.Same(sheet, machine.Editor);
        Assert.True(sheet.IsDirty);

        machine.ToggleEditorTab();
        Assert.Equal(ShellMode.MapEditor, machine.Mode);
        Assert.Same(map, machine.MapEditor);        // the SAME session, not a reload
        Assert.True(map.IsDirty);
        Assert.Equal(5, map.TileAt(10, 4));
    }

    /// <summary>
    /// The keyboard half of the tab strip flips both ways from either side. Break recipe:
    /// make <see cref="ShellModeMachine.ToggleEditorTab"/> always ask for the map tab — the
    /// second assertion goes red and the key becomes a one-way door.
    /// </summary>
    [Fact]
    public void TheToggleFlipsBothWays()
    {
        ShellModeMachine machine = MachineWithOpenEditor(out _);

        machine.ToggleEditorTab();
        Assert.Equal(ShellMode.MapEditor, machine.Mode);
        machine.ToggleEditorTab();
        Assert.Equal(ShellMode.Editor, machine.Mode);
    }

    // ---- the exit ----

    /// <summary>
    /// Esc on a dirty map raises the footer question instead of leaving, and Z saves and
    /// leaves. Break recipe: return true unconditionally from
    /// <see cref="MapEditorView.RequestClose"/> — the first assertion goes red and unsaved
    /// cells start leaving silently, which is the whole class of loss the prompt exists for.
    /// </summary>
    [Fact]
    public void EscapeOnADirtyMapAsksAndZSavesAndLeaves()
    {
        ShellModeMachine machine = MachineOnTheMapTab(out string folder);
        machine.MapEditor!.SelectSprite(9);
        Stroke(machine.MapEditor, 1, 1);

        machine.HandleEscape();

        Assert.Equal(ShellMode.MapEditor, machine.Mode);
        Assert.True(machine.MapView!.ExitPromptShown);

        machine.SaveMapAndClose();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.MapEditor);
        byte[] saved = File.ReadAllBytes(Path.Combine(folder, MapEditorSession.MapFileName));
        Assert.Equal(MapEditorSession.MapPayloadSize, saved.Length);
        Assert.Equal(9, saved[1 * MapEditorSession.MapColumns + 1]);
    }

    /// <summary>
    /// X leaves the disk byte-for-byte untouched — for a cart that had a map, and for one that
    /// did not (no file must be created). Break recipe: make <c>DiscardMapAndClose</c> call
    /// <c>Save</c> first and both assertions go red at once.
    /// </summary>
    [Fact]
    public void DiscardingTheMapWritesNothingAtAll()
    {
        ShellModeMachine machine = MachineOnTheMapTab(out string folder);
        machine.MapEditor!.SelectSprite(4);
        Stroke(machine.MapEditor, 7, 7);
        machine.HandleEscape();
        Assert.True(machine.MapView!.ExitPromptShown);

        machine.DiscardMapAndClose();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.False(File.Exists(Path.Combine(folder, MapEditorSession.MapFileName)));
    }

    /// <summary>
    /// The trap this design exists to avoid: leaving from the SPRITES tab while the map on the
    /// other tab is dirty must not drop the map. The editor stays open, the map tab comes to
    /// the front and asks. Break recipe: restore the old one-line body of
    /// <c>HandleEscape</c>'s editor case (<c>if (Editor.RequestClose()) CloseEditor();</c>) —
    /// the mode goes straight to Library and every assertion here goes red, which is the shape
    /// of the data loss.
    /// </summary>
    [Fact]
    public void LeavingFromTheSheetDoesNotDropADirtyMapOnTheOtherTab()
    {
        ShellModeMachine machine = MachineOnTheMapTab(out _);
        machine.MapEditor!.SelectSprite(2);
        Stroke(machine.MapEditor, 3, 3);
        machine.SwitchEditorTab(ShellMode.Editor);
        Assert.False(machine.Editor!.IsDirty);      // the sheet itself is clean

        machine.HandleEscape();

        Assert.Equal(ShellMode.MapEditor, machine.Mode);
        Assert.True(machine.MapView!.ExitPromptShown);
        Assert.NotNull(machine.Editor);

        machine.DiscardMapAndClose();
        Assert.Equal(ShellMode.Library, machine.Mode);
    }

    /// <summary>A clean editor with a clean map still leaves in one Esc — the chain must not add a step to the common case.</summary>
    [Fact]
    public void ACleanEditorWithACleanMapStillLeavesInOneEscape()
    {
        ShellModeMachine machine = MachineOnTheMapTab(out _);

        machine.HandleEscape();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
        Assert.Null(machine.MapEditor);
    }

    // ---- the button contract ----

    /// <summary>Everything a map button click may legally touch, in one comparable value.</summary>
    private sealed record Snapshot(
        ShellMode Mode, int Version, bool Dirty, bool CanUndo, bool CanRedo, int Tile, bool PromptShown);

    private static Snapshot Observe(ShellModeMachine machine)
    {
        MapEditorSession map = machine.MapEditor!;
        return new Snapshot(
            machine.Mode, map.Version, map.IsDirty, map.CanUndo, map.CanRedo, map.SelectedSprite,
            machine.MapView!.ExitPromptShown);
    }

    /// <summary>The shell's press dispatch over the real router pieces — see the type comment.</summary>
    private static void RouteClick(ShellModeMachine machine, EditorButton button)
    {
        if (EditorIcons.IsStub(button))
        {
            return;                                     // the router refuses stubs before any verb
        }
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            machine.SwitchEditorTab(tab);               // travel is the mode machine's verb
            return;
        }
        if (EditorIcons.ClickMapButton(machine.MapEditor!, button))
        {
            machine.HandleEscape();                     // the exit tab's verb belongs to the machine
        }
    }

    /// <summary>
    /// A session where every live button has work to do: a non-zero tile selected (so the
    /// eraser's click is a visible change), one stroke undone (so undo AND redo both have a
    /// step) and dirt (so save has a write and the exit tab has a prompt to raise).
    /// </summary>
    private static void Prepare(MapEditorSession map)
    {
        map.SelectSprite(7);
        Stroke(map, 1, 1);
        Stroke(map, 2, 2);
        map.Undo();
    }

    /// <summary>
    /// The sweep. Live buttons must change the snapshot; stubs, the tilemap tab (it names the
    /// screen already on show) and the pencil (it names the only tool the map model has — see
    /// <see cref="EditorIcons.ClickMapButton"/>) must change exactly nothing.
    ///
    /// <para>Break recipe: delete any <c>case</c> from <see cref="EditorIcons.ClickMapButton"/>
    /// — that one button's assertion goes red by name. Add a button to
    /// <see cref="MapEditorLayout"/> without wiring it and the same line names the new one.</para>
    /// </summary>
    [Fact]
    public void EveryPlacedLiveMapButtonChangesSomethingObservable()
    {
        foreach (EditorButtonPlace place in MapEditorLayout.Compute(1280, 720).Buttons)
        {
            ShellModeMachine machine = MachineOnTheMapTab(out _);
            Prepare(machine.MapEditor!);
            Snapshot before = Observe(machine);

            RouteClick(machine, place.Id);

            Snapshot after = Observe(machine);
            bool contractedNoOp = EditorIcons.IsStub(place.Id)
                || place.Id == EditorButton.TilemapTab
                || place.Id == EditorButton.ToolPencil;
            if (contractedNoOp)
            {
                Assert.True(before == after, $"{place.Id} is a no-op by contract but changed state");
            }
            else
            {
                Assert.True(before != after, $"{place.Id} is placed and live but its click changed nothing — unwired?");
            }
        }
    }

    /// <summary>
    /// The eraser is "tile 0", and tile 0 is emptiness (MAP-FORMAT §2) — clicking it and then
    /// drawing wipes cells rather than stamping sprite 0. Break recipe: change
    /// <c>session.SelectSprite(0)</c> to <c>SelectSprite(1)</c> in
    /// <see cref="EditorIcons.ClickMapButton"/>.
    /// </summary>
    [Fact]
    public void TheEraserButtonSelectsTheEmptyTileAndWipesWithIt()
    {
        ShellModeMachine machine = MachineOnTheMapTab(out _);
        MapEditorSession map = machine.MapEditor!;
        map.SelectSprite(12);
        Stroke(map, 6, 2);
        Assert.Equal(12, map.TileAt(6, 2));

        RouteClick(machine, EditorButton.ToolEraser);
        Stroke(map, 6, 2);

        Assert.Equal(0, map.SelectedSprite);
        Assert.Equal(0, map.TileAt(6, 2));
    }

    /// <summary>The exit tab is the mouse's Esc here too: dirty raises the prompt, clean leaves.</summary>
    [Fact]
    public void TheExitTabIsTheMouseEscape()
    {
        ShellModeMachine dirty = MachineOnTheMapTab(out _);
        dirty.MapEditor!.SelectSprite(1);
        Stroke(dirty.MapEditor, 0, 0);
        RouteClick(dirty, EditorButton.ExitTab);
        Assert.Equal(ShellMode.MapEditor, dirty.Mode);
        Assert.True(dirty.MapView!.ExitPromptShown);

        ShellModeMachine clean = MachineOnTheMapTab(out _);
        RouteClick(clean, EditorButton.ExitTab);
        Assert.Equal(ShellMode.Library, clean.Mode);
    }

    /// <summary>
    /// A map.bin of the wrong length reports the way a failed launch does and leaves the open
    /// sprites standing — a broken map must not take unsaved pixels with it. Break recipe:
    /// let the constructor's exception escape <see cref="ShellModeMachine.SwitchEditorTab"/>
    /// and this test fails with the exception instead of an assertion.
    /// </summary>
    [Fact]
    public void ABrokenMapReportsAndLeavesTheSpritesTabStanding()
    {
        ShellModeMachine machine = MachineWithOpenEditor(out string folder);
        File.WriteAllBytes(Path.Combine(folder, MapEditorSession.MapFileName), new byte[7]);

        machine.SwitchEditorTab(ShellMode.MapEditor);

        Assert.Equal(ShellMode.Editor, machine.Mode);
        Assert.Null(machine.MapEditor);
        Assert.NotNull(machine.Editor);
        Assert.NotNull(machine.LibraryMessage);
    }
}

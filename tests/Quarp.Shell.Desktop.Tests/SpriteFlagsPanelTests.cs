using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The flag panel the owner opened the editor and could not find (2026-08-25): the model was
/// there since wave 3b-1, the row of toggles was not. This file drives the row the way the
/// author does — through the production router
/// (<see cref="SpriteEditorInput.Update"/>), the production layout
/// (<see cref="SpriteEditorLayout.FlagRect"/>) and the production reader
/// (<see cref="ShellCommandReader"/>) — never through a mirror of any of them, because a
/// mirror stays green when the thing it mirrors is deleted. The harness below is
/// <see cref="EditorInputRouterTests"/>'s, shape for shape (that one is private to its class);
/// it is copied rather than reinvented so a frame here means the same thing a frame means
/// there.
///
/// <para><b>The rule this file pins hardest, because it is the one that could have gone three
/// different ways.</b> Our region is 1x1, 2x2 or 4x4 sprites — TIC-80's <c>sprite-&gt;size</c>
/// block — and a toggle acts on all of them at once, exactly as <c>drawFlags</c> does over
/// <c>getSpriteIndexes</c> (REFERENCES-EDITORS §2.1). When the block disagrees with itself,
/// <b><c>and</c> decides</b>: the bit comes DOWN only if it is up on every sprite of the block;
/// in every other case — none of them, or only some — a click puts it UP on all of them. So
/// the mixed state is one a click leaves behind, never one a click can create, and from any
/// starting state two clicks land on "up everywhere, then down everywhere". That is TIC-80's
/// rule carried over rather than invented here, and
/// <see cref="AClickOnAMixedBlockRaisesTheFlagOnEverySpriteRatherThanClearingIt"/> is what
/// stops a later refactor from quietly turning it into a per-sprite XOR.</para>
/// </summary>
public class SpriteFlagsPanelTests : IDisposable
{
    private readonly string _root;

    public SpriteFlagsPanelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-flags-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // One case leaves flags.bin read-only (the no-write proof, borrowed from
        // SpriteEditorSessionTests); Delete would throw on it.
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_root, recursive: true);
    }

    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    /// <summary>The console's own screen — the surface the sprite editor is laid out on since wave R2.</summary>
    private const int ConsoleWidth = 160;

    private const int ConsoleHeight = 90;

    /// <summary>One frame at 60 Hz — the router spends it on the tooltip and long-press clocks only.</summary>
    private const double FrameSeconds = 1.0 / 60.0;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    /// <summary>
    /// <see cref="EditorInputRouterTests"/>'s harness: the four shell objects the window owns,
    /// the two production readers it polls, and a back buffer that is a pair of constants
    /// instead of a presentation parameter.
    /// </summary>
    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        internal SpriteEditorSession Editor => Modes.Editor!;

        /// <summary>
        /// Rebuilt per frame, like the window's. Since wave R2 the two numbers are <b>the size
        /// of the surface the screen on show is laid out on</b>, and the sprite editor's surface
        /// is the console itself (ADR-029): 160x90, not the back buffer. <c>QuarpGame</c> makes
        /// exactly this switch — see <c>ConsoleEditorContext</c> — so a frame here means what a
        /// frame there means. The consequence for whoever writes a test against the sprite
        /// screen: <b>its mouse points are console pixels</b>, taken straight off the layout's
        /// own rectangles. Production reaches the same numbers by putting the window's point
        /// through <see cref="EditorMouse.ToConsole"/>, whose own arithmetic is pinned in
        /// <c>EditorMouseReaderTests</c> rather than re-run here.
        /// </summary>
        internal EditorShell Context =>
            Modes.Mode == ShellMode.Editor
                ? new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight)
                : new(Modes, Flyout, Hover, SheetScroll, WindowWidth, WindowHeight);

        /// <summary>The sprite screen's geometry, in console pixels — the very rectangles the renderer draws.</summary>
        internal SpriteEditorLayout Layout =>
            SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, Modes.Editor!.RegionCells);

        internal void Frame(Keys[] down, int mouseX, int mouseY, ButtonState left, double seconds)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, 0, left, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
            SpriteEditorInput.Update(Context, commands, mouse, seconds);
        }

        internal void Idle() => Frame(NoKeys, Off, Off, ButtonState.Released, FrameSeconds);

        /// <summary>A key pressed and released — a real key repeats only by being pressed again.</summary>
        internal void Tap(params Keys[] down)
        {
            Frame(down, Off, Off, ButtonState.Released, FrameSeconds);
            Idle();
        }

        internal void Click(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Pressed, FrameSeconds);
            Frame(NoKeys, x, y, ButtonState.Released, FrameSeconds);
        }

        /// <summary>Park the pointer on a point for the tracker's whole delay, in one frame's worth of calls.</summary>
        internal void HoverFor(int x, int y, double seconds)
        {
            Frame(NoKeys, x, y, ButtonState.Released, 0.0);
            Frame(NoKeys, x, y, ButtonState.Released, seconds);
        }

        /// <summary>Far outside any rectangle the layout places — an idle pointer must hit nothing.</summary>
        private const int Off = -1000;
    }

    /// <summary>
    /// A mode machine standing in the sprite editor over a one-cart library of its own, with an
    /// optional flags.bin already on disk — seeded BEFORE the session opens, so the scenarios
    /// that need a pre-existing flag start with an empty undo stack and a clean session, and a
    /// step counted below is the step a click made.
    /// </summary>
    private Harness OpenSpriteEditor(out string cartFolder, byte[]? flags = null)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        cartFolder = Path.Combine(root, "cart");
        Directory.CreateDirectory(cartFolder);
        File.WriteAllText(
            Path.Combine(cartFolder, "manifest.json"), "{\"name\":\"flags\",\"author\":\"\",\"profile\":8}");
        if (flags is not null)
        {
            File.WriteAllBytes(
                Path.Combine(cartFolder, SpriteEditorSession.FlagsFileName), flags);
        }
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();           // the real road since ADR-028: menu → library → editor
        machine.OpenLibrary();
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        return new Harness(machine);
    }

    /// <summary>A full-length flags payload with one bit already raised on one sprite.</summary>
    private static byte[] FlagsWith(int sprite, int bit)
    {
        var flags = new byte[SpriteEditorSession.FlagsPayloadSize];
        flags[sprite] = (byte)(1 << bit);
        return flags;
    }

    private static (int X, int Y) Centre(Rectangle rect) =>
        (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>
    /// The flag byte of one sprite by number. <see cref="SpriteEditorSession.Flags"/> answers
    /// only for the selected sprite by design, so reading a neighbour means moving the region —
    /// which writes nothing and pushes no undo step, so a check can be made after the fact
    /// without disturbing what is being checked.
    /// </summary>
    private static byte FlagsOf(SpriteEditorSession editor, int sprite)
    {
        editor.SelectRegionSize(1);
        editor.SelectRegionCell(
            sprite % SpriteEditorSession.GridCells, sprite / SpriteEditorSession.GridCells);
        return editor.Flags;
    }

    /// <summary>The four sprites of a 2x2 region anchored at the sheet's origin.</summary>
    private static readonly int[] Block2X2 = { 0, 1, 16, 17 };

    // ==================================================================================
    // 1. The click exists at all, and costs exactly one undo step.
    // ==================================================================================

    /// <summary>
    /// The owner's report, answered: there is a row of toggles, a click on one changes the
    /// selected sprite's flags, and the change is one step of the editor's one undo stack —
    /// undone by one Ctrl+Z, after which the stack is empty again.
    ///
    /// <para>Break recipe: delete the <c>layout.TryFlag</c> branch from
    /// <see cref="SpriteEditorInput"/>'s left-press chain. The click then falls through to
    /// nothing and the first assertion goes red — the exact defect the owner reported, in a
    /// test instead of in his hands.</para>
    /// </summary>
    [Fact]
    public void AClickOnAToggleSetsTheSelectedSpritesFlagInOneUndoStep()
    {
        Harness harness = OpenSpriteEditor(out _);
        SpriteEditorSession editor = harness.Editor;
        Assert.Equal(0, editor.Flags);
        Assert.False(editor.CanUndo);

        (int x, int y) = Centre(harness.Layout.FlagRect(3));
        harness.Click(x, y);

        Assert.Equal(0b0000_1000, editor.Flags);
        Assert.True(editor.IsFlagSet(3));
        Assert.True(editor.IsFlagsDirty);
        Assert.True(editor.CanUndo);

        editor.Undo();

        Assert.Equal(0, editor.Flags);
        Assert.False(editor.CanUndo);            // ONE step, not eight and not none
        Assert.False(editor.IsFlagsDirty);
    }

    /// <summary>
    /// Position is the number (PICO-8: "indexed from 0 starting from the left"), so the row's
    /// eight cells must land on eight different bits and on nothing else — a row wired through
    /// one shared index would pass the test above and fail this one.
    /// </summary>
    [Fact]
    public void EachCellOfTheRowOwnsItsOwnBitLeftToRight()
    {
        for (int bit = 0; bit < SpriteEditorSession.FlagBits; bit++)
        {
            Harness harness = OpenSpriteEditor(out _);
            (int x, int y) = Centre(harness.Layout.FlagRect(bit));

            harness.Click(x, y);

            Assert.Equal((byte)(1 << bit), harness.Editor.Flags);
        }
    }

    // ==================================================================================
    // 2 & 3. The region rule — TIC-80's drawFlags over getSpriteIndexes.
    // ==================================================================================

    /// <summary>
    /// At a 16 px region a toggle moves all four sprites of the block, and a second click puts
    /// all four back — one undo step each way, because the operation is the step whatever it
    /// touched.
    ///
    /// <para>Break recipe: make <see cref="SpriteEditorSession.ToggleRegionFlag"/> call
    /// <c>WriteRegionFlags(1, ...)</c> instead of <c>WriteRegionFlags(RegionCells, ...)</c>.
    /// Sprite 0 still moves and the first assertion passes; sprites 1, 16 and 17 do not, and
    /// the three after it go red.</para>
    /// </summary>
    [Fact]
    public void AtA16PxRegionOneClickSetsAllFourSpritesAndTheNextClearsThem()
    {
        Harness harness = OpenSpriteEditor(out _);
        SpriteEditorSession editor = harness.Editor;
        editor.SelectRegionSize(2);
        Assert.Equal(0, editor.RegionFlagsAny);

        (int x, int y) = Centre(harness.Layout.FlagRect(5));
        harness.Click(x, y);

        Assert.True(editor.IsFlagSetInAll(5));
        foreach (int sprite in Block2X2)
        {
            Assert.Equal(0b0010_0000, FlagsOf(editor, sprite));
        }

        editor.SelectRegionSize(2);
        editor.SelectRegionCell(0, 0);
        harness.Click(x, y);

        Assert.False(editor.IsFlagSetInAny(5));
        foreach (int sprite in Block2X2)
        {
            Assert.Equal(0, FlagsOf(editor, sprite));
        }

        // Two clicks, two steps: the second undo restores "raised on all four", not a half state.
        editor.SelectRegionSize(2);
        editor.SelectRegionCell(0, 0);
        editor.Undo();
        Assert.True(editor.IsFlagSetInAll(5));
        editor.Undo();
        Assert.False(editor.IsFlagSetInAny(5));
        Assert.False(editor.CanUndo);
    }

    /// <summary>
    /// The three-state rule's hard case, and the one the order asked to be written down: with
    /// the bit up on exactly ONE of the four sprites, the block reads "some" (any but not all)
    /// and a click takes it to "all" — TIC-80's <c>and</c> deciding, so the author's first
    /// click on a partly-flagged block always means "make this true everywhere" and never
    /// silently clears the sprite that already had it.
    ///
    /// <para>Break recipe: invert the condition in
    /// <see cref="SpriteEditorSession.ToggleRegionFlag"/> to test <c>RegionFlagsAny</c> instead
    /// of <c>RegionFlagsAll</c>. The mixed block then clears instead of filling and every
    /// assertion under the click goes red.</para>
    /// </summary>
    [Fact]
    public void AClickOnAMixedBlockRaisesTheFlagOnEverySpriteRatherThanClearingIt()
    {
        // Seeded on disk, so the session opens clean with an empty undo stack: the step counted
        // below is the click's, and nothing else.
        Harness harness = OpenSpriteEditor(out _, FlagsWith(sprite: 1, bit: 2));
        SpriteEditorSession editor = harness.Editor;
        editor.SelectRegionSize(2);

        Assert.True(editor.IsFlagSetInAny(2));       // sprite 1 has it
        Assert.False(editor.IsFlagSetInAll(2));      // the other three do not
        Assert.False(editor.CanUndo);

        (int x, int y) = Centre(harness.Layout.FlagRect(2));
        harness.Click(x, y);

        Assert.True(editor.IsFlagSetInAll(2));
        foreach (int sprite in Block2X2)
        {
            Assert.Equal(0b0000_0100, FlagsOf(editor, sprite));
        }

        // And it cost one step: undo returns the block to the mixed state it came from.
        editor.SelectRegionSize(2);
        editor.SelectRegionCell(0, 0);
        editor.Undo();
        Assert.False(editor.CanUndo);
        Assert.True(editor.IsFlagSetInAny(2));
        Assert.False(editor.IsFlagSetInAll(2));
        Assert.False(editor.IsFlagsDirty);
    }

    /// <summary>
    /// Dirt is content against the disk, not a history of clicks — the sheet's rule, held by the
    /// row too. Two clicks on a block that starts all-raised take it down and back up, and the
    /// session is clean again even though four sprites and two undo steps were touched on the
    /// way. Worth its own case because the flag bank has its own baseline
    /// (<see cref="SpriteEditorSession.IsFlagsDirty"/>) and could have grown a counter instead.
    /// </summary>
    [Fact]
    public void TwoClicksReturnTheBlockToTheDisksBytesAndTheSessionToClean()
    {
        var flags = new byte[SpriteEditorSession.FlagsPayloadSize];
        foreach (int sprite in Block2X2)
        {
            flags[sprite] = 0b0000_0001;
        }
        Harness harness = OpenSpriteEditor(out _, flags);
        SpriteEditorSession editor = harness.Editor;
        editor.SelectRegionSize(2);
        Assert.True(editor.IsFlagSetInAll(0));
        Assert.False(editor.IsFlagsDirty);

        // Down on all four (a real step), then up on all four again — back to the disk's bytes.
        (int x, int y) = Centre(harness.Layout.FlagRect(0));
        harness.Click(x, y);
        Assert.True(editor.IsFlagsDirty);
        Assert.False(editor.IsFlagSetInAny(0));
        harness.Click(x, y);

        Assert.True(editor.IsFlagSetInAll(0));
        Assert.False(editor.IsFlagsDirty);       // clean again, with two steps still on the stack
        Assert.True(editor.CanUndo);
    }

    // ==================================================================================
    // 4. Input parity — the keyboard lands exactly where the mouse lands.
    // ==================================================================================

    /// <summary>
    /// M9 stage 2.5's law over the new row: Shift+1..8 must do what a click on the matching
    /// cell does, in the same one step, on the same sprites. Two independent carts, one driven
    /// only by keys and one only by the pointer, compared byte-for-byte over the whole block —
    /// no <c>Keys</c> value exists in the mouse run and no window coordinate in the keyboard
    /// run.
    ///
    /// <para>Break recipe: delete the <c>commands.EditorFlagDigit</c> block from
    /// <see cref="SpriteEditorInput"/>, or drop the <c>EditorFlagDigit</c> assignment in
    /// <see cref="ShellCommandReader"/> — the keyboard cart stays all-zero and every comparison
    /// goes red while the mouse half still passes, which is the shape of a row that is
    /// mouse-only.</para>
    /// </summary>
    [Fact]
    public void ShiftDigitsAndClicksLandOnTheSameFlagsOfTheSameSprites()
    {
        Harness keyboard = OpenSpriteEditor(out _);
        keyboard.Editor.SelectRegionSize(2);
        keyboard.Tap(Keys.LeftShift, Keys.D4);          // digit 4 → bit 3
        keyboard.Tap(Keys.LeftShift, Keys.D8);          // digit 8 → bit 7

        Harness mouse = OpenSpriteEditor(out _);
        mouse.Editor.SelectRegionSize(2);
        (int x3, int y3) = Centre(mouse.Layout.FlagRect(3));
        mouse.Click(x3, y3);
        mouse.Editor.SelectRegionSize(2);
        mouse.Editor.SelectRegionCell(0, 0);
        (int x7, int y7) = Centre(mouse.Layout.FlagRect(7));
        mouse.Click(x7, y7);

        Assert.Equal(0b1000_1000, mouse.Editor.RegionFlagsAll);
        foreach (int sprite in Block2X2)
        {
            Assert.Equal(FlagsOf(mouse.Editor, sprite), FlagsOf(keyboard.Editor, sprite));
            Assert.Equal(0b1000_1000, FlagsOf(keyboard.Editor, sprite));
        }
        Assert.Equal(mouse.Editor.CanUndo, keyboard.Editor.CanUndo);
    }

    /// <summary>
    /// The negative half of the same split (wave 3b-2 took Shift+digit away from the toolbar):
    /// a BARE digit still picks a tool and touches no flag, and a Shift+digit still picks a
    /// flag and touches no tool. Without this, the two meanings could quietly collapse back
    /// into one.
    /// </summary>
    [Fact]
    public void BareDigitsStayOnTheToolbarAndShiftedOnesStayOnTheFlagRow()
    {
        Harness harness = OpenSpriteEditor(out _);
        SpriteEditorSession editor = harness.Editor;

        harness.Tap(Keys.D3);                            // the bucket
        Assert.Equal(SpriteEditorTool.Fill, editor.Tool);
        Assert.Equal(0, editor.Flags);

        harness.Tap(Keys.LeftShift, Keys.D3);            // bit 2
        Assert.Equal(0b0000_0100, editor.Flags);
        Assert.Equal(SpriteEditorTool.Fill, editor.Tool);  // the tool did not move with it
    }

    /// <summary>
    /// The toggles are discoverable the way every other control on this screen is: the hover
    /// tooltip names the bit AND the key that reaches it (the parity law's other half — a mouse
    /// user must be able to learn the keyboard from the screen), and it names the group rule,
    /// because a click that moves four sprites at once must not do so in silence.
    /// </summary>
    [Fact]
    public void HoveringAToggleRaisesATooltipThatNamesTheBitTheKeyAndTheRegionRule()
    {
        Harness harness = OpenSpriteEditor(out _);
        (int x, int y) = Centre(harness.Layout.FlagRect(3));

        harness.HoverFor(x, y, IconHoverTracker.TooltipDelaySeconds);

        Assert.Equal(HoverTarget.OfFlag(3), harness.Hover.Target);
        Assert.True(harness.Hover.TooltipVisible);
        string tooltip = EditorIcons.FlagTooltip(3);
        Assert.Contains("FLAG 3", tooltip, StringComparison.Ordinal);
        Assert.Contains("SHIFT+4", tooltip, StringComparison.Ordinal);
        Assert.Contains("REGION", tooltip, StringComparison.Ordinal);
        // Hovering is not clicking: the frame lights up, the flags do not move.
        Assert.Equal(0, harness.Editor.Flags);
    }

    // ==================================================================================
    // 5. The row reaches the disk, and only when it has something to say.
    // ==================================================================================

    /// <summary>
    /// The save contract, from the panel's end: flags set through the row survive a save into
    /// flags.bin at its exact length and reload into a fresh session, a flag-only edit writes
    /// that file and nothing else (no gfx.png, no gfx-layers.png), and the reopened clean
    /// session writes nothing at all — proven with a read-only file, so a write ATTEMPT would
    /// fail loudly and "no error" means "no write".
    ///
    /// <para>Break recipe: drop the <c>IsFlagsDirty</c> branch from
    /// <see cref="SpriteEditorSession.Save"/> — the file never appears and the first assertions
    /// go red; make that branch unconditional instead and the read-only assertion goes red.</para>
    /// </summary>
    [Fact]
    public void FlagsSetFromTheRowSurviveSaveAndACleanSessionWritesNothing()
    {
        Harness harness = OpenSpriteEditor(out string folder);
        SpriteEditorSession editor = harness.Editor;
        editor.SelectRegionCell(3, 2);                    // sprite 35, away from the default anchor
        int sprite = editor.SpriteIndex;

        (int x, int y) = Centre(harness.Layout.FlagRect(6));
        harness.Click(x, y);
        Assert.True(editor.IsFlagsDirty);
        Assert.False(editor.IsLayersDirty);
        Assert.True(editor.Save());

        string flagsPath = Path.Combine(folder, SpriteEditorSession.FlagsFileName);
        Assert.True(File.Exists(flagsPath));
        Assert.Equal(SpriteEditorSession.FlagsPayloadSize, new FileInfo(flagsPath).Length);
        Assert.Equal((byte)0b0100_0000, File.ReadAllBytes(flagsPath)[sprite]);
        // A flag-only save touches the flag file alone: only it and the manifest are on disk.
        string[] written = Directory.GetFiles(folder);
        Assert.Equal(2, written.Length);
        Assert.Contains(written, f => Path.GetFileName(f) == SpriteEditorSession.FlagsFileName);
        Assert.DoesNotContain(written, f => Path.GetFileName(f) == "gfx.png");
        Assert.DoesNotContain(written, f => Path.GetFileName(f) == SpriteEditorSession.LayersFileName);
        Assert.False(editor.IsDirty);

        // Reopened: the bit is back, the session is clean, and Save writes nothing — the file is
        // read-only, so any attempt would land in SaveError instead of passing silently.
        File.SetAttributes(flagsPath, FileAttributes.ReadOnly);
        DateTime before = File.GetLastWriteTimeUtc(flagsPath);
        var reopened = new SpriteEditorSession(folder);
        reopened.SelectRegionCell(3, 2);
        Assert.True(reopened.IsFlagSet(6));
        Assert.False(reopened.IsDirty);

        Assert.True(reopened.Save());

        Assert.Null(reopened.SaveError);
        Assert.Equal(before, File.GetLastWriteTimeUtc(flagsPath));
    }

    // ==================================================================================
    // Geometry — the row is where the seventh review says the column's blocks go.
    // ==================================================================================

    /// <summary>
    /// The middle column's rhythm, re-pinned for the console (wave R2) — <b>and this paragraph
    /// is the explanation the re-pin owes.</b> The seventh review's law was "one left edge, equal
    /// gaps" down the whole content column, and it survives the move intact; what changed is what
    /// the column contains and how wide the gaps are. At host resolution the column held palette,
    /// tab row, flag row, sheet and slider, spaced by <c>2 * ui</c>. On the console the sheet
    /// window has to be sixty-four rows tall — the strip's own height, at the only scale ninety
    /// rows allow — so it takes a column of its own at the screen's right edge, and the middle
    /// column keeps the three blocks that fit in twenty pixels: palette, flags, layer tabs, in
    /// that order, one clear pixel apart. The gap is one pixel because a console pixel is what a
    /// gap is here; <c>ui</c> does not exist on this screen and neither does anything to
    /// multiply by it.
    ///
    /// <para>Break recipe: anchor <c>flagPanel</c> to anything but <c>middleX</c> in
    /// <see cref="SpriteEditorLayout.Compute"/> — the shared-left-edge assertions go red; drop
    /// the <c>+ 1</c> from <c>layerTabsY</c> and the flag row lands on the tabs, which the
    /// overlap assertions catch.</para>
    /// </summary>
    [Fact]
    public void TheFlagRowKeepsTheColumnsLeftEdgeAndSpacing()
    {
        var layout = SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, regionCells: 1);
        Rectangle tabs = layout.ButtonRect(EditorButton.LayerTab1);
        Rectangle lastTab = layout.ButtonRect(EditorButton.LayerTab5);

        Assert.Equal(layout.Swatches.X, layout.FlagPanel.X);
        Assert.Equal(tabs.X, layout.FlagPanel.X);
        Assert.Equal(layout.SwatchSize, layout.FlagSize);        // the palette's own cell

        // Under the palette, over the tabs, one clear pixel from each — the column's rhythm.
        Assert.Equal(layout.Swatches.Bottom + 1, layout.FlagPanel.Y);
        Assert.Equal(layout.FlagPanel.Bottom + 1, tabs.Y);
        Assert.True(lastTab.Bottom <= layout.Chrome.ContentBottom);

        Assert.False(layout.FlagPanel.Intersects(layout.Sheet));
        Assert.False(layout.FlagPanel.Intersects(layout.Swatches));
        Assert.False(layout.FlagPanel.Intersects(layout.Canvas));
        Assert.False(layout.FlagPanel.Intersects(tabs));
        Assert.False(layout.FlagPanel.Intersects(lastTab));
        Assert.True(new Rectangle(0, 0, ConsoleWidth, ConsoleHeight).Contains(layout.FlagPanel));
    }

    /// <summary>
    /// The discipline every clickable rectangle on this screen is held to: eight cells, each
    /// hitting itself and nothing else, disjoint, all inside the panel.
    ///
    /// <para>Re-pinned in wave R2: the theory used to sweep five window sizes, and there is one
    /// surface now — the console. The mark assertion went with the move too, because the mark and
    /// the cell became the same four pixels; see <c>SpriteEditorRenderer.DrawFlags</c> for why a
    /// smaller mark inside a bigger cell has nothing left to be smaller than.</para>
    /// </summary>
    [Fact]
    public void FlagHitTestsRoundTripThroughTheirRectangles()
    {
        var layout = SpriteEditorLayout.Compute(ConsoleWidth, ConsoleHeight, regionCells: 1);

        for (int bit = 0; bit < SpriteEditorSession.FlagBits; bit++)
        {
            Rectangle cell = layout.FlagRect(bit);
            Assert.True(layout.TryFlag(cell.Center.X, cell.Center.Y, out int hit));
            Assert.Equal(bit, hit);
            Assert.True(layout.FlagPanel.Contains(cell));
            for (int other = bit + 1; other < SpriteEditorSession.FlagBits; other++)
            {
                Assert.False(cell.Intersects(layout.FlagRect(other)));
            }
        }
        // One cell further along is off the row — the hit test is bounded by FlagBits.
        Assert.False(layout.TryFlag(layout.FlagPanel.Right + layout.FlagSize, layout.FlagPanel.Y, out _));
    }
}

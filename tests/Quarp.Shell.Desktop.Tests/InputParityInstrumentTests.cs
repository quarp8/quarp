using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The wave-2j parity instrument (M9 stage 2.5's law: every editor action must be reachable
/// BOTH by keyboard alone and by mouse alone). The direct question this file answers:
///
/// <para><b>The seam is <see cref="SpriteEditorSession"/> itself, plus the two pure helper
/// types either input world's own translation goes through before it lands there —
/// <see cref="SpriteEditorLayout"/> (a pure function of window size, no <c>GraphicsDevice</c>
/// needed: <see cref="SpriteEditorLayout.Compute"/> takes plain ints) turns a mouse point into
/// a button id, a swatch, a sheet cell or a canvas pixel, and <see cref="EditorIcons"/>
/// (a static routing table, extracted in wave 2g precisely so it could be tested without a
/// window — see <see cref="EditorButtonContractTests"/>) turns a button id or a keyboard digit
/// into the session calls. Both are exercised here through the REAL reader classes
/// (<see cref="ShellCommandReader"/>, <see cref="EditorMouseReader"/>) so the edge-detection
/// they own is not reinvented, not through a real window — <c>QuarpGame</c> needs a
/// <c>GraphicsDevice</c> to construct and cannot run in this process. What is left of
/// <c>QuarpGame.UpdateEditor</c> after subtracting the window (the four-line canvas-gesture
/// dispatch and the frame's press/drag/release ordering) is mirrored here the same way
/// <see cref="EditorButtonContractTests.RouteClick"/> already mirrors the press-kind dispatch —
/// one precedented exception, not a second copy of editor policy: every verb the mirror calls
/// (<c>BeginStroke</c>, <c>Paint</c>, <c>FlipHorizontal</c>, <c>ApplyTransform</c>,
/// <c>Save</c>, …) is <see cref="SpriteEditorSession"/>'s own public method, called nowhere
/// twice for both channels — the two channels differ exactly where a human's hands differ:
/// how <c>(x, y)</c> or "which key" gets computed, never in what gets called once it is.</b></para>
///
/// <para><b>The claim proved: BOTH the sheet and the saved files.</b>
/// <see cref="KeyboardOnlyAndMouseOnlyRunsProduceByteIdenticalSheetsAndFiles"/> runs one
/// recognizable scenario (pick a color, draw a 7-pixel "L" — not one pixel, apply the
/// transform slot's flip, save) through a keyboard-only run and a mouse-only run against two
/// independent cart folders, then asserts <see cref="SpriteEditorSession.Pixels"/> agree
/// byte-for-byte AND both saved <c>gfx.png</c> and <c>gfx-layers.png</c> agree byte-for-byte —
/// no mouse coordinate exists anywhere in the keyboard run's call graph and no <c>Keys</c>
/// value exists anywhere in the mouse run's.</para>
///
/// <para><b>The live-button list, completed.</b>
/// <see cref="EditorIconsTests.LiveTooltipsNameTheirHotkeys"/> already spot-checks that live
/// tooltips name their hotkey; it never named <c>ToolSelect</c>, <c>ToolStamp</c>, or four of
/// the five layer tabs. <see cref="EveryLiveToolbarAndStatusButtonNamesAKeyboardTwin"/> does
/// not repeat that test — it drives the same fact from the button list
/// <see cref="SpriteEditorLayout.Compute"/> actually places (like
/// <see cref="EditorButtonContractTests.EveryPlacedLiveButtonChangesSomethingObservable"/>
/// does for wiring), so every live button not yet named there is now covered and a future
/// button placed without an entry in this sweep's table turns it red on arrival, the same
/// defect class <c>EditorButtonContractTests</c> closed for wiring.</para>
///
/// <para><b>The gap this instrument was written to find, and the owner found first: sprite
/// SELECTION had no keyboard twin at all.</b> <see cref="SpriteEditorSession.SelectRegionCell"/>
/// — which of the 256 sprites the toolbar, the canvas and Save act on — used to be called from
/// exactly one place in the whole tree: the mouse's sheet-grid hit test in
/// <c>QuarpGame.UpdateEditor</c>. No <see cref="ShellCommands"/> field reached it; Tab and the
/// size toggle change <see cref="SpriteEditorSession.RegionCells"/> (the region's SIZE) and never
/// touch its POSITION, so the whole scenario could be walked keyboard-only as long as nobody
/// tried to edit a SECOND sprite. Wave 2k wired Shift+arrows through
/// <see cref="SheetStrip"/>; <see cref="ShiftArrowsPickTheSpriteAndBareKeysNeverDo"/> pins both
/// halves — that bare keys still never move the anchor, and that the key path now lands exactly
/// where the click path lands.</para>
/// </summary>
public class InputParityInstrumentTests : IDisposable
{
    private readonly string _root;

    public InputParityInstrumentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-input-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FreshCartFolder(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ==================================================================================
    // Shared mirrors of the tiny dispatch QuarpGame.UpdateEditor owns and cannot expose
    // (it needs a GraphicsDevice to construct at all). Both the keyboard and the mouse
    // driver below call into these — never into each other — so the two channels can
    // never accidentally collapse into "the same method called twice".
    // ==================================================================================

    /// <summary>Verbatim mirror of QuarpGame's private BeginCanvasGesture — see that method's own doc.</summary>
    private static void BeginCanvasGestureMirror(SpriteEditorSession editor, int localX, int localY)
    {
        switch (editor.Tool)
        {
            case SpriteEditorTool.Fill:
                editor.Fill(localX, localY);
                break;
            case SpriteEditorTool.Shape:
                editor.BeginShape(localX, localY);
                break;
            case SpriteEditorTool.Select:
                editor.BeginSelect(localX, localY);
                break;
            case SpriteEditorTool.Stamp:
                editor.StampAt(localX, localY);
                break;
            default:
                editor.BeginStroke();
                editor.Paint(localX, localY);
                break;
        }
    }

    /// <summary>Verbatim mirror of QuarpGame's private EndCanvasGesture.</summary>
    private static void EndCanvasGestureMirror(SpriteEditorSession editor)
    {
        if (editor.ShapeActive)
        {
            editor.CommitShape();
        }
        else if (editor.SelectionGestureActive)
        {
            editor.CommitSelect();
        }
        else
        {
            editor.EndStroke();
        }
    }

    /// <summary>Verbatim mirror of QuarpGame's private RefreshGestures.</summary>
    private static void RefreshGesturesMirror(SpriteEditorSession editor, bool shapeFill)
    {
        if (editor.ShapeActive)
        {
            editor.UpdateShape(editor.CursorX, editor.CursorY, shapeFill);
        }
        if (editor.SelectionGestureActive)
        {
            editor.UpdateSelect(editor.CursorX, editor.CursorY);
        }
    }

    // ==================================================================================
    // Channel A: keyboard only. Every frame goes through the REAL ShellCommandReader (the
    // production edge detector), then through a mirror of the keyboard-reachable half of
    // UpdateEditor. No mouse type is referenced anywhere below this line.
    // ==================================================================================

    private static void ApplyKeyboardFrame(SpriteEditorSession editor, in ShellCommands c)
    {
        if (c.EditorUndo)
        {
            editor.Undo();
        }
        if (c.EditorRedo)
        {
            editor.Redo();
        }
        if (c.EditorSave)
        {
            editor.Save();
        }
        if (c.EditorToolToggle)
        {
            editor.ToggleTool();
        }
        EditorIcons.PressToolDigit(editor, c.EditorToolDigit);
        if (c.EditorRegionCycle)
        {
            editor.CycleRegionSize();
        }
        if (c.EditorFlipH)
        {
            editor.FlipHorizontal();
        }
        if (c.EditorFlipV)
        {
            editor.FlipVertical();
        }
        if (c.EditorRotate)
        {
            editor.RotateClockwise();
        }
        if (c.EditorClear)
        {
            editor.ClearRegion();
        }
        if (c.EditorColorPrev)
        {
            editor.SelectColor((editor.CurrentColor + Palette.VisibleCount - 1) % Palette.VisibleCount);
        }
        if (c.EditorColorNext)
        {
            editor.SelectColor((editor.CurrentColor + 1) % Palette.VisibleCount);
        }
        if (c.EditorLayerUp)
        {
            editor.SelectLayer(editor.ActiveLayerIndex + 1);
        }
        if (c.EditorLayerDown)
        {
            editor.SelectLayer(editor.ActiveLayerIndex - 1);
        }

        // NOT a mirror: the shell and this driver call the SAME owner of the step,
        // EditorSheetStep.Apply. That is the whole point of the type existing — a copy here
        // would stay green if QuarpGame's call were deleted. The scroll that follows the call
        // there is view state and has no effect on the session, so it is absent here.
        if (c.EditorSheetDx != 0 || c.EditorSheetDy != 0)
        {
            EditorSheetStep.Apply(editor, c.EditorSheetDx, c.EditorSheetDy);
        }

        bool steppedSheet = c.EditorSheetDx != 0 || c.EditorSheetDy != 0;
        int dx = steppedSheet ? 0 : (c.MenuRight ? 1 : 0) - (c.MenuLeft ? 1 : 0);
        int dy = steppedSheet ? 0 : (c.MenuDown ? 1 : 0) - (c.MenuUp ? 1 : 0);
        if (dx != 0 || dy != 0)
        {
            editor.MoveCursor(dx, dy);
            if (editor.StrokeActive && c.EditorPaintDown)
            {
                editor.Paint(editor.CursorX, editor.CursorY);
            }
        }
        if (c.EditorPaintPressed)
        {
            BeginCanvasGestureMirror(editor, editor.CursorX, editor.CursorY);
        }
        RefreshGesturesMirror(editor, c.EditorShapeFill);
        if (c.EditorPaintReleased)
        {
            EndCanvasGestureMirror(editor);
        }
        if (c.MenuEditor)
        {
            editor.PickColor(editor.CursorX, editor.CursorY);
        }
    }

    /// <summary>One KeyboardState frame through the real reader, then the mirror above.</summary>
    private static void KeyFrame(ShellCommandReader reader, SpriteEditorSession editor, params Keys[] down) =>
        ApplyKeyboardFrame(editor, reader.Read(new KeyboardState(down)));

    /// <summary>
    /// The keyboard-only run of the order's scenario: pick color 7 by stepping the palette
    /// seven times with '.', walk an L (7 pixels: down the left edge, across the bottom) by
    /// holding Z and stepping the arrows, flip with the direct F hotkey, save with Ctrl+S.
    /// Every arrow press is released before the next one presses again — a real key repeats
    /// only by being pressed again, exactly what <see cref="EditorKeysAndStatusLayoutTests"/>
    /// pins about held vs. pressed.
    /// </summary>
    private static void RunKeyboardOnlyScenario(SpriteEditorSession editor)
    {
        var reader = new ShellCommandReader();

        // Sprite selection has no keyboard route (see the class doc) — this run simply keeps
        // the region the session already opened on, sprite #0.
        for (int i = 0; i < 7; i++)
        {
            KeyFrame(reader, editor, Keys.OemPeriod);
            KeyFrame(reader, editor);
        }
        Assert.Equal(7, editor.CurrentColor);

        KeyFrame(reader, editor, Keys.Right);
        KeyFrame(reader, editor);
        KeyFrame(reader, editor, Keys.Down);
        KeyFrame(reader, editor);
        Assert.Equal((1, 1), (editor.CursorX, editor.CursorY));

        KeyFrame(reader, editor, Keys.Z);               // press: BeginStroke + Paint(1,1)
        KeyFrame(reader, editor, Keys.Z, Keys.Down);     // (1,2)
        KeyFrame(reader, editor, Keys.Z);
        KeyFrame(reader, editor, Keys.Z, Keys.Down);     // (1,3)
        KeyFrame(reader, editor, Keys.Z);
        KeyFrame(reader, editor, Keys.Z, Keys.Down);     // (1,4)
        KeyFrame(reader, editor, Keys.Z);
        KeyFrame(reader, editor, Keys.Z, Keys.Right);    // (2,4)
        KeyFrame(reader, editor, Keys.Z);
        KeyFrame(reader, editor, Keys.Z, Keys.Right);    // (3,4)
        KeyFrame(reader, editor, Keys.Z);
        KeyFrame(reader, editor, Keys.Z, Keys.Right);    // (4,4)
        KeyFrame(reader, editor);                        // release: EndStroke

        KeyFrame(reader, editor, Keys.F);
        KeyFrame(reader, editor);

        KeyFrame(reader, editor, Keys.LeftControl, Keys.S);
        KeyFrame(reader, editor);
    }

    // ==================================================================================
    // Channel B: mouse only. Every frame goes through the REAL EditorMouseReader, hit-tests
    // against a REAL SpriteEditorLayout (a pure function of window size — no GraphicsDevice),
    // then through a mirror of the mouse-reachable half of UpdateEditor, same ordering law
    // (an armed group slot's release is judged before a fresh press). No Keys value is
    // referenced anywhere below this line.
    // ==================================================================================

    private static void DispatchMousePress(
        SpriteEditorSession editor, ToolbarFlyout flyout, in SpriteEditorLayout layout, int x, int y)
    {
        if (layout.TryButton(x, y, out EditorButton pressed))
        {
            if (EditorIcons.IsStub(pressed))
            {
                return;
            }
            if (EditorIcons.IsGroupSlot(pressed))
            {
                flyout.Arm(pressed);
                return;
            }
            EditorIcons.ClickButton(editor, pressed);   // the exit tab's bool is the mode
            return;                                     // machine's job — unused by this scenario
        }
        if (layout.TrySwatch(x, y, out int color))
        {
            editor.SelectColor(color);
            return;
        }
        if (layout.TrySheetCell(x, y, 0, out int cellX, out int cellY))
        {
            editor.SelectRegionCell(cellX, cellY);
            return;
        }
        if (layout.TryCanvasPixel(x, y, out int localX, out int localY))
        {
            BeginCanvasGestureMirror(editor, localX, localY);
        }
    }

    private static void MouseFrame(
        SpriteEditorSession editor, ToolbarFlyout flyout, in SpriteEditorLayout layout, in EditorMouse mouse)
    {
        if (flyout.ArmedSlot is not null)
        {
            if (mouse.LeftDown)
            {
                return;
            }
            if (flyout.CompleteClick(out EditorButton clicked))
            {
                if (EditorIcons.ClickOpensFlyout(clicked))
                {
                    flyout.Open(clicked);
                }
                else
                {
                    EditorIcons.ClickGroupSlot(editor, clicked);
                }
                return;
            }
        }
        if (mouse.LeftPressed)
        {
            DispatchMousePress(editor, flyout, layout, mouse.X, mouse.Y);
        }
        else if (mouse.LeftDown && editor.StrokeActive)
        {
            layout.ClampCanvasPixel(mouse.X, mouse.Y, out int dragX, out int dragY);
            editor.SetCursor(dragX, dragY);
            editor.Paint(dragX, dragY);
        }
        RefreshGesturesMirror(editor, shapeFill: false);
        if (mouse.LeftReleased)
        {
            EndCanvasGestureMirror(editor);
        }
    }

    private static (int X, int Y) CanvasPoint(in SpriteEditorLayout layout, int localX, int localY) =>
        (layout.Canvas.X + localX * layout.CanvasScale + layout.CanvasScale / 2,
         layout.Canvas.Y + localY * layout.CanvasScale + layout.CanvasScale / 2);

    private static void Frame(
        SpriteEditorSession editor, ToolbarFlyout flyout, in SpriteEditorLayout layout,
        EditorMouseReader reader, int x, int y, ButtonState left)
    {
        var state = new MouseState(
            x, y, 0, left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        EditorMouse mouse = reader.Read(state);
        MouseFrame(editor, flyout, layout, mouse);
    }

    /// <summary>A whole click: press then release at the same point, both edges through the real reader.</summary>
    private static void Click(
        SpriteEditorSession editor, ToolbarFlyout flyout, in SpriteEditorLayout layout,
        EditorMouseReader reader, int x, int y)
    {
        Frame(editor, flyout, layout, reader, x, y, ButtonState.Pressed);
        Frame(editor, flyout, layout, reader, x, y, ButtonState.Released);
    }

    /// <summary>
    /// The mouse-only run of the SAME scenario: click sheet cell (0,0) (a real hit on the
    /// grid, even though it lands on the already-current sprite — see the class doc's gap),
    /// click swatch 7, press-drag-drag-release the SAME three waypoints the keyboard's arrows
    /// visited (Bresenham fills the same pixels either way — one owner of the line formula,
    /// <see cref="SpriteEditorSession"/>'s own <c>TraceLine</c>), short-click the transform
    /// slot (arm, then release before the long-press clock — <see cref="ToolbarFlyout"/>'s own
    /// contract), then click Save.
    /// </summary>
    private static void RunMouseOnlyScenario(SpriteEditorSession editor)
    {
        var layout = SpriteEditorLayout.Compute(1280, 720, regionCells: 1);
        var mouseReader = new EditorMouseReader();
        var flyout = new ToolbarFlyout();

        Click(editor, flyout, layout, mouseReader, layout.Sheet.X, layout.Sheet.Y);

        Rectangle swatch7 = layout.SwatchRect(7);
        Click(editor, flyout, layout, mouseReader, swatch7.X + swatch7.Width / 2, swatch7.Y + swatch7.Height / 2);
        Assert.Equal(7, editor.CurrentColor);

        (int px1, int py1) = CanvasPoint(layout, 1, 1);
        (int px2, int py2) = CanvasPoint(layout, 1, 4);
        (int px3, int py3) = CanvasPoint(layout, 4, 4);
        Frame(editor, flyout, layout, mouseReader, px1, py1, ButtonState.Pressed);
        Frame(editor, flyout, layout, mouseReader, px2, py2, ButtonState.Pressed);
        Frame(editor, flyout, layout, mouseReader, px3, py3, ButtonState.Pressed);
        Frame(editor, flyout, layout, mouseReader, px3, py3, ButtonState.Released);

        Rectangle transformButton = layout.ButtonRect(EditorButton.ToolTransform);
        int tx = transformButton.X + transformButton.Width / 2;
        int ty = transformButton.Y + transformButton.Height / 2;
        Frame(editor, flyout, layout, mouseReader, tx, ty, ButtonState.Pressed);
        Frame(editor, flyout, layout, mouseReader, tx, ty, ButtonState.Released);

        Rectangle saveButton = layout.ButtonRect(EditorButton.Save);
        int sx = saveButton.X + saveButton.Width / 2;
        int sy = saveButton.Y + saveButton.Height / 2;
        Frame(editor, flyout, layout, mouseReader, sx, sy, ButtonState.Pressed);
        Frame(editor, flyout, layout, mouseReader, sx, sy, ButtonState.Released);
    }

    // ==================================================================================
    // The instrument itself.
    // ==================================================================================

    /// <summary>
    /// The wave's direct deliverable. Negative control: comment out the mouse run's F-slot
    /// click (or the keyboard run's <c>Keys.F</c> press) — one channel then saves the
    /// unflipped L while the other saves the flipped one, and every assertion below (the
    /// pixels, both files) goes red together, never silently passing on a coincidence. A
    /// second recipe: change <see cref="CanvasPoint"/>'s rounding (drop the
    /// <c>CanvasScale / 2</c> centring) — a big enough canvas scale can then round a mouse
    /// waypoint onto the neighbouring pixel, and the pixel comparison goes red while the file
    /// comparison (which only cares about what got saved, not which channel is "right") goes
    /// red with it, showing the same defect from two angles.
    /// </summary>
    [Fact]
    public void KeyboardOnlyAndMouseOnlyRunsProduceByteIdenticalSheetsAndFiles()
    {
        string folderA = FreshCartFolder("keyboard-run");
        string folderB = FreshCartFolder("mouse-run");

        var editorA = new SpriteEditorSession(folderA);
        RunKeyboardOnlyScenario(editorA);

        var editorB = new SpriteEditorSession(folderB);
        RunMouseOnlyScenario(editorB);

        // The claim itself: two runs, two input channels sharing not one coordinate or key —
        // the same sheet.
        Assert.True(editorA.Pixels.SequenceEqual(editorB.Pixels));

        // Sanity against a vacuous pass (two blank sheets "agreeing" for free): seven struck
        // pixels survive the flip — it only relocates them — every one still color 7.
        int ink = 0;
        foreach (byte p in editorA.Pixels)
        {
            if (p != 0)
            {
                ink++;
            }
        }
        Assert.Equal(7, ink);
        foreach (byte p in editorA.Pixels)
        {
            Assert.True(p is 0 or 7);
        }

        Assert.False(editorA.IsDirty);   // both channels saved through their own Save route
        Assert.False(editorB.IsDirty);

        byte[] gfxA = File.ReadAllBytes(Path.Combine(folderA, "gfx.png"));
        byte[] gfxB = File.ReadAllBytes(Path.Combine(folderB, "gfx.png"));
        Assert.True(gfxA.AsSpan().SequenceEqual(gfxB));

        byte[] layersA = File.ReadAllBytes(Path.Combine(folderA, SpriteEditorSession.LayersFileName));
        byte[] layersB = File.ReadAllBytes(Path.Combine(folderB, SpriteEditorSession.LayersFileName));
        Assert.True(layersA.AsSpan().SequenceEqual(layersB));
    }

    /// <summary>
    /// Completes <see cref="EditorIconsTests.LiveTooltipsNameTheirHotkeys"/>'s spot check into
    /// an exhaustive sweep, driven off the button list the layout actually places (like
    /// <see cref="EditorButtonContractTests.EveryPlacedLiveButtonChangesSomethingObservable"/>
    /// does for wiring): every live button must own an entry naming its hotkey token here, and
    /// its tooltip must actually contain that token. Two live buttons that spot check never
    /// named (<c>ToolSelect</c>, <c>ToolStamp</c>) and four of the five layer tabs (only
    /// LayerTab3 was ever checked) are covered for the first time by this sweep. The size
    /// toggle's flyout list and the shape/select/transform variants stay
    /// <see cref="EditorIconsTests.VariantTooltipsExistAndNameTheKeys"/>'s fact, not repeated
    /// here.
    ///
    /// <para>Negative control: delete the "1" from <c>EditorIcons.Tooltip(EditorButton.ToolSelect)</c>'s
    /// text (or any other token in the table below) — that one button's assertion goes red by
    /// name, not the whole sweep, so the report can say exactly which live button lost its
    /// keyboard twin's advertisement.</para>
    /// </summary>
    [Fact]
    public void EveryLiveToolbarAndStatusButtonNamesAKeyboardTwin()
    {
        var expectedHotkeyToken = new Dictionary<EditorButton, string>
        {
            [EditorButton.ExitTab] = "ESC",
            [EditorButton.ToolSelect] = "1",
            [EditorButton.ToolPencil] = "2",
            [EditorButton.ToolFill] = "3",
            [EditorButton.ToolStamp] = "4",
            [EditorButton.ToolShape] = "5",
            [EditorButton.ToolTransform] = "F/V/R",
            [EditorButton.Clear] = "DEL",
            [EditorButton.Save] = "CTRL+S",
            [EditorButton.Undo] = "CTRL+Z",
            [EditorButton.Redo] = "CTRL+Y",
            [EditorButton.SizeToggle] = "TAB",
            [EditorButton.LayerTab1] = "PGUP",
            [EditorButton.LayerTab2] = "PGUP",
            [EditorButton.LayerTab3] = "PGUP",
            [EditorButton.LayerTab4] = "PGUP",
            [EditorButton.LayerTab5] = "PGUP",
        };

        foreach (EditorButtonPlace place in SpriteEditorLayout.Compute(1280, 720, regionCells: 1).Buttons)
        {
            EditorButton button = place.Id;
            if (EditorIcons.IsStub(button) || button == EditorButton.SpritesTab)
            {
                // Stubs answer "when", not a hotkey — they have none yet (EditorIcons.IsStub is
                // the one owner of that list). The sprites tab names the mode already on
                // screen; neither owes the mouse user a key to learn.
                continue;
            }
            Assert.True(
                expectedHotkeyToken.TryGetValue(button, out string? token),
                $"{button} is live and placed but this sweep's table does not know its hotkey — " +
                "add it here, not only to the tooltip text.");
            Assert.Contains(token!, EditorIcons.Tooltip(button), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The gap this instrument was written to find, now closed and pinned the other way round
    /// (wave 2k): sprite SELECTION — which of the 256 sprites the toolbar, the canvas and Save
    /// act on — had no keyboard route at all until Shift+arrows were wired, and the owner found
    /// it by hand before this file could. Two facts are pinned together, because either alone
    /// can go quietly wrong: bare editor keys must never move the region ANCHOR (they steer the
    /// cursor, cycle tools, resize the region), and Shift+arrows must move it exactly where a
    /// click on the same strip cell would.
    ///
    /// <para>Negative controls, all against production code: drop the Shift guard from
    /// MenuLeft/MenuRight in <c>ShellCommandReader</c> and the first assertion goes red (a bare
    /// arrow starts moving the anchor); break the clamp or the strip mapping in
    /// <see cref="EditorSheetStep.Apply"/> and the two channels stop landing on the same cell;
    /// break <c>SpriteEditorLayout.TrySheetCell</c> and the mouse half misses. What this test
    /// still cannot reach is the one line in <c>QuarpGame.UpdateEditor</c> that calls
    /// <see cref="EditorSheetStep.Apply"/>: that method needs a GraphicsDevice to construct, so
    /// its dispatch is covered by the owner's eyes, not by this file. Said out loud rather than
    /// papered over — a mirror of that dispatch would have been green with the call deleted.</para>
    /// </summary>
    [Fact]
    public void ShiftArrowsPickTheSpriteAndBareKeysNeverDo()
    {
        string folder = FreshCartFolder("region-anchor-parity");
        var editor = new SpriteEditorSession(folder);
        var reader = new ShellCommandReader();
        Assert.Equal((0, 0), (editor.RegionCellX, editor.RegionCellY));

        Keys[] everyBareEditorKey =
        {
            Keys.Z, Keys.X, Keys.B, Keys.Tab, Keys.F, Keys.V, Keys.R, Keys.Delete,
            Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.OemComma, Keys.OemPeriod,
            Keys.PageUp, Keys.PageDown, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6,
        };
        foreach (Keys key in everyBareEditorKey)
        {
            KeyFrame(reader, editor, key);
            KeyFrame(reader, editor);
        }
        KeyFrame(reader, editor, Keys.LeftControl, Keys.Z);
        KeyFrame(reader, editor);
        KeyFrame(reader, editor, Keys.LeftControl, Keys.Y);
        KeyFrame(reader, editor);
        KeyFrame(reader, editor, Keys.LeftControl, Keys.S);
        KeyFrame(reader, editor);

        // Bare keys: the anchor stayed put, while Tab did do its own, different job — the
        // region SIZE moved. That contrast is what tells the two facts apart.
        Assert.Equal((0, 0), (editor.RegionCellX, editor.RegionCellY));
        Assert.Equal(2, editor.RegionCells);

        // Shift+arrows: three steps right and one down, in strip space, from a region back at
        // the sheet's origin and back at one cell, so the anchor arithmetic is not clouded by
        // the size Tab left behind.
        editor.SelectRegionSize(1);
        editor.SelectRegionCell(0, 0);
        for (int i = 0; i < 3; i++)
        {
            KeyFrame(reader, editor, Keys.LeftShift, Keys.Right);
            KeyFrame(reader, editor, Keys.LeftShift);
        }
        KeyFrame(reader, editor, Keys.LeftShift, Keys.Down);
        KeyFrame(reader, editor, Keys.LeftShift);

        // The mouse twin is a real click, not a restatement: it goes through the same
        // EditorMouseReader and the same layout hit test the shell uses, at the window pixel
        // of that strip cell. Two channels that share only the session are what parity means.
        var mouseTwin = new SpriteEditorSession(FreshCartFolder("region-anchor-parity-mouse"));
        mouseTwin.SelectRegionSize(1);
        var layout = SpriteEditorLayout.Compute(1280, 720, mouseTwin.RegionCells);
        var flyout = new ToolbarFlyout();
        var mouseReader = new EditorMouseReader();
        int cell = VirtualConsole.SpriteSize * layout.SheetScale;
        int clickX = layout.Sheet.X + (3 * cell) + (cell / 2);
        int clickY = layout.Sheet.Y + (1 * cell) + (cell / 2);
        Click(mouseTwin, flyout, layout, mouseReader, clickX, clickY);

        Assert.Equal(
            (mouseTwin.RegionCellX, mouseTwin.RegionCellY),
            (editor.RegionCellX, editor.RegionCellY));
        Assert.Equal(mouseTwin.SpriteIndex, editor.SpriteIndex);
        Assert.NotEqual(0, editor.SpriteIndex);
    }
}

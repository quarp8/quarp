using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The editor's shell plumbing after the stage-2.5 verdict: every editor key is an
/// edge-detected press with the wave-2b chord discipline, the new keyboard-drawing keys
/// (arrows, Z/Space, digits, comma/period) report exactly what the parity law needs, and the
/// verdict layout keeps the canvas clear of the status bar and the reserved prompt line at
/// every region size — since the canvas is the rectangle that resizes.
/// </summary>
public class EditorKeysAndStatusLayoutTests
{
    private static readonly Keys[] EditorKeys = { Keys.B, Keys.Tab, Keys.F, Keys.V, Keys.R, Keys.Delete };

    private static bool[] EditorFlags(in ShellCommands commands) => new[]
    {
        commands.EditorToolToggle,
        commands.EditorRegionCycle,
        commands.EditorFlipH,
        commands.EditorFlipV,
        commands.EditorRotate,
        commands.EditorClear,
    };

    [Fact]
    public void EditorKeysFireOncePerPressNotPerHold()
    {
        var reader = new ShellCommandReader();

        Assert.All(EditorFlags(reader.Read(new KeyboardState(EditorKeys))), flag => Assert.True(flag));
        // The keys are still down the next frame: a held key must not repeat — R held for a
        // second is one quarter-turn, not sixty.
        Assert.All(EditorFlags(reader.Read(new KeyboardState(EditorKeys))), flag => Assert.False(flag));

        reader.Read(new KeyboardState());
        Assert.All(EditorFlags(reader.Read(new KeyboardState(EditorKeys))), flag => Assert.True(flag));
    }

    /// <summary>
    /// The chord rule from wave 2b ("a chord must not double as its bare key") extended to the
    /// new letters: Ctrl+F/V/R/B are nothing, not a flip — so a future Ctrl-chord landing on
    /// these letters cannot silently also transform the sheet.
    /// </summary>
    [Fact]
    public void CtrlChordsDoNotFireTheBareEditorLetters()
    {
        var reader = new ShellCommandReader();

        ShellCommands commands = reader.Read(
            new KeyboardState(Keys.LeftControl, Keys.B, Keys.F, Keys.V, Keys.R));

        Assert.False(commands.EditorToolToggle);
        Assert.False(commands.EditorFlipH);
        Assert.False(commands.EditorFlipV);
        Assert.False(commands.EditorRotate);
    }

    // ---- the keyboard pencil (Z/Space) ----

    [Theory]
    [InlineData(Keys.Z)]
    [InlineData(Keys.Space)]
    public void TheKeyboardPencilPressesHoldsAndReleases(Keys key)
    {
        var reader = new ShellCommandReader();

        ShellCommands press = reader.Read(new KeyboardState(key));
        ShellCommands hold = reader.Read(new KeyboardState(key));
        ShellCommands release = reader.Read(new KeyboardState());

        Assert.True(press.EditorPaintPressed);
        Assert.True(press.EditorPaintDown);
        Assert.False(hold.EditorPaintPressed);      // holding is a drag, not sixty strokes
        Assert.True(hold.EditorPaintDown);
        Assert.True(release.EditorPaintReleased);   // one release = one committed undo step
        Assert.False(release.EditorPaintDown);
    }

    /// <summary>
    /// Ctrl+Z is undo, never a pixel: with Ctrl held the pencil reports nothing — and pressing
    /// Ctrl while Z is still physically down counts as the pencil's release, so the open
    /// gesture closes before the undo lands instead of smearing across it.
    /// </summary>
    [Fact]
    public void CtrlZIsUndoNotAPixel()
    {
        var reader = new ShellCommandReader();

        ShellCommands chord = reader.Read(new KeyboardState(Keys.LeftControl, Keys.Z));
        Assert.False(chord.EditorPaintPressed);
        Assert.False(chord.EditorPaintDown);
        Assert.True(chord.EditorUndo);

        reader.Read(new KeyboardState());
        reader.Read(new KeyboardState(Keys.Z));                          // pencil down, painting
        ShellCommands ctrlArrives = reader.Read(new KeyboardState(Keys.LeftControl, Keys.Z));
        Assert.True(ctrlArrives.EditorPaintReleased);                    // the chord takes the key
        Assert.False(ctrlArrives.EditorPaintDown);
    }

    // ---- cursor arrows, tool digits, color cycle ----

    [Fact]
    public void ArrowKeysReportAllFourDirectionsAsPresses()
    {
        var reader = new ShellCommandReader();

        ShellCommands press = reader.Read(new KeyboardState(Keys.Up, Keys.Down, Keys.Left, Keys.Right));
        ShellCommands hold = reader.Read(new KeyboardState(Keys.Up, Keys.Down, Keys.Left, Keys.Right));

        Assert.True(press.MenuUp && press.MenuDown && press.MenuLeft && press.MenuRight);
        Assert.False(hold.MenuUp || hold.MenuDown || hold.MenuLeft || hold.MenuRight);
    }

    [Theory]
    [InlineData(Keys.D1, 1)]
    [InlineData(Keys.D2, 2)]
    [InlineData(Keys.D3, 3)]
    [InlineData(Keys.D4, 4)]
    [InlineData(Keys.D5, 5)]
    [InlineData(Keys.D6, 6)]   // the transform group slot, new in wave 2e
    public void ToolDigitsReportTheirToolbarPosition(Keys key, int digit)
    {
        var reader = new ShellCommandReader();

        Assert.Equal(digit, reader.Read(new KeyboardState(key)).EditorToolDigit);
        Assert.Equal(0, reader.Read(new KeyboardState(key)).EditorToolDigit);   // held ≠ pressed again
    }

    /// <summary>
    /// The map's grid key (wave 3d): the backtick, TIC-80's own, one edge per press and dead
    /// under Ctrl like every other bare editor key. Break recipe: drop the <c>!ctrl</c> guard
    /// in <see cref="ShellCommandReader"/> and the last assertion goes red; point it at another
    /// key and the first does.
    /// </summary>
    [Fact]
    public void TheBacktickReportsTheGridToggleOncePerPress()
    {
        var reader = new ShellCommandReader();

        Assert.True(reader.Read(new KeyboardState(Keys.OemTilde)).EditorGridToggle);
        Assert.False(reader.Read(new KeyboardState(Keys.OemTilde)).EditorGridToggle);   // held ≠ pressed again
        Assert.False(reader.Read(new KeyboardState()).EditorGridToggle);
        Assert.False(reader.Read(new KeyboardState(Keys.LeftControl, Keys.OemTilde)).EditorGridToggle);
    }

    /// <summary>
    /// The map's pan modifier (wave 3d): Space as a LEVEL, not an edge, because the drag it
    /// modifies lasts as long as the button is down. Space is still half of
    /// <see cref="ShellCommands.EditorPaintDown"/> — the reader reports both and the map router
    /// is where the modifier wins, which is exactly the split this pins.
    /// </summary>
    [Fact]
    public void SpaceReportsThePanModifierAsALevelWhileStillOpeningThePencil()
    {
        var reader = new ShellCommandReader();

        ShellCommands down = reader.Read(new KeyboardState(Keys.Space));
        ShellCommands held = reader.Read(new KeyboardState(Keys.Space));

        Assert.True(down.EditorPanModifier);
        Assert.True(down.EditorPaintPressed);       // both facts are reported; the router chooses
        Assert.True(held.EditorPanModifier);        // a level: still held, still true
        Assert.False(held.EditorPaintPressed);
        Assert.False(reader.Read(new KeyboardState()).EditorPanModifier);
        // Bare Z is not the modifier — that is what keeps the map's keyboard pencil alive.
        Assert.False(reader.Read(new KeyboardState(Keys.Z)).EditorPanModifier);
    }

    /// <summary>
    /// The map's tile-palette modifier (wave R3): Shift as a LEVEL, not an edge, because the
    /// overlay it raises is meant to be <em>peeked</em> — hold, look, pick, release
    /// (REFERENCES-EDITORS §3.1: TIC-80's button is labelled "SHOW TILES [shift]").
    ///
    /// <para>The overlap with Shift+arrows is the feature and not a clash, and this pins both
    /// halves of it: the very chord that steps the selected tile is the one that shows the
    /// tiles, so the author sees what he is stepping through. Break recipe: report
    /// <c>EditorTilesModifier</c> as an edge instead of a level — the "still held" assertion
    /// goes red, and in the window the palette would blink shut on the second frame.</para>
    /// </summary>
    [Fact]
    public void ShiftReportsTheTilePaletteModifierAsALevelWhileStillSteppingTheTile()
    {
        var reader = new ShellCommandReader();

        ShellCommands down = reader.Read(new KeyboardState(Keys.LeftShift, Keys.Right));
        ShellCommands held = reader.Read(new KeyboardState(Keys.LeftShift));

        Assert.True(down.EditorTilesModifier);
        Assert.Equal(1, down.EditorSheetDx);        // both facts are reported, from one chord
        Assert.True(held.EditorTilesModifier);      // a level: still held, still true
        Assert.False(reader.Read(new KeyboardState()).EditorTilesModifier);
        Assert.True(reader.Read(new KeyboardState(Keys.RightShift)).EditorTilesModifier);
        // Nothing else raises it — a bare arrow steers the map cursor and shows no palette.
        Assert.False(reader.Read(new KeyboardState(Keys.Right)).EditorTilesModifier);
    }

    // ---- the brush ladder's keys and the second ink's modifier ----

    /// <summary>
    /// TIC-80's brush keys are edges, not levels: <c>-</c> and <c>=</c> step the ladder once per
    /// press, or holding one would run the whole ladder past in four frames
    /// (REFERENCES-EDITORS §2.1 — <c>updateBrushSize</c> is called from a key <em>press</em>).
    ///
    /// <para>Break recipe: read them with <c>keyboard.IsKeyDown</c> instead of <c>Pressed</c> in
    /// <c>ShellCommandReader</c> and the "held" assertions go red — which is the difference
    /// between a brush that steps and a brush that spins.</para>
    /// </summary>
    [Fact]
    public void MinusAndEqualsStepTheBrushOncePerPress()
    {
        var reader = new ShellCommandReader();

        ShellCommands press = reader.Read(new KeyboardState(Keys.OemMinus, Keys.OemPlus));
        Assert.True(press.EditorBrushSmaller);
        Assert.True(press.EditorBrushBigger);

        ShellCommands held = reader.Read(new KeyboardState(Keys.OemMinus, Keys.OemPlus));
        Assert.False(held.EditorBrushSmaller);
        Assert.False(held.EditorBrushBigger);
    }

    /// <summary>
    /// Shift is the keyboard's second ink — a level, like Ctrl's filled flag, because what
    /// matters is which ink was in the hand at the press. LIKO-12 reads exactly this key for
    /// exactly this (<c>sprite.lua</c>: <c>isKDown("lshift","rshift") or isMDown(2)</c>,
    /// REFERENCES-EDITORS §2.2).
    ///
    /// <para>Break recipe: gate <c>EditorSecondaryInk</c> on anything but the shift level (say,
    /// <c>Pressed(Keys.LeftShift)</c>) and the "still held" assertion goes red — a modifier that
    /// only fires on its own edge would colour the first frame of a stroke and no other.</para>
    /// </summary>
    [Fact]
    public void ShiftReportsTheSecondInkAsALevel()
    {
        var reader = new ShellCommandReader();

        Assert.True(reader.Read(new KeyboardState(Keys.LeftShift)).EditorSecondaryInk);
        Assert.True(reader.Read(new KeyboardState(Keys.LeftShift)).EditorSecondaryInk);  // still held
        Assert.False(reader.Read(new KeyboardState()).EditorSecondaryInk);
        Assert.True(reader.Read(new KeyboardState(Keys.RightShift)).EditorSecondaryInk);
        // Nothing else raises it — a bare paint key is the first ink's, as it always was.
        Assert.False(reader.Read(new KeyboardState(Keys.Z)).EditorSecondaryInk);
    }

    // ---- the shape tool's filled modifier ----

    /// <summary>Ctrl is the shape's "filled" flag — a level, not an edge, so the preview flips the moment it changes.</summary>
    [Fact]
    public void CtrlReportsTheShapeFillModifierAsALevel()
    {
        var reader = new ShellCommandReader();

        Assert.True(reader.Read(new KeyboardState(Keys.LeftControl)).EditorShapeFill);
        Assert.True(reader.Read(new KeyboardState(Keys.LeftControl)).EditorShapeFill);  // still held, still true
        Assert.False(reader.Read(new KeyboardState()).EditorShapeFill);
        Assert.True(reader.Read(new KeyboardState(Keys.RightControl)).EditorShapeFill);
    }

    /// <summary>
    /// The keyboard's filled-shape gesture is Space+Ctrl: unlike Z (which the Ctrl chord
    /// releases), Space must survive Ctrl arriving mid-hold, or a filled shape could never be
    /// drawn from the keyboard at all — the parity law's escape hatch.
    /// </summary>
    [Fact]
    public void SpaceSurvivesCtrlArrivingMidHold()
    {
        var reader = new ShellCommandReader();

        reader.Read(new KeyboardState(Keys.Space));                                      // gesture opens
        ShellCommands chorded = reader.Read(new KeyboardState(Keys.Space, Keys.LeftControl));

        Assert.True(chorded.EditorPaintDown);           // the gesture lives on
        Assert.False(chorded.EditorPaintReleased);
        Assert.True(chorded.EditorShapeFill);           // and it is filled now
    }

    [Fact]
    public void CommaAndPeriodCycleTheColorOncePerPress()
    {
        var reader = new ShellCommandReader();

        ShellCommands press = reader.Read(new KeyboardState(Keys.OemComma, Keys.OemPeriod));
        ShellCommands hold = reader.Read(new KeyboardState(Keys.OemComma, Keys.OemPeriod));

        Assert.True(press.EditorColorPrev);
        Assert.True(press.EditorColorNext);
        Assert.False(hold.EditorColorPrev || hold.EditorColorNext);
    }

    // ---- the layout at every region size ----

    /// <summary>
    /// Every region size, every panel: whole-integer zoom, everything inside the window, and
    /// no two areas overlapping — checked pairwise over the named panels AND the narrow row's
    /// buttons, because wave 2k moved the size toggle and the layer tabs into a row whose
    /// left edge is measured from the canvas box.
    ///
    /// <para><b>Re-pinned in wave R2, and this is the paragraph that explains it.</b> The
    /// numbers here used to be 1280x720 — a window — and the sweep asked whether the panels fit
    /// inside it. The sprite screen no longer has a window: ADR-029 moved it onto the console,
    /// so the surface is 160x90 and there is exactly one of them. That makes the sweep
    /// STRONGER, not weaker: at host resolution "does it fit" depended on the window and had no
    /// single answer, and now it has one. Nothing about what is asserted changed — whole zooms,
    /// everything inside the surface, no two areas overlapping.</para>
    ///
    /// <para>What this sweep still does not prove: it runs on the one surface, so it cannot
    /// catch a canvas formula that only misbehaves at another size. On the console there is no
    /// other size to misbehave at, which is the whole point; the region-size sweep in
    /// <c>SpriteEditorLayoutTests.TabbingTheRegionSizeMovesNoPanel</c> covers the axis that is
    /// still free to vary — 8, 16 and 32 px sprites.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void EveryRegionSizeKeepsWholeScalesAndFitsTheConsole(int regionCells)
    {
        var layout = SpriteEditorLayout.Compute(160, 90, regionCells);
        var window = new Rectangle(0, 0, 160, 90);

        Assert.True(layout.CanvasScale >= 1);
        Assert.Equal(regionCells * VirtualConsole.SpriteSize, layout.RegionPixels);
        Assert.Equal(layout.RegionPixels * layout.CanvasScale, layout.Canvas.Width);   // whole-integer zoom
        var areas = new List<Rectangle>
        {
            layout.Canvas, layout.Swatches, layout.Sheet, layout.SheetSlider,
        };
        areas.AddRange(layout.Buttons.Select(place => place.Rect));
        for (int i = 0; i < areas.Count; i++)
        {
            Assert.True(window.Contains(areas[i]), $"{areas[i]} left the window at region {regionCells}");
            for (int j = i + 1; j < areas.Count; j++)
            {
                Assert.False(
                    areas[i].Intersects(areas[j]),
                    $"{areas[i]} overlaps {areas[j]} at region {regionCells}");
            }
        }
    }

    /// <summary>
    /// The message line is reserved at every region size (it carries the dirty-exit decision,
    /// save errors and the standing notice), so the canvas — the rectangle that resizes — may
    /// never grow into it or into the status band below it.
    ///
    /// <para>Re-pinned in wave R2 with the rest of this file: the line's height is the system
    /// font's own five pixels now, not the dead host atlas's <c>LineHeight(ui)</c>, because the screen
    /// prints with the console's font and the host font is not on it any more.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void TheCanvasStopsAboveTheMessageLineAndStatusBar(int regionCells)
    {
        var layout = SpriteEditorLayout.Compute(160, 90, regionCells);

        Assert.True(layout.Canvas.Bottom <= layout.PromptY);
        Assert.True(layout.PromptY + SystemFont.GlyphHeight <= layout.StatusBar.Y);
    }
}

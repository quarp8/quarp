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
    public void ToolDigitsReportTheirToolbarPosition(Keys key, int digit)
    {
        var reader = new ShellCommandReader();

        Assert.Equal(digit, reader.Read(new KeyboardState(key)).EditorToolDigit);
        Assert.Equal(0, reader.Read(new KeyboardState(key)).EditorToolDigit);   // held ≠ pressed again
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void EveryRegionSizeKeepsWholeScalesAndFitsTheDefaultWindow(int regionCells)
    {
        var layout = SpriteEditorLayout.Compute(1280, 720, regionCells);
        var window = new Rectangle(0, 0, 1280, 720);

        Assert.True(layout.CanvasScale >= 1);
        Assert.Equal(regionCells * VirtualConsole.SpriteSize, layout.RegionPixels);
        Assert.Equal(layout.RegionPixels * layout.CanvasScale, layout.Canvas.Width);   // whole-integer zoom
        Assert.True(window.Contains(layout.Canvas));
        Assert.False(layout.Canvas.Intersects(layout.Sheet));
        Assert.False(layout.Canvas.Intersects(layout.Swatches));
    }

    /// <summary>
    /// The prompt line is reserved at every region size (it carries the dirty-exit decision
    /// and save errors), so the canvas — the rectangle that resizes — may never grow into it
    /// or into the status bar below it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void TheCanvasStopsAboveThePromptLineAndStatusBar(int regionCells)
    {
        var layout = SpriteEditorLayout.Compute(1280, 720, regionCells);

        Assert.True(layout.Canvas.Bottom <= layout.PromptY);
        Assert.True(layout.PromptY + PixelFontAtlas.LineHeight(layout.Ui) <= layout.StatusBar.Y);
    }
}

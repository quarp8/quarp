using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Wave 2c's shell plumbing: the six new editor keys are edge-detected presses with the same
/// chord discipline the older keys carry, and the layout's new status row (active tool, region
/// size) sits between the canvas and the key hints without stealing either's pixels — at every
/// region size, since the canvas is the rectangle that resizes.
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
    /// The status row is always on (it carries the active tool), so unlike the occasional save
    /// error it may never overlap the canvas — the layout owns that by computing the canvas
    /// bottom above <see cref="SpriteEditorLayout.StatusY"/>.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void TheStatusRowSitsBetweenTheCanvasAndTheKeyHints(int regionCells)
    {
        var layout = SpriteEditorLayout.Compute(1280, 720, regionCells);

        Assert.True(layout.Canvas.Bottom <= layout.StatusY);
        Assert.True(layout.StatusY + PixelFontAtlas.LineHeight(layout.Ui) <= layout.FooterY);
    }
}

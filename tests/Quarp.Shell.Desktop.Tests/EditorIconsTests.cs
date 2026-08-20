using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The icon set and button metadata (M9 stage 2.5). Two of the wave's contracts are pinned
/// here: the digit→tool map keeps stub tools dead from the keyboard (the named negative
/// control — mapping a digit onto the stamp turns <see cref="StubDigitsSwitchNothing"/> red),
/// and every glyph is real ink, pairwise distinct, so a forgotten mask cannot ship as an
/// invisible or duplicated button.
/// </summary>
public class EditorIconsTests
{
    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));
    private static readonly EditorIcon[] AllIcons = (EditorIcon[])Enum.GetValues(typeof(EditorIcon));

    // ---- the keyboard's tool digits ----

    [Fact]
    public void LiveDigitsSelectTheirTools()
    {
        Assert.Equal(SpriteEditorTool.Pencil, EditorIcons.ToolForDigit(2));
        Assert.Equal(SpriteEditorTool.Fill, EditorIcons.ToolForDigit(3));
    }

    /// <summary>Inactive tools must be exactly as dead from the keyboard as from the mouse (the order's law).</summary>
    [Fact]
    public void StubDigitsSwitchNothing()
    {
        Assert.Null(EditorIcons.ToolForDigit(1));   // select — wave 2e
        Assert.Null(EditorIcons.ToolForDigit(4));   // stamp — wave 2e
        Assert.Null(EditorIcons.ToolForDigit(5));   // shapes — wave 2e
    }

    [Fact]
    public void DigitsOutsideTheToolbarSwitchNothing()
    {
        Assert.Null(EditorIcons.ToolForDigit(0));
        Assert.Null(EditorIcons.ToolForDigit(6));
        Assert.Null(EditorIcons.ToolForDigit(-1));
    }

    // ---- glyphs ----

    /// <summary>Every enum value must have a strip cell — a mask array that fell out of sync would misdraw every icon after it.</summary>
    [Fact]
    public void TheStripCoversEveryIcon()
    {
        Assert.Equal(AllIcons.Length, EditorIcons.IconCount);
    }

    [Fact]
    public void EveryIconHasVisibleInk()
    {
        foreach (EditorIcon icon in AllIcons)
        {
            int ink = 0;
            for (int row = 0; row < EditorIcons.IconPixels; row++)
            {
                for (int col = 0; col < EditorIcons.IconPixels; col++)
                {
                    if (EditorIcons.IsSet(icon, col, row))
                    {
                        ink++;
                    }
                }
            }
            // Fewer than six pixels cannot read as a picture at any scale — an all-zero
            // placeholder mask lands here, not on the owner's screen.
            Assert.True(ink >= 6, $"{icon} has only {ink} ink pixels");
        }
    }

    [Fact]
    public void IconsArePairwiseDistinct()
    {
        for (int a = 0; a < AllIcons.Length; a++)
        {
            for (int b = a + 1; b < AllIcons.Length; b++)
            {
                bool identical = true;
                for (int row = 0; row < EditorIcons.IconPixels && identical; row++)
                {
                    for (int col = 0; col < EditorIcons.IconPixels; col++)
                    {
                        if (EditorIcons.IsSet(AllIcons[a], col, row) != EditorIcons.IsSet(AllIcons[b], col, row))
                        {
                            identical = false;
                            break;
                        }
                    }
                }
                Assert.False(identical, $"{AllIcons[a]} and {AllIcons[b]} share one mask");
            }
        }
    }

    // ---- tooltips ----

    /// <summary>
    /// The parity law made discoverable: every live button's tooltip names its key path, so
    /// the mouse user learns the keyboard for free. Stubs answer "when", not a hotkey.
    /// </summary>
    [Fact]
    public void LiveTooltipsNameTheirHotkeys()
    {
        Assert.Contains("ESC", EditorIcons.Tooltip(EditorButton.ExitTab), StringComparison.Ordinal);
        Assert.Contains("2", EditorIcons.Tooltip(EditorButton.ToolPencil), StringComparison.Ordinal);
        Assert.Contains("3", EditorIcons.Tooltip(EditorButton.ToolFill), StringComparison.Ordinal);
        Assert.Contains("F", EditorIcons.Tooltip(EditorButton.FlipH), StringComparison.Ordinal);
        Assert.Contains("V", EditorIcons.Tooltip(EditorButton.FlipV), StringComparison.Ordinal);
        Assert.Contains("R", EditorIcons.Tooltip(EditorButton.Rotate), StringComparison.Ordinal);
        Assert.Contains("DEL", EditorIcons.Tooltip(EditorButton.Clear), StringComparison.Ordinal);
        Assert.Contains("CTRL+S", EditorIcons.Tooltip(EditorButton.Save), StringComparison.Ordinal);
        Assert.Contains("CTRL+Z", EditorIcons.Tooltip(EditorButton.Undo), StringComparison.Ordinal);
        Assert.Contains("CTRL+Y", EditorIcons.Tooltip(EditorButton.Redo), StringComparison.Ordinal);
        // The keyboard drawing vocabulary is surfaced on the pencil, per the order.
        Assert.Contains("Z/SPACE", EditorIcons.Tooltip(EditorButton.ToolPencil), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryButtonHasAnAsciiTooltip()
    {
        foreach (EditorButton button in AllButtons)
        {
            string tooltip = EditorIcons.Tooltip(button);
            Assert.False(string.IsNullOrWhiteSpace(tooltip));
            // The system font covers ASCII 32-126 only; anything else would draw fallback boxes.
            Assert.All(tooltip, c => Assert.InRange(c, ' ', '~'));
        }
    }

    /// <summary>The keyboard color mechanism is shown where the colors are — the swatch tooltip names , and .</summary>
    [Fact]
    public void SwatchTooltipsTeachTheColorKeys()
    {
        string tooltip = EditorIcons.SwatchTooltip(5);

        Assert.Contains("COLOR 5", tooltip, StringComparison.Ordinal);
        Assert.Contains(",", tooltip, StringComparison.Ordinal);
        Assert.Contains(".", tooltip, StringComparison.Ordinal);
    }
}

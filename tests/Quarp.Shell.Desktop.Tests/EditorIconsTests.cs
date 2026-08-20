using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The icon set and button metadata (M9 stage 2.5, wave 2e shape). Pinned here: the
/// digit→slot map (the stateful digit POLICY — repeat cycles a group's variant — lives with a
/// real session in <c>ToolbarGroupTests</c>), the group-slot metadata that drives the corner
/// markers and flyouts, and that every glyph is real ink, pairwise distinct, so a forgotten
/// mask cannot ship as an invisible or duplicated button.
/// </summary>
public class EditorIconsTests
{
    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));
    private static readonly EditorIcon[] AllIcons = (EditorIcon[])Enum.GetValues(typeof(EditorIcon));

    // ---- the keyboard's tool digits ----

    [Fact]
    public void EveryToolbarSlotHasItsDigitInColumnOrder()
    {
        Assert.Equal(EditorButton.ToolSelect, EditorIcons.ButtonForDigit(1));
        Assert.Equal(EditorButton.ToolPencil, EditorIcons.ButtonForDigit(2));
        Assert.Equal(EditorButton.ToolFill, EditorIcons.ButtonForDigit(3));
        Assert.Equal(EditorButton.ToolStamp, EditorIcons.ButtonForDigit(4));
        Assert.Equal(EditorButton.ToolShape, EditorIcons.ButtonForDigit(5));
        Assert.Equal(EditorButton.ToolTransform, EditorIcons.ButtonForDigit(6));
    }

    [Fact]
    public void DigitsOutsideTheToolbarMapToNothing()
    {
        Assert.Null(EditorIcons.ButtonForDigit(0));
        Assert.Null(EditorIcons.ButtonForDigit(7));
        Assert.Null(EditorIcons.ButtonForDigit(-1));
    }

    // ---- group slots ----

    /// <summary>Exactly the two verdict groups carry the marker-and-flyout mechanism; a stub can never be a group.</summary>
    [Fact]
    public void ExactlyShapeAndTransformAreGroupSlots()
    {
        foreach (EditorButton button in AllButtons)
        {
            bool group = button is EditorButton.ToolShape or EditorButton.ToolTransform;
            Assert.Equal(group, EditorIcons.IsGroupSlot(button));
            Assert.Equal(group ? button == EditorButton.ToolShape ? 2 : 3 : 0, EditorIcons.GroupVariantCount(button));
            if (group)
            {
                Assert.False(EditorIcons.IsStub(button));
            }
        }
    }

    /// <summary>
    /// The wave's card: the slot's face IS the current variant. VariantIcon is the one mapping
    /// the renderer uses for the slot and for the flyout, so pinning it pins both.
    /// </summary>
    [Fact]
    public void VariantIconsFollowTheSessionEnums()
    {
        Assert.Equal(EditorIcon.ShapeOval, EditorIcons.VariantIcon(EditorButton.ToolShape, (int)ShapeVariant.Oval));
        Assert.Equal(EditorIcon.ShapeRect, EditorIcons.VariantIcon(EditorButton.ToolShape, (int)ShapeVariant.Rectangle));
        Assert.Equal(EditorIcon.FlipH, EditorIcons.VariantIcon(EditorButton.ToolTransform, (int)TransformVariant.FlipH));
        Assert.Equal(EditorIcon.FlipV, EditorIcons.VariantIcon(EditorButton.ToolTransform, (int)TransformVariant.FlipV));
        Assert.Equal(EditorIcon.Rotate, EditorIcons.VariantIcon(EditorButton.ToolTransform, (int)TransformVariant.Rotate));
    }

    [Fact]
    public void VariantIconOutsideAGroupThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.VariantIcon(EditorButton.ToolPencil, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.VariantIcon(EditorButton.ToolShape, 2));
    }

    /// <summary>Every flyout variant has an ASCII tooltip naming its key path — the 3-second contract extends to variants.</summary>
    [Fact]
    public void VariantTooltipsExistAndNameTheKeys()
    {
        foreach (EditorButton slot in new[] { EditorButton.ToolShape, EditorButton.ToolTransform })
        {
            for (int i = 0; i < EditorIcons.GroupVariantCount(slot); i++)
            {
                string tooltip = EditorIcons.VariantTooltip(slot, i);
                Assert.False(string.IsNullOrWhiteSpace(tooltip));
                Assert.All(tooltip, c => Assert.InRange(c, ' ', '~'));
            }
        }
        Assert.Contains("F", EditorIcons.VariantTooltip(EditorButton.ToolTransform, (int)TransformVariant.FlipH), StringComparison.Ordinal);
        Assert.Contains("5", EditorIcons.VariantTooltip(EditorButton.ToolShape, (int)ShapeVariant.Oval), StringComparison.Ordinal);
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
        Assert.Contains("5", EditorIcons.Tooltip(EditorButton.ToolShape), StringComparison.Ordinal);
        Assert.Contains("CTRL", EditorIcons.Tooltip(EditorButton.ToolShape), StringComparison.Ordinal);
        Assert.Contains("F/V/R", EditorIcons.Tooltip(EditorButton.ToolTransform), StringComparison.Ordinal);
        Assert.Contains("6", EditorIcons.Tooltip(EditorButton.ToolTransform), StringComparison.Ordinal);
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

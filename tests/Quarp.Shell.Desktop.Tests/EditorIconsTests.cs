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

    /// <summary>
    /// Exactly the five variant groups carry the marker-and-flyout mechanism (select joined
    /// in wave 2f, the size toggle in 2h — the fourth review's "клик — список 8/16/32" rides
    /// the same machinery, and the brush toggle rides it in the brush wave); a stub can never
    /// be a group. Counts updated in wave 2g: the owner's third review added the wand as the
    /// select group's third variant, so the old "select has 2" pin was pinning behavior the
    /// owner cancelled — shape alone stays at two, and the brush list is as long as the
    /// session's ladder. The two SIZE slots are also the only ones whose short click opens the
    /// flyout (<see cref="EditorIcons.ClickOpensFlyout"/>): tool groups act on a click, a
    /// size list's only act IS choosing.
    ///
    /// <para>Break recipe: put <see cref="EditorButton.BrushToggle"/> back into the tool-group
    /// half of <c>ClickOpensFlyout</c> (i.e. return only <c>SizeToggle</c>) and the third
    /// assertion goes red for it — a brush click would then fall through to
    /// <see cref="EditorIcons.ClickGroupSlot"/>, which has no brush case, so the button would
    /// be visibly dead. Give the brush a literal 4 in <c>GroupVariantCount</c> instead of
    /// <see cref="SpriteEditorSession.BrushSizeCount"/> and nothing goes red today — which is
    /// precisely why the count is read from the session here too, and not written out.</para>
    /// </summary>
    [Fact]
    public void ExactlyTheFiveGroupSlotsCarryVariants()
    {
        foreach (EditorButton button in AllButtons)
        {
            bool group = button is EditorButton.ToolSelect or EditorButton.ToolShape
                or EditorButton.ToolTransform or EditorButton.SizeToggle
                or EditorButton.BrushToggle;
            Assert.Equal(group, EditorIcons.IsGroupSlot(button));
            int expected = button switch
            {
                EditorButton.ToolShape => 2,
                EditorButton.BrushToggle => SpriteEditorSession.BrushSizeCount,
                _ => group ? 3 : 0,
            };
            Assert.Equal(expected, EditorIcons.GroupVariantCount(button));
            Assert.Equal(
                button is EditorButton.SizeToggle or EditorButton.BrushToggle,
                EditorIcons.ClickOpensFlyout(button));
            if (group)
            {
                Assert.False(EditorIcons.IsStub(button));
            }
        }
    }

    /// <summary>
    /// The text-faced buttons and only them (wave 2h): the size toggle wears its current
    /// size — RegionCells 1/2/4 → "8"/"16"/"32", moving with Tab and the list alike — the
    /// brush toggle its current brush side, and the layer tabs their 1-based numbers; every
    /// other button answers null and keeps its glyph. A text-faced button must never reach
    /// IconFor — that is the throw pinned at the end, so the renderer's ButtonText-first branch
    /// cannot be silently bypassed.
    ///
    /// <para>Break recipe: drop the <see cref="EditorButton.BrushToggle"/> arm from
    /// <c>EditorIcons.ButtonText</c> and two assertions go red at once — the face stops being
    /// the brush size, and the "textFaced" sweep starts demanding a glyph for a button whose
    /// <c>IconFor</c> throws, which is the very crash this whole file exists for.</para>
    /// </summary>
    [Fact]
    public void TextFacesBelongToTheTwoSizeTogglesAndLayerTabsOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), "quarp-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var session = new SpriteEditorSession(root);
            Assert.Equal("8", EditorIcons.ButtonText(EditorButton.SizeToggle, session));
            session.SelectRegionSize(2);
            Assert.Equal("16", EditorIcons.ButtonText(EditorButton.SizeToggle, session));
            session.SelectRegionSize(4);
            Assert.Equal("32", EditorIcons.ButtonText(EditorButton.SizeToggle, session));
            Assert.Equal("1", EditorIcons.ButtonText(EditorButton.BrushToggle, session));
            session.SelectBrushSize(3);
            Assert.Equal("3", EditorIcons.ButtonText(EditorButton.BrushToggle, session));
            for (int i = 0; i < SpriteEditorSession.LayerCount; i++)
            {
                Assert.Equal((i + 1).ToString(), EditorIcons.ButtonText(EditorButton.LayerTab1 + i, session));
            }
            foreach (EditorButton button in AllButtons)
            {
                bool textFaced = button is EditorButton.SizeToggle or EditorButton.BrushToggle
                    || (button >= EditorButton.LayerTab1 && button <= EditorButton.LayerTab5);
                Assert.Equal(textFaced, EditorIcons.ButtonText(button, session) is not null);
                if (textFaced)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.IconFor(button));
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
        // The two size toggles are groups but their faces are text, not glyphs — the renderer
        // must branch on VariantText, never end up here.
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.VariantIcon(EditorButton.SizeToggle, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.VariantIcon(EditorButton.BrushToggle, 0));
    }

    /// <summary>
    /// <c>VariantText</c> is the one owner of "does this flyout variant wear text": exactly the
    /// two size lists answer with a label, every glyph slot answers null, and the two answers
    /// are complementary — a variant that answered both or neither would be a variant the
    /// flyout cannot draw. The labels themselves are the slots' own faces (8/16/32 and 1-4).
    ///
    /// <para>Break recipe: return a label for <see cref="EditorButton.ToolShape"/> too and its
    /// null assertion goes red; drop the <see cref="EditorButton.BrushToggle"/> arm and its
    /// label assertion goes red — and, in production, <c>DrawFlyout</c> would ask
    /// <c>VariantIcon</c> for a brush glyph that does not exist and take the window down on the
    /// first frame the brush list is open.</para>
    /// </summary>
    [Fact]
    public void OnlyTheTwoSizeListsWearTextInTheirFlyouts()
    {
        Assert.Equal("8", EditorIcons.VariantText(EditorButton.SizeToggle, 0));
        Assert.Equal("32", EditorIcons.VariantText(EditorButton.SizeToggle, 2));
        for (int i = 0; i < SpriteEditorSession.BrushSizeCount; i++)
        {
            Assert.Equal(
                SpriteEditorSession.BrushSizeAt(i).ToString(),
                EditorIcons.VariantText(EditorButton.BrushToggle, i));
        }
        foreach (EditorButton slot in new[]
        {
            EditorButton.ToolSelect, EditorButton.ToolShape, EditorButton.ToolTransform,
        })
        {
            for (int i = 0; i < EditorIcons.GroupVariantCount(slot); i++)
            {
                Assert.Null(EditorIcons.VariantText(slot, i));
                Assert.True(Enum.IsDefined(EditorIcons.VariantIcon(slot, i)));
            }
        }
    }

    /// <summary>Every flyout variant has an ASCII tooltip naming its key path — the 3-second contract extends to variants.</summary>
    [Fact]
    public void VariantTooltipsExistAndNameTheKeys()
    {
        foreach (EditorButton slot in new[]
        {
            EditorButton.ToolSelect, EditorButton.ToolShape, EditorButton.ToolTransform,
            EditorButton.SizeToggle, EditorButton.BrushToggle,
        })
        {
            for (int i = 0; i < EditorIcons.GroupVariantCount(slot); i++)
            {
                string tooltip = EditorIcons.VariantTooltip(slot, i);
                Assert.False(string.IsNullOrWhiteSpace(tooltip));
                Assert.All(tooltip, c => Assert.InRange(c, ' ', '~'));
            }
        }
        // The brush list names TIC-80's own two keys where the sizes are chosen.
        Assert.Contains("=", EditorIcons.VariantTooltip(EditorButton.BrushToggle, 0), StringComparison.Ordinal);
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
        // Wave 2h parity: the size toggle names Tab, the layer tabs name PgUp/PgDn, and the
        // buttonless slider's tooltip names its drag, wheel and bracket keys.
        Assert.Contains("TAB", EditorIcons.Tooltip(EditorButton.SizeToggle), StringComparison.Ordinal);
        Assert.Contains("PGUP", EditorIcons.Tooltip(EditorButton.LayerTab3), StringComparison.Ordinal);
        Assert.Contains("LAYER 3", EditorIcons.Tooltip(EditorButton.LayerTab3), StringComparison.Ordinal);
        Assert.Contains("[", EditorIcons.SliderTooltip, StringComparison.Ordinal);
        Assert.Contains("WHEEL", EditorIcons.SliderTooltip, StringComparison.Ordinal);
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
        // Two inks since the two-colour wave: which button loads which is the one thing a marked
        // palette cannot explain by itself, so it is said here.
        Assert.Contains("LCLICK", tooltip, StringComparison.Ordinal);
        Assert.Contains("RCLICK", tooltip, StringComparison.Ordinal);
    }
}

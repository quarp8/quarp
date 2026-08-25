using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Every button of every editor layout wears exactly one face — text or icon, never neither,
/// never a throw. This seam exists because of a crash the whole suite slept through: the
/// module-split wave left the sprite renderer computing an icon for every button, the first
/// text-faced one (the size toggle) threw <c>ArgumentOutOfRange</c> on the editor's very
/// first windowed frame, and 1264 headless tests stayed green — the choosing code lived only
/// behind a GraphicsDevice, so opening the editor from the library or from the menu's CREATE
/// GAME simply killed the window (found by the owner, 2026-08-24, both doors). The choice now
/// has one device-free owner, <see cref="EditorIcons.Face"/>, the renderer calls it, and this
/// file walks the real layouts' real buttons through it in every session state that changes a
/// face.
/// </summary>
public sealed class EditorButtonFaceTests : IDisposable
{
    private readonly string _root;
    private readonly SpriteEditorSession _session;

    public EditorButtonFaceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-faces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "manifest.json"),
            "{\"name\":\"faces\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(Path.Combine(_root, "src", "main.cs"), "// faces");
        _session = new SpriteEditorSession(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>One face and only one, on the layout's own button list — the crash's exact door.</summary>
    private static void AssertEveryButtonWearsOneFace(SpriteEditorSession session)
    {
        // 160x90: the sprite screen's surface since wave R2 is the console, not the window.
        var layout = SpriteEditorLayout.Compute(160, 90, session.RegionCells);
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            (string? text, EditorIcon? icon) = EditorIcons.Face(place.Id, session);
            Assert.True(
                text is null ^ icon is null,
                $"{place.Id}: text={text ?? "null"}, icon={(icon?.ToString()) ?? "null"} — a button wears exactly one face");
        }
    }

    [Fact]
    public void EverySpriteEditorButtonWearsExactlyOneFaceInEverySizeAndVariant()
    {
        // Every state that changes a face: the three region sizes (the size toggle's text and
        // its flyout variant), and every remembered variant of every group slot. The layout is
        // recomputed per size because the button list itself is the thing under test.
        for (int size = 0; size < EditorIcons.GroupVariantCount(EditorButton.SizeToggle); size++)
        {
            _session.SelectRegionSize(EditorIcons.SizeVariantCells(size));
            for (int selection = 0; selection < EditorIcons.GroupVariantCount(EditorButton.ToolSelect); selection++)
            {
                _session.SelectSelectionVariant((SelectionVariant)selection);
                AssertEveryButtonWearsOneFace(_session);
            }
            for (int shape = 0; shape < EditorIcons.GroupVariantCount(EditorButton.ToolShape); shape++)
            {
                _session.SelectShape((ShapeVariant)shape);
                AssertEveryButtonWearsOneFace(_session);
            }
            for (int transform = 0; transform < EditorIcons.GroupVariantCount(EditorButton.ToolTransform); transform++)
            {
                _session.SelectTransform((TransformVariant)transform);
                AssertEveryButtonWearsOneFace(_session);
            }
        }
    }

    /// <summary>
    /// The map screen draws every button through <see cref="EditorIcons.IconFor"/> with no
    /// text — so every button its layout places must have a glyph there. The day the map
    /// gains a text-faced button, this goes red before any window does.
    /// </summary>
    [Fact]
    public void EveryMapEditorButtonHasAnIcon()
    {
        var layout = MapEditorLayout.Compute(160, 90);
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            EditorIcon icon = EditorIcons.IconFor(place.Id);
            Assert.True(Enum.IsDefined(icon), $"{place.Id}: IconFor returned an undefined glyph");
        }
    }

    /// <summary>
    /// The flyouts draw variants directly (<see cref="EditorIcons.VariantIcon"/> for glyph
    /// slots, size labels for the size list) — walk every variant of every group slot the
    /// sprite layout places, the way <c>DrawFlyout</c> would with the flyout open.
    /// </summary>
    [Fact]
    public void EveryFlyoutVariantOfEveryGroupSlotResolves()
    {
        var layout = SpriteEditorLayout.Compute(160, 90, _session.RegionCells);
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            if (!EditorIcons.IsGroupSlot(place.Id))
            {
                continue;
            }
            for (int variant = 0; variant < EditorIcons.GroupVariantCount(place.Id); variant++)
            {
                if (place.Id == EditorButton.SizeToggle)
                {
                    Assert.False(string.IsNullOrEmpty(EditorIcons.SizeLabel(EditorIcons.SizeVariantCells(variant))));
                }
                else
                {
                    Assert.True(Enum.IsDefined(EditorIcons.VariantIcon(place.Id, variant)));
                }
                Assert.False(string.IsNullOrEmpty(EditorIcons.VariantTooltip(place.Id, variant)));
            }
        }
    }
}

using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The wordmark bitmap — facts about the data, from the mockup's side: the grid is the size
/// the mockup band measured, every char resolves to a visible slot or transparency, and the
/// five colors of the picture are exactly the mockup's five mapped into the palette
/// (wordmark white, tile sky-blue/green/yellow/pink). A drawing edit that dropped a tile or
/// leaked a secret-half slot would land here, not in the owner's eye three reviews later.
/// </summary>
public class MenuArtTests
{
    [Fact]
    public void TheGridIsTheMockupBand()
    {
        Assert.Equal(73, MenuArt.Width);
        Assert.Equal(12, MenuArt.Height);
    }

    [Fact]
    public void EveryPixelIsAVisibleSlotOrTransparent()
    {
        for (int y = 0; y < MenuArt.Height; y++)
        {
            for (int x = 0; x < MenuArt.Width; x++)
            {
                int slot = MenuArt.SlotAt(x, y);
                Assert.InRange(slot, -1, 15);   // visible palette only — the boot screen shows the face, not the secret half
            }
        }
    }

    [Fact]
    public void TheFiveMockupColorsAreAllPresentAndNothingElse()
    {
        var seen = new HashSet<int>();
        for (int y = 0; y < MenuArt.Height; y++)
        {
            for (int x = 0; x < MenuArt.Width; x++)
            {
                int slot = MenuArt.SlotAt(x, y);
                if (slot >= 0)
                {
                    seen.Add(slot);
                }
            }
        }
        Assert.Equal(new[] { 3, 5, 7, 8, 11 }, seen.Order().ToArray());
    }

    [Fact]
    public void OutOfRangeReadsAsTransparent()
    {
        Assert.Equal(-1, MenuArt.SlotAt(-1, 0));
        Assert.Equal(-1, MenuArt.SlotAt(0, -1));
        Assert.Equal(-1, MenuArt.SlotAt(MenuArt.Width, 0));
        Assert.Equal(-1, MenuArt.SlotAt(0, MenuArt.Height));
    }
}

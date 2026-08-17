using Xunit;

namespace Quarp.Core.Tests;

public class PaletteTests
{
    [Fact]
    public void MasterPaletteHas32UniqueColors()
    {
        Assert.Equal(Palette.MasterCount, Palette.Master32.Length);
        var seen = new HashSet<uint>();
        foreach (uint color in Palette.Master32)
        {
            Assert.True(seen.Add(color), $"duplicate color 0x{color:x6}");
            Assert.True(color <= 0xffffff, $"color 0x{color:x8} is not 24-bit RGB");
        }
    }
}

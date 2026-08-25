using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Icons on the console (wave R1): the same 8x8 masks the host atlas rasterizes into a
/// texture, plotted straight onto the shell's framebuffer instead. The point of the test is
/// that there is still only <b>one</b> owner of what an icon looks like — <see cref="EditorIcons"/>
/// — and that the console path reads it pixel for pixel rather than keeping a second copy in a
/// sprite sheet.
/// </summary>
public class ConsoleIconsTests
{
    /// <summary>
    /// Every icon in the table, plotted and read back. This is the assertion the host path
    /// never had: <see cref="EditorIconAtlas"/> writes into a <c>Texture2D</c> that no headless
    /// runner can read, so "the icon on screen is the icon in the table" was, until now, a
    /// claim rather than a check.
    /// </summary>
    [Fact]
    public void EveryIconLandsExactlyWhereItsMaskSaysItDoes()
    {
        for (int i = 0; i < EditorIcons.IconCount; i++)
        {
            var icon = (EditorIcon)i;
            var screen = new ShellScreen();
            screen.Begin();
            screen.Console.Cls(0);
            ConsoleIcons.Draw(screen.Console, icon, 10, 20, 7);

            for (int row = 0; row < ConsoleIcons.Size; row++)
            {
                for (int col = 0; col < ConsoleIcons.Size; col++)
                {
                    byte expected = EditorIcons.IsSet(icon, col, row) ? (byte)7 : (byte)0;
                    Assert.Equal(expected, screen.Console.Pget(10 + col, 20 + row));
                }
            }
        }
    }

    /// <summary>
    /// Clear mask bits leave whatever was underneath. An icon is a stencil, not a tile: the
    /// screen behind it — a toolbar's raised face, a selection bar — has to show through, which
    /// is exactly what the host path's tinted-white-on-transparent texture does.
    /// </summary>
    [Fact]
    public void ClearMaskBitsLeaveTheBackgroundAlone()
    {
        var screen = new ShellScreen();
        screen.Begin();
        screen.Console.Cls(0);
        screen.Console.RectFill(10, 20, ConsoleIcons.Size, ConsoleIcons.Size, 5);
        ConsoleIcons.Draw(screen.Console, EditorIcon.Pencil, 10, 20, 7);

        for (int row = 0; row < ConsoleIcons.Size; row++)
        {
            for (int col = 0; col < ConsoleIcons.Size; col++)
            {
                byte expected = EditorIcons.IsSet(EditorIcon.Pencil, col, row) ? (byte)7 : (byte)5;
                Assert.Equal(expected, screen.Console.Pget(10 + col, 20 + row));
            }
        }
    }

    /// <summary>
    /// An icon placed half off the screen is half drawn, not skipped and not wrapped — because
    /// every pixel goes through the console's own <c>Pset</c>, which clips. A drawing helper
    /// that invented its own bounds check would be a second clipping rule.
    /// </summary>
    [Fact]
    public void AnIconOffTheEdgeIsClippedByTheConsole()
    {
        var screen = new ShellScreen();
        screen.Begin();
        screen.Console.Cls(0);
        // Four columns hang off the right edge; the mask's own left half stays.
        ConsoleIcons.Draw(screen.Console, EditorIcon.Fill, screen.Width - 4, 40, 7);

        for (int row = 0; row < ConsoleIcons.Size; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                byte expected = EditorIcons.IsSet(EditorIcon.Fill, col, row) ? (byte)7 : (byte)0;
                Assert.Equal(expected, screen.Console.Pget(screen.Width - 4 + col, 40 + row));
            }
            // Nothing wrapped onto the next row's left edge.
            Assert.Equal((byte)0, screen.Console.Pget(0, 40 + row));
            Assert.Equal((byte)0, screen.Console.Pget(1, 40 + row));
        }
    }
}

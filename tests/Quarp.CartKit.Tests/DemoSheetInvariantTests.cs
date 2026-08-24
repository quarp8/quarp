using Xunit;
using Quarp.CartKit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The invariants demo art used to enforce in code, now that the art is a file (M9 wave A2,
/// gap found by the session audit of 2026-08-24).
///
/// <para>Before the migration digger's <c>Init</c> copied sprite 3 into sprite 8 cell by cell,
/// so "two tiles that must look identical have one drawing" was true by construction — a
/// half-redrawn rock could not exist. After the migration the two cells are independent bytes
/// in <c>gfx.png</c> and the rule survived only as a comment. The twelve pinned demo hashes do
/// not cover it: they compare the last frame of a walkthrough, and a sprite the track never
/// shows can drift without moving them.</para>
///
/// <para>These tests read the shipped art with the console's own decoder and state the rules
/// out loud. They are cheap, and each fails on exactly the edit it is meant to catch: change one
/// pixel of digger's rock in the editor and forget its twin, and the first test goes red.</para>
/// </summary>
public class DemoSheetInvariantTests
{
    private const int SheetWidth = CartData.GfxWidth;
    private const int SpriteSize = 8;
    private const int Columns = SheetWidth / SpriteSize;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "carts", "snake", "manifest.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/snake not found above the test directory");
    }

    private static byte[] Sheet(string cart)
    {
        string path = Path.Combine(RepoRoot(), "carts", cart, "gfx.png");
        Assert.True(File.Exists(path), $"{cart} has no gfx.png — wave A2 put one there on purpose");
        return PngDecoder.DecodeToPaletteIndices(
            File.ReadAllBytes(path), CartData.GfxWidth, CartData.GfxHeight, $"{cart}/gfx.png");
    }

    private static byte[] Cell(byte[] sheet, int sprite)
    {
        int originX = (sprite % Columns) * SpriteSize;
        int originY = (sprite / Columns) * SpriteSize;
        var cell = new byte[SpriteSize * SpriteSize];
        for (int y = 0; y < SpriteSize; y++)
        {
            for (int x = 0; x < SpriteSize; x++)
            {
                cell[(y * SpriteSize) + x] = sheet[((originY + y) * SheetWidth) + originX + x];
            }
        }
        return cell;
    }

    /// <summary>
    /// digger: sprite 8 is the rock over dug ground and sprite 3 is the rock over rubble; they
    /// are the same drawing on purpose. Negative control: flip one pixel of either cell in
    /// carts/digger/gfx.png and this goes red while all twelve demo hashes stay green — which
    /// is the whole reason this test exists.
    /// </summary>
    [Fact]
    public void DiggerSpriteEightIsPixelForPixelSpriteThree()
    {
        byte[] sheet = Sheet("digger");

        Assert.Equal(Cell(sheet, 3), Cell(sheet, 8));
    }

    /// <summary>
    /// digger: sprite 7 is the start marker the level scan erases in Init — it must stay empty
    /// in the file, or the marker would be drawn on the map before the scan removes it.
    /// Negative control: paint any pixel into cell 7 and this goes red.
    /// </summary>
    [Fact]
    public void DiggerStartMarkerSpriteIsEmptyInTheFile()
    {
        byte[] sheet = Sheet("digger");

        Assert.All(Cell(sheet, 7), pixel => Assert.Equal(0, pixel));
    }

    /// <summary>
    /// Every sprite a demo names as its own art carries ink. This is the cheap guard against
    /// the failure the twelve hashes cannot see: a sprite that a walkthrough never puts on
    /// screen quietly becoming blank. Negative control: clear any of these cells in the file.
    /// </summary>
    [Theory]
    [InlineData("digger", 5)]     // the closed exit
    [InlineData("digger", 6)]     // the open exit — mutually exclusive with 5 in any one frame
    [InlineData("shmup", 2)]      // the second enemy: only two-row waves use it
    [InlineData("platformer", 1)] // the wall (SprWall = 1; sprite 0 is the empty tile)
    [InlineData("dialogue", 0)]   // the first portrait's top-left cell
    public void ASpriteTheCartNamesIsNotBlank(string cart, int sprite)
    {
        byte[] cell = Cell(Sheet(cart), sprite);

        Assert.Contains(cell, pixel => pixel != 0);
    }
}

using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The library screen's geometry on the console's 160x90 grid (wave R1). These assertions were
/// impossible while the screen was host UI: "does everything fit" had no answer then, because
/// the answer depended on the window the player happened to have open. On a fixed screen it is
/// a fact, and this is where it is checked.
/// </summary>
public class LibraryLayoutTests
{
    private const int W = 160;
    private const int H = 90;

    private static LibraryLayout Layout(int count, int selected, bool message = false) =>
        LibraryLayout.Compute(W, H, count, selected, message);

    /// <summary>
    /// Everything the screen draws is inside the screen, in both message states and at both
    /// ends of a list long enough to scroll.
    /// </summary>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(9, 8, false)]
    [InlineData(40, 0, false)]
    [InlineData(40, 39, false)]
    [InlineData(40, 20, true)]
    [InlineData(3, 1, true)]
    public void EverythingFitsOnTheConsoleScreen(int count, int selected, bool message)
    {
        LibraryLayout layout = Layout(count, selected, message);

        Assert.InRange(layout.HeaderRule.Left, 0, W);
        Assert.InRange(layout.HeaderRule.Right, 0, W);
        Assert.InRange(layout.HeaderRule.Bottom, 0, H);
        Assert.InRange(layout.FooterRule.Right, 0, W);
        Assert.InRange(layout.FooterRule.Bottom, 0, H);

        // The footer text's last glyph row, and the message line's, are on screen.
        Assert.True(layout.FooterY + SystemFont.GlyphHeight <= H);
        Assert.True(layout.MessageY + SystemFont.GlyphHeight <= H);

        for (int slot = 0; slot < layout.DrawnRows; slot++)
        {
            var row = layout.Row(slot);
            Assert.InRange(row.Left, 0, W);
            Assert.InRange(row.Right, 0, W);
            Assert.InRange(row.Top, 0, H);
            Assert.InRange(row.Bottom, 0, H);
            // The name's last glyph column stays inside the bar.
            int textRight = LibraryLayout.RowTextX + layout.NameChars * SystemFont.CellWidth;
            Assert.True(textRight <= row.Right);
        }
    }

    /// <summary>
    /// Nothing overlaps anything: the header rule is above the first row, the last row is above
    /// whichever line comes next (the message when there is one, otherwise the footer rule),
    /// and consecutive rows share no pixel.
    /// </summary>
    [Theory]
    [InlineData(40, 20, false)]
    [InlineData(40, 20, true)]
    [InlineData(2, 0, true)]
    public void NoTwoPartsOfTheScreenOverlap(int count, int selected, bool message)
    {
        LibraryLayout layout = Layout(count, selected, message);

        Assert.True(layout.HeaderRule.Bottom <= LibraryLayout.ListTop);
        for (int slot = 1; slot < layout.DrawnRows; slot++)
        {
            Assert.Equal(layout.Row(slot - 1).Bottom, layout.Row(slot).Top);
        }
        int listBottom = layout.DrawnRows == 0
            ? LibraryLayout.ListTop
            : layout.Row(layout.DrawnRows - 1).Bottom;
        Assert.True(listBottom <= layout.ListBottom);

        int nextLineTop = message ? layout.MessageY : layout.FooterRuleY;
        Assert.True(listBottom <= nextLineTop);
        if (message)
        {
            Assert.True(layout.MessageY + SystemFont.GlyphHeight <= layout.FooterRuleY);
        }
        Assert.True(layout.FooterRuleY < layout.FooterY);
    }

    /// <summary>
    /// The console's real capacity, written down so the number cannot drift silently: nine rows,
    /// eight when a failed launch has a line to say. This is the figure the wave paid for the
    /// move onto the console — the 320x180-anchored host screen fitted about twenty-five rows in
    /// a 720p window — and it is the same order of magnitude PICO-8 and TIC-80 cart lists live
    /// in. If a future edit changes the header or the footer, this is the assertion that says
    /// what it cost.
    /// </summary>
    [Fact]
    public void TheConsoleScreenHoldsNineRowsAndEightWithAMessage()
    {
        Assert.Equal(9, Layout(40, 0).VisibleRows);
        Assert.Equal(8, Layout(40, 0, message: true).VisibleRows);
        Assert.Equal(38, Layout(1, 0).NameChars);
        Assert.Equal(39, Layout(1, 0).LineChars);
    }

    /// <summary>
    /// The list window follows the selection and clamps at both ends: a short list never
    /// scrolls, the middle of a long one centres, and neither end runs past its own edge.
    /// </summary>
    [Fact]
    public void TheWindowFollowsTheSelectionAndClampsAtBothEnds()
    {
        Assert.Equal(0, Layout(5, 4).FirstVisible);         // fits whole — no scrolling at all
        Assert.Equal(5, Layout(5, 4).DrawnRows);

        Assert.Equal(0, Layout(40, 0).FirstVisible);        // top of a long list
        Assert.Equal(0, Layout(40, 4).FirstVisible);        // still clamped at the top
        Assert.Equal(16, Layout(40, 20).FirstVisible);      // centred: 20 - 9/2
        Assert.Equal(31, Layout(40, 39).FirstVisible);      // clamped at the bottom: 40 - 9
        Assert.Equal(9, Layout(40, 39).DrawnRows);
    }

    /// <summary>
    /// The hit test answers the row the renderer drew, for every pixel of every row, and null
    /// everywhere else. Both facts matter: a click that selects nothing is a nuisance, a click
    /// that selects the wrong row of a nine-row list launches the wrong cartridge.
    /// </summary>
    [Fact]
    public void EveryPixelOfEveryRowHitsThatRowAndNothingElseHitsAny()
    {
        LibraryLayout layout = Layout(40, 20);
        var claimed = new int?[W, H];
        for (int slot = 0; slot < layout.DrawnRows; slot++)
        {
            var row = layout.Row(slot);
            for (int y = row.Top; y < row.Bottom; y++)
            {
                for (int x = row.Left; x < row.Right; x++)
                {
                    Assert.Equal<int?>(layout.FirstVisible + slot, layout.HitRow(x, y));
                    claimed[x, y] = slot;
                }
            }
        }
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (claimed[x, y] is null)
                {
                    Assert.Null(layout.HitRow(x, y));
                }
            }
        }
    }

    /// <summary>An empty library has no rows to hit, anywhere.</summary>
    [Fact]
    public void AnEmptyLibraryAnswersNoClick()
    {
        LibraryLayout layout = Layout(0, 0);
        Assert.Equal(0, layout.DrawnRows);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                Assert.Null(layout.HitRow(x, y));
            }
        }
    }

    /// <summary>
    /// The bottom half of the screen is measured up from the bottom edge, not written down as
    /// absolute rows: a console of another height must move the footer, not strand it in the
    /// middle. Checked on a hypothetical 160x120 screen that nothing ships — the point is only
    /// that no 90 is baked in, which is the same mistake at the layout scale that
    /// the dead host path's 320x180 text anchor was at the screen scale.
    /// </summary>
    [Fact]
    public void TheBottomOfTheScreenIsMeasuredFromTheBottom()
    {
        LibraryLayout tall = LibraryLayout.Compute(160, 120, 40, 0, false);
        Assert.Equal(114, tall.FooterY);
        Assert.Equal(111, tall.FooterRuleY);
        Assert.Equal(104, tall.MessageY);
        Assert.Equal(110, tall.ListBottom);
        Assert.Equal(14, tall.VisibleRows);         // (110 - 11) / 7
    }

    /// <summary>A name longer than a row is cut, not allowed to run off the screen.</summary>
    [Fact]
    public void LongNamesAreCutToTheRow()
    {
        LibraryLayout layout = Layout(1, 0);
        string tooLong = new('W', 80);
        Assert.Equal(layout.NameChars, layout.FitName(tooLong).Length);
        Assert.Equal("short", layout.FitName("short"));
        Assert.Equal(layout.LineChars, layout.FitLine(new string('.', 200)).Length);
    }
}

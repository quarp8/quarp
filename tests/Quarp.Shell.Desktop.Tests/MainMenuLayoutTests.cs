using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The boot screen's geometry on the console's own 160x90 grid (wave R6) — the arithmetic that
/// had no answer while this screen measured a window.
///
/// <para><b>Why these assertions did not exist before.</b> "Does the footer fit?" and "can the
/// message land on a door?" are questions about a screen of a known size. The host-resolution
/// boot screen had no known size: at 1280x720 everything fitted with room to spare, and the
/// same layout on a smaller window did not — so nothing here could be pinned and nothing was.
/// The console is 160x90 and always will be for profile 8, so every one of the facts below is
/// checkable, and the one the old layout actually got wrong (the message line landing on the
/// third door while the name field was up) is checked twice.</para>
/// </summary>
public class MainMenuLayoutTests
{
    private const int ScreenWidth = 160;
    private const int ScreenHeight = 90;

    private static MainMenuLayout Layout() => MainMenuLayout.Compute(ScreenWidth, ScreenHeight);

    /// <summary>
    /// Forty columns by fifteen rows of the 4x6 font, minus a margin each side — the whole line
    /// budget of this screen, derived from the screen rather than written down.
    /// </summary>
    [Fact]
    public void TheLineBudgetIsTheConsolesOwn()
    {
        MainMenuLayout layout = Layout();

        Assert.Equal(40, ScreenWidth / SystemFont.CellWidth);
        Assert.Equal(15, ScreenHeight / SystemFont.CellHeight);
        Assert.Equal(39, layout.LineChars);
        Assert.Equal("abc", layout.FitLine("abc"));
        Assert.Equal(39, layout.FitLine(new string('x', 80)).Length);
    }

    /// <summary>
    /// Every string this screen can print fits the line it is printed on. The two hint lines and
    /// the two spec rows are fixed text; the name field is the interesting one, because its
    /// content is the author's and its length cap is the scaffold's.
    ///
    /// <para>Break recipe: put the word DOWN back into the footer hint (the host screen said
    /// <c>UP/DOWN SELECT</c>) together with the 1-3 hint, and the first assertion goes red.</para>
    /// </summary>
    [Fact]
    public void EveryLineThisScreenPrintsFitsTheLine()
    {
        MainMenuLayout layout = Layout();

        Assert.True(MainMenuRenderer.FooterHint.Length <= layout.LineChars);
        Assert.True(MainMenuRenderer.EntryHint.Length <= layout.LineChars);
        Assert.True(
            MainMenuLayout.Margin + MainMenuRenderer.TaglineText.Length * SystemFont.CellWidth
                <= ScreenWidth);

        // The longest name the scaffold accepts, plus the cursor, still ends inside the screen.
        int nameRight = layout.NameTextX
            + (Quarp.CartKit.CartScaffold.MaxNameLength + 1) * SystemFont.CellWidth;
        Assert.True(nameRight <= ScreenWidth, $"name field runs to {nameRight}");

        // Both spec rows, laid out by the renderer's own rhythm: label, one cell, value, three
        // cells. Computed here the way the renderer computes it, so a widened value (a 16-bit
        // profile's "320x180", say) fails here before anyone sees it clipped.
        foreach ((string Label, string Value)[] row in MainMenuSession.SpecLines())
        {
            int x = MainMenuLayout.Margin;
            foreach ((string label, string value) in row)
            {
                x += (label.Length + 1) * SystemFont.CellWidth;
                x += (value.Length + MainMenuLayout.SpecGap) * SystemFont.CellWidth;
            }
            Assert.True(x <= ScreenWidth, $"spec row runs to {x}");
        }
    }

    /// <summary>
    /// The bands run top to bottom in the order the screen reads them, none of them overlaps its
    /// neighbour, and the last one ends inside the screen. This is the assertion the host layout
    /// would have failed: its message line sat seven pixels above the entry line whenever the
    /// name field was up, and seven pixels above that line is the third door.
    ///
    /// <para>Break recipe: give the message and the field one shared row again (set
    /// <c>MessageUp</c> equal to <c>EntryUp</c>) and the strict ordering goes red.</para>
    /// </summary>
    [Fact]
    public void TheBandsDoNotOverlapInAnyPhaseOfTheScreen()
    {
        MainMenuLayout layout = Layout();
        int glyph = SystemFont.GlyphHeight;

        int logoBottom = MainMenuLayout.LogoY + MenuArt.Height;
        Assert.True(logoBottom <= MainMenuLayout.TaglineY);
        Assert.True(MainMenuLayout.TaglineY + glyph <= MainMenuLayout.SpecY1);
        Assert.True(MainMenuLayout.SpecY1 + glyph <= MainMenuLayout.SpecY2);
        Assert.True(MainMenuLayout.SpecY2 + glyph <= MainMenuLayout.ItemTop);

        // The doors, then the message, then the field, then the rule, then the hints — all
        // strictly, and all inside the screen.
        Assert.True(layout.Row(MainMenuSession.ItemCount - 1).Bottom <= layout.MessageY);
        Assert.True(layout.MessageY + glyph <= layout.EntryY);
        Assert.True(layout.EntryY + glyph <= layout.FooterRuleY);
        Assert.True(layout.FooterRuleY < layout.FooterY);
        Assert.True(layout.FooterY + glyph <= ScreenHeight);

        // The exact rows this screen was designed on, so a silent re-cut is visible in a diff.
        Assert.Equal(67, layout.MessageY);
        Assert.Equal(74, layout.EntryY);
        Assert.Equal(81, layout.FooterRuleY);
        Assert.Equal(84, layout.FooterY);
        Assert.Equal(new Rectangle(2, 81, 156, 1), layout.FooterRule);
    }

    /// <summary>
    /// Three doors, each a seven-pixel bar with two clear pixels under it, all inside the screen
    /// and none touching another. The text sits one pixel down inside its bar, which centres
    /// five pixels of type in seven.
    /// </summary>
    [Fact]
    public void TheDoorsAreThreeSeparateBars()
    {
        MainMenuLayout layout = Layout();

        for (int i = 0; i < MainMenuSession.ItemCount; i++)
        {
            Rectangle row = layout.Row(i);
            Assert.Equal(1, row.X);
            Assert.Equal(ScreenWidth - 2, row.Width);
            Assert.Equal(MainMenuLayout.RowHeight, row.Height);
            Assert.True(row.Bottom <= ScreenHeight);
            Assert.Equal(row.Y + 1, MainMenuLayout.ItemTextY(i));
            if (i > 0)
            {
                Assert.Equal(MainMenuLayout.ItemPitch - MainMenuLayout.RowHeight,
                    row.Y - layout.Row(i - 1).Bottom);
            }
        }
        // The label column leaves room for the digit and its gap, and both are inside the bar.
        Assert.True(MainMenuLayout.ItemDigitX > layout.Row(0).X);
        Assert.True(MainMenuLayout.ItemDigitX + SystemFont.CellWidth <= MainMenuLayout.ItemLabelX);
    }

    /// <summary>
    /// The hit test answers the door the pointer is on and nothing else. The negative controls
    /// are the ones a two-step click makes expensive to get wrong: the gutter between two bars
    /// (door 2 opens an OS dialog, door 3 writes a folder), the one-pixel border either side,
    /// the specs above and the reserved lines below.
    ///
    /// <para>Break recipe: drop the <c>offset - index * ItemPitch &gt;= RowHeight</c> guard and
    /// the gutter starts answering the door above it.</para>
    /// </summary>
    [Fact]
    public void TheHitTestAnswersOnlyTheBarsThemselves()
    {
        MainMenuLayout layout = Layout();

        for (int i = 0; i < MainMenuSession.ItemCount; i++)
        {
            Rectangle row = layout.Row(i);
            Assert.Equal(i, layout.HitRow(row.X, row.Y));
            Assert.Equal(i, layout.HitRow(row.Right - 1, row.Bottom - 1));
            Assert.Equal(i, layout.HitRow(ScreenWidth / 2, row.Y + 3));
            // The gutter under every bar but the last belongs to no door.
            if (i < MainMenuSession.ItemCount - 1)
            {
                Assert.Null(layout.HitRow(ScreenWidth / 2, row.Bottom));
                Assert.Null(layout.HitRow(ScreenWidth / 2, row.Bottom + 1));
            }
        }
        Assert.Null(layout.HitRow(0, layout.Row(0).Y));                     // the left border
        Assert.Null(layout.HitRow(ScreenWidth - 1, layout.Row(0).Y));       // the right border
        Assert.Null(layout.HitRow(ScreenWidth / 2, MainMenuLayout.SpecY2)); // the spec rows
        Assert.Null(layout.HitRow(ScreenWidth / 2, layout.MessageY));       // the message line
        Assert.Null(layout.HitRow(ScreenWidth / 2, layout.FooterY));        // the hints
    }

    /// <summary>
    /// The intro's own geometry: sixteen columns of exactly ten pixels covering the screen with
    /// nothing left over, and a wordmark centred on the screen rather than left-aligned as it is
    /// on the menu.
    /// </summary>
    [Fact]
    public void TheIntroWallDividesTheScreenExactly()
    {
        MainMenuLayout layout = Layout();

        Assert.Equal(10, layout.WallColumnWidth);
        Assert.Equal(ScreenWidth, Palette.VisibleCount * layout.WallColumnWidth);
        Assert.Equal((ScreenWidth - MenuArt.Width) / 2, layout.IntroLogoX);
        Assert.True(layout.IntroLogoY + MenuArt.Height <= layout.IntroTaglineY);
        Assert.True(layout.IntroTaglineY + SystemFont.GlyphHeight <= ScreenHeight);
        Assert.True(layout.IntroLogoX + MenuArt.Width <= ScreenWidth);
    }
}

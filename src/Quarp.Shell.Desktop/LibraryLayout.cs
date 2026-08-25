using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The library screen's geometry, in <b>console pixels</b> — 160x90 on profile 8, 40 columns
/// by 15 rows of the 4x6 system font. Wave R1 moved this screen onto the console
/// (<see cref="ShellScreen"/>), and this type is the whole of what moved: every coordinate the
/// old host-resolution renderer derived from the window size and
/// <c>PixelFontMetrics.UiScale</c> is now a fixed number on the console's grid, the way
/// TIC-80 writes <c>PaletteY = 112</c> and means row 112 of a 136-row screen.
///
/// <para><b>What the move cost, honestly.</b> The old screen measured the window: at 1280x720
/// it picked scale x4, a 24 px row pitch and about 25 rows of list. The console has 90 pixels
/// total, a 7 px row pitch, and fits <b>nine</b> rows (eight when an error line is up). That is
/// not a regression to be worked around — it is the console's real size, and it is what PICO-8
/// and TIC-80's own cart lists live inside. Two things follow, and both are deliberate:
/// the list scrolls around the selection instead of showing everything, and the footer hint
/// had to be re-cut from 51 characters to 35, because 40 is the whole line.</para>
///
/// <para><b>Every number here is a screen fact, not a taste.</b> The header band, the two
/// rules, the row pitch and the footer are placed so that the list region is a whole number of
/// rows and nothing overlaps anything — which is a property a test can check, and does
/// (<c>LibraryLayoutTests</c>), now that the screen has a fixed size to be checked against.
/// At host resolution "does it fit" had no answer, because it depended on the window.</para>
/// </summary>
public readonly struct LibraryLayout
{
    /// <summary>Left and right inset for text and rules.</summary>
    public const int Margin = 2;

    /// <summary>Baseline of the title row: 5 px of glyph, one clear pixel above.</summary>
    public const int TitleY = 2;

    /// <summary>The rule under the header.</summary>
    public const int HeaderRuleY = 8;

    /// <summary>First row of the list.</summary>
    public const int ListTop = 11;

    /// <summary>Row pitch: one 6 px text cell plus a pixel, so a selection bar has a clear edge.</summary>
    public const int RowHeight = 7;

    /// <summary>Text inset inside a row — two pixels past the margin, so the bar has a lip.</summary>
    public const int RowTextX = Margin + 2;

    /// <summary>Glyph top inside a row: centres 5 px of type in a 7 px bar.</summary>
    public const int RowTextOffset = 1;

    // The bottom of the screen is measured up from the bottom, not written down as an absolute
    // row. Everything above is anchored to the top and is the same on any console; these five
    // are the ones that would quietly mean the wrong thing on a screen that is not 90 pixels
    // tall, which is precisely the mistake wave R1 exists to stop making. At 90 they come out
    // as 84, 81, 74, 80 and 72 — the numbers this screen was designed on.

    /// <summary>Key hints: five pixels of glyph plus one clear pixel at the bottom edge.</summary>
    private const int FooterTextUp = 6;

    /// <summary>The rule above the footer, two clear pixels above the hints.</summary>
    private const int FooterRuleUp = 9;

    /// <summary>The failed-launch line, a row's worth above the rule.</summary>
    private const int MessageUp = 16;

    /// <summary>Bottom of the list with no message: a clear pixel above the footer rule.</summary>
    private const int ListBottomUp = 10;

    /// <summary>Bottom of the list with a message: one whole row higher.</summary>
    private const int ListBottomWithMessageUp = 18;

    /// <summary>Screen width this layout was computed for.</summary>
    public int ScreenWidth { get; init; }

    /// <summary>Screen height this layout was computed for.</summary>
    public int ScreenHeight { get; init; }

    /// <summary>Bottom of the list region, exclusive — moves up when a message is shown.</summary>
    public int ListBottom { get; init; }

    /// <summary>How many rows fit; at least 1 even on a console too short to hold one.</summary>
    public int VisibleRows { get; init; }

    /// <summary>Index of the first entry drawn — the list window, centred on the selection.</summary>
    public int FirstVisible { get; init; }

    /// <summary>How many entries are actually drawn (fewer than <see cref="VisibleRows"/> near the end of a short list).</summary>
    public int DrawnRows { get; init; }

    /// <summary>True when the failed-launch line is on screen and the list is a row shorter.</summary>
    public bool HasMessage { get; init; }

    /// <summary>
    /// Longest name that fits a row without running under the right margin — 38 on a 160 px
    /// screen. Derived from <see cref="ScreenWidth"/> rather than written down, so a wider
    /// console (QUARP-16, one day) widens the names instead of keeping a 160-shaped constant.
    /// </summary>
    public int NameChars => (ScreenWidth - RowTextX - Margin) / SystemFont.CellWidth;

    /// <summary>Longest full-width line the screen can hold — 39 on a 160 px screen.</summary>
    public int LineChars => (ScreenWidth - 2 * Margin) / SystemFont.CellWidth;

    /// <summary>Glyph top of the failed-launch line — 74 on a 90 px screen.</summary>
    public int MessageY => ScreenHeight - MessageUp;

    /// <summary>Row of the rule above the footer — 81 on a 90 px screen.</summary>
    public int FooterRuleY => ScreenHeight - FooterRuleUp;

    /// <summary>Glyph top of the key hints — 84 on a 90 px screen.</summary>
    public int FooterY => ScreenHeight - FooterTextUp;

    /// <summary>The rule under the header, as a 1 px filled rectangle.</summary>
    public Rectangle HeaderRule => new(Margin, HeaderRuleY, ScreenWidth - 2 * Margin, 1);

    /// <summary>The rule above the footer.</summary>
    public Rectangle FooterRule => new(Margin, FooterRuleY, ScreenWidth - 2 * Margin, 1);

    /// <summary>
    /// The list window and the selection bar's geometry for a library of
    /// <paramref name="entryCount"/> entries with the bar on <paramref name="selectedIndex"/>.
    /// The window is centred on the selection and clamped at both ends, the same rule
    /// <see cref="CartLibrary.MoveSelection"/> uses for the selection itself: a list that
    /// scrolls past its end and a selection that wraps are two ways of losing the player's
    /// place.
    /// </summary>
    public static LibraryLayout Compute(
        int screenWidth, int screenHeight, int entryCount, int selectedIndex, bool hasMessage)
    {
        int bottom = screenHeight - (hasMessage ? ListBottomWithMessageUp : ListBottomUp);
        int visible = Math.Max(1, (bottom - ListTop) / RowHeight);
        int drawn = Math.Clamp(entryCount, 0, visible);
        int first = entryCount <= visible
            ? 0
            : Math.Clamp(selectedIndex - visible / 2, 0, entryCount - visible);
        return new LibraryLayout
        {
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            ListBottom = bottom,
            VisibleRows = visible,
            FirstVisible = first,
            DrawnRows = drawn,
            HasMessage = hasMessage,
        };
    }

    /// <summary>
    /// The bar rectangle of the <paramref name="slot"/>-th drawn row (0-based within the
    /// window, not an entry index). It spans the full width inside a one-pixel border so the
    /// selection reads as a bar and not only as a colour — colour alone disappears on a bad
    /// projector, and this screen is the first thing a player sees.
    /// </summary>
    public Rectangle Row(int slot) =>
        new(1, ListTop + slot * RowHeight, ScreenWidth - 2, RowHeight);

    /// <summary>
    /// Which entry index a console-space point falls on, or null for a point on no row.
    /// The one hit test on this screen; the mouse reaches it through
    /// <see cref="FramePlacement.TryToCanvas"/> and never through arithmetic of its own.
    /// </summary>
    public int? HitRow(int x, int y)
    {
        if (x < 1 || x >= ScreenWidth - 1 || y < ListTop)
        {
            return null;
        }
        int slot = (y - ListTop) / RowHeight;
        if (slot < 0 || slot >= DrawnRows)
        {
            return null;
        }
        return FirstVisible + slot;
    }

    /// <summary>
    /// Cuts a string to what a row can hold. Truncation, not ellipsis: an ellipsis costs a
    /// character of a name that is already down to <see cref="NameChars"/>, and folder names
    /// differ at their start far more often than at their end.
    /// </summary>
    public string FitName(string text) =>
        text.Length <= NameChars ? text : text[..NameChars];

    /// <summary>Cuts a full-width line (the footer, the error message) to <see cref="LineChars"/>.</summary>
    public string FitLine(string text) =>
        text.Length <= LineChars ? text : text[..LineChars];
}

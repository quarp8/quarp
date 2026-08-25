using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The boot screen's geometry, in <b>console pixels</b> — 160x90 on profile 8, 40 columns by
/// 15 rows of the 4x6 system font. Wave R6 moved the last two shell screens (the intro and the
/// main menu) onto the console itself, and this type is the whole of what moved: the renderer
/// translates nothing any more, because there is nothing left to translate into.
///
/// <para><b>What actually changed, honestly, because "the menu was already 160x90" is half
/// true.</b> The mockup was authored on a 160x90 canvas and this struct held its coordinates —
/// but it held them as a <em>window</em> placement (scale, origin, X(), Y()), so every pixel
/// went through a multiply on the way to a <c>SpriteBatch</c>, and the picture existed nowhere
/// a test could look at it. What moved is the destination, not the composition: the same
/// numbers now name pixels in the shell's framebuffer, the placement is
/// <see cref="ShellScreen.Placement"/>'s business like every other screen's, and the frame can
/// be hashed. The horizontal was already honest at 160 (see
/// <c>MainMenuScreenGoldenTests</c>'s line-budget assertions); the <b>vertical was not</b>, and
/// that is what this wave re-cut — see the bottom-band comment below.</para>
///
/// <para><b>Every number here is a screen fact.</b> The bands are placed so that nothing
/// overlaps anything on any phase of the screen, which is a property a test can check and does
/// (<c>MainMenuLayoutTests</c>) — at host resolution "does it fit" had no answer, because it
/// depended on the window.</para>
/// </summary>
public readonly struct MainMenuLayout
{
    /// <summary>Left inset for every block of text — the library's margin, so the two faces of the shell read as one shell.</summary>
    public const int Margin = 2;

    /// <summary>Top row of the wordmark band; <see cref="MenuArt"/> is 12 rows tall, so it ends at 15.</summary>
    public const int LogoY = 4;

    /// <summary>Glyph top of C# FANTASY CONSOLE.</summary>
    public const int TaglineY = 18;

    /// <summary>Glyph top of the VIDEO / COL / FPS row.</summary>
    public const int SpecY1 = 26;

    /// <summary>Glyph top of the CART / CODE / SAVE row.</summary>
    public const int SpecY2 = 33;

    /// <summary>Top of the first door's selection bar.</summary>
    public const int ItemTop = 41;

    /// <summary>Height of a door's selection bar — the library's row height, and for the same reason: a bar needs a clear edge.</summary>
    public const int RowHeight = 7;

    /// <summary>Row-to-row advance: a 7 px bar and two clear pixels between neighbours — the mockup's own pitch, kept.</summary>
    public const int ItemPitch = 9;

    /// <summary>The 1-2-3 column, one cell in from the bar's left lip.</summary>
    public const int ItemDigitX = Margin + SystemFont.CellWidth;

    /// <summary>The door-name column, three cells in.</summary>
    public const int ItemLabelX = Margin + 3 * SystemFont.CellWidth;

    /// <summary>Blank cells between a spec value and the next spec label — the mockup's rhythm.</summary>
    public const int SpecGap = 3;

    /// <summary>Cells reserved for the <c>NAME:</c> caption and the space after it.</summary>
    public const int NameCaptionCells = 6;

    // The bottom band is measured up from the bottom edge, not written down as absolute rows —
    // the same discipline LibraryLayout adopted in wave R1 and for the same reason: these four
    // are the numbers that would quietly mean the wrong thing on a console that is not 90 rows
    // tall. At 90 they come out as 67, 74, 81 and 84.
    //
    // THIS BAND IS THE RE-CUT. The host-era layout put the message on the entry line and, while
    // the name field was up, moved it seven pixels higher — straight into the third door's text
    // (the door rows ended at 71 and the shifted message began at 69). At 320x180-worth of
    // window that shift was invisible; on the console's real 90 rows it was an overlap. The
    // message and the field now have a row each, always, and neither can reach the doors.

    /// <summary>The message line (a refused name, a failed load): the row above the field.</summary>
    private const int MessageUp = 23;

    /// <summary>The name field's line.</summary>
    private const int EntryUp = 16;

    /// <summary>The rule above the key hints.</summary>
    private const int FooterRuleUp = 9;

    /// <summary>Key hints: five pixels of glyph plus one clear pixel at the bottom edge.</summary>
    private const int FooterTextUp = 6;

    /// <summary>Screen width this layout was computed for.</summary>
    public int ScreenWidth { get; init; }

    /// <summary>Screen height this layout was computed for.</summary>
    public int ScreenHeight { get; init; }

    /// <summary>Left edge of the wordmark on the menu — the same margin the text uses.</summary>
    public int LogoX => Margin;

    /// <summary>Glyph top of the message line — 67 on a 90-row console.</summary>
    public int MessageY => ScreenHeight - MessageUp;

    /// <summary>Glyph top of the name field — 74 on a 90-row console.</summary>
    public int EntryY => ScreenHeight - EntryUp;

    /// <summary>Row of the rule above the hints — 81 on a 90-row console.</summary>
    public int FooterRuleY => ScreenHeight - FooterRuleUp;

    /// <summary>Glyph top of the key hints — 84 on a 90-row console.</summary>
    public int FooterY => ScreenHeight - FooterTextUp;

    /// <summary>Longest full-width line the screen can hold — 39 characters on a 160 px console.</summary>
    public int LineChars => (ScreenWidth - 2 * Margin) / SystemFont.CellWidth;

    /// <summary>Where the typed name starts, past the <c>NAME:</c> caption.</summary>
    public int NameTextX => Margin + NameCaptionCells * SystemFont.CellWidth;

    /// <summary>The rule above the key hints, as a 1 px filled rectangle.</summary>
    public Rectangle FooterRule => new(Margin, FooterRuleY, ScreenWidth - 2 * Margin, 1);

    /// <summary>
    /// The intro's palette wall: one column per visible slot, the console's width divided
    /// exactly — 16 columns of 10 px on a 160 px screen. Derived rather than written down so a
    /// wider console widens the stripes instead of leaving a gap at the right edge.
    /// </summary>
    public int WallColumnWidth => ScreenWidth / Palette.VisibleCount;

    /// <summary>Left edge of the wordmark during the intro, where it is centred rather than left-aligned.</summary>
    public int IntroLogoX => (ScreenWidth - MenuArt.Width) / 2;

    /// <summary>Top of the wordmark during the intro: centred, then lifted four rows to leave room for the tagline.</summary>
    public int IntroLogoY => (ScreenHeight - MenuArt.Height) / 2 - 4;

    /// <summary>Glyph top of the intro's tagline, under the centred wordmark.</summary>
    public int IntroTaglineY => ScreenHeight / 2 + 8;

    /// <summary>The whole of the layout: the screen it is drawn on, and nothing else.</summary>
    public static MainMenuLayout Compute(int screenWidth, int screenHeight) =>
        new() { ScreenWidth = screenWidth, ScreenHeight = screenHeight };

    /// <summary>Glyph top of the <paramref name="index"/>-th door's text — centred in its 7 px bar.</summary>
    public static int ItemTextY(int index) => ItemTop + index * ItemPitch + 1;

    /// <summary>
    /// The selection bar of the <paramref name="index"/>-th door. Full width inside a one-pixel
    /// border, exactly like <see cref="LibraryLayout.Row"/>: the selection has to read as a bar
    /// and not only as a colour, because colour alone disappears on a bad projector and this is
    /// the first screen a player ever sees.
    /// </summary>
    public Rectangle Row(int index) =>
        new(1, ItemTop + index * ItemPitch, ScreenWidth - 2, RowHeight);

    /// <summary>
    /// Which door a console-space point falls on, or null. The one hit test on this screen; the
    /// mouse reaches it through <see cref="FramePlacement.TryToCanvas"/> and never through
    /// arithmetic of its own. The one clear pixel between two bars belongs to neither — a click
    /// that lands in the gutter selects nothing rather than the nearest door.
    /// </summary>
    public int? HitRow(int x, int y)
    {
        if (x < 1 || x >= ScreenWidth - 1 || y < ItemTop)
        {
            return null;
        }
        int offset = y - ItemTop;
        int index = offset / ItemPitch;
        if (index >= MainMenuSession.ItemCount || offset - index * ItemPitch >= RowHeight)
        {
            return null;
        }
        return index;
    }

    /// <summary>Cuts a full-width line (a message, the key hints) to <see cref="LineChars"/>.</summary>
    public string FitLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= LineChars ? text : text[..LineChars];
    }
}

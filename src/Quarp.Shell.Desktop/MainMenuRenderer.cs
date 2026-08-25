using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the boot screen — the intro animation and the main menu — onto the <b>console
/// itself</b>. Wave R6, the last of ADR-029: this file used to paint at the window's native
/// resolution through <c>PixelFontAtlas</c>, a <c>Texture2D</c> of <see cref="MenuArt"/> and a
/// <c>GraphicsDevice</c>; it now calls <c>Cls</c>, <c>RectFill</c>, <c>Print</c> and
/// <c>Pset</c> on a <see cref="ShellScreen"/>, the same calls a cartridge makes, and the
/// result is presented by the same <see cref="ConsolePresenter"/> the cartridge frame goes
/// through. With it went the last reader of the host font path, so
/// <c>EditorChrome</c>, <c>EditorChromeRenderer</c>, <c>EditorIconAtlas</c>,
/// <c>PixelFontAtlas</c> and <c>PixelFontMetrics</c> left the tree in the same commit.
///
/// <para><b>The wordmark needed no conversion.</b> <see cref="MenuArt"/> was already one char
/// per pixel naming a palette slot — the same shape <see cref="EditorIcons"/>' masks have, and
/// <see cref="ConsoleIcons"/> already plots those with <c>Pset</c>. What died was the
/// <em>upload</em>: the old renderer unpacked the grid into a texture once at construction
/// because a <c>SpriteBatch</c> cannot read a string. 876 plots per frame is drawing, not
/// simulation; it happens once per rendered frame and never in a tick.</para>
///
/// <para><b>The gain is not cosmetic.</b> A screen drawn into a framebuffer can be hashed by
/// <see cref="FrameHash"/> exactly as a cartridge's frame is, and that is what
/// <c>MainMenuScreenGoldenTests</c> does. Until this wave the console's very first screen —
/// the one every player sees before anything else — was the only surface in the shell that no
/// test in this solution could look at.</para>
///
/// <para><b>Not the cartridge's console.</b> The framebuffer written here belongs to the shell
/// (<see cref="ShellScreen"/>); the golden master the CI compares between architectures is
/// <see cref="CartSession"/>'s, and no call in this file can reach it.</para>
/// </summary>
public static class MainMenuRenderer
{
    // The mockup's cast, in the console's own visible slots (Palette.cs numbering). Colour
    // slots now, not unpacked RGB: with no Pal remap in force (ShellScreen.Begin resets it)
    // slot n is master n, which is what the host path spelled out through PaletteColors.
    private const byte Ink = 0;
    private const byte Dim = 1;
    private const byte Text = 2;
    private const byte Bright = 3;
    private const byte Bar = 5;         // the mockup's cyan selection bar
    private const byte Tagline = 7;     // its green subtitle
    private const byte Label = 8;       // its yellow spec labels
    private const byte Error = 10;

    /// <summary>The subtitle, on the intro's last beat and on the menu alike — one owner for the words.</summary>
    public const string TaglineText = "C# FANTASY CONSOLE";

    /// <summary>The menu's key hints, cut to 40 columns. Was 36 characters of host UI, in the host's <c>UP/DOWN</c> spelling.</summary>
    public const string FooterHint = "^V SELECT  1-3 GO  Z OPEN  ESC QUIT";

    /// <summary>The name field's key hints — the field owns the keyboard while it is up, so it owns the line.</summary>
    public const string EntryHint = "ENTER CREATE  ESC CANCEL";

    /// <summary>The name field's caption.</summary>
    public const string NameCaption = "NAME:";

    // --- the intro's clock, in seconds. Three beats that deliberately overlap: the first cut
    // had a black half-second between the wall leaving and the wordmark arriving, and dead air
    // in a 1.7 s intro is a third of the show.

    /// <summary>Beat 1 runs until here: the palette wall grows, then slides off the bottom.</summary>
    private const double WallEnd = 0.70;

    /// <summary>When each column of the wall has finished growing, measured from its own stagger.</summary>
    private const double WallGrow = 0.18;

    /// <summary>Per-slot stagger, so the sixteen columns race rather than march.</summary>
    private const double WallStagger = 0.02;

    /// <summary>When the wall starts sliding away.</summary>
    private const double WallSlideStart = 0.45;

    /// <summary>How long the slide takes.</summary>
    private const double WallSlide = 0.25;

    /// <summary>Beat 2: the wordmark starts wiping in here, over the wall's departing tail.</summary>
    private const double LogoStart = 0.50;

    /// <summary>How long the wipe takes.</summary>
    private const double LogoWipe = 0.65;

    /// <summary>Beat 3: the tagline lands.</summary>
    private const double TaglineStart = 1.20;

    /// <summary>
    /// One frame of the boot screen. Owns the whole surface: it resets the console's drawing
    /// state and clears, so nothing another screen left behind can bend these pixels. The intro
    /// is a pure function of the session's clock — this file animates nothing of its own, so a
    /// skipped intro and an expired one land on the same menu pixels.
    /// </summary>
    /// <returns>The layout used, so a test and the input router can ask exactly what was drawn.</returns>
    public static MainMenuLayout Draw(ShellScreen screen, MainMenuSession session)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(session);
        MainMenuLayout layout = LayoutFor(screen);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);
        if (session.Phase == MenuPhase.Intro)
        {
            DrawIntro(console, layout, session.IntroClock);
        }
        else
        {
            DrawMenu(console, layout, session);
        }
        return layout;
    }

    /// <summary>
    /// The layout the screen is drawn with. Public because the input router needs the same rows
    /// the renderer drew in order to answer a click, and computing them twice from two call
    /// sites is how a hit test drifts one row away from the picture (the lesson
    /// <see cref="LibraryRenderer.LayoutFor"/> already carries).
    /// </summary>
    public static MainMenuLayout LayoutFor(ShellScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return MainMenuLayout.Compute(screen.Width, screen.Height);
    }

    /// <summary>
    /// The boot animation, a pure function of the clock. Three beats, all in the console's own
    /// colours: the sixteen visible slots sweep down over the screen and slide away (the palette
    /// is the identity — it goes first, the way PICO-8's stripes do); the wordmark wipes in
    /// column by column while the jingle climbs; the tagline lands. Under two seconds, and any
    /// key cuts to the menu.
    ///
    /// <para>The arithmetic is the host version's, operator for operator, because the intro was
    /// the one part of this screen already composed on the console's own 160x90 grid — the wall
    /// is sixteen columns of <see cref="MainMenuLayout.WallColumnWidth"/>, which is 160 divided
    /// by the sixteen visible slots and always was. Only the destination changed.</para>
    /// </summary>
    private static void DrawIntro(VirtualConsole console, in MainMenuLayout layout, double t)
    {
        // Beat 1: sixteen columns, each visible slot in order, racing down with a small stagger,
        // then the whole wall slides off the bottom.
        if (t < WallEnd)
        {
            for (int slot = 0; slot < Palette.VisibleCount; slot++)
            {
                double grow = Math.Clamp((t - slot * WallStagger) / WallGrow, 0, 1);
                int height = (int)(grow * layout.ScreenHeight);
                int top = 0;
                if (t > WallSlideStart)
                {
                    int slide = (int)((t - WallSlideStart) / WallSlide * layout.ScreenHeight);
                    top = slide;
                    height = layout.ScreenHeight - slide;
                }
                if (height > 0)
                {
                    console.RectFill(
                        slot * layout.WallColumnWidth, top, layout.WallColumnWidth, height, (byte)slot);
                }
            }
        }

        // Beat 2: the wordmark wipes in, left to right, centred, OVER the wall's departing tail —
        // the four tiles sit at its right edge, so they arrive on the climb's last notes.
        if (t >= LogoStart)
        {
            int columns = (int)Math.Clamp((t - LogoStart) / LogoWipe * MenuArt.Width, 0, MenuArt.Width);
            DrawWordmark(console, layout.IntroLogoX, layout.IntroLogoY, columns);
        }

        // Beat 3: the subtitle, centred under the wordmark.
        if (t >= TaglineStart)
        {
            int textX = (layout.ScreenWidth - TaglineText.Length * SystemFont.CellWidth) / 2;
            console.Print(TaglineText, textX, layout.IntroTaglineY, Tagline);
        }
    }

    /// <summary>The menu proper — the mockup, row for row, plus the footer and the two field states.</summary>
    private static void DrawMenu(VirtualConsole console, in MainMenuLayout layout, MainMenuSession session)
    {
        DrawWordmark(console, layout.LogoX, MainMenuLayout.LogoY, MenuArt.Width);
        console.Print(TaglineText, MainMenuLayout.Margin, MainMenuLayout.TaglineY, Tagline);

        (string Label, string Value)[][] specs = MainMenuSession.SpecLines();
        DrawSpecLine(console, specs[0], MainMenuLayout.SpecY1);
        DrawSpecLine(console, specs[1], MainMenuLayout.SpecY2);

        for (int i = 0; i < MainMenuSession.ItemCount; i++)
        {
            bool selected = i == session.SelectedIndex && session.Phase == MenuPhase.Menu;
            if (selected)
            {
                Rectangle row = layout.Row(i);
                console.RectFill(row.X, row.Y, row.Width, row.Height, Bar);
            }
            byte rowColor = selected ? Ink : Text;
            int textY = MainMenuLayout.ItemTextY(i);
            console.Print($"{i + 1}", MainMenuLayout.ItemDigitX, textY, rowColor);
            console.Print(MainMenuSession.ItemLabel((MenuItem)i), MainMenuLayout.ItemLabelX, textY, rowColor);
        }

        // The message and the field each own a row of their own. On the host screen they shared
        // one and the message jumped seven pixels up whenever the field appeared — which on 90
        // real rows landed on the third door's text. See MainMenuLayout's bottom-band comment.
        if (session.Message is string message)
        {
            console.Print(layout.FitLine(message), MainMenuLayout.Margin, layout.MessageY, Error);
        }

        if (session.Phase == MenuPhase.NameEntry)
        {
            // The underscore is the cursor, always there — a solid cursor needs no clock, and
            // nothing else on this screen animates either.
            console.Print(NameCaption, MainMenuLayout.Margin, layout.EntryY, Label);
            console.Print(session.NameText + "_", layout.NameTextX, layout.EntryY, Bright);
        }

        Rectangle rule = layout.FooterRule;
        console.RectFill(rule.X, rule.Y, rule.Width, rule.Height, Dim);
        console.Print(
            session.Phase == MenuPhase.NameEntry ? EntryHint : FooterHint,
            MainMenuLayout.Margin,
            layout.FooterY,
            Dim);
    }

    /// <summary>
    /// The wordmark's leftmost <paramref name="columns"/> pixel columns, plotted straight from
    /// <see cref="MenuArt"/>'s grid — the discipline <see cref="ConsoleIcons"/> already lives
    /// under. A transparent cell is not plotted, which leaves whatever the screen drew
    /// underneath; that is what lets the wipe happen over the wall's tail.
    /// </summary>
    private static void DrawWordmark(VirtualConsole console, int x, int y, int columns)
    {
        for (int row = 0; row < MenuArt.Height; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int slot = MenuArt.SlotAt(col, row);
                if (slot >= 0)
                {
                    console.Pset(x + col, y + row, (byte)slot);
                }
            }
        }
    }

    /// <summary>
    /// One spec row: yellow label, light value, three blank cells between pairs — the mockup's
    /// rhythm. The cursor comes back from <c>Print</c> rather than from a measurement of our
    /// own: the font owns its own advance, and asking it is one owner fewer (the same move
    /// <see cref="LibraryRenderer"/> makes for its two-word header).
    /// </summary>
    private static void DrawSpecLine(VirtualConsole console, (string Label, string Value)[] pairs, int y)
    {
        int x = MainMenuLayout.Margin;
        foreach ((string label, string value) in pairs)
        {
            x = console.Print(label, x, y, Label) + SystemFont.CellWidth;
            x = console.Print(value, x, y, Text) + MainMenuLayout.SpecGap * SystemFont.CellWidth;
        }
    }
}

using System.Globalization;
using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the library screen — the console's face when no cartridge is running — onto the
/// <b>console itself</b>. Wave R1: this file used to paint at the window's native resolution
/// through <c>PixelFontAtlas</c> and a <c>GraphicsDevice</c>; it now calls <c>Cls</c>,
/// <c>RectFill</c> and <c>Print</c> on a <see cref="ShellScreen"/>, the same calls a cartridge
/// makes, and the result is presented by the same <see cref="ConsolePresenter"/> the cartridge
/// frame goes through.
///
/// <para><b>What that changed and why.</b> The owner's law of 2026-08-25: the console's
/// resolution and colours are the same for every game <em>and for every tool of the console
/// itself</em>. The old host path was a second machine — 320x180-anchored text density over a
/// 160x90 console — and this screen was its main inhabitant. Three things follow from the
/// move, all visible to a player: the list holds nine rows instead of the two dozen a 720p
/// window used to give it (<see cref="LibraryLayout"/> explains the arithmetic), the footer
/// hint is re-cut to fit 40 columns, and a long name is truncated at 38 characters instead of
/// running off a wide window. That is what the console's real screen affords, and it is the
/// size PICO-8's and TIC-80's own cart lists live inside.</para>
///
/// <para><b>The gain is not cosmetic.</b> A screen drawn into a framebuffer can be hashed by
/// <see cref="FrameHash"/>, exactly as a cartridge's frame is, and that is what
/// <c>LibraryScreenGoldenTests</c> does. Layout regressions on this screen were previously
/// undetectable by any test in the suite — there was no buffer to look at, only a
/// <c>SpriteBatch</c> nobody could run headless.</para>
///
/// <para><b>Not the cartridge's console.</b> The framebuffer written here belongs to the
/// shell (<see cref="ShellScreen"/>); the golden master the CI compares between architectures
/// is <see cref="CartSession"/>'s, and no call in this file can reach it.</para>
/// </summary>
public static class LibraryRenderer
{
    // The library's fixed cast of palette roles — the same six the host-resolution version
    // used, now as console colour slots rather than unpacked RGB. Indices are Master32's
    // documented visible slots (Palette.cs): 0 ink, 1 gray, 2 light gray, 3 white, 4 blue,
    // 10 red. With no Pal remap in force (ShellScreen.Begin resets it) slot n is master n.
    private const byte Ink = 0;
    private const byte Dim = 1;
    private const byte Text = 2;
    private const byte Bright = 3;
    private const byte SelectionBar = 4;
    private const byte Error = 10;

    /// <summary>The whole of the footer, cut to 40 columns. Was 51 characters at host resolution.</summary>
    private const string FooterHint = "^V SELECT  Z PLAY  X EDIT  ESC MENU";

    /// <summary>
    /// The layout the screen is drawn with. Public because the input router needs the same
    /// rows the renderer drew in order to answer a click, and computing them twice from two
    /// call sites is how a hit test drifts one row away from the picture.
    /// </summary>
    public static LibraryLayout LayoutFor(ShellScreen screen, CartLibrary library, string? message)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(library);
        return LibraryLayout.Compute(
            screen.Width, screen.Height, library.Entries.Count, library.SelectedIndex, message is not null);
    }

    /// <summary>
    /// One frame of the library. Owns the whole surface: it resets the console's drawing state
    /// and clears, so nothing another screen left behind can bend these pixels.
    /// </summary>
    /// <param name="message">The last failed launch's message, or null (ShellModeMachine.LibraryMessage).</param>
    /// <returns>The layout used, so a test can assert against exactly what was drawn.</returns>
    public static LibraryLayout Draw(ShellScreen screen, CartLibrary library, string? message)
    {
        LibraryLayout layout = LayoutFor(screen, library, message);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);

        // The header. The second word is placed at the cursor Print returns rather than at a
        // measured offset: the font owns its own advance, and asking it is one owner fewer.
        int after = console.Print("QUARP", LibraryLayout.Margin, LibraryLayout.TitleY, Bright);
        console.Print("GAME LIBRARY", after + SystemFont.CellWidth, LibraryLayout.TitleY, Dim);

        // Position counter, right-aligned in the header band. It exists because the list window
        // is nine rows: with more carts than that, a player scrolling needs to know there is a
        // beyond, and the bar alone does not say so.
        if (library.Entries.Count > 0)
        {
            string counter = (library.SelectedIndex + 1).ToString(CultureInfo.InvariantCulture)
                + "/" + library.Entries.Count.ToString(CultureInfo.InvariantCulture);
            console.Print(
                counter,
                screen.Width - LibraryLayout.Margin - counter.Length * SystemFont.CellWidth,
                LibraryLayout.TitleY,
                Dim);
        }

        Fill(console, layout.HeaderRule, Dim);

        if (library.Entries.Count == 0)
        {
            DrawEmptyState(console);
        }
        else
        {
            DrawEntries(console, library, layout);
        }

        if (message is not null)
        {
            console.Print(layout.FitLine(message), LibraryLayout.Margin, layout.MessageY, Error);
        }

        Fill(console, layout.FooterRule, Dim);
        console.Print(FooterHint, LibraryLayout.Margin, layout.FooterY, Dim);
        return layout;
    }

    /// <summary>
    /// The work order's empty-state promise, verbatim in spirit: tell the player where carts
    /// come from instead of showing them a test pattern. Re-wrapped for 40 columns — the one
    /// line that used to say it now takes two.
    /// </summary>
    private static void DrawEmptyState(VirtualConsole console)
    {
        console.Print("NO CARTRIDGES FOUND", LibraryLayout.Margin, LibraryLayout.ListTop, Bright);
        console.Print("PUT A CART FOLDER OR A .QUARP8", LibraryLayout.Margin, 21, Text);
        console.Print("FILE IN CARTS/", LibraryLayout.Margin, 28, Text);
        console.Print("OR CREATE ONE: QUARP NEW MYGAME", LibraryLayout.Margin, 38, Text);
    }

    /// <summary>
    /// The cart rows, windowed around the selection so a library longer than nine entries
    /// scrolls instead of clipping. The selected row gets a bar, not just a colour.
    /// </summary>
    private static void DrawEntries(VirtualConsole console, CartLibrary library, LibraryLayout layout)
    {
        for (int slot = 0; slot < layout.DrawnRows; slot++)
        {
            int index = layout.FirstVisible + slot;
            var row = layout.Row(slot);
            bool selected = index == library.SelectedIndex;
            if (selected)
            {
                Fill(console, row, SelectionBar);
            }
            console.Print(
                layout.FitName(library.Entries[index].Name),
                LibraryLayout.RowTextX,
                row.Y + LibraryLayout.RowTextOffset,
                selected ? Bright : Text);
        }
    }

    /// <summary>One filled rectangle, the layout's geometry unpacked into the console's call.</summary>
    private static void Fill(VirtualConsole console, Rectangle rect, byte color) =>
        console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, color);
}

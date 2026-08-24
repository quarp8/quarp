using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Pixels of the boot screen — the intro animation and the main menu (M9 stage 4, the
/// owner's mockup drawn for real). Host UI in the same sense the library is (ADR-009: the
/// host draws the console's face, no cartridge is running, nothing here can touch a frame
/// hash), but composed on the fixed 160x90 canvas <see cref="MainMenuLayout"/> owns rather
/// than laid out from the window, because the mockup is a picture, not a list.
///
/// <para>Everything is palette colors and the system font; the wordmark is
/// <see cref="MenuArt"/> uploaded once as a texture. The intro is a pure function of the
/// session's clock — this file animates nothing of its own, so a skipped intro and an
/// expired one land on the same menu pixels.</para>
/// </summary>
public sealed class MainMenuRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly PixelFontAtlas _font;
    private readonly Texture2D _white;
    private readonly Texture2D _logo;

    // The mockup's cast, in the console's own visible slots (Palette.cs numbering).
    private static readonly Color Ink = PaletteColors.Opaque(0);
    private static readonly Color Dim = PaletteColors.Opaque(1);
    private static readonly Color Text = PaletteColors.Opaque(2);
    private static readonly Color Bright = PaletteColors.Opaque(3);
    private static readonly Color Bar = PaletteColors.Opaque(5);        // the mockup's cyan selection bar
    private static readonly Color Tagline = PaletteColors.Opaque(7);    // its green subtitle
    private static readonly Color Label = PaletteColors.Opaque(8);      // its yellow spec labels
    private static readonly Color Error = PaletteColors.Opaque(10);

    public MainMenuRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _font = new PixelFontAtlas(device);
        _white = new Texture2D(device, 1, 1);
        _white.SetData(new[] { Color.White });
        _logo = BuildLogo(device);
    }

    /// <summary>MenuArt as one texture: indexed chars to palette pixels, uploaded once.</summary>
    private static Texture2D BuildLogo(GraphicsDevice device)
    {
        var texture = new Texture2D(device, MenuArt.Width, MenuArt.Height);
        var pixels = new Color[MenuArt.Width * MenuArt.Height];
        for (int y = 0; y < MenuArt.Height; y++)
        {
            for (int x = 0; x < MenuArt.Width; x++)
            {
                int slot = MenuArt.SlotAt(x, y);
                pixels[y * MenuArt.Width + x] = slot < 0 ? Color.Transparent : PaletteColors.Opaque(slot);
            }
        }
        texture.SetData(pixels);
        return texture;
    }

    /// <summary>
    /// One frame of the boot screen. Owns the whole surface (clears, begins and ends the
    /// batch), like the library. The bezel outside the canvas clears to the same ink as the
    /// canvas, so a window that is not an exact multiple shows no frame line.
    /// </summary>
    public void Draw(SpriteBatch batch, int width, int height, MainMenuSession session)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(session);
        _device.Clear(Ink);
        var layout = MainMenuLayout.Compute(width, height);
        batch.Begin(samplerState: SamplerState.PointClamp);
        if (session.Phase == MenuPhase.Intro)
        {
            DrawIntro(batch, layout, session.IntroClock);
        }
        else
        {
            DrawMenu(batch, layout, session);
        }
        batch.End();
    }

    public void Dispose()
    {
        _font.Dispose();
        _white.Dispose();
        _logo.Dispose();
    }

    /// <summary>A filled rectangle in canvas pixels.</summary>
    private void Cell(SpriteBatch batch, in MainMenuLayout layout, int x, int y, int w, int h, Color color) =>
        batch.Draw(_white, new Rectangle(layout.X(x), layout.Y(y), w * layout.Scale, h * layout.Scale), color);

    /// <summary>Text at canvas coordinates, in the canvas's scale.</summary>
    private void Print(SpriteBatch batch, in MainMenuLayout layout, string text, int x, int y, Color color) =>
        _font.Draw(batch, text, layout.X(x), layout.Y(y), layout.Scale, color);

    /// <summary>
    /// The boot animation, a pure function of the clock. Three beats, all in the console's
    /// own colors: the sixteen visible slots sweep down over the screen and slide away (the
    /// palette is the identity — it goes first, the way PICO-8's stripes do); the wordmark
    /// wipes in column by column while the jingle climbs; the tagline lands. Under two
    /// seconds, and any key cuts to the menu.
    /// </summary>
    private void DrawIntro(SpriteBatch batch, in MainMenuLayout layout, double t)
    {
        // Beat 1 (0 .. 0.70): sixteen 10 px columns, each visible slot in order, racing down
        // with a small stagger, then the whole wall slides off the bottom. The beats overlap
        // on purpose — the first storyboard had a black half-second between the wall leaving
        // and the wordmark arriving, and dead air in a 1.7 s intro is a third of the show.
        if (t < 0.70)
        {
            for (int slot = 0; slot < 16; slot++)
            {
                double grow = Math.Clamp((t - slot * 0.02) / 0.18, 0, 1);
                int height = (int)(grow * MainMenuLayout.CanvasHeight);
                int top = 0;
                if (t > 0.45)
                {
                    int slide = (int)((t - 0.45) / 0.25 * MainMenuLayout.CanvasHeight);
                    top = slide;
                    height = MainMenuLayout.CanvasHeight - slide;
                }
                if (height > 0)
                {
                    Cell(batch, layout, slot * 10, top, 10, height, PaletteColors.Opaque(slot));
                }
            }
        }

        // Beat 2 (0.50 .. 1.15): the wordmark wipes in, left to right, centered, OVER the
        // wall's departing tail — the four tiles sit at its right edge, so they arrive on
        // the climb's last notes.
        if (t >= 0.50)
        {
            int logoX = (MainMenuLayout.CanvasWidth - MenuArt.Width) / 2;
            int logoY = (MainMenuLayout.CanvasHeight - MenuArt.Height) / 2 - 4;
            int columns = (int)Math.Clamp((t - 0.50) / 0.65 * MenuArt.Width, 0, MenuArt.Width);
            if (columns > 0)
            {
                batch.Draw(
                    _logo,
                    new Rectangle(layout.X(logoX), layout.Y(logoY), columns * layout.Scale, MenuArt.Height * layout.Scale),
                    new Rectangle(0, 0, columns, MenuArt.Height),
                    Color.White);
            }
        }

        // Beat 3 (1.20 ..): the subtitle, centered under the wordmark.
        if (t >= 1.20)
        {
            const string tagline = "C# FANTASY CONSOLE";
            int textX = (MainMenuLayout.CanvasWidth - PixelFontMetrics.MeasureWidth(tagline, 1)) / 2;
            Print(batch, layout, tagline, textX, MainMenuLayout.CanvasHeight / 2 + 8, Tagline);
        }
    }

    /// <summary>The menu proper — the mockup, row for row, plus the footer and the two field states.</summary>
    private void DrawMenu(SpriteBatch batch, in MainMenuLayout layout, MainMenuSession session)
    {
        batch.Draw(
            _logo,
            new Rectangle(layout.X(MainMenuLayout.Margin), layout.Y(MainMenuLayout.LogoY), MenuArt.Width * layout.Scale, MenuArt.Height * layout.Scale),
            Color.White);
        Print(batch, layout, "C# FANTASY CONSOLE", MainMenuLayout.Margin, MainMenuLayout.TaglineY, Tagline);

        (string Label, string Value)[][] specs = MainMenuSession.SpecLines();
        DrawSpecLine(batch, layout, specs[0], MainMenuLayout.SpecY1);
        DrawSpecLine(batch, layout, specs[1], MainMenuLayout.SpecY2);

        for (int i = 0; i < MainMenuSession.ItemCount; i++)
        {
            int textY = MainMenuLayout.ItemTextY + i * MainMenuLayout.ItemPitch;
            bool selected = i == session.SelectedIndex && session.Phase == MenuPhase.Menu;
            if (selected)
            {
                Cell(
                    batch, layout,
                    MainMenuLayout.Margin, textY - 1,
                    MainMenuLayout.BarRight - MainMenuLayout.Margin, 7,
                    Bar);
            }
            Color rowColor = selected ? Ink : Text;
            Print(batch, layout, $"{i + 1}", MainMenuLayout.ItemDigitX, textY, rowColor);
            Print(batch, layout, MainMenuSession.ItemLabel((MenuItem)i), MainMenuLayout.ItemLabelX, textY, rowColor);
        }

        if (session.Phase == MenuPhase.NameEntry)
        {
            // The field replaces the message line; the underscore is the cursor, always there —
            // a solid cursor needs no clock, and nothing else on this screen animates either.
            Print(batch, layout, "NAME:", MainMenuLayout.Margin, MainMenuLayout.EntryY, Label);
            Print(batch, layout, session.NameText + "_", MainMenuLayout.Margin + 24, MainMenuLayout.EntryY, Bright);
            Print(batch, layout, "ENTER CREATE   ESC CANCEL", MainMenuLayout.Margin, MainMenuLayout.FooterY, Dim);
        }
        else
        {
            // Two-space gaps, not three: at 4 px a character, three gaps put the last letter
            // past pixel 160 — the footer must live inside the canvas it explains.
            Print(batch, layout, "UP/DOWN SELECT  Z/ENTER OK  ESC QUIT", MainMenuLayout.Margin, MainMenuLayout.FooterY, Dim);
        }
        if (session.Message is string message)
        {
            int y = session.Phase == MenuPhase.NameEntry ? MainMenuLayout.EntryY - 7 : MainMenuLayout.EntryY;
            Print(batch, layout, message, MainMenuLayout.Margin, y, Error);
        }
    }

    /// <summary>One spec row: yellow label, light value, three spaces between pairs — the mockup's rhythm.</summary>
    private void DrawSpecLine(SpriteBatch batch, in MainMenuLayout layout, (string Label, string Value)[] pairs, int y)
    {
        int x = MainMenuLayout.Margin;
        foreach ((string label, string value) in pairs)
        {
            Print(batch, layout, label, x, y, Label);
            x += PixelFontMetrics.MeasureWidth(label + " ", 1);
            Print(batch, layout, value, x, y, Text);
            x += PixelFontMetrics.MeasureWidth(value, 1) + PixelFontMetrics.MeasureWidth("   ", 1);
        }
    }
}

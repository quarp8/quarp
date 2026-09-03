using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The shell's own text layer — <c>PAUSE</c>, <c>&lt;&lt; 0.25x</c>, <c>REC</c>, rebuild
/// progress — drawn <b>on top of</b> the presented frame and never into it.
///
/// <para><b>Why this exists as a separate surface at all.</b> The console's framebuffer is
/// the project's golden master: <c>quarp sim</c> hashes it, the CI compares those hashes
/// between windows-x64 and linux-arm64, and that comparison is the M2 milestone criterion.
/// A status indicator stamped into the framebuffer with <c>Print</c> would land inside the
/// hash, so the moment a player paused, their frame would stop matching everyone else's.
/// The indicator therefore lives in its own RGBA texture of exactly the console's pixel
/// size, blended over the console texture at the same integer scale. The framebuffer stays
/// exactly as the cartridge drew it (M2 work order, "Интеграция (C)"; API-8 §8).</para>
///
/// <para><b>Why it still looks native.</b> It borrows <see cref="SystemFont"/> — read-only,
/// pure glyph data with pure accessors — and the console's own palette entries, so the
/// overlay is the same 3x5 type at the same 4x6 pitch as everything the cartridge draws.
/// Nothing here can write to console state: this file holds no reference to a
/// <see cref="VirtualConsole"/> at all.</para>
///
/// <para>Rasterization happens only when the text changes, so a paused session uploads one
/// texture and then costs a single blended quad per frame.</para>
/// </summary>
public sealed class ShellOverlay : IDisposable
{
    /// <summary>Distance from the screen edge to the indicator's box, in console pixels.</summary>
    private const int Margin = 2;

    /// <summary>Padding inside the box around the text.</summary>
    private const int Padding = 2;

    /// <summary>Height of the progress bar under the text, 0 when there is no progress to show.</summary>
    private const int BarHeight = 2;

    private readonly int _width;
    private readonly int _height;
    private readonly Color[] _pixels;
    private readonly Texture2D _texture;

    private readonly Color _ink;
    private readonly Color _text;
    private readonly Color _accent;

    private string? _shownText;
    private int _shownPercent = -1;
    private bool _empty = true;

    // The pause menu's block, its box and where its first glyph goes — all three computed by
    // PauseMenu and stored verbatim, because the hit test the pointer uses is that same
    // arithmetic. Nothing here measures the text: a painter that measured would be a second
    // owner of where the rows are, and the row under the cursor would eventually stop being the
    // row under the pointer.
    private string? _shownMenu;
    private Rectangle _menuBox;
    private Point _menuOrigin;

    public ShellOverlay(GraphicsDevice device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        _width = width;
        _height = height;
        _pixels = new Color[width * height];
        _texture = new Texture2D(device, width, height);

        // Palette colours, so the layer reads as part of the console rather than as a
        // browser notification. Alpha is the shell's own business — the console's indexed
        // framebuffer has no such concept, which is precisely why this is a separate layer.
        _ink = WithAlpha(Palette.Master32[0], 210);      // near-black box
        _text = WithAlpha(Palette.Master32[3], 255);     // off-white type
        _accent = WithAlpha(Palette.Master32[8], 255);   // yellow progress bar
    }

    /// <summary>True when there is nothing to blend this frame.</summary>
    public bool IsEmpty => _empty;

    /// <summary>
    /// Raises or lowers the pause menu (M9 stage 5). <paramref name="text"/> null takes it down.
    /// The box and the origin come from <see cref="PauseMenu"/> and are used exactly as given —
    /// see the field comment above for why this layer measures nothing.
    ///
    /// <para>It rides here, over the presented frame, rather than being printed into the
    /// cartridge's framebuffer, for the reason this whole class exists: that framebuffer is what
    /// <c>quarp sim</c> hashes and what the CI compares across architectures, so a menu drawn
    /// into it would put a paused player's frame outside everyone else's.</para>
    /// </summary>
    public void ShowMenu(string? text, Rectangle box, Point origin)
    {
        if (string.IsNullOrEmpty(text))
        {
            text = null;
        }
        if (text == _shownMenu && box == _menuBox && origin == _menuOrigin)
        {
            return;
        }
        _shownMenu = text;
        _menuBox = box;
        _menuOrigin = origin;
        Rasterize();
    }

    /// <summary>
    /// Sets what the indicator says. <paramref name="text"/> null or empty hides it.
    /// <paramref name="percent"/> in 0..100 adds a progress bar; anything else omits it.
    /// Re-rasterizes only on an actual change, so passing the same cached string every frame
    /// is free.
    /// </summary>
    public void Show(string? text, int percent = -1)
    {
        if (string.IsNullOrEmpty(text))
        {
            text = null;
        }
        if (text == _shownText && percent == _shownPercent)
        {
            return;
        }
        _shownText = text;
        _shownPercent = percent;
        Rasterize();
    }

    /// <summary>Lowers the pause menu without disturbing the status indicator.</summary>
    public void HideMenu() => ShowMenu(null, Rectangle.Empty, Point.Zero);

    /// <summary>
    /// Blends the layer over the frame. <paramref name="destination"/> must be the rectangle
    /// the console texture was drawn into, so overlay pixels line up with console pixels.
    /// </summary>
    public void Draw(SpriteBatch batch, Rectangle destination)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_empty)
        {
            return;
        }
        batch.Draw(_texture, destination, Color.White);
    }

    public void Dispose() => _texture.Dispose();

    private void Rasterize()
    {
        Array.Clear(_pixels);
        _empty = true;
        DrawStatusBox();
        DrawMenuBox();
        _texture.SetData(_pixels);
    }

    /// <summary>The pause menu's plate and rows, drawn where <see cref="PauseMenu"/> put them.</summary>
    private void DrawMenuBox()
    {
        if (_shownMenu is null)
        {
            return;
        }
        FillRect(_menuBox.X, _menuBox.Y, _menuBox.Width, _menuBox.Height, _ink);
        // A one-pixel edge, because the plate stands on the cartridge's own picture and a box
        // with no border reads as a hole in the game rather than as something on top of it. The
        // same argument the sprite and map panels' frames were given in wave S2.
        FrameRect(_menuBox.X, _menuBox.Y, _menuBox.Width, _menuBox.Height, _text);
        Print(_shownMenu, _menuOrigin.X, _menuOrigin.Y, _text);
        _empty = false;
    }

    /// <summary>The bottom-left indicator — PAUSE, the speed, REC, a rebuild's progress.</summary>
    private void DrawStatusBox()
    {
        if (_shownText is null)
        {
            return;
        }

        int textWidth = MeasureWidth(_shownText);
        int textHeight = MeasureHeight(_shownText);
        bool bar = _shownPercent is >= 0 and <= 100;
        int boxWidth = Math.Min(_width - 2 * Margin, textWidth + 2 * Padding);
        int boxHeight = textHeight + 2 * Padding + (bar ? BarHeight + 1 : 0);
        int boxX = Margin;
        int boxY = _height - Margin - boxHeight;   // bottom-left: out of the way of a HUD row

        FillRect(boxX, boxY, boxWidth, boxHeight, _ink);
        Print(_shownText, boxX + Padding, boxY + Padding, _text);
        if (bar)
        {
            int barWidth = boxWidth - 2 * Padding;
            int filled = barWidth * _shownPercent / 100;
            FillRect(boxX + Padding, boxY + boxHeight - Padding - BarHeight + 1, barWidth, BarHeight, WithAlpha(Palette.Master32[1], 255));
            FillRect(boxX + Padding, boxY + boxHeight - Padding - BarHeight + 1, filled, BarHeight, _accent);
        }

        _empty = false;
    }

    /// <summary>A one-pixel border on the rectangle's own edge — four fills, no diagonal maths.</summary>
    private void FrameRect(int x, int y, int width, int height, Color color)
    {
        FillRect(x, y, width, 1, color);
        FillRect(x, y + height - 1, width, 1, color);
        FillRect(x, y, 1, height, color);
        FillRect(x + width - 1, y, 1, height, color);
    }

    private static int MeasureWidth(string text)
    {
        int widest = 0;
        int current = 0;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                widest = Math.Max(widest, current);
                current = 0;
                continue;
            }
            if (c < ' ')
            {
                continue;
            }
            current += SystemFont.CellWidth;
        }
        // The trailing 1 px of inter-character spacing is not ink, so it is not measured.
        return Math.Max(widest, current) - (SystemFont.CellWidth - SystemFont.GlyphWidth);
    }

    private static int MeasureHeight(string text)
    {
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }
        return lines * SystemFont.CellHeight - (SystemFont.CellHeight - SystemFont.GlyphHeight);
    }

    /// <summary>The same 4x6 layout <see cref="VirtualConsole.Print"/> uses, on our own pixels.</summary>
    private void Print(string text, int x, int y, Color color)
    {
        int cursorX = x;
        int cursorY = y;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                cursorX = x;
                cursorY += SystemFont.CellHeight;
                continue;
            }
            if (c < ' ')
            {
                continue;
            }
            uint glyph = SystemFont.GetGlyph(c);
            for (int row = 0; row < SystemFont.GlyphHeight; row++)
            {
                for (int col = 0; col < SystemFont.GlyphWidth; col++)
                {
                    if (SystemFont.IsSet(glyph, col, row))
                    {
                        Plot(cursorX + col, cursorY + row, color);
                    }
                }
            }
            cursorX += SystemFont.CellWidth;
        }
    }

    private void FillRect(int x, int y, int width, int height, Color color)
    {
        int x1 = Math.Min(x + width, _width);
        int y1 = Math.Min(y + height, _height);
        for (int py = Math.Max(y, 0); py < y1; py++)
        {
            int row = py * _width;
            for (int px = Math.Max(x, 0); px < x1; px++)
            {
                _pixels[row + px] = color;
            }
        }
    }

    private void Plot(int x, int y, Color color)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            return;
        }
        _pixels[y * _width + x] = color;
    }

    /// <summary>
    /// A palette entry (0xRRGGBB) with alpha, premultiplied by hand. MonoGame's default
    /// <see cref="BlendState.AlphaBlend"/> expects premultiplied colour, and none of
    /// <see cref="Color"/>'s constructors multiply for you — they pack the bytes as given.
    /// Handing it straight alpha instead makes the semi-transparent box glow rather than
    /// darken, which is the kind of bug that looks like a palette mistake.
    /// </summary>
    private static Color WithAlpha(uint rgb, byte alpha) =>
        new(
            (byte)((byte)(rgb >> 16) * alpha / 255),
            (byte)((byte)(rgb >> 8) * alpha / 255),
            (byte)((byte)rgb * alpha / 255),
            alpha);
}

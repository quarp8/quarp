namespace Quarp.Shell.Desktop;

/// <summary>
/// Geometry of the boot screen: one 160x90 composition — the owner's mockup, coordinate for
/// coordinate — presented at the largest whole multiple that fits the window and centered,
/// exactly the way the game presenter shows a frame. This screen deliberately does
/// <b>not</b> use <see cref="PixelFontMetrics.UiScale"/>: that formula optimizes host text
/// for density (a 720p window reads at x4), while the boot screen is the console's face,
/// authored on the console's own 160x90 grid — at 1280x720 it scales x8, pixel for pixel
/// the mockup. The library and the editors keep their density scale; the two kinds of
/// screen are allowed to disagree because they answer different questions (a list wants
/// rows on screen, a face wants presence).
///
/// <para>All element coordinates are mockup facts, so they live here as the one owner —
/// the renderer translates them through <see cref="X"/>/<see cref="Y"/> and never does
/// layout arithmetic of its own.</para>
/// </summary>
public readonly struct MainMenuLayout
{
    /// <summary>The composition's own grid — the mockup's canvas, which is the profile-8 screen.</summary>
    public const int CanvasWidth = 160;

    public const int CanvasHeight = 90;

    // The mockup's coordinates, measured off quarp.pixelforge itself.
    public const int Margin = 10;           // left edge of every block
    public const int LogoY = 6;             // wordmark band top
    public const int TaglineY = 22;         // C# FANTASY CONSOLE
    public const int SpecY1 = 32;           // VIDEO / COL / FPS row
    public const int SpecY2 = 39;           // CART / CODE / SAVE row
    public const int ItemTextY = 49;        // first menu row's text
    public const int ItemPitch = 9;         // row-to-row advance
    public const int ItemDigitX = 16;       // the 1-2-3 column
    public const int ItemLabelX = 26;       // the door names
    public const int BarRight = 147;        // selection bar runs Margin..BarRight exclusive
    public const int EntryY = 76;           // name field / message line
    public const int FooterY = 83;          // key hints

    /// <summary>Whole-pixel multiple of the canvas, floor 1 — fractional scale is blur (ADR-021 discipline).</summary>
    public int Scale { get; init; }

    /// <summary>Window-space position of the canvas's top-left corner.</summary>
    public int OriginX { get; init; }

    public int OriginY { get; init; }

    public static MainMenuLayout Compute(int windowWidth, int windowHeight)
    {
        int scale = Math.Max(1, Math.Min(windowWidth / CanvasWidth, windowHeight / CanvasHeight));
        return new MainMenuLayout
        {
            Scale = scale,
            OriginX = (windowWidth - CanvasWidth * scale) / 2,
            OriginY = (windowHeight - CanvasHeight * scale) / 2,
        };
    }

    /// <summary>Canvas x to window x.</summary>
    public int X(int canvasX) => OriginX + canvasX * Scale;

    /// <summary>Canvas y to window y.</summary>
    public int Y(int canvasY) => OriginY + canvasY * Scale;
}

using Microsoft.Xna.Framework;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Where a console-sized picture lands inside the window, and — the half that did not exist
/// before wave R1 — how a window point comes back the other way. One integer scale, centred,
/// never fractional (ARCHITECTURE §5): a fractional scale resamples pixel art into blur.
///
/// <para><b>Why this is a type and not three lines repeated.</b> Before this wave the same
/// arithmetic lived twice: <c>QuarpGame.RenderFrame</c> computed it for the cartridge's frame,
/// and <see cref="MainMenuLayout"/> computed it again for the boot screen's 160x90 canvas. Two
/// copies of "where the picture is" were survivable only while nothing needed the inverse.
/// The moment a tool screen is drawn <em>into the console</em> and a mouse has to be answered
/// in console pixels, the inverse becomes load-bearing: an off-by-one between the forward and
/// the backward transform is a cursor that lies, and a cursor that lies about a one-pixel grid
/// is a broken editor. So the fact has exactly one owner, and both directions are computed
/// from the same two numbers.</para>
///
/// <para><b>Rounding is asymmetric on purpose.</b> Console-to-window is exact — a console
/// pixel is a square of <see cref="Scale"/> window pixels, and <see cref="X"/> names its
/// top-left corner. Window-to-console is many-to-one, so the round trip
/// window → console → window does not return the point you started from; it returns the
/// corner of the cell that point fell in. What <em>is</em> guaranteed, and what the tests
/// pin, is that the trip is idempotent from there on and that console → window → console is
/// the identity.</para>
///
/// <para><b>Outside is not clamped.</b> A click in the letterbox is not a click on the edge
/// pixel: <see cref="TryToCanvas"/> answers false and the caller drops the event. Clamping
/// would turn a miss into a hit on the border row, which on a list screen means launching the
/// wrong cartridge.</para>
/// </summary>
public readonly struct FramePlacement
{
    /// <summary>Width of the picture in console pixels.</summary>
    public int CanvasWidth { get; init; }

    /// <summary>Height of the picture in console pixels.</summary>
    public int CanvasHeight { get; init; }

    /// <summary>Whole-pixel multiple, floor 1 — a window smaller than the canvas still shows x1 and crops.</summary>
    public int Scale { get; init; }

    /// <summary>Window x of the canvas's top-left corner; negative when the window is narrower than the canvas.</summary>
    public int OriginX { get; init; }

    /// <summary>Window y of the canvas's top-left corner.</summary>
    public int OriginY { get; init; }

    /// <summary>Width of the presented picture in window pixels.</summary>
    public int DestWidth => CanvasWidth * Scale;

    /// <summary>Height of the presented picture in window pixels.</summary>
    public int DestHeight => CanvasHeight * Scale;

    /// <summary>The rectangle the picture is blitted into — what the presenter hands SpriteBatch.</summary>
    public Rectangle Destination => new(OriginX, OriginY, DestWidth, DestHeight);

    /// <summary>
    /// The placement of a <paramref name="canvasWidth"/> x <paramref name="canvasHeight"/>
    /// picture in a window of the given size. Zero or negative canvas dimensions are refused
    /// rather than silently producing a division by zero later.
    /// </summary>
    public static FramePlacement Compute(int windowWidth, int windowHeight, int canvasWidth, int canvasHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(canvasWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(canvasHeight, 1);
        int scale = Math.Max(1, Math.Min(windowWidth / canvasWidth, windowHeight / canvasHeight));
        return new FramePlacement
        {
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            Scale = scale,
            OriginX = (windowWidth - canvasWidth * scale) / 2,
            OriginY = (windowHeight - canvasHeight * scale) / 2,
        };
    }

    /// <summary>Canvas x to window x — the left edge of that console pixel's square.</summary>
    public int X(int canvasX) => OriginX + canvasX * Scale;

    /// <summary>Canvas y to window y — the top edge of that console pixel's square.</summary>
    public int Y(int canvasY) => OriginY + canvasY * Scale;

    /// <summary>
    /// Window point to console pixel. False — and both outputs zero — when the point is
    /// outside the presented picture: the letterbox, or the part of the canvas a too-small
    /// window crops away. The subtraction happens before the division because C# integer
    /// division truncates toward zero, so <c>-1 / 8</c> is 0 and a naive divide would report
    /// a point one pixel left of the frame as being on column 0.
    /// </summary>
    public bool TryToCanvas(int windowX, int windowY, out int canvasX, out int canvasY)
    {
        canvasX = 0;
        canvasY = 0;
        int dx = windowX - OriginX;
        int dy = windowY - OriginY;
        if (dx < 0 || dy < 0 || dx >= DestWidth || dy >= DestHeight)
        {
            return false;
        }
        canvasX = dx / Scale;
        canvasY = dy / Scale;
        return true;
    }
}

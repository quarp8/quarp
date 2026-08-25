using Microsoft.Xna.Framework.Input;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One frame of mouse input, already edge-detected — the pointer sibling of
/// <see cref="ShellCommands"/>. The mouse exists in the shell for the first time in M9 stage 2.
/// It was the editor's alone while the library was keyboard-only (work order); wave R1 moved
/// the library onto the console, which gave it a grid to point at, so this struct is now read
/// there too. Its fields are still shaped by the editor's needs — the pencil, the eyedropper,
/// the swatches and the sheet grid — and the library uses only <see cref="LeftPressed"/>.
///
/// <para><b>Positions are window-client pixels</b>, the same space
/// <c>GraphicsDevice.PresentationParameters</c> reports. What a reader does with them differs
/// by screen, and that difference is the whole of wave R1 at this layer: the four
/// host-resolution editor screens hit-test directly, because their layouts are measured from
/// the same window every frame; a screen drawn on the console must first convert through
/// <see cref="FramePlacement"/>, the single owner of window-to-console coordinates. No screen
/// may do that arithmetic itself.</para>
/// </summary>
public readonly struct EditorMouse
{
    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>Left button held — feeds drag positions into an open stroke.</summary>
    public bool LeftDown { get; init; }

    /// <summary>Left button went down this frame — begins a stroke, selects a swatch or a sheet cell.</summary>
    public bool LeftPressed { get; init; }

    /// <summary>Left button came up this frame — ends the stroke, committing it as one undo step.</summary>
    public bool LeftReleased { get; init; }

    /// <summary>
    /// Right button went down this frame. Sprite screen: the eyedropper (press, not hold, like
    /// PICO-8) and the flyout's no-clock door. Map screen since wave 3d: the erase press —
    /// LIKO-12's <c>tile.lua</c> forces the tile to 0 under button 2 for every drawing tool
    /// (REFERENCES-EDITORS §7.3), and that is the map's only eraser.
    /// </summary>
    public bool RightPressed { get; init; }

    /// <summary>Right button held — the map's erase drag keeps stamping tile 0 along it.</summary>
    public bool RightDown { get; init; }

    /// <summary>Right button came up this frame — commits the map's erase gesture as one undo step.</summary>
    public bool RightReleased { get; init; }

    /// <summary>
    /// Middle button went down this frame — the map's tile eyedropper (TIC-80
    /// <c>processMouseDrawMode</c>: the middle button puts the tile under the cursor back in
    /// the picker). No screen reads it as a hold, so there is no MiddleDown to keep in step.
    /// </summary>
    public bool MiddlePressed { get; init; }

    /// <summary>
    /// This frame's wheel movement in MonoGame detents (+120 per notch toward the user's
    /// "up"). The sheet window's horizontal scroll reads it (wave 2h); zero elsewhere.
    /// Reported as a delta, not the cumulative value, because only the reader holds the
    /// previous frame.
    /// </summary>
    public int WheelDelta { get; init; }
}

/// <summary>
/// Turns raw <see cref="MouseState"/> pairs into <see cref="EditorMouse"/>, remembering the
/// previous frame — the exact shape of <see cref="ShellCommandReader"/>, and for the same
/// reason: one reader, one previous-state, owned by the window and passed the state the
/// window polled. No global singleton, so tests feed synthetic states and a second window
/// (if one ever exists) gets its own edges. The shell reads it every frame in every mode —
/// a button held across a mode switch into the editor is then already "down" and produces
/// no phantom press on the editor's first frame.
/// </summary>
public sealed class EditorMouseReader
{
    private MouseState _previous;

    public EditorMouse Read(MouseState mouse)
    {
        bool left = mouse.LeftButton == ButtonState.Pressed;
        bool wasLeft = _previous.LeftButton == ButtonState.Pressed;
        bool right = mouse.RightButton == ButtonState.Pressed;
        bool wasRight = _previous.RightButton == ButtonState.Pressed;
        bool middle = mouse.MiddleButton == ButtonState.Pressed;
        bool wasMiddle = _previous.MiddleButton == ButtonState.Pressed;
        int wheel = mouse.ScrollWheelValue - _previous.ScrollWheelValue;
        _previous = mouse;
        return new EditorMouse
        {
            X = mouse.X,
            Y = mouse.Y,
            LeftDown = left,
            LeftPressed = left && !wasLeft,
            LeftReleased = !left && wasLeft,
            RightPressed = right && !wasRight,
            RightDown = right,
            RightReleased = !right && wasRight,
            MiddlePressed = middle && !wasMiddle,
            WheelDelta = wheel,
        };
    }
}

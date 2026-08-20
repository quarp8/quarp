using Microsoft.Xna.Framework.Input;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One frame of mouse input for the editor, already edge-detected — the pointer sibling of
/// <see cref="ShellCommands"/>. The mouse exists in the shell for the first time in M9 stage 2
/// and <b>only the editor reads it</b>: the library stays keyboard-driven (work order), so
/// this struct is consumed in exactly one mode and carries only what the pencil, the
/// eyedropper and the two click targets (swatches, sheet grid) need.
///
/// <para>Positions are window-client pixels, the same space
/// <c>GraphicsDevice.PresentationParameters</c> reports and <see cref="SpriteEditorLayout"/>
/// is computed in, so hit tests need no conversion — the "scale" between mouse and screen is
/// handled by the layout being measured from the same window every frame.</para>
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

    /// <summary>Right button went down this frame — the eyedropper (press, not hold, like PICO-8).</summary>
    public bool RightPressed { get; init; }

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
            WheelDelta = wheel,
        };
    }
}

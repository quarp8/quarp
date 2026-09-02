using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Api;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Maps physical devices to the logical QUARP-8 controller (M1 work order):
/// player 0 = arrow keys, Z (O), X (X), Enter (Start) plus gamepad 0
/// (D-pad, A = O, B = X, Start); player 1 = the ESDF block plus gamepad 1.
/// The cartridge only ever sees <see cref="InputState"/> bitmasks (SPEC-8 §5).
///
/// <para><b>Why player 1 has keys at all.</b> It used to be gamepad-only, which meant a
/// two-handed cartridge was unplayable on a keyboard. POOM is one: its own control chart puts
/// forward/back and strafe on one hand and turn and fire on the other, and it reads the second
/// hand as player 2 — the arrangement PICO-8 ships as its default player-2 keyconfig. The
/// letters are that keyconfig's, not an invention: S and F are left and right, E and D are up
/// and down, left shift is O and A is X (Tab too, because a laptop's A is under the strafe
/// hand). A cartridge that only reads player 0 does not notice they exist.</para>
/// </summary>
public static class InputMapper
{
    /// <summary>Polls the keyboard itself — for callers with no keyboard state of their own.</summary>
    public static InputState Read() => Read(Keyboard.GetState());

    /// <summary>
    /// Maps an already-polled keyboard state. The shell reads the keyboard once per frame and
    /// feeds it to both this and <see cref="ShellCommandReader"/>, so the cartridge and the
    /// time controls can never disagree about which keys were down on a given frame.
    /// </summary>
    public static InputState Read(KeyboardState keyboard)
    {
        byte player0 = FromKeyboard(keyboard);
        player0 |= FromGamePad(GamePad.GetState(PlayerIndex.One));
        byte player1 = FromSecondKeyboard(keyboard);
        player1 |= FromGamePad(GamePad.GetState(PlayerIndex.Two));
        return new InputState(player0, player1);
    }

    /// <summary>
    /// The full ADR-030 snapshot: buttons as above, plus the pointer moved from window pixels
    /// into console pixels. The conversion goes through <paramref name="placement"/> — the
    /// single owner of window-to-console coordinates — with the clamp ADR-030 п.6 prescribes
    /// for the cartridge: a pointer in the letterbox or off the window reads as the nearest
    /// screen pixel, because the API has no "off screen" answer in v1.
    /// <paramref name="wheelSteps"/> is already in whole notches for <em>this frame</em>; the
    /// caller owns the detent accumulator because only it sees every frame.
    /// </summary>
    public static InputState Read(
        KeyboardState keyboard, in EditorMouse mouse, in FramePlacement placement, int wheelSteps)
    {
        placement.ToCanvasClamped(mouse.X, mouse.Y, out int x, out int y);
        byte buttons = 0;
        if (mouse.LeftDown)
        {
            buttons |= 1 << (int)MouseButton.Left;
        }
        if (mouse.RightDown)
        {
            buttons |= 1 << (int)MouseButton.Right;
        }
        if (mouse.MiddleDown)
        {
            buttons |= 1 << (int)MouseButton.Middle;
        }
        return Read(keyboard).WithMouse(x, y, buttons, wheelSteps);
    }

    private static byte FromKeyboard(KeyboardState keyboard)
    {
        byte mask = 0;
        if (keyboard.IsKeyDown(Keys.Left))
        {
            mask |= Bit(Button.Left);
        }
        if (keyboard.IsKeyDown(Keys.Right))
        {
            mask |= Bit(Button.Right);
        }
        if (keyboard.IsKeyDown(Keys.Up))
        {
            mask |= Bit(Button.Up);
        }
        if (keyboard.IsKeyDown(Keys.Down))
        {
            mask |= Bit(Button.Down);
        }
        if (keyboard.IsKeyDown(Keys.Z))
        {
            mask |= Bit(Button.O);
        }
        if (keyboard.IsKeyDown(Keys.X))
        {
            mask |= Bit(Button.X);
        }
        if (keyboard.IsKeyDown(Keys.Enter))
        {
            mask |= Bit(Button.Start);
        }
        return mask;
    }

    /// <summary>PICO-8's default player-2 keyconfig: S F E D, left shift, A and Tab.</summary>
    private static byte FromSecondKeyboard(KeyboardState keyboard)
    {
        byte mask = 0;
        if (keyboard.IsKeyDown(Keys.S))
        {
            mask |= Bit(Button.Left);
        }
        if (keyboard.IsKeyDown(Keys.F))
        {
            mask |= Bit(Button.Right);
        }
        if (keyboard.IsKeyDown(Keys.E))
        {
            mask |= Bit(Button.Up);
        }
        if (keyboard.IsKeyDown(Keys.D))
        {
            mask |= Bit(Button.Down);
        }
        if (keyboard.IsKeyDown(Keys.LeftShift))
        {
            mask |= Bit(Button.O);
        }
        if (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Tab))
        {
            mask |= Bit(Button.X);
        }
        return mask;
    }

    private static byte FromGamePad(GamePadState pad)
    {
        if (!pad.IsConnected)
        {
            return 0;
        }
        byte mask = 0;
        if (pad.DPad.Left == ButtonState.Pressed)
        {
            mask |= Bit(Button.Left);
        }
        if (pad.DPad.Right == ButtonState.Pressed)
        {
            mask |= Bit(Button.Right);
        }
        if (pad.DPad.Up == ButtonState.Pressed)
        {
            mask |= Bit(Button.Up);
        }
        if (pad.DPad.Down == ButtonState.Pressed)
        {
            mask |= Bit(Button.Down);
        }
        if (pad.Buttons.A == ButtonState.Pressed)
        {
            mask |= Bit(Button.O);
        }
        if (pad.Buttons.B == ButtonState.Pressed)
        {
            mask |= Bit(Button.X);
        }
        if (pad.Buttons.Start == ButtonState.Pressed)
        {
            mask |= Bit(Button.Start);
        }
        return mask;
    }

    private static byte Bit(Button button) => (byte)(1 << (int)button);
}

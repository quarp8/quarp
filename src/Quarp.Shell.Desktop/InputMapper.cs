using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Api;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Maps physical devices to the logical QUARP-8 controller (M1 work order):
/// player 0 = arrow keys, Z (O), X (X), Enter (Start) plus gamepad 0
/// (D-pad, A = O, B = X, Start); player 1 = gamepad 1. The cartridge only ever
/// sees <see cref="InputState"/> bitmasks (SPEC-8 §5).
/// </summary>
public static class InputMapper
{
    public static InputState Read()
    {
        KeyboardState keyboard = Keyboard.GetState();
        byte player0 = FromKeyboard(keyboard);
        player0 |= FromGamePad(GamePad.GetState(PlayerIndex.One));
        byte player1 = FromGamePad(GamePad.GetState(PlayerIndex.Two));
        return new InputState(player0, player1);
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

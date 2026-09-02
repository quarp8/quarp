using Quarp.Api;

namespace Quarp.Core;

/// <summary>
/// Everything the outside world tells the simulation on one tick: one button bitmask per
/// player, bit index = (int)<see cref="Button"/> (SPEC-8 §5, two players max), and — since
/// ADR-030 — the pointer: position in console screen pixels, held pointer buttons
/// (bit index = (int)<see cref="MouseButton"/>) and the tick's wheel delta. The shell fills it
/// from real devices; replays store it verbatim (buttons and pointer as separate streams,
/// REPLAY-FORMAT §3). Immutable value type.
///
/// <para><b>The pointer coordinates here are bytes, not screen-clamped values.</b> The struct
/// does not know the screen size — <see cref="VirtualConsole"/> clamps to its profile when the
/// tick begins, so what a cartridge reads is always on screen whatever a shell or a script fed
/// in. The byte range 0..255 is also exactly what the replay's pointer stream can store, so a
/// recorded state always round-trips.</para>
/// </summary>
public readonly struct InputState : IEquatable<InputState>
{
    public const int MaxPlayers = 2;

    /// <summary>The pointer-button bits a state can hold: one per <see cref="MouseButton"/>, bits 0-2.</summary>
    public const byte KnownMouseButtons = (1 << ((int)MouseButton.Middle + 1)) - 1;

    /// <summary>Bitmask of buttons held by player 0.</summary>
    public byte Player0 { get; }

    /// <summary>Bitmask of buttons held by player 1.</summary>
    public byte Player1 { get; }

    /// <summary>Pointer x as handed in (0..255); the console clamps it to the screen at tick start.</summary>
    public byte MouseX { get; }

    /// <summary>Pointer y as handed in (0..255); the console clamps it to the screen at tick start.</summary>
    public byte MouseY { get; }

    /// <summary>Bitmask of pointer buttons held, bit index = (int)<see cref="MouseButton"/>.</summary>
    public byte MouseButtons { get; }

    /// <summary>Wheel steps turned on this tick, signed; the shell delivers a frame's notches to one tick only.</summary>
    public sbyte MouseWheel { get; }

    public InputState(byte player0, byte player1)
    {
        Player0 = player0;
        Player1 = player1;
    }

    public InputState(byte player0, byte player1, byte mouseX, byte mouseY, byte mouseButtons, sbyte mouseWheel)
    {
        Player0 = player0;
        Player1 = player1;
        MouseX = mouseX;
        MouseY = mouseY;
        MouseButtons = mouseButtons;
        MouseWheel = mouseWheel;
    }

    /// <summary>Bitmask of the given player; players outside 0-1 read as "nothing held".</summary>
    public byte Mask(int player) => player switch
    {
        0 => Player0,
        1 => Player1,
        _ => 0,
    };

    /// <summary>True while the button is held. Unknown players or buttons are simply not held.</summary>
    public bool IsDown(int player, Button button)
    {
        if ((uint)button > (uint)Button.Start)
        {
            return false;
        }
        return ((Mask(player) >> (int)button) & 1) != 0;
    }

    /// <summary>True while the pointer button is held; an unknown button is simply not held.</summary>
    public bool MouseIsDown(MouseButton button)
    {
        if ((uint)button > (uint)MouseButton.Middle)
        {
            return false;
        }
        return ((MouseButtons >> (int)button) & 1) != 0;
    }

    /// <summary>Copy with one button changed — for shells and tests (cold path).</summary>
    public InputState With(int player, Button button, bool down)
    {
        if ((uint)player >= MaxPlayers || (uint)button > (uint)Button.Start)
        {
            return this;
        }
        byte bit = (byte)(1 << (int)button);
        byte mask = down ? (byte)(Mask(player) | bit) : (byte)(Mask(player) & ~bit);
        return player == 0
            ? new InputState(mask, Player1, MouseX, MouseY, MouseButtons, MouseWheel)
            : new InputState(Player0, mask, MouseX, MouseY, MouseButtons, MouseWheel);
    }

    /// <summary>
    /// Copy with the pointer replaced. Coordinates are clamped into the byte range here and to
    /// the actual screen by the console at tick start; the wheel is clamped into a signed byte —
    /// more steps than ±127 in a sixtieth of a second is not a hand, it is an overflow.
    /// Unknown bits in <paramref name="buttons"/> are dropped so a state can never hold a
    /// pointer button the replay format cannot store.
    /// </summary>
    public InputState WithMouse(int x, int y, byte buttons, int wheel) => new(
        Player0,
        Player1,
        (byte)Math.Clamp(x, 0, byte.MaxValue),
        (byte)Math.Clamp(y, 0, byte.MaxValue),
        (byte)(buttons & KnownMouseButtons),
        (sbyte)Math.Clamp(wheel, sbyte.MinValue, sbyte.MaxValue));

    /// <summary>
    /// Copy with only the wheel changed. The shell uses it to hand a frame's wheel notches to
    /// the first tick of the frame's batch and zero to the rest — the wheel is a per-tick
    /// delta, and repeating it eight times at x8 would scroll eight times (ADR-030 п.6).
    /// </summary>
    public InputState WithMouseWheel(int wheel) => new(
        Player0, Player1, MouseX, MouseY, MouseButtons,
        (sbyte)Math.Clamp(wheel, sbyte.MinValue, sbyte.MaxValue));

    public bool Equals(InputState other) =>
        Player0 == other.Player0
        && Player1 == other.Player1
        && MouseX == other.MouseX
        && MouseY == other.MouseY
        && MouseButtons == other.MouseButtons
        && MouseWheel == other.MouseWheel;

    public override bool Equals(object? obj) => obj is InputState other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Player0, Player1, MouseX, MouseY, MouseButtons, MouseWheel);

    public static bool operator ==(InputState a, InputState b) => a.Equals(b);

    public static bool operator !=(InputState a, InputState b) => !a.Equals(b);
}

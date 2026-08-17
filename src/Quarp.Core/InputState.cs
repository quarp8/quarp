using Quarp.Api;

namespace Quarp.Core;

/// <summary>
/// Everything the outside world tells the simulation on one tick: one button bitmask per
/// player, bit index = (int)<see cref="Button"/> (SPEC-8 §5, two players max). The shell
/// fills it from real devices; replays store it verbatim. Immutable value type.
/// </summary>
public readonly struct InputState : IEquatable<InputState>
{
    public const int MaxPlayers = 2;

    /// <summary>Bitmask of buttons held by player 0.</summary>
    public byte Player0 { get; }

    /// <summary>Bitmask of buttons held by player 1.</summary>
    public byte Player1 { get; }

    public InputState(byte player0, byte player1)
    {
        Player0 = player0;
        Player1 = player1;
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

    /// <summary>Copy with one button changed — for shells and tests (cold path).</summary>
    public InputState With(int player, Button button, bool down)
    {
        if ((uint)player >= MaxPlayers || (uint)button > (uint)Button.Start)
        {
            return this;
        }
        byte bit = (byte)(1 << (int)button);
        byte mask = down ? (byte)(Mask(player) | bit) : (byte)(Mask(player) & ~bit);
        return player == 0 ? new InputState(mask, Player1) : new InputState(Player0, mask);
    }

    public bool Equals(InputState other) => Player0 == other.Player0 && Player1 == other.Player1;

    public override bool Equals(object? obj) => obj is InputState other && Equals(other);

    public override int GetHashCode() => Player0 | (Player1 << 8);

    public static bool operator ==(InputState a, InputState b) => a.Equals(b);

    public static bool operator !=(InputState a, InputState b) => !a.Equals(b);
}

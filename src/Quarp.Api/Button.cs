namespace Quarp.Api;

/// <summary>Logical buttons of a QUARP-8 controller (SPEC-8 §5). Mapping to physical input is the shell's job.</summary>
public enum Button
{
    /// <summary>D-pad left.</summary>
    Left = 0,

    /// <summary>D-pad right.</summary>
    Right = 1,

    /// <summary>D-pad up.</summary>
    Up = 2,

    /// <summary>D-pad down.</summary>
    Down = 3,

    /// <summary>Primary action button (keyboard Z, gamepad A).</summary>
    O = 4,

    /// <summary>Secondary action button (keyboard X, gamepad B).</summary>
    X = 5,

    /// <summary>Start: pause and console menu; a cartridge may also read it.</summary>
    Start = 6,
}

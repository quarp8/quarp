namespace Quarp.Core;

/// <summary>Virtual hardware profile — data, not code (ARCHITECTURE §2). No if(is16) anywhere.</summary>
public sealed class ConsoleProfile
{
    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>QUARP-8: 128x72 (16:9), tile grid 16x9 of 8x8 (SPEC-8 §1).</summary>
    public static readonly ConsoleProfile Profile8 = new()
    {
        Name = "QUARP-8",
        Width = 128,
        Height = 72,
    };
}

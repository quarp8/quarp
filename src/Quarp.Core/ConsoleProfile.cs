namespace Quarp.Core;

/// <summary>
/// Virtual hardware profile — data, not code (ARCHITECTURE §2). No if(is16) anywhere.
/// <para>One profile ships today, <see cref="Profile8"/>, but the type stays constructible:
/// "the same compiled cartridge on a different console lays itself out differently" is a
/// property tests check by building a profile of their own, and QUARP-16 (320x180, M6) is the
/// next real one. Nothing may read the screen size from a static — it is handed down from the
/// profile the console was built with, because a replay resimulated on a different console
/// than it was recorded on is not the same run.</para>
/// </summary>
public sealed class ConsoleProfile
{
    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>
    /// QUARP-8: 160x90 (16:9), tile grid 20 x 11.25 of 8x8 (SPEC-8 §1, ADR-021).
    ///
    /// <para><b>Why 160x90.</b> Six prototypes were played at both candidate sizes in M4 and
    /// the numbers went into M4-MEASUREMENTS.md before the games were written: action survives
    /// 128x72, text does not (a dialogue window eats 43.1 % of that screen against 34.4 % here).
    /// Of the resolutions in this class only 160x90 is an integer scale on every common display
    /// — x8 on 720p, x12 on 1080p, x16 on 1440p, x24 on 4K — which is the M0 criterion nothing
    /// is allowed to break. 176x99 (the arithmetic twin of PICO-8's area) is integer on no
    /// common display at all, 1080/99 being 10.9; 192x108 loses 720p, and with it the uConsole
    /// of M5.</para>
    ///
    /// <para><b>The 11.25 rows are permanent, not a bug to fix.</b> 90 is not a multiple of 8,
    /// so a full-screen 8x8 tile grid ends in a row cut to 2 px, the way TIC-80's does. Tile
    /// games are expected to live with it rather than pad the screen: derive the row count from
    /// the field the game actually draws into, <c>visibleRows = fieldH / TileSize</c>, then draw
    /// one cell past it each way — a camera clamped against a field that is not a whole number
    /// of tiles stops mid-cell, and the last, partly visible row still has to be painted
    /// (carts/digger is the worked example). Code that instead assumes <c>Height % 8 == 0</c>
    /// leaves a 2 px strip of stale pixels along the bottom edge on every frame.</para>
    /// </summary>
    public static readonly ConsoleProfile Profile8 = new()
    {
        Name = "QUARP-8",
        Width = 160,
        Height = 90,
    };
}

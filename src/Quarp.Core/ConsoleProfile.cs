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

    /// <summary>
    /// The 160x90 spike: <b>dev-only, not part of QUARP-8</b>. ADR-005 names it as the one
    /// fallback resolution if the worst-case genres — vertical platformers, text-heavy scenes —
    /// turn out to be cramped at 128x72, and M4 has to settle that question with evidence
    /// rather than taste. The evidence is the same demo game rendered at both sizes, so the
    /// alternative has to exist as something a run can be pointed at.
    ///
    /// <para><b>Why it is a profile and not a build flag.</b> "The hardware profile is data,
    /// not code" (ARCHITECTURE §2). A spike wired as <c>if (isWide)</c> would measure the
    /// branch instead of the resolution: every place that forgot the branch would keep drawing
    /// at 128x72 and quietly flatter the incumbent. As a second <see cref="ConsoleProfile"/> it
    /// travels the one path everything else already travels — <see cref="Framebuffer"/> sizes
    /// itself from it, <see cref="VirtualConsole"/> clips and plots against it, and a cartridge
    /// reads it back through <see cref="Quarp.Api.IConsoleApi.ScreenWidth"/>. Nothing branches.</para>
    ///
    /// <para><b>Why 160x90 specifically</b> (ADR-005): exact 16:9 like 128x72, and an integer
    /// scale on every real display — x8 is 720p, x12 is 1080p. Its cost, and the reason it is
    /// the fallback rather than the default, is that the 8x8 tile grid stops being whole:
    /// 20 x 11.25 cells, with the bottom row cut by 2 px the way TIC-80's is. The earlier
    /// candidate 160x96 was rejected outright — neither 16:9 nor pixel-perfect at 720p.</para>
    ///
    /// <para><b>What it must not touch</b> (M4 work order Р6). It is outside SPEC-8, outside
    /// CI, and outside every golden constant; the sprite sheet stays 128x128 and the map stays
    /// 256x72, so no cartridge asset changes shape when a run switches profiles. The snake and
    /// all four milestone anchors run on <see cref="Profile8"/> and are not allowed to move
    /// because this field exists — a frame hash that shifts is a bug in something else, found
    /// early. After the M4 verdict this profile either becomes the specification (and every
    /// anchor is re-pinned deliberately) or is deleted in a single commit; it is written to be
    /// removable.</para>
    /// </summary>
    public static readonly ConsoleProfile Profile8Wide = new()
    {
        Name = "QUARP-8W",
        Width = 160,
        Height = 90,
    };
}

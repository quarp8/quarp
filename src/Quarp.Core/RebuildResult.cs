namespace Quarp.Core;

/// <summary>
/// What happened when new cartridge code was dropped into a running session
/// (<see cref="TimeMachine.Rebuild(Quarp.Api.Cartridge)"/>): either it resimulated the whole
/// recorded log and the console stands where it stood, or it threw — and then
/// <see cref="Tick"/> says exactly which tick it died on, so the shell can name the frame in
/// its message and fall back to restart mode instead of pretending nothing happened.
/// </summary>
public readonly struct RebuildResult
{
    private RebuildResult(bool success, int tick, Exception? error)
    {
        Success = success;
        Tick = tick;
        Error = error;
    }

    /// <summary>True when the new code replayed the whole log without throwing.</summary>
    public bool Success { get; }

    /// <summary>
    /// On success, the tick the console landed on — the one it was on before the rebuild.
    /// On failure, the tick whose Update or Draw threw; 0 means the cartridge's Init, which is
    /// tick 0 of the simulation (SPEC-8 §7).
    /// </summary>
    public int Tick { get; }

    /// <summary>The exception the new code threw, or null on success.</summary>
    public Exception? Error { get; }

    public static RebuildResult Succeeded(int tick) => new(true, tick, null);

    public static RebuildResult Failed(int tick, Exception error) => new(false, tick, error);
}

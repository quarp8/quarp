using Quarp.Api;

namespace Quarp.Core;

/// <summary>
/// The deterministic time engine: one <see cref="VirtualConsole"/>, one <see cref="ReplayLog"/>,
/// and the rule that the console's state at tick N is nothing but a function of the header
/// (cartridge, seed, persistent snapshot) and the first N recorded inputs (SPEC-8 §7).
/// Everything the milestone promises falls out of that rule:
/// <list type="bullet">
/// <item>play — <see cref="Advance(InputState)"/>, recording as it goes;</item>
/// <item>rewind and step-back — <see cref="SeekTo"/>, resimulating from tick 0 with Draw
/// suppressed until the frame the caller will actually look at (ADR-006: checkpoint v1 is
/// tick 0, so the cost is linear in session length — ARCHITECTURE §4);</item>
/// <item>hot reload that continues instead of restarting — <see cref="Rebuild(Cartridge)"/>.</item>
/// </list>
/// Pure, like the rest of the core: no files, no clocks, no environment. Replays arrive and
/// leave as bytes; whoever owns the disk owns the disk.
/// Not thread-safe, and not meant to be — the simulation is one thread by construction.
/// </summary>
public sealed class TimeMachine
{
    /// <summary>Default ticks between <see cref="Progress"/> callbacks during a long resimulation.</summary>
    public const int DefaultProgressInterval = 1024;

    private readonly VirtualConsole _console;
    private readonly ReplayLog _log;

    private ReplayHeader _header;
    private Cartridge _cart;
    private int _progressInterval = DefaultProgressInterval;
    private int _seekDrawTail = 1;
    private bool _booted;

    /// <summary>
    /// Builds a session. The console is created here and kept for the machine's whole life —
    /// including across <see cref="Rebuild(Cartridge)"/> — so a shell may cache
    /// <see cref="Framebuffer"/> once and never look at it again.
    /// Pass a fresh <see cref="ReplayLog"/> to record, or one loaded from a .qrpr to play back.
    /// Nothing runs yet: call <see cref="Boot"/>, which is where a cartridge crash in Init
    /// surfaces, with the console already in hand to draw a banner on.
    /// </summary>
    public TimeMachine(
        ConsoleProfile profile,
        Cartridge cart,
        ReplayHeader header,
        ReplayLog log,
        byte[]? sheet = null,
        byte[]? map = null,
        byte[]? flags = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(log);
        _console = new VirtualConsole(profile, sheet, map, flags);
        _cart = cart;
        _header = header;
        _log = log;
    }

    /// <summary>The console being driven. Its identity never changes, cartridge reloads included.</summary>
    public VirtualConsole Console => _console;

    /// <summary>The framebuffer to present — stable across rebuilds, unlike a recreated console's.</summary>
    public Framebuffer Framebuffer => _console.Framebuffer;

    /// <summary>The input log: recorded by <see cref="Advance(InputState)"/>, replayed by everything else.</summary>
    public ReplayLog Log => _log;

    /// <summary>The cartridge instance currently driving the console.</summary>
    public Cartridge Cartridge => _cart;

    /// <summary>Cartridge identity, seed and persistent snapshot this session boots from.</summary>
    public ReplayHeader Header => _header;

    /// <summary>Where the console stands now. Init is tick 0, so the first Update lands on 1.</summary>
    public int Tick => _console.Ticks;

    /// <summary>False until <see cref="Boot"/> succeeds, and again if a boot crashes in Init.</summary>
    public bool IsBooted => _booted;

    /// <summary>
    /// Called during a long resimulation with (tick reached, tick aimed for) so a shell can show
    /// progress instead of freezing silently. Assign a cached delegate once — it is invoked in
    /// the resimulation loop, where a fresh closure per call would be an allocation per frame.
    /// Short seeks (under <see cref="ProgressInterval"/> ticks) never call it at all.
    /// </summary>
    public Action<int, int>? Progress { get; set; }

    /// <summary>Minimum ticks between <see cref="Progress"/> callbacks. At least 1.</summary>
    public int ProgressInterval
    {
        get => _progressInterval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _progressInterval = value;
        }
    }

    /// <summary>
    /// How many trailing ticks of a seek run their <c>Draw</c>. One — the default — is what the
    /// milestone asks for: skip Draw for the whole resimulation, draw only the frame the caller
    /// will see. That is exact for any cartridge whose Draw paints the frame it is given, which
    /// is every cartridge that starts with <c>Cls</c>. A cartridge that instead accumulates on
    /// top of previous frames needs a longer tail to look right after a rewind; the core cannot
    /// know how long, so it is the shell's dial. At least 1.
    /// </summary>
    public int SeekDrawTail
    {
        get => _seekDrawTail;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _seekDrawTail = value;
        }
    }

    // --- lifecycle ---

    /// <summary>
    /// Cold-boots the session to tick 0: assets back to the ones loaded, persistent memory back
    /// to the header's snapshot, RNG seeded from the header, then the cartridge's Init as tick 0.
    /// Every input of the simulation is restored, which is what makes the run that follows
    /// bit-identical to the recorded one.
    /// A crash inside Init propagates; the console stays usable for a banner and
    /// <see cref="IsBooted"/> stays false.
    /// </summary>
    public void Boot()
    {
        _booted = false;
        _console.ResetAssets();
        _console.LoadPersistent(_header.Persistent);
        _console.AttachCart(_cart, _header.Seed);
        _booted = true;
    }

    /// <summary>Drops the recorded timeline and cold-boots — "start over", not "rewind".</summary>
    public void Restart()
    {
        _log.Clear();
        Boot();
    }

    /// <summary>
    /// Points the header at a different cartridge build. Seed and persistent snapshot are left
    /// alone, so resimulation is unaffected — this only changes which hash a replay saved from
    /// now on will claim to belong to, which is what a hot reload has to update.
    /// </summary>
    public void SetCartIdentity(ReadOnlySpan<byte> cartIdentity) => _header = _header.WithIdentity(cartIdentity);

    // --- playing forward ---

    /// <summary>Records the input and runs one tick — the live play path.</summary>
    public void Advance(InputState input)
    {
        EnsureBooted();
        _log.Record(_console.Ticks, input);
        _console.Tick(input);
    }

    /// <summary>
    /// Records and runs <paramref name="ticks"/> ticks from the same input snapshot. That is
    /// exactly what fast-forward needs: the shell polls devices once a frame and, at x8, feeds
    /// those buttons to eight ticks. The simulation still sees 60 ticks per game second — speed
    /// is tick frequency, never a change to the simulation itself.
    /// Resuming play after a rewind branches the timeline: the recorded future is dropped.
    /// </summary>
    public void Advance(int ticks, InputState input)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        EnsureBooted();
        for (int i = 0; i < ticks; i++)
        {
            _log.Record(_console.Ticks, input);
            _console.Tick(input);
        }
    }

    /// <summary>
    /// Steps forward through already recorded input, drawing every frame — replay playback and
    /// step-forward-while-paused. Stops at the end of the log and returns how many ticks it ran,
    /// so a caller can tell "played 30" from "the replay ended".
    /// </summary>
    public int ReplayForward(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        EnsureBooted();
        int done = 0;
        while (done < ticks && _console.Ticks < _log.TickCount)
        {
            _console.Tick(_log.InputAt(_console.Ticks));
            done++;
        }
        return done;
    }

    // --- moving in time ---

    /// <summary>
    /// Puts the console on <paramref name="tick"/> with the frame that tick draws. Going back
    /// means a cold boot and a resimulation from 0 (ADR-006 — no snapshots in v1); going forward
    /// simply continues from here, which is the same state by the determinism rule and far
    /// cheaper. Either way only the last <see cref="SeekDrawTail"/> tick draws, so the landing
    /// frame is the one a straight run would have produced without paying for the frames nobody
    /// will ever see.
    /// </summary>
    public void SeekTo(int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        if (tick > _log.TickCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick,
                $"The log holds {_log.TickCount} ticks; play past its end with Advance, not SeekTo.");
        }
        if (!_booted || tick < _console.Ticks)
        {
            Boot();
        }
        if (_console.Ticks == tick)
        {
            return;
        }
        Resimulate(tick);
    }

    /// <summary>One tick back, the pause-mode step. False when already at tick 0.</summary>
    public bool StepBack()
    {
        if (_console.Ticks <= 0)
        {
            return false;
        }
        SeekTo(_console.Ticks - 1);
        return true;
    }

    // --- hot reload, continuation mode ---

    /// <summary>
    /// Swaps in newly compiled cartridge code and puts the session back where it was: fresh
    /// console state on the new code, the recorded input replayed up to the current tick. This
    /// is the milestone's headline feature — edit the snake's color mid-game and it is still the
    /// same snake, in the same place, only greener.
    /// Never throws for a cartridge crash: the returned <see cref="RebuildResult"/> names the
    /// tick that died so the shell can report it and fall back to restart mode. To undo, call
    /// this again with the previous cartridge instance — its ALC is still alive at that point,
    /// and the same replay puts the old code back exactly where it was.
    /// </summary>
    public RebuildResult Rebuild(Cartridge newCart) => RebuildCore(newCart, replaceAssets: false, null, null, null);

    /// <summary>
    /// <see cref="Rebuild(Cartridge)"/> with new assets too — the author edited gfx.png or the
    /// map, not just the code. The assets become the new boot image, so the resimulation and
    /// every later rewind start from them.
    /// </summary>
    public RebuildResult Rebuild(Cartridge newCart, byte[]? sheet, byte[]? map, byte[]? flags) =>
        RebuildCore(newCart, replaceAssets: true, sheet, map, flags);

    private RebuildResult RebuildCore(Cartridge newCart, bool replaceAssets, byte[]? sheet, byte[]? map, byte[]? flags)
    {
        ArgumentNullException.ThrowIfNull(newCart);
        // Where the player was. The log can be one tick ahead of the console when the old code
        // died mid-tick, and resimulating a tick the console never finished is not "the same
        // place" — aim at the console.
        int target = Math.Min(_console.Ticks, _log.TickCount);
        _cart = newCart;
        if (replaceAssets)
        {
            _console.LoadAssets(sheet, map, flags);
        }
        try
        {
            Boot();
            if (target > 0)
            {
                Resimulate(target);
            }
        }
        catch (Exception e)
        {
            // VirtualConsole bumps its tick counter before calling Update, so Ticks is already
            // the number of the tick that threw; a crash in Init leaves it at 0, which is tick 0.
            return RebuildResult.Failed(_console.Ticks, e);
        }
        return RebuildResult.Succeeded(target);
    }

    // --- the resimulation loop ---

    /// <summary>
    /// Runs forward to <paramref name="target"/> on recorded input. Ticks before the draw tail
    /// get Update only: Draw does not touch state (SPEC-8 §7), so skipping it changes nothing
    /// the next tick can see, and it is the difference between a rewind that feels instant and
    /// one that redraws a hundred thousand frames nobody asked for.
    /// Allocates nothing — the log hands back a struct and the progress callback takes ints.
    /// </summary>
    private void Resimulate(int target)
    {
        int start = _console.Ticks;
        int interval = _progressInterval;
        int firstDrawnTick = target - _seekDrawTail + 1;

        // A step-back is two ticks of work; announcing it would be noise. Only a resimulation
        // long enough to be felt gets an opening call, periodic calls and a closing one.
        Action<int, int>? progress = target - start >= interval ? Progress : null;
        progress?.Invoke(start, target);
        int nextReport = start + interval;

        while (_console.Ticks < target)
        {
            InputState input = _log.InputAt(_console.Ticks);
            if (_console.Ticks + 1 >= firstDrawnTick)
            {
                _console.Tick(input);
            }
            else
            {
                _console.TickUpdateOnly(input);
            }
            if (progress is not null && _console.Ticks >= nextReport)
            {
                progress(_console.Ticks, target);
                nextReport = _console.Ticks + interval;
            }
        }

        progress?.Invoke(target, target);
    }

    private void EnsureBooted()
    {
        if (!_booted)
        {
            throw new InvalidOperationException(
                "TimeMachine has not booted: call Boot() first, and deal with a cartridge crash in Init "
                + "before advancing.");
        }
    }
}

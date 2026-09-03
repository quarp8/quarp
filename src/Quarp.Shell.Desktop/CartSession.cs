using System.Buffers.Binary;
using System.Diagnostics;
using Quarp.Api;
using Quarp.CartKit;
using Quarp.Core;
using Quarp.Core.Audio;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One running cartridge inside the shell: load, tick, time control, hot reload, crash
/// handling, replay files and save.dat persistence.
///
/// <para>The session owns a <see cref="TimeMachine"/>, not a bare console. That single change
/// is what turns M1's restart-on-reload into M2's <b>continuation</b>: the input log is the
/// session's memory, so an edit mid-game replays that log against the new code and the player
/// lands in the same place with the new behaviour (ARCHITECTURE §4). The same log is what
/// makes pause, single-step, rewind and replay files possible without a line of cartridge
/// support.</para>
///
/// <para>The console instance never changes for the life of the session — rebuilds included —
/// so <see cref="Framebuffer"/> is safe to cache. Crashes pause the simulation, print the
/// stack trace (embedded PDB gives line numbers) and stamp a banner into the frame; a
/// successful reload resumes. Compile errors during reload keep the previous cartridge
/// running, and so does a transient file lock — that one is retried instead of dropped.</para>
/// </summary>
public sealed class CartSession : IDisposable
{
    private const int AutoSaveIntervalMs = 1000;

    /// <summary>Wait before retrying a reload that hit a transient file lock — one debounce window.</summary>
    private const int ReloadRetryIntervalMs = CartWatcher.DebounceMilliseconds;

    /// <summary>
    /// Past this, a resimulation stops being a hiccup and the shell owes the player a
    /// progress indicator instead of a frozen window (ARCHITECTURE §4).
    /// </summary>
    private const int ProgressAfterMs = 2000;

    /// <summary>
    /// Past this, a continuation reload is abandoned and the session restarts. Waiting ten
    /// seconds after every Ctrl+S is worse than starting over (ARCHITECTURE §4).
    /// </summary>
    private const int RebuildTimeoutMs = 10_000;

    /// <summary>Ticks between progress callbacks — also how often the timeout clock is checked.</summary>
    private const int ProgressIntervalTicks = 512;

    /// <summary>
    /// How much of one frame the pause menu's scrubber may spend travelling in time (M9 stage
    /// 5a). Eight milliseconds of a sixteen-millisecond frame: enough to be felt as movement,
    /// with the other half left for everything a frame still has to do. See
    /// <see cref="ScrubTo"/> for why a budget is the whole mechanism.
    /// </summary>
    private const double ScrubBudgetMs = 8.0;

    /// <summary>
    /// What a resimulated tick is assumed to cost before one has been timed. Deliberately about
    /// twice the measured cost of a real cartridge (<c>quarp bench carts/breakout</c>: 560 000
    /// resimulated ticks a second, 1.8 microseconds each), because the first guess is the one
    /// guess nobody can correct: too pessimistic delays the first commit by a frame or two, too
    /// optimistic spends a hundred milliseconds finding out.
    /// </summary>
    private const double SeedMsPerScrubTick = 0.004;

    /// <summary>Shortest travel worth timing — below this the stopwatch measures itself.</summary>
    private const int ScrubSampleTicks = 64;

    /// <summary>How fast the cost estimate is allowed to forget an expensive measurement.</summary>
    private const double ScrubCostDecay = 0.9;

    private readonly string _cartPath;
    private readonly string _savePath;
    private readonly string _replayFolder;
    private readonly CartWatcher? _watcher;
    private readonly Action<int, int> _progressCallback;
    private readonly Stopwatch _operationClock = new();

    private readonly TimeMachine _machine;
    private CartHost _host;
    private byte[] _identity;

    /// <summary>
    /// The assets the live console booted from, kept because a playback machine needs the
    /// same ones and the console does not hand its boot image back out.
    /// </summary>
    private byte[] _gfx;
    private byte[] _map;
    private byte[] _flags;
    private byte[] _sfx;
    private byte[] _music;

    /// <summary>The cartridge's data banks (ADR-035), kept for the same reason as the audio
    /// banks above: a playback machine needs the same ones the live session was given.</summary>
    private IReadOnlyList<byte[]> _dataBanks;

    /// <summary>Non-null while a saved replay is being watched; it shadows the live session.</summary>
    private TimeMachine? _playback;
    private string? _playbackName;

    /// <summary>Where the live session stood when playback took over, so it can be put back.</summary>
    private int _liveTick;

    private bool _crashed;
    private bool _paused;

    /// <summary>Armed <c>--break-at</c> target, cleared the moment it fires. Null = run freely.</summary>
    private int? _breakAt;

    /// <summary>True while the session is standing on the tick a <c>--break-at</c> stopped it at.</summary>
    private bool _atBreak;

    /// <summary>The tick the break stopped on, and the number the author asked for — for the overlay.</summary>
    private int _breakStoppedAtTick = -1;
    private int _breakRequested = -1;

    private int _speedIndex = TimeSpeed.NormalIndex;
    private long _lastSaveTick;
    private long _reloadRetryTick; // 0 = no retry armed.

    // Progress plumbing for a blocking resimulation.
    private string _operationLabel = "";
    private long _operationDeadlineMs;
    private bool _operationAnnounced;

    /// <summary>What one resimulated tick of THIS cartridge costs, as last measured — see <see cref="ScrubTo"/>.</summary>
    private double _msPerScrubTick = SeedMsPerScrubTick;

    /// <summary>
    /// The tick a scrub is travelling to while the session has not arrived yet, or null when the
    /// two agree. The whole of <see cref="ShownTick"/> — see that property for why the number the
    /// console prints has to have exactly one owner.
    /// </summary>
    private int? _scrubAim;

    /// <summary>
    /// Where the session stood when <see cref="_scrubAim"/> was last published. An aim is only
    /// believed while the session is still standing there, which is what makes the aim expire by
    /// itself: <c>,</c>, <c>.</c>, <c>Home</c>, held Backspace, a live tick and a reload all move
    /// the tick, and every one of them therefore drops a stale destination without having to
    /// remember to. A clearing call in every one of those roads is exactly the bookkeeping that
    /// gets forgotten in the seventh.
    /// </summary>
    private int _scrubAimFrom;

    private string? _status;
    private int _statusPercent = -1;
    private string? _flash;
    private long _flashUntilMs;

    /// <summary>
    /// Everything <see cref="RefreshStatus"/> reads, so an unchanged frame formats nothing. The
    /// tick in it is <see cref="ShownTick"/> and not <see cref="Tick"/>, because that is the one
    /// the line prints: a deferred scrub moves the printed number while the session stands still,
    /// and a signature built on the standing tick would cache the line through the whole travel.
    /// </summary>
    private (bool Crashed, bool Paused, int Speed, int Tick, string? Replay, string? Flash, bool InPlayback, bool AtBreak)
        _statusSignature = (false, false, -1, -1, null, null, false, false);

    /// <summary>The console this session runs on; playback rebuilds its machine on the same one.</summary>
    private readonly ConsoleProfile _profile;

    private CartSession(
        string cartPath,
        string savePath,
        CartWatcher? watcher,
        TimeMachine machine,
        CartHost host,
        CartData data,
        byte[] identity,
        ConsoleProfile profile)
    {
        _profile = profile;
        _cartPath = cartPath;
        _savePath = savePath;
        _replayFolder = Path.Combine(cartPath, "replays");
        _watcher = watcher;
        _machine = machine;
        _host = host;
        _identity = identity;
        _gfx = data.Gfx;
        _map = data.Map;
        _flags = data.Flags;
        _sfx = data.Sfx;
        _music = data.Music;
        _dataBanks = data.DataBanks;
        _progressCallback = OnResimulationProgress;   // cached once: it runs inside the seek loop
        machine.Progress = _progressCallback;
        machine.ProgressInterval = ProgressIntervalTicks;
    }

    /// <summary>The cart's display name from its manifest.</summary>
    public string Name { get; private set; } = "";

    /// <summary>
    /// The folder (or <c>.quarp8</c> file) this session was started from, absolute. Published
    /// since M9 stage 5, when leaving a running game for an editor tab stopped destroying the
    /// session: the mode machine has to open the editor banks of <b>the cart that is running</b>,
    /// and until then nobody outside this class ever had to name it — the library's entry did.
    /// A read-only path (a sealed <c>.quarp8</c>) is still not editable, which is why the machine
    /// asks <see cref="Directory.Exists(string)"/> about this before opening anything.
    /// </summary>
    public string CartPath => _cartPath;

    /// <summary>
    /// Weak reference to the current host's collectible AssemblyLoadContext (hot reload swaps
    /// hosts, so read it fresh). This is the observable half of the M9 lifecycle claim: after
    /// <see cref="Dispose"/> and the drop of the last session reference, the target must die —
    /// which the mode-transition tests assert, because "Unload was called" proves a request
    /// and only a dead weak reference proves the cart's assemblies actually left the process.
    /// </summary>
    public WeakReference LoadContextWeakReference => _host.LoadContextWeakReference;

    /// <summary>
    /// The framebuffer to present. Stable for the whole session — the TimeMachine keeps one
    /// console across rebuilds — except while a saved replay is being watched, which runs on
    /// its own console.
    /// </summary>
    public Framebuffer Framebuffer => Active.Framebuffer;

    /// <summary>
    /// The output state that framebuffer is shown through (<see cref="DisplayPalette"/>): it
    /// belongs to the same console and is read on the same road to the window, so a replay being
    /// watched is coloured by its own console's state and not by the live session's.
    /// </summary>
    public DisplayPalette Display => Active.Display;

    /// <summary>The machine the time controls act on: the replay being watched, or the live session.</summary>
    private TimeMachine Active => _playback ?? _machine;

    /// <summary>Where the console stands, in ticks since Init.</summary>
    public int Tick => Active.Tick;

    /// <summary>
    /// The tick the console is <b>showing the author</b>: the scrub's destination while a move is
    /// still in flight, and <see cref="Tick"/> the rest of the time.
    ///
    /// <para><b>Why this exists at all.</b> A backward scrub is deliberately allowed to lag its
    /// aim (<see cref="ScrubTo"/>), so for up to a few seconds "where the session stands" and
    /// "the tick the author is moving" are two different numbers. Both were on screen at once —
    /// the pause menu printed the aim between its arrows, this session's own status line printed
    /// <see cref="Tick"/> — and on a long session they disagreed for seconds at a time. One number
    /// with two printers is one number with two owners, so the answer lives here, and both the
    /// status line below and <see cref="ShellModeMachine.MenuTick"/> read it rather than deciding
    /// it. The aim is set by the only method that can start such a move and is cleared by the very
    /// act of moving — see <see cref="SeekTo"/> and <see cref="JumpTicks"/> — so there is no
    /// bookkeeping to forget except the one case the shell owns
    /// (<see cref="ForgetScrubAim"/>).</para>
    /// </summary>
    public int ShownTick => _scrubAim is int aim && Tick == _scrubAimFrom ? aim : Tick;

    /// <summary>The selected rung of the speed ladder.</summary>
    public TimeSpeed Speed => TimeSpeed.At(_speedIndex);

    /// <summary>True when the simulation is not advancing on its own: paused, crashed, or rewinding.</summary>
    public bool IsPaused => _paused || _crashed;

    /// <summary>
    /// True while a saved <c>.qrpr</c> is on screen instead of the live session (F8). Published
    /// in M9 stage 5 so a headless test can tell "the replay was refused" from "the replay
    /// played" without reading a status string — the two look the same from every other angle.
    /// </summary>
    public bool IsPlayingReplay => _playback is not null;

    /// <summary>
    /// <c>quarp run &lt;cart&gt; --break-at N</c>: the tick whose <c>Update</c> the session must
    /// stop <b>before</b>, or null to run freely. Set once, before the first
    /// <see cref="Update(int, InputState, bool)"/>; it disarms itself when it fires.
    ///
    /// <para><b>Which instant this names.</b> The console counts a tick before running it
    /// (<c>Ticks</c> is already N inside tick N's <c>Update</c>), so "before tick N's Update" is
    /// the state after N-1 ticks and <see cref="Tick"/> reads N-1 at the break. That is
    /// deliberate: it is the same instant a conditional breakpoint <c>Ticks == N</c> stops at,
    /// so the two ways of reaching a moment agree instead of being one tick apart. Pressing
    /// <c>.</c> runs tick N and shows what it drew.</para>
    ///
    /// <para><b>N = 0.</b> Tick 0 is <c>Init</c>, which has no <c>Update</c> at all (API-8 §2), so
    /// <c>--break-at 0</c> and <c>--break-at 1</c> name the same instant: paused at tick 0, Init
    /// done, no <c>Update</c> run yet. Negative values are clamped to 0; the CLI rejects them
    /// earlier anyway.</para>
    ///
    /// <para>Nothing about this reaches the simulation. It only decides how many ticks a frame is
    /// allowed to spend, which is the same knob pause and the speed ladder turn — so a run that
    /// breaks at N lands on exactly the state a run that did not break passed through.</para>
    /// </summary>
    public int? BreakAt
    {
        get => _breakAt;
        set => _breakAt = value is int tick ? Math.Max(0, tick) : (int?)null;
    }

    /// <summary>True while the session sits on the tick <see cref="BreakAt"/> stopped it at.</summary>
    public bool IsAtBreak => _atBreak;

    /// <summary>
    /// What the shell's overlay should say, or null for nothing. A cached string, rebuilt only
    /// when the state behind it changes, so presenting it every frame allocates nothing.
    /// </summary>
    public string? Status => _status;

    /// <summary>Progress for the overlay's bar in 0..100, or -1 when there is nothing to show.</summary>
    public int StatusPercent => _statusPercent;

    /// <summary>
    /// Called by the shell while a blocking resimulation is running, so the window can repaint
    /// instead of freezing. The session cannot draw — it has no graphics device — so it hands
    /// back the same status it publishes normally and lets the shell present a frame.
    /// </summary>
    public Action? PresentFrame { get; set; }

    /// <summary>
    /// Called with the console's <see cref="AudioBlock"/> after each tick that moves the
    /// session forward in real time — live play, fast-forward, single-step and replay
    /// playback. The block is the console's own array and is overwritten by the next tick, so
    /// the sink must copy what it wants.
    ///
    /// <para>Deliberately <b>not</b> called during a rewind or any other resimulation. A seek
    /// re-runs the session from tick 0 and generates one block per tick of it; playing those
    /// would be minutes of sound in a few milliseconds, and only the last block belongs to the
    /// tick the player lands on. Rewinding is silent, which is also what a tape does when it
    /// is being wound rather than played.</para>
    ///
    /// <para>The sink cannot influence the simulation: it is handed a block that has already
    /// been rendered, and nothing it returns is read. That is the boundary that lets PCM into
    /// the golden master — the sound card must never be able to pull a tick.</para>
    /// </summary>
    public Action<AudioBlock>? AudioSink { get; set; }

    /// <summary>
    /// Loads, compiles and starts a cartridge from a folder or a .quarp8 file.
    /// Load and compile failures throw <see cref="CartLoadException"/> (the caller exits);
    /// a crash inside the cartridge's Init starts the session paused with the banner up,
    /// so the author can fix the code and hot-reload.
    /// </summary>
    /// <param name="profile">
    /// Which console the cartridge runs on. Defaults to <see cref="ConsoleProfile.Profile8"/>,
    /// 160x90 (ADR-021). It is threaded through rather than read from a static because a replay
    /// resimulated on a different console than it was recorded on would not be the same run —
    /// the profile is part of what determines every frame, so the choice has to be visible at
    /// the call that starts the session.
    /// </param>
    public static CartSession Start(string path, ConsoleProfile? profile = null)
    {
        ConsoleProfile consoleProfile = profile ?? ConsoleProfile.Profile8;
        string fullPath = Path.GetFullPath(path);
        // Watch before loading: the first compile is the cold one (seconds — Roslyn's own
        // JIT) and an edit saved while it runs must not be lost. The watcher's pending flag
        // is thread-safe and survives until the shell polls it on the first frame.
        CartWatcher? watcher = Directory.Exists(fullPath) ? new CartWatcher(fullPath) : null;
        try
        {
            CartData data = CartSource.Load(fullPath);
            CartCompileResult result = CartCompiler.Compile(data);
            PrintWarnings(result);
            if (!result.Success)
            {
                throw new CartLoadException(
                    "cartridge failed to compile:" + Environment.NewLine
                    + string.Join(Environment.NewLine, result.Diagnostics));
            }

            CartHost host = CartHost.Load(result.AssemblyBytes);
            string savePath = Directory.Exists(fullPath)
                ? Path.Combine(fullPath, "save.dat")
                : Path.ChangeExtension(fullPath, ".save.dat");

            // The three external inputs of the simulation, all captured before anything runs:
            // the cartridge identity, the RNG seed and the persistent snapshot (REPLAY-FORMAT
            // §2). The seed stays 0 for now — a per-session seed would be perfectly legal
            // (the shell may read a clock, the core may not) but it would make two runs of the
            // same cart diverge by default, and that is a decision for a milestone that has a
            // reason to want it, not a side effect of wiring up replays.
            byte[] identity = CartIdentity.Compute(data);
            int[] persistent = ReadSaveFile(savePath);
            var header = new ReplayHeader(identity, seed: 0, persistent);
            var machine = new TimeMachine(
                consoleProfile, host.Cartridge, header, new ReplayLog(),
                data.Gfx, data.Map, data.Flags, data.Sfx, data.Music, data.DataBanks);

            var session = new CartSession(fullPath, savePath, watcher, machine, host, data, identity, consoleProfile)
            {
                Name = data.Manifest.Name,
            };
            try
            {
                machine.Boot();
            }
            catch (Exception e)
            {
                // The constructor ran no cartridge code, so the framebuffer exists and the
                // banner is drawable even though Init never finished.
                session.Crash(e);
            }
            session.RefreshStatus();
            return session;
        }
        catch
        {
            watcher?.Dispose(); // Nobody owns it yet — do not leak the FileSystemWatcher.
            throw;
        }
    }

    // --- the frame ---

    /// <summary>
    /// One frame of shell time control, applied before any ticks run: pause, speed, stepping,
    /// seeking, replay files. Returns after acting; the caller then asks for ticks.
    /// </summary>
    public void ApplyCommands(in ShellCommands commands)
    {
        if (commands.TogglePause)
        {
            _paused = !_paused;
        }
        if (commands.Slower)
        {
            _speedIndex = TimeSpeed.ClampIndex(_speedIndex - 1);
        }
        if (commands.Faster)
        {
            _speedIndex = TimeSpeed.ClampIndex(_speedIndex + 1);
        }
        if (commands.SaveReplay)
        {
            SaveReplay();
        }
        if (commands.PlayReplay)
        {
            TogglePlayback();
        }
        if (commands.ToStart)
        {
            SeekTo(0);
            _paused = true;
        }
        if (commands.StepBack)
        {
            _paused = true;
            StepBack();
        }
        if (commands.StepForward)
        {
            _paused = true;
            StepForward();
        }
        RefreshStatus();
    }

    /// <summary>
    /// Runs the frame's ticks. <paramref name="rewinding"/> spends them going backwards
    /// (Backspace held) instead of forwards — same clock, same ladder, opposite direction.
    /// Also polls the watcher for a debounced hot reload, once per frame on this thread.
    /// </summary>
    public void Update(int ticks, InputState input, bool rewinding)
    {
        PollReload();

        if (rewinding)
        {
            Rewind(ticks);
        }
        else if (!IsPaused && ticks > 0)
        {
            // The break trims the frame's budget rather than watching for the tick to go past:
            // a frame at x8, or a test frame worth a thousand ticks, must land on the break and
            // not overshoot it by up to a frame's worth of simulation.
            int allowed = TicksUntilBreak(ticks);
            if (allowed > 0)
            {
                RunForward(allowed, input);
            }
        }
        FireBreakIfArrived();

        SaveIfDirty(force: false);
        RefreshStatus();
    }

    /// <summary>
    /// One poll of the cartridge folder for a settled edit, and the rebuild it asks for. Lifted
    /// out of <see cref="Update(int, InputState, bool)"/> in M9 stage 5 because it grew a
    /// <b>second</b> caller, and that second caller is the whole of the stage's save rule (Р2):
    /// while the shell stands on an editor tab the session runs no ticks at all, so nothing was
    /// polling this and a <c>Ctrl+S</c> in the code, sprite, map, sound or music screen reached
    /// the running cartridge never.
    ///
    /// <para><b>Why the watcher stays the one trigger and no editor calls a rebuild directly.</b>
    /// Every save in this shell ends in a file inside the cart folder, and
    /// <see cref="CartWatcher"/> is already the one owner of "the cart on disk changed". Wiring
    /// five routers to a sixth entry point would have made two answers to that question — and
    /// would still have missed the author's other editor, the one running outside this window,
    /// which is the case this poll has always covered. So the rule is stated once: the disk is
    /// the truth, and the rebuild happens when the disk settles.</para>
    ///
    /// <para>Costs nothing on a quiet frame: the watcher's flag is a lock and a comparison.</para>
    /// </summary>
    public void PollReload()
    {
        if (_watcher is null || (!_watcher.ConsumeReloadRequest() && !ReloadRetryDue()))
        {
            return;
        }
        try
        {
            TryReload();
        }
        catch (Exception e)
        {
            // A failed rebuild must never take the console down (M1: a bad reload keeps
            // the previous cartridge running). Report and wait for the next edit.
            Console.Error.WriteLine("[quarp] reload failed unexpectedly:");
            Console.Error.WriteLine(e.ToString());
            ReportKeepingOldCart();
        }
    }

    /// <summary>
    /// Stops the simulation without toggling it — the verb the shell needs when the author walks
    /// off the game tab (M9 stage 5, Р1: "иначе автор правит движущуюся мишень"). Distinct from
    /// <c>ShellCommands.TogglePause</c> on purpose: a toggle called on the way into an editor
    /// would <em>start</em> a paused game running the moment the author opened the sprite sheet.
    /// </summary>
    public void PauseForEditing()
    {
        _paused = true;
        RefreshStatus();
    }

    /// <summary>
    /// Lets the simulation run again — the pause menu's RESUME, and nothing else. A crashed
    /// session stays stopped whatever this says: <see cref="IsPaused"/> is the or of the two.
    /// </summary>
    public void Resume()
    {
        _paused = false;
        RefreshStatus();
    }

    /// <summary>
    /// Travels <paramref name="ticks"/> ticks (negative back, positive forward) and stands
    /// still there. The distance is <b>exact</b>: going back a named number and coming forward
    /// the same number lands on the frame that was on screen, which is M9 stage 5's own
    /// acceptance check.
    ///
    /// <para>Forward travel is two different things and both are here: as far as the recorded
    /// log reaches it is a replay of what the player already did (so a jump forward after a
    /// jump back is exactly reversible), and past the log's tip it is fresh simulation with no
    /// buttons held, which is what <c>.</c> does at the tip too — and what the scrubber's right
    /// arrow does past the tip (stage 5a, Р3).</para>
    /// </summary>
    public void JumpTicks(int ticks)
    {
        _paused = true;
        if (ticks == 0)
        {
            RefreshStatus();
            return;
        }
        TimeMachine machine = Active;
        if (ticks < 0)
        {
            SeekTo(Math.Max(0, machine.Tick + ticks));
            RefreshStatus();
            return;
        }
        if (_crashed && machine.Tick >= machine.Log.TickCount)
        {
            Flash("REWIND TO RECOVER");    // same rule as StepForward's — see its comment
            RefreshStatus();
            return;
        }
        int recorded = Math.Min(ticks, machine.Log.TickCount - machine.Tick);
        if (recorded > 0)
        {
            SeekTo(machine.Tick + recorded, "SEEK");
        }
        int fresh = ticks - Math.Max(0, recorded);
        if (fresh > 0 && _playback is null)
        {
            try
            {
                machine.Advance(fresh, default);
                _crashed = false;
                PublishAudio(machine);
            }
            catch (Exception e)
            {
                Crash(e);
            }
        }
        RefreshStatus();
    }

    /// <summary>
    /// One frame of the pause menu's scrubber (M9 stage 5a): travel toward
    /// <paramref name="target"/> with a <b>frame budget</b>, so a held arrow can aim anywhere in
    /// the session without the window turning into a slide show.
    ///
    /// <para><b>The trap this method exists to walk around.</b> Going backwards is a cold boot
    /// and a resimulation from tick 0 (ADR-006 — no snapshots in v1), so its price is the
    /// <b>destination</b>, not the distance: at tick 100 000, stepping back a single tick costs
    /// the same ~180 ms as travelling to tick 99 000, while travelling all the way to tick 0
    /// costs nothing at all but the boot. Committing the aim every frame would therefore freeze
    /// the window for eleven frames per frame of hold, exactly where the author asked to go far.
    /// The consequence is the rule below, and it is not a compromise: <b>a backward scrub that
    /// waits gets cheaper</b>, because the aim it will eventually commit is lower. So a backward
    /// move the frame cannot afford is not attempted at all; the aim goes on moving, and the
    /// move happens either when the destination has fallen inside the budget (which a long hold
    /// reaches within a fraction of a second of its own ramp) or on <paramref name="release"/>,
    /// which pays at most what one press of <c>,</c> already costs at that tick.</para>
    ///
    /// <para><b>Forward is the opposite shape and is handled the opposite way.</b> Its price
    /// <em>is</em> the distance, and partial progress is never thrown away, so a forward scrub
    /// travels as far as this frame's budget allows and continues on the next — which is also
    /// why it ignores <paramref name="release"/>: there is nothing to defer, only work to
    /// spread. Past the end of the recording that work is fresh simulation with no buttons held
    /// (Р3), the old AHEAD row's own behaviour, and it is <see cref="JumpTicks"/> that owns both
    /// halves of forward travel — this method only decides how much of it to buy this frame.</para>
    ///
    /// <para><b>The budget is measured, not assumed.</b> <see cref="_msPerScrubTick"/> starts at
    /// a deliberately pessimistic seed and is corrected by every travel long enough to time; a
    /// wrong estimate can only make the scrubber commit a little early or a little late, never
    /// land on a wrong tick.</para>
    /// </summary>
    /// <param name="target">The tick the author is aiming at — <see cref="TickScrubber.Target"/>.</param>
    /// <param name="release">
    /// True on the frames no arrow is held. A backward move waiting for a cheaper moment has run
    /// out of moments and is made now.
    /// </param>
    public void ScrubTo(int target, bool release)
    {
        _paused = true;
        target = Math.Max(0, target);
        ScrubToCore(target, release);
        // Published last, and from here rather than from each of ScrubToCore's three roads, so
        // the tick the console shows is decided once for every outcome alike — arrived, deferred
        // or refused. See ShownTick: this assignment is the whole of how the pause menu and the
        // status line come to be printing the same number.
        _scrubAim = target;
        _scrubAimFrom = Tick;
        RefreshStatus();
    }

    /// <summary>The travel itself; <see cref="ScrubTo"/> owns the aim and the status line around it.</summary>
    private void ScrubToCore(int target, bool release)
    {
        TimeMachine machine = Active;
        if (target == machine.Tick)
        {
            return;
        }
        if (target < machine.Tick)
        {
            if (!release && target * _msPerScrubTick > ScrubBudgetMs)
            {
                return;         // too dear this frame; the aim goes on falling and so does the price
            }
            long started = Stopwatch.GetTimestamp();
            SeekTo(target);
            // The work a backward seek did is the destination: it booted and resimulated to it.
            RecordScrubCost(target, started);
            return;
        }
        int step = Math.Min(target - machine.Tick, AffordableScrubTicks);
        if (step <= 0)
        {
            return;
        }
        int stood = machine.Tick;
        long forwardStarted = Stopwatch.GetTimestamp();
        JumpTicks(step);                // owns replay-inside-the-log and fresh-past-the-tip alike
        // What it cost is what it MOVED, never what it was asked for: JumpTicks answers some
        // requests by declining them (a crashed cartridge standing at the end of its log has no
        // forward to run — see its own guard — and so has a replay at the end of its film), and
        // those frames return in microseconds. Timing a declined move against the ticks it did
        // not travel prices a resimulated tick at nothing, and one second of a held arrow on a
        // crashed cartridge was enough to convince the budget below that every seek is free.
        RecordScrubCost(Tick - stood, forwardStarted);
    }

    /// <summary>
    /// Drops a scrub destination the session is never going to reach, so <see cref="ShownTick"/>
    /// goes back to the tick the console really stands on. The shell calls this wherever it puts
    /// the scrubber's aim back on the session (<see cref="ShellModeMachine.ScrubFrame"/> and its
    /// neighbours): the aim and the printed number are one fact, and a scrubber re-aimed while
    /// this session still advertised the old destination would be exactly the two-printer split
    /// <see cref="ShownTick"/> exists to end.
    /// </summary>
    public void ForgetScrubAim()
    {
        if (_scrubAim is null)
        {
            return;
        }
        _scrubAim = null;
        RefreshStatus();
    }

    /// <summary>How many ticks the recording holds — the far end of a forward scrub inside recorded history.</summary>
    public int LogTickCount => Active.Log.TickCount;

    /// <summary>How many ticks one frame of scrubbing may spend, at the cost last measured. At least one.</summary>
    private int AffordableScrubTicks =>
        (int)Math.Clamp(ScrubBudgetMs / _msPerScrubTick, 1.0, int.MaxValue);

    /// <summary>
    /// Folds one timed travel into the cost estimate. The estimate tracks the <b>worst</b>
    /// recent cost and relaxes slowly, because being wrong in the cheap direction is what puts a
    /// 180 ms seek on a 16 ms frame. Travels too short to time are not sampled: a one-tick move
    /// measured against a stopwatch whose resolution is comparable to it would poison the
    /// estimate with pure quantisation — and neither is a travel that never happened, which is
    /// the same guard doing the same job for zero (see the caller: <paramref name="ticks"/> is
    /// measured movement, not a request).
    /// </summary>
    private void RecordScrubCost(int ticks, long startedTimestamp)
    {
        if (ticks < ScrubSampleTicks)
        {
            return;
        }
        double elapsedMs =
            (Stopwatch.GetTimestamp() - startedTimestamp) * 1000.0 / Stopwatch.Frequency;
        _msPerScrubTick = Math.Max(elapsedMs / ticks, _msPerScrubTick * ScrubCostDecay);
    }

    /// <summary>
    /// The tick <see cref="BreakAt"/> stops the console on: one before the tick it names, because
    /// the console counts a tick before running it. Tick 0 is <c>Init</c>, so 0 and 1 collapse
    /// onto the same instant.
    /// </summary>
    private static int BreakStopTick(int breakAt) => Math.Max(0, breakAt - 1);

    /// <summary>
    /// How many of the frame's <paramref name="ticks"/> the live session may actually spend
    /// before it must stand still and wait at the break.
    /// </summary>
    private int TicksUntilBreak(int ticks)
    {
        if (_breakAt is not int breakAt || _playback is not null)
        {
            return ticks;   // No break armed, or a replay is on screen — the break is the live session's.
        }
        int remaining = BreakStopTick(breakAt) - _machine.Tick;
        return remaining <= 0 ? 0 : Math.Min(ticks, remaining);
    }

    /// <summary>
    /// Trips the pause once the live session has reached the break, disarms it (a break is a
    /// one-shot: pressing Space means "carry on", not "stop again in a frame") and says so on
    /// stdout, because the interesting number — which tick is about to run — is not something a
    /// six-character overlay can spell out.
    /// </summary>
    private void FireBreakIfArrived()
    {
        if (_breakAt is not int breakAt || _playback is not null || _crashed)
        {
            return;
        }
        if (_machine.Tick < BreakStopTick(breakAt))
        {
            return;
        }
        _breakAt = null;
        _atBreak = true;
        _breakRequested = breakAt;
        _breakStoppedAtTick = _machine.Tick;
        _paused = true;
        Console.WriteLine(
            $"[quarp] --break-at {breakAt}: paused before Update of tick {breakAt} — the console stands at "
            + $"tick {_machine.Tick}"
            + (breakAt <= 1 ? " (tick 0 is Init, which has no Update). " : ". ")
            + "Press . to run that tick, Space to resume, Backspace to rewind.");
    }

    /// <summary>Writes save.dat immediately if there are unsaved Dset changes.</summary>
    public void SaveNow() => SaveIfDirty(force: true);

    public void Dispose()
    {
        // Quitting while watching a replay must still save the live session's progress, and
        // SaveIfDirty deliberately refuses to write anything while playback is up.
        StopPlayback(restore: false, quiet: true);
        SaveIfDirty(force: true);
        _watcher?.Dispose();
        _host.Unload();
    }

    // --- moving through time ---

    private void RunForward(int ticks, InputState input)
    {
        TimeMachine machine = Active;
        try
        {
            if (_playback is not null)
            {
                // Watching a replay: step through the recorded input, never the live pad.
                // Running out of recording is the end of the film, not an error.
                for (int i = 0; i < ticks; i++)
                {
                    if (machine.ReplayForward(1) == 0)
                    {
                        _paused = true;
                        return;
                    }
                    PublishAudio(machine);
                }
                return;
            }
            // One polled snapshot feeds every tick of the frame. At x8 that means the same
            // buttons are recorded eight times — faithful to what the simulation actually
            // saw, which is the only thing a replay is allowed to claim.
            //
            // A tick at a time rather than Advance(ticks, input) so the audio sink sees every
            // block: the console keeps one, and a frame that ran two ticks would otherwise
            // lose one. The work is identical — Advance(n) is this loop with the counter
            // inside it.
            for (int i = 0; i < ticks; i++)
            {
                machine.Advance(1, input);
                PublishAudio(machine);
                if (input.MouseWheel != 0)
                {
                    // The wheel is a per-tick delta (ADR-030 п.6): the frame's notches belong
                    // to the frame's FIRST tick alone. Position and buttons rightly repeat
                    // across the batch — the hand really was there for all of it — but a
                    // repeated wheel delta would scroll eight lists at x8 for one flick.
                    input = input.WithMouseWheel(0);
                }
            }
        }
        catch (Exception e)
        {
            Crash(e);
        }
    }

    /// <summary>Hands the tick's block to the shell, if anything is listening.</summary>
    private void PublishAudio(TimeMachine machine) => AudioSink?.Invoke(machine.Console.AudioBlock);

    private void Rewind(int ticks)
    {
        if (ticks <= 0)
        {
            return;
        }
        TimeMachine machine = Active;
        int target = Math.Max(0, machine.Tick - ticks);
        if (target == machine.Tick)
        {
            return;
        }
        // Rewinding out of a crash is the point of rewinding out of a crash: the tick that
        // threw is behind us now, so the session is live again.
        SeekTo(target);
    }

    private void StepBack()
    {
        TimeMachine machine = Active;
        if (machine.Tick <= 0)
        {
            Flash("AT START");
            return;
        }
        SeekTo(machine.Tick - 1);
    }

    private void StepForward()
    {
        TimeMachine machine = Active;
        if (_crashed && machine.Tick >= machine.Log.TickCount)
        {
            // The console counts a tick before running it, so a crashed tick leaves the
            // cartridge half-updated. Stepping forward from there would run the next tick on
            // state no straight run could ever produce — the opposite of what this milestone
            // is for. Rewinding re-boots and resimulates, which is a state that is real.
            Flash("REWIND TO RECOVER");
            return;
        }
        try
        {
            if (machine.Tick < machine.Log.TickCount)
            {
                // There is recorded future ahead (we stepped back into it): replay it rather
                // than branching the timeline on a keypress.
                machine.ReplayForward(1);
                _crashed = false;
                PublishAudio(machine);
            }
            else if (_playback is null)
            {
                machine.Advance(1, default);
                PublishAudio(machine);
            }
            else
            {
                Flash("REPLAY END");
            }
        }
        catch (Exception e)
        {
            Crash(e);
        }
    }

    /// <summary>
    /// Resimulates to <paramref name="tick"/>. Long seeks report progress through the cached
    /// callback; a cartridge that crashes on the way is caught here, because a rewind through
    /// a crashing tick must land the player somewhere, not take the window down.
    /// </summary>
    /// <param name="label">
    /// What a long seek calls itself on the progress line. It is "REWIND" for every caller that
    /// goes backwards — which was all of them until the pause menu's forward jump — because a
    /// forward seek inside the recorded log announcing "rewind" would be a lie on the one line
    /// the author can see.
    /// </param>
    private void SeekTo(int tick, string label = "REWIND")
    {
        TimeMachine machine = Active;
        BeginOperation(label, timeoutMs: 0);
        try
        {
            machine.SeekTo(Math.Clamp(tick, 0, machine.Log.TickCount));
            _crashed = false;
        }
        catch (Exception e)
        {
            Crash(e);
        }
        finally
        {
            EndOperation();
        }
    }

    // --- hot reload, continuation mode (the milestone's headline feature) ---

    private void TryReload()
    {
        // A retry of a locked-file reload says nothing new: report the streak once, not
        // every 150 ms, so a file held open for a while does not flood the terminal.
        bool isRetry = _reloadRetryTick != 0;
        _reloadRetryTick = 0;
        if (!isRetry)
        {
            Console.WriteLine($"[quarp] change detected — rebuilding {_cartPath}");
        }
        CartData data;
        try
        {
            data = CartSource.Load(_cartPath);
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"[quarp] reload failed: {e.Message}");
            ReportKeepingOldCart();
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Transient on Windows: the editor still holds the file it is saving (sharing
            // violation), or an attribute flip made it briefly unreadable. Losing the edit
            // would be worse than a late reload, so keep the old cart and try again.
            if (!isRetry)
            {
                Console.Error.WriteLine(
                    $"[quarp] cartridge files are busy ({e.Message}) — previous cartridge keeps running, retrying.");
            }
            ArmReloadRetry();
            return;
        }

        CartCompileResult result = CartCompiler.Compile(data);
        PrintWarnings(result);
        if (!result.Success)
        {
            foreach (string diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }
            ReportKeepingOldCart();
            return;
        }

        CartHost newHost;
        try
        {
            newHost = CartHost.Load(result.AssemblyBytes);
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"[quarp] reload failed: {e.Message}");
            ReportKeepingOldCart();
            return;
        }

        // Watching a replay while editing is a real thing to do, but the reload belongs to
        // the live session — drop back to it so the player sees the result of their edit.
        // Rebuild below boots and resimulates the live log itself, so restoring here first
        // would do that work twice.
        //
        // The pause is the SHELL's to hold, not the reload's (M9 stage 5, Р1). An edit arriving
        // while the game stands still under the pause menu — a save from an external editor, which
        // is the ordinary way an author works — must not start it running: the author would be
        // reading a menu over a moving game. Two lines past here clear the flag for their own
        // reasons (StopPlayback, on its way out of a replay), so it is taken now and put back
        // straight after; the fall-back-to-restart branch below no longer clears it at all.
        bool paused = _paused;
        StopPlayback(restore: false, quiet: true);
        _paused = paused;
        SaveIfDirty(force: true);

        CartHost oldHost = _host;
        int landing = _machine.Tick;
        byte[] newIdentity = CartIdentity.Compute(data);

        BeginOperation("RELOADING", RebuildTimeoutMs);
        RebuildResult rebuild;
        try
        {
            rebuild = _machine.Rebuild(
                newHost.Cartridge, data.Gfx, data.Map, data.Flags, data.Sfx, data.Music, data.DataBanks);
        }
        finally
        {
            EndOperation();
        }

        if (rebuild.Success)
        {
            AdoptBuild(newHost, data, newIdentity);
            _crashed = false;
            oldHost.Unload();
            Console.WriteLine(
                $"[quarp] reload OK — continuing at tick {rebuild.Tick} ({FormatSeconds(rebuild.Tick)}), "
                + $"cart {CartIdentity.ToShortHex(newIdentity)}.");
            Flash($"RELOADED @{rebuild.Tick}");
            return;
        }

        // The new code could not survive the recorded past. Say which tick killed it — that
        // is the only clue the author has — then fall back to restart mode (ARCHITECTURE §4).
        bool timedOut = rebuild.Error is ResimulationTimeoutException;
        Console.Error.WriteLine(timedOut
            ? $"[quarp] new code needed more than {RebuildTimeoutMs / 1000} s to resimulate "
              + $"{landing} ticks — falling back to restart."
            : $"[quarp] new code crashed on tick {rebuild.Tick} while replaying the session "
              + $"({landing} ticks) — falling back to restart.");
        if (!timedOut && rebuild.Error is not null)
        {
            Console.Error.WriteLine(rebuild.Error.ToString());
        }

        AdoptBuild(newHost, data, newIdentity);
        oldHost.Unload();
        try
        {
            // Restart mode: a clean session on the new code, timeline dropped. The old input
            // log described a game the new code cannot play. The pause is left exactly as the
            // shell had it — a restart is the reload's answer to bad code, not the author's
            // answer to a menu (see the note above the StopPlayback call).
            _machine.Restart();
            _crashed = false;
            Flash(timedOut ? "RESTART (SLOW)" : "RESTART (CRASH)");
            Console.Error.WriteLine("[quarp] restarted on the new code.");
        }
        catch (Exception e)
        {
            // Even Init throws now. Stay paused with the banner; the next edit gets another go.
            Crash(e);
        }
    }

    /// <summary>
    /// Takes ownership of a newly compiled build, whichever way the rebuild went. The
    /// identity is stamped on the machine so that a replay saved from now on names the build
    /// that is actually running; it touches neither seed nor persistent snapshot, so
    /// resimulation is unaffected.
    /// </summary>
    private void AdoptBuild(CartHost newHost, CartData data, byte[] newIdentity)
    {
        _host = newHost;
        _machine.SetCartIdentity(newIdentity);
        _identity = newIdentity;
        _gfx = data.Gfx;
        _map = data.Map;
        _flags = data.Flags;
        _sfx = data.Sfx;
        _music = data.Music;
        _dataBanks = data.DataBanks;
        Name = data.Manifest.Name;
    }

    /// <summary>True once the wait after a transient reload failure has elapsed.</summary>
    private bool ReloadRetryDue() =>
        _reloadRetryTick != 0 && Environment.TickCount64 >= _reloadRetryTick;

    /// <summary>
    /// Re-arms the reload the watcher already consumed. A file lock that lasts a few
    /// milliseconds must not swallow the author's edit: the next debounce window retries.
    /// </summary>
    private void ArmReloadRetry() => _reloadRetryTick = Environment.TickCount64 + ReloadRetryIntervalMs;

    private void ReportKeepingOldCart()
    {
        Console.Error.WriteLine(_crashed
            ? "[quarp] still crashed — fix the code to reload."
            : "[quarp] previous cartridge keeps running.");
    }

    private static void PrintWarnings(CartCompileResult result)
    {
        foreach (string warning in result.Warnings)
        {
            Console.Error.WriteLine(warning);
        }
    }

    // --- long operations: progress and the timeout ---

    private void BeginOperation(string label, int timeoutMs)
    {
        _operationLabel = label;
        _operationAnnounced = false;
        _operationDeadlineMs = timeoutMs > 0 ? timeoutMs : 0;
        _operationClock.Restart();
    }

    private void EndOperation()
    {
        _operationClock.Reset();
        _operationLabel = "";
        if (_operationAnnounced)
        {
            Console.WriteLine();     // close the progress line the callback was overwriting
            _operationAnnounced = false;
        }
        _statusPercent = -1;
    }

    /// <summary>
    /// The <see cref="TimeMachine.Progress"/> callback, fired every
    /// <see cref="ProgressIntervalTicks"/> ticks of a long resimulation. Three jobs: keep the
    /// window painting, keep the terminal informed, and enforce the ten-second ceiling.
    ///
    /// <para>The ceiling is enforced by throwing: <c>Rebuild</c> catches everything and turns
    /// it into a failed <see cref="RebuildResult"/>, which is exactly the "fall back to
    /// restart" path the work order asks for. There is no other way to stop a resimulation
    /// from outside, and inventing a cancellation token would have meant editing the core,
    /// which belongs to another team this milestone.</para>
    /// </summary>
    private void OnResimulationProgress(int reached, int target)
    {
        long elapsed = _operationClock.ElapsedMilliseconds;
        if (_operationDeadlineMs > 0 && elapsed > _operationDeadlineMs)
        {
            throw new ResimulationTimeoutException(reached, target, elapsed);
        }
        if (elapsed < ProgressAfterMs)
        {
            return;     // A rewind nobody can feel does not deserve a progress bar.
        }

        int percent = target > 0 ? (int)((long)reached * 100 / target) : 100;
        _status = $"{_operationLabel} {percent}%";
        _statusPercent = percent;
        // Painting from inside the callback keeps the window alive without moving the
        // simulation onto another thread — the console is not thread-safe, and a race on the
        // framebuffer would be a far worse bug than a progress bar that updates coarsely.
        PresentFrame?.Invoke();

        Console.Write($"\r[quarp] {_operationLabel.ToLowerInvariant()} {reached}/{target} ticks ({percent}%)   ");
        _operationAnnounced = true;
    }

    // --- replay files (F5 / F8) ---

    private void SaveReplay()
    {
        TimeMachine machine = Active;
        try
        {
            Directory.CreateDirectory(_replayFolder);
            // Sortable, collision-free, and readable at a glance. The shell may look at a
            // clock; the core may not, which is why this lives here and not in ReplayLog.
            string name = $"{DateTime.Now:yyyyMMdd-HHmmss}.qrpr";
            string path = Path.Combine(_replayFolder, name);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                machine.Log.WriteTo(stream, machine.Header);
            }
            Console.WriteLine(
                $"[quarp] replay saved: {path} ({machine.Log.TickCount} ticks, {machine.Log.ByteLength} bytes)");
            Flash("REPLAY SAVED");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[quarp] could not save the replay: {e.Message}");
            Flash("SAVE FAILED");
        }
    }

    private void TogglePlayback()
    {
        if (_playback is not null)
        {
            StopPlayback(restore: true, quiet: false);
            return;
        }

        string? file = NewestReplay();
        if (file is null)
        {
            Console.Error.WriteLine($"[quarp] no replay to play — press F5 first (looked in {_replayFolder}).");
            Flash("NO REPLAY");
            return;
        }

        try
        {
            ReplayLog log;
            ReplayHeader header;
            using (var stream = File.OpenRead(file))
            {
                log = ReplayLog.ReadFrom(stream, out header);
            }
            if (header.HasIdentity && !header.IdentityMatches(_identity))
            {
                // M9 stage 5 (work order Р8) turned this from a warning into a refusal, and it
                // is a deliberate divergence worth reading twice.
                //
                // REPLAY-FORMAT §5 says a mismatch is "предупреждение, а не отказ", and the
                // reason it gives is the continuation mode: an input log applied to NEW code is
                // the whole point of ADR-007, and refusing there would kill the feature. That
                // argument is about TimeMachine.Rebuild, which is untouched — it still applies
                // the live log to the new build without a word. It is also about the CLI's
                // `quarp replay play`, which is untouched too: that command has a terminal to
                // print both hashes into and a human who typed the file's name on purpose.
                //
                // What this branch is, is F8 in a WINDOW. There is no terminal in front of the
                // author, so the old warning went to a stderr nobody was reading, and after
                // this stage editing a cart mid-session is the ordinary thing to do — which
                // means the newest .qrpr beside the cart is routinely a recording of a
                // cartridge that no longer exists. Playing it "roughly" produces a run that
                // diverges silently and looks like a bug in the game. So: refuse, say so on
                // the one line the window has, and leave the live session exactly as it was.
                Console.Error.WriteLine(
                    $"[quarp] {Path.GetFileName(file)} was recorded against cart "
                    + $"{CartIdentity.ToShortHex(header.CartIdentity)}, this build is "
                    + $"{CartIdentity.ToShortHex(_identity)} — refusing to play it. "
                    + "Play it with `quarp replay play` if you want it anyway.");
                Flash("STALE REPLAY");
                RefreshStatus();
                return;
            }

            // The playback machine gets its own console but shares the one Cartridge instance
            // this session has. Booting it re-attaches that instance to the playback console
            // and re-runs Init, which is why leaving playback has to put the live session back
            // together rather than simply dropping the reference — see StopPlayback.
            var playback = new TimeMachine(
                _profile, _host.Cartridge, header, log, _gfx, _map, _flags, _sfx, _music, _dataBanks)
            {
                Progress = _progressCallback,
                ProgressInterval = ProgressIntervalTicks,
            };
            _liveTick = _machine.Tick;
            playback.Boot();

            _playback = playback;
            _playbackName = Path.GetFileName(file);
            _paused = false;
            _crashed = false;
            Console.WriteLine($"[quarp] playing {_playbackName} — {log.TickCount} ticks. F8 returns to the game.");
            Flash("PLAYING");
        }
        catch (ReplayFormatException e)
        {
            Console.Error.WriteLine($"[quarp] {Path.GetFileName(file)}: {e.Message}");
            Flash("BAD REPLAY");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[quarp] could not read {file}: {e.Message}");
            Flash("BAD REPLAY");
        }
        catch (Exception e)
        {
            // The cartridge crashed during the replay's Init.
            Console.Error.WriteLine("[quarp] cartridge crashed while starting the replay:");
            Console.Error.WriteLine(e.ToString());
            _playback = null;
            Flash("REPLAY CRASH");
        }
    }

    /// <summary>
    /// Leaves playback. <paramref name="restore"/> puts the live session back on its feet,
    /// which is more than dropping a reference: the two machines share one
    /// <see cref="Cartridge"/> instance, and booting the playback machine pointed that
    /// instance at the playback console and re-ran its Init. Re-booting the live machine and
    /// resimulating its own log re-attaches the cartridge and rebuilds the state that belongs
    /// to it. The cost is exactly one rewind, which is what the shell pays for a rewind
    /// anyway.
    ///
    /// <para>A caller that is about to <see cref="TimeMachine.Rebuild(Cartridge)"/> passes
    /// false: that does its own boot and resimulation from the same log, so restoring first
    /// would simply do the work twice.</para>
    /// </summary>
    private void StopPlayback(bool restore, bool quiet)
    {
        if (_playback is null)
        {
            return;
        }
        _playback = null;
        _playbackName = null;
        _paused = false;
        _crashed = false;

        if (restore)
        {
            BeginOperation("RESTORING", timeoutMs: 0);
            try
            {
                _machine.Boot();
                _machine.SeekTo(Math.Min(_liveTick, _machine.Log.TickCount));
            }
            catch (Exception e)
            {
                Crash(e);
            }
            finally
            {
                EndOperation();
            }
        }

        if (!quiet)
        {
            Console.WriteLine($"[quarp] back to the live session at tick {_machine.Tick}.");
            Flash("LIVE");
        }
    }

    /// <summary>The most recently written .qrpr next to the cartridge, or null when there is none.</summary>
    private string? NewestReplay()
    {
        if (!Directory.Exists(_replayFolder))
        {
            return null;
        }
        string? newest = null;
        DateTime newestTime = DateTime.MinValue;
        foreach (string file in Directory.GetFiles(_replayFolder, "*.qrpr"))
        {
            DateTime written = File.GetLastWriteTimeUtc(file);
            if (newest is null || written > newestTime)
            {
                newest = file;
                newestTime = written;
            }
        }
        return newest;
    }

    // --- crash handling ---

    private void Crash(Exception exception)
    {
        _crashed = true;
        Console.Error.WriteLine("[quarp] cartridge crashed:");
        Console.Error.WriteLine(exception.ToString());
        Console.Error.WriteLine(
            "[quarp] simulation paused — edit the code to reload, or hold Backspace to rewind past it.");
        DrawCrashBanner();
    }

    /// <summary>
    /// Stamps the banner over the last frame via the console's own drawing API. Laid out from
    /// the console's own width and height rather than from a screen size written here: this is
    /// the one frame that has to stay readable when something has already gone wrong, and a
    /// banner sized for a different console is either a stripe that stops two thirds of the way
    /// across or text hanging off the edge. Centering asks <see cref="SystemFont"/> for the cell
    /// width instead of restating it — <c>Print</c> advances by exactly that.
    /// </summary>
    private void DrawCrashBanner()
    {
        IConsoleApi c = Active.Console;
        // Reset draw state the cart may have left behind so the banner is always legible.
        c.Camera();
        c.Clip();
        c.Pal();

        const int BannerHeight = 24;        // two lines of text with air above, below and between
        int top = (c.ScreenHeight - BannerHeight) / 2;
        c.RectFill(0, top, c.ScreenWidth, BannerHeight, 10);
        c.Rect(0, top, c.ScreenWidth, BannerHeight, 3);
        PrintCentered(c, "CRASHED - SEE TERMINAL", top + 5);
        PrintCentered(c, "EDIT CODE TO RELOAD", top + 14);
    }

    private static void PrintCentered(IConsoleApi c, string text, int y) =>
        c.Print(text, (c.ScreenWidth - (text.Length * SystemFont.CellWidth)) / 2, y, 3);

    // --- status line for the shell overlay ---

    private void Flash(string message)
    {
        _flash = message;
        _flashUntilMs = Environment.TickCount64 + 1500;
    }

    /// <summary>
    /// Rebuilds the overlay string, but only when something behind it actually changed. The
    /// shell asks for the status every frame, and formatting one there would be a fresh
    /// string sixty times a second for a line that changes when a key is pressed.
    /// </summary>
    private void RefreshStatus()
    {
        if (_operationClock.IsRunning)
        {
            return;     // The progress callback owns the status while a seek is running.
        }

        string? flash = _flash;
        if (flash is not null && Environment.TickCount64 > _flashUntilMs)
        {
            _flash = null;
            flash = null;
        }

        // Standing at the break lasts exactly as long as the pause it caused and the tick it
        // stopped on: resume, step, rewind or Home and the overlay goes back to saying PAUSE.
        if (_atBreak && (!_paused || Tick != _breakStoppedAtTick || _playback is not null))
        {
            _atBreak = false;
        }

        // Cheap comparison first: everything the line is built from, packed into a value.
        // Only a real change pays for the formatting.
        var signature = (_crashed, _paused, _speedIndex, ShownTick, _playbackName, flash, _playback is not null, _atBreak);
        if (signature == _statusSignature)
        {
            return;
        }
        _statusSignature = signature;

        string? next;
        if (_crashed)
        {
            next = "CRASHED";
        }
        else if (_playback is not null)
        {
            next = $"REPLAY {_playbackName} {Tick}/{_playback.Log.TickCount}"
                + (_paused ? " PAUSE" : Speed.IsNormal ? "" : " " + Speed.Label);
        }
        else if (_atBreak)
        {
            // The number the author typed, not the tick the console stands on: the whole point
            // of the flag is "the tick I asked about has not run yet".
            next = $"BREAK @{_breakRequested}";
        }
        else if (_paused)
        {
            // ShownTick and not Tick: while a backward scrub waits for a frame it fits in, the
            // tick the author is moving is the aim, and the pause menu two rows up is printing
            // that same aim. One number, one owner — see ShownTick.
            next = $"PAUSE {ShownTick}";
        }
        else if (!Speed.IsNormal)
        {
            next = (_speedIndex < TimeSpeed.NormalIndex ? "<< " : ">> ") + Speed.Label;
        }
        else
        {
            next = null;
        }

        if (flash is not null)
        {
            next = next is null ? flash : next + "  " + flash;
        }
        if (!string.Equals(next, _status, StringComparison.Ordinal))
        {
            _status = next;
        }
        _statusPercent = -1;
    }

    private static string FormatSeconds(int ticks) =>
        $"{ticks / TickAccumulator.TicksPerSecond}.{ticks % TickAccumulator.TicksPerSecond * 100 / TickAccumulator.TicksPerSecond:D2}s";

    // --- persistence: save.dat = 64 x int32 little-endian raw Fix (M1 work order) ---

    private static int[] ReadSaveFile(string savePath)
    {
        var slots = new int[VirtualConsole.PersistentSlots];
        if (!File.Exists(savePath))
        {
            return slots;
        }
        try
        {
            byte[] bytes = File.ReadAllBytes(savePath);
            int count = Math.Min(bytes.Length / 4, slots.Length);
            for (int i = 0; i < count; i++)
            {
                slots[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[quarp] could not read {savePath}: {e.Message} — starting with empty save data.");
        }
        return slots;
    }

    private void SaveIfDirty(bool force)
    {
        if (_playback is not null)
        {
            // Watching a replay must not touch the player's saves: during playback Dset
            // writes to a copy (SPEC-8 §7 rule 7, API-8 §8). The copy is the playback
            // console's own persistent array — all the shell has to do is never flush it.
            return;
        }
        if (_operationClock.IsRunning)
        {
            // Mid-seek or mid-rebuild the persistent array is an intermediate state of a
            // resimulation, not a save the player made. Writing it would let a rewind
            // scribble half-replayed high scores onto the disk.
            return;
        }
        if (!_machine.Console.PersistentDirty)
        {
            return;
        }
        long now = Environment.TickCount64;
        if (!force && now - _lastSaveTick < AutoSaveIntervalMs)
        {
            return;
        }

        var slots = new int[VirtualConsole.PersistentSlots];
        _machine.Console.CopyPersistentTo(slots);
        byte[] bytes = new byte[slots.Length * 4];
        for (int i = 0; i < slots.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), slots[i]);
        }
        try
        {
            File.WriteAllBytes(_savePath, bytes);
            _machine.Console.PersistentDirty = false;
            _lastSaveTick = now;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked or read-only save.dat is not worth crashing the console over.
            // Keep the dirty flag: the next autosave window retries.
            Console.Error.WriteLine($"[quarp] could not write {_savePath}: {e.Message}");
            _lastSaveTick = now;
        }
    }
}

/// <summary>
/// Thrown out of the progress callback to abandon a resimulation that has outstayed its
/// welcome. <see cref="TimeMachine.Rebuild(Cartridge)"/> catches every exception and reports
/// it as a failed <see cref="RebuildResult"/>, so this is how the shell converts "too slow"
/// into the restart fallback without the core needing to know about clocks.
/// </summary>
public sealed class ResimulationTimeoutException : Exception
{
    public ResimulationTimeoutException(int reached, int target, long elapsedMilliseconds)
        : base($"Resimulation reached tick {reached} of {target} in {elapsedMilliseconds} ms and was abandoned.")
    {
        Reached = reached;
        Target = target;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public int Reached { get; }
    public int Target { get; }
    public long ElapsedMilliseconds { get; }
}

using System.Diagnostics;
using System.Globalization;
using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Cli;

/// <summary>
/// The headless half of the time machine: <c>quarp replay record</c>,
/// <c>quarp replay play</c> and <c>quarp bench</c> (M2 work order, "Интеграция (C)").
///
/// <para>These three exist so the milestone can be <em>proved</em> rather than demonstrated.
/// CI records a replay on windows-x64 and plays it on linux-arm64, and the framebuffer hashes
/// have to agree; that is the M2 criterion word for word (REPLAY-FORMAT §6).</para>
///
/// <para><b>Deliberately identical to <c>quarp sim</c>'s starting conditions</b>: seed 0, all
/// 64 persistent slots zero, and <c>save.dat</c> neither read nor written. A headless hash
/// must depend on the cartridge alone, never on whatever this machine happens to have saved —
/// otherwise the CI comparison would be measuring the runner's disk.</para>
/// </summary>
public static class ReplayCommands
{
    /// <summary>Ticks in a 30-minute session — the rewind cost ARCHITECTURE §4 asks to be measured.</summary>
    private const int HalfHourTicks = 30 * 60 * 60;

    private const string RecordUsage =
        "usage: quarp replay record <cart> -o <file>.qrpr --ticks N [--input <script>]\n"
        + "                          [--input-file <file>] [--every N]\n"
        + "  --input is a comma-separated list of tick:buttons, each setting what player 0\n"
        + "  holds from that tick until the next entry. Buttons are L R U D O X S (Start),\n"
        + "  and an empty list releases everything. Turns in most carts are edge-triggered\n"
        + "  (Btnp), so a tap is two entries: \"60:D,61:\".\n"
        + "  Example: --input \"60:D,61:,120:L,121:\"\n"
        + "  --input-file reads the same grammar from a file, where newlines also separate\n"
        + "  entries and '#' starts a comment. A track that keeps a cartridge alive for\n"
        + "  thousands of ticks runs to hundreds of entries and does not belong on a\n"
        + "  command line -- carts/snake/replays/golden.input is one.\n"
        + "  --every N additionally prints a 'tick <n> <frame> <audio>' checkpoint\n"
        + "  line every N\n"
        + "  ticks; the bare final hash is still the last line of stdout.";

    /// <summary>The seed and persistent snapshot every headless run starts from.</summary>
    private static ReplayHeader HeadlessHeader(byte[] identity) =>
        new(identity, seed: 0, ReadOnlySpan<int>.Empty);

    // --- quarp replay record <cart> -o file.qrpr --ticks N ---

    public static int Record(string[] args)
    {
        string? cartPath = null;
        string? outPath = null;
        string? script = null;
        string? scriptFile = null;
        int ticks = 600;
        int every = 0;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--input" when i + 1 < args.Length:
                    script = args[++i];
                    break;
                case "--input-file" when i + 1 < args.Length:
                    scriptFile = args[++i];
                    break;
                case "--every" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedEvery)
                    && parsedEvery > 0:
                    every = parsedEvery;
                    i++;
                    break;
                case "--ticks" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    && parsed >= 0:
                    ticks = parsed;
                    i++;
                    break;
                default:
                    if (cartPath is null && !args[i].StartsWith('-'))
                    {
                        cartPath = args[i];
                        break;
                    }
                    Console.Error.WriteLine($"quarp replay record: unknown argument '{args[i]}'");
                    Console.Error.WriteLine(RecordUsage);
                    return 1;
            }
        }
        if (cartPath is null)
        {
            Console.Error.WriteLine(RecordUsage);
            return 1;
        }

        InputScript inputs;
        try
        {
            inputs = LoadScript(script, scriptFile);
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine($"quarp replay record: {e.Message}");
            Console.Error.WriteLine(RecordUsage);
            return 1;
        }
        // Default next to the cart, which is where `quarp run`'s F5 puts its recordings too.
        outPath ??= Path.Combine(
            Path.GetFullPath(cartPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            "replays", "recorded.qrpr");

        return Run(() =>
        {
            using Session session = Session.Open(cartPath);
            // With no script this is empty input for the whole run, and the RLE encoder
            // collapses it to a single 4-byte record — exactly the 306-byte file
            // REPLAY-FORMAT §9 describes. With one, the buttons change on the named ticks.
            ulong audio = FrameHash.Empty;
            for (int tick = 0; tick < ticks; tick++)
            {
                session.Machine.Advance(inputs.At(tick));
                audio = FrameHash.Combine(audio, session.Machine.Console.AudioBlock);
                if (Checkpoint.IsDue(tick + 1, every, ticks))
                {
                    Console.WriteLine(Checkpoint.Line(tick + 1, session.Machine.Framebuffer, audio));
                }
            }

            string? folder = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            using (var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            {
                session.Machine.Log.WriteTo(stream, session.Machine.Header);
            }

            long size = new FileInfo(outPath).Length;
            Console.WriteLine(
                $"Recorded {session.Machine.Log.TickCount} ticks of {session.Name} "
                + $"({session.Machine.Log.RunCount} runs, {size} bytes) -> {outPath}");
            Console.WriteLine($"cart {CartIdentity.ToHex(session.Identity)}");
            // Same shape `quarp sim` prints, so a recording can be checked without replaying it:
            // the labelled audio digest of the whole run, then the bare final frame hash.
            Console.WriteLine(Checkpoint.AudioLine(audio));
            Console.WriteLine(FrameHash.Of(session.Machine.Framebuffer));
            return 0;
        });
    }

    // --- quarp replay play <file.qrpr> [--cart path] [--every N] ---

    public static int Play(string[] args)
    {
        string? replayPath = null;
        string? cartPath = null;
        int every = 0;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cart" when i + 1 < args.Length:
                    cartPath = args[++i];
                    break;
                case "--every" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    && parsed > 0:
                    every = parsed;
                    i++;
                    break;
                default:
                    if (replayPath is null && !args[i].StartsWith('-'))
                    {
                        replayPath = args[i];
                        break;
                    }
                    Console.Error.WriteLine($"quarp replay play: unknown argument '{args[i]}'");
                    Console.Error.WriteLine("usage: quarp replay play <file>.qrpr [--cart <path>] [--every N]");
                    return 1;
            }
        }
        if (replayPath is null)
        {
            Console.Error.WriteLine("usage: quarp replay play <file>.qrpr [--cart <path>] [--every N]");
            return 1;
        }
        if (!File.Exists(replayPath))
        {
            Console.Error.WriteLine($"quarp: replay not found: {replayPath}");
            return 1;
        }

        cartPath ??= GuessCartFolder(replayPath);
        if (cartPath is null)
        {
            Console.Error.WriteLine(
                $"quarp: cannot tell which cartridge {Path.GetFileName(replayPath)} belongs to — pass --cart <path>. "
                + "A replay stores the cartridge's identity hash, never its code (REPLAY-FORMAT §5).");
            return 1;
        }

        return Run(() =>
        {
            ReplayLog log;
            ReplayHeader header;
            using (var stream = File.OpenRead(replayPath))
            {
                log = ReplayLog.ReadFrom(stream, out header);
            }

            using Session session = Session.Open(cartPath, header, log);
            if (header.HasIdentity && !header.IdentityMatches(session.Identity))
            {
                // A warning, never a refusal (REPLAY-FORMAT §5): replaying a log against
                // edited code is the whole point of continuation mode. It goes to stderr so
                // it can never be mistaken for the hash on stdout.
                Console.Error.WriteLine(
                    $"quarp: warning — replay was recorded against cart "
                    + $"{CartIdentity.ToHex(header.CartIdentity)}");
                Console.Error.WriteLine(
                    $"quarp:           this cartridge is           {CartIdentity.ToHex(session.Identity)}");
                Console.Error.WriteLine("quarp:           playing anyway; frames may differ.");
            }

            // One tick per call, not one chunk per call: the audio digest has to see every
            // block, and ReplayForward draws every tick either way, so this costs nothing but
            // the loop. Checkpoints still land on exactly the ticks --every asks for.
            int played = 0;
            ulong audio = FrameHash.Empty;
            while (played < log.TickCount)
            {
                if (session.Machine.ReplayForward(1) == 0)
                {
                    break;
                }
                played++;
                audio = FrameHash.Combine(audio, session.Machine.Console.AudioBlock);
                if (Checkpoint.IsDue(played, every, log.TickCount))
                {
                    // Labelled, and emitted for the final tick as well, so the sequence is a
                    // complete description of the run: CI compares the whole block between
                    // architectures and a mismatch names the tick it happened on.
                    Console.WriteLine(Checkpoint.Line(played, session.Machine.Framebuffer, audio));
                }
            }

            // The contract with CI: the last line of stdout is the final framebuffer hash,
            // 16 lowercase hex digits, exactly as `quarp sim` prints it. The run's audio
            // digest goes on the labelled line above it, never bare.
            Console.WriteLine(Checkpoint.AudioLine(audio));
            Console.WriteLine(FrameHash.Of(session.Machine.Framebuffer));
            return 0;
        });
    }

    // --- quarp bench <cart> --ticks N ---

    public static int Bench(string[] args)
    {
        string? cartPath = null;
        string? script = null;
        string? scriptFile = null;
        int ticks = 10_000;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ticks" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    && parsed > 0:
                    ticks = parsed;
                    i++;
                    break;
                case "--input" when i + 1 < args.Length:
                    script = args[++i];
                    break;
                case "--input-file" when i + 1 < args.Length:
                    scriptFile = args[++i];
                    break;
                default:
                    if (cartPath is null && !args[i].StartsWith('-'))
                    {
                        cartPath = args[i];
                        break;
                    }
                    Console.Error.WriteLine($"quarp bench: unknown argument '{args[i]}'");
                    Console.Error.WriteLine(BenchUsage);
                    return 1;
            }
        }
        if (cartPath is null)
        {
            Console.Error.WriteLine(BenchUsage);
            return 1;
        }

        InputScript inputs;
        try
        {
            inputs = LoadScript(script, scriptFile);
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine($"quarp bench: {e.Message}");
            Console.Error.WriteLine(BenchUsage);
            return 1;
        }

        return Run(() =>
        {
            using Session session = Session.Open(cartPath);
            TimeMachine machine = session.Machine;

            // Warm-up, discarded: the first pass pays for tier-0 JIT of the cartridge and the
            // rasterizer, and reporting that as the steady-state rate understates rewind speed
            // badly. Restart() then puts the session back to a clean tick 0.
            PlayPass(machine, inputs, ticks);
            machine.SeekTo(0);
            machine.SeekTo(ticks);
            machine.Restart();

            // Both passes repeat until they have accumulated enough wall clock to be worth
            // reading. A single pass over a few hundred ticks is under a millisecond, which
            // measures the clock rather than the console.
            (long playTicks, TimeSpan playTime) = Measure(() =>
            {
                machine.Restart();
                PlayPass(machine, inputs, ticks);
                return ticks;
            });

            // One rewind is exactly a Boot plus N Update-only ticks, which is what the pair of
            // seeks below performs. A resimulated tick skips Draw, and that is the number that
            // actually governs how long holding Backspace takes (ARCHITECTURE §4).
            machine.Restart();
            PlayPass(machine, inputs, ticks);
            (long resimTicks, TimeSpan resimTime) = Measure(() =>
            {
                machine.SeekTo(0);
                machine.SeekTo(ticks);
                return ticks;
            });

            long playRate = Rate(playTicks, playTime);
            long resimRate = Rate(resimTicks, resimTime);

            Console.WriteLine($"cart:   {session.Name} ({CartIdentity.ToShortHex(session.Identity)})");
            Console.WriteLine($"ticks:  {ticks} per pass");
            Console.WriteLine($"input:  {(inputs.EntryCount == 0 ? "none (idle)" : script ?? scriptFile)}");
            Console.WriteLine();
            Console.WriteLine(
                $"play  (Update+Draw): {playRate,12:N0} ticks/sec  "
                + $"({playTicks:N0} ticks in {playTime.TotalMilliseconds:F0} ms)");
            Console.WriteLine(
                $"resim (Update only): {resimRate,12:N0} ticks/sec  "
                + $"({resimTicks:N0} ticks in {resimTime.TotalMilliseconds:F0} ms)");
            Console.WriteLine();
            if (resimRate > 0)
            {
                Console.WriteLine("rewind cost (resimulation from tick 0, ADR-006):");
                Report("30 minutes", HalfHourTicks, resimRate);
                Report("5 minutes", HalfHourTicks / 6, resimRate);
                Report("1 minute", 3600, resimRate);
            }
            if (inputs.EntryCount == 0)
            {
                // Said out loud because it is the difference between a real measurement and a
                // flattering one: a cartridge left idle usually stops doing work.
                Console.WriteLine();
                Console.WriteLine(
                    "note: with no --input the cartridge runs unattended, and a cart that reaches a");
                Console.WriteLine(
                    "      game-over screen does almost nothing per tick from then on. Pass --input");
                Console.WriteLine(
                    "      to keep it playing if you want the rate of a live session.");
            }
            return 0;
        });
    }

    private static void Report(string label, int ticks, long ratePerSecond) =>
        Console.WriteLine($"  {label,-12} {ticks,8:N0} ticks -> {(double)ticks / ratePerSecond * 1000,8:F1} ms");

    /// <summary>Replays the scripted input forward, recording it — the live-play cost.</summary>
    private static void PlayPass(TimeMachine machine, InputScript inputs, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            machine.Advance(inputs.At(tick));
        }
    }

    /// <summary>
    /// Runs <paramref name="pass"/> until at least <see cref="MinMeasureMilliseconds"/> of wall
    /// clock has gone by, and reports the total ticks and total time. Repeating rather than
    /// timing once is what makes a 180-tick cartridge measurable at all.
    /// </summary>
    private static (long Ticks, TimeSpan Elapsed) Measure(Func<int> pass)
    {
        var clock = Stopwatch.StartNew();
        long total = 0;
        do
        {
            total += pass();
        }
        while (clock.ElapsedMilliseconds < MinMeasureMilliseconds);
        clock.Stop();
        return (total, clock.Elapsed);
    }

    private const int MinMeasureMilliseconds = 400;

    private const string BenchUsage =
        "usage: quarp bench <cart> --ticks N [--input <script>] [--input-file <file>]\n"
        + "  --input uses the same tick:buttons grammar as `quarp replay record`.";

    private static long Rate(long ticks, TimeSpan elapsed) =>
        elapsed.TotalSeconds <= 0 ? 0 : (long)(ticks / elapsed.TotalSeconds);

    // --- shared plumbing ---

    /// <summary>
    /// The scripted button track a headless command runs on: <c>--input</c> spelled out on the
    /// command line, or <c>--input-file</c> read from disk. Reading the file is the CLI's job
    /// and nobody else's — the core never opens a path (ARCHITECTURE §2), and the grammar is
    /// the same either way (<see cref="InputScript"/>).
    ///
    /// <para>Both at once is an error rather than a precedence rule: a golden replay is
    /// regenerated from a command in a README, and "the file silently won" is exactly the kind
    /// of quiet answer that produces a reference artifact nobody can reproduce.</para>
    /// </summary>
    private static InputScript LoadScript(string? inline, string? file)
    {
        if (inline is not null && file is not null)
        {
            throw new FormatException("--input and --input-file are mutually exclusive; pass one of them.");
        }
        if (file is null)
        {
            return InputScript.Parse(inline);
        }
        if (!File.Exists(file))
        {
            throw new FormatException($"input script not found: {file}");
        }
        return InputScript.Parse(File.ReadAllText(file));
    }

    /// <summary>
    /// A compiled cartridge plus the TimeMachine driving it, with the headless starting
    /// conditions applied. Disposing unloads the cartridge's load context.
    /// </summary>
    private sealed class Session : IDisposable
    {
        private readonly CartHost _host;

        private Session(CartHost host, TimeMachine machine, byte[] identity, string name)
        {
            _host = host;
            Machine = machine;
            Identity = identity;
            Name = name;
        }

        public TimeMachine Machine { get; }
        public byte[] Identity { get; }
        public string Name { get; }

        public static Session Open(string cartPath) => Open(cartPath, null, null);

        public static Session Open(string cartPath, ReplayHeader? header, ReplayLog? log)
        {
            CartData data = CartSource.Load(cartPath);
            CartCompileResult result = CartCompiler.Compile(data);
            foreach (string warning in result.Warnings)
            {
                Console.Error.WriteLine(warning);
            }
            if (!result.Success)
            {
                foreach (string diagnostic in result.Diagnostics)
                {
                    Console.Error.WriteLine(diagnostic);
                }
                throw new CartLoadException("cartridge failed to compile.");
            }

            CartHost host = CartHost.Load(result.AssemblyBytes);
            try
            {
                byte[] identity = CartIdentity.Compute(data);
                // A replay's own header wins when playing one back: its seed and persistent
                // snapshot are inputs of the simulation being reproduced, and substituting
                // ours would reproduce a different game (REPLAY-FORMAT §2).
                var machine = new TimeMachine(
                    ConsoleProfile.Profile8,
                    host.Cartridge,
                    header ?? HeadlessHeader(identity),
                    log ?? new ReplayLog(),
                    data.Gfx,
                    data.Map,
                    data.Flags,
                    data.Sfx,
                    data.Music);
                machine.Boot();
                return new Session(host, machine, identity, data.Manifest.Name);
            }
            catch
            {
                host.Unload();
                throw;
            }
        }

        public void Dispose() => _host.Unload();
    }

    /// <summary>
    /// The cart folder a replay written by <c>F5</c> or <c>quarp replay record</c> sits in:
    /// <c>&lt;cart&gt;/replays/&lt;name&gt;.qrpr</c>. Only used when <c>--cart</c> is absent,
    /// and only accepted when the folder really is a cartridge.
    /// </summary>
    private static string? GuessCartFolder(string replayPath)
    {
        DirectoryInfo? folder = new FileInfo(Path.GetFullPath(replayPath)).Directory;
        for (int depth = 0; depth < 2 && folder is not null; depth++, folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "manifest.json")))
            {
                return folder.FullName;
            }
        }
        return null;
    }

    /// <summary>
    /// Turns the exception shapes these commands can hit into the plain messages a CI log
    /// should carry, and keeps a cartridge crash distinguishable from a broken file.
    /// </summary>
    private static int Run(Func<int> body)
    {
        try
        {
            return body();
        }
        catch (ReplayFormatException e)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        catch (Exception e)
        {
            // A cartridge exception: the embedded PDB puts file and line numbers in the trace.
            Console.Error.WriteLine("quarp: cartridge crashed:");
            Console.Error.WriteLine(e.ToString());
            return 1;
        }
    }
}

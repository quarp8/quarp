using System.Globalization;
using System.Text;
using System.Text.Json;
using Quarp.CartKit;
using Quarp.Cli;
using Quarp.Core;
using Quarp.Shell.Desktop;

// Diagnostics quote SPEC-8 by section (§) and use em dashes; the Windows console defaults to
// a legacy code page that turns both into noise. UTF-8 *without* a BOM specifically: the CI
// pulls the framebuffer hash out of stdout by matching a whole line against
// ^[0-9a-f]{16}$, and a BOM on the first line would break that match invisibly.
try
{
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (IOException)
{
    // No console attached (output fully redirected on some hosts). Nothing to configure.
}

string command = args.Length == 0 ? "run" : args[0];

switch (command)
{
    case "run":
    {
        // No path: the game library (M9 — the fact "console without a cartridge" has one
        // owner, and it is the library; the windowed test pattern is gone, `quarp pattern`
        // proves the palette headlessly). With a path: the cartridge directly, hot reload
        // and all, and Esc quits the process — the F5 loop the library stays out of.
        // --break-at N is the debugger-free half of "debugging in time" (M4 work order, stage 1):
        // the console catches up to tick N and pauses *before* that tick's Update, so the
        // author can look at the state the buggy tick is about to be handed.
        //
        // Every number this tool reads off a command line goes through NumberStyles.None and
        // CultureInfo.InvariantCulture — the same pair AudioSilenceCommand pins and the rule
        // SPEC-8 §7 states: what a tick number means must not depend on the machine that typed
        // it. NumberStyles.None is the strict half: no sign, no whitespace, no separators, so
        // `--break-at +5` and `--break-at 1,000` fail here, naming the value, instead of being
        // read as something the author did not write.
        const string RunUsage = "usage: quarp run [path] [--break-at N]";
        string? cartPath = null;
        int? breakAt = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--break-at")
            {
                if (i + 1 >= args.Length
                    || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedBreak)
                    || parsedBreak < 0)
                {
                    Console.Error.WriteLine($"quarp run: --break-at needs a tick number >= 0 ({RunUsage})");
                    return 1;
                }
                breakAt = parsedBreak;
                i++;
            }
            else if (cartPath is null && !args[i].StartsWith('-'))
            {
                cartPath = args[i];
            }
            else
            {
                Console.Error.WriteLine($"quarp run: unknown argument '{args[i]}' ({RunUsage})");
                return 1;
            }
        }
        if (breakAt is not null && cartPath is null)
        {
            // The test pattern has no simulation to stop, so this is a typo, not a request.
            Console.Error.WriteLine($"quarp run: --break-at needs a cartridge to run ({RunUsage})");
            return 1;
        }
        QuarpGame game;
        try
        {
            game = new QuarpGame(cartPath, breakAt);
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The cart files were unreadable at startup (locked by an editor, denied by
            // ACLs): a plain message, not a runtime stack trace.
            Console.Error.WriteLine($"quarp: cannot read the cartridge: {e.Message}");
            return 1;
        }

        using (game)
        {
            game.Run();
        }
        return 0;
    }

    case "sim":
    {
        // Headless determinism probe: N ticks of empty input, then one FNV-1a hash of the
        // framebuffer to stdout (M1 work order; the seed of future golden-master CI).
        // With --every N it also prints a checkpoint line per N ticks (see Checkpoint):
        // the final hash alone cannot tell "identical all the way" from "diverged and came
        // back", and for a cart sitting on a game-over screen it says almost nothing.
        const string SimUsage = "usage: quarp sim <path> --ticks N [--every N]";
        if (args.Length < 2)
        {
            Console.Error.WriteLine(SimUsage);
            return 1;
        }
        int ticks = 600;
        int every = 0;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--ticks" && i + 1 < args.Length
                && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                && parsed >= 0)
            {
                ticks = parsed;
                i++;
            }
            else if (args[i] == "--every" && i + 1 < args.Length
                && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedEvery)
                && parsedEvery > 0)
            {
                every = parsedEvery;
                i++;
            }
            else
            {
                Console.Error.WriteLine($"quarp sim: unknown argument '{args[i]}' ({SimUsage})");
                return 1;
            }
        }
        return RunSim(args[1], ticks, every);
    }

    case "pack":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: quarp pack <folder> [-o file]");
            return 1;
        }
        string folder = args[1];
        string? outFile = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                outFile = args[i + 1];
                i++;
            }
            else
            {
                Console.Error.WriteLine($"quarp pack: unknown argument '{args[i]}' (usage: quarp pack <folder> [-o file])");
                return 1;
            }
        }
        // Default: <folder>.quarp8 next to the cart folder.
        outFile ??= Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + ".quarp8";
        try
        {
            Quarp8Package.Pack(folder, outFile);
            Console.WriteLine($"Packed {folder} -> {outFile} ({new FileInfo(outFile).Length} bytes)");
            return 0;
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
    }

    case "build":
    {
        // The diagnosis command ROADMAP promised and the tool never had (M4 Р14): load,
        // compile, check the limits and the generated banks, print what was found — no window,
        // no tick. This is what .vscode/tasks.json runs before F5, in place of the
        // `sim --ticks 0` that used to stand in for it and that ran Init on every launch.
        return BuildCommand.Invoke(args[1..]);
    }

    case "new":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: quarp new <folder>");
            return 1;
        }
        return CreateNewCart(args[1]);
    }

    case "replay":
    {
        // `replay` is a group, not a command: `record` writes a .qrpr from a headless run,
        // `play` reproduces one and prints the frame hash CI compares across architectures.
        string? sub = args.Length > 1 ? args[1] : null;
        string[] rest = args.Length > 2 ? args[2..] : Array.Empty<string>();
        switch (sub)
        {
            case "record":
                return ReplayCommands.Record(rest);
            case "play":
                return ReplayCommands.Play(rest);
            default:
                Console.Error.WriteLine(sub is null
                    ? "usage: quarp replay <record|play> ..."
                    : $"quarp replay: unknown subcommand '{sub}'");
                Console.Error.WriteLine(
                    "  quarp replay record <cart> -o <file>.qrpr --ticks N [--input <script>] "
                    + "[--input-file <file>] [--every N]");
                Console.Error.WriteLine("  quarp replay play <file>.qrpr [--cart <path>] [--every N]");
                return 1;
        }
    }

    case "audio":
    {
        // `audio` is a group like `replay`: each subcommand owns its own argument errors and
        // exit codes (CartKit work order, deliverable 4). `silence` is the second one (M4 Р4.7):
        // it prints the PCM digest of a run in which nothing ever sounds, so that the CI mute
        // check derives that number from the real APU instead of carrying it as a constant that
        // rots the moment a tick count moves.
        string[] audioArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();
        return audioArgs.Length > 0 && audioArgs[0] == "silence"
            ? AudioSilenceCommand.Invoke(audioArgs)
            : AudioBuildCommand.Invoke(audioArgs);
    }

    case "map":
    {
        // `map` is a group like `audio` and `replay`: the subcommand owns its own arguments,
        // its error text and its exit code.
        return MapBuildCommand.Invoke(args.Length > 1 ? args[1..] : Array.Empty<string>());
    }

    case "gfx":
    {
        // `gfx` is a group like `audio` and `map`: the subcommand owns its arguments, its error
        // text and its exit code. `dump` is the first one (M9 wave A1) — it boots a cart
        // headless, runs Init and writes the console's sprite sheet as a gfx.png.
        return GfxDumpCommand.Invoke(args.Length > 1 ? args[1..] : Array.Empty<string>());
    }

    case "bench":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: quarp bench <cart> --ticks N");
            return 1;
        }
        return ReplayCommands.Bench(args[1..]);
    }

    case "pattern":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: quarp pattern <out.bmp>");
            return 1;
        }
        var fb = new Framebuffer(ConsoleProfile.Profile8);
        TestPattern.Render(fb);
        BmpWriter.Write(args[1], fb);
        Console.WriteLine($"Wrote {fb.Width}x{fb.Height} test pattern to {args[1]}");
        return 0;
    }

    default:
        Console.WriteLine("QUARP — fantasy console");
        Console.WriteLine("usage:");
        Console.WriteLine("  quarp run [path]             open the console window (the game library without a path,");
        Console.WriteLine("                               a cart folder or .quarp8 file with one; plain `quarp`");
        Console.WriteLine("                               does the same — scripts should use sim/replay/build)");
        Console.WriteLine("  quarp run <path> --break-at N");
        Console.WriteLine("                               same, but pause before Update of tick N and stay there");
        Console.WriteLine("                               (docs/DEBUGGING.md — debugging in time)");
        Console.WriteLine("  quarp new <folder>           create a cartridge template (manifest.json + src/main.cs,");
        Console.WriteLine("                               .quarp/cart.csproj and .vscode for F5 debugging)");
        Console.WriteLine("  quarp pack <folder> [-o f]   pack a cart folder into a .quarp8 file");
        Console.WriteLine("  quarp build <cart>           compile and check a cart — limits, metadata, banks —");
        Console.WriteLine("                               without opening a window or running a tick; this is");
        Console.WriteLine("                               what F5 runs before launching the debugger");
        Console.WriteLine("  quarp sim <path> --ticks N [--every N]");
        Console.WriteLine("                               run N ticks headless, print the framebuffer FNV-1a hash");
        Console.WriteLine("  quarp replay record <cart> -o <file>.qrpr --ticks N [--input <script>]");
        Console.WriteLine("                               [--input-file <file>] [--every N]");
        Console.WriteLine("                               record a headless replay for CI and goldens");
        Console.WriteLine("  quarp replay play <file>.qrpr [--cart <path>] [--every N]");
        Console.WriteLine("                               reproduce a replay, print the final framebuffer hash");
        Console.WriteLine("  --every N on any of the three above also prints checkpoint lines");
        Console.WriteLine("  'tick <n> <frame-hash> <audio-hash>', which is what the cross-architecture");
        Console.WriteLine("  CI comparison reads; the audio column covers every block, not just this tick.");
        Console.WriteLine("  quarp audio build <cart> [--check]");
        Console.WriteLine("                               compile sfx.txt/music.txt into sfx.bin/music.bin");
        Console.WriteLine("  quarp audio silence --ticks N");
        Console.WriteLine("                               print the PCM digest of N ticks in which nothing sounds");
        Console.WriteLine("                               (what CI compares a run against to catch a mute cart)");
        Console.WriteLine("  quarp map build <cart> [--check]");
        Console.WriteLine("                               compile map.csv into map.bin (docs/MAP-FORMAT.md);");
        Console.WriteLine("                               a cart without a map.csv is not an error");
        Console.WriteLine("  quarp gfx dump <cart> [-o <file.png>] [--force]");
        Console.WriteLine("                               run the cart's Init headless (no tick) and write the");
        Console.WriteLine("                               console's sprite sheet as gfx.png, printing its sha256;");
        Console.WriteLine("                               defaults to <cart>/gfx.png and never overwrites without");
        Console.WriteLine("                               --force");
        Console.WriteLine("  quarp bench <cart> --ticks N  measure play and resimulation speed (rewind cost)");
        Console.WriteLine("  quarp pattern <file>         write the test pattern as a .bmp image");
        Console.WriteLine();
        Console.WriteLine("time controls in `quarp run`: Space pause, . step, , step back, [ ] speed,");
        Console.WriteLine("Backspace rewind, Home to start, F5 save replay, F8 play replay, Esc quit");
        Console.WriteLine("(a cart launched from the library returns to it on Esc instead).");
        return command is "--help" or "-h" or "help" ? 0 : 1;
}

static int RunSim(string path, int ticks, int every)
{
    try
    {
        CartData data = CartSource.Load(path);
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
            return 1;
        }
        using var host = CartHost.Load(result.AssemblyBytes);
        // Persistent memory deliberately starts zeroed and save.dat is neither read nor
        // written: the hash must depend on the cart alone, not on this machine's saves.
        var console = new VirtualConsole(
            ConsoleProfile.Profile8, data.Gfx, data.Map, data.Flags, data.Sfx, data.Music);
        console.AttachCart(host.Cartridge);
        // AttachCart runs Init as tick 0 and produces neither a frame nor a block, so the
        // digest starts empty and covers ticks 1..N — the same ticks the frames come from.
        ulong audio = FrameHash.Empty;
        for (int i = 0; i < ticks; i++)
        {
            console.Tick(default);
            audio = FrameHash.Combine(audio, console.AudioBlock);
            if (Checkpoint.IsDue(i + 1, every, ticks))
            {
                Console.WriteLine(Checkpoint.Line(i + 1, console.Framebuffer, audio));
            }
        }
        Console.WriteLine(Checkpoint.AudioLine(audio));
        // The last line of stdout stays the bare 16-hex-digit final frame hash, checkpoints
        // or not: every M1/M2 consumer greps ^[0-9a-f]{16}$ for exactly this.
        Console.WriteLine(FrameHash.Of(console.Framebuffer));
        return 0;
    }
    catch (CartLoadException e)
    {
        Console.Error.WriteLine($"quarp: {e.Message}");
        return 1;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        // Reading the cart failed, which is not the cartridge crashing — say so plainly.
        Console.Error.WriteLine($"quarp: cannot read {path}: {e.Message}");
        return 1;
    }
    catch (Exception e)
    {
        // A cartridge exception: the embedded PDB puts file and line numbers in the trace.
        Console.Error.WriteLine("quarp: cartridge crashed during sim:");
        Console.Error.WriteLine(e.ToString());
        return 1;
    }
}

static int CreateNewCart(string folder)
{
    // The writer itself lives in CartKit (CartScaffold) since the boot-menu wave: the shell's
    // CREATE GAME writes the very same files, and the CLI cannot be referenced back by the
    // shell (quarp is one exe: Cli -> Shell.Desktop). This function owns only the terminal:
    // which refusals and warnings are printed, and what the summary says.
    string root = Path.GetFullPath(folder);
    if (CartScaffold.CartridgeExists(root))
    {
        Console.Error.WriteLine($"quarp: {root} already contains a cartridge (manifest.json exists).");
        return 1;
    }
    string name = CartScaffold.Create(root);
    bool devProject = CartScaffold.TryWriteDevProject(root, out string? devWarning);
    if (devWarning is not null)
    {
        Console.Error.WriteLine(devWarning);
    }
    bool vsCode = CartScaffold.TryWriteVsCodeFiles(root, out string? vsCodeWarning);
    if (vsCodeWarning is not null)
    {
        Console.Error.WriteLine(vsCodeWarning);
    }
    Console.WriteLine($"Created cartridge '{name}' in {root}");
    if (devProject)
    {
        Console.WriteLine(
            $"  {CartTemplate.DevFolder}/{CartTemplate.DevProjectFile} — dev-only, gives your editor the "
            + "QRP1001-QRP1004 diagnostics");
    }
    if (vsCode)
    {
        Console.WriteLine(
            $"  {CartTemplate.VsCodeFolder}/ — dev-only, open this folder in VS Code and press F5 to debug "
            + "(docs/DEBUGGING.md)");
    }
    Console.WriteLine($"  quarp run {folder}");
    return 0;
}

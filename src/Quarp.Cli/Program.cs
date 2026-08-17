using System.Text.Json;
using Quarp.CartKit;
using Quarp.Cli;
using Quarp.Core;
using Quarp.Shell.Desktop;

string command = args.Length == 0 ? "run" : args[0];

switch (command)
{
    case "run":
    {
        // No path: the palette test pattern; with a path: the cartridge, hot reload and all.
        string? cartPath = args.Length > 1 ? args[1] : null;
        QuarpGame game;
        try
        {
            game = new QuarpGame(cartPath);
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
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: quarp sim <path> --ticks N");
            return 1;
        }
        int ticks = 600;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--ticks" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed) && parsed >= 0)
            {
                ticks = parsed;
                i++;
            }
            else
            {
                Console.Error.WriteLine($"quarp sim: unknown argument '{args[i]}' (usage: quarp sim <path> --ticks N)");
                return 1;
            }
        }
        return RunSim(args[1], ticks);
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

    case "new":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: quarp new <folder>");
            return 1;
        }
        return CreateNewCart(args[1]);
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
        Console.WriteLine("  quarp run [path]             open the console window (test pattern without a path,");
        Console.WriteLine("                               a cart folder or .quarp8 file with one)");
        Console.WriteLine("  quarp new <folder>           create a cartridge template (manifest.json + src/main.cs)");
        Console.WriteLine("  quarp pack <folder> [-o f]   pack a cart folder into a .quarp8 file");
        Console.WriteLine("  quarp sim <path> --ticks N   run N ticks headless, print the framebuffer FNV-1a hash");
        Console.WriteLine("  quarp pattern <file>         write the test pattern as a .bmp image");
        return command is "--help" or "-h" or "help" ? 0 : 1;
}

static int RunSim(string path, int ticks)
{
    try
    {
        CartData data = CartSource.Load(path);
        CartCompileResult result = CartCompiler.Compile(data);
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
        var console = new VirtualConsole(ConsoleProfile.Profile8, data.Gfx, data.Map, data.Flags);
        console.AttachCart(host.Cartridge);
        for (int i = 0; i < ticks; i++)
        {
            console.Tick(default);
        }
        Console.WriteLine(Fnv1a64(console.Framebuffer.Pixels).ToString("x16"));
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

static ulong Fnv1a64(byte[] data)
{
    ulong hash = 14695981039346656037UL;
    for (int i = 0; i < data.Length; i++)
    {
        hash = unchecked((hash ^ data[i]) * 1099511628211UL);
    }
    return hash;
}

static int CreateNewCart(string folder)
{
    string root = Path.GetFullPath(folder);
    if (File.Exists(Path.Combine(root, "manifest.json")))
    {
        Console.Error.WriteLine($"quarp: {root} already contains a cartridge (manifest.json exists).");
        return 1;
    }
    string name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    if (name.Length == 0)
    {
        name = "my-cart";
    }

    Directory.CreateDirectory(Path.Combine(root, "src"));
    string manifest = "{\n"
        + $"    \"name\": {JsonSerializer.Serialize(name)},\n"
        + "    \"author\": \"\",\n"
        + "    \"profile\": 8\n"
        + "}\n";
    File.WriteAllText(Path.Combine(root, "manifest.json"), manifest);
    File.WriteAllText(Path.Combine(root, "src", "main.cs"), CartTemplate.MainCs);
    Console.WriteLine($"Created cartridge '{name}' in {root}");
    Console.WriteLine($"  quarp run {folder}");
    return 0;
}

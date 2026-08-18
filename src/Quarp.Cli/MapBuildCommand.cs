using Quarp.CartKit;

namespace Quarp.Cli;

/// <summary>
/// <c>quarp map build &lt;cart&gt; [--check]</c> — compiles the cart folder's <c>map.csv</c> into
/// <c>map.bin</c> (docs/MAP-FORMAT.md), the 256x72 tile bank the console loads and draws through
/// <c>Map()</c>. Until the map editor of M9 exists, this is how a cartridge gets a world: the CSV
/// is what the author edits — by hand or through Tiled's <c>File → Export As… → CSV</c> — this
/// command is the compiler, and the binary is what ships inside the <c>.quarp8</c>.
///
/// <para>Deliberately the same shape as <see cref="AudioBuildCommand"/>, down to the wording of
/// the errors and the one-line-per-file report: an author who has already built a bank of sound
/// should not have to learn a second command to build a map, and CI should not have to learn a
/// second exit-code convention. <c>--check</c> means the same thing here as it does there —
/// compile, write nothing, and fail when the committed binary disagrees with its source. That is
/// the CI form, and it works because compilation is a pure function of the text: the comparison
/// is byte-exact on every machine and architecture.</para>
///
/// <para><b>Where it parts ways with <c>audio build</c>, and why.</b> A cart folder with no
/// <c>map.csv</c> is <em>not</em> an error here; <c>audio build</c> on a folder with neither
/// <c>sfx.txt</c> nor <c>music.txt</c> exits 1. The difference is who calls it: this command is
/// meant to run over a whole cart — from <see cref="BuildCommand"/>, from a build script, from
/// CI — before anyone knows whether that cart has a map at all, and most carts in this project
/// do not. "No map" is a property of the cartridge, not a mistake in the command line, so it is
/// reported on stdout and answered with 0. What <em>is</em> an error is a <c>map.csv</c> whose
/// <c>map.bin</c> cannot be produced: a source with no built bank is the state
/// <c>CartSource.RequireBuiltAsset</c> refuses to load a cartridge in — the cart would otherwise
/// run, look wrong, and nothing anywhere would say why.</para>
/// </summary>
public static class MapBuildCommand
{
    private const string Usage = "usage: quarp map build <cart> [--check]";

    /// <summary>
    /// The exact size of a compiled bank. Not a validation of the author's file — that is
    /// <see cref="MapTextCompiler"/>'s job and it has already happened by the time this is read —
    /// but of the compiler itself: <c>map.bin</c> carries no header and no length field, so a bank
    /// of the wrong size is caught by <see cref="CartSource"/> at load time, one step removed from
    /// the code that produced it and phrased as a byte count nobody asked for. Caught here it
    /// names the real culprit and, more importantly, nothing wrong reaches the disk.
    /// </summary>
    private const int BankBytes = CartData.MapWidth * CartData.MapHeight;

    /// <summary>
    /// Entry point for the <c>map</c> command group; <paramref name="args"/> starts at the
    /// subcommand, exactly as <see cref="AudioBuildCommand.Invoke"/> does, so the dispatcher in
    /// <c>Program.cs</c> stays one line and every argument error belongs to the command that
    /// understands it.
    /// </summary>
    public static int Invoke(string[] args)
    {
        string? sub = args.Length > 0 ? args[0] : null;
        if (sub != "build")
        {
            Console.Error.WriteLine(sub is null
                ? "usage: quarp map <build> ..."
                : $"quarp map: unknown subcommand '{sub}'");
            Console.Error.WriteLine("  " + Usage);
            return 1;
        }
        return Build(args[1..]);
    }

    private static int Build(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }
        string folder = args[0];
        bool check = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--check")
            {
                check = true;
            }
            else
            {
                Console.Error.WriteLine($"quarp map build: unknown argument '{args[i]}' ({Usage})");
                return 1;
            }
        }

        string root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine(File.Exists(root)
                ? $"quarp: {folder} is a file; map build works on a cart folder, because that is where the "
                    + "map.csv source lives."
                : $"quarp: cart folder not found: {root}");
            return 1;
        }

        string mapText = Path.Combine(root, "map.csv");
        if (!File.Exists(mapText))
        {
            // Success, and on stdout: see the class remark. A cartridge without a map is a whole
            // cartridge — CartData hands the console 18432 zero bytes and every cell is empty.
            Console.WriteLine($"{root} has no map.csv — nothing to build.");
            return 0;
        }

        try
        {
            // File.ReadAllText, the same call audio build makes: the compiler is handed the text
            // as this tool reads it (UTF-8, BOM dropped), and every question about line endings,
            // comments and stray spaces belongs to docs/MAP-FORMAT.md, not to this file.
            byte[] bank = MapTextCompiler.CompileMap(File.ReadAllText(mapText), "map.csv");
            if (bank.Length != BankBytes)
            {
                Console.Error.WriteLine(
                    $"quarp: internal error — the map compiler produced {bank.Length} bytes, and a map bank is "
                    + $"exactly {BankBytes} ({CartData.MapWidth}x{CartData.MapHeight}). Refusing to write a bank "
                    + "the console would reject at load time.");
                return 1;
            }
            return Emit(root, bank, check) ? 0 : 1;
        }
        catch (CartLoadException e)
        {
            // Already carries "map.csv:14: message" — printing it as-is is the whole point, and it
            // is why the compiler prints nothing itself.
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
    }

    /// <summary>Writes the bank, or in <c>--check</c> mode compares it with the one on disk.</summary>
    private static bool Emit(string root, byte[] bytes, bool check)
    {
        string path = Path.Combine(root, "map.bin");
        string summary = Describe(bytes);
        if (!check)
        {
            File.WriteAllBytes(path, bytes);
            Console.WriteLine($"map.csv -> map.bin  ({bytes.Length} bytes, {summary})");
            return true;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"quarp: map.bin is missing; run 'quarp map build {root}'.");
            return false;
        }
        byte[] existing = File.ReadAllBytes(path);
        if (!existing.AsSpan().SequenceEqual(bytes))
        {
            Console.Error.WriteLine(
                $"quarp: map.bin does not match map.csv ({existing.Length} bytes on disk, {bytes.Length} "
                + $"compiled); run 'quarp map build {root}' and commit the result.");
            return false;
        }
        Console.WriteLine($"map.bin is up to date with map.csv  ({summary})");
        return true;
    }

    /// <summary>
    /// How full the map is, in the terms the console reads it in: tile 0 is the empty cell
    /// (API-8; <c>VirtualConsole.Map</c> skips it), so a count of non-zero bytes is a count of
    /// cells that will actually draw something. A freshly exported Tiled map is mostly empty,
    /// which makes this the one number that tells an author at a glance whether the file they
    /// exported is the map they drew.
    /// </summary>
    private static string Describe(ReadOnlySpan<byte> bank)
    {
        int filled = 0;
        for (int i = 0; i < bank.Length; i++)
        {
            if (bank[i] != 0)
            {
                filled++;
            }
        }
        return $"{filled} of {bank.Length} cells filled";
    }
}

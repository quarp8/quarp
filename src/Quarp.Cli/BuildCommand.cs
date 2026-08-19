using Quarp.CartKit;

namespace Quarp.Cli;

/// <summary>
/// <c>quarp build &lt;cart&gt;</c> — the diagnosis command ROADMAP has promised since M0 and the
/// tool has never had (M4 Р14). It loads a cartridge in either of its two shapes, compiles it
/// with the very compiler <c>quarp run</c> uses, checks that the generated banks still agree with
/// the sources they were generated from, prints what it found, and exits 0 or 1. It opens no
/// window and runs no tick.
///
/// <para><b>What it replaces.</b> Until now <c>.vscode/tasks.json</c> ran
/// <c>quarp sim &lt;cart&gt; --ticks 0</c> as its build task — a determinism probe pressed into
/// service as a compile check, which is why the generated file had to carry a comment explaining
/// that the two hashes it prints are not the point. Worse than untidy: <c>sim</c> attaches the
/// cartridge to a console, and attaching runs <c>Init</c>. A build task that executes the
/// author's code is a build task that can hang, crash, or scribble on state before a single
/// breakpoint has been set, and it reports the crash as a failed build.</para>
///
/// <para><b>Why it is not a second validator.</b> Every judgement below already existed in
/// <c>Quarp.CartKit</c> and is reached through the same entry points the console and the packer
/// reach it through: <see cref="CartSource.Load"/> for the manifest, the asset sizes, the audio
/// banks and the 256 KB code budget (SPEC-8 §6); <see cref="CartCompiler.Compile"/> for C# errors
/// and the QRP1001-QRP1004 determinism rules; <see cref="CartHost.Load"/> for "exactly one
/// <c>Cartridge</c> subclass, and it can be constructed"; <see cref="AudioTextCompiler"/> and
/// <see cref="MapTextCompiler"/> for the banks. This command contributes a report and an exit
/// code, nothing else — a second opinion about what a valid cartridge is would be a bug the day
/// it disagreed with the console.</para>
///
/// <para><b>The one piece of cartridge code that does run</b> is the constructor, inside
/// <see cref="CartHost.Load"/>. That is not a tick and not <c>Init</c>: instantiation is how
/// "exactly one <c>Cartridge</c> subclass with a public parameterless constructor" is checked at
/// all, it is a load-time contract that otherwise fails for the first time in front of the
/// author with the window already opening, and <c>sim --ticks 0</c> — the task this replaces —
/// checked it too. <c>Init</c>, <c>Update</c> and <c>Draw</c> are never called: no
/// <c>VirtualConsole</c> is constructed here, so there is nothing to attach a cartridge to.</para>
///
/// <para><b>Output shape is load-bearing.</b> Compiler diagnostics go to stderr verbatim, in the
/// <c>src/main.cs(7,17): error CS1525: ...</c> form Roslyn produces, with the path still relative
/// to the cart folder — that is what the <c>problemMatcher</c> in the generated
/// <c>tasks.json</c> matches and what makes an entry in the Problems panel link to the right
/// file. Reformatting them, prefixing them, or turning the path absolute breaks the panel
/// silently, which is the failure mode worth naming here because nothing would fail loudly.</para>
/// </summary>
public static class BuildCommand
{
    private const string Usage = "usage: quarp build <cart>";

    /// <summary>
    /// Entry point for <c>build</c>; <paramref name="args"/> starts after the command name.
    /// Exit codes: 0 when the cartridge compiles, loads and its banks are current; 1 for
    /// anything else, argument mistakes included. One failing exit code rather than a taxonomy
    /// on purpose — every caller this has (VS Code's task runner, CI, a shell script) asks the
    /// same yes-or-no question, and a code nobody branches on is a promise nobody keeps.
    /// </summary>
    public static int Invoke(string[] args)
    {
        string? path = null;
        foreach (string arg in args)
        {
            if (path is null && !arg.StartsWith('-'))
            {
                path = arg;
            }
            else
            {
                Console.Error.WriteLine($"quarp build: unknown argument '{arg}' ({Usage})");
                return 1;
            }
        }
        if (path is null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }
        return Build(path);
    }

    private static int Build(string path)
    {
        string root = Path.GetFullPath(path);
        bool folder = Directory.Exists(root);
        if (!folder && !File.Exists(root))
        {
            Console.Error.WriteLine(
                $"quarp: cartridge not found: {root} (expected a cart folder or a .quarp8 file).");
            return 1;
        }

        CartData cart;
        try
        {
            // Manifest, source set, asset sizes, audio bank canonicality, code budget. Everything
            // this throws is a user-facing CartLoadException with its own explanation attached.
            cart = CartSource.Load(root);
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            Console.Error.WriteLine($"build failed: {path}");
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"quarp: cannot read {root}: {e.Message}");
            Console.Error.WriteLine($"build failed: {path}");
            return 1;
        }

        Console.WriteLine($"{cart.Manifest.Name} — profile {cart.Manifest.Profile}, "
            + (cart.Manifest.Author.Length == 0 ? "author not set" : $"author {cart.Manifest.Author}"));
        Console.WriteLine($"  {root}");

        int codeBytes = CodeBudget.Measure(cart.Sources);
        Console.WriteLine(
            $"  code    {codeBytes} of {CodeBudget.MaxBytes} bytes after comments, in "
            + $"{Count(cart.Sources.Count, "source file")} ({codeBytes * 100 / CodeBudget.MaxBytes}%)");
        Console.WriteLine($"  assets  {DescribeAssets(cart)}");

        int errors = 0;
        errors += Compile(cart);
        if (folder)
        {
            // Only a folder cart can have the text sources at all: a .quarp8 ships the compiled
            // banks and nothing to compare them against (SPEC-8 §6, M4 Р13).
            errors += CheckBanks(root);
        }

        if (errors > 0)
        {
            Console.Error.WriteLine($"build failed: {path} — {Count(errors, "error")}.");
            return 1;
        }
        Console.WriteLine($"build ok: {path} — compiled and checked, no cartridge tick was run.");
        return 0;
    }

    /// <summary>
    /// Compilation and the load-time contract behind it, reported the way the Problems panel
    /// reads it. Returns the number of errors found.
    ///
    /// <para>Warnings go to stderr on success as well as on failure, for the reason
    /// <see cref="CartCompileResult.Warnings"/> gives: a cart that fails on QRP1001 still
    /// deserves to hear about its QRP1003, and a cart that succeeds deserves it more.</para>
    /// </summary>
    private static int Compile(CartData cart)
    {
        CartCompileResult result = CartCompiler.Compile(cart);
        foreach (string warning in result.Warnings)
        {
            Console.Error.WriteLine(warning);
        }
        if (!result.Success)
        {
            foreach (string diagnostic in result.Diagnostics)
            {
                // Verbatim: this exact shape is the problemMatcher's contract. See the class remark.
                Console.Error.WriteLine(diagnostic);
            }
            return result.Diagnostics.Count;
        }

        try
        {
            // Instantiates the cartridge — the constructor, and nothing after it. See the class
            // remark for why this is here and why it is not a tick.
            using CartHost host = CartHost.Load(result.AssemblyBytes);
            Console.WriteLine(
                $"  compile ok, {Count(result.Warnings.Count, "warning")}, cartridge "
                + $"{host.Cartridge.GetType().FullName}");
            return 0;
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The generated banks against the text they are generated from (M4 Р14: "сверка банков
    /// звука и карты"). Returns the number of problems found.
    ///
    /// <para>A stale bank is an error, not a note. It is the quietest bug this project can
    /// produce: the cartridge loads, plays, and sounds or looks like a version of itself the
    /// author stopped editing an hour ago, with nothing anywhere to say so — the same reasoning
    /// that made <c>CartSource.RequireBuiltAsset</c> refuse a missing bank rather than load
    /// silence. Every message names the command that fixes it.</para>
    /// </summary>
    private static int CheckBanks(string root)
    {
        int problems = 0;
        problems += CheckBank(root, "sfx.txt", "sfx.bin", "quarp audio build",
            static text => AudioFormat.WriteSfxFile(AudioTextCompiler.CompileSfx(text, "sfx.txt")));
        problems += CheckBank(root, "music.txt", "music.bin", "quarp audio build",
            static text => AudioFormat.WriteMusicFile(AudioTextCompiler.CompileMusic(text, "music.txt")));
        problems += CheckBank(root, "map.csv", "map.bin", "quarp map build",
            static text => MapTextCompiler.CompileMap(text, "map.csv"));
        return problems;
    }

    private static int CheckBank(
        string root, string textName, string binaryName, string rebuild, Func<string, byte[]> compile)
    {
        string textPath = Path.Combine(root, textName);
        string binaryPath = Path.Combine(root, binaryName);
        if (!File.Exists(textPath))
        {
            // A hand-made or exported bank with no text source beside it is legitimate — the
            // console reads the binary and never looks for the text — so this is a fact, not a
            // finding. Nothing at all is worth a line only when neither file is there.
            if (File.Exists(binaryPath))
            {
                Console.WriteLine($"  bank    {binaryName} present, no {textName} to check it against");
            }
            return 0;
        }

        byte[] expected;
        try
        {
            expected = compile(File.ReadAllText(textPath));
        }
        catch (CartLoadException e)
        {
            // "sfx.txt:14: ..." — the source is broken, so nothing can be said about the bank.
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }

        if (!File.Exists(binaryPath))
        {
            // Unreachable for a folder cart as things stand: CartSource.RequireBuiltAsset refuses
            // to load one whose source has no built bank, so the load above has already failed
            // with the same sentence. Kept, and deliberately worded the same way, because this is
            // the only other place that asks the question — if that check ever narrows, the build
            // must not be the thing that silently stopped asking.
            Console.Error.WriteLine(
                $"quarp: {textName} is present but {binaryName} is not — the console reads the compiled bank. "
                + $"Run '{rebuild} {root}' to build it.");
            return 1;
        }
        byte[] actual;
        try
        {
            actual = File.ReadAllBytes(binaryPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            Console.Error.WriteLine(
                $"quarp: {binaryName} does not match {textName} ({actual.Length} bytes on disk, "
                + $"{expected.Length} compiled); run '{rebuild} {root}'.");
            return 1;
        }
        Console.WriteLine($"  bank    {binaryName} is up to date with {textName}");
        return 0;
    }

    /// <summary>
    /// The four data banks as the console will see them, counted out of
    /// <see cref="CartData"/> rather than off the disk — so the numbers are identical for a
    /// folder cart and for the <c>.quarp8</c> packed from it, and an asset that failed to reach
    /// the cartridge reads as an empty bank here instead of as a file that exists.
    /// </summary>
    private static string DescribeAssets(CartData cart)
    {
        int sfxSlots = 0;
        for (int slot = 0; slot < AudioFormat.SfxSlotCount; slot++)
        {
            if (AudioFormat.SlotLength(cart.Sfx, slot) > 0)
            {
                sfxSlots++;
            }
        }
        int patterns = 0;
        for (int pattern = 0; pattern < AudioFormat.MusicPatternCount; pattern++)
        {
            if (!AudioFormat.PatternIsEmpty(cart.Music, pattern))
            {
                patterns++;
            }
        }
        return $"gfx {NonZero(cart.Gfx)} of {cart.Gfx.Length} pixels drawn, "
            + $"map {NonZero(cart.Map)} of {cart.Map.Length} cells filled, "
            + $"flags {NonZero(cart.Flags)} of {cart.Flags.Length} sprites tagged, "
            + $"sfx {sfxSlots} of {AudioFormat.SfxSlotCount} slots, "
            + $"music {patterns} of {AudioFormat.MusicPatternCount} patterns";
    }

    private static int NonZero(ReadOnlySpan<byte> bank)
    {
        int count = 0;
        for (int i = 0; i < bank.Length; i++)
        {
            if (bank[i] != 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>English, not "1 error(s)": this report is read by a human on every F5.</summary>
    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}

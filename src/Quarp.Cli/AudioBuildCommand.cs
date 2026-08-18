using Quarp.CartKit;

namespace Quarp.Cli;

/// <summary>
/// <c>quarp audio build &lt;cart&gt; [--check]</c> — compiles the cart folder's <c>sfx.txt</c> and
/// <c>music.txt</c> into <c>sfx.bin</c> and <c>music.bin</c> (docs/AUDIO-FORMAT.md). Until the
/// tracker of M9 exists, this is how a cartridge gets sound: the text is the instrument, this
/// command is the compiler, and the binaries are what ships inside the <c>.quarp8</c>.
///
/// <para><c>--check</c> compiles but writes nothing and fails when the committed binary differs
/// from what the text says it should be. That is the CI form: the banks are generated files that
/// live in git next to their source, and the only thing keeping a generated file honest is
/// somebody rebuilding it and comparing. Compilation is a pure function of the text, so the
/// comparison is byte-exact on every machine and architecture.</para>
/// </summary>
public static class AudioBuildCommand
{
    private const string Usage = "usage: quarp audio build <cart> [--check]";

    /// <summary>Entry point for the <c>audio</c> command group; <paramref name="args"/> starts at the subcommand.</summary>
    public static int Invoke(string[] args)
    {
        string? sub = args.Length > 0 ? args[0] : null;
        if (sub != "build")
        {
            Console.Error.WriteLine(sub is null
                ? "usage: quarp audio <build> ..."
                : $"quarp audio: unknown subcommand '{sub}'");
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
                Console.Error.WriteLine($"quarp audio build: unknown argument '{args[i]}' ({Usage})");
                return 1;
            }
        }

        string root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine(File.Exists(root)
                ? $"quarp: {folder} is a file; audio build works on a cart folder, because that is where the "
                    + "sfx.txt and music.txt sources live."
                : $"quarp: cart folder not found: {root}");
            return 1;
        }

        string sfxText = Path.Combine(root, "sfx.txt");
        string musicText = Path.Combine(root, "music.txt");
        if (!File.Exists(sfxText) && !File.Exists(musicText))
        {
            Console.Error.WriteLine(
                $"quarp: {root} has neither sfx.txt nor music.txt — nothing to build. "
                + "See docs/AUDIO-FORMAT.md for the text format.");
            return 1;
        }

        try
        {
            bool ok = true;
            if (File.Exists(sfxText))
            {
                byte[] payload = AudioTextCompiler.CompileSfx(File.ReadAllText(sfxText), "sfx.txt");
                ok &= Emit(root, "sfx.txt", "sfx.bin", AudioFormat.WriteSfxFile(payload), check,
                    DescribeSfx(payload));
            }
            if (File.Exists(musicText))
            {
                byte[] payload = AudioTextCompiler.CompileMusic(File.ReadAllText(musicText), "music.txt");
                ok &= Emit(root, "music.txt", "music.bin", AudioFormat.WriteMusicFile(payload), check,
                    DescribeMusic(payload));
            }
            return ok ? 0 : 1;
        }
        catch (CartLoadException e)
        {
            // Already carries "file:line: message" — printing it as-is is the whole point.
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
    private static bool Emit(string root, string textName, string binaryName, byte[] bytes, bool check, string summary)
    {
        string path = Path.Combine(root, binaryName);
        if (!check)
        {
            File.WriteAllBytes(path, bytes);
            Console.WriteLine($"{textName} -> {binaryName}  ({bytes.Length} bytes, {summary})");
            return true;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"quarp: {binaryName} is missing; run 'quarp audio build {root}'.");
            return false;
        }
        byte[] existing = File.ReadAllBytes(path);
        if (!existing.AsSpan().SequenceEqual(bytes))
        {
            Console.Error.WriteLine(
                $"quarp: {binaryName} does not match {textName} ({existing.Length} bytes on disk, {bytes.Length} "
                + $"compiled); run 'quarp audio build {root}' and commit the result.");
            return false;
        }
        Console.WriteLine($"{binaryName} is up to date with {textName}  ({summary})");
        return true;
    }

    private static string DescribeSfx(byte[] payload)
    {
        int used = 0;
        for (int slot = 0; slot < AudioFormat.SfxSlotCount; slot++)
        {
            if (AudioFormat.SlotLength(payload, slot) > 0)
            {
                used++;
            }
        }
        return $"{used} of {AudioFormat.SfxSlotCount} slots used";
    }

    private static string DescribeMusic(byte[] payload)
    {
        int used = 0;
        for (int pattern = 0; pattern < AudioFormat.MusicPatternCount; pattern++)
        {
            if (!AudioFormat.PatternIsEmpty(payload, pattern))
            {
                used++;
            }
        }
        return $"{used} of {AudioFormat.MusicPatternCount} patterns used";
    }
}

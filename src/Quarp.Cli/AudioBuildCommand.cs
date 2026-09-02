using Quarp.CartKit;

namespace Quarp.Cli;

/// <summary>
/// The <c>audio</c> command group: <c>build</c> and <c>check</c>.
///
/// <para><c>quarp audio build &lt;cart&gt; [--check]</c> compiles the cart folder's
/// <c>sfx.txt</c> and <c>music.txt</c> into <c>sfx.bin</c> and <c>music.bin</c>
/// (docs/AUDIO-FORMAT.md). The text is the instrument, this command is the compiler, and the
/// binaries are what ships inside the <c>.quarp8</c>.</para>
///
/// <para><c>--check</c> compiles but writes nothing and fails when the committed binary differs
/// from what the text says it should be. That is the CI form: the banks are generated files that
/// live in git next to their source, and the only thing keeping a generated file honest is
/// somebody rebuilding it and comparing. Compilation is a pure function of the text, so the
/// comparison is byte-exact on every machine and architecture.</para>
///
/// <para><c>quarp audio check &lt;cart&gt;</c> reads the banks that are on disk — not the text —
/// and reports what they hold: version, geometry, how much is used. It is the one command that
/// answers "is this file a bank at all", which is what a tracker, a bug report and a
/// hand-written data bank all need.</para>
///
/// <para>There is no <c>upgrade</c>. ADR-041 left the console one music format, so there is
/// nothing to upgrade from and nothing to upgrade to.</para>
/// </summary>
public static class AudioBuildCommand
{
    private const string Usage = "usage: quarp audio build <cart> [--check]";

    private const string CheckUsage = "usage: quarp audio check <cart>";

    /// <summary>Entry point for the <c>audio</c> command group; <paramref name="args"/> starts at the subcommand.</summary>
    public static int Invoke(string[] args)
    {
        string? sub = args.Length > 0 ? args[0] : null;
        switch (sub)
        {
            case "build":
                return Build(args[1..]);
            case "check":
                return Check(args[1..]);
            default:
                Console.Error.WriteLine(sub is null
                    ? "usage: quarp audio <build|check> ..."
                    : $"quarp audio: unknown subcommand '{sub}'");
                Console.Error.WriteLine("  " + Usage);
                Console.Error.WriteLine("  " + CheckUsage);
                return 1;
        }
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
        int patterns = 0;
        int rows = 0;
        for (int pattern = 0; pattern < AudioFormat.MusicPatternCount; pattern++)
        {
            int patternRows = MusicFormat.PatternRows(payload, pattern);
            if (patternRows > 0)
            {
                patterns++;
                rows += patternRows;
            }
        }
        int instruments = 0;
        for (int i = 0; i < MusicFormat.InstrumentCount; i++)
        {
            if (MusicFormat.InstrumentSlot(payload, i) != 0
                || MusicFormat.InstrumentRoot(payload, i) != 0
                || MusicFormat.InstrumentFlags(payload, i) != 0
                || MusicFormat.InstrumentSpeed(payload, i) != 0)
            {
                instruments++;
            }
        }
        return $"{MusicFormat.OrderLength(payload)} order entries, {patterns} of "
            + $"{AudioFormat.MusicPatternCount} patterns ({rows} rows), {instruments} instruments";
    }

    /// <summary>
    /// <c>quarp audio check &lt;cart&gt;</c> — reads the banks on disk and says what they are.
    /// Unlike <c>build --check</c> this never looks at the text: it answers the question a
    /// tracker, a bug report and a hand-written data bank all ask, which is "what is actually in
    /// this file". A bank that breaks a rule is refused with the same sentence the loader would
    /// use, which is the point of having one owner for the rules.
    /// </summary>
    private static int Check(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine(CheckUsage);
            return 1;
        }
        string root = Path.GetFullPath(args[0]);
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"quarp: cart folder not found: {root}");
            return 1;
        }
        try
        {
            bool any = false;
            string sfxPath = Path.Combine(root, "sfx.bin");
            if (File.Exists(sfxPath))
            {
                byte[] payload = AudioFormat.ParseSfxFile(File.ReadAllBytes(sfxPath), "sfx.bin");
                Console.WriteLine(
                    $"sfx.bin   {payload.Length + AudioFormat.HeaderSize} bytes, {DescribeSfx(payload)}");
                any = true;
            }
            string musicPath = Path.Combine(root, "music.bin");
            if (File.Exists(musicPath))
            {
                byte[] payload = AudioFormat.ParseMusicFile(File.ReadAllBytes(musicPath), "music.bin");
                Console.WriteLine(
                    $"music.bin {payload.Length + AudioFormat.HeaderSize} bytes, {DescribeMusic(payload)}");
                any = true;
            }
            if (!any)
            {
                // Silence is a valid cartridge (AUDIO-FORMAT §1), so this is a report, not a failure.
                Console.WriteLine("no sfx.bin and no music.bin — this cartridge is silent, which is legal.");
            }
            return 0;
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
    }
}

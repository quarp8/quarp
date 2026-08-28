using System.IO.Compression;
using System.Text;

namespace Quarp.CartKit;

/// <summary>
/// Loads cartridge content from its two physical shapes — a working folder or a packed
/// .quarp8 zip with the same layout — and enforces the load-time limits (M1 work order
/// "Форматы"): manifest sanity, exact asset sizes, the code budget, the package size cap.
/// Everything user-facing fails with <see cref="CartLoadException"/>.
/// </summary>
public static class CartSource
{
    /// <summary>Cap on one decompressed zip entry; keeps a hostile package from ballooning in memory.</summary>
    private const int MaxEntryBytes = 4 * 1024 * 1024;

    /// <summary>Loads a cart from either shape: an existing directory or a .quarp8 file.</summary>
    public static CartData Load(string path)
    {
        if (Directory.Exists(path))
        {
            return LoadFolder(path);
        }
        if (File.Exists(path))
        {
            return LoadPackage(path);
        }
        throw new CartLoadException($"cartridge not found: {path} (expected a cart folder or a .quarp8 file).");
    }

    public static CartData LoadFolder(string folder)
    {
        string root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            throw new CartLoadException($"cartridge folder not found: {root}");
        }

        string manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new CartLoadException($"{root}: no manifest.json — not a cartridge folder.");
        }
        CartManifest manifest = CartManifest.Parse(File.ReadAllBytes(manifestPath));

        string srcDir = Path.Combine(root, "src");
        if (!Directory.Exists(srcDir))
        {
            throw new CartLoadException($"{root}: no src folder — a cartridge needs at least one .cs file in src/.");
        }
        string[] files = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new CartLoadException($"{root}: src folder contains no .cs files.");
        }
        var sources = new List<CartSourceFile>(files.Length);
        foreach (string file in files)
        {
            string relative = "src/" + Path.GetRelativePath(srcDir, file).Replace('\\', '/');
            // The third field is the absolute path of the file — rooted because `root` is, and
            // present only here: this is the one cartridge shape whose sources exist on disk, so
            // it is the one shape a debugger can bind a breakpoint in (M4 Р1). It takes no part
            // in identity or the code budget; see CartSourceFile.
            sources.Add(new CartSourceFile(relative, File.ReadAllText(file), file));
        }
        // Sort by the cart-relative path with '/' — the very key LoadPackage sorts by.
        // Sorting the OS paths instead compares '\' (0x5C) where the package compares
        // '/' (0x2F), which reorders a nested folder against a sibling file whose name
        // starts with an uppercase letter (src/Ui/a.cs vs src/Ux.cs) — a folder cart and
        // its .quarp8 would then hand Roslyn the same files in a different order.
        SortByRelativePath(sources);

        byte[] gfx = LoadGfx(ReadOptionalFile(root, "gfx.png"));
        byte[] map = LoadFixedSize(ReadOptionalFile(root, "map.bin"), "map.bin", CartData.MapWidth * CartData.MapHeight);
        byte[] flags = LoadFixedSize(ReadOptionalFile(root, "flags.bin"), "flags.bin", CartData.FlagCount);

        // A folder cart is the only shape where the audio *sources* can be present, so it is the
        // only place that can catch the one confusing failure mode: an author edits sfx.txt,
        // never runs the compiler, and hears nothing for reasons the console cannot explain.
        RequireBuiltAsset(root, "sfx.txt", "sfx.bin", "audio build");
        RequireBuiltAsset(root, "music.txt", "music.bin", "audio build");
        RequireBuiltAsset(root, "map.csv", "map.bin", "map build");
        byte[] sfx = LoadSfx(ReadOptionalFile(root, "sfx.bin"));
        byte[] music = LoadMusic(ReadOptionalFile(root, "music.bin"));
        byte[][] dataBanks = LoadFolderDataBanks(root);

        CodeBudget.Validate(sources);
        return new CartData
        {
            Manifest = manifest,
            Sources = sources,
            Gfx = gfx,
            Map = map,
            Flags = flags,
            Sfx = sfx,
            Music = music,
            DataBanks = dataBanks,
        };
    }

    /// <summary>
    /// Reads <c>data/00.bin</c>..<c>data/63.bin</c> (ADR-035). A folder without <c>data/</c>, or
    /// with only some of the numbers, is normal: absent banks stay empty. Names that are not
    /// exactly two digits in range are ignored the same way the package ignores unknown entries —
    /// but a bank over the size limits is an error, because silently truncating a cartridge's
    /// data would show up as a corrupted level, not as a diagnostic.
    /// </summary>
    private static byte[][] LoadFolderDataBanks(string root)
    {
        byte[][] banks = CartData.EmptyDataBanks();
        string dir = Path.Combine(root, "data");
        if (!Directory.Exists(dir))
        {
            return banks;
        }
        long total = 0;
        for (int i = 0; i < CartData.DataBankCount; i++)
        {
            string path = Path.Combine(dir, BankFileName(i));
            if (!File.Exists(path))
            {
                continue;
            }
            byte[] bytes = File.ReadAllBytes(path);
            total = ValidateBank(bytes, BankEntryName(i), total);
            banks[i] = bytes;
        }
        return banks;
    }

    /// <summary>The file name of bank <paramref name="bank"/>: two decimal digits (ADR-035).</summary>
    public static string BankFileName(int bank) => $"{bank:00}.bin";

    /// <summary>The package entry name of bank <paramref name="bank"/> (ADR-035).</summary>
    /// <summary>Folder every data bank entry lives under, inside the package.</summary>
    public const string BankEntryPrefix = "data/";

    public static string BankEntryName(int bank) => $"{BankEntryPrefix}{bank:00}.bin";

    /// <summary>
    /// The two size limits of ADR-035, checked as the banks are read so the message names the
    /// bank rather than the sum. Returns the running total including this bank.
    /// </summary>
    private static long ValidateBank(byte[] bytes, string entryName, long runningTotal)
    {
        if (bytes.Length > CartData.DataBankMaxBytes)
        {
            throw new CartLoadException(
                $"{entryName}: data bank is {bytes.Length} bytes, over the {CartData.DataBankMaxBytes}-byte "
                + "per-bank limit (SPEC-8 §6, ADR-035).");
        }
        long total = runningTotal + bytes.Length;
        if (total > CartData.DataBanksMaxTotalBytes)
        {
            throw new CartLoadException(
                $"{entryName}: data banks total {total} bytes, over the {CartData.DataBanksMaxTotalBytes}-byte "
                + "limit for all banks together (SPEC-8 §6, ADR-035).");
        }
        return total;
    }

    /// <summary>
    /// Maps a package entry name to its bank number, or -1 when the name is not
    /// <c>data/NN.bin</c> with <c>NN</c> two digits in range. Ordinal and case-sensitive:
    /// SPEC-8 §6 requires package names to match byte for byte.
    /// </summary>
    private static int BankNumberOf(string entryName)
    {
        for (int i = 0; i < CartData.DataBankCount; i++)
        {
            if (string.Equals(entryName, BankEntryName(i), StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    public static CartData LoadPackage(string quarp8File)
    {
        string path = Path.GetFullPath(quarp8File);
        if (!File.Exists(path))
        {
            throw new CartLoadException($"package not found: {path}");
        }
        string fileName = Path.GetFileName(path);
        // Two limits, and they are different questions. The file may be as large as the
        // budget plus every bank ADR-035 allows — checked here, before the zip is opened, so a
        // wrong file is refused without being read. The budget itself covers everything except
        // the banks and can only be measured once the entries are known: see below.
        long fileSize = new FileInfo(path).Length;
        if (fileSize > Quarp8Package.MaxFileBytes)
        {
            throw new CartLoadException(
                $"{fileName}: package is {fileSize} bytes, over the {Quarp8Package.MaxFileBytes}-byte limit (SPEC-8 §6).");
        }

        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException e)
        {
            throw new CartLoadException($"{fileName}: not a valid .quarp8 (zip) file.", e);
        }

        using (zip)
        {
            long budgeted = Quarp8Package.BudgetedBytes(zip);
            if (budgeted > Quarp8Package.MaxPackageBytes)
            {
                throw new CartLoadException(
                    $"{fileName}: package is {budgeted} bytes without data banks, over the "
                    + $"{Quarp8Package.MaxPackageBytes}-byte limit (SPEC-8 §6).");
            }

            byte[]? manifestBytes = null;
            var sourceEntries = new List<ZipArchiveEntry>();
            ZipArchiveEntry? gfxEntry = null;
            ZipArchiveEntry? mapEntry = null;
            ZipArchiveEntry? flagsEntry = null;
            ZipArchiveEntry? sfxEntry = null;
            ZipArchiveEntry? musicEntry = null;
            var dataEntries = new ZipArchiveEntry?[CartData.DataBankCount];
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith("/", StringComparison.Ordinal))
                {
                    continue; // Directory entries.
                }
                switch (name)
                {
                    case "manifest.json":
                        manifestBytes = ReadEntry(entry, name);
                        break;
                    case "gfx.png":
                        gfxEntry = entry;
                        break;
                    case "map.bin":
                        mapEntry = entry;
                        break;
                    case "flags.bin":
                        flagsEntry = entry;
                        break;
                    case "sfx.bin":
                        sfxEntry = entry;
                        break;
                    case "music.bin":
                        musicEntry = entry;
                        break;
                    default:
                        if (name.StartsWith("src/", StringComparison.Ordinal)
                            && name.EndsWith(".cs", StringComparison.Ordinal))
                        {
                            sourceEntries.Add(entry);
                            break;
                        }
                        int bank = BankNumberOf(name);
                        if (bank >= 0)
                        {
                            dataEntries[bank] = entry;
                        }
                        break;
                }
            }

            if (manifestBytes is null)
            {
                throw new CartLoadException(
                    $"{fileName}: package has no manifest.json at its root (the .quarp8 layout is the cart folder "
                    + "zipped directly, without a wrapping folder).");
            }
            CartManifest manifest = CartManifest.Parse(manifestBytes);
            if (sourceEntries.Count == 0)
            {
                throw new CartLoadException($"{fileName}: package contains no src/*.cs sources.");
            }
            var sources = new List<CartSourceFile>(sourceEntries.Count);
            foreach (ZipArchiveEntry entry in sourceEntries)
            {
                string name = entry.FullName.Replace('\\', '/');
                // No disk path: the sources live inside the zip. Source-level debugging is a
                // folder-cart feature and unpacking to a temp folder to fake one is explicitly
                // out of scope (M4 Р1) — a made-up path would only make the debugger bind a
                // breakpoint to a file the author is not editing.
                sources.Add(new CartSourceFile(name, DecodeUtf8(ReadEntry(entry, name))));
            }
            SortByRelativePath(sources);   // Same key as LoadFolder — see the note there.

            byte[] gfx = LoadGfx(gfxEntry is null ? null : ReadEntry(gfxEntry, "gfx.png"));
            byte[] map = LoadFixedSize(
                mapEntry is null ? null : ReadEntry(mapEntry, "map.bin"),
                "map.bin", CartData.MapWidth * CartData.MapHeight);
            byte[] flags = LoadFixedSize(
                flagsEntry is null ? null : ReadEntry(flagsEntry, "flags.bin"),
                "flags.bin", CartData.FlagCount);
            byte[] sfx = LoadSfx(sfxEntry is null ? null : ReadEntry(sfxEntry, "sfx.bin"));
            byte[] music = LoadMusic(musicEntry is null ? null : ReadEntry(musicEntry, "music.bin"));
            byte[][] dataBanks = CartData.EmptyDataBanks();
            long dataTotal = 0;
            for (int i = 0; i < CartData.DataBankCount; i++)
            {
                ZipArchiveEntry? bankEntry = dataEntries[i];
                if (bankEntry is null)
                {
                    continue;
                }
                byte[] bytes = ReadEntry(bankEntry, BankEntryName(i));
                dataTotal = ValidateBank(bytes, BankEntryName(i), dataTotal);
                dataBanks[i] = bytes;
            }

            CodeBudget.Validate(sources);
            return new CartData
            {
                Manifest = manifest,
                Sources = sources,
                Gfx = gfx,
                Map = map,
                Flags = flags,
                Sfx = sfx,
                Music = music,
                DataBanks = dataBanks,
            };
        }
    }

    /// <summary>
    /// The one ordering both cart shapes agree on: ordinal on the cart-relative path with
    /// '/' separators. Compilation order is part of the cart's identity — it decides which
    /// duplicate-definition error the author sees first, and it must not depend on whether
    /// the cart is a folder or a package.
    /// </summary>
    private static void SortByRelativePath(List<CartSourceFile> sources) =>
        sources.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

    private static byte[]? ReadOptionalFile(string root, string name)
    {
        string path = Path.Combine(root, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static byte[] LoadGfx(byte[]? pngBytes) =>
        pngBytes is null
            ? new byte[CartData.GfxWidth * CartData.GfxHeight]
            : PngDecoder.DecodeToPaletteIndices(pngBytes, CartData.GfxWidth, CartData.GfxHeight, "gfx.png");

    /// <summary>
    /// The SFX bank, header stripped and fully validated; absent means an all-zero bank, which
    /// is 64 empty slots — silence is a valid cartridge, not a load error.
    /// </summary>
    private static byte[] LoadSfx(byte[]? bytes) =>
        bytes is null ? AudioFormat.EmptySfxPayload() : AudioFormat.ParseSfxFile(bytes, "sfx.bin");

    /// <summary>The music bank, same deal: absent means 64 empty patterns.</summary>
    private static byte[] LoadMusic(byte[]? bytes) =>
        bytes is null ? AudioFormat.EmptyMusicPayload() : AudioFormat.ParseMusicFile(bytes, "music.bin");

    /// <summary>
    /// Refuses a folder that has the audio source but not the compiled bank. The alternative —
    /// loading silence — is the worst outcome available: the cart works, sounds wrong, and
    /// nothing anywhere says why. The message names the command that fixes it.
    /// </summary>
    /// <summary>
    /// Refuses a folder that has an asset's source text but not the binary the console actually
    /// reads. The console never compiles <c>sfx.txt</c>, <c>music.txt</c> or <c>map.csv</c> on
    /// load — they are author files, built by a CLI command — so without this the forgotten
    /// build step is completely silent: the cartridge runs with a bank of rests or an empty map
    /// and the author goes looking for the bug in their own code. That silence is exactly what
    /// this project refuses everywhere else, so the check names the file, the missing binary and
    /// the command that fixes it.
    ///
    /// <para>The reverse case is deliberately legal: a binary without its source is a cartridge
    /// somebody shipped built, which is the normal shape of <c>.quarp8</c> and of a cart cloned
    /// without its authoring files.</para>
    /// </summary>
    private static void RequireBuiltAsset(string root, string textName, string binaryName, string command)
    {
        if (File.Exists(Path.Combine(root, textName)) && !File.Exists(Path.Combine(root, binaryName)))
        {
            string name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            throw new CartLoadException(
                $"{root}: {textName} is present but {binaryName} is not — the console reads the compiled file. "
                + $"Run 'quarp {command} {(name.Length == 0 ? root : name)}' to build it.");
        }
    }

    private static byte[] LoadFixedSize(byte[]? bytes, string name, int expectedLength)
    {
        if (bytes is null)
        {
            return new byte[expectedLength]; // Absent assets = zeros (Format spec v1).
        }
        if (bytes.Length != expectedLength)
        {
            throw new CartLoadException($"{name}: {bytes.Length} bytes, must be exactly {expectedLength}.");
        }
        return bytes;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, string name)
    {
        try
        {
            using Stream stream = entry.Open();
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int total = 0;
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > MaxEntryBytes)
                {
                    throw new CartLoadException($"{name}: decompressed entry exceeds {MaxEntryBytes} bytes — refusing to load.");
                }
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (InvalidDataException e)
        {
            throw new CartLoadException($"{name}: corrupt zip entry.", e);
        }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}

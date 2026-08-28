using System.IO.Compression;

namespace Quarp.CartKit;

/// <summary>
/// Writes .quarp8 packages: a zip of the cart folder layout (manifest.json, src/**/*.cs,
/// gfx.png, map.bin, flags.bin, sfx.bin, music.bin, cover.png), fully validated before
/// writing — manifest, code budget, gfx palette, asset sizes, audio banks — and size-capped
/// after (SPEC-8 §6: the packed file is at most 320 KB). Entry timestamps are fixed so
/// identical input folders pack into identical bytes.
///
/// <para>320 KB set by the owner's ratification-grill decision, 2026-08-19 (ADR-024), from
/// the arithmetic "256 KB of raw code + ~31 KB of raw assets + margin": a cartridge at the
/// code budget with every optional asset present still fits even with <b>zero</b> compression
/// benefit, so <see cref="CodeBudget.MaxBytes"/> is the limit an author actually runs into —
/// this check exists as a predictable backstop, not a limit anyone is meant to design against.</para>
/// </summary>
public static class Quarp8Package
{
    /// <summary>
    /// The budget every part of a cartridge except its data banks has to fit in, compressed.
    ///
    /// <para>Data banks are excluded, and the exclusion is the point. The arithmetic behind
    /// this number — 256 KB of code plus about 31 KB of assets, raw, so that the code budget
    /// is the limit an author actually meets — was written before ADR-035, and ADR-035
    /// ratified up to 4 MiB of banks. That left a cartridge the console explicitly permits
    /// unable to satisfy its own package limit. The POOM port hit it with 514 KB of banks for
    /// six levels; nobody had reached it earlier because <c>quarp build</c> does not package.
    /// Banks carry their own predictable limits (<see cref="CartData.DataBankMaxBytes"/> each,
    /// <see cref="CartData.DataBanksMaxTotalBytes"/> together, checked on both forms), so both
    /// ceilings stay predictable on their own — which is the whole of ADR-024's argument.</para>
    /// </summary>
    public const long MaxPackageBytes = 327680;

    /// <summary>
    /// The largest a <c>.quarp8</c> file can be at all: this budget plus every byte of banks
    /// the console allows. A file over it is refused before the zip is opened, so a
    /// mis-named multi-gigabyte file is not read into memory to be rejected.
    /// </summary>
    public const long MaxFileBytes = MaxPackageBytes + CartData.DataBanksMaxTotalBytes;

    /// <summary>
    /// Compressed bytes of everything the package budget covers — every entry that is not a
    /// data bank. Counted from the archive rather than from the files, because the limit is on
    /// the packed form.
    /// </summary>
    public static long BudgetedBytes(ZipArchive zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(CartSource.BankEntryPrefix, StringComparison.Ordinal))
            {
                total += entry.CompressedLength;
            }
        }
        return total;
    }

    private static readonly DateTimeOffset FixedEntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Pack(string folder, string outFile)
    {
        string root = Path.GetFullPath(folder);
        // Full user-facing validation: manifest, sources, code budget, gfx palette, bin sizes.
        CartSource.LoadFolder(root);

        string outPath = Path.GetFullPath(outFile);
        string? outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using (var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            AddFile(zip, Path.Combine(root, "manifest.json"), "manifest.json");

            string srcDir = Path.Combine(root, "src");
            string[] files = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
            var entries = new List<(string Name, string Path)>(files.Length);
            foreach (string file in files)
            {
                entries.Add(("src/" + Path.GetRelativePath(srcDir, file).Replace('\\', '/'), file));
            }
            // Ordered by entry name, the same key CartSource sorts by: entry order in the
            // package then matches the order a folder cart compiles in, and the packed
            // bytes stay identical whatever order the file system enumerated.
            entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach ((string name, string path) in entries)
            {
                AddFile(zip, path, name);
            }

            AddOptionalFile(zip, root, "gfx.png");
            AddOptionalFile(zip, root, "map.bin");
            AddOptionalFile(zip, root, "flags.bin");
            // The compiled banks only: sfx.txt and music.txt are sources, like a .aseprite next
            // to gfx.png, and SPEC-8 §6 lists exactly what a package contains.
            AddOptionalFile(zip, root, "sfx.bin");
            AddOptionalFile(zip, root, "music.bin");
            AddOptionalFile(zip, root, "cover.png");
            // Data banks last and in numeric order (ADR-035): the packer's entry order is part
            // of the format, so two packs of an unchanged folder stay byte-identical.
            for (int bank = 0; bank < CartData.DataBankCount; bank++)
            {
                string bankPath = Path.Combine(root, "data", CartSource.BankFileName(bank));
                if (File.Exists(bankPath))
                {
                    AddFile(zip, bankPath, CartSource.BankEntryName(bank));
                }
            }
        }

        long budgeted;
        using (ZipArchive written = ZipFile.OpenRead(outPath))
        {
            budgeted = BudgetedBytes(written);
        }
        if (budgeted > MaxPackageBytes)
        {
            File.Delete(outPath);
            throw new CartLoadException(
                $"{Path.GetFileName(outPath)}: packed size is {budgeted} bytes without data banks, "
                + $"over the {MaxPackageBytes}-byte limit (SPEC-8 §6).");
        }
    }

    private static void AddOptionalFile(ZipArchive zip, string root, string name)
    {
        string path = Path.Combine(root, name);
        if (File.Exists(path))
        {
            AddFile(zip, path, name);
        }
    }

    private static void AddFile(ZipArchive zip, string absolutePath, string entryName)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);
        entry.LastWriteTime = FixedEntryTime;
        using Stream target = entry.Open();
        using FileStream source = File.OpenRead(absolutePath);
        source.CopyTo(target);
    }
}

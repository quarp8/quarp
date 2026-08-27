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
    public const long MaxPackageBytes = 327680;

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

        long size = new FileInfo(outPath).Length;
        if (size > MaxPackageBytes)
        {
            File.Delete(outPath);
            throw new CartLoadException(
                $"{Path.GetFileName(outPath)}: packed size is {size} bytes, over the {MaxPackageBytes}-byte limit (SPEC-8 §6).");
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

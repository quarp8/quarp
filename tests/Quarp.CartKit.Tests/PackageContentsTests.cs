using System.IO.Compression;
using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// What a <c>.quarp8</c> contains, pinned as a list rather than as a habit. SPEC-8 §6 names the
/// files a package holds, and that sentence becomes permanent when the specification is ratified
/// at the end of M4 — so it is asserted here against the packer instead of being remembered.
///
/// <para>The cart built by this fixture is deliberately maximal on both sides: every asset the
/// format allows is present, and so is every kind of file that lives in a working folder and has
/// no business travelling — the two audio sources, the map source, the dev folders written by
/// <c>quarp new</c>, recorded replays, a save file, sprite-editor project files and loose notes.
/// The single assertion that the entry names are <em>exactly</em> the nine allowed ones covers
/// every one of them at once, and covers files nobody has invented yet.</para>
///
/// <para>Companion tests keep the individual dev-only paths honest one at a time:
/// <c>DevProjectIsolationTests</c> for <c>.quarp/</c>, <c>VsCodeFolderIsolationTests</c> for
/// <c>.vscode/</c>, <c>CartFormatMapSourceIsolationTests</c> for <c>map.csv</c>. This file is
/// the whole-package view they share.</para>
/// </summary>
public class PackageContentsTests : IDisposable
{
    /// <summary>
    /// The complete package manifest of SPEC-8 §6, in the order the packer writes it: the
    /// manifest first, then the sources ordered by their cart-relative path, then the assets.
    /// </summary>
    private static readonly string[] ExpectedEntries =
    {
        "manifest.json",
        "src/lib/util.cs",
        "src/main.cs",
        "gfx.png",
        "map.bin",
        "flags.bin",
        "sfx.bin",
        "music.bin",
        "cover.png",
    };

    /// <summary>
    /// The wall-clock timestamp <see cref="Quarp8Package"/> stamps on every entry. Pinned because
    /// it is the mechanism behind a property the project depends on — packing the same folder
    /// twice gives byte-identical files — and a mechanism only ever observed through its symptom
    /// tends to get "simplified" away.
    ///
    /// <para>A <see cref="DateTime"/> rather than a <see cref="DateTimeOffset"/>, deliberately:
    /// a zip stores DOS date and time with no zone at all, so the getter hands the wall clock
    /// back wearing the reader's local offset. Comparing offsets would make this test pass in
    /// London and fail everywhere else.</para>
    /// </summary>
    private static readonly DateTime FixedEntryTime = new(2000, 1, 1, 0, 0, 0);

    private readonly string _root;
    private readonly string _folder;

    public PackageContentsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-pkg-" + Guid.NewGuid().ToString("N"));
        _folder = Path.Combine(_root, "cart");
        Directory.CreateDirectory(Path.Combine(_folder, "src", "lib"));
        Directory.CreateDirectory(Path.Combine(_folder, ".vscode"));
        Directory.CreateDirectory(Path.Combine(_folder, ".quarp", "obj"));
        Directory.CreateDirectory(Path.Combine(_folder, "replays"));

        // --- everything the format allows ---
        File.WriteAllText(Path.Combine(_folder, "manifest.json"),
            "{\"name\":\"full\",\"author\":\"tester\",\"profile\":8}");
        File.WriteAllText(Path.Combine(_folder, "src", "main.cs"), """
            using Quarp.Api;

            public sealed class FullCart : Cartridge
            {
                public override void Draw() => Cls(Util.Background);
            }
            """);
        // Nested on purpose: the loader and the packer both walk src/ recursively, which is not
        // what SPEC-8 §6 used to say ("src/*.cs"). If either were ever narrowed to the top level,
        // this cart would ship without half its code and fail to compile at the player's end.
        File.WriteAllText(Path.Combine(_folder, "src", "lib", "util.cs"),
            "public static class Util { public const int Background = 0; }\n");
        File.WriteAllBytes(Path.Combine(_folder, "gfx.png"), BuildIndexedPng(CartData.GfxWidth, CartData.GfxHeight));
        File.WriteAllBytes(Path.Combine(_folder, "map.bin"), new byte[CartData.MapWidth * CartData.MapHeight]);
        File.WriteAllBytes(Path.Combine(_folder, "flags.bin"), new byte[CartData.FlagCount]);
        File.WriteAllBytes(Path.Combine(_folder, "sfx.bin"), AudioFormat.WriteSfxFile(AudioFormat.EmptySfxPayload()));
        File.WriteAllBytes(Path.Combine(_folder, "music.bin"), AudioFormat.WriteMusicFile(AudioFormat.EmptyMusicPayload()));
        File.WriteAllBytes(Path.Combine(_folder, "cover.png"), BuildIndexedPng(128, 72));

        // --- everything a working folder also accumulates, and none of which ships ---
        File.WriteAllText(Path.Combine(_folder, "sfx.txt"), "# the audio source\n");
        File.WriteAllText(Path.Combine(_folder, "music.txt"), "# the music source\n");
        File.WriteAllText(Path.Combine(_folder, "map.csv"), "0,0,0\n");
        File.WriteAllText(Path.Combine(_folder, ".vscode", "launch.json"), "{ \"version\": \"0.2.0\" }");
        File.WriteAllText(Path.Combine(_folder, ".quarp", "cart.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(_folder, ".quarp", "obj", "cart.AssemblyInfo.cs"), "// <auto-generated/>\n");
        File.WriteAllBytes(Path.Combine(_folder, "replays", "golden.qrpr"), new byte[64]);
        File.WriteAllBytes(Path.Combine(_folder, "save.dat"), new byte[256]);
        File.WriteAllBytes(Path.Combine(_folder, "gfx.aseprite"), new byte[32]);
        File.WriteAllText(Path.Combine(_folder, "README.md"), "how to play\n");
        // Not a source, and under src/ at that: the glob is *.cs everywhere it is applied.
        File.WriteAllText(Path.Combine(_folder, "src", "notes.txt"), "scratch\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
    }

    /// <summary>
    /// A 1-bit-per-pixel indexed PNG of the requested size, every pixel palette index 0 — the
    /// cheapest image the decoder accepts, since index 0 maps to a color that is in the visible
    /// sixteen.
    /// </summary>
    private static byte[] BuildIndexedPng(int width, int height) =>
        PngBuilder.Build(
            width,
            height,
            colorType: 3,
            pixels: new byte[width * height],
            plte: PngBuilder.PlteFromRgb(Palette.Master32[..Palette.VisibleCount].ToArray()));

    private string Pack()
    {
        string package = Path.Combine(_root, "full.quarp8");
        Quarp8Package.Pack(_folder, package);
        return package;
    }

    private static List<string> EntryNames(string package)
    {
        using ZipArchive zip = ZipFile.OpenRead(package);
        var names = new List<string>(zip.Entries.Count);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            names.Add(entry.FullName.Replace('\\', '/'));
        }
        return names;
    }

    /// <summary>
    /// The claim of the whole file: the package holds the nine names SPEC-8 §6 lists and nothing
    /// else — not the sources the assets were built from, not the dev folders, not the replays,
    /// not the save file. An allow-list is asserted as an allow-list: <c>Assert.Equal</c> on the
    /// full sequence, so a tenth entry fails here whatever it is called.
    /// </summary>
    [Fact]
    public void PackageHoldsExactlyTheFilesTheFormatDefines()
    {
        Assert.Equal(ExpectedEntries, EntryNames(Pack()));
    }

    /// <summary>
    /// Directory entries are not written either. A zip may carry them, and a reader that skipped
    /// the check would see an empty name where a file is expected; the packer simply never
    /// creates one, and the loader's own skip is then belt and braces.
    /// </summary>
    [Fact]
    public void PackageHasNoDirectoryEntries()
    {
        Assert.DoesNotContain(EntryNames(Pack()), n => n.EndsWith("/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every entry carries the same fixed timestamp. This is why packing the same folder twice
    /// produces identical bytes: without it the zip would embed the moment of packing, and two
    /// builds of an unchanged cart would differ.
    /// </summary>
    [Fact]
    public void EveryEntryCarriesTheFixedTimestamp()
    {
        using ZipArchive zip = ZipFile.OpenRead(Pack());
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            Assert.Equal(FixedEntryTime, entry.LastWriteTime.DateTime);
        }
    }

    /// <summary>
    /// The package and the folder it came from are the same cartridge, with every optional asset
    /// present. <c>CartIdentityTests</c> makes this claim for a minimal cart; making it again for
    /// a maximal one is what covers the assets, since each of them is hashed separately.
    /// </summary>
    [Fact]
    public void FullyLoadedPackageIsTheSameCartridgeAsItsFolder()
    {
        CartData fromFolder = CartSource.LoadFolder(_folder);
        CartData fromPackage = CartSource.LoadPackage(Pack());

        Assert.Equal(CartIdentity.Compute(fromFolder), CartIdentity.Compute(fromPackage));
        Assert.Equal(
            fromFolder.Sources.Select(s => s.RelativePath),
            fromPackage.Sources.Select(s => s.RelativePath));
    }

    /// <summary>
    /// What the loader does with an entry it does not recognise, pinned because a hand-built or
    /// future package will contain one sooner or later: it is ignored, and the cart loads. The
    /// package here is assembled by hand rather than by the packer — the packer cannot produce
    /// such a file, which is exactly why this case needs its own fixture.
    /// </summary>
    [Fact]
    public void LoaderIgnoresEntriesTheFormatDoesNotName()
    {
        string package = Path.Combine(_root, "extra.quarp8");
        using (var stream = new FileStream(package, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "manifest.json", "{\"name\":\"extra\",\"author\":\"\",\"profile\":8}");
            WriteEntry(zip, "src/main.cs", "using Quarp.Api;\npublic sealed class ExtraCart : Cartridge { }\n");
            WriteEntry(zip, "map.csv", "0,0,0\n");
            WriteEntry(zip, "docs/notes.txt", "left over from somewhere\n");
        }

        CartData data = CartSource.LoadPackage(package);

        Assert.Equal("extra", data.Manifest.Name);
        Assert.Equal("src/main.cs", Assert.Single(data.Sources).RelativePath);
        // map.csv was not read as a map: an absent map.bin is an all-zero map.
        Assert.Equal(new byte[CartData.MapWidth * CartData.MapHeight], data.Map);
    }

    private static void WriteEntry(ZipArchive zip, string name, string text)
    {
        using Stream target = zip.CreateEntry(name).Open();
        using var writer = new StreamWriter(target);
        writer.Write(text);
    }
}

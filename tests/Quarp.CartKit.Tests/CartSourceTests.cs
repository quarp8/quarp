using System.Text;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// Cart loading from both physical shapes (folder and .quarp8 zip), manifest validation,
/// asset size enforcement and the pack round-trip.
/// </summary>
public class CartSourceTests : IDisposable
{
    private readonly string _root;

    public CartSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    private string MakeCartFolder(
        string manifest = """{"name":"test-cart","author":"tester","profile":8}""",
        string mainCs = "using Quarp.Api;\npublic sealed class TestCart : Cartridge { }\n")
    {
        string folder = Path.Combine(_root, "cart");
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(folder, "src", "main.cs"), mainCs);
        return folder;
    }

    // --- folder shape ---

    [Fact]
    public void LoadsAMinimalFolder()
    {
        CartData data = CartSource.Load(MakeCartFolder());
        Assert.Equal("test-cart", data.Manifest.Name);
        Assert.Equal("tester", data.Manifest.Author);
        var source = Assert.Single(data.Sources);
        Assert.Equal("src/main.cs", source.RelativePath);
        // Absent assets come back as zeros, never null (Format spec v1).
        Assert.Equal(new byte[128 * 128], data.Gfx);
        Assert.Equal(new byte[256 * 72], data.Map);
        Assert.Equal(new byte[256], data.Flags);
    }

    [Fact]
    public void SourcesAreSortedByPathForDeterministicCompilation()
    {
        string folder = MakeCartFolder();
        File.WriteAllText(Path.Combine(folder, "src", "zeta.cs"), "class Z { }");
        File.WriteAllText(Path.Combine(folder, "src", "alpha.cs"), "class A { }");
        CartData data = CartSource.Load(folder);
        Assert.Equal(
            new[] { "src/alpha.cs", "src/main.cs", "src/zeta.cs" },
            data.Sources.Select(s => s.RelativePath).ToArray());
    }

    [Fact]
    public void MissingManifestFails()
    {
        string folder = Path.Combine(_root, "empty");
        Directory.CreateDirectory(folder);
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(folder));
        Assert.Contains("manifest.json", e.Message);
    }

    [Fact]
    public void MissingSourcesFail()
    {
        string folder = Path.Combine(_root, "nosrc");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), """{"name":"x","profile":8}""");
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(folder));
        Assert.Contains("src", e.Message);
    }

    [Fact]
    public void NonexistentPathFails()
    {
        var e = Assert.Throws<CartLoadException>(
            () => CartSource.Load(Path.Combine(_root, "no-such-thing")));
        Assert.Contains("not found", e.Message);
    }

    [Fact]
    public void WrongSizeBinAssetFails()
    {
        string folder = MakeCartFolder();
        File.WriteAllBytes(Path.Combine(folder, "map.bin"), new byte[100]);
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(folder));
        Assert.Contains("map.bin", e.Message);
        Assert.Contains("18432", e.Message);    // 256x72
    }

    [Fact]
    public void BinAssetsOfTheRightSizeLoadVerbatim()
    {
        string folder = MakeCartFolder();
        byte[] map = new byte[256 * 72];
        map[5] = 77;
        byte[] flags = new byte[256];
        flags[10] = 3;
        File.WriteAllBytes(Path.Combine(folder, "map.bin"), map);
        File.WriteAllBytes(Path.Combine(folder, "flags.bin"), flags);
        CartData data = CartSource.Load(folder);
        Assert.Equal(77, data.Map[5]);
        Assert.Equal(3, data.Flags[10]);
    }

    // --- manifest ---

    [Theory]
    [InlineData("""{"author":"x","profile":8}""", "name")]
    [InlineData("""{"name":"","profile":8}""", "name")]
    [InlineData("""{"name":"x"}""", "profile")]
    [InlineData("""{"name":"x","profile":16}""", "profile 16")]
    [InlineData("""{"name":"x","profile":"8"}""", "profile")]
    [InlineData("not json at all", "invalid JSON")]
    [InlineData("[1,2,3]", "object")]
    public void BadManifestsFailWithAPointedMessage(string manifest, string expectedFragment)
    {
        var e = Assert.Throws<CartLoadException>(
            () => CartManifest.Parse(Encoding.UTF8.GetBytes(manifest)));
        Assert.Contains(expectedFragment, e.Message);
    }

    [Fact]
    public void AuthorIsOptionalUnknownPropertiesAreIgnored()
    {
        CartManifest manifest = CartManifest.Parse(
            Encoding.UTF8.GetBytes("""{"name":"x","profile":8,"homepage":"https://example.org"}"""));
        Assert.Equal("x", manifest.Name);
        Assert.Equal("", manifest.Author);
    }

    // --- pack + package shape ---

    [Fact]
    public void PackedCartLoadsIdenticallyToTheFolder()
    {
        string folder = MakeCartFolder();
        byte[] map = new byte[256 * 72];
        map[42] = 9;
        File.WriteAllBytes(Path.Combine(folder, "map.bin"), map);
        string package = Path.Combine(_root, "cart.quarp8");
        Quarp8Package.Pack(folder, package);

        CartData fromFolder = CartSource.Load(folder);
        CartData fromPackage = CartSource.Load(package);
        Assert.Equal(fromFolder.Manifest.Name, fromPackage.Manifest.Name);
        Assert.Equal(
            fromFolder.Sources.Select(s => (s.RelativePath, s.Text)),
            fromPackage.Sources.Select(s => (s.RelativePath, s.Text)));
        Assert.Equal(fromFolder.Map, fromPackage.Map);
        Assert.Equal(fromFolder.Gfx, fromPackage.Gfx);
        Assert.Equal(fromFolder.Flags, fromPackage.Flags);
    }

    [Fact]
    public void PackingIsDeterministic()
    {
        string folder = MakeCartFolder();
        string a = Path.Combine(_root, "a.quarp8");
        string b = Path.Combine(_root, "b.quarp8");
        Quarp8Package.Pack(folder, a);
        Quarp8Package.Pack(folder, b);
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
    }

    [Fact]
    public void NonZipPackageFails()
    {
        string package = Path.Combine(_root, "fake.quarp8");
        File.WriteAllBytes(package, new byte[] { 1, 2, 3, 4, 5 });
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(package));
        Assert.Contains("not a valid .quarp8", e.Message);
    }

    /// <summary>
    /// The outer gate: a file larger than the budget plus every data bank the console allows
    /// is refused before the zip is opened, so a mis-named huge file is not read to be
    /// rejected. The inner gate — the budget itself, which excludes <c>data/</c> — can only be
    /// measured once the entries are known and lives in
    /// <see cref="PackageBankBudgetTests"/>.
    /// </summary>
    [Fact]
    public void OversizedPackageIsRejectedOnLoad()
    {
        string package = Path.Combine(_root, "big.quarp8");
        File.WriteAllBytes(package, new byte[Quarp8Package.MaxFileBytes + 1]);
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(package));
        Assert.Contains(Quarp8Package.MaxFileBytes.ToString(), e.Message);
    }

    /// <summary>
    /// The companion to <see cref="OversizedPackageIsRejectedOnLoad"/>: the gate is
    /// <c>&gt;</c>, not <c>&gt;=</c>, so a file of exactly <see cref="Quarp8Package.MaxFileBytes"/>
    /// bytes must clear the size check. It still fails one gate later — raw zero bytes are not
    /// a valid zip — and that different failure is the proof the size check itself let it
    /// through rather than the test accidentally passing for the wrong reason.
    /// </summary>
    [Fact]
    public void ExactlyAtTheLimitPassesTheSizeGate()
    {
        string package = Path.Combine(_root, "exact.quarp8");
        File.WriteAllBytes(package, new byte[Quarp8Package.MaxFileBytes]);
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(package));
        Assert.DoesNotContain(Quarp8Package.MaxFileBytes.ToString(), e.Message);
        Assert.Contains("not a valid .quarp8", e.Message);
    }

    /// <summary>
    /// <see cref="Quarp8Package.Pack"/>'s own size gate — distinct from the one
    /// <see cref="CartSource.Load(string)"/> applies to an already-built <c>.quarp8</c>
    /// (<see cref="OversizedPackageIsRejectedOnLoad"/> above): this one fires on the file
    /// <c>Pack</c> itself just zipped, and its message says "packed size" rather than "package
    /// is". Comments are free for the code budget, but still travel inside the packed <c>.cs</c>
    /// file — a giant comment is the one way to grow a package past its own limit without
    /// tripping the (unrelated, and much smaller) code budget first, which is exactly the case
    /// the ADR-024 arithmetic in <see cref="Quarp8Package"/>'s own doc comment calls out as the
    /// reason this check exists at all.
    ///
    /// <para>The padding is <paramref name="rawBytes"/> hex digits inside a <c>/* ... */</c>
    /// block comment: a uniform 16-symbol alphabet whose Shannon entropy is exactly 4 bits per
    /// byte, so no lossless compressor on any platform can pack it below half its raw size —
    /// that information-theoretic bound, not a number measured on one machine, is what keeps
    /// <see cref="PackSucceedsUnderTheLimit"/> and <see cref="PackThrowsOverTheLimit"/> honest
    /// across architectures: 300 KB of padding stays under the package limit even at literally
    /// <em>zero</em> compression, and 800 KB clears it even at compression's best possible
    /// outcome, so neither depends on which zlib build did the compressing.</para>
    /// </summary>
    private static string HexCommentPadded(int rawBytes)
    {
        var rng = new Random(12345);
        var sb = new StringBuilder(rawBytes + 128);
        sb.Append("using Quarp.Api;\npublic sealed class TestCart : Cartridge { }\n/*\n");
        const string hex = "0123456789abcdef";
        for (int i = 0; i < rawBytes; i++)
        {
            sb.Append(hex[rng.Next(hex.Length)]);
        }
        sb.Append("\n*/\n");
        return sb.ToString();
    }

    /// <summary>
    /// The control that keeps <see cref="PackThrowsOverTheLimit"/> meaningful: a cart carrying
    /// real (if padded) content still packs when it is genuinely under the limit, so the throw
    /// below is the gate doing its job and not a check that fires unconditionally.
    /// </summary>
    [Fact]
    public void PackSucceedsUnderTheLimit()
    {
        string folder = MakeCartFolder(mainCs: HexCommentPadded(300_000));
        string package = Path.Combine(_root, "under.quarp8");
        Quarp8Package.Pack(folder, package);
        Assert.True(File.Exists(package));
        Assert.InRange(new FileInfo(package).Length, 1, Quarp8Package.MaxPackageBytes);
    }

    [Fact]
    public void PackThrowsOverTheLimit()
    {
        string folder = MakeCartFolder(mainCs: HexCommentPadded(800_000));
        string package = Path.Combine(_root, "over.quarp8");
        var e = Assert.Throws<CartLoadException>(() => Quarp8Package.Pack(folder, package));
        Assert.Contains("packed size is", e.Message);
        Assert.Contains("327680", e.Message);
        Assert.False(File.Exists(package), "Pack must delete the oversized file it just wrote");
    }

    [Fact]
    public void Utf8BomInSourcesIsStripped()
    {
        string folder = MakeCartFolder();
        // Rewrite main.cs with a BOM; the loaded text must not start with U+FEFF.
        string text = "using Quarp.Api;\npublic sealed class TestCart : Cartridge { }\n";
        File.WriteAllBytes(
            Path.Combine(folder, "src", "main.cs"),
            new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray());
        string package = Path.Combine(_root, "bom.quarp8");
        Quarp8Package.Pack(folder, package);
        CartData data = CartSource.Load(package);
        Assert.Equal(text, Assert.Single(data.Sources).Text);
    }
}

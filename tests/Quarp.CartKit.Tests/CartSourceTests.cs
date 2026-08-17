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

    [Fact]
    public void OversizedPackageIsRejectedOnLoad()
    {
        string package = Path.Combine(_root, "big.quarp8");
        File.WriteAllBytes(package, new byte[Quarp8Package.MaxPackageBytes + 1]);
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(package));
        Assert.Contains("131072", e.Message);
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

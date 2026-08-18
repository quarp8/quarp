using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The 32-byte cartridge identity (REPLAY-FORMAT §5). Everything here is about one promise:
/// the same cartridge hashes to the same bytes on Windows and on Linux. The cases that could
/// break that promise are exactly the ones a unit test can reach — line endings, path
/// separators, enumeration order — so they are pinned individually rather than through one
/// "identity is stable" smoke test.
/// </summary>
public class CartIdentityTests
{
    private static CartData Cart(
        IReadOnlyList<CartSourceFile> sources,
        byte[]? gfx = null,
        byte[]? map = null,
        byte[]? flags = null,
        string name = "cart") =>
        new()
        {
            Manifest = new CartManifest { Name = name, Author = "", Profile = 8 },
            Sources = sources,
            Gfx = gfx ?? new byte[CartData.GfxWidth * CartData.GfxHeight],
            Map = map ?? new byte[CartData.MapWidth * CartData.MapHeight],
            Flags = flags ?? new byte[CartData.FlagCount],
        };

    private static CartData Simple(string text) =>
        Cart(new[] { new CartSourceFile("src/main.cs", text) });

    [Fact]
    public void IdentityIsThirtyTwoBytesAndStable()
    {
        byte[] first = CartIdentity.Compute(Simple("class A { }"));
        byte[] second = CartIdentity.Compute(Simple("class A { }"));

        Assert.Equal(CartIdentity.Size, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(CartIdentity.Unknown.ToArray(), first);
    }

    /// <summary>
    /// The one that decides whether the milestone's cross-architecture proof survives a
    /// <c>core.autocrlf=true</c> checkout: the same file with Windows and Unix line endings
    /// is the same cartridge.
    /// </summary>
    [Fact]
    public void LineEndingsAreNormalized()
    {
        byte[] lf = CartIdentity.Compute(Simple("class A\n{\n}\n"));
        byte[] crlf = CartIdentity.Compute(Simple("class A\r\n{\r\n}\r\n"));
        byte[] cr = CartIdentity.Compute(Simple("class A\r{\r}\r"));

        Assert.Equal(lf, crlf);
        Assert.Equal(lf, cr);
    }

    /// <summary>Comments count, unlike the code budget: the hash answers "is this that cartridge".</summary>
    [Fact]
    public void CommentsChangeTheIdentity()
    {
        Assert.NotEqual(
            CartIdentity.Compute(Simple("class A { }")),
            CartIdentity.Compute(Simple("// note\nclass A { }")));
    }

    /// <summary>A rename is a change — that is why the path is hashed alongside the text.</summary>
    [Fact]
    public void RenamingAFileChangesTheIdentity()
    {
        byte[] before = CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/a.cs", "class A { }") }));
        byte[] after = CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/b.cs", "class A { }") }));

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// Directory enumeration order differs between file systems, so the hash sorts the files
    /// itself instead of trusting whatever order it was handed.
    /// </summary>
    [Fact]
    public void SourceOrderDoesNotMatter()
    {
        var a = new CartSourceFile("src/a.cs", "class A { }");
        var b = new CartSourceFile("src/b.cs", "class B { }");

        Assert.Equal(
            CartIdentity.Compute(Cart(new[] { a, b })),
            CartIdentity.Compute(Cart(new[] { b, a })));
    }

    /// <summary>Windows folder carts hand out backslashes nowhere, but the hash must not care if they do.</summary>
    [Fact]
    public void PathSeparatorsAreNormalized()
    {
        Assert.Equal(
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/ui/hud.cs", "class H { }") })),
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src\\ui\\hud.cs", "class H { }") })));
    }

    /// <summary>
    /// The framing test: without a length prefix per field, moving a character from a path
    /// into the text that follows it would leave the concatenation — and the digest —
    /// unchanged.
    /// </summary>
    [Fact]
    public void FieldBoundariesCannotBeShifted()
    {
        Assert.NotEqual(
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/ab.cs", "c") })),
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/a.cs", "bc") })));
    }

    [Fact]
    public void AssetsAreCovered()
    {
        var gfx = new byte[CartData.GfxWidth * CartData.GfxHeight];
        var map = new byte[CartData.MapWidth * CartData.MapHeight];
        var flags = new byte[CartData.FlagCount];
        byte[] baseline = CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/main.cs", "class A { }") }));

        gfx[4096] = 7;
        Assert.NotEqual(baseline,
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/main.cs", "class A { }") }, gfx: gfx)));

        map[100] = 3;
        Assert.NotEqual(baseline,
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/main.cs", "class A { }") }, map: map)));

        flags[255] = 1;
        Assert.NotEqual(baseline,
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/main.cs", "class A { }") }, flags: flags)));
    }

    /// <summary>Cosmetics stay out: renaming the cart must not invalidate its replays.</summary>
    [Fact]
    public void ManifestNameIsNotPartOfTheIdentity()
    {
        Assert.Equal(
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/main.cs", "class A { }") }, name: "snake")),
            CartIdentity.Compute(Cart(new[] { new CartSourceFile("src/main.cs", "class A { }") }, name: "renamed")));
    }

    /// <summary>
    /// A folder cart and its .quarp8 are the same cartridge — the case the whole
    /// normalization exists for, checked end to end through the real loaders.
    /// </summary>
    [Fact]
    public void FolderAndPackagedCartHaveTheSameIdentity()
    {
        string folder = Path.Combine(Path.GetTempPath(), "quarp-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "src", "ui"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "manifest.json"),
                "{\"name\":\"identity\",\"author\":\"\",\"profile\":8}");
            File.WriteAllText(Path.Combine(folder, "src", "main.cs"), """
                using Quarp.Api;

                public sealed class IdentityCart : Cartridge
                {
                    public override void Draw() => Cls(0);
                }
                """);
            File.WriteAllText(Path.Combine(folder, "src", "ui", "hud.cs"), "internal static class Hud { }");

            byte[] fromFolder = CartIdentity.Compute(CartSource.LoadFolder(folder));

            string package = folder + ".quarp8";
            try
            {
                Quarp8Package.Pack(folder, package);
                byte[] fromPackage = CartIdentity.Compute(CartSource.LoadPackage(package));
                Assert.Equal(fromFolder, fromPackage);
            }
            finally
            {
                File.Delete(package);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void HexHelpersRoundTripTheDigestShape()
    {
        byte[] identity = CartIdentity.Compute(Simple("class A { }"));

        string hex = CartIdentity.ToHex(identity);
        Assert.Equal(CartIdentity.Size * 2, hex.Length);
        Assert.Equal(hex, hex.ToLowerInvariant());
        Assert.Equal(hex[..8], CartIdentity.ToShortHex(identity));
    }
}

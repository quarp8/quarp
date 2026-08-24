using System.Text.Json;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// <see cref="CartScaffold"/> — the template writer both entrances share since the boot-menu
/// wave (M9 stage 4): the CLI's <c>quarp new</c> and the shell menu's CREATE GAME.
///
/// <para><b>What is asserted through whom.</b> The written cartridge is judged by
/// <see cref="CartSource.Load"/> — the very loader <c>quarp run</c> uses — not by comparing
/// bytes back to <see cref="CartTemplate"/>'s constants, which would be the mirror-test
/// defect the M9 stage-2 lessons name twice: a check that reads the same constant it wrote
/// stays green whatever the constant says. Whether the template also <em>compiles</em> is
/// pinned end to end in <c>Quarp.Cli.Tests.QuarpNewTests</c> through the real child process;
/// this file owns the pieces the menu calls directly.</para>
/// </summary>
public sealed class CartScaffoldTests : IDisposable
{
    private readonly string _root;

    public CartScaffoldTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-scaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ACreatedCartridgeLoadsThroughTheRealLoader()
    {
        string cart = Path.Combine(_root, "mygame");

        string name = CartScaffold.Create(cart);

        Assert.Equal("mygame", name);
        // The loader that judges every cartridge judges this one: files present, manifest
        // parseable, profile known. A template CartSource refuses is not a template.
        CartData data = CartSource.Load(cart);
        Assert.Equal("mygame", data.Manifest.Name);
    }

    [Fact]
    public void TheManifestNamesTheFolderAndProfileEight()
    {
        string cart = Path.Combine(_root, "named-by-folder");
        CartScaffold.Create(cart);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(cart, "manifest.json")));
        Assert.Equal("named-by-folder", manifest.RootElement.GetProperty("name").GetString());
        Assert.Equal(8, manifest.RootElement.GetProperty("profile").GetInt32());
    }

    /// <summary>
    /// The refusal both entrances rely on: <see cref="CartScaffold.CartridgeExists"/> is the
    /// question, <see cref="CartScaffold.Create"/> throwing is the enforcement — the menu asks
    /// first to put the message in its footer, the CLI asks first to keep its exact stderr
    /// line, and a caller that forgets to ask still cannot overwrite anyone's src/main.cs.
    /// </summary>
    [Fact]
    public void CreatingOverAnExistingCartridgeThrowsInsteadOfOverwriting()
    {
        string cart = Path.Combine(_root, "twice");
        CartScaffold.Create(cart);
        string mine = File.ReadAllText(Path.Combine(cart, "src", "main.cs")) + "\n// mine\n";
        File.WriteAllText(Path.Combine(cart, "src", "main.cs"), mine);

        Assert.True(CartScaffold.CartridgeExists(cart));
        IOException refusal = Assert.Throws<IOException>(() => CartScaffold.Create(cart));

        Assert.Contains("already contains a cartridge", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(mine, File.ReadAllText(Path.Combine(cart, "src", "main.cs")));
    }

    /// <summary>
    /// A cartridge folder is also a library row, a <c>quarp run</c> argument and one day a
    /// shared file: the menu's gate accepts only names that never need quoting anywhere.
    /// The CLI deliberately keeps accepting whatever the author's terminal produced — this
    /// gate guards names born in the menu's entry field, nothing else.
    /// </summary>
    [Theory]
    [InlineData("mygame", true)]
    [InlineData("my-game_2", true)]
    [InlineData("x", true)]
    [InlineData("", false)]
    [InlineData("MyGame", false)]           // uppercase: the entry field folds it before this gate
    [InlineData("my game", false)]          // a space needs quoting in every shell
    [InlineData("my/game", false)]          // a separator would escape the carts root
    [InlineData("..", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaa", false)]   // 25 > MaxNameLength
    public void OnlyFolderSafeLowercaseNamesPassTheMenuGate(string name, bool valid) =>
        Assert.Equal(valid, CartScaffold.IsValidName(name));
}

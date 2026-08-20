using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The library's list model (M9 stage 1): what the scan counts as a cartridge, how two roots
/// merge, and how the selection bar behaves. All on real directories — the scan's job is to
/// read a disk, so a mocked disk would test the mock.
/// </summary>
public class CartLibraryTests : IDisposable
{
    private readonly string _root;

    public CartLibraryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A folder the scan should list: the manifest marker is all it looks for.</summary>
    private void MakeCartFolder(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), "{}");
    }

    /// <summary>
    /// A package the scan should list. Content-free on purpose: the scan judges by name and
    /// the loader judges by content — a package that does not open reports at launch, where
    /// the author can read the message, not by silently vanishing from the list.
    /// </summary>
    private void MakePackage(string fileName) =>
        File.WriteAllBytes(Path.Combine(_root, fileName), Array.Empty<byte>());

    [Fact]
    public void CartFoldersAndPackagesAreListedAsEquals()
    {
        MakeCartFolder("beta");
        MakePackage("alpha.quarp8");
        Directory.CreateDirectory(Path.Combine(_root, "junk"));            // no manifest — not a cart
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not a cart"); // wrong extension

        var library = new CartLibrary(_root);
        library.Rescan();

        // Sorted by name, one kind of row: the work order's "as equals" — a package gets no
        // second-class treatment and no special suffix.
        Assert.Equal(new[] { "alpha", "beta" }, library.Entries.Select(e => e.Name));
    }

    [Fact]
    public void TheSameRootNamedTwiceListsEachCartOnce()
    {
        // The default roots are exe-adjacent and cwd-adjacent carts/, and for anyone running
        // quarp from its own folder those are one directory — the everyday case, not an edge.
        MakeCartFolder("snake");
        MakePackage("puzzle.quarp8");

        var library = new CartLibrary(_root, _root);
        library.Rescan();

        Assert.Equal(2, library.Entries.Count);
    }

    [Fact]
    public void AMissingRootIsAnEmptyLibraryNotACrash()
    {
        var library = new CartLibrary(Path.Combine(_root, "no-such-folder"));
        library.Rescan();

        Assert.Empty(library.Entries);
        Assert.Null(library.Selected);
    }

    [Fact]
    public void SelectionClampsAtBothEndsInsteadOfWrapping()
    {
        MakeCartFolder("a");
        MakeCartFolder("b");
        MakeCartFolder("c");
        var library = new CartLibrary(_root);
        library.Rescan();

        library.MoveSelection(-5);
        Assert.Equal(0, library.SelectedIndex);
        library.MoveSelection(+99);
        Assert.Equal(2, library.SelectedIndex);
        library.MoveSelection(-1);
        Assert.Equal(1, library.SelectedIndex);
    }

    [Fact]
    public void MovingTheSelectionOfAnEmptyLibraryDoesNothing()
    {
        var library = new CartLibrary(_root);
        library.Rescan();

        library.MoveSelection(+1);   // must not throw or index anything

        Assert.Null(library.Selected);
    }

    /// <summary>
    /// The selection follows the cart, not the index: coming back from a game rescans, and if
    /// a new cart sorted itself above the one just played, an index-keeping library would land
    /// the bar on a stranger.
    /// </summary>
    [Fact]
    public void RescanKeepsTheBarOnTheSelectedCartWhenTheListShifts()
    {
        MakeCartFolder("m-cart");
        MakeCartFolder("z-cart");
        var library = new CartLibrary(_root);
        library.Rescan();
        library.MoveSelection(+1);
        Assert.Equal("z-cart", library.Selected!.Value.Name);

        MakeCartFolder("a-cart");    // sorts first, shifting every index by one
        library.Rescan();

        Assert.Equal(3, library.Entries.Count);
        Assert.Equal("z-cart", library.Selected!.Value.Name);
    }

    /// <summary>And when the selected cart itself is gone, the bar falls back to the top rather than out of range.</summary>
    [Fact]
    public void RescanFallsBackToTheTopWhenTheSelectedCartDisappeared()
    {
        MakeCartFolder("first");
        MakeCartFolder("second");
        var library = new CartLibrary(_root);
        library.Rescan();
        library.MoveSelection(+1);

        Directory.Delete(Path.Combine(_root, "second"), recursive: true);
        library.Rescan();

        Assert.Equal(0, library.SelectedIndex);
        Assert.Equal("first", library.Selected!.Value.Name);
    }
}

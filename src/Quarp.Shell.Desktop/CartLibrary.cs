namespace Quarp.Shell.Desktop;

/// <summary>
/// One row of the library: a display name and the path <see cref="CartSession.Start"/> accepts.
/// The name is the folder or file name, not the manifest's — reading every manifest would make
/// opening the library cost one JSON parse per cart, and the folder name is what the author
/// typed into <c>quarp new</c> anyway. The manifest's name takes over the moment the cart runs
/// (the window title comes from the loaded session).
/// </summary>
public readonly record struct CartLibraryEntry(string Name, string Path);

/// <summary>
/// The list behind the library screen (M9 stage 1): which cartridges exist, and which one the
/// selection bar is on. Deliberately free of MonoGame — the scan and the selection rules are
/// where the behaviour lives, and keeping them off the graphics device is what lets the mode
/// transition tests drive them without a window, the same split <see cref="CartSession"/> uses.
///
/// <para><b>What counts as a cartridge.</b> Inside each root: every folder holding a
/// <c>manifest.json</c>, and every <c>.quarp8</c> file — as equals, because both are launch
/// paths <c>quarp run</c> already accepts. The scan judges by name only; whether the cart
/// actually loads is the launcher's question, and a broken cart is reported at launch rather
/// than silently hidden from the list (an author debugging a cart needs to see it).</para>
///
/// <para><b>Two roots by default</b> (M9 work order): <c>carts/</c> next to the executable
/// (the installed console's own games) and <c>carts/</c> under the working directory (the
/// repository or project the author is standing in). When the two are the same directory the
/// dedup below lists each cart once.</para>
/// </summary>
public sealed class CartLibrary
{
    /// <summary>The folder each root points into. One name, used by both default roots.</summary>
    public const string FolderName = "carts";

    private readonly string[] _roots;
    private readonly List<CartLibraryEntry> _entries = new();

    /// <summary>Roots are remembered, not scanned: call <see cref="Rescan"/> before reading.</summary>
    public CartLibrary(params string[] roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = roots;
    }

    /// <summary>
    /// Where the shell looks by default. Both are legitimate at once: the exe-relative root is
    /// the console's own library, the cwd-relative one is whatever project the author launched
    /// from — and `quarp` is usually launched from a project.
    /// </summary>
    public static string[] DefaultRoots() => new[]
    {
        Path.Combine(AppContext.BaseDirectory, FolderName),
        Path.Combine(Environment.CurrentDirectory, FolderName),
    };

    /// <summary>What the last <see cref="Rescan"/> found, sorted by name for a stable screen.</summary>
    public IReadOnlyList<CartLibraryEntry> Entries => _entries;

    /// <summary>Index of the selection bar; meaningless while <see cref="Entries"/> is empty.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>The entry under the selection bar, or null for an empty library.</summary>
    public CartLibraryEntry? Selected => _entries.Count == 0 ? null : _entries[SelectedIndex];

    /// <summary>
    /// Moves the selection bar, clamped at both ends rather than wrapping: with a keyboard
    /// held down, a clamped list settles on an end while a wrapping one spins.
    /// </summary>
    public void MoveSelection(int delta)
    {
        if (_entries.Count == 0)
        {
            return;
        }
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, _entries.Count - 1);
    }

    /// <summary>
    /// Rebuilds the list from disk. Called on entering the library — including the return from
    /// a game, because carts appear and disappear while one is being played (a `quarp new` in
    /// another terminal, a deleted folder). The selection follows the previously selected
    /// cart's <em>path</em>, not its index: coming back from a game must land the bar on the
    /// game just played, however the list shifted around it.
    /// </summary>
    public void Rescan()
    {
        string? keepPath = Selected?.Path;
        _entries.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in _roots)
        {
            ScanRoot(root, seen);
        }
        // Name first for the reader, path second so two same-named carts from different roots
        // keep a stable relative order between rescans.
        _entries.Sort(static (a, b) =>
        {
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        });
        int kept = keepPath is null
            ? -1
            : _entries.FindIndex(e => string.Equals(e.Path, keepPath, StringComparison.OrdinalIgnoreCase));
        SelectedIndex = kept >= 0 ? kept : 0;
    }

    /// <summary>
    /// One root's contribution. Errors make the root contribute nothing rather than kill the
    /// library: a permission-denied <c>carts/</c> is that root's problem, and the other root's
    /// carts must still be playable.
    /// </summary>
    private void ScanRoot(string root, HashSet<string> seen)
    {
        try
        {
            string full = Path.GetFullPath(root);
            if (!Directory.Exists(full))
            {
                return;
            }
            foreach (string dir in Directory.GetDirectories(full))
            {
                // "manifest.json" is the marker CartSource.Load demands of a cart folder; the
                // literal is repeated here because the scan must not load anything, only look.
                if (File.Exists(Path.Combine(dir, "manifest.json")) && seen.Add(dir))
                {
                    _entries.Add(new CartLibraryEntry(Path.GetFileName(dir), dir));
                }
            }
            foreach (string file in Directory.GetFiles(full, "*.quarp8"))
            {
                if (seen.Add(file))
                {
                    _entries.Add(new CartLibraryEntry(Path.GetFileNameWithoutExtension(file), file));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[quarp] cannot list {root}: {e.Message}");
        }
    }
}

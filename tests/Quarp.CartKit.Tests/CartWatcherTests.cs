using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// Hot-reload watching: a save burst in src/ becomes exactly one debounced reload
/// request; editor temp files and unrelated files stay silent. Timing asserts use
/// generous margins to stay reliable on slow machines.
/// </summary>
public class CartWatcherTests : IDisposable
{
    private readonly string _root;

    public CartWatcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "main.cs"), "class C { }");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static bool PollForReload(CartWatcher watcher, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (watcher.ConsumeReloadRequest())
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return false;
    }

    [Fact]
    public void SourceEditRaisesExactlyOneDebouncedRequest()
    {
        using var watcher = new CartWatcher(_root);
        File.WriteAllText(Path.Combine(_root, "src", "main.cs"), "class C { int f; }");
        Assert.True(PollForReload(watcher, timeoutMs: 5000), "edit was never reported");
        // The burst was consumed: no second request without a new edit.
        Assert.False(PollForReload(watcher, timeoutMs: 300));
    }

    [Fact]
    public void RequestIsHeldBackUntilTheDebounceWindowPasses()
    {
        using var watcher = new CartWatcher(_root);
        File.WriteAllText(Path.Combine(_root, "src", "main.cs"), "class C { int g; }");
        Thread.Sleep(20);   // give the FS event time to arrive, still inside the window
        Assert.False(watcher.ConsumeReloadRequest());   // too fresh: still debouncing
        Assert.True(PollForReload(watcher, timeoutMs: 5000));
    }

    [Fact]
    public void AssetEditsAreRelevantToo()
    {
        using var watcher = new CartWatcher(_root);
        File.WriteAllBytes(Path.Combine(_root, "map.bin"), new byte[16]);
        Assert.True(PollForReload(watcher, timeoutMs: 5000), "map.bin edit was never reported");
    }

    [Fact]
    public void EditorTempFilesStaySilent()
    {
        using var watcher = new CartWatcher(_root);
        File.WriteAllText(Path.Combine(_root, "src", "main.cs.tmp"), "half-saved");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "unrelated");
        Assert.False(PollForReload(watcher, timeoutMs: 500));
    }

    [Fact]
    public void MissingFolderFailsUpFront()
    {
        Assert.Throws<CartLoadException>(() => new CartWatcher(Path.Combine(_root, "gone")));
    }
}

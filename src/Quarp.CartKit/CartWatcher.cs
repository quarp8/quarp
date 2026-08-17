namespace Quarp.CartKit;

/// <summary>
/// Watches a cartridge folder for edits — manifest.json, src/**/*.cs, gfx.png, map.bin,
/// flags.bin — and turns bursts of FileSystemWatcher events into one debounced reload flag
/// (150 ms, M1 work order). No callbacks into engine threads: the shell polls
/// <see cref="ConsumeReloadRequest"/> once per frame on its main thread and performs
/// the rebuild itself. The .cs suffix filter keeps editor temp files (atomic-save
/// .tmp companions) from causing reload storms; an editor's final rename to *.cs
/// still lands here as a Renamed event.
/// </summary>
public sealed class CartWatcher : IDisposable
{
    public const int DebounceMilliseconds = 150;

    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private bool _pending;
    private long _lastEventTick;

    public CartWatcher(string cartFolder)
    {
        string root = Path.GetFullPath(cartFolder);
        if (!Directory.Exists(root))
        {
            throw new CartLoadException($"cannot watch cartridge folder, it does not exist: {root}");
        }
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Polled by the shell on its main thread. Returns true exactly once per settled
    /// burst of edits: only after <see cref="DebounceMilliseconds"/> have passed since
    /// the newest relevant event (editors fire several events per save).
    /// </summary>
    public bool ConsumeReloadRequest()
    {
        lock (_gate)
        {
            if (!_pending)
            {
                return false;
            }
            if (Environment.TickCount64 - _lastEventTick < DebounceMilliseconds)
            {
                return false;
            }
            _pending = false;
            return true;
        }
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (IsRelevant(e.FullPath))
        {
            Mark();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRelevant(e.FullPath) || IsRelevant(e.OldFullPath))
        {
            Mark();
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        Mark(); // Event buffer overflow: something may have changed unseen — reload to be safe.
    }

    private bool IsRelevant(string fullPath)
    {
        string relative = Path.GetRelativePath(_watcher.Path, fullPath).Replace('\\', '/');
        if (relative.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
        {
            return relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }
        // manifest.json belongs here too: it is a load-time input (name, author, profile)
        // and a cart that fails to load because of it must recover the moment it is fixed.
        return string.Equals(relative, "manifest.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relative, "gfx.png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relative, "map.bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relative, "flags.bin", StringComparison.OrdinalIgnoreCase);
    }

    private void Mark()
    {
        lock (_gate)
        {
            _pending = true;
            _lastEventTick = Environment.TickCount64;
        }
    }
}

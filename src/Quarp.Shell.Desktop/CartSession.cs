using System.Buffers.Binary;
using Quarp.Api;
using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One running cartridge inside the shell: load, tick, hot reload, crash handling and
/// save.dat persistence. Owns the <see cref="VirtualConsole"/> (recreated on every
/// successful reload — restart mode, ARCHITECTURE §3) and the <see cref="CartHost"/>.
/// A cartridge exception pauses the simulation, prints the full stack trace (embedded
/// PDB gives line numbers) and stamps a red banner into the framebuffer; a successful
/// reload resumes. Compile errors during reload keep the previous cartridge running, and so
/// does a transient file lock (an editor mid-save) — that one is retried instead of dropped.
/// Folder carts are watched for edits; a packed .quarp8 has no watcher (nothing to edit).
/// </summary>
public sealed class CartSession : IDisposable
{
    private const int AutoSaveIntervalMs = 1000;

    /// <summary>Wait before retrying a reload that hit a transient file lock — one debounce window.</summary>
    private const int ReloadRetryIntervalMs = CartWatcher.DebounceMilliseconds;

    private readonly string _cartPath;
    private readonly string _savePath;
    private readonly CartWatcher? _watcher;

    private VirtualConsole _console;
    private CartHost _host;
    private bool _crashed;
    private long _lastSaveTick;
    private long _reloadRetryTick; // 0 = no retry armed.

    private CartSession(string cartPath, string savePath, CartWatcher? watcher, VirtualConsole console, CartHost host)
    {
        _cartPath = cartPath;
        _savePath = savePath;
        _watcher = watcher;
        _console = console;
        _host = host;
    }

    /// <summary>The cart's display name from its manifest.</summary>
    public string Name { get; private set; } = "";

    /// <summary>The framebuffer to present; changes identity after a successful reload.</summary>
    public Framebuffer Framebuffer => _console.Framebuffer;

    /// <summary>
    /// Loads, compiles and starts a cartridge from a folder or a .quarp8 file.
    /// Load and compile failures throw <see cref="CartLoadException"/> (the caller exits);
    /// a crash inside the cartridge's Init starts the session paused with the banner up,
    /// so the author can fix the code and hot-reload.
    /// </summary>
    public static CartSession Start(string path)
    {
        string fullPath = Path.GetFullPath(path);
        // Watch before loading: the first compile is the cold one (seconds — Roslyn's own
        // JIT) and an edit saved while it runs must not be lost. The watcher's pending flag
        // is thread-safe and survives until the shell polls it on the first frame.
        CartWatcher? watcher = Directory.Exists(fullPath) ? new CartWatcher(fullPath) : null;
        try
        {
            CartData data = CartSource.Load(fullPath);
            CartCompileResult result = CartCompiler.Compile(data);
            if (!result.Success)
            {
                throw new CartLoadException(
                    "cartridge failed to compile:" + Environment.NewLine
                    + string.Join(Environment.NewLine, result.Diagnostics));
            }

            CartHost host = CartHost.Load(result.AssemblyBytes);
            var console = new VirtualConsole(ConsoleProfile.Profile8, data.Gfx, data.Map, data.Flags);
            string savePath = Directory.Exists(fullPath)
                ? Path.Combine(fullPath, "save.dat")
                : Path.ChangeExtension(fullPath, ".save.dat");
            console.LoadPersistent(ReadSaveFile(savePath));

            var session = new CartSession(fullPath, savePath, watcher, console, host) { Name = data.Manifest.Name };
            try
            {
                console.AttachCart(host.Cartridge);
            }
            catch (Exception e)
            {
                session.Crash(e);
            }
            return session;
        }
        catch
        {
            watcher?.Dispose(); // Nobody owns it yet — do not leak the FileSystemWatcher.
            throw;
        }
    }

    /// <summary>
    /// One fixed 60 Hz step: poll the watcher for a debounced reload, advance the
    /// simulation unless crashed, and autosave dirty persistent memory at most once
    /// per second.
    /// </summary>
    public void Update(InputState input)
    {
        if (_watcher is not null && (_watcher.ConsumeReloadRequest() || ReloadRetryDue()))
        {
            try
            {
                TryReload();
            }
            catch (Exception e)
            {
                // A failed rebuild must never take the console down (M1: a bad reload keeps
                // the previous cartridge running). Report and wait for the next edit.
                Console.Error.WriteLine("[quarp] reload failed unexpectedly:");
                Console.Error.WriteLine(e.ToString());
                ReportKeepingOldCart();
            }
        }
        if (!_crashed)
        {
            try
            {
                _console.Tick(input);
            }
            catch (Exception e)
            {
                Crash(e);
            }
        }
        SaveIfDirty(force: false);
    }

    /// <summary>Writes save.dat immediately if there are unsaved Dset changes.</summary>
    public void SaveNow() => SaveIfDirty(force: true);

    public void Dispose()
    {
        SaveIfDirty(force: true);
        _watcher?.Dispose();
        _host.Unload();
    }

    // --- hot reload (restart mode: fresh console + Init, ARCHITECTURE §3) ---

    private void TryReload()
    {
        // A retry of a locked-file reload says nothing new: report the streak once, not
        // every 150 ms, so a file held open for a while does not flood the terminal.
        bool isRetry = _reloadRetryTick != 0;
        _reloadRetryTick = 0;
        if (!isRetry)
        {
            Console.WriteLine($"[quarp] change detected — rebuilding {_cartPath}");
        }
        CartData data;
        try
        {
            data = CartSource.Load(_cartPath);
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"[quarp] reload failed: {e.Message}");
            ReportKeepingOldCart();
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Transient on Windows: the editor still holds the file it is saving (sharing
            // violation), or an attribute flip made it briefly unreadable. Losing the edit
            // would be worse than a late reload, so keep the old cart and try again.
            if (!isRetry)
            {
                Console.Error.WriteLine(
                    $"[quarp] cartridge files are busy ({e.Message}) — previous cartridge keeps running, retrying.");
            }
            ArmReloadRetry();
            return;
        }

        CartCompileResult result = CartCompiler.Compile(data);
        if (!result.Success)
        {
            foreach (string diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }
            ReportKeepingOldCart();
            return;
        }

        CartHost newHost;
        try
        {
            newHost = CartHost.Load(result.AssemblyBytes);
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"[quarp] reload failed: {e.Message}");
            ReportKeepingOldCart();
            return;
        }

        // The new build is good — swap. Flush pending saves, carry persistent memory over
        // (it belongs to the player, not to the cart instance), drop the old ALC.
        SaveIfDirty(force: true);
        var persistent = new int[VirtualConsole.PersistentSlots];
        _console.CopyPersistentTo(persistent);
        var newConsole = new VirtualConsole(ConsoleProfile.Profile8, data.Gfx, data.Map, data.Flags);
        newConsole.LoadPersistent(persistent);
        _host.Unload();
        _host = newHost;
        _console = newConsole;
        _crashed = false;
        Name = data.Manifest.Name;
        try
        {
            newConsole.AttachCart(newHost.Cartridge);
            Console.WriteLine("[quarp] reload OK — cartridge restarted.");
        }
        catch (Exception e)
        {
            // The new code crashed in Init: stay on the new build (it is what the author
            // is editing), paused with the banner, waiting for the next fix.
            Crash(e);
        }
    }

    /// <summary>True once the wait after a transient reload failure has elapsed.</summary>
    private bool ReloadRetryDue() =>
        _reloadRetryTick != 0 && Environment.TickCount64 >= _reloadRetryTick;

    /// <summary>
    /// Re-arms the reload the watcher already consumed. A file lock that lasts a few
    /// milliseconds must not swallow the author's edit: the next debounce window retries.
    /// </summary>
    private void ArmReloadRetry() => _reloadRetryTick = Environment.TickCount64 + ReloadRetryIntervalMs;

    private void ReportKeepingOldCart()
    {
        Console.Error.WriteLine(_crashed
            ? "[quarp] still crashed — fix the code to reload."
            : "[quarp] previous cartridge keeps running.");
    }

    // --- crash handling ---

    private void Crash(Exception exception)
    {
        _crashed = true;
        Console.Error.WriteLine("[quarp] cartridge crashed:");
        Console.Error.WriteLine(exception.ToString());
        Console.Error.WriteLine("[quarp] simulation paused — edit the code to reload.");
        DrawCrashBanner();
    }

    /// <summary>Stamps the banner over the last frame via the console's own drawing API.</summary>
    private void DrawCrashBanner()
    {
        IConsoleApi c = _console;
        // Reset draw state the cart may have left behind so the banner is always legible.
        c.Camera();
        c.Clip();
        c.Pal();
        c.RectFill(0, 24, 128, 24, 10);
        c.Rect(0, 24, 128, 24, 3);
        c.Print("CRASHED - SEE TERMINAL", 20, 29, 3);
        c.Print("EDIT CODE TO RELOAD", 26, 38, 3);
    }

    // --- persistence: save.dat = 64 x int32 little-endian raw Fix (M1 work order) ---

    private static int[] ReadSaveFile(string savePath)
    {
        var slots = new int[VirtualConsole.PersistentSlots];
        if (!File.Exists(savePath))
        {
            return slots;
        }
        try
        {
            byte[] bytes = File.ReadAllBytes(savePath);
            int count = Math.Min(bytes.Length / 4, slots.Length);
            for (int i = 0; i < count; i++)
            {
                slots[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[quarp] could not read {savePath}: {e.Message} — starting with empty save data.");
        }
        return slots;
    }

    private void SaveIfDirty(bool force)
    {
        if (!_console.PersistentDirty)
        {
            return;
        }
        long now = Environment.TickCount64;
        if (!force && now - _lastSaveTick < AutoSaveIntervalMs)
        {
            return;
        }

        var slots = new int[VirtualConsole.PersistentSlots];
        _console.CopyPersistentTo(slots);
        byte[] bytes = new byte[slots.Length * 4];
        for (int i = 0; i < slots.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), slots[i]);
        }
        try
        {
            File.WriteAllBytes(_savePath, bytes);
            _console.PersistentDirty = false;
            _lastSaveTick = now;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked or read-only save.dat is not worth crashing the console over.
            // Keep the dirty flag: the next autosave window retries.
            Console.Error.WriteLine($"[quarp] could not write {_savePath}: {e.Message}");
            _lastSaveTick = now;
        }
    }
}

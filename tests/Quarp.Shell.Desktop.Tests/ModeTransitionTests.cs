using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Quarp.Api;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The M9 stage 1 lifecycle claims, driven through <see cref="ShellModeMachine"/> with real
/// cartridges on disk — a Roslyn compile, a collectible load context, a real save.dat. The
/// machine exists precisely so these are provable without a window (the same split that made
/// <see cref="ContinuationReloadTests"/> possible): <c>QuarpGame</c> needs a graphics device,
/// the policy under test does not.
///
/// <para>Three of these are the work order's named proofs: Esc out of a game (1) flushes the
/// unsaved tail of save.dat through the session's Dispose, (2) drains the audio device before
/// the library shows, and (3) lets the cart's collectible AssemblyLoadContext actually die —
/// not "Unload was requested" but a dead weak reference, because a leaked context is invisible
/// in any other way.</para>
/// </summary>
public class ModeTransitionTests : IDisposable
{
    /// <summary>
    /// Writes a new persistent value on every tick, so the disk and the memory disagree
    /// whenever the autosave window has not elapsed — which is exactly the state the forced
    /// save in Dispose exists for.
    /// </summary>
    private const string SaverSource = """
        using Quarp.Api;

        public sealed class SaverCart : Cartridge
        {
            public override void Update()
            {
                Dset(0, Ticks);
            }

            public override void Draw()
            {
                Cls(0);
            }
        }
        """;

    private readonly string _root;
    private readonly string _saverFolder;

    public ModeTransitionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-modes-" + Guid.NewGuid().ToString("N"));
        _saverFolder = Path.Combine(_root, "saver");
        Directory.CreateDirectory(Path.Combine(_saverFolder, "src"));
        File.WriteAllText(Path.Combine(_saverFolder, "manifest.json"),
            "{\"name\":\"saver\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(Path.Combine(_saverFolder, "src", "main.cs"), SaverSource);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Counts drain calls — the audio seam, observed instead of mocked away.</summary>
    private sealed class DrainCounter
    {
        public int Calls { get; private set; }

        public void Bump() => Calls++;
    }

    /// <summary>A machine entered through the library, the way plain `quarp` enters it.</summary>
    private ShellModeMachine LibraryMachine(DrainCounter drain) =>
        new(new CartLibrary(_root), static path => CartSession.Start(path), drain.Bump);

    [Fact]
    public void EscapeFromALibraryLaunchedGameReturnsToTheLibrary()
    {
        var machine = LibraryMachine(new DrainCounter());
        Assert.Equal(ShellMode.Library, machine.Mode);

        Assert.NotNull(machine.LaunchSelected());
        Assert.Equal(ShellMode.Game, machine.Mode);

        machine.HandleEscape();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Session);
        Assert.False(machine.ExitRequested);        // the process lives on — that is the point
    }

    /// <summary>
    /// The save.dat guarantee. The first Update autosaves (the interval clock starts at zero),
    /// pinning tick 1 to the disk; the next ten ticks land inside the one-second autosave
    /// window, so only the forced save in <c>CartSession.Dispose</c> can move the file to 11.
    /// If more than a second somehow passed between the two Updates the autosave would write
    /// 11 early and this test could not distinguish the paths — accepted: the two calls are
    /// microseconds apart, and the negative control (remove the forced save from Dispose)
    /// shows this red reliably.
    /// </summary>
    [Fact]
    public void EscapeFromAGameWritesTheUnsavedTailOfSaveDat()
    {
        var machine = LibraryMachine(new DrainCounter());
        Assert.NotNull(machine.LaunchSelected());
        CartSession session = machine.Session!;
        session.Update(1, default, rewinding: false);    // autosave: disk now holds tick 1
        session.Update(10, default, rewinding: false);   // memory holds tick 11, disk still 1

        machine.HandleEscape();

        byte[] saved = File.ReadAllBytes(Path.Combine(_saverFolder, "save.dat"));
        Fix expected = 11;      // Dset stores Fix raw values; the conversion owns the format
        Assert.Equal(expected.Raw, BinaryPrimitives.ReadInt32LittleEndian(saved.AsSpan(0, 4)));
    }

    [Fact]
    public void LeavingAGameDrainsTheAudioDeviceExactlyOnce()
    {
        var drain = new DrainCounter();
        var machine = LibraryMachine(drain);
        Assert.NotNull(machine.LaunchSelected());
        machine.Session!.Update(5, default, rewinding: false);
        Assert.Equal(0, drain.Calls);       // playing never drains — only leaving does

        machine.HandleEscape();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Equal(1, drain.Calls);
    }

    /// <summary>
    /// The leak detector. A weak reference to the cart's collectible AssemblyLoadContext must
    /// die once the transition has run and the session reference is gone: one live context per
    /// running cart, zero after leaving — counted through the reference itself rather than by
    /// name over <c>AssemblyLoadContext.All</c>, because other tests in this run legitimately
    /// hold their own "quarp-cart" contexts in parallel.
    /// </summary>
    [Fact]
    public void LeavingAGameLetsTheCartsLoadContextDie()
    {
        var machine = LibraryMachine(new DrainCounter());
        WeakReference context = LaunchRunAndLeave(machine);

        // Collectible contexts need more than one pass: the unload is asynchronous and the
        // finalizer queue is part of the path.
        for (int i = 0; i < 10 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive,
            "the cartridge's collectible AssemblyLoadContext survived the game -> library transition");
    }

    /// <summary>
    /// Everything that touches the session lives in this non-inlined frame, so no JIT-extended
    /// local keeps the object graph — session, TimeMachine, Cartridge instance — reachable
    /// after the method returns. Without this the Debug-configuration JIT pins locals to the
    /// end of the calling test and the weak reference above stays alive for a wrong reason.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LaunchRunAndLeave(ShellModeMachine machine)
    {
        Assert.NotNull(machine.LaunchSelected());
        machine.Session!.Update(30, default, rewinding: false);
        WeakReference context = machine.Session.LoadContextWeakReference;
        Assert.True(context.IsAlive);
        machine.HandleEscape();
        Assert.Null(machine.Session);
        return context;
    }

    /// <summary>
    /// `quarp run &lt;cart&gt;` keeps its contract: Esc asks the process to exit and touches
    /// nothing — the window's own OnExiting/Dispose path saves and unloads, exactly as it did
    /// before modes existed. The library must not wedge itself into the F5 loop.
    /// </summary>
    [Fact]
    public void EscapeInADirectLaunchRequestsExitAndLeavesTheSessionStanding()
    {
        var drain = new DrainCounter();
        using CartSession session = CartSession.Start(_saverFolder);
        var machine = new ShellModeMachine(
            new CartLibrary(_root),
            static _ => throw new InvalidOperationException("a direct launch never starts sessions"),
            drain.Bump,
            session);
        Assert.Equal(ShellMode.Game, machine.Mode);

        machine.HandleEscape();

        Assert.True(machine.ExitRequested);
        Assert.Same(session, machine.Session);
        Assert.Equal(0, drain.Calls);
    }

    [Fact]
    public void EscapeInTheLibraryRequestsExit()
    {
        var machine = LibraryMachine(new DrainCounter());

        machine.HandleEscape();

        Assert.True(machine.ExitRequested);
    }

    /// <summary>
    /// Stage 2 made the editor real; the stage 1 transition claim still holds: X opens the
    /// editor mode from the library, and Esc from a clean session returns without quitting.
    /// The editor-specific behaviour (sheets, dirt, the exit prompt) lives in
    /// <see cref="SpriteEditorMachineTests"/>.
    /// </summary>
    [Fact]
    public void TheEditorOpensFromTheLibraryAndEscapeFromACleanSessionReturns()
    {
        var machine = LibraryMachine(new DrainCounter());

        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        Assert.NotNull(machine.Editor);             // a real session now, not a named empty screen

        machine.HandleEscape();
        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
        Assert.False(machine.ExitRequested);        // returning is not quitting
    }

    /// <summary>
    /// A cart that does not compile stays on the shelf and says why; the shell survives, and
    /// the working cart next to it still launches. This is the library's contract with an
    /// author mid-edit: their broken cart is a message, never a dead window.
    /// </summary>
    [Fact]
    public void ABrokenCartReportsOnTheLibraryScreenInsteadOfCrashingTheShell()
    {
        string brokenFolder = Path.Combine(_root, "broken");
        Directory.CreateDirectory(Path.Combine(brokenFolder, "src"));
        File.WriteAllText(Path.Combine(brokenFolder, "manifest.json"),
            "{\"name\":\"broken\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(Path.Combine(brokenFolder, "src", "main.cs"), "public class Broken : { not C#");

        var machine = LibraryMachine(new DrainCounter());
        Assert.Equal("broken", machine.Library.Selected!.Value.Name);   // sorts before "saver"

        Assert.Null(machine.LaunchSelected());
        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Session);
        Assert.NotNull(machine.LibraryMessage);
        Assert.StartsWith("broken:", machine.LibraryMessage, StringComparison.Ordinal);

        machine.Library.MoveSelection(+1);
        Assert.NotNull(machine.LaunchSelected());
        Assert.Null(machine.LibraryMessage);        // a successful launch clears the report
        machine.HandleEscape();
    }

    /// <summary>
    /// Leave and come back: the rescan keeps the bar on the cart just played and the relaunch
    /// is a fresh session at tick 0, not the old one somehow revived.
    /// </summary>
    [Fact]
    public void ACartCanBeRelaunchedFreshAfterLeavingIt()
    {
        var machine = LibraryMachine(new DrainCounter());
        Assert.NotNull(machine.LaunchSelected());
        machine.Session!.Update(5, default, rewinding: false);
        machine.HandleEscape();

        Assert.NotNull(machine.LaunchSelected());
        machine.Session!.Update(3, default, rewinding: false);

        Assert.Equal(3, machine.Session.Tick);
        machine.HandleEscape();
    }
}

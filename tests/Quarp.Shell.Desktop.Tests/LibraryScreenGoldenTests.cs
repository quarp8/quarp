using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The golden master of a tool screen</b> — the thing wave R1 was worth doing for.
///
/// <para>Until this wave the library screen was painted at the window's native resolution
/// through a <c>SpriteBatch</c>, and there was no artefact of it a test could look at: no
/// buffer, no pixels, nothing but draw calls into a graphics device no headless runner has. A
/// layout regression on the console's front page — a row off by three pixels, a footer running
/// off the edge, a selection bar on the wrong entry — was undetectable by every test in this
/// solution. Now the screen is drawn into a <see cref="Framebuffer"/> by the same core calls a
/// cartridge uses, so it can be hashed by exactly the owner that hashes a cartridge's frame:
/// <see cref="FrameHash"/>. Same digest, same 16-hex text form, same discipline — the one used
/// by <c>quarp sim</c>, by the replay tests and by <c>scripts/check-anchors.sh</c>. There is no
/// second hasher in this repository and this test does not introduce one.</para>
///
/// <para><b>These constants are not determinism anchors.</b> The eight anchors and the twelve
/// demo hashes are facts about the <em>cartridge's</em> console and must never move without a
/// verdict. These are facts about a shell screen's layout, they belong to no cross-architecture
/// promise, and they are expected to move whenever the screen is deliberately redesigned. The
/// discipline that does carry over is the one from PLAYBOOK §4: never re-pin silently. If one
/// of these changes, the commit message says which pixel moved and why.</para>
///
/// <para><b>Where these three constants came from</b> — read this before re-pinning one.
/// Wave R1 was carried out in an environment with no .NET SDK and no package feed, so nothing
/// in the repository could be built or run. The hashes below were therefore <em>derived</em>,
/// not observed: by transliterating <c>VirtualConsole</c>'s <c>Cls</c>, <c>RectFill</c>,
/// <c>Print</c> and <c>Plot</c> together with <see cref="SystemFont"/>'s glyph table and this
/// screen's draw order, and running <see cref="FrameHash"/>'s FNV-1a over the result. That is a
/// model of the rasterizer, and a model can be wrong where the original is right. If one of
/// these three fails on the first real build while the probe assertions above it all pass, the
/// most likely explanation by far is a slip in that derivation and not a defect in the screen:
/// check the probes, look at the frame, and re-pin with a note saying so. If a probe fails too,
/// the screen genuinely changed and the ordinary rule applies — say which pixel moved and
/// why.</para>
///
/// <para><b>Why the probes are here too.</b> A bare hash mismatch says "something moved" and
/// nothing else. The handful of <c>Pget</c> assertions above each hash name the specific
/// structural facts the picture is supposed to have — the bar is on the selected row and only
/// there, the rules reach their margins, the background is ink — so a failure tells whoever
/// reads it whether the screen is broken or merely redrawn.</para>
/// </summary>
public class LibraryScreenGoldenTests : IDisposable
{
    private readonly string _root;

    public LibraryScreenGoldenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-libscreen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// A library of exactly these carts, in this order. Real directories, like
    /// <c>CartLibraryTests</c>: the scan's job is to read a disk, and the sort is by name, so
    /// the list the screen receives is the same on every machine.
    /// </summary>
    private CartLibrary LibraryOf(params string[] names)
    {
        foreach (string name in names)
        {
            string folder = Path.Combine(_root, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "manifest.json"), "{}");
        }
        var library = new CartLibrary(_root);
        library.Rescan();
        Assert.Equal(names.Length, library.Entries.Count);
        return library;
    }

    /// <summary>The everyday picture: three carts, the bar on the middle one, no error line.</summary>
    [Fact]
    public void ThreeCartsWithTheBarOnTheMiddleOne()
    {
        CartLibrary library = LibraryOf("alpha", "beta", "gamma");
        library.MoveSelection(+1);
        var screen = new ShellScreen();

        LibraryLayout layout = LibraryRenderer.Draw(screen, library, null);

        // The screen is the console's screen, not a window's.
        Assert.Equal(160, screen.Framebuffer.Width);
        Assert.Equal(90, screen.Framebuffer.Height);
        Assert.Equal(3, layout.DrawnRows);
        Assert.Equal(0, layout.FirstVisible);

        VirtualConsole console = screen.Console;
        // Row 1 (y 18..24) carries the selection bar; rows 0 and 2 do not.
        Assert.Equal((byte)4, console.Pget(1, 18));
        Assert.Equal((byte)4, console.Pget(158, 24));
        Assert.Equal((byte)0, console.Pget(1, 11));
        Assert.Equal((byte)0, console.Pget(1, 25));
        // The two rules run from the margin to the margin and no further.
        Assert.Equal((byte)1, console.Pget(2, 8));
        Assert.Equal((byte)1, console.Pget(157, 81));
        Assert.Equal((byte)0, console.Pget(158, 81));
        // Nothing outside the library's six palette roles ever reaches the buffer.
        foreach (byte pixel in screen.Framebuffer.Pixels)
        {
            Assert.InRange(pixel, (byte)0, (byte)10);
        }

        Assert.Equal("e4cb4595387473c8", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The empty state — the first thing anyone sees on a fresh install, and the one screen a
    /// regression here would show to a new player before anything else.
    /// </summary>
    [Fact]
    public void AnEmptyLibraryTellsThePlayerWhereCartsGo()
    {
        var library = new CartLibrary(_root);
        library.Rescan();
        Assert.Empty(library.Entries);
        var screen = new ShellScreen();

        LibraryLayout layout = LibraryRenderer.Draw(screen, library, null);

        Assert.Equal(0, layout.DrawnRows);
        // No selection bar anywhere: colour 4 appears nowhere in an empty library's frame.
        Assert.DoesNotContain((byte)4, screen.Framebuffer.Pixels);
        Assert.Equal("8b972baa67eeae99", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// A failed launch puts a red line above the footer and costs the list one row. Both halves
    /// are in the hash; the row count is asserted separately so a failure can say which half
    /// moved.
    /// </summary>
    [Fact]
    public void AFailedLaunchMessageShortensTheList()
    {
        CartLibrary library = LibraryOf("alpha", "beta", "gamma");
        var screen = new ShellScreen();

        LibraryLayout layout = LibraryRenderer.Draw(
            screen, library, "cannot open beta: manifest.json is not valid json");

        Assert.True(layout.HasMessage);
        Assert.Equal(8, layout.VisibleRows);            // nine without the line
        // The message line is drawn in the error colour at y 74. Its first glyph is a lowercase
        // 'c', whose top row is blank, so the first ink pixel is one row down at x 3.
        Assert.Equal((byte)10, screen.Console.Pget(3, 75));
        Assert.Equal("8a1497cd9b65d427", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Two consoles, two framebuffers. The shell's screen and a cartridge's are separate
    /// instances by construction (<see cref="ShellScreen"/>), and this is the assertion that
    /// says so out loud: drawing the whole library leaves a console built the same way
    /// untouched. It is the property that lets a tool screen be opened over a paused game
    /// without eating the frame that game left behind — and, more importantly, the property
    /// that keeps anything the shell draws out of the buffer the CI hashes.
    /// </summary>
    [Fact]
    public void DrawingTheLibraryTouchesNoOtherConsole()
    {
        CartLibrary library = LibraryOf("alpha", "beta", "gamma");
        var shell = new ShellScreen();
        var other = new ShellScreen();
        string before = FrameHash.Of(other.Framebuffer);

        LibraryRenderer.Draw(shell, library, null);

        Assert.Equal(before, FrameHash.Of(other.Framebuffer));
        Assert.NotEqual(before, FrameHash.Of(shell.Framebuffer));
        Assert.NotSame(shell.Framebuffer, other.Framebuffer);
    }

    /// <summary>
    /// Redrawing the same state gives the same bytes: the screen depends on the library and the
    /// message and on nothing else — no clock, no window size, no leftover console state. That
    /// is what makes a pinned hash meaningful rather than lucky, and it is why
    /// <c>ShellScreen.Begin</c> resets camera, clip, palette and transparency before every draw.
    /// </summary>
    [Fact]
    public void TheScreenIsAPureFunctionOfTheLibraryAndTheMessage()
    {
        CartLibrary library = LibraryOf("alpha", "beta", "gamma");
        var screen = new ShellScreen();

        LibraryRenderer.Draw(screen, library, null);
        string first = FrameHash.Of(screen.Framebuffer);

        // A different picture in between, so a stale-state bug has something to leak.
        LibraryRenderer.Draw(screen, library, "boom");
        LibraryRenderer.Draw(screen, library, null);

        Assert.Equal(first, FrameHash.Of(screen.Framebuffer));
    }
}

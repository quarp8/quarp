using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The boot menu's doors through <see cref="ShellModeMachine"/> (M9 stage 4, ADR-028), with
/// real cartridges and a real scaffold on a temp disk — the same no-window discipline as
/// <see cref="ModeTransitionTests"/>. What the stage promises is walked end to end here:
/// born on the menu; door 1 into a freshly scanned library; door 2's by-path launch (the OS
/// picker and the window drop both end in the same machine call — the dialog itself is a
/// blocking OS call nothing headless can drive); door 3 scaffolding a cart with the very
/// template <c>quarp new</c> writes and landing in the editor, from which Esc lands the
/// library's bar on the newborn.
/// </summary>
public sealed class MenuModeTests : IDisposable
{
    private readonly string _root;
    private readonly string _carts;

    public MenuModeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-menu-" + Guid.NewGuid().ToString("N"));
        _carts = Path.Combine(_root, "carts");
        Directory.CreateDirectory(_carts);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ShellModeMachine Machine() => new(
        new CartLibrary(_carts),
        static path => CartSession.Start(path),
        static () => { },
        createRoot: _carts);

    private string WriteCart(string name)
    {
        string folder = Path.Combine(_carts, name);
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(Path.Combine(folder, "manifest.json"),
            $"{{\"name\":\"{name}\",\"author\":\"\",\"profile\":8}}");
        File.WriteAllText(Path.Combine(folder, "src", "main.cs"), """
            using Quarp.Api;

            public sealed class Blank : Cartridge
            {
                public override void Draw()
                {
                    Cls(0);
                }
            }
            """);
        return folder;
    }

    [Fact]
    public void PlainQuarpIsBornOnTheMenuInItsIntro()
    {
        var machine = Machine();

        Assert.Equal(ShellMode.Menu, machine.Mode);
        Assert.Equal(MenuPhase.Intro, machine.Menu.Phase);
        Assert.False(machine.ExitRequested);
    }

    [Fact]
    public void DoorOneOpensAFreshlyScannedLibrary()
    {
        var machine = Machine();
        machine.Menu.SkipIntro();
        WriteCart("appeared-after-boot");       // on disk after the machine was built

        machine.OpenLibrary();

        Assert.Equal(ShellMode.Library, machine.Mode);
        // The scan ran on the way through the door, not at construction.
        CartLibraryEntry entry = Assert.Single(machine.Library.Entries);
        Assert.Equal("appeared-after-boot", entry.Name);
    }

    [Fact]
    public void EscapeMidIntroSkipsInsteadOfQuitting()
    {
        var machine = Machine();

        machine.HandleEscape();

        Assert.False(machine.ExitRequested);
        Assert.Equal(MenuPhase.Menu, machine.Menu.Phase);
    }

    /// <summary>Door 3, the whole road: name → scaffold → editor → Esc → the bar on the newborn.</summary>
    [Fact]
    public void CreateGameScaffoldsTheRealTemplateAndLandsInTheEditor()
    {
        var machine = Machine();
        // Sorts ahead of "newborn": without SelectPath the rescan would park the bar here at
        // index 0, and the landing assertion below would pass by accident on a one-cart list.
        WriteCart("aaa-earlier");
        machine.Menu.SkipIntro();
        machine.BeginCreateGame();
        Assert.Equal(MenuPhase.NameEntry, machine.Menu.Phase);
        foreach (char c in "newborn")
        {
            machine.Menu.TypeChar(c);
        }

        machine.ConfirmCreateGame();

        Assert.Equal(ShellMode.Editor, machine.Mode);
        Assert.Equal("newborn", machine.Editor!.CartName);
        string folder = Path.Combine(_carts, "newborn");
        Assert.True(File.Exists(Path.Combine(folder, "manifest.json")), "manifest.json");
        // The very template quarp new writes — the loader judges it, not a byte comparison.
        Assert.Equal("newborn", CartSource.Load(folder).Manifest.Name);

        machine.HandleEscape();     // the newborn sheet is clean: straight back

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Equal("newborn", machine.Library.Selected?.Name);    // SelectPath's promise
    }

    [Fact]
    public void ATakenNameReportsAndKeepsTheFieldUp()
    {
        var machine = Machine();
        WriteCart("taken");
        machine.Menu.SkipIntro();
        machine.BeginCreateGame();
        foreach (char c in "taken")
        {
            machine.Menu.TypeChar(c);
        }

        machine.ConfirmCreateGame();

        Assert.Equal(ShellMode.Menu, machine.Mode);
        Assert.Equal(MenuPhase.NameEntry, machine.Menu.Phase);      // fix the name, not retype it
        Assert.Contains("ALREADY EXISTS", machine.Menu.Message);
        Assert.Null(machine.Editor);
    }

    [Fact]
    public void AnEmptyNameIsRefusedBeforeAnyDiskIsTouched()
    {
        var machine = Machine();
        machine.Menu.SkipIntro();
        machine.BeginCreateGame();

        machine.ConfirmCreateGame();

        Assert.Equal(MenuPhase.NameEntry, machine.Menu.Phase);
        Assert.NotNull(machine.Menu.Message);
        Assert.Empty(Directory.GetDirectories(_carts));
    }

    /// <summary>Door 2's machine half — the same call a window drop and the OS picker both end in.</summary>
    [Fact]
    public void ACartArrivingByPathLaunchesFromTheMenu()
    {
        var machine = Machine();
        string folder = WriteCart("dropped");
        machine.Menu.SkipIntro();

        CartSession? session = machine.LoadCartFromPath(folder);

        Assert.NotNull(session);
        Assert.Equal(ShellMode.Game, machine.Mode);

        machine.HandleEscape();     // a by-path cart is a library-style launch, not an F5 loop

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.False(machine.ExitRequested);
    }

    [Fact]
    public void ABrokenPathReportsOnTheScreenTheAuthorIsLookingAt()
    {
        var machine = Machine();
        machine.Menu.SkipIntro();

        Assert.Null(machine.LoadCartFromPath(Path.Combine(_root, "no-such-cart")));

        Assert.Equal(ShellMode.Menu, machine.Mode);
        Assert.Contains("no-such-cart", machine.Menu.Message);

        machine.OpenLibrary();
        Assert.Null(machine.LoadCartFromPath(Path.Combine(_root, "still-missing")));

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Contains("still-missing", machine.LibraryMessage);
        Assert.Null(machine.Menu.Message);      // the menu's line was cleared on the way in
    }

    [Fact]
    public void ADropDuringTheIntroIsIgnoredAndDuringNameEntryPutsTheFieldAway()
    {
        var machine = Machine();
        string folder = WriteCart("insistent");

        Assert.Null(machine.LoadCartFromPath(folder));      // mid-intro: not a screen yet
        Assert.Equal(MenuPhase.Intro, machine.Menu.Phase);

        machine.Menu.SkipIntro();
        machine.BeginCreateGame();
        machine.Menu.TypeChar('x');

        Assert.NotNull(machine.LoadCartFromPath(folder));

        Assert.Equal(ShellMode.Game, machine.Mode);
        Assert.Equal(MenuPhase.Menu, machine.Menu.Phase);    // the field went away first
    }
}

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
///
/// <para><b>What wave R6 changed here, and what it did not.</b> The order for that wave said to
/// re-pin the menu tests that pinned host pixels. None of them did — this file drives the
/// <em>machine</em>, never a surface, which is why it could be written before the menu had a
/// framebuffer at all, and why moving the screen onto the console did not touch a line of it.
/// The host-shaped fact about the boot menu lived in one place only, <c>FramePlacementTests</c>,
/// and it is re-pinned there with its own paragraph. What R6 <em>adds</em> here is the half of
/// the stage-2.5 parity law the boot menu never had: it was keyboard-only for as long as it was
/// host UI, because there was no grid to point at. Now there is, and
/// <see cref="EveryDoorOpensByPointerExactlyAsItOpensByKey"/> walks all three doors both ways
/// against the same machine.</para>
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

    /// <summary>
    /// <b>Input parity for the boot menu, both ways.</b> Every door is reachable by keyboard
    /// alone (arrows, the 1-2-3 hotkeys, Z/Enter) and by pointer alone, and the two channels
    /// differ exactly where a human's hands differ — how "which door" is computed, never in what
    /// is called once it is.
    ///
    /// <para>The pointer half goes through the real hit test the renderer's own layout answers
    /// (<see cref="MainMenuRenderer.LayoutFor"/> then <see cref="MainMenuLayout.HitRow"/>), and
    /// through the same two-step rule the window applies: the first press moves the bar, a press
    /// on the door that already has it walks through. That rule is four lines of
    /// <c>QuarpGame.ClickMenu</c> and is mirrored below rather than driven, for the reason
    /// <see cref="EditorButtonContractTests.RouteClick"/> already mirrors the editor's press
    /// dispatch: <c>QuarpGame</c> needs a <c>GraphicsDevice</c> and cannot be constructed in this
    /// process. Nothing about menu policy is duplicated — every verb the mirror calls is
    /// <see cref="MainMenuSession"/>'s or <see cref="ShellModeMachine"/>'s own public method.</para>
    ///
    /// <para>Break recipe: delete the <c>index != SelectedIndex</c> arm from
    /// <c>QuarpGame.ClickMenu</c> and the mirror below stops matching the window, which the
    /// second half of this test catches as a door that opened on its first click. Delete the
    /// gutter guard from <see cref="MainMenuLayout.HitRow"/> and the "between two doors" negative
    /// control goes red.</para>
    /// </summary>
    [Fact]
    public void EveryDoorOpensByPointerExactlyAsItOpensByKey()
    {
        var screen = new ShellScreen();
        MainMenuLayout layout = MainMenuRenderer.LayoutFor(screen);

        for (int door = 0; door < MainMenuSession.ItemCount; door++)
        {
            // --- keyboard alone: arrows from the top, then "go".
            var byKey = Machine();
            byKey.Menu.SkipIntro();
            for (int i = 0; i < door; i++)
            {
                byKey.Menu.MoveSelection(+1);
            }

            // --- pointer alone: two presses in the middle of that door's bar.
            // The bar starts on door 0, and a press on the door that ALREADY has the bar is the
            // opening press, not the moving one (that is the whole two-step rule). So for door 0
            // the bar is first parked on its neighbour: otherwise this loop would be asserting
            // that the rule does not hold for the one door it starts on. The neighbour case is
            // pinned on its own below, in APressOnTheDoorThatAlreadyHasTheBarOpensItAtOnce.
            var byMouse = Machine();
            byMouse.Menu.SkipIntro();
            if (byMouse.Menu.SelectedIndex == door)
            {
                byMouse.Menu.MoveSelection(door == 0 ? +1 : -1);
                Assert.NotEqual(door, byMouse.Menu.SelectedIndex);
            }
            Microsoft.Xna.Framework.Rectangle bar = layout.Row(door);
            int x = bar.X + bar.Width / 2;
            int y = bar.Y + MainMenuLayout.RowHeight / 2;
            Assert.Equal(door, layout.HitRow(x, y));
            bool opened = ClickMirror(byMouse.Menu, layout, x, y);
            Assert.False(opened);                       // the first press only moves the bar
            opened = ClickMirror(byMouse.Menu, layout, x, y);
            Assert.True(opened);                        // the second press on the same door goes

            Assert.Equal(byKey.Menu.Selected, byMouse.Menu.Selected);
            Assert.Equal((MenuItem)door, byMouse.Menu.Selected);
        }

        // The negative controls: the gutter between two doors, and the reserved lines below them.
        var machine = Machine();
        machine.Menu.SkipIntro();
        machine.Menu.MoveSelection(+2);
        Microsoft.Xna.Framework.Rectangle first = layout.Row(0);
        Assert.False(ClickMirror(machine.Menu, layout, first.X + 10, first.Bottom));
        Assert.False(ClickMirror(machine.Menu, layout, first.X + 10, layout.MessageY));
        Assert.False(ClickMirror(machine.Menu, layout, 0, first.Y));
        Assert.Equal(MenuItem.CreateGame, machine.Menu.Selected);   // nothing moved the bar
    }

    /// <summary>
    /// The other half of the two-step, stated on its own so the rule cannot be read out of the
    /// loop above by accident: a press on the door that ALREADY carries the bar walks through it
    /// on that very press. The menu is born with the bar on door 0, so a pointer that arrives
    /// there and clicks once opens the library — one press, not two. This is
    /// <c>QuarpGame.ClickMenu</c>'s last line, and it is the same rule
    /// <c>QuarpGame.ClickLibrary</c> applies to a row that already has the bar.
    ///
    /// <para>Break recipe: make <c>ClickMenu</c> demand a second press even on the selected door
    /// (an "armed" flag, say) and this goes red while the loop above stays green — which is
    /// exactly why the two halves are written apart.</para>
    /// </summary>
    [Fact]
    public void APressOnTheDoorThatAlreadyHasTheBarOpensItAtOnce()
    {
        var screen = new ShellScreen();
        MainMenuLayout layout = MainMenuRenderer.LayoutFor(screen);

        var machine = Machine();
        machine.Menu.SkipIntro();
        Assert.Equal(0, machine.Menu.SelectedIndex);         // born on door 0

        Microsoft.Xna.Framework.Rectangle bar = layout.Row(0);
        int x = bar.X + bar.Width / 2;
        int y = bar.Y + MainMenuLayout.RowHeight / 2;

        Assert.True(ClickMirror(machine.Menu, layout, x, y));
        Assert.Equal(MenuItem.Library, machine.Menu.Selected);

        // The negative control: the same press one door down does NOT open, it only moves the
        // bar — so the True above is the rule speaking and not the mirror answering True always.
        Microsoft.Xna.Framework.Rectangle next = layout.Row(1);
        Assert.False(ClickMirror(machine.Menu, layout, next.X + next.Width / 2,
            next.Y + MainMenuLayout.RowHeight / 2));
        Assert.Equal(1, machine.Menu.SelectedIndex);
    }

    /// <summary>
    /// The four lines of <c>QuarpGame.ClickMenu</c> that survive after subtracting the window:
    /// window-to-console is <see cref="FramePlacement"/>'s and is tested there, so what is left
    /// is the two-step. Returns true when this press would have opened the door.
    /// </summary>
    private static bool ClickMirror(MainMenuSession menu, in MainMenuLayout layout, int x, int y)
    {
        if (layout.HitRow(x, y) is not int index)
        {
            return false;
        }
        if (index != menu.SelectedIndex)
        {
            menu.MoveSelection(index - menu.SelectedIndex);
            return false;
        }
        return true;
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

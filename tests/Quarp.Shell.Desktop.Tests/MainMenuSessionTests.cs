using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The boot screen's model (M9 stage 4, ADR-028), driven headless the way
/// <see cref="CartLibraryTests"/> drives the library: phases, the intro clock, the skip
/// edge, selection, the name field's filter — behaviour, no window.
///
/// <para>The spec-line test at the bottom pins the screen to the <b>ratified literals</b> of
/// SPEC-8 (§1 video, §2 colors, §7 tick rate, §6 cart/code, §7 save), not to the constants
/// the lines are built from — the constants are the code under test, and a test that read
/// them back would be the mirror the stage-2 lessons warn about twice. The spec is
/// permanent; these strings may change only when an ADR changes the spec.</para>
/// </summary>
public class MainMenuSessionTests
{
    [Fact]
    public void TheIntroEndsOnItsOwnClock()
    {
        var menu = new MainMenuSession();
        Assert.Equal(MenuPhase.Intro, menu.Phase);

        // 120 frames at 60 Hz = 2.0 s — past IntroDuration with margin, not at its edge
        // (the first cut of this ran 100 frames = 1.667 s and proved only that 1.7 > 1.667).
        for (int i = 0; i < 120; i++)
        {
            menu.AdvanceIntro(1.0 / 60, anyInputDown: false);
        }

        Assert.Equal(MenuPhase.Menu, menu.Phase);
        Assert.Equal(MainMenuSession.IntroDuration, menu.IntroClock);    // the clock clamps at the end
    }

    [Fact]
    public void AFreshKeySkipsTheIntro()
    {
        var menu = new MainMenuSession();
        menu.AdvanceIntro(0.1, anyInputDown: false);

        bool left = menu.AdvanceIntro(0.016, anyInputDown: true);

        Assert.True(left);
        Assert.Equal(MenuPhase.Menu, menu.Phase);
        Assert.True(menu.IntroClock < 1);       // it really was a skip, not the clock
    }

    /// <summary>
    /// The Enter that launched <c>quarp</c> from a terminal can still be physically down on
    /// the first frame; a key that was never released inside the intro is not a request to
    /// skip it.
    /// </summary>
    [Fact]
    public void AKeyHeldSinceLaunchDoesNotSkipUntilRepressed()
    {
        var menu = new MainMenuSession();

        Assert.False(menu.AdvanceIntro(0.016, anyInputDown: true));
        Assert.False(menu.AdvanceIntro(0.016, anyInputDown: true));
        Assert.Equal(MenuPhase.Intro, menu.Phase);

        menu.AdvanceIntro(0.016, anyInputDown: false);      // released…
        Assert.True(menu.AdvanceIntro(0.016, anyInputDown: true));   // …and pressed again: that is a skip
    }

    [Fact]
    public void SelectionClampsAtBothEndsLikeTheLibrary()
    {
        var menu = new MainMenuSession();
        menu.SkipIntro();

        menu.MoveSelection(-3);
        Assert.Equal(MenuItem.Library, menu.Selected);

        menu.MoveSelection(+1);
        menu.MoveSelection(+1);
        menu.MoveSelection(+5);
        Assert.Equal(MenuItem.CreateGame, menu.Selected);
    }

    [Fact]
    public void ADigitSelectsAndMeansGoAndTheToolbarTailIsNoDoor()
    {
        var menu = new MainMenuSession();
        menu.SkipIntro();

        Assert.True(menu.ActivateDigit(2));
        Assert.Equal(MenuItem.LoadCart, menu.Selected);

        // The shared reader reports D1..D6; the menu has three rows and 0 means "no digit".
        Assert.False(menu.ActivateDigit(0));
        Assert.False(menu.ActivateDigit(4));
        Assert.Equal(MenuItem.LoadCart, menu.Selected);
    }

    [Fact]
    public void TheNameFieldFoldsFiltersAndCaps()
    {
        var menu = new MainMenuSession();
        menu.SkipIntro();
        menu.BeginNameEntry();
        Assert.Equal(MenuPhase.NameEntry, menu.Phase);

        foreach (char c in "My Game_2!")
        {
            menu.TypeChar(c);
        }

        // Uppercase folded, the space and the bang dropped, everything legal kept.
        Assert.Equal("mygame_2", menu.NameText);
        Assert.True(menu.CanConfirmName);

        menu.EraseChar();
        Assert.Equal("mygame_", menu.NameText);

        for (int i = 0; i < 100; i++)
        {
            menu.TypeChar('x');
        }
        Assert.Equal(Quarp.CartKit.CartScaffold.MaxNameLength, menu.NameText.Length);
    }

    [Fact]
    public void AnEmptyNameCannotConfirmAndCancelForgets()
    {
        var menu = new MainMenuSession();
        menu.SkipIntro();
        menu.BeginNameEntry();

        Assert.False(menu.CanConfirmName);

        menu.TypeChar('a');
        menu.CancelNameEntry();
        Assert.Equal(MenuPhase.Menu, menu.Phase);

        menu.BeginNameEntry();
        Assert.Equal("", menu.NameText);        // the field starts clean every time
    }

    /// <summary>The screen's figures against the ratified spec, literal by literal (see the class comment).</summary>
    [Fact]
    public void TheSpecLinesSayExactlyWhatSpec8Ratified()
    {
        (string Label, string Value)[][] lines = MainMenuSession.SpecLines();

        Assert.Equal(
            new[] { ("VIDEO", "160x90"), ("COL", "32"), ("FPS", "60") },
            lines[0]);
        Assert.Equal(
            new[] { ("CART", "320K"), ("CODE", "256K"), ("SAVE", "256B") },
            lines[1]);
    }
}

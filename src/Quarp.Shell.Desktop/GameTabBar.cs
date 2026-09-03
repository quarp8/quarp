using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The top band of the <b>game</b> screen while it is paused (M9 stage 5a): the exit arrow at
/// the left, the cartridge's name beside it, the six tabs off the right corner — the same band
/// the five editor screens wear, on the one screen that never had one.
///
/// <para><b>While the game is running there is no band at all</b>, and that is deliberate rather
/// than unfinished: the console is 160x90, the band costs eleven of those ninety rows, and a
/// player is not editing anything. It appears exactly when the simulation stops, which is also
/// exactly when the author needs to know which cartridge is open and where the other five tabs
/// are.</para>
///
/// <para><b>It is painted over the frame, not into it.</b> The paused picture underneath is the
/// cartridge's own framebuffer — the project's golden master, hashed by <c>quarp sim</c> and
/// compared across architectures by CI — so nothing here may write a pixel of it, and the band
/// must not push it down either: the frame the player sees at the moment they press Esc has to
/// stay the frame they were playing. So this class draws into the <em>shell's</em> console (the
/// surface that is never presented while a cartridge is on screen) and
/// <see cref="ShellOverlay.ShowBand"/> lifts the finished rows into the same RGBA layer the
/// pause menu and the PAUSE indicator ride on.</para>
///
/// <para><b>Why that indirection instead of a painter of its own.</b> The band's geometry has
/// one owner (<see cref="ConsoleChrome"/>) and its pixels have one owner
/// (<see cref="ConsoleChromeRenderer"/>), and both of them speak <see cref="VirtualConsole"/>.
/// A second painter that plotted icon masks into the overlay's colour array would be a second
/// answer to "what does a tab button look like", which is exactly the class of bug that had the
/// F-key order and the icon order disagreeing before M9 stage 5. Copying finished pixels costs
/// one memcmp and 1760 palette lookups on the frames the band actually changes.</para>
/// </summary>
public readonly struct GameTabBar
{
    private readonly EditorButtonPlace[] _buttons;

    private GameTabBar(ConsoleChrome chrome, EditorButtonPlace[] buttons)
    {
        Chrome = chrome;
        _buttons = buttons;
    }

    /// <summary>The frame this band was measured on — the console's own size, never the window's.</summary>
    public ConsoleChrome Chrome { get; }

    /// <summary>The seven placed buttons: the exit arrow, then the six tabs from the right corner leftwards.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons => _buttons;

    /// <summary>
    /// How many rows of the screen the band occupies, the rule under it included — which is what
    /// <see cref="ShellOverlay.ShowBand"/> is handed, because a band without its rule reads as a
    /// hole in the picture rather than as something laid on top of it.
    /// </summary>
    public int Rows => ConsoleChrome.TopBarHeight + 1;

    /// <summary>Measures the band for a console of the given size. Nothing here is drawn; nothing here is cached.</summary>
    public static GameTabBar Compute(int screenWidth, int screenHeight)
    {
        var buttons = new EditorButtonPlace[1 + ConsoleChrome.RightTabs.Count];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(screenWidth, screenHeight, buttons, ref placed);
        return new GameTabBar(chrome, buttons);
    }

    /// <summary>The button under a console point, or false — the pointer's half of F1..F6 and Esc.</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        ConsoleChrome.TryButton(_buttons, x, y, out id);

    /// <summary>Whether this band places that button — so a hover target measured on another screen cannot ask for a label here.</summary>
    public bool Holds(EditorButton id)
    {
        foreach (EditorButtonPlace place in _buttons)
        {
            if (place.Id == id)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Paints the band into the shell's own console. The whole surface is cleared, not just the
    /// band: with nothing running the game screen <em>is</em> this console (Р7's START menu
    /// stands on it), and with a cartridge running only <see cref="Rows"/> rows are ever lifted
    /// out, so the clear costs a screen nobody sees.
    /// </summary>
    /// <param name="title">The cartridge's name — <see cref="ShellModeMachine.GameTitle"/>.</param>
    /// <param name="hover">What the pointer is on, from the shell's one hover clock.</param>
    /// <param name="tooltipVisible">Whether that hover has lasted the owner's three seconds.</param>
    public void Draw(ShellScreen screen, string title, HoverTarget? hover, bool tooltipVisible)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(title);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(ConsoleChromeRenderer.Ink);
        console.RectFill(0, Chrome.HeaderRuleY, Chrome.ScreenWidth, 1, ConsoleChromeRenderer.Dim);
        foreach (EditorButtonPlace place in _buttons)
        {
            var state = new EditorButtonState(
                // The game's own tab is the one that reads "you are here", exactly as each editor
                // screen lights its own.
                Active: place.Id == EditorButton.GameTab,
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: false,
                CanUndo: false,
                CanRedo: false);
            ConsoleChromeRenderer.DrawButton(
                console, place, state, EditorIcons.IconFor(place.Id), text: null);
        }
        // The band's free strip carries the hovered control's label, and the cartridge's name
        // when nothing is hovered — the same field, the same fallback rule and the same painter
        // the five editor screens use for their own names (TIC-80's drawToolbar, by way of
        // ConsoleChrome.TooltipChars).
        ConsoleChromeRenderer.DrawTooltipField(
            console,
            Chrome,
            tooltipVisible && hover?.Button is EditorButton hovered && Holds(hovered)
                ? EditorIcons.Tooltip(hovered)
                : null,
            title);
    }
}

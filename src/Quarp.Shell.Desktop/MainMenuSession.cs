using System.Text;
using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>Where the main menu is in its short life: the boot animation, the menu itself, or the name field.</summary>
public enum MenuPhase
{
    Intro,
    Menu,
    NameEntry,
}

/// <summary>The three doors of the boot menu, in the order the mockup draws them.</summary>
public enum MenuItem
{
    Library = 0,
    LoadCart = 1,
    CreateGame = 2,
}

/// <summary>
/// The model behind the boot screen — what <c>quarp</c> without arguments shows since the
/// boot-menu wave (M9 stage 4, owner's order of 2026-08-24, mockup in the order): an intro
/// in the palette's own colors, then QUARP / 1 LIBRARY / 2 LOAD CART / 3 CREATE GAME.
/// The niche's three consoles all boot through a splash into a terminal (PICO-8 manual;
/// TIC-80's <c>--skip</c> flag names the animation; LIKO-12's BIOS POST screen); Quarp has
/// no terminal to land in (ADR-009), so the landing is this menu — ADR-028.
///
/// <para>Deliberately free of MonoGame, like <see cref="CartLibrary"/>: phases, the intro
/// clock, selection and the name field are where the behaviour lives, and keeping them off
/// the graphics device is what lets the transition tests drive a whole boot without a
/// window. Time enters as plain elapsed seconds; "any key" enters as one bool the caller
/// reads off the real keyboard.</para>
/// </summary>
public sealed class MainMenuSession
{
    /// <summary>Doors on the menu, also the range of the 1-2-3 hotkeys the mockup numbers.</summary>
    public const int ItemCount = 3;

    /// <summary>
    /// How long the whole intro runs before the menu appears by itself. Short on purpose:
    /// the niche's boot animations are moments, not cutscenes — TIC-80 ships a flag whose
    /// whole job is skipping its one, and PICO-8's forum asked for the same for years.
    /// Ours is under two seconds <em>and</em> any key skips it.
    /// </summary>
    public const double IntroDuration = 1.7;

    private readonly StringBuilder _name = new();

    /// <summary>True while a key was down on the previous look, so only a fresh press skips the intro.</summary>
    private bool _anyKeyWasDown = true;   // a key still held from the terminal that launched us is not a skip

    public MenuPhase Phase { get; private set; } = MenuPhase.Intro;

    /// <summary>Seconds since the intro started, clamped to <see cref="IntroDuration"/>; the renderer's clock.</summary>
    public double IntroClock { get; private set; }

    /// <summary>The row the selection bar is on, 0..2.</summary>
    public int SelectedIndex { get; private set; }

    public MenuItem Selected => (MenuItem)SelectedIndex;

    /// <summary>The name field's current text — always already folded and filtered, never invalid characters.</summary>
    public string NameText => _name.ToString();

    /// <summary>
    /// What the menu's message line says (a failed load, a refused name), or null. The mode
    /// machine writes it, the way <c>ShellModeMachine.LibraryMessage</c> reports a failed
    /// launch; it clears on the next phase change so a stale error cannot outlive its cause.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// One look at the intro per frame: advances the clock, ends the intro on its own at
    /// <see cref="IntroDuration"/>, and ends it early on a fresh key or click —
    /// <paramref name="anyInputDown"/> is level, not edge, and the edge is taken here so a
    /// key held since before the window existed never counts. Returns true when this call
    /// left the intro (the caller silences the boot jingle on that frame).
    /// </summary>
    public bool AdvanceIntro(double elapsedSeconds, bool anyInputDown)
    {
        if (Phase != MenuPhase.Intro)
        {
            return false;
        }
        IntroClock = Math.Min(IntroClock + Math.Max(0, elapsedSeconds), IntroDuration);
        bool skip = anyInputDown && !_anyKeyWasDown;
        _anyKeyWasDown = anyInputDown;
        if (skip || IntroClock >= IntroDuration)
        {
            Phase = MenuPhase.Menu;
            return true;
        }
        return false;
    }

    /// <summary>The escape hatch the mode machine pulls when Esc arrives mid-intro.</summary>
    public void SkipIntro()
    {
        if (Phase == MenuPhase.Intro)
        {
            Phase = MenuPhase.Menu;
        }
    }

    /// <summary>Clamped like the library's bar, and for the same reason: a held key settles on an end.</summary>
    public void MoveSelection(int delta)
    {
        if (Phase == MenuPhase.Menu)
        {
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, ItemCount - 1);
        }
    }

    /// <summary>
    /// The 1-2-3 hotkeys the mockup prints on the rows: a valid digit moves the bar AND means
    /// "go" (three doors need no second keypress to walk through). Returns true when the
    /// digit was one of the rows.
    /// </summary>
    public bool ActivateDigit(int digit)
    {
        if (Phase != MenuPhase.Menu || digit is < 1 or > ItemCount)
        {
            return false;
        }
        SelectedIndex = digit - 1;
        return true;
    }

    public void BeginNameEntry()
    {
        if (Phase == MenuPhase.Menu)
        {
            Phase = MenuPhase.NameEntry;
            _name.Clear();
            Message = null;
        }
    }

    /// <summary>Esc in the field, or the machine putting the menu back after a create: the field forgets.</summary>
    public void CancelNameEntry()
    {
        if (Phase == MenuPhase.NameEntry)
        {
            Phase = MenuPhase.Menu;
            Message = null;
        }
    }

    /// <summary>
    /// One typed character into the name field. Uppercase folds to lowercase (cart folders
    /// are lowercase by convention — the scaffold's gate would refuse capitals, and refusing
    /// a shift-typed letter the field could just fold would be pedantry); everything outside
    /// <c>[a-z0-9-_]</c> is dropped silently; the length cap is the scaffold's own.
    /// </summary>
    public void TypeChar(char c)
    {
        if (Phase != MenuPhase.NameEntry)
        {
            return;
        }
        if (c is >= 'A' and <= 'Z')
        {
            c = (char)(c + ('a' - 'A'));
        }
        bool allowed = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
        if (allowed && _name.Length < CartScaffold.MaxNameLength)
        {
            _name.Append(c);
        }
    }

    public void EraseChar()
    {
        if (Phase == MenuPhase.NameEntry && _name.Length > 0)
        {
            _name.Length--;
        }
    }

    /// <summary>True when Enter should create — the scaffold's own gate, asked before any disk is touched.</summary>
    public bool CanConfirmName => Phase == MenuPhase.NameEntry && CartScaffold.IsValidName(NameText);

    /// <summary>
    /// The two spec rows under the wordmark, label/value pairs so the renderer can give
    /// labels and values their mockup colors. Values come from the ratified constants —
    /// screen from the profile, colors from the master palette, tick rate from the shell's
    /// accumulator, cart and code budgets from the packer, save from the console's slots —
    /// so this screen cannot drift from SPEC-8 §1/§2/§6/§7 without the constant itself
    /// moving, and <c>MainMenuSessionTests</c> pins the rendered figures to the ratified
    /// literals from the other side.
    /// </summary>
    public static (string Label, string Value)[][] SpecLines() => new[]
    {
        new[]
        {
            ("VIDEO", $"{ConsoleProfile.Profile8.Width}x{ConsoleProfile.Profile8.Height}"),
            ("COL", $"{Palette.MasterCount}"),
            ("FPS", $"{TickAccumulator.TicksPerSecond}"),
        },
        new[]
        {
            ("CART", $"{Quarp8Package.MaxPackageBytes / 1024}K"),
            ("CODE", $"{CodeBudget.MaxBytes / 1024}K"),
            ("SAVE", $"{Quarp.Core.VirtualConsole.PersistentSlots * 4}B"),
        },
    };

    /// <summary>The row labels, indexed by <see cref="MenuItem"/> — one owner for the words on the doors.</summary>
    public static string ItemLabel(MenuItem item) => item switch
    {
        MenuItem.Library => "LIBRARY",
        MenuItem.LoadCart => "LOAD CART",
        _ => "CREATE GAME",
    };
}

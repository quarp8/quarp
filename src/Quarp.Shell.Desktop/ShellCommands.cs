using Microsoft.Xna.Framework.Input;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The shell's time-control keys for one frame, already edge-detected (API-8 §8):
///
/// <list type="table">
///   <item><term>Space</term><description>pause / resume</description></item>
///   <item><term>.</term><description>one tick forward</description></item>
///   <item><term>,</term><description>one tick back</description></item>
///   <item><term>[ / ]</term><description>slower / faster along the ladder</description></item>
///   <item><term>Backspace (held)</term><description>rewind in real time</description></item>
///   <item><term>Home</term><description>back to tick 0</description></item>
///   <item><term>F5 / F8</term><description>save replay / play replay</description></item>
///   <item><term>Esc</term><description>quit</description></item>
/// </list>
///
/// These never reach the cartridge: <see cref="InputMapper"/> maps a disjoint set of keys,
/// so a cart cannot bind space to jump and fight the shell over it. Everything but the
/// rewind is a press, not a hold — repeat-on-hold would make a single tap of <c>,</c>
/// unpredictable, and holding is what Backspace is for.
///
/// <para><b>The menu block (M9).</b> The library and the editor stub read the
/// <c>Menu*</c> fields; the game mode ignores them, because in a game those same keys
/// belong to the cartridge via <see cref="InputMapper"/>. They live in this one struct,
/// filled by the one reader with the one previous-frame state, so a key held across a mode
/// switch cannot fire twice: Esc that left the game is already "down" when the library's
/// first frame reads the keyboard, and edge detection sees no edge.</para>
/// </summary>
public readonly struct ShellCommands
{
    public bool Quit { get; init; }
    public bool TogglePause { get; init; }
    public bool StepForward { get; init; }
    public bool StepBack { get; init; }
    public bool Slower { get; init; }
    public bool Faster { get; init; }
    public bool Rewinding { get; init; }
    public bool ToStart { get; init; }
    public bool SaveReplay { get; init; }
    public bool PlayReplay { get; init; }

    /// <summary>Library: move the selection bar up. Editor: canvas cursor one pixel up.</summary>
    public bool MenuUp { get; init; }

    /// <summary>Library: move the selection bar down. Editor: canvas cursor one pixel down.</summary>
    public bool MenuDown { get; init; }

    /// <summary>Editor: canvas cursor one pixel left (M9 stage 2.5 keyboard drawing). The library's list has no columns.</summary>
    public bool MenuLeft { get; init; }

    /// <summary>Editor: canvas cursor one pixel right.</summary>
    public bool MenuRight { get; init; }

    /// <summary>
    /// Library: launch the selected cart — Z or Enter, the confirm keys the pad maps to
    /// O/Start. In the editor's exit prompt the same Z means "save and exit". Never fires
    /// with Ctrl held: Ctrl+Z is <see cref="EditorUndo"/>, and a chord must not double as
    /// its bare key.
    /// </summary>
    public bool MenuConfirm { get; init; }

    /// <summary>
    /// Library: open the sprite editor for the selected cart — X (M9 stage 2). In the
    /// editor's exit prompt the same X means "exit without saving"; during normal editing it
    /// is the keyboard eyedropper at the canvas cursor (stage 2.5 parity — the key mirrors
    /// the right mouse button). Ctrl-chorded X is ignored for the same reason as
    /// <see cref="MenuConfirm"/>.
    /// </summary>
    public bool MenuEditor { get; init; }

    /// <summary>Editor: Ctrl+Z — undo one pencil stroke.</summary>
    public bool EditorUndo { get; init; }

    /// <summary>Editor: Ctrl+Y — redo.</summary>
    public bool EditorRedo { get; init; }

    /// <summary>Editor: Ctrl+S — save the sheet (a no-op on a clean session, by the save contract).</summary>
    public bool EditorSave { get; init; }

    /// <summary>Editor: B — pencil ↔ bucket. Bare key, so Ctrl-guarded like every editor letter (wave 2c).</summary>
    public bool EditorToolToggle { get; init; }

    /// <summary>Editor: Tab — cycle the region size 8/16/32 px.</summary>
    public bool EditorRegionCycle { get; init; }

    /// <summary>Editor: F — flip the region horizontally (PICO-8's key, per the niche survey).</summary>
    public bool EditorFlipH { get; init; }

    /// <summary>Editor: V — flip the region vertically.</summary>
    public bool EditorFlipV { get; init; }

    /// <summary>Editor: R — rotate the region 90° clockwise.</summary>
    public bool EditorRotate { get; init; }

    /// <summary>Editor: Delete — clear the region to color 0.</summary>
    public bool EditorClear { get; init; }

    /// <summary>
    /// Editor: the keyboard pencil is held — bare Z (never the Ctrl+Z chord) or Space, either
    /// one (M9 stage 2.5: draw a stroke by holding this and steering with the arrows). Space
    /// doubles as the game mode's pause; the modes never read each other's fields.
    /// </summary>
    public bool EditorPaintDown { get; init; }

    /// <summary>Editor: the keyboard pencil went down this frame — begins a stroke (or fills) at the canvas cursor.</summary>
    public bool EditorPaintPressed { get; init; }

    /// <summary>
    /// Editor: the keyboard pencil came up this frame — commits the stroke as one undo step,
    /// the exact mirror of the mouse's <see cref="EditorMouse.LeftReleased"/>. Pressing Ctrl
    /// while Z is held counts as a release: the chord takes the key, and the open gesture must
    /// close rather than smear into whatever Ctrl+Z is about to undo.
    /// </summary>
    public bool EditorPaintReleased { get; init; }

    /// <summary>
    /// Editor: which toolbar digit (1-5, the toolbar's top-to-bottom order) was pressed this
    /// frame, 0 for none. The digit→tool policy — including that stub tools stay dead — is
    /// <see cref="EditorIcons.ToolForDigit"/>'s, not the reader's: this only reports the key.
    /// </summary>
    public int EditorToolDigit { get; init; }

    /// <summary>Editor: , — previous palette color (the keyboard's swatch hand; shown in the swatch tooltips).</summary>
    public bool EditorColorPrev { get; init; }

    /// <summary>Editor: . — next palette color, wrapping 15 → 0.</summary>
    public bool EditorColorNext { get; init; }
}

/// <summary>
/// Turns raw keyboard states into <see cref="ShellCommands"/>, remembering the previous
/// frame so a held key produces exactly one command. Lives in the shell, one instance per
/// window; polling the keyboard is the caller's job, because the same
/// <see cref="KeyboardState"/> also drives the cartridge's own input.
/// </summary>
public sealed class ShellCommandReader
{
    private KeyboardState _previous;

    public ShellCommands Read(KeyboardState keyboard)
    {
        bool ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        // The keyboard pencil's held state is computed against each frame's own Ctrl: pressing
        // Ctrl mid-hold turns "down" into "released" (the chord takes the key), and both edges
        // below fall out of comparing the two frames' truths rather than raw key states.
        bool prevCtrl = _previous.IsKeyDown(Keys.LeftControl) || _previous.IsKeyDown(Keys.RightControl);
        bool paintDown = (!ctrl && keyboard.IsKeyDown(Keys.Z)) || keyboard.IsKeyDown(Keys.Space);
        bool paintWasDown = (!prevCtrl && _previous.IsKeyDown(Keys.Z)) || _previous.IsKeyDown(Keys.Space);
        var commands = new ShellCommands
        {
            Quit = Pressed(keyboard, Keys.Escape),
            TogglePause = Pressed(keyboard, Keys.Space),
            StepForward = Pressed(keyboard, Keys.OemPeriod),
            StepBack = Pressed(keyboard, Keys.OemComma),
            Slower = Pressed(keyboard, Keys.OemOpenBrackets),
            Faster = Pressed(keyboard, Keys.OemCloseBrackets),
            Rewinding = keyboard.IsKeyDown(Keys.Back),
            ToStart = Pressed(keyboard, Keys.Home),
            SaveReplay = Pressed(keyboard, Keys.F5),
            PlayReplay = Pressed(keyboard, Keys.F8),
            MenuUp = Pressed(keyboard, Keys.Up),
            MenuDown = Pressed(keyboard, Keys.Down),
            MenuLeft = Pressed(keyboard, Keys.Left),
            MenuRight = Pressed(keyboard, Keys.Right),
            MenuConfirm = (!ctrl && Pressed(keyboard, Keys.Z)) || Pressed(keyboard, Keys.Enter),
            MenuEditor = !ctrl && Pressed(keyboard, Keys.X),
            EditorUndo = ctrl && Pressed(keyboard, Keys.Z),
            EditorRedo = ctrl && Pressed(keyboard, Keys.Y),
            EditorSave = ctrl && Pressed(keyboard, Keys.S),
            // The editor letters carry the !ctrl guard for the same reason MenuConfirm does:
            // a chord must not double as its bare key, today (Ctrl+S over a future S-binding)
            // or when a chord lands on these letters later.
            EditorToolToggle = !ctrl && Pressed(keyboard, Keys.B),
            EditorRegionCycle = Pressed(keyboard, Keys.Tab),
            EditorFlipH = !ctrl && Pressed(keyboard, Keys.F),
            EditorFlipV = !ctrl && Pressed(keyboard, Keys.V),
            EditorRotate = !ctrl && Pressed(keyboard, Keys.R),
            EditorClear = Pressed(keyboard, Keys.Delete),
            EditorPaintDown = paintDown,
            EditorPaintPressed = paintDown && !paintWasDown,
            EditorPaintReleased = !paintDown && paintWasDown,
            EditorToolDigit = ToolDigit(keyboard),
            EditorColorPrev = Pressed(keyboard, Keys.OemComma),
            EditorColorNext = Pressed(keyboard, Keys.OemPeriod),
        };
        _previous = keyboard;
        return commands;
    }

    /// <summary>First freshly pressed toolbar digit, 0 for none — two digits in one frame is not a gesture worth defining.</summary>
    private int ToolDigit(KeyboardState keyboard)
    {
        for (int i = 0; i < 5; i++)
        {
            if (Pressed(keyboard, Keys.D1 + i))
            {
                return i + 1;
            }
        }
        return 0;
    }

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_previous.IsKeyDown(key);
}

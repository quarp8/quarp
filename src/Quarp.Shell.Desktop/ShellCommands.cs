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
        };
        _previous = keyboard;
        return commands;
    }

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_previous.IsKeyDown(key);
}

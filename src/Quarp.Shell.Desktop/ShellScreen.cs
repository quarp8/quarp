using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The shell's own console — the surface every tool screen is drawn on from wave R1 onwards.
///
/// <para><b>The law this implements</b> (owner, 2026-08-25): the console's resolution and
/// palette are the same for games and for the console's own tools, and an editor "runs on the
/// same virtual hardware as a cartridge". That is how the references do it: TIC-80 lays its
/// editors out in screen pixels (<c>sprite.c</c>: <c>SheetX = TIC80_WIDTH - …</c>,
/// <c>PaletteY = 112</c> on a 240x136 screen) and paints them with <c>tic_api_rect</c> /
/// <c>tic_api_print</c> / <c>tic_api_spr</c>; LIKO-12 measures everything from
/// <c>screenSize()</c> and draws through the same GPU peripheral a game uses. What we had
/// instead was a second machine — <c>PixelFontMetrics.UiScale</c> anchored at 320x180, in its
/// own words "because host UI wants density" — laid over a 160x90 console. That is repealed.</para>
///
/// <para><b>A separate instance from the cartridge's, deliberately.</b> This console is
/// constructed here and belongs to the shell; <see cref="CartSession"/> builds its own. Two
/// framebuffers means pausing over a running game and opening a tool screen cannot scribble on
/// the frame the game left behind — and, just as important, nothing a tool screen draws can
/// ever reach the framebuffer the CI hashes. The 12 demo hashes and the 8 determinism anchors
/// are facts about the cartridge's console; this one is not it.</para>
///
/// <para><b>No cartridge is ever attached.</b> The shell never calls <c>Tick</c> on it: there
/// is no simulation here, only drawing. So the RNG, the input state and the tick counter stay
/// at their construction values forever, and the sound chip stays silent — the shell's audio
/// comes from <see cref="CartSession"/> and from the two bare <c>Apu</c>s the boot jingle and
/// the sfx audition borrow.</para>
/// </summary>
public sealed class ShellScreen
{
    private readonly VirtualConsole _console;

    /// <param name="profile">
    /// Which console the shell's own screen is. Null means <see cref="ConsoleProfile.Profile8"/>.
    /// It stays a parameter for the same reason <see cref="QuarpGame"/>'s is: the day QUARP-16
    /// exists, the shell's screen has to become a 16 as well — the law is that the console is
    /// the same for everything, not that it is forever 160x90.
    /// </param>
    public ShellScreen(ConsoleProfile? profile = null)
    {
        _console = new VirtualConsole(profile ?? ConsoleProfile.Profile8);
    }

    /// <summary>The drawing surface itself: tool screens call Cls/Rect/RectFill/Line/Print/Spr/Pset on this.</summary>
    public VirtualConsole Console => _console;

    /// <summary>What the presenter puts on the window — the same type <see cref="CartSession.Framebuffer"/> is.</summary>
    public Framebuffer Framebuffer => _console.Framebuffer;

    /// <summary>
    /// The output state this screen is shown through. Tool screens leave it at identity — see
    /// <see cref="Begin"/> — so the shell's pixels reach the window exactly as its framebuffer
    /// has them, and the editor golden hashes stay facts about drawing alone.
    /// </summary>
    public DisplayPalette Display => _console.Display;

    /// <summary>Screen width in console pixels; 160 on profile 8.</summary>
    public int Width => _console.ScreenWidth;

    /// <summary>Screen height in console pixels; 90 on profile 8.</summary>
    public int Height => _console.ScreenHeight;

    /// <summary>
    /// Where this screen sits in a window of the given size, and how a mouse point comes back.
    /// The shell asks the screen rather than computing it, so no caller can pick a different
    /// scale from the one the picture was actually drawn at.
    /// </summary>
    public FramePlacement Placement(int windowWidth, int windowHeight) =>
        FramePlacement.Compute(windowWidth, windowHeight, Width, Height);

    /// <summary>
    /// Resets the drawing state — camera, clip, palette remap, transparency — and the output
    /// state — the display sets and the row selector — to the console's defaults before a screen
    /// starts painting. Screens are allowed to use all of it (a list that clips its rows, a panel
    /// that shifts the camera); this is what keeps one screen's leftovers from bending the next
    /// screen's pixels, which on a shared surface would be a bug that only appears after a
    /// particular mode switch.
    ///
    /// <para>The display stage is reset here for a sharper reason than tidiness: it is the one
    /// piece of console state whose leftovers would be invisible to every test in the suite. The
    /// editor goldens hash the index buffer, so a tool screen left tinted would hash exactly
    /// right and look wrong on the window. Resetting it here — and asserting it in a test — is
    /// what keeps "the shell's output state is identity" a checked fact rather than a habit.</para>
    /// </summary>
    public void Begin()
    {
        _console.Camera();
        _console.Clip();
        _console.Pal();
        _console.Palt();
        _console.Pald();
        _console.Palr();
    }
}
